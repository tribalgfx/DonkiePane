namespace DonkiePane.Core;

/// <summary>
/// Herkent een dubbelklik uit losse klikken (tijd in milliseconden, positie in pixels), op dezelfde
/// manier als Windows: de tweede klik moet snel genoeg komen en dicht genoeg bij de eerste liggen.
/// Een derde klik telt niet als nieuwe dubbelklik; die begint opnieuw.
/// </summary>
public sealed class DoubleClickDetector(int maxIntervalMs, int maxDistanceX, int maxDistanceY)
{
    private bool _hasFirst;
    private long _firstTime;
    private int _firstX, _firstY;

    /// <summary>Meld een klik. Geeft <c>true</c> als dit de tweede klik van een dubbelklik is.</summary>
    public bool Register(long timeMs, int x, int y)
    {
        if (_hasFirst &&
            timeMs - _firstTime is >= 0 and var elapsed && elapsed <= maxIntervalMs &&
            Math.Abs(x - _firstX) <= maxDistanceX && Math.Abs(y - _firstY) <= maxDistanceY)
        {
            _hasFirst = false;
            return true;
        }

        _hasFirst = true;
        _firstTime = timeMs;
        _firstX = x;
        _firstY = y;
        return false;
    }

    /// <summary>Vergeet de vorige klik (bijvoorbeeld omdat die op een andere plek viel dan een leeg bureaublad).</summary>
    public void Reset() => _hasFirst = false;
}
