using System;
using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Krate.Gui;

public sealed partial class WeatherPage : UserControl
{
    public WeatherPage()
    {
        InitializeComponent();
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) OnSearch(sender, null);
    }

    async void OnSearch(object sender, RoutedEventArgs? e)
    {
        var city = CityInput.Text.Trim();
        ResultCard.Visibility = Visibility.Collapsed;
        ErrorOutput.Visibility = Visibility.Collapsed;
        Loading.IsActive = true;
        Loading.Visibility = Visibility.Visible;

        try
        {
            var info = await Krate.Core.WeatherApi.GetAsync(city);
            
            LocName.Text = info.Location;
            WxIcon.Text = info.Icon;
            WxTemp.Text = $"{info.TempC:0.#}°C";
            WxDesc.Text = info.Description;
            
            WxApparent.Text = $"{info.ApparentC:0.#}°C";
            WxHumid.Text = $"{info.Humidity}%";
            WxWind.Text = $"{info.WindKmh:0.#} km/h";

            ResultCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ErrorOutput.Text = $"Error: {ex.Message}";
            ErrorOutput.Visibility = Visibility.Visible;
        }
        finally
        {
            Loading.IsActive = false;
            Loading.Visibility = Visibility.Collapsed;
        }
    }
}
