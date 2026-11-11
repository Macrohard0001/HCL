using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        // 记忆上次关闭时的最大化状态（尺寸始终默认 960×540）
        if (AppSettings.Instance.WindowMaximized)
        {
            WindowState = System.Windows.WindowState.Maximized;
        }
        Closing += (_, _) =>
        {
            AppSettings.Instance.WindowMaximized = WindowState == System.Windows.WindowState.Maximized;
            AppSettings.Instance.Save();
        };
        Loaded += (_, _) =>
        {
            // 默认落在启动页
            try { RootNavigation.Navigate(typeof(Views.HomePage)); } catch { }
            // 主题设置为"跟随系统"时挂上监听：Windows 深浅色切换时启动器实时跟随
            if (AppSettings.Instance.Theme == "system")
            {
                try { Wpf.Ui.Appearance.SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica, true); }
                catch { }
            }
        };
        // fix7：Esc 返回主页（PCL 同款「返回」）——子页面按 Esc 回到启动页，弹窗不受影响
        //（弹窗是独立 Window，有焦点时主窗口收不到按键）
        System.Windows.Controls.Page? currentPage = null;
        try
        {
            RootNavigation.Navigated += (_, e) =>
            {
                currentPage = e.Page as System.Windows.Controls.Page;
                DisableNavigationViewOuterScroll(); // v0.1.9：每次导航后确保关闭外层动态滚动
            };
        }
        catch { currentPage = null; }
        Loaded += (_, _) => DisableNavigationViewOuterScroll(); // 首次加载同样处理
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (currentPage is Views.HomePage) return; // 已在主页，无操作
            try { RootNavigation.Navigate(typeof(Views.HomePage)); e.Handled = true; } catch { }
        };
    }

    /// <summary>
    /// v0.1.9：消除「日志容器造成的首页滚动」。
    /// Wpf-Ui 的 NavigationViewContentPresenter 默认 IsDynamicScrollViewerEnabled=true，
    /// 页面被包在 DynamicScrollViewer 里：展开首页日志后 TextBox 按全部文本高度参与
    /// 无限高度测量，整页被撑高、出现外层滚动条（日志内滚永远不生效）。
    /// 所有页面均已自管滚动（页内 ScrollViewer / 列表虚拟化），这里关闭外层动态滚动。
    /// </summary>
    private void DisableNavigationViewOuterScroll()
    {
        try
        {
            var presenter = FindDescendant<Wpf.Ui.Controls.NavigationViewContentPresenter>(RootNavigation);
            presenter?.SetValue(
                Wpf.Ui.Controls.NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty, false);
        }
        catch { /* 模板尚未加载时忽略；Navigated / Loaded 会再次尝试 */ }
    }

    private static T? FindDescendant<T>(System.Windows.DependencyObject? d) where T : System.Windows.DependencyObject
    {
        if (d == null) return null;
        if (d is T hit) return hit;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
        for (var i = 0; i < count; i++)
        {
            var r = FindDescendant<T>(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
            if (r != null) return r;
        }
        return null;
    }
}
