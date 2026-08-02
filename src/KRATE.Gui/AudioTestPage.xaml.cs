using System.Runtime.InteropServices.WindowsRuntime;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Krate.Gui;

/// <summary>Left / right channel tester: plays a tone in one channel so you can check which speaker or
/// earbud is which. The tone is a stereo WAV built in memory (signal only in the chosen channel) and
/// played with MediaPlayer — no audio dependency, no files on disk.</summary>
public sealed partial class AudioTestPage : UserControl
{
    readonly MediaPlayer _player = new();

    public AudioTestPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Audio_Title");
        Subtitle.Text = Strings.Get("Audio_Subtitle");
        LeftLabel.Text = "◀  " + Strings.Get("Audio_Left");
        RightLabel.Text = Strings.Get("Audio_Right") + "  ▶";
        BothBtn.Content = Strings.Get("Audio_Both");
        _player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => { Highlight(null); Status.Text = ""; });
    }

    void OnUnloaded(object sender, RoutedEventArgs e) => _player.Pause(); // stop the tone when navigating away

    void OnLeft(object sender, RoutedEventArgs e) => Play(true, false, LeftBtn, "Audio_PlayingLeft");
    void OnRight(object sender, RoutedEventArgs e) => Play(false, true, RightBtn, "Audio_PlayingRight");
    void OnBoth(object sender, RoutedEventArgs e) => Play(true, true, BothBtn, "Audio_PlayingBoth");

    async void Play(bool left, bool right, Button active, string statusKey)
    {
        Highlight(active);
        Status.Text = Strings.Get(statusKey);
        try
        {
            var wav = MakeTone(left, right, freq: 440, seconds: 1.5);
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(wav.AsBuffer());
            stream.Seek(0);
            _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            _player.Play();
        }
        catch (Exception ex) { Highlight(null); Status.Text = ex.Message; }
    }

    void Highlight(Button? active)
    {
        foreach (var b in new[] { LeftBtn, RightBtn, BothBtn }) b.ClearValue(BackgroundProperty);
        if (active is not null) active.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
    }

    // A stereo 16-bit PCM WAV of a sine tone, with the signal only in the requested channel(s).
    static byte[] MakeTone(bool left, bool right, double freq, double seconds)
    {
        const int rate = 44100, channels = 2, bytesPerSample = 2;
        var frames = (int)(rate * seconds);
        var dataSize = frames * channels * bytesPerSample;
        var fade = rate / 20; // 50 ms fade in/out so the tone doesn't click

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                                  // fmt chunk size
        w.Write((short)1);                            // PCM
        w.Write((short)channels);
        w.Write(rate);
        w.Write(rate * channels * bytesPerSample);    // byte rate
        w.Write((short)(channels * bytesPerSample));  // block align
        w.Write((short)(bytesPerSample * 8));         // bits per sample
        w.Write("data"u8.ToArray());
        w.Write(dataSize);

        for (var i = 0; i < frames; i++)
        {
            var env = i < fade ? (double)i / fade : i > frames - fade ? (double)(frames - i) / fade : 1.0;
            var sample = (short)(0.3 * short.MaxValue * env * Math.Sin(2 * Math.PI * freq * i / rate));
            w.Write(left ? sample : (short)0);
            w.Write(right ? sample : (short)0);
        }
        w.Flush();
        return ms.ToArray();
    }
}
