using System.IO;
using System.Windows.Threading;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>De indeling exporteren naar een bestand en weer importeren (bijv. op een nieuwe pc).</summary>
public partial class App
{
    /// <summary>Waar de indeling van vóór de laatste import staat, om terug te kunnen.</summary>
    internal static string BeforeImportPath => Path.Combine(Path.GetDirectoryName(LayoutStore.DefaultPath)!, "layout.before-import.json");

    private void ExportLayout(string path) => File.WriteAllText(path, LayoutTransfer.Serialize(_layout));

    /// <summary>
    /// Vervangt de panes door die uit een geïmporteerde indeling. De huidige indeling gaat eerst naar
    /// <see cref="BeforeImportPath"/>. Iconen die op deze pc niet bestaan vallen weg; wat hier wel op het bureaublad
    /// staat maar nergens in het bestand, komt in de Inbox.
    /// </summary>
    private void ImportLayout(Layout imported)
    {
        File.WriteAllText(BeforeImportPath, LayoutTransfer.Serialize(_layout));

        var iconsWereHidden = _layout.HideDesktopIcons;
        LayoutTransfer.ApplyTo(_layout, imported);
        LayoutEditor.Repair(_layout);
        var unavailable = new List<string>();
        LayoutEditor.Sync(_layout, _desktop!.EnumerateItems(unavailable), unavailable);
        SaveLayout();

        _sources.SetPollInterval(_layout.SyncInterval);
        if (iconsWereHidden != _layout.HideDesktopIcons) DesktopIcons.SetVisible(!_layout.HideDesktopIcons);
        RecreatePaneWindows();

        // Het instellingenvenster toont nog de oude waarden: opnieuw openen.
        if (_settingsWindow is { } settings)
            Dispatcher.InvokeAsync(() =>
            {
                settings.Close();
                ShowSettings();
            }, DispatcherPriority.Background);
    }
}
