using System.Net;
using System.Text.Json.Nodes;
using DonkiePane.Plugins.RestTasks;

namespace DonkiePane.Tasks.Tests;

public sealed class RestTaskProviderTests
{
    private sealed class Fixture : IDisposable
    {
        public FakeTasksServer Server { get; } = new();
        public TestTime Time { get; } = new();
        public RestTaskProvider Provider { get; }
        public List<string> Changed { get; } = [];
        public List<TaskProviderStatus> Statuses { get; } = [];

        public Fixture(string token = FakeTasksServer.ValidToken)
        {
            var client = new RestTasksClient(new HttpClient(Server), new Uri("https://dash.test:4173/"), () => token);
            Provider = new RestTaskProvider(client, Time) { PollInterval = TimeSpan.FromHours(1) }; // de timer doet niets tijdens een test
            Provider.ListChanged += (_, id) => Changed.Add(id);
            Provider.StatusChanged += (_, s) => Statuses.Add(s);
        }

        public IEnumerable<ServerRequest> Writes => Server.Requests.Where(r => r.Method != HttpMethod.Get);

        public void Dispose() => Provider.Dispose();
    }

    [Fact]
    public async Task Open_tasks_with_a_deadline_come_first_then_the_newest_without()
    {
        using var f = new Fixture();
        f.Server.AddTask("oud", "Oud zonder datum", createdAt: 1_000);
        f.Server.AddTask("laat", "Later", due: "2026-12-31", createdAt: 2_000);
        f.Server.AddTask("nieuw", "Nieuw zonder datum", createdAt: 3_000);
        f.Server.AddTask("vroeg", "Vroeg", due: "2026-12-01", createdAt: 4_000);

        var tasks = await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        Assert.Equal(["vroeg", "laat", "nieuw", "oud"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
    }

    [Fact]
    public async Task Completed_tasks_are_marked_and_get_a_stable_time()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "Klaar", done: true, createdAt: 1_768_000_000_000);

        var tasks = await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        var done = Assert.Single(TaskOrdering.Completed(tasks));
        Assert.Equal("Klaar", done.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1_768_000_000_000), done.CompletedAt);
        Assert.Equal("a", done.ExternalId);
        Assert.Equal(RestTaskProvider.ListId, done.ListId);
    }

    [Fact]
    public void Capabilities_say_no_star_no_reordering_and_no_ownership()
    {
        using var f = new Fixture();

        Assert.False(f.Provider.Capabilities.Importance);
        Assert.False(f.Provider.Capabilities.Reorder);
        Assert.False(f.Provider.Capabilities.PaneOwnsTasks);
    }

    [Fact]
    public async Task Polling_asks_for_the_revision_and_stays_quiet_when_nothing_changed()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        await f.Provider.PollOnceAsync();

        Assert.Empty(f.Changed);
        Assert.Equal($"\"{f.Server.Revision}\"", f.Server.Requests.Last().IfNoneMatch);
    }

    [Fact]
    public async Task Polling_announces_a_change_made_elsewhere_once()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        f.Server.AddTask("b", "Van mijn telefoon");
        await f.Provider.PollOnceAsync();
        await f.Provider.PollOnceAsync();

        Assert.Equal([RestTaskProvider.ListId], f.Changed);
        var tasks = await f.Provider.GetTasksAsync(RestTaskProvider.ListId);
        Assert.Equal(2, tasks.Count);
    }

    [Fact]
    public async Task After_a_401_nothing_more_is_sent_so_the_source_cannot_lock_us_out()
    {
        using var f = new Fixture(token: "fout");

        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => f.Provider.GetTasksAsync(RestTaskProvider.ListId));
        var sent = f.Server.Requests.Count;

        // Alles wat daarna komt, wordt lokaal geweigerd: lezen, pollen en schrijven.
        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => f.Provider.GetTasksAsync(RestTaskProvider.ListId));
        await f.Provider.PollOnceAsync();
        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => f.Provider.AddAsync(RestTaskProvider.ListId, new TaskItem { Title = "X" }));

        Assert.Equal(sent, f.Server.Requests.Count);
        Assert.Equal(1, f.Server.FailedAttempts);
        Assert.Contains("token", f.Statuses.Last().Message);
    }

    [Fact]
    public async Task A_lockout_pauses_requests_until_the_wait_is_over()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId); // goed, dus er is een cache

        // De bron blokkeert alles na vijf mislukte pogingen (hier van "een ander programma"), ook met ons geldige token.
        f.Server.RetryAfterSeconds = 120;
        f.Server.FailedAttempts = 5;
        f.Server.Touch();

        var shown = await f.Provider.GetTasksAsync(RestTaskProvider.ListId); // 429: de laatst bekende taken met een melding
        Assert.Equal("a", Assert.Single(shown).Id);
        Assert.Contains("wachten", f.Statuses.Last().Message);
        var sent = f.Server.Requests.Count;

        await f.Provider.GetTasksAsync(RestTaskProvider.ListId); // binnen de wachttijd gaat er niets heen
        await f.Provider.PollOnceAsync();
        Assert.Equal(sent, f.Server.Requests.Count);

        f.Time.Now = f.Time.Now.AddSeconds(121);
        f.Server.FailedAttempts = 0; // de blokkade is verlopen
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        Assert.True(f.Server.Requests.Count > sent); // nu mag het weer
        Assert.True(f.Statuses.Last().IsOnline);
        Assert.Null(f.Statuses.Last().Message);
    }
    [Fact]
    public async Task Offline_after_a_good_fetch_shows_the_last_known_tasks_and_a_message()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);
        f.Server.Failures.Enqueue(new HttpRequestException("geen netwerk"));

        var tasks = await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        Assert.Equal("a", Assert.Single(tasks).Id);
        Assert.False(f.Statuses.Last().IsOnline);
        Assert.Contains("Geen verbinding", f.Statuses.Last().Message);
    }

    [Fact]
    public async Task Offline_on_the_very_first_fetch_is_an_error()
    {
        using var f = new Fixture();
        f.Server.Failures.Enqueue(new HttpRequestException("geen netwerk"));

        await Assert.ThrowsAsync<TaskProviderException>(() => f.Provider.GetTasksAsync(RestTaskProvider.ListId));
    }

    [Fact]
    public async Task Coming_back_online_clears_the_message()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);
        f.Server.Failures.Enqueue(new HttpRequestException());
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);

        Assert.True(f.Statuses.Last().IsOnline);
        Assert.Null(f.Statuses.Last().Message);
    }

    [Fact]
    public async Task Add_uses_a_valid_own_id_trims_the_text_and_announces_the_change()
    {
        using var f = new Fixture();

        await f.Provider.AddAsync(RestTaskProvider.ListId, new TaskItem { Title = "  Melk halen  ", DueDate = new DateOnly(2026, 12, 24) });

        var task = Assert.Single(f.Server.Tasks);
        Assert.Equal("Melk halen", task.Text);
        Assert.Equal("2026-12-24", task.DueDate);
        Assert.StartsWith("dp-", task.Id);
        Assert.InRange(task.Id.Length, 1, 36);
        Assert.Matches("^[A-Za-z0-9_-]+$", task.Id);
        Assert.Equal([RestTaskProvider.ListId], f.Changed);
    }

    [Fact]
    public async Task Add_that_fails_on_the_network_is_repeated_once_with_the_same_id_without_a_double_task()
    {
        using var f = new Fixture();
        // Het eerste verzoek bereikt de server wel maar het antwoord gaat verloren; het tweede komt aan.
        f.Server.AddTask("bestaand", "Bestaand");
        f.Server.Failures.Enqueue(new HttpRequestException("verbinding verbroken"));

        await f.Provider.AddAsync(RestTaskProvider.ListId, new TaskItem { Title = "Nieuw" });

        var posts = f.Server.Requests.Where(r => r.Method == HttpMethod.Post).ToList();
        Assert.Equal(2, posts.Count);
        Assert.Equal((string?)JsonNode.Parse(posts[0].Body!)!["id"], (string?)JsonNode.Parse(posts[1].Body!)!["id"]);
        Assert.Equal(2, f.Server.Tasks.Count); // bestaand + precies één nieuwe
    }

    [Fact]
    public async Task A_text_that_is_too_long_is_refused_before_anything_is_sent()
    {
        using var f = new Fixture();

        var ex = await Assert.ThrowsAsync<TaskProviderException>(
            () => f.Provider.AddAsync(RestTaskProvider.ListId, new TaskItem { Title = new string('x', 257) }));

        Assert.Contains("256", ex.Message);
        Assert.Empty(f.Server.Requests);
    }

    [Fact]
    public async Task Update_sends_only_what_changed()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "Oud", due: "2026-12-24");
        var task = (await f.Provider.GetTasksAsync(RestTaskProvider.ListId)).Single();

        task.Title = "Nieuw";
        await f.Provider.UpdateAsync([task]);
        task.IsCompleted = true;
        await f.Provider.UpdateAsync([task]);
        task.DueDate = null;
        await f.Provider.UpdateAsync([task]);

        var bodies = f.Writes.Select(w => JsonNode.Parse(w.Body!)!.AsObject()).ToList();
        Assert.Equal(["text"], bodies[0].Select(p => p.Key));
        Assert.Equal(["done"], bodies[1].Select(p => p.Key));
        Assert.Equal(["dueDate"], bodies[2].Select(p => p.Key));
        Assert.Null(bodies[2]["dueDate"]);
        Assert.Equal("Nieuw", f.Server.Tasks[0].Text);
        Assert.True(f.Server.Tasks[0].Done);
        Assert.Null(f.Server.Tasks[0].DueDate);
    }

    [Fact]
    public async Task Reopening_sends_done_false()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A", done: true);
        var task = (await f.Provider.GetTasksAsync(RestTaskProvider.ListId)).Single();
        task.IsCompleted = false;

        await f.Provider.UpdateAsync([task]);

        Assert.Equal(false, (bool?)JsonNode.Parse(Assert.Single(f.Writes).Body!)!["done"]);
        Assert.False(f.Server.Tasks[0].Done);
    }

    [Fact]
    public async Task Changing_only_importance_or_order_sends_nothing()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        var task = (await f.Provider.GetTasksAsync(RestTaskProvider.ListId)).Single();
        task.Importance = TaskImportance.High;
        task.SortOrder = 99;

        await f.Provider.UpdateAsync([task]);

        Assert.Empty(f.Writes);
        Assert.Empty(f.Changed);
    }

    [Fact]
    public async Task Deleting_a_task_that_is_already_gone_counts_as_done()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);
        f.Server.Tasks.Clear(); // elders verwijderd
        f.Server.Touch();

        await f.Provider.DeleteAsync(RestTaskProvider.ListId, "a"); // geen fout

        Assert.Equal([RestTaskProvider.ListId], f.Changed);
    }

    [Fact]
    public async Task Moving_is_not_supported_and_says_so()
    {
        using var f = new Fixture();

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => f.Provider.MoveAsync(RestTaskProvider.ListId, "a", 0));

        Assert.Contains("verplaatsen", ex.Message);
        Assert.Empty(f.Server.Requests);
    }

    [Fact]
    public async Task Removing_a_pane_never_touches_the_tasks_at_the_source()
    {
        using var f = new Fixture();
        f.Server.AddTask("a", "A");
        await f.Provider.GetTasksAsync(RestTaskProvider.ListId);
        var before = f.Server.Requests.Count;

        await f.Provider.DeleteListAsync(RestTaskProvider.ListId);

        Assert.Equal(before, f.Server.Requests.Count);
        Assert.Single(f.Server.Tasks);
    }
}

