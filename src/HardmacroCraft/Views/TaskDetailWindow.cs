using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 任务详情弹窗（对齐主流启动器的任务详情/下载详情）：
/// 任务中心点击任务卡片后弹出，展示任务标题 / 状态徽章 / 大号进度条与百分比 /
/// 当前详情（速度 · 字节 · 剩余时间）/ 起止时间与耗时 / 全部进度日志（可复制）。
/// v0.1.11：
/// - 任务进行中时详情实时自动刷新（500ms 或跟随界面刷新频率设置），日志增量追加；
/// - 修复任务日志不能滚轮滚动：接管 TextBox 内部滚动条的滚轮路由；
/// - 滚轮滚动动画可选（Settings → 平滑滚动日志，默认开，走全局 SmoothScroll 引擎）。
/// </summary>
public static class TaskDetailWindow
{
    public static void Show(Window? owner, DownloadTaskService.TaskItem task)
    {
        string L(string key, string fallback) => App.Current.TryFindResource(key) as string ?? fallback;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = (L("S.Task.DetailTitle", "任务详情")) + " · " + task.Title,
            Width = 640,
            Height = 580,
            MinHeight = 420,
            MinWidth = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 0 标题
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 1 进度区
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 2 元信息
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 3 日志标题
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4 日志框
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // 5 按钮

        // 标题 + 状态徽章（实时刷新）
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
        Grid.SetColumn(badge, 1);
        head.Children.Add(badge);
        Grid.SetRow(head, 0);
        root.Children.Add(head);

        // 进度区（PCL 式）：大进度条 + 百分比大字 + 当前详情（速度 / 字节 / 剩余时间）
        var progressPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var barRow = new Grid();
        barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var bar = new ProgressBar
        {
            Height = 8, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        Grid.SetColumn(bar, 0);
        barRow.Children.Add(bar);
        var pctText = new TextBlock
        {
            FontSize = 16, FontWeight = FontWeights.SemiBold, MinWidth = 86,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        pctText.SetResourceReference(TextBlock.ForegroundProperty, "SystemAccentColorPrimaryBrush");
        Grid.SetColumn(pctText, 1);
        barRow.Children.Add(pctText);
        progressPanel.Children.Add(barRow);
        var detail = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(task.Detail) ? "-" : task.Detail,
            FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
        };
        detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        progressPanel.Children.Add(detail);
        Grid.SetRow(progressPanel, 1);
        root.Children.Add(progressPanel);

        // 元信息：起止时间 / 耗时（实时刷新）
        var meta = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        Grid.SetRow(meta, 2);
        root.Children.Add(meta);

        // 进度日志标题
        var logHeader = new TextBlock
        {
            Text = L("S.Task.DetailLog", "任务日志"),
            FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6),
        };
        logHeader.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetRow(logHeader, 3);
        root.Children.Add(logHeader);

        // 进度日志框（等宽、自动刷新增量追加、滚轮平滑滚动可选）
        var logBox = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true, FontSize = 11.5, FontFamily = new FontFamily("Consolas"),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 0, 0, 12),
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
        };
        Grid.SetRow(logBox, 4);
        root.Children.Add(logBox);

        // 底部按钮：取消（可取消任务）/ 复制日志 / 关闭
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (task.CanCancel)
        {
            var btnCancel = new Wpf.Ui.Controls.Button
            {
                Content = L("S.Common.Cancel", "取消"),
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss24 },
                MinHeight = 34, MinWidth = 92, Margin = new Thickness(0, 0, 10, 0),
            };
            btnCancel.Click += (_, _) =>
            {
                task.RequestCancel();
                btnCancel.IsEnabled = false;
            };
            btnRow.Children.Add(btnCancel);
        }
        var btnCopy = new Wpf.Ui.Controls.Button
        {
            Content = L("S.Task.DetailCopy", "复制日志"),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Copy20 },
            MinHeight = 34, MinWidth = 100, Margin = new Thickness(0, 0, 10, 0),
        };
        btnCopy.Click += (_, _) =>
        {
            try { Clipboard.SetText(logBox.Text); } catch { /* 剪贴板被占用时忽略 */ }
        };
        var btnClose = new Wpf.Ui.Controls.Button
        {
            Content = L("S.Common.Close", "关闭"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 34, MinWidth = 96,
        };
        btnClose.Click += (_, _) => win.Close();
        btnRow.Children.Add(btnCopy);
        btnRow.Children.Add(btnClose);
        Grid.SetRow(btnRow, 5);
        root.Children.Add(btnRow);

        FluentDialog.SetContent(win, root);

        // ---------- 实时自动刷新 ----------
        int _renderedLogs = 0;   // 已渲染的日志条数（增量追加）
        bool _followTail = true; // 用户没往上翻时自动跟随日志尾部

        void RenderLog()
        {
            var logs = task.LogSnapshot();
            if (logs.Count == _renderedLogs) return;
            // 用户往上翻过（不在底部）时暂停追加，避免打断阅读；回到底部后继续跟随
            _followTail = logBox.ExtentHeight <= 0
                || Math.Abs(logBox.ExtentHeight - logBox.ViewportHeight - logBox.VerticalOffset) < 24
                || _renderedLogs == 0;
            if (_renderedLogs == 0)
            {
                logBox.Text = logs.Count > 0 ? string.Join(Environment.NewLine, logs) : L("S.Task.DetailNoLog", "（暂无日志）");
            }
            else
            {
                for (var i = _renderedLogs; i < logs.Count; i++)
                    logBox.AppendText(logs[i] + Environment.NewLine);
            }
            _renderedLogs = logs.Count;
            if (_followTail) ScrollLogToBottom();
        }

        void ScrollLogToBottom()
        {
            var sv = FindTextBoxScrollViewer(logBox);
            if (sv == null) { logBox.ScrollToEnd(); return; }
            if (AppSettings.Instance.SmoothScrollLogs && logBox.Text.Length < 200_000)
                SmoothScroll.ScrollTo(sv, sv.ScrollableHeight);
            else
                sv.ScrollToBottom();
        }

        void RefreshUi()
        {
            // 状态徽章
            var (statusText, statusBg, statusFg) = TaskCenterPage.StatusBadge(task);
            badge.Background = statusBg;
            badge.Child = new TextBlock { Text = statusText, FontSize = 12, Foreground = statusFg };
            // 进度条 + 百分比
            if (task.IsActive && task.Percent < 0)
            {
                bar.IsIndeterminate = true;
                pctText.Text = "…";
            }
            else
            {
                bar.IsIndeterminate = false;
                bar.Value = Math.Max(0, task.Percent);
                pctText.Text = $"{Math.Max(0, task.Percent):0.#} %";
            }
            // 当前详情
            detail.Text = string.IsNullOrWhiteSpace(task.Detail) ? "-" : task.Detail;
            // 元信息（含实时耗时）
            meta.Text = string.Format(
                L("S.Task.DetailMeta", "开始 {0:HH:mm:ss}{1} · 耗时 {2}"),
                task.StartedAt,
                task.IsActive ? "" : $" → {task.EndedAt:HH:mm:ss}",
                FormatDuration(task));
            RenderLog();
        }

        var timer = new DispatcherTimer();
        timer.Tick += (_, _) => RefreshUi();
        var s = AppSettings.Instance;
        timer.Interval = TimeSpan.FromMilliseconds(
            UiRefreshPolicy.IsEventDriven(s.UiRefreshMode) ? 500
                : Math.Max(200, UiRefreshPolicy.PollIntervalMs(s.UiRefreshMode, s.UiRefreshFps)));
        win.Loaded += (_, _) => { RefreshUi(); timer.Start(); };
        win.Closed += (_, _) => timer.Stop();

        // ---------- 滚轮滚动修复（接管 TextBox 内部滚动条，支持可选平滑动画） ----------
        logBox.PreviewMouseWheel += (_, e) =>
        {
            var sv = FindTextBoxScrollViewer(logBox);
            if (sv == null) return;
            var delta = e.Delta;
            if (delta == 0) return;
            // 横向溢出优先横向滚（长路径日志）；否则纵向滚动
            if (sv.ScrollableWidth > 0 && sv.ExtentWidth > sv.ViewportWidth && sv.ScrollableHeight <= 0)
            {
                sv.ScrollToHorizontalOffset(sv.HorizontalOffset - delta);
            }
            else if (sv.ScrollableHeight > 0)
            {
                var step = Math.Max(48, sv.ViewportHeight / 3.0) * (delta > 0 ? -1 : 1);
                if (AppSettings.Instance.SmoothScrollLogs)
                    SmoothScroll.ScrollTo(sv, sv.VerticalOffset + step);
                else
                    sv.ScrollToVerticalOffset(sv.VerticalOffset + step);
            }
            else
            {
                return; // 无可滚动内容 → 交给上层处理
            }
            e.Handled = true;
        };

        win.ShowDialog();
    }

    /// <summary>取 TextBox 内部滚动条（PART_ContentHost）。WPF 的 TextBox 自身不处理滚轮，
    /// 某些宿主（Mica 窗口 / 触摸板驱动）会把滚轮路由吞掉 —— 我们显式接管（见 PreviewMouseWheel）。</summary>
    private static System.Windows.Controls.ScrollViewer? FindTextBoxScrollViewer(System.Windows.Controls.TextBox tb)
    {
        System.Windows.Controls.ScrollViewer? found = null;
        void Walk(System.Windows.DependencyObject d)
        {
            if (found != null) return;
            var n = VisualTreeHelper.GetChildrenCount(d);
            for (var i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                if (c is System.Windows.Controls.ScrollViewer sv) { found = sv; return; }
                Walk(c);
            }
        }
        Walk(tb);
        return found;
    }

    private static string FormatDuration(DownloadTaskService.TaskItem task)
    {
        var el = task.Elapsed;
        return el.TotalMinutes < 1 ? $"{el.Seconds:0} 秒"
            : el.TotalHours < 1 ? $"{(int)el.TotalMinutes} 分 {el.Seconds:0} 秒"
            : $"{(int)el.TotalHours} 小时 {(int)(el.TotalMinutes % 60)} 分";
    }
}
