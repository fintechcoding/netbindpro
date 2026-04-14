// Copyright (c) 2024 NetBind Pro Contributors
// Licensed under the MIT License. See LICENSE in the project root for details.

using System.Windows;
using System.Windows.Controls;
using NetBindPro.UI.ViewModels;

namespace NetBindPro.UI.Views;

public partial class RulesPage : Page
{
    private readonly RulesViewModel _vm;

    public RulesPage(RulesViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => vm.Refresh();
    }

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var dialog = App.GetService<AddRuleWindow>();
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true)
            _vm.Refresh();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _vm.Refresh();

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        => _vm.FilterText = SearchBox.Text;
}
