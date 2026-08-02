namespace Krate.Core;

/// <summary>Most-recently-used ordering: newest first, no duplicates, capped. Pure so it's testable
/// off-disk; <see cref="Settings"/> handles the persistence.</summary>
public static class Mru
{
    public static List<string> Update(IEnumerable<string> existing, string id, int max = 8) =>
        new[] { id }
            .Concat(existing.Where(r => !string.Equals(r, id, StringComparison.OrdinalIgnoreCase)))
            .Take(max)
            .ToList();
}
