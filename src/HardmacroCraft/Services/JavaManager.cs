using System.Net.Http;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// Java 自动管理（PCL 的"Java 自动管理"同款）：
/// - 按 Minecraft 版本解析所需 Java 大版本（版本 json 的 javaVersion.majorVersion，
///   缺失时按版本名启发式：1.17 → 16，1.18–1.20.4 → 17，1.20.5+ → 21，其余 → 8）；
/// - 从 Adoptium（Eclipse Temurin JRE）下载指定大版本的 Windows x64 运行时到
///   exe 同目录 runtime/java-{major}/（便携化，不写系统目录）；
///   优先清华 TUNA 镜像，失败回退 GitHub 官方发布，下载后校验 SHA-256；
/// - 启动时自动挑选最合适的 Java：用户手填路径 &gt; 已托管运行时（主版本达标且最接近）
///   &gt; 交回 CmlLib 默认检测。
/// </summary>
public static class JavaManager
{
    public const int LatestSupportedMajor = 25;

    /// <summary>托管运行时根目录：设置自定义的 JavaRuntimeDir，否则 exe 同目录 runtime/。</summary>
    public static string RuntimeRoot =>
        string.IsNullOrWhiteSpace(AppSettings.Instance.JavaRuntimeDir)
            ? Path.Combine(AppSettings.AppDir, "runtime")
            : AppSettings.Instance.JavaRuntimeDir;

    public static string ManagedDir(int major) => Path.Combine(RuntimeRoot, "java-" + major);

    // ---------- 版本 → Java 大版本 ----------

    /// <summary>从版本 json 文本解析所需 Java 大版本；解析失败时用版本名启发式。</summary>
    public static int RequiredJavaMajor(string? versionJson, string versionName)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(versionJson))
            {
                using var doc = JsonDocument.Parse(versionJson);
                if (doc.RootElement.TryGetProperty("javaVersion", out var jv)
                    && jv.TryGetProperty("majorVersion", out var maj)
                    && maj.TryGetInt32(out var m) && m > 0)
                    return m;
            }
        }
        catch { /* json 损坏走启发式 */ }
        return RequiredJavaMajorByName(versionName);
    }

    /// <summary>版本名启发式（官方版本命名规则）。</summary>
    public static int RequiredJavaMajorByName(string versionName)
    {
        var (maj, min, patch, ok) = ParseMinecraftVersion(versionName);
        if (!ok) return 8;
        // 26.x 起新版本化方案（2026 年度版本）需要 Java 25（实测 26.3 启动链）
        if (maj >= 26) return 25;
        if (maj > 1 || min > 20 || (min == 20 && patch >= 5)) return 21;
        if (min >= 18) return 17;
        if (min == 17) return 16;
        return 8;
    }

    /// <summary>解析 "1.21.4" / "1.20" 形式的版本号；无法解析返回 ok=false。</summary>
    public static (int Major, int Minor, int Patch, bool Ok) ParseMinecraftVersion(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return (0, 0, 0, false);
        // 取形如 N.N(.N) 的前缀（容错 forge/fabric 后缀：如 "1.20.1-forge-..."）
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(\d+)(?:\.(\d+))?(?:\.(\d+))?");
        if (!m.Success) return (0, 0, 0, false);
        var maj = int.Parse(m.Groups[1].Value);
        var min = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        var patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return (maj, min, patch, true);
    }

    /// <summary>读取本地版本 json（versions/&lt;name&gt;/&lt;name&gt;.json），不存在返回 null。</summary>
    public static string? ReadVersionJson(string gameRoot, string versionName)
    {
        var f = Path.Combine(gameRoot, "versions", versionName, versionName + ".json");
        return File.Exists(f) ? File.ReadAllText(f) : null;
    }

    /// <summary>
    /// 沿 inheritsFrom 链解析所需 Java 大版本（fix5）：子版本 json（forge / fabric / 改名版本）
    /// 常常不含 javaVersion，需要回溯父版本；全部失败时退回版本名启发式。
    /// </summary>
    public static int RequiredJavaMajorChain(string gameRoot, string versionName)
    {
        var current = versionName;
        for (var hop = 0; hop < 6 && !string.IsNullOrWhiteSpace(current); hop++)
        {
            var json = ReadVersionJson(gameRoot, current);
            if (json == null) break;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("javaVersion", out var jv)
                    && jv.TryGetProperty("majorVersion", out var maj)
                    && maj.TryGetInt32(out var m) && m > 0)
                    return m;
                // 本层没有 javaVersion：跟 inheritsFrom 找父版本
                if (root.TryGetProperty("inheritsFrom", out var inherits)
                    && inherits.GetString() is { Length: > 0 } parent
                    && parent != current)
                {
                    current = parent;
                    continue;
                }
                break;
            }
            catch { break; }
        }
        return RequiredJavaMajorByName(versionName);
    }

    /// <summary>
    /// 解析用户填写的 Java 路径（fix5）：可以是 javaw.exe / java.exe 文件，
    /// 也可以是任意层级的文件夹（自动递归搜 javaw.exe）。找不到返回 null。
    /// </summary>
    public static string? ResolveJavaPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(full))
            {
                var name = Path.GetFileName(full).ToLowerInvariant();
                if (name is "javaw.exe" or "java.exe")
                {
                    // java.exe 也优先换成同目录的 javaw.exe（后台窗口模式）
                    var sibling = Path.Combine(Path.GetDirectoryName(full)!, "javaw.exe");
                    return File.Exists(sibling) ? sibling : full;
                }
                return name.EndsWith(".exe") ? full : null;
            }
            if (Directory.Exists(full)) return FindJavaw(full);
        }
        catch { /* 非法路径 */ }
        return null;
    }

    /// <summary>
    /// 探测指定 javaw.exe 的 Java 大版本（运行 javaw -version 解析输出；失败返回 0）。
    /// 用于手选 Java 时校验版本是否达标（fix5：26.3 手选 Java 25 仍被提示装 Java 的防护）。
    /// </summary>
    public static int GetJavaMajor(string javawPath)
    {
        try
        {
            using var p = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(javawPath, "-version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                });
            if (p == null) return 0;
            var output = p.StandardError.ReadToEnd() + p.StandardOutput.ReadToEnd();
            p.WaitForExit(15000);
            // 输出形如 openjdk version "25.0.1" / "1.8.0_412"
            var m = System.Text.RegularExpressions.Regex.Match(output, @"version ""(\d+)(?:\.(\d+))?");
            if (!m.Success) return 0;
            var a = int.Parse(m.Groups[1].Value);
            if (a != 1) return a;
            return m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 8;
        }
        catch { return 0; }
    }

    // ---------- 托管运行时 ----------

    /// <summary>已安装的托管 Java：runtime/java-*/ 下含 bin/javaw.exe 的目录。</summary>
    public static List<(int Major, string JavaPath)> FindManagedJavas()
    {
        var result = new List<(int, string)>();
        try
        {
            if (!Directory.Exists(RuntimeRoot)) return result;
            foreach (var dir in Directory.GetDirectories(RuntimeRoot, "java-*"))
            {
                var exe = FindJavaw(dir);
                if (exe != null)
                {
                    var namePart = Path.GetFileName(dir);
                    var numPart = new string(namePart.SkipWhile(c => !char.IsDigit(c)).ToArray());
                    _ = int.TryParse(numPart, out var major);
                    result.Add((major <= 0 ? GuessMajorFromPath(exe) : major, exe));
                }
            }
        }
        catch { /* 扫描失败视为无托管 Java */ }
        return result;
    }

    private static int GuessMajorFromPath(string javaw)
    {
        var m = System.Text.RegularExpressions.Regex.Match(javaw, @"jdk-(\d+)|java-(\d+)|(\d+)U?");
        foreach (var g in new[] { m.Groups[1], m.Groups[2], m.Groups[3] })
            if (g.Success && int.TryParse(g.Value, out var n)) return n;
        return 0;
    }

    /// <summary>递归查找目录中的 javaw.exe（Adoptium 解压后多一层版本目录，直接拍平查找）。</summary>
    public static string? FindJavaw(string dir)
    {
        try
        {
            var direct = Path.Combine(dir, "bin", "javaw.exe");
            if (File.Exists(direct)) return direct;
            return Directory.GetFiles(dir, "javaw.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
        }
        catch { return null; }
    }

    /// <summary>
    /// PATH 中是否存在 java.exe（最后的兜底：直接用系统 Java）。
    /// v0.1.3：此前无 Java 时启动报"系统找不到指定的文件"，用户无从下手——
    /// 现在启动前先检查，缺 Java 直接引导安装而不是甩出文件系统异常。
    /// </summary>
    public static bool ExistsOnPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var d = dir.Trim().Trim('"');
                if (d.Length == 0) continue;
                if (File.Exists(Path.Combine(d, "java.exe"))) return true;
                if (File.Exists(Path.Combine(d, "javaw.exe"))) return true;
            }
            catch { /* 单段 PATH 异常不阻断整体 */ }
        }
        return false;
    }

    /// <summary>
    /// 挑选启动用 Java：用户手填 &gt; 托管运行时（主版本达标且最接近所需版本）。
    /// 返回 null 表示交给 CmlLib 默认检测。
    /// </summary>
    public static string? PickJavaFor(string versionName, string gameRoot,
        string? userJavaPath, bool autoSelect)
    {
        if (!string.IsNullOrWhiteSpace(userJavaPath)) return null; // 用户手动指定优先（原样交给调用方）
        if (!autoSelect) return null;

        var required = RequiredJavaMajor(ReadVersionJson(gameRoot, versionName), versionName);
        // 达标的托管 Java 中取主版本最接近的（21 达标给 17 需求时优先 17 而不是 21）
        var candidates = FindManagedJavas()
            .Where(j => j.Item1 >= required)
            .OrderBy(j => j.Item1)
            .ToList();
        return candidates.Count > 0 ? candidates[0].Item2 : null;
    }

    // ---------- 下载 ----------

    /// <summary>单个 JRE 下载任务的描述（供任务中心 / 设置页展示）。</summary>
    public sealed record JreAsset(string FileName, string MirrorUrl, string OfficialUrl, string Sha256, long SizeBytes);

    /// <summary>查询 Adoptium 最新 JRE 元数据；MirrorUrl 指向清华 TUNA 镜像。</summary>
    public static async Task<JreAsset> GetLatestJreAsync(int major, HttpClient http)
    {
        var url = $"https://api.adoptium.net/v3/assets/latest/{major}/hotspot" +
                  "?architecture=x64&image_type=jre&os=windows";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == 0)
            throw new Exception($"Adoptium 未提供 Java {major} 的 Windows x64 JRE。");
        var first = root[0];
        var pkg = first.GetProperty("binary").GetProperty("package");
        var fileName = pkg.GetProperty("name").GetString()
            ?? throw new Exception("Adoptium 响应缺少包文件名。");
        var link = pkg.GetProperty("link").GetString() ?? "";
        var sha = pkg.TryGetProperty("checksum", out var c) ? (c.GetString() ?? "") : "";
        var size = pkg.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
        // TUNA 镜像路径：/Adoptium/{major}/jre/x64/windows/{fileName}
        var mirror = $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{major}/jre/x64/windows/{fileName}";
        return new JreAsset(fileName, mirror, link, sha, size);
    }

    /// <summary>
    /// 下载并解压 JRE 到 runtime/java-{major}/（或 installDir 指定目录）。镜像优先、官方兜底，
    /// SHA-256 校验，重复下载幂等（目录已存在直接返回）。progress 为 0–100。
    /// fix7：支持 CancellationToken（任务中心「取消」按钮），取消后不再回退到下一个源。</summary>
    public static async Task<string> EnsureJreAsync(int major, HttpClient http,
        IProgress<double>? progress = null, Action<string>? log = null, string? installDir = null,
        CancellationToken ct = default)
    {
        var dest = string.IsNullOrWhiteSpace(installDir) ? ManagedDir(major) : installDir;
        var existing = FindJavaw(dest);
        if (existing != null) return existing;

        Directory.CreateDirectory(dest);
        // fix5 修复：zip 必须放在 dest 之外——此前 zip 在 dest 里，随后
        // Directory.Delete(dest) 会把 zip 一起删掉，解压必然失败（“Java 下载失败”的真因）
        var zipDir = Path.GetDirectoryName(dest);
        if (string.IsNullOrEmpty(zipDir)) zipDir = Path.GetTempPath();
        var zipPath = Path.Combine(zipDir, $"temurin-{major}-jre.zip");

        var asset = await GetLatestJreAsync(major, http);
        ct.ThrowIfCancellationRequested();
        log?.Invoke($"[Java] 获取到 Temurin JRE {major}: {asset.FileName}");

        if (File.Exists(zipPath))
        {
            try { File.Delete(zipPath); } catch { }
        }

        await DownloadWithFallbackAsync(asset, zipPath, http, progress, log, ct);
        ct.ThrowIfCancellationRequested();
        VerifySha256(zipPath, asset.Sha256);
        log?.Invoke("[Java] 校验通过（SHA-256），正在解压...");

        if (Directory.Exists(dest))
        {
            try { Directory.Delete(dest, true); } catch { }
        }
        Directory.CreateDirectory(dest);
        ZipFile.ExtractToDirectory(zipPath, dest, overwriteFiles: true);
        try { File.Delete(zipPath); } catch { }

        var javaw = FindJavaw(dest) ?? throw new Exception("解压后未找到 bin/javaw.exe。");
        log?.Invoke($"[Java] Java {major} 安装完成: {javaw}");
        return javaw;
    }

    private static async Task DownloadWithFallbackAsync(JreAsset asset, string dest,
        HttpClient http, IProgress<double>? progress, Action<string>? log, CancellationToken ct = default)
    {
        Exception? last = null;
        foreach (var (url, label) in new[] { (asset.MirrorUrl, "清华 TUNA 镜像"), (asset.OfficialUrl, "GitHub 官方") })
        {
            try
            {
                log?.Invoke($"[Java] 从{label}下载...");
                await DownloadFileAsync(url, dest, http, progress, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 用户取消：不再回退到下一个源，直接抛出（fix7）
                try { File.Delete(dest); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                log?.Invoke($"[Java] {label}下载失败: {ex.Message}");
                try { File.Delete(dest); } catch { }
            }
        }
        throw new Exception("JRE 下载失败（镜像与官方源均不可达）。", last);
    }

    private static async Task DownloadFileAsync(string url, string dest,
        HttpClient http, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
        var buffer = new byte[1 << 16];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report(read * 100.0 / total);
        }
    }

    private static void VerifySha256(string file, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected)) return;
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(file);
        var hash = sha.ComputeHash(stream);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(hex, expected.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            throw new Exception($"JRE 下载校验失败（SHA-256 不匹配）。\n期望 {expected}\n实际 {hex}");
    }
}
