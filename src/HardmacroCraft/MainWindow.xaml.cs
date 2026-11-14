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
                DisableNavigationViewOuterScrollWithRetry(); // v0.1.9：每次导航后确保关闭外层动态滚动
            };
        }
        catch { currentPage = null; }
        Loaded += (_, _) => DisableNavigationViewOuterScrollWithRetry(); // 首次加载同样处理
        // v0.15.0：侧栏超长时滚动（下载分组展开后条目超出窗口高度也可滚动查看，PCL 式）
        Loaded += (_, _) => EnsurePaneScrollableWithRetry();
        RootNavigation.Navigated += (_, _) => EnsurePaneScrollableWithRetry();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (currentPage is Views.HomePage) return; // 已在主页，无操作
            try { RootNavigation.Navigate(typeof(Views.HomePage)); e.Handled = true; } catch { }
        };
    }

    /// <summary>
    /// v0.15.0：确保导航窗格（侧栏）内容超长时可滚动。
    /// Wpf.Ui 的 NavigationView 模板里窗格条目宿主自带 ScrollViewer / ListView，
    /// 但个别状态（分组展开 + 小窗高）滚动条不出现 —— 找到窗格内的滚动宿主，
    /// 强制 VerticalScrollBarVisibility=Auto（内容区 presenter 的动态滚动不受影响，
    /// 那是另一个控件，且已被 DisableNavigationViewOuterScrollWithRetry 单独处理）。
    /// </summary>
    private void EnsurePaneScrollableWithRetry()
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var presenter = FindDescendant<Wpf.Ui.Controls.NavigationViewContentPresenter>(RootNavigation);
                    // 窗格内的 ScrollViewer（跳过内容区的动态 ScrollViewer）
                    EnsureScrollable(FindPaneDescendants<System.Windows.Controls.ScrollViewer>(RootNavigation, presenter));
                    // 窗格内的 ListView（自带模板 ScrollViewer，Auto 即可滚动）
                    foreach (var lv in FindPaneDescendants<System.Windows.Controls.ListView>(RootNavigation, presenter))
                        System.Windows.Controls.ScrollViewer.SetVerticalScrollBarVisibility(lv,
                            System.Windows.Controls.ScrollBarVisibility.Auto);
                }
                catch { /* 模板未实例化时静默跳过（下轮重试） */ }
            }), i == 0 ? System.Windows.Threading.DispatcherPriority.Loaded
                       : System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private static void EnsureScrollable(IEnumerable<System.Windows.Controls.ScrollViewer> viewers)
    {
        foreach (var sv in viewers)
        {
            if (sv.VerticalScrollBarVisibility == System.Windows.Controls.ScrollBarVisibility.Auto) continue;
            sv.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        }
    }

    /// <summary>收集 target 之外（= 窗格侧）的指定类型后代。</summary>
    private static IEnumerable<T> FindPaneDescendants<T>(System.Windows.DependencyObject? root,
        System.Windows.DependencyObject? excludeSubtree) where T : System.Windows.DependencyObject
    {
        if (root == null) yield break;
        if (ReferenceEquals(root, excludeSubtree)) yield break;
        if (root is T hit) yield return hit;
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
            foreach (var r in FindPaneDescendants<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i), excludeSubtree))
                yield return r;
    }

    /// <summary>
    /// v0.1.11：带重试的外层滚动禁用。v0.1.9 的实现只在导航瞬间找一次 presenter，
    /// 模板尚未实例化时静默失败且不再补试 —— 首页日志展开仍可能包进 DynamicScrollViewer
    /// 出现整页滚动。现在在导航后按 Loaded / Background 优先级重试多次，直到成功关闭为止。
    /// </summary>
    private void DisableNavigationViewOuterScrollWithRetry()
    {
        for (var i = 0; i < 6; i++)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var presenter = FindDescendant<Wpf.Ui.Controls.NavigationViewContentPresenter>(RootNavigation);
                presenter?.SetValue(
                    Wpf.Ui.Controls.NavigationViewContentPresenter.IsDynamicScrollViewerEnabledProperty,
                    false);
            }), i == 0 ? System.Windows.Threading.DispatcherPriority.Loaded
                       : System.Windows.Threading.DispatcherPriority.Background);
        }
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
