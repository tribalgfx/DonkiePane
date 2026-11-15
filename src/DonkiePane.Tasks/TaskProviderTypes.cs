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
public sealed class SourceUnauthorizedException(string message) : TaskProviderException(message);

/// <summary>De bron vraagt om even te wachten. Respecteer <see cref="RetryAfter"/> en probeer niet eerder opnieuw.</summary>
public sealed class SourceRateLimitedException(TimeSpan retryAfter)
    : TaskProviderException($"De bron vraagt om even te wachten ({(int)Math.Ceiling(retryAfter.TotalMinutes)} min). DonkiePane probeert het daarna vanzelf opnieuw.")
{
    public TimeSpan RetryAfter { get; } = retryAfter;
}

