using System.IO;
using System.Windows;
using System.Windows.Controls;
using DonkiePane.App;

namespace DonkiePane.App.Tests;

[Collection("WPF-vensters")] // WPF heeft één Application per proces: vensters niet tegelijk vanaf verschillende threads bouwen
public sealed class WelcomeAndStartupTests
{
    [Fact]
    public void No_arguments_means_a_normal_start()
    {
        var options = StartupOptions.Parse([]);

        Assert.False(options.OpenSettings);
        Assert.False(options.Quit);
    }

    [Theory]
    [InlineData("--settings", true, false)]
    [InlineData("--SETTINGS", true, false)]
    [InlineData("--quit", false, true)]
    [InlineData("--Quit", false, true)]
    public void Known_options_are_recognised_whatever_the_case(string arg, bool settings, bool quit)
    {
        var options = StartupOptions.Parse([arg]);

        Assert.Equal(settings, options.OpenSettings);
        Assert.Equal(quit, options.Quit);
    }

    [Fact]
    public void Unknown_arguments_are_ignored_and_options_can_be_combined()
    {
        var options = StartupOptions.Parse(["--onzin", "--settings", "--quit"]);

        Assert.True(options.OpenSettings);
        Assert.True(options.Quit);
    }

    [Theory]
    [InlineData(new[] { "--restart-after", "1234" }, 1234)] // opnieuw starten (andere taal): eerst wachten op dit proces
    [InlineData(new[] { "--restart-after" }, null)]          // zonder nummer: niets
    [InlineData(new[] { "--restart-after", "nee" }, null)]
    [InlineData(new[] { "--restart-after", "-5" }, null)]
    [InlineData(new string[0], null)]
    public void Restarting_waits_for_the_previous_process(string[] args, int? expected) =>
        Assert.Equal(expected, StartupOptions.Parse(args).RestartAfter);

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
    public void The_welcome_window_builds_and_shows_the_given_choices()
    {
        OnStaThread(() =>
        {
            var window = new WelcomeWindow(hideIcons: false, startWithWindows: true);

            Assert.False(window.HideDesktopIcons);
            Assert.True(window.StartWithWindows);
            Assert.False(window.OpenSettings);
        });
    }

    [Fact]
    public void The_welcome_window_defaults_to_hiding_the_windows_icons_and_not_starting_with_windows()
    {
        OnStaThread(() =>
        {
            var window = new WelcomeWindow();

            Assert.True(window.HideDesktopIcons);
            Assert.False(window.StartWithWindows);
        });
    }

    [Fact]
    public void Changing_the_checkboxes_changes_the_choices()
    {
        OnStaThread(() =>
        {
            var window = new WelcomeWindow();

            ((CheckBox)window.FindName("HideIconsBox")).IsChecked = false;
            ((CheckBox)window.FindName("AutoStartBox")).IsChecked = true;

            Assert.False(window.HideDesktopIcons);
            Assert.True(window.StartWithWindows);
        });
    }

    [Fact]
    public void The_welcome_window_can_be_rendered_to_an_image_for_review()
    {
        OnStaThread(() =>
        {
            var window = new WelcomeWindow();
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(480, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();
            Assert.True(root.DesiredSize.Height > 200);

            var folder = Environment.GetEnvironmentVariable("DONKIEPANE_SNAPSHOTS");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x1A, 0x1A)), null, new Rect(root.DesiredSize));
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.DesiredSize.Width, (int)root.DesiredSize.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, "welkom.png"));
            encoder.Save(file);
        });
    }
}
