using System.Globalization;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;
using SevenZip;

namespace Krate.Core;

/// <summary>Tools whose input is a path. Keeping them string-in/string-out means the GUI gets
/// them for free once a dropped file writes its path into the input box.</summary>
public static class Files
{
    /// <summary>Anything that writes must never silently destroy what is already there.</summary>
    static void RefuseToOverwrite(string path)
    {
        if (File.Exists(path)) throw new ArgumentException(Strings.Get("Error_FileExists", path));
    }

    public static string[] PdfSplitKeywords => ["pdf", "split", "extract", "pages"];
    public static string[] StripMetadataKeywords => ["exif", "metadata", "photo", "image", "strip", "remove", "privacy", "clean"];

    static List<string> ParseFiles(string input)
    {
        var path = input.Trim().Trim('"');
        return File.Exists(path) ? [path] : throw new ArgumentException(Strings.Get("Error_NoFile", path));
    }

    static string Directory_(string input)
    {
        var path = input.Trim().Trim('"');
        if (path.Length == 0) path = Environment.CurrentDirectory;
        return Directory.Exists(path) ? path : throw new ArgumentException(Strings.Get("Error_NoFolder", path));
    }

    static string File_(string input)
    {
        var path = input.Trim().Trim('"');
        return File.Exists(path) ? path : throw new ArgumentException(Strings.Get("Error_NoFile", path));
    }

    public static string HumanSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }

    /// <summary>"10MB", "512k", "2 GiB" → a byte count.</summary>
    public static long ParseSize(string input)
    {
        var s = input.Trim().Replace(" ", "");
        var digits = s.TakeWhile(c => char.IsDigit(c) || c is '.' or ',').Count();
        if (digits == 0) throw new ArgumentException(Strings.Get("Error_BadSize", input));
        var value = double.Parse(s[..digits].Replace(',', '.'), CultureInfo.InvariantCulture);
        var multiplier = s[digits..].ToLowerInvariant() switch
        {
            "" or "b" => 1L,
            "k" or "kb" => 1000L,
            "m" or "mb" => 1000_000L,
            "g" or "gb" => 1000_000_000L,
            "kib" => 1024L,
            "mib" => 1024L * 1024,
            "gib" => 1024L * 1024 * 1024,
            _ => throw new ArgumentException(Strings.Get("Error_BadSize", input)),
        };
        return (long)(value * multiplier);
    }

    const int MaxTreeEntries = 5000;

    /// <summary>"path [depth]" → the folder tree as text, ready to paste into a README.</summary>
    public static string Tree(string input)
    {
        var (path, rest) = SplitLastNumber(input);
        var maxDepth = rest ?? 3;
        var root = Directory_(path);

        var lines = new List<string> { Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar)) + "/" };
        var truncated = Walk(root, "", 0);
        if (truncated) lines.Add(Strings.Get("Files_TreeTruncated", MaxTreeEntries));
        return string.Join('\n', lines);

        bool Walk(string folder, string prefix, int depth)
        {
            if (depth >= maxDepth) return false;
            DirectoryInfo[] folders;
            FileInfo[] files;
            try
            {
                var info = new DirectoryInfo(folder);
                folders = info.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
                files = info.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (UnauthorizedAccessException) { lines.Add(prefix + "└── " + Strings.Get("Files_AccessDenied")); return false; }

            var entries = folders.Select(d => (Name: d.Name + "/", d.FullName, IsFolder: true))
                .Concat(files.Select(f => (Name: f.Name, f.FullName, IsFolder: false)))
                .ToArray();

            for (var i = 0; i < entries.Length; i++)
            {
                if (lines.Count >= MaxTreeEntries) return true;
                var last = i == entries.Length - 1;
                lines.Add($"{prefix}{(last ? "└── " : "├── ")}{entries[i].Name}");
                if (entries[i].IsFolder && Walk(entries[i].FullName, prefix + (last ? "    " : "│   "), depth + 1)) return true;
            }
            return false;
        }
    }

    /// <summary>Total size of a folder, plus what is actually taking the room.</summary>
    public static string FolderSize(string input)
    {
        var root = Directory_(input);
        var files = EnumerateFiles(root).ToArray();
        if (files.Length == 0) return Strings.Get("Files_Empty");

        var total = files.Sum(f => f.Length);
        var biggest = files.OrderByDescending(f => f.Length).Take(5)
            .Select(f => $"  {HumanSize(f.Length),10}  {Path.GetRelativePath(root, f.FullName)}");
        var byExtension = files.GroupBy(f => f.Extension.ToLowerInvariant() is { Length: > 0 } e ? e : "(none)")
            .OrderByDescending(g => g.Sum(f => f.Length)).Take(5)
            .Select(g => $"  {HumanSize(g.Sum(f => f.Length)),10}  {g.Key} ({g.Count()})");

        return string.Join('\n', [
            Strings.Get("Files_Total", HumanSize(total), files.Length),
            "", Strings.Get("Files_Largest"), .. biggest,
            "", Strings.Get("Files_ByType"), .. byExtension]);
    }

    /// <summary>Duplicate files under a folder. Sizes are compared first, so only genuine
    /// candidates are ever hashed — that is what makes this usable on a big folder.</summary>
    public static string Duplicates(string input)
    {
        var root = Directory_(input);
        var groups = EnumerateFiles(root)
            .Where(f => f.Length > 0)
            .GroupBy(f => f.Length)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.GroupBy(f => Hashing.Sha256File(f.FullName)))
            .Where(g => g.Count() > 1)
            .OrderByDescending(g => g.First().Length)
            .ToArray();

        if (groups.Length == 0) return Strings.Get("Files_NoDuplicates");

        var wasted = groups.Sum(g => g.First().Length * (g.Count() - 1));
        var lines = new List<string> { Strings.Get("Files_DuplicatesFound", groups.Length, HumanSize(wasted)), "" };
        foreach (var group in groups)
        {
            lines.Add($"{HumanSize(group.First().Length)} × {group.Count()}");
            lines.AddRange(group.Select(f => "  " + Path.GetRelativePath(root, f.FullName)));
            lines.Add("");
        }
        // Nothing is deleted: this reports, you decide.
        lines.Add(Strings.Get("Files_DuplicatesReadOnly"));
        return string.Join('\n', lines);
    }

    static IEnumerable<FileInfo> EnumerateFiles(string root) =>
        new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,           // a locked system folder must not abort the whole scan
            AttributesToSkip = FileAttributes.ReparsePoint, // don't follow junctions into a loop
        });

    /// <summary>Every digest of a file, plus its size and dates.</summary>
    public static string Describe(string input)
    {
        var path = File_(input);
        var info = new System.IO.FileInfo(path);
        return string.Join('\n',
            $"{Strings.Get("Files_Name")}  {info.Name}",
            // "N0" in an interpolated string would use CurrentCulture — the OS language — so the
            // byte count was grouped the machine's way while the dates beside it followed the
            // language the user picked. The two must agree.
            $"{Strings.Get("Files_Size")}  {HumanSize(info.Length)} ({info.Length.ToString("N0", Strings.Culture)} B)",
            $"{Strings.Get("Files_Created")}  {info.CreationTime.ToString("g", Strings.Culture)}",
            $"{Strings.Get("Files_Modified")}  {info.LastWriteTime.ToString("g", Strings.Culture)}",
            "",
            $"SHA-256  {Hashing.Sha256File(path)}");
    }

    /// <summary>Two paths, one per line: are these the same file?</summary>
    public static string Compare(string input)
    {
        var paths = input.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (paths.Length < 2) throw new ArgumentException(Strings.Get("Error_NeedTwoFiles"));
        var (a, b) = (File_(paths[0]), File_(paths[1]));
        return Hashing.SameFile(a, b)
            ? Strings.Get("Files_Identical")
            : Strings.Get("Files_Different", HumanSize(new FileInfo(a).Length), HumanSize(new FileInfo(b).Length));
    }

    const int CopyBuffer = 1 << 20; // 1 MB: big enough to be fast, small enough to stay off the large object heap

    /// <summary>"path 10MB" → path.part001, path.part002… The original is left alone.</summary>
    public static string Split(string input)
    {
        var words = input.Trim().Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length < 2) throw new ArgumentException(Strings.Get("Error_SplitUsage"));
        var size = ParseSize(words[^1]);
        var path = File_(string.Join(' ', words[..^1]));
        if (size < 1024) throw new ArgumentException(Strings.Get("Error_BadSize", words[^1]));

        using var source = File.OpenRead(path);
        var written = new List<string>();
        var buffer = new byte[CopyBuffer];
        for (var index = 1; source.Position < source.Length; index++)
        {
            var partPath = $"{path}.part{index:000}";
            RefuseToOverwrite(partPath);
            using var part = File.Create(partPath);
            for (long remaining = size; remaining > 0;)
            {
                var read = source.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));
                if (read == 0) break;
                part.Write(buffer, 0, read);
                remaining -= read;
            }
            written.Add(Path.GetFileName(partPath));
        }
        return string.Join('\n', [Strings.Get("Files_SplitDone", written.Count), .. written]);
    }

    /// <summary>Point at any .partNNN file and the original is rebuilt beside it.</summary>
    public static string Join(string input)
    {
        var first = File_(input);
        var target = first[..first.LastIndexOf(".part", StringComparison.OrdinalIgnoreCase)];
        if (!first.Contains(".part", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Strings.Get("Error_JoinUsage"));
        RefuseToOverwrite(target);

        var parts = Directory.GetFiles(Path.GetDirectoryName(first)!, Path.GetFileName(target) + ".part*")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        using (var output = File.Create(target))
            foreach (var part in parts)
                using (var source = File.OpenRead(part)) source.CopyTo(output, CopyBuffer);

        return Strings.Get("Files_JoinDone", Path.GetFileName(target), parts.Length, HumanSize(new FileInfo(target).Length));
    }

    /// <summary>"path 100MB" → a file of exactly that size, for testing uploads and quotas.</summary>
    public static string TestFile(string input)
    {
        var words = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) throw new ArgumentException(Strings.Get("Error_TestFileUsage"));
        var size = ParseSize(words[^1]);
        var path = string.Join(' ', words[..^1]).Trim('"');
        RefuseToOverwrite(path);

        // SetLength writes a sparse file on NTFS: instant, and the size on disk is still reported correctly.
        using (var file = File.Create(path)) file.SetLength(size);
        return Strings.Get("Files_TestFileDone", path, HumanSize(size));
    }

    /// <summary>Zips a file or folder into "&lt;name&gt;.zip" beside it. Built on System.IO.Compression —
    /// no archiver dependency, no copied code. The original is left in place.</summary>
    /// <summary>Text-tool entry: "path" makes a zip; "path | tgz" (or tar / tar.bz2) picks the format.</summary>
    public static string Compress(string input)
    {
        var parts = input.Split('|', 2);
        return Compress(parts[0], parts.Length > 1 ? parts[1].Trim() : "zip");
    }

    /// <summary>Points SevenZipSharp at the native 7z.dll shipped beside the app. Swallowing a missing
    /// DLL only defers the failure to a COM error nobody can act on, so say what is wrong and what works.</summary>
    static void LoadSevenZip()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, Environment.Is64BitProcess ? "x64" : "x86", "7z.dll");
        if (!File.Exists(dll)) throw new ArgumentException(Strings.Get("Error_SevenZipMissing", dll));
        SevenZipBase.SetLibraryPath(dll);
    }

    /// <summary>Compresses a file or folder into the chosen format. Creatable: zip, tar, tgz (tar.gz),
    /// tbz2 (tar.bz2) and 7z. rar / xz can be extracted but not created (format limits).</summary>
    public static string Compress(string pathInput, string format)
    {
        var path = pathInput.Trim().Trim('"').TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path)) throw new ArgumentException(Strings.Get("Error_NoFile", path));

        var args = format.Trim().Split('|');
        var fmt = args[0].ToLowerInvariant().TrimStart('.');
        
        if (fmt == "7z")
        {
            var outPath7z = path + ".7z";
            RefuseToOverwrite(outPath7z);
            LoadSevenZip();
            var compressor = new SevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
            string? pwd = null;
            if (args.Length >= 6)
            {
                compressor.CompressionLevel = args[1] switch {
                    "Fast" => SevenZip.CompressionLevel.Fast,
                    "Normal" => SevenZip.CompressionLevel.Normal,
                    "Maximum" => SevenZip.CompressionLevel.High,
                    "Ultra" => SevenZip.CompressionLevel.Ultra,
                    _ => SevenZip.CompressionLevel.None
                };
                compressor.CustomParameters.Add("md", args[2].Replace(" MB", "m"));
                compressor.CustomParameters.Add("s", args[3].ToLowerInvariant() == "true" ? "on" : "off");
                compressor.CustomParameters.Add("mt", args[4].ToLowerInvariant() == "true" ? "on" : "off");
                if (!string.IsNullOrWhiteSpace(args[5])) { pwd = args[5]; compressor.ZipEncryptionMethod = ZipEncryptionMethod.Aes256; }
            }
            if (isDir) {
                if (pwd != null) compressor.CompressDirectory(path, outPath7z, pwd);
                else compressor.CompressDirectory(path, outPath7z);
            } else {
                if (pwd != null) compressor.CompressFilesEncrypted(outPath7z, pwd, path);
                else compressor.CompressFiles(outPath7z, path);
            }
            return Strings.Get("Archive_Zipped", Path.GetFileName(outPath7z), HumanSize(new System.IO.FileInfo(outPath7z).Length));
        }

        var (ext, tar, comp) = fmt switch
        {
            "zip" or "" => (".zip", false, SharpCompress.Common.CompressionType.None),
            "tar" => (".tar", true, SharpCompress.Common.CompressionType.None),
            "tgz" or "targz" or "tar.gz" or "gz" => (".tar.gz", true, SharpCompress.Common.CompressionType.GZip),
            "tbz2" or "tbz" or "tarbz2" or "tar.bz2" or "bz2" => (".tar.bz2", true, SharpCompress.Common.CompressionType.BZip2),
            "rar" or "xz" => throw new ArgumentException(Strings.Get("Error_CannotCreate", fmt)),
            _ => throw new ArgumentException(Strings.Get("Error_UnknownFormat", fmt)),
        };

        var outPath = path + ext;
        RefuseToOverwrite(outPath);

        if (!tar) // zip via System.IO.Compression
        {
            if (isDir)
                ZipFile.CreateFromDirectory(path, outPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            else
            {
                using var zip = ZipFile.Open(outPath, ZipArchiveMode.Create);
                zip.CreateEntryFromFile(path, Path.GetFileName(path), System.IO.Compression.CompressionLevel.Optimal);
            }
        }
        else // tar / tar.gz / tar.bz2 via SharpCompress
        {
            using var stream = File.Create(outPath);
            using var writer = new TarWriter(stream, new TarWriterOptions(comp, finalizeArchiveOnClose: true));
            if (isDir) writer.WriteAll(path, "*", SearchOption.AllDirectories);
            else { using var entry = File.OpenRead(path); writer.Write(Path.GetFileName(path), entry, null); }
        }
        return Strings.Get("Archive_Zipped", Path.GetFileName(outPath), HumanSize(new System.IO.FileInfo(outPath).Length));
    }

    static readonly string[] ArchiveExtensions = [".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"];

    /// <summary>Every archive extension the tool can open (for the manager view and drop detection).</summary>
    public static readonly string[] KnownArchiveExtensions = [".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz"];

    /// <summary>Lists an archive's file entries (name, uncompressed size, modified time) for the manager
    /// view. Handles every extractable type: zip natively, the rest through SharpCompress.</summary>
    public static IReadOnlyList<(string Name, long Size, DateTime? Modified)> ListArchive(string input)
    {
        var path = File_(input);
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var list = new List<(string, long, DateTime?)>();

        if (ext == ".zip")
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var e in archive.Entries)
                if (e.Name.Length > 0) list.Add((e.FullName, e.Length, e.LastWriteTime.LocalDateTime));
        }
        else if (ext is ".7z" or ".rar")
        {
            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
            foreach (var e in archive.Entries)
                if (!e.IsDirectory) list.Add((e.Key ?? "", e.Size, e.LastModifiedTime));
        }
        else if (ArchiveExtensions.Contains(ext))
        {
            using var reader = ReaderFactory.OpenReader(path, new ReaderOptions());
            while (reader.MoveToNextEntry())
                if (!reader.Entry.IsDirectory) list.Add((reader.Entry.Key ?? "", reader.Entry.Size, reader.Entry.LastModifiedTime));
        }
        else throw new ArgumentException(Strings.Get("Error_NotArchive", Path.GetFileName(path)));

        return list;
    }

    /// <summary>Extracts an archive into a folder of the same name beside it. .zip uses the built-in
    /// reader; 7z/rar/tar/gz and friends go through SharpCompress. Both paths guard against zip-slip
    /// (entries escaping the target), so a malicious archive can't write elsewhere.</summary>
    public static string Extract(string input)
    {
        var path = File_(input);
        var dest = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        if (dest.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) dest = dest[..^4]; // x.tar.gz → x
        if (File.Exists(dest) || Directory.Exists(dest)) throw new ArgumentException(Strings.Get("Error_FileExists", dest));

        var ext = Path.GetExtension(path).ToLowerInvariant();
        int count;
        if (ext == ".zip")
        {
            using (var archive = ZipFile.OpenRead(path)) count = archive.Entries.Count(e => e.Name.Length > 0);
            ZipFile.ExtractToDirectory(path, dest);
        }
        else if (ArchiveExtensions.Contains(ext))
        {
            count = ExtractWithSharpCompress(path, dest);
        }
        else throw new ArgumentException(Strings.Get("Error_NotArchive", Path.GetFileName(path)));

        return Strings.Get("Archive_Extracted", Path.GetFileName(dest), count);
    }

    static int ExtractWithSharpCompress(string path, string dest)
    {
        Directory.CreateDirectory(dest);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        var count = 0;

        void Write(string? key, Func<Stream> open)
        {
            if (key is null) return;
            var target = Path.GetFullPath(Path.Combine(dest, key));
            if (!target.StartsWith(root, StringComparison.Ordinal)) return; // zip-slip guard
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var src = open();
            using var dst = File.Create(target);
            src.CopyTo(dst);
            count++;
        }

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".7z" or ".rar")
        {
            // Random-access formats.
            using var archive = ArchiveFactory.OpenArchive(path, new ReaderOptions());
            foreach (var entry in archive.Entries) if (!entry.IsDirectory) Write(entry.Key, entry.OpenEntryStream);
        }
        else
        {
            // tar / tar.gz / tgz / gz / bz2 / xz — the streaming reader unwraps the compression layers.
            using var reader = ReaderFactory.OpenReader(path, new ReaderOptions());
            while (reader.MoveToNextEntry()) if (!reader.Entry.IsDirectory) Write(reader.Entry.Key, reader.OpenEntryStream);
        }
        return count;
    }

    /// <summary>The find→replace rename plan for a folder's files (pure — renames nothing). Only files
    /// whose name actually changes are included.</summary>
    public static List<(string Old, string New)> RenamePlan(string folder, string find, string replace)
    {
        var plan = new List<(string, string)>();
        foreach (var file in Directory.GetFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (!name.Contains(find, StringComparison.Ordinal)) continue;
            var renamed = name.Replace(find, replace);
            if (renamed != name && renamed.Length > 0) plan.Add((file, Path.Combine(folder, renamed)));
        }
        return plan;
    }

    /// <summary>"folder | find | replace" previews the renames; add "| apply" to actually rename.
    /// Dry-run by default so the live GUI view never renames files as you type.</summary>
    public static string BulkRename(string input)
    {
        var parts = input.Split('|').Select(p => p.Trim()).ToArray();
        if (parts.Length < 3 || parts[1].Length == 0) throw new ArgumentException(Strings.Get("Error_RenameUsage"));
        var folder = Directory_(parts[0].Trim('"'));
        var (find, replace) = (parts[1], parts[2]);
        var apply = parts.Length > 3 && parts[3].Equals("apply", StringComparison.OrdinalIgnoreCase);

        var plan = RenamePlan(folder, find, replace);
        if (plan.Count == 0) return Strings.Get("Rename_NoMatch", find);

        if (!apply)
            return string.Join('\n', new[] { Strings.Get("Rename_Preview", plan.Count) }
                .Concat(plan.Select(p => $"  {Path.GetFileName(p.Old)}  →  {Path.GetFileName(p.New)}"))
                .Append(Strings.Get("Rename_ApplyHint")));

        // Refuse the whole batch if any target already exists — never clobber.
        foreach (var (_, @new) in plan)
            if (File.Exists(@new)) throw new ArgumentException(Strings.Get("Error_FileExists", Path.GetFileName(@new)));
        foreach (var (old, @new) in plan) File.Move(old, @new);
        return Strings.Get("Rename_Done", plan.Count);
    }

    static (string Path, int? Number) SplitLastNumber(string input)
    {
        var words = input.Trim().Trim('"').Split(' ');
        return words.Length > 1 && int.TryParse(words[^1], out var n)
            ? (string.Join(' ', words[..^1]), n)
            : (input.Trim(), null);
    }

    public static string StripMetadata(string input)
    {
        var files = input.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (files.Length != 2) throw new ArgumentException(Strings.Get("Error_StripMetadataUsage"));
        var inFile = files[0].Trim('"');
        var outFile = files[1].Trim('"');
        
        var ext = Path.GetExtension(inFile).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".webp")
            return Tools.ImageMetadata.StripMetadata(inFile, outFile);
            
        return Media.StripMetadata(inFile, outFile);
    }
}
