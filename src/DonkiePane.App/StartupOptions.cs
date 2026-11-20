namespace DonkiePane.App;

/// <summary>De opties op de opdrachtregel van DonkiePane.exe.</summary>
public sealed record StartupOptions(bool OpenSettings, bool Quit, int? RestartAfter = null)
{
    /// <summary>
    /// <c>--settings</c> opent Instellingen. <c>--quit</c> vraagt een draaiende DonkiePane netjes af te sluiten (dan zet die
    /// de Windows-bureaubladiconen terug) en start niets als er geen draait; de installer en het verwijderprogramma gebruiken dat.
    /// <c>--restart-after &lt;proces-id&gt;</c> wacht eerst tot dat proces weg is (opnieuw starten vanuit Instellingen).
    /// </summary>
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var list = args.ToList();
        bool Has(string name) => list.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        var restart = list.FindIndex(a => string.Equals(a, "--restart-after", StringComparison.OrdinalIgnoreCase));
        int? restartAfter = restart >= 0 && restart + 1 < list.Count && int.TryParse(list[restart + 1], out var pid) && pid > 0 ? pid : null;
        return new StartupOptions(Has("--settings"), Has("--quit"), restartAfter);
    }
}
