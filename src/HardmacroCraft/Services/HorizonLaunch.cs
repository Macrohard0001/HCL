using System.Diagnostics;
using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.4：Horizon（Rust 版 Minecraft）启动 —— 在已检测到的安装/下载目录里找
/// Horizon 可执行文件并拉起（Rust 版自带启动器，Java 运行时由其自动安装）。
/// 候选名覆盖常见命名（Horizon.exe / horizon-launcher.exe / HorizonLauncher.exe），
/// 找不到精确名时取目录内唯一的顶层 .exe 兜底。
/// </summary>
public static class HorizonLaunch
{
    public static readonly string[] CandidateExeNames =
        { "Horizon.exe", "horizon.exe", "horizon-launcher.exe", "HorizonLauncher.exe", "horizon_rust.exe" };

    /// <summary>在目录中定位 Horizon 可执行文件（候选名优先，回退唯一顶层 exe）；null = 未找到。</summary>
    public static string? FindExe(string? dir)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;
        foreach (var name in CandidateExeNames)
        {
            var p = Path.Combine(dir, name);
            if (File.Exists(p)) return p;
        }
        var exes = Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly);
        return exes.Length == 1 ? exes[0] : null;
    }

    /// <summary>启动 Horizon：优先已检测安装，其次启动器下载目录中的版本包解压位置提示。
    /// 返回启动描述（供状态行显示）；抛 FileNotFoundException = 没有可启动的 Horizon。</summary>
    public static async Task<string> LaunchDetectedAsync()
    {
        var dir = HorizonService.DetectInstall();
        if (dir == null)
        {
            // 未检测到常规安装 → 兜底扫描下载目录（版本包 zip 用户可能已手动解压）
            var dl = HorizonService.DownloadDir;
            if (Directory.Exists(dl))
            {
                foreach (var sub in Directory.GetDirectories(dl))
                {
                    dir = FindExe(sub) != null ? sub : null;
                    if (dir != null) break;
                }
            }
        }
        var exe = FindExe(dir) ?? throw new FileNotFoundException(
            "未找到 Horizon 可执行文件——请先「下载 Rust 版 MC」并解压版本包。");
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
        Services.BootLogger.Info($"Horizon：已启动 {exe}");
        await Task.CompletedTask;
        return exe;
    }
}
