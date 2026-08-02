using System.Globalization;

namespace Krate.Core;

/// <summary>How long a file takes to move at a given bandwidth. The one thing people get wrong here
/// is bits vs bytes (Mbps ≠ MB/s), so the unit case is honoured strictly.</summary>
public static class Transfer
{
    public static string Time(string input)
    {
        var tokens = input.Split([' ', ',', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length != 2) throw new ArgumentException(Strings.Get("Error_TransferUsage"));

        // The bandwidth token is the one carrying a rate ("bps" or "/s"); the other is the size.
        var bwIndex = Array.FindIndex(tokens, IsBandwidth);
        if (bwIndex < 0) throw new ArgumentException(Strings.Get("Error_TransferUsage"));
        var bitsPerSecond = ParseBandwidth(tokens[bwIndex]);
        var bytes = Files.ParseSize(tokens[1 - bwIndex]);

        var seconds = bytes * 8 / bitsPerSecond;
        return string.Join('\n',
            // Was a plain interpolated string, so "N0" grouped using the OS locale rather than
            // the app's language: a French machine printed "8 000 000 000" with the interface
            // set to English. The two lines below already opt into invariant formatting.
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Transfer_Size")}  {Files.HumanSize(bytes)} ({bytes * 8:N0} bits)"),
            string.Create(CultureInfo.InvariantCulture, $"{Strings.Get("Transfer_Rate")}  {bitsPerSecond / 1e6:0.###} Mbps"),
            $"{Strings.Get("Transfer_TimeLabel")}  {Dates.Duration(seconds.ToString("0.###", CultureInfo.InvariantCulture)).Split('\n')[0]}");
    }

    static bool IsBandwidth(string token) =>
        token.Contains("bps", StringComparison.OrdinalIgnoreCase) || token.Contains("/s", StringComparison.Ordinal);

    /// <summary>Bandwidth token → bits per second. Case matters: "Mbps" is megabits, "MB/s" is megabytes.</summary>
    public static double ParseBandwidth(string token)
    {
        var i = 0;
        while (i < token.Length && (char.IsDigit(token[i]) || token[i] is '.' or ',')) i++;
        if (i == 0) throw new ArgumentException(Strings.Get("Error_TransferRate", token));
        var value = double.Parse(token[..i].Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = token[i..];

        // Bit rates keep their exact case ('b'); byte rates use 'B/s'. Never fold the case here.
        var bitsPerUnit = unit switch
        {
            "bps" or "bit/s" => 1.0,
            "kbps" or "Kbps" => 1e3,
            "Mbps" => 1e6,
            "Gbps" => 1e9,
            "Tbps" => 1e12,
            "B/s" or "Bps" => 8,
            "kB/s" or "KB/s" => 8e3,
            "MB/s" => 8e6,
            "GB/s" => 8e9,
            "KiB/s" => 8.0 * 1024,
            "MiB/s" => 8.0 * 1024 * 1024,
            "GiB/s" => 8.0 * 1024 * 1024 * 1024,
            _ => throw new ArgumentException(Strings.Get("Error_TransferRate", token)),
        };
        return value * bitsPerUnit;
    }
}
