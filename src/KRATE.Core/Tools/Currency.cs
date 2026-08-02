using System.Globalization;
using System.Text.Json;

namespace Krate.Core;

/// <summary>The one online tool (spec §9): fetches exchange rates, caches them, and falls back to the
/// last known rates offline — showing their date. Everything else in KRATE stays fully offline.</summary>
public static class Currency
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    static string CacheDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRATE");

    /// <summary>Common currency codes, for a dropdown UI.</summary>
    public static readonly string[] CommonCodes =
    [
        "USD", "EUR", "GBP", "JPY", "CHF", "CAD", "AUD", "NZD", "CNY", "HKD", "SGD", "INR",
        "BRL", "MXN", "ZAR", "SEK", "NOK", "DKK", "PLN", "CZK", "TRY", "RUB", "KRW", "AED",
    ];

    /// <summary>Pure conversion given a rate table — the testable core.</summary>
    public static double Compute(double amount, IReadOnlyDictionary<string, double> rates, string to) =>
        rates.TryGetValue(to, out var rate) ? amount * rate
        : throw new ArgumentException(Strings.Get("Error_UnknownCurrency", to));

    /// <summary>"100 USD EUR" (amount optional, defaults to 1).</summary>
    public static string Convert(string input)
    {
        double amount = 1;
        var codes = new List<string>();
        foreach (var t in input.Split([' ', ',', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(t, NumberStyles.Number, CultureInfo.InvariantCulture, out var a)) amount = a;
            else if (t.Length == 3 && t.All(char.IsLetter)) codes.Add(t.ToUpperInvariant());
        }
        if (codes.Count < 2) throw new ArgumentException(Strings.Get("Error_CurrencyUsage"));
        var (from, to) = (codes[0], codes[1]);

        var (rates, date, offline) = GetRates(from);
        var rate = rates.TryGetValue(to, out var r) ? r : throw new ArgumentException(Strings.Get("Error_UnknownCurrency", to));

        return string.Join('\n',
            string.Create(CultureInfo.InvariantCulture, $"{amount:0.##} {from} = {amount * rate:0.##} {to}"),
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Cur_Rate")}  1 {from} = {rate:0.####} {to}"),
            $"{Strings.Get("Cur_Updated")}  {date}{(offline ? "  " + Strings.Get("Cur_Offline") : "")}");
    }

    /// <summary>Rates for a base currency: fetched when the cache is missing/stale, otherwise read from
    /// cache; on any network failure, the last cached rates are used and flagged offline.</summary>
    static (Dictionary<string, double> Rates, string Date, bool Offline) GetRates(string @base)
    {
        var cachePath = Path.Combine(CacheDir, $"rates_{@base}.json");
        var fresh = File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < Ttl;
        var fetched = false;

        if (!fresh)
        {
            try
            {
                var json = Http.GetStringAsync($"https://open.er-api.com/v6/latest/{@base}").GetAwaiter().GetResult();
                using var probe = JsonDocument.Parse(json);
                if (probe.RootElement.GetProperty("result").GetString() != "success")
                    throw new ArgumentException(Strings.Get("Error_UnknownCurrency", @base));
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(cachePath, json);
                fetched = true;
            }
            catch (ArgumentException) { throw; }
            catch { /* offline — fall back to whatever is cached */ }
        }

        if (!File.Exists(cachePath)) throw new ArgumentException(Strings.Get("Error_NoRates"));

        using var doc = JsonDocument.Parse(File.ReadAllText(cachePath));
        var rates = doc.RootElement.GetProperty("rates").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble());
        var date = doc.RootElement.TryGetProperty("time_last_update_utc", out var d) ? d.GetString() ?? "" : "";
        return (rates, date, !fetched);
    }
}
