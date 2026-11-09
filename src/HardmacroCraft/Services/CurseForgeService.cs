using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// CurseForge（Flame）REST API 客户端 —— 第二大 Mod 平台接入（v1.9.0）。
/// - 需要免费 API Key（console.curseforge.com 申请），经 x-api-key 请求头发送；
/// - classId（分区）映射与社区通用值一致：Mod=6 / 资源包=12 / 世界=17 / 光影=6552 / 数据包=6945；
/// - 部分项目 downloadUrl 为空时按官方 CDN 规则回退
///   mediafilez.forgecdn.net/files/{fileId/1000}/{fileId}/{文件名}；
/// - 下载复用 DownloadEngine（临时文件 + 重试 + 校验）。
/// </summary>
public class CurseForgeService
{
    private readonly HttpClient _http;
    public const string ApiBase = "https://api.curseforge.com";
    public const int GameIdMinecraft = 432;

    public CurseForgeService(HttpClient http) => _http = http;

    /// <summary>分区 id 映射（与 CurseForge 官网 / 主流开源启动器一致）。</summary>
    public static int ClassIdFor(string type) => type switch
    {
        "mod" => 6,
        "resourcepack" => 12,
        "shader" => 6552,
        "datapack" => 6945,
        _ => 6,
    };

    /// <summary>排序字段映射：CF 排序枚举 1=Featured 2=Popularity 3=LastUpdated 4=Name 5=Author 6=TotalDownloads。</summary>
    public static int SortFieldFor(string index) => index switch
    {
        "downloads" => 6,
        "updated" => 3,
        "newest" => 3,
        "follows" => 2, // CF 无关注排序，退化为热度
        _ => 2,         // relevance → Popularity
    };

    /// <summary>官方 CDN 回退地址（downloadUrl 缺失时使用）。</summary>
    public static string BuildCdnUrl(int fileId, string fileName)
        => $"https://mediafilez.forgecdn.net/files/{fileId / 1000}/{fileId % 1000}/{fileName}";

    /// <summary>CF 文件发布类型：1=Release 2=Beta 3=Alpha。</summary>
    public static string ReleaseTypeName(int releaseType) => releaseType switch
    {
        1 => "release",
        2 => "beta",
        3 => "alpha",
        _ => "release",
    };

    private HttpRequestMessage Req(HttpMethod method, string url)
    {
        var key = AppSettings.Instance.CurseForgeApiKey?.Trim() ?? "";
        var req = new HttpRequestMessage(method, url);
        if (key.Length > 0) req.Headers.Add("x-api-key", key);
        return req;
    }

    public bool HasKey => !string.IsNullOrWhiteSpace(AppSettings.Instance.CurseForgeApiKey);

    /// <summary>搜索。loader 仅在给出 gameVersion 时才附加（CF 要求 modLoaderType 与 gameVersion 成对）。</summary>
    public async Task<(List<ModrinthService.SearchHit> Hits, int Total)> SearchAsync(
        string query, string type, string index = "relevance", int offset = 0,
        string? gameVersion = null, string? loader = null, CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder()
            .Append($"{ApiBase}/v1/mods/search?gameId={GameIdMinecraft}")
            .Append($"&classId={ClassIdFor(type)}")
            .Append($"&searchFilter={Uri.EscapeDataString(query ?? "")}")
            .Append($"&sortField={SortFieldFor(index)}&sortOrder=desc")
            .Append($"&pageSize=25&index={offset}");
        if (!string.IsNullOrWhiteSpace(gameVersion))
        {
            sb.Append("&gameVersion=").Append(Uri.EscapeDataString(gameVersion));
            if (!string.IsNullOrWhiteSpace(loader) && loader != "any")
                sb.Append("&modLoaderType=").Append(CfLoaderId(loader));
        }
        var url = sb.ToString();

        using var resp = await _http.SendAsync(Req(HttpMethod.Get, url), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        var list = new List<ModrinthService.SearchHit>();
        foreach (var m in json.GetProperty("data").EnumerateArray())
        {
            var authors = m.TryGetProperty("authors", out var aus) && aus.ValueKind == JsonValueKind.Array
                ? string.Join(", ", aus.EnumerateArray()
                    .Select(a => a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    .Where(s => s.Length > 0))
                : "";
            var categories = m.TryGetProperty("categories", out var cs) && cs.ValueKind == JsonValueKind.Array
                ? cs.EnumerateArray()
                    .Select(c => c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "")
                    .Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>();
            string? icon = null;
            if (m.TryGetProperty("logo", out var logo) && logo.ValueKind == JsonValueKind.Object)
            {
                if (logo.TryGetProperty("thumbnailUrl", out var tu) && tu.ValueKind == JsonValueKind.String)
                    icon = tu.GetString();
                if (string.IsNullOrEmpty(icon) && logo.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                    icon = u.GetString();
            }
            var latest = m.TryGetProperty("latestFilesIndexes", out var lfi) && lfi.ValueKind == JsonValueKind.Array
                ? lfi.EnumerateArray()
                    .Select(x => x.TryGetProperty("gameVersion", out var gv) ? gv.GetString() ?? "" : "")
                    .ToArray()
                : Array.Empty<string>();

            list.Add(new ModrinthService.SearchHit(
                ProjectId: m.GetProperty("id").GetInt32().ToString(),
                Slug: m.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "",
                Title: m.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                Description: m.TryGetProperty("summary", out var sm) ? sm.GetString() ?? "" : "",
                ProjectType: type,
                Downloads: m.TryGetProperty("downloadCount", out var dc) ? dc.GetInt64() : 0,
                IconUrl: icon,
                Versions: latest,
                Author: authors,
                DateModified: m.TryGetProperty("dateModified", out var dm) && dm.TryGetDateTimeOffset(out var dmo) ? dmo : default,
                Categories: categories,
                Follows: 0,
                Source: "curseforge"));
        }

        var total = json.TryGetProperty("pagination", out var pg) && pg.TryGetProperty("total", out var t) && t.TryGetInt32(out var ti) ? ti : list.Count;
        return (list, total);
    }

    /// <summary>modLoaderType 枚举：0=Any 1=Forge 4=Fabric 5=Quilt 6=NeoForge。</summary>
    public static int CfLoaderId(string loader) => loader switch
    {
        "forge" => 1,
        "fabric" => 4,
        "quilt" => 5,
        "neoforge" => 6,
        _ => 0,
    };

    public record CfFile(int Id, int ModId, string DisplayName, string FileName, long Size,
        string ReleaseType, string[] GameVersions, DateTimeOffset DateModified, string? DownloadUrl,
        IReadOnlyList<CfDependency> Dependencies);

    /// <summary>CF 文件依赖：relationType 1=Embedded 2=Optional 3=Required 4=Tool 5=Incompatible 6=Include。</summary>
    public record CfDependency(int ModId, int RelationType);

    /// <summary>CF 加载器在 gameVersions 数组里的写法（首字母大写）。</summary>
    public static string CfLoaderName(string loader) => loader switch
    {
        "forge" => "Forge",
        "fabric" => "Fabric",
        "quilt" => "Quilt",
        "neoforge" => "NeoForge",
        _ => "",
    };

    /// <summary>
    /// 从文件列表里挑与游戏版本 / 加载器最兼容的文件（纯函数，便于测试）：
    /// 先按 gameVersion 过滤，再按加载器过滤（文件未标注加载器时视为通用），
    /// release 优先，其后取最新。
    /// </summary>
    public static CfFile? PickCompatibleFile(
        IEnumerable<CfFile> files, string? gameVersion, string? loader)
    {
        var loaderName = loader is null or "any" or "" ? null : CfLoaderName(loader);
        var query = files.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(gameVersion))
            query = query.Where(f => f.GameVersions.Contains(gameVersion));
        if (loaderName != null)
        {
            // 仅保留：明确支持该加载器，或完全未标注加载器的文件
            var loaderNames = new[] { "Forge", "Fabric", "Quilt", "NeoForge" };
            query = query.Where(f =>
                !f.GameVersions.Any(g => loaderNames.Contains(g)) ||
                f.GameVersions.Contains(loaderName));
        }
        var candidates = query.ToList();
        if (candidates.Count == 0) return null;
        return candidates
            .OrderByDescending(f => f.ReleaseType == "release")
            .ThenByDescending(f => f.DateModified)
            .First();
    }

    private static CfFile ParseFile(JsonElement f)
    {
        var id = f.GetProperty("id").GetInt32();
        var name = f.TryGetProperty("fileName", out var fn) ? fn.GetString() ?? "" : "";
        var deps = new List<CfDependency>();
        if (f.TryGetProperty("dependencies", out var ds) && ds.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in ds.EnumerateArray())
            {
                if (d.TryGetProperty("modId", out var dmi) && d.TryGetProperty("relationType", out var drt))
                    deps.Add(new CfDependency(dmi.GetInt32(), drt.GetInt32()));
            }
        }
        return new CfFile(
            Id: id,
            ModId: f.TryGetProperty("modId", out var mi) ? mi.GetInt32() : 0,
            DisplayName: f.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? name : name,
            FileName: name,
            Size: f.TryGetProperty("fileLength", out var fl) ? fl.GetInt64() : 0,
            ReleaseType: ReleaseTypeName(f.TryGetProperty("releaseType", out var rt) ? rt.GetInt32() : 1),
            GameVersions: f.TryGetProperty("gameVersions", out var gvs) && gvs.ValueKind == JsonValueKind.Array
                ? gvs.EnumerateArray().Select(g => g.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                : Array.Empty<string>(),
            DateModified: f.TryGetProperty("fileDate", out var fd) && fd.TryGetDateTimeOffset(out var fdo) ? fdo : default,
            DownloadUrl: f.TryGetProperty("downloadUrl", out var du) && du.ValueKind == JsonValueKind.String ? du.GetString() : null,
            Dependencies: deps);
    }

    /// <summary>某项目的全部文件（默认每页 50，足够普通项目）。</summary>
    public async Task<List<CfFile>> GetFilesAsync(int modId, CancellationToken ct = default)
    {
        using var resp = await _http.SendAsync(
            Req(HttpMethod.Get, $"{ApiBase}/v1/mods/{modId}/files?pageIndex=0&pageSize=50"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var list = new List<CfFile>();
        foreach (var f in json.GetProperty("data").EnumerateArray())
            list.Add(ParseFile(f));
        return list.OrderByDescending(x => x.DateModified).ToList();
    }

    /// <summary>批量取项目信息（id → 名称），用于依赖展示。POST /v1/mods。</summary>
    public async Task<Dictionary<int, (string Name, string Slug)>> GetModsAsync(
        IEnumerable<int> modIds, CancellationToken ct = default)
    {
        var ids = modIds.Distinct().Take(50).ToArray();
        var result = new Dictionary<int, (string, string)>();
        if (ids.Length == 0) return result;
        using var req = Req(HttpMethod.Post, $"{ApiBase}/v1/mods");
        req.Content = JsonContent.Create(new { modIds = ids });
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        foreach (var m in json.GetProperty("data").EnumerateArray())
        {
            if (!m.TryGetProperty("id", out var mid)) continue;
            result[mid.GetInt32()] = (
                m.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "",
                m.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "");
        }
        return result;
    }

    /// <summary>
    /// 取某项目与游戏版本 / 加载器最兼容的文件（用于依赖自动补齐）。
    /// 返回 null 表示没有兼容文件。
    /// </summary>
    public async Task<CfFile?> GetLatestCompatibleFileAsync(
        int modId, string? gameVersion, string? loader, CancellationToken ct = default)
    {
        var files = await GetFilesAsync(modId, ct);
        return PickCompatibleFile(files, gameVersion, loader);
    }


    /// <summary>拉取项目详情并投影为统一模型（GET /v1/mods/{id} + /description，HTML 已转纯文本）。</summary>
    public async Task<ModrinthService.ProjectDetail> GetModDetailAsync(int modId, CancellationToken ct = default)
    {
        using var resp = await _http.SendAsync(
            Req(HttpMethod.Get, $"{ApiBase}/v1/mods/{modId}"), ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var m = json.GetProperty("data");
        var name = m.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : $"#{modId}";
        var slug = m.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "";
        var summary = m.TryGetProperty("summary", out var su) ? su.GetString() ?? "" : "";
        var downloads = m.TryGetProperty("downloadCount", out var dc) && dc.TryGetInt64(out var d) ? d : 0;
        var icon = "";
        if (m.TryGetProperty("logo", out var lg) && lg.TryGetProperty("thumbnailUrl", out var tu)
            && tu.ValueKind == JsonValueKind.String)
            icon = tu.GetString() ?? "";
        var author = "";
        if (m.TryGetProperty("authors", out var aus) && aus.ValueKind == JsonValueKind.Array
            && aus.GetArrayLength() > 0)
            author = aus[0].TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
        var webUrl = "";
        if (m.TryGetProperty("links", out var lk) && lk.TryGetProperty("websiteUrl", out var wu)
            && wu.ValueKind == JsonValueKind.String)
            webUrl = wu.GetString() ?? "";
        var body = summary;
        try
        {
            using var dresp = await _http.SendAsync(
                Req(HttpMethod.Get, $"{ApiBase}/v1/mods/{modId}/description"), ct);
            if (dresp.IsSuccessStatusCode)
            {
                var djson = await dresp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                if (djson.TryGetProperty("data", out var db) && db.ValueKind == JsonValueKind.String)
                {
                    var html = db.GetString() ?? "";
                    if (html.Trim().Length > 0) body = ModrinthService.StripHtml(html);
                }
            }
        }
        catch { /* 详情正文失败时退回简介 */ }
        return new ModrinthService.ProjectDetail(modId.ToString(), slug, name, body,
            icon.Length > 0 ? icon : null, downloads, 0, author, webUrl, "curseforge");
    }

    /// <summary>下载文件：downloadUrl 缺失时回退官方 CDN 规则地址。</summary>
    public Task DownloadAsync(CfFile file, string destFile, IProgress<long>? bytes = null, CancellationToken ct = default)
    {
        var url = !string.IsNullOrEmpty(file.DownloadUrl) ? file.DownloadUrl! : BuildCdnUrl(file.Id, file.FileName);
        return DownloadEngine.DownloadFileAsync(_http, url, destFile, sha1: null, expectedSize: 0,
            byteProgress: bytes, ct: ct);
    }
}
