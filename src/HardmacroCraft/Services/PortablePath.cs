using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.11 相对路径增强（纯逻辑，进测试）——「设置 → 游戏目录 → 路径相对化」。
/// 开启后，凡是落在启动器目录内的路径（游戏根目录 / Java 运行时目录等）
/// 在 config/settings.json 里以「相对启动器目录」的形式保存；
/// 整个启动器文件夹（含 .minecraft / config / runtime）拷到移动磁盘另一台机器
/// 的不同盘符 / 目录下时，配置无需修改即可继续使用。
/// 启动器目录外的路径不受影响（仍保存绝对路径）。
/// </summary>
public static class PortablePath
{
    /// <summary>
    /// 序列化：path 在 baseDir 内 → 返回相对形式（统一 '/' 分隔，"./" 前缀便于肉眼识别）；
    /// 否则原样返回绝对路径。baseDir / path 任一为空返回原值。
    /// </summary>
    public static string ToRelative(string? baseDir, string? path)
    {
        if (string.IsNullOrWhiteSpace(baseDir) || string.IsNullOrWhiteSpace(path)) return path ?? "";
        try
        {
            var baseFull = Path.GetFullPath(baseDir).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pathFull = Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var rel = Path.GetRelativePath(baseFull, pathFull);
            if (string.IsNullOrEmpty(rel) || rel == ".") return "./";
            // 不在 baseDir 内（GetRelativePath 会给出 ..\ 前缀）→ 保留绝对路径
            if (rel.StartsWith("..", StringComparison.Ordinal)) return path;
            return "./" + rel.Replace('\\', '/');
        }
        catch
        {
            return path;
        }
    }

    /// <summary>
    /// 反序列化：以 "./" 开头（或非 rooted）的值按 baseDir 解析为绝对路径；
    /// 已是绝对路径的原样返回（兼容老配置）。baseDir 为空时按进程当前目录解析（不推荐）。
    /// </summary>
    public static string ToAbsolute(string? baseDir, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        if (value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith(".\\", StringComparison.Ordinal))
        {
            var rel = value[2..].Replace('/', Path.DirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(baseDir)
                ? Path.GetFullPath(rel)
                : Path.Combine(Path.GetFullPath(baseDir), rel);
        }
        try
        {
            if (Path.IsPathRooted(value)) return value;
        }
        catch { /* 非法字符按原样返回 */ }
        // Windows 风格盘符路径（跨平台一致）：按绝对路径原样返回
        if (LooksWindowsRooted(value)) return value;
        return string.IsNullOrWhiteSpace(baseDir) ? value : Path.Combine(baseDir, value);
    }

    /// <summary>判断 value 是否为「相对化保存」的形式（./ 前缀或非 rooted 相对路径）。
    /// Windows 风格盘符路径（C:\...）在任何平台上都视为绝对路径（测试可在 Linux 跑）。</summary>
    public static bool LooksRelative(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.StartsWith("./", StringComparison.Ordinal) || value.StartsWith(".\\", StringComparison.Ordinal))
            return true;
        if (LooksWindowsRooted(value)) return false;
        try { return !Path.IsPathRooted(value); }
        catch { return false; }
    }

    /// <summary>识别 Windows 风格盘符 / UNC 路径（跨平台一致，便于无头测试）。</summary>
    private static bool LooksWindowsRooted(string value)
        => (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':'
                && (value[2] == '\\' || value[2] == '/'))
           || value.StartsWith("\\\\", StringComparison.Ordinal);
}
