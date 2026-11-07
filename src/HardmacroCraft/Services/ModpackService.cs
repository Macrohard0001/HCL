using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using CmlLib.Core;

namespace HardmacroCraft.Services;

/// <summary>
/// 整合包安装器：解析并安装 Modrinth .mrpack 整合包。
/// 安装到独立的版本目录（版本隔离），共享 assets / libraries；
/// 随后按依赖自动安装 Fabric / Forge / NeoForge / Quilt 装载器。
/// </summary>
public class ModpackService
{
    private readonly HttpClient _http;

    public ModpackService(HttpClient http) => _http = http;

    // modrinth.index.json 结构
    public class MrpackIndex
    {
        public int FormatVersion { get; set; }
        public string Game { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Summary { get; set; }
        public Dictionary<string, string> Dependencies { get; set; } = new();
        public List<MrpackFile> Files { get; set; } = new();
    }

    public class MrpackFile
    {
        public string Path { get; set; } = "";
        public Dictionary<string, string>? Hashes { get; set; }
        public Dictionary<string, string>? Env { get; set; }
        public List<string> Downloads { get; set; } = new();
        public long FileSize { get; set; }
    }

    public record ModpackInfo(string Name, string GameVersion, string? Loader, string? LoaderVersion, int FileCount, long TotalBytes);

    /// <summary>读取 .mrpack 的元信息（不安装）。</summary>
    public static ModpackInfo ReadInfo(string mrpackPath)
    {
        using var zip = ZipFile.OpenRead(mrpackPath);
        var entry = zip.GetEntry("modrinth.index.json")
            ?? throw new InvalidDataException("不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");
        var index = ParseIndex(entry);
        var (loader, loaderVer) = GetLoader(index.Dependencies);
        return new ModpackInfo(
            string.IsNullOrEmpty(index.Name) ? Path.GetFileNameWithoutExtension(mrpackPath) : index.Name,
            index.Dependencies.GetValueOrDefault("minecraft", "?"),
            loader, loaderVer,
            index.Files.Count,
            index.Files.Sum(f => f.FileSize));
    }

    // modrinth.index.json 实际字段为 camelCase（formatVersion / versionId / fileSize），
    // 必须开启大小写不敏感匹配，否则真实整合包全部解析为空
    private static readonly JsonSerializerOptions MrpackJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static MrpackIndex ParseIndex(ZipArchiveEntry entry)
    {
        using var s = entry.Open();
        return JsonSerializer.Deserialize<MrpackIndex>(s, MrpackJsonOpts) ?? throw new InvalidDataException("modrinth.index.json 解析失败。");
    }

    private static (string? loader, string? version) GetLoader(Dictionary<string, string> deps)
    {
        foreach (var key in new[] { "fabric-loader", "forge", "neoforge", "quilt-loader" })
            if (deps.TryGetValue(key, out var v))
                return (key, v);
        return (null, null);
    }

    public record ModpackInstallProgress(string CurrentFile, int DoneFiles, int TotalFiles, long DoneBytes, long TotalBytes);

    /// <summary>
    /// 完整安装流程：下载整合包内容 → 安装装载器 → 返回实际可启动的版本 ID。
    /// v1.7.0 起文件下载走 DownloadEngine（并发 + sha1 校验 + 重试 + 断点续传），
    /// 彻底修复之前直接流拷贝导致的半截文件 / 静默损坏。
    /// </summary>
    public async Task<string> InstallAsync(string mrpackPath, string versionName,
        LauncherService launcher, IProgress<ModpackInstallProgress> progress,
        Action<string> log, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(mrpackPath);
        var entry = zip.GetEntry("modrinth.index.json")
            ?? throw new InvalidDataException("不是有效的 Modrinth 整合包。");
        var index = ParseIndex(entry);
        var mcVersion = index.Dependencies.GetValueOrDefault("minecraft")
            ?? throw new InvalidDataException("整合包未声明 Minecraft 版本。");

        // 1. 安装原版前置（jar / assets / libraries）
        log($"[整合包] 安装 Minecraft {mcVersion} 前置文件...");
        await launcher.InstallAsync(mcVersion, null, null);

        // 2. 下载整合包文件（跳过客户端不需要的文件）
        //    目标目录 = 该版本的隔离游戏目录（mods / resourcepacks / shaderpacks 互不污染）
        var gameDir = Path.GetFullPath(launcher.BuildGamePath(versionName).BasePath);
        Directory.CreateDirectory(gameDir);
        var files = index.Files
            .Where(f => f.Env == null || f.Env.GetValueOrDefault("client", "required") != "unsupported")
            .ToList();
        var totalBytes = files.Sum(f => f.FileSize);
        log($"[整合包] 下载 {files.Count} 个文件（{totalBytes / 1024 / 1024} MB）...");

        var items = new List<DownloadEngine.BatchItem>();
        foreach (var f in files)
        {
            var dest = Path.GetFullPath(Path.Combine(gameDir, f.Path));
            if (!dest.StartsWith(gameDir))
                continue; // 防路径穿越
            items.Add(new DownloadEngine.BatchItem
            {
                Url = f.Downloads.FirstOrDefault()
                    ?? throw new InvalidOperationException($"{f.Path} 无可用下载链接。"),
                Dest = dest,
                DisplayName = f.Path,
                Sha1 = f.Hashes?.GetValueOrDefault("sha1"),
                Size = f.FileSize,
            });
        }

        // 4 线程并发；进度上报到页面（当前文件 / 完成数 / 总字节）
        var batchProgress = new Progress<DownloadEngine.BatchProgress>(p =>
            progress?.Report(new ModpackInstallProgress(p.CurrentFile, p.DoneFiles, p.TotalFiles, p.DoneBytes, p.TotalBytes)));
        await DownloadEngine.DownloadBatchAsync(_http, items, 4, batchProgress, s => log($"[整合包] {s}"), ct);

        // 3. 安装载器（版本 JSON 落在共享 versions 目录）
        var versionsDir = Path.Combine(launcher.RootPath, "versions");
        var before = Directory.Exists(versionsDir)
            ? Directory.GetDirectories(versionsDir).ToHashSet() : new HashSet<string>();
        var (loader, loaderVer) = GetLoader(index.Dependencies);
        switch (loader)
        {
            case "fabric-loader":
                log($"[整合包] 安装 Fabric Loader {loaderVer}...");
                await new CmlLib.Core.ModLoaders.FabricMC.FabricInstaller(_http)
                    .Install(mcVersion, loaderVer!, new MinecraftPath(launcher.RootPath));
                break;
            case "quilt-loader":
                log($"[整合包] 安装 Quilt Loader {loaderVer}...");
                await new CmlLib.Core.ModLoaders.QuiltMC.QuiltInstaller(_http)
                    .Install(mcVersion, loaderVer!, new MinecraftPath(launcher.RootPath));
                break;
            case "forge":
                log($"[整合包] 安装 Forge {loaderVer}（需要 Java，耗时较长）...");
                await launcher.ForgeInstallAsync(mcVersion, loaderVer!);
                break;
            case "neoforge":
                log($"[整合包] 安装 NeoForge {loaderVer}...");
                await launcher.NeoForgeInstallAsync(mcVersion, loaderVer!);
                break;
            default:
                log("[整合包] 无装载器依赖，纯原版整合包。");
                break;
        }

        // 4. 检测装载器生成的真实版本 ID（如 fabric-loader-1.20.1-0.16.14）
        var newVersion = Directory.Exists(versionsDir)
            ? Directory.GetDirectories(versionsDir).FirstOrDefault(d => !before.Contains(d)) : null;
        var launchVersion = newVersion != null ? Path.GetFileName(newVersion) : versionName;
        log($"[整合包] 完成！在启动页选择版本 [{launchVersion}] 即可游玩。");
        return launchVersion;
    }

}
