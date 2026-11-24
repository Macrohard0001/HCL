using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.0：Horizon 支持 —— Rust 重制版 Minecraft（出处备注见下）。
///
/// 出处（用户要求备注）：
///   - 项目本体：https://github.com/horizon-rust/horizon （"Minecraft 26.3 rewritten in Rust"，LGPL-3.0-or-later）
///   - 官网：https://playhorizon.dev
///   - 重要澄清：官方 README 明确称"rewritten from scratch"（从零重写），**没有提供任何
///     "无损转译器"**；社区流传的"无损转译为 Rust"实指其用 281 套差分测试（differential
///     harnesses）对比 Rust 版与 Java 原版输出、保证行为级无损。本启动器整合的是
///     "获取/管理 Horizon 启动器"这一真实功能，并如实标注出处，不冒充转译能力。
///
/// 运行时说明（真实依赖，无 rustc）：
///   - 玩家需拥有正版 Minecraft Java 26.3，且用官方启动器跑过一次；
///   - Mojang Java 运行时（如 Java 25）由 Horizon 启动器按版本 JSON 自动安装；
///   - 下载对象是预编译 Horizon 启动器安装包（Windows x64 setup.exe，约 82 MB）。
/// </summary>
public static class HorizonService
{
    public const string RepoUrl = "https://github.com/horizon-rust/horizon";
    public const string SiteUrl = "https://playhorizon.dev";
    public const string ReleasesUrl = RepoUrl + "/releases";
    public const string ReleaseApiUrl = "https://api.github.com/repos/horizon-rust/horizon/releases/latest";

    /// <summary>启动器安装包（Windows x64 setup.exe）落盘目录。</summary>
    public static string DownloadDir => AppSettings.Instance.DownloadsDir;

    public sealed record ReleaseInfo(string Version, string AssetName, string AssetUrl, long SizeBytes);

    private sealed class GhRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public GhAsset[]? Assets { get; set; }
    }

    private sealed class GhAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
    }

    /// <summary>拉取最新 release 的 Windows x64 安装包信息（GitHub API，走传入的下载管线 = 镜像/代理出口）。</summary>
    public static async Task<ReleaseInfo?> FetchLatestAsync(HttpClient http, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl);
        req.Headers.UserAgent.ParseAdd("HardmacroCraftLauncher/0.16.0");
        using var resp = await http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var rel = await resp.Content.ReadFromJsonAsync<GhRelease>(cancellationToken: ct);
        if (rel?.Assets is null) return null;
        // Windows x64 优先：名称含 setup.exe 且不含 arm64
        var asset = rel.Assets.FirstOrDefault(a =>
                (a.Name?.EndsWith("setup.exe", StringComparison.OrdinalIgnoreCase) ?? false)
                && !(a.Name.Contains("arm64", StringComparison.OrdinalIgnoreCase)))
            ?? rel.Assets.FirstOrDefault(a =>
                a.Name?.Contains("windows", StringComparison.OrdinalIgnoreCase) ?? false);
        if (asset?.Url is null) return null;
        return new ReleaseInfo(rel.TagName ?? "latest", asset.Name ?? "horizon-setup.exe", asset.Url, asset.Size);
    }

    /// <summary>下载安装包（复用 DownloadEngine：重试/续传/校验），返回落盘路径。</summary>
    public static async Task<string> DownloadLauncherAsync(
        HttpClient http, ReleaseInfo info, IProgress<long>? byteProgress, CancellationToken ct = default)
    {
        var dest = Path.Combine(DownloadDir, info.AssetName);
        await DownloadEngine.DownloadFileAsync(http, info.AssetUrl, dest,
            expectedSize: info.SizeBytes, byteProgress: byteProgress, ct: ct);
        return dest;
    }

    /// <summary>
    /// 检测本机是否已装 Horizon 启动器/游戏（启发式扫常见安装位置，找不到 ≠ 没装）。
    /// </summary>
    public static string? DetectInstall()
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        }.Where(p => !string.IsNullOrEmpty(p)).SelectMany(root => new[]
        {
            Path.Combine(root, "Horizon"), Path.Combine(root, "horizon"),
            Path.Combine(root, "horizon-rust"), Path.Combine(root, "Programs", "Horizon"),
        }).Distinct();

        foreach (var dir in candidates)
        {
            try
            {
                if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
                    return dir;
            }
            catch { /* 权限问题跳过 */ }
        }
        return null;
    }

    /// <summary>资源管理器打开落盘的安装包所在目录（手动下一步用）。</summary>
    public static void RevealInExplorer(string filePath)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }
}
