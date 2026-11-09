using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 通用文本输入弹窗（v0.1.5）：预设命名等轻量输入场景。
/// 打开即聚焦全选，回车确认、Esc 取消；非空才允许确认。
/// </summary>
public static class PromptWindow
{
    public static string? Show(Window? owner, string title, string placeholder, string? initial = null,
        string? confirmText = null)
    {
        string? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = title,
            Width = 440,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(20) };

        var caption = new TextBlock { Text = placeholder, FontSize = 13.5, TextWrapping = TextWrapping.Wrap };
        caption.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var tb = new Wpf.Ui.Controls.TextBox { MinHeight = 36, Margin = new Thickness(0, 12, 0, 0), MaxLength = 48 };
        if (!string.IsNullOrEmpty(initial)) tb.Text = initial;

        var err = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        err.SetResourceReference(TextBlock.ForegroundProperty, "SystemFillColorCriticalBrush");

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = confirmText ?? (App.Current.TryFindResource("S.Common.Confirm") as string ?? "确定"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 34, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Common.Cancel") as string ?? "取消",
            MinHeight = 34, MinWidth = 88,
        };
        buttons.Children.Add(btnOk);
        buttons.Children.Add(btnCancel);
        root.Children.Add(caption);
        root.Children.Add(tb);
        root.Children.Add(err);
        root.Children.Add(buttons);

        void Confirm()
        {
            var text = tb.Text.Trim();
            if (text.Length == 0)
            {
                err.Text = App.Current.TryFindResource("S.Prompt.Empty") as string ?? "内容不能为空。";
                err.Visibility = Visibility.Visible;
                return;
            }
            result = text;
            win.Close();
        }

        btnOk.Click += (_, _) => Confirm();
        btnCancel.Click += (_, _) => win.Close();
        tb.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) Confirm();
            else if (e.Key == System.Windows.Input.Key.Escape) win.Close();
        };
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape) win.Close();
        };

        LoadedHandler(tb);
        static void LoadedHandler(Wpf.Ui.Controls.TextBox box)
        {
            box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        }

        Services.FluentDialog.SetContent(win, root);
        win.ShowDialog();
        return result;
    }
}
