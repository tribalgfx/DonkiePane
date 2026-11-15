namespace DonkiePane.Tasks;

/// <summary>
/// De klok achter "elke minuut kijken of er iets veranderd is". Wacht in stukjes van een seconde en leest het
/// interval elke keer opnieuw, zodat een nieuw gekozen interval meteen geldt en niet pas na de lange wachttijd
/// van de vorige instelling.
/// </summary>
public static class PollLoop
{
    private static readonly TimeSpan Slice = TimeSpan.FromSeconds(1);

    /// <summary>Roept <paramref name="poll"/> steeds aan na <paramref name="interval"/> tot <paramref name="ct"/> stopt. Stopt zonder fout.</summary>
    public static async Task RunAsync(Func<TimeSpan> interval, Func<CancellationToken, Task> poll, TimeProvider? time, CancellationToken ct)
    {
        time ??= TimeProvider.System;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var waited = TimeSpan.Zero;
                while (waited < interval())
                {
                    await Task.Delay(Slice, time, ct);
                    waited += Slice;
                }
                await poll(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Afsluiten.
        }
    }
}
