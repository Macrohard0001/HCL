using System.IO;
using System.Reflection;

namespace HardmacroCraft.Services;

/// <summary>
/// 启动追踪日志 —— 记录启动过程每一步到 config/boot.log。
/// 目的：如果用户侧"静默闪退"，boot.log 能区分是
///   a) exe 没跑起来（杀软隔离 / 运行时缺失，boot.log 不存在）还是
///   b) 跑起来了但在某一步崩（boot.log 停在哪一步就是哪里）。
/// 写不进 config/（如 Program Files 只读）时自动回退到用户临时目录，
/// 保证任何环境下至少留下一份日志。
/// </summary>
public static class BootLogger
{
    private static readonly object Lock = new();
    private static string? _logFile;

    /// <summary>当前日志文件路径（可能不在 config/ 而在临时目录）。</summary>
    public static string LogFile => _logFile ??= ResolveLogFile();

    private static string ResolveLogFile()
    {
        // 首选：exe 同目录 config/（便携化原则）
        var candidates = new List<string>();
        try
        {
            var appDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            candidates.Add(Path.Combine(appDir, "config", "boot.log"));
        }
        catch { }

        // 兜底：用户临时目录（config/ 只读或不可写时仍能留痕）
        try
        {
            candidates.Add(Path.Combine(Path.GetTempPath(), "HardmacroCraft-boot.log"));
        }
        catch { }

        foreach (var file in candidates)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                // 探测可写
                File.AppendAllText(file, "");
                return file;
            }
            catch { /* 下一个候选 */ }
        }
        // 全部失败：返回 config/ 下的理想路径，写入时静默忽略
        return candidates.Count > 0 ? candidates[0] : "boot.log";
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                File.AppendAllText(LogFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}");
            }
        }
        catch { /* 日志失败绝不影响启动 */ }
    }

    /// <summary>格式化异常（含全部内层异常与堆栈）。</summary>
    public static string Format(Exception? ex)
    {
        var sb = new System.Text.StringBuilder();
        var indent = 0;
        while (ex != null)
        {
            sb.Append(' ', indent).Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
            sb.Append(' ', indent).AppendLine(ex.StackTrace);
            ex = ex.InnerException;
            if (ex != null) sb.Append(' ', indent).AppendLine(" ---> 内层异常:");
            indent += 2;
        }
        return sb.ToString();
    }

    /// <summary>当前版本号（来自 csproj Version，如 0.1.0-beta）。</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
}
