using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.10：回收站管理（TrashTool 窗口后端）—— 统 .hcl-trash 目录的查看 / 恢复 /
/// 永久删除 / 清空。写入方目前是 WorldsService.MoveToTrash（世界删除进回收站）；
/// 恢复规则：条目含 level.dat 判定为世界 → 移回共享 saves（重名 -restored-N 不覆盖），
/// 否则移回游戏根目录（来源未记录，回根目录人工处理）。rootOverride 供测试注入。
/// </summary>
public static class TrashService
{
    public sealed record TrashEntry(string Path, string Name, DateTime MovedAt, bool LooksLikeWorld)
    {
        public string Display => $"{Name}  （{MovedAt:yyyy-MM-dd HH:mm}，{(LooksLikeWorld ? "世界" : "未知类型")}）";
    }

    /// <summary>回收站目录 {root}/.hcl-trash（不存在时返回 null 而不是创建——只读列表不应有副作用）。</summary>
    public static string? TrashDir(string? rootOverride = null)
    {
        var dir = Path.Combine(rootOverride ?? AppSettings.Instance.GameRoot, ".hcl-trash");
        return Directory.Exists(dir) ? dir : null;
    }

    /// <summary>列出回收站条目（移入时间倒序）。</summary>
    public static List<TrashEntry> ListTrash(string? rootOverride = null)
    {
        var dir = TrashDir(rootOverride);
        var list = new List<TrashEntry>();
        if (dir == null) return list;
        foreach (var d in Directory.GetDirectories(dir))
        {
            try
            {
                list.Add(new TrashEntry(d, Path.GetFileName(d), Directory.GetLastWriteTime(d),
                    File.Exists(Path.Combine(d, "level.dat"))));
            }
            catch { /* 单个条目不可读时跳过 */ }
        }
        return list.OrderByDescending(e => e.MovedAt).ToList();
    }

    /// <summary>
    /// 恢复条目：世界 → 共享 saves/{清洗名}，重名自动 -restored-N；其它 → 游戏根/{清洗名}-restored-N。
    /// 返回恢复目标目录。
    /// </summary>
    public static string Restore(TrashEntry entry, string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var name = WorldsService.SanitizeFolder(entry.Name);
        var baseDir = entry.LooksLikeWorld ? Path.Combine(root, "saves") : root;
        Directory.CreateDirectory(baseDir);

        var dest = Path.Combine(baseDir, name);
        var n = 1;
        while (Directory.Exists(dest))
        {
            n++;
            dest = Path.Combine(baseDir, $"{name}-restored-{n}");
        }
        Directory.Move(entry.Path, dest);
        BootLogger.Info($"回收站恢复：{entry.Path} → {dest}");
        return dest;
    }

    /// <summary>永久删除单个条目（不可找回，UI 需二次确认）。</summary>
    public static void DeletePermanently(TrashEntry entry) =>
        Directory.Delete(entry.Path, recursive: true);

    /// <summary>清空回收站（永久删除全部条目），返回删除数。</summary>
    public static int EmptyTrash(string? rootOverride = null)
    {
        var dir = TrashDir(rootOverride);
        if (dir == null) return 0;
        var count = 0;
        foreach (var d in Directory.GetDirectories(dir))
        {
            try
            {
                Directory.Delete(d, recursive: true);
                count++;
            }
            catch { /* 单个条目删除失败继续 */ }
        }
        BootLogger.Info($"回收站清空：删除 {count} 条");
        return count;
    }
}
