namespace DonkiePane.Tasks;

/// <summary>Wat een bron van taken wel en niet kan; de takenpane past zich daarop aan.</summary>
/// <param name="Importance">Kent de bron "belangrijk" (de ster)? Google Taken en REST niet.</param>
/// <param name="PaneOwnsTasks">
/// Verwijdert het verwijderen van een pane ook de taken? Bij lokale taken wel (daar
/// staan ze alleen in het pane); bij een gekoppelde bron nooit: de lijst blijft daar staan.
/// </param>
/// <param name="Reorder">Kunnen taken verplaatst worden? Zo niet, dan sorteert DonkiePane zelf en is slepen uit.</param>
public sealed record TaskProviderCapabilities(bool Importance, bool PaneOwnsTasks, bool Reorder = true);

/// <summary>Verbinding met de bron, bijvoorbeeld "geen verbinding". <see cref="Message"/> is <c>null</c> als alles goed gaat.</summary>
public sealed record TaskProviderStatus(string ListId, bool IsOnline, string? Message);

/// <summary>Een fout die de gebruiker te zien krijgt (geen verbinding, niet ingelogd, ...).</summary>
public class TaskProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// De bron weigert het token of de inloggegevens. Niet herhalen: sommige bronnen blokkeren na een
/// paar mislukte pogingen alles, ook met een geldig token. De gebruiker moet het eerst verbeteren.
/// </summary>
public class SourceUnauthorizedException(string message) : TaskProviderException(message);

/// <summary>De bron vraagt om even te wachten. Respecteer <see cref="RetryAfter"/> en probeer niet eerder opnieuw.</summary>
public sealed class SourceRateLimitedException(TimeSpan retryAfter)
    : TaskProviderException($"De bron vraagt om even te wachten ({(int)Math.Ceiling(retryAfter.TotalMinutes)} min). DonkiePane probeert het daarna vanzelf opnieuw.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Het laatste vangnet voor fouten uit een bron of plug-in. Een gewone <see cref="TaskProviderException"/> is al
/// een melding voor de gebruiker; elke andere fout (een fout in een plug-in, een vol of vergrendeld bestand) mag de
/// app niet laten crashen, maar wordt ook een melding.
/// </summary>
public static class TaskErrors
{
    /// <summary>Annuleren (afsluiten, venster dicht) is geen fout en wordt nooit gevangen.</summary>
    public static bool IsHandled(Exception ex) => ex is not OperationCanceledException;

    public static string Message(Exception ex) =>
        ex is TaskProviderException ? ex.Message : $"Er ging iets mis: {ex.Message}";
}

/// <summary>
/// De bron hapert even: een serverfout (5xx) of een antwoord dat DonkiePane niet kan lezen, zoals de inlogpagina van
/// een proxy. Later opnieuw proberen helpt; offline wijzigingen blijven daarom bewaard.
/// </summary>
public class SourceUnavailableException(string message, Exception? inner = null) : TaskProviderException(message, inner)
{
    public const string UnreadableMessage = "De bron gaf een antwoord dat DonkiePane niet kan lezen. Staat het adres goed, en zit er geen inlogpagina tussen?";

    /// <summary>Een fout bij het lezen van een antwoord (JSON van de verkeerde vorm, onmogelijke waarden).</summary>
    public static bool IsUnreadable(Exception ex) =>
        ex is System.Text.Json.JsonException or InvalidOperationException or FormatException
            or ArgumentException or KeyNotFoundException or NullReferenceException;
}

