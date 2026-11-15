namespace DonkiePane.Tasks;

/// <summary>
/// De regels voor wanneer een bron aangesproken mag worden. Na een geweigerd token (401) gaat er niets meer heen tot
/// de gebruiker de instellingen heeft verbeterd, want een bron kan na een paar mislukte pogingen alles blokkeren, ook met
/// een geldig token. Vraagt de bron om te wachten (429, of een herstart), dan gaat er niets heen tot die tijd om is.
/// </summary>
public sealed class SourceGate(TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly object _lock = new();
    private string? _haltedMessage;
    private DateTimeOffset _pausedUntil;

    /// <summary>Gooit als er nu niets naar de bron mag; zo kost een geweigerde aanvraag geen enkel verzoek.</summary>
    public void ThrowIfClosed()
    {
        lock (_lock)
        {
            if (_haltedMessage is not null) throw new SourceUnauthorizedException(_haltedMessage);
            var now = _time.GetUtcNow();
            if (now < _pausedUntil) throw new SourceRateLimitedException(_pausedUntil - now);
        }
    }

    /// <summary>Onthoudt wat een fout van de bron voor de volgende verzoeken betekent.</summary>
    public void Remember(TaskProviderException ex)
    {
        lock (_lock)
        {
            switch (ex)
            {
                case SourceUnauthorizedException unauthorized:
                    _haltedMessage = unauthorized.Message;
                    break;
                case SourceRateLimitedException limited:
                    _pausedUntil = _time.GetUtcNow() + limited.RetryAfter;
                    break;
            }
        }
    }

    /// <summary>Bij een gewone fout (bijv. 403) is er wel verbinding; alleen "geen verbinding" telt als offline.</summary>
    public static bool IsReachable(TaskProviderException ex) =>
        ex.InnerException is not HttpRequestException and not TaskCanceledException;
}
