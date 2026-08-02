using System.Globalization;
using System.Security.Cryptography;

namespace Krate.Core;

/// <summary>Things the user asks the app to invent: ids, passwords, random draws.
/// Everything here uses the crypto RNG — it costs nothing and removes a whole class of mistake.</summary>
public static class Generators
{
    public static string Uuid(string input)
    {
        var count = ParseCount(input, max: 1000);
        return string.Join('\n', Enumerable.Range(0, count).Select(_ => Guid.NewGuid().ToString()));
    }

    const string Lower = "abcdefghijkmnopqrstuvwxyz";      // no l
    const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";       // no I, O
    const string Digits = "23456789";                      // no 0, 1
    const string Symbols = "!@#$%^&*-_=+?";

    /// <summary>Password of the requested length (default 20). Ambiguous glyphs are excluded
    /// so a password stays transcribable from a screen.</summary>
    public static string Password(string input) =>
        Password(ParseCount(input, max: 4096, fallback: 20), upper: true, lower: true, digits: true, symbols: true);

    /// <summary>Password from the chosen character classes. At least one class must be on.</summary>
    public static string Password(int length, bool upper, bool lower, bool digits, bool symbols)
    {
        if (length is < 1 or > 4096) throw new ArgumentException(Strings.Get("Error_OutOfRange", 1, 4096));
        var pool = (lower ? Lower : "") + (upper ? Upper : "") + (digits ? Digits : "") + (symbols ? Symbols : "");
        if (pool.Length == 0) throw new ArgumentException(Strings.Get("Error_NoCharset"));
        return new string(RandomNumberGenerator.GetItems<char>(pool.ToCharArray(), length));
    }

    /// <summary>"1 100" → an integer in that range. Empty → 1..100.</summary>
    public static string RandomNumber(string input)
    {
        var (min, max) = ParseRange(input);
        return RandomNumberGenerator.GetInt32(min, max == int.MaxValue ? max : max + 1)
            .ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Dice in "2d6" notation ("d20", "6" and "" also work). Shows each roll and the total.</summary>
    /// <summary>Crypto-backed dice roll: <paramref name="count"/> dice of <paramref name="faces"/> sides
    /// each, one value per die. Shared by the text tool and the GUI dice page.</summary>
    public static int[] Roll(int count, int faces)
    {
        if (count is < 1 or > 1000 || faces < 2) throw new ArgumentException(Strings.Get("Error_BadDice"));
        return Enumerable.Range(0, count).Select(_ => RandomNumberGenerator.GetInt32(1, faces + 1)).ToArray();
    }

    public static string Dice(string input)
    {
        var spec = input.Trim().ToLowerInvariant();
        if (spec.Length == 0) spec = "1d6";
        var parts = spec.Split('d', StringSplitOptions.TrimEntries);
        int Number(string text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : throw new ArgumentException(Strings.Get("Error_BadDice"));

        var count = parts.Length > 1 && parts[0].Length > 0 ? Number(parts[0]) : 1;
        var faces = Number(parts[^1]);

        var rolls = Roll(count, faces);
        return count == 1 ? rolls[0].ToString(CultureInfo.InvariantCulture)
            : $"{string.Join(" + ", rolls)} = {rolls.Sum()}";
    }

    public static string Coin(string _) =>
        Strings.Get(RandomNumberGenerator.GetInt32(2) == 0 ? "Random_Heads" : "Random_Tails");

    /// <summary>Picks one entry at random from a comma- or newline-separated list.</summary>
    public static string Pick(string input)
    {
        var items = input.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length == 0) throw new ArgumentException(Strings.Get("Error_EmptyList"));
        return items[RandomNumberGenerator.GetInt32(items.Length)];
    }

    /// <summary>Shuffles the list (Fisher-Yates) — also covers "split into a random order".</summary>
    public static string Shuffle(string input)
    {
        var items = input.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        RandomNumberGenerator.Shuffle<string>(items);
        return string.Join('\n', items);
    }

    public static string RandomColor(string _) =>
        Colors.Describe(RandomNumberGenerator.GetInt32(0x1000000));

    static readonly string[] Ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];
    static readonly string[] Suits = ["♠", "♥", "♦", "♣"];

    /// <summary>Draws N distinct cards from a shuffled 52-card deck (default 1, max 52).</summary>
    public static string Cards(string input)
    {
        var count = ParseCount(input, max: 52, fallback: 1);
        var deck = (from s in Suits from r in Ranks select r + s).ToArray();
        RandomNumberGenerator.Shuffle<string>(deck);
        return string.Join(' ', deck.Take(count));
    }

    /// <summary>"3; alice, bob, carol, ..." splits the names into 3 random balanced teams. A lone
    /// number anywhere is the team count (default 2); everything else is a name.</summary>
    public static string Teams(string input)
    {
        var tokens = input.Split([',', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var teamCount = 2;
        var numIdx = tokens.FindIndex(t => int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out _));
        if (numIdx >= 0) { teamCount = int.Parse(tokens[numIdx], CultureInfo.InvariantCulture); tokens.RemoveAt(numIdx); }

        var names = tokens.ToArray();
        if (names.Length == 0) throw new ArgumentException(Strings.Get("Error_EmptyList"));
        teamCount = Math.Clamp(teamCount, 1, names.Length); // more teams than people would leave some empty

        RandomNumberGenerator.Shuffle<string>(names);
        var teams = Enumerable.Range(0, teamCount).Select(_ => new List<string>()).ToArray();
        for (var i = 0; i < names.Length; i++) teams[i % teamCount].Add(names[i]);
        return string.Join('\n', teams.Select((t, i) => $"{Strings.Get("Teams_Label", i + 1)}: {string.Join(", ", t)}"));
    }

    /// <summary>int.Parse throws a raw, untranslated FormatException at the user; every counted
    /// generator routes through here.</summary>
    static int ParseCount(string input, int max, int fallback = 1)
    {
        var t = input.Trim();
        if (t.Length == 0) return fallback;
        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > max)
            throw new ArgumentException(Strings.Get("Error_OutOfRange", 1, max));
        return n;
    }

    static (int Min, int Max) ParseRange(string input)
    {
        var parts = input.Split([' ', ',', '-', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return (1, 100);

        int Number(string text) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n
                : throw new ArgumentException(Strings.Get("Error_NeedNumber"));

        if (parts.Length == 1) return (1, Number(parts[0]));
        var (min, max) = (Number(parts[0]), Number(parts[1]));
        return min <= max ? (min, max) : (max, min);
    }
}
