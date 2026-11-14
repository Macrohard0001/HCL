namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.14 版本默认命名（纯逻辑，进测试）—— 彻底对齐 PCL：默认版本名不再包含游戏版本
/// 前缀（v0.1.13 及之前是 "1.20.1-Forge_47.2.0" 风格，用户指出 PCL 的默认名只有装载器名 +
/// 装载器版本；游戏版本在版本列表副标题 / 版本 json 中另有记录，不该塞进版本名）：
///   原版：&lt;MC 版本&gt;              如 1.20.1
///   Forge：Forge &lt;loader 版本&gt;     如 Forge 47.2.0
///   NeoForge：NeoForge &lt;loader 版本&gt;   如 NeoForge 21.0.67
///   Fabric：Fabric &lt;loader 版本&gt;   如 Fabric 0.16.9（去掉 +build）
///   LegacyFabric：Legacy Fabric &lt;v&gt;
///   Quilt：Quilt &lt;loader 版本&gt;
///   LiteLoader：LiteLoader（无版本号后缀）
///   OptiFine：OptiFine &lt;显示名&gt;    如 OptiFine HD_U_I7
/// 用户在安装弹窗里改名后以用户输入为准（前缀相同则视为未改，安装后不再重命名）。
/// </summary>
public static class VersionNaming
{
    /// <summary>按 MC 版本 + 加载器种类（VersionNaming.LoaderKind 字符串）+ 加载器版本生成默认版本名。</summary>
    public static string DefaultName(string mcVersion, string loaderKind, string? loaderVersion)
    {
        var kind = (loaderKind ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(kind) || kind == "vanilla") return (mcVersion ?? "").Trim();
        var lv = TrimLoader(loaderVersion);
        return kind switch
        {
            "forge" => lv.Length > 0 ? "Forge " + lv : "Forge",
            "neoforge" => lv.Length > 0 ? "NeoForge " + lv : "NeoForge",
            "fabric" => lv.Length > 0 ? "Fabric " + lv : "Fabric",
            "legacyfabric" => lv.Length > 0 ? "Legacy Fabric " + lv : "Legacy Fabric",
            "quilt" => lv.Length > 0 ? "Quilt " + lv : "Quilt",
            "liteloader" => "LiteLoader",
            "optifine" => lv.Length > 0 ? "OptiFine " + OptiFineDisplay(loaderVersion ?? "") : "OptiFine",
            _ => lv.Length > 0 ? $"{loaderKind} {lv}" : loaderKind ?? "",
        };
    }

    /// <summary>去掉 Fabric 系版本号里的 +build 后缀（PCL 同款 GetSelectName：
    /// "0.16.9+build.1" → "0.16.9.1"，v0.1.13 修复此前只 Trim 没剥 +build 的偏差）。</summary>
    public static string TrimLoader(string v) => (v ?? "").Trim().Replace("+build", "");

    /// <summary>OptiFine 显示名（PCL GetSelectName 同款）：剥掉 "1.20.1 " 版本号前缀再压缩空格，
    /// 如 "1.20.1 HD U I7" → "HD_U_I7"（v0.1.13 修复此前前缀没剥掉的问题）。</summary>
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
