using System.Threading;
using System.Threading.Tasks;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Krate.Gui;

/// <summary>The one view shared by every string-in/string-out tool. Runs the tool live as the
/// user types; a dropped file just writes its path into the input box.</summary>
public sealed partial class ToolView : UserControl
{
    Tool? _tool;

    /// <summary>Cancels the previous keystroke's pending run, so only the last one lands.</summary>
    CancellationTokenSource? _pending;

    /// <summary>How long to wait after the last keystroke before running.
    ///
    /// Long enough that typing a path does not fire a run per character — which mattered: a tool
    /// like Encrypt or Zip has real side effects, and Encrypt derives a key over 600k PBKDF2
    /// iterations. Short enough to still feel live for the text tools.</summary>
    static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    public ToolView()
    {
        InitializeComponent();
        InputLabel.Text = Strings.Get("Gui_Input");
        OutputLabel.Text = Strings.Get("Gui_Output");
        ToolTipService.SetToolTip(PasteButton, Strings.Get("Gui_Paste"));
        ToolTipService.SetToolTip(ClearButton, Strings.Get("Gui_Clear"));
        ToolTipService.SetToolTip(CopyButton, Strings.Get("Gui_Copy"));
        ToolTipService.SetToolTip(StarButton, Strings.Get("Home_Favorites"));
    }

    async void OnPaste(object sender, RoutedEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.Text)) Input.Text = await content.GetTextAsync();
    }

    void OnClear(object sender, RoutedEventArgs e) => Input.Text = "";

    bool? _wide; // current layout, so we only reflow when it actually flips

    // DevToys-style: input and output side-by-side on a wide pane, stacked when it gets narrow.
    void OnEditorResize(object sender, SizeChangedEventArgs e)
    {
        var wide = e.NewSize.Width >= 720;
        if (wide == _wide) return;
        _wide = wide;

        EditorGrid.RowDefinitions.Clear();
        EditorGrid.ColumnDefinitions.Clear();
        if (wide)
        {
            EditorGrid.ColumnDefinitions.Add(new ColumnDefinition());
            EditorGrid.ColumnDefinitions.Add(new ColumnDefinition());
            Place(InputCard, 0, 0);
            Place(OutputCard, 0, 1);
        }
        else
        {
            EditorGrid.RowDefinitions.Add(new RowDefinition());
            EditorGrid.RowDefinitions.Add(new RowDefinition());
            Place(InputCard, 0, 0);
            Place(OutputCard, 1, 0);
        }
    }

    static void Place(FrameworkElement el, int row, int col) { Grid.SetRow(el, row); Grid.SetColumn(el, col); }

    public void Show(Tool tool) => Show(tool, "");

    /// <summary>Shows a tool with its input prefilled (used by the right-click "Text tools" menu).</summary>
    public void Show(Tool tool, string input)
    {
        _tool = tool;
        ToolName.Text = tool.Name;
        ToolDesc.Text = tool.Description;
        Input.Text = input;
        UpdateStar();
        // No debounce here: the user picked a tool and expects to see it act at once.
        Run(immediate: true);
    }

    // Filled star when pinned, outline when not (Segoe Fluent glyphs).
    void UpdateStar() => StarIcon.Glyph = _tool is not null && Settings.IsFavorite(_tool.Id) ? "" : "";

    void OnStar(object sender, RoutedEventArgs e)
    {
        if (_tool is null) return;
        Settings.ToggleFavorite(_tool.Id);
        UpdateStar();
    }

    /// <summary>Runs the tool and shows the result, off the UI thread.
    ///
    /// Previously this ran synchronously on the UI thread on every keystroke, so a slow tool froze
    /// the window and a tool with side effects fired once per character typed. Now each keystroke
    /// cancels the last pending run, waits out the debounce, and does the work on the thread pool;
    /// only the newest result is ever displayed.</summary>
    async void Run(bool immediate = false)
    {
        // Supersede whatever the previous keystroke scheduled.
        _pending?.Cancel();
        _pending?.Dispose();
        var cts = new CancellationTokenSource();
        _pending = cts;
        var token = cts.Token;

        if (_tool is null) { Output.Text = ""; return; }
        var tool = _tool;
        var input = Input.Text;

        try
        {
            if (!immediate) await Task.Delay(Debounce, token);

            // A tool must never take the app down with it, and must never block the window.
            var result = await Task.Run(() =>
            {
                try { return (Ok: true, Text: tool.Run(input)); }
                catch (Exception ex) { return (Ok: false, Text: ex.Message); }
            }, token);

            // A later keystroke may have overtaken this run while it was working.
            if (token.IsCancellationRequested) return;

            Output.Text = result.Ok
                ? result.Text
                // Don't nag with a "type something" error before anything has been typed.
                : string.IsNullOrWhiteSpace(input) ? "" : Strings.Get("Gui_Error", result.Text);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke; the newer run owns the output.
        }
    }

    void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        var text = Input.Text;
        InputCount.Text = text.Length == 0 ? "" : Strings.Get("Gui_Count", text.Length, text.Split('\n').Length);
        Run();
    }

    // Dropping a file writes its path into the input box, which is all any of the file tools need.
    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = Strings.Get("Gui_DropHint");
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            Input.Text = string.Join('\n', items.Select(i => i.Path));
        }
        catch (Exception ex) { Output.Text = Strings.Get("Gui_Error", ex.Message); }
        finally { deferral.Complete(); }
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        var data = new DataPackage();
        data.SetText(Output.Text);
        Clipboard.SetContent(data);
    }
}
