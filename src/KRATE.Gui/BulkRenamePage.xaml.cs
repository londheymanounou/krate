using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Folder + find/replace with a live preview and an explicit Apply — over Core's tested
/// <see cref="Files.RenamePlan"/> / <see cref="Files.BulkRename"/>.</summary>
public sealed partial class BulkRenamePage : UserControl
{
    readonly nint _hwnd;
    string? _folder;

    public BulkRenamePage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Rename_Name");
        FolderButton.Content = Strings.Get("Rename_ChooseFolder");
        ApplyButton.Content = Strings.Get("Rename_Apply");
    }

    async void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFolderAsync() is { } folder)
        {
            _folder = folder.Path;
            FolderPath.Text = _folder;
            Refresh();
        }
    }

    void OnChanged(object sender, TextChangedEventArgs e) => Refresh();

    void Refresh()
    {
        Preview.Items.Clear();
        ApplyButton.IsEnabled = false;
        if (_folder is null || Find.Text.Length == 0) { Status.Text = ""; return; }

        var plan = Files.RenamePlan(_folder, Find.Text, Replace.Text);
        foreach (var (old, @new) in plan)
            Preview.Items.Add($"{System.IO.Path.GetFileName(old)}   →   {System.IO.Path.GetFileName(@new)}");
        Status.Text = Strings.Get(plan.Count == 0 ? "Rename_NoMatchShort" : "Rename_Count", plan.Count);
        ApplyButton.IsEnabled = plan.Count > 0;
    }

    void OnApply(object sender, RoutedEventArgs e)
    {
        if (_folder is null) return;
        try
        {
            Status.Text = Files.BulkRename($"{_folder} | {Find.Text} | {Replace.Text} | apply").Split('\n')[0];
            Refresh(); // names changed → clear/rebuild the preview
        }
        catch (Exception ex) { Status.Text = ex.Message; }
    }
}
