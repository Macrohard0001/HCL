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
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
}
