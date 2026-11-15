using System.Net;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.Json.Nodes;
using DonkiePane.Plugins.RestTasks;

namespace DonkiePane.Tasks.Tests;

public sealed class RestTasksClientTests
{
    private static readonly Uri Base = new("https://dash.test:4173/");

    private static RestTasksClient ClientFor(FakeTasksServer server, string token = FakeTasksServer.ValidToken) =>
        new(new HttpClient(server), Base, () => token);

    [Fact]
    public async Task Get_returns_the_tasks_and_the_revision_with_a_bearer_token()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "Melk halen", due: "2026-12-24");
        server.AddTask("b", "Bellen", done: true);

        var list = await ClientFor(server).GetAsync(null);

        Assert.NotNull(list);
        Assert.Equal(server.Revision, list.Revision);
        Assert.Equal(["a", "b"], list.Tasks.Select(t => t.Id));
        Assert.Equal("2026-12-24", list.Tasks[0].DueDate);
        Assert.True(list.Tasks[1].Done);
        Assert.Equal(FakeTasksServer.ValidToken, server.Requests[0].Bearer);
        Assert.Equal("/api/v1/tasks", server.Requests[0].Path);
    }

    [Fact]
    public async Task An_unchanged_revision_gives_304_and_costs_no_body()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        var client = ClientFor(server);
        var first = await client.GetAsync(null);

        var second = await client.GetAsync(first!.Revision);

        Assert.Null(second); // niet gewijzigd
        Assert.Equal($"\"{first.Revision}\"", server.Requests[1].IfNoneMatch);
    }

    [Fact]
    public async Task A_change_elsewhere_gives_the_new_list()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        var client = ClientFor(server);
        var first = await client.GetAsync(null);
        server.AddTask("b", "Van mijn telefoon");

        var second = await client.GetAsync(first!.Revision);

        Assert.NotNull(second);
        Assert.Equal(2, second.Tasks.Count);
        Assert.NotEqual(first.Revision, second.Revision);
    }

    [Fact]
    public async Task Add_sends_our_own_id_and_repeating_it_creates_no_second_task()
    {
        var server = new FakeTasksServer();
        var client = ClientFor(server);

        var first = await client.AddAsync("dp-1", "Melk halen", new DateOnly(2026, 12, 24));
        var again = await client.AddAsync("dp-1", "Melk halen", new DateOnly(2026, 12, 24));

        Assert.Equal("dp-1", first.Id);
        Assert.Equal("dp-1", again.Id);
        Assert.Single(server.Tasks);
        var body = JsonNode.Parse(server.Requests[0].Body!)!;
        Assert.Equal("dp-1", (string?)body["id"]);
        Assert.Equal("2026-12-24", (string?)body["dueDate"]);
    }

    [Fact]
    public async Task Patch_ticks_reopens_and_clears_the_due_date()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A", due: "2026-12-24");
        var client = ClientFor(server);

        await client.PatchAsync("a", new JsonObject { ["done"] = true });
        Assert.True(server.Tasks[0].Done);
        await client.PatchAsync("a", new JsonObject { ["done"] = false });
        Assert.False(server.Tasks[0].Done);
        await client.PatchAsync("a", new JsonObject { ["dueDate"] = null });
        Assert.Null(server.Tasks[0].DueDate);

        var clear = server.Requests[2].Body!;
        Assert.Contains("\"dueDate\":null", clear); // null wist, weglaten laat staan
    }

    [Fact]
    public async Task Delete_removes_and_a_second_delete_says_gone()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        var client = ClientFor(server);

        await client.DeleteAsync("a");

        Assert.Empty(server.Tasks);
        await Assert.ThrowsAsync<TaskGoneException>(() => client.DeleteAsync("a"));
    }

    [Fact]
    public async Task Patching_a_task_that_is_gone_says_so()
    {
        var server = new FakeTasksServer();

        await Assert.ThrowsAsync<TaskGoneException>(() => ClientFor(server).PatchAsync("weg", new JsonObject { ["done"] = true }));
    }

    [Fact]
    public async Task A_wrong_token_is_reported_as_unauthorized_and_not_as_a_connection_problem()
    {
        var server = new FakeTasksServer();

        var ex = await Assert.ThrowsAsync<SourceUnauthorizedException>(() => ClientFor(server, "fout").GetAsync(null));

        Assert.Contains("token", ex.Message);
        Assert.Contains("niet opnieuw", ex.Message);
    }

    [Fact]
    public async Task Lockout_uses_the_Retry_After_header()
    {
        var server = new FakeTasksServer { RetryAfterSeconds = 600 };
        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAnyAsync<TaskProviderException>(() => ClientFor(server, "fout").GetAsync(null));

        var ex = await Assert.ThrowsAsync<SourceRateLimitedException>(() => ClientFor(server).GetAsync(null));

        Assert.Equal(TimeSpan.FromSeconds(600), ex.RetryAfter);
    }

    [Fact]
    public async Task Without_the_header_the_wait_comes_from_the_body_or_defaults_to_a_minute()
    {
        var fromBody = new FakeHandler(_ => Responses.Raw("""{"error":"too_many_attempts","retryAfter":120}""", (HttpStatusCode)429));
        var ex1 = await Assert.ThrowsAsync<SourceRateLimitedException>(() => new RestTasksClient(new HttpClient(fromBody), Base, () => "t").GetAsync(null));
        Assert.Equal(TimeSpan.FromSeconds(120), ex1.RetryAfter);

        var nothing = new FakeHandler(_ => Responses.Raw("{}", (HttpStatusCode)429));
        var ex2 = await Assert.ThrowsAsync<SourceRateLimitedException>(() => new RestTasksClient(new HttpClient(nothing), Base, () => "t").GetAsync(null));
        Assert.Equal(TimeSpan.FromMinutes(1), ex2.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, """{"error":"forbidden"}""", "403")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"https_required"}""", "https://")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":"invalid"}""", "ongeldige")]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, """{"error":"payload_too_large"}""", "te groot")]
    [InlineData(HttpStatusCode.InternalServerError, "kapot", "500")]
    public async Task Other_errors_become_readable_messages(HttpStatusCode status, string body, string expected)
    {
        var handler = new FakeHandler(_ => Responses.Raw(body, status));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => new RestTasksClient(new HttpClient(handler), Base, () => "t").GetAsync(null));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task No_connection_is_a_readable_error_with_the_cause_attached()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("dns"));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => new RestTasksClient(new HttpClient(handler), Base, () => "t").GetAsync(null));

        Assert.Contains("Geen verbinding", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task A_failed_tls_handshake_points_at_the_fingerprint()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("ssl", new AuthenticationException("De externe certificaat is ongeldig")));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => new RestTasksClient(new HttpClient(handler), Base, () => "t").GetAsync(null));

        Assert.Contains("vingerafdruk", ex.Message);
        Assert.DoesNotContain("Geen verbinding", ex.Message);
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

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new RestTasksClient(new HttpClient(handler), Base, () => "t").GetAsync(null, cts.Token));
    }

    [Fact]
    public async Task The_token_is_asked_for_on_every_request_so_a_changed_token_is_used()
    {
        var server = new FakeTasksServer();
        var token = "fout";
        var client = new RestTasksClient(new HttpClient(server), Base, () => token);

        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => client.GetAsync(null));
        token = FakeTasksServer.ValidToken;

        Assert.NotNull(await client.GetAsync(null));
    }

    [Fact]
    public void Due_dates_are_plain_dates()
    {
        Assert.Equal("2026-01-05", RestTasksClient.DueString(new DateOnly(2026, 1, 5)));
    }
}
