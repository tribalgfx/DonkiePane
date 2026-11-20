using System.Text.Json.Serialization;
using DonkiePane.Tasks;

namespace DonkiePane.Plugins.Google;

/// <summary>
/// De OAuth-client van de gebruiker in het eigen Google Cloud-project (type "Desktop app").
/// Bij een desktop-app is het secret niet echt geheim (Google zegt dat zelf), maar het blijft
/// toch in de versleutelde geheimenopslag, niet in een instellingenbestand.
/// </summary>
public sealed record GoogleClientConfig(string ClientId, string? ClientSecret)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>Bewaart het langlevende inlogtoken (refresh token) van Google.</summary>
public interface ITokenStore
{
    string? Load();
    void Save(string refreshToken);
    void Delete();
}

public sealed class NotSignedInException() : SourceUnauthorizedException(Loc.T("Niet ingelogd bij Google. Open Instellingen, kies deze bron en log in."));

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
