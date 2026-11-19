using System.IO;
using System.Text;
using DonkiePane.App;
using DonkiePane.Core;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Offline;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App.Tests;

/// <summary>Een tijdelijke map voor de gegevens van één test, met echte versleuteling (DPAPI) en echte bestanden.</summary>
public abstract class TempData : IDisposable
{
    protected string Dir { get; } = Path.Combine(Path.GetTempPath(), "DonkiePaneTests", Guid.NewGuid().ToString("N"));

    protected TempData() => Directory.CreateDirectory(Dir);

    public void Dispose()
    {
        try { Directory.Delete(Dir, recursive: true); }
        catch (IOException) { }
    }

    protected string SecretsDir => Path.Combine(Dir, "secrets");
    protected string QueueDir => Path.Combine(Dir, "queue");
    protected string SourcesPath => Path.Combine(Dir, "sources.json");
}

/// <summary>Een proefplug-in: onthoudt wat de app hem meegaf en kan op bevel falen.</summary>
internal sealed class FakePlugin(string typeId = "fake", Exception? failOnCreate = null) : ITaskSourcePlugin
{
    public List<SourceContext> Created { get; } = [];
    public string TypeId { get; } = typeId;
    public string DisplayName => "Proefbron";
    public string Description => "voor tests";
    public IReadOnlyList<SettingField> Fields { get; } = [new("Url", "Adres", SettingKind.Url), new("Token", "Token", SettingKind.Secret)];
    public IReadOnlyList<PluginAction> Actions { get; } = [];

    public ITaskSource Create(SourceContext context)
    {
        if (failOnCreate is not null) throw failOnCreate;
        Created.Add(context);
        return new FakeSource();
    }
}

/// <summary>Een bron die van buiten kan veranderen en dus een controle-interval en een verversknop heeft.</summary>
internal sealed class FakeRefreshable : IRefreshableTaskProvider
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
    public int Refreshed { get; private set; }
    public Task RefreshAsync(CancellationToken ct = default) { Refreshed++; return Task.CompletedTask; }
    public TaskProviderCapabilities Capabilities { get; } = new(false, false, false);
    public event EventHandler<string>? ListChanged { add { } remove { } }
    public event EventHandler<TaskProviderStatus>? StatusChanged { add { } remove { } }
    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TaskItem>>([]);
    public Task AddAsync(string listId, TaskItem task, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default) => Task.CompletedTask;
    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string listId, string taskId, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteListAsync(string listId, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeSource : ITaskSource
{
    public bool Disposed { get; private set; }
    public ITaskProvider Provider { get; } = new FakeRefreshable();
    public Task<IReadOnlyList<TaskListInfo>> GetListsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TaskListInfo>>([]);
    public Task<SourceCheck> TestAsync(CancellationToken ct = default) => Task.FromResult(new SourceCheck(true, "ok"));
    public Task<string> RunActionAsync(string actionId, CancellationToken ct = default) => Task.FromResult("ok");
    public void Dispose() => Disposed = true;
}

internal sealed class FakeLocal : ITaskProvider
{
    public TaskProviderCapabilities Capabilities { get; } = new(true, true);
    public event EventHandler<string>? ListChanged { add { } remove { } }
    public event EventHandler<TaskProviderStatus>? StatusChanged { add { } remove { } }
    public Task<IReadOnlyList<TaskItem>> GetTasksAsync(string listId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TaskItem>>([]);
    public Task AddAsync(string listId, TaskItem task, CancellationToken ct = default) => Task.CompletedTask;
    public Task UpdateAsync(IEnumerable<TaskItem> tasks, CancellationToken ct = default) => Task.CompletedTask;
    public Task MoveAsync(string listId, string taskId, int newIndex, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteAsync(string listId, string taskId, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteListAsync(string listId, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class DpapiSecretStoreTests : TempData
{
    [Fact]
    public void A_secret_is_stored_and_read_back_also_by_a_new_instance()
    {
        new DpapiSecretStore("c1", SecretsDir).Set("Token", "dd_geheim-123");

        Assert.Equal("dd_geheim-123", new DpapiSecretStore("c1", SecretsDir).Get("Token"));
    }

    [Fact]
    public void A_file_that_stays_locked_is_never_overwritten_with_only_the_new_secret()
    {
        var store = new DpapiSecretStore("c1", SecretsDir);
        store.Set("ClientSecret", "s3cr3t");

        using (new FileStream(Path.Combine(SecretsDir, "c1.dat"), FileMode.Open, FileAccess.Read, FileShare.None)) // bijv. een virusscanner
        {
            Assert.ThrowsAny<IOException>(() => store.Set("RefreshToken", "nieuw"));
            Assert.Null(store.Get("ClientSecret")); // even niet te lezen, maar geen crash
        }

        Assert.Equal("s3cr3t", store.Get("ClientSecret")); // niets kwijt
    }

    [Fact]
    public void Two_instances_for_the_same_connection_writing_at_once_lose_nothing()
    {
        // De host maakt per aanroep een nieuwe instantie; de poll-thread en het instellingenvenster schrijven soms tegelijk.
        Parallel.For(0, 20, i => new DpapiSecretStore("c1", SecretsDir).Set("Key" + i, "waarde" + i));

        var store = new DpapiSecretStore("c1", SecretsDir);
        Assert.All(Enumerable.Range(0, 20), i => Assert.Equal("waarde" + i, store.Get("Key" + i)));
        Assert.Single(Directory.GetFiles(SecretsDir)); // geen achtergebleven tijdelijke bestanden
    }

    [Theory]
    [InlineData("c1", true)]
    [InlineData("a-b_C9", true)]
    [InlineData("..\\x", false)]
    [InlineData("../x", false)]
    [InlineData("c1\n", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_safe_connection_ids_become_file_names(string? id, bool safe) =>
        Assert.Equal(safe, TaskSourceHost.IsSafeId(id));

    [Fact]
    public void An_id_with_a_trailing_line_break_is_refused() =>
        Assert.Throws<ArgumentException>(() => new DpapiSecretStore("c1\n", SecretsDir));

    [Fact]
    public void The_file_on_disk_does_not_contain_the_secret_in_readable_form()
    {
        new DpapiSecretStore("c1", SecretsDir).Set("Token", "dd_geheim-123");

        var bytes = File.ReadAllBytes(Path.Combine(SecretsDir, "c1.dat"));

        Assert.DoesNotContain("dd_geheim", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain("dd_geheim", Encoding.Unicode.GetString(bytes));
    }

    [Fact]
    public void Connections_do_not_see_each_others_secrets_and_empty_removes_a_secret()
    {
        var one = new DpapiSecretStore("c1", SecretsDir);
        var two = new DpapiSecretStore("c2", SecretsDir);
        one.Set("Token", "een");
        two.Set("Token", "twee");
        one.Set("Extra", "x");

        one.Set("Token", "");

        Assert.Null(one.Get("Token"));
        Assert.Equal("x", one.Get("Extra"));
        Assert.Equal("twee", two.Get("Token"));
    }

    [Fact]
    public void Clearing_removes_the_file_and_the_last_removed_secret_leaves_no_empty_file()
    {
        var store = new DpapiSecretStore("c1", SecretsDir);
        store.Set("Token", "a");
        store.Set("Token", null);
        Assert.False(File.Exists(Path.Combine(SecretsDir, "c1.dat")));

        store.Set("Token", "b");
        store.Clear();
        Assert.Null(store.Get("Token"));
        Assert.False(File.Exists(Path.Combine(SecretsDir, "c1.dat")));
    }

    [Theory]
    [InlineData("..\\evil")]
    [InlineData("../evil")]
    [InlineData("a b")]
    [InlineData("")]
    public void An_unsafe_id_can_never_become_a_path(string id) =>
        Assert.Throws<ArgumentException>(() => new DpapiSecretStore(id, SecretsDir));

    [Fact]
    public void A_damaged_file_gives_no_secrets_instead_of_an_error()
    {
        Directory.CreateDirectory(SecretsDir);
        File.WriteAllText(Path.Combine(SecretsDir, "c1.dat"), "geen versleutelde inhoud");

        Assert.Null(new DpapiSecretStore("c1", SecretsDir).Get("Token"));
    }
}

public sealed class TaskSourceHostTests : TempData
{
    private TaskSourceHost NewHost(FakePlugin? plugin = null, ITaskProvider? local = null)
    {
        var result = new PluginLoadResult();
        if (plugin is not null) result.Plugins.Add(new LoadedPlugin(plugin, "test.dll"));
        return new TaskSourceHost(new SourcesStore(SourcesPath), result, id => new DpapiSecretStore(id, SecretsDir), _ => Task.CompletedTask, local ?? new FakeLocal(), QueueDir);
    }

    private static Pane PaneFor(string? sourceId) => new() { Kind = PaneKind.Tasks, SourceId = sourceId, TaskListId = "tasks" };

    [Fact]
    public void A_pane_without_a_source_uses_the_local_tasks()
    {
        var local = new FakeLocal();
        using var host = NewHost(local: local);

        Assert.Same(local, host.ProviderFor(PaneFor(null)));
        Assert.Same(local, host.ProviderFor(PaneFor("")));
    }

    [Fact]
    public void Connections_survive_a_restart_but_their_secrets_stay_out_of_the_config_file()
    {
        var plugin = new FakePlugin();
        using (var host = NewHost(plugin))
        {
            var connection = host.NewConnection("fake", "Mijn bron");
            connection.Values["Url"] = "https://dash.test";
            host.SecretsFor(connection.Id).Set("Token", "dd_geheim-123");
            host.Save(connection);
        }

        using var again = NewHost(plugin);

        var loaded = Assert.Single(again.Connections);
        Assert.Equal("Mijn bron", loaded.Name);
        Assert.Equal("https://dash.test", loaded.Values["Url"]);
        Assert.Equal("dd_geheim-123", again.SecretsFor(loaded.Id).Get("Token"));
        Assert.DoesNotContain("dd_geheim", File.ReadAllText(SourcesPath));
    }

    [Fact]
    public void The_plugin_gets_the_values_and_the_secrets_of_its_own_connection()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        connection.Values["Url"] = "https://dash.test";
        host.SecretsFor(connection.Id).Set("Token", "geheim");
        host.Save(connection);

        var source = host.GetSource(connection.Id, out var error);

        Assert.NotNull(source);
        Assert.Null(error);
        var context = Assert.Single(plugin.Created);
        Assert.Equal("https://dash.test", context.Value("Url"));
        Assert.Equal("geheim", context.Secrets.Get("Token"));
    }

    [Fact]
    public void A_source_is_created_once_and_reused()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);

        var first = host.GetSource(connection.Id, out _);
        var second = host.GetSource(connection.Id, out _);

        Assert.Same(first, second);
        Assert.Single(plugin.Created);
    }

    [Fact]
    public void Saving_with_reset_starts_a_new_source_and_tells_the_panes_without_reset_it_does_not()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        var old = (FakeSource)host.GetSource(connection.Id, out _)!;
        var resets = new List<string>();
        host.SourceReset += (_, id) => resets.Add(id);

        host.Save(connection, resetSource: false);
        Assert.Empty(resets);
        Assert.False(old.Disposed);

        host.Save(connection, resetSource: true);
        Assert.Equal([connection.Id], resets);
        Assert.True(old.Disposed);
        Assert.NotSame(old, host.GetSource(connection.Id, out _));
    }

    [Fact]
    public void Removing_a_connection_wipes_its_secrets_and_stops_its_source()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.SecretsFor(connection.Id).Set("Token", "geheim");
        host.Save(connection);
        var source = (FakeSource)host.GetSource(connection.Id, out _)!;

        host.Remove(connection.Id);

        Assert.Empty(host.Connections);
        Assert.True(source.Disposed);
        Assert.Null(host.SecretsFor(connection.Id).Get("Token"));
        Assert.Empty(new SourcesStore(SourcesPath).Load().Connections);
    }

    [Fact]
    public async Task A_pane_whose_connection_is_gone_explains_it_instead_of_crashing()
    {
        using var host = NewHost(new FakePlugin());

        var provider = host.ProviderFor(PaneFor("bestaat-niet"));

        var ex = await Assert.ThrowsAsync<TaskProviderException>(() => provider.GetTasksAsync("tasks"));
        Assert.Contains("bestaat niet meer", ex.Message);
    }

    [Fact]
    public void A_missing_plugin_is_explained_and_the_connection_is_kept()
    {
        using var host = NewHost(plugin: null);
        var connection = host.NewConnection("verdwenen", "Oude bron");
        host.Save(connection);

        var source = host.GetSource(connection.Id, out var error);

        Assert.Null(source);
        Assert.Contains("plug-in", error);
        Assert.Contains("verdwenen", error);
        Assert.Single(host.Connections); // de instellingen raak je niet kwijt als de plug-in tijdelijk ontbreekt
        Assert.Contains("niet geladen", host.TypeName(connection));
    }

    [Fact]
    public void A_plugin_that_fails_on_start_is_reported_and_does_not_take_the_app_down()
    {
        var plugin = new FakePlugin(failOnCreate: new InvalidOperationException("kapot in de plug-in"));
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);

        var source = host.GetSource(connection.Id, out var error);

        Assert.Null(source);
        Assert.Contains("kapot in de plug-in", error);
        Assert.Contains("Bron", error);
    }

    [Fact]
    public void A_linked_source_sits_in_the_offline_queue_and_the_same_one_is_reused()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);

        var provider = host.ProviderFor(PaneFor(connection.Id));

        Assert.IsAssignableFrom<OfflineQueueProvider>(provider);
        Assert.IsAssignableFrom<IRefreshableTaskProvider>(provider); // de bron kan van buiten veranderen: knop en interval blijven
        Assert.Same(provider, host.ProviderFor(PaneFor(connection.Id)));
    }

    [Fact]
    public void Local_tasks_are_not_put_in_a_queue()
    {
        var local = new FakeLocal();
        using var host = NewHost(local: local);

        Assert.Same(local, host.ProviderFor(PaneFor(null)));
    }

    [Fact]
    public void A_waiting_queue_on_disk_is_picked_up_when_the_source_starts()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        new PendingQueueStore(Path.Combine(QueueDir, connection.Id + ".json")).Save(new PendingQueueData
        {
            Operations = [new PendingOperation { Kind = PendingOperation.Add, ListId = "tasks", TaskId = "queued-1", Task = new TaskItem { Id = "queued-1", Title = "Wachtte al" } }],
        });

        var provider = (OfflineQueueProvider)host.ProviderFor(PaneFor(connection.Id));

        Assert.Equal(1, provider.PendingCount);
    }

    [Fact]
    public void Removing_a_connection_discards_its_waiting_changes()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        var file = Path.Combine(QueueDir, connection.Id + ".json");
        new PendingQueueStore(file).Save(new PendingQueueData { Operations = [new PendingOperation { Kind = PendingOperation.Delete, ListId = "tasks", TaskId = "x" }] });

        host.Remove(connection.Id);

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void Changing_a_connection_keeps_its_waiting_changes_and_starts_a_fresh_queue_object()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        var file = Path.Combine(QueueDir, connection.Id + ".json");
        new PendingQueueStore(file).Save(new PendingQueueData { Operations = [new PendingOperation { Kind = PendingOperation.Delete, ListId = "tasks", TaskId = "x" }] });
        var before = (OfflineQueueProvider)host.ProviderFor(PaneFor(connection.Id));

        host.Save(connection, resetSource: true); // bijvoorbeeld een ander token ingevuld
        var after = (OfflineQueueProvider)host.ProviderFor(PaneFor(connection.Id));

        Assert.NotSame(before, after);
        Assert.Equal(1, after.PendingCount); // het wachtende staat in het bestand en gaat niet verloren
    }

    [Fact]
    public void A_new_source_starts_with_the_interval_the_user_chose()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        host.SetPollInterval(TimeSpan.FromSeconds(30));
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);

        host.GetSource(connection.Id, out _);

        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Single(plugin.Created).PollInterval);
    }

    [Fact]
    public void Changing_the_interval_reaches_a_source_that_is_already_running()
    {
        var plugin = new FakePlugin();
        using var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        var source = host.GetSource(connection.Id, out _)!;

        host.SetPollInterval(TimeSpan.FromSeconds(120));

        Assert.Equal(TimeSpan.FromSeconds(120), ((FakeRefreshable)source.Provider).PollInterval);
        Assert.Single(plugin.Created); // zonder de bron opnieuw te maken
    }

    [Fact]
    public void Disposing_the_host_disposes_all_sources()
    {
        var plugin = new FakePlugin();
        var host = NewHost(plugin);
        var connection = host.NewConnection("fake", "Bron");
        host.Save(connection);
        var source = (FakeSource)host.GetSource(connection.Id, out _)!;

        host.Dispose();

        Assert.True(source.Disposed);
    }
}

public sealed class LegacyGoogleMigrationTests : TempData
{
    private TaskSourceHost NewHost() =>
        new(new SourcesStore(SourcesPath), new PluginLoadResult(), id => new DpapiSecretStore(id, SecretsDir), _ => Task.CompletedTask, new FakeLocal(), QueueDir);

    private void WriteLegacy(string clientId, string? secret)
    {
        File.WriteAllText(Path.Combine(Dir, "google.json"),
            secret is null ? $$"""{ "ClientId": "{{clientId}}" }""" : $$"""{ "ClientId": "{{clientId}}", "ClientSecret": "{{secret}}" }""");
    }

    private static Layout LayoutWithGooglePane(out Pane google)
    {
        google = new Pane { Kind = PaneKind.Tasks, Title = "Mijn taken", TaskListId = "LIJST1", TaskSource = TaskSources.Google };
        return new Layout { Panes = [new Pane { Kind = PaneKind.Tasks, TaskListId = "lokaal" }, google] };
    }

    [Fact]
    public void An_existing_google_setup_becomes_a_connection_with_its_secrets_and_token()
    {
        WriteLegacy("client-123.apps.googleusercontent.com", "geheim-abc");
        var layout = LayoutWithGooglePane(out var google);
        using var host = NewHost();

        var changed = LegacyGoogleMigration.Run(host, layout, Dir, () => "refresh-xyz");

        Assert.True(changed);
        var connection = Assert.Single(host.Connections);
        Assert.Equal("google-tasks", connection.TypeId);
        Assert.Equal("client-123.apps.googleusercontent.com", connection.Values["ClientId"]);
        var secrets = host.SecretsFor(connection.Id);
        Assert.Equal("geheim-abc", secrets.Get("ClientSecret"));
        Assert.Equal("refresh-xyz", secrets.Get("refreshToken"));
        Assert.Equal(connection.Id, google.SourceId);
        Assert.Equal("LIJST1", google.TaskListId); // dezelfde Google-lijst
    }

    [Fact]
    public void Local_panes_are_left_alone()
    {
        WriteLegacy("client-123", null);
        var layout = LayoutWithGooglePane(out _);
        using var host = NewHost();

        LegacyGoogleMigration.Run(host, layout, Dir, () => null);

        var local = layout.Panes[0];
        Assert.Null(local.SourceId);
        Assert.Equal(TaskSources.Local, local.TaskSource);
    }

    [Fact]
    public void Nothing_secret_ends_up_in_the_readable_config_file()
    {
        WriteLegacy("client-123", "geheim-abc");
        using var host = NewHost();

        LegacyGoogleMigration.Run(host, LayoutWithGooglePane(out _), Dir, () => "refresh-xyz");

        var config = File.ReadAllText(SourcesPath);
        Assert.DoesNotContain("geheim-abc", config);
        Assert.DoesNotContain("refresh-xyz", config);
    }

    [Fact]
    public void Running_it_twice_changes_nothing_the_second_time()
    {
        WriteLegacy("client-123", "geheim-abc");
        var layout = LayoutWithGooglePane(out _);
        using var host = NewHost();
        LegacyGoogleMigration.Run(host, layout, Dir, () => "refresh-xyz");

        var again = LegacyGoogleMigration.Run(host, layout, Dir, () => "refresh-xyz");

        Assert.False(again);
        Assert.Single(host.Connections);
    }

    [Fact]
    public void It_also_works_after_a_restart()
    {
        WriteLegacy("client-123", null);
        var layout = LayoutWithGooglePane(out _);
        using (var host = NewHost()) LegacyGoogleMigration.Run(host, layout, Dir, () => null);

        using var restarted = NewHost();

        Assert.False(LegacyGoogleMigration.Run(restarted, layout, Dir, () => null));
        Assert.Single(restarted.Connections);
    }

    [Fact]
    public void Without_any_google_setup_nothing_is_created()
    {
        var layout = new Layout { Panes = [new Pane { Kind = PaneKind.Tasks, TaskListId = "lokaal" }] };
        using var host = NewHost();

        Assert.False(LegacyGoogleMigration.Run(host, layout, Dir, () => null));
        Assert.Empty(host.Connections);
    }

    [Fact]
    public void A_google_pane_without_a_settings_file_still_gets_a_connection_so_it_can_be_repaired()
    {
        var layout = LayoutWithGooglePane(out var google);
        using var host = NewHost();

        Assert.True(LegacyGoogleMigration.Run(host, layout, Dir, () => null));

        var connection = Assert.Single(host.Connections);
        Assert.Equal("", connection.Values["ClientId"]);
        Assert.Equal(connection.Id, google.SourceId);
    }

    [Fact]
    public void A_broken_settings_file_does_not_stop_the_start()
    {
        File.WriteAllText(Path.Combine(Dir, "google.json"), "{ kapot");
        var layout = LayoutWithGooglePane(out var google);
        using var host = NewHost();

        Assert.True(LegacyGoogleMigration.Run(host, layout, Dir, () => null));
        Assert.NotNull(google.SourceId);
    }

    [Fact]
    public void The_old_files_are_left_in_place_as_a_fallback()
    {
        WriteLegacy("client-123", "geheim-abc");
        File.WriteAllText(Path.Combine(Dir, "google.token"), "x");
        using var host = NewHost();

        LegacyGoogleMigration.Run(host, LayoutWithGooglePane(out _), Dir, () => "refresh");

        Assert.True(File.Exists(Path.Combine(Dir, "google.json")));
        Assert.True(File.Exists(Path.Combine(Dir, "google.token")));
    }
}
