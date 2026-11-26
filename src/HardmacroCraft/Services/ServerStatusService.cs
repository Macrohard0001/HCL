using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.7：Minecraft 服务器状态查询（Server List Ping，TCP 1.7+ 协议）——
/// 输入 host:port → MOTD / 版本 / 在线人数 / 延迟 / 图标。
/// 协议：握手包（ nextState=1 ）→ 状态请求 → 状态响应（JSON）。
/// VarInt 编解码与 JSON 解析为纯函数，headless 可测；TCP 部分带超时与取消。
/// </summary>
public static class ServerStatusService
{
    public sealed record ServerStatus(
        string Motd, string Version, int PlayersOnline, int PlayersMax,
        int LatencyMs, string? FaviconBase64, string? PlayerSample);

    /// <summary>查询服务器状态（5s 连接 / 5s 响应超时）。</summary>
    public static async Task<ServerStatus> QueryAsync(string host, ushort port, CancellationToken ct = default)
    {
        using var client = new System.Net.Sockets.TcpClient();
        var connectTask = client.ConnectAsync(host, port, ct).AsTask();
        var timeout = Task.Delay(5000, ct);
        if (await Task.WhenAny(connectTask, timeout) != connectTask)
            throw new TimeoutException("连接超时（5 秒）——检查地址或服务器是否在线。");
        await connectTask; // 传播连接异常（DNS 失败 / 拒绝连接等）

        using var stream = client.GetStream();
        stream.ReadTimeout = 5000;
        stream.WriteTimeout = 5000;
        var latency = System.Diagnostics.Stopwatch.StartNew();

        // 握手：protocol=-1（状态查询专用）、nextState=1
        var hostBytes = Encoding.UTF8.GetBytes(host);
        var handshake = new byte[]
        {
            0x00, // packet id
        }
        .Concat(WriteVarInt(-1))
        .Concat(WriteVarInt(hostBytes.Length)).Concat(hostBytes)
        .Concat(BitConverter.GetBytes((ushort)port))
        .Concat(WriteVarInt(1))
        .ToArray();
        await stream.WriteAsync(ConcatFrames(handshake), ct);

        // 状态请求：空包 0x01
        await stream.WriteAsync(new byte[] { 0x01, 0x00 }, ct);
        await stream.FlushAsync(ct);

        // 响应：VarInt 总长 → VarInt 0x00 → VarInt 字符串长 → JSON
        var frameLen = ReadVarInt(stream);
        var packetId = ReadVarInt(stream);
        if (packetId != 0x00) throw new InvalidOperationException("服务器响应了非状态包（协议异常）。");
        var strLen = ReadVarInt(stream);
        var buf = new byte[strLen];
        await ReadExactlyAsync(stream, buf, ct);
        latency.Stop();

        var status = ParseStatusJson(Encoding.UTF8.GetString(buf));
        return status with { LatencyMs = (int)latency.ElapsedMilliseconds };
    }

    /// <summary>解析状态 JSON（description 兼容字符串与 chat 组件对象两种形态）。</summary>
    public static ServerStatus ParseStatusJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var motd = FlattenChat(root.TryGetProperty("description", out var desc) ? desc : default);
        var version = "";
        if (root.TryGetProperty("version", out var ver) && ver.TryGetProperty("name", out var vn))
            version = vn.GetString() ?? "";
        int online = 0, max = 0;
        string? sample = null;
        if (root.TryGetProperty("players", out var players))
        {
            if (players.TryGetProperty("online", out var o)) online = o.GetInt32();
            if (players.TryGetProperty("max", out var m)) max = m.GetInt32();
            if (players.TryGetProperty("sample", out var s) && s.ValueKind == JsonValueKind.Array)
                sample = string.Join(", ", s.EnumerateArray()
                    .Where(e => e.TryGetProperty("name", out var n))
                    .Select(e => e.GetProperty("name").GetString()));
        }
        string? favicon = root.TryGetProperty("favicon", out var f) ? f.GetString() : null;
        return new ServerStatus(motd, version, online, max, 0, favicon, sample);
    }

    /// <summary>把 chat 组件（字符串 / {text,extra[]}）压成带 § 色码的纯文本。</summary>
    public static string FlattenChat(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String) return el.GetString() ?? "";
        if (el.ValueKind != JsonValueKind.Object && el.ValueKind != JsonValueKind.Array) return "";
        var sb = new StringBuilder();
        AppendChat(sb, el);
        return sb.ToString();
    }

    private static void AppendChat(StringBuilder sb, JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append(el.GetString());
                break;
            case JsonValueKind.Object:
                if (el.TryGetProperty("color", out var color))
                    sb.Append('§').Append(ColorToCode(color.GetString()));
                if (el.TryGetProperty("bold", out var bold) && bold.ValueKind == JsonValueKind.True)
                    sb.Append("§l");
                if (el.TryGetProperty("text", out var text))
                    sb.Append(text.GetString());
                if (el.TryGetProperty("extra", out var extra) && extra.ValueKind == JsonValueKind.Array)
                    foreach (var child in extra.EnumerateArray())
                        AppendChat(sb, child);
                break;
            case JsonValueKind.Array:
                foreach (var child in el.EnumerateArray())
                    AppendChat(sb, child);
                break;
        }
    }

    private static char ColorToCode(string? color) => color?.ToLowerInvariant() switch
    {
        "black" => '0', "dark_blue" => '1', "dark_green" => '2', "dark_aqua" => '3',
        "dark_red" => '4', "dark_purple" => '5', "gold" => '6', "gray" => '7',
        "dark_gray" => '8', "blue" => '9', "green" => 'a', "aqua" => 'b',
        "red" => 'c', "light_purple" => 'd', "yellow" => 'e', "white" => 'f',
        _ => 'r',
    };

    // ---- VarInt（Java 风格，最高位=续传标志） ----

    public static byte[] WriteVarInt(int value)
    {
        var bytes = new List<byte>();
        var v = (uint)value;
        do
        {
            var b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            bytes.Add(b);
        } while (v != 0);
        return bytes.ToArray();
    }

    public static int ReadVarInt(Stream stream)
    {
        var value = 0;
        var shift = 0;
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0) throw new EndOfStreamException("服务器提前断开（VarInt 不完整）。");
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 28) throw new InvalidOperationException("VarInt 过长（协议异常）。");
        }
        return value;
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buf, CancellationToken ct)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = await stream.ReadAsync(buf.AsMemory(read), ct);
            if (n <= 0) throw new EndOfStreamException("服务器提前断开（状态 JSON 不完整）。");
            read += n;
        }
    }

    /// <summary>握手帧 = VarInt(内容长) + 内容。</summary>
    private static byte[] ConcatFrames(byte[] packet)
    {
        var len = WriteVarInt(packet.Length);
        var frame = new byte[len.Length + packet.Length];
        len.CopyTo(frame, 0);
        packet.CopyTo(frame, len.Length);
        return frame;
    }

    /// <summary>把 host:port 拆开（无端口 → 25565）。非法输入抛 FormatException。</summary>
    public static (string Host, ushort Port) ParseAddress(string input)
    {
        var s = input.Trim();
        if (s.Length == 0) throw new FormatException("地址为空。");
        var port = (ushort)25565;
        var idx = s.LastIndexOf(':');
        if (idx > 0)
        {
            if (!ushort.TryParse(s[(idx + 1)..], out port))
                throw new FormatException($"端口非法：{s[(idx + 1)..]}");
            s = s[..idx];
        }
        if (s.Length == 0) throw new FormatException("主机名为空。");
        return (s, port);
    }
}
