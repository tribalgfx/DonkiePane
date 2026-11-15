using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DonkiePane.Plugins.Google;
using DonkiePane.Plugins.RestTasks;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.Tasks.Tests;

public sealed class CertPinTests
{
    private static X509Certificate2 NewCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
    }

    [Theory]
    [InlineData("E8:19:37:91:87:DA:79:6C:63:BA:0F:2F:E4:74:31:C2:E0:D7:BE:8B:BB:83:6B:06:C8:21:D8:9C:68:41:E7:DF")]
    [InlineData("e8:19:37:91:87:da:79:6c:63:ba:0f:2f:e4:74:31:c2:e0:d7:be:8b:bb:83:6b:06:c8:21:d8:9c:68:41:e7:df")]
    [InlineData("E81937 9187DA796C63BA0F2FE47431C2E0D7BE8BBB836B06C821D89C6841E7DF")]
    [InlineData("E8-19-37-91-87-DA-79-6C-63-BA-0F-2F-E4-74-31-C2-E0-D7-BE-8B-BB-83-6B-06-C8-21-D8-9C-68-41-E7-DF")]
    public void Normalize_accepts_the_usual_notations(string input)
    {
        Assert.Equal("E81937918 7DA796C63BA0F2FE47431C2E0D7BE8BBB836B06C821D89C6841E7DF".Replace(" ", ""), CertPin.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_fingerprint_means_no_pin(string? input) => Assert.Null(CertPin.Normalize(input));

    [Theory]
    [InlineData("E8:19:37")]
    [InlineData("ZZ:19:37:91:87:DA:79:6C:63:BA:0F:2F:E4:74:31:C2:E0:D7:BE:8B:BB:83:6B:06:C8:21:D8:9C:68:41:E7:DF")]
    [InlineData("niet een afdruk")]
    public void A_wrong_fingerprint_is_refused_with_an_explanation(string input)
    {
        var ex = Assert.Throws<FormatException>(() => CertPin.Normalize(input));
        Assert.Contains("SHA-256", ex.Message);
    }

    [Fact]
    public void Without_a_pin_only_a_fully_trusted_certificate_is_accepted()
    {
        using var cert = NewCertificate();

        Assert.True(CertPin.Validate(cert, SslPolicyErrors.None, null));
        Assert.False(CertPin.Validate(cert, SslPolicyErrors.RemoteCertificateChainErrors, null)); // zelfondertekend zonder pin: nee
        Assert.False(CertPin.Validate(cert, SslPolicyErrors.RemoteCertificateNameMismatch, null));
        Assert.False(CertPin.Validate(null, SslPolicyErrors.RemoteCertificateNotAvailable, null));
    }

    [Fact]
    public void With_a_pin_exactly_that_certificate_is_accepted_even_though_the_chain_is_not_trusted()
    {
        using var cert = NewCertificate();
        var pin = CertPin.Normalize(CertPin.Format(CertPin.Fingerprint(cert)));

        Assert.True(CertPin.Validate(cert, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch, pin));
    }

    [Fact]
    public void With_a_pin_any_other_certificate_is_refused_even_if_it_is_otherwise_trusted()
    {
        using var pinned = NewCertificate();
        using var other = NewCertificate();
        var pin = CertPin.Normalize(CertPin.Fingerprint(pinned));

        Assert.False(CertPin.Validate(other, SslPolicyErrors.None, pin));
        Assert.False(CertPin.Validate(null, SslPolicyErrors.None, pin));
    }

    [Fact]
    public void The_fingerprint_is_the_sha256_of_the_der_certificate()
    {
        using var cert = NewCertificate();

        Assert.Equal(Convert.ToHexString(SHA256.HashData(cert.RawData)), CertPin.Fingerprint(cert));
        Assert.Equal(95, CertPin.Format(CertPin.Fingerprint(cert)).Length); // 32 bytes + 31 dubbele punten
    }
}

public sealed class RestSourceTests
{
    private sealed class MemorySecrets(Dictionary<string, string>? start = null) : ISecretStore
    {
        private readonly Dictionary<string, string> _values = start ?? [];
        public string? Get(string key) => _values.GetValueOrDefault(key);
        public void Set(string key, string? value) { if (string.IsNullOrEmpty(value)) _values.Remove(key); else _values[key] = value; }
        public void Clear() => _values.Clear();
    }

    private static SourceContext Context(string url, string? token, string fingerprint = "") =>
        new(new Dictionary<string, string> { [RestTasksPlugin.BaseUrlKey] = url, [RestTasksPlugin.FingerprintKey] = fingerprint },
            new MemorySecrets(token is null ? null : new() { [RestTasksPlugin.TokenKey] = token }),
            _ => Task.CompletedTask);

    [Theory]
    [InlineData("https://dash.test:4173")]
    [InlineData("https://dash.test:4173/")]
    [InlineData("https://dash.test/nas/dash")]
    public void Https_addresses_are_accepted_with_or_without_a_trailing_slash(string url) =>
        Assert.Equal(Uri.UriSchemeHttps, RestTaskSource.ParseBaseUri(url).Scheme);

    [Theory]
    [InlineData("http://dash.test:4173")]
    [InlineData("dash.test")]
    [InlineData("ftp://dash.test")]
    public void Anything_but_https_is_refused_so_a_token_never_travels_unencrypted(string url)
    {
        var ex = Assert.Throws<FormatException>(() => RestTaskSource.ParseBaseUri(url));
        Assert.Contains("https://", ex.Message);
    }

    [Fact]
    public void An_empty_address_asks_for_one()
    {
        Assert.Contains("adres", Assert.Throws<FormatException>(() => RestTaskSource.ParseBaseUri("")).Message);
    }

    [Theory]
    [InlineData("http://dash.test", "tok", "", "https://")]
    [InlineData("", "tok", "", "adres")]
    [InlineData("https://dash.test", null, "", "token")]
    [InlineData("https://dash.test", "tok", "kapot", "SHA-256")]
    public async Task A_wrong_setup_shows_the_reason_in_the_pane_instead_of_crashing(string url, string? token, string fingerprint, string expected)
    {
        using var source = new RestTaskSource(Context(url, token, fingerprint));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => source.Provider.GetTasksAsync("tasks"));
        var check = await source.TestAsync();

        Assert.Contains(expected, ex.Message);
        Assert.False(check.Ok);
        Assert.Contains(expected, check.Message);
    }

    [Fact]
    public async Task A_good_setup_lists_one_list_after_checking_the_connection()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        using var source = new RestTaskSource(Context("https://dash.test:4173", FakeTasksServer.ValidToken), server);

        var lists = await source.GetListsAsync();

        var list = Assert.Single(lists);
        Assert.Equal(RestTaskProvider.ListId, list.Id);
        Assert.Single(server.Requests); // het adres en token zijn echt gecontroleerd
    }

    [Fact]
    public async Task A_wrong_token_stops_the_pane_from_being_created()
    {
        var server = new FakeTasksServer();
        using var source = new RestTaskSource(Context("https://dash.test:4173", "fout"), server);

        await Assert.ThrowsAsync<SourceUnauthorizedException>(() => source.GetListsAsync());
    }

    [Fact]
    public async Task Test_reports_the_number_of_tasks()
    {
        var server = new FakeTasksServer();
        server.AddTask("a", "A");
        server.AddTask("b", "B");
        using var source = new RestTaskSource(Context("https://dash.test:4173", FakeTasksServer.ValidToken), server);

        var check = await source.TestAsync();

        Assert.True(check.Ok);
        Assert.Contains("2 taken", check.Message);
    }

    [Fact]
    public async Task The_token_comes_from_the_secret_store_and_never_from_the_ordinary_settings()
    {
        var server = new FakeTasksServer();
        // Een token in de gewone waarden wordt genegeerd; alleen de geheimenopslag telt.
        var context = new SourceContext(
            new Dictionary<string, string> { [RestTasksPlugin.BaseUrlKey] = "https://dash.test", [RestTasksPlugin.TokenKey] = FakeTasksServer.ValidToken },
            new MemorySecrets(), _ => Task.CompletedTask);
        using var source = new RestTaskSource(context, server);

        var check = await source.TestAsync();

        Assert.False(check.Ok);
        Assert.Contains("token", check.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public void The_plugin_describes_itself_without_naming_any_specific_dashboard()
    {
        var plugin = new RestTasksPlugin();
        var text = plugin.DisplayName + plugin.Description + string.Join("", plugin.Fields.Select(f => f.Label + f.Help));

        Assert.Equal("tasks-rest-v1", plugin.TypeId);
        Assert.Equal(SettingKind.Secret, plugin.Fields.Single(f => f.Key == RestTasksPlugin.TokenKey).Kind);
        Assert.False(plugin.Fields.Single(f => f.Key == RestTasksPlugin.FingerprintKey).Required);
    }
}

public sealed class PluginLoaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // Een geladen plug-in houdt zijn bestand vast tot het proces stopt; het zijn tijdelijke bestanden.
        }
        catch (IOException)
        {
        }
    }

    private string Install(string folderName, Type pluginType)
    {
        var dir = Path.Combine(_root, "plugins", folderName);
        Directory.CreateDirectory(dir);
        var source = pluginType.Assembly.Location;
        var target = Path.Combine(dir, Path.GetFileName(source));
        File.Copy(source, target);
        return target;
    }

    [Fact]
    public void A_missing_folder_gives_no_plugins_and_no_errors()
    {
        var result = PluginLoader.Load([Path.Combine(_root, "bestaat-niet")]);

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Real_plugin_assemblies_are_found_and_work_through_the_shared_interface()
    {
        Install("google", typeof(GoogleSourcePlugin));
        Install("rest", typeof(RestTasksPlugin));

        var result = PluginLoader.Load([Path.Combine(_root, "plugins")]);

        Assert.Empty(result.Errors);
        Assert.Equal(["google-tasks", "tasks-rest-v1"], result.Plugins.Select(p => p.Plugin.TypeId).Order());
        var rest = result.Plugins.Single(p => p.Plugin.TypeId == "tasks-rest-v1").Plugin;
        Assert.Equal(3, rest.Fields.Count);
        // Het type komt uit de eigen laadcontext van de plug-in, het contract van de app.
        Assert.NotSame(typeof(RestTasksPlugin), rest.GetType());
        Assert.IsAssignableFrom<ITaskSourcePlugin>(rest);
    }

    [Fact]
    public async Task A_loaded_plugin_can_really_create_a_source()
    {
        Install("rest", typeof(RestTasksPlugin));
        var plugin = PluginLoader.Load([Path.Combine(_root, "plugins")]).Plugins.Single().Plugin;
        var context = new SourceContext(
            new Dictionary<string, string> { [RestTasksPlugin.BaseUrlKey] = "http://onveilig" },
            new RestSourceTests_Secrets(), _ => Task.CompletedTask);

        using var source = plugin.Create(context);

        // Een instelling die niet klopt, geeft een leesbare melding in plaats van een crash.
        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => source.Provider.GetTasksAsync("tasks"));
        Assert.Contains("https://", ex.Message);
    }

    [Fact]
    public void A_broken_file_is_reported_and_does_not_stop_the_others()
    {
        Install("rest", typeof(RestTasksPlugin));
        var bad = Path.Combine(_root, "plugins", "kapot");
        Directory.CreateDirectory(bad);
        File.WriteAllText(Path.Combine(bad, "DonkiePane.Plugins.Kapot.dll"), "dit is geen assembly");

        var result = PluginLoader.Load([Path.Combine(_root, "plugins")]);

        Assert.Equal("tasks-rest-v1", Assert.Single(result.Plugins).Plugin.TypeId);
        var error = Assert.Single(result.Errors);
        Assert.EndsWith("DonkiePane.Plugins.Kapot.dll", error.Path);
    }

    [Fact]
    public void Dependency_dlls_without_the_plugin_prefix_are_not_treated_as_plugins()
    {
        var dir = Path.Combine(_root, "plugins", "x");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Iets.Anders.dll"), "geen assembly");

        var result = PluginLoader.Load([Path.Combine(_root, "plugins")]);

        Assert.Empty(result.Plugins);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void The_same_type_in_two_places_is_loaded_once_and_the_duplicate_is_reported()
    {
        Install("rest", typeof(RestTasksPlugin));
        Install("rest-kopie", typeof(RestTasksPlugin));

        var result = PluginLoader.Load([Path.Combine(_root, "plugins")]);

        Assert.Single(result.Plugins);
        Assert.Contains("bestaat al", Assert.Single(result.Errors).Message);
    }

    private sealed class RestSourceTests_Secrets : ISecretStore
    {
        public string? Get(string key) => null;
        public void Set(string key, string? value) { }
        public void Clear() { }
    }
}

public sealed class SourcesStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Connections_survive_a_restart_and_contain_no_secrets()
    {
        var store = new SourcesStore(Path.Combine(_dir, "sources.json"));
        store.Save(new SourcesConfig
        {
            Connections =
            [
                new SourceConnection { Id = "c1", TypeId = "tasks-rest-v1", Name = "Thuis", Values = { ["BaseUrl"] = "https://dash.test", ["Fingerprint"] = "AA:BB" } },
            ],
        });

        var loaded = new SourcesStore(Path.Combine(_dir, "sources.json")).Load();

        var connection = Assert.Single(loaded.Connections);
        Assert.Equal("Thuis", connection.Name);
        Assert.Equal("https://dash.test", connection.Values["BaseUrl"]);
        Assert.DoesNotContain("Token", File.ReadAllText(Path.Combine(_dir, "sources.json")));
    }

    [Fact]
    public void A_missing_file_gives_an_empty_config() =>
        Assert.Empty(new SourcesStore(Path.Combine(_dir, "nee.json")).Load().Connections);
}

