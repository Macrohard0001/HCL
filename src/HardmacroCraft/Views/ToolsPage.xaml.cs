using System.Globalization;
using System.IO;
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
                + "\n" + ((string?)TryFindResource("S.Tool.HorizonRuntime") ?? "下载内容为 Rust 版 Minecraft 版本包（含启动器），解压即用；Java 运行时由 Horizon 自动安装。");
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
