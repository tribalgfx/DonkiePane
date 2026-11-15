namespace DonkiePane.Tasks;

/// <summary>
/// Een bron die zelf bijhoudt of er buiten DonkiePane iets veranderd is (bijvoorbeeld op je telefoon).
/// Lokale taken hebben dit niet nodig.
/// </summary>
public interface IRefreshableTaskProvider : ITaskProvider
{
    /// <summary>Hoe vaak er gekeken wordt. Mag tijdens het draaien veranderen; een nieuwe waarde geldt bij de eerstvolgende ronde.</summary>
    TimeSpan PollInterval { get; set; }

    /// <summary>Kijkt nu meteen of er iets veranderd is (de verversknop). Respecteert een blokkade of wachttijd van de bron.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
