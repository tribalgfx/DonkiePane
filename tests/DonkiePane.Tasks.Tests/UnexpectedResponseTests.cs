using System.Net;
using System.Net.Http.Headers;
using DonkiePane.Plugins.Google;
using DonkiePane.Plugins.HomeAssistant;
using DonkiePane.Plugins.RestTasks;
using DonkiePane.Tasks.Offline;

namespace DonkiePane.Tasks.Tests;

/// <summary>
/// Een bron die iets onverwachts terugstuurt (de inlogpagina van een proxy, JSON van een andere vorm, een serverfout)
/// mag de app nooit laten crashen, het verversen niet stilzetten en geen offline wijzigingen kosten.
/// </summary>
public sealed class UnexpectedResponseTests : IDisposable
{
    private const string LoginPage = "<!doctype html><html><body>Log in</body></html>";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static HttpClient Http(Func<RecordedRequest, HttpResponseMessage> respond) => new(new FakeHandler(respond));

    private static RestTasksClient Rest(Func<RecordedRequest, HttpResponseMessage> respond) =>
        new(Http(respond), new Uri("https://dash.test/"), () => "t");

    private static HomeAssistantClient HomeAssistant(Func<RecordedRequest, HttpResponseMessage> respond) =>
        new(Http(respond), new Uri("https://ha.test/"), () => "t");

    private static GoogleTasksClient Google(FakeHandler handler) => new(new HttpClient(handler), (_, _) => Task.FromResult("tok"));

    // ---------- REST ----------

    [Theory]
    [InlineData(LoginPage)]
    [InlineData("[]")]
    [InlineData("""{"tasks":"geen lijst"}""")]
    public async Task Rest_an_unreadable_answer_is_a_temporary_source_error_not_a_crash(string body)
    {
        var ex = await Assert.ThrowsAsync<SourceUnavailableException>(() => Rest(_ => Responses.Raw(body)).GetAsync(null));

        Assert.Contains("inlogpagina", ex.Message);
    }

    [Fact]
    public async Task Rest_tasks_without_an_id_are_skipped_and_impossible_values_are_made_safe()
    {
        var body = """{"revision":"1","tasks":[null,{"id":null,"text":"x"},{"id":"a","text":null,"createdAt":9223372036854775807}]}""";

        var list = await Rest(_ => Responses.Raw(body)).GetAsync(null);

        var task = Assert.Single(list!.Tasks);
        Assert.Equal("a", task.Id);
        Assert.Equal("", task.Text);
        _ = DateTimeOffset.FromUnixTimeMilliseconds(task.CreatedAt); // gooit niet meer
    }

    [Fact]
    public async Task Rest_a_redirect_is_not_followed_but_explained()
    {
        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => Rest(_ =>
        {
            var response = Responses.Raw("", HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://login.voorbeeld.nl/");
            return response;
        }).GetAsync(null));

        Assert.Contains("verwijst door", ex.Message);
        Assert.Contains("login.voorbeeld.nl", ex.Message);
    }

    [Fact]
    public async Task HomeAssistant_a_redirect_is_not_followed_but_explained()
    {
        var ex = await Assert.ThrowsAsync<TaskProviderException>(() =>
            HomeAssistant(_ => Responses.Raw("", HttpStatusCode.MovedPermanently)).ListTodoListsAsync());

        Assert.Contains("verwijst door", ex.Message);
    }

    [Fact]
    public void The_connection_to_a_source_never_follows_redirects() =>
        Assert.False(DonkiePane.Tasks.Security.PinnedHttp.CreateHandler(null).AllowAutoRedirect);

    [Fact]
    public async Task Rest_a_server_error_is_temporary()
    {
        await Assert.ThrowsAsync<SourceUnavailableException>(() =>
            Rest(_ => Responses.Raw("Bad gateway", HttpStatusCode.BadGateway)).GetAsync(null));
    }

    [Fact]
    public async Task Rest_an_error_body_of_another_shape_still_gives_a_readable_message()
    {
        var ex = await Assert.ThrowsAsync<TaskProviderException>(() =>
            Rest(_ => Responses.Raw("""["kapot"]""", HttpStatusCode.BadRequest)).GetAsync(null));

        Assert.Contains("weigerde", ex.Message);
    }

    [Fact]
    public async Task Rest_an_absurd_retry_after_is_capped_at_an_hour()
    {
        var ex = await Assert.ThrowsAsync<SourceRateLimitedException>(() => Rest(_ =>
        {
            var response = Responses.Raw("{}", HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(int.MaxValue));
            return response;
        }).GetAsync(null));

        Assert.Equal(TimeSpan.FromHours(1), ex.RetryAfter);
    }

    [Fact]
    public async Task Rest_a_poll_that_gets_a_login_page_reports_it_and_does_not_throw()
    {
        var statuses = new List<TaskProviderStatus>();
        var client = Rest(_ => Responses.Raw(LoginPage));
        using var provider = new RestTaskProvider(client, new TestTime()) { PollInterval = TimeSpan.FromHours(1) };
        provider.StatusChanged += (_, s) => statuses.Add(s);

        await provider.PollOnceAsync();

        var status = Assert.Single(statuses);
        Assert.False(status.IsOnline);
        Assert.Contains("inlogpagina", status.Message);
    }

    // ---------- Home Assistant ----------

    [Fact]
    public async Task HomeAssistant_a_login_page_is_a_temporary_source_error()
    {
        await Assert.ThrowsAsync<SourceUnavailableException>(() => HomeAssistant(_ => Responses.Raw(LoginPage)).ListTodoListsAsync());
    }

    [Fact]
    public async Task HomeAssistant_states_of_another_shape_are_skipped()
    {
        var body = """[1,"x",{"entity_id":5},{"entity_id":"todo.a","attributes":{"friendly_name":7}}]""";

        var lists = await HomeAssistant(_ => Responses.Raw(body)).ListTodoListsAsync();

        var list = Assert.Single(lists);
        Assert.Equal("todo.a", list.EntityId);
        Assert.Equal("todo.a", list.Title);
    }

    [Fact]
    public async Task HomeAssistant_items_of_another_shape_are_skipped_or_made_safe()
    {
        var body = """{"service_response":{"todo.a":{"items":[1,{"summary":"zonder uid"},{"uid":"u","summary":3,"status":null}]}}}""";

        var items = await HomeAssistant(_ => Responses.Raw(body)).GetItemsAsync("todo.a");

        var item = Assert.Single(items);
        Assert.Equal("u", item.Uid);
        Assert.Equal("", item.Summary);
        Assert.Equal("needs_action", item.Status);
    }

    [Fact]
    public async Task HomeAssistant_a_proxy_that_cannot_reach_it_is_temporary()
    {
        await Assert.ThrowsAsync<SourceUnavailableException>(() =>
            HomeAssistant(_ => Responses.Raw("", HttpStatusCode.BadGateway)).ListTodoListsAsync());
    }

    // ---------- Google ----------

    [Fact]
    public async Task Google_a_login_page_is_a_temporary_source_error()
    {
        await Assert.ThrowsAsync<SourceUnavailableException>(() => Google(new FakeHandler(_ => Responses.Raw(LoginPage))).ListTasksAsync("L"));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Google_overload_and_outages_are_temporary(HttpStatusCode status)
    {
        await Assert.ThrowsAsync<SourceUnavailableException>(() => Google(new FakeHandler(_ => Responses.Raw("{}", status))).ListTasksAsync("L"));
    }

    [Fact]
    public async Task Google_a_page_token_that_repeats_does_not_loop_forever()
    {
        var handler = new FakeHandler(_ => Responses.Raw("""{"items":[{"id":"a","title":"A"}],"nextPageToken":"p"}"""));

        var tasks = await Google(handler).ListTasksAsync("L");

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(2, tasks.Count);
    }

    // ---------- Verversen ----------

    [Fact]
    public async Task Polling_keeps_going_after_an_unexpected_error()
    {
        var calls = 0;
        using var stop = new CancellationTokenSource();
        var loop = PollLoop.RunAsync(() => TimeSpan.FromSeconds(1), _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("een fout in een plug-in");
            return Task.CompletedTask;
        }, null, stop.Token);

        await Task.Delay(3500);
        stop.Cancel();
        await loop;

        Assert.InRange(calls, 2, 4);
    }

    // ---------- Offline wachtrij ----------

    private OfflineQueueProvider Queue(InMemoryTasks server) =>
        OfflineQueueProvider.Wrap(server, new PendingQueueStore(Path.Combine(_dir, "queue.json")));

    [Fact]
    public async Task A_change_made_online_is_not_undone_by_an_older_queued_change()
    {
        var server = new InMemoryTasks();
        var task = server.Seed("s1", "Begin");
        var queue = Queue(server);
        await queue.GetTasksAsync("L");

        server.Online = false;
        await queue.UpdateAsync([new TaskItem { Id = "s1", ListId = "L", Title = "Offline" }]);
        server.Online = true;
        await queue.UpdateAsync([new TaskItem { Id = "s1", ListId = "L", Title = "Nieuwer" }]);
        await queue.GetTasksAsync("L"); // verstuurt wat er nog wacht

        Assert.Equal(0, queue.PendingCount);
        Assert.Equal("Nieuwer", task.Title);
    }

    [Fact]
    public async Task A_server_error_while_sending_keeps_the_offline_changes()
    {
        var server = new InMemoryTasks();
        var queue = Queue(server);
        await queue.GetTasksAsync("L");
        server.Online = false;
        await queue.AddAsync("L", new TaskItem { Title = "Offline gemaakt" });
        server.Online = true;

        server.NextFailure = new SourceUnavailableException("De bron hapert even (502).");
        await queue.GetTasksAsync("L");
        Assert.Equal(1, queue.PendingCount); // niet weggegooid

        await queue.GetTasksAsync("L");
        Assert.Equal(0, queue.PendingCount);
        Assert.Contains(server.Server, t => t.Title == "Offline gemaakt");
    }

    [Fact]
    public async Task An_unexpected_error_while_sending_keeps_the_changes_and_reading_still_works()
    {
        var server = new InMemoryTasks();
        server.Seed("s1", "Bestaand");
        var queue = Queue(server);
        await queue.GetTasksAsync("L");
        server.Online = false;
        await queue.AddAsync("L", new TaskItem { Title = "Offline gemaakt" });
        server.Online = true;

        server.NextFailure = new InvalidOperationException("een fout in een plug-in");
        var view = await queue.GetTasksAsync("L"); // gooit niet

        Assert.Equal(1, queue.PendingCount);
        Assert.Contains(view, t => t.Title == "Bestaand");
    }
}
