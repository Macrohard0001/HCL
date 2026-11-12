namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.11 界面刷新频率（纯逻辑，进测试）—— 对齐主流启动器「设置 → 界面 → 界面刷新频率」：
///   auto          自动：默认 500ms 轮询（兼顾流畅与功耗，与此前行为一致）
///   manual        手动：按用户指定的 FPS 轮询（1-240）
///   event         事件驱动：不做轮询，只在有任务 / 实例状态变化时立刻刷新（最省电）
///   event-limited 事件驱动（限频）：事件到达立刻刷新，但两次刷新至少间隔 1000/maxFps ms
/// 轮询类页面（任务中心 / 实例管理）共用同一策略；「启动进度」等本来就是事件驱动
/// 的 UI 不受此设置影响（它们始终即时刷新）。
/// </summary>
public static class UiRefreshPolicy
{
    public const string ModeAuto = "auto";
    public const string ModeManual = "manual";
    public const string ModeEvent = "event";
    public const string ModeEventLimited = "event-limited";

    public static bool IsValidMode(string? mode) => mode switch
    {
        ModeAuto or ModeManual or ModeEvent or ModeEventLimited => true,
        _ => false,
    };

    /// <summary>规范化模式（非法值回落 auto）。</summary>
    public static string Normalize(string? mode) => IsValidMode(mode) ? mode! : ModeAuto;

    /// <summary>是否为事件驱动模式（页面应停用轮询定时器、改订阅 Changed 事件）。</summary>
    public static bool IsEventDriven(string? mode) => Normalize(mode) is ModeEvent or ModeEventLimited;

    /// <summary>
    /// 轮询间隔（毫秒）。manual → 1000/fps；auto → 500；事件驱动模式不轮询返回 0。
    /// fps / maxFps 非法（≤0 或 >240）时回落 60。
    /// </summary>
    public static int PollIntervalMs(string? mode, int fps)
    {
        return Normalize(mode) switch
        {
            ModeManual => 1000 / Math.Clamp(fps <= 0 || fps > 240 ? 60 : fps, 1, 240),
            ModeEvent or ModeEventLimited => 0,
            _ => 500,
        };
    }

    /// <summary>
    /// 事件驱动限频模式下的最小刷新间隔（毫秒）。event 模式为 0（立刻刷新）；
    /// event-limited → 1000/maxFps（maxFps 非法回落 60）；轮询模式不适用返回 0。
    /// </summary>
    public static int EventMinIntervalMs(string? mode, int maxFps)
    {
        return Normalize(mode) switch
        {
            ModeEvent => 0,
            ModeEventLimited => 1000 / Math.Clamp(maxFps <= 0 || maxFps > 240 ? 60 : maxFps, 1, 240),
            _ => 0,
        };
    }
}

/// <summary>
/// 事件驱动限频节流器（纯逻辑，进测试）：事件到达 → 是否应该立刻刷新。
/// </summary>
public sealed class RefreshThrottle
{
    private DateTime _last = DateTime.MinValue;
    private readonly int _minIntervalMs;

    public RefreshThrottle(int minIntervalMs) => _minIntervalMs = Math.Max(0, minIntervalMs);

    /// <summary>事件到达：距上次刷新已超过最小间隔 → 立刻刷新（返回 true 并记账）。</summary>
    public bool TryRefresh(DateTime now)
    {
        if (_minIntervalMs <= 0 || (now - _last).TotalMilliseconds >= _minIntervalMs)
        {
            _last = now;
            return true;
        }
        return false;
    }
}
