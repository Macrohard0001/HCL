using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HardmacroCraft.Services;

/// <summary>
/// 版本独立设置（v0.1.6，对齐主流启动器的「版本设置」）：
/// 每个版本可单独覆盖 Java 路径 / 内存 / JVM 参数 / 进服地址 / 版本隔离，
/// 保存于 config/versions/&lt;版本名&gt;.json（便携化 config/ 体系，随设置走）。
/// 字段留空 / 为 null 表示「跟随全局设置」。
/// </summary>
public sealed class VersionSettings
{
    /// <summary>版本隔离覆盖：null=跟随全局；true=该版本独立存档/mods；false=该版本共用根目录。</summary>
    public bool? IsolationOverride { get; set; }

    /// <summary>该版本专用 Java（javaw.exe 或所在文件夹），空 = 跟随全局设置。</summary>
    public string JavaPath { get; set; } = "";

    /// <summary>内存模式覆盖：null=跟随全局；true=自动检测；false=使用 MemoryMb。</summary>
    public bool? MemoryAuto { get; set; }

    /// <summary>该版本内存上限（MemoryAuto == false 时生效）。</summary>
    public int MemoryMb { get; set; }

    /// <summary>该版本自定义 JVM 参数（覆盖全局自定义参数；优化参数仍按全局开关注入），空 = 跟随全局。</summary>
    public string CustomJvmArgs { get; set; } = "";

    /// <summary>该版本固定进服地址（一键进服默认填充），空 = 不预填。</summary>
    public string QuickServer { get; set; } = "";

    /// <summary>是否全部为默认值（用于判断是否还需要落盘）。</summary>
    [JsonIgnore]
    public bool IsDefault =>
        IsolationOverride == null && string.IsNullOrWhiteSpace(JavaPath)
        && MemoryAuto == null && string.IsNullOrWhiteSpace(CustomJvmArgs)
        && string.IsNullOrWhiteSpace(QuickServer);
}

/// <summary>版本独立设置的读写与生效值计算（纯逻辑，无 WPF 依赖，进测试）。</summary>
public static class VersionSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string StoreDir => Path.Combine(AppSettings.ConfigDir, "versions");

    private static string FilePath(string version) => Path.Combine(StoreDir, version + ".json");

    /// <summary>读取版本设置（不存在 / 损坏返回 null）。</summary>
    public static VersionSettings? Get(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        try
        {
            var file = FilePath(version);
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<VersionSettings>(File.ReadAllText(file));
        }
        catch { return null; }
    }

    /// <summary>保存版本设置；全默认时删除文件（保持 config/ 干净）。</summary>
    public static void Save(string version, VersionSettings settings)
    {
        if (settings == null || settings.IsDefault) { Delete(version); return; }
        Directory.CreateDirectory(StoreDir);
        File.WriteAllText(FilePath(version), JsonSerializer.Serialize(settings, JsonOpts));
    }

    /// <summary>删除版本设置文件（版本删除 / 恢复默认时调用；不存在时静默）。</summary>
    public static void Delete(string version)
    {
        try
        {
            var file = FilePath(version);
            if (File.Exists(file)) File.Delete(file);
        }
        catch { /* 非致命 */ }
    }

    /// <summary>版本隔离的最终生效值：版本覆盖 &gt; 全局设置。</summary>
    public static bool EffectiveIsolation(string? version, bool globalDefault) =>
        Get(version)?.IsolationOverride ?? globalDefault;

    /// <summary>内存的最终生效值（自动 / 手动），供启动流程与日志复用。</summary>
    public static (int Mb, bool Auto) EffectiveMemory(string? version, AppSettings global)
    {
        var vs = Get(version);
        var auto = vs?.MemoryAuto ?? global.MemoryAuto;
        var mb = auto ? (int)SystemInfo.RecommendedMemoryMb() : (vs?.MemoryMb ?? global.MemoryMb);
        return (mb, auto);
    }
}
