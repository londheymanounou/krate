using System.Text.RegularExpressions;

namespace Krate.Core;

/// <summary>Password strength by entropy, not by "one uppercase, one digit" theatre. The estimate
/// is deliberately conservative — a guide, never a guarantee.</summary>
public static partial class Security
{
    /// <summary>Estimated entropy in bits, with a penalty for repeats/sequences. 0 for an empty password.</summary>
    public static double Entropy(string password)
    {
        if (password.Length == 0) return 0;

        // Charset size the attacker must assume, from the character classes actually used.
        var pool = 0;
        if (LowerPattern().IsMatch(password)) pool += 26;
        if (UpperPattern().IsMatch(password)) pool += 26;
        if (DigitPattern().IsMatch(password)) pool += 10;
        if (SymbolPattern().IsMatch(password)) pool += 33;

        var bits = password.Length * Math.Log2(Math.Max(pool, 1));

        // A repeated or sequential password has far less real entropy than its length suggests.
        var penalty = 0.0;
        if (HasRun(password)) penalty += 0.25;
        if (password.Distinct().Count() <= password.Length / 2) penalty += 0.25;
        return bits * (1 - penalty);
    }

    /// <summary>Band key for an entropy value — for the meter colour and the rating label.</summary>
    public static string Band(double bits) => bits switch
    {
        < 28 => "Pw_VeryWeak",
        < 36 => "Pw_Weak",
        < 60 => "Pw_Reasonable",
        < 128 => "Pw_Strong",
        _ => "Pw_VeryStrong",
    };

    public static string Strength(string password)
    {
        if (password.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedText"));

        var bits = Entropy(password);
        var penalised = HasRun(password) || password.Distinct().Count() <= password.Length / 2;

        // Guesses at 10^10/s (a modern offline attack on a fast hash) — order of magnitude, not a promise.
        var seconds = Math.Pow(2, bits) / 2 / 1e10;
        return string.Join('\n',
            Strings.Get("Pw_Entropy", $"{bits:0}"),
            Strings.Get("Pw_Rating", Strings.Get(Band(bits))),
            Strings.Get("Pw_Crack", HumanTime(seconds)),
            penalised ? Strings.Get("Pw_Note") : "").TrimEnd('\n');
    }

    /// <summary>True if the password contains three sequential or repeated characters (abc, 123, aaa).</summary>
    static bool HasRun(string s)
    {
        for (var i = 0; i + 2 < s.Length; i++)
        {
            int a = s[i], b = s[i + 1], c = s[i + 2];
            if ((b - a == 1 && c - b == 1) || (a == b && b == c)) return true;
        }
        return false;
    }

    static string HumanTime(double seconds)
    {
        if (seconds < 1) return Strings.Get("Time_Instant");
        (double Limit, string Key)[] units =
        [
            (60, "Time_Seconds"), (3600, "Time_Minutes"), (86400, "Time_Hours"),
            (2_592_000, "Time_Days"), (31_536_000, "Time_Months"), (3_153_600_000, "Time_Years"),
        ];
        var divisor = 1.0;
        foreach (var (limit, key) in units)
        {
            if (seconds < limit) return Strings.Get(key, $"{seconds / divisor:N0}");
            divisor = limit;
        }
        return Strings.Get("Time_Centuries");
    }

    [GeneratedRegex("[a-z]")] private static partial Regex LowerPattern();
    [GeneratedRegex("[A-Z]")] private static partial Regex UpperPattern();
    [GeneratedRegex("[0-9]")] private static partial Regex DigitPattern();
    [GeneratedRegex(@"[^a-zA-Z0-9]")] private static partial Regex SymbolPattern();
}
