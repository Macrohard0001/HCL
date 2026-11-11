using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// 本机 Mod 目录决策（v0.1.9 纯逻辑，进测试）：
/// 与 PCL 一致——开启版本隔离时 mod / 资源包等放在 versions/&lt;id&gt;/ 下，
/// 否则放在游戏根目录；由调用方传入最终生效的隔离开关
/// （VersionSettingsStore.EffectiveIsolation：版本独立覆盖 &gt; 全局设置）。
/// </summary>
public static class ModTargetDir
{
    public static string Resolve(string gameRoot, string? version, string projectType, bool isolation)
    {
        if (isolation && !string.IsNullOrWhiteSpace(version))
        {
            var iso = Path.Combine(gameRoot, "versions", version);
            return projectType switch
            {
                "mod" => Path.Combine(iso, "mods"),
                "resourcepack" => Path.Combine(iso, "resourcepacks"),
                "shader" => Path.Combine(iso, "shaderpacks"),
                "datapack" => Path.Combine(iso, "datapacks"),
                _ => iso,
            };
        }
        return projectType switch
        {
            "mod" => Path.Combine(gameRoot, "mods"),
            "resourcepack" => Path.Combine(gameRoot, "resourcepacks"),
            "shader" => Path.Combine(gameRoot, "shaderpacks"),
            "datapack" => Path.Combine(gameRoot, "datapacks"),
            _ => gameRoot,
        };
    }
}

/// <summary>本机 Mod 条目元数据（v0.1.9，对齐 PCL 的本机 Mod 列表信息）。</summary>
public sealed class LocalModMeta
{
    /// <summary>文件名（含 .disabled 后缀时表示已禁用）。</summary>
    public string FileName { get; set; } = "";
    /// <summary>Mod 显示名（jar 内元数据解析；失败回退文件名去后缀）。</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Mod 版本号（解析失败为空）。</summary>
    public string ModVersion { get; set; } = "";
    /// <summary>Mod 描述（解析失败为空）。</summary>
    public string Description { get; set; } = "";
    /// <summary>加载器类型（fabric / quilt / forge / neoforge / unknown）。</summary>
    public string Loader { get; set; } = "unknown";
    /// <summary>是否启用（.disabled 后缀 = 禁用，与 PCL 一致）。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>文件大小（字节）。</summary>
    public long SizeBytes { get; set; }
}

/// <summary>
/// 本机 Mod 元数据扫描（v0.1.9，纯逻辑可测试；PCL 式「本机 Mod 列表显示名字 / 版本 / 加载器」）：
/// 直接读 jar（zip）内的：
/// - fabric.mod.json（Fabric / Forge Fabric）→ JSON 的 name / version / description；
/// - quilt.mod.json（Quilt）→ quilt_loader.id / version 及 metadata.name；
/// - META-INF/mods.toml（Forge 1.13+）、META-INF/neoforge.mods.toml（NeoForge）→ TOML 行级解析；
/// - mcmod.info（Forge 1.12-）→ JSON 数组首项的 name / version / description。
/// 解析失败一律回退文件名，绝不抛异常（目录扫描不能被单个坏文件打断）。
/// </summary>
public static class LocalModScanner
{
    public static LocalModMeta ScanFile(string filePath)
    {
        var meta = new LocalModMeta
        {
            FileName = Path.GetFileName(filePath),
            SizeBytes = TrySize(filePath),
        };
        var name = Path.GetFileName(filePath);
        if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            meta.Enabled = false;
            name = name[..^".disabled".Length];
        }
        var fallback = Path.GetFileNameWithoutExtension(name);
        meta.DisplayName = fallback;
        try
        {
            using var zip = ZipFile.OpenRead(filePath);
            // Fabric
            var fabric = zip.GetEntry("fabric.mod.json");
            if (fabric != null)
            {
                meta.Loader = "fabric";
                ParseFabricJson(ReadEntry(fabric), meta);
                return FillFallback(meta, fallback);
            }
            // Quilt
            var quilt = zip.GetEntry("quilt.mod.json");
            if (quilt != null)
            {
                meta.Loader = "quilt";
                ParseQuiltJson(ReadEntry(quilt), meta);
                return FillFallback(meta, fallback);
            }
            // NeoForge（先于 Forge：neoforge.mods.toml 也含 forge 字样）
            var neo = zip.GetEntry("META-INF/neoforge.mods.toml");
            if (neo != null)
            {
                meta.Loader = "neoforge";
                ParseModsToml(ReadEntry(neo), meta);
                return FillFallback(meta, fallback);
            }
            // Forge（1.13+ mods.toml）
            var toml = zip.GetEntry("META-INF/mods.toml");
            if (toml != null)
            {
                meta.Loader = "forge";
                ParseModsToml(ReadEntry(toml), meta);
                return FillFallback(meta, fallback);
            }
            // Forge（1.12- mcmod.info）
            var oldInfo = zip.GetEntry("mcmod.info");
            if (oldInfo != null)
            {
                meta.Loader = "forge";
                ParseMcmodInfo(ReadEntry(oldInfo), meta);
                return FillFallback(meta, fallback);
            }
        }
        catch { /* 坏 zip / 加密文件：保留文件名回退 */ }
        return FillFallback(meta, fallback);
    }

    private static LocalModMeta FillFallback(LocalModMeta meta, string fallback)
    {
        if (string.IsNullOrWhiteSpace(meta.DisplayName)) meta.DisplayName = fallback;
        return meta;
    }

    private static long TrySize(string path)
    {
        try { return new FileInfo(path).Length; } catch { return 0; }
    }

    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        using var sr = new StreamReader(s);
        return sr.ReadToEnd();
    }

    /// <summary>扫描目录内全部 jar / zip / disabled 文件（目录不存在返回空表）。</summary>
    public static List<LocalModMeta> ScanDir(string dir)
    {
        var list = new List<LocalModMeta>();
        try
        {
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir)
                         .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
            {
                list.Add(ScanFile(f));
            }
        }
        catch { /* 目录不可读返回已扫到的部分 */ }
        return list;
    }

    private static void ParseFabricJson(string json, LocalModMeta meta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            meta.ModVersion = GetStr(root, "version");
            // Fabric 约定 name &gt; displayName &gt; id
            meta.DisplayName = GetStr(root, "name") is { Length: > 0 } n ? n : GetStr(root, "displayName");
            if (meta.DisplayName.Length == 0) meta.DisplayName = GetStr(root, "id");
            meta.Description = GetStr(root, "description");
        }
        catch { /* json 损坏回退文件名 */ }
    }

    private static void ParseQuiltJson(string json, LocalModMeta meta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("quilt_loader", out var loader))
            {
                meta.ModVersion = GetStr(loader, "version");
                var id = GetStr(loader, "id");
                if (loader.TryGetProperty("metadata", out var md))
                {
                    meta.DisplayName = GetStr(md, "name");
                    meta.Description = GetStr(md, "description");
                }
                if (meta.DisplayName.Length == 0) meta.DisplayName = id;
            }
        }
        catch { /* json 损坏回退文件名 */ }
    }

    /// <summary>mods.toml 行级解析（不引 TOML 依赖）：取首个 [[mods]] 段内的键。</summary>
    private static void ParseModsToml(string text, LocalModMeta meta)
    {
        try
        {
            var inMods = false;
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("[[") && line.Contains("mods]]")) { inMods = true; continue; }
                if (line.StartsWith("[[")) { if (inMods) break; continue; } // 进入下一段（dependencies 等）
                if (!inMods) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim().Trim('"', '\'');
                switch (key)
                {
                    case "displayName" when val.Length > 0: meta.DisplayName = val; break;
                    case "modId": meta.DisplayName = meta.DisplayName == Path.GetFileNameWithoutExtension(meta.FileName) || meta.DisplayName.Length == 0 ? val : meta.DisplayName; break;
                    case "version" when val.Length > 0 && meta.ModVersion.Length == 0: meta.ModVersion = val; break;
                    case "description" when val.Length > 0 && meta.Description.Length == 0: meta.Description = val; break;
                }
            }
        }
        catch { /* 解析失败回退文件名 */ }
    }

    /// <summary>mcmod.info（Forge 1.12-）：JSON 数组首项。</summary>
    private static void ParseMcmodInfo(string json, LocalModMeta meta)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement first = default;
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                first = doc.RootElement[0];
            else if (doc.RootElement.TryGetProperty("modList", out var ml) && ml.GetArrayLength() > 0)
                first = ml[0];
            if (first.ValueKind == JsonValueKind.Object)
            {
                meta.DisplayName = GetStr(first, "name");
                meta.ModVersion = GetStr(first, "version");
                meta.Description = GetStr(first, "description");
            }
        }
        catch { /* json 损坏回退文件名 */ }
    }

    private static string GetStr(JsonElement e, string key)
    {
        if (e.ValueKind != JsonValueKind.Object) return "";
        return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
    }
}
