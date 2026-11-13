using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>一键进服弹窗结果：地址 + 可选的「保存到多人游戏列表」名称（null 表示不保存）。</summary>
public sealed record JoinServerResult(string Address, string? SaveName);

/// <summary>
/// v0.1.6 一键进服弹窗（对齐主流启动器交互）：填服务器地址；
/// 复选框「保存到游戏的多人游戏列表」勾选后追加名称输入框（默认 Minecraft服务器）。
/// </summary>
public static class JoinServerWindow
{
    public static JoinServerResult? Show(Window? owner, string? presetAddress = null)
    {
        JoinServerResult? result = null;
        bool confirmed = false;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = Find("S.Join.Title") ?? "一键进服",
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
            Topmost = true,
        };
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>()
            .LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // 说明
        sp.Children.Add(MakeText(Find("S.Join.Hint") ?? "启动后将直接连入该服务器", 12,
            BrushesSecondary()));

        // 服务器地址
        sp.Children.Add(MakeText(Find("S.Join.Address") ?? "服务器地址", 13, BrushesPrimary(),
            top: 12, bottom: 6));
        var txtAddress = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 30,
            PlaceholderText = Find("S.Join.AddressPh") ?? "mc.example.com 或 mc.example.com:25565",
            Text = presetAddress ?? "",
        };
        sp.Children.Add(txtAddress);

        // 保存到多人游戏列表
        var chkSave = new CheckBox
        {
            Content = MakeText(Find("S.Join.Save") ?? "保存到游戏的多人游戏列表", 13, BrushesPrimary()),
            Margin = new Thickness(0, 14, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        sp.Children.Add(chkSave);

        // 名称输入框（勾选后显示，默认 Minecraft服务器）
        var pnlName = new StackPanel { Visibility = Visibility.Collapsed };
        pnlName.Children.Add(MakeText(Find("S.Join.SaveName") ?? "列表中的名称", 13, BrushesPrimary(),
            top: 10, bottom: 6));
        var txtName = new Wpf.Ui.Controls.TextBox { MinHeight = 32, Text = Find("S.Join.DefaultName") ?? "Minecraft服务器" };
        pnlName.Children.Add(txtName);
        sp.Children.Add(pnlName);
        chkSave.Checked += (_, _) => pnlName.Visibility = Visibility.Visible;
        chkSave.Unchecked += (_, _) => pnlName.Visibility = Visibility.Collapsed;

        // 按钮行
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Common.Cancel") ?? "取消",
            MinHeight = 32, MinWidth = 96, Margin = new Thickness(0, 0, 10, 0),
        };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Join.Ok") ?? "启动并进服",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 32, MinWidth = 120,
        };
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        sp.Children.Add(btnRow);

        void Confirm()
        {
            var addr = txtAddress.Text.Trim();
            if (addr.Length == 0)
            {
                txtAddress.Focus();
                return;
            }
            result = new JoinServerResult(addr,
                chkSave.IsChecked == true ? txtName.Text.Trim() : null);
            confirmed = true;
            win.Close();
        }

        btnOk.Click += (_, _) => Confirm();
        btnCancel.Click += (_, _) => win.Close();
        txtAddress.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) Confirm(); };
        FluentDialog.SetContent(win, sp);

        win.ShowDialog();
        return confirmed ? result : null;
    }

    private static string? Find(string key)
        => System.Windows.Application.Current.TryFindResource(key) as string;

    private static Brush BrushesPrimary() => (Brush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];
    private static Brush BrushesSecondary() => (Brush)System.Windows.Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static TextBlock MakeText(string text, double size, Brush brush,
        double top = 0, double bottom = 0)
        => new()
        {
            Text = text,
            FontSize = size,
            Foreground = brush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, top, 0, bottom),
        };
}
