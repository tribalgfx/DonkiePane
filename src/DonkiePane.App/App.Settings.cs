namespace DonkiePane.App;

public partial class App
{
    private SettingsWindow? _settingsWindow;

    private void ShowSettings()
    {
        // Eén instellingenvenster tegelijk: een tweede keer openen haalt het naar voren.
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_layout, SaveLayout, BringAllPanesToPrimaryScreen, _sources, _updates, ShowUpdateWindow);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}

