using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.0 崩溃智能分析（PCL2/HMCL 同类功能，本地规则匹配，不上传任何数据）。
/// 输入 latest.log 或 crash-report 文本 → 关键词规则表 → 中文诊断 + 建议。
/// 只做提示不做结论：命中的每条规则都给出「原因 + 建议」。
/// </summary>
public static class CrashAnalyzer
{
    public sealed record Finding(string Keyword, string Reason, string Advice);

    // 规则表（懒构建缓存）：按具体→通用排列
    private static List<(Regex Re, Finding F)>? _cache;

    public static IReadOnlyList<(Regex Re, Finding F)> Rules2 => _cache ??= BuildRules();

    private static List<(Regex Re, Finding F)> BuildRules() => new()
    {
        (new Regex(@"UnsupportedClassVersionError|bad class file", RegexOptions.IgnoreCase),
            new Finding("UnsupportedClassVersionError",
                "Java 版本与该 Minecraft 版本不匹配",
                "在 设置 → Java 中为该版本选择正确的 Java（1.20.5+ 需 Java 21、1.18+ 需 Java 17、旧版用 Java 8）；或使用 Java 自动管理")),
        (new Regex(@"Could not reserve enough space|Unable to allocate .*MB bitmap|OutOfMemoryError|OutOfMemory Direct buffer memory", RegexOptions.IgnoreCase),
            new Finding("OutOfMemory",
                "内存不足（堆或直接缓冲区分配失败）",
                "降低分配内存（实例设置 → 内存）后重试；32 位 Java 最多约 1.5G，请换 64 位 Java；关闭过多后台程序")),
        (new Regex(@"Missing or unsupported mandatory|MissingModsException|Missing metadata|Incompatible mod set!", RegexOptions.IgnoreCase),
            new Finding("ModMissing",
                "模组缺失或模组集不完整",
                "按日志提示补齐缺失模组（多为前置依赖）；或移除刚安装的可疑模组")),
        (new Regex(@"Mixin apply failed|MixinTargetNotFound|InjectionError|Critical injection failure", RegexOptions.IgnoreCase),
            new Finding("Mixin",
                "Mixin 注入失败（模组与当前版本不兼容）",
                "更新或移除日志中提到的模组；确认模组版本与游戏/加载器版本一致")),
        (new Regex(@"Fabric(?:Loader)? (?:update|version)|The Fabric Loader requires", RegexOptions.IgnoreCase),
            new Finding("FabricLoader",
                "Fabric Loader 版本过旧或与游戏版本不符",
                "重新安装/更新 Fabric Loader；不要混用不同加载器的模组")),
        (new Regex(@"GLFW(?: error| _error)|OpenGL|The driver .* does not appear to support|dxgi", RegexOptions.IgnoreCase),
            new Finding("GPU",
                "显卡/驱动初始化失败",
                "更新显卡驱动；笔记本确认用独立显卡运行；远程桌面/虚拟机不支持 OpenGL 时换实机")),
        (new Regex(@"Access is denied|Permission denied|AccessDeniedException", RegexOptions.IgnoreCase),
            new Finding("Permission",
                "文件权限被拒绝（常见于杀毒软件/受控文件夹拦截）",
                "把游戏目录加入杀毒软件白名单；以管理员运行一次或把游戏移出受保护目录")),
        (new Regex(@"Connection refused|timed out|UnknownHostException|SSLException|HTTP 4\d\d|HTTP 5\d\d", RegexOptions.IgnoreCase),
            new Finding("Network",
                "网络下载/连接失败",
                "检查网络与代理设置；在设置中切换镜像源后重试；校园网/公司网可能拦截资源站")),
        (new Regex(@"expecting number, got|JsonSyntaxException|com\.google\.gson|Failed to (?:parse|load) version", RegexOptions.IgnoreCase),
            new Finding("VersionJson",
                "版本 JSON 损坏",
                "删除该版本目录后重新安装/导入；或使用版本管理 → 导入官方版本修复")),
        (new Regex(@"Game crashed.*Codewhite|Found multiple mods|Duplicate|DuplicateModsFoundException", RegexOptions.IgnoreCase),
            new Finding("DuplicateMod",
                "重复安装了同一模组（或同一模组的多版本）",
                "删除 mods 目录中的重复 jar（保留其一）")),
        (new Regex(@"Invalid player data|Failed to read level|corrupt(?:ed)? (?:level|save|region)", RegexOptions.IgnoreCase),
            new Finding("SaveCorrupt",
                "存档损坏",
                "从备份恢复存档；不要用旧版本打开新版本存档")),
        (new Regex(@"Pixel format not accelerated|Could not create context|no capable", RegexOptions.IgnoreCase),
            new Finding("GLContext",
                "OpenGL 上下文创建失败",
                "更新显卡驱动；强制用独显；不要在远程桌面会话中启动游戏")),
    };

    /// <summary>分析文本，返回命中结果（去重，最多 6 条）。</summary>
    public static List<Finding> Analyze(string text)
    {
        var result = new List<Finding>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        foreach (var (re, f) in Rules2)
        {
            if (re.IsMatch(text) && !result.Contains(f)) result.Add(f);
            if (result.Count >= 6) break;
        }
        return result;
    }

    /// <summary>
    /// 读取并分析崩溃上下文：优先最新崩溃报告，辅以 latest.log 尾部。
    /// 返回可直接写入运行日志的多行诊断文本（无命中返回 null）。
    /// </summary>
    public static string? DiagnoseLatest(string gameRoot)
    {
        var report = CrashHelper.FindLatestCrashReport(gameRoot);
        string text = "";
        try
        {
            if (report is not null) text += File.ReadAllText(report);
        }
        catch { /* 报告读取失败不阻断 */ }
        try
        {
            var log = CrashHelper.LatestLogPath(gameRoot);
            if (File.Exists(log))
            {
                var lines = File.ReadAllLines(log);
                text += "\n" + string.Join("\n", lines[^Math.Min(lines.Length, 4000)..]);
            }
        }
        catch { /* 日志读取失败不阻断 */ }

        var findings = Analyze(text);
        if (findings.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("【崩溃诊断】可能的原因（本地规则匹配，仅供参考）：");
        foreach (var f in findings)
        {
            sb.Append("  • ").Append(f.Reason).AppendLine();
            sb.Append("    建议：").Append(f.Advice).AppendLine();
        }
        // 出处备注：诊断规则参考 PCL2 / HMCL 的崩溃分析思路（本地总结，未复制代码）。
        sb.Append("出处说明：完整报告见 crash-reports 目录；诊断规则为社区常见崩溃案例的本地总结，仅供参考。");
        return sb.ToString().TrimEnd();
    }
}
