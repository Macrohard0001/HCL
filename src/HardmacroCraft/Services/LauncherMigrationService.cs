using System.IO;

namespace HardmacroCraft.Services;

/// <summary>可导入的外部启动器版本（候选）。</summary>
public class MigrationCandidate
{
    /// <summary>来源标注（官方 / PCL / HMCL / Prism 系 / 自定义目录）。</summary>
    public string LauncherKind { get; set; } = "";
    public string VersionName { get; set; } = "";
    /// <summary>含 &lt;name&gt;.json 的版本目录（json + jar，导入时整体复制）。</summary>
    public string VersionDir { get; set; } = "";
    /// <summary>可随版本迁移的数据根（saves / mods / config 等），来自实例目录；标准 .minecraft 时为其根。</summary>
    public string? DataDir { get; set; }
    public override string ToString() => $"{VersionName}（{LauncherKind}）";
}

/// <summary>
/// v0.15.9：从其他启动器导入版本（实用功能，同类启动器通用做法）。
/// 扫描常见启动器目录（官方 / 其他启动器的 .minecraft，Prism / PolyMC /
/// MultiMC 的 instances），把版本目录（json + jar）复制进本启动器 GameRoot，
/// 隔离开时把实例数据（saves / mods / config / 资源包 / 光影 / options.txt）
/// 一并并入 versions/&lt;name&gt; 的 per-version 根（与 BuildGamePath 隔离语义一致）。
/// </summary>
public static class LauncherMigrationService
{
    /// <summary>内置扫描位置（存在才参与扫描）。</summary>
    public static string[] BuiltinRoots()
    {
        var appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return new[]
        {
            Path.Combine(appdata, ".minecraft"),
            Path.Combine(appdata, "PrismLauncher", "instances"),
            Path.Combine(appdata, "PolyMC", "instances"),
            Path.Combine(appdata, "MultiMC", "instances"),
            Path.Combine(appdata, "prismlauncher", "instances"),
        };
    }

    /// <summary>扫描一个目录（根目录 / instances 容器 / 实例目录均可）。</summary>
    public static List<MigrationCandidate> ScanFolder(string folder)
    {
        var result = new List<MigrationCandidate>();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return result;

        // instances 容器（Prism 系）：逐个实例子目录扫
        foreach (var sub in Directory.GetDirectories(folder))
        {
            if (File.Exists(Path.Combine(sub, "mmc-pack.json")))
                result.AddRange(ScanInstance(sub, "Prism / PolyMC / MultiMC"));
        }
        // 标准布局：folder/versions/*/*.json（官方 / PCL / HMCL 的 .minecraft）
        result.AddRange(ScanStandardLayout(folder, "标准 .minecraft（官方 / 其他启动器）"));
        return result;
    }

    /// <summary>Prism 系实例目录：mmc-pack.json + minecraft/.minecraft 内的 versions。</summary>
    private static IEnumerable<MigrationCandidate> ScanInstance(string instanceDir, string kind)
    {
        foreach (var inner in new[] { "minecraft", ".minecraft" })
        {
            var dir = Path.Combine(instanceDir, inner);
            if (!Directory.Exists(dir)) continue;
            foreach (var c in ScanStandardLayout(dir, kind))
                yield return c;
        }
    }

    /// <summary>标准布局：dir/versions/&lt;name&gt;/&lt;name&gt;.json（含 PCL 第二层嵌套）。</summary>
    private static IEnumerable<MigrationCandidate> ScanStandardLayout(string dir, string kind)
    {
        var versionsDir = Path.Combine(dir, "versions");
        if (!Directory.Exists(versionsDir)) yield break;
        foreach (var sub in Directory.GetDirectories(versionsDir))
        {
            // 第一层：versions/<name>/<name>.json
            var name = Path.GetFileName(sub);
            if (File.Exists(Path.Combine(sub, name + ".json")))
            {
                yield return new MigrationCandidate
                {
                    LauncherKind = kind, VersionName = name, VersionDir = sub, DataDir = dir,
                };
                continue;
            }
            // 第二层（PCL 分目录管理）：versions/<目录>/<id>/<id>.json
            foreach (var deep in Directory.GetDirectories(sub))
            {
                var deepName = Path.GetFileName(deep);
                if (File.Exists(Path.Combine(deep, deepName + ".json")))
                    yield return new MigrationCandidate
                    {
                        LauncherKind = kind, VersionName = deepName, VersionDir = deep, DataDir = dir,
                    };
            }
        }
    }

    /// <summary>
    /// 导入候选：复制版本目录进 GameRoot/versions（重名自动加 -imported 后缀）；
    /// 隔离开且候选带数据根时，并入 saves/mods/config 等实例数据。
    /// 返回导入后的版本名（供刷新与提示）。
    /// </summary>
    public static string Import(MigrationCandidate candidate, bool isolation)
    {
        var root = AppSettings.Instance.GameRoot;
        var versionsDir = Path.Combine(root, "versions");
        Directory.CreateDirectory(versionsDir);

        var name = candidate.VersionName;
        var dest = Path.Combine(versionsDir, name);
        if (Directory.Exists(dest))
        {
            name = name + "-imported";
            dest = Path.Combine(versionsDir, name);
        }
        CopyDirectory(candidate.VersionDir, dest);

        // 隔离语义（BuildGamePath）：per-version 根 = versions/<name>，
        // 实例数据并入该目录；非隔离时不并数据（避免污染共享根，提示用户）
        if (isolation && candidate.DataDir != null)
        {
            foreach (var dataName in new[] { "saves", "mods", "config", "resourcepacks", "shaderpacks" })
            {
                var src = Path.Combine(candidate.DataDir, dataName);
                if (Directory.Exists(src))
                    CopyDirectory(src, Path.Combine(dest, dataName));
            }
            var options = Path.Combine(candidate.DataDir, "options.txt");
            if (File.Exists(options))
                File.Copy(options, Path.Combine(dest, "options.txt"), overwrite: false);
        }
        Services.BootLogger.Info($"迁移：已导入版本 {candidate.VersionName} → {name}（来源 {candidate.LauncherKind}）");
        return name;
    }

    private static void CopyDirectory(string src, string dst) => CopyDir(src, dst, overwrite: false);

    /// <summary>v0.16.4：公开目录复制（一键更新版本的数据迁移复用；merge 语义 = 同名文件按 overwrite 决定）。</summary>
    public static void CopyDir(string src, string dst, bool overwrite)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.GetFiles(src))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite);
        foreach (var dir in Directory.GetDirectories(src))
            CopyDir(dir, Path.Combine(dst, Path.GetFileName(dir)), overwrite);
    }
}
