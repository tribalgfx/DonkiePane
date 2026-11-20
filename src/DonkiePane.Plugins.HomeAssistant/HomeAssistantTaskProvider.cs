using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DonkiePane.Tasks;

namespace DonkiePane.Plugins.HomeAssistant;

/// <summary>
/// De items van to-do lijsten uit Home Assistant. Een lijst is een <c>todo.*</c>-entiteit (het lijst-id van het pane).
/// Home Assistant geeft geen volgorde of aanmaakdatum mee die we kunnen gebruiken en kent geen "belangrijk": DonkiePane
/// toont eerst wat een deadline heeft (vroegste bovenaan) en daarna de rest in de volgorde van Home Assistant.
///
/// Er is geen revisie om goedkoop te controleren; elke ronde wordt de lijst opgehaald en vergeleken. Dat blijft binnen je
/// eigen netwerk. Na een 401 stopt alles tot het token verbeterd is; bij een herstart van Home Assistant wordt even gewacht.
/// </summary>
public sealed partial class HomeAssistantTaskProvider(HomeAssistantClient client, TimeProvider? time = null) : IRefreshableTaskProvider, IDisposable
{
    private readonly SourceGate _gate = new(time);
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _lock = new();
    private readonly Dictionary<string, List<HaItem>> _items = [];
    private readonly Dictionary<string, string> _signatures = [];
    private readonly HashSet<string> _watched = [];
    private readonly CancellationTokenSource _stop = new();
    private Task? _poller;

    public TaskProviderCapabilities Capabilities { get; } = new(Importance: false, PaneOwnsTasks: false, Reorder: false);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);

    public event EventHandler<string>? ListChanged;
    public event EventHandler<TaskProviderStatus>? StatusChanged;

    // ---------- Lezen ----------

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        Watch(listId);
        try
        {
            _gate.ThrowIfClosed();
            await FetchAsync(listId, ct);
            Publish(listId, online: true, message: null);
        }
        catch (TaskProviderException ex)
        {
            _gate.Remember(ex);
            Publish(listId, SourceGate.IsReachable(ex), ex.Message);
            lock (_lock)
            {
                if (!_items.ContainsKey(listId)) throw; // nog nooit gelukt: er is niets om te tonen
            }
        }
        return Current(listId);
    }

    // ---------- Wijzigen ----------

    public async Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        var title = ValidTitle(task.Title);
        // Geen nieuwe poging bij een netwerkfout: Home Assistant kent geen eigen id, dus herhalen kan een dubbele taak geven.
        await Write(listId, () => client.AddItemAsync(listId, title, task.DueDate, ct));
    }

    public async Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            HaItem? current;
            lock (_lock) current = _items.GetValueOrDefault(task.ListId)?.FirstOrDefault(i => i.Uid == task.Id);

            var changes = new JsonObject();
            if (current is null || task.Title != current.Summary) changes["rename"] = ValidTitle(task.Title);
            if (current is null || task.IsCompleted != (current.Status == "completed"))
                changes["status"] = task.IsCompleted ? "completed" : "needs_action";
            if (current is null || task.DueDate != ParseDate(current.Due))
                changes["due_date"] = task.DueDate is { } due ? HomeAssistantClient.DueString(due) : null; // null wist de deadline

            if (changes.Count == 0) continue; // alleen "belangrijk" of volgorde veranderd: Home Assistant kent dat niet
            var listId = task.ListId;
            await Write(listId, async () =>
            {
                await client.UpdateItemAsync(listId, task.Id, changes, ct);
                lock (_lock)
                {
                    if (current is null) return;
                    current.Summary = task.Title;
                    current.Status = task.IsCompleted ? "completed" : "needs_action";
                    current.Due = task.DueDate is { } d ? HomeAssistantClient.DueString(d) : null;
                }
            });
        }
    }

    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) =>
        throw new TaskProviderException(Loc.T("Deze bron ondersteunt het verplaatsen van taken niet."));

    public async Task DeleteAsync(string listId, string taskId, CancellationToken ct = default) =>
        await Write(listId, async () =>
        {
            await client.RemoveItemAsync(listId, taskId, ct);
            lock (_lock) _items.GetValueOrDefault(listId)?.RemoveAll(i => i.Uid == taskId);
        });

    /// <summary>Het pane is weg; de items in Home Assistant blijven staan. Alleen het lokale geheugen opruimen.</summary>
    public Task DeleteListAsync(string listId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _watched.Remove(listId);
            _items.Remove(listId);
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
                _gate.ThrowIfClosed();
                await FetchAsync(listId, ct);
                Publish(listId, online: true, message: null);
                string? after;
                lock (_lock) after = _signatures.GetValueOrDefault(listId);
                if (before != after) ListChanged?.Invoke(this, listId);
            }
            catch (TaskProviderException ex)
            {
                _gate.Remember(ex);
                Publish(listId, SourceGate.IsReachable(ex), ex.Message);
            }
        }
    }

    public Task RefreshAsync(CancellationToken ct = default) => PollOnceAsync(ct);

    private void Watch(string listId)
    {
        lock (_lock)
        {
            _watched.Add(listId);
            _poller ??= Task.Run(() => PollLoop.RunAsync(() => PollInterval, PollOnceAsync, _time, _stop.Token));
        }
    }

    // ---------- Hulpjes ----------

    private async Task FetchAsync(string listId, CancellationToken ct)
    {
        var items = (await client.GetItemsAsync(listId, ct)).ToList();
        var signature = string.Join('\n', items.Select(i => $"{i.Uid}|{i.Summary}|{i.Status}|{i.Due}"));
        lock (_lock)
        {
            _items[listId] = items;
            _signatures[listId] = signature;
        }
    }

    /// <summary>Voert een schrijfactie uit en meldt daarna dat de lijst veranderd is.</summary>
    private async Task Write(string listId, Func<Task> action)
    {
        _gate.ThrowIfClosed();
        try
        {
            await action();
        }
        catch (TaskProviderException ex)
        {
            _gate.Remember(ex);
            throw;
        }
        ListChanged?.Invoke(this, listId);
    }

    private IReadOnlyList<TaskItem> Current(string listId)
    {
        lock (_lock) return Map(listId, _items.GetValueOrDefault(listId) ?? []);
    }

    /// <summary>
    /// Items omzetten naar taken. Er is geen aanmaaktijd: de plek in de lijst van Home Assistant dient als vaste volgorde,
    /// en bij afgeronde items blijft die volgorde ook zo (bovenaan wat Home Assistant bovenaan heeft).
    /// </summary>
    public static List<TaskItem> Map(string listId, IReadOnlyList<HaItem> items)
    {
        var epoch = DateTimeOffset.UnixEpoch;
        var mapped = items.Select((item, index) => new TaskItem
        {
            Id = item.Uid,
            ListId = listId,
            Title = item.Summary,
            IsCompleted = item.Status == "completed",
            DueDate = ParseDate(item.Due),
            CreatedAt = epoch.AddSeconds(items.Count - index),
            UpdatedAt = epoch.AddSeconds(items.Count - index),
            CompletedAt = item.Status == "completed" ? epoch.AddSeconds(items.Count - index) : null,
            ExternalId = item.Uid,
        }).ToList();

        var order = 0;
        foreach (var task in mapped.Where(t => !t.IsCompleted).OrderBy(t => t.DueDate ?? DateOnly.MaxValue).ThenByDescending(t => t.CreatedAt))
            task.SortOrder = order++;
        return mapped;
    }

    /// <summary>De datum van een deadline. Een deadline met tijd telt als die dag; het tijdstip kan DonkiePane niet tonen.</summary>
    public static DateOnly? ParseDate(string? due) =>
        due is not null && DatePrefix().Match(due) is { Success: true } m
        && DateOnly.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static string ValidTitle(string title)
    {
        var text = title.Trim();
        if (text.Length == 0) throw new TaskProviderException(Loc.T("Een taak heeft een tekst nodig."));
        return text;
    }

    private void Publish(string listId, bool online, string? message) =>
        StatusChanged?.Invoke(this, new TaskProviderStatus(listId, online, message));

    public void Dispose() => _stop.Cancel();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}")]
    private static partial Regex DatePrefix();
}
