using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.4：一键更新版本（一键配置页）—— 带着 mods / 存档 / 配置 / 资源包飞到新 MC 版本：
/// 1) 按源版本加载器（fabric / vanilla；forge 自动更新暂不支持 → 仅迁移数据并提示）
///    安装目标版本（LauncherService.InstallAsync / FabricInstallAsync，默认最新 loader）；
/// 2) 隔离语义下迁移实例数据（saves / config / 资源包 / 光影 / options.txt 等）；
/// 3) mods 迁移：SHA1 反查 Modrinth 拿最新兼容文件自动更新；未收录或无兼容文件时
///    先用 jar 内 fabric.mod.json 的 minecraft 依赖范围评估能否直接沿用 —— 都不行才
///    弹窗询问（保留旧版本 / 不安装此 mod，可勾选“对余下未匹配项全部执行此操作”）。
/// </summary>
public static partial class VersionUpdateService
{
    [GeneratedRegex(@"^[0-9]+\.[0-9]+(\.[0-9]+)?$")]
    public static partial Regex McVersionRegex();

    public static readonly string[] DataDirNames =
        { "saves", "config", "resourcepacks", "shaderpacks", "servers.dat", "servers.dat_old", "usercache.json", "options.txt" };

    public enum ModChoice { KeepOld, Skip }

    public sealed record ModUpdateOutcome(string File, string Action, string Detail);

    // ---------- 纯逻辑（进测试） ----------

    /// <summary>MC 版本比较：逐段数值（1.21.4 &gt; 1.21）；非数字段按字符串。null/空 = 视为最旧。</summary>
    public static int CompareMc(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a)) return string.IsNullOrEmpty(b) ? 0 : -1;
        if (string.IsNullOrEmpty(b)) return 1;
        var pa = a.Split('.'); var pb = b.Split('.');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var sa = i < pa.Length ? pa[i] : "0";
            var sb = i < pb.Length ? pb[i] : "0";
            if (int.TryParse(sa, out var na) && int.TryParse(sb, out var nb))
            {
                if (na != nb) return na.CompareTo(nb);
            }
            else if (sa != sb) return string.CompareOrdinal(sa, sb);
        }
        return 0;
    }

    /// <summary>
    /// 评估 fabric.mod.json 的 minecraft 依赖范围是否满足目标版本。
    /// 支持：* / 精确 / = / &gt;= / &gt; / &lt;= / &lt; / 空格分隔 AND / “1.21.x” 前缀通配。
    /// </summary>
    public static bool IsMcDepSatisfied(string? dep, string targetMc)
    {
        if (string.IsNullOrWhiteSpace(dep) || dep.Trim() == "*") return true;
        foreach (var cond in dep.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var v = cond;
            // 统一“目标优先”方向：>=1.22 对 1.21.4 = CompareMc("1.21.4","1.22") < 0 → 不满足。
            // v0.16.4 实跑断言抓过一版比较参数写反的 bug（“<1.22”误判不满足），此实现以后为准。
            string bare; // 先剥运算符，再剥 .x —— 顺序写反会把 ">=" 带进版本比较（实跑断言抓过）
            if (v.StartsWith(">=") || v.StartsWith("<=")) bare = v[2..];
            else if (v.StartsWith('>') || v.StartsWith('<') || v.StartsWith('=')) bare = v[1..];
            else bare = v;
            if (bare.EndsWith(".x")) bare = bare[..^2];
            if (v.StartsWith(">=")) { if (CompareMc(targetMc, bare) < 0) return false; }
            else if (v.StartsWith("<=")) { if (CompareMc(targetMc, bare) > 0) return false; }
            else if (v.StartsWith('>')) { if (CompareMc(targetMc, bare) <= 0) return false; }
            else if (v.StartsWith('<')) { if (CompareMc(targetMc, bare) >= 0) return false; }
            else if (v.StartsWith('=')) { if (!ExactOrPrefixMatch(v[1..], targetMc)) return false; }
            else if (!ExactOrPrefixMatch(v, targetMc)) return false;
        }
        return true;
    }

    private static bool ExactOrPrefixMatch(string v, string targetMc)
    {
        if (v.EndsWith(".x")) return targetMc.StartsWith(v[..^2] + ".");
        return CompareMc(v, targetMc) == 0;
    }

    /// <summary>读 jar 内 fabric.mod.json 的 depends.minecraft（无该条目/非 fabric jar 返回 null）。</summary>
    public static string? ReadFabricMcDep(string jarPath)
    {
        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            var entry = zip.GetEntry("fabric.mod.json") ?? zip.GetEntry("quilt.mod.json");
            if (entry == null) return null;
            using var r = new StreamReader(entry.Open());
            using var doc = System.Text.Json.JsonDocument.Parse(r.ReadToEnd());
            var root = doc.RootElement;
            if (!root.TryGetProperty("depends", out var deps)) return null;
            if (deps.ValueKind != System.Text.Json.JsonValueKind.Object) return null;
            if (!deps.TryGetProperty("minecraft", out var mc)) return null;
            return mc.ValueKind == System.Text.Json.JsonValueKind.String
                ? mc.GetString()
                : string.Join(" ", mc.EnumerateArray().Select(x => x.GetString() ?? ""));
        }
        catch { return null; }
    }

    /// <summary>加载器启发式：版本 json 文本含 fabric-loader → fabric；forge → forge；否则 vanilla。</summary>
    public static string DetectLoader(string versionDir)
    {
        var name = Path.GetFileName(versionDir.TrimEnd(Path.DirectorySeparatorChar));
        var json = Path.Combine(versionDir, name + ".json");
        if (!File.Exists(json)) return "vanilla";
        var text = File.ReadAllText(json);
        if (text.Contains("fabric-loader", StringComparison.OrdinalIgnoreCase)) return "fabric";
        if (text.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase)) return "quilt";
        if (text.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "forge";
        return "vanilla";
    }

    public static string ComputeSha1(string file)
    {
        using var fs = File.OpenRead(file);
        return Convert.ToHexString(SHA1.HashData(fs)).ToLowerInvariant();
    }

    public static List<string> ListMods(string versionDir)
    {
        var mods = Path.Combine(versionDir, "mods");
        return Directory.Exists(mods)
            ? Directory.GetFiles(mods, "*.jar").OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList()
            : [];
    }

    /// <summary>隔离语义下的实例数据迁移（逐项复制，options.txt 不覆盖目标已有）。</summary>
    public static (int Copied, List<string> Items) MigrateData(string srcDir, string dstDir)
    {
        Directory.CreateDirectory(dstDir);
        var copied = 0;
        var items = new List<string>();
        foreach (var n in DataDirNames)
        {
            var src = Path.Combine(srcDir, n);
            if (!File.Exists(src) && !Directory.Exists(src)) continue;
            var dst = Path.Combine(dstDir, n);
            LauncherMigrationService.CopyDir(src, dst, overwrite: n != "options.txt");
            copied++;
            items.Add(n);
        }
        return (copied, items);
    }

    /// <summary>定位 CmlLib 装出的目标版本目录（vanilla=目标号；fabric/quilt=loader 前缀 + 目标号结尾）。</summary>
    public static string? FindInstalledTargetDir(string targetMc, string loader)
    {
        var versions = Path.Combine(AppSettings.Instance.GameRoot, "versions");
        if (!Directory.Exists(versions)) return null;
        if (loader == "vanilla")
        {
            var d = Path.Combine(versions, targetMc);
            return File.Exists(Path.Combine(d, targetMc + ".json")) ? d : null;
        }
        var hit = Directory.GetDirectories(versions)
            .Where(p => Path.GetFileName(p).EndsWith(targetMc, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(p, Path.GetFileName(p) + ".json")))
            .OrderByDescending(p => Directory.GetLastWriteTimeUtc(p))
            .FirstOrDefault();
        return hit;
    }
}
