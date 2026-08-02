using Krate.Core;
using Xunit;

public class MruTests
{
    [Fact]
    public void Update_PutsNewestFirst_AndDropsDuplicates()
    {
        Assert.Equal(["b", "a"], Mru.Update(["a"], "b"));
        // Re-opening an existing item moves it to the front, not a second copy.
        Assert.Equal(["a", "c", "b"], Mru.Update(["c", "b", "a"], "a"));
        Assert.Single(Mru.Update([], "only"));
    }

    [Fact]
    public void Update_IsCaseInsensitive_AndCapped()
    {
        // "SHA256" and "sha256" are the same tool.
        Assert.Equal(["sha256", "url"], Mru.Update(["url", "SHA256"], "sha256"));

        var many = Enumerable.Range(0, 20).Select(i => $"t{i}");
        Assert.Equal(3, Mru.Update(many, "new", max: 3).Count);
        Assert.Equal("new", Mru.Update(many, "new", max: 3)[0]);
    }
}
