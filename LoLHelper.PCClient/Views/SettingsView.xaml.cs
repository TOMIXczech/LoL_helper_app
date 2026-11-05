using System;
using System.Windows;
using System.Windows.Controls;
using LoLHelper.Core.Services;

namespace LoLHelper.PCClient.Views;

public partial class SettingsView : UserControl
{
    private string? _selectedBackgroundImage = null;

    public SettingsView()
    {
        InitializeComponent();
    }

    private void BrowseBgBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image Files|*.jpg;*.jpeg;*.png;*.webp",
            Title = "Select League Client Background"
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedBackgroundImage = dialog.FileName;
            SelectedBgText.Text = System.IO.Path.GetFileName(_selectedBackgroundImage);
        }
    }

    private async void InstallElainaBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var pengu = new PenguService();
            await pengu.InstallElainaV4Async(_selectedBackgroundImage);
            MessageBox.Show("Theme generated and installed successfully!\n\n(Restart your League Client to see changes if Pengu Loader is active).", 
                            "Pengu Loader", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error installing theme: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
