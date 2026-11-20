using System.Globalization;
using System.Text.Json.Nodes;
using DonkiePane.Tasks;

namespace DonkiePane.Plugins.RestTasks;

/// <summary>
/// De taken van een bron die het protocol "Tasks REST v1" spreekt. Het protocol kent één lijst per token
/// (<see cref="ListId"/>), geen volgorde en geen "belangrijk"; DonkiePane sorteert dus zelf: eerst wat een
/// deadline heeft (vroegste bovenaan), dan de rest met de nieuwste bovenaan.
///
/// Zorgvuldig met het netwerk: elke minuut één goedkope vraag met de revisie (304 als er niets veranderd is).
/// Bij 401 stopt alles tot de gebruiker de instellingen verbetert, want de bron kan na een paar mislukte
/// pogingen alles blokkeren, ook met een geldig token. Bij 429 wordt de wachttijd van de bron gerespecteerd.
/// </summary>
public sealed class RestTaskProvider(RestTasksClient client, TimeProvider? time = null) : IRefreshableTaskProvider, IDisposable
{
    /// <summary>Het protocol heeft één lijst; dit is het vaste ID ervan.</summary>
    public const string ListId = "tasks";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stop = new();

    // De lijst zoals de bron hem laatst gaf, bijgewerkt met onze eigen wijzigingen. De revisie blijft bewust
    // die van de laatste echte ophaling: zo ziet de eerstvolgende poll ook wat een ander apparaat intussen deed.
    private List<RestTask>? _remote;
    private string? _revision;
    private string? _haltedMessage;          // 401: niets meer sturen
    private DateTimeOffset _pausedUntil;     // 429: pas daarna weer
    private Task? _poller;
    private bool _watching;

    public TaskProviderCapabilities Capabilities { get; } = new(Importance: false, PaneOwnsTasks: false, Reorder: false);

    /// <summary>Hoe vaak er gekeken wordt of de lijst buiten DonkiePane is gewijzigd.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    public event EventHandler<string>? ListChanged;
    public event EventHandler<TaskProviderStatus>? StatusChanged;

    // ---------- Lezen ----------

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        StartWatching();
        try
        {
            RefuseWhenNotAllowed();
            var list = await client.GetAsync(KnownRevision(), ct);
            if (list is not null) Store(list);
            Publish(online: true, message: null);
            return Current();
        }
        catch (TaskProviderException ex)
        {
            Remember(ex);
            Publish(online: IsReachable(ex), message: ex.Message);
            lock (_lock)
            {
                if (_remote is not null) return Current(); // toon de laatst bekende taken met de melding eronder
            }
            throw;
        }
    }

    // ---------- Wijzigen ----------

    public async Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        var text = ValidText(task.Title);
        // Eigen id: een herhaald verzoek maakt geen tweede taak. Daarom mag een mislukte poging één keer opnieuw.
        var id = "dp-" + Guid.NewGuid().ToString("N")[..24];
        await Write(async () =>
        {
            RestTask created;
            try
            {
                created = await client.AddAsync(id, text, task.DueDate, ct);
            }
            catch (TaskProviderException ex) when (ex.InnerException is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                created = await client.AddAsync(id, text, task.DueDate, ct);
            }
            lock (_lock) _remote?.Add(created);
        });
    }

    public async Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            RestTask? current;
            lock (_lock) current = _remote?.FirstOrDefault(t => t.Id == task.Id);

            var patch = new JsonObject();
            if (current is null || task.Title != current.Text) patch["text"] = ValidText(task.Title);
            if (current is null || task.IsCompleted != current.Done) patch["done"] = task.IsCompleted;
            if (current is null || task.DueDate != ParseDate(current.DueDate))
                patch["dueDate"] = task.DueDate is { } due ? RestTasksClient.DueString(due) : null; // null wist de deadline

            if (patch.Count == 0) continue; // alleen volgorde of "belangrijk" veranderd: de bron kent dat niet
            await Write(async () =>
            {
                var updated = await client.PatchAsync(task.Id, patch, ct);
                lock (_lock)
                {
                    var index = _remote?.FindIndex(t => t.Id == task.Id) ?? -1;
                    if (index >= 0) _remote![index] = updated;
                }
            });
        }
    }

    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) =>
        throw new TaskProviderException(Loc.T("Deze bron ondersteunt het verplaatsen van taken niet."));

    public async Task DeleteAsync(string listId, string taskId, CancellationToken ct = default)
    {
        try
        {
            await Write(async () =>
            {
                await client.DeleteAsync(taskId, ct);
                lock (_lock) _remote?.RemoveAll(t => t.Id == taskId);
            });
        }
        catch (TaskGoneException)
        {
            // Al weg (bijvoorbeeld op een ander apparaat verwijderd): dat is precies wat de gebruiker wilde.
            lock (_lock) _remote?.RemoveAll(t => t.Id == taskId);
            ListChanged?.Invoke(this, ListId);
        }
    }

    /// <summary>Het pane is weg; de taken bij de bron blijven staan. Alleen het lokale geheugen opruimen.</summary>
    public Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _remote = null;
            _revision = null;
        }
        return Task.CompletedTask;
    }

    // ---------- Synchronisatie ----------

    /// <summary>Kijkt of er buiten DonkiePane iets is veranderd en meldt dat dan. (Publiek voor tests.)</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        try
        {
            RefuseWhenNotAllowed();
            var list = await client.GetAsync(KnownRevision(), ct);
            if (list is not null)
            {
                Store(list);
                ListChanged?.Invoke(this, ListId);
            }
            Publish(online: true, message: null);
        }
        catch (TaskProviderException ex)
        {
            Remember(ex);
            Publish(online: IsReachable(ex), message: ex.Message);
        }
    }

    private void StartWatching()
    {
        lock (_lock)
        {
            if (_watching) return;
            _watching = true;
            _poller = Task.Run(PollLoopAsync);
        }
    }

    private Task PollLoopAsync() => PollLoop.RunAsync(() => PollInterval, PollOnceAsync, _time, _stop.Token);

    /// <summary>De verversknop: nu meteen kijken in plaats van wachten op de volgende ronde.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => PollOnceAsync(ct);

    // ---------- Hulpjes ----------

    /// <summary>Voert een schrijfactie uit en meldt daarna dat de lijst veranderd is.</summary>
    private async Task Write(Func<Task> action)
    {
        RefuseWhenNotAllowed();
        try
        {
            await action();
        }
        catch (TaskProviderException ex) when (ex is not TaskGoneException)
        {
            Remember(ex);
            throw;
        }
        ListChanged?.Invoke(this, ListId);
    }

    /// <summary>Geen enkel verzoek sturen als de bron ons net heeft geweigerd of om wachten heeft gevraagd.</summary>
    private void RefuseWhenNotAllowed()
    {
        lock (_lock)
        {
            if (_haltedMessage is not null) throw new SourceUnauthorizedException(_haltedMessage);
            var now = _time.GetUtcNow();
            if (now < _pausedUntil) throw new SourceRateLimitedException(_pausedUntil - now);
        }
    }

    private void Remember(TaskProviderException ex)
    {
        lock (_lock)
        {
            switch (ex)
            {
                case SourceUnauthorizedException unauthorized:
                    _haltedMessage = unauthorized.Message;
                    break;
                case SourceRateLimitedException limited:
                    _pausedUntil = _time.GetUtcNow() + limited.RetryAfter;
                    break;
            }
        }
    }

    /// <summary>Zonder lijst in het geheugen altijd alles ophalen, anders alleen vragen of er iets veranderd is.</summary>
    private string? KnownRevision()
    {
        lock (_lock) return _remote is null ? null : _revision;
    }

    private void Store(RestTaskList list)
    {
        lock (_lock)
        {
            _remote = list.Tasks.ToList();
            _revision = list.Revision;
        }
    }

    private IReadOnlyList<TaskItem> Current()
    {
        lock (_lock) return Map(_remote ?? []);
    }

    /// <summary>Open taken: eerst met deadline (vroegste eerst), dan zonder (nieuwste eerst). Voltooide staan los daarvan.</summary>
    internal static List<TaskItem> Map(IEnumerable<RestTask> tasks)
    {
        var mapped = tasks.Select(t =>
        {
            var created = DateTimeOffset.FromUnixTimeMilliseconds(t.CreatedAt);
            return new TaskItem
            {
                Id = t.Id,
                ListId = ListId,
                Title = t.Text,
                IsCompleted = t.Done,
                // Het protocol kent geen afrondtijd; de aanmaaktijd houdt voltooide taken in een vaste volgorde.
                CompletedAt = t.Done ? created : null,
                DueDate = ParseDate(t.DueDate),
                CreatedAt = created,
                UpdatedAt = created,
                ExternalId = t.Id,
            };
        }).ToList();

        var order = 0;
        foreach (var task in mapped.Where(t => !t.IsCompleted)
                     .OrderBy(t => t.DueDate ?? DateOnly.MaxValue)
                     .ThenByDescending(t => t.CreatedAt))
            task.SortOrder = order++;
        return mapped;
    }

    private static DateOnly? ParseDate(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string ValidText(string title)
    {
        var text = title.Trim();
        if (text.Length == 0) throw new TaskProviderException(Loc.T("Een taak heeft een tekst nodig."));
        if (text.Length > RestTasksClient.MaxTextLength)
            throw new TaskProviderException(Loc.F("De tekst is te lang voor deze bron (maximaal {0} tekens, nu {1}).", RestTasksClient.MaxTextLength, text.Length));
        return text;
    }

    /// <summary>Bij een gewone fout (bijv. 403) is er wel verbinding; alleen "geen verbinding" telt als offline.</summary>
    private static bool IsReachable(TaskProviderException ex) =>
        ex is not SourceUnavailableException && ex.InnerException is not HttpRequestException and not TaskCanceledException;

    private void Publish(bool online, string? message) =>
        StatusChanged?.Invoke(this, new TaskProviderStatus(ListId, online, message));

    public void Dispose() => _stop.Cancel();
}
