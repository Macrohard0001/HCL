using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 任务中心（HMCL 式）：显示所有下载 / 安装 / 补全文件任务的进度与历史。
/// DispatcherTimer 轮询快照（500ms），避免为每条任务挂事件导致泄漏。
/// </summary>
public partial class TaskCenterPage : Page
{
    private readonly DispatcherTimer _timer;
    private RefreshThrottle? _throttle;

    public TaskCenterPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            ApplyRefreshPolicy();
        };
        Unloaded += (_, _) =>
        {
            _timer.Stop();
            DownloadTaskService.Instance.Changed -= OnTasksChanged;
        };
    }

    /// <summary>v0.1.11：刷新频率跟随「设置 → 界面刷新频率」（轮询 / 事件驱动 / 事件驱动限频）。</summary>
    private void ApplyRefreshPolicy()
    {
        var s = AppSettings.Instance;
        var mode = s.UiRefreshMode;
        DownloadTaskService.Instance.Changed -= OnTasksChanged;
        if (UiRefreshPolicy.IsEventDriven(mode))
        {
            // 事件驱动：停用轮询，任务状态变化时立刻（或限频后）刷新 —— 最省电
            _timer.Stop();
            _throttle = new RefreshThrottle(UiRefreshPolicy.EventMinIntervalMs(mode, s.UiRefreshMaxFps));
            DownloadTaskService.Instance.Changed += OnTasksChanged;
        }
        else
        {
            _throttle = null;
            var interval = UiRefreshPolicy.PollIntervalMs(mode, s.UiRefreshFps);
            if (interval <= 0) interval = 500;
            _timer.Interval = TimeSpan.FromMilliseconds(interval);
            _timer.Start();
        }
    }

    private void OnTasksChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnTasksChanged); return; }
        if (_throttle != null && !_throttle.TryRefresh(DateTime.Now)) return;
        Refresh();
    }

    private void Refresh()
    {
        var tasks = DownloadTaskService.Instance.Snapshot();
        PnlTasks.Children.Clear();
        if (tasks.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = (string)TryFindResource("S.Task.Empty") ?? "暂无任务",
                FontSize = 13, Margin = new Thickness(0, 24, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            PnlTasks.Children.Add(empty);
            return;
        }

        foreach (var t in tasks)
            PnlTasks.Children.Add(BuildRow(t));
    }

    private UIElement BuildRow(DownloadTaskService.TaskItem t)
    {
        var card = new Border
        {
            Style = (Style)App.Current.TryFindResource("HmCard")!,
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Cursor = System.Windows.Input.Cursors.Hand, // v0.1.10：可点击查看详情
        };
        // v0.1.10：点击卡片查看任务详细信息（HMCL 式）
        card.MouseLeftButtonUp += (_, _) => TaskDetailWindow.Show(Window.GetWindow(this), t);
        var sp = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock { Text = t.Title, FontSize = 14, FontWeight = FontWeights.SemiBold };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(title, 0);
        head.Children.Add(title);

        var (statusText, statusBg, statusFg) = StatusBadge(t);
        // 微软式状态徽章：Fluent 语义色 subtle 背景 + 语义色文字（不再用 Material 彩色药丸）
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 1, 8, 2),
            Background = statusBg,
            VerticalAlignment = VerticalAlignment.Center,
        };
        badge.Child = new TextBlock { Text = statusText, FontSize = 10.5, Foreground = statusFg };
        Grid.SetColumn(badge, 1);

        // fix7：可取消任务的「取消」按钮（PCL/HMCL 同款），替代状态徽章的位置
        if (t.CanCancel)
        {
            var cancel = new Wpf.Ui.Controls.Button
            {
                Content = App.Current.TryFindResource("S.Common.Cancel") as string ?? "取消",
                MinHeight = 28,
                Padding = new Thickness(10, 2, 10, 2),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Secondary,
            };
            cancel.Click += (_, _) => t.RequestCancel();
            Grid.SetColumn(cancel, 1);
            head.Children.Add(cancel);
        }
        else
        {
            head.Children.Add(badge);
        }
        sp.Children.Add(head);

        if (!string.IsNullOrEmpty(t.Detail))
        {
            var detail = new TextBlock
            {
                Text = t.Detail, FontSize = 12, Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            detail.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            sp.Children.Add(detail);
        }

        var bar = new ProgressBar
        {
            Height = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Minimum = 0, Maximum = 100,
            IsIndeterminate = t.IsActive && t.Percent < 0,
            Value = Math.Max(0, t.Percent),
        };
        sp.Children.Add(bar);

        var time = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
            Text = (t.Percent >= 0 ? $"{t.Percent:0.#}% · " : "")
                + t.StartedAt.ToString("HH:mm:ss")
                + (t.IsActive ? "" : "  →  " + t.EndedAt.ToString("HH:mm:ss")),
        };
        time.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        sp.Children.Add(time);

        card.Child = sp;
        return card;
    }

    /// <summary>状态徽章（供任务卡片与详情弹窗共用）。</summary>
    internal static (string, Brush, Brush) StatusBadge(DownloadTaskService.TaskItem t)
    {
        var res = App.Current.TryFindResource;
        return t.Status switch
        {
            // Fluent 语义色（Wpf-Ui 主题字典）：进行中强调蓝 / 成功绿 / 失败红 / 已取消灰
            "running" => ((string)res("S.Task.Running")!,
                (Brush)res("SubtleFillColorSecondaryBrush")!, (Brush)res("SystemAccentColorPrimaryBrush")!),
            "done" => ((string)res("S.Task.Done")!,
                (Brush)res("SystemFillColorSuccessBackgroundBrush")!, (Brush)res("SystemFillColorSuccessBrush")!),
            "failed" => ((string)res("S.Task.Failed")!,
                (Brush)res("SystemFillColorCriticalBackgroundBrush")!, (Brush)res("SystemFillColorCriticalBrush")!),
            _ => ((string)res("S.Task.Canceled")!,
                (Brush)res("SubtleFillColorSecondaryBrush")!, (Brush)res("TextFillColorSecondaryBrush")!),
        };
    }

    private void BtnClear_Click(object sender, RoutedEventArgs e)
        => DownloadTaskService.Instance.ClearFinished();
}
