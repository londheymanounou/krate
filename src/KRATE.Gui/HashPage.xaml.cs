using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using System;

namespace Krate.Gui;

/// <summary>All four digests of a text at once, each with a copy button — over Core's <see cref="Hashing"/>.</summary>
public sealed partial class HashPage : UserControl
{
    public HashPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_HashAll_Name");
        InputLabel.Text = Strings.Get("Gui_Input");
        foreach (var b in new[] { Md5Copy, Sha1Copy, Sha256Copy, Sha512Copy }) b.Content = Strings.Get("Gui_Copy");
        Md5Copy.Tag = Md5; Sha1Copy.Tag = Sha1; Sha256Copy.Tag = Sha256; Sha512Copy.Tag = Sha512;
        Compute();
    }

    void OnChanged(object sender, TextChangedEventArgs e) => Compute();

    async void Compute()
    {
        var text = Input.Text;
        var cleanPath = text.Trim(' ', '"', '\r', '\n');
        
        if (System.IO.File.Exists(cleanPath))
        {
            Md5.Text = "Computing...";
            Sha1.Text = "Computing...";
            Sha256.Text = "Computing...";
            Sha512.Text = "Computing...";
            
            try
            {
                await System.Threading.Tasks.Task.Run(() =>
                {
                    var m = Hashing.Md5File(cleanPath);
                    var s1 = Hashing.Sha1File(cleanPath);
                    var s256 = Hashing.Sha256File(cleanPath);
                    var s512 = Hashing.Sha512File(cleanPath);
                    
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        Md5.Text = m;
                        Sha1.Text = s1;
                        Sha256.Text = s256;
                        Sha512.Text = s512;
                    });
                });
                return;
            }
            catch { /* fallback to string hash if file is locked */ }
        }

        Md5.Text = Hashing.Md5(text);
        Sha1.Text = Hashing.Sha1(text);
        Sha256.Text = Hashing.Sha256(text);
        Sha512.Text = Hashing.Sha512(text);
    }

    void OnCopy(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not TextBox box) return;
        var data = new DataPackage();
        data.SetText(box.Text);
        Clipboard.SetContent(data);
    }

    async void OnPickFile(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow ?? throw new Exception());
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        if (await picker.PickSingleFileAsync() is { } file)
        {
            Input.Text = file.Path;
        }
    }
}
