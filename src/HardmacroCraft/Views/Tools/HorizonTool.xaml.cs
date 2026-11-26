using System.IO;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>
/// v0.16.7：Horizon（Rust 版 Minecraft）工具（独立窗口版，自 ToolsPage 迁移）。
/// 加载即爬取最新 release；下载→自动解压/安装→检测→启动全链路。
/// </summary>
public partial class HorizonTool : UserControl
{
    // 最近一次下载的 Horizon 安装包（zip 自动解压后无需用到；setup.exe 时供「运行安装包」）
    private string? _horizonLastFile;

    public HorizonTool()
    {
        InitializeComponent();
        Loaded += async (_, _) => await FetchHorizonInfoAsync();
    }

    /// <summary>爬取 GitHub 最新 release 信息并展示（失败静默，不打断使用）。</summary>
    private async Task FetchHorizonInfoAsync()
    {
        try
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonFetching") ?? "正在获取最新版本…";
            var info = await HorizonService.FetchLatestAsync(App.LauncherService.Http);
            if (info is null)
            {
                TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonNoAsset") ?? "未找到 Windows 安装包，请用「浏览器手动下载」。";
                return;
            }
            TxtHorizonStatus.Text = $"Horizon {info.Version} · {info.AssetName} · 约 {info.SizeBytes / 1024 / 1024} MB"
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonRuntime") ?? "下载内容为 Rust 版 Minecraft 版本包（含启动器），解压即用；Java 运行时由 Horizon 自动安装。");
        }
        catch
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonManualHint") ?? "获取失败，可用「浏览器手动下载」。";
        }
    }

    private void BtnHorizonDetect_Click(object sender, RoutedEventArgs e)
    {
        var dir = HorizonService.DetectInstall();
        TxtHorizonStatus.Text = dir is null
            ? (string?)TryFindResource("S.Tool.HorizonNotFound") ?? "未在常见位置检测到 Horizon 安装。"
            : $"✓ {dir}";
    }

    private async void BtnHorizonDownload_Click(object sender, RoutedEventArgs e)
    {
        // 防重入：下载中再点无效
        if (BtnHorizonDownload.IsEnabled == false) return;
        BtnHorizonDownload.IsEnabled = false;
        HorizonProgress.Visibility = Visibility.Visible;
        HorizonProgress.IsIndeterminate = true;
        try
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonFetching") ?? "正在获取最新版本信息…";
            var info = await HorizonService.FetchLatestAsync(App.LauncherService.Http);
            if (info is null)
            {
                TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonNoAsset") ?? "未找到 Windows 安装包，请到发布页手动下载。";
                return;
            }
            var mb = info.SizeBytes / 1024 / 1024;
            TxtHorizonStatus.Text = $"Horizon {info.Version}：{info.AssetName}（约 {mb} MB）";
            HorizonProgress.IsIndeterminate = false;
            var progress = new Progress<long>(b =>
            {
                if (info.SizeBytes > 0) HorizonProgress.Value = 100d * b / info.SizeBytes;
            });
            var dest = await HorizonService.DownloadLauncherAsync(App.LauncherService.Http, info, progress);
            HorizonProgress.Value = 100;
            _horizonLastFile = dest;
            // zip 版本包自动解压（解压后即可直接「启动 Horizon」）；setup.exe 安装包
            // 则亮出「运行安装包」按钮（含 UAC 提权，用户确认后静默交给安装器）
            var extracted = await HorizonService.ExtractIfArchiveAsync(dest, info.Version);
            if (extracted is not null)
            {
                BtnHorizonRun.Visibility = Visibility.Collapsed;
                TxtHorizonStatus.Text = ((string?)TryFindResource("S.Tool.HorizonDone") ?? "下载完成：") + dest
                    + "\n" + ((string?)TryFindResource("S.Tool.HorizonExtracted") ?? "已自动解压：") + extracted
                    + "\n" + ((string?)TryFindResource("S.Tool.HorizonDirect") ?? "点「启动 Horizon」即可开始（需正版 Minecraft Java 26.3，Java 运行时由 Horizon 自动安装）。");
                HorizonService.RevealInExplorer(extracted);
            }
            else
            {
                BtnHorizonRun.Visibility = Visibility.Visible;
                TxtHorizonStatus.Text = ((string?)TryFindResource("S.Tool.HorizonDone") ?? "下载完成：") + dest
                    + "\n" + ((string?)TryFindResource("S.Tool.HorizonRunIt") ?? "双击安装包安装 Horizon；运行需正版 Minecraft Java 26.3（官方启动器跑过一次），Java 运行时由 Horizon 自动安装。");
                HorizonService.RevealInExplorer(dest);
            }
        }
        catch (Exception ex)
        {
            TxtHorizonStatus.Text = ((string?)TryFindResource("S.Tool.HorizonFail") ?? "下载失败：") + ex.Message
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonManualHint") ?? "可用「浏览器手动下载」按钮到发布页下载。");
        }
        finally
        {
            BtnHorizonDownload.IsEnabled = true;
        }
    }

    private void BtnHorizonManual_Click(object sender, RoutedEventArgs e) =>
        ToolHostWindow.OpenUrl(HorizonService.ReleasesUrl);

    private void BtnHorizonRun_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_horizonLastFile) || !File.Exists(_horizonLastFile))
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonRunMissing") ?? "安装包不存在——请重新下载。";
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_horizonLastFile)
            {
                UseShellExecute = true, // 安装器需要提权：shell 执行触发 UAC
            });
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonRunStarted") ?? "已运行安装包（如弹出 UAC 请确认）。安装完成后点「检测本地安装」。";
        }
        catch (Exception ex)
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonRunFail") ?? "运行失败：" + ex.Message;
        }
    }

    private async void BtnHorizonLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnHorizonLaunch.IsEnabled = false;
            TxtHorizonStatus.Text = "正在定位 Horizon 可执行文件…";
            var exe = await HorizonLaunch.LaunchDetectedAsync();
            TxtHorizonStatus.Text = "已启动：" + exe;
        }
        catch (System.IO.FileNotFoundException) { TxtHorizonStatus.Text = "未找到 Horizon——请先「下载 Rust 版 MC」并解压版本包，或安装后再试。"; }
        catch (Exception ex) { TxtHorizonStatus.Text = "启动失败：" + ex.Message; }
        finally { BtnHorizonLaunch.IsEnabled = true; }
    }
}
