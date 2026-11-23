using System.Collections.ObjectModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>局域网房间（本机开放的或发现的对等端）。</summary>
public class LanRoom
{
    public string RoomName { get; set; } = "";
    public string RoomCode { get; set; } = "";
    public string HostAddress { get; set; } = ""; // 不含端口的 IP / 主机名
    public int GamePort { get; set; }
    public DateTime LastSeen { get; set; } = DateTime.Now;

    /// <summary>MC「直接连接」用地址（host:port）。</summary>
    public string JoinAddress => $"{HostAddress}:{GamePort}";
}

/// <summary>
/// v0.15.9：联机服务（P2P 风格）。当前能力：
/// - 局域网房间发现：UDP 广播（端口 44490，2s 信标），开放方广播房间码，
///   同网启动器自动出现在「局域网房间」列表；
/// - 加入：按房间码解析地址，或直接输入 host:port，复制后在 MC「直接连接」；
/// - 中转预留：RelayEndpoint 配置就绪后接入 TCP 隧道（公网互通）。
/// 不改游戏进程 / 不注入网络栈 —— 走的是 Minecraft 原生「对局域网开放」端口。
/// </summary>
public sealed class MultiplayerService
{
    public static MultiplayerService Instance { get; } = new();

    public const int DiscoveryPort = 44490;
    private const string Magic = "hcl-room-v1";
    private static readonly TimeSpan RoomTtl = TimeSpan.FromSeconds(9); // 3 个信标周期

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private readonly object _lock = new();

    /// <summary>发现的房间列表（UI 绑定；后台线程增删，UI 经 RoomsChanged 事件后自行刷新）。</summary>
    public ObservableCollection<LanRoom> Rooms { get; } = new();
    /// <summary>房间列表变化（任意线程触发，订阅方自行调度到 UI 线程）。</summary>
    public event Action? RoomsChanged;

    public bool Hosting { get; private set; }
    public string HostingRoomName { get; private set; } = "";
    public string HostingRoomCode { get; private set; } = "";
    public int HostingGamePort { get; private set; }
    public bool Discovering { get; private set; }

    /// <summary>中转是否已配置（服务端就绪后填 RelayEndpoint 即自动启用入口）。</summary>
    public bool RelayConfigured =>
        !string.IsNullOrWhiteSpace(AppSettings.Instance.RelayEndpoint);

    /// <summary>生成 5 位房间码（去掉易混淆的 0/O/1/I）。</summary>
    public static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<char> code = stackalloc char[5];
        for (var i = 0; i < 5; i++)
            code[i] = alphabet[Random.Shared.Next(alphabet.Length)];
        return new string(code);
    }

    /// <summary>
    /// 开放本机世界：gamePort 为 Minecraft「对局域网开放」给出的端口。
    /// 返回 false = 本机 44490 被占（另一实例已在开放/发现，端口冲突）。
    /// </summary>
    public bool StartHosting(int gamePort, string roomName)
    {
        // 44490 是单套接字：既收（发现）也发（信标）。已在发现中则复用现成套接字，
        // 只补启信标循环 —— 否则重复绑定会撞 ReuseAddress / ExclusiveAddressUse。
        var wasDiscovering = Discovering;
        Hosting = true;
        HostingRoomName = string.IsNullOrWhiteSpace(roomName) ? "我的世界" : roomName.Trim();
        HostingRoomCode = NewRoomCode();
        HostingGamePort = gamePort;
        if (wasDiscovering)
        {
            _ = AnnounceLoopAsync(_cts!.Token);
            Services.BootLogger.Info(
                $"联机：开放房间 {HostingRoomName}（{HostingRoomCode}，端口 {gamePort}）");
            return true;
        }
        try
        {
            _cts = new CancellationTokenSource();
            Discovering = true;
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.ExclusiveAddressUse = false;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            StartUdpLoops();
            Services.BootLogger.Info(
                $"联机：开放房间 {HostingRoomName}（{HostingRoomCode}，端口 {gamePort}）");
            return true;
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("联机：开放房间失败（端口可能被占用）: " + ex.Message);
            CleanupUdp();
            Hosting = false;
            Discovering = false;
            return false;
        }
    }

    public void StopHosting()
    {
        if (!Hosting) return;
        Hosting = false;
        HostingRoomName = HostingRoomCode = "";
        HostingGamePort = 0;
        // 仍保持发现循环运行（页面上开放与发现互不排斥）
        Services.BootLogger.Info("联机：房间已关闭");
    }

    /// <summary>开始局域网房间发现（重复调用无副作用）。</summary>
    public bool StartDiscovery()
    {
        if (Discovering) return true;
        try
        {
            _cts = new CancellationTokenSource();
            Discovering = true;
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.ExclusiveAddressUse = false;
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            StartUdpLoops();
            Services.BootLogger.Info($"联机：开始局域网房间发现（UDP {DiscoveryPort}）");
            return true;
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("联机：房间发现启动失败: " + ex.Message);
            CleanupUdp();
            Discovering = false;
            return false;
        }
    }

    public void StopDiscovery()
    {
        Discovering = false;
        CleanupUdp();
        lock (_lock) Rooms.Clear();
        RoomsChanged?.Invoke();
    }

    /// <summary>解析加入目标：房间码（在已发现列表内）或 host:port；返回 MC 直接连接地址，未知返回 null。</summary>
    public string? ResolveJoinTarget(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return null;
        if (input.Contains(':')) return input; // host:port 直连透传（含 IPv6 [..]:port）
        // 5 位房间码 → 查发现列表（本机开放也算，返回 127.0.0.1）
        lock (_lock)
        {
            var hit = Rooms.FirstOrDefault(r =>
                string.Equals(r.RoomCode, input, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit.JoinAddress;
        }
        if (Hosting && string.Equals(HostingRoomCode, input, StringComparison.OrdinalIgnoreCase))
            return $"127.0.0.1:{HostingGamePort}";
        return null;
    }

    /// <summary>把房间码 / 地址复制到剪贴板的统一入口（返回实际地址，null=解析失败）。</summary>
    public string? CopyJoinAddress(string input)
    {
        var address = ResolveJoinTarget(input);
        if (address != null) System.Windows.Clipboard.SetText(address);
        return address;
    }

    // ===== 内部：UDP 信标广播 + 接收解析 =====

    private void StartUdpLoops()
    {
        var token = _cts!.Token;
        var announce = Hosting
            ? AnnounceLoopAsync(token)
            : Task.CompletedTask;
        var receive = ReceiveLoopAsync(token);
        var prune = PruneLoopAsync(token);
        _ = Task.WhenAll(announce, receive, prune).ContinueWith(t =>
        {
            if (t.IsFaulted)
                Services.BootLogger.Error("联机：UDP 循环异常退出 " + t.Exception?.Message);
        }, TaskScheduler.Default);
    }

    private async Task AnnounceLoopAsync(CancellationToken token)
    {
        // 本机所有 IPv4 地址都发一份（多网卡环境任一路可达即可）
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!token.IsCancellationRequested && Hosting)
        {
            var payload = JsonSerializer.Serialize(new
            {
                magic = Magic,
                code = HostingRoomCode,
                name = HostingRoomName,
                port = HostingGamePort,
            });
            var bytes = Encoding.UTF8.GetBytes(payload);
            foreach (var ip in GetLocalIPv4Addresses())
            {
                try
                {
                    await _udp!.SendAsync(bytes, new IPEndPoint(ip, DiscoveryPort), token);
                }
                catch (SocketException) { /* 单网卡广播失败不致命 */ }
                catch (ObjectDisposedException) { return; }
            }
            await timer.WaitForNextTickAsync(token);
        }
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up
                    || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                    continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(addr.Address);
            }
        }
        catch { }
        if (list.Count == 0) list.Add(IPAddress.Broadcast); // 兜底：全网广播
        return list;
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udp != null)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(token);
            }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { continue; } // 网络抖动继续收
            catch (ObjectDisposedException) { return; }
            try
            {
                var doc = JsonDocument.Parse(result.Buffer);
                var root = doc.RootElement;
                if (!root.TryGetProperty("magic", out var m) || m.GetString() != Magic) continue;
                var code = root.TryGetProperty("code", out var c) ? c.GetString() ?? "" : "";
                var name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var port = root.TryGetProperty("port", out var p) && p.TryGetInt32(out var pv) ? pv : 0;
                if (port <= 0 || code.Length == 0) continue;
                // 本机开放房间不进发现列表（在「开放状态」卡片展示）
                if (Hosting && code == HostingRoomCode) continue;
                var sender = result.RemoteEndPoint.Address;
                // IPv6 链路本地地址直接连接不可用，跳过
                if (sender.AddressFamily != AddressFamily.InterNetwork) continue;
                var address = sender.Equals(IPAddress.Loopback)
                    ? "127.0.0.1" : sender.ToString();
                lock (_lock)
                {
                    var existing = Rooms.FirstOrDefault(r => r.RoomCode == code);
                    if (existing != null)
                    {
                        existing.LastSeen = DateTime.Now;
                        existing.GamePort = port;
                    }
                    else
                    {
                        Rooms.Add(new LanRoom
                        {
                            RoomCode = code, RoomName = name, HostAddress = address, GamePort = port,
                        });
                        Services.BootLogger.Info($"联机：发现房间 {name}（{code}，{address}:{port}）");
                        RoomsChanged?.Invoke();
                    }
                }
            }
            catch (JsonException) { /* 非本协议流量，忽略 */ }
            catch (Exception ex)
            {
                Services.BootLogger.Error("联机：信标解析失败: " + ex.Message);
            }
        }
    }

    private async Task PruneLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (!token.IsCancellationRequested)
        {
            await timer.WaitForNextTickAsync(token);
            bool changed = false;
            lock (_lock)
            {
                var stale = Rooms.Where(r => DateTime.Now - r.LastSeen > RoomTtl).ToList();
                foreach (var room in stale) { Rooms.Remove(room); changed = true; }
            }
            if (changed) RoomsChanged?.Invoke();
        }
    }

    private void CleanupUdp()
    {
        try { _cts?.Cancel(); } catch { }
        try { _udp?.Close(); } catch { }
        _udp = null;
        _cts?.Dispose();
        _cts = null;
    }
}
