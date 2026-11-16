using System.Windows;
using DonkiePane.Core;

namespace DonkiePane.App;

/// <summary>Eerste start en netjes afsluiten op verzoek (voor de installer).</summary>
public partial class App
{
    /// <summary>Naam van het signaal waarmee een tweede <c>DonkiePane.exe --quit</c> de draaiende instantie vraagt te stoppen.</summary>
    internal const string QuitEventName = @"Local\DonkiePane.Quit";

    private EventWaitHandle? _quitEvent;

    // ---------- Eerste start ----------

    /// <summary>
    /// Toont bij de allereerste start het welkomstscherm en past de keuzes toe. Geeft <c>true</c> als de gebruiker
    /// meteen een takenbron wil toevoegen.
    /// </summary>
    private bool ShowWelcomeIfFirstRun()
    {
        if (_layout.WelcomeShown != false) return false;

        var welcome = new WelcomeWindow(hideIcons: _layout.HideDesktopIcons, startWithWindows: AutoStart.IsEnabled());
        welcome.ShowDialog();

        // Ook sluiten met het kruisje telt: de standaardkeuzes gelden dan, en het scherm komt niet steeds terug.
        _layout.HideDesktopIcons = welcome.HideDesktopIcons;
        _layout.WelcomeShown = true;
        SaveLayout();
        try
        {
            AutoStart.SetEnabled(welcome.StartWithWindows);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            MessageBox.Show(ex.Message, "DonkiePane", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return welcome.OpenSettings;
    }

    // ---------- Netjes afsluiten op verzoek ----------

    /// <summary>De draaiende instantie luistert naar het signaal van <c>--quit</c> en sluit dan op de gewone manier af.</summary>
    private void ListenForQuitRequest()
    {
        _quitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        var thread = new Thread(() =>
        {
            _quitEvent.WaitOne();
            Dispatcher.InvokeAsync(Shutdown); // OnExit zet de Windows-iconen terug
        })
        { IsBackground = true, Name = "DonkiePane.QuitListener" };
        thread.Start();
    }

    private static void RequestRunningInstanceToQuit()
    {
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        quit.Set();
    }
}
