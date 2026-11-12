using System.IO;
using System.Text;

namespace HardmacroCraft.Services;

/// <summary>NBT 标签类型（编号与 Minecraft 二进制规范一致，全部大端序）。</summary>
public enum NbtType : byte
{
    End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
    ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12,
}

/// <summary>
/// 最小化 NBT 树节点：Compound 用 Children（Name 有效），List 用 Children（Name 为空），
/// 其余类型直接载荷字段。字段全部公开可变，便于构造测试数据。
/// </summary>
public sealed class NbtTag
{
    public NbtType Type { get; set; }
    public string Name { get; set; } = "";
    public byte B { get; set; }
    public short S { get; set; }
    public int I { get; set; }
    public long L { get; set; }
    public float F { get; set; }
    public double D { get; set; }
    public byte[] Bytes { get; set; } = Array.Empty<byte>();
    public string Str { get; set; } = "";
    public int[] Ints { get; set; } = Array.Empty<int>();
    public long[] Longs { get; set; } = Array.Empty<long>();
    public List<NbtTag> Children { get; } = new();

    public static NbtTag NewCompound(string name) => new() { Type = NbtType.Compound, Name = name };
    public static NbtTag NewString(string name, string value) => new() { Type = NbtType.String, Name = name, Str = value };
    public static NbtTag NewList(string name, NbtType itemType) => new() { Type = NbtType.List, Name = name, I = (int)itemType };

    public NbtTag? Child(string name) =>
        Children.FirstOrDefault(c => c.Name == name);
}

/// <summary>
/// v0.1.6 一键进服「保存到游戏的多人游戏列表」：servers.dat（未压缩 NBT，大端序）的
/// 解析 / 修改 / 写回。手写实现以保持零依赖与可测试性 —— 结构固定为
/// 根 Compound { "servers": List&lt;Compound { name: String, ip: String, icon: ByteArray }&gt; }，
/// 但解析器支持全部 12 种标签类型以保证 roundtrip 无损（保留 icon 与游戏写入的其它字段）。
/// 注：字符串按标准 UTF-8 编解码（Java 端 writeUTF 的代理对差异仅影响超过 U+FFFF 的
/// 极端字符，服务器名 / 地址实际不会用到）。
/// </summary>
public static class Nbt
{
    public static NbtTag? Parse(byte[] data)
    {
        try
        {
            var p = 0;
            var root = ReadTag(data, ref p, named: true);
            return p == data.Length ? root : null; // 尾部多余字节视为损坏
        }
        catch { return null; }
    }

    public static byte[] Write(NbtTag root)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, root, named: true);
        return ms.ToArray();
    }

    private static string ReadStr(byte[] d, ref int p)
    {
        var len = ReadU16(d, ref p);
        var s = Encoding.UTF8.GetString(d, p, len);
        p += len;
        return s;
    }

    private static void WriteStr(Stream s, string v)
    {
        var b = Encoding.UTF8.GetBytes(v);
        WriteU16(s, (ushort)b.Length);
        s.Write(b);
    }

    private static ushort ReadU16(byte[] d, ref int p) { var v = (ushort)((d[p] << 8) | d[p + 1]); p += 2; return v; }
    private static void WriteU16(Stream s, ushort v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }

    private static void WriteI32(Stream s, int v)
    {
        s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16));
        s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
    }

    private static void WriteI64(Stream s, long v)
    {
        for (var i = 7; i >= 0; i--) s.WriteByte((byte)(v >> (i * 8)));
    }

    private static NbtTag ReadTag(byte[] d, ref int p, bool named)
    {
        var type = (NbtType)d[p++];
        var tag = new NbtTag { Type = type };
        if (named) tag.Name = ReadStr(d, ref p);
        switch (type)
        {
            case NbtType.End: break;
            case NbtType.Byte: tag.B = d[p++]; break;
            case NbtType.Short: tag.S = (short)((d[p] << 8) | d[p + 1]); p += 2; break;
            case NbtType.Int:
                tag.I = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; break;
            case NbtType.Long:
                for (var i = 0; i < 8; i++) tag.L = (tag.L << 8) | d[p++]; break;
            case NbtType.Float:
                var fb = new byte[4]; Array.Copy(d, p, fb, 0, 4); p += 4;
                tag.F = BitConverter.IsLittleEndian ? BitConverter.Int32BitsToSingle(BitConverter.ToInt32(fb.Reverse().ToArray())) : BitConverter.Int32BitsToSingle(BitConverter.ToInt32(fb));
                break;
            case NbtType.Double:
                var db = new byte[8]; Array.Copy(d, p, db, 0, 8); p += 8;
                if (BitConverter.IsLittleEndian) Array.Reverse(db);
                tag.D = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(db));
                break;
            case NbtType.ByteArray:
                var bl = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4;
                tag.Bytes = new byte[bl];
                Array.Copy(d, p, tag.Bytes, 0, bl); p += bl;
                break;
            case NbtType.String: tag.Str = ReadStr(d, ref p); break;
            case NbtType.List:
                var itemType = (NbtType)d[p++];
                var count = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4;
                tag.I = (int)itemType;
                for (var i = 0; i < count; i++) tag.Children.Add(ReadTag(d, ref p, named: false));
                break;
            case NbtType.Compound:
                // TAG_End 无名称字段：先看类型字节，End 直接消费 1 字节结束（此前按带名读取错位）
                while (d[p] != 0)
                    tag.Children.Add(ReadTag(d, ref p, named: true));
                p++;
                break;
            case NbtType.IntArray:
                var il = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4;
                tag.Ints = new int[il];
                for (var i = 0; i < il; i++)
                { tag.Ints[i] = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4; }
                break;
            case NbtType.LongArray:
                var ll = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3]; p += 4;
                tag.Longs = new long[ll];
                for (var i = 0; i < ll; i++)
                { long x = 0; for (var j = 0; j < 8; j++) x = (x << 8) | d[p++]; tag.Longs[i] = x; }
                break;
            default:
                throw new InvalidDataException("未知 NBT 标签类型 " + type);
        }
        return tag;
    }

    private static void WriteTag(Stream s, NbtTag t, bool named)
    {
        s.WriteByte((byte)t.Type);
        if (named) WriteStr(s, t.Name);
        switch (t.Type)
        {
            case NbtType.End: break;
            case NbtType.Byte: s.WriteByte(t.B); break;
            case NbtType.Short: WriteU16(s, (ushort)t.S); break;
            case NbtType.Int: WriteI32(s, t.I); break;
            case NbtType.Long: WriteI64(s, t.L); break;
            case NbtType.Float:
                var fb = BitConverter.GetBytes(t.F);
                if (BitConverter.IsLittleEndian) Array.Reverse(fb);
                s.Write(fb); break;
            case NbtType.Double:
                var db = BitConverter.GetBytes(t.D);
                if (BitConverter.IsLittleEndian) Array.Reverse(db);
                s.Write(db); break;
            case NbtType.ByteArray:
                WriteI32(s, t.Bytes.Length); s.Write(t.Bytes); break;
            case NbtType.String: WriteStr(s, t.Str); break;
            case NbtType.List:
                s.WriteByte((byte)(t.Children.Count > 0 ? t.Children[0].Type : (NbtType)t.I));
                WriteI32(s, t.Children.Count);
                foreach (var c in t.Children) WriteTag(s, c, named: false);
                break;
            case NbtType.Compound:
                foreach (var c in t.Children) WriteTag(s, c, named: true);
                s.WriteByte(0); // End
                break;
            case NbtType.IntArray:
                WriteI32(s, t.Ints.Length);
                foreach (var x in t.Ints) WriteI32(s, x);
                break;
            case NbtType.LongArray:
                WriteI32(s, t.Longs.Length);
                foreach (var x in t.Longs) WriteI64(s, x);
                break;
        }
    }
}

/// <summary>servers.dat 的高层操作：读取条目 / 追加或更新条目 / 原子写回。</summary>
public static class ServerList
{
    public static string ServersDatPath(string gameRoot, bool isolation, string version) =>
        isolation
            ? Path.Combine(gameRoot, "versions", version, "servers.dat")
            : Path.Combine(gameRoot, "servers.dat");

    /// <summary>读取多人游戏列表（解析失败返回空表）。</summary>
    public static List<(string Name, string Ip)> ReadEntries(byte[]? dat)
    {
        var list = new List<(string, string)>();
        if (dat == null || dat.Length == 0) return list;
        var root = Nbt.Parse(dat);
        var servers = root?.Child("servers");
        if (servers == null || servers.Type != NbtType.List) return list;
        foreach (var entry in servers.Children.Where(c => c.Type == NbtType.Compound))
            list.Add((entry.Child("name")?.Str ?? "", entry.Child("ip")?.Str ?? ""));
        return list;
    }

    /// <summary>
    /// 在 servers.dat 内容中追加（或按地址更新名称）一条服务器记录，返回新的完整内容。
    /// 原始内容为空 / 无法解析时返回 null（调用方不应覆盖原文件，避免弄坏游戏的列表）。
    /// </summary>
    public static byte[]? AddOrUpdate(byte[]? dat, string name, string ip)
    {
        // 无文件 / 空文件 → 新建根；有内容但解析失败 → 返回 null（调用方不覆盖原文件）
        var root = dat is { Length: > 0 } ? Nbt.Parse(dat) : NbtTag.NewCompound("");
        if (root == null || root.Type != NbtType.Compound) return null;

        var servers = root.Child("servers");
        if (servers == null || servers.Type != NbtType.List)
        {
            servers = NbtTag.NewList("servers", NbtType.Compound);
            root.Children.Add(servers);
        }
        // 空 List 声明的元素类型为 End —— 补齐为 Compound，否则游戏读不出条目
        if (servers.Children.Count == 0) servers.I = (int)NbtType.Compound;

        var entry = servers.Children.FirstOrDefault(c =>
            c.Type == NbtType.Compound && string.Equals(c.Child("ip")?.Str, ip, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            entry = NbtTag.NewCompound("");
            entry.Children.Add(NbtTag.NewString("name", name));
            entry.Children.Add(NbtTag.NewString("ip", ip));
            servers.Children.Add(entry);
        }
        else
        {
            var nameTag = entry.Child("name");
            if (nameTag != null) nameTag.Str = name;
            else entry.Children.Add(NbtTag.NewString("name", name));
        }
        return Nbt.Write(root);
    }

    /// <summary>原子写回 servers.dat（临时文件 + 替换），失败返回错误信息，成功返回 null。</summary>
    public static string? TryWrite(string path, byte[] data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".hcltmp";
            File.WriteAllBytes(tmp, data);
            // 游戏自带的旧副本规则：写新前先保留一份 servers.dat_old（与游戏行为一致）
            if (File.Exists(path))
            {
                try { File.Copy(path, path.Replace(".dat", ".dat_old", StringComparison.Ordinal), true); }
                catch { /* 旧副本失败不阻断 */ }
            }
            File.Move(tmp, path, true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }
}
