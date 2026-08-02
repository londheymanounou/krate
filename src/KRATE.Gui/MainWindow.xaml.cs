using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Krate.Gui;

/// <summary>Shell: catalog on the left, swappable content on the right. Most tools share one
/// data-driven <see cref="ToolView"/>; the few that need real controls (images, timers) get their
/// own page, listed here rather than in Core so Core keeps no UI dependency.</summary>
public sealed partial class MainWindow : Window
{
    // GUI-only interactive tools: category, name key, page factory, and the text-box tool ids this
    // page replaces (so the redundant text version is hidden from the sidebar).
    readonly (string Category, string NameKey, Func<UserControl> Make, string[] Replaces)[] _interactive;
    readonly ToolView _toolView = new();
    readonly Dictionary<Func<UserControl>, UserControl> _pageCache = new();

    public MainWindow()
    {
        InitializeComponent();
        Title = Strings.Get("App_Name");
        Search.PlaceholderText = Strings.Get("Gui_SearchPlaceholder");
        PaletteBox.PlaceholderText = Strings.Get("Palette_Hint");

        // The three things that make a WinUI window read as native Windows 11:
        SystemBackdrop = new MicaBackdrop();      // translucent, wallpaper-tinted background
        ExtendsContentIntoTitleBar = true;        // content flows under the window buttons, like Settings
        AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;

        // ApplicationIcon puts the icon on the .exe, which covers Explorer and the taskbar — but an
        // unpackaged WinUI window does not pick it up for its own title bar and Alt-Tab entry, so
        // set it explicitly. Missing file is not worth crashing over; the default icon is survivable.
        try
        {
            var icon = Path.Combine(AppContext.BaseDirectory, "krate.ico");
            if (File.Exists(icon)) AppWindow.SetIcon(icon);

            // Same logo in the pane header, beside the wordmark.
            var logo = Path.Combine(AppContext.BaseDirectory, "krate-logo-256.png");
            if (File.Exists(logo))
                LogoImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logo));
        }
        catch (Exception) { }
        ApplyTheme(Settings.Get("theme") switch { "light" => ElementTheme.Light, "dark" => ElementTheme.Dark, _ => ElementTheme.Default });

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _interactive =
        [
            ("Text", "Md_Preview_Name", () => new MarkdownPreviewPage(), []),
            ("Developer", "Tool_Regex_Name", () => new RegexPage(), ["Regex"]),
            ("Encoding", "Tool_Bases_Name", () => new BaseConverterPage(), ["Bases"]),
            ("Colors", "Tool_Gradient_Name", () => new GradientPage(), ["Gradient"]),
            ("Text", "Tool_Lorem_Name", () => new LoremPage(), ["Lorem"]),
            ("Colors", "Tool_ColorTemp_Name", () => new ColorTempPage(), ["ColorTemp"]),
            ("Hashing", "Tool_PasswordStrength_Name", () => new PasswordStrengthPage(), ["PasswordStrength"]),
            ("Maths", "Tool_Sequence_Name", () => new SequencePage(), ["Sequence"]),
            ("Encoding", "Tool_Cron_Name", () => new CronPage(), ["Cron"]),
            ("Text", "Tool_Diff_Name", () => new DiffPage(), ["Diff"]),
            ("Dates", "Tool_Timestamp_Name", () => new TimestampPage(), ["Timestamp"]),
            ("Dates", "Tool_Timezone_Name", () => new TimezonePage(), ["Timezone"]),
            ("Dates", "Tool_Duration_Name", () => new DurationPage(), ["Duration"]),
            ("Conversions", "Tool_Spell_Name", () => new SpellPage(), ["Spell"]),
            ("Hashing", "Tool_HashAll_Name", () => new HashPage(), ["Md5", "Sha1", "Sha256", "Sha512", "HashAll"]),
            ("Everyday", "Notepad_Title", () => new NotepadPage(hwnd), []),
            ("Everyday", "Clipboard_Title", () => new ClipboardPage(), []),
            ("Hashing", "Pwd_Title", () => new PasswordPage(), ["Password"]),
            ("Random", "Tool_Coin_Name", () => new CoinPage(), ["Coin"]),
            ("Random", "Tool_Cards_Name", () => new CardsPage(), ["Cards"]),
            ("Random", "Tool_RandomColor_Name", () => new RandomColorPage(), ["RandomColor"]),
            ("Everyday", "Tool_SysInfo_Name", () => new SysInfoPage(), ["SysInfo"]),
            ("Random", "Tool_Dice_Name", () => new DicePage(), ["Dice"]),
            ("Random", "Tool_Random_Name", () => new RandomNumberPage(), ["Random"]),
            ("Hashing", "Tool_Uuid_Name", () => new UuidPage(), ["Uuid"]),
            ("Hashing", "Encrypt_Title", () => new EncryptPage(hwnd), ["Encrypt", "Decrypt"]),
            ("Developer", "Tool_Qr_Name", () => new QrPage(hwnd), ["Qr"]),
            ("Conversions", "Tool_Convert_Name", () => new ConvertPage(), ["Convert"]),
            ("Conversions", "Tool_Currency_Name", () => new CurrencyPage(), ["Currency"]),
            ("Images", "Tool_Exif_Name", () => new ExifPage(hwnd), ["Exif"]),
            ("Files", "Tool_Rename_Name", () => new BulkRenamePage(hwnd), ["Rename"]),
            ("Files", "Tool_FileCompare_Name", () => new FileComparePage(hwnd), ["FileCompare"]),
            ("Files", "Tool_FileHash_Name", () => new FilePickerPage(hwnd, "Tool_FileHash_Name", FilePickerPage.PickMode.File, Files.Describe), ["FileHash"]),
            ("Files", "Tool_Tree_Name", () => new FilePickerPage(hwnd, "Tool_Tree_Name", FilePickerPage.PickMode.Folder, Files.Tree), ["Tree"]),
            ("Files", "Tool_FolderSize_Name", () => new FilePickerPage(hwnd, "Tool_FolderSize_Name", FilePickerPage.PickMode.Folder, Files.FolderSize), ["FolderSize"]),
            ("Files", "Tool_Duplicates_Name", () => new FilePickerPage(hwnd, "Tool_Duplicates_Name", FilePickerPage.PickMode.Folder, Files.Duplicates), ["Duplicates"]),
            ("Files", "Archive_Title", () => new ArchivePage(hwnd), ["Zip", "Unzip"]),
            ("Developer", "Tool_HexDump_Name", () => new FilePickerPage(hwnd, "Tool_HexDump_Name", FilePickerPage.PickMode.File, Dev.HexDump), ["HexDump"]),
            ("Images", "Tool_ImageInfo_Name", () => new FilePickerPage(hwnd, "Tool_ImageInfo_Name", FilePickerPage.PickMode.File, Images.Dimensions), ["ImageInfo"]),
            ("Files", "Tool_FileJoin_Name", () => new FilePickerPage(hwnd, "Tool_FileJoin_Name", FilePickerPage.PickMode.File, Files.Join), ["FileJoin"]),
            ("Files", "Tool_FileSplit_Name", () => new FileSizePage(hwnd, "Tool_FileSplit_Name", FileSizePage.Mode.SplitFile, Files.Split), ["FileSplit"]),
            ("Files", "Tool_TestFile_Name", () => new FileSizePage(hwnd, "Tool_TestFile_Name", FileSizePage.Mode.CreateFile, Files.TestFile), ["TestFile"]),
            ("Dates", "Tool_WeekInfo_Name", () => new DatePickerPage("Tool_WeekInfo_Name", Dates.WeekInfo), ["WeekInfo"]),
            ("Conversions", "Tool_ShoeSize_Name", () => new ShoePage(), ["ShoeSize"]),
            ("Conversions", "Tool_Roman_Name", () => new RomanPage(), ["Roman"]),
            ("Conversions", "Tool_SpeedDistanceTime_Name", () => new SpeedDistanceTimePage(), ["SpeedDistanceTime"]),
            ("Conversions", "Tool_TransferTime_Name", () => new TransferTimePage(), ["TransferTime"]),
            ("Colors", "Tool_CssUnits_Name", () => new CssUnitsPage(), ["CssUnits"]),
            ("Images", "Tool_AspectRatio_Name", () => new AspectRatioPage(), ["AspectRatio"]),
            ("Maths", "Tool_Combinatorics_Name", CalcFormPage.Combinatorics, ["Combinatorics"]),
            ("Maths", "Tool_Factor_Name", CalcFormPage.Factor, ["Factor"]),
            ("Maths", "Tool_Percent_Name", CalcFormPage.Percent, ["Percent"]),
            ("Maths", "Tool_Fraction_Name", CalcFormPage.Fraction, ["Fraction"]),
            ("Maths", "Tool_Solve_Name", CalcFormPage.Solve, ["Solve"]),
            ("Colors", "Cp_Title", ColorSwatchesPage.Palette, ["Color", "Palette"]),
            ("Colors", "Tool_ColorBlind_Name", ColorSwatchesPage.ColorBlind, ["ColorBlind"]),
            ("Everyday", "Ruler_Title", () => new RulerPage(), []),
            ("Everyday", "Gamepad_Title", () => new GamepadPage(), []),
            ("Everyday", "Clicker_Title", () => new ClickerPage(), []),
            ("Everyday", "Weather_Title", () => new WeatherPage(), ["Weather"]),
            ("Everyday", "Snake_Title", () => new Games.SnakePage(), ["Snake"]),
            ("Everyday", "Game2048_Title", () => new Games.Game2048Page(), ["Game2048"]),
            ("Everyday", "Tetris_Title", () => new Games.TetrisPage(), ["Tetris"]),
            ("Everyday", "Audio_Title", () => new AudioTestPage(), []),
            ("Random", "Wheel_Title", () => new WheelPage(), []),
            ("Files", "Pdf_Title", () => new PdfPage(hwnd), ["PdfMerge", "PdfSplit"]),
            ("Files", "Converter_Title", () => new ConverterPage(hwnd), []),
            ("Files", "Yt_Title", () => new YouTubePage(hwnd), []),
            ("Maths", "Tool_Calc_Name", () => new CalculatorPage(), ["Calc"]),
            ("Images", "StripMetadata_Title", () => new StripMetadataPage(hwnd), ["StripMetadata"]),
            ("Settings", "Settings_Title", () => new SettingsPage(ApplyTheme, ApplyLanguage), []),
            ("Maths", "Tool_Graph_Name", () => new GraphPage(), []),
            ("Dates", "Tool_DateDiff_Name", () => new DateDiffPage(), ["DateDiff"]),
            ("Colors", "Tool_Contrast_Name", () => new ContrastPage(), ["Contrast"]),
            ("Everyday", "Tool_Bmi_Name", CalcFormPage.Bmi, ["Bmi"]),
            ("Everyday", "Tool_Tip_Name", CalcFormPage.Tip, ["Tip"]),
            ("Everyday", "Tool_Loan_Name", CalcFormPage.Loan, ["Loan"]),
            ("Images", "Img_Title", () => new ImagePage(hwnd), []),
            ("Images", "Watermark_Title", () => new WatermarkPage(hwnd), []),
            ("Colors", "Favicon_Title", () => new FaviconPage(hwnd), []),
            ("Dates", "Timer_Title", () => new TimerPage(), []),
            ("Dates", "Clock_Title", () => new ClockPage(), []),
        ];

        ShowTools(Catalog.Tools);
        Host.Content = new HomePage(OpenTool);
    }

    UserControl BuildHome() => new HomePage(OpenTool);

    /// <summary>Right-click "Text tools" menu: open a text tool with the file's content as its input.</summary>
    public void OpenToolWithFile(string toolId, string path)
    {
        if (Catalog.Find(toolId) is not { } tool) return;
        string text;
        try { text = File.ReadAllText(path); } catch { text = ""; }
        Settings.PushRecent(tool.Id);
        Usage.Record(tool.Id);
        _toolView.Show(tool, text);
        Host.Content = _toolView;
    }

    /// <summary>Launched from the Explorer right-click menu: open the matching page and act on the file.</summary>
    public void OpenForFile(string path, string action)
    {
        var (nameKey, run) = action switch
        {
            "encrypt" => ("Encrypt_Title", (Action<UserControl>)(p => (p as EncryptPage)?.LoadFile(path))),
            "convert" => ("Converter_Title", p => (p as ConverterPage)?.LoadFile(path)),
            "edit" => ("Notepad_Title", p => (p as NotepadPage)?.LoadFile(path)),
            _ => ("Archive_Title", p => (p as ArchivePage)?.RunContextAction(path, action)),
        };
        var entry = _interactive.FirstOrDefault(i => i.NameKey == nameKey);
        if (entry.Make is null) return;
        ShowPage(entry.Make);
        if (_pageCache.TryGetValue(entry.Make, out var page)) run(page);
    }

    void ApplyTheme(ElementTheme theme) => Nav.RequestedTheme = theme;

    // ---- Command palette (Ctrl+K) ----

    // A palette entry: what to show, and what to do when chosen.
    sealed record PaletteItem(string Name, Action Open) { public override string ToString() => Name; }

    void OnPaletteAccelerator(Microsoft.UI.Xaml.Input.KeyboardAccelerator sender, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs args)
    {
        Palette.Visibility = Palette.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (Palette.Visibility == Visibility.Visible)
        {
            PaletteBox.Text = "";
            FillPalette("");
            PaletteBox.Focus(FocusState.Programmatic);
        }
        args.Handled = true;
    }

    void OnPaletteEscape(Microsoft.UI.Xaml.Input.KeyboardAccelerator s, Microsoft.UI.Xaml.Input.KeyboardAcceleratorInvokedEventArgs a) { Palette.Visibility = Visibility.Collapsed; a.Handled = true; }
    void OnPaletteBackdrop(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => Palette.Visibility = Visibility.Collapsed;
    void OnPaletteInner(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => e.Handled = true; // don't dismiss when clicking the box

    void OnPaletteQuery(object sender, TextChangedEventArgs e) => FillPalette(PaletteBox.Text);

    void FillPalette(string query)
    {
        var interactiveByCategory = _interactive.ToLookup(i => i.Category);
        var replaced = _interactive.SelectMany(i => i.Replaces).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var items = new List<PaletteItem>();
        // Interactive pages first (they're the richer tools), then the shared-view tools.
        foreach (var page in _interactive)
        {
            var name = Strings.Get(page.NameKey);
            if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
                items.Add(new PaletteItem(name, () => ShowPage(page.Make)));
        }
        foreach (var tool in Catalog.Search(query))
            if (!replaced.Contains(tool.Id))
                items.Add(new PaletteItem(tool.Name, () => OpenTool(tool)));

        PaletteResults.ItemsSource = items.Take(40).ToList();
    }

    void OnPaletteInvoke(object sender, ItemClickEventArgs e)
    {
        Palette.Visibility = Visibility.Collapsed;
        if (e.ClickedItem is PaletteItem item) item.Open();
    }

    void ShowPage(Func<UserControl> make)
    {
        // Record the open for the usage stats: the tool id it replaces, else the page's own key.
        var entry = _interactive.FirstOrDefault(i => i.Make == make);
        Usage.Record(entry.Replaces?.FirstOrDefault() ?? entry.NameKey);

        if (!_pageCache.TryGetValue(make, out var page)) _pageCache[make] = page = make();
        Host.Content = page;
    }

    // Changing the language rebuilds the whole UI in the new culture: cached pages are dropped so
    // they re-render, the sidebar relabels, and the home page comes back fresh.
    void ApplyLanguage(string? code)
    {
        Settings.Language = code;
        _pageCache.Clear();
        ShowTools(Catalog.Tools);
        Host.Content = new HomePage(OpenTool);
    }

    // Opening a tool anywhere (nav or a home-page button) records it and shows the shared view.
    void OpenTool(Tool tool)
    {
        Settings.PushRecent(tool.Id);
        Usage.Record(tool.Id);
        _toolView.Show(tool);
        Host.Content = _toolView;
    }

    // Segoe Fluent glyph per category — the icons that make the sidebar read like Settings.
    static readonly Dictionary<string, string> CategoryGlyphs = new()
    {
        ["Text"] = "", ["Encoding"] = "", ["Hashing"] = "", ["Developer"] = "",
        ["Colors"] = "", ["Conversions"] = "", ["Dates"] = "", ["Maths"] = "",
        ["Random"] = "", ["Files"] = "", ["Images"] = "", ["Everyday"] = "",
    };
    static readonly FontFamily FluentIcons = new("Segoe Fluent Icons"); // one shared instance, not one per item
    static IconElement Glyph(string g) => new FontIcon { Glyph = g, FontFamily = FluentIcons };

    // Does an interactive page's localized name contain the search query? (empty query = show all)
    static bool PageMatches(string nameKey, string query) =>
        query.Length == 0 || Strings.Get(nameKey).Contains(query, StringComparison.OrdinalIgnoreCase);

    void ShowTools(IEnumerable<Tool> tools, string query = "", bool expand = false)
    {
        Nav.MenuItems.Clear();
        Nav.MenuItems.Add(new NavigationViewItem { Content = Strings.Get("Home_Title"), Icon = Glyph(""), Tag = (Func<UserControl>)BuildHome });

        // Each category is one expandable item with an icon; its tools are children — collapses 108
        // tools into 13 tidy sections instead of a wall.
        var interactiveByCategory = _interactive.Where(i => PageMatches(i.NameKey, query)).ToLookup(i => i.Category);
        var toolsByCategory = tools.GroupBy(t => t.Category).ToDictionary(g => g.Key, g => g.ToList());
        // Tools a rich page replaces are hidden from the sidebar — no redundant text-box version.
        var replaced = _interactive.SelectMany(i => i.Replaces).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var cat in Catalog.Tools.Select(t => t.Category).Distinct())
        {
            var catTools = (toolsByCategory.TryGetValue(cat, out var list) ? list : []).Where(t => !replaced.Contains(t.Id)).ToList();
            var catPages = interactiveByCategory[cat].ToList();
            if (catTools.Count == 0 && catPages.Count == 0) continue; // nothing matched in this category
            var category = new NavigationViewItem
            {
                Content = Strings.Get($"Category_{cat}"),
                Icon = Glyph(CategoryGlyphs.GetValueOrDefault(cat, "")),
                SelectsOnInvoked = false,        // clicking the header expands, never "runs" it
                IsExpanded = expand,
            };
            var glyph = CategoryGlyphs.GetValueOrDefault(cat, "");
            foreach (var tool in catTools)
                category.MenuItems.Add(new NavigationViewItem { Content = tool.Name, Icon = Glyph(glyph), Tag = tool });
            foreach (var page in interactiveByCategory[cat])
                category.MenuItems.Add(new NavigationViewItem { Content = Strings.Get(page.NameKey), Icon = Glyph(glyph), Tag = page.Make });
            Nav.MenuItems.Add(category);
        }
    }

    // Rebuilding the whole sidebar on every keystroke is the one visibly slow path; debounce so a
    // burst of typing reflows the tree once it settles, not per character.
    Microsoft.UI.Dispatching.DispatcherQueueTimer? _searchTimer;

    void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs e)
    {
        _searchTimer ??= CreateSearchTimer();
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    Microsoft.UI.Dispatching.DispatcherQueueTimer CreateSearchTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(120);
        timer.IsRepeating = false;
        // Expand everything while searching so matches aren't hidden inside collapsed categories.
        timer.Tick += (_, _) => ShowTools(Catalog.Search(Search.Text), Search.Text, Search.Text.Length > 0);
        return timer;
    }

    void OnToolSelected(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.IsSettingsSelected) { Host.Content = new SettingsPage(ApplyTheme, ApplyLanguage); return; }
        switch ((e.SelectedItem as NavigationViewItem)?.Tag)
        {
            case Tool tool:
                OpenTool(tool);
                break;
            case Func<UserControl> make:
                // Cache the page: reopening is instant and state (a running timer, a picked colour) sticks.
                ShowPage(make);
                break;
        }
    }
}
