namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.11 启动进度统一计算（纯逻辑，进测试）—— 修复「启动时进度条乱蹦」。
/// 根因：下载文件阶段同时存在两把不同刻度的进度尺 ——
///   1) 字节尺（已下载字节 / 总字节，多线程预取）；
///   2) 文件尺（已完成文件数 / 总文件数，内置安装器逐文件上报）。
/// 两把尺交替写入进度条时百分比来回横跳（一个大 jar 顶几千个小文件）。
/// 规则（对齐主流启动器的单一进度源思路）：
///   - 字节尺有效（总量稳定且未回退）时永远优先字节尺；
///   - 字节尺失效（逐文件上报导致总量频繁变化 / 进度回退）时回退文件尺；
///   - 显示值按平滑公式逼近真实值：上升每拍靠拢 20%+0.5%，下降快速滑落；
///   - 显示值恒不超过真实值（不提前「装完」）。
/// </summary>
public sealed class LaunchProgressCalc
{
    private long _byteTotal = -1;
    private long _byteDone;
    private long _filesTotal;
    private long _filesDone;
    private int _stableByteCount;
    private bool _byteReliable;

    /// <summary>文件进度上报（内置安装器 / 预取文件计数）。</summary>
    public void OnFiles(int done, int total)
    {
        if (total > 0) { _filesTotal = total; _filesDone = Math.Max(0, done); }
    }

    /// <summary>字节进度上报（预取 / 安装器字节累计）。总量变化或回退时字节尺降级。</summary>
    public void OnBytes(long done, long total)
    {
        if (total <= 0) return;
        if (total != _byteTotal)
        {
            // 总量变了：多半是切换到了另一个文件（逐文件上报）→ 字节尺降级，
            // 直到同一总量下连续稳定上报 2 次才恢复信任。
            _byteTotal = total;
            _byteDone = Math.Max(0, done);
            _byteReliable = false;
            _stableByteCount = 0;
        }
        else if (done < _byteDone)
        {
            // 同总量但进度回退 → 服务器不支持续传重下等场景，字节尺降级
            _byteReliable = false;
            _stableByteCount = 0;
            _byteDone = Math.Max(0, done);
        }
        else
        {
            _byteDone = done;
            if (_stableByteCount < 2) _stableByteCount++;
            if (_stableByteCount >= 2) _byteReliable = true;
        }
    }

    /// <summary>
    /// 计算当前真实百分比（0-100）。字节尺可靠时用字节尺，否则用文件尺；
    /// 两把尺都没有数据时返回 null（调用方维持不确定进度）。
    /// </summary>
    public double? Compute()
    {
        if (_byteReliable && _byteTotal > 0)
            return Math.Clamp(_byteDone * 100.0 / _byteTotal, 0, 100);
        if (_filesTotal > 0)
            return Math.Clamp(_filesDone * 100.0 / _filesTotal, 0, 100);
        return null;
    }

    /// <summary>字节尺当前是否为主进度来源（决定文案以速度 / 字节为主还是文件计数为主）。</summary>
    public bool ByteDominant => _byteReliable && _byteTotal > 0;

    /// <summary>
    /// 显示平滑：显示值向真实值靠拢，永不越过真实值。
    /// 上升：每拍靠拢剩余距离的 20% + 0.5%（启动快、接近真实值时收敛）；
    /// 下降：按 35% 快速滑落（阶段切换 / 重新校验时平滑过渡，不生硬回跳）。
    /// </summary>
    public static double Smooth(double shown, double actual)
    {
        shown = Math.Clamp(shown, 0, 100);
        actual = Math.Clamp(actual, 0, 100);
        if (actual >= shown)
        {
            var next = shown + (actual - shown) * 0.2 + 0.005;
            return Math.Min(next, actual);
        }
        var back = shown + (actual - shown) * 0.35;
        return Math.Max(actual, back);
    }
}
