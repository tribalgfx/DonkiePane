using System.Diagnostics;
using System.IO;
using System.Windows;
using DonkiePane.Core;
using DonkiePane.Tasks;
using DonkiePane.Tasks.Plugins;
using Forms = System.Windows.Forms;

namespace DonkiePane.App;

/// <summary>Takenbronnen: plug-ins laden, verbindingen beheren en takenpanes eraan koppelen.</summary>
public partial class App
{
    private TaskSourceHost _sources = null!;
    private bool _addingSourcePane;

    private ITaskProvider TaskProviderFor(Pane pane) => _sources.ProviderFor(pane);

    private readonly PluginApprovals _pluginApprovals = new(PluginApprovals.DefaultPath);

    /// <summary>
    /// Laadt de plug-ins uit twee mappen: <c>plugins</c> naast het programma (van de installer) en
    /// <c>%APPDATA%\DonkiePane\plugins</c> (je eigen plug-ins; pas als je ze hebt toegestaan, zie
    /// <see cref="ApprovePlugins"/>). Nooit uit een andere plek en nooit van internet.
    /// </summary>
    private TaskSourceHost CreateSourceHost()
    {
        var appData = Path.GetDirectoryName(LayoutStore.DefaultPath)!;
        var plugins = PluginLoader.Load([Path.Combine(AppContext.BaseDirectory, "plugins")], Path.Combine(appData, "plugins"),
            _pluginApprovals.IsApproved);

        var host = new TaskSourceHost(new SourcesStore(SourcesStore.DefaultPath), plugins,
            id => new DpapiSecretStore(id), OpenBrowser, _localTasks, Path.Combine(appData, "queue"));
        host.SourceReset += (_, sourceId) => Dispatcher.InvokeAsync(() => RebuildTaskPanes(sourceId));
        return host;
    }

    /// <summary>
    /// Na het starten: staan er nieuwe of veranderde plug-ins in je eigen plug-inmap, dan zijn die niet geladen. Eén
    /// melding; toestaan gaat in Instellingen → Takenbronnen.
    /// </summary>
    private void NotifyPendingPlugins()
    {
        if (_sources.Plugins.Pending.Count == 0 || _trayIcon is null) return;
        _balloonClicked = ShowSettings;
        _trayIcon.ShowBalloonTip(15_000, Loc.T("Nieuwe plug-in gevonden"),
            Loc.F("{0} staat in je plug-inmap, maar is niet geladen. Heb je hem zelf geplaatst? Sta hem toe in Instellingen → Takenbronnen.",
                string.Join(", ", _sources.Plugins.Pending.Select(p => Path.GetFileNameWithoutExtension(p.Path)))),
            Forms.ToolTipIcon.Warning);
    }

    /// <summary>Sta deze plug-ins toe en start opnieuw, zodat ze geladen worden.</summary>
    private void ApprovePlugins(IReadOnlyList<PendingPlugin> plugins)
    {
        try
        {
            _pluginApprovals.Approve(plugins.Select(p => p.Hash));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            MessageBox.Show(Loc.F("Toestaan lukte niet: {0}", ex.Message), "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Restart();
    }

    /// <summary>Opent een inlogpagina in de standaardbrowser; alleen gewone webadressen.</summary>
    private static Task OpenBrowser(Uri url)
    {
        if (url.Scheme is not ("https" or "http"))
            throw new TaskProviderException(Loc.T("Alleen webadressen worden in de browser geopend."));
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>De instellingen van een bron zijn veranderd: panes die hem gebruiken beginnen opnieuw met de nieuwe waarden.</summary>
    private void RebuildTaskPanes(string sourceId)
    {
        foreach (var pane in _layout.Panes.Where(p => p is { Kind: PaneKind.Tasks, TaskListId: not null } && p.SourceId == sourceId).ToList())
        {
            ForgetContent(pane);
            if (WindowOf(pane) is { } window) RefreshTabs(window);
        }
    }

    /// <summary>Het submenu "Nieuw takenpane": lokaal plus elke ingestelde bron, alleen wat er echt is.</summary>
    private void FillNewTasksMenu(Forms.ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        menu.DropDownItems.Add(Loc.T("Lokaal (alleen op deze pc)"), null, (_, _) => AddPane(Pane.NewTaskPane()));
        foreach (var connection in _sources.Connections)
        {
            var captured = connection;
            menu.DropDownItems.Add($"{connection.Name}…", null, (_, _) => _ = AddSourcePaneAsync(captured));
        }
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add(Loc.T("Bronnen beheren…"), null, (_, _) => ShowSettings());
    }

    /// <summary>
    /// Laat een lijst kiezen bij de bron en maakt er een pane van. Elke stap kan mislukken (niet ingelogd,
    /// geen verbinding, geweigerd); dat wordt gemeld, niets gaat stuk.
    /// </summary>
    private async Task AddSourcePaneAsync(SourceConnection connection)
    {
        if (_addingSourcePane) return; // niet twee keer tegelijk
        _addingSourcePane = true;
        try
        {
            var source = _sources.GetSource(connection.Id, out var error);
            if (source is null)
            {
                MessageBox.Show(error, connection.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var lists = await source.GetListsAsync();
            if (lists.Count == 0)
            {
                MessageBox.Show(Loc.T("Bij deze bron zijn geen lijsten gevonden."), connection.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Een bron met één lijst hoeft geen keuze; het pane krijgt de naam van de bron.
            if (lists.Count == 1)
            {
                AddPane(Pane.NewSourcePane(connection.Id, lists[0].Id, connection.Name));
                return;
            }

            var picker = new ListPickerWindow(connection.Name, Loc.T("Welke lijst wil je in dit pane tonen? De lijst blijft bij de bron staan; verwijder je het pane later, dan blijven je taken bewaard."), lists);
            if (picker.ShowDialog() == true && picker.Selected is { } chosen)
                AddPane(Pane.NewSourcePane(connection.Id, chosen.Id, chosen.Title));
        }
        catch (Exception ex) when (TaskErrors.IsHandled(ex))
        {
            MessageBox.Show(TaskErrors.Message(ex), connection.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _addingSourcePane = false;
        }
    }
}
