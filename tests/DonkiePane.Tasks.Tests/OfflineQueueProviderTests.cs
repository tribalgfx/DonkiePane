using DonkiePane.Tasks.Offline;

namespace DonkiePane.Tasks.Tests;

/// <summary>Een bron in het geheugen die op commando offline gaat en dan doet wat echte bronnen doen: de laatste lijst tonen, schrijven weigeren.</summary>
internal class InMemoryTasks : ITaskProvider
{
    private List<TaskItem>? _cache;
    private int _next = 1;

    public List<TaskItem> Server { get; } = [];
    public bool Online { get; set; } = true;
    public List<string> Calls { get; } = [];

    /// <summary>Dit overkomt de eerstvolgende schrijfactie (en daarna niet meer).</summary>
    public Exception? NextFailure { get; set; }

    public TaskProviderCapabilities Capabilities { get; } = new(false, false, false);
    public event EventHandler<string>? ListChanged;
    public event EventHandler<TaskProviderStatus>? StatusChanged;

    public TaskItem Seed(string id, string title, bool done = false)
    {
        var task = new TaskItem { Id = id, ListId = "L", Title = title, IsCompleted = done, SortOrder = Server.Count };
        Server.Add(task);
        return task;
    }

    private static TaskProviderException OfflineError() => new("Geen verbinding met de bron.", new HttpRequestException("weg"));

    public void RaiseStatus(bool online, string? message) => StatusChanged?.Invoke(this, new TaskProviderStatus("L", online, message));

    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default)
    {
        if (!Online)
        {
            RaiseStatus(false, "Geen verbinding met de bron.");
            if (_cache is null) throw OfflineError();
            return Task.FromResult<IReadOnlyList<TaskItem>>(_cache.Select(t => t.Clone()).ToList());
        }
        _cache = Server.Select(t => t.Clone()).ToList();
        RaiseStatus(true, null);
        return Task.FromResult<IReadOnlyList<TaskItem>>(_cache.Select(t => t.Clone()).ToList());
    }

    private void FailIfNeeded()
    {
        if (!Online) throw OfflineError();
        if (NextFailure is { } failure)
        {
            NextFailure = null;
            throw failure;
        }
    }

    public Task AddAsync(string listId, TaskItem task, CancellationToken ct = default)
    {
        FailIfNeeded();
        Calls.Add($"add:{task.Title}");
        Server.Add(new TaskItem { Id = "srv-" + _next++, ListId = listId, Title = task.Title, DueDate = task.DueDate, SortOrder = -_next });
        ListChanged?.Invoke(this, listId);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default)
    {
        foreach (var task in tasks)
        {
            FailIfNeeded();
            Calls.Add($"update:{task.Id}");
            var target = Server.FirstOrDefault(t => t.Id == task.Id) ?? throw new TaskProviderException("Deze taak bestaat niet meer bij de bron.");
            target.Title = task.Title;
            target.IsCompleted = task.IsCompleted;
            target.DueDate = task.DueDate;
        }
        ListChanged?.Invoke(this, "L");
        return Task.CompletedTask;
    }

    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default)
    {
        if (!Online) throw OfflineError();
        Calls.Add("move");
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string listId, string taskId, CancellationToken ct = default)
    {
        FailIfNeeded();
        Calls.Add($"delete:{taskId}");
        if (Server.RemoveAll(t => t.Id == taskId) == 0) throw new TaskProviderException("Deze taak bestaat niet meer bij de bron.");
        ListChanged?.Invoke(this, listId);
        return Task.CompletedTask;
    }

    public Task DeleteListAsync(string listId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class InMemoryRefreshable : InMemoryTasks, IRefreshableTaskProvider
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
    public int Refreshed { get; private set; }
    public Task RefreshAsync(CancellationToken ct = default) { Refreshed++; return Task.CompletedTask; }
}

public sealed class OfflineQueueProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
    private readonly InMemoryTasks _server = new();
    private readonly List<TaskProviderStatus> _statuses = [];

    private string QueueFile => Path.Combine(_dir, "queue.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private OfflineQueueProvider Create(ITaskProvider? inner = null)
    {
        var provider = OfflineQueueProvider.Wrap(inner ?? _server, new PendingQueueStore(QueueFile));
        provider.StatusChanged += (_, s) => _statuses.Add(s);
        return provider;
    }

    private static TaskItem New(string title) => new() { Title = title };

    private static IEnumerable<string> OpenTitles(IReadOnlyList<TaskItem> tasks) => TaskOrdering.Ordered(tasks).Select(t => t.Title);

    [Fact]
    public async Task While_online_writes_go_straight_through_and_nothing_is_queued()
    {
        var queue = Create();

        await queue.AddAsync("L", New("A"));

        Assert.Single(_server.Server);
        Assert.Equal(0, queue.PendingCount);
        Assert.False(File.Exists(QueueFile));
    }

    [Fact]
    public async Task An_offline_add_is_kept_shown_at_the_top_and_saved_to_disk()
    {
        _server.Seed("s1", "Bestaand");
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;

        await queue.AddAsync("L", New("Nieuw")); // geen fout

        Assert.Equal(1, queue.PendingCount);
        Assert.True(File.Exists(QueueFile));
        var view = await queue.GetTasksAsync("L");
        Assert.Equal(["Nieuw", "Bestaand"], OpenTitles(view));
        Assert.StartsWith("queued-", view.Single(t => t.Title == "Nieuw").Id);
        Assert.Single(_server.Server); // bij de bron is nog niets veranderd
    }

    [Fact]
    public async Task When_the_source_is_back_the_queue_is_sent_and_the_task_appears_exactly_once()
    {
        _server.Seed("s1", "Bestaand");
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        _server.Online = true;

        var view = await queue.GetTasksAsync("L");

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(2, _server.Server.Count);
        Assert.Equal(2, view.Count);
        Assert.DoesNotContain(view, t => t.Id.StartsWith("queued-")); // de tijdelijke taak is vervangen door de echte
    }

    [Fact]
    public async Task An_offline_edit_shows_at_once_and_reaches_the_source_later()
    {
        _server.Seed("s1", "Oud");
        var queue = Create();
        var task = (await queue.GetTasksAsync("L")).Single();
        _server.Online = false;
        task.Title = "Nieuw";
        task.IsCompleted = true;

        await queue.UpdateAsync([task]);

        var view = await queue.GetTasksAsync("L");
        Assert.Equal("Nieuw", view.Single().Title);
        Assert.True(view.Single().IsCompleted);
        Assert.Equal("Oud", _server.Server.Single().Title);

        _server.Online = true;
        await queue.GetTasksAsync("L");
        Assert.Equal("Nieuw", _server.Server.Single().Title);
        Assert.True(_server.Server.Single().IsCompleted);
    }

    [Fact]
    public async Task Editing_the_same_task_twice_offline_sends_only_the_latest_state()
    {
        _server.Seed("s1", "Oud");
        var queue = Create();
        var task = (await queue.GetTasksAsync("L")).Single();
        _server.Online = false;
        task.Title = "Tussenstap";
        await queue.UpdateAsync([task]);
        task.Title = "Eind";
        await queue.UpdateAsync([task]);

        Assert.Equal(1, queue.PendingCount);
        _server.Online = true;
        await queue.GetTasksAsync("L");

        Assert.Equal("Eind", _server.Server.Single().Title);
        Assert.Single(_server.Calls); // niet twee wijzigingen
    }

    [Fact]
    public async Task Editing_a_task_that_only_exists_offline_changes_the_pending_add_itself()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        var pending = (await queue.GetTasksAsync("L")).Single();
        pending.Title = "Verbeterd";
        pending.DueDate = new DateOnly(2026, 12, 24);

        await queue.UpdateAsync([pending]);

        Assert.Equal(1, queue.PendingCount); // nog steeds één toevoeging, geen losse wijziging
        _server.Online = true;
        await queue.GetTasksAsync("L");
        var created = Assert.Single(_server.Server);
        Assert.Equal("Verbeterd", created.Title);
        Assert.Equal(new DateOnly(2026, 12, 24), created.DueDate);
        Assert.Equal(["add:Verbeterd"], _server.Calls);
    }

    [Fact]
    public async Task A_task_ticked_off_offline_right_after_creating_it_is_reported_instead_of_silently_lost()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        var pending = (await queue.GetTasksAsync("L")).Single();
        pending.IsCompleted = true;
        await queue.UpdateAsync([pending]);
        _server.Online = true;

        await queue.GetTasksAsync("L");

        Assert.False(_server.Server.Single().IsCompleted); // bronnen maken een taak altijd open aan
        Assert.Contains("open", _statuses.Last().Message);
        Assert.Contains("Nieuw", _statuses.Last().Message);
    }

    [Fact]
    public async Task Deleting_a_task_that_only_exists_offline_cancels_it_and_it_never_arrives()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        var pending = (await queue.GetTasksAsync("L")).Single();

        await queue.DeleteAsync("L", pending.Id);

        Assert.Equal(0, queue.PendingCount);
        Assert.Empty(await queue.GetTasksAsync("L"));
        _server.Online = true;
        await queue.GetTasksAsync("L");
        Assert.Empty(_server.Server);
        Assert.Empty(_server.Calls);
    }

    [Fact]
    public async Task An_offline_delete_hides_the_task_and_drops_edits_made_to_it()
    {
        _server.Seed("s1", "Weg");
        var queue = Create();
        var task = (await queue.GetTasksAsync("L")).Single();
        _server.Online = false;
        task.Title = "Nog gewijzigd";
        await queue.UpdateAsync([task]);

        await queue.DeleteAsync("L", "s1");

        Assert.Empty(await queue.GetTasksAsync("L"));
        Assert.Equal(1, queue.PendingCount); // alleen de verwijdering; de wijziging doet er niet meer toe
        _server.Online = true;
        await queue.GetTasksAsync("L");
        Assert.Empty(_server.Server);
        Assert.Equal(["delete:s1"], _server.Calls);
    }

    [Fact]
    public async Task The_queue_survives_a_restart_and_is_sent_by_the_new_instance()
    {
        _server.Seed("s1", "Bestaand");
        var first = Create();
        await first.GetTasksAsync("L");
        _server.Online = false;
        await first.AddAsync("L", New("Nieuw"));
        first.Dispose(); // de app sluit af

        var second = Create(); // de app start weer
        Assert.Equal(1, second.PendingCount);
        _server.Online = true;
        await second.GetTasksAsync("L");

        Assert.Equal(0, second.PendingCount);
        Assert.Equal(["Bestaand", "Nieuw"], _server.Server.Select(t => t.Title).Order());
    }

    [Fact]
    public async Task Changes_are_sent_in_the_order_they_were_made()
    {
        _server.Seed("s1", "Een");
        _server.Seed("s2", "Twee");
        var queue = Create();
        var one = (await queue.GetTasksAsync("L")).Single(t => t.Id == "s1");
        _server.Online = false;
        one.Title = "Een, gewijzigd";
        await queue.UpdateAsync([one]);
        await queue.AddAsync("L", New("X"));
        await queue.DeleteAsync("L", "s2");
        _server.Online = true;

        await queue.GetTasksAsync("L");

        Assert.Equal(["update:s1", "add:X", "delete:s2"], _server.Calls);
    }

    [Fact]
    public async Task A_change_the_source_refuses_for_good_is_dropped_with_a_notice_and_does_not_block_the_rest()
    {
        _server.Seed("s1", "Bestaand");
        var queue = Create();
        var task = (await queue.GetTasksAsync("L")).Single();
        _server.Online = false;
        task.Title = "Wijziging";
        await queue.UpdateAsync([task]);
        await queue.AddAsync("L", New("Blijft"));
        _server.Server.Clear(); // intussen elders verwijderd
        _server.Online = true;

        await queue.GetTasksAsync("L");

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal(["Blijft"], _server.Server.Select(t => t.Title)); // de rest is wel aangekomen
        Assert.Contains("weggelaten", _statuses.Last().Message);
        Assert.Contains("bestaat niet meer", _statuses.Last().Message);
    }

    [Fact]
    public async Task A_refused_token_keeps_the_queue_so_nothing_is_lost()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        _server.Online = true;
        _server.NextFailure = new SourceUnauthorizedException("token geweigerd");

        await queue.GetTasksAsync("L");

        Assert.Equal(1, queue.PendingCount);
        Assert.Empty(_server.Server);
        // Is het token hersteld, dan komt het alsnog aan.
        await queue.GetTasksAsync("L");
        Assert.Equal(0, queue.PendingCount);
        Assert.Single(_server.Server);
    }

    [Fact]
    public async Task A_real_refusal_while_online_is_shown_and_not_queued()
    {
        var queue = Create();
        _server.NextFailure = new TaskProviderException("De tekst is te lang.");

        await Assert.ThrowsAsync<TaskProviderException>(() => queue.AddAsync("L", New("X")));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task A_source_that_asks_to_wait_counts_as_temporary_and_the_change_is_kept()
    {
        var queue = Create();
        _server.NextFailure = new SourceRateLimitedException(TimeSpan.FromMinutes(1));

        await queue.AddAsync("L", New("X"));

        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task The_pane_is_told_what_is_waiting_and_the_message_goes_away_after_sending()
    {
        _server.Seed("s1", "A");
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Een"));
        await queue.AddAsync("L", New("Twee"));

        Assert.Contains("2 wijzigingen wachten", _statuses.Last().Message);
        _server.Online = true;
        await queue.GetTasksAsync("L");

        Assert.Null(_statuses.Last().Message);
        Assert.True(_statuses.Last().IsOnline);
    }

    [Fact]
    public async Task The_existing_connection_problem_stays_visible_next_to_what_is_waiting()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Een"));

        await queue.GetTasksAsync("L"); // offline: de bron meldt zelf "geen verbinding"

        var message = _statuses.Last().Message!;
        Assert.Contains("Geen verbinding", message);
        Assert.Contains("1 wijziging wacht", message);
    }

    [Fact]
    public async Task Reordering_is_not_queued_because_a_position_means_nothing_later()
    {
        var queue = Create();
        _server.Online = false;

        await Assert.ThrowsAsync<TaskProviderException>(() => queue.MoveAsync("L", "s1", 0));

        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public async Task When_the_source_reports_it_is_online_again_the_queue_is_sent_without_anyone_asking()
    {
        var queue = Create();
        await queue.GetTasksAsync("L");
        _server.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        _server.Online = true;

        _server.RaiseStatus(online: true, message: null); // bijvoorbeeld de volgende controleronde

        for (var i = 0; i < 40 && _server.Server.Count == 0; i++) await Task.Delay(50);
        Assert.Single(_server.Server);
        Assert.Equal(0, queue.PendingCount);
    }

    [Fact]
    public void Only_sources_that_can_change_from_outside_get_a_refresh_button()
    {
        var plain = OfflineQueueProvider.Wrap(new InMemoryTasks(), new PendingQueueStore(QueueFile));
        var refreshable = OfflineQueueProvider.Wrap(new InMemoryRefreshable(), new PendingQueueStore(Path.Combine(_dir, "b.json")));

        Assert.IsNotAssignableFrom<IRefreshableTaskProvider>(plain);
        Assert.IsAssignableFrom<IRefreshableTaskProvider>(refreshable);
    }

    [Fact]
    public async Task The_interval_and_the_refresh_pass_through_and_a_refresh_also_sends_the_queue()
    {
        var inner = new InMemoryRefreshable();
        var queue = (IRefreshableTaskProvider)OfflineQueueProvider.Wrap(inner, new PendingQueueStore(QueueFile));
        await queue.GetTasksAsync("L");
        inner.Online = false;
        await queue.AddAsync("L", New("Nieuw"));
        inner.Online = true;

        queue.PollInterval = TimeSpan.FromSeconds(15);
        await queue.RefreshAsync();

        Assert.Equal(TimeSpan.FromSeconds(15), inner.PollInterval);
        Assert.Equal(1, inner.Refreshed);
        Assert.Single(inner.Server); // de verversknop verstuurde ook wat wachtte
    }

    [Fact]
    public void The_capabilities_are_those_of_the_source()
    {
        Assert.Equal(_server.Capabilities, Create().Capabilities);
    }
}
