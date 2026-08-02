using System.Globalization;

namespace Krate.Core;

/// <summary>Shoe size conversion via a lookup table. ponytail: sizes vary by brand and region, so
/// this is the common EU/UK/US/cm mapping, nearest-row match, not a guarantee. Add brand tables if
/// anyone needs precision.</summary>
public static class Sizes
{
    // (EU, UK, US, foot cm) — standard men's / women's rows.
    static readonly (double Eu, double Uk, double Us, double Cm)[] Men =
    [
        (38, 5, 5.5, 24.0), (39, 6, 6.5, 24.7), (40, 6.5, 7, 25.4), (41, 7.5, 8, 26.0),
        (42, 8, 8.5, 26.7), (43, 9, 9.5, 27.3), (44, 9.5, 10, 28.0), (45, 10.5, 11, 28.6),
        (46, 11, 11.5, 29.3), (47, 12, 12.5, 29.8), (48, 13, 13.5, 30.5),
    ];
    static readonly (double Eu, double Uk, double Us, double Cm)[] Women =
    [
        (35, 2.5, 4.5, 22.0), (36, 3.5, 5.5, 22.7), (37, 4, 6, 23.3), (38, 5, 7, 24.0),
        (39, 6, 8, 24.7), (40, 6.5, 9, 25.4), (41, 7.5, 9.5, 26.0), (42, 8, 10.5, 26.7),
        (43, 9, 11, 27.3), (44, 9.5, 12, 28.0),
    ];

    public static string Shoe(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        var women = s.Contains('w') || s.Contains("women") || s.Contains('f');
        var table = women ? Women : Men;

        // System keyword: check the two-letter ones; cm before us/uk/eu so it isn't shadowed.
        var system = s.Contains("cm") ? 3 : s.Contains("uk") ? 1 : s.Contains("us") ? 2 : 0; // default EU

        var digits = s.Where(c => char.IsDigit(c) || c is '.' or ',').ToArray();
        if (digits.Length == 0) throw new ArgumentException(Strings.Get("Error_ShoeUsage"));
        var value = double.Parse(new string(digits).Replace(',', '.'), CultureInfo.InvariantCulture);

        double Field((double Eu, double Uk, double Us, double Cm) r, int i) => i switch { 1 => r.Uk, 2 => r.Us, 3 => r.Cm, _ => r.Eu };
        var row = table.MinBy(r => Math.Abs(Field(r, system) - value));

        string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        return string.Join('\n',
            $"{Strings.Get(women ? "Shoe_Women" : "Shoe_Men")}",
            $"EU  {N(row.Eu)}",
            $"UK  {N(row.Uk)}",
            $"US  {N(row.Us)}",
            $"CM  {N(row.Cm)}",
            Strings.Get("Shoe_Note"));
    }
}
