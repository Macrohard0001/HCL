using System.IO;
using System.IO.Compression;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.8：世界存档管理（PCL / Prism / Modrinth App 标配工具）—— 扫描共享 saves 与
/// 隔离 versions/*/saves 的世界，解析 level.dat（gzip 压缩 NBT）取出世界名 / 种子 /
/// 模式 / 难度 / 最后游玩时间；备份为 zip、从 zip 恢复（zip-slip 防护沿用
/// InstanceBackupService 规则）、删除进回收站目录 .hcl-trash（不直接物理删除）。
/// level.dat 解析复用 ServerList.Nbt（零依赖，headless 可测）；rootOverride 供测试注入。
/// </summary>
public static class WorldsService
{
    public sealed record WorldInfo(
        string Dir, string Folder, string SavesRoot,
        string LevelName, string VersionName, long Seed,
        string GameType, string Difficulty, bool Hardcore, DateTime? LastPlayed)
    {
        /// <summary>下拉框显示文本：世界名（文件夹名）。</summary>
        public string Display => LevelName == Folder ? Folder : $"{LevelName}（{Folder}）";
    }

    /// <summary>全部候选存档目录（共享 saves + 隔离 versions/*/saves，同 WorldMapRenderer.FindWorlds 口径）。</summary>
    public static List<string> FindSavesDirs(string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var dirs = new List<string> { Path.Combine(root, "saves") };
        var versionsDir = Path.Combine(root, "versions");
        try
        {
            if (Directory.Exists(versionsDir))
                dirs.AddRange(Directory.GetDirectories(versionsDir).Select(v => Path.Combine(v, "saves")));
        }
        catch { /* versions 目录不可读时忽略隔离版本 */ }
        return dirs.Where(Directory.Exists).Distinct().ToList();
    }

    /// <summary>收集全部世界（文件夹名排序；无 level.dat 的目录不算世界）。</summary>
    public static List<WorldInfo> ListWorlds(string? rootOverride = null)
    {
        var worlds = new List<WorldInfo>();
        foreach (var saves in FindSavesDirs(rootOverride))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(saves))
                {
                    var dat = Path.Combine(dir, "level.dat");
                    if (!File.Exists(dat)) continue;
                    var info = ParseLevelDat(File.ReadAllBytes(dat), dir, saves);
                    if (info != null) worlds.Add(info);
                }
            }
            catch { /* 该 saves 目录不可读时跳过 */ }
        }
        return worlds.OrderBy(w => w.Folder, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 解析 level.dat：gzip 解压（个别老存档/工具会存未压缩 NBT，解压失败按原样解析）→
    /// 根 Compound 的 Data 子节点。种子优先取 1.16+ 的 WorldGenSettings.seed，回退老字段
    /// RandomSeed；解析失败返回 null（调用方跳过该目录）。
    /// </summary>
    public static WorldInfo? ParseLevelDat(byte[] raw, string dir, string savesRoot)
    {
        byte[] nbt;
        try
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
            {
                gz.CopyTo(ms);
            }
            nbt = ms.ToArray();
        }
        catch { nbt = raw; }

        var root = Nbt.Parse(nbt);
        var data = root?.Child("Data");
        if (data == null) return null;

        var folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var levelName = data.Child("LevelName")?.Str ?? "";

        // 种子：1.16+ WorldGenSettings.seed → 老 RandomSeed
        long seed = data.Child("WorldGenSettings")?.Child("seed")?.L ?? 0;
        if (seed == 0) seed = data.Child("RandomSeed")?.L ?? 0;

        var gameType = data.Child("GameType")?.I ?? 0;
        var gt = gameType switch
        {
            1 => "creative",
            2 => "adventure",
            3 => "spectator",
            _ => "survival",
        };
        var diff = (data.Child("Difficulty")?.B ?? 1) switch
        {
            0 => "peaceful",
            1 => "easy",
            3 => "hard",
            _ => "normal",
        };
        DateTime? last = null;
        var lp = data.Child("LastPlayed")?.L ?? 0;
        if (lp > 0) last = DateTimeOffset.FromUnixTimeMilliseconds(lp).ToLocalTime().DateTime;

        return new WorldInfo(dir, folder, savesRoot,
            levelName.Length > 0 ? levelName : folder,
            data.Child("version")?.Child("Name")?.Str ?? "",
            seed, gt, diff,
            (data.Child("hardcore")?.B ?? 0) == 1,
            last);
    }

    /// <summary>备份单个世界为 zip（含世界文件夹名作为顶层目录），存到下载目录。</summary>
    public static string BackupWorld(string dir)
    {
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException($"世界目录不存在：{dir}");
        Directory.CreateDirectory(HorizonService.DownloadDir);
        var folder = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var dest = Path.Combine(HorizonService.DownloadDir, $"HmWorld-{folder}-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        ZipFile.CreateFromDirectory(dir, dest, CompressionLevel.Optimal, includeBaseDirectory: true);
        BootLogger.Info($"世界备份：{folder} → {dest}");
        return dest;
    }

    /// <summary>
    /// 从 zip 恢复世界到共享 saves：顶层目录名 = 世界文件夹名（扁平包用 zip 文件名）；
    /// 重名自动加 -restored-N 后缀不覆盖；zip-slip 防护（拒绝越出目标根的条目）。
    /// 返回恢复出的世界目录。
    /// </summary>
    public static string RestoreWorld(string zipPath, string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var savesRoot = Path.Combine(root, "saves");
        Directory.CreateDirectory(savesRoot);

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.Entries.Count == 0) throw new InvalidOperationException("压缩包为空");
        var first = zip.Entries.First(e => e.FullName.Length > 0 && !e.FullName.EndsWith("/"));
        var parts = first.FullName.Replace('\\', '/').Split('/');
        var worldName = SanitizeFolder(parts.Length > 1 ? parts[0] : Path.GetFileNameWithoutExtension(zipPath));

        var dest = Path.Combine(savesRoot, worldName);
        var n = 1;
        while (Directory.Exists(dest))
        {
            n++;
            dest = Path.Combine(savesRoot, $"{worldName}-restored-{n}");
        }
        Directory.CreateDirectory(dest);
        var destFull = Path.GetFullPath(dest);
        var savesFull = Path.GetFullPath(savesRoot);
        if (!destFull.StartsWith(savesFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("恢复目标越出 saves 根（zip 顶层目录名非法）");

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Length == 0) continue;
            var norm = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (norm.Length == 0 || norm.EndsWith("/")) continue;
            if (norm.Contains("..")) continue; // zip-slip 防护
            var rel = parts.Length > 1
                ? string.Join('/', norm.Split('/').Skip(1))
                : norm;
            if (rel.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(destFull, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var es = entry.Open();
            using var fs = File.Create(target);
            es.CopyTo(fs);
        }
        BootLogger.Info($"世界恢复：{zipPath} → {dest}");
        return dest;
    }

    /// <summary>删除 = 移入回收站目录 {root}/.hcl-trash/{文件夹名}-{时间戳}（可手工找回，不直接物理删除）。返回回收站中的新路径。</summary>
    public static string MoveToTrash(string dir, string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var trash = Path.Combine(root, ".hcl-trash");
        Directory.CreateDirectory(trash);
        var name = SanitizeFolder(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        var dest = Path.Combine(trash, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var n = 1;
        while (Directory.Exists(dest))
        {
            n++;
            dest = Path.Combine(trash, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}-{n}");
        }
        Directory.Move(dir, dest);
        BootLogger.Info($"世界移入回收站：{dir} → {dest}");
        return dest;
    }

    /// <summary>文件夹名净化（跨平台非法字符 / 空白收边）；空名或纯点名（"." / ".."）回退 world。</summary>
    public static string SanitizeFolder(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.Trim())
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        var s = sb.ToString().Trim();
        return s.Length > 0 && s.Trim('.') != "" ? s : "world";
    }
}
