using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DonkiePane.App;
using DonkiePane.Core;
using DonkiePane.Shell;
using DonkiePane.Tasks;

namespace DonkiePane.App.Tests;

/// <summary>
/// Tekent een iconen- en een takenpane buiten beeld (zonder venster op het bureaublad) op een voorbeeldachtergrond.
/// Zo wordt elke wijziging aan de vormgeving echt opgebouwd en getekend. Met de omgevingsvariabele
/// <c>DONKIEPANE_RENDER_DIR</c> komen de afbeeldingen als PNG in die map, om ze te bekijken.
/// </summary>
[Collection("WPF-vensters")]
public sealed class PaneRenderTests : TempData
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

    /// <summary>De app-brede stijlen, vers geladen op deze thread (WPF-objecten horen bij de thread die ze maakte).</summary>
    private static ResourceDictionary UseTheme()
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Alleen om te bekijken: DONKIEPANE_RENDER_LANG=en tekent de panes in het Engels (zet de taal voor dit hele testproces).
        if (Environment.GetEnvironmentVariable("DONKIEPANE_RENDER_LANG") is { Length: > 0 } language) Loc.Use(language);
        var theme = (ResourceDictionary)Application.LoadComponent(new Uri("/DonkiePane;component/Theme.xaml", UriKind.Relative));
        app.Resources.MergedDictionaries.Add(theme);
        return theme;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Haalt de inhoud uit het venster (dat nooit getoond wordt) en tekent die op een kleurige achtergrond.</summary>
    private static BitmapSource Render(PaneWindow window, double width, double height)
    {
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        PaneThemes.Apply(root.Resources, window.Pane);
        foreach (var key in window.Resources.Keys) root.Resources[key] ??= window.Resources[key];

        var wallpaper = new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0xC9, 0x7B, 0x63), 35);
        var stage = new Grid { Background = wallpaper, Width = width + 64, Height = height + 64 };
        stage.Children.Add(new Ellipse(Color.FromArgb(0x90, 0xF4, 0xD0, 0x6F), 40, 30, 180));
        stage.Children.Add(new Ellipse(Color.FromArgb(0x70, 0x6F, 0xD4, 0xC9), width - 60, height - 40, 220));
        root.Width = width;
        root.Height = height;
        root.Margin = new Thickness(32);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(root);

        var size = new Size(stage.Width, stage.Height);
        for (var i = 0; i < 3; i++)
        {
            stage.Measure(size);
            stage.Arrange(new Rect(size));
            stage.UpdateLayout();
            Pump(50);
        }

        var bitmap = new RenderTargetBitmap((int)(size.Width * 2), (int)(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        bitmap.Freeze();
        return bitmap;
    }

    private sealed class Ellipse : System.Windows.Shapes.Shape
    {
        private readonly Geometry _geometry;

        public Ellipse(Color color, double x, double y, double size)
        {
            _geometry = new EllipseGeometry(new Rect(x, y, size, size));
            Fill = new RadialGradientBrush(color, Colors.Transparent);
            IsHitTestVisible = false;
        }

        protected override Geometry DefiningGeometry => _geometry;
    }

    private static void Save(BitmapSource bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("DONKIEPANE_RENDER_DIR") is not { Length: > 0 } dir) return;
        Directory.CreateDirectory(dir);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(file);
    }

    private static List<ItemView> SampleItems()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var paths = new[]
        {
            VirtualDesktopItems.ParsingName("{645FF040-5081-101B-9F08-00AA002F954E}"),
            Path.Combine(windows, "notepad.exe"),
            Path.Combine(windows, "explorer.exe"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Path.Combine(windows, "regedit.exe"),
            Path.Combine(windows, "System32", "taskmgr.exe"),
            Path.Combine(windows, "win.ini"),
        };
        var names = new[] { null, "Kladblok", "Verkenner", null, "Register-editor met een lange naam", "Taakbeheer", "Notities 2026.txt" };
        return paths.Select((p, i) => new ItemView("test/" + i, p, names[i] ?? ShellItemInfo.GetDisplayName(p), ShellItemInfo.GetIcon(p, 96))).ToList();
    }

    [Fact]
    public void An_icons_pane_renders()
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var pane = new Pane { Title = "Programma's", Width = 352, Height = 288 };
                var window = new PaneWindow(pane);
                var view = new IconsView();
                var items = SampleItems();
                view.SetItems(items);
                view.Select([items[1].Id, items[2].Id]);
                window.SetContent(view);
                // Hoe een map/programma oplicht als je er bestanden boven houdt (in de accentkleur).
                view.Loaded += (_, _) => { };
                view.Dispatcher.InvokeAsync(() =>
                {
                    var list = (ListBox)view.FindName("ItemsHost");
                    if (list.ItemContainerGenerator.ContainerFromIndex(5) is ListBoxItem target) target.Tag = "DropTarget";
                }, DispatcherPriority.Background);

                var bitmap = Render(window, pane.Width, pane.Height);
                Save(bitmap, "icons-pane");
                Assert.True(bitmap.PixelWidth > 0);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_tasks_pane_renders(bool light)
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var provider = new LocalTaskProvider(new TaskStore(Path.Combine(Dir, "tasks.json")));
                const string list = "lijst";
                var titles = new[] { "Boodschappen doen", "Belastingaangifte afronden", "Fietsband plakken", "Verjaardagskaart voor oma sturen" };
                for (var i = 0; i < titles.Length; i++)
                {
                    provider.AddAsync(list, new TaskItem
                    {
                        Title = titles[i],
                        SortOrder = i,
                        Importance = i == 1 ? TaskImportance.High : TaskImportance.Normal,
                        DueDate = i switch { 1 => DateOnly.FromDateTime(DateTime.Today.AddDays(-1)), 2 => DateOnly.FromDateTime(DateTime.Today.AddDays(2)), _ => null },
                    }).GetAwaiter().GetResult();
                }
                provider.AddAsync(list, new TaskItem { Title = "Gras maaien", IsCompleted = true }).GetAwaiter().GetResult();

                var pane = new Pane { Kind = PaneKind.Tasks, Title = "Taken", Width = 320, Height = 352 };
                var window = new PaneWindow(pane);
                var view = new TasksView(provider, list);
                window.SetContent(view);
                var reload = view.ReloadAsync();
                while (!reload.IsCompleted) Pump(20);
                reload.GetAwaiter().GetResult();

                var bitmap = light ? RenderWithLook(window, pane.Width, pane.Height, light: true) : Render(window, pane.Width, pane.Height);
                Save(bitmap, light ? "tasks-pane-light" : "tasks-pane");
                Assert.True(bitmap.PixelWidth > 0);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Fact]
    public void The_selection_survives_a_refresh_of_the_desktop()
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var view = new IconsView();
                var items = SampleItems();
                view.SetItems(items);
                view.Select([items[1].Id, items[3].Id]);

                // Er komt een bestand bij: de lijst wordt opnieuw gezet, de selectie blijft.
                var refreshed = items.Take(5).Append(new ItemView("test/nieuw", items[6].Path, "Nieuw", items[6].Icon)).ToList();
                view.SetItems(refreshed);

                var list = (ListBox)view.FindName("ItemsHost");
                Assert.Equal([items[1].Id, items[3].Id], list.SelectedItems.OfType<ItemView>().Select(i => i.Id).Order());
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Theory]
    [InlineData(IconSize.Small)]
    [InlineData(IconSize.List)]
    public void Small_icons_and_the_list_render(IconSize size)
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var pane = new Pane { Title = "Programma's", Width = 352, Height = 288, IconSize = size };
                var window = new PaneWindow(pane);
                var view = new IconsView();
                var items = SampleItems();
                view.SetView(size, sorted: false);
                view.SetItems(items);
                view.Select([items[2].Id]);
                window.SetContent(view);

                var bitmap = Render(window, pane.Width, pane.Height);
                Save(bitmap, "icons-pane-" + size.ToString().ToLowerInvariant());
                Assert.True(bitmap.PixelWidth > 0);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(null, false)]
    [InlineData(80, false)]
    public void Own_icon_size_hidden_names_and_gray_icons_render(int? pixels, bool showNames)
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var pane = new Pane { Title = "Programma's", Width = 352, Height = 288, IconPixels = pixels, ShowNames = showNames };
                var window = new PaneWindow(pane);
                var view = new IconsView();
                var items = SampleItems();
                view.SetView(IconSize.Large, sorted: false, pixels, showNames);
                view.SetItems(items);
                view.SetDimmed([items[1].Id, items[4].Id]);
                window.SetContent(view);

                var bitmap = Render(window, pane.Width, pane.Height);
                Save(bitmap, $"icons-pane-{pixels?.ToString() ?? "standaard"}-{(showNames ? "namen" : "zonder-namen")}");

                var host = (ListBox)view.FindName("ItemsHost");
                var containers = items.Select(i => (ListBoxItem)host.ItemContainerGenerator.ContainerFromItem(i)).ToList();
                var expected = IconMetrics.TileWidth(IconSize.Large, IconMetrics.Pixels(IconSize.Large, pixels), showNames);
                var tile = (Border)containers[0].Template.FindName("Tile", containers[0]);
                var icon = (Image)containers[0].Template.FindName("Icon", containers[0]);
                var names = (FrameworkElement)containers[0].Template.FindName("NameArea", containers[0]);
                Assert.Equal(expected, tile.ActualWidth, 0.5);
                Assert.Equal(IconMetrics.Pixels(IconSize.Large, pixels), icon.ActualWidth, 0.5);
                Assert.Equal(showNames ? Visibility.Visible : Visibility.Collapsed, names.Visibility);

                Assert.Equal([false, true, false, false, true, false, false], containers.Select(IconsView.GetIsDimmed));
                var grayIcon = (Image)containers[1].Template.FindName("Icon", containers[1]);
                Assert.Same(items[1].GrayIcon, grayIcon.Source);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Fact]
    public void A_gray_icon_keeps_its_transparent_edges()
    {
        var pixels = new byte[] { 0, 0, 255, 255, /* rood, massief */ 0, 0, 0, 0 /* doorzichtig */ };
        var source = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var gray = ItemView.Grayscale(source);
        var result = new byte[8];
        gray.CopyPixels(result, 8, 0);
        Assert.Equal([76, 76, 76, 255, 0, 0, 0, 0], result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pane_with_tabs_renders_dark_and_light(bool light)
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var work = new Pane { Title = "Programma's", Width = 352, Height = 288 };
                var home = new Pane { Title = "Thuis", Theme = "groen" };
                var docs = new Pane { Title = "Documenten", Theme = "klassiek-rood" };
                var window = new PaneWindow(work) { LookSettings = () => new PaneStyle(light, 10, RollUpOpen.Button) };
                var view = new IconsView();
                var items = SampleItems();
                view.SetItems(items);
                view.Select([items[1].Id]);
                window.SetTabs([work, home, docs], work, view);

                var bitmap = RenderWithLook(window, work.Width, work.Height, light);
                Save(bitmap, light ? "tabs-pane-light" : "tabs-pane");
                Assert.True(bitmap.PixelWidth > 0);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Fact]
    public void A_folder_pane_renders()
    {
        var root = Path.Combine(Dir, "Projecten");
        Directory.CreateDirectory(Path.Combine(root, "Website"));
        Directory.CreateDirectory(Path.Combine(root, "Foto's"));
        File.WriteAllText(Path.Combine(root, "Plan van aanpak.docx"), "x");
        File.WriteAllText(Path.Combine(root, "Begroting 2027.xlsx"), "x");
        File.WriteAllText(Path.Combine(root, "notities.txt"), "x");

        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var pane = Pane.NewFolderPane(root);
                pane.Width = 352;
                pane.Height = 288;
                var window = new PaneWindow(pane);
                var view = new FolderPaneView(pane);
                window.SetContent(view);
                view.Navigate(root, force: true);
                view.Icons.Select([Path.Combine(root, "Website")]);

                var bitmap = Render(window, pane.Width, pane.Height);
                Save(bitmap, "folder-pane");
                Assert.True(bitmap.PixelWidth > 0);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    [Fact]
    public void The_mica_backdrop_is_made_from_the_wallpaper()
    {
        OnStaThread(() =>
        {
            // Zonder te lezen achtergrond (bijv. op een server) is er geen Mica; dat mag.
            if (WallpaperBackdrop.For(new PxRect(200, 150, 704, 576)) is not { } sample) return;
            Assert.InRange(sample.Viewbox.Width, 0.01, 1.5);

            var theme = UseTheme();
            try
            {
                var pane = new Pane { Title = "Mica", Width = 352, Height = 288, Material = PaneMaterial.Mica };
                var window = new PaneWindow(pane);
                var view = new IconsView();
                view.SetItems(SampleItems());
                window.SetContent(view);
                var backdrop = (Border)window.FindName("Backdrop");
                backdrop.Background = new ImageBrush(sample.Image) { Viewbox = sample.Viewbox, ViewboxUnits = BrushMappingMode.RelativeToBoundingBox, Stretch = Stretch.Fill };
                backdrop.Visibility = Visibility.Visible;
                Save(Render(window, pane.Width, pane.Height), "mica-pane");
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }

    /// <summary>Zoals <see cref="Render"/>, maar met lichte of donkere kleuren (Render past de donkere toe).</summary>
    private static BitmapSource RenderWithLook(PaneWindow window, double width, double height, bool light)
    {
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        PaneThemes.Apply(root.Resources, window.ActivePane, light);
        foreach (var key in window.Resources.Keys) root.Resources[key] ??= window.Resources[key];
        return RenderElement(root, width, height);
    }

    private static BitmapSource RenderElement(FrameworkElement root, double width, double height)
    {
        if (root.Parent is Panel panel) panel.Children.Remove(root);
        var wallpaper = new LinearGradientBrush(Color.FromRgb(0x2B, 0x4C, 0x7E), Color.FromRgb(0xC9, 0x7B, 0x63), 35);
        var stage = new Grid { Background = wallpaper, Width = width + 64, Height = height + 64 };
        root.Width = width;
        root.Height = height;
        root.Margin = new Thickness(32);
        root.HorizontalAlignment = HorizontalAlignment.Left;
        root.VerticalAlignment = VerticalAlignment.Top;
        stage.Children.Add(root);
        var size = new Size(stage.Width, stage.Height);
        for (var i = 0; i < 3; i++)
        {
            stage.Measure(size);
            stage.Arrange(new Rect(size));
            stage.UpdateLayout();
            Pump(50);
        }
        var bitmap = new RenderTargetBitmap((int)(size.Width * 2), (int)(size.Height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(stage);
        bitmap.Freeze();
        return bitmap;
    }

    [Fact]
    public void Typing_filters_the_icons_by_name_and_survives_a_refresh()
    {
        OnStaThread(() =>
        {
            var theme = UseTheme();
            try
            {
                var pane = new Pane { Title = "Programma's", Width = 352, Height = 288 };
                var window = new PaneWindow(pane);
                var view = new IconsView();
                var items = SampleItems();
                view.SetItems(items);
                window.SetContent(view);

                view.OpenFilter("KLAD");
                Assert.Equal(["Kladblok"], view.VisibleItems.Select(i => i.DisplayName));

                // Er verandert iets op het bureaublad: het filter blijft staan.
                view.SetItems(items.Take(4).ToList());
                Assert.Equal(["Kladblok"], view.VisibleItems.Select(i => i.DisplayName));

                view.OpenFilter("taak");
                var bitmap = Render(window, pane.Width, pane.Height);
                Save(bitmap, "icons-pane-filter");

                view.CloseFilter(focusIcons: false);
                Assert.Equal(4, view.VisibleItems.Count);
            }
            finally
            {
                Application.Current.Resources.MergedDictionaries.Remove(theme);
            }
        });
    }
}
