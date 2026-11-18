namespace DonkiePane.Core.Updates;

/// <summary>Versienummers zoals <c>0.1.0</c>, <c>v0.2.0</c> of <c>0.1.0+abc123</c> lezen en vergelijken.</summary>
public static class AppVersion
{
    /// <summary>
    /// Leest een versie. Een voorloop-<c>v</c> en bouwgegevens na <c>+</c> tellen niet mee. Een voorlopige versie
    /// (<c>0.2.0-beta</c>) geeft <c>null</c>: die wordt nooit als update aangeboden.
    /// </summary>
    public static Version? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var plus = value.IndexOf('+');
        if (plus >= 0) value = value[..plus];
        if (value.Contains('-')) return null;
        if (!Version.TryParse(value, out var version)) return null;

        // 0.2 en 0.2.0 zijn dezelfde versie; Version vindt van niet (ontbrekende delen zijn -1).
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>Is <paramref name="candidate"/> nieuwer dan <paramref name="current"/>?</summary>
    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    /// <summary>Korte weergave: <c>0.2.0</c> (de vierde positie alleen als die er echt is).</summary>
    public static string Format(Version version) =>
        version.Revision > 0 ? version.ToString(4) : version.ToString(3);

    private static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
