namespace DonkiePane.App;

/// <summary>De opties op de opdrachtregel van DonkiePane.exe.</summary>
public sealed record StartupOptions(bool OpenSettings, bool Quit)
{
    /// <summary>
    /// <c>--settings</c> opent Instellingen. <c>--quit</c> vraagt een draaiende DonkiePane netjes af te sluiten (dan zet die
    /// de Windows-bureaubladiconen terug) en start niets als er geen draait; de installer en het verwijderprogramma gebruiken dat.
    /// </summary>
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        var list = args.ToList();
        bool Has(string name) => list.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return new StartupOptions(Has("--settings"), Has("--quit"));
    }
}
