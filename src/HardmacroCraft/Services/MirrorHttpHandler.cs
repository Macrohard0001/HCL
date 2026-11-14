using System.Net.Http;

namespace HardmacroCraft.Services;

/// <summary>
/// BMCLAPI 镜像处理器：把 Mojang 官方下载域名重写到国内镜像，
/// 覆盖元数据 / 库 / 资源 / 启动器Manifest 等全部域名。
/// </summary>
public class MirrorHttpHandler : DelegatingHandler
{
    private static readonly Dictionary<string, (string Host, string PathPrefix)> Map = new()
    {
        ["launchermeta.mojang.com"] = ("bmclapi2.bangbang93.com", ""),
        ["piston-meta.mojang.com"] = ("bmclapi2.bangbang93.com", ""),
        ["piston-data.mojang.com"] = ("bmclapi2.bangbang93.com", ""),
        ["launcher.mojang.com"] = ("bmclapi2.bangbang93.com", ""),
        ["libraries.minecraft.net"] = ("bmclapi2.bangbang93.com", "/maven"),
        ["resources.download.minecraft.net"] = ("bmclapi2.bangbang93.com", "/assets"),
        // Forge（fix3：国内直连 maven.minecraftforge.net 常超时，"模组加载器获取失败"根因之一；
        // files.minecraftforge.net 的 HTML 列表页 BMCLAPI 不镜像，保持直连）
        ["maven.minecraftforge.net"] = ("bmclapi2.bangbang93.com", "/maven"),
    };

    public MirrorHttpHandler() : base(new SocketsHttpHandler
    {
        UseCookies = false,
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
    }

    /// <summary>
    /// v0.1.14 下载源方案（对齐 PCL「下载源」选择）：
    /// "auto"（默认）= 走镜像，连接失败自动回退官方源直连；
    /// "mirror" = 强制镜像；"official" = 强制官方直连。
    /// 此前设置页的镜像开关从未接入本处理器（恒镜像）—— 现在随设置实时生效。
    /// </summary>
    public string Mode { get; set; } = "auto";

    /// <summary>旧布尔开关兼容（镜像开 = auto，关 = official）。</summary>
    public bool UseMirror
    {
        get => Mode != "official";
        set => Mode = value ? "auto" : "official";
    }

    /// <summary>判断某 URL 是否会被镜像重写（纯规则，进测试）。</summary>
    public static bool IsMirrorable(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        return u.Host.EndsWith("mojang.com", StringComparison.OrdinalIgnoreCase) || Map.ContainsKey(u.Host);
    }

    /// <summary>重写规则（纯函数，进测试）：返回镜像 URL；不适用返回 null。</summary>
    public static string? Rewrite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (!Map.TryGetValue(uri.Host, out var target)) return null;
        var prefix = target.PathPrefix;
        var path = string.IsNullOrEmpty(prefix) ? uri.PathAndQuery : prefix + uri.PathAndQuery;
        var b = new UriBuilder(uri)
        {
            Host = target.Host,
            Path = "",
            Query = "",
        };
        b.Path = path.StartsWith('/') ? path : "/" + path;
        return b.Uri.ToString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var original = request.RequestUri;
        // 强制官方：跳过重写
        if (Mode == "official")
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        var rewritten = original != null ? Rewrite(original.ToString()) : null;
        if (rewritten == null)
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        request.RequestUri = new Uri(rewritten);
        if (Mode == "mirror")
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // auto：镜像连接失败（DNS / 超时 / 5xx）时回退官方源重试一次；
        // 响应已开始流式传输后的中断由下载引擎的重试逻辑负责（此处不重复处理）
        try
        {
            var resp = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)resp.StatusCode >= 500)
            {
                resp.Dispose();
                return await SendOriginalAsync(original, request, cancellationToken).ConfigureAwait(false);
            }
            return resp;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
                                   && ex is HttpRequestException
                                   or System.Net.Sockets.SocketException
                                   or InvalidOperationException)
        {
            return await SendOriginalAsync(original, request, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendOriginalAsync(
        Uri? original, HttpRequestMessage request, CancellationToken ct)
    {
        if (original == null) throw new HttpRequestException("镜像与官方源均不可用");
        var retry = new HttpRequestMessage(request.Method, original)
        {
            Content = request.Content,
        };
        return await base.SendAsync(retry, ct).ConfigureAwait(false);
    }
}
