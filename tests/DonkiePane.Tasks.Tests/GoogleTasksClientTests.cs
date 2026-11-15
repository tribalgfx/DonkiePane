using System.Net;
using System.Text.Json.Nodes;
using DonkiePane.Tasks.Google;

namespace DonkiePane.Tasks.Tests;

public sealed class GoogleTasksClientTests
{
    private static GoogleTasksClient ClientFor(FakeHandler handler, Func<bool, CancellationToken, Task<string>>? token = null) =>
        new(new HttpClient(handler), token ?? ((_, _) => Task.FromResult("tok")));

    [Fact]
    public async Task Lists_come_from_the_lists_endpoint_with_a_bearer_token()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { items = new[] { new { id = "L1", title = "Werk" }, new { id = "L2", title = "Thuis" } } }));

        var lists = await ClientFor(handler).ListListsAsync();

        Assert.Equal(["Werk", "Thuis"], lists.Select(l => l.Title));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("/tasks/v1/users/@me/lists", request.PathAndQuery);
        Assert.Equal("tok", request.Bearer);
    }

    [Fact]
    public async Task Tasks_are_fetched_page_by_page_including_completed_and_hidden()
    {
        var handler = new FakeHandler(r => r.PathAndQuery.Contains("pageToken=p2")
            ? Responses.Json(new { items = new[] { new { id = "b", title = "B" } } })
            : Responses.Json(new { items = new[] { new { id = "a", title = "A" } }, nextPageToken = "p2" }));

        var tasks = await ClientFor(handler).ListTasksAsync("L1");

        Assert.Equal(["a", "b"], tasks.Select(t => t.Id));
        Assert.Equal(2, handler.Requests.Count);
        foreach (var request in handler.Requests)
        {
            Assert.Contains("/lists/L1/tasks", request.PathAndQuery);
            Assert.Contains("showCompleted=true", request.PathAndQuery);
            Assert.Contains("showHidden=true", request.PathAndQuery); // anders ontbreken taken die in de Google-apps zijn afgevinkt
            Assert.Contains("maxResults=100", request.PathAndQuery);
        }
    }

    [Fact]
    public async Task Deleted_tasks_are_left_out()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { items = new object[] { new { id = "a", title = "A" }, new { id = "x", title = "Weg", deleted = true } } }));

        var tasks = await ClientFor(handler).ListTasksAsync("L1");

        Assert.Equal(["a"], tasks.Select(t => t.Id));
    }

    [Fact]
    public async Task An_empty_list_gives_no_tasks()
    {
        var handler = new FakeHandler(_ => Responses.Raw("{}"));

        Assert.Empty(await ClientFor(handler).ListTasksAsync("L1"));
    }

    [Fact]
    public async Task A_rejected_token_is_refreshed_once_and_the_request_repeated()
    {
        var calls = new List<bool>();
        var handler = new FakeHandler(r => r.Bearer == "oud"
            ? Responses.Raw("{}", HttpStatusCode.Unauthorized)
            : Responses.Json(new { items = Array.Empty<object>() }));
        var client = ClientFor(handler, (force, _) =>
        {
            calls.Add(force);
            return Task.FromResult(force ? "nieuw" : "oud");
        });

        await client.ListTasksAsync("L1");

        Assert.Equal([false, true], calls);
        Assert.Equal(["oud", "nieuw"], handler.Requests.Select(r => r.Bearer));
    }

    [Fact]
    public async Task A_second_rejection_is_reported_not_repeated_forever()
    {
        var handler = new FakeHandler(_ => Responses.Raw("{}", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(handler).ListTasksAsync("L1"));

        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Forbidden_mentions_the_admin_and_the_status_code()
    {
        var handler = new FakeHandler(_ => Responses.Raw("""{"error":{"message":"Not authorized"}}""", HttpStatusCode.Forbidden));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(handler).ListTasksAsync("L1"));

        Assert.Contains("403", ex.Message);
        Assert.Contains("beheerder", ex.Message);
        Assert.Contains("Not authorized", ex.Message);
    }

    [Fact]
    public async Task NotFound_says_the_list_or_task_is_gone()
    {
        var handler = new FakeHandler(_ => Responses.Raw("{}", HttpStatusCode.NotFound));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(handler).ListTasksAsync("L1"));

        Assert.Contains("bestaat niet meer", ex.Message);
    }

    [Fact]
    public async Task No_connection_becomes_a_readable_error_with_the_cause_attached()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("dns"));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => ClientFor(handler).ListTasksAsync("L1"));

        Assert.Contains("Geen verbinding", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Cancelling_is_not_turned_into_a_connection_error()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeHandler(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientFor(handler).ListTasksAsync("L1", cts.Token));
    }

    [Fact]
    public async Task Insert_posts_the_title_and_a_date_only_due()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { id = "n1", title = "Nieuw" }));

        var created = await ClientFor(handler).InsertAsync("L1", "Nieuw", new DateOnly(2026, 11, 20));

        Assert.Equal("n1", created.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/lists/L1/tasks", request.PathAndQuery);
        var body = JsonNode.Parse(request.Body!)!;
        Assert.Equal("Nieuw", (string?)body["title"]);
        Assert.Equal("2026-11-20T00:00:00.000Z", (string?)body["due"]);
    }

    [Fact]
    public async Task Insert_without_a_due_date_sends_none()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { id = "n1" }));

        await ClientFor(handler).InsertAsync("L1", "Zonder", null);

        Assert.Null(JsonNode.Parse(handler.Requests[0].Body!)!["due"]);
    }

    [Fact]
    public async Task Patch_and_delete_use_the_right_methods_and_paths()
    {
        var handler = new FakeHandler(_ => Responses.NoContent());
        var client = ClientFor(handler);

        await client.PatchAsync("L1", "T1", new JsonObject { ["title"] = "Nieuw" });
        await client.DeleteAsync("L1", "T1");

        Assert.Equal(HttpMethod.Patch, handler.Requests[0].Method);
        Assert.EndsWith("/lists/L1/tasks/T1", handler.Requests[0].PathAndQuery);
        Assert.Equal("Nieuw", (string?)JsonNode.Parse(handler.Requests[0].Body!)!["title"]);
        Assert.Equal(HttpMethod.Delete, handler.Requests[1].Method);
        Assert.EndsWith("/lists/L1/tasks/T1", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task Ids_with_special_characters_are_escaped()
    {
        var handler = new FakeHandler(_ => Responses.NoContent());

        await ClientFor(handler).DeleteAsync("L/1", "T 1");

        Assert.EndsWith("/lists/L%2F1/tasks/T%201", handler.Requests[0].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Move_puts_the_task_below_the_given_sibling_or_on_top()
    {
        var handler = new FakeHandler(_ => Responses.Json(new { id = "T1" }));
        var client = ClientFor(handler);

        await client.MoveAsync("L1", "T1", "T0");
        await client.MoveAsync("L1", "T1", null);

        Assert.All(handler.Requests, r => Assert.Equal(HttpMethod.Post, r.Method));
        Assert.EndsWith("/lists/L1/tasks/T1/move?previous=T0", handler.Requests[0].PathAndQuery);
        Assert.EndsWith("/lists/L1/tasks/T1/move", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public void Due_dates_are_sent_as_midnight_utc()
    {
        Assert.Equal("2026-01-05T00:00:00.000Z", GoogleTasksClient.DueString(new DateOnly(2026, 1, 5)));
    }
}
