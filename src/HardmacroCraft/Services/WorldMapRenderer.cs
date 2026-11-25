using System.IO;
using System.IO.Compression;

namespace HardmacroCraft.Services;

/// <summary>单列地表信息：最顶非空气方块名 + 高度。</summary>
public sealed record MapPixel(string? Block, int Y);

/// <summary>
/// v0.16.5：世界地图渲染（对齐 KAMUCL 的地图查看类实用功能）——
/// 解析存档 region/*.mca（Anvil），取每列最顶非空气方块着色（含高度明暗），
/// 拼装成顶视图 PNG。纯 BCL 实现（NBT 复用 ServerList.Nbt，PNG 手写编码器），
/// headless 可测。区块格式支持 1.18+ 的 sections/block_states（1.16+ 连续打包位）；
/// 旧格式（Level/Sections/Palette + BlockStates 跨长填充）跳过并在汇总里注明。
/// </summary>
public static class WorldMapRenderer
{
    public const string AirMarker = "__air";

    private static readonly HashSet<string> AirBlocks =
        new(StringComparer.OrdinalIgnoreCase) { "air", "cave_air", "void_air" };

    // 常见方块基色（minecraft: 前缀键）。水按深度自然叠加在浅色地表上方不明显，取固定蓝。
    private static readonly Dictionary<string, (byte R, byte G, byte B)> BlockColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grass_block"] = (110, 168, 74), ["dirt"] = (134, 96, 67), ["stone"] = (125, 125, 125),
        ["deepslate"] = (80, 80, 84), ["cobblestone"] = (110, 110, 110), ["sand"] = (219, 207, 163),
        ["red_sand"] = (190, 102, 33), ["gravel"] = (136, 130, 127), ["water"] = (58, 111, 212),
        ["lava"] = (233, 109, 25), ["snow"] = (249, 251, 252), ["snow_block"] = (249, 251, 252),
        ["ice"] = (162, 204, 238), ["packed_ice"] = (141, 180, 224), ["clay"] = (160, 166, 179),
        ["oak_leaves"] = (76, 124, 44), ["birch_leaves"] = (107, 141, 80), ["spruce_leaves"] = (56, 91, 49),
        ["jungle_leaves"] = (58, 134, 34), ["acacia_leaves"] = (87, 128, 42), ["dark_oak_leaves"] = (51, 90, 38),
        ["cherry_leaves"] = (236, 179, 195), ["mangrove_leaves"] = (48, 118, 27),
        ["oak_log"] = (109, 84, 50), ["spruce_log"] = (58, 37, 16), ["birch_log"] = (215, 203, 143),
        ["podzol"] = (91, 63, 24), ["mycelium"] = (111, 97, 90), ["terracotta"] = (152, 94, 67),
        ["netherrack"] = (97, 38, 38), ["end_stone"] = (219, 222, 158), ["obsidian"] = (20, 18, 30),
        ["bedrock"] = (84, 84, 84), ["mud"] = (61, 50, 44), ["moss_block"] = (89, 109, 54),
        ["mossy_cobblestone"] = (109, 118, 91), ["coarse_dirt"] = (119, 85, 59),
        ["calcite"] = (223, 224, 220), ["dripstone_block"] = (134, 107, 92), ["tuff"] = (108, 109, 102),
    };

    /// <summary>方块名 → 基色（minecraft: 前缀可带可不带）；未收录按名称哈希出稳定的暗色系。</summary>
    public static (byte R, byte G, byte B) BlockColor(string fullName)
    {
        var name = fullName.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? fullName[10..] : fullName;
        if (BlockColors.TryGetValue(name, out var c)) return c;
        // 未收录：哈希出稳定的中低亮度色（不是纯随机灰，能区分不同方块群）
        var h = 0;
        foreach (var ch in name) h = h * 31 + ch;
        var r = (byte)(90 + (h & 63)); var g = (byte)(90 + ((h >> 6) & 63)); var b = (byte)(90 + ((h >> 12) & 63));
        return (r, g, b);
    }

    /// <summary>高度明暗：低处暗、高处亮（y ∈ [-64, 320] 线性映射 0.55→1.0）。</summary>
    public static double Shade(int y)
        => 0.55 + 0.45 * Math.Clamp((y + 64) / 384.0, 0.0, 1.0);

    /// <summary>1.16+ 连续打包位取值：每 long 内 entriesPerLong 个、不跨长。</summary>
    public static int UnpackIndex(long[] data, int bitsPer, int entriesPerLong, int index)
    {
        var li = index / entriesPerLong;
        if (li < 0 || li >= data.Length) return 0;
        var shift = (index % entriesPerLong) * bitsPer;
        var mask = (1L << bitsPer) - 1;
        return (int)((data[li] >> shift) & mask);
    }

    /// <summary>解析 region 文件 → 已压缩区块的 NBT 根列表（坏块跳过）。</summary>
    public static List<NbtTag> ParseRegion(byte[] file)
    {
        var roots = new List<NbtTag>();
        if (file.Length < 8192) return roots;
        for (var i = 0; i < 1024; i++)
        {
            var o = i * 4;
            var off = (file[o] << 16) | (file[o + 1] << 8) | file[o + 2];
            var cnt = file[o + 3];
            if (off == 0 || cnt == 0) continue;
            var start = off * 4096L;
            if (start + 5 > file.Length) continue;
            var len = (file[start] << 24) | (file[start + 1] << 16) | (file[start + 2] << 8) | file[start + 3];
            if (len <= 1 || start + 4 + len > file.Length) continue;
            var comp = file[start + 4];
            var payload = new byte[len - 1];
            Array.Copy(file, start + 5, payload, 0, len - 1);
            try
            {
                var raw = comp switch
                {
                    1 => Decompress(payload, new GZipStream(new MemoryStream(payload), CompressionMode.Decompress)),
                    2 => Decompress(payload, new ZLibStream(new MemoryStream(payload), CompressionMode.Decompress)),
                    3 => payload,
                    _ => null,
                };
                if (raw == null) continue;
                var root = Nbt.Parse(raw);
                if (root != null) roots.Add(root);
            }
            catch { /* 单个区块损坏不拖垮整图 */ }
        }
        return roots;
    }

    private static byte[] Decompress(byte[] payload, Stream s)
    {
        using var _ = s;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>chunk NBT → 16×16 列地表（不支持的旧格式返回 note 非空 + px null）。</summary>
    public static (MapPixel?[,]? Px, string Note) ChunkColumns(NbtTag root)
    {
        var sections = root.Child("sections");
        if (sections == null || sections.Type != NbtType.List || sections.Children.Count == 0)
        {
            var legacy = root.Child("Level")?.Child("Sections");
            if (legacy != null && legacy.Children.Count > 0)
                return (null, "legacy");
            return (null, "empty");
        }

        var ordered = sections.Children
            .Where(s => s.Child("Y") != null)
            .OrderByDescending(s => s.Child("Y")!.B);
        var px = new MapPixel?[16, 16];
        var filled = 0;
        foreach (var sec in ordered)
        {
            var sy = sec.Child("Y")!.B;
            var bs = sec.Child("block_states");
            if (bs == null) continue;
            var palette = bs.Child("palette");
            if (palette == null || palette.Children.Count == 0) continue;
            var names = palette.Children.Select(p => p.Child("Name")?.Str ?? "unknown").ToArray();

            // 每列独立取最顶方块：section 自上而下，列一旦填过就不再覆盖
            long[] data = bs.Child("data")?.Longs ?? Array.Empty<long>();
            // 1.16+ 打包：bits 最小 4（palette≤16 也按 4bit），每 long 装 floor(64/bits) 个、不跨长。
            // v0.16.5 实跑断言抓过：per 在 bits<8 时误留 0 → UnpackIndex 除零（堆栈被内联吞掉）。
            var bits = Math.Max(4, (int)Math.Ceiling(Math.Log2(names.Length)));
            var per = Math.Max(1, 64 / bits);
            for (var yl = 15; yl >= 0; yl--)
            {
                for (var z = 0; z < 16; z++)
                {
                    for (var x = 0; x < 16; x++)
                    {
                        if (px[x, z] != null) continue;
                        var idx = yl * 256 + z * 16 + x;
                        var pi = names.Length == 1 ? 0 : UnpackIndex(data, bits, per, idx);
                        if (pi < 0 || pi >= names.Length) continue;
                        var blockName = names[pi];
                        // v0.16.5 实跑断言抓过：空气不能“占位”——真实区块顶部全是空气，
                        // 占位会让整列停在本段、永远找不到地表。空列（全程无方块）保持 null。
                        if (AirBlocks.Contains(BlockKey(blockName))) continue;
                        px[x, z] = new MapPixel(blockName, sy * 16 + yl);
                        filled++;
                    }
                }
            }
            if (filled >= 256) break;
        }
        return (px, filled == 0 ? "empty" : "ok");
    }

    private static string BlockKey(string n) => n.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase) ? n[10..] : n;

    /// <summary>把各 chunk 的列拼装成整图（cx/cz 为区块坐标）。返回 RGB 与缩放信息。</summary>
    public static (byte[] Rgb, int Width, int Height, string Info) Assemble(
        Dictionary<(int Cx, int Cz), MapPixel?[,]> chunks, int maxPixels = 3000)
    {
        if (chunks.Count == 0) throw new InvalidOperationException("没有可渲染的区块");
        int minX = chunks.Keys.Min(k => k.Cx), maxX = chunks.Keys.Max(k => k.Cx);
        int minZ = chunks.Keys.Min(k => k.Cz), maxZ = chunks.Keys.Max(k => k.Cz);
        var mapW = (maxX - minX + 1) * 16;
        var mapH = (maxZ - minZ + 1) * 16;
        var scale = Math.Max(1, (int)Math.Ceiling(Math.Max(mapW, mapH) / (double)maxPixels));
        var w = mapW / scale;
        var h = mapH / scale;
        var rgb = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var px = x * scale;
                var pz = y * scale;
                var cx = minX + px / 16;
                var cz = minZ + pz / 16;
                if (!chunks.TryGetValue((cx, cz), out var chunk)) continue;
                var p = chunk[px % 16, pz % 16];
                if (p == null) continue;
                var (r, g, b) = p.Block == null ? ((byte)20, (byte)24, (byte)20) : BlockColor(p.Block);
                var shade = p.Block == null ? 1.0 : Shade(p.Y);
                var o = (y * w + x) * 3;
                rgb[o] = (byte)Math.Clamp(r * shade, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g * shade, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b * shade, 0, 255);
            }
        }
        return (rgb, w, h, $"{mapW}×{mapH}px 原始 → {w}×{h}px（缩放 {scale}）");
    }

    /// <summary>渲染一个存档目录 → PNG 字节（异常上抛给调用方提示）。</summary>
    public static (byte[] Png, int W, int H, string Info, int ChunksOk, int Skipped) RenderWorld(string worldDir)
    {
        var regionDir = Path.Combine(worldDir, "region");
        if (!Directory.Exists(regionDir)) throw new DirectoryNotFoundException("该世界没有 region 目录（可能不是 Java 版主世界）");
        var chunks = new Dictionary<(int, int), MapPixel?[,]>();
        var skipped = 0;
        foreach (var f in Directory.GetFiles(regionDir, "*.mca"))
        {
            foreach (var root in ParseRegion(File.ReadAllBytes(f)))
            {
                var (px, note) = ChunkColumns(root);
                if (px == null) { if (note == "legacy") skipped++; continue; }
                var xp = root.Child("xPos")?.I;
                var zp = root.Child("zPos")?.I;
                if (xp == null || zp == null) continue;
                chunks[(xp.Value, zp.Value)] = px;
            }
        }
        if (chunks.Count == 0)
            throw new InvalidOperationException(skipped > 0
                ? $"所有 {skipped} 个区块都是旧版格式（1.17- 的 Palette/BlockStates 布局），暂不支持渲染"
                : "region 里没有可渲染的区块");
        var (rgb, w, h, info) = Assemble(chunks);
        return (EncodePng(w, h, rgb), w, h, info, chunks.Count, skipped);
    }

    /// <summary>扫描可渲染的世界：共享 saves/* + 隔离 versions/*/saves/*（有 level.dat 才算）。</summary>
    public static List<(string Name, string Dir)> FindWorlds()
    {
        var root = AppSettings.Instance.GameRoot;
        var list = new List<(string Name, string Dir)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var savesDirs = new List<string> { Path.Combine(root, "saves") };
        var versionsDir = Path.Combine(root, "versions");
        if (Directory.Exists(versionsDir))
            savesDirs.AddRange(Directory.GetDirectories(versionsDir).Select(v => Path.Combine(v, "saves")));
        foreach (var dir in savesDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var w in Directory.GetDirectories(dir))
            {
                if (!File.Exists(Path.Combine(w, "level.dat"))) continue;
                if (seen.Add(w)) list.Add((Path.GetFileName(w), w));
            }
        }
        return list.OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- 手写 PNG 编码器（BCL：ZLibStream + CRC32，headless 可测） ----------

    private static readonly uint[] CrcTable = BuildCrc();
    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (var i = 0; i < 256; i++)
        {
            var c = (uint)i;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    private static uint Crc32(byte[] data, int len)
    {
        uint c = 0xFFFFFFFF;
        for (var i = 0; i < len; i++) c = CrcTable[(c ^ data[i]) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static void PutChunk(List<byte> outBytes, string type, byte[] body)
    {
        outBytes.AddRange(new byte[] { (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length });
        var head = System.Text.Encoding.ASCII.GetBytes(type);
        var buf = new byte[head.Length + body.Length];
        head.CopyTo(buf, 0); body.CopyTo(buf, head.Length);
        outBytes.AddRange(buf);
        var c = Crc32(buf, buf.Length);
        outBytes.AddRange(new byte[] { (byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c });
    }

    /// <summary>RGB888 → PNG（filter 0 行；IDAT 用 ZLibStream）。width/height 上限 65535。</summary>
    public static byte[] EncodePng(int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("rgb 长度与尺寸不符");
        var outBytes = new List<byte>(width * height * 3 / 2 + 256) { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new byte[13];
        ihdr[0] = (byte)(width >> 24); ihdr[1] = (byte)(width >> 16); ihdr[2] = (byte)(width >> 8); ihdr[3] = (byte)width;
        ihdr[4] = (byte)(height >> 24); ihdr[5] = (byte)(height >> 16); ihdr[6] = (byte)(height >> 8); ihdr[7] = (byte)height;
        ihdr[8] = 8; ihdr[9] = 2; // 8bit RGB
        PutChunk(outBytes, "IHDR", ihdr);

        // 原始数据：每行前缀 filter 0
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            raw[y * (1 + width * 3)] = 0;
            Array.Copy(rgb, y * width * 3, raw, y * (1 + width * 3) + 1, width * 3);
        }
        var idat = new List<byte>();
        using (var zs = new ZLibStream(new MemoryStreamWrapper(idat), CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(raw, 0, raw.Length);
        PutChunk(outBytes, "IDAT", idat.ToArray());
        PutChunk(outBytes, "IEND", Array.Empty<byte>());
        return outBytes.ToArray();
    }

    /// <summary>写入型 MemoryStream 包装（避免额外拷贝）。</summary>
    private sealed class MemoryStreamWrapper : Stream
    {
        private readonly List<byte> _sink;
        public MemoryStreamWrapper(List<byte> sink) => _sink = sink;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _sink.Count;
        public override long Position { get => _sink.Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) { for (var i = 0; i < c; i++) _sink.Add(b[o + i]); }
    }
}
