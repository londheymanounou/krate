using System.Globalization;
using System.IO;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>A path plus a size — for splitting a file into parts, or creating a dummy test file of a
/// given size. Real pickers and a size+unit field instead of typing "path 10MB". Because both write
/// to disk, nothing runs until the Go button.</summary>
public sealed partial class FileSizePage : UserControl
{
    public enum Mode { SplitFile, CreateFile }

    static readonly string[] Units = ["KB", "MB", "GB"];

    readonly nint _hwnd;
    readonly Mode _mode;
    readonly Func<string, string> _run;
    string? _base;   // the picked file (split) or folder (create)

    public FileSizePage(nint hwnd, string titleKey, Mode mode, Func<string, string> run)
    {
        InitializeComponent();
        _hwnd = hwnd;
        _mode = mode;
        _run = run;
        Title.Text = Strings.Get(titleKey);
        ChooseButton.Content = Strings.Get(mode == Mode.SplitFile ? "Pick_ChooseFile" : "Pick_ChooseFolder");
        RunButton.Content = Strings.Get("Fs_Go");
        NameBox.Header = Strings.Get("Fs_FileName");
        NameBox.Visibility = mode == Mode.CreateFile ? Visibility.Visible : Visibility.Collapsed;
        foreach (var u in Units) Unit.Items.Add(u);
        Unit.SelectedIndex = 1; // MB
    }

    async void OnChoose(object sender, RoutedEventArgs e)
    {
        if (_mode == Mode.SplitFile)
        {
            var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            if (await picker.PickSingleFileAsync() is { } f) { _base = f.Path; SelectedPath.Text = f.Path; }
        }
        else
        {
            var picker = new FolderPicker { FileTypeFilter = { "*" } };
            WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
            if (await picker.PickSingleFolderAsync() is { } d) { _base = d.Path; SelectedPath.Text = d.Path; }
        }
    }

    void OnChanged(object sender, object e) { } // handlers exist for XAML; work happens on Go

    void OnRun(object sender, RoutedEventArgs e)
    {
        if (_base is null) { Result.Text = Strings.Get(_mode == Mode.SplitFile ? "Pick_DropFile" : "Pick_DropFolder"); return; }
        var size = (double.IsNaN(Size.Value) ? 0 : Size.Value).ToString(CultureInfo.InvariantCulture);
        var arg = $"{size}{Units[Math.Max(0, Unit.SelectedIndex)]}";
        var path = _mode == Mode.SplitFile ? _base : Path.Combine(_base, NameBox.Text.Trim());
        try { Result.Text = _run($"{path} {arg}"); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }
}
