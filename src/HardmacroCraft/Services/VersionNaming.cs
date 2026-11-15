namespace HardmacroCraft.Services;

/// <summary>
/// v0.15.1 版本默认命名（纯逻辑，进测试）—— 重新对齐 PCL 真实命名规则。
/// v0.1.14 误改为「无游戏版本前缀」；经 PCL 崩溃日志实证（Launched Version:
/// 1.20.1-Forge_47.4.0 / 1.20.1-Forge_47.2.20 等），PCL 的装载器版本默认名是
/// 「&lt;MC 版本&gt;-&lt;加载器名&gt;_&lt;加载器版本&gt;」（游戏版本前缀 + 下划线分隔）：
///   原版：&lt;MC 版本&gt;                如 1.20.1
///   Forge：&lt;MC&gt;-Forge_&lt;v&gt;           如 1.20.1-Forge_47.2.0
///   NeoForge：&lt;MC&gt;-NeoForge_&lt;v&gt;     如 1.20.1-NeoForge_20.1.80
///   Fabric：&lt;MC&gt;-Fabric_&lt;v&gt;         如 1.19.2-Fabric_0.16.9（去掉 +build）
///   LegacyFabric：&lt;MC&gt;-LegacyFabric_&lt;v&gt;
///   Quilt：&lt;MC&gt;-Quilt_&lt;v&gt;           如 1.20.1-Quilt_0.26.0
///   LiteLoader：&lt;MC&gt;-LiteLoader     （无版本号后缀）
///   OptiFine：&lt;MC&gt;-OptiFine_&lt;显示名&gt; 如 1.12.2-OptiFine_HD_U_I7
/// MC 版本缺失时回落「&lt;加载器名&gt;_&lt;版本&gt;」（不产生开头的连字符）。
/// 用户在安装弹窗里改名后以用户输入为准（前缀相同则视为未改，安装后不再重命名）。
/// </summary>
public static class VersionNaming
{
    /// <summary>按 MC 版本 + 加载器种类（VersionNaming.LoaderKind 字符串）+ 加载器版本生成默认版本名。</summary>
    public static string DefaultName(string mcVersion, string loaderKind, string? loaderVersion)
    {
        var kind = (loaderKind ?? "").Trim().ToLowerInvariant();
        var mc = (mcVersion ?? "").Trim();
        if (string.IsNullOrEmpty(kind) || kind == "vanilla") return mc;
        var lv = TrimLoader(loaderVersion);
        var name = kind switch
        {
            "forge" => lv.Length > 0 ? $"Forge_{lv}" : "Forge",
            "neoforge" => lv.Length > 0 ? $"NeoForge_{lv}" : "NeoForge",
            "fabric" => lv.Length > 0 ? $"Fabric_{lv}" : "Fabric",
            "legacyfabric" => lv.Length > 0 ? $"LegacyFabric_{lv}" : "LegacyFabric",
            "quilt" => lv.Length > 0 ? $"Quilt_{lv}" : "Quilt",
            "liteloader" => "LiteLoader",
            "optifine" => lv.Length > 0 ? "OptiFine_" + OptiFineDisplay(loaderVersion ?? "") : "OptiFine",
            _ => lv.Length > 0 ? $"{loaderKind}_{lv}" : loaderKind ?? "",
        };
        return mc.Length > 0 ? $"{mc}-{name}" : name;
    }

    /// <summary>去掉 Fabric 系版本号里的 +build 后缀（PCL 同款 GetSelectName：
    /// "0.16.9+build.1" → "0.16.9.1"）。</summary>
    public static string TrimLoader(string v) => (v ?? "").Trim().Replace("+build", "");

    /// <summary>OptiFine 显示名（PCL GetSelectName 同款）：剥掉 "1.20.1 " 版本号前缀再压缩空格，
    /// 如 "1.20.1 HD U I7" → "HD_U_I7"。</summary>
    public static string OptiFineDisplay(string display)
    {
        var s = (display ?? "").Trim();
        var idx = s.IndexOf("HD", StringComparison.OrdinalIgnoreCase);
        if (idx > 0) s = s[idx..];
        return s.Replace(' ', '_');
    }

    /// <summary>判断用户输入是否仍是默认名（或为空）—— 安装流程据此决定是否需要重命名。</summary>
    public static bool IsDefaultOrEmpty(string? userInput, string mcVersion, string loaderKind, string? loaderVersion)
        => string.IsNullOrWhiteSpace(userInput)
           || string.Equals(userInput.Trim(), DefaultName(mcVersion, loaderKind, loaderVersion), StringComparison.Ordinal);
}
