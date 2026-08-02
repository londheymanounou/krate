using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using System.Threading.Tasks;

namespace Krate.Gui;

public sealed class ArchiveEntryVm
{
    public string Name { get; init; } = "";
    public string Size { get; init; } = "";
    public string Modified { get; init; } = "";
}

public sealed partial class ArchivePage : UserControl
{
    static readonly string[] Formats = ["7z", "zip", "tar", "tar.gz", "tar.bz2"];

    readonly nint _hwnd;
    string? _archivePath;
    string? _stagedPath;

    public ArchivePage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Archive_Title");
        OpenLabel.Text = Strings.Get("Archive_Open");
        ExtractLabel.Text = Strings.Get("Archive_ExtractAll");
        FormatLabel.Text = Strings.Get("Archive_Format");
        AddFileBtn.Content = Strings.Get("Pick_ChooseFile");
        AddFolderBtn.Content = Strings.Get("Pick_ChooseFolder");
        CompressBtn.Content = Strings.Get("Archive_Compress");
        NameHdr.Text = Strings.Get("Archive_Name");
        SizeHdr.Text = Strings.Get("Archive_Size");
        DateHdr.Text = Strings.Get("Archive_Modified");
        foreach (var f in Formats) Format.Items.Add(f);
        Format.SelectedIndex = 0;
    }

    public void RunContextAction(string path, string action)
    {
        if (action == "extract") { LoadArchive(path); OnExtract(this, new RoutedEventArgs()); }
        else StagePath(path);
    }

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in Files.KnownArchiveExtensions) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file) LoadArchive(file.Path);
    }

    void LoadArchive(string path)
    {
        try
        {
            var entries = Files.ListArchive(path);
            Entries.ItemsSource = entries.Select(en => new ArchiveEntryVm
            {
                Name = en.Name,
                Size = Files.HumanSize(en.Size),
                Modified = en.Modified is { } m ? m.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
            }).ToList();
            _archivePath = path;
            ExtractBtn.IsEnabled = true;
            Report(InfoBarSeverity.Informational,
                Strings.Get("Archive_Info", Path.GetFileName(path), entries.Count, Files.HumanSize(entries.Sum(en => en.Size))));
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    async void OnExtract(object sender, RoutedEventArgs e)
    {
        if (_archivePath is null) return;
        OverlayText.Text = "Extracting Archive...";
        Overlay.Visibility = Visibility.Visible;
        ProgressSpinner.IsActive = true;
        try
        {
            var path = _archivePath;
            var result = await Task.Run(() => Files.Extract(path));
            Report(InfoBarSeverity.Success, result);
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
        finally
        {
            Overlay.Visibility = Visibility.Collapsed;
            ProgressSpinner.IsActive = false;
        }
    }

    async void OnAddFile(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file) StagePath(file.Path);
    }

    async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFolderAsync() is { } folder) StagePath(folder.Path);
    }

    void StagePath(string path)
    {
        _stagedPath = path;
        CompressBtn.IsEnabled = true;
        Report(InfoBarSeverity.Informational, Strings.Get("Archive_Staged", Path.GetFileName(path)));
    }

    async void OnCompress(object sender, RoutedEventArgs e)
    {
        if (_stagedPath is null) return;
        
        var format = (string)Format.SelectedItem;
        if (format == "7z")
        {
            // Build the advanced options string: format|level|dict|solid|threads|password
            var level = (string)LevelBox.SelectedItem;
            var dict = (string)DictBox.SelectedItem;
            var solid = (string)SolidBox.SelectedItem == "Solid";
            var threads = MultithreadBox.IsChecked == true;
            var password = PasswordBox.Text;
            format = $"7z|{level}|{dict}|{solid}|{threads}|{password}";
        }

        OverlayText.Text = "Compressing Archive...";
        Overlay.Visibility = Visibility.Visible;
        ProgressSpinner.IsActive = true;
        
        try
        {
            var path = _stagedPath;
            var fmt = format;
            var result = await Task.Run(() => Files.Compress(path, fmt));
            Report(InfoBarSeverity.Success, result);
        }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
        finally
        {
            Overlay.Visibility = Visibility.Collapsed;
            ProgressSpinner.IsActive = false;
        }
    }

    void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 0) return;
        var path = items[0].Path;
        if (Files.KnownArchiveExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) LoadArchive(path);
        else StagePath(path);
    }

    void Report(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
