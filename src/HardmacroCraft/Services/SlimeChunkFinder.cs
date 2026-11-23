namespace HardmacroCraft.Services;

/// <summary>
/// v0.15.10：史莱姆区块查询（种子工具第一步）。
/// 判定公式为社区通用的 Java 版史莱姆区块算法（1.14+ 主世界仍有效）：
/// 以种子 + 区块坐标混合出 per-chunk 种子，经 Java Random 的 LCG 走
/// nextInt(10)，结果为 0 即史莱姆区块。Java Random 语义（48 位状态、
/// 掩码乘加、nextInt 的 do/while 溢出重试）在此逐位复刻。公式经两份独立
/// 开源实现交叉核对（常量 4987142 / 5947611 / 4392871 / 389711 / 987234911）。
/// 纯数学判定，无需读取世界文件——输入种子即可整片预览。
/// </summary>
public static class SlimeChunkFinder
{
    /// <summary>该种子的指定区块是否为史莱姆区块（主世界，非超平坦）。
    /// 公式按原版反编译源逐项复刻：x 侧乘法先按 32 位 int 回绕再放宽（与 z 侧
    /// 先平方再放宽不同），混种固定 XOR 987234911。</summary>
    public static bool IsSlimeChunk(long worldSeed, int chunkX, int chunkZ)
    {
        unchecked
        {
            var mixed = worldSeed
                        + (long)(chunkX * chunkX * 4987142)   // int 回绕后放宽
                        + (long)(chunkX * 5947611)            // int 回绕后放宽
                        + (long)(chunkZ * chunkZ) * 4392871L  // 先平方回绕，放宽后 64 位乘
                        + (long)(chunkZ * 389711);            // int 回绕后放宽
            mixed ^= 987234911L;
            // Java Random(seed)：seed = (seed ^ multiplier) & mask
            const ulong multiplier = 0x5DEECE66DUL;
            const ulong mask = (1UL << 48) - 1;
            var state = ((ulong)mixed ^ multiplier) & mask;
            // nextInt(10)：10 非二次幂 → do { bits = next(31); val = bits % 10; }
            // while (bits - val + 9 < 0)（Java int 溢出回绕，C# unchecked 同语义）
            int bits;
            int val;
            do
            {
                state = (state * multiplier + 0xBUL) & mask;
                bits = (int)(state >> 17); // next(31)：状态高 31 位
                val = bits % 10;
            } while (bits - val + 9 < 0);
            return val == 0;
        }
    }

    /// <summary>统计范围内史莱姆区块数量并生成 2 布尔矩阵（[z][x]，含端点）。</summary>
    public static bool[,] Map(long worldSeed, int chunkXFrom, int chunkXTo, int chunkZFrom, int chunkZTo,
        out int slimeCount)
    {
        var width = chunkXTo - chunkXFrom + 1;
        var height = chunkZTo - chunkZFrom + 1;
        var map = new bool[height, width];
        slimeCount = 0;
        for (var z = 0; z < height; z++)
            for (var x = 0; x < width; x++)
                if (IsSlimeChunk(worldSeed, chunkXFrom + x, chunkZFrom + z))
                {
                    map[z, x] = true;
                    slimeCount++;
                }
        return map;
    }

    /// <summary>
    /// 挂机点建议：在矩阵里找 3×3 全史莱姆区块的中心（挂机塔最理想：九格同种），
    /// 找不到则回退到 2×2 全绿中心，再找不到返回 null。
    /// 返回值为区块坐标（即矩阵中的位置）。
    /// </summary>
    public static (int ChunkX, int ChunkZ, int Size)? BestFarmSpot(bool[,] map, int chunkXFrom, int chunkZFrom)
    {
        var height = map.GetLength(0);
        var width = map.GetLength(1);
        for (var size = 3; size >= 2; size--)
        {
            for (var z = 0; z + size <= height; z++)
                for (var x = 0; x + size <= width; x++)
                {
                    var all = true;
                    for (var dz = 0; dz < size && all; dz++)
                        for (var dx = 0; dx < size && all; dx++)
                            all = map[z + dz, x + dx];
                    if (all)
                        return (chunkXFrom + x + size / 2, chunkZFrom + z + size / 2, size);
                }
        }
        return null;
    }
}
