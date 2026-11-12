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
        DispatcherUnhandledException += (_, args) =>
        {
            Services.BootLogger.Error("UI线程未处理异常: " + Services.BootLogger.Format(args.Exception));
            try
            {
                File.AppendAllText(
                    Path.Combine(AppSettings.ConfigDir, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {Services.BootLogger.Format(args.Exception)}{Environment.NewLine}");
            }
            catch { }
            MessageBox.Show($"发生错误：{args.Exception.Message}\n\n详细信息已记录到 config/crash.log",
                "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

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

        Services.BootLogger.Info($"应用语言 {AppSettings.Instance.Language} ...");
        ApplyLanguage(AppSettings.Instance.Language);
        // 主题在窗口创建前应用（深/浅/跟随系统），窗口内的所有 DynamicResource 颜色会随之统一
        Services.BootLogger.Info($"应用主题 {AppSettings.Instance.Theme} ...");
        ApplyTheme(AppSettings.Instance.Theme, attachWatcher: false);
        Services.BootLogger.Info("主题应用完成，创建主窗口 ...");

        var win = new MainWindow();
        MainWindow = win;
        win.Show();
        Services.BootLogger.Info("主窗口已显示，启动完成");
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

        // 隧道路由（根→叶）：若鼠标位置与本滚动器之间还有更内层的 ScrollViewer，
        // 交给它处理（它先滚，滚不动才轮到我）
        if (HasInnerScrollViewer(sv, e.OriginalSource as DependencyObject)) return;

        // v1.8.0：每格固定像素步长（±120/格 → 100px），对齐主流启动器 / 浏览器手感；
        // 旧值 40px 页面滚动太短，而列表又是按条目计偏移导致一格翻底（见 LogicalDelta）
        const double WheelPxPerNotch = 100.0;
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
        const Wpf.Ui.Controls.WindowBackdropType backdrop = Wpf.Ui.Controls.WindowBackdropType.Mica;
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
        // 退出时保存设置：config/ 只读等异常不阻止正常退出
        try { AppSettings.Instance.Save(); }
        catch (Exception ex) { Services.BootLogger.Error("退出时保存设置失败: " + ex.Message); }
        LauncherService?.Dispose();
        base.OnExit(e);
    }
}
