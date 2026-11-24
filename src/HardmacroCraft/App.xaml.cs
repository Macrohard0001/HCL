using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HardmacroCraft.Services;
using Wpf.Ui.Appearance;

namespace HardmacroCraft;

public partial class App : Application
{
    public static LauncherService LauncherService { get; private set; } = null!;
    public static AuthService Auth { get; private set; } = null!;
    public static ModrinthService Modrinth { get; private set; } = null!;
    public static CurseForgeService CurseForge { get; private set; } = null!;
    public static ModpackService Modpacks { get; private set; } = null!;
    public static string Language { get; private set; } = "zh-CN";

    // ===== v0.15.11：UI 异常风暴断路器 =====
    // 布局循环里的异常会被 WPF 无限重抛（crash.log 2026-11-24：同一异常 30 秒内
    // 1210 次，每次都弹窗 → 「一百万个报错弹窗」）。同一签名异常在滑动窗口内
    // 超阈值即停止弹窗（仍落日志）；已知可自愈的风暴原因直接回退配置。
    private static readonly Dictionary<string, List<DateTime>> _exceptionStorm = new();
    private static readonly object _stormLock = new();
    private const int StormThreshold = 6;
    private static readonly TimeSpan StormWindow = TimeSpan.FromSeconds(60);

    private static void OnDispatcherException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
    {
        var ex = args.Exception;
        Services.BootLogger.Error("UI线程未处理异常: " + Services.BootLogger.Format(ex));
        try
        {
            File.AppendAllText(
                Path.Combine(AppSettings.ConfigDir, "crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Services.BootLogger.Format(ex)}{Environment.NewLine}");
        }
        catch { }

        var signature = ex.GetType().FullName + "|" + ex.Message;
        int repeats;
        lock (_stormLock)
        {
            if (!_exceptionStorm.TryGetValue(signature, out var hits))
                _exceptionStorm[signature] = hits = new List<DateTime>();
            hits.RemoveAll(t => DateTime.Now - t > StormWindow);
            hits.Add(DateTime.Now);
            repeats = hits.Count;
        }

        if (repeats >= StormThreshold)
        {
            if (repeats == StormThreshold) // 风暴首达阈值：一次性处置 + 提示，此后静默兜底
            {
                Services.BootLogger.Error(
                    $"异常风暴保护触发：同一异常 {StormWindow.TotalSeconds}s 内第 {repeats} 次，停止弹窗（仍记录 crash.log）");
                // 已知自愈场景：Metro 壳页面宿主类异常 → 回退 Fluent，重启即恢复
                var healed = false;
                if (ex.Message.Contains("Page can have only Window or Frame"))
                {
                    try
                    {
                        AppSettings.Instance.ThemeStyle = "fluent";
                        AppSettings.Instance.Save();
                        Services.BootLogger.Error("已将界面风格回退为 Fluent，重启后恢复");
                        healed = true;
                    }
                    catch { }
                }
                MessageBox.Show(
                    "应用出现重复异常，已停止逐次弹窗（详情记录在 config/crash.log）。" +
                    (healed ? "\n\n界面风格已自动回退为 Fluent，请重新启动。" : "\n\n若界面无法使用，请重新启动。"),
                    "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
            return;
        }

        MessageBox.Show($"发生错误：{ex.Message}\n\n详细信息已记录到 config/crash.log",
            "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        args.Handled = true;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ===== 全局异常兜底（必须最先注册：0.1.0 曾有异常发生在处理器注册之前 → 静默闪退）=====
        // 1) 非UI线程致命异常（AppDomain 级，CLR 即将终止进程）
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            Services.BootLogger.Error("AppDomain 未处理异常 (IsTerminating=" + args.IsTerminating + "): "
                + (ex != null ? Services.BootLogger.Format(ex) : args.ExceptionObject?.ToString() ?? "null"));
            try
            {
                File.AppendAllText(
                    Path.Combine(AppSettings.ConfigDir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [非UI线程] {(ex != null ? Services.BootLogger.Format(ex) : "未知异常")}{Environment.NewLine}");
            }
            catch { }
            // IsTerminating = true 时进程必死，弹窗让用户看到原因而不是静默消失
            if (args.IsTerminating)
                MessageBox.Show(
                    "启动器遇到致命错误，即将关闭：\n\n" + (ex?.Message ?? "未知异常") +
                    "\n\n详细信息已记录到 config/crash.log（若不存在请查看 %TEMP%\\HardmacroCraft-boot.log）",
                    "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        // 2) 后台任务未观察异常（默认会静默吞掉进程崩溃）
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Services.BootLogger.Error("未观察的任务异常: " + Services.BootLogger.Format(args.Exception));
            args.SetObserved(); // 防止它升级为进程崩溃；具体业务错误已在各任务内部处理
        };
        // 3) UI 线程未处理异常（弹窗提示而不是闪退）
        DispatcherUnhandledException += OnDispatcherException;

        Services.BootLogger.Info($"===== 启动 Hardmacro Craft Launcher v{Services.BootLogger.Version} =====");
        Services.BootLogger.Info($"进程: {Environment.ProcessPath} | .NET: {Environment.Version} | OS: {Environment.OSVersion.VersionString}");

        // ===== 启动步骤（每步留痕，boot.log 停在哪一步就是哪里出了问题）=====
        try { Services.BootLogger.Info("加载配置 settings.json ..."); AppSettings.Load(); }
        catch (Exception ex)
        {
            Services.BootLogger.Error("配置加载失败: " + Services.BootLogger.Format(ex));
            MessageBox.Show($"加载配置失败，将使用默认配置继续启动。\n\n{ex.Message}", "Hardmacro Craft Launcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        try { Services.BootLogger.Info("加载账号 accounts.json ..."); AccountStore.Load(); }
        catch (Exception ex)
        {
            Services.BootLogger.Error("账号加载失败: " + Services.BootLogger.Format(ex));
        }

        // fix3：清理历史版本在 %APPDATA% 产生的 .minecraft 空壳
        //（仅含 version_manifest_v2.json 垃圾文件才删，真实游戏目录一律不动）
        try { Services.LauncherService.CleanupPollutedAppDataShell(m => Services.BootLogger.Info(m)); }
        catch (Exception ex) { Services.BootLogger.Error("APPDATA 空壳清理失败: " + ex.Message); }

        // 便携化：所有配置 / 账号都存 exe 同目录 config/

        try { Services.BootLogger.Info("初始化服务 ..."); }
        catch (Exception ex) { Services.BootLogger.Error("服务初始化日志失败: " + ex.Message); }
        LauncherService = new LauncherService();
        Auth = new AuthService();
        Modrinth = new ModrinthService(LauncherService.Http);
        CurseForge = new CurseForgeService(LauncherService.Http);
        Modpacks = new ModpackService(LauncherService.Http);
        Services.BootLogger.Info("服务初始化完成");

        // v0.1.3 内存优化：空闲时段定时回收（重大操作后的即时优化在各业务收尾处触发）
        Services.MemoryManager.Initialize();

        // 滚轮滚动全局兜底：在最外层 ScrollViewer 上直接手动滚动。
        // 这不依赖滚轮事件链（ScrollInfo 接线差异、内层 ListBox 的空滚动器吞事件、
        // 第三方控件模板差异等任何一层失效都能工作），只要偏移量接口存在即可。
        EventManager.RegisterClassHandler(typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnScrollViewerPreviewMouseWheel),
            handledEventsToo: true);

        // v0.15.2：原生触控支持 —— 为所有 ScrollViewer 开启 WPF 原生拖拽平移
        // （PanningMode，触屏 / 触控板手势直接生效，带惯性减速），无需逐页设置。
        EventManager.RegisterClassHandler(typeof(ScrollViewer),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnScrollViewerLoadedForTouch),
            handledEventsToo: true);

        Services.BootLogger.Info($"应用语言 {AppSettings.Instance.Language} ...");
        ApplyLanguage(AppSettings.Instance.Language);
        // 主题在窗口创建前应用（深/浅/跟随系统），窗口内的所有 DynamicResource 颜色会随之统一
        Services.BootLogger.Info($"应用主题 {AppSettings.Instance.Theme} ...");
        ApplyTheme(AppSettings.Instance.Theme, attachWatcher: false);
        // v0.15.4：Metro 模式下跟随系统的深浅色监听（SystemThemeWatcher 内部只切
        // Wpf.Ui 主题字典）不会重挂 MahApps 覆盖层 —— 监听主题变化事件同步变体
        ApplicationThemeManager.Changed += OnWpfUiThemeChanged;
        // v0.15.8：启动字典组合落日志 —— 用户机上报"空白"时 boot.log 直接给出
        // 当时实际生效的合并字典清单（v0.15.7 及以前这里是无日志盲区）
        var dictDump = string.Join(" | ",
            Current.Resources.MergedDictionaries.Select(d => d.Source?.OriginalString ?? "<内联>"));
        Services.BootLogger.Info($"主题应用完成（ThemeStyle={AppSettings.Instance.ThemeStyle}），字典组合: {dictDump}");
        Services.BootLogger.Info("创建主窗口 ...");

        // v0.15.2：主窗口创建防护 —— XAML 解析失败（如个别系统 urlmon MIME 组件异常
        // 影响资源图片加载）不再裸崩，给出可操作的提示并落盘 crash.log
        Window win;
        try
        {
            // v0.15.9：双壳 —— Metro（Win10 怀旧）模式用 MahApps 壳，其余用 Fluent 壳
            win = AppSettings.Instance.ThemeStyle == "metro" ? new MetroMainWindow() : new MainWindow();
            MainWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("主窗口创建失败: " + Services.BootLogger.Format(ex));
            try
            {
                File.AppendAllText(Path.Combine(AppSettings.ConfigDir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Services.BootLogger.Format(ex)}{Environment.NewLine}");
            }
            catch { }
            MessageBox.Show(
                "启动器界面初始化失败：" + ex.Message +
                "\n\n常见原因：系统图形 / MIME 组件异常。请更新 Windows 后重试；" +
                "若反复出现，请把 config/crash.log 反馈给开发者。",
                "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Services.BootLogger.Info("主窗口已显示，启动完成");
    }

    /// <summary>v0.15.2：原生触控 —— ScrollViewer 加载时开启竖向拖拽平移（惯性减速对齐系统）。
    /// PanningMode 默认 None，触屏用户此前只能拖滚动条；开启后触摸直接平移内容。</summary>
    private static void OnScrollViewerLoadedForTouch(object sender, RoutedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;
        if (sv.PanningMode == PanningMode.None)
        {
            sv.PanningMode = PanningMode.VerticalOnly;
            sv.PanningDeceleration = 0.006; // 默认 0.001 太滑，接近系统默认手感
        }
    }

    /// <summary>
    /// 滚轮兜底：最内层（最靠近鼠标）的 ScrollViewer 优先滚动；
    /// 滚到边界后自动委托最近的可滚动祖先继续滚（列表滚完接页面滚动）；
    /// 实际滚动由 SmoothScroll 平滑完成（v1.6.0 起带滚动动画）。
    /// </summary>
    private static void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 拖动/点击滚动条本身时不干预
        if (e.OriginalSource is ScrollBar) return;

        var sv = (ScrollViewer)sender;

        // 触控板惯性产生的小步长：交给默认链路（SmoothScroll 的目标式动画对
        // 高频小步长反而迟钝），仅处理常规滚轮格（±120 倍数）
        if (e.Delta == 0 || Math.Abs(e.Delta) % 120 != 0) return;

        // 隧道路由（根→叶）：若鼠标位置与本滚动器之间还有更内层的 ScrollViewer，
        // 交给它处理（它先滚，滚不动才轮到我）
        if (HasInnerScrollViewer(sv, e.OriginalSource as DependencyObject)) return;

        // v0.15.2：每格滚动长度 100px → 66px（≈3 行文本）—— 侧栏 / 详情页等较矮容器
        // 此前一格接近半屏，方向感混乱；66px 对齐浏览器 / PCL 的常规步长
        const double WheelPxPerNotch = 66.0;
        var deltaPx = e.Delta / 120.0 * WheelPxPerNotch;

        // 本层滚不动：内容不足一屏，或已滚到对应方向的边界
        var atEdge = sv.ScrollableHeight <= 0
                     || (e.Delta > 0 && sv.VerticalOffset <= 0)
                     || (e.Delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight);
        if (atEdge)
        {
            // 委托最近的可滚动祖先（列表到边 → 接力滚动外层页面）
            var outer = FindScrollableAncestor(sv);
            if (outer != null)
            {
                // 外层页面固定是像素滚动（StackPanel 布局），直接用像素步长
                Services.SmoothScroll.ScrollTo(outer, outer.VerticalOffset - deltaPx);
                e.Handled = true;
            }
            return; // 找不到可滚动祖先则不吞事件，走默认链路
        }

        Services.SmoothScroll.ScrollTo(sv, sv.VerticalOffset - LogicalDelta(sv, deltaPx));
        e.Handled = true;
    }

    /// <summary>
    /// CanContentScroll=true 的列表（ListBox 默认）偏移按“条目”计而非像素，
    /// 像素步长直接用会一格翻底（游戏版本列表的根因）。换算成条目数：每格约滚 100px / 首个已生成条目高度。
    /// </summary>
    private static double LogicalDelta(ScrollViewer sv, double deltaPx)
    {
        if (!sv.CanContentScroll) return deltaPx;
        var itemH = 0.0;
        if (sv.Content is ItemsControl ic && ic.Items.Count > 0
            && ic.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement fe)
        {
            itemH = fe.ActualHeight;
        }
        if (itemH <= 0) itemH = 48; // 条目未生成 / 不可见时的兜底高度
        return Math.Max(1.0, deltaPx / itemH);
    }

    /// <summary>判断事件源与 outer 之间是否存在更内层的 ScrollViewer。</summary>
    private static bool HasInnerScrollViewer(ScrollViewer outer, DependencyObject? node)
    {
        while (node != null && node != outer)
        {
            if (node is ScrollViewer) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>向上找最近的还有可滚动内容的祖先 ScrollViewer。</summary>
    private static ScrollViewer? FindScrollableAncestor(DependencyObject? node)
    {
        while (node != null)
        {
            node = VisualTreeHelper.GetParent(node);
            if (node is ScrollViewer sv && sv.ScrollableHeight > 0)
                return sv;
        }
        return null;
    }

    /// <summary>
    /// 应用主题。所有界面颜色均来自主题资源字典（DynamicResource），
    /// 切换时文字 / 卡片 / 边框 / 滚动条会一起变色 —— 不存在写死的字体颜色。
    /// theme = "dark" / "light" / "system"（跟随 Windows 并自动监听系统切换）。
    /// </summary>
    public static void ApplyTheme(string theme, bool attachWatcher = true)
    {
        // v0.15.3：主题风格与深浅色正交叠加 ——
        // metro（Win10 怀旧）关闭云母，窗体用主题实色底（Win10 式不透明），叠加直角 + 经典蓝覆盖层
        var metro = string.Equals(AppSettings.Instance.ThemeStyle, "metro", StringComparison.OrdinalIgnoreCase);
        var backdrop = metro
            ? Wpf.Ui.Controls.WindowBackdropType.None
            : Wpf.Ui.Controls.WindowBackdropType.Mica;

        // v0.15.6：有存活窗口（运行中热切）时绝不全量重建样式集 —— WPF 遍历视觉树
        // 换隐式样式时旧模板链的动画以 NaN 为目标值求值会崩（v0.15.5 crash.log）。
        // 运行中 Metro 只同位换深浅色变体；Fluent 交给 Wpf.Ui 原地更新；风格切换必须重启
        var hasWindows = Current is not null && Current.Windows.Count > 0;

        if (!metro || !hasWindows)
        {
            switch (theme)
            {
                case "light":
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, backdrop);
                    break;
                case "system":
                    ApplicationThemeManager.ApplySystemTheme();
                    break;
                default: // dark
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, backdrop);
                    break;
            }
        }

        if (metro && !hasWindows)
        {
            // 启动期（无窗口，树尚未建立）：MahApps 全局接管 —— 整体重建合并字典是安全的
            ApplyMetroOverlay(true, theme);
        }
        else if (metro)
        {
            // 运行中（Metro）：样式集不动，只同位替换深浅色变体字典（隐式样式对象不变）
            SwapMetroVariant(theme);
        }
        // Fluent：Wpf.Ui 原地更新它缓存的主题字典即可 —— 出厂组合保持 App.xaml 原样，
        // 不做任何字典手术（v0.15.5 的启动期重建反而会切断 Wpf.Ui 的缓存引用）

        // v0.15.4：Win11 圆角窗体是 Fluent 的标志 —— Metro 模式下关掉 DWM 圆角，
        // 切回 Fluent 恢复 Round（仅 Wpf.Ui 窗体支持该属性）
        if (Current?.MainWindow is Wpf.Ui.Controls.FluentWindow uiWin)
        {
            try { uiWin.WindowCornerPreference = metro
                ? Wpf.Ui.Controls.WindowCornerPreference.DoNotRound
                : Wpf.Ui.Controls.WindowCornerPreference.Round; }
            catch { /* 平台不支持 DWM 角偏好时忽略 */ }
        }

        // 跟随系统时挂上监听器：Windows 深浅色变化时启动器实时跟随
        var win = Current?.MainWindow;
        if (win != null)
        {
            try
            {
                SystemThemeWatcher.UnWatch(win);
                if (theme == "system" && attachWatcher)
                    SystemThemeWatcher.Watch(win, backdrop, true);
            }
            catch { /* 忽略监听器异常，主题本身已应用 */ }
        }
    }

    // Wpf.Ui 主字典 = ui:ControlsDictionary（含标准控件隐式 Fluent 样式 + 全部基建）
    private const string WpfUiMasterDict =
        "pack://application:,,,/Wpf.Ui;component/Resources/Wpf.Ui.xaml";
    private const string WpfUiThemeDictPrefix =
        "pack://application:,,,/Wpf.Ui;component/Resources/Theme/";
    // v0.15.6：OS 深浅色变化（跟随系统监听）——
    // Fluent：Wpf.Ui 已原地更新其字典，无需干预；Metro：只同位换深浅色变体；
    // 启动期（无窗口）什么都不做，ApplyTheme 稍后整体重建。绝不在运行中全量重建。
    private static void OnWpfUiThemeChanged(ApplicationTheme currentApplicationTheme, Color systemAccent)
    {
        if (!string.Equals(AppSettings.Instance.ThemeStyle, "metro", StringComparison.OrdinalIgnoreCase))
            return;
        if (Current is null || Current.Windows.Count == 0)
            return;
        SwapMetroVariant(currentApplicationTheme == ApplicationTheme.Dark ? "dark" : "light");
    }

    /// <summary>
    /// v0.15.7：全局字典切换（Metro ↔ Fluent）—— 仅允许启动期（无窗口）调用。
    /// on=false（Fluent）：恢复 App.xaml 出厂组合（Wpf.Ui 主题字典 + 主字典）。
    /// on=true（Metro）：Wpf.Ui 主题字典 + 主字典（ui: 自定义控件模板必需，
    /// 库内无 Generic.xaml）→ MahApps 全套（后位压制主字典对标准控件的隐式样式）
    /// → Metro.xaml 经典蓝/直角压轴覆盖。
    /// theme 决定 Dark/Light 变体（含跟随系统，经 ThemeChanged 事件重挂）。
    /// </summary>
    public static void ApplyMetroOverlay(bool on, string theme)
    {
        var appTheme = ApplicationThemeManager.GetAppTheme();
        var dark = theme == "dark"
            || (theme != "light" && appTheme == ApplicationTheme.Dark);
        var variant = dark ? "Dark" : "Light";

        var resources = Current.Resources;
        var md = resources.MergedDictionaries;
        try
        {
        // 移除既有全套主题相关字典（语言字典不受影响）
        // v0.15.6：RemoveAt 同样触发 OnMergedDictionariesChanged → 全树失效遍历，
        // 必须连同 Add 一起包进 try/catch（v0.15.5 只包了 Add，RemoveAt 裸奔）
        var prefixes = new[]
        {
            "Themes/Metro.",
            "Wpf.Ui;component/Resources/Wpf.Ui.xaml",
            "Wpf.Ui;component/Resources/Theme/",
            "Wpf.Ui;component/Resources/Variables",
            "Wpf.Ui;component/Resources/Fonts",
            "Wpf.Ui;component/Resources/Typography",
            "Wpf.Ui;component/Resources/StaticColors",
            "Wpf.Ui;component/Resources/Accent",
            "Wpf.Ui;component/Resources/Palette",
            "Wpf.Ui;component/Resources/DefaultContextMenu",
            "Wpf.Ui;component/Resources/DefaultFocusVisualStyle",
            "Wpf.Ui;component/Resources/DefaultTextBoxScrollViewerStyle",
            "MahApps.Metro;component/Styles/",
            "Themes/MetroSkin.",
            "Themes/Win10Controls.",
        };
        for (var i = md.Count - 1; i >= 0; i--)
        {
            var src = md[i].Source?.OriginalString;
            if (src != null && prefixes.Any(p => src.Contains(p)))
                md.RemoveAt(i);
        }

        }
        catch (Exception ex)
        {
            // 兜底：RemoveAt 同样触发 OnMergedDictionariesChanged → 全树失效遍历
            Services.BootLogger.Error("Metro 全局字典切换失败(移除段): " + ex.Message);
            return;
        }

        // v0.15.8：逐字典强校验加载 —— ResourceDictionary.Source 是延迟解析，
            // BAML 解析异常原本要到窗口创建解析样式时才炸（v0.15.7 用户机上表现为
            // 整窗空白且日志里查不到是哪个字典）。这里强制立即解析（含嵌套合并字典），
            // 哪个字典坏了在加载点一步一 log；主字典挂掉自动回退 Fluent，绝不空白。
            void AddChecked(string uri, List<string> loaded)
            {
                try
                {
                    var d = new ResourceDictionary { Source = new Uri(uri) };
                    _ = d.Keys.Count; // 强制立即解析 BAML，异常在加载点暴露
                    md.Add(d);
                    loaded.Add(uri);
                }
                catch (Exception ex)
                {
                    Services.BootLogger.Error($"字典加载失败 [{uri}]: " + Services.BootLogger.Format(ex));
                    throw;
                }
            }

            var loadedDicts = new List<string>();
            try
            {
                if (!on)
                {
                    // Fluent：出厂组合（Wpf.Ui 主题字典 + 主字典，隐式样式全量回归）
                    AddChecked(WpfUiThemeDictPrefix + variant + ".xaml", loadedDicts);
                    AddChecked(WpfUiMasterDict, loadedDicts);
                    Services.BootLogger.Info("Fluent 组合完成: " + string.Join(" | ", loadedDicts));
                    return;
                }

                // Metro：1) Wpf.Ui 主题字典 + 主字典整体保留。Wpf.Ui 库内没有
                // Themes/Generic.xaml，ui: 自定义控件（FluentWindow / NavigationView /
                // TitleBar / SymbolIcon / NumberBox 等）的默认模板只存在于主字典；
                // 主字典对标准控件的隐式样式会被后面的 MahApps 压制（后位优先）。
                // 主字典是 ui: 控件模板的唯一来源 —— 它挂掉则 Metro 必然空白，回退 Fluent。
                try
                {
                    AddChecked(WpfUiThemeDictPrefix + variant + ".xaml", loadedDicts);
                    AddChecked(WpfUiMasterDict, loadedDicts);
                }
                catch
                {
                    Services.BootLogger.Error("Metro 核心字典加载失败，回退 Fluent 出厂组合（可见性优先）");
                    ApplyMetroOverlay(false, theme);
                    return;
                }

                // 2) MahApps 全局接管：全部标准控件隐式样式 + MahApps 主题画刷
                // 3) 压轴覆盖（后位优先）：直角 + 经典 Win10 蓝
                try
                {
                    // MahApps 2.4.x 主题字典必须带颜色段（程序集资源实测只有 styles/themes/light.blue.baml，
                    // 没有 light.xaml —— v0.15.11 用户机日志证实 404）。Blue = 经典 Win10 蓝
                    AddChecked($"pack://application:,,,/MahApps.Metro;component/Styles/Themes/{{variant}}.Blue.xaml", loadedDicts);
                    AddChecked("pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml", loadedDicts);
                    // v0.15.9：Metro 皮肤 —— Win10 经典填色压住 Wpf.Ui 主题字典的
                    // WinUI 半透明填充键（ui: 控件保留模板，外观变 Metro 实底）
                    AddChecked($"pack://application:,,,/Themes/MetroSkin.{variant}.xaml", loadedDicts);
                    AddChecked("pack://application:,,,/Themes/Metro.xaml", loadedDicts);
                    // v0.15.14：Win10 换骨层压轴 —— ui: 控件模板从 Wpf.Ui Win11 Fluent 形状
                    // 整体重铸为 Win10 桌面规格（v0.15.13 用户反馈"还是不像"根因：
                    // MetroSkin 只换色不换骨）。画刷全走 MetroSkin 变体键，热切换自动生效。
                    AddChecked("pack://application:,,,/Themes/Win10Controls.xaml", loadedDicts);
                }
                catch
                {
                    // 增强层挂掉只影响样式接管程度，不致空白 —— 保留 Wpf.Ui 基础组合
                    Services.BootLogger.Error("Metro 增强字典（MahApps/覆盖层）加载失败，保留 Wpf.Ui 基础组合");
                    return;
                }
                Services.BootLogger.Info("Metro 组合完成: " + string.Join(" | ", loadedDicts));
            }
            catch (Exception ex)
            {
                // 兜底：字典变更引发的资源失效遍历里任何控件抛错都不许带崩应用
                Services.BootLogger.Error("Metro 全局字典切换失败: " + ex.Message);
            }
    }

    /// <summary>
    /// v0.15.6：Metro 模式下运行中切换深浅色 —— 只把 MahApps / Wpf.Ui 的主题字典
    /// 同位替换为另一变体（隐式样式对象不变，无模板交换，热切换安全）。
    /// </summary>
    private static void SwapMetroVariant(string theme)
    {
        try
        {
            // 跟随系统时用 WPF 已解析的系统主题，未知取浅色（Win10 默认）
            var dark = theme switch
            {
                "dark" => true,
                "light" => false,
                _ => ApplicationThemeManager.GetSystemTheme() == Wpf.Ui.Appearance.SystemTheme.Dark,
            };
            var variant = dark ? "Dark.xaml" : "Light.xaml";
            var md = Current.Resources.MergedDictionaries;
            SwapThemeDictInPlace(md, "Wpf.Ui;component/Resources/Theme/",
                WpfUiThemeDictPrefix + variant);
            SwapThemeDictInPlace(md, "MahApps.Metro;component/Styles/Themes/",
                $"pack://application:,,,/MahApps.Metro;component/Styles/Themes/{variant}.Blue");
            // v0.15.9：Metro 皮肤变体同步切换（MetroSkin.Light / MetroSkin.Dark）
            SwapThemeDictInPlace(md, "Themes/MetroSkin.",
                $"pack://application:,,,/Themes/MetroSkin.{variant}");
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("Metro 深浅色变体切换失败: " + ex.Message);
        }
    }

    /// <summary>在合并字典列表里把含 contains 的主题字典原地换成 newSource（保持索引位置）。</summary>
    private static void SwapThemeDictInPlace(System.Collections.ObjectModel.Collection<ResourceDictionary> md,
        string contains, string newSource)
    {
        for (var i = 0; i < md.Count; i++)
        {
            var src = md[i].Source?.OriginalString;
            if (src == null || !src.Contains(contains)) continue;
            if (src.EndsWith(newSource, StringComparison.OrdinalIgnoreCase)) return; // 已是目标变体
            md.RemoveAt(i); // 同位替换，保持字典查找顺序稳定
            md.Insert(i, new ResourceDictionary { Source = new Uri(newSource) });
            return;
        }
    }

    /// <summary>v0.15.6：重启应用（当前进程退出，拉起新进程读取已保存的设置）。</summary>
    public static void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                });
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("重启应用失败: " + ex.Message);
        }
        Current.Shutdown();
    }



    public static void ApplyLanguage(string lang)
    {
        Language = lang;
        var resources = Current.Resources;
        var dict = new ResourceDictionary
        {
            // 绝对 pack URI：编译后的 BAML 资源，避免相对 Uri 解析差异
            Source = new Uri($"pack://application:,,,/Themes/Languages/Strings.{lang}.xaml")
        };
        // 移除旧语言字典（保留主题字典）
        for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var d = resources.MergedDictionaries[i];
            if (d.Source != null && d.Source.OriginalString.Contains("Themes/Languages/Strings."))
                resources.MergedDictionaries.RemoveAt(i);
        }
        resources.MergedDictionaries.Add(dict);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // v0.15.2：退出前先取消全部运行中任务 —— 此前直接 Dispose 服务，后台下载 /
        // 安装任务撞上已释放的 HttpClient 抛 ObjectDisposedException，
        // 经 UnhandledException 升级为「致命错误」（crash.log 2026-11-09 即此因）
        try { DownloadTaskService.Instance.CancelAllActive("启动器正在退出"); }
        catch (Exception ex) { Services.BootLogger.Error("退出取消任务失败: " + ex.Message); }
        // 退出时保存设置：config/ 只读等异常不阻止正常退出
        try { AppSettings.Instance.Save(); }
        catch (Exception ex) { Services.BootLogger.Error("退出时保存设置失败: " + ex.Message); }
        LauncherService?.Dispose();
        base.OnExit(e);
    }
}
