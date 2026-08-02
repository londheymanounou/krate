using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Compare two files by picking (or dropping) each — no path typing. Runs Core's
/// <see cref="Files.Compare"/> once both slots are filled.</summary>
public sealed partial class FileComparePage : UserControl
{
    readonly nint _hwnd;
    string? _a, _b;

    public FileComparePage(nint hwnd)
    {
        InitializeComponent();
        _hwnd = hwnd;
        Title.Text = Strings.Get("Tool_FileCompare_Name");
        PathA.Text = Strings.Get("Compare_FileA");
        PathB.Text = Strings.Get("Compare_FileB");
        ButtonA.Content = ButtonB.Content = Strings.Get("Pick_ChooseFile");
    }

    async void OnPickA(object sender, RoutedEventArgs e) { if (await Pick() is { } p) Set("A", p); }
    async void OnPickB(object sender, RoutedEventArgs e) { if (await Pick() is { } p) Set("B", p); }

    async Task<string?> Pick()
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        return (await picker.PickSingleFileAsync())?.Path;
    }

    void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0) Set((string)((Border)sender).Tag, items[0].Path);
    }

    void Set(string slot, string path)
    {
        if (slot == "A") { _a = path; PathA.Text = path; } else { _b = path; PathB.Text = path; }
        if (_a is not null && _b is not null)
            try { Result.Text = Files.Compare($"{_a}\n{_b}"); }
            catch (Exception ex) { Result.Text = ex.Message; }
    }
}
