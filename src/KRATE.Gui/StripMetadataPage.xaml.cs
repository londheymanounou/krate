using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using Windows.Storage.Pickers;
using WinRT.Interop;
using Krate.Core;
using Krate.Core.Tools;

namespace Krate.Gui;

public sealed partial class StripMetadataPage : UserControl
{
    private string _inputFile = string.Empty;
    private readonly IntPtr _hwnd;

    public StripMetadataPage(IntPtr hwnd)
    {
        this.InitializeComponent();
        _hwnd = hwnd;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
    }

    private async void OnSelectImage(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _inputFile = file.Path;
            SelectedFileText.Text = file.Path;
            StripBtn.IsEnabled = true;
            ResultText.Text = string.Empty;
        }
    }

    private async void OnStrip(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_inputFile)) return;

        var picker = new FileSavePicker();
        InitializeWithWindow.Initialize(picker, _hwnd);
        picker.SuggestedFileName = Path.GetFileNameWithoutExtension(_inputFile) + "_clean" + Path.GetExtension(_inputFile);
        picker.FileTypeChoices.Add("Image File", new[] { Path.GetExtension(_inputFile).ToLowerInvariant() });

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            StripBtn.IsEnabled = false;
            ResultText.Text = "Stripping...";

            try
            {
                var result = Files.StripMetadata($"\"{_inputFile}\"|\"{file.Path}\"");
                ResultText.Text = "Done! Saved to: " + file.Path;
            }
            catch (Exception ex)
            {
                ResultText.Text = "Error: " + ex.Message;
            }
            finally
            {
                StripBtn.IsEnabled = true;
            }
        }
    }
}
