using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Metadata.Profiles.Iptc;
using SixLabors.ImageSharp.Metadata.Profiles.Xmp;

namespace Krate.Core.Tools;

public static class ImageMetadata
{
    public static string StripMetadata(string inputPath, string outputPath)
    {
        // Was a FileNotFoundException with its own resource key — the only place in the codebase
        // that did either. The shells catch ArgumentException for user mistakes, so this one escaped
        // as an unhandled exception; every other tool reports a missing file the same way.
        if (!File.Exists(inputPath))
            throw new ArgumentException(Strings.Get("Error_NoFile", inputPath));

        try
        {
            using var image = Image.Load(inputPath);
            
            // Remove Exif
            image.Metadata.ExifProfile = null;
            // Remove IPTC
            image.Metadata.IptcProfile = null;
            // Remove XMP
            image.Metadata.XmpProfile = null;

            image.Save(outputPath);
            
            return Strings.Get("ImageMetadata_Success", outputPath);
        }
        catch (Exception ex)
        {
            return Strings.Get("Error_General", ex.Message);
        }
    }
}
