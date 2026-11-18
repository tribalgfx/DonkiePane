using System.Windows;
using System.Windows.Controls;
using DonkiePane.App;
using DonkiePane.Core;

namespace DonkiePane.App.Tests;

/// <summary>Een pane-venster wordt pas gebouwd als de app draait; een fout in het opbouwen of in het menu zou de hele app laten crashen.</summary>
[Collection("WPF-vensters")] // WPF heeft één Application per proces: vensters niet tegelijk vanaf verschillende threads bouwen
public sealed class PaneWindowTests
{
    private static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Exception("Fout op de UI-thread: " + error, error);
    }

    [Fact]
    public void The_window_builds_and_the_menu_reflects_and_changes_the_look()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { Title = "Test", Opacity = 0.5, ShowPattern = true };
            var changed = 0;
            var window = new PaneWindow(pane);
            window.PaneChanged += (_, _) => changed++;

            var transparency = (MenuItem)window.FindName("TransparencyMenu");
            var pattern = (MenuItem)window.FindName("PatternMenuItem");
            Assert.Equal(PaneThemes.TransparencySteps.Count, transparency.Items.Count);
            Assert.True(pattern.IsChecked);
            Assert.Equal("50%", ((MenuItem)transparency.Items.Cast<object>().Single(i => ((MenuItem)i).IsChecked)).Header);

            // Alle opties staan binnen "Thema", niet los ernaast.
            var themeMenu = (MenuItem)window.FindName("ThemeMenu");
            Assert.Contains(transparency, themeMenu.Items.Cast<object>());
            Assert.Contains(pattern, themeMenu.Items.Cast<object>());
            Assert.Equal(3, ((ContextMenu)((FrameworkElement)window.FindName("TitleBar")).ContextMenu).Items.Count);

            // Een thema kiezen wist een eigen kleur en meldt de wijziging.
            pane.CustomColor = "#2F9E6B";
            var theme = (MenuItem)((MenuItem)window.FindName("ThemeMenu")).Items[1];
            theme.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(pane.CustomColor);
            Assert.Equal("blauw", pane.Theme);
            Assert.Equal(1, changed);
        });
    }
}
