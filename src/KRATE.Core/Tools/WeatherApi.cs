using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Krate.Core;

public record WeatherInfo(string Location, double TempC, double ApparentC, int Humidity, double WindKmh, string Icon, string Description);

public static class WeatherApi
{
    record GeoResult(double latitude, double longitude, string name, string country);
    record GeoResponse(GeoResult[] results);

    record CurrentWeather(double temperature_2m, double apparent_temperature, int relative_humidity_2m, int is_day, int weather_code, double wind_speed_10m);
    record WeatherResponse(CurrentWeather current);

    public static async Task<WeatherInfo> GetAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) query = "Paris"; // Fallback to a default or we could use IP geolocation, but for simplicity...

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        
        // 1. Geocoding
        var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=1&language=en&format=json";
        var geoJson = await client.GetStringAsync(geoUrl);
        var geo = JsonSerializer.Deserialize<GeoResponse>(geoJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        if (geo?.results is null || geo.results.Length == 0)
            throw new Exception($"City not found: {query}");
            
        var loc = geo.results[0];

        // 2. Weather
        var wxUrl = $"https://api.open-meteo.com/v1/forecast?latitude={loc.latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={loc.longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}&current=temperature_2m,relative_humidity_2m,apparent_temperature,is_day,weather_code,wind_speed_10m&timezone=auto";
        var wxJson = await client.GetStringAsync(wxUrl);
        var wx = JsonSerializer.Deserialize<WeatherResponse>(wxJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        if (wx?.current is null)
            throw new Exception("Could not fetch weather data.");

        var c = wx.current;
        var (icon, desc) = GetWmoCode(c.weather_code, c.is_day == 1);

        return new WeatherInfo($"{loc.name}, {loc.country}", c.temperature_2m, c.apparent_temperature, c.relative_humidity_2m, c.wind_speed_10m, icon, desc);
    }

    static (string Icon, string Desc) GetWmoCode(int code, bool day) => code switch
    {
        0 => (day ? "☀️" : "🌙", "Clear sky"),
        1 => (day ? "🌤️" : "☁️", "Mainly clear"),
        2 => ("⛅", "Partly cloudy"),
        3 => ("☁️", "Overcast"),
        45 or 48 => ("🌫️", "Fog"),
        51 or 53 or 55 => ("🌧️", "Drizzle"),
        56 or 57 => ("🌧️❄️", "Freezing Drizzle"),
        61 or 63 or 65 => ("🌧️", "Rain"),
        66 or 67 => ("🌧️❄️", "Freezing Rain"),
        71 or 73 or 75 => ("❄️", "Snow fall"),
        77 => ("❄️", "Snow grains"),
        80 or 81 or 82 => ("🌦️", "Rain showers"),
        85 or 86 => ("🌨️", "Snow showers"),
        95 => ("⛈️", "Thunderstorm"),
        96 or 99 => ("⛈️", "Thunderstorm with hail"),
        _ => ("❓", "Unknown")
    };
}
