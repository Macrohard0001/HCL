using System.IO;
using System.IO.Compression;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.5：实例备份 / 恢复（KAMUCL 实例管理中心同款实用功能）——
/// 备份：整个 versions/&lt;name&gt; 目录打成 zip（含隔离语义下的 saves/mods/config），
/// 存到下载目录 HmBackup-&lt;name&gt;-&lt;时间戳&gt;.zip。
/// 恢复：从 zip 还原为 versions/&lt;顶层目录名&gt;；重名自动加 -restored 后缀；
/// 解压带 zip-slip 防护（拒绝越出目标根的条目）。
/// </summary>
public static class InstanceBackupService
{
    public static string Backup(string version)
    {
        var src = Path.Combine(AppSettings.Instance.GameRoot, "versions", version);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"版本目录不存在：{src}");
        Directory.CreateDirectory(HorizonService.DownloadDir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(HorizonService.DownloadDir, $"HmBackup-{version}-{stamp}.zip");
        ZipFile.CreateFromDirectory(src, dest, CompressionLevel.Optimal, includeBaseDirectory: true);
        Services.BootLogger.Info($"备份：{version} → {dest}");
        return dest;
    }

    /// <summary>恢复 zip → versions/&lt;名&gt;。返回还原后的版本名。</summary>
    public static string Restore(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count == 0) throw new InvalidOperationException("压缩包里没有内容");
        // 版本名 = zip 内顶层目录名（备份产物必含 BaseDir；外来包取首个带 / 的顶层段）
        var top = zip.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => n.Contains('/'))
            .Select(n => n.Split('/')[0])
            .FirstOrDefault(n => n.Length > 0 && !n.EndsWith(":") && !n.StartsWith(".."));
        if (string.IsNullOrEmpty(top)) throw new InvalidOperationException("压缩包里没有顶层目录——不是本启动器的备份格式");

        var versionsDir = Path.Combine(AppSettings.Instance.GameRoot, "versions");
        Directory.CreateDirectory(versionsDir);
        var dest = Path.Combine(versionsDir, top);
        var name = top;
        for (var i = 1; Directory.Exists(dest); i++)
        {
            name = i == 1 ? top + "-restored" : $"{top}-restored-{i}";
            dest = Path.Combine(versionsDir, name);
        }
        var destFull = Path.GetFullPath(dest);
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue; // 目录条目
            var norm = e.FullName.Replace('\\', '/');
            if (norm.Contains("..")) continue; // zip-slip 防护
            var target = Path.GetFullPath(Path.Combine(destFull, norm[(top.Length + 1)..]));
            if (!target.StartsWith(destFull, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, overwrite: true);
        }
        Services.BootLogger.Info($"恢复：{zipPath} → {name}");
        return name;
    }
}
