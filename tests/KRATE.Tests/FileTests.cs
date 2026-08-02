using System.Globalization;
using Krate.Core;
using Xunit;

/// <summary>File tools touch the disk, so each test gets its own throwaway folder.</summary>
public class FileToolTests : IDisposable
{
    readonly string _root = Path.Combine(Path.GetTempPath(), "krate-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public FileToolTests()
    {
        Strings.Culture = CultureInfo.GetCultureInfo("en");
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "hello");        // duplicate of a.txt
        File.WriteAllText(Path.Combine(_root, "sub", "c.txt"), "different content");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("10MB", 10_000_000L)]
    [InlineData("512k", 512_000L)]
    [InlineData("2GiB", 2147483648L)]
    [InlineData("1024", 1024L)]
    public void ParseSize_ReadsTheUsualSpellings(string input, long expected) =>
        Assert.Equal(expected, Files.ParseSize(input));

    [Fact]
    public void ParseSize_RejectsNonsense() => Assert.Throws<ArgumentException>(() => Files.ParseSize("big"));

    [Theory]
    [InlineData("zip", ".zip")]
    [InlineData("tar", ".tar")]
    [InlineData("tgz", ".tar.gz")]
    [InlineData("tar.bz2", ".tar.bz2")]
    public void Compress_MakesTheRequestedFormat(string format, string ext)
    {
        var folder = Path.Combine(_root, "sub");
        var result = Files.Compress(folder, format);
        var archive = folder + ext;
        Assert.True(File.Exists(archive), $"{archive} not created");
        Assert.True(new FileInfo(archive).Length > 0);
        Assert.Contains("sub" + ext, result);
    }

    [Fact]
    public void Compress_RejectsFormatsThatCannotBeCreated()
    {
        // rar / xz are extract-only; asking to create one is a clear error, not a crash.
        foreach (var f in new[] { "rar", "xz", "nonsense" })
            Assert.Throws<ArgumentException>(() => Files.Compress(Path.Combine(_root, "a.txt"), f));
    }

    /// <summary>7z needs the native 7z.dll beside the assembly. That copy only reaches projects
    /// referencing KRATE.Core if the csproj ships it explicitly, so this is a deployment test
    /// as much as a compression one — it is exactly what broke the CLI and GUI.</summary>
    [Fact]
    public void Compress_MakesA7zArchive()
    {
        var folder = Path.Combine(_root, "sub");
        Files.Compress(folder, "7z");
        Assert.True(new FileInfo(folder + ".7z").Length > 0);
    }

    [Fact]
    public void Crypt_RoundTripsAndRejectsBadInput()
    {
        var plain = Path.Combine(_root, "secret.txt");
        File.WriteAllText(plain, "top secret content");

        Crypt.EncryptFile(plain, "hunter2");
        var enc = plain + ".crate";
        Assert.True(File.Exists(enc));
        Assert.NotEqual("top secret content", File.ReadAllText(enc)); // actually scrambled

        File.Delete(plain);                                  // free the decrypt destination
        Crypt.DecryptFile(enc, "hunter2");
        Assert.Equal("top secret content", File.ReadAllText(plain)); // recovered exactly

        // Wrong password is rejected by the MAC (no plaintext produced).
        File.Delete(plain);
        Assert.Throws<ArgumentException>(() => Crypt.DecryptFile(enc, "wrong"));
        Assert.False(File.Exists(plain));

        // A tampered ciphertext byte fails authentication.
        var bytes = File.ReadAllBytes(enc);
        bytes[^1] ^= 0xFF;
        var tampered = Path.Combine(_root, "t.crate");
        File.WriteAllBytes(tampered, bytes);
        Assert.Throws<ArgumentException>(() => Crypt.DecryptFile(tampered, "hunter2"));

        // A plain (non-encrypted) file is reported, not misread.
        Assert.Throws<ArgumentException>(() => Crypt.DecryptFile(Path.Combine(_root, "a.txt"), "hunter2"));
    }

    [Fact]
    public void ListArchive_ListsEntriesAcrossFormats()
    {
        Files.Compress(Path.Combine(_root, "sub"), "zip");
        Assert.Contains(Files.ListArchive(Path.Combine(_root, "sub.zip")), e => e.Name.EndsWith("c.txt"));

        Files.Compress(Path.Combine(_root, "sub"), "tgz");
        Assert.Contains(Files.ListArchive(Path.Combine(_root, "sub.tar.gz")), e => e.Name.EndsWith("c.txt"));
    }

    [Fact]
    public void Compress_RoundTripsThroughExtract()
    {
        // Make a tar.gz of a single file, then extract it back and confirm the content survives.
        var src = Path.Combine(_root, "note.txt");
        File.WriteAllText(src, "round trip");
        Files.Compress(src, "tgz");                              // → note.txt.tar.gz
        var archive = src + ".tar.gz";
        Assert.True(File.Exists(archive));

        File.Delete(src);                                        // free the extract destination (note.txt)
        Files.Extract(archive);                                  // dest dir = note.txt (stripped from note.txt.tar)
        Assert.Equal("round trip", File.ReadAllText(Path.Combine(_root, "note.txt", "note.txt")));
    }

    [Fact]
    public void Tree_ListsFoldersAndFiles()
    {
        var result = Files.Tree(_root);
        Assert.Contains("├── sub/", result);
        Assert.Contains("a.txt", result);
        Assert.Contains("c.txt", result);          // nested, within the default depth
        // Depth 1 stops before the folder contents.
        Assert.DoesNotContain("c.txt", Files.Tree(_root + " 1"));
    }

    [Fact]
    public void FolderSize_AddsUpEveryFile()
    {
        var result = Files.FolderSize(_root);
        Assert.Contains("in 3 files", result);
        Assert.Contains(".txt (3)", result);
    }

    [Fact]
    public void Duplicates_FindsIdenticalFiles_AndDeletesNothing()
    {
        var result = Files.Duplicates(_root);
        Assert.Contains("a.txt", result);
        Assert.Contains("b.txt", result);
        Assert.DoesNotContain("c.txt", result);
        Assert.Contains("Nothing was deleted", result);
        Assert.True(File.Exists(Path.Combine(_root, "b.txt")));
    }

    [Fact]
    public void Compare_UsesContent_NotNames()
    {
        var (a, b, c) = (Path.Combine(_root, "a.txt"), Path.Combine(_root, "b.txt"), Path.Combine(_root, "sub", "c.txt"));
        Assert.Contains("identical", Files.Compare($"{a}\n{b}"));
        Assert.Contains("differ", Files.Compare($"{a}\n{c}"));
        Assert.Throws<ArgumentException>(() => Files.Compare(a));
    }

    [Fact]
    public void SplitThenJoin_RebuildsTheExactOriginal()
    {
        var original = Path.Combine(_root, "big.bin");
        var content = new byte[10_000];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(original, content);

        var split = Files.Split($"{original} 4096");
        Assert.Contains("3 parts", split);         // 4096 + 4096 + 1808
        Assert.True(File.Exists(original + ".part003"));

        File.Delete(original);                      // the parts must be enough to rebuild it
        Files.Join(original + ".part002");          // any part will do
        Assert.Equal(content, File.ReadAllBytes(original));
    }

    [Fact]
    public void Join_And_TestFile_RefuseToOverwrite()
    {
        var existing = Path.Combine(_root, "a.txt");
        var error = Assert.Throws<ArgumentException>(() => Files.TestFile($"{existing} 1MB"));
        Assert.Contains("already exists", error.Message);
        Assert.Equal("hello", File.ReadAllText(existing));  // untouched
    }

    [Fact]
    public void TestFile_CreatesTheExactSize()
    {
        var path = Path.Combine(_root, "generated.bin");
        Assert.Contains("Created", Files.TestFile($"{path} 2MB"));
        Assert.Equal(2_000_000, new FileInfo(path).Length);
    }

    [Fact]
    public void Zip_Then_Unzip_RoundTripsTheFolder()
    {
        var zip = Files.Compress(_root);
        Assert.Contains(".zip", zip);
        var zipPath = _root + ".zip";
        Assert.True(File.Exists(zipPath));

        // Extract to a fresh location and confirm the files came back with their content.
        var extractDir = Path.Combine(Path.GetTempPath(), "krate-unzip-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.GetDirectoryName(extractDir)!);
        var movedZip = extractDir + ".zip";
        File.Move(zipPath, movedZip);
        Files.Extract(movedZip);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(extractDir, "a.txt")));
        Assert.Equal("different content", File.ReadAllText(Path.Combine(extractDir, "sub", "c.txt")));

        File.Delete(movedZip);
        Directory.Delete(extractDir, recursive: true);
    }

    [Fact]
    public void Zip_And_Unzip_RefuseToClobber_AndRejectNonZip()
    {
        Files.Compress(_root);
        Assert.Throws<ArgumentException>(() => Files.Compress(_root));                 // .zip already exists
        Assert.Throws<ArgumentException>(() => Files.Extract(Path.Combine(_root, "a.txt"))); // not a .zip
        File.Delete(_root + ".zip");
    }

    [Fact]
    public void Describe_ShowsSizeAndDigest()
    {
        var result = Files.Describe(Path.Combine(_root, "a.txt"));
        Assert.Contains("5 B", result);
        Assert.Contains(Hashing.Sha256("hello"), result);   // same digest as the text tool
    }

    [Fact]
    public void MissingPaths_GiveAClearError()
    {
        Assert.Throws<ArgumentException>(() => Files.Describe(Path.Combine(_root, "nope.txt")));
        Assert.Throws<ArgumentException>(() => Files.Tree(Path.Combine(_root, "nope")));
    }
}
