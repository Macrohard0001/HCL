using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.2：基岩版（Bedrock / UWP）下载与启动 —— 只走正版路径：
/// 1) 检测本机 Microsoft Store 版（Get-AppxPackage Microsoft.MinecraftUWP），
///    已购用户点击即用 explorer shell:AppsFolder 启动（微软账号随系统登录，
///    单机/离线模式由商店版自身支持）；
/// 2) 官方基岩版专用服务端（BDS）—— Mojang 免费公开下载，爬取 minecraft.net
///    官方下载页拿到 bedrock-server-*.zip 直链后用 DownloadEngine 下载（重试/续传/进度）；
/// 3) 不提供绕过正版验证的登录方式（无授权微软账号登录 = 绕过付费授权，不做），
///    购买/安装引导到 minecraft.net 官方入口。
/// </summary>
public static partial class BedrockService
{
    public const string StorePfnPrefix = "Microsoft.MinecraftUWP";
    public const string StoreUrl = "https://www.minecraft.net/en-us/download";
    private const string BdsPageUrl = "https://www.minecraft.net/en-us/download/server/bedrock";

    [GeneratedRegex(@"href=""(https://[^""]*?bedrock-server-[\d.]+\.zip)""")]
    private static partial Regex BdsLinkRegex();

    public sealed record BedrockInstall(string Version, string PackageFamilyName, string InstallLocation);

    /// <summary>检测本机商店版基岩版（Windows；返回 null = 未安装）。</summary>
    public static async Task<BedrockInstall?> DetectStoreInstallAsync()
    {
        if (!OperatingSystem.IsWindows()) return null;
        var psi = new ProcessStartInfo("powershell", """
            -NoProfile -NonInteractive -Command "Get-AppxPackage -Name Microsoft.MinecraftUWP | ConvertTo-Json -Compress"
            """)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var json = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().StartsWith('{') is false) return null;
        return ParseAppxJson(json);
    }

    /// <summary>纯逻辑（进测试）：解析 Get-AppxPackage ConvertTo-Json 输出（单对象或数组取第一个）。</summary>
    public static BedrockInstall? ParseAppxJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                if (root.GetArrayLength() == 0) return null;
                root = root[0];
            }
            var pfn = root.TryGetProperty("PackageFamilyName", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(pfn) || !pfn.StartsWith(StorePfnPrefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return new BedrockInstall(
                root.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "",
                pfn,
                root.TryGetProperty("InstallLocation", out var l) ? l.GetString() ?? "" : "");
        }
        catch { return null; }
    }

    /// <summary>启动商店版基岩版（shell:AppsFolder 协议；未安装时抛 FileNotFoundException）。</summary>
    public static async Task LaunchStoreAsync()
    {
        var inst = await DetectStoreInstallAsync()
            ?? throw new FileNotFoundException("未检测到已安装的 Microsoft Store 版基岩版。");
        Process.Start(new ProcessStartInfo("explorer.exe", $@"shell:AppsFolder\{inst.PackageFamilyName}!App")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>爬取官方 BDS 下载页，返回（版本号, zip 直链）。页面结构变化时返回 null。</summary>
    public static async Task<(string Version, string Url)?> FetchLatestBdsAsync(HttpClient http)
    {
        var html = await http.GetStringAsync(BdsPageUrl);
        var m = BdsLinkRegex().Match(html);
        if (!m.Success) return null;
        var url = m.Groups[1].Value;
        var v = Regex.Match(url, @"bedrock-server-([\d.]+)\.zip").Groups[1].Value;
        return (v, url);
    }

    /// <summary>下载 BDS 服务端 zip（DownloadEngine 复用：重试/续传/进度回调），返回落盘路径。</summary>
    public static async Task<string> DownloadBdsAsync(HttpClient http, string url, IProgress<long>? progress)
    {
        var fileName = url[(url.LastIndexOf('/') + 1)..];
        var dest = Path.Combine(HorizonService.DownloadDir, fileName);
        await DownloadEngine.DownloadFileAsync(http, url, dest, byteProgress: progress);
        return dest;
    }

    /// <summary>在资源管理器中显示下载好的文件。</summary>
    public static void RevealInExplorer(string path) => HorizonService.RevealInExplorer(path);
}
