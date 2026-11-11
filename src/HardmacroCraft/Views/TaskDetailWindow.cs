using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.10 任务详情弹窗（HMCL 式）：任务中心点击任务卡片后弹出，
/// 展示任务标题 / 状态 / 进度 / 当前详情 / 起止时间与耗时 / 全部进度日志（可复制）。
/// </summary>
public static class TaskDetailWindow
{
    public static void Show(Window? owner, DownloadTaskService.TaskItem task)
    {
        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = (App.Current.TryFindResource("S.Task.DetailTitle") as string ?? "任务详情") + " · " + task.Title,
            Width = 620,
            Height = 560,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0 标题
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1 元信息
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2 进度条
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 3 当前详情
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 4 日志标题
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 5 日志框
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 6 按钮

        // 标题 + 状态徽章
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = task.Title, FontSize = 17, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(title, 0);
        head.Children.Add(title);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 2, 10, 3),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        };
        var (statusText, statusBg, statusFg) = TaskCenterPage.StatusBadge(task);
        badge.Background = statusBg;
        badge.Child = new TextBlock { Text = statusText, FontSize = 12, Foreground = statusFg };
        Grid.SetColumn(badge, 1);
        head.Children.Add(badge);
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        // 元信息：起止时间 / 耗时
        var meta = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            Text = string.Format(
                App.Current.TryFindResource("S.Task.DetailMeta") as string
                    ?? "开始 {0:HH:mm:ss}{1} · 耗时 {2}",
                task.StartedAt,
                task.IsActive ? "" : $" → {task.EndedAt:HH:mm:ss}",
                FormatDuration(task)),
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        Grid.SetRow(meta, 1);
        root.Children.Add(meta);

        // 当前进度
        var bar = new ProgressBar
        {
            Height = 6, Minimum = 0, Maximum = 100,
            IsIndeterminate = task.IsActive && task.Percent < 0,
            Value = Math.Max(0, task.Percent), Margin = new Thickness(0, 0, 0, 6),
        };
        Grid.SetRow(bar, 2);
        root.Children.Add(bar);

        var detail = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(task.Detail) ? "-" : task.Detail,
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetRow(detail, 3);
        root.Children.Add(detail);

        // 进度日志
        var logHeader = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Task.DetailLog") as string ?? "任务日志",
            FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6),
        };
        logHeader.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetRow(logHeader, 4);
        root.Children.Add(logHeader);

        var logBox = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true, FontSize = 11.5, FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Text = string.Join(Environment.NewLine, task.LogSnapshot()),
        };
        if (logBox.Text.Length == 0)
            logBox.Text = App.Current.TryFindResource("S.Task.DetailNoLog") as string ?? "（暂无日志）";
        Grid.SetRow(logBox, 5);
        root.Children.Add(logBox);

        // 底部按钮：复制日志 / 关闭
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
        };
        var btnCopy = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Task.DetailCopy") as string ?? "复制日志",
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Copy20 },
            MinHeight = 34, MinWidth = 100, Margin = new Thickness(0, 0, 10, 0),
        };
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(logBox.Text); } catch { /* 剪贴板被占用时忽略 */ }
        };
        var btnClose = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Common.Close") as string ?? "关闭",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 34, MinWidth = 96,
        };
        btnClose.Click += (_, _) => win.Close();
        btnRow.Children.Add(btnCopy);
        btnRow.Children.Add(btnClose);
        Grid.SetRow(btnRow, 6);
        root.Children.Add(btnRow);

        FluentDialog.SetContent(win, root);
        win.ShowDialog();
    }

    private static string FormatDuration(DownloadTaskService.TaskItem task)
    {
        var el = task.Elapsed;
        return el.TotalMinutes < 1 ? $"{el.Seconds:0} 秒"
            : el.TotalHours < 1 ? $"{(int)el.TotalMinutes} 分 {el.Seconds:0} 秒"
            : $"{(int)el.TotalHours} 小时 {(int)(el.TotalMinutes % 60)} 分";
    }
}
