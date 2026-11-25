using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.10：种子工具页 —— 史莱姆挂机点查询（算法见 SlimeChunkFinder）。
/// v0.16.6 升级为内置种子地图：纯算法图层（史莱姆区块 + 最佳挂机点黄色高亮）+
/// 拖动平移 / 滚轮缩放 / 光标实时区块与方块坐标 / 大图窗口导出。
/// 不含地形群系渲染（需 cubiomes 级移植，v0.15.10 已评估放弃）——口径在图例中如实标注。
/// </summary>
public partial class ToolsPage : Page
{
    private const int ChunkPx = 4;                 // 底图每区块像素（放大交给 RenderTransform）
    private const uint SlimeColor = 0xFF3CA84C;    // BGRA：史莱姆绿
    private const uint BaseColor = 0xFF2A2A2A;     // BGRA：底格深灰
    private const uint FarmColor = 0xFFFFB900;     // BGRA：最佳挂机点高亮（黄）
    private const double ZoomMin = 1.0, ZoomMax = 12.0;

    private long _seed;
    private int _seedRadius;
    private bool[,] _seedMap = new bool[0, 0];
    private int _farmX = int.MinValue, _farmZ, _farmSize;
    private bool _panning;
    private Point _dragStart;
    private Vector _transStart;

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
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonRuntime") ?? "下载内容为 Rust 版 Minecraft 版本包（含启动器），解压即用；Java 运行时由 Horizon 自动安装。");
        }
        catch
        {
            TxtHorizonStatus.Text = (string?)TryFindResource("S.Tool.HorizonManualHint") ?? "获取失败，可用「浏览器手动下载」。";
        }
    }

    // ===== v0.16.6：内置种子地图（史莱姆区块图层 + 平移 / 缩放 / 坐标追踪） =====

    private void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        // 种子：接受任意 64 位整数（含负数；MC /seed 输出的形式）
        if (!long.TryParse(TxtSeed.Text.Trim(), NumberFormatInfo.InvariantInfo, out var seed))
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.InvalidSeed") ?? "种子无效";
            SeedMapHost.Visibility = Visibility.Collapsed;
            TxtSeedPos.Visibility = Visibility.Collapsed;
            return;
        }
        if (!int.TryParse(TxtRange.Text.Trim(), out var radius) || radius is < 5 or > 64)
            radius = 40; // 缺省半径 40 区块（±320 格），够挑挂机塔位置

        var size = radius * 2 + 1;
        _seed = seed;
        _seedRadius = radius;
        _seedMap = SlimeChunkFinder.Map(seed, -radius, radius, -radius, radius, out var slimeCount);

        // WriteableBitmap 逐格渲染（BGRA32，每区块 ChunkPx 像素）
        var bmp = new WriteableBitmap(size * ChunkPx, size * ChunkPx, 96, 96, PixelFormats.Bgra32, null);
        var stride = size * ChunkPx * 4;
        var pixels = new uint[size * ChunkPx * size * ChunkPx];
        for (var cz = 0; cz < size; cz++)
            for (var cx = 0; cx < size; cx++)
            {
                var color = _seedMap[cz, cx] ? SlimeColor : BaseColor;
                for (var py = 0; py < ChunkPx; py++)
                {
                    var rowBase = ((cz * ChunkPx) + py) * stride / 4 + cx * ChunkPx;
                    for (var px = 0; px < ChunkPx; px++)
                        pixels[rowBase + px] = color;
                }
            }
        _farmX = int.MinValue;
        if (SlimeChunkFinder.BestFarmSpot(_seedMap, -radius, -radius) is var (centerX, centerZ, spotSize))
        {
            _farmX = centerX; _farmZ = centerZ; _farmSize = spotSize;
            // 最佳挂机点连片方块染黄（中心 ± size/2 的 size×size 范围）
            var half = spotSize / 2;
            for (var dz = -half; dz <= spotSize - 1 - half; dz++)
                for (var dx = -half; dx <= spotSize - 1 - half; dx++)
                {
                    var gx = (centerX + dx + radius) * ChunkPx;
                    var gz = (centerZ + dz + radius) * ChunkPx;
                    for (var py = 0; py < ChunkPx; py++)
                    {
                        var rowBase = (gz + py) * stride / 4 + gx;
                        for (var px = 0; px < ChunkPx; px++)
                            pixels[rowBase + px] = FarmColor;
                    }
                }
        }
        bmp.WritePixels(new Int32Rect(0, 0, size * ChunkPx, size * ChunkPx), pixels, stride, 0);
        SeedMapImage.Source = bmp;
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = 1;
        SeedMapTranslate.X = SeedMapTranslate.Y = 0;
        SeedMapHost.Visibility = Visibility.Visible;
        TxtSeedPos.Visibility = Visibility.Visible;
        TxtSeedPos.Text = "";

        var total = size * size;
        var statsTemplate = (string?)TryFindResource("S.Tool.Stats") ?? "史莱姆区块 {0} 个 / 共 {1} 个（{2}%）";
        var stats = string.Format(statsTemplate, slimeCount, total,
            (100.0 * slimeCount / total).ToString("0.0", CultureInfo.CurrentCulture));

        if (_farmX != int.MinValue)
        {
            // 区块 → 方块坐标（区块左上角 16×16 范围）
            var bestTemplate = (string?)TryFindResource("S.Tool.Best") ??
                "最佳挂机点：区块 ({0}, {1})，{2}×{2} 全绿连片，方块坐标 ({3}, {4})——挂机层 Y<40 有效";
            TxtStatus.Text = stats + "\n" + string.Format(bestTemplate,
                _farmX, _farmZ, _farmSize, _farmX * 16, _farmZ * 16);
        }
        else
        {
            TxtStatus.Text = stats + "\n" + ((string?)TryFindResource("S.Tool.BestNone")
                ?? "范围内没有 2×2 连片的史莱姆区块（可扩大半径再试）");
        }
    }

    /// <summary>宿主坐标 → 底图区块坐标（含变换反解与越界判定）。</summary>
    private (int ChunkX, int ChunkZ)? ChunkAt(Point hostPos)
    {
        if (SeedMapImage.Source is not BitmapSource bmp) return null;
        var px = (hostPos.X - SeedMapTranslate.X) / SeedMapScale.ScaleX;
        var py = (hostPos.Y - SeedMapTranslate.Y) / SeedMapScale.ScaleY;
        var size = _seedRadius * 2 + 1;
        var cx = (int)Math.Floor(px / ChunkPx);
        var cz = (int)Math.Floor(py / ChunkPx);
        if (cx < 0 || cz < 0 || cx >= size || cz >= size || px < 0 || py < 0) return null;
        return (cx - _seedRadius, cz - _seedRadius);
    }

    /// <summary>把平移量夹在「图像始终与宿主有交集」的范围内（拖丢不迷路）。</summary>
    private void ClampTranslate()
    {
        if (SeedMapImage.Source is not BitmapSource bmp) return;
        var w = bmp.PixelWidth * SeedMapScale.ScaleX;
        var h = bmp.PixelHeight * SeedMapScale.ScaleY;
        var minX = Math.Min(0, SeedMapHost.ActualWidth - w);
        var maxX = Math.Max(0, SeedMapHost.ActualWidth - w);
        var minY = Math.Min(0, SeedMapHost.ActualHeight - h);
        var maxY = Math.Max(0, SeedMapHost.ActualHeight - h);
        SeedMapTranslate.X = Math.Clamp(SeedMapTranslate.X, minX, maxX);
        SeedMapTranslate.Y = Math.Clamp(SeedMapTranslate.Y, minY, maxY);
    }

    private void SeedMap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible) return;
        _panning = true;
        _dragStart = e.GetPosition(SeedMapHost);
        _transStart = new Vector(SeedMapTranslate.X, SeedMapTranslate.Y);
        SeedMapHost.CaptureMouse();
    }

    private void SeedMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible) return;
        // 光标坐标追踪：区块 + 方块（实时）
        if (ChunkAt(e.GetPosition(SeedMapHost)) is var (cx, cz))
        {
            var tpl = (string?)TryFindResource("S.Tool.SeedPos") ?? "区块 ({0}, {1}) · 方块 ({2}, {3})";
            TxtSeedPos.Text = string.Format(tpl, cx, cz, cx * 16, cz * 16);
        }
        else
        {
            TxtSeedPos.Text = "";
        }
        if (!_panning) return;
        var pos = e.GetPosition(SeedMapHost);
        SeedMapTranslate.X = _transStart.X + (pos.X - _dragStart.X);
        SeedMapTranslate.Y = _transStart.Y + (pos.Y - _dragStart.Y);
        ClampTranslate();
    }

    private void SeedMap_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        SeedMapHost.ReleaseMouseCapture();
    }

    private void SeedMap_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        SeedMapHost.ReleaseMouseCapture();
    }

    private void SeedMap_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible || SeedMapImage.Source is null) return;
        var oldScale = SeedMapScale.ScaleX;
        var newScale = Math.Clamp(oldScale * (e.Delta > 0 ? 1.2 : 1 / 1.2), ZoomMin, ZoomMax);
        if (Math.Abs(newScale - oldScale) < 0.0001) { e.Handled = true; return; }
        // 以光标为锚点缩放：光标下的区块在缩放前后保持不动
        var pos = e.GetPosition(SeedMapHost);
        SeedMapTranslate.X = pos.X - (pos.X - SeedMapTranslate.X) * (newScale / oldScale);
        SeedMapTranslate.Y = pos.Y - (pos.Y - SeedMapTranslate.Y) * (newScale / oldScale);
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = newScale;
        ClampTranslate();
        e.Handled = true;
    }

    private void BtnSeedReset_Click(object sender, RoutedEventArgs e)
    {
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = 1;
        SeedMapTranslate.X = SeedMapTranslate.Y = 0;
    }

    private void BtnSeedBig_Click(object sender, RoutedEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible || SeedMapImage.Source is not BitmapSource bmp)
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.SeedFirst") ?? "请先生成种子地图。";
            return;
        }
        try
        {
            Directory.CreateDirectory(HorizonService.DownloadDir);
            var path = Path.Combine(HorizonService.DownloadDir,
                $"HmSeedMap-{_seed}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            var title = (string?)TryFindResource("S.Tool.SlimeTitle") ?? "种子地图";
            MapViewerWindow.Show(Window.GetWindow(this), path, $"{title} · seed={_seed} · ±{_seedRadius} 区块");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.SeedExportFail") ?? "导出失败：" + ex.Message;
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
            _horizonLastFile = dest;
            // v0.16.6：zip 版本包自动解压（解压后即可直接「启动 Horizon」）；setup.exe 安装包
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

    private void BtnHorizonManual_Click(object sender, RoutedEventArgs e) => OpenUrl(HorizonService.ReleasesUrl);

    // v0.16.6：最近一次下载的 Horizon 安装包（zip 自动解压后无需用到；setup.exe 时供「运行安装包」）
    private string? _horizonLastFile;

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

    // ---- v0.16.2：基岩版（只走正版路径：Store 版检测/启动 + 官方 BDS 下载）----

    private async void BtnBedrockDetect_Click(object sender, RoutedEventArgs e)
    {
        TxtBedrockStatus.Text = "正在检测 Microsoft Store 版基岩版…";
        try
        {
            var inst = await BedrockService.DetectStoreInstallAsync();
            TxtBedrockStatus.Text = inst is null
                ? "未检测到已安装的 Store 版基岩版——请用「购买/安装（官方）」获取。"
                : $"已安装：版本 {inst.Version}\n位置：{inst.InstallLocation}\n微软账号随系统登录，单机/离线由商店版自身支持。";
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "检测失败：" + ex.Message; }
    }

    private async void BtnBedrockLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BedrockService.LaunchStoreAsync();
            TxtBedrockStatus.Text = "已通过 shell:AppsFolder 启动商店版基岩版。";
        }
        catch (System.IO.FileNotFoundException) { TxtBedrockStatus.Text = "未检测到已安装的 Store 版基岩版——请先「购买/安装（官方）」。"; }
        catch (Exception ex) { TxtBedrockStatus.Text = "启动失败：" + ex.Message; }
    }

    private async void BtnBedrockBds_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnBedrockBds.IsEnabled = false;
            TxtBedrockStatus.Text = "正在爬取官方 BDS 下载页…";
            var bds = await BedrockService.FetchLatestBdsAsync(App.LauncherService.Http)
                ?? throw new InvalidOperationException("官方页面结构变化，未找到 BDS 直链——请到 minecraft.net 官方页下载。");
            BedrockProgress.IsIndeterminate = true;
            BedrockProgress.Visibility = Visibility.Visible;
            TxtBedrockStatus.Text = $"BDS {bds.Version} 开始下载（重试/续传已启用）…";
            BedrockProgress.IsIndeterminate = false;
            var progress = new Progress<long>(b => BedrockProgress.Value = Math.Min(100, 100d * b / 80_000_000));
            var dest = await BedrockService.DownloadBdsAsync(App.LauncherService.Http, bds.Url, progress);
            BedrockProgress.Value = 100;
            TxtBedrockStatus.Text = "服务端已下载：" + dest + "\n解压后运行 bedrock_server.exe 即可开服（局域网离线联机）。";
            BedrockService.RevealInExplorer(dest);
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "下载失败：" + ex.Message; }
        finally { BtnBedrockBds.IsEnabled = true; }
    }

    private void BtnBedrockStore_Click(object sender, RoutedEventArgs e) => OpenUrl(BedrockService.StoreUrl);

    private async void BtnBedrockJoin_Click(object sender, RoutedEventArgs e)
    {
        var addr = TxtJoinAddr.Text.Trim();
        var name = string.IsNullOrWhiteSpace(TxtJoinName.Text) ? "Bedrock Server" : TxtJoinName.Text.Trim();
        if (addr.Length == 0 || !addr.Contains(':'))
        {
            TxtBedrockStatus.Text = "请填写 服务器地址:端口（例如 play.example.net:19132）。";
            return;
        }
        try
        {
            await BedrockService.JoinServerAsync(name, addr);
            TxtBedrockStatus.Text = $"已通过 minecraft:// 深链注册「{name}」（{addr}）并启动基岩版——游戏内「服务器」列表可见。";
        }
        catch (System.IO.FileNotFoundException) { TxtBedrockStatus.Text = "未检测到已安装的 Store 版基岩版——请先「购买/安装（官方）」。"; }
        catch (Exception ex) { TxtBedrockStatus.Text = "启动失败：" + ex.Message; }
    }

    private async void BtnLeviDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnLeviDownload.IsEnabled = false;
            TxtBedrockStatus.Text = "正在获取 LeviLauncher 最新 Release（api.github.com）…";
            var rel = await BedrockService.FetchLatestLeviAsync(App.LauncherService.Http)
                ?? throw new InvalidOperationException("未找到可用资产——请到 GitHub Releases 手动下载。");
            TxtBedrockStatus.Text = $"LeviLauncher {rel.Version}（{rel.AssetName}）开始下载…";
            var dest = await BedrockService.DownloadLeviAsync(App.LauncherService.Http, rel, null);
            TxtBedrockStatus.Text = "已下载：" + dest + "\nLeviLauncher 要求正版授权副本与微软账号登录（见其 README）——解压后按文档使用。";
            BedrockService.RevealInExplorer(dest);
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "下载失败：" + ex.Message; }
        finally { BtnLeviDownload.IsEnabled = true; }
    }

    private void BtnLeviDocs_Click(object sender, RoutedEventArgs e) => OpenUrl(BedrockService.LeviDocsUrl);

    // ---- v0.16.4：Horizon（Rust 版 MC）启动 ----

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

    // ---- v0.16.4：从其他启动器导入（用户友好迁移向导） ----

    private async void BtnMigScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnMigScan.IsEnabled = false;
            TxtMigStatus.Text = "正在扫描内置位置（官方 .minecraft / Prism / PolyMC / MultiMC 及常见嵌套布局）…";
            var cands = await Task.Run(() =>
                LauncherMigrationService.BuiltinRoots()
                    .SelectMany(LauncherMigrationService.ScanFolder)
                    .GroupBy(c => (c.LauncherKind, c.VersionName))
                    .Select(g => g.First())
                    .ToList());
            CmbMigCandidates.ItemsSource = cands;
            if (cands.Count > 0)
            {
                CmbMigCandidates.SelectedIndex = 0;
                ChkMigIsolation.IsChecked = AppSettings.Instance.VersionIsolation;
            }
            TxtMigStatus.Text = cands.Count == 0
                ? "内置位置没有发现可导入的版本——常见启动器布局（官方 .minecraft / instances 容器 / 二级版本目录）均已支持，也可把对应目录放进上述位置再扫。"
                : $"发现 {cands.Count} 个候选版本，下拉选择后点「导入所选」。";
        }
        catch (Exception ex) { TxtMigStatus.Text = "扫描失败：" + ex.Message; }
        finally { BtnMigScan.IsEnabled = true; }
    }

    private async void BtnMigImport_Click(object sender, RoutedEventArgs e)
    {
        if (CmbMigCandidates.SelectedItem is not MigrationCandidate cand)
        {
            TxtMigStatus.Text = "请先「扫描」并选择一个候选版本。";
            return;
        }
        try
        {
            BtnMigImport.IsEnabled = false;
            var isolation = ChkMigIsolation.IsChecked == true;
            TxtMigStatus.Text = $"导入中：{cand.VersionName}（{cand.LauncherKind}，{(isolation ? "带实例数据" : "仅版本")}）…";
            var name = await Task.Run(() => LauncherMigrationService.Import(cand, isolation));
            TxtMigStatus.Text = $"✓ 导入完成，新版本名「{name}」——启动页 / 版本列表刷新后即可使用。";
        }
        catch (Exception ex) { TxtMigStatus.Text = "导入失败：" + ex.Message; }
        finally { BtnMigImport.IsEnabled = true; }
    }

    // ---- v0.16.5：世界地图（KAMUCL 式地图查看） ----

    private List<(string Name, string Dir)> RefreshMapWorlds()
    {
        var worlds = WorldMapRenderer.FindWorlds();
        CmbMapWorlds.ItemsSource = worlds.Select(w => w.Name).ToList();
        CmbMapWorlds.SelectedIndex = worlds.Count > 0 ? 0 : -1;
        try
        {
            CmbBackupVersion.ItemsSource = App.LauncherService.GetInstalledVersions();
        }
        catch { /* 版本列表读取失败不阻塞地图扫描 */ }
        return worlds;
    }

    private void BtnMapScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var worlds = RefreshMapWorlds();
            TxtMapStatus.Text = worlds.Count == 0
                ? "没有找到带 level.dat 的世界（共享 saves 与隔离 versions/*/saves 都扫了）——先进游戏创建存档。"
                : $"找到 {worlds.Count} 个世界，选择后点「生成顶视图」。";
        }
        catch (Exception ex) { TxtMapStatus.Text = "扫描失败：" + ex.Message; }
    }

    private async void BtnMapRender_Click(object sender, RoutedEventArgs e)
    {
        if (CmbMapWorlds.SelectedItem is not string name)
        {
            TxtMapStatus.Text = "请先「扫描世界」并选择一个存档。";
            return;
        }
        var hit = WorldMapRenderer.FindWorlds().FirstOrDefault(w => w.Name == name);
        var dir = hit.Dir;
        if (string.IsNullOrEmpty(dir)) { TxtMapStatus.Text = "世界目录已不存在，请重新扫描。"; return; }
        try
        {
            BtnMapRender.IsEnabled = false;
            TxtMapStatus.Text = $"正在渲染「{name}」的顶视图（后台解析 region/*.mca）…";
            var pngPath = Path.Combine(HorizonService.DownloadDir, $"HmMap-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var result = await Task.Run(() =>
            {
                var (png, w, h, info, ok, skip) = WorldMapRenderer.RenderWorld(dir);
                Directory.CreateDirectory(HorizonService.DownloadDir);
                File.WriteAllBytes(pngPath, png);
                return (Info: info, Chunks: ok, Skipped: skip);
            });
            var note = result.Skipped > 0 ? $"，{result.Skipped} 个旧格式区块已跳过" : "";
            TxtMapStatus.Text = $"✓ 渲染完成：{result.Chunks} 个区块{note} · {result.Info} → {pngPath}";
            MapViewerWindow.Show(Window.GetWindow(this), pngPath, $"{result.Info} · {result.Chunks} 区块{note}");
        }
        catch (Exception ex) { TxtMapStatus.Text = "渲染失败：" + ex.Message; }
        finally { BtnMapRender.IsEnabled = true; }
    }

    // ---- v0.16.5：实例备份 / 恢复 ----

    private async void BtnBackupDo_Click(object sender, RoutedEventArgs e)
    {
        if (CmbBackupVersion.SelectedItem is not string ver)
        {
            TxtBackupStatus.Text = "请先选择要备份的版本。";
            return;
        }
        try
        {
            BtnBackupDo.IsEnabled = false;
            TxtBackupStatus.Text = $"备份「{ver}」中（整个版本目录打成 zip，含隔离实例数据）…";
            var path = await Task.Run(() => InstanceBackupService.Backup(ver));
            TxtBackupStatus.Text = "✓ 备份完成：" + path;
            HorizonService.RevealInExplorer(path);
        }
        catch (Exception ex) { TxtBackupStatus.Text = "备份失败：" + ex.Message; }
        finally { BtnBackupDo.IsEnabled = true; }
    }

    private async void BtnBackupRestore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择备份压缩包",
            Filter = "备份压缩包 (*.zip)|*.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            BtnBackupRestore.IsEnabled = false;
            TxtBackupStatus.Text = "恢复中（重名版本自动加 -restored 后缀，不会覆盖现有版本）…";
            var name = await Task.Run(() => InstanceBackupService.Restore(dlg.FileName));
            TxtBackupStatus.Text = $"✓ 已恢复为版本「{name}」——启动页 / 版本列表刷新后即可使用。";
        }
        catch (Exception ex) { TxtBackupStatus.Text = "恢复失败：" + ex.Message; }
        finally { BtnBackupRestore.IsEnabled = true; }
    }

    // ---- v0.16.6：截图管理（KAMUCL 同类实用功能） ----

    private List<ScreenshotService.ShotInfo> _shots = new();

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

    // ---- v0.16.4：一键诊断报告 ----

    private async void BtnDoctor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnDoctor.IsEnabled = false;
            TxtDoctorStatus.Text = "正在收集环境信息…";
            var path = await DoctorService.WriteReportAsync();
            TxtDoctorStatus.Text = "✓ 报告已生成：" + path;
            HorizonService.RevealInExplorer(path);
        }
        catch (Exception ex) { TxtDoctorStatus.Text = "诊断失败：" + ex.Message; }
        finally { BtnDoctor.IsEnabled = true; }
    }
}
