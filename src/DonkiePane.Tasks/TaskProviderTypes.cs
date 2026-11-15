namespace DonkiePane.Tasks;

/// <summary>Wat een bron van taken wel en niet kan; de takenpane past zich daarop aan.</summary>
/// <param name="Importance">Kent de bron "belangrijk" (de ster)? Google Taken niet.</param>
/// <param name="PaneOwnsTasks">
/// Verwijdert het verwijderen van een pane ook de taken? Bij lokale taken wel (daar
/// staan ze alleen in het pane); bij Google Taken nooit: de lijst blijft bij Google staan.
/// </param>
public sealed record TaskProviderCapabilities(bool Importance, bool PaneOwnsTasks);

/// <summary>Verbinding met de bron, bijvoorbeeld "geen verbinding". <see cref="Message"/> is <c>null</c> als alles goed gaat.</summary>
public sealed record TaskProviderStatus(string ListId, bool IsOnline, string? Message);

/// <summary>Een fout die de gebruiker te zien krijgt (geen verbinding, niet ingelogd, ...).</summary>
public class TaskProviderException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class NotSignedInException() : TaskProviderException("Niet ingelogd bij Google. Open Instellingen en log in.");
