using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HardmacroCraft.Services;

/// <summary>
/// 子窗口标题栏助手（fix5）：ExtendsContentIntoTitleBar=true 的 FluentWindow
/// 若模板里没有 ui:TitleBar，WPF 不会绘制任何系统标题栏按钮，
/// 窗口只能 Alt+F4 关闭（「账号管理 / 游戏安装等子窗口没有关闭按钮」的根因）。
/// 本助手把原内容包进 Grid，并在顶部挂一条 Wpf.Ui 标题栏（含最小化 / 最大化 / 关闭按钮，
/// NoResize 窗口自动隐藏最小化 / 最大化，只保留关闭）。
/// </summary>
public static class FluentDialog
{
    public static void SetContent(Window win, FrameworkElement content)
    {
        var resizable = win.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip
            or ResizeMode.CanMinimize;
        var tb = new Wpf.Ui.Controls.TitleBar
        {
            Title = win.Title ?? string.Empty,
            Padding = new Thickness(10, 8, 0, 0),
            ShowMinimize = resizable,
            ShowMaximize = win.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip,
            ShowClose = true,
        };

        // fix7：Esc 关闭窗口（PCL 同款）—— 所有走本助手的弹窗统一生效
        win.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) win.Close();
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(tb, 0);
        root.Children.Add(tb);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        win.Content = root;
    }
}
