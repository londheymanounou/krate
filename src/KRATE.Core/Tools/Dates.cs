using System.Globalization;

namespace Krate.Core;

public static class Dates
{
    /// <summary>Empty → now. A number → the date it encodes (seconds or milliseconds).
    /// A date → its Unix timestamp. One box, both directions.</summary>
    public static string Timestamp(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) return Describe(DateTimeOffset.UtcNow);
        if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            // 10 digits is seconds, 13 is milliseconds — the usual ambiguity, resolved by magnitude.
            return Describe(Math.Abs(n) > 100_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(n)
                : DateTimeOffset.FromUnixTimeSeconds(n));
        return Describe(ParseDate(s));
    }

    static string Describe(DateTimeOffset d) => string.Join('\n',
        $"UNIX   {d.ToUnixTimeSeconds()}",
        $"MS     {d.ToUnixTimeMilliseconds()}",
        $"ISO    {d.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}",
        $"LOCAL  {d.ToLocalTime().ToString("F", Strings.Culture)}");

    /// <summary>"2020-01-01 2024-03-05", or one date to compare against today.
    /// Also answers "how old am I" — same question, same maths.</summary>
    public static string Difference(string input)
    {
        var parts = input.Split([' ', '\n', '\t', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedDate"));
        var a = ParseDate(parts[0]).Date;
        var b = parts.Length > 1 ? ParseDate(parts[1]).Date : DateTime.Today;
        if (b < a) (a, b) = (b, a);

        // Calendar-correct: a month is what the calendar says, not 30 days. Walking the calendar
        // forward is slower than arithmetic on the parts and immune to the 31 Jan → 1 Mar traps.
        var years = 0;
        while (a.AddYears(years + 1) <= b) years++;
        var months = 0;
        while (a.AddYears(years).AddMonths(months + 1) <= b) months++;
        var days = (b - a.AddYears(years).AddMonths(months)).Days;

        var total = (b - a).Days;
        return string.Join('\n',
            Strings.Get("Dates_Exact", years, months, days),
            Strings.Get("Dates_TotalDays", total),
            Strings.Get("Dates_TotalWeeks", total / 7, total % 7),
            Strings.Get("Dates_BusinessDays", BusinessDays(a, b)));
    }

    /// <summary>Weekdays between two dates, end date excluded. Public holidays are not
    /// counted — they differ per country and would need a data table per locale.</summary>
    // ponytail: no holiday calendar. Add one per country if users ask.
    public static int BusinessDays(DateTime a, DateTime b)
    {
        var days = 0;
        for (var d = a; d < b; d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) days++;
        return days;
    }

    /// <summary>Time units and how many seconds each is. A "light-year" is the time light takes to cross
    /// one light-year — by definition one year — so it belongs here as a (fun, exact) time unit.</summary>
    public static readonly (string Key, string Label, string[] Aliases, double Seconds)[] TimeUnits =
    [
        ("ms", "milliseconds", ["ms", "milli", "millis", "millisecond", "milliseconds"], 0.001),
        ("s", "seconds", ["s", "sec", "secs", "second", "seconds"], 1),
        ("min", "minutes", ["m", "min", "mins", "minute", "minutes"], 60),
        ("h", "hours", ["h", "hr", "hrs", "hour", "hours"], 3600),
        ("day", "days", ["d", "day", "days"], 86400),
        ("week", "weeks", ["w", "wk", "wks", "week", "weeks"], 604800),
        ("month", "months", ["mo", "mon", "month", "months"], 2629800),        // average month = year / 12
        ("year", "years", ["y", "yr", "yrs", "year", "years"], 31557600),      // Julian year, 365.25 days
        ("decade", "decades", ["decade", "decades"], 315576000),
        ("century", "centuries", ["century", "centuries"], 3155760000),
        ("lightyear", "light-years", ["ly", "lightyear", "lightyears", "light-year", "light-years"], 31557600),
    ];

    static bool TryUnit(string token, out double seconds, out string label)
    {
        foreach (var u in TimeUnits)
            if (u.Aliases.Contains(token, StringComparer.OrdinalIgnoreCase)) { (seconds, label) = (u.Seconds, u.Label); return true; }
        (seconds, label) = (0, "");
        return false;
    }

    /// <summary>Parses a duration: a bare number of seconds ("90000"), unit tokens ("1d 2h 30m",
    /// "1.5h", "500ms"), or a clock ("2:30:00" = h:m:s, "90:00" = m:s).</summary>
    public static double ParseDuration(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedNumber"));

        if (s.Contains(':'))
        {
            var p = s.Split(':').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            return p.Length switch
            {
                3 => p[0] * 3600 + p[1] * 60 + p[2],
                2 => p[0] * 60 + p[1],
                _ => throw new ArgumentException(Strings.Get("Error_DurationUsage")),
            };
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(s, @"(\d+\.?\d*)\s*([a-z-]+)");
        if (matches.Count > 0)
        {
            double total = 0;
            foreach (System.Text.RegularExpressions.Match mt in matches)
            {
                if (!TryUnit(mt.Groups[2].Value, out var us, out _)) throw new ArgumentException(Strings.Get("Error_UnknownUnit", mt.Groups[2].Value));
                total += double.Parse(mt.Groups[1].Value, CultureInfo.InvariantCulture) * us;
            }
            return total;
        }

        return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture); // bare number = seconds
    }

    /// <summary>Converts <paramref name="value"/> of one time unit into another (by key, e.g. "h"→"s").</summary>
    public static double ConvertUnits(double value, string fromKey, string toKey)
    {
        if (!TryUnit(fromKey, out var f, out _)) throw new ArgumentException(Strings.Get("Error_UnknownUnit", fromKey));
        if (!TryUnit(toKey, out var t, out _)) throw new ArgumentException(Strings.Get("Error_UnknownUnit", toKey));
        return value * f / t;
    }

    /// <summary>A number of seconds expressed in every time unit — drives the GUI's live list.</summary>
    public static IReadOnlyList<(string Label, string Value)> InEveryUnit(double seconds) =>
        TimeUnits.Select(u => (u.Label, N(seconds / u.Seconds))).ToList();

    /// <summary>Any-unit-to-any-unit time conversion. "5 h s" converts 5 hours to seconds; "5 h" (or a bare
    /// number, "1d 2h 30m", "2:30:00") shows a compact breakdown, the ISO-8601 form, and every unit.</summary>
    public static string Duration(string input)
    {
        var tokens = input.Trim().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // "<value> <from> <to>" → one direct conversion.
        if (tokens.Length == 3
            && double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && TryUnit(tokens[1], out var fromS, out var fromL)
            && TryUnit(tokens[2], out var toS, out var toL))
            return string.Create(CultureInfo.InvariantCulture, $"{N(value)} {fromL} = {N(value * fromS / toS)} {toL}");

        return Breakdown(ParseDuration(input));
    }

    static string Breakdown(double seconds)
    {
        var negative = seconds < 0;
        var totalMs = (long)Math.Round(Math.Abs(seconds) * 1000);

        long w = totalMs / 604_800_000, rem = totalMs % 604_800_000;
        long d = rem / 86_400_000; rem %= 86_400_000;
        long h = rem / 3_600_000; rem %= 3_600_000;
        long mi = rem / 60_000; rem %= 60_000;
        long se = rem / 1000, ms = rem % 1000;

        // Compact breakdown: drop leading zero units, keep everything once the first non-zero appears.
        var comps = new List<string>();
        foreach (var (v, label) in new (long, string)[] { (w, "w"), (d, "d"), (h, "h"), (mi, "m"), (se, "s") })
            if (v > 0 || comps.Count > 0) comps.Add($"{v}{label}");
        if (ms > 0) comps.Add($"{ms}ms");
        if (comps.Count == 0) comps.Add("0s");
        var compact = (negative ? "-" : "") + string.Join(' ', comps);

        // ISO 8601: weeks fold into days so the date and time parts can coexist (P…T…).
        var isoDays = w * 7 + d;
        var secFrac = se + ms / 1000.0;
        var iso = "P" + (isoDays > 0 ? $"{isoDays}D" : "");
        if (h > 0 || mi > 0 || secFrac > 0 || isoDays == 0)
        {
            iso += "T";
            if (h > 0) iso += $"{h}H";
            if (mi > 0) iso += $"{mi}M";
            if (secFrac > 0 || (h == 0 && mi == 0)) iso += $"{N(secFrac)}S";
        }
        if (negative) iso = "-" + iso;

        var lines = new List<string> { compact, iso };
        foreach (var u in TimeUnits)
            lines.Add($"{u.Label.ToUpperInvariant(),-13} {N(seconds / u.Seconds)}");
        return string.Join('\n', lines);
    }

    static string N(double v) => string.Create(CultureInfo.InvariantCulture, $"{v:0.######}");

    /// <summary>ISO week number, day of year, quarter and more for a date (empty = today).</summary>
    public static string WeekInfo(string input)
    {
        var s = input.Trim();
        var date = s.Length == 0 ? DateTime.Today : ParseDate(s).Date;
        var iso = System.Globalization.ISOWeek.GetWeekOfYear(date);
        var isoYear = System.Globalization.ISOWeek.GetYear(date);      // late-Dec/early-Jan can belong to the neighbour's year
        var dayOfYear = date.DayOfYear;
        var daysInYear = DateTime.IsLeapYear(date.Year) ? 366 : 365;
        var quarter = (date.Month - 1) / 3 + 1;

        return string.Join('\n',
            $"{Strings.Get("Week_Date")}  {date.ToString("D", Strings.Culture)}",
            $"{Strings.Get("Week_Iso")}  {isoYear}-W{iso:00} ({date.DayOfWeek.ToString()[..3]})",
            $"{Strings.Get("Week_DayOfYear")}  {dayOfYear} / {daysInYear}",
            $"{Strings.Get("Week_Quarter")}  Q{quarter}",
            $"{Strings.Get("Week_Weekend")}  {Strings.Get(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "Week_Yes" : "Week_No")}");
    }

    // A few city names → IANA zone ids, so users don't have to know "Europe/Paris". Raw IANA ids
    // (and, on Windows, Windows ids) still work directly. ponytail: a short hand-list, not a city database.
    static readonly Dictionary<string, string> ZoneAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["utc"] = "UTC", ["gmt"] = "UTC", ["z"] = "UTC",
        ["paris"] = "Europe/Paris", ["london"] = "Europe/London", ["berlin"] = "Europe/Berlin",
        ["madrid"] = "Europe/Madrid", ["rome"] = "Europe/Rome", ["moscow"] = "Europe/Moscow",
        ["newyork"] = "America/New_York", ["nyc"] = "America/New_York", ["ny"] = "America/New_York",
        ["losangeles"] = "America/Los_Angeles", ["la"] = "America/Los_Angeles", ["sf"] = "America/Los_Angeles",
        ["chicago"] = "America/Chicago", ["denver"] = "America/Denver", ["toronto"] = "America/Toronto",
        ["saopaulo"] = "America/Sao_Paulo", ["mexicocity"] = "America/Mexico_City",
        ["tokyo"] = "Asia/Tokyo", ["shanghai"] = "Asia/Shanghai", ["beijing"] = "Asia/Shanghai",
        ["hongkong"] = "Asia/Hong_Kong", ["singapore"] = "Asia/Singapore", ["seoul"] = "Asia/Seoul",
        ["dubai"] = "Asia/Dubai", ["mumbai"] = "Asia/Kolkata", ["delhi"] = "Asia/Kolkata", ["kolkata"] = "Asia/Kolkata",
        ["sydney"] = "Australia/Sydney", ["auckland"] = "Pacific/Auckland",
    };

    static readonly string[] DefaultZones = ["UTC", "America/New_York", "Europe/London", "Europe/Paris", "Asia/Tokyo"];

    /// <summary>"14:30 paris tokyo" — a wall-clock time in one zone shown in others. "now nyc london",
    /// or just "tokyo" (assumes now) also work. One source zone, then any number of targets; no
    /// targets falls back to a handful of common zones.</summary>
    public static string Timezone(string input)
    {
        var tokens = input.Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) throw new ArgumentException(Strings.Get("Error_TimezoneUsage"));

        // First token is the time if it parses as one; otherwise everything is zones and the time is "now".
        string timeToken;
        string[] zoneNames;
        if (LooksLikeTime(tokens[0])) { timeToken = tokens[0]; zoneNames = tokens[1..]; }
        else { timeToken = "now"; zoneNames = tokens; }
        if (zoneNames.Length == 0) throw new ArgumentException(Strings.Get("Error_TimezoneUsage"));

        var (sourceId, source) = Zone(zoneNames[0]);
        var instant = ResolveInstant(timeToken, source);

        var targets = zoneNames.Length > 1 ? zoneNames[1..] : DefaultZones;
        var lines = new List<string> { Line(sourceId, source, instant) };
        foreach (var name in targets)
        {
            var (id, tz) = Zone(name);
            lines.Add(Line(id, tz, instant));
        }
        return string.Join('\n', lines);
    }

    static bool LooksLikeTime(string s) =>
        s.Equals("now", StringComparison.OrdinalIgnoreCase)
        || TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out _)
        || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>A curated city list for the GUI's zone dropdowns (label + IANA id).</summary>
    public static readonly IReadOnlyList<(string Label, string Id)> CommonZones =
    [
        ("UTC", "UTC"), ("Los Angeles", "America/Los_Angeles"), ("New York", "America/New_York"),
        ("São Paulo", "America/Sao_Paulo"), ("London", "Europe/London"), ("Paris", "Europe/Paris"),
        ("Moscow", "Europe/Moscow"), ("Dubai", "Asia/Dubai"), ("Mumbai", "Asia/Kolkata"),
        ("Singapore", "Asia/Singapore"), ("Tokyo", "Asia/Tokyo"), ("Sydney", "Australia/Sydney"),
    ];

    /// <summary>The instant of a wall-clock time (hour/minute) *today* in the given zone.</summary>
    public static DateTimeOffset InstantFrom(int hour, int minute, string zoneId)
    {
        var tz = Zone(zoneId).Tz;
        var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz).Date;
        var local = today.AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(local, tz.GetUtcOffset(local));
    }

    /// <summary>That instant as seen in <paramref name="zoneId"/>.</summary>
    public static DateTimeOffset InZone(DateTimeOffset instant, string zoneId) =>
        TimeZoneInfo.ConvertTime(instant, Zone(zoneId).Tz);

    static (string Id, TimeZoneInfo Tz) Zone(string name)
    {
        var key = name.Replace(" ", "").Replace("_", "");
        var id = ZoneAliases.TryGetValue(key, out var mapped) ? mapped : name;
        try { return (id, TimeZoneInfo.FindSystemTimeZoneById(id)); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException(Strings.Get("Error_UnknownZone", name));
        }
    }

    // "now" is an absolute instant; a clock time is that time *today, in the source zone*; a full
    // date/time is likewise read as local to the source zone.
    static DateTimeOffset ResolveInstant(string timeToken, TimeZoneInfo source)
    {
        if (timeToken.Equals("now", StringComparison.OrdinalIgnoreCase)) return DateTimeOffset.UtcNow;
        if (TimeSpan.TryParse(timeToken, CultureInfo.InvariantCulture, out var t))
        {
            var today = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, source).Date;
            var local = today + t;
            return new DateTimeOffset(local, source.GetUtcOffset(local));
        }
        var dt = DateTime.SpecifyKind(DateTime.Parse(timeToken, CultureInfo.InvariantCulture), DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, source.GetUtcOffset(dt));
    }

    static string Line(string id, TimeZoneInfo tz, DateTimeOffset instant)
    {
        var there = TimeZoneInfo.ConvertTime(instant, tz);
        return string.Create(CultureInfo.InvariantCulture, $"{id,-20} {there:yyyy-MM-dd HH:mm} {there:zzz}");
    }

    /// <summary>Dates are typed by humans: accept the local format, then ISO, then anything .NET knows.</summary>
    static DateTimeOffset ParseDate(string s) =>
        DateTimeOffset.TryParse(s, Strings.Culture, DateTimeStyles.None, out var local) ? local
        : DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso) ? iso
        : throw new ArgumentException(Strings.Get("Error_BadDate", s));
}
