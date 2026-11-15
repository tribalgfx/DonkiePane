namespace DonkiePane.Core;

/// <summary>
/// Een bureaubladitem wordt aangeduid met de map waar het staat plus de
/// bestandsnaam, bijvoorbeeld <c>user/Notities.txt</c> of <c>public/Firefox.lnk</c>.
/// Zo kan een bestand met dezelfde naam op beide bureaubladen bestaan.
/// </summary>
public static class DesktopItemId
{
    public const string User = "user";
    public const string Public = "public";

    /// <summary>Systeemiconen zonder bestand (Prullenbak, Deze pc); de naam is een CLSID.</summary>
    public const string Shell = "shell";

    public static string Create(string location, string fileName) => $"{location}/{fileName}";

    public static (string Location, string FileName) Parse(string id)
    {
        var slash = id.IndexOf('/');
        if (slash <= 0) throw new FormatException($"Ongeldig item-id: {id}");
        return (id[..slash], id[(slash + 1)..]);
    }
}
