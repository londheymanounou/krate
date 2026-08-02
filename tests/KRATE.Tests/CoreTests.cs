using System.Globalization;
using Krate.Core;
using Xunit;

// Tests mutate the ambient culture, so they must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

public class HashingTests
{
    [Fact]
    public void Sha256_MatchesKnownVector()
    {
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", Hashing.Sha256("hello"));
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", Hashing.Sha256(""));
    }

    [Fact]
    public void Sha256_IsUtf8_NotLocaleDependent()
    {
        // Accented input must hash the same whatever the current culture is.
        var expected = Hashing.Sha256("héllo");
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        Assert.Equal(expected, Hashing.Sha256("héllo"));
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }
}

public class CatalogTests
{
    [Fact]
    public void Find_IsCaseInsensitive_AndUnknownReturnsNull()
    {
        Assert.NotNull(Catalog.Find("sha256"));
        Assert.NotNull(Catalog.Find("SHA256"));
        Assert.Null(Catalog.Find("nope"));
    }

    [Fact]
    public void Search_MatchesAliases_AndEmptyReturnsAll()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Contains(Catalog.Search("checksum"), t => t.Id == "Sha256");

        // Search must find tools by their aliases in the active language too.
        Strings.Culture = CultureInfo.GetCultureInfo("fr");
        Assert.Contains(Catalog.Search("empreinte"), t => t.Id == "Sha256");
        Assert.Equal(Catalog.Tools.Count, Catalog.Search("  ").Count());
        Assert.Empty(Catalog.Search("zzzz"));
    }
}

public class LocalizationTests
{
    [Fact]
    public void Strings_ResolveInEnglishAndFrench()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Assert.Equal("SHA-256 hash", Strings.Get("Tool_Sha256_Name"));

        Strings.Culture = CultureInfo.GetCultureInfo("fr");
        Assert.Equal("Empreinte SHA-256", Strings.Get("Tool_Sha256_Name"));
    }

    [Fact]
    public void Strings_UnknownCultureFallsBackToNeutral_AndMissingKeyReturnsKey()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("de");
        Assert.Equal("SHA-256 hash", Strings.Get("Tool_Sha256_Name"));
        Assert.Equal("Nope_Missing", Strings.Get("Nope_Missing"));
    }
}
