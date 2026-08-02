using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Add PDFs to merge (in order), or pick one to split — over Core's tested <see cref="Pdf"/>.</summary>
public sealed partial class PdfPage : UserControl
{
    readonly nint _hwnd;

    public PdfPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Pdf_Title");
        MergeLabel.Text = Strings.Get("Pdf_MergeLabel");
        DropHint.Text = Strings.Get("Pdf_Drop");
        AddButton.Content = Strings.Get("Pdf_Add");
        MergeButton.Content = Strings.Get("Pdf_MergeBtn");
        ClearButton.Content = Strings.Get("Clipboard_Clear");
        SplitButton.Content = Strings.Get("Pdf_SplitBtn");
    }

    async void OnAdd(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdf");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        foreach (var file in await picker.PickMultipleFilesAsync()) Add(file.Path);
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = DataPackageOperation.Copy;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        foreach (var item in await e.DataView.GetStorageItemsAsync())
            if (item is StorageFile { Path: var p } && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) Add(p);
    }

    void Add(string path)
    {
        Files.Items.Add(path);
        DropHint.Visibility = Visibility.Collapsed;
    }

    void OnClearList(object sender, RoutedEventArgs e)
    {
        Files.Items.Clear();
        DropHint.Visibility = Visibility.Visible;
    }

    void OnMerge(object sender, RoutedEventArgs e)
    {
        var paths = Files.Items.Cast<string>().ToArray();
        try { Status.Text = Pdf.Merge(string.Join('\n', paths)); }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    async void OnSplit(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pdf");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file)
        {
            try { Status.Text = Pdf.Split(file.Path); }
            catch (Exception ex) { Status.Text = ex.Message; }
        }
    }
}
