using System.Diagnostics;
using System.IO;
using System.Text;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.4：一键诊断报告（doctor）—— 把排障需要的环境信息汇总成一份文本报告
/// （下载目录 HmDoctor-report.txt），游戏报障/求助发帖时直接附上：
/// 系统与启动器版本 / 游戏根目录与隔离模式 / 磁盘剩余空间 / 托管与专用内存 /
/// 托管 Java 清单 / 源与代理 / 已装版本 / Horizon 与基岩版检测结果。
/// 只收集环境信息，不读存档与账号数据。
/// </summary>
public static class DoctorService
{
    public static async Task<string> WriteReportAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"HardmacroCraft 诊断报告  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"启动器版本 : v{LauncherService.LauncherVersion}");
        sb.AppendLine($"系统       : {Environment.OSVersion.VersionString}（{Environment.Is64BitOperatingSystem} 位）");
        var root = AppSettings.Instance.GameRoot;
        sb.AppendLine($"游戏根目录 : {root}");
        sb.AppendLine($"版本隔离   : {(AppSettings.Instance.VersionIsolation ? "开" : "关")}");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root)) ?? "C:\\");
            sb.AppendLine($"磁盘剩余   : {drive.AvailableFreeSpace / (1024 * 1024)} MB / {drive.TotalSize / (1024 * 1024)} MB");
        }
        catch { /* 盘符不可用则跳过 */ }
        sb.AppendLine($"内存       : 专用工作集 {MemoryManager.PrivateWorkingSetMb} MB · 提交 {MemoryManager.PrivateMb} MB");
        sb.AppendLine($"定时清理   : {(AppSettings.Instance.AutoMemoryOptimize ? (AppSettings.Instance.AutoCleanIntervalMinutes <= 0 ? "开（未启用定时）" : $"开 · 每 {AppSettings.Instance.AutoCleanIntervalMinutes} 分钟") : "关")}");
        sb.AppendLine($"下载源     : {AppSettings.Instance.EffectiveDownloadSource}");
        sb.AppendLine($"代理       : {(AppSettings.Instance.ProxyEnabled ? AppSettings.Instance.ProxyType + "://" + AppSettings.Instance.ProxyHost + ":" + AppSettings.Instance.ProxyPort : "未启用")}");

        // 托管 Java
        sb.AppendLine();
        sb.AppendLine("== 托管 Java ==");
        try
        {
            var runtimeRoot = JavaManager.RuntimeRoot;
            if (Directory.Exists(runtimeRoot))
                foreach (var dir in Directory.GetDirectories(runtimeRoot))
                    sb.AppendLine($"· {Path.GetFileName(dir)} → {Path.Combine(dir, "bin")}");
            else sb.AppendLine("· （尚未安装托管 Java）");
        }
        catch { sb.AppendLine("· （扫描失败）"); }

        // 已装版本
        sb.AppendLine();
        sb.AppendLine("== 已装版本 ==");
        try
        {
            var versions = App.LauncherService.GetInstalledVersions();
            sb.AppendLine(string.Join(" · ", versions.Take(30)));
        }
        catch (Exception ex) { sb.AppendLine($"（读取失败：{ex.Message}）"); }

        // Horizon / 基岩版
        sb.AppendLine();
        sb.AppendLine("== Horizon / 基岩版 ==");
        sb.AppendLine($"Horizon 安装检测 : {HorizonService.DetectInstall() ?? "未检测到"}");
        try
        {
            var bedrock = await BedrockService.DetectStoreInstallAsync();
            sb.AppendLine($"基岩版（Store）  : {(bedrock is null ? "未检测到" : $"{bedrock.Version} · {bedrock.InstallLocation}")}");
        }
        catch { sb.AppendLine("基岩版（Store）  : （检测失败）"); }

        var path = Path.Combine(HorizonService.DownloadDir, "HmDoctor-report.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false));
        Services.BootLogger.Info($"诊断报告已生成：{path}");
        return path;
    }
}
