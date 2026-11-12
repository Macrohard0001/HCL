using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CmlLib.Core.Auth;

namespace HardmacroCraft.Services;

/// <summary>微软正版登录 —— 设备码流程（与 PCL 一致的体验）。</summary>
/// 流程：请求设备码 → 用户打开 aka.ms/RemoteConnect（或 microsoft.com/link）输入验证码
/// → 客户端轮询令牌 → Xbox Live → XSTS → Minecraft → 档案。
public class AuthService
{
    private readonly HttpClient _http = new();

    public record DeviceCodeResult(string DeviceCode, string UserCode, string VerificationUri, int ExpiresIn, int Interval);

    public record LoginResult(string Name, string Uuid, string AccessToken, string RefreshToken);

    private static class Endpoints
    {
        public const string Tenant = "https://login.microsoftonline.com/consumers/oauth2/v2.0";
        public const string XblUser = "https://user.auth.xboxlive.com/user/authenticate";
        public const string Xsts = "https://xsts.auth.xboxlive.com/xsts/authorize";
        public const string McLogin = "https://api.minecraftservices.com/authentication/login_with_xbox";
        public const string McProfile = "https://api.minecraftservices.com/minecraft/profile";
    }

    private const string Scope = "XboxLive.signin offline_access";

    // ---------- 第 1 步：申请设备码 ----------
    public async Task<DeviceCodeResult> RequestDeviceCodeAsync(string clientId, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"{Endpoints.Tenant}/devicecode",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = Scope,
            }), ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"设备码申请失败: {json.GetProperty("error_description").GetString()}");
        return new DeviceCodeResult(
            json.GetProperty("device_code").GetString()!,
            json.GetProperty("user_code").GetString()!,
            json.GetProperty("verification_uri").GetString()!,
            json.GetProperty("expires_in").GetInt32(),
            json.TryGetProperty("interval", out var iv) ? iv.GetInt32() : 5);
    }

    // ---------- 第 2 步：轮询令牌 ----------
    public async Task<LoginResult> PollTokenAsync(string clientId, DeviceCodeResult device,
        CancellationToken ct = default)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(2, device.Interval));
        var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            await Task.Delay(interval, ct);
            var resp = await _http.PostAsync($"{Endpoints.Tenant}/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["device_code"] = device.DeviceCode,
                }), ct);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (json.TryGetProperty("error", out var err))
            {
                var code = err.GetString();
                switch (code)
                {
                    case "authorization_pending": continue;
                    case "slow_down": interval = interval.Add(TimeSpan.FromSeconds(5)); continue;
                    default: throw new Exception($"登录失败: {json.GetProperty("error_description").GetString()}");
                }
            }
            var accessToken = json.GetProperty("access_token").GetString()!;
            var refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : "";
            return await XboxLoginAsync(accessToken, refreshToken, ct);
        }
        throw new OperationCanceledException("登录超时，请重试。");
    }

    // ---------- 用 refresh_token 免验证码续登 ----------
    public async Task<LoginResult> RefreshAsync(string clientId, string refreshToken, CancellationToken ct = default)
    {
        var resp = await _http.PostAsync($"{Endpoints.Tenant}/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["scope"] = Scope,
            }), ct);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!resp.IsSuccessStatusCode)
            throw new Exception("令牌已失效，请重新登录。");
        return await XboxLoginAsync(json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : refreshToken, ct);
    }

    // ---------- Xbox Live → XSTS → Minecraft ----------
    private async Task<LoginResult> XboxLoginAsync(string msAccessToken, string refreshToken, CancellationToken ct)
    {
        // XBL
        var xbl = await _http.PostAsJsonAsync(Endpoints.XblUser, new
        {
            Properties = new { AuthMethod = "RPS", SiteName = "user.auth.xboxlive.com", RpsTicket = "d=" + msAccessToken },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT",
        }, ct);
        xbl.EnsureSuccessStatusCode();
        var xblJson = await xbl.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var userToken = xblJson.GetProperty("Token").GetString()!;

        // XSTS
        var xsts = await _http.PostAsJsonAsync(Endpoints.Xsts, new
        {
            Properties = new { SandboxId = "RETAIL", UserTokens = new[] { userToken } },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType = "JWT",
        }, ct);
        var xstsJson = await xsts.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        if (!xsts.IsSuccessStatusCode)
        {
            var xerr = xstsJson.TryGetProperty("XErr", out var xe) ? xe.GetInt64() : 0;
            var why = xerr switch
            {
                2148916233 => "该微软账号没有 Xbox 档案，请先创建 Xbox 账号。",
                2148916238 => "账号是儿童账号，需要家长在家庭设置中允许游玩。",
                _ => "XSTS 授权失败。",
            };
            throw new Exception(why);
        }
        var xstsToken = xstsJson.GetProperty("Token").GetString()!;
        var uhs = xstsJson.GetProperty("DisplayClaims").GetProperty("xui")[0].GetProperty("uhs").GetString()!;

        // Minecraft
        var mc = await _http.PostAsJsonAsync(Endpoints.McLogin,
            new { identityToken = $"XBL3.0 x={uhs};{xstsToken}" }, ct);
        mc.EnsureSuccessStatusCode();
        var mcJson = await mc.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        var mcToken = mcJson.GetProperty("access_token").GetString()!;

        // Profile
        var pf = await _http.GetAsync(Endpoints.McProfile, ct);
        if (!pf.IsSuccessStatusCode)
            throw new Exception("该微软账号尚未购买 Minecraft: Java Edition。");
        var pfJson = await pf.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return new LoginResult(
            pfJson.GetProperty("name").GetString()!,
            pfJson.GetProperty("id").GetString()!,
            mcToken, refreshToken);
    }

    // ---------- 离线 ----------
    public static MSession OfflineSession(string username)
        => new(username, "0", OfflineUuid(username));

    /// <summary>
    /// 清洗离线玩家名（v0.1.3）：去首尾与内部空白；空或超过 16 字符（Minecraft 上限）
    /// 返回 null。此前直接透传，带空格/超长名字会导致启动参数异常或被服务器拒绝。
    /// </summary>
    public static string? NormalizeOfflineName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var name = string.Concat(input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return name.Length is > 0 and <= 16 ? name : null;
    }

    /// <summary>离线玩家名校验错误类型（登录系统全面检查）。</summary>
    public enum OfflineNameError
    {
        None,
        Empty,        // 没填
        TooShort,     // 少于 3 字符（Java 版官方规则）
        TooLong,      // 超过 16 字符
        InvalidChars, // 含字母 / 数字 / 下划线以外的字符（如中文、空格、符号）
    }

    /// <summary>
    /// 离线玩家名完整校验（登录系统全面检查）：规则与 Java 版官方玩家名一致 ——
    /// 3-16 个字符，仅限字母 / 数字 / 下划线。返回清洗后的名字与错误类型，
    /// Error == None 表示可用。此前 UI 层对非法输入静默丢弃（点确定毫无反应），
    /// 现在把"为什么不行"返回给界面就地提示。
    /// </summary>
    public static (string? Name, OfflineNameError Error) ValidateOfflineName(string? input)
    {
        var name = NormalizeOfflineName(input);
        if (name == null)
            return (null, string.IsNullOrWhiteSpace(input) ? OfflineNameError.Empty : OfflineNameError.TooLong);
        if (name.Length < 3) return (name, OfflineNameError.TooShort);
        foreach (var c in name)
            if (!(char.IsAsciiLetterOrDigit(c) || c == '_'))
                return (name, OfflineNameError.InvalidChars);
        return (name, OfflineNameError.None);
    }

    /// <summary>离线 UUID：MD5("OfflinePlayer:" + name) 的 UUID v3（与官方离线模式一致）。</summary>
    public static string OfflineUuid(string username)
    {
        var bytes = MD5.HashData(System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}";
    }

    public static MSession ToSession(LoginResult r) => new(r.Name, r.AccessToken, r.Uuid);
}
