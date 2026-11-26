using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：截图管理（独立窗口版，自 ToolsPage 迁移）——
/// 扫描共享与隔离 screenshots/，缩略图墙查看（ScreenshotViewerWindow）。</summary>
public partial class ScreenshotsTool : UserControl
{
    private List<ScreenshotService.ShotInfo> _shots = new();

    public ScreenshotsTool()
    {
        InitializeComponent();
        Loaded += (_, _) => BtnShotsScan_Click(this, new RoutedEventArgs()); // 打开即扫一次
    }

    private void BtnShotsScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _shots = ScreenshotService.ListAll();
            var dirs = ScreenshotService.FindScreenshotDirs();
            TxtShotsStatus.Text = _shots.Count == 0
                ? (string?)TryFindResource("S.Tool.ShotsNone")
                    ?? "没有找到截图——进游戏按 F2 截图后重试（共享与隔离版本的 screenshots/ 都会扫描）。"
                : string.Format(
                    (string?)TryFindResource("S.Tool.ShotsFound")
                    ?? "找到 {0} 张截图（{1} 个目录）——点「查看截图」打开缩略图墙。",
                    _shots.Count, dirs.Count);
        }
        catch (Exception ex) { TxtShotsStatus.Text = "扫描失败：" + ex.Message; }
    }

    private void BtnShotsView_Click(object sender, RoutedEventArgs e)
    {
        if (_shots.Count == 0)
        {
            BtnShotsScan_Click(sender, e); // 未先扫描则自动扫一次
            if (_shots.Count == 0) return;
        }
        ScreenshotViewerWindow.Show(Window.GetWindow(this), _shots);
    }
}
