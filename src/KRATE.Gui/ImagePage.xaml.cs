using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Krate.Gui;

/// <summary>Convert / resize / compress an image using the platform's own imaging (WIC), so no
/// image-processing dependency is pulled in. GUI-only — pixel work never belongs in Core.</summary>
public sealed partial class ImagePage : UserControl
{
    readonly nint _hwnd;
    StorageFile? _source;

    public ImagePage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Img_Title");
        Subtitle.Text = Strings.Get("Img_Subtitle");
        DropHint.Text = Strings.Get("Img_DropHint");
        FormatLabel.Text = Strings.Get("Img_Format");
        MaxWidthLabel.Text = Strings.Get("Img_MaxWidth");
        QualityLabel.Text = Strings.Get("Img_Quality");
        SaveButton.Content = Strings.Get("Img_Save");
        SaveButton.IsEnabled = false;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.Get("Img_DropHint");
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
            WidthBox.Value = decoder.PixelWidth; // default: keep the current width
            SaveButton.IsEnabled = true;
            Status.IsOpen = false;
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        var (id, extension) = SelectedFormat();

        var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(_source.Name) };
        picker.FileTypeChoices.Add(id.ToUpperInvariant(), [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd); // unpackaged pickers need the window handle
        var target = await picker.PickSaveFileAsync();
        if (target is null) return;

        try
        {
            await TranscodeAsync(target);
            Report(InfoBarSeverity.Success, Strings.Get("Img_Saved", target.Name));
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    async Task TranscodeAsync(StorageFile target)
    {
        // WIC ships decoders for WebP but no encoder, so the save formats are the four it can write.
        var encoderId = SelectedFormat().Id switch
        {
            "jpeg" => BitmapEncoder.JpegEncoderId,
            "tiff" => BitmapEncoder.TiffEncoderId,
            "bmp" => BitmapEncoder.BmpEncoderId,
            _ => BitmapEncoder.PngEncoderId,
        };

        using var input = await _source!.OpenAsync(FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        // Quality only applies to JPEG; setting it for the lossless encoders is harmless but pointless.
        var options = new BitmapPropertySet();
        if (encoderId == BitmapEncoder.JpegEncoderId)
            options.Add("ImageQuality", new BitmapTypedValue((float)(Quality.Value / 100.0), Windows.Foundation.PropertyType.Single));

        using var output = await target.OpenAsync(FileAccessMode.ReadWrite);
        output.Size = 0; // truncate any existing content before writing
        var encoder = await BitmapEncoder.CreateAsync(encoderId, output, options);
        encoder.SetSoftwareBitmap(pixels);

        // A max width of 0 (or ≥ current) means "don't upscale"; otherwise scale keeping the ratio.
        var maxWidth = (uint)Math.Max(0, WidthBox.Value);
        if (maxWidth > 0 && maxWidth < decoder.PixelWidth)
        {
            encoder.BitmapTransform.ScaledWidth = maxWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)(decoder.PixelHeight * (double)maxWidth / decoder.PixelWidth);
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant; // best quality for downscaling
        }

        await encoder.FlushAsync();
    }

    (string Id, string Extension) SelectedFormat()
    {
        var item = (ComboBoxItem)Format.SelectedItem;
        var id = (string)item.Tag;
        return (id, id switch { "jpeg" => ".jpg", "tiff" => ".tiff", "bmp" => ".bmp", _ => ".png" });
    }

    void Report(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
