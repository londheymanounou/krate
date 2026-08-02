using System.Globalization;

namespace Krate.Core;

/// <summary>Describes a 5-field cron expression in words. Not a scheduler — it explains what a
/// line means, which is the thing people actually get wrong.</summary>
public static class Cron
{
    // Month and weekday names come from the active culture — free localization, no hardcoded lists.
    static string[] Months => DateTimeFormatInfo.GetInstance(Strings.Culture).MonthNames;   // [0]=Jan … [11]=Dec, [12]=""
    static string[] Days => DateTimeFormatInfo.GetInstance(Strings.Culture).DayNames;         // [0]=Sunday … [6]=Saturday

    public static string Describe(string input)
    {
        var fields = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Named shortcuts save people looking up the field order.
        if (fields is [var shortcut] && shortcut.StartsWith('@'))
            fields = Shortcut(shortcut).Split(' ');
        if (fields.Length != 5) throw new ArgumentException(Strings.Get("Error_CronUsage"));

        var (minute, hour, dom, month, dow) = (fields[0], fields[1], fields[2], fields[3], fields[4]);
        var parts = new List<string>();

        // Time of day: an exact minute+hour reads as a clock time; anything else is described per field.
        if (IsSingle(minute, out var m) && IsSingle(hour, out var h))
            parts.Add(Strings.Get("Cron_AtTime", $"{h:00}:{m:00}"));
        else
        {
            parts.Add(DescribeField(minute, 0, 59, Strings.Get("Cron_Minute")));
            // A "*" hour adds nothing next to a minute rule ("every 15 minutes, every hour" is noise).
            if (hour != "*") parts.Add(DescribeField(hour, 0, 23, Strings.Get("Cron_Hour")));
        }

        if (dom != "*") parts.Add(DescribeField(dom, 1, 31, Strings.Get("Cron_DayOfMonth")));
        if (month != "*") parts.Add(Strings.Get("Cron_InMonths", Named(month, i => Months[Math.Clamp(i - 1, 0, 11)])));
        if (dow != "*") parts.Add(Strings.Get("Cron_OnDays", Named(dow, i => Days[i == 7 ? 0 : Math.Clamp(i, 0, 6)])));

        return string.Join(", ", parts);
    }

    /// <summary>The next <paramref name="count"/> times the expression fires, at or after <paramref name="from"/>.</summary>
    public static List<DateTime> NextRuns(string expr, int count, DateTime from)
    {
        var fields = expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields is [var shortcut] && shortcut.StartsWith('@')) fields = Shortcut(shortcut).Split(' ');
        if (fields.Length != 5) throw new ArgumentException(Strings.Get("Error_CronUsage"));

        var minute = Expand(fields[0], 0, 59);
        var hour = Expand(fields[1], 0, 23);
        var dom = Expand(fields[2], 1, 31);
        var month = Expand(fields[3], 1, 12);
        var dowRaw = Expand(fields[4], 0, 7);
        var dow = new bool[7];
        for (var i = 0; i <= 7; i++) if (dowRaw[i]) dow[i % 7] = true; // 0 and 7 both mean Sunday
        var (domRestricted, dowRestricted) = (fields[2] != "*", fields[4] != "*");

        var results = new List<DateTime>();
        var t = from.AddSeconds(-from.Second).AddMilliseconds(-from.Millisecond).AddMinutes(1);
        var limit = t.AddYears(5); // guard against an impossible expression (e.g. 30 Feb)
        while (results.Count < count && t < limit)
        {
            if (minute[t.Minute] && hour[t.Hour] && month[t.Month])
            {
                var domMatch = dom[t.Day];
                var dowMatch = dow[(int)t.DayOfWeek];
                // Standard cron: when both day fields are restricted it's OR; otherwise AND (a * field is always true).
                if (domRestricted && dowRestricted ? domMatch || dowMatch : domMatch && dowMatch)
                    results.Add(t);
            }
            t = t.AddMinutes(1);
        }
        return results;
    }

    /// <summary>Expands one cron field ("*", "*/5", "1-3", "1,4", "7") into a match set over [min, max].</summary>
    static bool[] Expand(string field, int min, int max)
    {
        var set = new bool[max + 1];
        foreach (var part in field.Split(','))
        {
            var (range, step) = part.Contains('/') ? (part[..part.IndexOf('/')], int.Parse(part[(part.IndexOf('/') + 1)..])) : (part, 1);
            var (lo, hi) = range == "*" ? (min, max)
                : range.Contains('-') ? (int.Parse(range[..range.IndexOf('-')]), int.Parse(range[(range.IndexOf('-') + 1)..]))
                : (int.Parse(range), int.Parse(range));
            for (var v = lo; v <= hi; v += step) if (v >= min && v <= max) set[v] = true;
        }
        return set;
    }

    static string Shortcut(string s) => s.ToLowerInvariant() switch
    {
        "@yearly" or "@annually" => "0 0 1 1 *",
        "@monthly" => "0 0 1 * *",
        "@weekly" => "0 0 * * 0",
        "@daily" or "@midnight" => "0 0 * * *",
        "@hourly" => "0 * * * *",
        _ => throw new ArgumentException(Strings.Get("Error_CronUsage")),
    };

    static bool IsSingle(string field, out int value) =>
        int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    static string DescribeField(string field, int min, int max, string unit)
    {
        if (field == "*") return Strings.Get("Cron_EveryUnit", unit);
        if (field.StartsWith("*/") && int.TryParse(field[2..], out var step))
            return Strings.Get("Cron_EveryN", step, unit);
        if (field.Contains('-') && !field.Contains(','))
        {
            var range = field.Split('-');
            return Strings.Get("Cron_Range", unit, range[0], range[1]);
        }
        if (field.Contains(','))
            return Strings.Get("Cron_List", unit, field.Replace(",", ", "));
        return Strings.Get("Cron_At", unit, field);
    }

    /// <summary>Renders a month/day field using names (January, Monday…) instead of numbers.</summary>
    static string Named(string field, Func<int, string> nameOf)
    {
        if (field.StartsWith("*/") && int.TryParse(field[2..], out var step))
            return Strings.Get("Cron_EveryNBare", step);
        string Name(string n) => int.TryParse(n, out var i) ? nameOf(i) : n;

        if (field.Contains('-'))
        {
            var r = field.Split('-');
            return $"{Name(r[0])}–{Name(r[1])}";
        }
        return string.Join(", ", field.Split(',').Select(Name));
    }
}
