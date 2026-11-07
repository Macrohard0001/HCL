using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using CmlLib.Core.Auth;
using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft.Views;

/// <summary>
/// PCL 式装载器选择弹窗：点击版本行的「安装」按钮后弹出，
/// 选择加载器（原版 / Forge / NeoForge / Fabric / Quilt / Legacy Fabric / LiteLoader）与版本后安装。
/// fix3：安装过程显示详细进度（文件数 / 字节 / 速度）、支持取消与后台安装
/// （后台 = 关闭弹窗任务继续，进度在任务中心可见），窗口关闭不再导致
/// 任务中心永远显示"安装中"。
/// </summary>
public partial class LoaderPickerWindow : FluentWindow
{
    /// <summary>装载器选项行。</summary>
    public record LoaderRow(
        string Id, string Name, string Desc, ImageSource Icon,
        bool Recommended = false, bool Legacy = false)
    {
        public System.Windows.Visibility RecommendedVisibility
            => Recommended ? Visibility.Visible : Visibility.Collapsed;
        public System.Windows.Visibility LegacyVisibility
            => Legacy ? Visibility.Visible : Visibility.Collapsed;
    }

    private readonly string _mcVersion;
    private readonly List<LoaderRow> _rows;
    private readonly Dictionary<string, ImageSource> _icons;
    private bool _installing;
    private bool _backgrounded;   // 后台模式：窗口关闭，任务继续，UI 回调只更新任务中心
    private CancellationTokenSource? _cts;
    private DownloadTaskService.TaskItem? _centerTask;

    public LoaderPickerWindow(string mcVersion)
    {
        InitializeComponent();
        _mcVersion = mcVersion;
        // fix7：Esc 关闭窗口（PCL 同款）
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Title = (FindResource("S.Dl.LoaderTitleText") as string ?? "选择加载器") + " - " + mcVersion;
        TxtTitle.Text = string.Format(FindResource("S.Dl.LoaderTitle") as string ?? "选择加载器 - {0}", mcVersion);

        // pack URI 图标在弹窗中也走应用资源，无须重新加载
        _icons = new Dictionary<string, ImageSource>
        {
            ["vanilla"] = LoadIcon("/Assets/grass.png"),
            ["forge"] = LoadIcon("/Assets/Anvil.png"),
            ["neoforge"] = LoadIcon("/Assets/NeoForge.png"),
            ["fabric"] = LoadIcon("/Assets/Fabric.png"),
            ["quilt"] = LoadIcon("/Assets/Quilt.png"),
            ["legacyfabric"] = LoadIcon("/Assets/Fabric.png"),
            ["liteloader"] = LoadIcon("/Assets/Anvil.png"),
            ["forbric"] = LoadIcon("/Assets/Fabric.png"),
        };

        _rows = new List<LoaderRow>
        {
            new("vanilla", FindRes("S.Dl.LoaderVanilla"), FindRes("S.Dl.LoaderVanillaDesc"), _icons["vanilla"], Recommended: true),
            new("forge", "Forge", FindRes("S.Dl.LoaderForgeDesc"), _icons["forge"]),
            new("neoforge", "NeoForge", FindRes("S.Dl.LoaderNeoDesc"), _icons["neoforge"]),
            new("fabric", "Fabric", FindRes("S.Dl.LoaderFabricDesc"), _icons["fabric"]),
            new("quilt", "Quilt", FindRes("S.Dl.LoaderQuiltDesc"), _icons["quilt"]),
            new("legacyfabric", "Legacy Fabric", FindRes("S.Dl.LoaderLegacyDesc"), _icons["legacyfabric"], Legacy: true),
            new("liteloader", "LiteLoader", FindRes("S.Dl.LoaderLiteDesc"), _icons["liteloader"], Legacy: true),
            // fix5：Forbric（混合加载器：同一实例同时跑 Fabric / Forge / NeoForge 模组）
            new("forbric", "Forbric", FindRes("S.Dl.LoaderForbricDesc"), _icons["forbric"]),
        };
        LstLoaders.ItemsSource = _rows;
        LstLoaders.SelectedIndex = 0;

        // 标题栏 X 关闭：安装中不终止任务，转为后台安装（任务中心可见），
        // 避免"关窗后任务仍占着安装锁，再次启动报 Already installing"的老问题。
        Closing += (_, _) =>
        {
            if (_installing && !_backgrounded) _backgrounded = true;
        };
    }

    private static System.Windows.Media.Imaging.BitmapImage LoadIcon(string path)
        => new(new Uri("pack://application:,,," + path));

    private string FindRes(string key) => FindResource(key) as string ?? key;

    private LoaderRow? Selected => LstLoaders.SelectedItem as LoaderRow;

    // ---------- 加载器版本 ----------
    private async void LstLoaders_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_installing) return;
        var row = Selected;
        if (row == null) return;

        // 原版不需要加载器版本；Forbric 走独立安装器（无镜像版本列表）
        CmbLoaderVersion.ItemsSource = null;
        CmbLoaderVersion.IsEnabled = row.Id is not ("vanilla" or "forbric");
        if (row.Id == "vanilla")
        {
            CmbLoaderVersion.Items.Clear();
            return;
        }
        if (row.Id == "forbric")
        {
            CmbLoaderVersion.Items.Clear();
            // Forbric 0.3.1 目前仅支持 Minecraft 26.2
            var ok = _mcVersion == "26.2";
            BtnInstall.IsEnabled = ok;
            BtnInstall.Visibility = Visibility.Visible;
            BtnBackground.Visibility = Visibility.Collapsed;
            BtnCancel.Visibility = Visibility.Collapsed;
            PbInstall.Visibility = Visibility.Collapsed;
            TxtStatus.Text = ok
                ? FindRes("S.Dl.ForbricHint")
                : string.Format(FindRes("S.Dl.ForbricOnly26"), _mcVersion);
            return;
        }

        TxtStatus.Text = FindRes("S.Dl.LoaderFetching");
        BtnInstall.IsEnabled = false;
        try
        {
            var vers = row.Id switch
            {
                "forge" => await App.LauncherService.ForgeVersionsAsync(_mcVersion),
                "neoforge" => await App.LauncherService.NeoForgeVersionsAsync(_mcVersion),
                "fabric" => await App.LauncherService.FabricLoadersAsync(_mcVersion),
                "quilt" => await App.LauncherService.QuiltLoadersAsync(_mcVersion),
                "legacyfabric" => await App.LauncherService.LegacyFabricLoadersAsync(_mcVersion),
                "liteloader" => await App.LauncherService.LiteLoaderVersionsAsync(_mcVersion),
                "forbric" => Array.Empty<string>(),
                _ => Array.Empty<string>(),
            };
            if (vers.Length == 0)
            {
                TxtStatus.Text = string.Format(FindRes("S.Dl.LoaderNone"), row.Name, _mcVersion);
                return;
            }
            var items = new List<string> { FindRes("S.Dl.LoaderLatest") };
            items.AddRange(vers);
            CmbLoaderVersion.ItemsSource = items;
            CmbLoaderVersion.SelectedIndex = 0;
            TxtStatus.Text = string.Format(FindRes("S.Dl.LoaderFetched"), vers.Length);
            BtnInstall.IsEnabled = true;
        }
        catch (Exception ex)
        {
            // 版本列表拉取失败：仍允许尝试安装（默认最新），给出提示
            TxtStatus.Text = FindRes("S.Dl.LoaderFetchFail") + ex.Message;
            CmbLoaderVersion.ItemsSource = new[] { FindRes("S.Dl.LoaderLatest") };
            CmbLoaderVersion.SelectedIndex = 0;
            // Forge / NeoForge 必须指定版本，拉不到列表就不能盲装；其余可默认最新
            BtnInstall.IsEnabled = row.Id is "forge" or "neoforge" ? false : true;
        }
    }

    // ---------- 安装（fix3：详细进度 + 取消 + 后台） ----------
    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        var row = Selected;
        if (row == null || _installing) return;
        if (CmbLoaderVersion.IsEnabled && CmbLoaderVersion.SelectedItem is not string) return;

        _installing = true;
        _cts = new CancellationTokenSource();
        var versionsBefore = new HashSet<string>(App.LauncherService.GetInstalledVersions()); // fix5：自定义版本名用
        BtnInstall.IsEnabled = false;
        BtnInstall.Visibility = Visibility.Collapsed;
        BtnBackground.Visibility = Visibility.Visible;
        BtnCancel.Visibility = Visibility.Visible;
        PbInstall.Visibility = Visibility.Visible;
        PbInstall.IsIndeterminate = false;
        PbInstall.Value = 0;
        TxtStatus.Text = string.Format(FindRes("S.Dl.LoaderInstalling"), row.Name, _mcVersion);

        var loaderVersion = CmbLoaderVersion.SelectedItem as string;
        if (loaderVersion == FindRes("S.Dl.LoaderLatest")) loaderVersion = null;

        _centerTask = DownloadTaskService.Instance.StartCancellable(
            $"安装 {row.Name} {_mcVersion}" + (loaderVersion != null ? " (" + loaderVersion + ")" : ""),
            FindRes("S.Task.CheckingFiles"));
        // fix7：统一使用任务中心任务的取消源 —— 窗口取消按钮与任务中心「取消」等效
        _cts = null;
        var ct = DownloadTaskService.Instance.TokenFor(_centerTask);
        var meter = new SpeedMeter();

        // 详细进度（fix3）：文件数百分比 + 字节 / 速度；后台模式只更新任务中心
        var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(p =>
            Dispatcher.Invoke(() =>
            {
                var pct = p.TotalTasks > 0 ? p.ProgressedTasks * 100.0 / p.TotalTasks : -1;
                var text = $"[{p.ProgressedTasks}/{p.TotalTasks}] {p.Name}";
                if (pct >= 0)
                {
                    PbInstall.IsIndeterminate = false;
                    PbInstall.Value = pct;
                }
                if (!_backgrounded) TxtStatus.Text = text;
                DownloadTaskService.Instance.Update(_centerTask, Math.Max(pct, 0), text);
            }));
        var byteProgress = new Progress<CmlLib.Core.ByteProgress>(p =>
            Dispatcher.Invoke(() =>
            {
                if (p.TotalBytes <= 0) return;
                var pct = p.ProgressedBytes * 100.0 / p.TotalBytes;
                var text = $"{SpeedMeter.FormatBytes(p.ProgressedBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)}"
                           + $" · {SpeedMeter.FormatSpeed(meter.Observe(p.ProgressedBytes))}";
                if (pct > 0)
                {
                    PbInstall.IsIndeterminate = false;
                    PbInstall.Value = pct;
                }
                if (!_backgrounded) TxtStatus.Text = text;
                DownloadTaskService.Instance.Update(_centerTask, pct, text);
            }));

        try
        {
            switch (row.Id)
            {
                case "vanilla":
                    await App.LauncherService.InstallAsync(_mcVersion, fileProgress, byteProgress, ct);
                    break;
                case "forge":
                    await App.LauncherService.ForgeInstallAsync(_mcVersion, loaderVersion!, fileProgress, byteProgress, ct);
                    break;
                case "neoforge":
                    await App.LauncherService.NeoForgeInstallAsync(_mcVersion, loaderVersion!, ct);
                    break;
                case "fabric":
                    await App.LauncherService.FabricInstallAsync(_mcVersion, loaderVersion, ct);
                    break;
                case "quilt":
                    await App.LauncherService.QuiltInstallAsync(_mcVersion, loaderVersion, ct);
                    break;
                case "legacyfabric":
                    await App.LauncherService.LegacyFabricInstallAsync(_mcVersion, loaderVersion, ct);
                    break;
                case "liteloader":
                    await App.LauncherService.LiteLoaderInstallAsync(_mcVersion, loaderVersion, ct);
                    break;
                case "forbric":
                    await InstallForbricAsync(ct);
                    break;
            }

            // fix5：自定义版本名称（PCL 同款）——安装成功后重命名新增的版本目录
            var customName = TxtVersionName.Text?.Trim();
            if (row.Id != "forbric" && !string.IsNullOrEmpty(customName))
            {
                var newId = App.LauncherService.GetInstalledVersions()
                    .FirstOrDefault(v => !versionsBefore.Contains(v));
                if (newId != null && newId != customName)
                {
                    App.LauncherService.RenameVersion(newId, customName);
                    if (!_backgrounded)
                        TxtStatus.Text = string.Format(FindRes("S.Dl.VersionNameDone"), newId, customName);
                }
            }
            DownloadTaskService.Instance.Complete(_centerTask!, FindRes("S.Dl.LoaderDone"));
            _centerTask = null;
            if (_backgrounded) return; // 后台模式：任务中心已标记完成，窗口已关，无事可做
            TxtStatus.Text = string.Format(FindRes("S.Dl.LoaderDone2"), row.Name, _mcVersion);
            PbInstall.IsIndeterminate = false;
            PbInstall.Value = 100;
            await Task.Delay(700);
            DialogResult = true;
            Close();
        }
        catch (OperationCanceledException)
        {
            DownloadTaskService.Instance.Cancel(_centerTask!, FindRes("S.Dl.Canceled"));
            _centerTask = null;
            if (_backgrounded) return;
            PbInstall.Visibility = Visibility.Collapsed;
            TxtStatus.Text = FindRes("S.Dl.Canceled");
            RestoreIdleUi();
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error($"装载器安装失败 {row.Name} {_mcVersion}: " + Services.BootLogger.Format(ex));
            DownloadTaskService.Instance.Fail(_centerTask!, "安装失败: " + ex.Message);
            _centerTask = null;
            if (_backgrounded) return;
            PbInstall.Visibility = Visibility.Collapsed;
            TxtStatus.Text = FindRes("S.Dl.LoaderFail") + ex.Message;
            RestoreIdleUi();
        }
        finally
        {
            _installing = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ---------- Forbric（fix5）：下载官方安装器并拉起，装好后版本列表出现 26.2-forbric ----------
    private const string ForbricVersion = "0.3.1-beta2";

    private async Task InstallForbricAsync(CancellationToken ct)
    {
        TxtStatus.Text = FindRes("S.Dl.ForbricDownloading");
        PbInstall.IsIndeterminate = true;
        var dir = Path.Combine(AppSettings.Instance.GameRoot, "forbric");
        Directory.CreateDirectory(dir);
        var jarPath = Path.Combine(dir, $"forbric-kernel-installer-{ForbricVersion}.jar");
        var batPath = Path.Combine(dir, "Forbric-Installer.bat");
        var github = $"https://github.com/Ray-T-r/Minecraft-Forbric-mod-loader/releases/download/v{ForbricVersion}";
        // 直连 + 镜像前缀兑底（GitHub 直连在国内不稳定）
        string[] prefixes = { "", "https://gh-proxy.com/", "https://ghproxy.net/" };
        await DownloadForbricFileAsync(jarPath, $"{github}/forbric-kernel-installer-{ForbricVersion}.jar", prefixes, ct);
        await DownloadForbricFileAsync(batPath, $"{github}/Forbric-Installer.bat", prefixes, ct);
        PbInstall.IsIndeterminate = false;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(batPath)
        {
            UseShellExecute = true,
            WorkingDirectory = dir,
        });
    }

    private static async Task DownloadForbricFileAsync(string dest, string url, string[] prefixes, CancellationToken ct)
    {
        if (File.Exists(dest) && new FileInfo(dest).Length > 0) return;
        Exception? last = null;
        foreach (var prefix in prefixes)
        {
            try
            {
                using var resp = await App.LauncherService.Http.GetAsync(prefix + url,
                    HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await src.CopyToAsync(fs, ct);
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                try { File.Delete(dest); } catch { }
            }
        }
        throw new Exception(url + " → " + last?.Message);
    }

    /// <summary>安装结束（失败 / 取消）后恢复可再次安装的界面状态。</summary>
    private void RestoreIdleUi()
    {
        BtnInstall.Visibility = Visibility.Visible;
        BtnInstall.IsEnabled = true;
        BtnBackground.Visibility = Visibility.Collapsed;
        BtnCancel.Visibility = Visibility.Collapsed;
        PbInstall.Value = 0;
    }

    // ---------- 取消 ----------
    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_installing) return;
        _centerTask?.RequestCancel(); // fix7：统一走任务中心取消源（_cts 已并入任务）
        try { _cts?.Cancel(); } catch { }
        TxtStatus.Text = FindRes("S.Dl.Canceling");
        BtnCancel.IsEnabled = false;
    }

    // ---------- 后台安装（fix3）：关窗任务继续，任务中心看进度 ----------
    private void BtnBackground_Click(object sender, RoutedEventArgs e)
    {
        if (!_installing) return;
        _backgrounded = true;
        DialogResult = null;
        Close();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        if (_installing)
        {
            // 安装中点「关闭」= 转后台（任务继续，不弹确认）
            _backgrounded = true;
        }
        DialogResult = _installing ? null : false;
        Close();
    }
}
