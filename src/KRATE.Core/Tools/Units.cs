using System.Globalization;

namespace Krate.Core;

public static class Units
{
    /// <summary>unit → (dimension, how many base units it is). Base units: metre, gram, second,
    /// byte, metre/second, square metre, litre.</summary>
    // ponytail: one flat table, linear factors only. Temperature is the one non-linear case, handled apart.
    // Case-sensitive on purpose: mb (megabit) and MB (megabyte) are different units.
    // Lookup falls back to a case-insensitive match when it is unambiguous, so "KM" still works.
    static readonly Dictionary<string, (string Dim, double Factor)> Table = new(StringComparer.Ordinal)
    {
        // length (metre)
        ["mm"] = ("length", 0.001), ["cm"] = ("length", 0.01), ["m"] = ("length", 1),
        ["km"] = ("length", 1000), ["in"] = ("length", 0.0254), ["ft"] = ("length", 0.3048),
        ["yd"] = ("length", 0.9144), ["mi"] = ("length", 1609.344), ["nmi"] = ("length", 1852),
        // mass (gram)
        ["mg"] = ("mass", 0.001), ["g"] = ("mass", 1), ["kg"] = ("mass", 1000), ["t"] = ("mass", 1_000_000),
        ["oz"] = ("mass", 28.349523125), ["lb"] = ("mass", 453.59237), ["st"] = ("mass", 6350.29318),
        // time (second)
        ["ms"] = ("time", 0.001), ["s"] = ("time", 1), ["min"] = ("time", 60), ["h"] = ("time", 3600),
        ["d"] = ("time", 86400), ["wk"] = ("time", 604800),
        // data (byte) — decimal and binary kept distinct, because that is the whole point
        ["b"] = ("data", 0.125), ["kb"] = ("data", 125), ["mb"] = ("data", 125_000),
        ["byte"] = ("data", 1), ["B"] = ("data", 1), ["kB"] = ("data", 1000), ["MB"] = ("data", 1e6),
        ["GB"] = ("data", 1e9), ["TB"] = ("data", 1e12),
        ["KiB"] = ("data", 1024), ["MiB"] = ("data", 1048576), ["GiB"] = ("data", 1073741824),
        ["TiB"] = ("data", 1099511627776),
        // speed (metre/second)
        ["mps"] = ("speed", 1), ["kmh"] = ("speed", 1 / 3.6), ["mph"] = ("speed", 0.44704), ["kn"] = ("speed", 0.514444),
        // area (square metre)
        ["m2"] = ("area", 1), ["km2"] = ("area", 1e6), ["ha"] = ("area", 10000),
        ["ft2"] = ("area", 0.09290304), ["acre"] = ("area", 4046.8564224),
        // volume (litre)
        ["ml"] = ("volume", 0.001), ["l"] = ("volume", 1), ["m3"] = ("volume", 1000),
        ["gal"] = ("volume", 3.785411784), ["pt"] = ("volume", 0.473176473), ["floz"] = ("volume", 0.0295735295625),
        // angle (degree)
        ["deg"] = ("angle", 1), ["rad"] = ("angle", 180 / Math.PI), ["grad"] = ("angle", 0.9), ["turn"] = ("angle", 360),
    };

    static readonly string[] Temperatures = ["c", "f", "k"];

    /// <summary>"10 km mi", "100 f c", "5 GiB MB".</summary>
    public static string Convert(string input)
    {
        var parts = input.Split([' ', ',', '\t', '\n', '>'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3) throw new ArgumentException(Strings.Get("Error_ConvertUsage"));

        // double.Parse throws a raw, untranslated FormatException straight at the user.
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        var (from, to) = (parts[1], parts[^1]);

        if (Temperatures.Contains(from, StringComparer.OrdinalIgnoreCase))
            return Format(Temperature(value, from, to), to);

        // Case matters for data units (mb vs MB), so look up exactly first.
        var (fromDim, fromFactor) = Lookup(from);
        var (toDim, toFactor) = Lookup(to);
        if (fromDim != toDim) throw new ArgumentException(Strings.Get("Error_DimensionMismatch", from, to));
        return Format(value * fromFactor / toFactor, to);
    }

    public static double Temperature(double value, string from, string to)
    {
        var celsius = from.ToLowerInvariant() switch
        {
            "c" => value,
            "f" => (value - 32) * 5 / 9,
            "k" => value - 273.15,
            _ => throw new ArgumentException(Strings.Get("Error_UnknownUnit", from)),
        };
        return to.ToLowerInvariant() switch
        {
            "c" => celsius,
            "f" => celsius * 9 / 5 + 32,
            "k" => celsius + 273.15,
            _ => throw new ArgumentException(Strings.Get("Error_UnknownUnit", to)),
        };
    }

    static (string Dim, double Factor) Lookup(string unit)
    {
        if (Table.TryGetValue(unit, out var exact)) return exact;
        var candidates = Table.Keys.Where(k => k.Equals(unit, StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates switch
        {
            [var only] => Table[only],
            // "Mb" could mean megabit or megabyte — refuse rather than guess wrong by a factor of 8.
            { Length: > 1 } => throw new ArgumentException(Strings.Get("Error_AmbiguousUnit", unit, string.Join(", ", candidates))),
            _ => throw new ArgumentException(Strings.Get("Error_UnknownUnit", unit)),
        };
    }

    static string Format(double v, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{v:0.##########} {unit}");

    public static IEnumerable<string> KnownUnits => Table.Keys.Concat(["C", "F", "K"]);

    /// <summary>Units grouped by what they measure — drives the converter's category/from/to dropdowns.</summary>
    public static IReadOnlyDictionary<string, string[]> UnitsByDimension()
    {
        var map = Table.GroupBy(kv => kv.Value.Dim)
            .ToDictionary(g => g.Key, g => g.Select(kv => kv.Key).ToArray());
        map["temperature"] = ["C", "F", "K"]; // handled apart from the linear table
        return map;
    }

    static readonly (int Value, string Symbol)[] RomanTable =
    [
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
        (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
    ];

    /// <summary>Roman ↔ Arabic, direction detected from the input.</summary>
    public static string Roman(string input)
    {
        var s = input.Trim().ToUpperInvariant();
        if (s.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? ToRoman(n)
            : FromRoman(s).ToString(CultureInfo.InvariantCulture);
    }

    public static string ToRoman(int value)
    {
        if (value is < 1 or > 3999) throw new ArgumentException(Strings.Get("Error_RomanRange"));
        var sb = new System.Text.StringBuilder();
        foreach (var (v, symbol) in RomanTable)
            while (value >= v) { sb.Append(symbol); value -= v; }
        return sb.ToString();
    }

    public static int FromRoman(string s)
    {
        var (total, i) = (0, 0);
        while (i < s.Length)
        {
            var match = RomanTable.FirstOrDefault(r => s.AsSpan(i).StartsWith(r.Symbol));
            if (match.Symbol is null) throw new ArgumentException(Strings.Get("Error_BadRoman", s));
            total += match.Value;
            i += match.Symbol.Length;
        }
        // Round-trip check: rejects IIII, VV, IC and friends without a rule table.
        if (ToRoman(total) != s) throw new ArgumentException(Strings.Get("Error_BadRoman", s));
        return total;
    }
}
