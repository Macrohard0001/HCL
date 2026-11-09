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
}
