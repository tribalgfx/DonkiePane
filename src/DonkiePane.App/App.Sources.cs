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

    /// <summary>
    /// Laadt de plug-ins uit twee mappen die de gebruiker zelf beheert: <c>plugins</c> naast het programma
    /// en <c>%APPDATA%\DonkiePane\plugins</c>. Nooit uit een andere plek en nooit van internet.
    /// </summary>
    private TaskSourceHost CreateSourceHost()
    {
        var appData = Path.GetDirectoryName(LayoutStore.DefaultPath)!;
        var plugins = PluginLoader.Load([Path.Combine(AppContext.BaseDirectory, "plugins"), Path.Combine(appData, "plugins")]);

        var host = new TaskSourceHost(new SourcesStore(SourcesStore.DefaultPath), plugins,
            id => new DpapiSecretStore(id), OpenBrowser, _localTasks);
        host.SourceReset += (_, sourceId) => Dispatcher.InvokeAsync(() => RebuildTaskPanes(sourceId));
        return host;
    }

    /// <summary>Opent een inlogpagina in de standaardbrowser; alleen gewone webadressen.</summary>
    private static Task OpenBrowser(Uri url)
    {
        if (url.Scheme is not ("https" or "http"))
            throw new TaskProviderException("Alleen webadressen worden in de browser geopend.");
        Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    /// <summary>De instellingen van een bron zijn veranderd: panes die hem gebruiken beginnen opnieuw met de nieuwe waarden.</summary>
    private void RebuildTaskPanes(string sourceId)
    {
        foreach (var window in _windows.Where(w => w.Pane is { Kind: PaneKind.Tasks, TaskListId: not null } p && p.SourceId == sourceId))
            window.SetContent(new TasksView(_sources.ProviderFor(window.Pane), window.Pane.TaskListId!));
    }

    /// <summary>Het submenu "Nieuw takenpane": lokaal plus elke ingestelde bron, alleen wat er echt is.</summary>
    private void FillNewTasksMenu(Forms.ToolStripMenuItem menu)
    {
        menu.DropDownItems.Clear();
        menu.DropDownItems.Add("Lokaal (alleen op deze pc)", null, (_, _) => AddPane(Pane.NewTaskPane()));
        foreach (var connection in _sources.Connections)
        {
            var captured = connection;
            menu.DropDownItems.Add($"{connection.Name}…", null, (_, _) => _ = AddSourcePaneAsync(captured));
        }
        menu.DropDownItems.Add(new Forms.ToolStripSeparator());
        menu.DropDownItems.Add("Bronnen beheren…", null, (_, _) => ShowSettings());
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
                MessageBox.Show("Bij deze bron zijn geen lijsten gevonden.", connection.Name, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Een bron met één lijst hoeft geen keuze; het pane krijgt de naam van de bron.
            if (lists.Count == 1)
            {
                AddPane(Pane.NewSourcePane(connection.Id, lists[0].Id, connection.Name));
                return;
            }

            var picker = new ListPickerWindow(connection.Name, "Welke lijst wil je in dit pane tonen? De lijst blijft bij de bron staan; verwijder je het pane later, dan blijven je taken bewaard.", lists);
            if (picker.ShowDialog() == true && picker.Selected is { } chosen)
                AddPane(Pane.NewSourcePane(connection.Id, chosen.Id, chosen.Title));
        }
        catch (TaskProviderException ex)
        {
            MessageBox.Show(ex.Message, connection.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _addingSourcePane = false;
        }
    }
}
