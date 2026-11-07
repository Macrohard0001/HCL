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

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri != null && uri.Host.EndsWith("mojang.com", StringComparison.OrdinalIgnoreCase) ||
            uri != null && Map.ContainsKey(uri.Host))
        {
            if (uri != null && Map.TryGetValue(uri.Host, out var target))
            {
                var prefix = target.PathPrefix;
                var path = string.IsNullOrEmpty(prefix)
                    ? uri.PathAndQuery
                    : prefix + uri.PathAndQuery;
                var b = new UriBuilder(uri)
                {
                    Host = target.Host,
                    Path = "",
                    Query = "",
                };
                b.Path = path.StartsWith('/') ? path : "/" + path;
                request.RequestUri = b.Uri;
            }
        }
        return base.SendAsync(request, cancellationToken);
    }
}
