using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// 第三方验证站登录（authlib-injector / Yggdrasil 协议，与 PCL 的“第三方登录”一致）。
/// 支持 LittleSkin 等任意兼容皮肤站：
///   1. 用户输入站点根域名 → 通过响应头 X-Authlib-Injector-API-Location 自动发现 API Root；
///   2. POST /authserver/authenticate 登录，得到 accessToken + 角色档案；
///   3. 启动游戏前自动下载 authlib-injector.jar（BMCLAPI 镜像，官方源兜底，SHA-256 校验），
///      以 -javaagent 注入游戏进程完成服务端身份接管。
/// </summary>
public class AuthlibService
{
    private readonly HttpClient _http;

    public AuthlibService(HttpClient http) => _http = http;

    /// <summary>对外暴露 HttpClient（下载 agent jar 复用同一管道）。</summary>
    public HttpClient Http => _http;

    /// <summary>登录结果。AllProfiles 为该账号下全部可用角色（v0.1.12 多角色直切）。</summary>
    public record AuthlibLogin(string Name, string Uuid, string AccessToken, string ClientToken, string ApiRoot,
        IReadOnlyList<StoredProfile>? AllProfiles = null);

    // ---------------- API Root 自动发现 ----------------

    /// <summary>把用户输入（littleskin.cn / https://...）规范化为 Yggdrasil API Root。</summary>
    public async Task<string> ResolveApiRootAsync(string input, Action<string>? log = null)
    {
        input = input.Trim().TrimEnd('/');
        if (input.Length == 0)
            throw new ArgumentException("请输入验证服务器地址。");
        if (!input.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            input = "https://" + input;

        // 已经是 API Root 形式的直接返回
        var frag = new Uri(input).AbsolutePath;
        if (frag.Contains("yggdrasil", StringComparison.OrdinalIgnoreCase) || frag.EndsWith("/api"))
            return input;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, input);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var resp = await _http.SendAsync(req, timeout.Token);
            // 规范要求：站点根页面通过该响应头告知真正的 API 位置
            if (resp.Headers.TryGetValues("X-Authlib-Injector-API-Location", out var vals))
            {
                var loc = vals.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(loc))
                {
                    var root = new Uri(new Uri(input), loc).ToString().TrimEnd('/');
                    log?.Invoke($"已发现 API Root: {root}");
                    return root;
                }
            }
            // 有些站点直接在根路径提供 API（返回 JSON meta）
            var ct = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (ct.Contains("json"))
                return input;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // v0.1.3：此前吞掉异常继续拿输入地址登录，最终报出的错误让人摸不着头脑；
            // 连不上就是连不上，直接给出明确原因
            throw new Exception($"无法连接验证站 {input}：{ex.Message}（请检查地址、网络 / 代理）");
        }
        catch (Exception ex)
        {
            log?.Invoke($"自动发现 API Root 失败（将直接使用输入地址）: {ex.Message}");
        }
        return input;
    }

    // ---------------- 登录 / 刷新 / 校验 ----------------

    private static string NewClientToken() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// 统一读取 Yggdrasil 响应（v0.1.3 修复"登录界面报错"）：
    /// - 网络失败 / 超时 → 明确的中文提示（此前抛 TaskCanceledException 等原始异常）；
    /// - 非 JSON 响应（CDN 拦截页 / 空响应）→ 展示 HTTP 码与内容摘要，而不是 JSON 解析异常；
    /// - 业务错误 → 优先取 errorMessage。
    /// </summary>
    public static async Task<JsonElement> ReadYggdrasilAsync(HttpResponseMessage resp, string action,
        CancellationToken ct = default)
    {
        string body;
        try { body = await resp.Content.ReadAsStringAsync(ct); }
        catch (Exception ex)
        {
            throw new Exception($"{action}失败：读取响应出错（{ex.Message}）。");
        }
        if (string.IsNullOrWhiteSpace(body))
            throw new Exception($"{action}失败：服务器返回空响应（HTTP {(int)resp.StatusCode}）。");
        JsonElement json;
        try
        {
            using var doc = JsonDocument.Parse(body);
            json = doc.RootElement.Clone();
        }
        catch
        {
            var snippet = body.Length > 120 ? body[..120] + "…" : body;
            throw new Exception(
                $"{action}失败：服务器返回了无法解析的内容（HTTP {(int)resp.StatusCode}）：{snippet}");
        }
        if (!resp.IsSuccessStatusCode)
        {
            var msg = json.TryGetProperty("errorMessage", out var em) ? em.GetString() : null;
            msg ??= json.TryGetProperty("error", out var er) ? er.GetString() : null;
            throw new Exception(msg ?? $"{action}失败（HTTP {(int)resp.StatusCode}）。");
        }
        return json;
    }

    /// <summary>带 20 秒超时与友好网络错误提示的 POST（v0.1.3：此前默认 100 秒才超时且报原始异常）。</summary>
    private async Task<HttpResponseMessage> PostJsonAsync(string url, object body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            return await _http.PostAsJsonAsync(url, body, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new Exception("连接验证站超时（20 秒）。请检查网络 / 代理，或稍后重试。");
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"无法连接验证站：{ex.Message}（请检查站点地址、代理或防火墙设置）");
        }
    }

    /// <summary>
    /// 账号密码登录。
    /// 若无 selectedProfile：单角色自动绑定；多角色通过 selectProfile 回调让用户选择
    /// （PCL 同款体验：登录后弹出角色选择）。selectProfile 入参为可选角色数组，返回选中项。
    /// </summary>
    public async Task<AuthlibLogin> AuthenticateAsync(string apiRoot, string user, string password,
        Func<IReadOnlyList<(string Id, string Name)>, Task<(string Id, string Name)>>? selectProfile = null,
        CancellationToken ct = default)
    {
        var clientToken = NewClientToken();
        using var resp = await PostJsonAsync($"{apiRoot}/authserver/authenticate", new
        {
            agent = new { name = "Minecraft", version = 1 }, // Yggdrasil 规范要求固定为 "Minecraft"
            username = user,
            password,
            clientToken,
            requestUser = true,
        }, ct);
        var json = await ReadYggdrasilAsync(resp, "登录", ct);
        var accessToken = json.GetProperty("accessToken").GetString()!;
        // v0.1.12 多角色直切：登录响应里的全部可用角色随结果一并返回，
        // 账号管理可凭同一 accessToken 直接 refresh 绑定其他角色（无需重新输密码）
        var allProfiles = ParseProfiles(json);

        if (json.TryGetProperty("selectedProfile", out var sp) && sp.ValueKind == JsonValueKind.Object)
        {
            return FromProfile(sp, accessToken, clientToken, apiRoot) with { AllProfiles = allProfiles };
        }

        if (json.TryGetProperty("availableProfiles", out var ap) && ap.ValueKind == JsonValueKind.Array
            && ap.GetArrayLength() > 0)
        {
            var profiles = ap.EnumerateArray()
                .Where(p => p.ValueKind == JsonValueKind.Object)
                .Select(p => (Id: p.GetProperty("id").GetString() ?? "",
                              Name: p.GetProperty("name").GetString() ?? ""))
                .Where(p => p.Id.Length > 0)
                .ToList();
            if (profiles.Count == 0)
                throw new Exception("该账号下没有可用角色，请先在验证站创建角色。");

            // 单角色：直接绑定；多角色：交由调用方选择（无回调时默认第一个，与旧版行为兼容）
            var chosen = profiles.Count == 1
                ? profiles[0]
                : selectProfile != null ? await selectProfile(profiles) : profiles[0];
            var profileJson = JsonSerializer.SerializeToElement(new { id = chosen.Id, name = chosen.Name });
            var refreshed = await RefreshAsync(apiRoot, accessToken, clientToken, profileJson);
            return refreshed with { AllProfiles = allProfiles };
        }
        throw new Exception("该账号下没有可用角色，请先在验证站创建角色。");
    }

    /// <summary>v0.1.12：从 Yggdrasil 响应解析 availableProfiles（角色档案列表）。</summary>
    private static List<StoredProfile> ParseProfiles(JsonElement json)
    {
        var list = new List<StoredProfile>();
        if (json.TryGetProperty("availableProfiles", out var ap) && ap.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in ap.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.Object) continue;
                var id = p.TryGetProperty("id", out var pi) ? pi.GetString() : null;
                var nm = p.TryGetProperty("name", out var pn) ? pn.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(nm))
                    list.Add(new StoredProfile(id, nm));
            }
        }
        return list;
    }

    // ---------------- 站点 meta（注册链接等） ----------------

    /// <summary>读取验证站 meta；成功返回 (serverName, registerUrl)，失败返回 ("", "")。</summary>
    public async Task<(string ServerName, string RegisterUrl)> GetMetaAsync(string apiRoot)
    {
        try
        {
            var j = await _http.GetFromJsonAsync<JsonElement>($"{apiRoot}/");
            var meta = j.GetProperty("meta");
            var serverName = meta.TryGetProperty("serverName", out var sn) ? sn.GetString() ?? "" : "";
            var register = meta.TryGetProperty("links", out var links) && links.TryGetProperty("register", out var reg)
                ? reg.GetString() ?? "" : "";
            return (serverName, register);
        }
        catch { return ("", ""); }
    }

    /// <summary>刷新令牌（启动前保活）。传 profile 则同时绑定角色。
    /// 带 20 秒超时（v0.1.12 启动卡住修复：此前无超时，验证站不可达时启动流程干等）。</summary>
    public async Task<AuthlibLogin> RefreshAsync(string apiRoot, string accessToken,
        string clientToken, JsonElement? profile)
    {
        object body = profile is { ValueKind: JsonValueKind.Object } p
            ? new { accessToken, clientToken, requestUser = true, selectedProfile = p }
            : new { accessToken, clientToken, requestUser = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var resp = await PostJsonAsync($"{apiRoot}/authserver/refresh", body, timeout.Token);
        var json = await ReadYggdrasilAsync(resp, "令牌续期");
        var newToken = json.GetProperty("accessToken").GetString()!;
        if (json.TryGetProperty("selectedProfile", out var sp) && sp.ValueKind == JsonValueKind.Object)
            return FromProfile(sp, newToken, clientToken, apiRoot);
        // 部分实现未传 selectedProfile 时只回 accessToken：令牌已续期成功，角色沿用启动时传入的
        if (profile is { ValueKind: JsonValueKind.Object })
            return FromProfile(profile.Value, newToken, clientToken, apiRoot);
        throw new Exception("令牌已续期，但服务端未返回角色信息，请重新登录。");
    }

    /// <summary>校验令牌是否仍有效（不改变状态）。带 15 秒超时 —— 此前无超时，
    /// 启动阶段碰上验证站不可达时会长时间停在“正在检查第三方令牌”（v0.1.12 启动卡住修复）。</summary>
    public async Task<bool> ValidateAsync(string apiRoot, string accessToken, string clientToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var resp = await _http.PostAsJsonAsync($"{apiRoot}/authserver/validate",
                new { accessToken, clientToken }, timeout.Token);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static AuthlibLogin FromProfile(JsonElement profile, string accessToken,
        string clientToken, string apiRoot)
        => new(
            profile.GetProperty("name").GetString()!,
            profile.GetProperty("id").GetString()!,
            accessToken, clientToken, apiRoot);

    // ---------------- authlib-injector.jar 获取 ----------------

    private const string MirrorLatest = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json";
    private const string OfficialLatest = "https://authlib-injector.yushi.moe/artifact/latest.json";

    private record ArtifactInfo(int BuildNumber, string Version, string DownloadUrl, string Sha256);

    private static async Task<ArtifactInfo> FetchLatestAsync(HttpClient http)
    {
        Exception? last = null;
        foreach (var url in new[] { MirrorLatest, OfficialLatest })
        {
            try
            {
                var j = await http.GetFromJsonAsync<JsonElement>(url);
                return new ArtifactInfo(
                    j.GetProperty("build_number").GetInt32(),
                    j.GetProperty("version").GetString()!,
                    j.GetProperty("download_url").GetString()!,
                    j.GetProperty("checksums").GetProperty("sha256").GetString()!);
            }
            catch (Exception ex) { last = ex; }
        }
        throw new Exception("无法获取 authlib-injector 版本信息（镜像与官方源均失败）。" + last?.Message);
    }

    /// <summary>
    /// 确保 authlib-injector.jar 已下载到 config/ 目录（幂等，SHA-256 校验）。
    /// 返回 jar 的绝对路径。
    /// </summary>
    public static async Task<string> EnsureAgentJarAsync(string configDir, HttpClient http, Action<string>? log = null)
    {
        Directory.CreateDirectory(configDir);
        var jarPath = Path.Combine(configDir, "authlib-injector.jar");
        var info = await FetchLatestAsync(http);

        if (File.Exists(jarPath))
        {
            var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(jarPath))).ToLowerInvariant();
            if (hash == info.Sha256)
            {
                log?.Invoke($"authlib-injector {info.Version} 已是最新。");
                return jarPath;
            }
            log?.Invoke($"authlib-injector 有新版本 {info.Version}，重新下载...");
        }
        else
        {
            log?.Invoke($"下载 authlib-injector {info.Version}...");
        }

        var tmp = jarPath + ".tmp";
        using (var resp = await http.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(tmp);
            await src.CopyToAsync(dst);
        }
        // 校验
        var got = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(tmp))).ToLowerInvariant();
        if (got != info.Sha256)
        {
            File.Delete(tmp);
            throw new Exception("authlib-injector.jar 校验失败（SHA-256 不匹配）。");
        }
        File.Move(tmp, jarPath, overwrite: true);
        log?.Invoke($"authlib-injector {info.Version} 就绪。");
        return jarPath;
    }
}
