using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;
using MahApps.Metro.Controls;

namespace HardmacroCraft;

/// <summary>
/// v0.15.9：Metro（Win10 怀旧）主窗口壳 —— 双壳方案之二。
/// ThemeStyle=metro 时由 App.OnStartup 启用（替代 Fluent 壳 MainWindow）：
/// 窗框 MahApps MetroWindow（Win10 方形标题栏），导航窗格 Win10 NavigationView 式，
/// 页面与 Fluent 壳完全共用（按类型缓存实例）。
/// </summary>
public partial class MetroMainWindow : MetroWindow
{
    private readonly Dictionary<Type, Page> _pages = new();
    private Page? _currentPage;
    private bool _suppressNavSelection; // 程序性选中（Esc 回主页）时防重入

    public MetroMainWindow()
    {
        InitializeComponent();
        // 记忆上次关闭时的最大化状态（与 Fluent 壳一致，尺寸始终默认 960×540）
        if (AppSettings.Instance.WindowMaximized)
            WindowState = WindowState.Maximized;

        Closing += (_, e) =>
        {
            AppSettings.Instance.WindowMaximized =
                WindowState == WindowState.Maximized;
            AppSettings.Instance.Save();
            // 任务未完成时确认退出（与 Fluent 壳同款）：直接关闭会让后台任务
            // 撞上已释放的服务对象抛致命错误（crash.log 2026-11-09）
            var active = DownloadTaskService.Instance.Snapshot().Count(t => t.IsActive);
            if (active > 0)
            {
                var r = System.Windows.MessageBox.Show(
                    $"还有 {active} 个任务正在进行中（下载 / 安装 / 启动）。\n\n" +
                    "退出会自动取消这些任务，确定退出吗？",
                    "Hardmacro Craft Launcher", MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) e.Cancel = true;
            }
        };

        // 默认落在主页；v0.15.15：同时建立导航选中态（v0.15.14 截图实证 SelPill
        // 从未亮过 —— Navigate 只切内容不选中 ListBoxItem，选中条触发器空转）
        Loaded += (_, _) =>
        {
            // v0.16.7：窗控钮（右上角）按深浅色变体显式套用 MahApps 窗控样式
            //（Win10 规格：无框扁平，close 悬停红）—— 此前 WindowButtonCommands.Theme
            // 走默认值，按钮在样式触发器不匹配时落到 Wpf.Ui Win11 隐式 Button（带 1px
            // 描边），用户反馈"右上角按钮边框都露出来了"。
            SyncWindowButtonsTheme();
            try
            {
                Navigate(typeof(Views.HomePage));
                NavList.SelectedIndex = 0;
            }
            catch (Exception navEx)
            { Services.BootLogger.Error("首页导航失败: " + Services.BootLogger.Format(navEx)); }
        };

        // v0.16.7：运行中深浅色切换（设置页 / 跟随系统）同步窗控钮样式
        App.MetroVariantChanged += _ => SyncWindowButtonsTheme();

        // fix7：Esc 返回主页（与 Fluent 壳同款）
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _currentPage is Views.HomePage) return;
            try { Navigate(typeof(Views.HomePage)); }
            catch (Exception navEx)
            { Services.BootLogger.Error("Esc 返回主页导航失败: " + Services.BootLogger.Format(navEx)); }
            _suppressNavSelection = true;
            NavList.SelectedItem = null;
            FooterNav.SelectedItem = null;
            _suppressNavSelection = false;
        };
    }

    /// <summary>导航窗格 / 底部栏选中 —— 按项 Tag（页面类型）加载页面。</summary>
    private void NavList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNavSelection) return;
        if ((sender as ListBox)?.SelectedItem is ListBoxItem { Tag: Type pageType })
        {
            try { Navigate(pageType); }
            catch (Exception navEx)
            {
                Services.BootLogger.Error(
                    $"Metro 壳导航失败 [{pageType.Name}]: " + Services.BootLogger.Format(navEx));
            }
            // 两个列表互斥选中（同页在另一列表保持空选）
            var other = sender == NavList ? FooterNav : NavList;
            _suppressNavSelection = true;
            other.SelectedItem = null;
            _suppressNavSelection = false;
        }
    }

    /// <summary>按类型加载页面（缓存实例；失败落日志，内容区不留白无提示）。</summary>
    private void Navigate(Type pageType)
    {
        if (_currentPage is not null && _currentPage.GetType() == pageType) return;
        if (!_pages.TryGetValue(pageType, out var page))
        {
            page = (Page)Activator.CreateInstance(pageType)!;
            _pages[pageType] = page;
            // v0.15.15：换骨层直接合并进页面级资源。v0.15.14 用户截图实证：
            // app 级 implicit 样式对窗口内容生效（汉堡/窗控钮都吃到了），但 Frame
            // 里 Page 内的 ui: 控件被隔离挡住（74 个 ui:Button 全是 Fluent 圆角）。
            // 页面级 MergedDictionaries 在 Page 资源查找链上无条件命中，绕开隔离。
            // 画刷全走 app 级 MetroSkin 变体键（DynamicResource），深浅色热切换不受影响。
            try
            {
                page.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Themes/Win10Controls.xaml")
                });
            }
            catch (Exception skinEx)
            {
                Services.BootLogger.Error("页面换骨层合并失败: " + Services.BootLogger.Format(skinEx));
            }
        }
        _currentPage = page;
        PageHost.Content = page;
    }

    /// <summary>汉堡按钮：折叠 224px 完整窗格 ↔ 48px 窄轨（窄轨隐藏标签）。</summary>
    private void Hamburger_Click(object sender, RoutedEventArgs e)
    {
        PaneBorder.Width = PaneBorder.Width > 48 ? 48 : 224;
    }

    /// <summary>
    /// v0.16.7：窗控钮样式随深浅色变体切换（Light/Dark 触发样式 = Win10 规格：
    /// 无框扁平 + close 悬停红 #E81123）。
    /// </summary>
    private void SyncWindowButtonsTheme()
    {
        try
        {
            var dark = App.CurrentDarkVariant;
            if (WindowButtonCommands.Theme != (dark ? "Dark" : "Light"))
                WindowButtonCommands.Theme = dark ? "Dark" : "Light";
        }
        catch (Exception ex)
        { Services.BootLogger.Error("窗控钮样式切换失败: " + Services.BootLogger.Format(ex)); }
    }
}
