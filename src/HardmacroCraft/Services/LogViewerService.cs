using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.16.8：日志查看器（PCL / Prism / Modrinth App 标配）—— 汇总游戏根与隔离版本的
/// logs/*.log(.gz) 与 crash-reports/*.txt，按修改时间倒序；读取（.gz 自动解压，
/// 超大文件只取尾部防卡死）；一键上传 mclo.gs 生成分享链接（求助帖标配）。
/// 目录扫描 / 读取为纯文件系统逻辑；上传注入 HttpClient，headless 可测。
/// </summary>
public static class LogViewerService
{
    /// <summary>Kind：log = 运行日志，crash = 崩溃报告。</summary>
    public sealed record LogFileInfo(string Path, DateTime Modified, long SizeBytes, string Kind)
    {
        public string Display =>
            $"{(Kind == "crash" ? "[崩溃] " : "")}{System.IO.Path.GetFileName(Path)}  " +
            $"({Modified:yyyy-MM-dd HH:mm}, {SizeBytes / 1024.0:0.#} KB)";
    }

    /// <summary>全部候选日志目录（游戏根与隔离版本的 logs / crash-reports）。</summary>
    public static List<string> FindLogDirs(string? rootOverride = null)
    {
        var root = rootOverride ?? AppSettings.Instance.GameRoot;
        var dirs = new List<string> { Path.Combine(root, "logs"), Path.Combine(root, "crash-reports") };
        var versionsDir = Path.Combine(root, "versions");
        try
        {
            if (Directory.Exists(versionsDir))
                dirs.AddRange(Directory.GetDirectories(versionsDir)
                    .SelectMany(v => new[] { Path.Combine(v, "logs"), Path.Combine(v, "crash-reports") }));
        }
        catch { /* versions 目录不可读时忽略隔离版本 */ }
        return dirs.Where(Directory.Exists).Distinct().ToList();
    }

    /// <summary>收集全部日志（修改时间倒序，新在前；limit 防止老玩家上千条日志卡 UI）。</summary>
    public static List<LogFileInfo> ListLogs(string? rootOverride = null, int limit = 200)
    {
        var files = new List<LogFileInfo>();
        foreach (var dir in FindLogDirs(rootOverride))
        {
            var kind = System.IO.Path.GetFileName(dir).Equals("crash-reports", StringComparison.OrdinalIgnoreCase)
                ? "crash" : "log";
            try
            {
                files.AddRange(Directory.GetFiles(dir)
                    .Where(f => kind == "crash"
                        ? f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
                        : f.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".log.gz", StringComparison.OrdinalIgnoreCase))
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.Exists)
                    .Select(fi => new LogFileInfo(fi.FullName, fi.LastWriteTime, fi.Length, kind)));
            }
            catch { /* 该目录不可读时跳过 */ }
        }
        return files.OrderByDescending(f => f.Modified).Take(limit).ToList();
    }

    /// <summary>
    /// 读取日志：.log.gz 自动 gzip 解压；超过 maxBytes 的只保留尾部（启动日志头部
    /// 通常无用，报错在尾部），前缀一行省略提示。返回文本（UTF-8，非法字节替换）。
    /// </summary>
    public static string ReadLog(string path, int maxBytes = 256 * 1024)
    {
        var raw = File.ReadAllBytes(path);
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
            {
                gz.CopyTo(ms);
            }
            raw = ms.ToArray();
        }
        if (raw.Length <= maxBytes) return Encoding.UTF8.GetString(raw);
        var tail = Encoding.UTF8.GetString(raw[^maxBytes..]);
        var nl = tail.IndexOf('\n');
        if (nl >= 0) tail = tail[(nl + 1)..]; // 掐掉截断的半行
        var sizeText = maxBytes >= 1024 ? $"{maxBytes / 1024} KB" : $"{maxBytes} 字节";
        return $"…（日志过大，仅显示最后 {sizeText}；完整文件请用「打开位置」）\n{tail}";
    }

    /// <summary>
    /// 上传到 mclo.gs（Minecraft 社区通用的日志分享服务）：POST form content →
    /// JSON {"success":true,"url":"https://mclo.gs/xxxx","key":"xxxx"}。
    /// success=false 抛出携带 error 的异常；返回分享 URL。
    /// </summary>
    public static async Task<string> UploadToMclogsAsync(HttpClient http, string content)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["content"] = content });
        using var resp = await http.PostAsync("https://api.mclo.gs/1/log", form);
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var ok = doc.RootElement.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
        if (!ok)
        {
            var err = doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
            throw new InvalidOperationException($"mclo.gs 上传失败：{err ?? body}");
        }
        if (doc.RootElement.TryGetProperty("url", out var u) && u.GetString() is { Length: > 0 } url)
            return url;
        var key = doc.RootElement.TryGetProperty("key", out var k) ? k.GetString() : null;
        return string.IsNullOrEmpty(key) ? throw new InvalidOperationException("mclo.gs 响应缺少 url/key") : $"https://mclo.gs/{key}";
    }
}
