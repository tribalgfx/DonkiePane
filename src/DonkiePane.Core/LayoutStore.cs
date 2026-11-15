namespace DonkiePane.Core;

/// <summary>De layout van alle panes, in <c>%APPDATA%\DonkiePane\layout.json</c>.</summary>
public sealed class LayoutStore(string filePath) : JsonFileStore<Layout>(filePath)
{
    public static string DefaultPath => AppDataPath("layout.json");
}
