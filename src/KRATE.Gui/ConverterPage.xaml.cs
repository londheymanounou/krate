using System.Text;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>File converter (audio / video / image) over the bundled ffmpeg. Drop or pick files, choose a
/// target format, convert. Offers a one-time ffmpeg download if it isn't present yet; conversions run off
/// the UI thread so the window stays responsive.</summary>
public sealed partial class ConverterPage : UserControl
{
    readonly nint _hwnd;
    readonly List<string> _files = new();

    public ConverterPage(nint hwnd)
    {
        InitializeComponent();
        _hwnd = hwnd;
        Title.Text = Strings.Get("Converter_Title");
        Subtitle.Text = Strings.Get("Converter_Subtitle");
        AddButton.Content = Strings.Get("Converter_AddFiles");
        ClearButton.Content = Strings.Get("Converter_Clear");
        ConvertButton.Content = Strings.Get("Converter_Convert");
        DownloadButton.Content = Strings.Get("Converter_Download");
        DropHint.Text = Strings.Get("Converter_DropHint");

        // Formats grouped by category: "MP3 · audio".
        foreach (var f in Media.Formats)
            FormatBox.Items.Add(new ComboBoxItem { Content = $"{f.Id.ToUpperInvariant()}  ·  {Strings.Get($"Converter_Cat_{f.Category}")}", Tag = f.Id });
        FormatBox.SelectedIndex = 0;

        RefreshFfmpeg();
    }

    void RefreshFfmpeg()
    {
        var ready = Media.HasFfmpeg;
        FfmpegBar.IsOpen = !ready;
        FfmpegBar.Message = Strings.Get("Converter_NeedFfmpeg");
        ConvertButton.IsEnabled = ready;
    }

    async void OnAdd(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var files = await picker.PickMultipleFilesAsync();
        foreach (var f in files) Add(f.Path);
    }

    void OnClear(object sender, RoutedEventArgs e) { _files.Clear(); RefreshList(); }

    void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        foreach (var item in await e.DataView.GetStorageItemsAsync())
            if (item is Windows.Storage.StorageFile file) Add(file.Path);
    }

    /// <summary>Preload a file (from the right-click menu).</summary>
    public void LoadFile(string path) => Add(path);

    void Add(string path)
    {
        if (!_files.Contains(path, StringComparer.OrdinalIgnoreCase)) _files.Add(path);
        RefreshList();
    }

    void RefreshList()
    {
        FilesList.ItemsSource = _files.Select(System.IO.Path.GetFileName).ToList();
        DropHint.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnDownload(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        Bar.IsIndeterminate = true;
        var status = new Progress<string>(s => Status.Text = s);
        try
        {
            await Media.EnsureFfmpegAsync(status);
            Status.Text = Strings.Get("Media_Ready");
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            DownloadButton.IsEnabled = true;
        }
        Bar.IsIndeterminate = false;
        RefreshFfmpeg();
    }

    async void OnConvert(object sender, RoutedEventArgs e)
    {
        if (!Media.HasFfmpeg || _files.Count == 0 || FormatBox.SelectedItem is not ComboBoxItem item) return;
        var format = (string)item.Tag;

        ConvertButton.IsEnabled = AddButton.IsEnabled = false;
        var log = new StringBuilder();
        foreach (var path in _files.ToList())
        {
            Status.Text = Strings.Get("Media_Converting", System.IO.Path.GetFileName(path), format);
            Bar.Value = 0;
            var progress = new Progress<double>(p => Bar.Value = p * 100);
            try
            {
                var message = await Task.Run(() => Media.Convert(path, format, progress));
                log.AppendLine($"✓  {message}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"✗  {System.IO.Path.GetFileName(path)} — {ex.Message}");
            }
            Results.Text = log.ToString();
        }
        Bar.Value = 0;
        Status.Text = Strings.Get("Converter_AllDone");
        ConvertButton.IsEnabled = AddButton.IsEnabled = true;
    }
}
