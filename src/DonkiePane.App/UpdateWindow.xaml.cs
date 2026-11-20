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

        Headline.Text = Loc.F("DonkiePane {0} is er", AppVersion.Format(release.Version));
        Subline.Text = Loc.F("Je hebt nu versie {0}. Bijwerken duurt even: DonkiePane sluit af, de nieuwe versie wordt geïnstalleerd en start daarna vanzelf. Je panes, taken en instellingen blijven staan.", AppVersion.Format(current));
        Notes.Text = PlainNotes(release.Notes) is { Length: > 0 } notes ? notes : Loc.T("Bij deze versie staan geen opmerkingen.");

        if (release.Installer?.Sha256 is null)
        {
            InstallButton.IsEnabled = false;
            ShowStatus(release.Installer is null
                ? Loc.T("Bij deze versie staat geen installer. Kijk op de releasepagina.")
                : Loc.T("Bij deze installer staat geen controlegetal, dus DonkiePane installeert hem niet zelf. Download hem via de releasepagina."));
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        Progress.Value = 0;
        Progress.Visibility = Visibility.Visible;
        ShowStatus(Loc.T("Downloaden…"));
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
            ShowStatus(Loc.T("Gecontroleerd. De installer start; DonkiePane sluit zo even af."));
            _launch(path);
            LaterButton.Content = Loc.T("Sluiten");
        }
        catch (OperationCanceledException)
        {
            // Venster gesloten tijdens het downloaden.
        }
        catch (Exception ex) when (ex is UpdateException or Win32Exception or System.IO.IOException or UnauthorizedAccessException)
        {
            Progress.Visibility = Visibility.Collapsed;
            ShowStatus(ex is Win32Exception ? Loc.F("De installer kon niet starten: {0}", ex.Message) : ex.Message);
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
            ShowStatus(Loc.F("Kon de browser niet openen. De pagina is {0}", _release.Page.AbsoluteUri));
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
