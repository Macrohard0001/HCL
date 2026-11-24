using System;
using System.Net;
using System.Net.Http;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.0 代理出口（HMCL 同类功能）：按 AppSettings 代理配置构造 SocketsHttpHandler。
/// 只作用于启动器下载管线（Mojang/BMCLAPI/CurseForge/Modrinth 等）；改配置需重启生效。
/// </summary>
public static class ProxyHelper
{
    /// <summary>按设置构造下载管线用的消息处理器（未启用代理 = 原行为不变）。</summary>
    public static SocketsHttpHandler CreateDownloadHandler()
    {
        var h = new SocketsHttpHandler
        {
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            // v0.15.2：连接池提速（对 BMCLAPI 单域名批量下载是瓶颈）——从 MirrorHttpHandler 迁入
            MaxConnectionsPerServer = 128,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        if (TryGetProxy(out var proxy)) { h.Proxy = proxy; h.UseProxy = true; }
        return h;
    }

    /// <summary>设置里的代理是否可用（启用 + 主机非空 + 端口合法）。</summary>
    public static bool TryGetProxy(out WebProxy proxy)
    {
        proxy = new WebProxy();
        var s = AppSettings.Instance;
        if (!s.ProxyEnabled || string.IsNullOrWhiteSpace(s.ProxyHost)) return false;
        if (s.ProxyPort < 1 || s.ProxyPort > 65535) return false;
        var type = string.Equals(s.ProxyType, "socks5", StringComparison.OrdinalIgnoreCase) ? "socks5" : "http";
        proxy = new WebProxy($"{type}://{s.ProxyHost}:{s.ProxyPort}");
        return true;
    }
}
