using System;

namespace HardmacroCraft.Services;

/// <summary>
/// 系统信息（fix6）—— 物理内存检测与游戏内存推荐值（PCL 式「自动分配」）。
///
/// 检测方式：GC.GetGCMemoryInfo().TotalAvailableMemoryBytes（.NET 6+ 官方 API），
/// 返回容器/系统层面的可用物理内存上限，Windows / Linux / macOS 通用，
/// 无需 P/Invoke、无需 WMI（WMI 首次查询慢且仅 Windows）。
/// </summary>
public static class SystemInfo
{
    /// <summary>物理内存字节 → MB 的换算。</summary>
    private const long BytesPerMb = 1024 * 1024;

    /// <summary>检测失败时的兜底值（与旧默认 MemoryMb=4096 一致）。</summary>
    public const long FallbackTotalMb = 4096;

    /// <summary>推荐分配上限：Minecraft 实例很少需要超过 8GB，再多只会浪费。</summary>
    public const long RecommendedCapMb = 8192;

    /// <summary>推荐分配下限：低于 1GB 的分配对 1.17+ 基本不可玩。</summary>
    public const long RecommendedFloorMb = 1024;

    /// <summary>物理内存总量（MB）。检测失败时回退 4096MB（旧默认值）。</summary>
    public static long TotalPhysicalMemoryMb
    {
        get
        {
            try
            {
                var bytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (bytes <= 0) return FallbackTotalMb;
                return Math.Max(1, bytes / BytesPerMb);
            }
            catch
            {
                return FallbackTotalMb;
            }
        }
    }

    /// <summary>
    /// 推荐游戏内存（PCL 式「自动」）：物理内存的一半，按 512MB 取整，
    /// 限制在 [1024, min(8192, 总内存-1024)] 区间（给系统至少留 1GB）。
    /// </summary>
    public static long RecommendedMemoryMb(long totalMb)
    {
        if (totalMb <= 0) totalMb = FallbackTotalMb;

        // 内存极小的机器（&lt;2GB）：分配 1GB，启动日志会另行提示内存不足
        if (totalMb < 2048) return RecommendedFloorMb;

        // 一半内存，512MB 向下取整
        var rec = totalMb / 2 / 512 * 512;

        // 下限 1024，上限 min(8192, 总内存-1024)
        var max = Math.Min(RecommendedCapMb, totalMb - RecommendedFloorMb);
        if (max < RecommendedFloorMb) max = RecommendedFloorMb;
        return Math.Clamp(rec, RecommendedFloorMb, max);
    }

    /// <summary>按当前机器推荐游戏内存。</summary>
    public static long RecommendedMemoryMb() => RecommendedMemoryMb(TotalPhysicalMemoryMb);

    /// <summary>
    /// 启动前内存体检（PCL 启动前的「内存不足」警告）：
    /// 返回警告文案；null = 无问题。仅提示，不阻止启动（PCL 同款）。
    /// </summary>
    public static string? CheckAllocatedMemory(long allocatedMb)
    {
        var total = TotalPhysicalMemoryMb;
        if (allocatedMb > total)
            return $"分配内存 {allocatedMb}MB 大于物理内存 {total}MB，游戏可能无法启动；请在设置中调低内存或开启自动检测。";
        if (allocatedMb > total - RecommendedFloorMb)
            return $"分配内存 {allocatedMb}MB 接近物理内存上限（{total}MB），可能挤占系统内存导致卡顿或崩溃。";
        return null;
    }
}
