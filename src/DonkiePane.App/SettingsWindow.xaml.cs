using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DonkiePane.Core;
using DonkiePane.Shell;

namespace DonkiePane.App;

/// <summary>Instellingen. Elke wijziging geldt direct; er is geen OK-knop.</summary>
public partial class SettingsWindow : Window
{
    private readonly Layout _layout;
    private readonly Action _save;
    private readonly Action _bringPanes;
    private bool _loading = true;

    public SettingsWindow(Layout layout, Action save, Action bringPanes)
    {
        InitializeComponent();
        _layout = layout;
        _save = save;
        _bringPanes = bringPanes;

        HideIconsBox.IsChecked = layout.HideDesktopIcons;
        SnapBox.IsChecked = layout.SnapToGrid;
        GridSlider.Value = layout.GridSize;
        GridValue.Text = $"{layout.GridSize:0}";
        GridSlider.IsEnabled = layout.SnapToGrid;
        RefreshAutoStart();
        DataPath.Text = Path.GetDirectoryName(LayoutStore.DefaultPath);
        VersionText.Text = "DonkiePane " + (Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "");
        _loading = false;
    }

    private void RefreshAutoStart()
    {
        AutoStartBox.IsChecked = AutoStart.IsEnabled();
        AutoStartHint.Text = AutoStart.RegisteredPath() is { } path
            ? $"Start met: {path}"
            : "Je kunt dit ook in Taakbeheer onder Opstarten aan- of uitzetten.";
    }

    private void HideIcons_Click(object sender, RoutedEventArgs e)
    {
        _layout.HideDesktopIcons = HideIconsBox.IsChecked == true;
        DesktopIcons.SetVisible(!_layout.HideDesktopIcons);
        _save();
    }

    private void AutoStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AutoStart.SetEnabled(AutoStartBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshAutoStart(); // toon wat er werkelijk geregistreerd staat
    }

    private void Snap_Click(object sender, RoutedEventArgs e)
    {
        _layout.SnapToGrid = SnapBox.IsChecked == true;
        GridSlider.IsEnabled = _layout.SnapToGrid;
        _save();
    }

    private void GridSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        _layout.GridSize = GridSlider.Value;
        GridValue.Text = $"{GridSlider.Value:0}";
        _save();
    }

    private void BringPanes_Click(object sender, RoutedEventArgs e) => _bringPanes();

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(LayoutStore.DefaultPath)!;
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\""));
    }
}
