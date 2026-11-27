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
            Assert.Equal(8, ((ContextMenu)((FrameworkElement)window.FindName("TitleBar")).ContextMenu).Items.Count);

            // Een thema kiezen wist een eigen kleur en meldt de wijziging.
            pane.CustomColor = "#2F9E6B";
            var theme = (MenuItem)((MenuItem)window.FindName("ThemeMenu")).Items[1];
            theme.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(pane.CustomColor);
            Assert.Equal("blauw", pane.Theme);
            Assert.Equal(1, changed);
        });
    }

    [Fact]
    public void An_icons_pane_can_switch_view_and_sorting()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { Title = "Test" };
            var window = new PaneWindow(pane);
            var viewChanged = 0;
            var changed = 0;
            window.ViewChanged += (_, _) => viewChanged++;
            window.PaneChanged += (_, _) => changed++;

            var viewMenu = (MenuItem)window.FindName("ViewMenu");
            var sortMenu = (MenuItem)window.FindName("SortMenu");
            Assert.Equal(Visibility.Visible, viewMenu.Visibility);
            MenuItem[] Sizes() => viewMenu.Items.OfType<MenuItem>().Where(m => m.Tag is IconSize).ToArray();
            Assert.Equal(3, Sizes().Length);
            Assert.Equal(4, sortMenu.Items.Count);
            Assert.True(((MenuItem)viewMenu.Items[0]).IsChecked);  // grote iconen
            Assert.True(((MenuItem)sortMenu.Items[0]).IsChecked);  // eigen volgorde

            ((MenuItem)viewMenu.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)sortMenu.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(IconSize.List, pane.IconSize);
            Assert.Equal(ItemSortOrder.Name, pane.SortBy);
            Assert.Equal((2, 2), (viewChanged, changed));
            // Het vinkje staat nu bij de nieuwe keuze, en alleen daar.
            Assert.Equal([2], Sizes().Select((m, i) => (m, i)).Where(x => x.m.IsChecked).Select(x => x.i));
            Assert.Equal([1], sortMenu.Items.Cast<MenuItem>().Select((m, i) => (m, i)).Where(x => x.m.IsChecked).Select(x => x.i));
        });
    }

    [Fact]
    public void Icon_size_and_names_are_set_per_pane()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { Title = "Test" };
            var window = new PaneWindow(pane);
            var viewChanged = 0;
            window.ViewChanged += (_, _) => viewChanged++;
            var viewMenu = (MenuItem)window.FindName("ViewMenu");
            MenuItem Item(string header) => viewMenu.Items.OfType<MenuItem>().Single(m => (string)m.Header == Loc.T(header));
            void Click(string header) => Item(header).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.False(Item("Standaardgrootte").IsEnabled); // nog de grootte van de weergave
            Click("Grotere iconen");
            Assert.Equal(64, pane.IconPixels);
            Click("Kleinere iconen");
            Assert.Null(pane.IconPixels); // terug op 48: de standaard, dus niets eigens meer
            Click("Kleinere iconen");
            Assert.Equal(40, pane.IconPixels);
            Assert.True(Item("Standaardgrootte").IsEnabled);

            Assert.True(Item("Namen tonen").IsChecked);
            Click("Namen tonen");
            Assert.False(pane.ShowNames);
            Assert.False(Item("Namen tonen").IsChecked);

            // Een andere weergave kiezen zet de eigen grootte terug; een lijst heeft de namen altijd.
            viewMenu.Items.OfType<MenuItem>().Single(m => m.Tag is IconSize.List).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Null(pane.IconPixels);
            Assert.DoesNotContain(viewMenu.Items.OfType<MenuItem>(), m => (string)m.Header == Loc.T("Namen tonen"));
            Assert.Equal(5, viewChanged);
        });
    }

    [Fact]
    public void A_tasks_pane_has_no_view_or_sort_menu()
    {
        OnStaThread(() =>
        {
            var window = new PaneWindow(new Pane { Kind = PaneKind.Tasks, Title = "Taken" });
            Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("ViewMenu")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("SortMenu")).Visibility);
        });
    }
}
