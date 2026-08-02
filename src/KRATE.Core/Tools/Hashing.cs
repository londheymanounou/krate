using System.Security.Cryptography;
using System.Text;

namespace Krate.Core;

public static class Hashing
{
    // MD5 and SHA-1 are here as file/download checksums, not as security primitives.
    public static string Md5(string text) => Hex(MD5.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Sha1(string text) => Hex(SHA1.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Sha256(string text) => Hex(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Sha512(string text) => Hex(SHA512.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>All four digests of one text, for when you don't know which one you need.</summary>
    public static string All(string text) => string.Join('\n',
        $"MD5      {Md5(text)}",
        $"SHA-1    {Sha1(text)}",
        $"SHA-256  {Sha256(text)}",
        $"SHA-512  {Sha512(text)}");

    /// <summary>Streamed, so hashing a 4 GB file doesn't load it into memory.</summary>
    public static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        return Hex(SHA256.HashData(stream));
    }

    public static string Md5File(string path)
    {
        using var stream = File.OpenRead(path);
        return Hex(MD5.HashData(stream));
    }

    public static string Sha1File(string path)
    {
        using var stream = File.OpenRead(path);
        return Hex(SHA1.HashData(stream));
    }

    public static string Sha512File(string path)
    {
        using var stream = File.OpenRead(path);
        return Hex(SHA512.HashData(stream));
    }

    /// <summary>Compares two files: cheap length check first, digests only if it could still match.</summary>
    public static bool SameFile(string a, string b) =>
        new FileInfo(a).Length == new FileInfo(b).Length && Sha256File(a) == Sha256File(b);

    static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();
}
