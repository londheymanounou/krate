using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>A real notepad: open/save actual .txt files, word wrap, zoom, and a line/column + word
/// count status bar — while still autosaving to the settings folder so the scratch is always there.</summary>
public sealed partial class NotepadPage : UserControl
{
    static string ScratchFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KRATE", "notepad.txt");

    readonly nint _hwnd;
    bool _loading = true;

    public NotepadPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Notepad_Title");
        NewBtn.Label = Strings.Get("Notepad_New");
        OpenBtn.Label = Strings.Get("Notepad_Open");
        SaveBtn.Label = Strings.Get("Notepad_SaveAs");
        WrapBtn.Label = Strings.Get("Notepad_Wrap");
        ZoomInBtn.Label = Strings.Get("Notepad_ZoomIn");
        ZoomOutBtn.Label = Strings.Get("Notepad_ZoomOut");

        // Restore the wrap + zoom preferences.
        var wrap = Settings.Get("notepad_wrap") != "0"; // default on
        WrapBtn.IsChecked = wrap;
        Editor.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        if (int.TryParse(Settings.Get("notepad_font"), out var fs)) Editor.FontSize = Math.Clamp(fs, 8, 40);

        try { if (System.IO.File.Exists(ScratchFile)) Editor.Text = System.IO.File.ReadAllText(ScratchFile); } catch { }
        _loading = false;
        UpdateStatus();
    }

    /// <summary>Preload a text file (from the right-click menu).</summary>
    public void LoadFile(string path)
    {
        try { Editor.Text = System.IO.File.ReadAllText(path); Saved.Text = Strings.Get("Notepad_Opened", System.IO.Path.GetFileName(path)); }
        catch (Exception ex) { Saved.Text = ex.Message; }
    }

    void OnChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Autosave();
        UpdateStatus();
    }

    void OnSelection(object sender, RoutedEventArgs e) => UpdatePosition();

    void Autosave()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ScratchFile)!);
            System.IO.File.WriteAllText(ScratchFile, Editor.Text);
            Saved.Text = Strings.Get("Notepad_Saved");
        }
        catch (Exception ex) { Saved.Text = ex.Message; }
    }

    void OnNew(object sender, RoutedEventArgs e)
    {
        Editor.Text = "";
        Editor.Focus(FocusState.Programmatic);
    }

    async void OnOpen(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try { Editor.Text = await FileIO.ReadTextAsync(file); Saved.Text = Strings.Get("Notepad_Opened", file.Name); }
        catch (Exception ex) { Saved.Text = ex.Message; }
    }

    async void OnSaveAs(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "note" };
        picker.FileTypeChoices.Add("Text", [".txt"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try { await FileIO.WriteTextAsync(file, Editor.Text); Saved.Text = Strings.Get("Notepad_SavedTo", file.Name); }
        catch (Exception ex) { Saved.Text = ex.Message; }
    }

    void OnWrap(object sender, RoutedEventArgs e)
    {
        var wrap = WrapBtn.IsChecked == true;
        Editor.TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Settings.Set("notepad_wrap", wrap ? "1" : "0");
    }

    void OnZoomIn(object sender, RoutedEventArgs e) => SetFont(Editor.FontSize + 2);
    void OnZoomOut(object sender, RoutedEventArgs e) => SetFont(Editor.FontSize - 2);

    void SetFont(double size)
    {
        Editor.FontSize = Math.Clamp(size, 8, 40);
        Settings.Set("notepad_font", ((int)Editor.FontSize).ToString(CultureInfo.InvariantCulture));
    }

    void UpdateStatus()
    {
        UpdatePosition();
        var words = Editor.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Counts.Text = Strings.Get("Notepad_Counts", Editor.Text.Length, words);
    }

    void UpdatePosition()
    {
        var caret = Math.Clamp(Editor.SelectionStart, 0, Editor.Text.Length);
        var upto = Editor.Text.AsSpan(0, caret);
        var line = 1;
        var lastNewline = -1;
        for (var i = 0; i < upto.Length; i++)
            if (upto[i] == '\n') { line++; lastNewline = i; }
        Position.Text = Strings.Get("Notepad_Position", line, caret - lastNewline);
    }
}
