// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Win32;
using NetBindPro.Core.Services;
using NetBindPro.UI.ViewModels;

namespace NetBindPro.UI.Views;

public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _vm;
    private readonly ProfileService    _profile;

    public SettingsPage(SettingsViewModel vm, ProfileService profile)
    {
        _vm      = vm;
        _profile = profile;
        DataContext = vm;
        InitializeComponent();
    }

    private void OpenTemp_Click(object sender, RoutedEventArgs e)
        => Process.Start("explorer.exe", Path.GetTempPath());

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter   = "JSON files (*.json)|*.json",
            FileName = "netbindpro-profile.json",
            Title    = "Export Profile"
        };
        if (dlg.ShowDialog() != true) return;

        var src = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetBindPro", "profile.json");

        if (File.Exists(src))
        {
            File.Copy(src, dlg.FileName, overwrite: true);
            MessageBox.Show("Profile exported successfully.", "NetBind Pro",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("No profile file found to export.", "NetBind Pro",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title  = "Import Profile"
        };
        if (dlg.ShowDialog() != true) return;

        var confirm = MessageBox.Show(
            "Importing will overwrite your current rules and settings. Continue?",
            "Confirm Import", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var dest = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetBindPro", "profile.json");

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(dlg.FileName, dest, overwrite: true);

        MessageBox.Show("Profile imported. Restart NetBind Pro to apply changes.", "NetBind Pro",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Hyperlink_Navigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
