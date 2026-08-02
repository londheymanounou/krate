using System.Runtime.InteropServices.WindowsRuntime;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Krate.Gui;

/// <summary>QR as an actual scannable image, not ASCII. Encoding is Core's <see cref="Qr.Png"/>;
/// this page just shows the PNG and can save it.</summary>
public sealed partial class QrPage : UserControl
{
    byte[] _png = [];
    readonly nint _hwnd;

    public QrPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Qr_Name");
        Prompt.Text = Strings.Get("Tool_Qr_Desc");
        SaveButton.Content = Strings.Get("Img_Save");
        Input.Text = "https://";
    }

    async void OnInput(object sender, TextChangedEventArgs e)
    {
        if (Input.Text.Length == 0) { QrImage.Source = null; _png = []; SaveButton.IsEnabled = false; return; }
        try
        {
            _png = Qr.Png(Input.Text);
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(_png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            QrImage.Source = bitmap;
            SaveButton.IsEnabled = true;
        }
        catch { QrImage.Source = null; SaveButton.IsEnabled = false; } // too much data for one QR, etc.
    }

    async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_png.Length == 0) return;
        var picker = new FileSavePicker { SuggestedFileName = "qr" };
        picker.FileTypeChoices.Add("PNG", [".png"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is not null) await Windows.Storage.FileIO.WriteBytesAsync(file, _png);
    }
}
