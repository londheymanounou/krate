using Krate.Core;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Live Markdown preview: editor on the left, rendered HTML (via Core's <see cref="Markdown.ToHtml"/>)
/// shown in a WebView2 on the right. The WebView renders a local HTML string — nothing goes online.</summary>
public sealed partial class MarkdownPreviewPage : UserControl
{
    public MarkdownPreviewPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Md_Preview_Name");
        Input.Text = "# KRATE\n\nType **Markdown** on the left — see it *rendered* on the right.\n\n- lists\n- `inline code`\n- [links](https://example.com)\n\n> A blockquote.\n\n```\ncode block\n```";
        Init();
    }

    async void Init()
    {
        try { await Web.EnsureCoreWebView2Async(); Render(); }
        catch { /* WebView2 runtime not present — leave the preview blank rather than crash */ }
    }

    void OnInput(object sender, TextChangedEventArgs e) => Render();

    void Render()
    {
        if (Web.CoreWebView2 is null) return;
        try { Web.NavigateToString(Wrap(Markdown.ToHtml(Input.Text))); } catch { /* oversized doc */ }
    }

    static string Wrap(string body) => $"<!doctype html><html><head><meta charset=\"utf-8\"><style>{Css}</style></head><body>{body}</body></html>";

    // Readable defaults that follow the system light/dark theme.
    const string Css =
        "body{font-family:'Segoe UI',system-ui,sans-serif;padding:20px;line-height:1.6;color:#1a1a1a;background:#fff}" +
        "@media(prefers-color-scheme:dark){body{color:#e4e4e4;background:#1f1f1f}}" +
        "h1,h2,h3{line-height:1.25}" +
        "code{background:rgba(128,128,128,.2);padding:2px 5px;border-radius:4px;font-family:Consolas,monospace;font-size:.9em}" +
        "pre{background:rgba(128,128,128,.15);padding:12px;border-radius:6px;overflow:auto}pre code{background:none;padding:0}" +
        "a{color:#4098ff}blockquote{border-left:3px solid #4098ff;margin:0;padding:2px 0 2px 12px;opacity:.85}" +
        "table{border-collapse:collapse}td,th{border:1px solid #888;padding:4px 8px}img{max-width:100%}";
}
