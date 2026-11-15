using DonkiePane.Tasks;
using System.Globalization;
using System.Text.Json.Nodes;

namespace DonkiePane.Plugins.Google;

/// <summary>
/// Taken uit Google Taken. De lijst blijft bij Google staan; DonkiePane toont hem en
/// schrijft wijzigingen direct terug. Elke 60 seconden wordt gekeken of er buiten
/// DonkiePane iets is veranderd (bijv. op je telefoon).
///
/// Google Taken kent geen "belangrijk" en geen tijdstip bij een deadline; die velden
/// worden dus genegeerd (zie <see cref="Capabilities"/>).
/// </summary>
public sealed class GoogleTaskProvider(GoogleTasksClient client) : IRefreshableTaskProvider, IDisposable
{
    private readonly object _lock = new();
    private readonly Dictionary<string, List<TaskItem>> _cache = [];
    private readonly Dictionary<string, string> _signatures = [];
    private readonly HashSet<string> _watched = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _poller;

    public TaskProviderCapabilities Capabilities { get; } = new(Importance: false, PaneOwnsTasks: false);

    /// <summary>Hoe vaak er gekeken wordt of een lijst buiten DonkiePane is gewijzigd.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    public event EventHandler<string>? ListChanged;
    public event EventHandler<TaskProviderStatus>? StatusChanged;

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        Watch(listId);
        try
        {
            var tasks = await FetchAsync(listId, ct);
            Publish(listId, online: true, message: null);
            return Clones(tasks);
        }
        catch (TaskProviderException ex)
        {
            Publish(listId, online: ex is not NotSignedInException && IsOnline(ex), message: ex.Message);
            lock (_lock)
            {
                // Geen verbinding: toon de laatst bekende lijst in plaats van een lege pane.
                if (_cache.TryGetValue(listId, out var cached)) return Clones(cached);
            }
            throw;
        }
    }

    public async Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        await client.InsertAsync(listId, task.Title, task.DueDate, ct);
        ListChanged?.Invoke(this, listId);
    }

    public async Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        var changedLists = new HashSet<string>();
        foreach (var task in tasks)
        {
            TaskItem? current;
            lock (_lock)
                current = _cache.GetValueOrDefault(task.ListId)?.FirstOrDefault(t => t.Id == task.Id);

            var patch = new JsonObject();
            if (current is null || task.Title != current.Title) patch["title"] = task.Title;
            if (current is null || task.IsCompleted != current.IsCompleted)
            {
                if (task.IsCompleted)
                {
                    patch["status"] = "completed";
                }
                else
                {
                    // Heropenen: de afgevinkt-datum moet expliciet weg.
                    patch["status"] = "needsAction";
                    patch["completed"] = null;
                }
            }
            if (current is null || task.DueDate != current.DueDate)
                patch["due"] = task.DueDate is { } due ? GoogleTasksClient.DueString(due) : null;

            if (patch.Count == 0) continue; // alleen "belangrijk" of volgorde veranderd: Google kent dat niet
            await client.PatchAsync(task.ListId, task.Id, patch, ct);
            changedLists.Add(task.ListId);
        }

        foreach (var listId in changedLists)
            ListChanged?.Invoke(this, listId);
    }

    public async Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default)
    {
        List<TaskItem> tasks;
        lock (_lock)
            tasks = _cache.TryGetValue(listId, out var cached) ? Clones(cached).ToList() : [];
        if (tasks.Count == 0) tasks = (await FetchAsync(listId, ct)).ToList();

        // Alleen verplaatsen binnen dezelfde "familie": een subtaak blijft onder zijn hoofdtaak (en gaat dus met
        // zijn ouder mee), een gewone taak zoekt zijn buur tussen de gewone taken.
        var ordered = TaskOrdering.Ordered(tasks).ToList();
        var moved = ordered.FirstOrDefault(t => t.Id == taskId);
        var previous = TaskOrdering.PreviousSiblingId(ordered, taskId, newIndex, sameParent: true);
        await client.MoveAsync(listId, taskId, previous, moved?.ParentId, ct);
        ListChanged?.Invoke(this, listId);
    }

    public async Task DeleteAsync(string listId, string taskId, CancellationToken ct = default)
    {
        await client.DeleteAsync(listId, taskId, ct);
        lock (_lock)
            _cache.GetValueOrDefault(listId)?.RemoveAll(t => t.Id == taskId);
        ListChanged?.Invoke(this, listId);
    }

    /// <summary>Het pane is weg; de lijst bij Google blijft staan. Alleen het lokale geheugen opruimen.</summary>
    public Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _watched.Remove(listId);
            _cache.Remove(listId);
            _signatures.Remove(listId);
        }
        return Task.CompletedTask;
    }

    // ---------- Synchronisatie ----------

    /// <summary>Kijkt of een gevolgde lijst buiten DonkiePane is veranderd en meldt dat dan. (Publiek voor tests.)</summary>
    public async Task PollOnceAsync(CancellationToken ct = default)
    {
        string[] lists;
        lock (_lock) lists = [.. _watched];

        foreach (var listId in lists)
        {
            string? before;
            lock (_lock) before = _signatures.GetValueOrDefault(listId);
            try
            {
                await FetchAsync(listId, ct);
                Publish(listId, online: true, message: null);
                string? after;
                lock (_lock) after = _signatures.GetValueOrDefault(listId);
                if (before != after) ListChanged?.Invoke(this, listId);
            }
            catch (TaskProviderException ex)
            {
                Publish(listId, online: ex is not NotSignedInException && IsOnline(ex), message: ex.Message);
            }
        }
    }

    private void Watch(string listId)
    {
        lock (_lock)
        {
            _watched.Add(listId);
            _poller ??= Task.Run(PollLoopAsync);
        }
    }

    private Task PollLoopAsync() => PollLoop.RunAsync(() => PollInterval, PollOnceAsync, null, _stop.Token);

    /// <summary>De verversknop: nu meteen kijken in plaats van wachten op de volgende ronde.</summary>
    public Task RefreshAsync(CancellationToken ct = default) => PollOnceAsync(ct);

    private async Task<List<TaskItem>> FetchAsync(string listId, CancellationToken ct)
    {
        var dtos = await client.ListTasksAsync(listId, ct);
        var tasks = Map(listId, dtos);
        var signature = string.Join('\n', dtos.Select(d =>
            $"{d.Id}|{d.Etag}|{d.Updated}|{d.Position}|{d.Status}|{d.Due}|{d.Title}"));
        lock (_lock)
        {
            _cache[listId] = tasks;
            _signatures[listId] = signature;
        }
        return tasks;
    }

    /// <summary>
    /// Google-taken omzetten naar DonkiePane-taken. De volgorde van Google (positie, alfabetisch) wordt een rangnummer,
    /// waarbij subtaken direct onder hun hoofdtaak komen. Een subtaak telt alleen als zodanig als zijn hoofdtaak ook
    /// open is; anders staat hij los in de lijst.
    /// </summary>
    public static List<TaskItem> Map(string listId, IEnumerable<GoogleTaskDto> dtos)
    {
        var all = dtos.OrderBy(d => d.Position ?? "", StringComparer.Ordinal).ToList();
        var openIds = all.Where(d => d.Status != "completed").Select(d => d.Id).ToHashSet();
        string? OpenParent(GoogleTaskDto d) => d.Status != "completed" && d.Parent is { } p && openIds.Contains(p) ? p : null;

        var order = new Dictionary<string, int>();
        var next = 0;
        foreach (var top in all.Where(d => d.Status != "completed" && OpenParent(d) is null))
        {
            order[top.Id] = next++;
            foreach (var child in all.Where(c => OpenParent(c) == top.Id))
                order[child.Id] = next++;
        }

        var result = new List<TaskItem>(all.Count);
        foreach (var d in all)
        {
            var completed = d.Status == "completed";
            var updated = ParseTime(d.Updated) ?? DateTimeOffset.UtcNow;
            result.Add(new TaskItem
            {
                Id = d.Id,
                ListId = listId,
                ParentId = OpenParent(d),
                Title = d.Title ?? "",
                IsCompleted = completed,
                CompletedAt = completed ? ParseTime(d.Completed) ?? updated : null,
                DueDate = ParseDate(d.Due),
                SortOrder = completed ? 0 : order[d.Id],
                CreatedAt = updated, // Google geeft geen aanmaakdatum
                UpdatedAt = updated,
                ExternalId = d.Id,
            });
        }
        return result;
    }

    private static DateOnly? ParseDate(string? due) =>
        due is { Length: >= 10 } && DateOnly.TryParseExact(due[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static DateTimeOffset? ParseTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time) ? time : null;

    private static IReadOnlyList<TaskItem> Clones(List<TaskItem> tasks) => tasks.Select(t => t.Clone()).ToList();

    /// <summary>Bij een gewone fout (bijv. 403) is er wel verbinding; alleen "geen verbinding" telt als offline.</summary>
    private static bool IsOnline(TaskProviderException ex) => ex.InnerException is not HttpRequestException and not TaskCanceledException;

    private void Publish(string listId, bool online, string? message) =>
        StatusChanged?.Invoke(this, new TaskProviderStatus(listId, online, message));

    public void Dispose() => _stop.Cancel();
}


