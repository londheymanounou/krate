using System.Globalization;
using System.Runtime.InteropServices;
using Krate.Core;
using Xunit;

/// <summary>Parity for the random tools, which cannot be compared by equality.
///
/// <see cref="RustParityTests"/> asserts the two implementations return identical text. That is
/// impossible here: two correct generators never agree. So each tool declares the *properties*
/// its output must hold — length, range, character set, permutation — and both implementations
/// are held to the same ones. A tool passing here is weaker evidence than byte equality, and
/// that difference is deliberate and recorded.</summary>
public partial class RandomParityTests
{
    const string Lib = "krate_core";

    [StructLayout(LayoutKind.Sequential)]
    struct KrateResult
    {
        public int Ok;
        public IntPtr Text;
    }

    [LibraryImport(Lib, EntryPoint = "krate_run", StringMarshalling = StringMarshalling.Utf8)]
    private static partial KrateResult KrateRun(string id, string input);

    [LibraryImport(Lib, EntryPoint = "krate_set_language", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void KrateSetLanguage(string language);

    [LibraryImport(Lib, EntryPoint = "krate_free")]
    private static partial void KrateFree(IntPtr text);

    static (bool Ok, string Text) Rust(string id, string input)
    {
        var result = KrateRun(id, input);
        try { return (result.Ok != 0, Marshal.PtrToStringUTF8(result.Text) ?? ""); }
        finally { KrateFree(result.Text); }
    }

    public RandomParityTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        KrateSetLanguage("en");
    }

    /// <summary>Runs the same input through both implementations and applies one predicate to
    /// each. Repeated, because a single draw from a random generator proves very little.</summary>
    static void BothSatisfy(string id, Func<string, string> csharp, string input,
                            Action<string, string> check, int runs = 20)
    {
        for (var i = 0; i < runs; i++)
        {
            check("C#", csharp(input));
            var (ok, text) = Rust(id, input);
            Assert.True(ok, $"Rust {id}({input}) failed: {text}");
            check("Rust", text);
        }
    }

    /// <summary>Both must reject the same inputs, even though their messages may differ.</summary>
    static void BothReject(string id, Func<string, string> csharp, params string[] inputs)
    {
        foreach (var input in inputs)
        {
            Assert.Throws<ArgumentException>(() => csharp(input));
            var (ok, _) = Rust(id, input);
            Assert.False(ok, $"Rust {id} accepted {input.Replace("\n", "\\n")}, C# rejected it");
        }
    }

    [Fact]
    public void Uuid_IsVersion4AndDistinct()
    {
        BothSatisfy("Uuid", Generators.Uuid, "50", (side, output) =>
        {
            var ids = output.Split('\n');
            Assert.Equal(50, ids.Length);
            Assert.Equal(50, ids.Distinct().Count());
            Assert.All(ids, id =>
            {
                Assert.Equal(36, id.Length);
                Assert.True(Guid.TryParse(id, out _), $"{side}: {id} is not a UUID");
                Assert.Equal('4', id[14]);                              // version
                Assert.Contains(id[19], "89ab");                        // RFC 4122 variant
                Assert.Equal(id, id.ToLowerInvariant());                // lower-case hex
            });
        }, runs: 3);

        BothReject("Uuid", Generators.Uuid, "0", "1001", "-5");
    }

    [Fact]
    public void Password_HonoursLengthAndExcludesAmbiguousGlyphs()
    {
        foreach (var (input, expected) in new[] { ("", 20), ("64", 64), ("1", 1) })
            BothSatisfy("Password", Generators.Password, input, (side, output) =>
            {
                Assert.Equal(expected, output.Length);
                // 'l', 'I', 'O', '0' and '1' are excluded so a password stays transcribable.
                Assert.DoesNotContain(output, c => c is 'l' or 'I' or 'O' or '0' or '1');
            });

        BothReject("Password", Generators.Password, "0", "5000");
    }

    [Fact]
    public void Random_StaysInRangeAndCoversIt()
    {
        foreach (var side in new[] { "C#", "Rust" })
        {
            var seen = new HashSet<int>();
            for (var i = 0; i < 300; i++)
            {
                var text = side == "C#" ? Generators.RandomNumber("1 3") : Rust("Random", "1 3").Text;
                var value = int.Parse(text, CultureInfo.InvariantCulture);
                Assert.InRange(value, 1, 3);
                seen.Add(value);
            }
            Assert.Equal(3, seen.Count);
        }

        BothSatisfy("Random", Generators.RandomNumber, "5 5", (_, o) => Assert.Equal("5", o));
        // A reversed range is normalised rather than rejected.
        BothSatisfy("Random", Generators.RandomNumber, "9 1",
            (side, o) => Assert.InRange(int.Parse(o, CultureInfo.InvariantCulture), 1, 9));
    }

    [Fact]
    public void Dice_RollWithinFacesAndSumCorrectly()
    {
        BothSatisfy("Dice", Generators.Dice, "d6",
            (_, o) => Assert.InRange(int.Parse(o, CultureInfo.InvariantCulture), 1, 6));

        BothSatisfy("Dice", Generators.Dice, "3d6", (side, output) =>
        {
            var parts = output.Split(" = ");
            Assert.Equal(2, parts.Length);
            var rolls = parts[0].Split(" + ").Select(int.Parse).ToArray();
            Assert.Equal(3, rolls.Length);
            Assert.All(rolls, r => Assert.InRange(r, 1, 6));
            Assert.Equal(rolls.Sum(), int.Parse(parts[1], CultureInfo.InvariantCulture));
        });

        BothReject("Dice", Generators.Dice, "1d1", "2000d6", "zzz");
    }

    [Fact]
    public void Coin_ShowsBothFaces()
    {
        foreach (var side in new[] { "C#", "Rust" })
        {
            var faces = new HashSet<string>();
            for (var i = 0; i < 200; i++)
                faces.Add(side == "C#" ? Generators.Coin("") : Rust("Coin", "").Text);
            Assert.Equal(2, faces.Count);
            Assert.Subset(new HashSet<string> { "Heads", "Tails" }, faces);
        }
    }

    [Fact]
    public void RandomColor_IsAlwaysAValidColour()
    {
        BothSatisfy("RandomColor", Generators.RandomColor, "", (side, output) =>
        {
            var hex = output.Split('\n')[0];
            Assert.StartsWith("HEX  #", hex);
            Assert.True(int.TryParse(hex[6..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _),
                $"{side}: {hex}");
        }, runs: 40);
    }

    [Fact]
    public void PickAndShuffle_PreserveTheList()
    {
        const string list = "a,b,c,d,e";
        string[] expected = ["a", "b", "c", "d", "e"];

        BothSatisfy("Pick", Generators.Pick, list, (side, output) => Assert.Contains(output, expected));
        BothSatisfy("Shuffle", Generators.Shuffle, list,
            (side, output) => Assert.Equal(expected, output.Split('\n').Order()));

        BothReject("Pick", Generators.Pick, "   ", "");
    }

    [Fact]
    public void Cards_AreDrawnWithoutReplacement()
    {
        BothSatisfy("Cards", Generators.Cards, "52", (side, output) =>
        {
            var hand = output.Split(' ');
            Assert.Equal(52, hand.Length);
            Assert.Equal(52, hand.Distinct().Count());
        }, runs: 5);

        BothSatisfy("Cards", Generators.Cards, "5",
            (_, o) => Assert.Equal(5, o.Split(' ').Distinct().Count()));
        BothReject("Cards", Generators.Cards, "53", "0");
    }

    [Fact]
    public void Teams_PlaceEveryPersonExactlyOnce()
    {
        BothSatisfy("Teams", Generators.Teams, "3; alice, bob, carol, dave, eve", (side, output) =>
        {
            var lines = output.Split('\n');
            Assert.Equal(3, lines.Length);
            var names = lines.SelectMany(l => l.Split(": ")[1].Split(", ")).Order();
            Assert.Equal(["alice", "bob", "carol", "dave", "eve"], names);
        });

        // More teams than people collapses rather than emitting empty teams.
        BothSatisfy("Teams", Generators.Teams, "9; a, b",
            (_, o) => Assert.Equal(2, o.Split('\n').Length));
        BothReject("Teams", Generators.Teams, "3", "");
    }

    /// <summary>Guards the file: if the native library stops loading, every test above would
    /// still exercise the C# half and could look healthy.</summary>
    [Fact]
    public void TheRustSideIsActuallyBeingExercised()
    {
        var (ok, text) = Rust("Uuid", "1");
        Assert.True(ok, "krate_core did not answer");
        Assert.True(Guid.TryParse(text, out _), $"expected a UUID, got {text}");
    }
}
