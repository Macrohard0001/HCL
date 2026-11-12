using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HardmacroCraft.Services;

/// <summary>
/// 全局平滑滚动引擎：滚轮不再瞬时跳变，而是按帧指数逼近目标偏移。
/// - 天然可中断：连续滚动只需更新目标值，不产生动画时钟堆叠
/// - 帧率无关收敛（每帧走剩余距离的 25%），手感与主流启动器一致
/// - 用户拖动滚动条时自动让位（取消平滑目标）
/// </summary>
public static class SmoothScroll
{
    private const double ApproachRatio = 0.25;   // 每帧逼近比例
    private const double FinishThreshold = 0.6;  // 距目标小于该像素值即收敛

    private static readonly Dictionary<ScrollViewer, double> Targets = new();
    private static readonly HashSet<ScrollViewer> Hooked = new();
    private static bool _ticking;

    /// <summary>平滑滚动到指定偏移（自动夹紧到 [0, ScrollableHeight]）。</summary>
    public static void ScrollTo(ScrollViewer sv, double target)
    {
        target = Math.Clamp(target, 0, sv.ScrollableHeight);
        if (sv.ScrollableHeight <= 0 || Math.Abs(target - sv.VerticalOffset) < 0.5) return;

        lock (Targets)
        {
            Hook(sv);
            Targets[sv] = target;
            if (!_ticking)
            {
                _ticking = true;
                CompositionTarget.Rendering += Tick;
            }
        }
    }

    /// <summary>取消某个滚动器的平滑目标（用户接管时调用）。</summary>
    public static void Cancel(ScrollViewer sv)
    {
        lock (Targets) Targets.Remove(sv);
    }

    private static void Hook(ScrollViewer sv)
    {
        if (!Hooked.Add(sv)) return;
        // 任何滚动事件（拖动滚动条 / 点轨道 / 键盘）都视为用户接管，取消平滑目标；
        // 我们自己 ScrollToVerticalOffset 产生的回调只会是 EndScroll，不在此列
        sv.AddHandler(ScrollBar.ScrollEvent, new ScrollEventHandler(OnUserScroll), handledEventsToo: true);
        sv.Unloaded += (_, _) => Cancel(sv);
    }

    private static void OnUserScroll(object sender, ScrollEventArgs e)
    {
        if (e.ScrollEventType != ScrollEventType.EndScroll)
            Cancel((ScrollViewer)sender);
    }

    private static void Tick(object? sender, EventArgs e)
    {
        List<KeyValuePair<ScrollViewer, double>> snapshot;
        lock (Targets) snapshot = Targets.ToList();

        if (snapshot.Count == 0)
        {
            _ticking = false;
            CompositionTarget.Rendering -= Tick;
            return;
        }

        foreach (var (sv, target) in snapshot)
        {
            var current = sv.VerticalOffset;
            var next = current + (target - current) * ApproachRatio;
            if (Math.Abs(target - next) < FinishThreshold || Math.Abs(next - current) < 0.05)
            {
                next = target;
                lock (Targets) Targets.Remove(sv);
            }
            sv.ScrollToVerticalOffset(next);
        }
    }
}
