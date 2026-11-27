using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.8：日志查看器（独立窗口）—— 列表 / 查看 / 复制 / mclo.gs 上传 / 定位。</summary>
public partial class LogTool : UserControl
{
    public LogTool()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    private void BtnLogsRefresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        try
        {
            var logs = LogViewerService.ListLogs();
            CmbLogs.ItemsSource = logs;
            if (logs.Count > 0)
            {
                CmbLogs.SelectedIndex = 0;
                TxtLogsStatus.Text = $"共 {logs.Count} 条（运行日志 + 崩溃报告，游戏根与隔离版本都扫了，新在前）。";
            }
            else
            {
                TxtLogContent.Text = "";
                TxtLogsStatus.Text = "没有找到日志或崩溃报告——先启动一次游戏再来看。";
            }
        }
        catch (Exception ex) { TxtLogsStatus.Text = "扫描失败：" + ex.Message; }
    }

    private void CmbLogs_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadSelected();

    private void LoadSelected()
    {
        if (CmbLogs.SelectedItem is not LogViewerService.LogFileInfo f) { TxtLogContent.Text = ""; return; }
        try
        {
            TxtLogContent.Text = LogViewerService.ReadLog(f.Path);
        }
        catch (Exception ex)
        {
            TxtLogContent.Text = "";
            TxtLogsStatus.Text = "读取失败：" + ex.Message;
        }
    }

    private LogViewerService.LogFileInfo? Selected =>
        CmbLogs.SelectedItem as LogViewerService.LogFileInfo;

    private void BtnLogsCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { TxtLogsStatus.Text = "请先选择日志。"; return; }
        try
        {
            Clipboard.SetText(TxtLogContent.Text);
            TxtLogsStatus.Text = "✓ 已复制到剪贴板（可粘贴到求助帖）。";
        }
        catch (Exception ex) { TxtLogsStatus.Text = "复制失败：" + ex.Message; }
    }

    private async void BtnLogsUpload_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is null) { TxtLogsStatus.Text = "请先选择日志。"; return; }
        try
        {
            BtnLogsUpload.IsEnabled = false;
            TxtLogsStatus.Text = "上传 mclo.gs 中（日志公开分享链接，求助帖标配）…";
            var url = await LogViewerService.UploadToMclogsAsync(App.LauncherService.Http, TxtLogContent.Text);
            TxtLogsStatus.Text = "✓ 上传完成：" + url + "（已复制到剪贴板）";
            Clipboard.SetText(url);
            ToolHostWindow.OpenUrl(url);
        }
        catch (Exception ex) { TxtLogsStatus.Text = "上传失败：" + ex.Message; }
        finally { BtnLogsUpload.IsEnabled = true; }
    }

    private void BtnLogsReveal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } f) { TxtLogsStatus.Text = "请先选择日志。"; return; }
        try { HorizonService.RevealInExplorer(f.Path); }
        catch (Exception ex) { TxtLogsStatus.Text = "打开失败：" + ex.Message; }
    }
}
