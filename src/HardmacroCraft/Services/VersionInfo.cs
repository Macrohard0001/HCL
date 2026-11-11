using System.IO;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>版本加载器类型（v0.1.6：版本列表 / 首页下拉框图标与副标题的数据来源）。</summary>
public enum McLoaderKind
{
    Vanilla,
    Forge,
    NeoForge,
    Fabric,
    LegacyFabric,
    Quilt,
    LiteLoader,
    OptiFine,
}

/// <summary>创建该版本的启动器来源（按版本目录内的标记文件识别，PCL 式「版本图标看得出处」）。</summary>
public enum LauncherSource
{
    /// <summary>无标记（官方启动器 / 手动放置 / 各类安装器直接产物）。</summary>
    None,
    /// <summary>本启动器安装的版本（版本目录内写入 hcl.json 标记）。</summary>
    Hcl,
    /// <summary>其他主流第三方启动器创建（pcl.json / 旧版 .id 标记）。</summary>
    Pcl,
    /// <summary>另一款常见开源启动器创建（hmclversion.cfg 标记）。</summary>
    Hmcl,
}

/// <summary>
/// v0.1.6 版本信息识别（纯逻辑，无 WPF 依赖，进测试）：
/// - 加载器类型：版本 json 的 libraries / id 特征 + 目录内 .forge/.fabric/.quilt 等标记；
/// - 启动器来源：版本目录内 pcl.json（新版）/ .id（旧版）/ hmclversion.cfg / hcl.json 标记；
/// - 显示版本号清洗：剥除构建期追加的 "+提交哈希"（关于页乱码根因）。
/// </summary>
public static class VersionInfo
{
    // ---------- 显示版本号清洗 ----------

    /// <summary>
    /// 剥除 InformationalVersion 里构建期自动追加的源码修订号部分（"&lt;版本&gt;+&lt;sha&gt;"）。
    /// 预发布后缀（-beta / -rc.1）保留；无 '+' 时原样返回；null / 空白返回 null。
    /// </summary>
    public static string? CleanDisplayVersion(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return null;
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }

    // ---------- 加载器检测 ----------

    /// <summary>
    /// 从版本 json 文本推断加载器类型。按特征串匹配（避开完整 JSON 解析 ——
    /// 部分版本 json 超过 1MB，且结构随安装器版本变化）。顺序敏感：
    /// NeoForge 的特征串也含 "forge" 字样，必须先于 Forge 判断。
    /// </summary>
    public static McLoaderKind DetectLoaderFromJson(string? jsonText, string? versionId = null)
    {
        var t = jsonText ?? "";
        var id = versionId ?? "";

        // NeoForge：maven 包名 net/neoforged（1.20.1 特例 artifact 名也含 neoforged）
        if (t.Contains("neoforged", StringComparison.OrdinalIgnoreCase)
            || id.Contains("neoforge", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.NeoForge;

        // Forge：maven 包名 minecraftforge / libraries 内 forge 特征 / id 后缀
        if (t.Contains("minecraftforge", StringComparison.OrdinalIgnoreCase)
            || t.Contains("\"net.minecraftforge:forge\"", StringComparison.OrdinalIgnoreCase)
            || id.Contains("-forge", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.Forge;

        // OptiFine：optifine:OptiFine 依赖（可与 Forge 共存 —— 此处按「谁在 json 里先出现」
        // 无法判断，OptiFine 单独安装时才有独立版本目录，因此放在 Forge 之后兜底）
        if (t.Contains("optifine", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.OptiFine;

        // Fabric / LegacyFabric：fabricloader 依赖；LegacyFabric 的 maven 组为 net.legacyfabric
        if (t.Contains("legacyfabric", StringComparison.OrdinalIgnoreCase)
            || id.Contains("legacy-fabric", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.LegacyFabric;
        if (t.Contains("fabricloader", StringComparison.OrdinalIgnoreCase)
            || id.Contains("-fabric", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.Fabric;

        // Quilt：org.quiltmc / quilt-loader
        if (t.Contains("quilt-loader", StringComparison.OrdinalIgnoreCase)
            || t.Contains("org.quiltmc", StringComparison.OrdinalIgnoreCase)
            || t.Contains("org/quiltmc", StringComparison.OrdinalIgnoreCase)
            || id.Contains("-quilt", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.Quilt;

        // LiteLoader
        if (t.Contains("liteloader", StringComparison.OrdinalIgnoreCase))
            return McLoaderKind.LiteLoader;

        return McLoaderKind.Vanilla;
    }

    /// <summary>
    /// 检测版本目录的加载器类型：优先读目录内标记文件（.forge / .fabric / .quilt /
    /// .optifine 等，各类安装器 / 其他启动器会留下），否则解析 &lt;版本&gt;.json（找不到同名
    /// json 时回退目录内任意一个 .json）。
    /// </summary>
    public static McLoaderKind DetectLoader(string? versionDir, string? versionId = null)
    {
        if (versionDir == null || !Directory.Exists(versionDir)) return McLoaderKind.Vanilla;

        // 目录级标记（Fabric/Quilt 安装器会写 .fabric/.quilt；部分工具写 .forge）
        foreach (var (file, kind) in new[]
                 {
                     (".neoforge", McLoaderKind.NeoForge),
                     (".forge", McLoaderKind.Forge),
                     (".optifine", McLoaderKind.OptiFine),
                     (".legacyfabric", McLoaderKind.LegacyFabric),
                     (".fabric", McLoaderKind.Fabric),
                     (".quilt", McLoaderKind.Quilt),
                     (".liteloader", McLoaderKind.LiteLoader),
                 })
            if (File.Exists(Path.Combine(versionDir, file)))
                return kind;

        // 版本 json：优先 <目录名>.json（个别安装器目录名与 json 名不一致，回退任意 json）
        var name = versionId ?? Path.GetFileName(versionDir);
        var jsonFile = Path.Combine(versionDir, name + ".json");
        if (!File.Exists(jsonFile))
        {
            var any = SafeFirstJson(versionDir);
            if (any == null) return McLoaderKind.Vanilla;
            jsonFile = any;
        }
        string text;
        try { text = File.ReadAllText(jsonFile); }
        catch { return McLoaderKind.Vanilla; }
        var mainKind = DetectLoaderFromJson(text, name);
        if (mainKind != McLoaderKind.Vanilla) return mainKind;

        // v0.1.8 紧急修复：自定义版本（目录名 / 主 json 不含加载器特征串，但目录里另有
        // 加载器安装器写入的 json，如 inheritsFrom 合并结构）—— 扫描目录内其余 json，
        // 任一带加载器特征即按该加载器展示（纯净版 json 不会误判）。
        try
        {
            foreach (var j in Directory.GetFiles(versionDir, "*.json"))
            {
                if (string.Equals(Path.GetFullPath(j), Path.GetFullPath(jsonFile), StringComparison.OrdinalIgnoreCase))
                    continue;
                string t2;
                try { t2 = File.ReadAllText(j); } catch { continue; }
                var k2 = DetectLoaderFromJson(t2, Path.GetFileNameWithoutExtension(j));
                if (k2 != McLoaderKind.Vanilla) return k2;
            }
        }
        catch { /* 目录读取失败按已得结果返回 */ }
        return mainKind;
    }

    private static string? SafeFirstJson(string dir)
        => Directory.GetFiles(dir, "*.json").FirstOrDefault();

    // ---------- 启动器来源检测 ----------

    /// <summary>本启动器安装版本时写入的标记文件名（小写，不含任何其他启动器字样）。</summary>
    public const string OwnedMarkerFile = "hcl.json";

    /// <summary>按版本目录内的标记文件识别创建来源（无标记 → None，即官方启动器 / 安装器直接产物）。</summary>
    public static LauncherSource DetectSource(string? versionDir)
    {
        if (versionDir == null || !Directory.Exists(versionDir)) return LauncherSource.None;
        if (File.Exists(Path.Combine(versionDir, OwnedMarkerFile))) return LauncherSource.Hcl;
        if (File.Exists(Path.Combine(versionDir, "pcl.json"))) return LauncherSource.Pcl;
        // 旧版标记：.id 文件内容以 ';' 开头的是另一款启动器的私有格式
        var idFile = Path.Combine(versionDir, ".id");
        if (File.Exists(idFile))
        {
            try
            {
                if (File.ReadAllText(idFile).TrimStart().StartsWith(';')) return LauncherSource.Pcl;
            }
            catch { /* 读取失败按无标记 */ }
        }
        if (File.Exists(Path.Combine(versionDir, "hmclversion.cfg"))) return LauncherSource.Hmcl;
        return LauncherSource.None;
    }

    /// <summary>写入（或刷新）本启动器的所有权标记。失败静默（标记只用于图标展示，不影响功能）。</summary>
    public static void WriteOwnedMarker(string? versionDir)
    {
        if (versionDir == null || !Directory.Exists(versionDir)) return;
        try
        {
            File.WriteAllText(Path.Combine(versionDir, OwnedMarkerFile),
                $$"""{"launcher":"hcl","version":"{{LauncherService.LauncherVersion}}"}""");
        }
        catch { /* 只读目录等情况忽略 */ }
    }

    // ---------- 版本条目描述（供首页下拉框 / 版本管理列表共用） ----------

    /// <summary>版本列表条目：加载器 + 来源一次性识别结果（副标题 / 图标共用）。</summary>
    public sealed record VersionDescriptor(McLoaderKind Loader, LauncherSource Source);

    /// <summary>检测版本目录，返回加载器与来源描述（目录不存在 → 原版 / 无来源）。</summary>
    public static VersionDescriptor Describe(string gameRoot, string versionName)
    {
        var dir = Path.Combine(gameRoot, "versions", versionName);
        return new VersionDescriptor(DetectLoader(dir, versionName), DetectSource(dir));
    }

    /// <summary>加载器显示名键（S.Ver.Loader.*），UI 层用 FindResource 取文案。</summary>
    public static string LoaderKey(McLoaderKind k) => k switch
    {
        McLoaderKind.Forge => "S.Ver.Loader.Forge",
        McLoaderKind.NeoForge => "S.Ver.Loader.NeoForge",
        McLoaderKind.Fabric => "S.Ver.Loader.Fabric",
        McLoaderKind.LegacyFabric => "S.Ver.Loader.LegacyFabric",
        McLoaderKind.Quilt => "S.Ver.Loader.Quilt",
        McLoaderKind.LiteLoader => "S.Ver.Loader.LiteLoader",
        McLoaderKind.OptiFine => "S.Ver.Loader.OptiFine",
        _ => "S.Ver.Loader.Vanilla",
    };
}
