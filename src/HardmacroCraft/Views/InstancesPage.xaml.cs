using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.11 实例管理页：运行中的游戏实例（多开支持）集中在此查看与操作。
/// 自主页「运行中的实例」卡迁移而来；每张实例卡可打开实例详情（状态 / PID / 启动时长 /
/// 目录入口）与结束进程。刷新频率跟随「设置 → 界面刷新频率」（轮询 / 事件驱动均支持）。
/// </summary>
public partial class InstancesPage : Page
{
    private readonly DispatcherTimer _timer;
    private RefreshThrottle? _throttle;
    private bool _loadedOnce;

    public InstancesPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            ApplyRefreshPolicy();
            GameInstanceManager.Changed += OnInstancesChanged;
            if (!_loadedOnce)
            {
                _loadedOnce = true;
                _timer.Start();
            }
            else
            {
                _timer.Start();
            }
        };
        Unloaded += (_, _) =>
        {
            GameInstanceManager.Changed -= OnInstancesChanged;
            _timer.Stop();
        };
    }

    /// <summary>按「设置 → 界面刷新频率」选择轮询 / 事件驱动刷新。</summary>
    private void ApplyRefreshPolicy()
    {
        var s = AppSettings.Instance;
        var mode = s.UiRefreshMode;
        // 事件驱动：停掉轮询定时器，改订阅实例状态变化（限频模式下经 RefreshThrottle 节流）
        GameInstanceManager.Changed -= OnInstancesChanged;
        if (UiRefreshPolicy.IsEventDriven(mode))
        {
            _timer.Stop();
            _throttle = new RefreshThrottle(UiRefreshPolicy.EventMinIntervalMs(mode, s.UiRefreshMaxFps));
            GameInstanceManager.Changed += OnInstancesChanged;
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

    private void OnInstancesChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(OnInstancesChanged); return; }
        if (_throttle != null && !_throttle.TryRefresh(DateTime.Now)) return;
        Refresh();
    }

    private void Refresh()
    {
        var list = GameInstanceManager.Instances;
        TxtEmpty.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 只做增量重建：数量与版本集合一致时不重建，避免打断按钮悬停等交互
        var rows = PnlInstances.Children.OfType<Border>().ToList();
        if (rows.Count == list.Count
            && rows.Zip(list).All(p => p.First.Tag is GameInstance g && ReferenceEquals(g, p.Second)))
            return;

        PnlInstances.Children.Clear();
        foreach (var inst in list)
            PnlInstances.Children.Add(BuildCard(inst));
    }

    private static (string Text, Brush Bg, Brush Fg) StateBadge(GameInstance inst)
    {
        var res = App.Current.TryFindResource;
        if (inst.State == GameInstanceState.Running)
            return ((string)res("S.Home.InstanceRunning")!, (Brush)res("SystemFillColorSuccessBackgroundBrush")!,
                (Brush)res("SystemFillColorSuccessBrush")!);
        return ((string)res("S.Home.InstanceWaiting")!, (Brush)res("SubtleFillColorSecondaryBrush")!,
            (Brush)res("SystemAccentColorPrimaryBrush")!);
    }

    private UIElement BuildCard(GameInstance inst)
    {
        var card = new Border
        {
            Style = (Style)App.Current.TryFindResource("HmCard")!,
            Padding = new Thickness(14, 10, 14, 12),
            Margin = new Thickness(0, 0, 0, 10),
            Tag = inst,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        card.MouseLeftButtonUp += (_, _) => InstanceDetailWindow.Show(Window.GetWindow(this), inst);

        var sp = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = new System.Windows.Controls.Image
        {
            Width = 24, Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
                    };
        try
        {
            var desc = VersionInfo.Describe(AppSettings.Instance.GameRoot, inst.Version);
            icon.Source = VersionIconProvider.DescribeIcon(desc).Icon
                ?? new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/grass.png"));
        }
        catch
        {
            icon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/grass.png"));
        }
        left.Children.Add(icon);
        var name = new TextBlock
        {
            Text = inst.Version, FontSize = 14, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
        left.Children.Add(name);
        Grid.SetColumn(left, 0);
        head.Children.Add(left);

        var (stateText, stateBg, stateFg) = StateBadge(inst);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 1, 8, 2),
            Background = stateBg,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        badge.Child = new TextBlock { Text = stateText, FontSize = 10.5, Foreground = stateFg };
        Grid.SetColumn(badge, 1);
        head.Children.Add(badge);

        var kill = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.InstanceKill") as string ?? "结束进程",
            FontSize = 11, MinHeight = 26, Padding = new Thickness(8, 2, 8, 2),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            ToolTip = App.Current.TryFindResource("S.Home.InstanceKillTip") as string ?? "结束整个游戏进程树",
        };
        kill.Click += (_, e) =>
        {
            e.Handled = true;
            GameInstanceManager.Kill(inst); // 退出 / 结束结果经 GameInstanceManager.Log 写入主页日志
        };
        Grid.SetColumn(kill, 2);
        head.Children.Add(kill);
        sp.Children.Add(head);

        var meta = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
            Text = $"PID {inst.Pid} · {App.Current.TryFindResource("S.Inst.StartedAt") as string ?? "启动于"} {inst.StartTime:HH:mm:ss}",
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        sp.Children.Add(meta);

        card.Child = sp;
        return card;
    }
}
