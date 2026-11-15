using System.Net;
using DonkiePane.Plugins.Google;

namespace DonkiePane.Tasks.Tests;

public sealed class PkceTests
{
    [Fact]
    public void Challenge_matches_the_RFC_7636_example()
    {
        // Voorbeeld uit RFC 7636, bijlage B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            Pkce.ChallengeFor("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void Created_verifier_is_valid_and_matches_its_challenge()
    {
        var (verifier, challenge) = Pkce.Create();

        Assert.InRange(verifier.Length, 43, 128);
        Assert.Matches("^[A-Za-z0-9_-]+$", verifier);
        Assert.Equal(Pkce.ChallengeFor(verifier), challenge);
        Assert.NotEqual(verifier, Pkce.Create().Verifier);
    }
}

public sealed class LoopbackReceiverTests
{
    private static readonly HttpClient Browser = new();

    [Fact]
    public async Task Returns_the_code_when_the_state_matches()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("s1", TimeSpan.FromSeconds(10));

        var response = await Browser.GetAsync($"{receiver.RedirectUri}?code=abc%2F123&state=s1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("abc/123", await wait);
    }

    [Fact]
    public async Task Ignores_requests_with_another_state()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("goed", TimeSpan.FromSeconds(10));

        var evil = await Browser.GetAsync($"{receiver.RedirectUri}?code=gestolen&state=kwaad");
        Assert.Equal(HttpStatusCode.NotFound, evil.StatusCode);
        Assert.False(wait.IsCompleted);

        await Browser.GetAsync($"{receiver.RedirectUri}?code=echt&state=goed");
        Assert.Equal("echt", await wait);
    }

    [Fact]
    public async Task Other_paths_like_favicon_are_ignored()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("s", TimeSpan.FromSeconds(10));

        var favicon = await Browser.GetAsync($"http://127.0.0.1:{receiver.Port}/favicon.ico");
        Assert.Equal(HttpStatusCode.NotFound, favicon.StatusCode);

        await Browser.GetAsync($"{receiver.RedirectUri}?code=c&state=s");
        Assert.Equal("c", await wait);
    }

    [Fact]
    public async Task A_refusal_from_google_becomes_an_error()
    {
        using var receiver = new LoopbackReceiver();
        var wait = receiver.WaitForCodeAsync("s", TimeSpan.FromSeconds(10));

        await Browser.GetAsync($"{receiver.RedirectUri}?error=access_denied&state=s");

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => wait);
        Assert.Contains("access_denied", ex.Message);
    }

    [Fact]
    public async Task Waiting_too_long_times_out_with_a_clear_message()
    {
        using var receiver = new LoopbackReceiver();

        var ex = await Assert.ThrowsAsync<TaskProviderException>(
            () => receiver.WaitForCodeAsync("s", TimeSpan.FromMilliseconds(300)));

        Assert.Contains("te lang", ex.Message);
    }

    [Fact]
    public async Task Cancelling_is_not_reported_as_a_timeout()
    {
        using var receiver = new LoopbackReceiver();
        using var cts = new CancellationTokenSource();
        var wait = receiver.WaitForCodeAsync("s", TimeSpan.FromSeconds(30), cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}

public sealed class GoogleAuthTests
{
    private static readonly GoogleClientConfig Config = new("client-123", null);

    private static string Token(string access, string? refresh = null, int expires = 3600) =>
        refresh is null
            ? $$"""{"access_token":"{{access}}","expires_in":{{expires}},"token_type":"Bearer"}"""
            : $$"""{"access_token":"{{access}}","expires_in":{{expires}},"refresh_token":"{{refresh}}","token_type":"Bearer"}""";

    private static Dictionary<string, string> Form(RecordedRequest request) =>
        request.Body!.Split('&').Select(p => p.Split('='))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

    [Fact]
    public async Task Signing_in_exchanges_the_code_and_keeps_the_refresh_token()
    {
        var store = new MemoryTokenStore();
        var handler = new FakeHandler(_ => Responses.Raw(Token("acc-1", "ref-1")));
        var auth = new GoogleAuth(new HttpClient(handler), store, () => Config);
        Uri? opened = null;

        await auth.SignInAsync(url =>
        {
            opened = url;
            // De "browser": Google stuurt terug naar het adres uit de URL, met de state uit de URL.
            var query = LoopbackReceiver_Parse(url);
            _ = Task.Run(() => new HttpClient().GetAsync($"{query["redirect_uri"]}?code=the-code&state={query["state"]}"));
            return Task.CompletedTask;
        });

        // Het verzoek aan Google bevat de juiste dingen.
        var authQuery = LoopbackReceiver_Parse(opened!);
        Assert.Equal("client-123", authQuery["client_id"]);
        Assert.Equal(GoogleAuth.Scope, authQuery["scope"]);
        Assert.Equal("code", authQuery["response_type"]);
        Assert.Equal("S256", authQuery["code_challenge_method"]);
        Assert.StartsWith("http://127.0.0.1:", authQuery["redirect_uri"]);

        var exchange = Form(Assert.Single(handler.Requests));
        Assert.Equal("the-code", exchange["code"]);
        Assert.Equal("authorization_code", exchange["grant_type"]);
        Assert.Equal(authQuery["redirect_uri"], exchange["redirect_uri"]);
        Assert.Equal(authQuery["code_challenge"], Pkce.ChallengeFor(exchange["code_verifier"]));
        Assert.False(exchange.ContainsKey("client_secret")); // alleen meesturen als er een is

        Assert.Equal("ref-1", store.Load());
        Assert.Equal("acc-1", await auth.GetAccessTokenAsync()); // uit het geheugen
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task The_client_secret_is_sent_when_configured()
    {
        var handler = new FakeHandler(_ => Responses.Raw(Token("acc", "ref")));
        var auth = new GoogleAuth(new HttpClient(handler), new MemoryTokenStore("ref"), () => new GoogleClientConfig("id", "geheim"));

        await auth.GetAccessTokenAsync();

        Assert.Equal("geheim", Form(Assert.Single(handler.Requests))["client_secret"]);
    }

    [Fact]
    public async Task Access_token_is_refreshed_only_when_it_has_expired()
    {
        var time = new TestTime();
        var counter = 0;
        var handler = new FakeHandler(_ => Responses.Raw(Token($"acc-{++counter}")));
        var auth = new GoogleAuth(new HttpClient(handler), new MemoryTokenStore("ref-1"), () => Config, time);

        Assert.Equal("acc-1", await auth.GetAccessTokenAsync());
        Assert.Equal("acc-1", await auth.GetAccessTokenAsync()); // nog geldig

        time.Now = time.Now.AddMinutes(30);
        Assert.Equal("acc-1", await auth.GetAccessTokenAsync());

        time.Now = time.Now.AddMinutes(30); // 60 min: binnen de marge van 1 minuut voor het einde
        Assert.Equal("acc-2", await auth.GetAccessTokenAsync());
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("refresh_token", Form(handler.Requests[0])["grant_type"]);
    }

    [Fact]
    public async Task Force_refresh_always_asks_google_again()
    {
        var counter = 0;
        var handler = new FakeHandler(_ => Responses.Raw(Token($"acc-{++counter}")));
        var auth = new GoogleAuth(new HttpClient(handler), new MemoryTokenStore("ref"), () => Config);

        await auth.GetAccessTokenAsync();
        Assert.Equal("acc-2", await auth.GetAccessTokenAsync(forceRefresh: true));
    }

    [Fact]
    public async Task Revoked_token_signs_you_out_and_deletes_the_stored_token()
    {
        var store = new MemoryTokenStore("ref");
        var handler = new FakeHandler(_ => Responses.Raw("""{"error":"invalid_grant"}""", HttpStatusCode.BadRequest));
        var auth = new GoogleAuth(new HttpClient(handler), store, () => Config);

        await Assert.ThrowsAsync<NotSignedInException>(() => auth.GetAccessTokenAsync());

        Assert.Null(store.Load());
        Assert.False(auth.IsSignedIn);
    }

    [Fact]
    public async Task No_connection_keeps_the_stored_token()
    {
        var store = new MemoryTokenStore("ref");
        var handler = new FakeHandler(_ => throw new HttpRequestException("geen netwerk"));
        var auth = new GoogleAuth(new HttpClient(handler), store, () => Config);

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => auth.GetAccessTokenAsync());

        Assert.IsNotType<NotSignedInException>(ex);
        Assert.Equal("ref", store.Load()); // offline is geen reden om uit te loggen
    }

    [Fact]
    public async Task Without_a_stored_token_you_are_not_signed_in()
    {
        var auth = new GoogleAuth(new HttpClient(new FakeHandler(_ => Responses.Raw("{}"))), new MemoryTokenStore(), () => Config);

        await Assert.ThrowsAsync<NotSignedInException>(() => auth.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Without_a_client_id_you_are_told_to_fill_it_in()
    {
        var auth = new GoogleAuth(new HttpClient(new FakeHandler(_ => Responses.Raw("{}"))), new MemoryTokenStore("ref"), () => null);

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => auth.GetAccessTokenAsync());

        Assert.Contains("client-ID", ex.Message);
    }

    [Fact]
    public async Task Sign_out_revokes_at_google_and_deletes_the_token()
    {
        var store = new MemoryTokenStore("ref-9");
        var handler = new FakeHandler(_ => Responses.Raw("{}"));
        var auth = new GoogleAuth(new HttpClient(handler), store, () => Config);

        await auth.SignOutAsync();

        Assert.Null(store.Load());
        var revoke = Assert.Single(handler.Requests);
        Assert.Equal("https://oauth2.googleapis.com/revoke", revoke.Uri.ToString());
        Assert.Equal("ref-9", Form(revoke)["token"]);
    }

    [Fact]
    public async Task Sign_out_works_offline_too()
    {
        var store = new MemoryTokenStore("ref");
        var auth = new GoogleAuth(new HttpClient(new FakeHandler(_ => throw new HttpRequestException())), store, () => Config);

        await auth.SignOutAsync();

        Assert.Null(store.Load());
    }

    private static Dictionary<string, string> LoopbackReceiver_Parse(Uri url) =>
        url.Query.TrimStart('?').Split('&').Select(p => p.Split('='))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
}

