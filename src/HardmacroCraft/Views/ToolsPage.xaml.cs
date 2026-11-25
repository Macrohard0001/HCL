using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.10：种子工具页 —— 史莱姆挂机点查询（算法见 SlimeChunkFinder）。
/// 网格渲染：每区块一格，绿=史莱姆区块；统计占比 + 3×3 / 2×2 全绿连片中心推荐。
/// </summary>
public partial class ToolsPage : Page
{
    private const int CellPx = 6;
    private const uint SlimeColor = 0xFF3CA84C;   // BGRA：史莱姆绿
    private const uint BaseColor = 0xFF2A2A2A;    // BGRA：底格深灰

    public ToolsPage()
    {
        InitializeComponent();
        // v0.16.1：Horizon 下载入口 —— 加载即爬取最新 release，状态栏直接显示版本/体积
        Loaded += async (_, _) => await FetchHorizonInfoAsync();
    }

    /// <summary>爬取 GitHub 最新 release 信息并展示（失败静默，不打断页面使用）。</summary>
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
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonRuntime") ?? "需正版 Minecraft Java 26.3；Java 运行时由 Horizon 启动器自动安装。");
        }
        catch
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonManualHint") ?? "获取失败，可用「浏览器手动下载」。";
        }
    }

    private void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        // 种子：接受任意 64 位整数（含负数；MC /seed 输出的形式）
        if (!long.TryParse(TxtSeed.Text.Trim(), NumberFormatInfo.InvariantInfo, out var seed))
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.InvalidSeed") ?? "种子无效";
            MapImage.Visibility = Visibility.Collapsed;
            return;
        }
        if (!int.TryParse(TxtRange.Text.Trim(), out var radius) || radius is < 5 or > 64)
            radius = 40; // 缺省半径 40 区块（±320 格），够挑挂机塔位置

        var size = radius * 2 + 1;
        var map = SlimeChunkFinder.Map(seed, -radius, radius, -radius, radius, out var slimeCount);

        // WriteableBitmap 逐格渲染（BGRA32）
        var bmp = new WriteableBitmap(size * CellPx, size * CellPx, 96, 96, PixelFormats.Bgra32, null);
        var stride = size * CellPx * 4;
        var pixels = new uint[size * CellPx * size * CellPx];
        for (var cz = 0; cz < size; cz++)
            for (var cx = 0; cx < size; cx++)
            {
                var color = map[cz, cx] ? SlimeColor : BaseColor;
                for (var py = 0; py < CellPx; py++)
                {
                    var rowBase = ((cz * CellPx) + py) * stride / 4 + cx * CellPx;
                    for (var px = 0; px < CellPx; px++)
                        pixels[rowBase + px] = color;
                }
            }
        bmp.WritePixels(new Int32Rect(0, 0, size * CellPx, size * CellPx), pixels, stride, 0);
        MapImage.Source = bmp;
        MapImage.Visibility = Visibility.Visible;

        var total = size * size;
        var statsTemplate = (string?)TryFindResource("S.Tool.Stats") ?? "史莱姆区块 {0} 个 / 共 {1} 个（{2}%）";
        var stats = string.Format(statsTemplate, slimeCount, total,
            (100.0 * slimeCount / total).ToString("0.0", CultureInfo.CurrentCulture));

        var spot = SlimeChunkFinder.BestFarmSpot(map, -radius, -radius);
        if (spot is var (centerX, centerZ, spotSize))
        {
            // 区块 → 方块坐标（区块左上角 16×16 范围）
            var bestTemplate = (string?)TryFindResource("S.Tool.Best") ??
                "最佳挂机点：区块 ({0}, {1})，{2}×{2} 全绿连片，方块坐标 ({3}, {4})——挂机层 Y<40 有效";
            TxtStatus.Text = stats + "\n" + string.Format(bestTemplate,
                centerX, centerZ, spotSize, centerX * 16, centerZ * 16);
        }
        else
        {
            TxtStatus.Text = stats + "\n" + ((string?)TryFindResource("S.Tool.BestNone")
                ?? "范围内没有 2×2 连片的史莱姆区块（可扩大半径再试）");
        }
    }

    // ===== v0.16.0：Horizon（Rust 重制版 Minecraft）=====
    // 出处：github.com/horizon-rust/horizon（LGPL-3.0）+ playhorizon.dev。
    // 官方无"转译器"——所谓"无损" = 281 套差分测试保证行为级无损；本页如实标注。

    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });

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
            TxtHorizonStatus.Text = ((string?)TryFindResource("S.Tool.HorizonDone") ?? "下载完成：") + dest
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonRunIt") ?? "双击安装包安装 Horizon；运行需正版 Minecraft Java 26.3（官方启动器跑过一次），Java 运行时由 Horizon 自动安装。");
            HorizonService.RevealInExplorer(dest);
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

    private void BtnHorizonManual_Click(object sender, RoutedEventArgs e) => OpenUrl(HorizonService.ReleasesUrl);
}
