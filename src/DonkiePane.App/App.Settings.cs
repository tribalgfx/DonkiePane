using DonkiePane.Core;

namespace DonkiePane.App;

public partial class App
{
    private SettingsWindow? _settingsWindow;

    /// <summary>
    /// DonkiePane opnieuw starten (bijv. voor een andere taal). Het nieuwe proces wacht tot dit proces weg is: er mag er
    /// maar één tegelijk draaien.
    /// </summary>
    private void Restart()
    {
        if (Environment.ProcessPath is not { } exe) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--restart-after {Environment.ProcessId}")
            {
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return; // dan blijft deze gewoon draaien
        }
        Shutdown();
    }

    /// <summary>De regels nu toepassen op wat er in de Inbox staat; geeft het aantal verplaatste iconen.</summary>
    private int ApplyRulesNow()
    {
        var moved = LayoutEditor.ApplyRulesToInbox(_layout, IsFolderItem);
        if (moved == 0) return 0;
        SaveLayout();
        RefreshItems();
        return moved;
    }

    private void ShowSettings()
    {
        // Eén instellingenvenster tegelijk: een tweede keer openen haalt het naar voren.
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_layout, SaveLayout, BringAllPanesToPrimaryScreen, _sources, _updates, ShowUpdateWindow, Restart,
            ExportLayout, ImportLayout, RefreshAllLooks, ApplyPeekHotkey, ApplyRulesNow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}

