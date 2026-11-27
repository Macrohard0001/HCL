using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.9：启动器自更新（HMCL / PCL / Modrinth App 标配）—— GitHub Releases latest
/// 检测 → 语义化版本比较（仅升不降）→ 下载发布资产（便携全量包优先、单文件包兜底）
/// → 解压到 staging → 写 upgrade.bat（等旧进程退出 → xcopy 覆盖 → 重启 → 自删）
/// → 启动脚本后关闭自身。Windows 文件锁无法覆盖正在运行的 exe，延迟替换脚本绕开。
/// 纯逻辑（版本比较 / release JSON 解析 / bat 文本 / zip-slip 解压）headless 可测。
/// </summary>
public static class SelfUpdateService
{
    public const string ReleaseApiUrl = "https://api.github.com/repos/Macrohard0001/HCL/releases/latest";
    public const string ExeName = "HardmacroCraftLauncher.exe";

    public sealed record LatestInfo(string Version, string AssetName, string AssetUrl, long SizeBytes, string Notes);

    /// <summary>
    /// 版本比较：'v0.16.9' / '0.16.9-beta' / '0.16.9+sha' 统一清洗后逐段比，
    /// 段数不齐补零（1.20 == 1.20.0）。返回 -1/0/1。
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        var pa = Segments(a);
        var pb = Segments(b);
        for (var i = 0; i < Math.Max(pa.Count, pb.Count); i++)
        {
            var x = i < pa.Count ? pa[i] : 0;
            var y = i < pb.Count ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;

        static List<int> Segments(string v)
        {
            var head = v.Trim().TrimStart('v', 'V');
            var plus = head.IndexOf('+');
            if (plus >= 0) head = head[..plus];
            var dash = head.IndexOf('-');
            if (dash >= 0) head = head[..dash];
            var list = new List<int>();
            foreach (var seg in head.Split('.'))
                list.Add(int.TryParse(seg, out var n) ? n : 0);
            return list;
        }
    }

    /// <summary>
    /// 解析 latest release JSON：tag_name 取版本，与当前版本比较（相同 / 更旧返回 null =
    /// 已是最新）。资产选 zip：便携全量包（HardmacroCraft-Launcher-vX-Portable.zip）优先，
    /// 单文件包兜底，同名其它 zip 再兜底；无 zip 资产返回 null。
    /// </summary>
    public static LatestInfo? ParseReleaseJson(string json, string currentVersion)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var ver = tag.TrimStart('v', 'V');
            if (ver.Length == 0 || CompareVersions(ver, currentVersion) <= 0) return null;
            string name = "", url = "";
            long size = 0;
            // 资产优先级：便携全量包 0 < 单文件包 1 < 同前缀任意 zip 2（全扫一遍取最优，
            // 不能命中即 break —— 列表顺序不定，单文件排前会抢走便携包）
            var prefer = new[]
            {
                $"HardmacroCraft-Launcher-v{ver}-Portable.zip",
                $"HardmacroCraft-Launcher-v{ver}-SingleFile.zip",
            };
            var bestRank = 3;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var n = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                var rank = n == prefer[0] ? 0
                    : n == prefer[1] ? 1
                    : n.StartsWith($"HardmacroCraft-Launcher-v{ver}", StringComparison.Ordinal)
                        && n.EndsWith(".zip", StringComparison.Ordinal) ? 2 : -1;
                if (rank < 0 || rank >= bestRank) continue;
                bestRank = rank;
                name = n;
                url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                size = a.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                if (bestRank == 0) break; // 最优已到手
            }
            if (name.Length == 0 || url.Length == 0) return null;
            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            return new LatestInfo(ver, name, url, size, notes);
        }
        catch { return null; }
    }

    /// <summary>拉取 latest release 并与当前版本比较；已是最新 / 无可用资产 / 网络异常都返回 null（异常向上抛由 UI 呈现）。</summary>
    public static async Task<LatestInfo?> CheckAsync(HttpClient http, string currentVersion)
    {
        using var resp = await http.GetAsync(ReleaseApiUrl);
        resp.EnsureSuccessStatusCode();
        return ParseReleaseJson(await resp.Content.ReadAsStringAsync(), currentVersion);
    }

    /// <summary>下载更新包（DownloadEngine 复用：断点续传 + 大小校验 + 进度上报）。</summary>
    public static Task DownloadAsync(HttpClient http, LatestInfo info, string dest, IProgress<long>? progress) =>
        DownloadEngine.DownloadFileAsync(http, info.AssetUrl, dest, expectedSize: info.SizeBytes, byteProgress: progress);

    /// <summary>解压更新包到 staging 目录（zip-slip 防护：拒绝越出 staging 的条目）。</summary>
    public static void ExtractUpdate(string zipPath, string stagingDir)
    {
        Directory.CreateDirectory(stagingDir);
        var stagingFull = Path.GetFullPath(stagingDir);
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.Length == 0) continue;
            var norm = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (norm.Length == 0) continue;
            if (norm.Contains("..")) continue; // zip-slip 防护
            var target = Path.GetFullPath(Path.Combine(stagingFull, norm.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(stagingFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var es = entry.Open();
            using var fs = File.Create(target);
            es.CopyTo(fs);
        }
    }

    /// <summary>
    /// 升级脚本文本（纯函数）：循环等旧进程退出 → xcopy 整目录覆盖 → 重启 → 自删。
    /// </summary>
    public static string BuildUpdateScriptText(string stagingDir, string appDir)
    {
        return $"""
@echo off
rem HCL self-update: wait for launcher exit, overwrite files, restart
:wait
tasklist /FI "IMAGENAME eq {ExeName}" | find /I "{ExeName}" >nul 2>&1
if not errorlevel 1 (
  timeout /t 1 /nobreak >nul
  goto wait
)
xcopy /e /y /i "{stagingDir}" "{appDir}"
start "" "{Path.Combine(appDir, ExeName)}"
del "%~f0"
""";
    }

    /// <summary>写升级脚本到临时目录（时间戳命名避碰撞），返回 bat 路径。</summary>
    public static string WriteUpdateScript(string stagingDir, string appDir)
    {
        var bat = Path.Combine(Path.GetTempPath(), $"HCL-update-{DateTime.Now:HHmmss}.bat");
        File.WriteAllText(bat, BuildUpdateScriptText(stagingDir, appDir));
        return bat;
    }

    /// <summary>
    /// 应用更新：解压 → 写脚本 → 启动脚本（cmd 静默）。调用方随后自行关闭主窗口 ——
    /// 脚本会等旧进程完全退出再覆盖，重启后即新版本。
    /// </summary>
    public static string ApplyUpdate(string zipPath, string stagingDir, string appDir)
    {
        Directory.CreateDirectory(stagingDir);
        ExtractUpdate(zipPath, stagingDir);
        var bat = WriteUpdateScript(stagingDir, appDir);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{bat}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        BootLogger.Info($"自更新：脚本 {bat}，staging={stagingDir}，app={appDir}");
        return bat;
    }
}
