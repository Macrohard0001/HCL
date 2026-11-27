using System.IO;
using System.IO.Compression;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.10：启动前自动备份存档（同类启动器标配的可选安全网）—— 每次启动前把
/// 共享 saves 与隔离 versions/*/saves 各打一份 zip 到 {root}/.hcl-backups/saves，
/// 滚动保留最近 N 份（超出按修改时间删最旧）。线程池执行 + 全程可失败（失败只记
/// 日志不阻断启动，安全网不该挡住游戏）。纯逻辑（命名 / 滚动清理）headless 可测。
/// </summary>
public static class SaveBackupService
{
    /// <summary>备份目录 {root}/.hcl-backups/saves。</summary>
    public static string BackupDir(string? rootOverride = null) =>
        Path.Combine(rootOverride ?? AppSettings.Instance.GameRoot, ".hcl-backups", "saves");

    /// <summary>备份 zip 命名：saves-{标签}-{yyyyMMdd-HHmmss}.zip（纯函数）。</summary>
    public static string BuildBackupName(string label) =>
        $"saves-{label}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    /// <summary>列出全部备份 zip（修改时间倒序）。</summary>
    public static List<FileInfo> ListBackups(string? rootOverride = null)
    {
        var dir = BackupDir(rootOverride);
        if (!Directory.Exists(dir)) return new List<FileInfo>();
        return Directory.GetFiles(dir, "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTime)
            .ToList();
    }

    /// <summary>
    /// 备份一个存档目录为 zip（目录不存在 / 无世界子目录时返回 null，不产空 zip）。
    /// label 进文件名（共享 saves 用 "shared"，隔离用版本名）。
    /// </summary>
    public static string? BackupOne(string savesDir, string label, string? rootOverride = null)
    {
        if (!Directory.Exists(savesDir)) return null;
        if (Directory.GetDirectories(savesDir).Length == 0) return null; // 空存档不备份
        Directory.CreateDirectory(BackupDir(rootOverride));
        var dest = Path.Combine(BackupDir(rootOverride), BuildBackupName(label));
        ZipFile.CreateFromDirectory(savesDir, dest, CompressionLevel.Optimal, includeBaseDirectory: false);
        BootLogger.Info($"存档备份：{savesDir} → {dest}");
        return dest;
    }

    /// <summary>
    /// 备份全部存档目录（共享 saves + 各隔离 versions/*/saves），返回生成的 zip 路径列表。
    /// </summary>
    public static List<string> BackupAll(string? rootOverride = null)
    {
        var made = new List<string>();
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var shared = Path.Combine(root, "saves");
        var z = BackupOne(shared, "shared", rootOverride);
        if (z != null) made.Add(z);
        var versionsDir = Path.Combine(root, "versions");
        try
        {
            if (Directory.Exists(versionsDir))
                foreach (var v in Directory.GetDirectories(versionsDir))
                {
                    var iso = Path.Combine(v, "saves");
                    if (!Directory.Exists(iso)) continue;
                    z = BackupOne(iso, Path.GetFileName(v), rootOverride);
                    if (z != null) made.Add(z);
                }
        }
        catch { /* versions 不可读时只备份共享 */ }
        return made;
    }

    /// <summary>滚动清理：按修改时间保留最近 keepN 份，其余删除（keepN &lt; 1 视为 1，纯逻辑进测试）。</summary>
    public static int PruneKeepN(string backupDir, int keepN)
    {
        if (keepN < 1) keepN = 1;
        if (!Directory.Exists(backupDir)) return 0;
        var files = Directory.GetFiles(backupDir, "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTime)
            .ToList();
        var removed = 0;
        foreach (var fi in files.Skip(keepN))
        {
            try { fi.Delete(); removed++; }
            catch { /* 单个删除失败继续 */ }
        }
        return removed;
    }
}
