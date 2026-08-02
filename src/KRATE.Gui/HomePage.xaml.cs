using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Landing page: the app name plus the tools you opened most recently, one click to reopen.</summary>
public sealed partial class HomePage : UserControl
{
    public HomePage(Action<Tool> onOpen)
    {
        InitializeComponent();
        Title.Text = Strings.Get("App_Name");
        Subtitle.Text = Strings.Get("App_Tagline");

        var favorites = Section(FavLabel, FavPanel, "Home_Favorites", Settings.Favorites, onOpen);
        var recents = Section(RecentLabel, RecentPanel, "Home_Recent", Settings.Recents, onOpen);

        if (!favorites && !recents)
            Hint.Text = Strings.Get("Home_Empty");
    }

    // Fills a label+panel with cards for the tools in `ids`; hides both if there are none.
    // Returns whether anything was shown.
    static bool Section(TextBlock label, Panel panel, string labelKey, IEnumerable<string> ids, Action<Tool> onOpen)
    {
        var tools = ids.Select(Catalog.Find).OfType<Tool>().ToList();
        if (tools.Count == 0) { label.Visibility = Visibility.Collapsed; return false; }
        label.Text = Strings.Get(labelKey);
        foreach (var tool in tools) panel.Children.Add(Card(tool, onOpen));
        return true;
    }

    // A Settings-style list card: name on top, description muted below, chevron on the right.
    static Button Card(Tool tool, Action<Tool> onOpen)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = tool.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        text.Children.Add(new TextBlock { Text = tool.Description, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, FontSize = 12 });

        var chevron = new FontIcon { Glyph = "", FontSize = 12, Opacity = 0.6 }; // Segoe chevron-right

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(chevron, 1);
        chevron.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(text);
        row.Children.Add(chevron);

        var button = new Button
        {
            Content = row,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 12, 16, 12),
        };
        button.Click += (_, _) => onOpen(tool);
        return button;
    }
}
