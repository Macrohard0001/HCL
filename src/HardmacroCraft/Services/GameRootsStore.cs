using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.9：多游戏目录管理（HMCL 标配）—— 保存多个 .minecraft 根目录一键切换。
/// 列表存 AppSettings.GameRoots（当前生效目录 = GameRoot，不必在列表里也能跑，
/// 「保存当前」后入列）；纯逻辑（归一化 / 去重 / 上限）headless 可测。
/// </summary>
public static class GameRootsStore
{
    /// <summary>列表上限（HMCL 同款思路：防列表无限膨胀；8 个足够日常多开场景）。</summary>
    public const int MaxRoots = 8;

    /// <summary>
    /// 目录归一化：相对路径按 AppDir 补全 → 全路径；收尾分隔符；非法路径原样返回
    /// （保守策略，让既有保存校验去报错而不是这里丢数据）。
    /// </summary>
    public static string NormalizeRoot(string path)
    {
        var p = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (p.Length == 0) return p;
        try
        {
            return Path.IsPathRooted(p)
                ? Path.GetFullPath(p)
                : Path.GetFullPath(Path.Combine(AppSettings.AppDir, p));
        }
        catch { return p; }
    }

    /// <summary>归一化列表：非空 + 去重（大小写不敏感，Windows 惯例）+ 截断到上限。返回新列表。</summary>
    public static List<string> NormalizeList(IEnumerable<string> roots)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var r in roots)
        {
            var n = NormalizeRoot(r);
            if (n.Length == 0 || !seen.Add(n)) continue;
            list.Add(n);
            if (list.Count >= MaxRoots) break;
        }
        return list;
    }

    /// <summary>添加目录（归一化 + 去重 + 上限保护）。返回是否真的新入列。</summary>
    public static bool Add(List<string> roots, string root)
    {
        var n = NormalizeRoot(root);
        if (n.Length == 0) return false;
        if (roots.Contains(n, StringComparer.OrdinalIgnoreCase)) return false;
        roots.Add(n);
        var clean = NormalizeList(roots);
        roots.Clear();
        roots.AddRange(clean);
        return true;
    }

    /// <summary>移除目录（归一化后匹配，大小写不敏感）。返回是否移除了。</summary>
    public static bool Remove(List<string> roots, string root)
    {
        var n = NormalizeRoot(root);
        var idx = roots.FindIndex(r => string.Equals(r, n, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        roots.RemoveAt(idx);
        return true;
    }
}
