using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.11 实例详情弹窗：查看运行中游戏实例的详细信息 —— 版本 / 加载器图标 / 状态 /
/// PID / 启动时间 / 已运行时长（实时刷新）/ 进程存活探测，附目录入口（版本目录 / 存档 /
/// Mod / 日志）与「结束进程」。状态每 500ms 自动刷新（跟随界面刷新频率设置）。
/// </summary>
public static class InstanceDetailWindow
{
    private static readonly Dictionary<int, Wpf.Ui.Controls.FluentWindow> OpenWindows = new();

    public static void Show(Window? owner, GameInstance inst)
    {
        // 同一实例已打开详情时置于前台（避免重复弹窗）
        if (OpenWindows.TryGetValue(inst.Pid, out var existing))
        {
            existing.Activate();
            return;
        }

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = (App.Current.TryFindResource("S.Inst.DetailTitle") as string ?? "实例详情") + " · " + inst.Version,
            Width = 520,
            SizeToContent = SizeToContent.Height,
            MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };

        // 头部：图标 + 版本名 + 状态徽章
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var icon = new System.Windows.Controls.Image
        {
            Width = 32, Height = 32,
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
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
        head.Children.Add(icon);
        var name = new TextBlock
        {
            Text = inst.Version, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        head.Children.Add(name);
        root.Children.Add(head);

        // 状态徽章
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 2, 10, 3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 6, 0, 12),
        };
        root.Children.Add(badge);

        // 信息表（实时刷新行：状态 / 已运行时长 / 进程存活）
        string Row(string key, string fallback) => App.Current.TryFindResource(key) as string ?? fallback;
        var txtState = InfoRow(root, Row("S.Inst.State", "状态"));
        var txtPid = InfoRow(root, "PID");
        var txtStarted = InfoRow(root, Row("S.Inst.StartedAt", "启动时间"));
        var txtElapsed = InfoRow(root, Row("S.Inst.Elapsed", "已运行"));
        var txtAlive = InfoRow(root, Row("S.Inst.ProcessAlive", "进程状态"));

        // 目录入口（WrapPanel：窗口过窄自动换行防溢出）
        root.Children.Add(GroupTitle(Row("S.Vs.GroupDirs", "目录")));
        var dirs = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var gameRoot = AppSettings.Instance.GameRoot;
        var iso = VersionSettingsStore.EffectiveIsolation(inst.Version, AppSettings.Instance.VersionIsolation);
        var baseDir = iso ? System.IO.Path.Combine(gameRoot, "versions", inst.Version) : gameRoot;
        void AddDir(string key, string fallback, string dir)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = App.Current.TryFindResource(key) as string ?? fallback,
                MinHeight = 32, MinWidth = 92, Margin = new Thickness(0, 4, 8, 4),
            };
            b.Click += (_, _) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
                }
                catch { /* 打开失败静默 */ }
            };
            dirs.Children.Add(b);
        }
        AddDir("S.Vs.OpenVersionDir", "打开版本目录", System.IO.Path.Combine(gameRoot, "versions", inst.Version));
        AddDir("S.Vs.OpenSavesDir", "打开存档", System.IO.Path.Combine(baseDir, "saves"));
        AddDir("S.Vs.OpenModsDir", "打开 Mod 目录", System.IO.Path.Combine(baseDir, "mods"));
        AddDir("S.Vs.OpenLogsDir", "打开日志", System.IO.Path.Combine(gameRoot, "logs"));
        root.Children.Add(dirs);

        // 底部按钮：结束进程 + 关闭
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var btnKill = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.InstanceKill") as string ?? "结束进程",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Dismiss24 },
            MinHeight = 34, MinWidth = 110, Margin = new Thickness(0, 0, 10, 0),
            ToolTip = App.Current.TryFindResource("S.Home.InstanceKillTip") as string ?? "结束整个游戏进程树",
        };
        btnKill.Click += (_, _) =>
        {
            if (!GameInstanceManager.Kill(inst)) return;
            try { win.Close(); } catch { }
        };
        var btnClose = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Common.Close") as string ?? "关闭",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 34, MinWidth = 96,
        };
        btnClose.Click += (_, _) => win.Close();
        btnRow.Children.Add(btnKill);
        btnRow.Children.Add(btnClose);
        root.Children.Add(btnRow);

        FluentDialog.SetContent(win, root);

        // 实时刷新（状态 / 已运行时长 / 进程存活 / 结束按钮可用性）
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        void Tick()
        {
            var running = inst.State == GameInstanceState.Running;
            string stateText;
            Brush stateBg, stateFg;
            if (running)
            {
                stateText = App.Current.TryFindResource("S.Home.InstanceRunning") as string ?? "运行中";
                stateBg = (Brush)App.Current.TryFindResource("SystemFillColorSuccessBackgroundBrush")!;
                stateFg = (Brush)App.Current.TryFindResource("SystemFillColorSuccessBrush")!;
            }
            else
            {
                stateText = App.Current.TryFindResource("S.Home.InstanceWaiting") as string ?? "等待窗口出现";
                stateBg = (Brush)App.Current.TryFindResource("SubtleFillColorSecondaryBrush")!;
                stateFg = (Brush)App.Current.TryFindResource("SystemAccentColorPrimaryBrush")!;
            }
            badge.Background = stateBg;
            badge.Child = new TextBlock { Text = stateText, FontSize = 12, Foreground = stateFg };
            txtState.Text = stateText;
            txtPid.Text = inst.Pid.ToString();
            txtStarted.Text = inst.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            txtElapsed.Text = FormatElapsed(DateTime.Now - inst.StartTime);
            txtAlive.Text = inst.HasExited
                ? Row("S.Inst.Exited", "已退出")
                : Row("S.Inst.Alive", "运行中");
            btnKill.IsEnabled = !inst.HasExited;
        }
        void StartTimer()
        {
            var s = AppSettings.Instance;
            var interval = UiRefreshPolicy.IsEventDriven(s.UiRefreshMode) ? 500
                : Math.Max(200, UiRefreshPolicy.PollIntervalMs(s.UiRefreshMode, s.UiRefreshFps));
            timer.Interval = TimeSpan.FromMilliseconds(interval);
            timer.Start();
        }
        timer.Tick += (_, _) => Tick();
        win.Loaded += (_, _) => { Tick(); StartTimer(); };
        win.Closed += (_, _) => { timer.Stop(); OpenWindows.Remove(inst.Pid); };
        OpenWindows[inst.Pid] = win;
        win.ShowDialog();
    }

    private static TextBlock InfoRow(StackPanel root, string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        l.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetColumn(l, 0);
        grid.Children.Add(l);
        var v = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        v.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        Grid.SetColumn(v, 1);
        grid.Children.Add(v);
        root.Children.Add(grid);
        return v;
    }

    private static TextBlock GroupTitle(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 12, 0, 4),
    };

    private static string FormatElapsed(TimeSpan el)
    {
        return el.TotalMinutes < 1 ? $"{el.Seconds:0} 秒"
            : el.TotalHours < 1 ? $"{(int)el.TotalMinutes} 分 {el.Seconds:0} 秒"
            : $"{(int)el.TotalHours} 小时 {(int)(el.TotalMinutes % 60)} 分";
    }
}
