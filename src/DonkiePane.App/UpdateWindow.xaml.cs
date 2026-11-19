using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using DonkiePane.Core.Updates;

namespace DonkiePane.App;

/// <summary>
/// Er is een nieuwe versie: wat er nieuw is, en op verzoek downloaden, controleren en installeren. Zonder klik op
/// "Nu bijwerken" gebeurt er niets.
/// </summary>
public partial class UpdateWindow : Window
{
    private readonly ReleaseInfo _release;
    private readonly Func<ReleaseInfo, IProgress<double>, CancellationToken, Task<string>> _download;
    private readonly Action<string> _launch;
    private CancellationTokenSource? _busy;

    internal UpdateWindow(ReleaseInfo release, Version current,
        Func<ReleaseInfo, IProgress<double>, CancellationToken, Task<string>> download, Action<string> launch)
    {
        InitializeComponent();
        _release = release;
        _download = download;
        _launch = launch;

        Headline.Text = $"DonkiePane {AppVersion.Format(release.Version)} is er";
        Subline.Text = $"Je hebt nu versie {AppVersion.Format(current)}. Bijwerken duurt even: DonkiePane sluit af, " +
            "de nieuwe versie wordt geïnstalleerd en start daarna vanzelf. Je panes, taken en instellingen blijven staan.";
        Notes.Text = PlainNotes(release.Notes) is { Length: > 0 } notes ? notes : "Bij deze versie staan geen opmerkingen.";

        if (release.Installer?.Sha256 is null)
        {
            InstallButton.IsEnabled = false;
            ShowStatus(release.Installer is null
                ? "Bij deze versie staat geen installer. Kijk op de releasepagina."
                : "Bij deze installer staat geen controlegetal, dus DonkiePane installeert hem niet zelf. Download hem via de releasepagina.");
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        ShowStatus("Downloaden…");
        _busy = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(p =>
            {
                Progress.Value = p;
                ShowStatus($"Downloaden… {p:P0}");
            });
            var path = await _download(_release, progress, _busy.Token);
            _busy.Token.ThrowIfCancellationRequested(); // intussen op Later geklikt of DonkiePane sluit af: niet starten
            ShowStatus("Gecontroleerd. De installer start; DonkiePane sluit zo even af.");
            _launch(path);
            LaterButton.Content = "Sluiten";
        }
        catch (OperationCanceledException)
        {
            // Venster gesloten tijdens het downloaden.
        }
        catch (Exception ex) when (ex is UpdateException or Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            Progress.Visibility = Visibility.Collapsed;
            ShowStatus(ex is Win32Exception ? "De installer kon niet starten: " + ex.Message : ex.Message);
            InstallButton.IsEnabled = _release.Installer?.Sha256 is not null;
        }
        finally
        {
            _busy?.Dispose();
            _busy = null;
        }
    }

    private void Page_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_release.Page.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowStatus("Kon de browser niet openen. De pagina is " + _release.Page.AbsoluteUri);
        }
    }

    private void Later_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        _busy?.Cancel();
        base.OnClosing(e);
    }

    private void ShowStatus(string text)
    {
        Status.Text = text;
        Status.Visibility = Visibility.Visible;
    }

    /// <summary>De release-opmerkingen zijn Markdown; hier zonder sterretjes, backticks en linkadressen.</summary>
    internal static string PlainNotes(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");       // [tekst](adres) → tekst
        text = Regex.Replace(text, @"^\s{0,3}#{1,6}\s*", "", RegexOptions.Multiline); // koppen
        text = Regex.Replace(text, @"^(\s*)[-*]\s+", "$1• ", RegexOptions.Multiline);  // opsommingen
        text = text.Replace("**", "").Replace("__", "").Replace("`", "");
        text = Regex.Replace(text, @"(?<![\w*])\*(?!\s)([^*\n]+?)\*(?!\w)", "$1");    // *schuin*
        return Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
    }
}
