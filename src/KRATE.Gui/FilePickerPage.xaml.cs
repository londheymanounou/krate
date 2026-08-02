using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Reusable "pick a path → run a Core tool → show the text result" page. Replaces the
/// paste-a-path text view for every file/folder tool: a real file-explorer button (and drag-drop)
/// instead of a raw path field. Configured per tool at construction.</summary>
public sealed partial class FilePickerPage : UserControl
{
    public enum PickMode { File, Folder, Both }

    readonly nint _hwnd;
    readonly Func<string, string> _run;

    public FilePickerPage(nint hwnd, string titleKey, PickMode mode, Func<string, string> run)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _run = run;
        Title.Text = Strings.Get(titleKey);
        FileButton.Content = Strings.Get("Pick_ChooseFile");
        FolderButton.Content = Strings.Get("Pick_ChooseFolder");
        FileButton.Visibility = mode is PickMode.File or PickMode.Both ? Visibility.Visible : Visibility.Collapsed;
        FolderButton.Visibility = mode is PickMode.Folder or PickMode.Both ? Visibility.Visible : Visibility.Collapsed;
        // The one button that stays gets the accent; with both, folder is the accent one.
        if (mode == PickMode.File) FileButton.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        Prompt.Text = Strings.Get(mode == PickMode.Folder ? "Pick_DropFolder" : "Pick_DropFile");
        DropHint.Text = Prompt.Text;
    }

    async void OnPickFile(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file) Run(file.Path);
    }

    async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFolderAsync() is { } folder) Run(folder.Path);
    }

    void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0) Run(items[0].Path);
    }

    void Run(string path)
    {
        SelectedPath.Text = path;
        DropHint.Visibility = Visibility.Collapsed;
        try { Result.Text = _run(path); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}
