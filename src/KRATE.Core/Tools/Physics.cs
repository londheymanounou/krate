using System.Globalization;

namespace Krate.Core;

/// <summary>Speed / distance / time: give any two, get the third. Each value carries its unit,
/// so "100km 2h" and "60km/h 90min" both just work.</summary>
public static class Physics
{
    // unit → (kind, factor to the base). Base units: metre, second, metre/second.
    static readonly Dictionary<string, (string Kind, double ToBase)> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        ["m"] = ("dist", 1), ["km"] = ("dist", 1000), ["cm"] = ("dist", 0.01),
        ["mi"] = ("dist", 1609.344), ["ft"] = ("dist", 0.3048), ["yd"] = ("dist", 0.9144), ["nmi"] = ("dist", 1852),
        ["s"] = ("time", 1), ["sec"] = ("time", 1), ["min"] = ("time", 60), ["h"] = ("time", 3600), ["hr"] = ("time", 3600), ["d"] = ("time", 86400),
        ["m/s"] = ("speed", 1), ["km/h"] = ("speed", 1 / 3.6), ["kmh"] = ("speed", 1 / 3.6),
        ["mph"] = ("speed", 0.44704), ["kn"] = ("speed", 0.514444), ["kt"] = ("speed", 0.514444),
    };

    public static string Solve(string input)
    {
        var tokens = input.Split([' ', ',', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var given = new Dictionary<string, double>(); // kind → base value

        foreach (var token in tokens)
        {
            var (kind, value) = Parse(token);
            if (given.ContainsKey(kind)) throw new ArgumentException(Strings.Get("Error_SdtDuplicate", kind));
            given[kind] = value;
        }
        if (given.Count != 2) throw new ArgumentException(Strings.Get("Error_SdtUsage"));

        double dist, time, speed;
        if (!given.ContainsKey("dist")) { speed = given["speed"]; time = given["time"]; dist = speed * time; }
        else if (!given.ContainsKey("time")) { dist = given["dist"]; speed = given["speed"]; time = speed == 0 ? throw new ArgumentException(Strings.Get("Error_SdtZeroSpeed")) : dist / speed; }
        else { dist = given["dist"]; time = given["time"]; speed = time == 0 ? throw new ArgumentException(Strings.Get("Error_SdtZeroTime")) : dist / time; }

        return string.Join('\n',
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Sdt_Distance")}  {dist / 1000:0.####} km  ({dist:0.##} m)"),
            $"{Strings.Get("Sdt_Time")}  {Dates.Duration($"{time.ToString(CultureInfo.InvariantCulture)}").Split('\n')[0]}",
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Sdt_Speed")}  {speed * 3.6:0.####} km/h  ({speed:0.####} m/s)"));
    }

    static (string Kind, double Base) Parse(string token)
    {
        var i = 0;
        while (i < token.Length && (char.IsDigit(token[i]) || token[i] is '.' or ',' or '-' or '+')) i++;
        if (i == 0) throw new ArgumentException(Strings.Get("Error_SdtToken", token));
        var value = double.Parse(token[..i].Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = token[i..];
        if (!Units.TryGetValue(unit, out var u)) throw new ArgumentException(Strings.Get("Error_SdtUnit", unit));
        return (u.Kind, value * u.ToBase);
    }
}
