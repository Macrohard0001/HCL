using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>
/// v0.16.7：速通工具箱（独立窗口）——
/// ① 计时器：空格 开始/暂停，分段记录（R 重置）；② 传送门坐标 1:8 双向换算；
/// ③ 末影之眼三角定位（两次投掷点 + F3 yaw → 要塞交汇点）；④ 珍珠/烈焰棒计数。
/// 纯数学走 SpeedrunUtil（headless 可测）。
/// </summary>
public partial class SpeedrunTool : UserControl
{
    private readonly Stopwatch _watch = new();
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private int _pearls, _rods;

    public SpeedrunTool()
    {
        InitializeComponent();
        _timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(31), // ~30fps 计时刷新足够顺滑
        };
        _timer.Tick += (_, _) => UpdateTimerText();
        PreviewKeyDown += Speedrun_PreviewKeyDown;
    }

    // ---- 计时器 ----

    private void UpdateTimerText()
    {
        TxtTimer.Text = SpeedrunUtil.FormatTimer(_watch.Elapsed);
    }

    private void BtnTimerStart_Click(object sender, RoutedEventArgs e)
    {
        if (_watch.IsRunning)
        {
            _watch.Stop();
            _timer.Stop();
            BtnTimerStart.Content = (string?)TryFindResource("S.Tool.SpeedStart") ?? "开始";
        }
        else
        {
            _watch.Start();
            _timer.Start();
            BtnTimerStart.Content = (string?)TryFindResource("S.Tool.SpeedPause") ?? "暂停";
        }
    }

    private void BtnTimerLap_Click(object sender, RoutedEventArgs e)
    {
        if (_watch.ElapsedTicks == 0) return;
        var line = SpeedrunUtil.FormatTimer(_watch.Elapsed);
        TxtLaps.Text = string.IsNullOrEmpty(TxtLaps.Text)
            ? line
            : TxtLaps.Text + "\n" + line;
    }

    private void BtnTimerReset_Click(object sender, RoutedEventArgs e)
    {
        _watch.Reset();
        _timer.Stop();
        BtnTimerStart.Content = (string?)TryFindResource("S.Tool.SpeedStart") ?? "开始";
        TxtLaps.Text = "";
        UpdateTimerText();
    }

    private void Speedrun_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // 空格 = 开始/暂停（速通热键）；R = 重置。输入框聚焦时放行打字。
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;
        if (e.Key == Key.Space)
        {
            BtnTimerStart_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (e.Key == Key.R)
        {
            BtnTimerReset_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    // ---- 传送门坐标换算 ----

    private void Portal_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 主世界 → 下界（÷8）
        if (int.TryParse(TxtOwX.Text.Trim(), out var owX) && int.TryParse(TxtOwZ.Text.Trim(), out var owZ))
        {
            TxtPortalToNether.Text = SpeedrunUtil.PortalConvert(owX, false) + ", " +
                                     SpeedrunUtil.PortalConvert(owZ, false);
        }
        else TxtPortalToNether.Text = "";
        // 下界 → 主世界（×8）
        if (int.TryParse(TxtNeX.Text.Trim(), out var neX) && int.TryParse(TxtNeZ.Text.Trim(), out var neZ))
        {
            TxtPortalToOverworld.Text = SpeedrunUtil.PortalConvert(neX, true) + ", " +
                                        SpeedrunUtil.PortalConvert(neZ, true);
        }
        else TxtPortalToOverworld.Text = "";
    }

    // ---- 末影之眼三角定位 ----

    private void BtnEyesCalc_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadThrow(TxtE1X, TxtE1Z, TxtE1Yaw, out var t1) ||
            !TryReadThrow(TxtE2X, TxtE2Z, TxtE2Yaw, out var t2))
        {
            TxtEyesResult.Text = (string?)TryFindResource("S.Tool.EyesInvalid")
                ?? "请填写两组投掷数据：X、Z 坐标和 F3 的 yaw 角度。";
            return;
        }
        var hit = SpeedrunUtil.Triangulate(t1, t2);
        TxtEyesResult.Text = hit is null
            ? (string?)TryFindResource("S.Tool.EyesParallel")
                ?? "两条投掷方向接近平行（或重合）——请走到另一个位置再投一次。"
            : string.Format(
                (string?)TryFindResource("S.Tool.EyesResult") ?? "要塞 ≈ ({0}, {1})\n距投掷点 1：{2} 格 · 距投掷点 2：{3} 格",
                (int)Math.Round(hit.Value.X), (int)Math.Round(hit.Value.Z),
                (int)Math.Round(hit.Value.Dist1), (int)Math.Round(hit.Value.Dist2));
    }

    private bool TryReadThrow(TextBox tbX, TextBox tbZ, TextBox tbYaw,
        out (double X, double Z, double Yaw) t)
    {
        t = default;
        return double.TryParse(tbX.Text.Trim(), out t.X)
               && double.TryParse(tbZ.Text.Trim(), out t.Z)
               && double.TryParse(tbYaw.Text.Trim(), out t.Yaw);
    }

    // ---- 资源计数器 ----

    private void BtnPearlPlus_Click(object sender, RoutedEventArgs e) { _pearls++; RefreshCounts(); }
    private void BtnPearlMinus_Click(object sender, RoutedEventArgs e) { if (_pearls > 0) _pearls--; RefreshCounts(); }
    private void BtnRodPlus_Click(object sender, RoutedEventArgs e) { _rods++; RefreshCounts(); }
    private void BtnRodMinus_Click(object sender, RoutedEventArgs e) { if (_rods > 0) _rods--; RefreshCounts(); }

    private void RefreshCounts()
    {
        TxtPearls.Text = _pearls.ToString();
        TxtRods.Text = _rods.ToString();
        // 速通参考：1 烈焰棒 = 2 粉（Brewing 需 1 粉/眼）；要 8 眼 ≥ 8 珍珠 + 4 烈焰棒
        var eyes = Math.Min(_pearls, _rods * 2);
        TxtCountAdvice.Text = string.Format(
            (string?)TryFindResource("S.Tool.CountAdvice") ?? "可合成 {0} 颗末影之眼（珍珠 {1} / 烈焰棒 {2}）——速通一般需要 8 颗左右。",
            eyes, _pearls, _rods);
    }
}
