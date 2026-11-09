using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace HardmacroCraft.Services;

/// <summary>
/// v1.7.0 统一下载引擎：修复"下载的文件不全"。
/// - 一律先写 .hmc-part 临时文件，校验通过后才改名为正式文件（不会留半截文件被游戏读取）
/// - sha1 / 大小双重校验（整合包 index 自带 sha1）
/// - 失败自动重试（指数退避），并支持 HTTP Range 断点续传
/// - 批量模式：多线程并发 + 汇总进度（当前文件 / 完成数 / 总字节）
/// </summary>
public static class DownloadEngine
{
    private const string PartSuffix = ".hmc-part";

    // ---------- 单文件下载 ----------

    /// <summary>
    /// 下载单个文件（带重试 / 续传 / 校验）。校验失败或超限会抛出带原因的异常。
    /// </summary>
    public static async Task DownloadFileAsync(
        HttpClient http, string url, string dest,
        string? sha1 = null, long expectedSize = 0,
        IProgress<long>? byteProgress = null,
        CancellationToken ct = default, int maxRetries = 3)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var part = dest + PartSuffix;

        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var resumed = await TryDownloadOnceAsync(http, url, part, byteProgress, ct, expectedSize);
                // 校验：sha1 优先，其次大小
                var reason = await VerifyAsync(part, sha1, expectedSize, ct);
                if (reason != null)
                    throw new InvalidOperationException($"校验失败（{reason}）");
                File.Move(part, dest, overwrite: true);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // 用户取消不重试
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < maxRetries)
                {
                    // 指数退避后重试（part 文件保留用于续传）
                    await Task.Delay(TimeSpan.FromMilliseconds(600 * Math.Pow(2, attempt - 1)), ct);
                }
            }
        }

        // 全部重试失败：清理 part，抛出明确错误
        try { File.Delete(part); } catch { }
        throw new IOException($"下载失败（已重试 {maxRetries} 次）：{Path.GetFileName(dest)} - {lastError?.Message}");
    }

    /// <summary>执行一次下载；若 part 已存在且服务器支持 Range 则断点续传。</summary>
    private static async Task<bool> TryDownloadOnceAsync(
        HttpClient http, string url, string part,
        IProgress<long>? byteProgress, CancellationToken ct, long expectedSize = 0)
    {
        long resumeFrom = 0;
        if (File.Exists(part))
        {
            var partLen = new FileInfo(part).Length;
            // v0.1.2 修复：part 已达 / 超过目标大小（上次下载后校验前中断等）时，
            // Range 请求会得到 416 或空响应，重试 3 次同因失败 —— 直接丢弃 part 从头下。
            if (expectedSize > 0 && partLen >= expectedSize)
            {
                try { File.Delete(part); } catch { }
            }
            else
            {
                resumeFrom = partLen;
            }
        }

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = resumeFrom > 0
            ? new System.Net.Http.Headers.RangeHeaderValue(resumeFrom, null)
            : null;

        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resumeFrom > 0 && resp.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            // 服务器不支持续传 → 从头下。
            // v0.1.2 修复：此前已实时上报的字节增量不会自动回退，重下后进度会重复累计
            // 超过 100%（批量汇总 doneBytes 直接膨胀）—— 这里报一个负增量抵消。
            if (byteProgress != null && resumeFrom > 0)
                byteProgress.Report(-resumeFrom);
            resumeFrom = 0;
            resp.EnsureSuccessStatusCode();
        }
        else if (resumeFrom == 0)
        {
            resp.EnsureSuccessStatusCode();
        }

        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = new FileStream(part,
            resumeFrom > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 81920);

        var buf = new byte[1 << 16];
        int read;
        while ((read = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, read), ct);
            byteProgress?.Report(read);
        }
        await dst.FlushAsync(ct);
        return true;
    }

    /// <summary>校验已下载文件，返回 null = 通过，否则返回失败原因描述。</summary>
    private static async Task<string?> VerifyAsync(string file, string? sha1, long expectedSize, CancellationToken ct)
    {
        var fi = new FileInfo(file);
        if (expectedSize > 0 && fi.Length != expectedSize)
            return $"大小不符 {fi.Length} != {expectedSize}";
        if (!string.IsNullOrEmpty(sha1))
        {
            await using var fs = fi.OpenRead();
            var hash = await SHA1.HashDataAsync(fs, ct);
            var hex = Convert.ToHexString(hash).ToLowerInvariant();
            if (!hex.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                return $"sha1 不符 {hex[..8]}… != {sha1[..8]}…";
        }
        else if (expectedSize <= 0 && fi.Length == 0)
        {
            return "文件为空";
        }
        return null;
    }

    /// <summary>快速判断已有文件是否完整（sha1 或大小），用于跳过重复下载。</summary>
    public static async Task<bool> IsFileCompleteAsync(string file, string? sha1, long expectedSize, CancellationToken ct = default)
    {
        if (!File.Exists(file)) return false;
        try
        {
            return await VerifyAsync(file, sha1, expectedSize, ct) == null;
        }
        catch { return false; }
    }

    // ---------- 批量并发下载 ----------

    public class BatchItem
    {
        public string Url { get; set; } = "";
        public string Dest { get; set; } = "";        // 绝对路径
        public string DisplayName { get; set; } = ""; // 进度显示用（通常是相对路径或文件名）
        public string? Sha1 { get; set; }
        public long Size { get; set; }
    }

    public record BatchProgress(string CurrentFile, int DoneFiles, int TotalFiles, long DoneBytes, long TotalBytes);

    /// <summary>
    /// 并发下载一批文件。单个文件失败不中断整批，最后汇总抛出（含失败清单）。
    /// skipped 集合返回被跳过（已完整）的文件名。
    /// </summary>
    public static async Task DownloadBatchAsync(
        HttpClient http, IReadOnlyList<BatchItem> items, int parallelism,
        IProgress<BatchProgress>? progress, Action<string>? log,
        CancellationToken ct = default)
    {
        var totalBytes = items.Sum(i => Math.Max(0, i.Size));
        var doneBytes = 0L;
        var doneFiles = 0;
        var currentFile = "";
        var failed = new List<string>();
        var skipped = new List<string>();
        var gate = new SemaphoreSlim(Math.Max(1, parallelism));
        var sync = new object();

        void Report(string current)
        {
            lock (sync)
            {
                currentFile = current;
                progress?.Report(new BatchProgress(currentFile, doneFiles, items.Count, doneBytes, totalBytes));
            }
        }

        var tasks = items.Select(item => Task.Run(async () =>
        {
            await gate.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                // 已完整 → 跳过（校验已有文件，杜绝上次留下的半截文件混进游戏目录）
                if (await IsFileCompleteAsync(item.Dest, item.Sha1, item.Size, ct))
                {
                    lock (sync)
                    {
                        doneFiles++;
                        doneBytes += Math.Max(0, item.Size);
                        skipped.Add(item.DisplayName);
                    }
                    Report($"跳过 {item.DisplayName}");
                    return;
                }

                Report(item.DisplayName);
                log?.Invoke($"下载 {item.DisplayName}");

                // 进度折算：字节增量直接汇总到总进度
                var fileProgress = new Progress<long>(delta =>
                {
                    lock (sync) doneBytes += delta;
                });

                try
                {
                    await DownloadFileAsync(http, item.Url, item.Dest, item.Sha1, item.Size, fileProgress, ct);
                }
                catch (Exception ex)
                {
                    lock (sync) failed.Add($"{item.DisplayName}: {ex.Message}");
                    return;
                }

                lock (sync) doneFiles++;
                Report(item.DisplayName);
            }
            finally
            {
                gate.Release();
            }
        }, ct)).ToList();

        await Task.WhenAll(tasks);

        // 终态汇总：保证最后一条进度回调一定是全量完成态（并发交错时中间报告顺序不定）
        progress?.Report(new BatchProgress(currentFile, doneFiles, items.Count, doneBytes, totalBytes));

        if (failed.Count > 0)
            throw new InvalidOperationException(
                $"有 {failed.Count} 个文件下载失败：\n- " + string.Join("\n- ", failed.Take(8))
                + (failed.Count > 8 ? $"\n- …等共 {failed.Count} 个" : ""));
    }
}

/// <summary>下载速度计算 + 字节数格式化（进度文案用）。</summary>
public sealed class SpeedMeter
{
    private long _lastBytes;
    private DateTime _lastTime = DateTime.UtcNow;
    private double _speedBps;

    /// <summary>传入累计字节，返回估算速度（字节/秒）。间隔过短时沿用上次估算避免抖动。</summary>
    public double Observe(long totalSoFar)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTime).TotalSeconds;
        if (dt < 0.4) return _speedBps;
        var db = totalSoFar - _lastBytes;
        if (db >= 0) _speedBps = db / dt;
        _lastBytes = totalSoFar;
        _lastTime = now;
        return _speedBps;
    }

    public static string FormatBytes(long b)
    {
        return b switch
        {
            >= 1L << 30 => $"{b / 1024.0 / 1024 / 1024:F2} GB",
            >= 1L << 20 => $"{b / 1024.0 / 1024:F1} MB",
            >= 1L << 10 => $"{b / 1024.0:F0} KB",
            _ => $"{b} B",
        };
    }

    public static string FormatSpeed(double bps)
    {
        return bps switch
        {
            >= 1L << 20 => $"{bps / 1024 / 1024:F1} MB/s",
            >= 1L << 10 => $"{bps / 1024:F0} KB/s",
            > 0 => $"{bps:F0} B/s",
            _ => "",
        };
    }
}
