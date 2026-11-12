namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.11 版本默认命名（纯逻辑，进测试）—— 对齐主流启动器的默认版本名风格：
///   原版：&lt;MC 版本&gt;                    如 1.20.1
///   Forge：&lt;MC&gt;-Forge_&lt;loader 版本&gt;       如 1.20.1-Forge_47.2.0
///   NeoForge：&lt;MC&gt;-NeoForge_&lt;loader 版本&gt; 如 1.21.0-NeoForge_21.0.67
///   Fabric：&lt;MC&gt;-Fabric &lt;loader 版本&gt;     如 1.20.1-Fabric 0.16.9（去掉 +build）
///   LegacyFabric：&lt;MC&gt;-LegacyFabric &lt;v&gt;
///   Quilt：&lt;MC&gt;-Quilt &lt;loader 版本&gt;
///   LiteLoader：&lt;MC&gt;-LiteLoader（无版本号后缀）
///   OptiFine：&lt;MC&gt;-OptiFine_&lt;显示名&gt;
/// 用户在安装弹窗里改名后以用户输入为准（前缀相同则视为未改，安装后不再重命名）。
/// </summary>
public static class VersionNaming
{
    /// <summary>按 MC 版本 + 加载器种类（VersionNaming.LoaderKind 字符串）+ 加载器版本生成默认版本名。</summary>
    public static string DefaultName(string mcVersion, string loaderKind, string? loaderVersion)
    {
        var mc = (mcVersion ?? "").Trim();
        if (string.IsNullOrEmpty(mc)) return "";
        return loaderKind?.Trim().ToLowerInvariant() switch
        {
            "" or "vanilla" => mc,
            "forge" => loaderVersion is { Length: > 0 } ? $"{mc}-Forge_{TrimLoader(loaderVersion)}" : mc + "-Forge",
            "neoforge" => loaderVersion is { Length: > 0 } ? $"{mc}-NeoForge_{TrimLoader(loaderVersion)}" : mc + "-NeoForge",
            "fabric" => loaderVersion is { Length: > 0 } ? $"{mc}-Fabric {TrimLoader(loaderVersion)}" : mc + "-Fabric",
            "legacyfabric" => loaderVersion is { Length: > 0 } ? $"{mc}-LegacyFabric {TrimLoader(loaderVersion)}" : mc + "-LegacyFabric",
            "quilt" => loaderVersion is { Length: > 0 } ? $"{mc}-Quilt {TrimLoader(loaderVersion)}" : mc + "-Quilt",
            "liteloader" => mc + "-LiteLoader",
            "optifine" => loaderVersion is { Length: > 0 } ? $"{mc}-OptiFine_{OptiFineDisplay(loaderVersion)}" : mc + "-OptiFine",
            _ => loaderVersion is { Length: > 0 } ? $"{mc}-{loaderKind}_{TrimLoader(loaderVersion)}" : mc,
        };
    }

    /// <summary>去掉 Fabric 系版本号里的 +build 后缀（主流启动器同款显示习惯）。</summary>
    public static string TrimLoader(string v) => (v ?? "").Trim();

    /// <summary>OptiFine 显示名：剥掉 "1.20.1 " 前缀与空格（如 "1.20.1 HD U I7" → "HD_U_I7"）。</summary>
    public static string OptiFineDisplay(string display)
    {
        var s = (display ?? "").Trim();
        if (s.StartsWith("HD", StringComparison.OrdinalIgnoreCase))
            return s.Replace(' ', '_');
        return s.Replace(' ', '_');
    }

    /// <summary>判断用户输入是否仍是默认名（或为空）—— 安装流程据此决定是否需要重命名。</summary>
    public static bool IsDefaultOrEmpty(string? userInput, string mcVersion, string loaderKind, string? loaderVersion)
        => string.IsNullOrWhiteSpace(userInput)
           || string.Equals(userInput.Trim(), DefaultName(mcVersion, loaderKind, loaderVersion), StringComparison.Ordinal);
}
