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
    public const string PreviewPfnPrefix = "Microsoft.MinecraftWindowsBeta";
    public const string StoreUrl = "https://www.minecraft.net/en-us/download";
    private const string BdsPageUrl = "https://www.minecraft.net/en-us/download/server/bedrock";
    public const string LeviDocsUrl = "https://levilauncher.levimc.org/zh-CN/";
    public const string LeviReleaseApiUrl = "https://api.github.com/repos/LiteLDev/LeviLauncher/releases/latest";

    /// <summary>LeviLauncher（LiteLDev，GPL-3.0 开源）—— 实例隔离/mod/LeviLamina 管理；
    /// 其 README 明确要求「A legitimate licensed copy of Minecraft Bedrock Edition」+ 微软账号登录，
    /// 我们只做下载/文档/调用集成，授权模型与本项目一致（正版硬前提）。</summary>
    public sealed record LeviRelease(string Version, string AssetName, string AssetUrl, long SizeBytes);

    [GeneratedRegex(@"href=""(https://[^""]*?bedrock-server-[\d.]+\.zip)""")]
    private static partial Regex BdsLinkRegex();

    public sealed record BedrockInstall(string Version, string PackageFamilyName, string InstallLocation);

    /// <summary>
    /// v0.16.3：检测本机商店版基岩版（Windows）——同时识别正式版（MinecraftUWP）与
    /// Preview（MinecraftWindowsBeta），正式版排在前面；空列表 = 未安装。
    /// </summary>
    public static async Task<List<BedrockInstall>> DetectStoreInstallsAsync()
    {
        if (!OperatingSystem.IsWindows()) return [];
        var psi = new ProcessStartInfo("powershell", """
            -NoProfile -NonInteractive -Command "Get-AppxPackage -Name Microsoft.Minecraft* | ConvertTo-Json -Compress -Depth 2"
            """)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var json = await p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        return ParseAppxList(json);
    }

    /// <summary>兼容旧调用：返回检测到的第一个（正式版优先）安装；null = 未安装。</summary>
    public static async Task<BedrockInstall?> DetectStoreInstallAsync()
        => (await DetectStoreInstallsAsync()).FirstOrDefault();

    /// <summary>纯逻辑（进测试）：解析 Get-AppxPackage ConvertTo-Json 输出（单对象或数组；正式版优先排序）。</summary>
    public static List<BedrockInstall> ParseAppxList(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var list = new List<BedrockInstall>();
                foreach (var item in root.EnumerateArray())
                    if (TryParseInstall(item, out var inst)) list.Add(inst);
                return OrderInstalls(list);
            }
            return TryParseInstall(root, out var one) ? OrderInstalls([one]) : [];
        }
        catch { return []; }
    }

    private static List<BedrockInstall> OrderInstalls(List<BedrockInstall> list)
        => list.OrderByDescending(i => !i.PackageFamilyName.StartsWith(PreviewPfnPrefix, StringComparison.OrdinalIgnoreCase)).ToList();

    private static bool TryParseInstall(System.Text.Json.JsonElement root, out BedrockInstall inst)
    {
        inst = new BedrockInstall("", "", "");
        var pfn = root.TryGetProperty("PackageFamilyName", out var p) ? p.GetString() : null;
        if (string.IsNullOrEmpty(pfn)) return false;
        var isMc = pfn.StartsWith(StorePfnPrefix, StringComparison.OrdinalIgnoreCase)
                || pfn.StartsWith(PreviewPfnPrefix, StringComparison.OrdinalIgnoreCase);
        if (!isMc) return false;
        inst = new BedrockInstall(
            root.TryGetProperty("Version", out var v) ? v.GetString() ?? "" : "",
            pfn,
            root.TryGetProperty("InstallLocation", out var l) ? l.GetString() ?? "" : "");
        return true;
    }

    /// <summary>兼容旧调用：解析单个 Get-AppxPackage 输出（正式版优先），null = 未安装。</summary>
    public static BedrockInstall? ParseAppxJson(string json)
        => ParseAppxList(json).FirstOrDefault();

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

    /// <summary>
    /// v0.16.3：minecraft:// 深链一键注册外部服务器并启动游戏（官方文档协议），
    /// addExternalServer=名称|host:port —— 游戏内「服务器」列表直接出现该条目。
    /// </summary>
    public static async Task<bool> JoinServerAsync(string name, string hostPort)
    {
        var installs = await DetectStoreInstallsAsync();
        if (installs.Count == 0)
            throw new FileNotFoundException("未检测到已安装的 Microsoft Store 版基岩版。");
        var uri = $"minecraft://?addExternalServer={Uri.EscapeDataString(name)}%7C{Uri.EscapeDataString(hostPort)}";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        return true;
    }

    /// <summary>抓取 LeviLauncher 最新 Release（优先 windows 相关资产，回退首个 zip/资产）。</summary>
    public static async Task<LeviRelease?> FetchLatestLeviAsync(HttpClient http)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LeviReleaseApiUrl);
        req.Headers.UserAgent.ParseAdd("HardmacroCraftLauncher/0.16.3");
        using var resp = await http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var rel = doc.RootElement;
        if (!rel.TryGetProperty("assets", out var assets)) return null;
        System.Text.Json.JsonElement chosen = default;
        var hasAsset = false;
        foreach (var a in assets.EnumerateArray())
        {
            if (!a.TryGetProperty("browser_download_url", out var u)) continue;
            var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!hasAsset
                || (name.Contains("windows", StringComparison.OrdinalIgnoreCase) && !(chosen.TryGetProperty("name", out var cn) ? cn.GetString() ?? "" : "").Contains("windows", StringComparison.OrdinalIgnoreCase))
                || (hasAsset && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !(chosen.TryGetProperty("name", out var cn2) ? cn2.GetString() ?? "" : "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                chosen = a;
                hasAsset = true;
            }
        }
        if (!hasAsset) return null;
        return new LeviRelease(
            rel.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "latest" : "latest",
            chosen.TryGetProperty("name", out var an) ? an.GetString() ?? "levilauncher.zip" : "levilauncher.zip",
            chosen.GetProperty("browser_download_url").GetString() ?? "",
            chosen.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0);
    }

    /// <summary>下载 LeviLauncher 资产（DownloadEngine 复用），返回落盘路径。</summary>
    public static async Task<string> DownloadLeviAsync(HttpClient http, LeviRelease info, IProgress<long>? progress)
    {
        var dest = Path.Combine(HorizonService.DownloadDir, info.AssetName);
        await DownloadEngine.DownloadFileAsync(http, info.AssetUrl, dest, expectedSize: info.SizeBytes, byteProgress: progress);
        return dest;
    }

    /// <summary>在资源管理器中显示下载好的文件。</summary>
    public static void RevealInExplorer(string path) => HorizonService.RevealInExplorer(path);
}
