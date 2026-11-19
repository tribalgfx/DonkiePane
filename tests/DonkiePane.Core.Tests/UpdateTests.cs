using System.Net;
using System.Security.Cryptography;
using System.Text;
using DonkiePane.Core.Updates;

namespace DonkiePane.Core.Tests;

/// <summary>Een nagebootste GitHub: geeft per verzoek een antwoord en onthoudt de adressen.</summary>
internal sealed class FakeGitHub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("0.1.0", "0.1.0")]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("V1.2", "1.2.0")]
    [InlineData("0.1.0+8072fca", "0.1.0")]
    [InlineData(" 1.0.0 ", "1.0.0")]
    public void Versions_are_read_with_or_without_v_and_build_metadata(string text, string expected) =>
        Assert.Equal(Version.Parse(expected).ToString(3), AppVersion.Format(AppVersion.TryParse(text)!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nieuw")]
    [InlineData("0.2.0-beta")] // voorlopige versies worden nooit aangeboden
    public void Unreadable_or_preliminary_versions_give_null(string? text) =>
        Assert.Null(AppVersion.TryParse(text));

    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.10.0", "0.9.0", true)] // geen tekstvergelijking
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1", "0.1.0", false)]   // 0.1 is hetzelfde als 0.1.0
    [InlineData("0.1.0", "0.2.0", false)]
    public void Only_a_higher_version_is_newer(string candidate, string current, bool newer) =>
        Assert.Equal(newer, AppVersion.IsNewer(AppVersion.TryParse(candidate)!, AppVersion.TryParse(current)!));
}

public sealed class ReleaseFeedTests
{
    private static readonly Uri Fallback = new("https://github.com/tribalgfx/DonkiePane/releases");
    internal const string Digest = "f48aecb69b529dbffce3ac3e610f7b704098dbe53697113beca9c00403043f35";

    internal static string ReleaseJson(string tag = "v0.2.0", bool prerelease = false, string? digest = "sha256:" + Digest,
        string assetName = "DonkiePane-Setup-0.2.0.exe", string downloadHost = "github.com") => $$"""
        {
          "tag_name": "{{tag}}",
          "html_url": "https://github.com/tribalgfx/DonkiePane/releases/tag/{{tag}}",
          "draft": false,
          "prerelease": {{(prerelease ? "true" : "false")}},
          "body": "**Nieuw:** controle op updates.",
          "assets": [
            { "name": "checksums.txt", "browser_download_url": "https://github.com/x/checksums.txt", "size": 10 },
            {
              "name": "{{assetName}}",
              "browser_download_url": "https://{{downloadHost}}/tribalgfx/DonkiePane/releases/download/{{tag}}/{{assetName}}",
              "size": 51877988,
              "digest": {{(digest is null ? "null" : $"\"{digest}\"")}}
            }
          ]
        }
        """;

    [Fact]
    public void A_release_gives_version_page_notes_and_the_installer_with_its_checksum()
    {
        var release = ReleaseFeed.Parse(ReleaseJson(), Fallback)!;

        Assert.Equal(new Version(0, 2, 0, 0), release.Version);
        Assert.Equal("https://github.com/tribalgfx/DonkiePane/releases/tag/v0.2.0", release.Page.AbsoluteUri);
        Assert.Contains("controle op updates", release.Notes);
        Assert.Equal("DonkiePane-Setup-0.2.0.exe", release.Installer!.Name);
        Assert.Equal(51877988, release.Installer.Size);
        Assert.Equal(Digest, release.Installer.Sha256);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"tekst\"")]
    public void An_answer_that_is_no_release_is_a_failed_check_not_a_crash(string json) =>
        Assert.Throws<System.Text.Json.JsonException>(() => ReleaseFeed.Parse(json, Fallback));

    [Fact]
    public void Odd_values_in_the_list_of_files_are_skipped()
    {
        var json = """
            { "tag_name": "v0.3.0", "assets": [ 7, "x", null,
              { "name": "DonkiePane-Setup-0.3.0.exe", "size": "groot",
                "browser_download_url": "https://github.com/tribalgfx/DonkiePane/releases/download/v0.3.0/DonkiePane-Setup-0.3.0.exe" } ] }
            """;

        var release = ReleaseFeed.Parse(json, Fallback)!;

        Assert.Equal("DonkiePane-Setup-0.3.0.exe", release.Installer!.Name);
        Assert.Equal(0, release.Installer.Size);
        Assert.Equal(Fallback, release.Page);
    }

    [Theory]
    [InlineData("DonkiePane-Setup-0.1.0.exe", "github.com")] // een oude installer bij een nieuwe tag
    public void Only_the_installer_of_exactly_this_version_counts(string assetName, string host) =>
        Assert.Null(ReleaseFeed.Parse(ReleaseJson(assetName: assetName, downloadHost: host), Fallback)!.Installer);

    [Fact]
    public void An_installer_from_another_repository_does_not_count()
    {
        var json = ReleaseJson().Replace("/tribalgfx/DonkiePane/releases/download/", "/iemand/anders/releases/download/");

        Assert.Null(ReleaseFeed.Parse(json, Fallback)!.Installer);
    }

    [Fact]
    public void A_release_page_outside_github_is_replaced_by_the_releases_page()
    {
        var json = ReleaseJson().Replace("https://github.com/tribalgfx/DonkiePane/releases/tag/v0.2.0", "https://example.com/phish");

        Assert.Equal(Fallback, ReleaseFeed.Parse(json, Fallback)!.Page);
    }

    [Fact]
    public void A_preliminary_release_is_ignored() =>
        Assert.Null(ReleaseFeed.Parse(ReleaseJson(prerelease: true), Fallback));

    [Theory]
    [InlineData(null)]
    [InlineData("md5:abc")]
    [InlineData("sha256:te-kort")]
    public void Without_a_usable_checksum_the_installer_has_none(string? digest) =>
        Assert.Null(ReleaseFeed.Parse(ReleaseJson(digest: digest), Fallback)!.Installer!.Sha256);

    [Theory]
    [InlineData("iets-anders.exe")]
    [InlineData("DonkiePane-Setup-0.2.0.zip")]
    [InlineData("DonkiePane-Setup-..\\\\..\\\\x.exe")]
    public void Only_a_plain_DonkiePane_setup_exe_counts_as_the_installer(string name) =>
        Assert.Null(ReleaseFeed.Parse(ReleaseJson(assetName: name), Fallback)!.Installer);

    [Fact]
    public async Task GitHub_is_asked_for_the_latest_release_with_a_user_agent()
    {
        var github = new FakeGitHub(_ => FakeGitHub.Json(ReleaseJson()));
        var feed = new ReleaseFeed(new HttpClient(github));

        var release = await feed.GetLatestAsync();

        Assert.Equal(new Version(0, 2, 0, 0), release!.Version);
        var request = Assert.Single(github.Requests);
        Assert.Equal("https://api.github.com/repos/tribalgfx/DonkiePane/releases/latest", request.RequestUri!.AbsoluteUri);
        Assert.NotEmpty(request.Headers.UserAgent);
        Assert.Null(request.Headers.Authorization); // geen token: alleen openbare gegevens
    }

    [Fact]
    public async Task No_release_or_a_private_repository_gives_null()
    {
        var feed = new ReleaseFeed(new HttpClient(new FakeGitHub(_ => FakeGitHub.Json("""{"message":"Not Found"}""", HttpStatusCode.NotFound))));

        Assert.Null(await feed.GetLatestAsync());
    }

    [Fact]
    public async Task A_server_error_is_reported_as_an_http_error()
    {
        var feed = new ReleaseFeed(new HttpClient(new FakeGitHub(_ => FakeGitHub.Json("{}", HttpStatusCode.InternalServerError))));

        await Assert.ThrowsAsync<HttpRequestException>(() => feed.GetLatestAsync());
    }
}

public sealed class InstallerDownloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] Installer = Encoding.UTF8.GetBytes("pretend this is an installer");
    private static readonly string InstallerSha = Convert.ToHexStringLower(SHA256.HashData(Installer));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static ReleaseAsset Asset(string? sha = null, string host = "github.com", string name = "DonkiePane-Setup-0.2.0.exe") =>
        new(name, new Uri($"https://{host}/tribalgfx/DonkiePane/releases/download/v0.2.0/{name}"), Installer.Length, sha ?? InstallerSha);

    private static HttpClient Serving(byte[] bytes) =>
        new(new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));

    [Fact]
    public async Task A_correct_download_ends_up_under_its_own_name_with_progress_up_to_one()
    {
        var reported = new List<double>();
        var progress = new SyncProgress(reported.Add);

        var path = await InstallerDownload.DownloadAsync(Serving(Installer), Asset(), _dir, progress);

        Assert.Equal("DonkiePane-Setup-0.2.0.exe", Path.GetFileName(path));
        Assert.Equal(_dir, Path.GetDirectoryName(Path.GetDirectoryName(path))); // in een eigen map per download
        Assert.Equal(Installer, File.ReadAllBytes(path));
        Assert.Equal(1.0, reported.Last());
        Assert.Empty(Directory.GetFiles(_dir, "*.part", SearchOption.AllDirectories));
        Assert.True(InstallerDownload.IsUnchanged(path, InstallerSha));
    }

    [Fact]
    public async Task Two_downloads_get_different_places()
    {
        var first = await InstallerDownload.DownloadAsync(Serving(Installer), Asset(), _dir);
        var firstFolder = Path.GetDirectoryName(first);

        var second = await InstallerDownload.DownloadAsync(Serving(Installer), Asset(), _dir);

        Assert.NotEqual(firstFolder, Path.GetDirectoryName(second));
    }

    [Fact]
    public async Task A_download_that_does_not_match_the_checksum_is_thrown_away()
    {
        var tampered = Encoding.UTF8.GetBytes("something else entirely");

        await Assert.ThrowsAsync<UpdateException>(() => InstallerDownload.DownloadAsync(Serving(tampered), Asset(), _dir));

        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task A_download_of_another_size_than_announced_is_refused()
    {
        var bigger = Installer.Concat(new byte[1000]).ToArray(); // Content-Length past niet bij de grootte van GitHub

        await Assert.ThrowsAsync<UpdateException>(() => InstallerDownload.DownloadAsync(Serving(bigger), Asset(), _dir));

        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task A_stream_that_keeps_going_is_cut_off_at_the_announced_size()
    {
        var endless = new FakeGitHub(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new EndlessStream()), // zonder Content-Length
        });

        await Assert.ThrowsAsync<UpdateException>(() => InstallerDownload.DownloadAsync(new HttpClient(endless), Asset(), _dir));

        Assert.Empty(Directory.GetFileSystemEntries(_dir));
    }

    [Fact]
    public async Task A_changed_installer_no_longer_counts_as_checked()
    {
        var path = await InstallerDownload.DownloadAsync(Serving(Installer), Asset(), _dir);

        File.AppendAllText(path, "erbij");

        Assert.False(InstallerDownload.IsUnchanged(path, InstallerSha));
    }

    private sealed class EndlessStream : Stream
    {
        public override int Read(byte[] buffer, int offset, int count) => count;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Without_a_checksum_nothing_is_downloaded()
    {
        var github = new FakeGitHub(_ => throw new InvalidOperationException("mag niet downloaden"));
        var asset = Asset() with { Sha256 = null };

        await Assert.ThrowsAsync<UpdateException>(() => InstallerDownload.DownloadAsync(new HttpClient(github), asset, _dir));
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task An_installer_that_is_not_on_github_is_not_downloaded()
    {
        var github = new FakeGitHub(_ => throw new InvalidOperationException("mag niet downloaden"));

        await Assert.ThrowsAsync<UpdateException>(() =>
            InstallerDownload.DownloadAsync(new HttpClient(github), Asset(host: "example.com"), _dir));
        Assert.Empty(github.Requests);
    }

    [Fact]
    public async Task Installers_from_earlier_updates_are_cleaned_up()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "eerder"));
        File.WriteAllText(Path.Combine(_dir, "DonkiePane-Setup-0.1.5.exe"), "oud");
        File.WriteAllText(Path.Combine(_dir, "eerder", "DonkiePane-Setup-0.2.1.exe"), "oud");

        await InstallerDownload.DownloadAsync(Serving(Installer), Asset(), _dir);

        Assert.Equal(["DonkiePane-Setup-0.2.0.exe"], Directory.GetFiles(_dir, "*", SearchOption.AllDirectories).Select(Path.GetFileName));
    }

    /// <summary><see cref="Progress{T}"/> meldt via de threadpool; deze direct, zodat de test kan tellen.</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
