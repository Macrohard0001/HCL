using System.Diagnostics;
using CmlLib.Core.ProcessBuilder;

namespace HardmacroCraft.Services;

/// <summary>运行中游戏实例的状态：等待窗口出现 → 运行中 → 已退出（随即从列表移除）。</summary>
public enum GameInstanceState
{
    /// <summary>进程已创建，游戏窗口尚未出现（加载中）。</summary>
    WaitingWindow,
    /// <summary>游戏窗口已出现，正常运行。</summary>
    Running,
    /// <summary>进程已退出（记录退出码后从列表移除）。</summary>
    Exited,
}

/// <summary>
/// 窗口等待判定（纯函数，进测试）：游戏进程创建后轮询等待窗口出现。
/// v0.1.7 修复「启动器显示已启动，但 MC 窗口迟迟不出、中间没有任何提示」的时间差问题 ——
/// 此阶段进度区持续显示等待文案，与窗口出现（或超时 / 退出）后分别给出明确反馈。
/// </summary>
public static class GameWindowWait
{
    public enum Decision
    {
        /// <summary>窗口未出现且未超时，继续等待。</summary>
        KeepWaiting,
        /// <summary>本轮轮询检测到窗口出现（只上报一次）。</summary>
        WindowSeen,
        /// <summary>等待超时（进程仍存活）—— 放弃等待，按运行中处理（少数启动器/驱动下窗口句柄拿不到）。</summary>
        TimedOut,
        /// <summary>进程在窗口出现前就退出了。</summary>
        Exited,
    }

    public static Decision Decide(bool exited, bool windowSeen, TimeSpan elapsed, TimeSpan timeout)
    {
        if (exited) return Decision.Exited;
        if (!windowSeen && elapsed >= timeout) return Decision.TimedOut;
        return windowSeen ? Decision.WindowSeen : Decision.KeepWaiting;
    }
}

/// <summary>
/// v0.1.7 多实例管理：跟踪所有由启动器拉起的游戏进程（支持同时运行多个版本 / 多开）。
/// 对齐同类启动器的「运行中的游戏实例」：主页实时列出实例（版本 / PID / 状态），
/// 可一键结束整个进程树（Java 拉起的子进程一并结束）。进程退出后自动移出列表并写日志。
/// </summary>
public static class GameInstanceManager
{
    private static readonly object Gate = new();
    private static readonly List<GameInstance> Instances_ = new();

    /// <summary>当前实例快照（新序在前）。</summary>
    public static IReadOnlyList<GameInstance> Instances
    {
        get { lock (Gate) return Instances_.ToList().AsReadOnly(); }
    }

    /// <summary>运行中（含等待窗口）实例数。</summary>
    public static int ActiveCount
    {
        get { lock (Gate) return Instances_.Count; }
    }

    /// <summary>实例增删 / 状态变化（可在任意线程触发，UI 侧需封送）。</summary>
    public static event Action? Changed;

    /// <summary>实例相关日志（退出码等），由主页写入日志区。</summary>
    public static event Action<string>? Log;

    /// <summary>注册一个刚创建（尚未 Start 或已 Start）的游戏进程。</summary>
    public static GameInstance Register(string version, ProcessWrapper wrapper)
    {
        var inst = new GameInstance(version, wrapper);
        wrapper.Exited += (_, _) => OnExited(inst);
        lock (Gate)
        {
            Instances_.Insert(0, inst);
        }
        Fire();
        return inst;
    }

    /// <summary>结束实例（整个进程树，包括 Java 派生的子进程）。返回是否发出结束请求。</summary>
    public static bool Kill(GameInstance inst) => inst?.Kill() == true;

    /// <summary>按 PID 结束实例。</summary>
    public static bool Kill(int pid)
    {
        GameInstance? found;
        lock (Gate) found = Instances_.FirstOrDefault(i => i.Pid == pid);
        return found != null && Kill(found);
    }

    private static void OnExited(GameInstance inst)
    {
        int code = int.MinValue;
        try
        {
            var p = inst.Process;
            if (p != null && p.HasExited) code = p.ExitCode;
        }
        catch { /* 进程对象不可用时按未知退出码处理 */ }
        inst.MarkExited(code);
        Log?.Invoke($"[实例] {inst.Version}（PID {inst.Pid}）已退出，退出代码 {code}");
        Remove(inst);
    }

    private static void Remove(GameInstance inst)
    {
        lock (Gate) Instances_.Remove(inst);
        Fire();
    }

    /// <summary>状态变化广播（注册 / 窗口出现 / 退出移除）。</summary>
    public static void Fire() => Changed?.Invoke();

    /// <summary>测试辅助：清空跟踪列表（不结束任何真实进程）。</summary>
    internal static void ClearForTests()
    {
        lock (Gate) Instances_.Clear();
        Changed = null;
        Log = null;
    }
}

/// <summary>单个运行中的游戏实例。</summary>
public sealed class GameInstance
{
    private GameInstanceState _state = GameInstanceState.WaitingWindow;

    internal GameInstance(string version, ProcessWrapper wrapper)
    {
        Version = version;
        Wrapper = wrapper;
        StartTime = DateTime.Now;
        try { Pid = wrapper.Process?.Id ?? -1; } catch { Pid = -1; }
    }

    public string Version { get; }
    public int Pid { get; }
    public DateTime StartTime { get; }
    public GameInstanceState State => _state;
    public int ExitCode { get; private set; } = int.MinValue;
    internal ProcessWrapper Wrapper { get; }
    internal Process? Process => Wrapper.Process;

    /// <summary>窗口已出现（主页的窗口等待轮询上报）。</summary>
    public void MarkRunning()
    {
        if (_state == GameInstanceState.WaitingWindow)
        {
            _state = GameInstanceState.Running;
            GameInstanceManager.Fire();
        }
    }

    internal void MarkExited(int code)
    {
        _state = GameInstanceState.Exited;
        ExitCode = code;
    }

    /// <summary>结束整个进程树。已经退出的实例返回 false。</summary>
    public bool Kill()
    {
        var p = Process;
        if (p == null) return false;
        try
        {
            if (p.HasExited) return false;
            p.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>探测窗口句柄是否已出现（供轮询调用；失败按未出现处理）。</summary>
    public bool TryPeekWindow()
    {
        var p = Process;
        if (p == null) return false;
        try
        {
            if (p.HasExited) return false;
            p.Refresh();
            return p.MainWindowHandle != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    public bool HasExited
    {
        get
        {
            try { return Process?.HasExited ?? true; }
            catch { return true; }
        }
    }
}
