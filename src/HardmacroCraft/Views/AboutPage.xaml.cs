using System.IO;
using System.Reflection;
using HardmacroCraft.Services;
using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        // 版本号运行时从程序集读取，避免资源串硬编码过期（如 v1.3.1 忘改的情况）。
        // 优先读 InformationalVersion（含预发布后缀，如 0.1.0-beta），回退数值版本。
        // v0.1.6 修复：.NET SDK 在 git 仓库构建时会把源码修订号（提交哈希）追加为
        // "<版本>+<40位sha>"（InformationalVersion），此前直接展示就出现「版本号后面一串乱码」。
        // 只取 '+' 之前的部分 —— 预发布后缀（-beta）保留，提交哈希舍弃。
        var informational = typeof(AboutPage).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var v = typeof(AboutPage).Assembly.GetName().Version;
        var raw = informational ?? (v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0");
        var shown = VersionInfo.CleanDisplayVersion(raw);
        var template = TryFindResource("S.About.Version") as string;
        if (string.IsNullOrEmpty(template))
            template = "v{0} · MIT License";
        TxtVersion.Text = string.Format(template, shown);
    }

    // ===== v0.16.9：启动器自更新 =====
    private SelfUpdateService.LatestInfo? _pendingUpdate;

    private async void BtnUpdateCheck_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnUpdateCheck.IsEnabled = false;
            BtnUpdateApply.IsEnabled = false;
            TxtUpdateStatus.Text = "正在检查更新（GitHub Releases latest）…";
            var latest = await SelfUpdateService.CheckAsync(App.LauncherService.Http, LauncherService.LauncherVersion);
            if (latest == null)
            {
                TxtUpdateStatus.Text = $"✓ 已是最新版本（v{LauncherService.LauncherVersion}）。";
            }
            else
            {
                _pendingUpdate = latest;
                BtnUpdateApply.IsEnabled = true;
                TxtUpdateStatus.Text = $"发现新版本 v{latest.Version}（{latest.AssetName}，{latest.SizeBytes / 1024.0 / 1024.0:0.#} MB）——点「下载并更新」开始，更新完成后启动器会自动重启。";
            }
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "检查更新失败：" + ex.Message;
        }
        finally { BtnUpdateCheck.IsEnabled = true; }
    }

    private async void BtnUpdateApply_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is not { } info) return;
        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"下载 v{info.Version} 并更新？\n\n更新完成后启动器会自动重启（当前窗口将关闭）。",
            "Hardmacro Craft Launcher", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            BtnUpdateApply.IsEnabled = false;
            var zipPath = Path.Combine(Path.GetTempPath(), info.AssetName);
            var staging = Path.Combine(Path.GetTempPath(), "HCL-update-staging");
            var progress = new Progress<long>(p =>
                TxtUpdateStatus.Text = $"下载中… {p / 1024.0 / 1024.0:0.#} / {info.SizeBytes / 1024.0 / 1024.0:0.#} MB");
            await SelfUpdateService.DownloadAsync(App.LauncherService.Http, info, zipPath, progress);
            TxtUpdateStatus.Text = "解压更新包并准备升级脚本…";
            var appDir = AppContext.BaseDirectory;
            SelfUpdateService.ApplyUpdate(zipPath, staging, appDir);
            TxtUpdateStatus.Text = "✓ 更新就绪——启动器即将关闭并自动重启。";
            await Task.Delay(800);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            TxtUpdateStatus.Text = "更新失败：" + ex.Message;
            BtnUpdateApply.IsEnabled = true;
        }
    }
}
