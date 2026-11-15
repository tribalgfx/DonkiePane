using DonkiePane.Tasks;
using System.Text.Json;

namespace DonkiePane.Plugins.Google;

/// <summary>
/// Inloggen bij Google en toegangstokens ophalen. Eén keer in de browser inloggen;
/// daarna houdt DonkiePane zichzelf ingelogd met het bewaarde refresh token.
/// </summary>
public sealed class GoogleAuth(HttpClient http, ITokenStore store, Func<GoogleClientConfig?> config, TimeProvider? time = null)
{
    public const string Scope = "https://www.googleapis.com/auth/tasks";
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpires;

    public bool IsSignedIn => store.Load() is not null;

    /// <summary>Opent via <paramref name="openBrowser"/> de Google-inlogpagina en wacht op de terugkeer.</summary>
    public async Task SignInAsync(Func<Uri, Task> openBrowser, CancellationToken ct = default)
    {
        var cfg = RequireConfig();
        var (verifier, challenge) = Pkce.Create();
        var state = Pkce.RandomState();

        using var receiver = new LoopbackReceiver();
        var url = BuildAuthorizationUrl(cfg, receiver.RedirectUri, challenge, state);

        await openBrowser(url);
        var code = await receiver.WaitForCodeAsync(state, TimeSpan.FromMinutes(3), ct);

        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = cfg.ClientId,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = receiver.RedirectUri,
        };
        if (cfg.ClientSecret is not null) form["client_secret"] = cfg.ClientSecret;

        var token = await PostTokenAsync(form, ct);
        if (token.RefreshToken is null)
            throw new TaskProviderException("Google gaf geen blijvend inlogtoken terug. Probeer het opnieuw te koppelen.");

        store.Save(token.RefreshToken);
        CacheAccessToken(token);
    }

    public static Uri BuildAuthorizationUrl(GoogleClientConfig cfg, string redirectUri, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent", // anders geeft Google bij opnieuw koppelen soms geen blijvend token
        };
        return new Uri(AuthEndpoint + "?" + string.Join("&", query.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}")));
    }

    /// <summary>
    /// Een geldig toegangstoken. Gebruikt het bewaarde token zolang het geldig is;
    /// <paramref name="forceRefresh"/> haalt altijd een nieuw op (bij een afgewezen verzoek).
    /// </summary>
    public async Task<string> GetAccessTokenAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && TokenStillValid()) return _accessToken!;

        await _refreshLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && TokenStillValid()) return _accessToken!; // een ander was ons voor

            var refreshToken = store.Load() ?? throw new NotSignedInException();
            var cfg = RequireConfig();
            var form = new Dictionary<string, string>
            {
                ["client_id"] = cfg.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            };
            if (cfg.ClientSecret is not null) form["client_secret"] = cfg.ClientSecret;

            try
            {
                var token = await PostTokenAsync(form, ct);
                CacheAccessToken(token);
                return token.AccessToken;
            }
            catch (InvalidGrantException)
            {
                // Het token is ingetrokken of verlopen: opnieuw inloggen is nodig.
                // Alleen hier wissen; bij "geen verbinding" blijft het token bewaard.
                store.Delete();
                _accessToken = null;
                throw new NotSignedInException();
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>Trekt de toestemming in bij Google (als dat lukt) en wist het bewaarde token.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var refreshToken = store.Load();
        store.Delete();
        _accessToken = null;
        if (refreshToken is null) return;

        try
        {
            using var response = await http.PostAsync(RevokeEndpoint,
                new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = refreshToken }), ct);
            // Mislukt het intrekken (geen verbinding), dan is de lokale koppeling toch weg.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
        }
    }

    private bool TokenStillValid() =>
        _accessToken is not null && _time.GetUtcNow() < _accessTokenExpires - TimeSpan.FromSeconds(60);

    private void CacheAccessToken(TokenResponse token)
    {
        _accessToken = token.AccessToken;
        _accessTokenExpires = _time.GetUtcNow().AddSeconds(token.ExpiresIn);
    }

    private GoogleClientConfig RequireConfig()
    {
        var cfg = config();
        if (cfg is not { IsValid: true })
            throw new TaskProviderException("Vul eerst het Google client-ID in via Instellingen.");
        return cfg;
    }

    private async Task<TokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(form), ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : null;
                if (error == "invalid_grant") throw new InvalidGrantException();
                var description = root.TryGetProperty("error_description", out var d) ? d.GetString() : error;
                throw new TaskProviderException($"Inloggen bij Google is mislukt: {description ?? response.StatusCode.ToString()}");
            }

            return new TokenResponse(
                root.GetProperty("access_token").GetString()!,
                root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600,
                root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new TaskProviderException("Geen verbinding met Google.", ex);
        }
    }

    private sealed record TokenResponse(string AccessToken, int ExpiresIn, string? RefreshToken);

    private sealed class InvalidGrantException : Exception;
}


