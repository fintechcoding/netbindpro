// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetBindPro.Core.Models;
using NetBindPro.Core.Services;

namespace NetBindPro.UI.Views;

/// <summary>
/// A small modal that lists the available network adapters (VPN/tunnel adapters first)
/// and returns the one the user picks. Built in code so it needs no XAML/DI wiring —
/// used by the Processes page's one-off "Inject" action.
/// </summary>
internal sealed class AdapterPickerWindow : Window
{
    private readonly ListBox _list = new()
    {
        Margin = new Thickness(16, 8, 16, 8),
        BorderThickness = new Thickness(1),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
    };

    private AdapterPickerWindow(string processName)
    {
        Title = "Choose adapter";
        Width = 420;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brushes.White;
        ResizeMode = ResizeMode.NoResize;

        var root = new DockPanel { LastChildFill = true };

        var header = new TextBlock
        {
            Text = $"Bind \"{processName}\" to:",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)),
            Margin = new Thickness(16, 16, 16, 0),
        };
        DockPanel.SetDock(header, Dock.Top);

        foreach (var a in AdapterDiscovery.GetAll().OrderByDescending(x => x.IsVpnLike).ThenBy(x => x.Name))
        {
            var ip = string.IsNullOrEmpty(a.IpV4Address) ? "no IPv4" : a.IpV4Address;
            _list.Items.Add(new ListBoxItem
            {
                Content = $"{a.Name}   —   {ip}",
                Tag = a,
                Padding = new Thickness(8, 6, 8, 6),
                FontSize = 13,
            });
        }
        if (_list.Items.Count > 0) _list.SelectedIndex = 0;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(16, 0, 16, 16),
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };

        var ok = new Button { Content = "Inject", Padding = new Thickness(16, 6, 16, 6), MinWidth = 80, IsDefault = true };
        ok.Click += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: AdapterInfo })
            {
                DialogResult = true;
                Close();
            }
        };
        _list.MouseDoubleClick += (_, _) => { if (_list.SelectedItem is ListBoxItem { Tag: AdapterInfo }) { DialogResult = true; Close(); } };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);

        root.Children.Add(header);
        root.Children.Add(buttons);
        root.Children.Add(_list);
        Content = root;
    }

    /// <summary>Shows the picker and returns the chosen adapter, or <c>null</c> if cancelled.</summary>
    public static AdapterInfo? Pick(Window? owner, string processName)
    {
        var dlg = new AdapterPickerWindow(processName) { Owner = owner };
        return dlg.ShowDialog() == true && dlg._list.SelectedItem is ListBoxItem { Tag: AdapterInfo a }
            ? a
            : null;
    }
}
