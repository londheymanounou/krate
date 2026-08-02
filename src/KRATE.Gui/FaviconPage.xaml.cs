using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Krate.Gui;

/// <summary>Turns an image into a multi-resolution .ico favicon. Each size is a PNG encoded by the
/// platform (WIC); the ICO container is assembled by hand — it's a tiny, well-specified format, not
/// worth a dependency. GUI-only, since the pixel work can't live in Core.</summary>
public sealed partial class FaviconPage : UserControl
{
    static readonly uint[] Sizes = [16, 32, 48, 64, 128, 256];

    readonly nint _hwnd;
    StorageFile? _source;

    public FaviconPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Favicon_Title");
        Subtitle.Text = Strings.Get("Favicon_Subtitle");
        DropHint.Text = Strings.Get("Favicon_DropHint");
        SizesLabel.Text = Strings.Get("Favicon_Sizes", string.Join(", ", Sizes));
        SaveButton.Content = Strings.Get("Favicon_Save");
        SaveButton.IsEnabled = false;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.Get("Favicon_DropHint");
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.FirstOrDefault() is StorageFile file) await LoadAsync(file);
        }
        finally { deferral.Complete(); }
    }

    async Task LoadAsync(StorageFile file)
    {
        try
        {
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            _source = file;

            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
            Preview.Source = bitmap;
            DropHint.Visibility = Visibility.Collapsed;

            InfoLabel.Text = $"{file.Name}\n{decoder.PixelWidth} × {decoder.PixelHeight} px";
            SaveButton.IsEnabled = true;
            Status.IsOpen = false;
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var picker = new FileSavePicker { SuggestedFileName = "favicon" };
        picker.FileTypeChoices.Add("ICO", [".ico"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var target = await picker.PickSaveFileAsync();
        if (target is null) return;

        try
        {
            var pngs = new List<byte[]>();
            foreach (var size in Sizes) pngs.Add(await SquarePngAsync(size));
            await FileIO.WriteBytesAsync(target, BuildIco(pngs));
            Report(InfoBarSeverity.Success, Strings.Get("Favicon_Saved", target.Name));
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    // A centered square crop of the source, scaled to size×size, encoded as PNG. Scaling to the
    // shorter side then cropping (WIC scales before it crops) keeps the aspect ratio undistorted.
    async Task<byte[]> SquarePngAsync(uint size)
    {
        using var input = await _source!.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(input);

        var factor = (double)size / Math.Min(decoder.PixelWidth, decoder.PixelHeight);
        var scaledW = (uint)Math.Round(decoder.PixelWidth * factor);
        var scaledH = (uint)Math.Round(decoder.PixelHeight * factor);

        using var mem = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, mem);
        encoder.BitmapTransform.ScaledWidth = scaledW;
        encoder.BitmapTransform.ScaledHeight = scaledH;
        encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        encoder.BitmapTransform.Bounds = new BitmapBounds
        {
            X = (scaledW - size) / 2,
            Y = (scaledH - size) / 2,
            Width = size,
            Height = size,
        };
        await encoder.FlushAsync();

        var bytes = new byte[mem.Size];
        mem.Seek(0);
        using var reader = new DataReader(mem);
        await reader.LoadAsync((uint)mem.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }

    // ICONDIR + one ICONDIRENTRY per image + the PNG payloads. PNG-in-ICO is valid since Windows Vista.
    static byte[] BuildIco(List<byte[]> pngs)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)0);              // reserved
        w.Write((ushort)1);              // type: icon
        w.Write((ushort)pngs.Count);

        var offset = 6 + 16 * pngs.Count; // header + all entries
        for (var i = 0; i < pngs.Count; i++)
        {
            var side = Sizes[i];
            w.Write((byte)(side >= 256 ? 0 : side)); // 0 encodes 256
            w.Write((byte)(side >= 256 ? 0 : side));
            w.Write((byte)0);            // palette count
            w.Write((byte)0);            // reserved
            w.Write((ushort)1);          // colour planes
            w.Write((ushort)32);         // bits per pixel
            w.Write((uint)pngs[i].Length);
            w.Write((uint)offset);
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
        w.Flush();
        return ms.ToArray();
    }

    void Report(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
