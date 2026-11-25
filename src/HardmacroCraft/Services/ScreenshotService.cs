using System.Diagnostics;
using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.6：截图管理（KAMUCL 同类实用功能）—— 扫描游戏根目录与隔离版本的
/// screenshots/ 目录，列出 PNG（按修改时间倒序）；打开 / 删除 / 定位。
/// 纯文件系统操作，不读世界数据。rootOverride 供测试注入临时目录。
/// </summary>
public static class ScreenshotService
{
    /// <summary>全部候选截图目录（游戏根 screenshots + 隔离 versions/*/screenshots）。</summary>
    public static List<string> FindScreenshotDirs(string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var dirs = new List<string> { Path.Combine(root, "screenshots") };
        var versionsDir = Path.Combine(root, "versions");
        try
        {
            if (Directory.Exists(versionsDir))
                dirs.AddRange(Directory.GetDirectories(versionsDir)
                    .Select(v => Path.Combine(v, "screenshots")));
        }
        catch { /* versions 目录不可读时忽略隔离版本截图 */ }
        return dirs.Where(Directory.Exists).Distinct().ToList();
    }

    public sealed record ShotInfo(string Path, DateTime Modified, long SizeBytes);

    /// <summary>收集全部截图（PNG，按修改时间倒序，新在前）。</summary>
    public static List<ShotInfo> ListAll(string? rootOverride = null)
    {
        var shots = new List<ShotInfo>();
        foreach (var dir in FindScreenshotDirs(rootOverride))
        {
            try
            {
                shots.AddRange(Directory.GetFiles(dir, "*.png")
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.Exists)
                    .Select(fi => new ShotInfo(fi.FullName, fi.LastWriteTime, fi.Length)));
            }
            catch { /* 权限问题跳过该目录 */ }
        }
        return shots.OrderByDescending(s => s.Modified).ToList();
    }

    /// <summary>用系统默认图片查看器打开截图。</summary>
    public static void Open(string file) =>
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });

    /// <summary>资源管理器定位截图所在目录（多截图目录时打开该截图自己的）。</summary>
    public static void RevealFolder(string file) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });

    public static void Delete(string file) => File.Delete(file);
}
