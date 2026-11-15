using System.Net;
using System.Text.Json.Nodes;
using DonkiePane.Plugins.Google;

namespace DonkiePane.Tasks.Tests;

public sealed class GoogleTaskProviderTests
{
    private sealed class Fixture : IDisposable
    {
        public List<object> Remote { get; set; } = [];
        public bool Offline { get; set; }
        public FakeHandler Handler { get; }
        public GoogleTaskProvider Provider { get; }
        public List<string> Changed { get; } = [];
        public List<TaskProviderStatus> Statuses { get; } = [];

        public Fixture()
        {
            Handler = new FakeHandler(Respond);
            var client = new GoogleTasksClient(new HttpClient(Handler), (_, _) => Task.FromResult("tok"));
            Provider = new GoogleTaskProvider(client) { PollInterval = TimeSpan.FromHours(1) }; // de timer doet niets tijdens een test
            Provider.ListChanged += (_, listId) => Changed.Add(listId);
            Provider.StatusChanged += (_, status) => Statuses.Add(status);
        }

        private HttpResponseMessage Respond(RecordedRequest request)
        {
            if (Offline) throw new HttpRequestException("geen netwerk");
            if (request.Method == HttpMethod.Get) return Responses.Json(new { items = Remote });
            // Aanmaken en verplaatsen geven bij Google de taak terug; patchen ook, verwijderen niet.
            return request.Method == HttpMethod.Delete ? Responses.NoContent() : Responses.Json(new { id = "nieuw", title = "" });
        }

        public IEnumerable<RecordedRequest> Writes => Handler.Requests.Where(r => r.Method != HttpMethod.Get);

        public void Dispose() => Provider.Dispose();
    }

    private static object Item(string id, string title, string position, string status = "needsAction", string? due = null, string? completed = null) =>
        new { id, title, position, status, due, completed, updated = "2026-11-10T08:00:00.000Z", etag = "e-" + id };

    [Fact]
    public async Task Open_tasks_follow_Googles_order_and_completed_ones_are_marked()
    {
        using var f = new Fixture();
        f.Remote =
        [
            Item("c", "Derde", "00000000000000000030", due: "2026-11-20T00:00:00.000Z"),
            Item("a", "Eerste", "00000000000000000010"),
            Item("done", "Klaar", "00000000000000000005", "completed", completed: "2026-11-14T09:30:00.000Z"),
            Item("b", "Tweede", "00000000000000000020"),
        ];

        var tasks = await f.Provider.GetTasksAsync("L1");

        Assert.Equal(["a", "b", "c"], TaskOrdering.Ordered(tasks).Select(t => t.Id));
        var third = tasks.Single(t => t.Id == "c");
        Assert.Equal(new DateOnly(2026, 11, 20), third.DueDate);
        Assert.Equal("L1", third.ListId);
        Assert.Equal("c", third.ExternalId);
        var done = Assert.Single(TaskOrdering.Completed(tasks));
        Assert.Equal("Klaar", done.Title);
        Assert.Equal(new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero), done.CompletedAt);
    }

    [Fact]
    public void Capabilities_hide_the_star_and_protect_the_list_when_a_pane_is_removed()
    {
        using var f = new Fixture();

        Assert.False(f.Provider.Capabilities.Importance);
        Assert.False(f.Provider.Capabilities.PaneOwnsTasks);
    }

    [Fact]
    public async Task Removing_a_pane_never_deletes_anything_at_Google()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");
        var before = f.Handler.Requests.Count;

        await f.Provider.DeleteListAsync("L1");

        Assert.Equal(before, f.Handler.Requests.Count); // geen enkel verzoek
    }

    [Fact]
    public async Task Editing_the_title_patches_only_the_title()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "Oud", "1")];
        var task = (await f.Provider.GetTasksAsync("L1")).Single();
        task.Title = "Nieuw";

        await f.Provider.UpdateAsync([task]);

        var patch = JsonNode.Parse(Assert.Single(f.Writes).Body!)!.AsObject();
        Assert.Equal(["title"], patch.Select(p => p.Key));
        Assert.Equal("Nieuw", (string?)patch["title"]);
        Assert.Equal(["L1"], f.Changed);
    }

    [Fact]
    public async Task Completing_sets_the_status()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        var task = (await f.Provider.GetTasksAsync("L1")).Single();
        task.IsCompleted = true;

        await f.Provider.UpdateAsync([task]);

        var patch = JsonNode.Parse(Assert.Single(f.Writes).Body!)!.AsObject();
        Assert.Equal("completed", (string?)patch["status"]);
        Assert.False(patch.ContainsKey("completed")); // Google zet de datum zelf
    }

    [Fact]
    public async Task Reopening_clears_the_completed_date_explicitly()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1", "completed", completed: "2026-11-14T09:30:00.000Z")];
        var task = (await f.Provider.GetTasksAsync("L1")).Single();
        task.IsCompleted = false;

        await f.Provider.UpdateAsync([task]);

        var body = Assert.Single(f.Writes).Body!;
        var patch = JsonNode.Parse(body)!.AsObject();
        Assert.Equal("needsAction", (string?)patch["status"]);
        Assert.True(patch.ContainsKey("completed"));
        Assert.Null(patch["completed"]);
        Assert.Contains("\"completed\":null", body);
    }

    [Fact]
    public async Task Setting_and_clearing_the_due_date()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1", due: "2026-11-20T00:00:00.000Z")];
        var task = (await f.Provider.GetTasksAsync("L1")).Single();

        task.DueDate = new DateOnly(2026, 12, 1);
        await f.Provider.UpdateAsync([task]);
        task.DueDate = null;
        await f.Provider.UpdateAsync([task]);

        var writes = f.Writes.ToList();
        Assert.Equal("2026-12-01T00:00:00.000Z", (string?)JsonNode.Parse(writes[0].Body!)!["due"]);
        var clear = JsonNode.Parse(writes[1].Body!)!.AsObject();
        Assert.True(clear.ContainsKey("due"));
        Assert.Null(clear["due"]);
    }

    [Fact]
    public async Task Changing_only_importance_sends_nothing_because_Google_has_no_such_field()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        var task = (await f.Provider.GetTasksAsync("L1")).Single();
        task.Importance = TaskImportance.High;

        await f.Provider.UpdateAsync([task]);

        Assert.Empty(f.Writes);
        Assert.Empty(f.Changed);
    }

    [Fact]
    public async Task Move_puts_the_task_below_its_new_neighbour()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "10"), Item("b", "B", "20"), Item("c", "C", "30")];
        await f.Provider.GetTasksAsync("L1");

        await f.Provider.MoveAsync("L1", "a", 3);   // achteraan
        await f.Provider.MoveAsync("L1", "c", 0);   // bovenaan
        await f.Provider.MoveAsync("L1", "a", 2);   // vóór "c", dus onder "b"

        var moves = f.Writes.ToList();
        Assert.EndsWith("/tasks/a/move?previous=c", moves[0].PathAndQuery);
        Assert.EndsWith("/tasks/c/move", moves[1].PathAndQuery);
        Assert.EndsWith("/tasks/a/move?previous=b", moves[2].PathAndQuery);
        Assert.Equal(["L1", "L1", "L1"], f.Changed);
    }

    [Fact]
    public async Task Add_posts_to_the_list_and_announces_the_change()
    {
        using var f = new Fixture();

        await f.Provider.AddAsync("L1", new TaskItem { Title = "Nieuw", DueDate = new DateOnly(2026, 11, 20) });

        var post = Assert.Single(f.Writes);
        Assert.Equal(HttpMethod.Post, post.Method);
        Assert.EndsWith("/lists/L1/tasks", post.PathAndQuery);
        Assert.Equal("Nieuw", (string?)JsonNode.Parse(post.Body!)!["title"]);
        Assert.Equal(["L1"], f.Changed);
    }

    [Fact]
    public async Task Delete_removes_the_task_at_Google_and_announces_the_change()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");

        await f.Provider.DeleteAsync("L1", "a");

        var delete = Assert.Single(f.Writes);
        Assert.Equal(HttpMethod.Delete, delete.Method);
        Assert.EndsWith("/lists/L1/tasks/a", delete.PathAndQuery);
        Assert.Equal(["L1"], f.Changed);
    }

    [Fact]
    public async Task Offline_after_a_good_fetch_shows_the_last_known_tasks_and_a_message()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");
        f.Offline = true;

        var tasks = await f.Provider.GetTasksAsync("L1");

        Assert.Equal("a", Assert.Single(tasks).Id);
        var status = f.Statuses.Last();
        Assert.False(status.IsOnline);
        Assert.Contains("Geen verbinding", status.Message);
    }

    [Fact]
    public async Task Offline_on_the_very_first_fetch_is_an_error()
    {
        using var f = new Fixture();
        f.Offline = true;

        await Assert.ThrowsAsync<TaskProviderException>(() => f.Provider.GetTasksAsync("L1"));
    }

    [Fact]
    public async Task Coming_back_online_clears_the_message()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");
        f.Offline = true;
        await f.Provider.GetTasksAsync("L1");
        f.Offline = false;

        await f.Provider.GetTasksAsync("L1");

        var status = f.Statuses.Last();
        Assert.True(status.IsOnline);
        Assert.Null(status.Message);
    }

    [Fact]
    public async Task Polling_announces_changes_made_elsewhere_but_not_when_nothing_changed()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");

        await f.Provider.PollOnceAsync();
        Assert.Empty(f.Changed); // niets veranderd

        f.Remote = [Item("a", "A", "1"), Item("b", "Van mijn telefoon", "2")];
        await f.Provider.PollOnceAsync();
        Assert.Equal(["L1"], f.Changed);

        await f.Provider.PollOnceAsync();
        Assert.Equal(["L1"], f.Changed); // geen tweede melding voor dezelfde wijziging
    }

    [Fact]
    public async Task Polling_notices_a_task_completed_on_another_device()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");

        f.Remote = [Item("a", "A", "1", "completed", completed: "2026-11-15T10:00:00.000Z")];
        await f.Provider.PollOnceAsync();

        Assert.Equal(["L1"], f.Changed);
    }

    [Fact]
    public async Task Polling_while_offline_reports_status_but_does_not_throw()
    {
        using var f = new Fixture();
        f.Remote = [Item("a", "A", "1")];
        await f.Provider.GetTasksAsync("L1");
        f.Offline = true;

        await f.Provider.PollOnceAsync();

        Assert.False(f.Statuses.Last().IsOnline);
        Assert.Empty(f.Changed);
    }

    [Fact]
    public async Task A_forbidden_answer_is_not_called_offline()
    {
        var handler = new FakeHandler(_ => Responses.Raw("{}", HttpStatusCode.Forbidden));
        var provider = new GoogleTaskProvider(new GoogleTasksClient(new HttpClient(handler), (_, _) => Task_("tok"))) { PollInterval = TimeSpan.FromHours(1) };
        var statuses = new List<TaskProviderStatus>();
        provider.StatusChanged += (_, s) => statuses.Add(s);

        await Assert.ThrowsAsync<TaskProviderException>(() => provider.GetTasksAsync("L1"));

        Assert.True(statuses.Last().IsOnline); // er is wel verbinding; alleen de toegang is geweigerd
        provider.Dispose();
    }

    private static Task<string> Task_(string value) => System.Threading.Tasks.Task.FromResult(value);
}


