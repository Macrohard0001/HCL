using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>Modrinth API v2 客户端（搜索 / 版本 / 下载）。</summary>
public class ModrinthService
{
    private readonly HttpClient _http;
    private const string Base = "https://api.modrinth.com/v2";

    public ModrinthService(HttpClient http) => _http = http;

    public record SearchHit(string ProjectId, string Slug, string Title, string Description,
        string ProjectType, long Downloads, string? IconUrl, string[] Versions, string Author,
        DateTimeOffset DateModified, string[] Categories, long Follows, string Source = "modrinth");

    public record ModDependency(string ProjectId, string DepType);
    public record VersionFile(string Url, string Filename, long Size, string? Primary);

    public record ProjectVersion(string Id, string Name, string VersionNumber, string VersionType,
        string[] GameVersions, string[] Loaders, VersionFile[] Files, DateTimeOffset DatePublished,
        ModDependency[] Dependencies);

    /// <summary>
    /// 搜索 Modrinth。index：relevance / downloads / follows / newest / updated；
    /// offset 用于"加载更多"分页；返回命中列表与服务端总结果数。
    /// gameVersion / loader 非空时按版本与加载器过滤（PCL 同款筛选项）。
    /// </summary>
    public async Task<(List<SearchHit> Hits, int Total)> SearchAsync(
        string query, string projectType, string index = "relevance", int offset = 0,
        string? gameVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var inner = new List<string> { $"project_type:{projectType}" };
        if (!string.IsNullOrWhiteSpace(loader) && loader != "any")
            inner.Add($"categories:{loader}");
        if (!string.IsNullOrWhiteSpace(gameVersion))
            inner.Add($"versions:{gameVersion}");
        var facets = Uri.EscapeDataString(
            System.Text.Json.JsonSerializer.Serialize(new[] { inner }));
        var q = Uri.EscapeDataString(query ?? "");
        var url = $"{Base}/search?limit=25&index={index}&offset={offset}&query={q}&facets={facets}";
        var json = await _http.GetFromJsonAsync<JsonElement>(url, ct);
        var list = new List<SearchHit>();
        foreach (var hit in json.GetProperty("hits").EnumerateArray())
        {
            list.Add(new SearchHit(
                hit.GetProperty("project_id").GetString() ?? "",
                hit.GetProperty("slug").GetString() ?? "",
                hit.GetProperty("title").GetString() ?? "",
                hit.GetProperty("description").GetString() ?? "",
                hit.GetProperty("project_type").GetString() ?? "",
                hit.GetProperty("downloads").GetInt64(),
                hit.TryGetProperty("icon_url", out var ic) && ic.ValueKind == JsonValueKind.String ? ic.GetString() : null,
                hit.TryGetProperty("versions", out var vs) ? vs.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : Array.Empty<string>(),
                hit.TryGetProperty("author", out var au) ? au.GetString() ?? "" : "",
                hit.TryGetProperty("date_modified", out var dm) && dm.TryGetDateTimeOffset(out var dmo) ? dmo : default,
                hit.TryGetProperty("categories", out var cs)
                    ? cs.EnumerateArray().Select(c => c.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                    : Array.Empty<string>(),
                hit.TryGetProperty("follows", out var fo) && fo.ValueKind == JsonValueKind.Number ? fo.GetInt64() : 0));
        }
        var total = json.TryGetProperty("total_hits", out var th) && th.TryGetInt32(out var t) ? t : list.Count;
        return (list, total);
    }

    public async Task<List<ProjectVersion>> GetVersionsAsync(string slugOrId, CancellationToken ct = default)
    {
        var json = await _http.GetFromJsonAsync<JsonElement>($"{Base}/project/{slugOrId}/version", ct);
        var list = new List<ProjectVersion>();
        foreach (var v in json.EnumerateArray())
        {
            var files = new List<VersionFile>();
            if (v.TryGetProperty("files", out var fs))
                foreach (var f in fs.EnumerateArray())
                    files.Add(new VersionFile(
                        f.GetProperty("url").GetString() ?? "",
                        f.GetProperty("filename").GetString() ?? "",
                        f.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                        f.TryGetProperty("primary", out var pr) && pr.ValueKind == JsonValueKind.True ? "1" : null));
            var deps = new List<ModDependency>();
            if (v.TryGetProperty("dependencies", out var ds) && ds.ValueKind == JsonValueKind.Array)
                foreach (var d in ds.EnumerateArray())
                    deps.Add(new ModDependency(
                        d.TryGetProperty("project_id", out var pid) ? pid.GetString() ?? "" : "",
                        d.TryGetProperty("dependency_type", out var dt) ? dt.GetString() ?? "" : ""));
            list.Add(new ProjectVersion(
                v.GetProperty("id").GetString() ?? "",
                v.GetProperty("name").GetString() ?? "",
                v.GetProperty("version_number").GetString() ?? "",
                v.TryGetProperty("version_type", out var vt) ? vt.GetString() ?? "" : "",
                v.TryGetProperty("game_versions", out var gv) ? gv.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>(),
                v.TryGetProperty("loaders", out var ld) ? ld.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>(),
                files.ToArray(),
                v.TryGetProperty("date_published", out var dp) && dp.TryGetDateTimeOffset(out var dto) ? dto : DateTimeOffset.MinValue,
                deps.ToArray()));
        }
        return list.OrderByDescending(v => v.DatePublished).ToList();
    }

    /// <summary>
    /// 取某项目对指定游戏版本 / 加载器最合适的文件（PCL 依赖批量下载的核心步骤）：
    /// 过滤版本 → release 优先 → 主文件优先。无匹配返回 null。
    /// </summary>
    public async Task<(ProjectVersion Version, VersionFile File)?> GetLatestCompatibleFileAsync(
        string slugOrId, string? gameVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(slugOrId, ct);
        IEnumerable<ProjectVersion> q = versions;
        if (!string.IsNullOrWhiteSpace(gameVersion))
            q = q.Where(v => v.GameVersions.Contains(gameVersion));
        if (!string.IsNullOrWhiteSpace(loader) && loader != "any")
            q = q.Where(v => v.Loaders.Contains(loader));
        foreach (var v in q.OrderByDescending(v => v.VersionType == "release")
                     .ThenByDescending(v => v.DatePublished))
        {
            var file = v.Files.FirstOrDefault(f => f.Primary != null) ?? v.Files.FirstOrDefault();
            if (file != null) return (v, file);
        }
        return null;
    }

    /// <summary>下载文件（v1.7.0 起走 DownloadEngine：临时文件 + 重试 + 大小校验）。</summary>
    public Task DownloadAsync(string url, string destFile, IProgress<long>? bytes = null, CancellationToken ct = default)
        => DownloadEngine.DownloadFileAsync(_http, url, destFile, sha1: null, expectedSize: 0,
            byteProgress: bytes, ct: ct);
}

/// <summary>
/// Mod 版本选择行：Modrinth 版本与 CurseForge 文件的统一展示模型
/// （供 ModVersionWindow / ModsPage 使用；放在 Services 便于核心测试引用）。
/// </summary>
public sealed class ModVersionRow
{
    public string Title { get; init; } = "";
    /// <summary>release / beta / alpha</summary>
    public string ReleaseType { get; init; } = "release";
    /// <summary>游戏版本 + 加载器 + 日期 + 大小 徽章文本。</summary>
    public List<string> Badges { get; init; } = new();
    public ModrinthService.ProjectVersion? MrVersion { get; init; }
    public ModrinthService.VersionFile? MrFile { get; init; }
    public CurseForgeService.CfFile? Cf { get; init; }

    public string BadgeText => string.Join("  ·  ", Badges);
}
