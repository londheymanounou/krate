using System.Security.Cryptography;
using System.Text;

namespace Krate.Core;

/// <summary>Password-based file encryption using only .NET's own primitives, in a standard
/// Encrypt-then-MAC construction: PBKDF2-SHA256 (600k iterations) derives an AES key and an HMAC key
/// from the password + a random salt; the file is encrypted with AES-256-CBC and authenticated with
/// HMAC-SHA256 over the salt, IV and ciphertext. Decryption verifies the MAC <b>before</b> writing any
/// plaintext, so a wrong password or a tampered file fails cleanly instead of producing garbage.</summary>
// ponytail: BCL primitives, no crypto dependency. This is not Picocrypt's Argon2id / XChaCha20 / BLAKE2b
// (none of which ship in .NET), but the same idea — strong authenticated password encryption. Reach for a
// vetted crypto library only if a specific algorithm or Reed-Solomon recovery is actually required.
public static class Crypt
{
    static readonly byte[] Magic = "KRATE01\n"u8.ToArray(); // 8-byte file signature
    const int SaltSize = 16, IvSize = 16, KeySize = 32, MacSize = 32, Iterations = 600_000, ChunkSize = 64 * 1024;

    /// <summary>Text-tool entry: "path | password".</summary>
    public static string Encrypt(string input) => WithPassword(input, EncryptFile);
    public static string Decrypt(string input) => WithPassword(input, DecryptFile);

    static string WithPassword(string input, Func<string, string, string> op)
    {
        var i = input.LastIndexOf('|');
        if (i < 0) throw new ArgumentException(Strings.Get("Error_CryptUsage"));
        return op(input[..i].Trim().Trim('"'), input[(i + 1)..].Trim());
    }

    public static string EncryptFile(string path, string password)
    {
        path = path.Trim().Trim('"');
        if (!File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));
        if (password.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedPassword"));

        var outPath = path + ".krate";
        if (File.Exists(outPath)) throw new ArgumentException(Strings.Get("Error_FileExists", outPath));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv = RandomNumberGenerator.GetBytes(IvSize);
        var (encKey, macKey) = DeriveKeys(password, salt);

        using var aes = Aes.Create();
        aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        using var hmac = new HMACSHA256(macKey);
        hmac.TransformBlock(salt, 0, salt.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        using (var outFile = File.Create(outPath))
        {
            outFile.Write(Magic);
            outFile.Write(salt);
            outFile.Write(iv);
            using (var input = File.OpenRead(path))
            using (var encryptor = aes.CreateEncryptor())
            using (var tee = new MacTee(outFile, hmac))
            using (var crypto = new CryptoStream(tee, encryptor, CryptoStreamMode.Write, leaveOpen: true))
                input.CopyTo(crypto, ChunkSize);
            hmac.TransformFinalBlock([], 0, 0);
            outFile.Write(hmac.Hash!); // the MAC trails the ciphertext
        }
        return Strings.Get("Crypt_Encrypted", Path.GetFileName(outPath));
    }

    public static string DecryptFile(string path, string password)
    {
        path = path.Trim().Trim('"');
        if (!File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));
        if (password.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedPassword"));

        using var input = File.OpenRead(path);
        var headerLen = Magic.Length + SaltSize + IvSize;
        if (input.Length < headerLen + MacSize) throw new ArgumentException(Strings.Get("Error_NotEncrypted", Path.GetFileName(path)));

        var header = new byte[headerLen];
        input.ReadExactly(header);
        if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new ArgumentException(Strings.Get("Error_NotEncrypted", Path.GetFileName(path)));
        var salt = header[Magic.Length..(Magic.Length + SaltSize)];
        var iv = header[(Magic.Length + SaltSize)..];
        var (encKey, macKey) = DeriveKeys(password, salt);

        var cipherStart = headerLen;
        var cipherLen = input.Length - cipherStart - MacSize;

        // Pass 1: authenticate the whole thing before we write a single byte of plaintext.
        using (var hmac = new HMACSHA256(macKey))
        {
            hmac.TransformBlock(salt, 0, salt.Length, null, 0);
            hmac.TransformBlock(iv, 0, iv.Length, null, 0);
            input.Position = cipherStart;
            MacOrDecrypt(input, cipherLen, (b, n) => hmac.TransformBlock(b, 0, n, null, 0));
            hmac.TransformFinalBlock([], 0, 0);
            var stored = new byte[MacSize];
            input.ReadExactly(stored);
            if (!CryptographicOperations.FixedTimeEquals(hmac.Hash!, stored))
                throw new ArgumentException(Strings.Get("Error_WrongPassword"));
        }

        // Pass 2: MAC verified, safe to decrypt.
        var outPath = path.EndsWith(".krate", StringComparison.OrdinalIgnoreCase) ? path[..^6] : path + ".dec";
        if (File.Exists(outPath)) throw new ArgumentException(Strings.Get("Error_FileExists", outPath));

        using var aes = Aes.Create();
        aes.KeySize = 256; aes.Key = encKey; aes.IV = iv; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7;
        input.Position = cipherStart;
        using (var outFile = File.Create(outPath))
        using (var decryptor = aes.CreateDecryptor())
        using (var crypto = new CryptoStream(outFile, decryptor, CryptoStreamMode.Write, leaveOpen: true))
        {
            MacOrDecrypt(input, cipherLen, (b, n) => crypto.Write(b, 0, n));
            crypto.FlushFinalBlock();
        }
        return Strings.Get("Crypt_Decrypted", Path.GetFileName(outPath));
    }

    // Streams `count` bytes of ciphertext from `input`, handing each chunk to `sink` (HMAC or decryptor).
    static void MacOrDecrypt(Stream input, long count, Action<byte[], int> sink)
    {
        var buffer = new byte[ChunkSize];
        while (count > 0)
        {
            var n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
            if (n <= 0) break;
            sink(buffer, n);
            count -= n;
        }
    }

    static (byte[] Enc, byte[] Mac) DeriveKeys(string password, byte[] salt)
    {
        var material = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, KeySize * 2);
        return (material[..KeySize], material[KeySize..]);
    }

    // Writes ciphertext to the output stream while feeding it into the HMAC (encrypt-then-MAC).
    sealed class MacTee(Stream output, HMACSHA256 mac) : Stream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            mac.TransformBlock(buffer, offset, count, null, 0);
            output.Write(buffer, offset, count);
        }
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override void Flush() => output.Flush();
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
