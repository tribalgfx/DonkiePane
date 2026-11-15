using DonkiePane.Core;

namespace DonkiePane.Tasks.Plugins;

/// <summary>Een ingestelde bron, bijvoorbeeld "Mijn dashboard" van het soort "Taken via REST".</summary>
public sealed class SourceConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TypeId { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>De niet-geheime ingevulde waarden. Geheimen staan hier nooit in.</summary>
    public Dictionary<string, string> Values { get; set; } = [];
}

public sealed class SourcesConfig
{
    public int Version { get; set; } = 1;
    public List<SourceConnection> Connections { get; set; } = [];
}

/// <summary><c>%APPDATA%\DonkiePane\sources.json</c></summary>
public sealed class SourcesStore(string filePath) : JsonFileStore<SourcesConfig>(filePath)
{
    public static string DefaultPath => AppDataPath("sources.json");
}
