using System.Net.Http;
using System.Net.Http.Json;
using System.IO;
using System.Reflection;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using CmlLib.Core.VersionMetadata;
using CmlLib.Core.VersionLoader;

namespace HardmacroCraft.Services;

/// <summary>
/// 启动核心：封装 CmlLib.Core，统一注入镜像 HttpClient、管理版本安装与启动。
/// </summary>
public class LauncherService : IDisposable
{
    public HttpClient Http { get; }
    private MinecraftLauncher? _launcher;
    private HttpClient? _directHttp;      // 直连官方（镜像失败时回退用）
    private MinecraftLauncher? _directLauncher;

    /// <summary>
    /// 全局安装闸门：CmlLib 的 GameInstallerBase 同一实例同时只允许一个安装任务，
    /// 并发时直接抛 "Already installing"（用户日志中的启动失败根因）。
    /// 此前装载器安装 / 启动补全 / 整合包安装都可能并发，任一任务未结束时再次启动即报错。
    /// 现在所有安装操作统一排队；超过 15 分钟仍未轮到则报友好错误而不是永远卡住。
    /// </summary>
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    /// <summary>v0.1.1：应用当前版本号（上报 / 校验缓存戳共用，改版本只改这一处）。</summary>
    public const string LauncherVersion = "0.1.2";

    private static async Task<T> RunInInstallGateAsync<T>(Func<Task<T>> work)
        => await RunInInstallGateAsync(CancellationToken.None, work);

    /// <summary>v0.1.1：支持取消 —— 等闸门期间用户点「取消」立即退出（以前会干等 15 分钟超时）。</summary>
    private static async Task<T> RunInInstallGateAsync<T>(CancellationToken ct, Func<Task<T>> work)
    {
        while (true)
        {
            if (await InstallGate.WaitAsync(TimeSpan.FromMinutes(15), ct)) break;
            throw new InvalidOperationException(
                "另一个安装任务超过 15 分钟仍未结束，本次操作已取消。可到任务中心查看进行中的任务，或重启启动器后重试。");
        }
        try { return await work(); }
        finally { InstallGate.Release(); }
    }

    private static Task RunInInstallGateAsync(Func<Task> work)
        => RunInInstallGateAsync<object?>(async () => { await work(); return null; });

    /// <summary>v0.1.1：Action 版 + 取消令牌（等待闸门期间可取消）。</summary>
    private static Task RunInInstallGateAsync(CancellationToken ct, Func<Task> work)
        => RunInInstallGateAsync<object?>(ct, async () => { await work(); return null; });

    public LauncherService()
    {
        Http = new HttpClient(new MirrorHttpHandler());
        Http.DefaultRequestHeaders.UserAgent.ParseAdd($"HardmacroCraftLauncher/{LauncherVersion}");
    }

    public string RootPath => AppSettings.Instance.GameRoot;

    /// <summary>
    /// 创建 MinecraftLauncher。参数对象私有构造，须用静态工厂 CreateDefault() 初始化
    /// VersionLoader / GameInstaller 等默认组件，再注入镜像 HttpClient 与游戏根目录。
    /// </summary>
    private MinecraftLauncher BuildLauncher(HttpClient? http = null)
    {
        var pType = typeof(MinecraftLauncherParameters);
        var parameters = (MinecraftLauncherParameters)pType
            .GetMethod("CreateDefault", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)!
            .Invoke(null, Array.Empty<object>())!;
        // 注意：CreateDefault() 内部会 new MinecraftPath()，其 Windows 默认路径是
        // "appdata 环境变量 + \.minecraft"，变量异常时退化为相对路径（落到盘符根目录）。
        // 必须立即覆盖为我们控制的便携目录，绝不能使用默认值：
        parameters.HttpClient = http ?? Http;
        var ownPath = new MinecraftPath(RootPath);
        parameters.MinecraftPath = ownPath;
        // %APPDATA%\.minecraft 污染根因（fix3）：CreateDefault() 创建的 VersionLoader
        // 绑定在一个一次性的 new MinecraftPath()（= %APPDATA%\.minecraft）上，
        // 每次拉版本清单都会把 version_manifest_v2.json 缓存写进那里并建出目录。
        // 这里显式用我们自己的游戏根目录重建 VersionLoader，清单缓存落回便携目录；
        // UseLocalManifestWhenError：镜像短暂不可达时用上次缓存的清单兜底而不是直接报错。
        parameters.VersionLoader = new MojangJsonVersionLoaderV2(ownPath, http ?? Http)
        {
            UseLocalManifestWhenError = true,
        };
        return new MinecraftLauncher(parameters);
    }

    public MinecraftLauncher Launcher => _launcher ??= BuildLauncher();

    /// <summary>所有可用版本（远程清单 + 本地已装版本）。镜像失败自动回退直连官方。</summary>
    public async Task<IEnumerable<IVersionMetadata>> GetAllVersionsAsync()
    {
        try
        {
            return await Launcher.GetAllVersionsAsync();
        }
        catch (Exception mirrorEx)
        {
            try
            {
                // BMCLAPI 不可达时直连 Mojang 官方清单
                _directHttp ??= new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(30),
                };
                _directHttp.DefaultRequestHeaders.UserAgent.ParseAdd($"HardmacroCraftLauncher/{LauncherVersion}");
                _directLauncher ??= BuildLauncher(_directHttp);
                return await _directLauncher.GetAllVersionsAsync();
            }
            catch (Exception directEx)
            {
                throw new Exception(
                    $"镜像与官方源均不可达。\n- BMCLAPI 镜像: {mirrorEx.Message}\n- 官方直连: {directEx.Message}");
            }
        }
    }

    public IVersionMetadata? FindLocal(IEnumerable<IVersionMetadata> versions, string name)
        => versions.FirstOrDefault(v => v.Name == name);

    /// <summary>安装原版 / 或安装已经存在于 versions 目录的任意版本（含装载器版本）。</summary>
    public Task InstallAsync(string versionName,
        IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<CmlLib.Core.ByteProgress>? byteProgress, CancellationToken ct = default)
        => RunInInstallGateAsync(() => InstallCoreAsync(versionName, fileProgress, byteProgress, ct));

    /// <summary>不带闸门的安装实现（供已持闸门的调用方复用，避免 SemaphoreSlim 嵌套死锁）。</summary>
    private Task InstallCoreAsync(string versionName,
        IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<CmlLib.Core.ByteProgress>? byteProgress, CancellationToken ct = default)
        => Launcher.InstallAsync(versionName, fileProgress, byteProgress, ct).AsTask();

    /// <summary>Forge 版本列表：BMCLAPI JSON 优先（国内直连 files.minecraftforge.net 常超时，
    /// "模组加载器获取失败"根因），失败回退 CmlLib 官方 HTML 抓取。</summary>
    public async Task<string[]> ForgeVersionsAsync(string mcVersion)
    {
        try
        {
            var json = await Http.GetStringAsync($"{ForgeMirror.BaseUrl}/forge/minecraft/{Uri.EscapeDataString(mcVersion)}");
            return ForgeMirror.ParseBmclapiVersionList(json);
        }
        catch { /* 镜像不可达 → 回退官方源 */ }
        var vers = await new CmlLib.Core.Installer.Forge.ForgeInstaller(Launcher, Http)
            .GetForgeVersions(mcVersion);
        return vers.Select(v => v.ForgeVersionName).Reverse().ToArray();
    }

    /// <summary>
    /// Forge 安装：直接构造 ForgeVersion 走 BMCLAPI maven 直链安装器（不再二次抓官方 HTML），
    /// 失败（如老版本分支命名差异）时回退 CmlLib 官方流程。支持进度与取消。
    /// </summary>
    public Task ForgeInstallAsync(string mcVersion, string forgeVersion,
        IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? fileProgress = null,
        IProgress<CmlLib.Core.ByteProgress>? byteProgress = null, CancellationToken ct = default)
        => RunInInstallGateAsync(async () =>
        {
            var options = new CmlLib.Core.Installer.Forge.ForgeInstallOptions
            {
                CancellationToken = ct,
                FileProgress = fileProgress,
                ByteProgress = byteProgress,
            };
            var fv = ForgeMirror.BuildForgeVersion(mcVersion, forgeVersion);
            var installer = new CmlLib.Core.Installer.Forge.ForgeInstaller(Launcher, Http);
            try
            {
                await installer.Install(fv, options);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            catch
            {
                // BMCLAPI 直链 404（老版本分支路径差异）等 → 官方流程兜底（内部自抓 HTML 列表）
                await installer.Install(mcVersion, forgeVersion);
            }
        });

    public Task NeoForgeInstallAsync(string mcVersion, string neoVersion, CancellationToken ct = default)
        => new CmlLib.Core.Installer.NeoForge.NeoForgeInstaller(Launcher, Http)
            .Install(mcVersion, neoVersion, new CmlLib.Core.Installer.Forge.ForgeInstallOptions
            {
                CancellationToken = ct,
            });

    public async Task<string[]> NeoForgeVersionsAsync(string mcVersion)
    {
        var vers = await new CmlLib.Core.Installer.NeoForge.NeoForgeInstaller(Launcher, Http)
            .GetNeoForgeVersions(mcVersion);
        return vers.Select(v => v.NeoForgeVersionName).Reverse().ToArray();
    }

    public async Task FabricInstallAsync(string mcVersion, string? loaderVersion, CancellationToken ct = default)
    {
        var ins = new CmlLib.Core.ModLoaders.FabricMC.FabricInstaller(Http);
        if (loaderVersion == null)
            await ins.Install(mcVersion, new MinecraftPath(RootPath));
        else
            await ins.Install(mcVersion, loaderVersion, new MinecraftPath(RootPath));
    }

    public async Task<string[]> FabricLoadersAsync(string mcVersion)
    {
        var loaders = await new CmlLib.Core.ModLoaders.FabricMC.FabricInstaller(Http).GetLoaders(mcVersion);
        return loaders.Select(l => l.Version).ToArray();
    }

    public async Task QuiltInstallAsync(string mcVersion, string? loaderVersion, CancellationToken ct = default)
    {
        var ins = new CmlLib.Core.ModLoaders.QuiltMC.QuiltInstaller(Http);
        if (loaderVersion == null)
            await ins.Install(mcVersion, new MinecraftPath(RootPath));
        else
            await ins.Install(mcVersion, loaderVersion, new MinecraftPath(RootPath));
    }

    public async Task<string[]> QuiltLoadersAsync(string mcVersion)
    {
        var loaders = await new CmlLib.Core.ModLoaders.QuiltMC.QuiltInstaller(Http).GetLoaders(mcVersion);
        return loaders.Select(l => l.Version).ToArray();
    }

    // ---------- Legacy Fabric（冷门装载器：为 1.3 - 1.13 等旧版本提供 Fabric 风格加载器） ----------
    private const string LegacyFabricMeta = "https://meta.legacyfabric.net/v2";

    /// <summary>Legacy Fabric 元数据不含 Mojang 域名，走直连（与 Fabric / Quilt meta 一致）。</summary>
    private static HttpClient HttpForMeta => new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<string[]> LegacyFabricLoadersAsync(string mcVersion)
    {
        using var http = HttpForMeta;
        var json = await http.GetFromJsonAsync<System.Text.Json.JsonElement>(
            $"{LegacyFabricMeta}/versions/loader/{Uri.EscapeDataString(mcVersion)}");
        return json.EnumerateArray()
            .Select(e => e.GetProperty("version").GetString() ?? "")
            .Where(v => v.Length > 0)
            .ToArray();
    }

    public async Task LegacyFabricInstallAsync(string mcVersion, string? loaderVersion, CancellationToken ct = default)
    {
        // 无指定版本时取 API 返回的第一个（最新）
        if (loaderVersion == null)
        {
            var list = await LegacyFabricLoadersAsync(mcVersion);
            loaderVersion = list.FirstOrDefault()
                ?? throw new Exception($"Legacy Fabric 没有 {mcVersion} 可用的加载器版本。");
        }
        using var http = HttpForMeta;
        var profile = await http.GetStringAsync(
            $"{LegacyFabricMeta}/versions/loader/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(loaderVersion)}/profile/json");
        // profile 为继承原版的完整版本 JSON（inheritsFrom = mcVersion），落盘后走常规安装补全库
        var id = $"legacy-fabric-{loaderVersion}-{mcVersion}";
        var dir = Path.Combine(RootPath, "versions", id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, id + ".json"), profile);
        // 本方法已在安装闸门内（装载器弹窗统一过闸），这里必须走不带闸的核心实现
        await InstallCoreAsync(id, null, null, ct);
    }

    // ---------- LiteLoader（冷门装载器：1.5 - 1.12 时代的 Mod 加载器） ----------
    public async Task<string[]> LiteLoaderVersionsAsync(string mcVersion)
    {
        var loaders = await new CmlLib.Core.ModLoaders.LiteLoader.LiteLoaderInstaller(Http)
            .GetAllLiteLoaders();
        return loaders.Where(l => l.BaseVersion == mcVersion)
            .Select(l => l.Version)
            .ToArray();
    }

    public async Task LiteLoaderInstallAsync(string mcVersion, string? loaderVersion, CancellationToken ct = default)
    {
        var ins = new CmlLib.Core.ModLoaders.LiteLoader.LiteLoaderInstaller(Http);
        var loaders = (await ins.GetAllLiteLoaders()).Where(l => l.BaseVersion == mcVersion).ToArray();
        var loader = loaderVersion == null
            ? loaders.LastOrDefault()
            : loaders.FirstOrDefault(l => l.Version == loaderVersion)
                ?? throw new Exception($"LiteLoader {loaderVersion} 不适用于 {mcVersion}。");
        if (loader == null)
            throw new Exception($"LiteLoader 没有 {mcVersion} 可用的版本。");
        var baseVersion = await Launcher.GetVersionAsync(mcVersion);
        await ins.Install(loader, baseVersion, new MinecraftPath(RootPath));
    }

    /// <summary>
    /// Forge 镜像（BMCLAPI）：版本列表 JSON 解析与安装器直链构造。
    /// 独立静态类便于单元测试（国内直连 files.minecraftforge.net 常超时）。
    /// </summary>
    public static class ForgeMirror
    {
        public const string BaseUrl = "https://bmclapi2.bangbang93.com";

        /// <summary>解析 BMCLAPI /forge/minecraft/{mc} 的 JSON 数组 → Forge 版本名（升序，与官方 HTML 顺序一致）。</summary>
        public static string[] ParseBmclapiVersionList(string json)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
                throw new Exception("BMCLAPI Forge 版本列表格式异常。");
            return doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("version", out var v) ? v.GetString() : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .Distinct()
                .ToArray();
        }

        /// <summary>构造直连 BMCLAPI maven 安装器的 ForgeVersion（免去二次抓官方 HTML）。</summary>
        public static CmlLib.Core.Installer.Forge.Versions.ForgeVersion BuildForgeVersion(string mcVersion, string forgeVersion)
        {
            var key = $"{mcVersion}-{forgeVersion}";
            return new CmlLib.Core.Installer.Forge.Versions.ForgeVersion(mcVersion, forgeVersion)
            {
                Files = new[]
                {
                    new ForgeVersionFile
                    {
                        Type = "installer",
                        DirectUrl = $"{BaseUrl}/maven/net/minecraftforge/forge/{key}/{key}-installer.jar",
                    },
                },
            };
        }
    }

    /// <summary>
    /// 构造游戏目录。开启版本隔离时，存档 / mod / 资源包 / 截图等放入
    /// versions/&lt;id&gt;/，而 assets / libraries / versions 共享基础目录（与 PCL 版本隔离一致）。
    /// </summary>
    public MinecraftPath BuildGamePath(string versionName)
    {
        var root = AppSettings.Instance.GameRoot;
        if (!AppSettings.Instance.VersionIsolation)
            return new MinecraftPath(root);

        var isoDir = Path.Combine(root, "versions", versionName);
        Directory.CreateDirectory(isoDir);
        var iso = new MinecraftPath(isoDir)
        {
            Library = Path.Combine(root, "libraries"),
            Assets = Path.Combine(root, "assets"),
            Versions = Path.Combine(root, "versions"),
            Runtime = Path.Combine(root, "runtime"),
            Resource = Path.Combine(root, "resources"),
        };
        return iso;
    }

    /// <summary>
    /// 启动游戏并返回进程包装（附输出事件）。
    /// authlibServer 不为空时，自动注入 authlib-injector（第三方验证站登录，PCL 同款）。
    /// offlineSkinPath 不为空时（离线账号 + 自定义皮肤），安装完成后把皮肤写入 client jar
    /// 默认贴图（PCL 式离线皮肤）：必须在 InstallAsync 之后打补丁，否则会被文件校验重下洗掉。
    /// </summary>
    public async Task<CmlLib.Core.ProcessBuilder.ProcessWrapper> LaunchAsync(
        string versionName, MSession session,
        IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<CmlLib.Core.ByteProgress>? byteProgress,
        Action<string>? onLog, string? authlibServer = null, string? javaPathOverride = null,
        string? offlineSkinPath = null, string? quickServer = null, Action<int>? onStage = null,
        CancellationToken ct = default)
    {
        var option = new MLaunchOption
        {
            Path = BuildGamePath(versionName),
            Session = session,
            VersionType = "HardmacroCraft",
            GameLauncherName = "HardmacroCraft",
            GameLauncherVersion = LauncherVersion,
        };

        // fix6：游戏内存自动检测（PCL 同款「自动分配」）。自动模式下每次启动按当前
        // 物理内存计算推荐值；手动模式用设置页滑杆值。超出物理内存时警告但不阻断启动。
        var totalMb = SystemInfo.TotalPhysicalMemoryMb;
        var memMb = AppSettings.Instance.MemoryAuto
            ? (int)SystemInfo.RecommendedMemoryMb(totalMb)
            : AppSettings.Instance.MemoryMb;
        option.MaximumRamMb = memMb;
        // fix7（PCL 对齐）：初始内存 -Xms 自动设为 min(2GB, 分配值)，加快堆预热且避免反复扩容
        option.MinimumRamMb = Math.Min(memMb, 2048);
        onLog?.Invoke($"[启动] 游戏内存 {memMb}MB" +
            (AppSettings.Instance.MemoryAuto
                ? $"（自动检测，物理内存 {totalMb}MB）"
                : "（手动设置）"));
        var memWarn = SystemInfo.CheckAllocatedMemory(memMb);
        if (memWarn != null) onLog?.Invoke("[警告] " + memWarn);

        // fix6：主页「快速进服」（PCL 同款）—— 启动后直接连入服务器（host 或 host:port）
        if (!string.IsNullOrWhiteSpace(quickServer))
        {
            var (serverIp, serverPort) = ParseServerAddress(quickServer);
            if (serverIp != null)
            {
                option.ServerIp = serverIp;
                if (serverPort > 0) option.ServerPort = serverPort;
                onLog?.Invoke($"[启动] 快速进服 {serverIp}" + (serverPort > 0 ? $":{serverPort}" : ""));
            }
            else
            {
                onLog?.Invoke($"[警告] 服务器地址无法解析（{quickServer}），已忽略，正常启动。");
            }
        }
        // Java：调用方解析好的路径（自动安装/自动挑选结果）优先，其次用户手填路径；
        // fix5：手填支持文件夹 / java.exe，自动递归解析为 javaw.exe
        var userJavaRaw = javaPathOverride ?? AppSettings.Instance.JavaPath;
        var userJava = JavaManager.ResolveJavaPath(userJavaRaw);
        if (userJava == null && !string.IsNullOrWhiteSpace(userJavaRaw))
            onLog?.Invoke($"[警告] 设置中的 Java 路径无效（{userJavaRaw}），改用自动选择。");
        if (!string.IsNullOrWhiteSpace(userJava))
            option.JavaPath = userJava;

        // Java 自动管理（PCL 同款）：按版本需要自动挑选托管 Java
        var autoJava = JavaManager.PickJavaFor(versionName, AppSettings.Instance.GameRoot,
            userJava, AppSettings.Instance.AutoJavaSelect);
        if (autoJava != null)
        {
            option.JavaPath = autoJava;
            onLog?.Invoke($"[启动] 已自动选择 Java: {autoJava}");
        }

        // 游戏窗口分辨率（fix7：未指定时默认 1280×720，PCL 同款）
        var gw = AppSettings.Instance.GameWidth > 0 ? AppSettings.Instance.GameWidth : 1280;
        var gh = AppSettings.Instance.GameHeight > 0 ? AppSettings.Instance.GameHeight : 720;
        option.ScreenWidth = gw;
        option.ScreenHeight = gh;
        onLog?.Invoke($"[启动] 游戏窗口 {gw}×{gh}" +
            (AppSettings.Instance.GameWidth > 0 && AppSettings.Instance.GameHeight > 0
                ? "（自定义）"
                : "（默认 1280×720，可在设置中自定义）"));

        // fix7：启动前游戏选项补丁（PCL 同款可选）—— 跳过初始屏幕 / 游戏语言
        try
        {
            GameOptions.ApplyForVersion(AppSettings.Instance.GameRoot, versionName,
                AppSettings.Instance.VersionIsolation,
                AppSettings.Instance.GameLang, AppSettings.Instance.SkipOnboarding);
            if (AppSettings.Instance.SkipOnboarding)
                onLog?.Invoke("[启动] 已跳过初始屏幕（onboardAccessibility=false）");
            if (!string.IsNullOrWhiteSpace(AppSettings.Instance.GameLang))
                onLog?.Invoke($"[启动] 游戏语言已设为 {AppSettings.Instance.GameLang}");
        }
        catch (Exception gex)
        {
            onLog?.Invoke("[警告] 游戏选项写入失败（不影响启动）: " + gex.Message);
        }

        // 第三方验证站：下载（幂等）authlib-injector.jar 并以 -javaagent 注入
        if (!string.IsNullOrWhiteSpace(authlibServer))
        {
            var jar = await Services.AuthlibService.EnsureAgentJarAsync(AppSettings.ConfigDir, Http, onLog);
            option.ExtraJvmArguments = new[] { new CmlLib.Core.ProcessBuilder.MArgument($"-javaagent:{jar}={authlibServer}") };
            onLog?.Invoke($"已注入 authlib-injector: {authlibServer}");
        }

        // fix8（PCL 同款）：默认 JVM 优化参数开关 + 用户自定义参数（自定义在后、优先生效）
        var jvmExtra = LaunchArgs.BuildJvmExtra(AppSettings.Instance.JvmOptimize, AppSettings.Instance.CustomJvmArgs);
        if (jvmExtra.Count > 0)
        {
            var merged = new List<CmlLib.Core.ProcessBuilder.MArgument>(option.ExtraJvmArguments ?? Array.Empty<CmlLib.Core.ProcessBuilder.MArgument>());
            merged.AddRange(jvmExtra.Select(a => new CmlLib.Core.ProcessBuilder.MArgument(a)));
            option.ExtraJvmArguments = merged.ToArray();
            if (AppSettings.Instance.JvmOptimize)
                onLog?.Invoke($"[启动] JVM 优化参数已启用（G1GC / Forge 兼容 / Log4Shell 缓解）");
            if (LaunchArgs.ParseArgs(AppSettings.Instance.CustomJvmArgs).Count > 0)
                onLog?.Invoke($"[启动] 已应用 {LaunchArgs.ParseArgs(AppSettings.Instance.CustomJvmArgs).Count} 个自定义 JVM 参数");
        }

        // fix8（PCL 的“游戏参数”）：自定义游戏参数（如 --demo）附加在游戏命令行末尾
        var gameExtra = LaunchArgs.BuildGameExtra(AppSettings.Instance.CustomGameArgs);
        if (gameExtra.Count > 0)
        {
            option.ExtraGameArguments = gameExtra.Select(a => new CmlLib.Core.ProcessBuilder.MArgument(a)).ToArray();
            onLog?.Invoke($"[启动] 已应用 {gameExtra.Count} 个自定义游戏参数");
        }

        // fix8（PCL 同款）：全屏启动 —— 忽略窗口分辨率设置，游戏以全屏打开
        option.FullScreen = AppSettings.Instance.GameFullscreen;
        if (option.FullScreen) onLog?.Invoke("[启动] 全屏启动（--fullscreen）");

        // 先安装文件（jar / assets / libraries），再构建并启动进程。
        // 过全局安装闸门：装载器安装等任务未结束时排队等待（而不是抛 Already installing），
        // 用户可在日志里看到等待原因；v0.1.1 起排队期间可取消。
        // v0.1.1 校验缓存：上次启动成功校验过的版本直接跳过补全（以前每次启动都对全部
        // 2800+ 文件重新算哈希，看起来像"每次都重新补全所有游戏文件"）。
        onLog?.Invoke("[启动] 检查游戏文件（jar / assets / libraries）...");
        onStage?.Invoke(2); // fix7：阶段回调 → 主页显示「正在补全游戏文件【2/3】」
        var stampOk = !AppSettings.Instance.VerifyFilesEveryLaunch
                      && VerifyStamp.IsValid(AppSettings.Instance.GameRoot, versionName);
        if (stampOk)
        {
            onLog?.Invoke("[启动] 游戏文件已校验过，快速启动（跳过补全）。重新校验可在设置中开启，或删除 config/verify-cache/。");
        }
        else
        {
            await RunInInstallGateAsync(ct, async () =>
            {
                // v0.1.1 多线程预取：CmlLib 内置安装器逐个下载，2800+ 个资源文件串行极慢；
                // 先按设置页线程数并发预取（已完整的自动跳过），失败不中断（内置安装器兜底）。
                try
                {
                    await PrefetchGameFilesAsync(option.Path, versionName, onLog, fileProgress, byteProgress, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception pex)
                {
                    onLog?.Invoke("[启动] 多线程预取中断，改由内置安装器继续: " + pex.Message);
                }
                await Launcher.InstallAsync(versionName, fileProgress, byteProgress, ct).AsTask();
            });
            VerifyStamp.Write(AppSettings.Instance.GameRoot, versionName);
        }

        // 离线皮肤（PCL 式）：安装完成后再打补丁 —— CmlLib 对 client jar 做 SHA1 校验，
        // 补丁必须落在校验之后才不会被重新下载覆盖。失败只记日志，绝不阻断启动。
        if (!string.IsNullOrWhiteSpace(offlineSkinPath) && File.Exists(offlineSkinPath))
        {
            try
            {
                var skinBytes = File.ReadAllBytes(offlineSkinPath);
                if (SkinService.IsSkinPng(skinBytes))
                {
                    var jarPath = SkinService.ResolveMainJarPath(
                        option.Path.Versions ?? Path.Combine(AppSettings.Instance.GameRoot, "versions"), versionName);
                    if (jarPath != null)
                    {
                        var entries = SkinService.PatchJarDefaultSkin(jarPath, skinBytes);
                        onLog?.Invoke($"[皮肤] 离线皮肤已注入 {Path.GetFileName(jarPath)}（{entries.Count} 处默认贴图，仅本机单机可见）");
                    }
                    else
                    {
                        onLog?.Invoke("[皮肤] 未找到版本主 jar，本次跳过离线皮肤注入。");
                    }
                }
                else
                {
                    onLog?.Invoke("[皮肤] 皮肤文件不是有效的 64×64 / 64×32 PNG，已跳过注入。");
                }
            }
            catch (Exception sex)
            {
                onLog?.Invoke("[皮肤] 离线皮肤注入失败（不影响启动）: " + sex.Message);
            }
        }

        onLog?.Invoke("[启动] 游戏文件就绪，正在创建游戏进程...");
        onStage?.Invoke(3); // fix7：阶段回调 → 主页显示「正在启动游戏【3/3】」
        var process = await Launcher.CreateProcessAsync(versionName, option);
        var wrapper = new ProcessWrapper(process);
        if (onLog != null)
        {
            wrapper.OutputReceived += (_, line) => onLog(line ?? "");
            // 游戏退出事件：把退出码写回日志（Exited 在线程池触发，onLog 内部已做 UI 封送）
            wrapper.Exited += (_, _) =>
            {
                try
                {
                    var code = wrapper.Process?.HasExited == true ? wrapper.Process.ExitCode : -1;
                    onLog($"[启动] 游戏已退出，退出代码 {code}");
                }
                catch
                {
                    onLog("[启动] 游戏已退出");
                }
            };
        }
        wrapper.StartWithEvents();
        return wrapper;
    }

    /// <summary>
    /// v0.1.1 多线程预取：把 client jar / libraries / assets 里缺失的文件列出来，
    /// 用 DownloadEngine 按「设置 → 下载线程数」并发预下载。
    /// - 已完整的文件自动跳过（sha1 / 大小校验）；
    /// - 远古版（virtual / resources 映射资源索引）结构特殊，直接交给内置安装器；
    /// - 任何失败都不阻断启动（CmlLib 内置安装器会接着补）。
    /// </summary>
    private async Task PrefetchGameFilesAsync(
        MinecraftPath path, string versionName, Action<string>? onLog,
        IProgress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? fileProgress,
        IProgress<CmlLib.Core.ByteProgress>? byteProgress, CancellationToken ct)
    {
        var threads = Math.Clamp(AppSettings.Instance.DownloadThreads, 1, 256);
        if (threads <= 1) return; // 单线程：直接用内置安装器，行为与旧版一致

        var mv = await Launcher.GetVersionAsync(versionName, ct);
        var items = new List<DownloadEngine.BatchItem>();

        // 1) client jar（主 jar；MainJarId 与版本 id 可能不同）
        var client = mv.Client;
        var jarId = !string.IsNullOrEmpty(mv.MainJarId) ? mv.MainJarId : mv.Id;
        var jarPath = !string.IsNullOrEmpty(client?.Path) ? client.Path : $"{jarId}/{jarId}.jar";
        if (!string.IsNullOrEmpty(client?.Url))
        {
            var jarDest = System.IO.Path.Combine(path.Versions, jarPath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(jarDest))
                items.Add(new DownloadEngine.BatchItem
                {
                    Url = client!.Url, Dest = jarDest, DisplayName = jarId + ".jar",
                    Sha1 = string.IsNullOrEmpty(client.Sha1) ? null : client.Sha1, Size = client.Size,
                });
        }

        // 2) libraries（整条 inheritsFrom 链；跳过纯服务端库； natives-windows 一并预取）
        var libs = new List<CmlLib.Core.Version.MLibrary>();
        for (CmlLib.Core.Version.IVersion? cur = mv; cur != null; cur = cur.ParentVersion)
            libs.AddRange(cur.Libraries);
        foreach (var lib in libs)
        {
            if (lib.IsServerRequired) continue;
            var artifact = lib.Artifact;
            if (!string.IsNullOrEmpty(artifact?.Url) && !string.IsNullOrEmpty(artifact.Path))
            {
                var dest = System.IO.Path.Combine(path.Library, artifact.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!File.Exists(dest))
                    items.Add(new DownloadEngine.BatchItem
                    {
                        Url = artifact.Url, Dest = dest,
                        DisplayName = "libraries/" + artifact.Path,
                        Sha1 = string.IsNullOrEmpty(artifact.Sha1) ? null : artifact.Sha1, Size = artifact.Size,
                    });
            }
            if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var nw)
                && lib.Classifiers != null && lib.Classifiers.TryGetValue(nw, out var nat)
                && !string.IsNullOrEmpty(nat?.Url) && !string.IsNullOrEmpty(nat.Path))
            {
                var dest = System.IO.Path.Combine(path.Library, nat.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (!File.Exists(dest))
                    items.Add(new DownloadEngine.BatchItem
                    {
                        Url = nat.Url, Dest = dest,
                        DisplayName = "libraries/" + nat.Path,
                        Sha1 = string.IsNullOrEmpty(nat.Sha1) ? null : nat.Sha1, Size = nat.Size,
                    });
            }
        }

        // 3) assets（资源索引 + 全部资源对象；远古版 virtual 索引（legacy/pre-1.6）结构特殊，跳过交给内置安装器）
        var assetIndex = mv.AssetIndex;
        var isLegacyAssetIndex = assetIndex != null && (assetIndex.Id == "legacy" || assetIndex.Id == "pre-1.6");
        if (!string.IsNullOrEmpty(assetIndex?.Url) && !isLegacyAssetIndex)
        {
            var indexDest = System.IO.Path.Combine(path.Assets, "indexes", assetIndex.Id + ".json");
            if (!File.Exists(indexDest))
            {
                items.Add(new DownloadEngine.BatchItem
                {
                    Url = assetIndex.Url, Dest = indexDest, DisplayName = "assets/indexes/" + assetIndex.Id + ".json",
                    Sha1 = string.IsNullOrEmpty(assetIndex.Sha1) ? null : assetIndex.Sha1, Size = assetIndex.Size,
                });
            }
            if (File.Exists(indexDest))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(indexDest));
                    if (doc.RootElement.TryGetProperty("objects", out var objects))
                    {
                        foreach (var obj in objects.EnumerateObject())
                        {
                            if (obj.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                            var hash = obj.Value.TryGetProperty("hash", out var h) ? h.GetString() : null;
                            var size = obj.Value.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var s) ? s : 0;
                            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
                            var dest = System.IO.Path.Combine(path.Assets, "objects", hash[..2], hash);
                            if (File.Exists(dest)) continue;
                            items.Add(new DownloadEngine.BatchItem
                            {
                                Url = "https://resources.download.minecraft.net/" + hash[..2] + "/" + hash,
                                Dest = dest, DisplayName = "assets/objects/" + hash[..2] + "/" + hash,
                                Sha1 = hash, Size = size,
                            });
                        }
                    }
                }
                catch (Exception aex)
                {
                    onLog?.Invoke("[启动] 资源索引解析失败（交给内置安装器）: " + aex.Message);
                }
            }
        }

        if (items.Count == 0) return;
        onLog?.Invoke($"[启动] 多线程预取：待下载 {items.Count} 个文件（{threads} 线程）...");

        var prog = new Progress<DownloadEngine.BatchProgress>(p =>
        {
            byteProgress?.Report(new CmlLib.Core.ByteProgress(p.TotalBytes, p.DoneBytes));
            fileProgress?.Report(new CmlLib.Core.Installers.InstallerProgressChangedEventArgs(
                p.TotalFiles, p.DoneFiles, p.CurrentFile, CmlLib.Core.Installers.InstallerEventType.Queued));
        });
        await DownloadEngine.DownloadBatchAsync(Http, items, threads, prog,
            null, ct); // 不逐文件写日志（2800 行会刷屏）；文件名经 fileProgress 进度行显示
        onLog?.Invoke("[启动] 多线程预取完成。");
    }

    /// <summary>
    /// 解析服务器地址（fix6 快速进服）：支持「host」「host:port」「[IPv6]:port」；
    /// 无端口时端口返回 0（游戏用默认 25565）；host 为空时 ip 返回 null。
    /// </summary>
    public static (string? ip, int port) ParseServerAddress(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (null, 0);
        var s = input.Trim();
        // [IPv6]:port
        if (s.StartsWith("["))
        {
            var close = s.IndexOf(']');
            if (close > 1)
            {
                var ip = s[1..close];
                var port = 0;
                if (close + 2 <= s.Length && s[close + 1] == ':')
                    int.TryParse(s[(close + 2)..], out port);
                return (string.IsNullOrWhiteSpace(ip) ? null : ip, port);
            }
            return (null, 0);
        }
        // host:port —— 仅按「恰好 1 个冒号」切分（裸 IPv6 字面量有多个冒号，不在此列）
        var colon = s.LastIndexOf(':');
        if (colon > 0 && s.Count(c => c == ':') == 1)
        {
            var host = s[..colon].Trim();
            var p = s[(colon + 1)..].Trim();
            if (int.TryParse(p, out var port) && port is > 0 and <= 65535 && host.Length > 0)
                return (host, port);
            return (null, 0); // 有冒号但端口非法 → 整体视为无效（避免把坏地址当 host 传给游戏）
        }
        // 纯主机名（无冒号）：冒号开头/结尾的残缺输入视为无效
        return s[0] == ':' || s[^1] == ':' ? (null, 0) : (s, 0);
    }

    /// <summary>
    /// 解析 JVM 自定义参数（fix8 起委托至 LaunchArgs.ParseArgs，单一事实来源）：
    /// 空白分隔；支持双引号包裹含空格的参数（PCL 行为）；引号本身会被剥除；空串返回空列表。
    /// </summary>
    public static List<string> ParseJvmArgs(string? input) => LaunchArgs.ParseArgs(input);

    /// <summary>已安装版本 = versions 目录下的每个子文件夹（root 为空时用配置的游戏根目录）。</summary>
    public IReadOnlyList<string> GetInstalledVersions(string? root = null)
    {
        var dir = Path.Combine(root ?? AppSettings.Instance.GameRoot, "versions");
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.GetDirectories(dir)
            .Where(d => Directory.GetFiles(d, "*.json").Length > 0)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void DeleteVersion(string versionName)
    {
        var dir = Path.Combine(AppSettings.Instance.GameRoot, "versions", versionName);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        VerifyStamp.Invalidate(AppSettings.Instance.GameRoot, versionName);
    }

    /// <summary>把版本目录内的 json/jar 文件名与 json 中的 id 字段改为新版本名。</summary>
    private static void PatchVersionFiles(string versionDir, string oldName, string newName)
    {
        foreach (var f in Directory.GetFiles(versionDir))
        {
            var name = Path.GetFileName(f);
            if (name == oldName + ".json")
                File.Move(f, Path.Combine(versionDir, newName + ".json"));
            else if (name == oldName + ".jar")
                File.Move(f, Path.Combine(versionDir, newName + ".jar"));
        }
        var jsonFile = Path.Combine(versionDir, newName + ".json");
        if (File.Exists(jsonFile))
        {
            try
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(jsonFile));
                if (node != null)
                {
                    node["id"] = newName;
                    File.WriteAllText(jsonFile, node.ToJsonString(new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    }));
                }
            }
            catch { /* json 结构异常时保留原样 */ }
        }
    }

    /// <summary>
    /// 清理历史版本污染的 %APPDATA%\.minecraft 空壳：旧版把版本清单缓存
    /// version_manifest_v2.json 写进了 CreateDefault() 一次性默认路径（fix3 根因修复前的残留），
    /// 部分旧版本还会把 Mojang Java 运行时下载到那里的 runtime\ 子目录（fix7 用户反馈）。
    /// 若该目录仅含这两类无用户数据的内容（见 IsPollutedAppdataShell 白名单）则整目录删除；
    /// 含任何其他文件则视为真实游戏目录，一律不动。
    /// </summary>
    public static void CleanupPollutedAppDataShell(Action<string>? log = null)
    {
        try
        {
            var appdata = Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrWhiteSpace(appdata)) return;
            var shell = Path.Combine(appdata, ".minecraft");
            if (!Directory.Exists(shell)) return;

            var files = Directory.GetFiles(shell, "*", SearchOption.AllDirectories);
            var rel = files.Select(f => Path.GetRelativePath(shell, f)).ToArray();
            if (!AppSettings.IsPollutedAppdataShell(rel))
            {
                log?.Invoke("[清理] %APPDATA%\\.minecraft 含真实游戏数据（存档 / 设置等），未改动。");
                return;
            }
            Directory.Delete(shell, true);
            log?.Invoke("[清理] 已删除历史版本在 %APPDATA% 产生的 .minecraft 空壳（仅含清单缓存 / Java 运行时残留，均无用户数据）。");
        }
        catch (Exception ex)
        {
            log?.Invoke("[清理] %APPDATA% 检查失败（不影响使用）: " + ex.Message);
        }
    }

    /// <summary>重命名版本（连同隔离目录内的存档 / mods 一起改名）。</summary>
    public void RenameVersion(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
        if (newName.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
            throw new ArgumentException("版本名包含非法字符。");
        var root = AppSettings.Instance.GameRoot;
        var src = Path.Combine(root, "versions", oldName);
        var dst = Path.Combine(root, "versions", newName);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("版本不存在: " + oldName);
        if (Directory.Exists(dst)) throw new IOException("目标版本名已存在: " + newName);
        Directory.Move(src, dst);
        PatchVersionFiles(dst, oldName, newName);
        if (AppSettings.Instance.LastVersion == oldName)
        {
            AppSettings.Instance.LastVersion = newName;
            AppSettings.Instance.Save();
        }
    }

    /// <summary>复制版本（用于以某版本为模板创建新版本）。</summary>
    public void CopyVersion(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
        if (newName.Any(c => Path.GetInvalidFileNameChars().Contains(c)))
            throw new ArgumentException("版本名包含非法字符。");
        var root = AppSettings.Instance.GameRoot;
        var src = Path.Combine(root, "versions", oldName);
        var dst = Path.Combine(root, "versions", newName);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException("版本不存在: " + oldName);
        if (Directory.Exists(dst)) throw new IOException("目标版本名已存在: " + newName);
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(src, dst));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(src, dst), true);
        PatchVersionFiles(dst, oldName, newName);
    }

    public void Dispose()
    {
        // MinecraftLauncher 在 CmlLib 4 中不实现 IDisposable，仅释放 HttpClient
        try { Http.Dispose(); } catch { }
    }
}
