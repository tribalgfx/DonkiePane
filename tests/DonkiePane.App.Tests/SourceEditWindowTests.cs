using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DonkiePane.App;
using DonkiePane.Plugins.Google;
using DonkiePane.Plugins.RestTasks;
using DonkiePane.Tasks.Plugins;

namespace DonkiePane.App.Tests;

/// <summary>
/// Het instellingenvenster van een bron is volledig in code opgebouwd. Deze tests bouwen het echt op (zonder
/// het te tonen) met de velden van de echte plug-ins, zodat een fout hier niet pas bij het klikken op
/// "Toevoegen" naar boven komt. Zet <c>DONKIEPANE_SNAPSHOTS</c> op een map om er een afbeelding van te laten maken.
/// </summary>
public sealed class SourceEditWindowTests : TempData
{
    /// <summary>WPF-schermonderdelen moeten op een STA-thread; fouten daar komen terug naar de test.</summary>
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
        if (error is not null) throw new Exception("Fout op de UI-thread: " + error.Message, error);
    }

    private TaskSourceHost NewHost() =>
        new(new SourcesStore(SourcesPath), new PluginLoadResult(), id => new DpapiSecretStore(id, SecretsDir), _ => Task.CompletedTask, new FakeLocal());

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private void Render(ITaskSourcePlugin plugin, string name)
    {
        OnStaThread(() =>
        {
            using var host = NewHost();
            var window = new SourceEditWindow(host, host.NewConnection(plugin.TypeId, plugin.DisplayName), plugin);

            var scroll = Assert.IsType<ScrollViewer>(window.Content);
            var root = Assert.IsAssignableFrom<FrameworkElement>(scroll.Content);
            root.Measure(new Size(480, double.PositiveInfinity));
            root.Arrange(new Rect(root.DesiredSize));
            root.UpdateLayout();

            // Een veld per instelling, een knop per actie plus Opslaan, Verbinding testen en Sluiten.
            var inputs = Descendants<TextBox>(root).Count() + Descendants<PasswordBox>(root).Count();
            Assert.Equal(plugin.Fields.Count + 1, inputs); // + de naam
            Assert.Equal(plugin.Actions.Count + 3, Descendants<Button>(root).Count());
            Assert.Equal(plugin.Fields.Count(f => f.Kind == SettingKind.Secret), Descendants<PasswordBox>(root).Count());

            var folder = Environment.GetEnvironmentVariable("DONKIEPANE_SNAPSHOTS");
            if (string.IsNullOrEmpty(folder)) return;
            Directory.CreateDirectory(folder);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x20, 0x1A, 0x1A)), null, new Rect(root.DesiredSize));
            var bitmap = new RenderTargetBitmap((int)root.DesiredSize.Width, (int)root.DesiredSize.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        });
    }

    [Fact]
    public void The_window_for_the_rest_source_builds_with_its_real_fields() => Render(new RestTasksPlugin(), "bron-rest");

    [Fact]
    public void The_window_for_the_google_source_builds_with_its_real_fields_and_buttons() => Render(new GoogleSourcePlugin(), "bron-google");

    [Fact]
    public void A_fake_plugin_without_buttons_builds_too() => Render(new FakePlugin(), "bron-proef");
}
