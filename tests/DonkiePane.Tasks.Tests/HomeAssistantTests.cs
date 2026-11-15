using System.Net;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using DonkiePane.Plugins.HomeAssistant;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.Tasks.Tests;

public sealed class HomeAssistantClientTests
{
    private static readonly Uri Base = new("http://ha.test:8123/");

    private static HomeAssistantClient ClientFor(FakeHomeAssistant server, string token = FakeHomeAssistant.ValidToken) =>
        new(new HttpClient(server), Base, () => token);

    private static FakeHomeAssistant ServerWithList()
    {
        var server = new FakeHomeAssistant();
        server.AddList("todo.boodschappen", "Boodschappen");
        return server;
    }

    [Fact]
    public async Task Only_todo_entities_are_lists_with_their_friendly_name_sorted()
    {
        var server = new FakeHomeAssistant();
        server.AddList("todo.zzz", "Werk");
        server.AddList("todo.boodschappen", "Boodschappen");

        var lists = await ClientFor(server).ListTodoListsAsync();

        Assert.Equal(["Boodschappen", "Werk"], lists.Select(l => l.Title)); // light.keuken doet niet mee
        Assert.Equal(["todo.boodschappen", "todo.zzz"], lists.Select(l => l.EntityId));
        Assert.Equal(FakeHomeAssistant.ValidToken, server.Requests[0].Bearer);
        Assert.Equal("/api/states", server.Requests[0].Path);
    }

    [Fact]
    public async Task Items_are_read_with_return_response_and_with_both_statuses_so_completed_ones_are_included()
    {
        var server = ServerWithList();
        server.Add("todo.boodschappen", "Melk", due: "2026-12-24");
        server.Add("todo.boodschappen", "Brood", status: "completed");

        var items = await ClientFor(server).GetItemsAsync("todo.boodschappen");

        Assert.Equal(["Melk", "Brood"], items.Select(i => i.Summary));
        Assert.Equal("completed", items[1].Status);
        Assert.Equal("2026-12-24", items[0].Due);
        Assert.Null(items[1].Due);
        var request = server.Requests.Last();
        Assert.Contains("return_response", request.Query);
        Assert.Equal("/api/services/todo/get_items", request.Path);
        Assert.Equal(["needs_action", "completed"], request.Body!["status"]!.AsArray().Select(s => (string?)s));
    }

    [Fact]
    public async Task Writes_never_use_return_response_because_Home_Assistant_answers_a_400()
    {
        var server = ServerWithList();
        var item = server.Add("todo.boodschappen", "Melk");
        var client = ClientFor(server);

        await client.AddItemAsync("todo.boodschappen", "Brood", new DateOnly(2026, 12, 24));
        await client.UpdateItemAsync("todo.boodschappen", item.Uid, new JsonObject { ["status"] = "completed" });
        await client.RemoveItemAsync("todo.boodschappen", item.Uid);

        Assert.All(server.Requests, r => Assert.DoesNotContain("return_response", r.Query));
        Assert.Equal("Brood", server.Lists["todo.boodschappen"].Items.Single().Summary);
        Assert.Equal("2026-12-24", server.Lists["todo.boodschappen"].Items.Single().Due);
    }

    [Fact]
    public async Task Update_names_the_item_by_uid_and_the_list_as_entity()
    {
        var server = ServerWithList();
        var item = server.Add("todo.boodschappen", "Melk");

        await ClientFor(server).UpdateItemAsync("todo.boodschappen", item.Uid, new JsonObject { ["rename"] = "Havermelk", ["status"] = "completed" });

        var body = server.Requests.Last().Body!;
        Assert.Equal(item.Uid, (string?)body["item"]);
        Assert.Equal("todo.boodschappen", (string?)body["entity_id"]);
        Assert.Equal("Havermelk", server.Lists["todo.boodschappen"].Items.Single().Summary);
        Assert.Equal("completed", server.Lists["todo.boodschappen"].Items.Single().Status);
    }

    [Fact]
    public async Task A_wrong_token_is_unauthorized_and_does_not_look_like_a_connection_problem()
    {
        var server = ServerWithList();

        var ex = await Assert.ThrowsAsync<SourceUnauthorizedException>(() => ClientFor(server, "fout").ListTodoListsAsync());

        Assert.Contains("token", ex.Message);
        Assert.Contains("niet opnieuw", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "", "404")]
    [InlineData(HttpStatusCode.BadRequest, "Bad request: kapot", "kapot")]
    [InlineData(HttpStatusCode.InternalServerError, "{\"message\":\"Unable to find to-do list item\"}", "Unable to find")]
    [InlineData(HttpStatusCode.MethodNotAllowed, "", "405")]
    public async Task Other_statuses_become_readable_messages_with_the_explanation_of_Home_Assistant(HttpStatusCode status, string body, string expected)
    {
        var server = ServerWithList();
        server.NextStatus = status;
        server.NextStatusBody = body;

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(server).ListTodoListsAsync());

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task A_restarting_Home_Assistant_is_temporary_so_changes_wait_instead_of_failing()
    {
        var server = ServerWithList();
        server.NextStatus = HttpStatusCode.ServiceUnavailable;

        var ex = await Assert.ThrowsAsync<SourceRateLimitedException>(() => ClientFor(server).ListTodoListsAsync());

        Assert.True(ex.RetryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task No_connection_and_a_wrong_certificate_are_told_apart()
    {
        var offline = new FakeHomeAssistant();
        offline.Failures.Enqueue(new HttpRequestException("dns"));
        var certificate = new FakeHomeAssistant();
        certificate.Failures.Enqueue(new HttpRequestException("ssl", new AuthenticationException("ongeldig")));

        var a = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(offline).ListTodoListsAsync());
        var b = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(certificate).ListTodoListsAsync());

        Assert.Contains("Geen verbinding", a.Message);
        Assert.IsType<HttpRequestException>(a.InnerException);
        Assert.Contains("vingerafdruk", b.Message);
        Assert.DoesNotContain("Geen verbinding", b.Message);
    }

    [Fact]
    public async Task An_answer_that_is_not_what_was_expected_is_reported_instead_of_crashing()
    {
        var handler = new FakeHandler(_ => Responses.Raw("""{"iets":"anders"}"""));
        var client = new HomeAssistantClient(new HttpClient(handler), Base, () => "t");

        await Assert.ThrowsAsync<TaskProviderException>(() => client.GetItemsAsync("todo.x"));
        await Assert.ThrowsAsync<TaskProviderException>(() => client.ListTodoListsAsync());
    }
}

public sealed class HomeAssistantProviderTests
{
    private const string List = "todo.boodschappen";

    private sealed class Rig : IDisposable
    {
        public FakeHomeAssistant Server { get; } = new();
        public TestTime Time { get; } = new();
        public HomeAssistantTaskProvider Provider { get; }
        public List<string> Changed { get; } = [];
        public List<TaskProviderStatus> Statuses { get; } = [];

        public Rig(string token = FakeHomeAssistant.ValidToken)
        {
            Server.AddList(List, "Boodschappen");
            var client = new HomeAssistantClient(new HttpClient(Server), new Uri("http://ha.test:8123/"), () => token);
            Provider = new HomeAssistantTaskProvider(client, Time) { PollInterval = TimeSpan.FromHours(1) };
            Provider.ListChanged += (_, id) => Changed.Add(id);
            Provider.StatusChanged += (_, s) => Statuses.Add(s);
        }

        public IEnumerable<HaRequest> Writes => Server.Requests.Where(r => r.Path.Contains("add_item") || r.Path.Contains("update_item") || r.Path.Contains("remove_item"));

        public void Dispose() => Provider.Dispose();
    }

    [Fact]
    public async Task Tasks_with_a_deadline_come_first_then_the_rest_in_the_order_of_Home_Assistant()
    {
        using var r = new Rig();
        r.Server.Add(List, "Eerst zonder", due: null);
        r.Server.Add(List, "Later", due: "2026-12-31");
        r.Server.Add(List, "Tweede zonder");
        r.Server.Add(List, "Vroeg", due: "2026-12-01");

        var tasks = await r.Provider.GetTasksAsync(List);

        Assert.Equal(["Vroeg", "Later", "Eerst zonder", "Tweede zonder"], TaskOrdering.Ordered(tasks).Select(t => t.Title));
        Assert.All(tasks, t => Assert.Equal(List, t.ListId));
    }

    [Fact]
    public async Task Completed_items_are_completed_and_a_deadline_with_a_time_counts_as_that_day()
    {
        using var r = new Rig();
        r.Server.Add(List, "Klaar", status: "completed");
        r.Server.Add(List, "Met tijd", due: "2026-12-24T10:30:00+01:00");
        r.Server.Add(List, "Met spatie", due: "2026-12-25 09:00:00");

        var tasks = await r.Provider.GetTasksAsync(List);

        Assert.Equal("Klaar", Assert.Single(TaskOrdering.Completed(tasks)).Title);
        Assert.Equal(new DateOnly(2026, 12, 24), tasks.Single(t => t.Title == "Met tijd").DueDate);
        Assert.Equal(new DateOnly(2026, 12, 25), tasks.Single(t => t.Title == "Met spatie").DueDate);
    }

    [Fact]
    public void An_unreadable_deadline_is_no_deadline_rather_than_an_error()
    {
        Assert.Null(HomeAssistantTaskProvider.ParseDate(null));
        Assert.Null(HomeAssistantTaskProvider.ParseDate(""));
        Assert.Null(HomeAssistantTaskProvider.ParseDate("morgen"));
        Assert.Null(HomeAssistantTaskProvider.ParseDate("2026-13-45"));
    }

    [Fact]
    public void The_capabilities_say_no_star_no_reordering_and_no_ownership()
    {
        using var r = new Rig();

        Assert.False(r.Provider.Capabilities.Importance);
        Assert.False(r.Provider.Capabilities.Reorder);
        Assert.False(r.Provider.Capabilities.PaneOwnsTasks);
    }

    [Fact]
    public async Task Add_sends_the_text_and_deadline_announces_the_change_and_is_not_repeated_after_a_network_error()
    {
        using var r = new Rig();

        await r.Provider.AddAsync(List, new TaskItem { Title = "  Melk  ", DueDate = new DateOnly(2026, 12, 24) });
        Assert.Equal("Melk", r.Server.Lists[List].Items.Single().Summary);
        Assert.Equal([List], r.Changed);

        r.Server.Failures.Enqueue(new HttpRequestException("verbroken"));
        await Assert.ThrowsAsync<TaskProviderException>(() => r.Provider.AddAsync(List, new TaskItem { Title = "Brood" }));
        // Home Assistant kent geen eigen id; herhalen zou een dubbele taak kunnen geven.
        Assert.Equal(2, r.Writes.Count());
    }

    [Fact]
    public async Task Update_sends_only_what_changed_and_clears_a_deadline_with_null()
    {
        using var r = new Rig();
        var item = r.Server.Add(List, "Oud", due: "2026-12-24");
        var task = (await r.Provider.GetTasksAsync(List)).Single();

        task.Title = "Nieuw";
        await r.Provider.UpdateAsync([task]);
        task.IsCompleted = true;
        await r.Provider.UpdateAsync([task]);
        task.DueDate = null;
        await r.Provider.UpdateAsync([task]);

        var bodies = r.Writes.Select(w => w.Body!).ToList();
        Assert.Equal(["rename"], bodies[0].Select(p => p.Key).Where(k => k is not ("entity_id" or "item")));
        Assert.Equal(["status"], bodies[1].Select(p => p.Key).Where(k => k is not ("entity_id" or "item")));
        Assert.Equal(["due_date"], bodies[2].Select(p => p.Key).Where(k => k is not ("entity_id" or "item")));
        Assert.Null(bodies[2]["due_date"]);
        Assert.Contains("\"due_date\":null", bodies[2].ToJsonString());
        Assert.Equal("Nieuw", item.Summary);
        Assert.Equal("completed", item.Status);
        Assert.Null(item.Due);
    }

    [Fact]
    public async Task Reopening_sends_needs_action()
    {
        using var r = new Rig();
        r.Server.Add(List, "Klaar", status: "completed");
        var task = (await r.Provider.GetTasksAsync(List)).Single();
        task.IsCompleted = false;

        await r.Provider.UpdateAsync([task]);

        Assert.Equal("needs_action", (string?)r.Writes.Single().Body!["status"]);
    }

    [Fact]
    public async Task Changing_only_importance_or_order_sends_nothing()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        var task = (await r.Provider.GetTasksAsync(List)).Single();
        task.Importance = TaskImportance.High;
        task.SortOrder = 99;

        await r.Provider.UpdateAsync([task]);

        Assert.Empty(r.Writes);
        Assert.Empty(r.Changed);
    }

    [Fact]
    public async Task Delete_removes_the_item_and_move_is_not_supported()
    {
        using var r = new Rig();
        var item = r.Server.Add(List, "Weg");
        await r.Provider.GetTasksAsync(List);

        await r.Provider.DeleteAsync(List, item.Uid);

        Assert.Empty(r.Server.Lists[List].Items);
        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => r.Provider.MoveAsync(List, "x", 0));
        Assert.Contains("verplaatsen", ex.Message);
    }

    [Fact]
    public async Task Polling_stays_quiet_when_nothing_changed_and_announces_a_change_made_elsewhere_once()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        await r.Provider.GetTasksAsync(List);

        await r.Provider.PollOnceAsync();
        Assert.Empty(r.Changed);

        r.Server.Add(List, "Van mijn telefoon");
        await r.Provider.PollOnceAsync();
        await r.Provider.PollOnceAsync();

        Assert.Equal([List], r.Changed);
    }

    [Fact]
    public async Task After_a_401_nothing_more_is_sent_so_Home_Assistant_cannot_ban_the_address()
    {
        using var r = new Rig(token: "fout");

        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => r.Provider.GetTasksAsync(List));
        var sent = r.Server.Requests.Count;

        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => r.Provider.GetTasksAsync(List));
        await r.Provider.PollOnceAsync();
        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => r.Provider.AddAsync(List, new TaskItem { Title = "X" }));

        Assert.Equal(sent, r.Server.Requests.Count);
        Assert.Equal(1, r.Server.FailedAttempts);
    }

    [Fact]
    public async Task A_restart_of_Home_Assistant_pauses_requests_and_shows_the_last_known_list()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        await r.Provider.GetTasksAsync(List);
        r.Server.NextStatus = HttpStatusCode.ServiceUnavailable;

        var shown = await r.Provider.GetTasksAsync(List);
        var sent = r.Server.Requests.Count;
        await r.Provider.GetTasksAsync(List); // binnen de wachttijd

        Assert.Equal("A", Assert.Single(shown).Title);
        Assert.Equal(sent, r.Server.Requests.Count);
        Assert.Contains("wachten", r.Statuses.Last().Message);

        r.Time.Now = r.Time.Now.AddSeconds(31);
        await r.Provider.GetTasksAsync(List);
        Assert.True(r.Server.Requests.Count > sent);
        Assert.Null(r.Statuses.Last().Message);
    }

    [Fact]
    public async Task Offline_after_a_good_fetch_shows_the_last_tasks_and_a_message_and_the_first_fetch_offline_is_an_error()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        r.Server.Failures.Enqueue(new HttpRequestException("weg"));
        await Assert.ThrowsAsync<TaskProviderException>(() => r.Provider.GetTasksAsync(List));

        await r.Provider.GetTasksAsync(List); // goed
        r.Server.Failures.Enqueue(new HttpRequestException("weg"));
        var shown = await r.Provider.GetTasksAsync(List);

        Assert.Equal("A", Assert.Single(shown).Title);
        Assert.False(r.Statuses.Last().IsOnline);
    }

    [Fact]
    public async Task Removing_a_pane_never_touches_the_items_in_Home_Assistant()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        await r.Provider.GetTasksAsync(List);
        var sent = r.Server.Requests.Count;

        await r.Provider.DeleteListAsync(List);

        Assert.Equal(sent, r.Server.Requests.Count);
        Assert.Single(r.Server.Lists[List].Items);
    }

    [Fact]
    public async Task The_refresh_button_picks_up_a_change_without_waiting()
    {
        using var r = new Rig();
        r.Server.Add(List, "A");
        await r.Provider.GetTasksAsync(List);
        r.Server.Add(List, "Nieuw elders");

        await ((IRefreshableTaskProvider)r.Provider).RefreshAsync();

        Assert.Equal([List], r.Changed);
    }
}

public sealed class HomeAssistantSourceTests
{
    private sealed class Secrets(string? token) : ISecretStore
    {
        public string? Get(string key) => key == HomeAssistantPlugin.TokenKey ? token : null;
        public void Set(string key, string? value) { }
        public void Clear() { }
    }

    private static SourceContext Context(string url, string? token, bool allowHttp = false, string fingerprint = "") =>
        new(new Dictionary<string, string>
        {
            [HomeAssistantPlugin.BaseUrlKey] = url,
            [HomeAssistantPlugin.AllowHttpKey] = allowHttp ? "true" : "false",
            [HomeAssistantPlugin.FingerprintKey] = fingerprint,
        }, new Secrets(token), _ => Task.CompletedTask);

    [Theory]
    [InlineData("https://ha.voorbeeld.nl", false)]
    [InlineData("https://ha.voorbeeld.nl:8123/", false)]
    [InlineData("http://homeassistant.local:8123", true)]
    [InlineData("https://ha.voorbeeld.nl", true)]
    public void These_addresses_are_accepted(string url, bool allowHttp) =>
        Assert.NotNull(HomeAssistantSource.ParseBaseUri(url, allowHttp));

    [Theory]
    [InlineData("http://homeassistant.local:8123", false)] // zonder uitdrukkelijke keuze nooit onversleuteld
    [InlineData("homeassistant.local", false)]
    [InlineData("homeassistant.local", true)]
    [InlineData("ftp://ha.voorbeeld.nl", true)]
    [InlineData("", true)]
    public void These_addresses_are_refused_with_an_explanation(string url, bool allowHttp)
    {
        var ex = Assert.Throws<FormatException>(() => HomeAssistantSource.ParseBaseUri(url, allowHttp));
        Assert.False(string.IsNullOrWhiteSpace(ex.Message));
    }

    [Fact]
    public void The_refusal_of_http_explains_the_way_out()
    {
        var ex = Assert.Throws<FormatException>(() => HomeAssistantSource.ParseBaseUri("http://homeassistant.local:8123", allowHttp: false));

        Assert.Contains("https://", ex.Message);
        Assert.Contains("toestaan", ex.Message);
    }

    [Fact]
    public async Task The_lists_come_from_Home_Assistant_after_the_connection_works()
    {
        var server = new FakeHomeAssistant();
        server.AddList("todo.boodschappen", "Boodschappen");
        server.AddList("todo.werk", "Werk");
        using var source = new HomeAssistantSource(Context("http://ha.test:8123", FakeHomeAssistant.ValidToken, allowHttp: true), server);

        var lists = await source.GetListsAsync();
        var check = await source.TestAsync();

        Assert.Equal(["Boodschappen", "Werk"], lists.Select(l => l.Title));
        Assert.Equal("todo.boodschappen", lists[0].Id);
        Assert.True(check.Ok);
        Assert.Contains("2 lijsten", check.Message);
    }

    [Fact]
    public async Task A_Home_Assistant_without_todo_lists_is_explained()
    {
        using var source = new HomeAssistantSource(Context("http://ha.test:8123", FakeHomeAssistant.ValidToken, allowHttp: true), new FakeHomeAssistant());

        var check = await source.TestAsync();

        Assert.False(check.Ok);
        Assert.Contains("geen to-do lijsten", check.Message);
    }

    [Theory]
    [InlineData("http://ha.test:8123", "tok", false, "", "https://")]
    [InlineData("", "tok", false, "", "adres")]
    [InlineData("https://ha.test", null, false, "", "token")]
    [InlineData("https://ha.test", "tok", false, "kapot", "SHA-256")]
    public async Task A_wrong_setup_shows_the_reason_in_the_pane_instead_of_crashing(string url, string? token, bool allowHttp, string fingerprint, string expected)
    {
        using var source = new HomeAssistantSource(Context(url, token, allowHttp, fingerprint));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => source.Provider.GetTasksAsync("todo.x"));
        var check = await source.TestAsync();

        Assert.Contains(expected, ex.Message);
        Assert.False(check.Ok);
        Assert.Contains(expected, check.Message);
    }

    [Fact]
    public void The_plugin_describes_its_fields_and_warns_about_unencrypted_connections()
    {
        var plugin = new HomeAssistantPlugin();

        Assert.Equal("home-assistant-todo", plugin.TypeId);
        Assert.Equal(SettingKind.Secret, plugin.Fields.Single(f => f.Key == HomeAssistantPlugin.TokenKey).Kind);
        var http = plugin.Fields.Single(f => f.Key == HomeAssistantPlugin.AllowHttpKey);
        Assert.Equal(SettingKind.Bool, http.Kind);
        Assert.False(http.Required);
        Assert.Contains("onversleuteld", http.Help);
        Assert.Empty(plugin.Actions);
    }

    [Fact]
    public void Flags_are_read_as_true_or_false()
    {
        var on = new SourceContext(new Dictionary<string, string> { ["A"] = "true", ["B"] = "TRUE", ["C"] = "false", ["D"] = "" }, new Secrets(null), _ => Task.CompletedTask);

        Assert.True(on.Flag("A"));
        Assert.True(on.Flag("B"));
        Assert.False(on.Flag("C"));
        Assert.False(on.Flag("D"));
        Assert.False(on.Flag("bestaat-niet"));
    }
}
