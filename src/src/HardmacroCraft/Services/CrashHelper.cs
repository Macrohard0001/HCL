using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// 崩溃报告与游戏日志定位（PCL 同款体验：游戏异常退出时快速给出报告位置）。
/// 纯路径/文件逻辑，无 UI 依赖，供桌面端与手机端 HmCli 共用。
/// </summary>
public static class CrashHelper
{
    /// <summary>游戏日志目录（Minecraft 标准：&lt;root&gt;/logs）。</summary>
    public static string LogsDir(string gameRoot) => Path.Combine(gameRoot, "logs");

    /// <summary>崩溃报告目录（Minecraft 标准：&lt;root&gt;/crash-reports）。</summary>
    public static string CrashReportsDir(string gameRoot) => Path.Combine(gameRoot, "crash-reports");

    /// <summary>游戏最新日志（logs/latest.log）。</summary>
    public static string LatestLogPath(string gameRoot) => Path.Combine(LogsDir(gameRoot), "latest.log");

    /// <summary>
    /// 列出崩溃报告（*.txt / *.log），按修改时间倒序（最新在前）。
    /// 目录不存在或为空时返回空列表，不抛异常。
    /// </summary>
    public static List<string> ListCrashReports(string gameRoot)
    {
        var dir = CrashReportsDir(gameRoot);
        try
        {
            if (!Directory.Exists(dir)) return new List<string>();
            return Directory.GetFiles(dir)
                .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>最新一份崩溃报告的完整路径；没有则为 null。</summary>
    public static string? FindLatestCrashReport(string gameRoot) =>
        ListCrashReports(gameRoot).FirstOrDefault();

    /// <summary>
    /// 退出码判定：0 = 正常退出；非 0 = 可能崩溃（Minecraft 崩溃时通常为 -1 / 1）。
    /// 同时给出「游戏还是 JVM 层面异常」的粗略提示文案（PCL 式友好描述）。
    /// </summary>
    public static bool IsAbnormalExit(int exitCode) => exitCode != 0;
}
