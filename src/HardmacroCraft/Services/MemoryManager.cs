using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HardmacroCraft.Services;

/// <summary>
/// 启动器内存优化（v0.1.3，参考 PCL / HMCL 的内存管理思路）：
/// 1) 重大操作（版本安装 / 整合包导入 / 批量下载 / 游戏启动）结束后延迟做一次
///    完整 GC + 物理工作集回收 —— 下载缓冲、临时文件句柄、JSON 解析残留在这一刻集中释放，
///    避免启动器长挂后台时占用持续走高；
/// 2) 空闲时段定时优化（每 5 分钟检查一次，仅在启动器完全空闲时执行，
///    绝不与游戏启动 / 下载任务抢占 CPU）；
/// 3) 设置页可关闭（AppSettings.AutoMemoryOptimize，默认开）。
/// GC 策略配套：csproj 显式工作站并发 GC（ServerGarbageCollector=false），
/// 桌面单窗口应用下工作集与暂停时间都优于服务器 GC。
///
/// v0.1.12 内存占用口径修复：此前设置页把 WorkingSet64（完整工作集，含大量系统 DLL
/// 与共享映射）当作“内存占用”展示，数值与任务管理器对不上（任务管理器进程列是
/// “活动专用工作集”，详细信息列里另有“提交大小”），用户对比后觉得“算得不准”。
/// 现在统一为双口径：
///   - 专用内存（PrivateMb）= PROCESS_MEMORY_COUNTERS_EX.PrivateUsage（提交大小），
///     与进程实际独占的内存一致，是设置页展示口径；
///   - 工作集（WorkingSetMb）= WorkingSet64，物理内存口径，与“内存清理”收益对应。
/// </summary>
public static class MemoryManager
{
    private static readonly object Lock = new();
    private static System.Timers.Timer? _idleTimer;

    /// <summary>当前进程物理工作集（MB）。日志与测试断言用。</summary>
    public static long WorkingSetMb
    {
        get
        {
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            return p.WorkingSet64 / (1024 * 1024);
        }
    }

    /// <summary>
    /// 当前进程专用内存 / 提交大小（MB）—— 与任务管理器「详细信息 → 提交大小」一致。
    /// 非 Windows 或 P/Invoke 失败时回退 PrivateMemorySize64。
    /// </summary>
    public static long PrivateMb
    {
        get
        {
            if (OperatingSystem.IsWindows() && TryGetPrivateUsage(out var usage) && usage > 0)
                return usage / (1024 * 1024);
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            return p.PrivateMemorySize64 / (1024 * 1024);
        }
    }

    /// <summary>
    /// v0.1.13：专用工作集（MB）—— 与任务管理器默认「内存」列完全同口径（只统计
    /// 本进程独占、且驻留在物理内存的页）。此前展示的“专用内存(提交)”包含大量已提交
    /// 但未驻留 / 共享映射的页，用户对比任务管理器（31 MB）与启动器（500+ MB）觉得算错。
    /// v0.15.0 修复「首次显示不正确」：QueryWorkingSetEx 缓冲区加宽 + 重试加多，
    /// 统计瞬时失败时沿用上一次成功值，不再直接以完整工作集口径显示首读数。
    /// </summary>
    public static long PrivateWorkingSetMb
    {
        get
        {
            if (OperatingSystem.IsWindows() && TryGetPrivateWorkingSet(out var bytes) && bytes > 0)
            {
                _lastGoodPrivateWsBytes = bytes;
                return bytes / (1024 * 1024);
            }
            // v0.15.0：统计瞬时失败 → 沿用上一次成功值；尚无成功值 → 工作集兜底
            //（正常不再发生：缓冲余量已加宽 + 5 轮重试；工作集口径保证 ≤ 真实工作集）
            if (_lastGoodPrivateWsBytes > 0)
                return _lastGoodPrivateWsBytes / (1024 * 1024);
            return WorkingSetMb;
        }
    }

    /// <summary>v0.15.0：最近一次成功的专用工作集字节数（瞬时失败时沿用，0 = 尚无成功值）。</summary>
    private static long _lastGoodPrivateWsBytes;

    /// <summary>
    /// 纯逻辑（进测试）：按 PSAPI_WORKING_SET_EX_BLOCK 标志位统计专用物理内存字节数。
    /// 私有页判定：Valid(bit0)=1 且 Shared(bit15-16)=0（0=私有，1=共享，2=可共享）。
    /// 与任务管理器「内存（活动专用工作集）」同口径。
    /// </summary>
    public static long ComputePrivateBytes(IEnumerable<ulong> blockFlags, int pageSize = 4096)
    {
        long total = 0;
        foreach (var f in blockFlags)
        {
            if ((f & 1UL) == 0) continue;                    // Valid:1 = bit0
            var shared = (f >> 15) & 0x3UL;                  // Shared:2 = bit15-16
            if (shared != 0) continue;                       // 0 = 私有
            total += pageSize;
        }
        return total;
    }

    /// <summary>QueryWorkingSetEx 遍历工作集页表，统计专用工作集字节（仅 Windows）。</summary>
    private static bool TryGetPrivateWorkingSet(out long bytes)
    {
        bytes = 0;
        try
        {
            var counters = new PROCESS_MEMORY_COUNTERS_EX
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>(),
            };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.cb))
                return false;
            var wsPages = (ulong)counters.WorkingSetSize / 4096UL;
            if (wsPages == 0) return false;

            // 缓冲区：头（NumberOfEntries，IntPtr）+ 每页一个 IntPtr 大小的块。
            // 工作集随时在变，逐轮放大缓冲重试：
            // v0.15.0 首读修复 —— 首轮余量从 +20% 提到 +100%（工作集在两次调用间
            // 快速增长时旧余量不够，QueryWorkingSetEx 连续失败会静默回落到错误口径，
            // 造成设置页首次显示的内存占用明显偏高），重试 3 → 5 轮。
            var capacity = (ulong)((wsPages * 2) + 8192);
            for (var round = 0; round < 5; round++, capacity = (ulong)(capacity * 2) + 8192)
            {
                var size = (uint)(IntPtr.Size + (long)capacity * IntPtr.Size);
                var buf = System.Runtime.InteropServices.Marshal.AllocHGlobal((IntPtr)size);
                try
                {
                    if (!QueryWorkingSetEx(GetCurrentProcess(), buf, size))
                        continue;
                    var count = (ulong)System.Runtime.InteropServices.Marshal.ReadIntPtr(buf);
                    if (count == 0) return false;
                    var basePtr = IntPtr.Add(buf, IntPtr.Size);
                    var flags = new ulong[count];
                    for (ulong i = 0; i < count; i++)
                        flags[i] = (ulong)System.Runtime.InteropServices.Marshal.ReadIntPtr(
                            basePtr, (int)(i * (ulong)IntPtr.Size));
                    bytes = ComputePrivateBytes(flags);
                    return true;
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buf); }
            }
            return false;
        }
        catch { return false; }
    }

    private static bool TryGetPrivateUsage(out long privateUsage)
    {
        privateUsage = 0;
        try
        {
            var counters = new PROCESS_MEMORY_COUNTERS_EX
            {
                cb = (uint)System.Runtime.InteropServices.Marshal.SizeOf<PROCESS_MEMORY_COUNTERS_EX>(),
            };
            if (!GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.cb))
                return false;
            privateUsage = counters.PrivateUsage.ToInt64();
            return true;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public IntPtr PrivateUsage;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetProcessMemoryInfo(IntPtr hProcess, ref PROCESS_MEMORY_COUNTERS_EX counters, uint cb);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool QueryWorkingSetEx(IntPtr hProcess, IntPtr pv, uint cb);

    /// <summary>
    /// 启动器是否忙碌（游戏启动流程 / 任务中心运行中的任务）。
    /// 忙碌时自动优化直接跳过 —— GC 暂停若打断下载进度回调节奏得不偿失。
    /// </summary>
    public static bool IsLauncherBusy()
    {
        if (LaunchProgressHub.Current.Running) return true;
        foreach (var t in DownloadTaskService.Instance.Snapshot())
            if (t.IsActive) return true;
        return false;
    }

    /// <summary>
    /// 立即执行一次内存优化：两轮完整 GC（2 代 + LOH 压缩）+ 等待终结器 + Windows 下
    /// SetProcessWorkingSetSize(-1,-1) 把物理工作集还给系统（PCL「内存优化」同款；
    /// 托管对象本就由 GC 回收，工作集收缩的是物理内存占用，对象仍按需换回）。
    /// 返回（优化前, 优化后）托管堆字节数，供日志 / 测试断言。
    /// </summary>
    public static (long Before, long After) OptimizeNow()
    {
        var before = GC.GetTotalMemory(false);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        if (OperatingSystem.IsWindows())
        {
            try { _ = SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1)); }
            catch { /* 最低工作集限制 / 权限不足时忽略 —— GC 收益已经拿到 */ }
        }
        return (before, GC.GetTotalMemory(false));
    }

    /// <summary>
    /// v0.1.12：带工作集口径的完整优化结果 ——
    /// （物理工作集优化前/后 MB，托管堆优化前/后 MB，专用内存优化后 MB）。
    /// 设置页「立即清理」据此展示真实收益：此前只报托管堆（几十 MB 级别），
    /// 与任务管理器里几百 MB 的变化对不上，看起来像“算错了”。
    /// </summary>
    public static (long WsBefore, long WsAfter, long ManagedBefore, long ManagedAfter, long PrivateAfter)
        OptimizeNowDetailed()
    {
        var wsBefore = WorkingSetMb;
        var (mBefore, mAfter) = OptimizeNow();
        return (wsBefore, WorkingSetMb, mBefore / (1024 * 1024), mAfter / (1024 * 1024), PrivateMb);
    }

    /// <summary>
    /// 重大操作结束后调用：延迟 1.5 秒在后台线程做优化（给 UI 收尾渲染留时间），
    /// 不阻塞调用方。AutoMemoryOptimize 关闭时不动作。
    /// </summary>
    public static void OptimizeAfterHeavyOp()
    {
        if (!AppSettings.Instance.AutoMemoryOptimize) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(1500);
                OptimizeNow();
            }
            catch { /* 后台优化失败绝不影响业务 */ }
        });
    }

    /// <summary>
    /// 启动空闲定时优化（App.OnStartup 调用一次，重复调用幂等）。
    /// 每次触发先判断忙碌与设置开关，两个条件都不满足才真正 GC。
    /// </summary>
    public static void Initialize()
    {
        lock (Lock)
        {
            if (_idleTimer != null) return;
            _idleTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds)
            {
                AutoReset = true,
            };
            _idleTimer.Elapsed += (_, _) => AutoOptimize();
            _idleTimer.Start();
        }
    }

    private static void AutoOptimize()
    {
        try
        {
            if (!AppSettings.Instance.AutoMemoryOptimize) return;
            if (IsLauncherBusy()) return;
            OptimizeNow();
        }
        catch { /* 定时优化失败静默 */ }
    }

    // ---- Windows 物理工作集回收 ----

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
