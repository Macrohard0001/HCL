using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 角色选择弹窗（v0.1.12 多角色直切）：第三方验证站同一账号下有多个角色时，
/// 账号管理里凭 accessToken 直接 refresh 绑定其他角色，无需重新输入密码（PCL 同款体验）。
/// 代码构建设计：与 ModPresetWindow / AccountManager 一致，随 Fluent 主题深浅色。
/// </summary>
public static class RolePickerWindow
{
    /// <summary>弹出角色选择；返回选中角色，取消返回 null。</summary>
    public static StoredProfile? Show(Window? owner, IReadOnlyList<StoredProfile> profiles,
        string? currentRoleName = null)
    {
        StoredProfile? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = App.Current.TryFindResource("S.Acct.SwitchRole") as string ?? "切换角色",
            Width = 400,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;
        // Esc 关闭（PCL 同款）
        win.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        var tip = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Acct.SwitchRoleTip") as string
                   ?? "同一验证站账号下的多个角色可直接切换，无需重新登录。",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10),
        };
        tip.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(tip);

        var list = new ListBox { MinHeight = 160 };
        foreach (var p in profiles)
        {
            var row = new ListBoxItem { Content = p.Name, Tag = p };
            if (!string.IsNullOrWhiteSpace(currentRoleName) && p.Name == currentRoleName)
            {
                row.Content = p.Name + "  (" + (App.Current.TryFindResource("S.Acct.Current") as string ?? "当前") + ")";
                row.IsSelected = true;
            }
            list.Items.Add(row);
        }
        root.Children.Add(list);

        var ok = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Authlib.Confirm") as string ?? "确定",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 30, MinWidth = 120, Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsEnabled = list.SelectedItem != null,
        };
        list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedItem != null;
        ok.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem it && it.Tag is StoredProfile p) result = p;
            win.Close();
        };
        root.Children.Add(ok);

        FluentDialog.SetContent(win, root);
        win.ShowDialog();
        return result;
    }
}
