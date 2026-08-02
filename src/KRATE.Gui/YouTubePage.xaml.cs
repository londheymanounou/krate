using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krate.Gui;

/// <summary>Video / audio downloader over a fetched yt-dlp — paste a URL, pick a format, download. The
/// download itself and the yt-dlp management are all Core's <see cref="YouTube"/>; conversions reuse the
/// same ffmpeg as the file converter.</summary>
public sealed partial class YouTubePage : UserControl
{
    readonly nint _hwnd;
    string _folder = YouTube.DefaultFolder;

    public YouTubePage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Yt_Title");
        Subtitle.Text = Strings.Get("Yt_Subtitle");
        Url.PlaceholderText = Strings.Get("Yt_Url");
        FormatLabel.Text = Strings.Get("Yt_Format");
        FolderButton.Content = Strings.Get("Yt_Change");
        DownloadButton.Content = Strings.Get("Yt_Download");
        SetupButton.Content = Strings.Get("Yt_Get");
        SetupBar.Message = Strings.Get("Yt_NeedSetup");
        foreach (var (id, _) in YouTube.Formats) Format.Items.Add(id.ToUpperInvariant());
        Format.SelectedIndex = 0;
        FolderText.Text = _folder;
        RefreshSetup();
    }

    void RefreshSetup()
    {
        var ready = YouTube.HasYtDlp;
        SetupBar.IsOpen = !ready;
        DownloadButton.IsEnabled = ready;
    }

    async void OnSetup(object sender, RoutedEventArgs e)
    {
        SetupButton.IsEnabled = false;
        Bar.IsIndeterminate = true;
        Status.Text = Strings.Get("Yt_Getting");
        try { await YouTube.EnsureYtDlpAsync(new Progress<string>(s => Status.Text = s)); Status.Text = Strings.Get("Yt_Ready"); }
        catch (Exception ex) { Status.Text = ex.Message; SetupButton.IsEnabled = true; }
        Bar.IsIndeterminate = false;
        RefreshSetup();
    }

    async void OnChangeFolder(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFolderAsync() is { } folder) { _folder = folder.Path; FolderText.Text = _folder; }
    }

    async void OnDownload(object sender, RoutedEventArgs e)
    {
        var url = Url.Text.Trim();
        if (url.Length == 0) { Status.Text = Strings.Get("Yt_NeedUrl"); return; }
        var format = YouTube.Formats[Math.Max(0, Format.SelectedIndex)].Id;

        DownloadButton.IsEnabled = false;
        Status.Text = Strings.Get("Yt_Downloading");
        Bar.Value = 0;
        var progress = new Progress<double>(p => Bar.Value = p * 100);
        try { Status.Text = await Task.Run(() => YouTube.Download(url, format, _folder, progress)); }
        catch (Exception ex) { Status.Text = ex.Message; }
        Bar.Value = 0;
        DownloadButton.IsEnabled = true;
    }

    CancellationTokenSource? _thumbCts;

    public record Suggestion(string Title, string Url, string Thumbnail) { public override string ToString() => Title; }

    async void OnUrlChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var url = Url.Text.Trim();

        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput && !url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) 
            return;

        VideoInfoPanel.Visibility = Visibility.Collapsed;
        SearchRing.Visibility = Visibility.Collapsed;
        SearchRing.IsActive = false;
        _thumbCts?.Cancel();
        if (url.Length == 0) { Url.ItemsSource = null; return; }

        _thumbCts = new CancellationTokenSource();
        var ct = _thumbCts.Token;

        try
        {
            await Task.Delay(500, ct); // debounce
            
            SearchRing.Visibility = Visibility.Visible;
            SearchRing.IsActive = true;

            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                Url.ItemsSource = null;
                var info = await YouTube.GetVideoInfoAsync(url, ct);
                if (info != null && !ct.IsCancellationRequested)
                {
                    Thumbnail.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(info.Thumbnail));
                    VideoTitle.Text = info.Title;
                    VideoChannel.Text = info.Channel;
                    VideoDuration.Text = info.Duration;
                    VideoInfoPanel.Visibility = Visibility.Visible;
                }
            }
            else
            {
                var results = await YouTube.SearchAsync(url, ct);
                if (!ct.IsCancellationRequested && results.Count > 0)
                {
                    Url.ItemsSource = results.Select(r => new Suggestion(r.Title, r.Url, r.Thumbnail)).ToList();
                }
            }
        }
        catch (TaskCanceledException) { }
        finally
        {
            SearchRing.Visibility = Visibility.Collapsed;
            SearchRing.IsActive = false;
        }
    }

    void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is Suggestion s)
        {
            Url.Text = s.Url;
        }
    }
}
