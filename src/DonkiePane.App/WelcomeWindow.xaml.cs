using System.Windows;

namespace DonkiePane.App;

/// <summary>
/// Het scherm bij de allereerste start: legt uit wat DonkiePane met het bureaublad doet en laat twee keuzes maken
/// voordat er iets verandert. Pas na "Aan de slag" worden de Windows-bureaubladiconen verborgen.
/// </summary>
public partial class WelcomeWindow : Window
{
    public WelcomeWindow(bool hideIcons = true, bool startWithWindows = false)
    {
        InitializeComponent();
        HideIconsBox.IsChecked = hideIcons;
        AutoStartBox.IsChecked = startWithWindows;
    }

    public bool HideDesktopIcons => HideIconsBox.IsChecked == true;
    public bool StartWithWindows => AutoStartBox.IsChecked == true;

    /// <summary>De gebruiker wil direct een takenbron toevoegen (opent daarna Instellingen).</summary>
    public bool OpenSettings { get; private set; }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Sources_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings = true;
        DialogResult = true;
    }
}
