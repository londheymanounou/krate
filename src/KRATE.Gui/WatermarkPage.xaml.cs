using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Stamps a text watermark onto an image. The image and the text are composed in a XAML tree
/// and captured with <see cref="RenderTargetBitmap"/> — so no drawing/imaging dependency is needed.</summary>
// ponytail: RenderTargetBitmap caps very large images, so output is capped at 2048px on the long side.
// Raise the cap or move to a WIC/Direct2D text pass only if someone needs full-resolution stamping.
public sealed partial class WatermarkPage : UserControl
{
    static readonly (string Key, HorizontalAlignment H, VerticalAlignment V)[] Positions =
    [
        ("Watermark_BottomRight", HorizontalAlignment.Right, VerticalAlignment.Bottom),
        ("Watermark_BottomLeft", HorizontalAlignment.Left, VerticalAlignment.Bottom),
        ("Watermark_TopRight", HorizontalAlignment.Right, VerticalAlignment.Top),
        ("Watermark_TopLeft", HorizontalAlignment.Left, VerticalAlignment.Top),
        ("Watermark_Center", HorizontalAlignment.Center, VerticalAlignment.Center),
    ];

    readonly nint _hwnd;
    StorageFile? _source;
    BitmapImage? _bitmap;
    uint _w, _h;

    public WatermarkPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Watermark_Title");
        Subtitle.Text = Strings.Get("Watermark_Subtitle");
        DropHint.Text = Strings.Get("Watermark_DropHint");
        TextLabel.Text = Strings.Get("Watermark_Text");
        PositionLabel.Text = Strings.Get("Watermark_Position");
        OpacityLabel.Text = Strings.Get("Watermark_Opacity");
        SaveButton.Content = Strings.Get("Watermark_Save");
        SaveButton.IsEnabled = false;
        MarkText.Text = Strings.Get("Watermark_Sample");
        foreach (var p in Positions) Position.Items.Add(Strings.Get(p.Key));
        Position.SelectedIndex = 0;
        UpdatePreview();
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.Get("Watermark_DropHint");
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
            _w = decoder.PixelWidth;
            _h = decoder.PixelHeight;

            _bitmap = new BitmapImage();
            stream.Seek(0);
            await _bitmap.SetSourceAsync(stream);
            Preview.Source = _bitmap;
            DropHint.Visibility = Visibility.Collapsed;

            InfoLabel.Text = $"{file.Name}\n{_w} × {_h} px";
            SaveButton.IsEnabled = true;
            Status.IsOpen = false;
            UpdatePreview();
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    void OnChanged(object sender, object e) => UpdatePreview();

    void UpdatePreview()
    {
        if (PreviewMark is null || Position is null) return; // handlers can fire before the tree is built
        var pos = Positions[Math.Max(0, Position.SelectedIndex)];
        PreviewMark.Text = MarkText.Text;
        PreviewMark.HorizontalAlignment = pos.H;
        PreviewMark.VerticalAlignment = pos.V;
        PreviewMark.Opacity = OpacitySlider.Value / 100.0;
        PreviewMark.FontSize = 26;
    }

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_bitmap is null) return;
        var picker = new FileSavePicker
        {
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_source?.Name ?? "image") + "-wm",
        };
        picker.FileTypeChoices.Add("PNG", [".png"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var target = await picker.PickSaveFileAsync();
        if (target is null) return;

        try
        {
            const double cap = 2048;
            var scale = Math.Min(1.0, cap / Math.Max(_w, _h));
            var ow = (int)Math.Round(_w * scale);
            var oh = (int)Math.Round(_h * scale);

            var composite = BuildComposite(ow, oh);
            RenderHost.Children.Clear();
            RenderHost.Children.Add(composite);
            composite.UpdateLayout();

            var rtb = new RenderTargetBitmap();
            await rtb.RenderAsync(composite, ow, oh);
            var pixels = (await rtb.GetPixelsAsync()).ToArray();
            RenderHost.Children.Clear();

            using var output = await target.OpenAsync(FileAccessMode.ReadWrite);
            output.Size = 0;
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
            Report(InfoBarSeverity.Success, Strings.Get("Watermark_Saved", target.Name));
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    Grid BuildComposite(int w, int h)
    {
        var pos = Positions[Math.Max(0, Position.SelectedIndex)];
        var image = new Image { Source = _bitmap, Width = w, Height = h, Stretch = Stretch.Fill };
        var mark = new TextBlock
        {
            Text = MarkText.Text,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = Math.Max(14, h * 0.05),
            Opacity = OpacitySlider.Value / 100.0,
            Margin = new Thickness(h * 0.03),
            HorizontalAlignment = pos.H,
            VerticalAlignment = pos.V,
        };
        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(image);
        grid.Children.Add(mark);
        return grid;
    }

    void Report(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
