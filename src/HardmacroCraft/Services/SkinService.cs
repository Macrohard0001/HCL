using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// 皮肤头像服务（PCL 式账号头像）：
/// - 微软正版：Mojang sessionserver 取 textures 属性（base64）→ SKIN url → 下载皮肤；
/// - 第三方验证站：按 authlib-injector 规范 GET {API Root}/profiles/{uuid} 同样解析；
/// - 离线账号无皮肤，返回 null（UI 回退为字母头像）。
/// 只负责定位并下载皮肤贴图；裁剪头像（8,8–16,16 的 8×8 头部区域）在 UI 层完成
/// （WPF CroppedBitmap），保证本服务可被 net10.0 无头测试直接调用。
/// </summary>
public static class SkinService
{
    /// <summary>账号的皮肤贴图 URL；无皮肤 / 无网络 / 解析失败返回 null。</summary>
    public static async Task<string?> GetSkinUrlAsync(StoredAccount acc, HttpClient http)
    {
        try
        {
            return acc.Type switch
            {
                "microsoft" => await GetMojangSkinUrlAsync(acc.Uuid, http),
                "authlib" => await GetAuthlibSkinUrlAsync(acc.ServerUrl, acc.Uuid, http),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetMojangSkinUrlAsync(string uuid, HttpClient http)
    {
        if (string.IsNullOrWhiteSpace(uuid)) return null;
        var url = $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid.Replace("-", "")}";
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        return ExtractSkinUrl(json);
    }

    private static async Task<string?> GetAuthlibSkinUrlAsync(string apiRoot, string uuid, HttpClient http)
    {
        if (string.IsNullOrWhiteSpace(apiRoot) || string.IsNullOrWhiteSpace(uuid)) return null;
        var root = apiRoot.TrimEnd('/');
        var url = $"{root}/profiles/{uuid}";
        using var resp = await http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var json = await resp.Content.ReadAsStringAsync();
        return ExtractSkinUrl(json);
    }

    /// <summary>从 profile json（Mojang / Yggdrasil 通用格式）提取 SKIN url。</summary>
    public static string? ExtractSkinUrl(string profileJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("properties", out var props)) return null;
            foreach (var p in props.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var n) && n.GetString() == "textures"
                    && p.TryGetProperty("value", out var v))
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(v.GetString() ?? ""));
                    using var tex = JsonDocument.Parse(decoded);
                    if (tex.RootElement.TryGetProperty("textures", out var t)
                        && t.TryGetProperty("SKIN", out var skin)
                        && skin.TryGetProperty("url", out var u))
                        return u.GetString();
                }
            }
        }
        catch { /* base64 / json 损坏 */ }
        return null;
    }

    /// <summary>下载皮肤贴图，返回 PNG 字节；失败返回 null（UI 保持字母头像）。</summary>
    public static async Task<byte[]?> DownloadSkinAsync(string skinUrl, HttpClient http)
    {
        try
        {
            using var resp = await http.GetAsync(skinUrl);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            // 简单 PNG 魔数校验，防止错误内容被 BitmapImage 消费
            if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50) return bytes;
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ==================== 默认头像（PCL 式 Steve / Alex 回退） ====================
    // 原版规则：账号 UUID 的 Java hashCode 为奇数 → Alex（纤细），偶数 → Steve（宽臂）。
    // 无皮肤账号（离线 / 未上传皮肤的正版）的头像即由此选择，与游戏内默认皮肤一致。

    /// <summary>UUID 是否应使用 Alex 默认皮肤（Java UUID.hashCode 奇偶，与原版客户端一致）。</summary>
    public static bool PicksAlexDefault(string? uuid)
    {
        try
        {
            var hex = (uuid ?? "").Replace("-", "");
            if (hex.Length != 32) return false;
            var most = Convert.ToInt64(hex[..16], 16);
            var least = Convert.ToInt64(hex[16..], 16);
            long hilo = most ^ least;
            var hash = (int)(hilo >> 32) ^ (int)hilo; // Java UUID.hashCode()
            return Math.Abs(hash % 2) == 1;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>默认头像资源路径（WPF pack URI 相对路径），PicksAlexDefault ? alex : steve。</summary>
    public static string DefaultFacePath(string? uuid)
        => PicksAlexDefault(uuid) ? "/Assets/alex.png" : "/Assets/steve.png";

    // ==================== 离线皮肤（PCL 式本机注入） ====================
    // 原理与 PCL 一致：离线玩家没有服务端身份，客户端渲染默认皮肤（Steve/Alex）。
    // 启动前把用户 PNG 写入游戏 jar 内的默认皮肤贴图条目，即可"本地伪装"。
    // 仅本机单机可见；正版玩家与皮肤站玩家不受影响（走各自的贴图 URL）。

    /// <summary>jar 内默认皮肤贴图条目：覆盖新旧两代布局，全部替换以保证
    /// 无论客户端按 UUID 选中宽臂（Steve）还是纤细（Alex）模型都能显示自定义皮肤。</summary>
    public static readonly string[] DefaultSkinEntries =
    {
        "textures/entity/player/wide/steve.png", // 1.19.3+ 宽臂默认
        "textures/entity/player/slim/alex.png",  // 1.19.3+ 纤细默认
        "textures/entity/steve.png",             // 1.19.3 之前宽臂默认
        "textures/entity/alex.png",              // 1.19.3 之前纤细默认
    };

    /// <summary>校验皮肤 PNG：PNG 魔数 + 尺寸为 64×64 或 64×32（旧版布局）。</summary>
    public static bool IsSkinPng(byte[]? png)
    {
        if (png == null || png.Length < 24) return false;
        if (png[0] != 0x89 || png[1] != 0x50 || png[2] != 0x4E || png[3] != 0x47) return false;
        // IHDR 宽高位于固定偏移：8(签名)+8(chunk头)+4 → 宽@16 高@20（大端）
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w == 64 && h == 64) || (w == 64 && h == 32);
    }

    /// <summary>
    /// 把自定义皮肤写入游戏 jar 的默认皮肤贴图条目。
    /// 只替换 jar 中实际存在的条目，其余条目原样保留；jar 不存在 / 非 zip 抛异常。
    /// 返回实际替换的条目名列表（用于日志与测试）。
    /// </summary>
    public static List<string> PatchJarDefaultSkin(string jarPath, byte[] skinPng)
    {
        using var fs = new FileStream(jarPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var zip = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Update);
        var patched = new List<string>();
        foreach (var entry in DefaultSkinEntries)
        {
            var existing = zip.GetEntry(entry);
            if (existing == null) continue;
            existing.Delete();
            var created = zip.CreateEntry(entry, System.IO.Compression.CompressionLevel.NoCompression);
            using var os = created.Open();
            os.Write(skinPng, 0, skinPng.Length);
            patched.Add(entry);
        }
        return patched;
    }

    /// <summary>
    /// 解析某版本实际使用的主 jar 路径（client jar）：
    /// versions/&lt;id&gt;/&lt;id&gt;.jar；Forge/OptiFine 等加载器版本通过 inheritsFrom 逐级回溯到根原版 jar。
    /// 本地解析，不联网；找不到返回 null。
    /// </summary>
    public static string? ResolveMainJarPath(string versionsDir, string versionId)
    {
        var cur = versionId;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(cur); i++)
        {
            var jar = Path.Combine(versionsDir, cur, cur + ".jar");
            if (File.Exists(jar)) return jar;
            // 沿 inheritsFrom 链向上找（与 CmlLib ClientFileExtractor 的 MainJarId 行为一致）
            var json = Path.Combine(versionsDir, cur, cur + ".json");
            if (!File.Exists(json)) return null;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                if (!doc.RootElement.TryGetProperty("inheritsFrom", out var p)) return null;
                cur = p.GetString();
            }
            catch { return null; }
        }
        return null;
    }
}
