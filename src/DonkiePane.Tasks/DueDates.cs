using System.Globalization;

namespace DonkiePane.Tasks;

/// <summary>Deadlines kiezen en leesbaar tonen.</summary>
public static class DueDates
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    /// <summary>De eerstvolgende maandag (nooit vandaag).</summary>
    public static DateOnly NextWeek(DateOnly today)
    {
        var days = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(days == 0 ? 7 : days);
    }

    public static bool IsOverdue(DateOnly due, DateOnly today) => due < today;

    /// <summary>"Vandaag", "Morgen", "Gisteren", een weekdag binnen een week, anders een datum.</summary>
    public static string Format(DateOnly due, DateOnly today, CultureInfo culture)
    {
        var diff = due.DayNumber - today.DayNumber;
        switch (diff)
        {
            case 0: return "Vandaag";
            case 1: return "Morgen";
            case -1: return "Gisteren";
            case > 1 and < 7:
                var day = culture.DateTimeFormat.GetDayName(due.DayOfWeek);
                return culture.TextInfo.ToUpper(day[0]) + day[1..];
            default:
                return due.ToString(due.Year == today.Year ? "ddd d MMM" : "d MMM yyyy", culture);
        }
    }
}
