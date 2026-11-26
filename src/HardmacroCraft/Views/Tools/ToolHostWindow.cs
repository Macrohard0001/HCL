using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views.Tools;

/// <summary>
/// v0.16.7：工具独立窗口宿主 —— 《工具》页重构后主窗口只留入口网格，
/// 每个工具在独立 FluentWindow 中打开（不影响主窗口，可多开并排）。
/// 窗体跟随应用背景色；内容 = 工具 UserControl，外层像素滚动。
/// </summary>
public static class ToolHostWindow
{
    public static void Show(Window? owner, string title, UserControl content)
    {
        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = title,
            Width = 880,
            Height = 660,
            MinWidth = 720,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
            FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI"),
        };
        if (App.Current.TryFindResource("ApplicationBackgroundBrush") is System.Windows.Media.Brush bg)
            win.Background = bg;
        if (owner?.IsLoaded == true) win.Owner = owner; else win.Owner = App.Current.MainWindow;

        content.Margin = new Thickness(24, 12, 24, 16);
        content.MaxWidth = 780;
        content.HorizontalAlignment = HorizontalAlignment.Stretch;
        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        win.Content = scroll;
        win.Show();
    }

    /// <summary>系统浏览器打开链接（Horizon 发布页 / Levi 文档 / 官方商店页等）。</summary>
    public static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
