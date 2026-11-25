using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.16.4：一键更新版本 —— 未匹配到新版 mods 时的询问窗口（Win10 设置对话框形态）：
/// 「保留旧版本」/「不安装此 mod」二选一，可勾选「对余下未匹配项全部执行此操作」
/// （= 全部执行；不勾 = 仅当前）。
/// </summary>
public static class UpdateModAskDialog
{
    public static (VersionUpdateService.ModChoice Choice, bool ApplyAll) Show(Window? owner, string modFile, string reason)
    {
        var choice = VersionUpdateService.ModChoice.KeepOld;
        var applyAll = false;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = Find("S.Msu.AskTitle") ?? "未匹配到兼容的新版本 mod",
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ExtendsContentIntoTitleBar = true,
        };
        if (owner?.IsLoaded == true) win.Owner = owner; else win.Owner = App.Current.MainWindow;

        var txtFile = new TextBlock
        {
            Text = modFile,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 2),
        };
        var txtReason = new TextBlock
        {
            Text = reason,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 12),
        };
        var cbAll = new CheckBox
        {
            Content = Find("S.Msu.AskApplyAll") ?? "对余下未匹配项全部执行此操作",
            Margin = new Thickness(0, 4, 0, 14),
        };

        var btnKeep = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Msu.AskKeep") ?? "保留旧版本",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinWidth = 120,
            MinHeight = 32,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var btnSkip = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Msu.AskSkip") ?? "不安装此 mod",
            MinWidth = 120,
            MinHeight = 32,
        };

        btnKeep.Click += (_, _) => { choice = VersionUpdateService.ModChoice.KeepOld; applyAll = cbAll.IsChecked == true; win.Close(); };
        btnSkip.Click += (_, _) => { choice = VersionUpdateService.ModChoice.Skip; applyAll = cbAll.IsChecked == true; win.Close(); };

        win.Content = new Border
        {
            Padding = new Thickness(24, 46, 24, 20),
            Child = new StackPanel
            {
                Children =
                {
                    txtFile, txtReason, cbAll,
                    new StackPanel { Orientation = Orientation.Horizontal, Children = { btnKeep, btnSkip } },
                },
            },
        };

        win.ShowDialog();
        return (choice, applyAll);
    }

    private static string? Find(string key) => App.Current.TryFindResource(key) as string;
}
