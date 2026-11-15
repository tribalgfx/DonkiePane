using System.Text.Json.Serialization;
using DonkiePane.Core;

namespace DonkiePane.Tasks.Google;

/// <summary>
/// Het OAuth-client van DonkiePane in het Google Cloud-project (type "Desktop app").
/// Bij een desktop-app is het secret niet echt geheim (Google zegt dat zelf), maar
/// het blijft toch buiten git: het staat in <c>%APPDATA%\DonkiePane\google.json</c>.
/// </summary>
public sealed record GoogleClientConfig(string ClientId, string? ClientSecret)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>Bewaart het langlevende inlogtoken (refresh token). In de app versleuteld met DPAPI.</summary>
public interface ITokenStore
{
    string? Load();
    void Save(string refreshToken);
    void Delete();
}

public sealed record GoogleTaskList(string Id, string Title);

/// <summary>Een taak zoals de Google Tasks-API hem teruggeeft (alleen de velden die we gebruiken).</summary>
public sealed class GoogleTaskDto
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("etag")] public string? Etag { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("due")] public string? Due { get; set; }
    [JsonPropertyName("completed")] public string? Completed { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("updated")] public string? Updated { get; set; }
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("deleted")] public bool? Deleted { get; set; }
}

/// <summary>Instellingen voor Google Taken.</summary>
public sealed class GoogleSettings
{
    public string ClientId { get; set; } = "";
    public string? ClientSecret { get; set; }

    public GoogleClientConfig? ToConfig() =>
        string.IsNullOrWhiteSpace(ClientId)
            ? null
            : new GoogleClientConfig(ClientId.Trim(), string.IsNullOrWhiteSpace(ClientSecret) ? null : ClientSecret.Trim());
}

/// <summary><c>%APPDATA%\DonkiePane\google.json</c></summary>
public sealed class GoogleSettingsStore(string filePath) : JsonFileStore<GoogleSettings>(filePath)
{
    public static string DefaultPath => AppDataPath("google.json");
}
