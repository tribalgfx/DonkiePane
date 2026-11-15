using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>Alle panes tijdelijk verbergen met een dubbelklik op het bureaublad.</summary>
public partial class App
{
    private DesktopDoubleClickWatcher? _doubleClickWatcher;
    private bool _panesHidden;

    private void WatchDesktopDoubleClick()
    {
        _doubleClickWatcher = new DesktopDoubleClickWatcher();
        _doubleClickWatcher.DoubleClickedOnDesktop += (_, _) =>
        {
            // De instelling wordt pas hier gelezen, zodat een wijziging in Instellingen direct geldt.
            // Staat ze uit terwijl de panes verborgen zijn, dan komen ze toch terug.
            if (_layout.HideOnDesktopDoubleClick || _panesHidden) TogglePanesHidden();
        };
    }

    private void TogglePanesHidden()
    {
        if (_panesHidden) ShowAllPanes();
        else HideAllPanes();
    }

    private void HideAllPanes()
    {
        _gridOverlay?.Hide();
        foreach (var window in _windows) window.Hide(); // de inhoud (iconen, taken) gaat vanzelf mee
        _panesHidden = true;
    }

    private void ShowAllPanes()
    {
        _panesHidden = false;
        foreach (var window in _windows)
        {
            window.Show();
            DesktopLayer.SendToBottom(window);
        }
    }
}
