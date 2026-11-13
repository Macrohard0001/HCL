using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 离线登录用户名输入窗口（登录系统全面检查）：
/// - 打开即聚焦输入框，回车确认、Esc 取消；
/// - 每次输入实时校验（3-16 位，仅字母 / 数字 / 下划线），
///   非法原因就地红字提示，确定按钮仅在合法时可用；
/// - 传入 initialName 可预填（如当前离线账号名）。
/// 此前用通用 Prompt + 静默丢弃非法输入：填错后点确定毫无反应，
/// 看起来像"按钮坏了"，这正是离线登录用户名填写问题的根因。
/// </summary>
public static class OfflineNameWindow
{
    public static string? Show(Window? owner, string? initialName = null)
    {
        string? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = App.Current.TryFindResource("S.Home.OfflineLogin") as string ?? "离线登录",
            Width = 440,
            Height = 230,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win))
            win.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(20) };

        var caption = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Home.OfflineName") as string ?? "玩家名",
            FontSize = 14,
        };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var tb = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 32,
            Margin = new Thickness(0, 12, 0, 6),
            MaxLength = 64, // 允许粘贴超长名字以便给出"过长"提示，而不是被输入框截断
            Text = initialName ?? "",
        };
        tb.SetResourceReference(Wpf.Ui.Controls.TextBox.PlaceholderTextProperty, "S.Login.NameRule");

        // 就地错误提示（红字）；合法时隐藏
        var err = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            // WPF-UI 的关键色刷子缺失时回退固定红色，避免错误提示渲染成默认黑字
            Foreground = App.Current.TryFindResource("SystemFillColorCriticalBrush") as System.Windows.Media.Brush
                ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC4, 0x2B, 0x1C)),
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Login.Cancel") as string ?? "取消",
            MinHeight = 32, MinWidth = 96, Margin = new Thickness(0, 0, 8, 0),
        };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Login.Ok") as string ?? "确定",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 32, MinWidth = 96,
        };
        buttons.Children.Add(btnCancel);
        buttons.Children.Add(btnOk);

        root.Children.Add(caption);
        root.Children.Add(tb);
        root.Children.Add(err);
        root.Children.Add(buttons);

        void SetError(string key, string fallback)
        {
            err.Text = App.Current.TryFindResource(key) as string ?? fallback;
            err.Visibility = Visibility.Visible;
        }

        void Validate()
        {
            var (_, e) = AuthService.ValidateOfflineName(tb.Text);
            switch (e)
            {
                case AuthService.OfflineNameError.None:
                    err.Visibility = Visibility.Collapsed;
                    break;
                case AuthService.OfflineNameError.Empty:
                    err.Visibility = Visibility.Collapsed; // 还没填就先不吵
                    break;
                case AuthService.OfflineNameError.TooShort: SetError("S.Login.NameTooShort", "用户名至少需要 3 个字符。"); break;
                case AuthService.OfflineNameError.TooLong: SetError("S.Login.NameTooLong", "用户名最多 16 个字符。"); break;
                case AuthService.OfflineNameError.InvalidChars: SetError("S.Login.NameInvalidChars", "只能使用字母、数字和下划线。"); break;
            }
            btnOk.IsEnabled = e == AuthService.OfflineNameError.None;
        }

        void Confirm()
        {
            if (!btnOk.IsEnabled) return;
            var (name, _) = AuthService.ValidateOfflineName(tb.Text);
            if (name == null) return;
            result = name;
            win.Close();
        }

        tb.TextChanged += (_, _) => Validate();
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; Confirm(); }
            else if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; win.Close(); }
        };
        btnOk.Click += (_, _) => Confirm();
        btnCancel.Click += (_, _) => win.Close();

        FluentDialog.SetContent(win, root); // 挂标题栏（含关闭按钮），与其它弹窗一致
        Validate(); // 预填名字时同步初始按钮状态
        win.ShowDialog();
        return result;
    }
}
