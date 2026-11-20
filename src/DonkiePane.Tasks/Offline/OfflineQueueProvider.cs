namespace DonkiePane.Tasks.Offline;

/// <summary>
/// Een laag om een bron heen die wijzigingen bewaart zolang de bron niet bereikbaar is, en ze verstuurt zodra
/// het weer kan. Wat je offline doet (toevoegen, aanpassen, afvinken, verwijderen) blijft direct zichtbaar,
/// en gaat in dezelfde volgorde naar de bron. De wachtrij staat in een bestand, dus een herstart verliest niets.
///
/// Alleen echt tijdelijke storingen worden bewaard: geen verbinding, een bron die om wachten vraagt of even hapert. Een
/// weigering (ongeldig token, ongeldige gegevens) komt gewoon als fout terug, want opnieuw proberen helpt niet.
/// Verplaatsen wordt niet bewaard: een positie zegt niets meer als de lijst intussen is veranderd.
/// </summary>
public class OfflineQueueProvider : ITaskProvider, IDisposable
{
    private readonly ITaskProvider _inner;
    private readonly PendingQueueStore _store;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly List<PendingOperation> _operations;
    private readonly Dictionary<string, string> _notices = [];
    private readonly SemaphoreSlim _flushGate = new(1, 1);

    /// <summary>
    /// Waar binnen <see cref="GetTasksAsync"/> gelezen wordt. Daar is net al verstuurd; een "weer bereikbaar" van dat
    /// lezen mag dan niet meteen een tweede poging starten (na een geweigerd token zou dat de bron bestoken).
    /// </summary>
    private static readonly AsyncLocal<bool> ReadingAfterFlush = new();

    // Na Dispose (bijv. de bron is opnieuw gestart) neemt een nieuwe wachtrij hetzelfde bestand over: deze verstuurt
    // en bewaart dan niets meer, anders zou hij een verouderde lijst over die van de nieuwe heen schrijven.
    private readonly CancellationTokenSource _disposed = new();

    protected OfflineQueueProvider(ITaskProvider inner, PendingQueueStore store, TimeProvider? time)
    {
        _inner = inner;
        _store = store;
        _time = time ?? TimeProvider.System;
        _operations = store.Load().Operations;

        inner.ListChanged += OnInnerListChanged;
        inner.StatusChanged += OnInnerStatusChanged;
    }

    /// <summary>Wikkelt een bron; bronnen die van buiten kunnen veranderen houden hun verversknop en interval.</summary>
    public static OfflineQueueProvider Wrap(ITaskProvider inner, PendingQueueStore store, TimeProvider? time = null) =>
        inner is IRefreshableTaskProvider refreshable
            ? new RefreshableOfflineQueueProvider(refreshable, store, time)
            : new OfflineQueueProvider(inner, store, time);

    public TaskProviderCapabilities Capabilities => _inner.Capabilities;

    public event EventHandler<string>? ListChanged;
    public event EventHandler<TaskProviderStatus>? StatusChanged;

    /// <summary>Hoeveel wijzigingen er nog op de bron wachten.</summary>
    public int PendingCount
    {
        get { lock (_lock) return _operations.Count; }
    }

    // ---------- Lezen ----------

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        await FlushAsync(ct); // eerst wat er nog wacht, dan lezen
        IReadOnlyList<TaskItem> server;
        ReadingAfterFlush.Value = true;
        try
        {
            server = await _inner.GetTasksAsync(listId, ct);
        }
        finally
        {
            ReadingAfterFlush.Value = false;
        }
        return Apply(listId, server);
    }

    // ---------- Wijzigen ----------

    public async Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        ClearNotice(listId);
        try
        {
            await _inner.AddAsync(listId, task, ct);
        }
        catch (TaskProviderException ex) when (IsTransient(ex))
        {
            var queued = new TaskItem
            {
                Id = "queued-" + Guid.NewGuid().ToString("N")[..16],
                ListId = listId,
                Title = task.Title,
                DueDate = task.DueDate,
                Importance = task.Importance,
                SortOrder = task.SortOrder,
                CreatedAt = _time.GetUtcNow(),
            };
            Enqueue(new PendingOperation { Kind = PendingOperation.Add, ListId = listId, TaskId = queued.Id, Task = queued });
            Changed(listId);
        }
    }

    public async Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            ClearNotice(task.ListId);

            // Een taak die nog nergens bestaat, passen we in de wachtrij zelf aan: bij de bron komt hij meteen goed aan.
            if (UpdatePendingAdd(task)) { Changed(task.ListId); continue; }

            try
            {
                await _inner.UpdateAsync([task], ct);
                // Wat nog van eerder wachtte is ouder dan wat net aankwam; later versturen zou het terugdraaien.
                DropPendingUpdates(task.Id);
            }
            catch (TaskProviderException ex) when (IsTransient(ex))
            {
                lock (_lock) _operations.RemoveAll(o => o.Kind == PendingOperation.Update && o.TaskId == task.Id); // alleen de nieuwste telt
                Enqueue(new PendingOperation { Kind = PendingOperation.Update, ListId = task.ListId, TaskId = task.Id, Task = task.Clone() });
                Changed(task.ListId);
            }
        }
    }

    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) =>
        _inner.MoveAsync(listId, taskId, newIndex, ct);

    public async Task DeleteAsync(string listId, string taskId, CancellationToken ct = default)
    {
        ClearNotice(listId);

        // Een taak die nog nergens bestaat, hoeft nooit aan te komen.
        bool cancelledAdd;
        lock (_lock) cancelledAdd = _operations.RemoveAll(o => o.Kind == PendingOperation.Add && o.TaskId == taskId) > 0;
        if (cancelledAdd)
        {
            Save();
            Changed(listId);
            return;
        }

        try
        {
            await _inner.DeleteAsync(listId, taskId, ct);
            DropPendingUpdates(taskId); // een wijziging aan een taak die weg is, zou alleen nog een fout geven
        }
        catch (TaskProviderException ex) when (IsTransient(ex))
        {
            lock (_lock) _operations.RemoveAll(o => o.Kind == PendingOperation.Update && o.TaskId == taskId); // wijzigingen aan iets dat weggaat, doen er niet meer toe
            Enqueue(new PendingOperation { Kind = PendingOperation.Delete, ListId = listId, TaskId = taskId });
            Changed(listId);
        }
    }

    public Task DeleteListAsync(string listId, CancellationToken ct = default) => _inner.DeleteListAsync(listId, ct);

    // ---------- Versturen ----------

    /// <summary>
    /// Verstuurt wat er wacht, in volgorde, tot het niet meer lukt. Tijdelijke storingen en een geweigerd token laten
    /// de rest staan (later weer proberen); een wijziging die de bron definitief weigert (bijvoorbeeld voor een taak die
    /// intussen weg is) wordt weggegooid, met een melding, zodat hij de rest niet blokkeert.
    /// </summary>
    protected async Task FlushAsync(CancellationToken ct)
    {
        if (PendingCount == 0) return;
        if (!await _flushGate.WaitAsync(0, ct)) return; // er loopt al een ronde

        var changedLists = new HashSet<string>();
        try
        {
            while (true)
            {
                PendingOperation? op;
                lock (_lock) op = _operations.FirstOrDefault();
                if (op is null) break;

                try
                {
                    await Send(op, ct);

                    // Bronnen maken een nieuwe taak altijd open aan, dus een taak die offline meteen is afgevinkt,
                    // komt open aan. Dat zeggen we, in plaats van het stil te laten gebeuren.
                    if (op is { Kind: PendingOperation.Add, Task.IsCompleted: true })
                        lock (_lock)
                            _notices[op.ListId] = Loc.F("De taak \"{0}\" was offline afgevinkt, maar staat nu als open taak bij de bron. Vink hem opnieuw af.", op.Task!.Title);
                }
                catch (TaskProviderException ex) when (IsTransient(ex) || ex is SourceUnauthorizedException)
                {
                    break; // later weer; niets weggooien
                }
                catch (TaskProviderException ex)
                {
                    lock (_lock) _notices[op.ListId] = Loc.F("Een wijziging is niet verstuurd en weggelaten: {0}", ex.Message);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Geen melding van de bron maar een onverwachte fout (bijv. in een plug-in): niets weggooien,
                    // later opnieuw proberen. Lezen gaat gewoon door.
                    lock (_lock) _notices[op.ListId] = Loc.F("Wijzigingen wachten nog: {0}", ex.Message);
                    break;
                }

                lock (_lock) _operations.Remove(op);
                Save();
                changedLists.Add(op.ListId);
            }
        }
        finally
        {
            _flushGate.Release();
            foreach (var listId in changedLists) Changed(listId);
        }
    }

    private Task Send(PendingOperation op, CancellationToken ct) => op switch
    {
        { Kind: PendingOperation.Add, Task: { } task } => _inner.AddAsync(op.ListId, task, ct),
        { Kind: PendingOperation.Update, Task: { } task } => _inner.UpdateAsync([task], ct),
        { Kind: PendingOperation.Delete } => _inner.DeleteAsync(op.ListId, op.TaskId, ct),
        _ => Task.CompletedTask, // onbekend soort of beschadigde regel in het bestand: weggooien
    };

    private void DropPendingUpdates(string taskId)
    {
        bool removed;
        lock (_lock) removed = _operations.RemoveAll(o => o.Kind == PendingOperation.Update && o.TaskId == taskId) > 0;
        if (removed) Save();
    }

    // ---------- Zicht op de lijst ----------

    /// <summary>De lijst van de bron met de wachtende wijzigingen erover, zodat je offline direct ziet wat je deed.</summary>
    private IReadOnlyList<TaskItem> Apply(string listId, IReadOnlyList<TaskItem> server)
    {
        var view = server.Select(t => t.Clone()).ToList();
        lock (_lock)
        {
            foreach (var op in _operations.Where(o => o.ListId == listId))
            {
                switch (op.Kind)
                {
                    case PendingOperation.Add when op.Task is not null:
                        var added = op.Task.Clone();
                        var open = view.Where(t => !t.IsCompleted).ToList();
                        added.SortOrder = open.Count == 0 ? 0 : open.Min(t => t.SortOrder) - 1; // bovenaan, zoals bij toevoegen
                        view.Add(added);
                        break;
                    case PendingOperation.Update when op.Task is not null:
                        var target = view.FirstOrDefault(t => t.Id == op.TaskId);
                        if (target is not null) CopyEditable(op.Task, target);
                        break;
                    case PendingOperation.Delete:
                        view.RemoveAll(t => t.Id == op.TaskId);
                        break;
                }
            }
        }
        return view;
    }

    private static void CopyEditable(TaskItem from, TaskItem to)
    {
        to.Title = from.Title;
        to.IsCompleted = from.IsCompleted;
        to.CompletedAt = from.CompletedAt;
        to.DueDate = from.DueDate;
        to.Importance = from.Importance;
    }

    private bool UpdatePendingAdd(TaskItem task)
    {
        lock (_lock)
        {
            var pending = _operations.FirstOrDefault(o => o.Kind == PendingOperation.Add && o.TaskId == task.Id);
            if (pending?.Task is null) return false;
            CopyEditable(task, pending.Task);
            Save();
            return true;
        }
    }

    // ---------- Hulpjes ----------

    /// <summary>
    /// Alleen "geen verbinding", "even wachten" en "bron hapert" (5xx, onleesbaar antwoord) zijn tijdelijk; een
    /// weigering moet de gebruiker zien.
    /// </summary>
    private static bool IsTransient(TaskProviderException ex) =>
        ex is SourceRateLimitedException or SourceUnavailableException || ex.InnerException is HttpRequestException or TaskCanceledException;

    private void Enqueue(PendingOperation op)
    {
        op.QueuedAt = _time.GetUtcNow();
        lock (_lock) _operations.Add(op);
        Save();
        Publish(op.ListId, online: false, message: PendingText(op.ListId));
    }

    private void Save()
    {
        if (_disposed.IsCancellationRequested) return;
        lock (_lock) _store.Save(new PendingQueueData { Operations = [.. _operations] });
    }

    private void Changed(string listId) => ListChanged?.Invoke(this, listId);

    private void ClearNotice(string listId)
    {
        lock (_lock) _notices.Remove(listId);
    }

    private string? PendingText(string listId)
    {
        int count;
        lock (_lock) count = _operations.Count(o => o.ListId == listId);
        return count switch
        {
            0 => null,
            1 => Loc.T("1 wijziging wacht om verstuurd te worden."),
            _ => Loc.F("{0} wijzigingen wachten om verstuurd te worden.", count),
        };
    }

    private void Publish(string listId, bool online, string? message) =>
        StatusChanged?.Invoke(this, new TaskProviderStatus(listId, online, message));

    private void OnInnerListChanged(object? sender, string listId) => ListChanged?.Invoke(this, listId);

    /// <summary>
    /// Geeft de status van de bron door, maar vult hem aan: wachten er nog wijzigingen, dan staat dat erbij; en zodra de
    /// bron weer bereikbaar blijkt, begint het versturen vanzelf.
    /// </summary>
    private void OnInnerStatusChanged(object? sender, TaskProviderStatus status)
    {
        var message = status.Message;
        if (PendingText(status.ListId) is { } pending)
        {
            message = message is null ? pending : $"{message} {pending}"; // de storing blijft zichtbaar, met wat er wacht
        }
        else if (message is null)
        {
            lock (_lock) _notices.TryGetValue(status.ListId, out message); // een wijziging die is weggelaten
        }
        StatusChanged?.Invoke(this, status with { Message = message });

        if (status.IsOnline && status.Message is null && PendingCount > 0 && !ReadingAfterFlush.Value)
            _ = Task.Run(() => FlushAsync(_disposed.Token));
    }

    public void Dispose()
    {
        _inner.ListChanged -= OnInnerListChanged;
        _inner.StatusChanged -= OnInnerStatusChanged;
        _disposed.Cancel(); // een lopende ronde versturen stopt
        GC.SuppressFinalize(this);
    }
}

/// <summary>De variant voor bronnen met een controle-interval en verversknop.</summary>
public sealed class RefreshableOfflineQueueProvider(IRefreshableTaskProvider inner, PendingQueueStore store, TimeProvider? time)
    : OfflineQueueProvider(inner, store, time), IRefreshableTaskProvider
{
    public TimeSpan PollInterval
    {
        get => inner.PollInterval;
        set => inner.PollInterval = value;
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await FlushAsync(ct);
        await inner.RefreshAsync(ct);
    }
}
