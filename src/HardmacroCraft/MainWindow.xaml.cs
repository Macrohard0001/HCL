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
            RootNavigation.Navigated += (_, e) => currentPage = e.Page as System.Windows.Controls.Page;
        }
        catch { currentPage = null; }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            if (currentPage is Views.HomePage) return; // 已在主页，无操作
            try { RootNavigation.Navigate(typeof(Views.HomePage)); e.Handled = true; } catch { }
        };
    }
}
