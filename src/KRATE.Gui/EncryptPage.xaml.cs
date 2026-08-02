using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace Krate.Gui;

/// <summary>Password-encrypt or decrypt a file. Pick a file, type a password, choose Encrypt or Decrypt.
/// All the crypto is Core's <see cref="Crypt"/> (AES-256 + HMAC, authenticated).</summary>
public sealed partial class EncryptPage : UserControl
{
    readonly nint _hwnd;
    string? _path;

    public EncryptPage(nint hwnd)
    {
        _hwnd = hwnd;
        InitializeComponent();
        Title.Text = Strings.Get("Encrypt_Title");
        Subtitle.Text = Strings.Get("Encrypt_Subtitle");
        ChooseBtn.Content = Strings.Get("Pick_ChooseFile");
        Pwd.PlaceholderText = Strings.Get("Encrypt_Password");
        ConfirmPwd.PlaceholderText = Strings.Get("Encrypt_Confirm");
        EncryptBtn.Content = Strings.Get("Encrypt_Do");
        DecryptBtn.Content = Strings.Get("Decrypt_Do");
    }

    async void OnChoose(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { FileTypeFilter = { "*" } };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, _hwnd);
        if (await picker.PickSingleFileAsync() is { } file) SetPath(file.Path);
    }

    void OnDragOver(object sender, DragEventArgs e) => e.AcceptedOperation = DataPackageOperation.Copy;

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0) SetPath(items[0].Path);
    }

    /// <summary>Preload a file (from the right-click menu).</summary>
    public void LoadFile(string path) => SetPath(path);

    void SetPath(string path)
    {
        _path = path;
        SelectedPath.Text = path;
        EncryptBtn.IsEnabled = DecryptBtn.IsEnabled = true;
        Status.IsOpen = false;
    }

    void OnEncrypt(object sender, RoutedEventArgs e)
    {
        if (Pwd.Password != ConfirmPwd.Password) { Report(InfoBarSeverity.Error, Strings.Get("Cli_PasswordMismatch")); return; }
        Run(Crypt.EncryptFile);
    }

    void OnDecrypt(object sender, RoutedEventArgs e) => Run(Crypt.DecryptFile); // no confirm needed to decrypt

    void Run(Func<string, string, string> op)
    {
        if (_path is null) return;
        try { Report(InfoBarSeverity.Success, op(_path, Pwd.Password)); }
        catch (Exception ex) { Report(InfoBarSeverity.Error, ex.Message); }
    }

    void Report(InfoBarSeverity severity, string message)
    {
        Status.Severity = severity;
        Status.Message = message;
        Status.IsOpen = true;
    }
}
