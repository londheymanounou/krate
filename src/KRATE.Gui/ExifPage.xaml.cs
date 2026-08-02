using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Drop or open a photo to read its EXIF (via Core's <see cref="Exif.Read"/>).</summary>
public sealed partial class ExifPage : UserControl
{
    readonly nint _hwnd;

    public ExifPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Exif_Name");
        OpenButton.Content = Strings.Get("Img_Open");
        DropHint.Text = Strings.Get("Exif_Drop");
    }

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".tiff", ".webp" }) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file) Load(file.Path);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault() is StorageFile file) Load(file.Path);
    }

    void Load(string path)
    {
        DropHint.Visibility = Visibility.Collapsed;
        FileName.Text = System.IO.Path.GetFileName(path);
        try { Result.Text = Exif.Read(path); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}
