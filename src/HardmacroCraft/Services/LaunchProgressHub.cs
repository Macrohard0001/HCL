namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.1 启动进度中枢：启动进度的单一事实来源，跨页面存活。
/// 此前进度只存在 HomePage 实例的控件上，Wpf-UI 导航每次都新建页面实例 ——
/// 启动过程中切换标签页再回来，新实例拿到的是"空闲"UI，看起来进度丢了（实际还在启动）。
/// 现在 LaunchAsync 把所有状态写进这里；每个 HomePage 实例订阅 Changed 并回放快照，
/// 无论怎么切页面，进度、阶段、取消按钮都跟手。
/// 状态更新永远发生在 UI 线程（Progress 回调 / Dispatcher.Invoke / 按钮事件），
/// Changed 处理器内部仍做了线程检查兜底。
/// </summary>
public static class LaunchProgressHub
{
    public sealed class Snapshot
    {
        public bool Running;
        public string Version = "";
        public double Percent;              // 0-100
        public bool Indeterminate = true;
        public string ProgressLine = "";    // 主页第一行进度文案
        public string StageLine = "";       // 主页第二行阶段文案（含【n/3】）
        public string ButtonText = "";      // 启动按钮当前文本（下载中 xx% 等）
        public bool CancelVisible;
        public bool CancelEnabled = true;
    }

    /// <summary>当前快照（永远返回同一实例，读方按字段读）。</summary>
    public static Snapshot Current { get; private set; } = new();

    /// <summary>
    /// 最近一次启动的终态文案（End 时的进度行）。v0.1.2 修复：此前启动结束后
    /// 切页再回来，新实例回放的是空白 —— 旧实例上原本能看到「已取消 / 启动失败 /
    /// 游戏运行中」等终态，现在保留下来，切页后也一致。</summary>
    public static string LastProgressLine { get; private set; } = "";

    /// <summary>快照变化（UI 线程触发）。</summary>
    public static event Action? Changed;

    /// <summary>任意页面实例的「取消」按钮都会走到这里；运行中的 LaunchAsync 负责响应。</summary>
    public static event Action? CancelRequested;

    public static void Begin(string version)
    {
        Current = new Snapshot
        {
            Running = true,
            Version = version,
            Indeterminate = true,
            CancelVisible = true,
            CancelEnabled = true,
            ButtonText = "…",
        };
        Fire();
    }

    public static void Update(Action<Snapshot> mutate)
    {
        mutate(Current);
        Fire();
    }

    /// <summary>启动流程结束（成功 / 取消 / 失败）——快照复位，订阅页面恢复空闲 UI。
    /// 终态进度行保留在 LastProgressLine 供切页后的新实例回放。</summary>
    public static void End()
    {
        LastProgressLine = Current.ProgressLine;
        Current = new Snapshot();
        Fire();
    }

    public static void RequestCancel() => CancelRequested?.Invoke();

    private static void Fire() => Changed?.Invoke();
}
