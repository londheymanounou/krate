using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Krate.Gui;

/// <summary>Remembers the text you copy while KRATE is running; click an item to copy it again.
/// Session-only and in-memory — clipboard history is sensitive, so nothing is written to disk.</summary>
public sealed partial class ClipboardPage : UserControl
{
    const int Max = 25;
    bool _pasting; // ignore the ContentChanged we cause ourselves when re-copying

    public ClipboardPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Clipboard_Title");
        ClearButton.Content = Strings.Get("Clipboard_Clear");
        Hint.Text = Strings.Get("Clipboard_Hint");
        Clipboard.ContentChanged += OnClipboardChanged;
        UpdateHint();
    }

    async void OnClipboardChanged(object? sender, object e)
    {
        if (_pasting) return;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            var text = await content.GetTextAsync();
            if (string.IsNullOrWhiteSpace(text)) return;

            // Move an existing copy to the top rather than duplicating it.
            for (var i = History.Items.Count - 1; i >= 0; i--)
                if ((string)History.Items[i] == text) History.Items.RemoveAt(i);
            History.Items.Insert(0, text);
            while (History.Items.Count > Max) History.Items.RemoveAt(History.Items.Count - 1);
            UpdateHint();
        }
        catch { /* clipboard busy — skip this change */ }
    }

    void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string text) return;
        _pasting = true;
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        _pasting = false;
    }

    void OnClear(object sender, RoutedEventArgs e) { History.Items.Clear(); UpdateHint(); }

    void UpdateHint() => Hint.Visibility = History.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
