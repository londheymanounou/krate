using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krate.Gui;

/// <summary>Theme and language, persisted to the local settings file. Theme applies live; changing
/// the language rebuilds the UI in the new culture (spec §10: language override in settings).</summary>
public sealed partial class SettingsPage : UserControl
{
    readonly Action<ElementTheme> _onTheme;
    readonly Action<string?> _onLanguage;
    bool _loading = true;

    // Language code (null = follow the OS) paired with its display name.
    static readonly (string? Code, string Name)[] Languages = [(null, "System"), ("en", "English"), ("fr", "Français"),
        ("zh-CN", "中文 (简体)"), ("es", "Español"), ("pt-BR", "Português"), ("de", "Deutsch"), ("ja", "日本語"),
        ("ru", "Русский"), ("ko", "한국어"), ("zh-TW", "中文 (繁體)"), ("hi", "हिन्दी"), ("tr", "Türkçe"),
        ("pl", "Polski"), ("vi", "Tiếng Việt"), ("id", "Bahasa Indonesia"), ("it", "Italiano"), ("nl", "Nederlands")];

    public SettingsPage(Action<ElementTheme> onTheme, Action<string?> onLanguage)
    {
        _onTheme = onTheme;
        _onLanguage = onLanguage;
        InitializeComponent();

        Title.Text = Strings.Get("Settings_Title");
        AppearanceHeader.Text = Strings.Get("Settings_Theme");
        ThemeCard.Header = Strings.Get("Settings_ThemeCard");
        ThemeCard.Description = Strings.Get("Settings_ThemeDesc");
        LanguageCard.Header = Strings.Get("Settings_Language");
        LanguageCard.Description = Strings.Get("Settings_LangDesc");
        AboutHeader.Text = Strings.Get("Settings_AboutHeader");
        AboutCard.Header = Strings.Get("App_FullName");
        AboutCard.Description = Strings.Get("Settings_About", Catalog.Tools.Count);

        Theme.Items.Add(Strings.Get("Theme_System"));
        Theme.Items.Add(Strings.Get("Theme_Light"));
        Theme.Items.Add(Strings.Get("Theme_Dark"));
        Theme.SelectedIndex = Settings.Get("theme") switch { "light" => 1, "dark" => 2, _ => 0 };

        foreach (var (_, name) in Languages) LangBox.Items.Add(name);
        LangBox.SelectedIndex = Array.FindIndex(Languages, l => l.Code == Settings.Language);
        if (LangBox.SelectedIndex < 0) LangBox.SelectedIndex = 0;

        IntegrationHeader.Text = Strings.Get("Settings_Integration");
        ContextCard.Header = Strings.Get("Settings_ContextCard");
        ContextCard.Description = Strings.Get("Settings_ContextDesc");
        try { ContextToggle.IsOn = WindowsIntegration.IsInstalled(); } catch { }

        LoadStats();
        _loading = false;
    }

    void OnContextToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try
        {
            if (ContextToggle.IsOn) WindowsIntegration.Install();
            else WindowsIntegration.Uninstall();
        }
        catch (Exception ex)
        {
            ContextCard.Description = ex.Message;
            _loading = true; ContextToggle.IsOn = !ContextToggle.IsOn; _loading = false; // revert on failure
        }
    }

    void LoadStats()
    {
        UsageHeader.Text = Strings.Get("Settings_UsageHeader");
        ResetUsage.Content = Strings.Get("Settings_UsageReset");
        var ranked = Usage.Ranked();
        UsageTotal.Text = Strings.Get("Settings_UsageTotal", Usage.Total(), ranked.Count);

        UsageList.Children.Clear();
        if (ranked.Count == 0)
        {
            UsageList.Children.Add(new TextBlock { Text = Strings.Get("Settings_UsageEmpty"), Opacity = 0.7 });
            return;
        }
        var max = ranked[0].Count;
        foreach (var (id, count) in ranked.Take(12))
            UsageList.Children.Add(StatRow(Usage.DisplayName(id), count, max));
    }

    // Name · proportional bar · count.
    static FrameworkElement StatRow(string name, int count, int max)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };

        var bar = new Grid { Height = 12, VerticalAlignment = VerticalAlignment.Center };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(count, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(max - count, 0), GridUnitType.Star) });
        var fill = new Border { Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], CornerRadius = new CornerRadius(6), MinWidth = 6 };
        bar.Children.Add(fill);
        Grid.SetColumn(bar, 1);

        var num = new TextBlock { Text = count.ToString(CultureInfo.InvariantCulture), FontFamily = new FontFamily("Consolas"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(num, 2);

        grid.Children.Add(label);
        grid.Children.Add(bar);
        grid.Children.Add(num);
        return grid;
    }

    void OnResetUsage(object sender, RoutedEventArgs e) { Usage.Reset(); LoadStats(); }

    void OnTheme(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var (setting, theme) = Theme.SelectedIndex switch
        {
            1 => ("light", ElementTheme.Light),
            2 => ("dark", ElementTheme.Dark),
            _ => ("", ElementTheme.Default),
        };
        Settings.Set("theme", setting);
        _onTheme(theme);
    }

    void OnLanguage(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LangBox.SelectedIndex < 0) return;
        _onLanguage(Languages[LangBox.SelectedIndex].Code); // MainWindow persists it and rebuilds the UI
    }
}
