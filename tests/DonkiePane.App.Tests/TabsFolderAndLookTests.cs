using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DonkiePane.App;
using DonkiePane.Core;

namespace DonkiePane.App.Tests;

/// <summary>Tabbladen, mappanes, licht/donker, matglas, oprollen en de regel-dialoog: echt opgebouwd op een STA-thread.</summary>
[Collection("WPF-vensters")]
public sealed class TabsFolderAndLookTests : TempData
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

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    [Fact]
    public void A_window_with_two_panes_shows_tabs_and_switching_asks_the_app()
    {
        OnStaThread(() =>
        {
            var a = new Pane { Title = "Werk" };
            var b = new Pane { Title = "Thuis", Theme = "groen" };
            var window = new PaneWindow(a);
            Pane? selected = null;
            window.TabSelected += (_, pane) => selected = pane;

            window.SetTabs([a, b], a, new Grid());

            var strip = (StackPanel)window.FindName("TabStrip");
            Assert.Equal(Visibility.Visible, strip.Visibility);
            Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("TitleText")).Visibility);
            Assert.Equal(2, strip.Children.Count);
            Assert.Equal(Visibility.Visible, ((MenuItem)window.FindName("DetachMenuItem")).Visibility);

            // Klikken op het tweede tabblad: de app beslist (en zet dan de nieuwe inhoud).
            var second = (Border)strip.Children[1];
            second.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
            Assert.Same(b, selected);

            window.SetTabs([a, b], b, new Grid());
            Assert.Same(b, window.ActivePane);
            Assert.Same(a, window.Pane); // plek en grootte blijven van het venster-pane

            // Het menu gaat over het open tabblad.
            Click((MenuItem)((MenuItem)window.FindName("ThemeMenu")).Items[0]);
            Assert.Equal("antraciet", b.Theme);
            Assert.Equal("", a.Theme);

            // Terug naar één pane: geen tabbladen meer.
            window.SetTabs([a], a, new Grid());
            Assert.Equal(Visibility.Collapsed, strip.Visibility);
            Assert.Equal(Visibility.Collapsed, ((MenuItem)window.FindName("DetachMenuItem")).Visibility);
        });
    }

    [Fact]
    public void The_menu_offers_other_panes_to_become_a_tab_of()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { Title = "Werk" };
            var other = new Pane { Title = "Documenten" };
            var window = new PaneWindow(pane) { MergeTargets = () => [other] };
            Pane? target = null;
            window.MergeRequested += (_, t) => target = t;

            var menu = (ContextMenu)((FrameworkElement)window.FindName("TitleBar")).ContextMenu;
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent, menu));
            var merge = (MenuItem)window.FindName("MergeMenu");
            Assert.True(merge.IsEnabled);
            Click((MenuItem)merge.Items[0]);
            Assert.Same(other, target);
        });
    }

    [Fact]
    public void Frosted_glass_is_switched_on_per_pane_from_the_theme_menu()
    {
        OnStaThread(() =>
        {
            var pane = new Pane();
            var window = new PaneWindow(pane);
            var changed = 0;
            window.PaneChanged += (_, _) => changed++;

            var frosted = (MenuItem)window.FindName("FrostedMenuItem");
            frosted.IsChecked = true;
            Click(frosted);

            Assert.Equal(PaneMaterial.Frosted, pane.Material);
            Assert.Equal(1, changed);

            Click((MenuItem)((MenuItem)window.FindName("ThemeMenu")).Items.OfType<MenuItem>().Last()); // standaard herstellen
            Assert.Equal(PaneMaterial.Solid, pane.Material);
        });
    }

    [Fact]
    public void Light_mode_gives_dark_text_on_a_light_background()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { Opacity = 1 };
            var dark = new ResourceDictionary();
            var light = new ResourceDictionary();
            PaneThemes.Apply(dark, pane, lightMode: false);
            PaneThemes.Apply(light, pane, lightMode: true);

            static double Lum(object brush) => ((SolidColorBrush)brush).Color is var c ? (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255 : 0;
            Assert.True(Lum(dark["PaneBackground"]) < 0.2);
            Assert.True(Lum(light["PaneBackground"]) > 0.8);
            Assert.True(Lum(light["PaneText"]) < 0.2);
            Assert.True(Lum(dark["PaneText"]) > 0.8);
            Assert.True(dark.Contains("PaneTextShadow"));
            Assert.False(light.Contains("PaneTextShadow"));
        });
    }

    [Fact]
    public void Corner_radius_comes_from_the_settings()
    {
        OnStaThread(() =>
        {
            var window = new PaneWindow(new Pane()) { LookSettings = () => new PaneStyle(false, 0, RollUpOpen.Button) };
            window.RefreshLook();
            Assert.Equal(new CornerRadius(0), ((Border)window.FindName("Frame")).CornerRadius);

            window.LookSettings = () => new PaneStyle(false, 99, RollUpOpen.Button); // te groot: begrensd
            window.RefreshLook();
            Assert.Equal(new CornerRadius(Layout.MaxCornerRadius), ((Border)window.FindName("Frame")).CornerRadius);
        });
    }

    [Fact]
    public void A_rolled_up_pane_opens_for_a_while_without_being_unrolled()
    {
        OnStaThread(() =>
        {
            var pane = new Pane { IsRolledUp = true, Height = 300 };
            var window = new PaneWindow(pane);
            var body = (ContentControl)window.FindName("Body");
            Assert.Equal(Visibility.Collapsed, body.Visibility);

            window.OpenRolledUpForAWhile();

            Assert.True(window.IsPeekOpen);
            Assert.Equal(Visibility.Visible, body.Visibility);
            Assert.Equal(300, window.Height);
            Assert.True(pane.IsRolledUp); // bewaard blijft hij opgerold
            Assert.Equal(300, pane.Height);
        });
    }

    [Fact]
    public void A_folder_pane_lists_the_folder_and_can_look_inside_a_subfolder()
    {
        var root = Path.Combine(Dir, "Projecten");
        Directory.CreateDirectory(Path.Combine(root, "Website", "Ontwerp"));
        File.WriteAllText(Path.Combine(root, "plan.txt"), "x");
        File.WriteAllText(Path.Combine(root, "Website", "index.html"), "x");
        File.WriteAllText(Path.Combine(root, "Website", "Ontwerp", "logo-ontwerp.svg"), "x");
        var hidden = Path.Combine(root, "geheim.txt");
        File.WriteAllText(hidden, "x");
        File.SetAttributes(hidden, FileAttributes.Hidden);

        OnStaThread(() =>
        {
            var pane = Pane.NewFolderPane(root);
            Assert.Equal("Projecten", pane.Title);
            var view = new FolderPaneView(pane);
            view.Navigate(root, force: true);

            // Mappen eerst, verborgen bestanden niet.
            Assert.Equal(["Website", "plan.txt"], view.Icons.VisibleItems.Select(i => Path.GetFileName(i.Path)));

            view.Navigate(Path.Combine(root, "Website"));
            Assert.Equal(["Ontwerp", "index.html"], view.Icons.VisibleItems.Select(i => Path.GetFileName(i.Path)));
            Assert.Equal(Path.Combine(root, "Website"), view.Icons.TargetFolder); // plakken en neerzetten gaat hierheen

            // Buiten de map van het pane kan niet: dan terug naar de map van het pane.
            view.Navigate(Dir);
            Assert.Equal(root, view.CurrentFolder);
        });
    }

    [Fact]
    public void Searching_a_folder_pane_also_looks_in_subfolders()
    {
        var root = Path.Combine(Dir, "Zoek");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        File.WriteAllText(Path.Combine(root, "a", "b", "Factuur maart.pdf"), "x");
        File.WriteAllText(Path.Combine(root, "factuur april.pdf"), "x");
        File.WriteAllText(Path.Combine(root, "notities.txt"), "x");

        var found = FolderPaneView.Search(root, "factuur", CancellationToken.None);

        Assert.Equal(["factuur april.pdf", "Factuur maart.pdf"], found.Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_missing_folder_shows_a_hint_instead_of_crashing()
    {
        OnStaThread(() =>
        {
            var view = new FolderPaneView(Pane.NewFolderPane(Path.Combine(Dir, "bestaat-niet")));
            view.Navigate(view.RootFolder, force: true);
            Assert.Empty(view.Icons.VisibleItems);
        });
    }

    [Fact]
    public void A_global_hotkey_can_be_registered_and_released()
    {
        OnStaThread(() =>
        {
            using var hotkeys = new DonkiePane.Shell.GlobalHotkeys();
            // Ctrl+Alt+Shift+F13: een toets die vrijwel niemand heeft, dus vrij.
            var vk = KeyInterop.VirtualKeyFromKey(Key.F13);
            Assert.True(hotkeys.Register(99, (int)(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift), vk));
            Assert.True(hotkeys.Register(99, (int)(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift), vk)); // opnieuw: vervangt
            hotkeys.Unregister(99);
            Assert.False(hotkeys.Register(98, 0, 0)); // geen toets
        });
    }

    [Fact]
    public void The_rule_dialog_builds_and_saves_a_rule()
    {
        OnStaThread(() =>
        {
            var docs = new Pane { Title = "Documenten" };
            var rule = new FileRule { TargetPane = docs.Id };
            var window = new RuleEditWindow(rule, [new Pane { Title = "Inbox", IsInbox = true }, docs]);
            Assert.NotNull(window.Content);
        });
    }

    [Fact]
    public void The_settings_change_the_look_and_ask_to_redraw()
    {
        OnStaThread(() =>
        {
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            if (!app.Resources.Contains(typeof(CheckBox))) app.Resources[typeof(CheckBox)] = new Style(typeof(CheckBox));
            using var host = new TaskSourceHost(new DonkiePane.Tasks.Plugins.SourcesStore(Path.Combine(Dir, "sources.json")),
                new DonkiePane.Tasks.Plugins.PluginLoadResult(), id => new DpapiSecretStore(id, Path.Combine(Dir, "secrets")),
                _ => Task.CompletedTask, new DonkiePane.Tasks.LocalTaskProvider(new DonkiePane.Tasks.TaskStore(Path.Combine(Dir, "t.json"))), Path.Combine(Dir, "queue"));
            var layout = new Layout { CornerRadius = 4, RollUpOpen = RollUpOpen.Hover, PeekHotkey = "Ctrl+Alt+Space" };
            var redraws = 0;
            string? hotkey = "x";

            var window = new SettingsWindow(layout, () => { }, () => { }, host, lookChanged: () => redraws++,
                setPeekHotkey: text => { hotkey = text; return null; });

            Assert.Equal(4, ((Slider)window.FindName("CornerSlider")).Value);
            Assert.True(((RadioButton)window.FindName("RollHover")).IsChecked);
            Assert.True(((RadioButton)window.FindName("AppearanceDark")).IsChecked);
            Assert.Equal(0, redraws); // het opbouwen zelf verandert niets

            ((RadioButton)window.FindName("AppearanceLight")).IsChecked = true;
            Assert.Equal(AppTheme.Light, layout.Appearance);
            ((Slider)window.FindName("CornerSlider")).Value = 14;
            Assert.Equal(14, layout.CornerRadius);
            Assert.Equal(2, redraws);

            Assert.False(((Button)window.FindName("ApplyRulesButton")).IsEnabled); // nog geen regels
            var clear = ((StackPanel)((TextBox)window.FindName("HotkeyBox")).Parent).Children.OfType<Button>().Single();
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(hotkey);
        });
    }
}
