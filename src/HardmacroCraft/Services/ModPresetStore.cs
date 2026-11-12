using System.IO;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>模组预设条目：记录平台与项目标识，安装时按当前游戏版本 / 加载器自动匹配文件。</summary>
public sealed class ModPresetItem
{
    /// <summary>modrinth / curseforge</summary>
    public string Platform { get; set; } = "modrinth";
    /// <summary>Modrinth 项目 id（slug 或 uuid） / CurseForge 数字 modId。</summary>
    public string ProjectId { get; set; } = "";
    /// <summary>Modrinth slug（CF 为 slug 同样存一份便于展示）。</summary>
    public string Slug { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>保存时的资源类型（mod / resourcepack / shader / datapack）。</summary>
    public string Type { get; set; } = "mod";
    /// <summary>v0.1.12：本条目已成功安装到各版本的文件名（主文件，不含依赖）。
    /// 「一键配置 → 开始移除」据此删除对应文件；空列表时回退按最新兼容文件名匹配。</summary>
    public List<string> InstalledFiles { get; set; } = new();
}

/// <summary>模组预设（PCL 式一键装机单）：一组 mod 的清单 + 名称，安装时自动匹配版本。</summary>
public sealed class ModPreset
{
    public string Name { get; set; } = "";
    public List<ModPresetItem> Items { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>与当前筛选一致的条目才参与一键安装（类型不同无法确定目标目录）。</summary>
    public IEnumerable<ModPresetItem> OfType(string type) => Items.Where(i => i.Type == type);
}

/// <summary>
/// 模组预设存储：config/mod-presets.json，便携随启动器目录携带。
/// 读写均为静态方法（传入路径），无 UI 依赖，可被无头测试直接调用。
/// </summary>
public static class ModPresetStore
{
    public static string PresetsPath => Path.Combine(AppSettings.ConfigDir, "mod-presets.json");

    public static List<ModPreset> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<ModPreset>();
            var json = File.ReadAllText(path);
            var presets = JsonSerializer.Deserialize<List<ModPreset>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return presets ?? new List<ModPreset>();
        }
        catch
        {
            // 文件损坏不阻塞启动：备份后返回空列表（下次保存时覆盖）
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            return new List<ModPreset>();
        }
    }

    public static void Save(string path, List<ModPreset> presets)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(presets,
            new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>新建 / 追加保存：同名覆盖（PCL 式"保存预设"语义），返回保存后的列表。</summary>
    public static List<ModPreset> Upsert(List<ModPreset> presets, string name, List<ModPresetItem> items)
    {
        presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        presets.Insert(0, new ModPreset { Name = name, Items = items, CreatedAt = DateTimeOffset.Now });
        return presets;
    }

    /// <summary>为条目追加一个已安装文件名（去重；v0.1.12 开始移除的依据）。</summary>
    public static void RecordInstalled(ModPresetItem item, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return;
        if (!item.InstalledFiles.Contains(fileName)) item.InstalledFiles.Add(fileName);
    }
}

/// <summary>
/// v0.1.12 一键配置：从已装版本导入预设的纯逻辑（无 UI / 无网络，可无头测试）。
/// 调用方逐个文件提供 SHA1 → 匹配回调（网络反查），本类只负责目录扫描、哈希计算、
/// 条目生成与去重；未收录文件（CurseForge 专属 / 本地 jar）不进入预设并单独汇报。
/// </summary>
public static class ModPresetImport
{
    /// <summary>扫描 mods 目录里可反查的 jar 文件（跳过禁用 .disabled / 旧版 / 来源标记）。</summary>
    public static List<string> ListModJars(string dir)
    {
        var result = new List<string>();
        if (!Directory.Exists(dir)) return result;
        foreach (var f in Directory.GetFiles(dir, "*.jar"))
        {
            var name = Path.GetFileName(f);
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) continue;
            if (name.EndsWith(".old", StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(f);
        }
        result.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    /// <summary>计算文件 SHA1（小写十六进制）。</summary>
    public static async Task<string> Sha1OfFileAsync(string file, CancellationToken ct = default)
        => Convert.ToHexString(await System.Security.Cryptography.SHA1.HashDataAsync(
            File.OpenRead(file), ct)).ToLowerInvariant();

    /// <summary>
    /// 单个 jar → 预设条目（match 由调用方注入：SHA1 反查 Modrinth）。未命中返回 null。
    /// </summary>
    public static async Task<ModPresetItem?> ImportOneAsync(string jarFile,
        Func<string, CancellationToken, Task<(string ProjectId, string Slug, string Title, string Type)?>> match,
        CancellationToken ct = default)
    {
        var sha1 = await Sha1OfFileAsync(jarFile, ct);
        var hit = await match(sha1, ct);
        if (hit == null) return null;
        var (projectId, slug, title, type) = hit.Value;
        if (string.IsNullOrWhiteSpace(projectId)) return null;
        return new ModPresetItem
        {
            Platform = "modrinth",
            ProjectId = projectId,
            Slug = slug,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(jarFile) : title,
            Type = string.IsNullOrWhiteSpace(type) ? "mod" : type,
        };
    }
}
