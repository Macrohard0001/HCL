using System.IO;
using System.Linq;
using System.Text;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.1 校验缓存戳：版本文件在"上次启动"时已被 CmlLib 安装器完整校验过一次，
/// 之后每次启动都重新对全部 2800+ 文件算哈希既慢又没必要（用户观感 = "每次都重新补全"）。
/// 戳 = 版本 json 的 LastWriteTimeUtc + 长度 + 启动器版本号 + 游戏根目录；
/// 任一变化（更新版本 / 升级启动器 / 换游戏目录）即自动失效，重新走完整校验。
/// 客户端主 jar 必须同时存在才算有效（防止只删 jar 时误判）。
/// </summary>
public static class VerifyStamp
{
    /// <summary>戳文件路径（configDir 可注入便于测试）。</summary>
    public static string StampPath(string gameRoot, string versionName, string? configDir = null)
        => Path.Combine(configDir ?? AppSettings.ConfigDir, "verify-cache",
            Encode(gameRoot) + "_" + Encode(versionName) + ".stamp");

    /// <summary>期望的戳内容（版本 json 未变 + 启动器版本 + 游戏根目录一致）。</summary>
    public static string ExpectedStamp(string gameRoot, string versionName)
    {
        var jsonPath = Path.Combine(gameRoot, "versions", versionName, versionName + ".json");
        var fi = new FileInfo(jsonPath);
        return $"{fi.LastWriteTimeUtc.Ticks}|{fi.Length}|{LauncherService.LauncherVersion}|{gameRoot}";
    }

    /// <summary>该版本是否可以跳过文件校验（快速启动）。</summary>
    public static bool IsValid(string gameRoot, string versionName, string? configDir = null)
    {
        try
        {
            var jsonPath = Path.Combine(gameRoot, "versions", versionName, versionName + ".json");
            if (!File.Exists(jsonPath)) return false;
            var stampPath = StampPath(gameRoot, versionName, configDir);
            if (!File.Exists(stampPath)) return false;
            return File.ReadAllText(stampPath) == ExpectedStamp(gameRoot, versionName);
        }
        catch { return false; }
    }

    /// <summary>完整校验成功后写入戳。同时要求版本目录里有 jar（防"只有 json 没有 jar"的残缺版本被缓存）。</summary>
    public static void Write(string gameRoot, string versionName, string? configDir = null)
    {
        try
        {
            var verDir = Path.Combine(gameRoot, "versions", versionName);
            if (!Directory.Exists(verDir) || !Directory.GetFiles(verDir, "*.jar").Any()) return;
            var dir = Path.GetDirectoryName(StampPath(gameRoot, versionName, configDir))!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(StampPath(gameRoot, versionName, configDir), ExpectedStamp(gameRoot, versionName), new UTF8Encoding(false));
        }
        catch { /* 写戳失败不影响启动，下次重新校验 */ }
    }

    /// <summary>删除版本 / 强制重校验时清除戳。</summary>
    public static void Invalidate(string gameRoot, string versionName, string? configDir = null)
    {
        try { if (File.Exists(StampPath(gameRoot, versionName, configDir))) File.Delete(StampPath(gameRoot, versionName, configDir)); }
        catch { /* 占用等场景忽略 */ }
    }

    /// <summary>路径转安全文件名（含路径分隔符 / 中文等都可逆编码）。</summary>
    public static string Encode(string raw)
    {
        var sb = new StringBuilder();
        foreach (var c in raw)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        var s = sb.ToString();
        return s.Length == 0 ? "empty" : (s.Length > 80 ? s[..80] : s);
    }
}
