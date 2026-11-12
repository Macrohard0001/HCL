using System.IO;
using System.Text.Json;

namespace HardmacroCraft.Services;

/// <summary>
/// 应用设置 —— 便携化设计：所有配置保存在可执行文件同目录的 config/ 文件夹中，
/// 不写入 AppData、不写入注册表。整个目录拷走即可迁移。
/// </summary>
public class AppSettings
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>可执行文件所在目录（单文件发布下同样指向 exe 真实位置）。</summary>
    public static string AppDir { get; } =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>配置目录：exe 同目录下的 config/。</summary>
    public static string ConfigDir { get; } = Path.Combine(AppDir, "config");

    private static string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    // ---- 可持久化字段 ----
    public string GameRoot { get; set; } = Path.Combine(AppDir, ".minecraft");
    /// <summary>
    /// 用户是否在设置页显式修改过游戏根目录。false 时 Load() 会把意外落盘的污染路径
    /// （历史版本泄漏的盘根 \ .minecraft / %ProgramData%\.minecraft 等）自动纠正回
    /// 默认的 AppDir\.minecraft —— 消除外部 .minecraft 目录产生的根因（v0.1.0-beta 重置版）。
    /// </summary>
    public bool GameRootCustomized { get; set; }
    public string JavaPath { get; set; } = "";
    /// <summary>
    /// 内存自动检测（fix6，PCL 同款）：开启时每次启动按物理内存自动计算分配量
    /// （一半内存、512MB 取整、限 [1024, min(8192, 总-1024)]），MemoryMb 仅在手动模式生效。
    /// </summary>
    public bool MemoryAuto { get; set; } = true;
    public int MemoryMb { get; set; } = 4096;
    public bool UseMirror { get; set; } = true;
    public string Language { get; set; } = "zh-CN";
    public string AzureClientId { get; set; } = "";
    /// <summary>版本隔离：每个版本独立的存档 / mod / 资源包目录（类似 PCL 的版本隔离）。</summary>
    public bool VersionIsolation { get; set; } = true;
    public string LastVersion { get; set; } = "";
    /// <summary>模组页记忆（v0.1.5，PCL 式记住筛选）：上次使用的平台 / 资源类型 / 加载器。</summary>
    public string LastModPlatform { get; set; } = "modrinth";
    public string LastModType { get; set; } = "mod";
    public string LastModLoader { get; set; } = "any";
    /// <summary>主题：dark / light / system（跟随 Windows 深浅色）。fix6 起新装默认跟随系统。</summary>
    public string Theme { get; set; } = "system";
    /// <summary>最近启动的版本（主页快捷启动，最新在前，最多 6 个）。</summary>
    public List<string> RecentVersions { get; set; } = new();
    /// <summary>关闭时窗口是否处于最大化（记忆窗口状态，尺寸始终默认 960×540）。</summary>
    public bool WindowMaximized { get; set; }
    /// <summary>JVM 自定义参数（PCL 的“JVM 参数”）：空格分隔，支持双引号包裹含空格参数，留空用默认。</summary>
    public string CustomJvmArgs { get; set; } = "";
    /// <summary>
    /// 默认 JVM 优化参数（fix8，PCL 同款推荐开启）：G1GC / 关闭自适应SizePolicy /
    /// Forge 兼容性 -D / Log4Shell 缓解。与自定义 JVM 参数冲突时自定义在后、优先生效。
    /// </summary>
    public bool JvmOptimize { get; set; } = true;
    /// <summary>游戏自定义参数（fix8，PCL 的“游戏参数”）：如 --fullscreen --demo，留空不附加。</summary>
    public string CustomGameArgs { get; set; } = "";
    /// <summary>全屏启动（fix8，PCL 同款）：以 --fullscreen 启动，忽略窗口分辨率设置。</summary>
    public bool GameFullscreen { get; set; }
    /// <summary>
    /// v0.1.1 下载线程数（1-256，对齐 PCL）：启动补全 / 资源多线程预取的并发数。默认 64。
    /// 设为 1 时退回内置安装器的单文件串行下载。
    /// </summary>
    public int DownloadThreads { get; set; } = 64;
    /// <summary>
    /// v0.1.1 每次启动都重新校验游戏文件。默认关（校验过一次的版本直接快速启动）；
    /// 怀疑文件损坏 / 游戏异常时再开启。
    /// </summary>
    public bool VerifyFilesEveryLaunch { get; set; }

    /// <summary>Java 自动选择：启动时按版本需要自动挑选托管 Java（用户手填 Java 路径时仍以手填优先）。</summary>
    public bool AutoJavaSelect { get; set; } = true;
    /// <summary>Java 自动管理的下载根目录（fix5）：空 = 默认 exe 同目录 runtime/；可自定义到任意位置。</summary>
    public string JavaRuntimeDir { get; set; } = "";
    /// <summary>第三方登录窗口默认服务器（记住上次使用，PCL 同款）。</summary>
    public string LastAuthlibServer { get; set; } = "";
    /// <summary>
    /// v0.1.3 启动器内存优化：重大操作后与空闲时段自动回收内存并压缩物理内存占用。
    /// 默认开；关闭后启动器常驻内存可能更高。
    /// </summary>
    public bool AutoMemoryOptimize { get; set; } = true;
    /// <summary>游戏窗口宽度（0 = 游戏默认）。</summary>
    public int GameWidth { get; set; }
    /// <summary>游戏窗口高度（0 = 游戏默认）。</summary>
    public int GameHeight { get; set; }
    /// <summary>
    /// v0.1.10 游戏窗口大小模式（对齐 PCL）：default = 默认 1280×720；
    /// custom = 使用 GameWidth / GameHeight；match-launcher = 与启动器窗口大小一致。
    /// GameFullscreen 为 true 时忽略此设置（以 --fullscreen 启动）。
    /// </summary>
    public string GameWindowSizeMode { get; set; } = "default";
    /// <summary>
    /// CurseForge REST API Key（免费，console.curseforge.com 申请）。
    /// 仅保存在本机便携 config/settings.json，不随源码分发。
    /// </summary>
    public string CurseForgeApiKey { get; set; } = "";
    /// <summary>游戏启动后自动隐藏启动器（游戏退出时自动恢复，HMCL 同款）。</summary>
    public bool HideLauncherOnGameStart { get; set; }
    /// <summary>游戏退出后自动关闭启动器（HMCL 同款）。</summary>
    public bool ExitLauncherOnGameExit { get; set; }
    /// <summary>主页「快速进服」上次使用的服务器地址（host:port，fix6，PCL 同款记忆）。</summary>
    public string LastQuickServer { get; set; } = "";
    /// <summary>
    /// 跳过初始屏幕（fix7，PCL 同款可选）：启动前把 options.txt 的 onboardAccessibility
    /// 写为 false，首次运行不再显示欢迎 / 引导界面。
    /// </summary>
    public bool SkipOnboarding { get; set; }
    /// <summary>
    /// 游戏语言（fix7，PCL 同款可选）：启动前写入 options.txt 的 lang。
    /// 空 = 不修改（尊重游戏自身设置）；"zh_CN" / "en_US" 等。
    /// </summary>
    public string GameLang { get; set; } = "";
    /// <summary>
    /// v0.1.11 相对路径增强：开启后落在启动器目录内的路径（游戏根目录 / Java 运行时目录）
    /// 以「./相对路径」形式保存，整个启动器文件夹拷到别的盘符 / 电脑后配置无需修改。
    /// </summary>
    public bool UseRelativePaths { get; set; }
    /// <summary>
    /// v0.1.11 界面刷新频率（PCL 同款）：auto 自动 / manual 手动 FPS / event 事件驱动 /
    /// event-limited 事件驱动（限最大频率）。详见 UiRefreshPolicy。
    /// </summary>
    public string UiRefreshMode { get; set; } = UiRefreshPolicy.ModeAuto;
    /// <summary>v0.1.11：UiRefreshMode=manual 时的轮询 FPS（1-240）。</summary>
    public int UiRefreshFps { get; set; } = 30;
    /// <summary>v0.1.11：UiRefreshMode=event-limited 时的最大刷新频率（1-240）。</summary>
    public int UiRefreshMaxFps { get; set; } = 30;
    /// <summary>v0.1.11：首页日志卡是否展开（切页 / 重启后恢复）。</summary>
    public bool HomeLogOpen { get; set; }
    /// <summary>v0.1.11：任务详情日志滚动动画（平滑滚动，可关）。</summary>
    public bool SmoothScrollLogs { get; set; } = true;

    // ---- 存取 ----
    public static AppSettings Instance { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                Instance = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFile), JsonOpts) ?? new();
        }
        catch (Exception ex)
        {
            // 配置损坏 → 回退默认值，同时留痕（boot.log 已在 App 启动最前开启）
            Services.BootLogger.Error("settings.json 解析失败，使用默认配置: " + ex.Message);
            Instance = new();
        }
        // v0.1.11 相对路径增强：先把相对形式的路径解析回绝对路径，再做合法性清洗
        Instance.GameRoot = PortablePath.ToAbsolute(AppDir, Instance.GameRoot);
        Instance.JavaRuntimeDir = PortablePath.ToAbsolute(AppDir, Instance.JavaRuntimeDir);
        // 配置文件被手改坏（如 GameRoot 为空）或被历史泄漏路径污染时回退默认值，避免启动即崩
        Instance.GameRoot = SanitizeGameRoot(Instance.GameRoot, Instance.GameRootCustomized);
        // v0.1.1：目录创建失败（Program Files 只读 / OneDrive 接管桌面 / 杀软拦截）
        // 不再让启动崩溃 —— 游戏真正要写文件时才需要这些目录，到时另有兜底提示
        try { Directory.CreateDirectory(ConfigDir); }
        catch (Exception ex) { Services.BootLogger.Error("config/ 目录创建失败: " + ex.Message); }
        try { Directory.CreateDirectory(Instance.GameRoot); }
        catch (Exception ex) { Services.BootLogger.Error("游戏根目录创建失败 (" + Instance.GameRoot + "): " + ex.Message); }
    }

    /// <summary>
    /// 游戏根目录合法性清洗（消除 %ProgramData%/.minecraft 根因）：
    /// CmlLib 的 MinecraftLauncherParameters.CreateDefault() 内部会 new MinecraftPath()，
    /// 其 Windows 默认路径是 环境变量 appdata + "\.minecraft" —— appdata 变量异常时
    /// 退化为相对路径 \.minecraft，GetFullPath 后落到进程当前盘符根目录等意外位置，
    /// 并可能被旧版持久化进 settings.json。此方法：
    /// 1) 空 / 非法路径 → 默认 AppDir\.minecraft；
    /// 2) 用户未自定义但路径与默认不一致（污染）→ 纠正回默认；
    /// 3) 用户显式自定义 → 尊重用户选择。
    /// </summary>
    public static string SanitizeGameRoot(string? saved, bool customized)
    {
        var def = Path.Combine(AppDir, ".minecraft");
        if (string.IsNullOrWhiteSpace(saved)) return def;
        if (customized) return saved;
        string norm;
        try
        {
            norm = Path.GetFullPath(saved).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return def; // 含非法字符 / 盘符不存在等
        }
        var defNorm = Path.GetFullPath(def).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(norm, defNorm, StringComparison.OrdinalIgnoreCase) ? saved : def;
    }

    /// <summary>
    /// 判定 %APPDATA%\.minecraft 是否为本启动器历史版本产生的「空壳」（fix3，fix7 扩展）：
    /// 旧版把版本清单缓存 version_manifest_v2.json 写进了 CreateDefault() 的一次性
    /// 默认路径；部分旧版本还会把 Mojang 官方 Java 运行时下载到那里的 runtime\ 子目录。
    /// 目录里除以下两类「无用户数据」内容与空目录外没有任何其他文件 → 是空壳，可安全删除：
    ///   1) versions\version_manifest_v2.json（清单缓存垃圾）；
    ///   2) runtime\ 子树（Java 运行时二进制，官方启动器 / 旧版启动器都会自动重建，永不含存档等用户数据）。
    /// 只要出现任何其他文件（options.txt / saves / versions 的真实版本 / screenshots 等）
    /// 就视为真实游戏目录，一律不动。
    /// </summary>
    public static bool IsPollutedAppdataShell(IReadOnlyCollection<string> relativeFiles)
    {
        if (relativeFiles.Count == 0) return true; // 纯空目录（同样是我们建出来的）
        foreach (var raw in relativeFiles)
        {
            var rel = raw.Replace('\\', '/').TrimStart('/');
            if (string.Equals(rel, "versions/version_manifest_v2.json", StringComparison.OrdinalIgnoreCase))
                continue; // fix3 识别过的清单缓存垃圾
            if (rel.StartsWith("runtime/", StringComparison.OrdinalIgnoreCase))
                continue; // fix7：Java 运行时残留（仅二进制，自动可重建，无用户数据）
            return false; // 任何其他文件 → 真实游戏目录
        }
        return true;
    }

    public void Save()
    {
        try { Directory.CreateDirectory(ConfigDir); }
        catch (Exception ex) { Services.BootLogger.Error("config/ 目录创建失败: " + ex.Message); }
        // v0.1.11 相对路径增强：开启时把启动器目录内的路径相对化后再落盘。
        // 序列化的是临时快照，不改运行中的 Instance（绝对路径继续驱动一切业务）。
        var snapshot = this;
        if (UseRelativePaths)
        {
            snapshot = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOpts), JsonOpts) ?? this;
            snapshot.GameRoot = PortablePath.ToRelative(AppDir, GameRoot);
            snapshot.JavaRuntimeDir = PortablePath.ToRelative(AppDir, JavaRuntimeDir);
        }
        // 写入临时文件后原子替换：写一半崩溃 / 断电不会留下半截 JSON
        var tmp = SettingsFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOpts));
        try
        {
            File.Move(tmp, SettingsFile, overwrite: true);
        }
        catch (IOException)
        {
            // 覆盖被占用（如杀软短暂锁定）时保留旧配置也不丢新配置
            File.Copy(tmp, SettingsFile, overwrite: true);
            File.Delete(tmp);
        }
    }

    /// <summary>记录一次启动（最新在前、去重、最多 6 个）。</summary>
    public void RecordLaunch(string version)
    {
        RecentVersions.RemoveAll(v => v == version);
        RecentVersions.Insert(0, version);
        if (RecentVersions.Count > 6) RecentVersions = RecentVersions.Take(6).ToList();
        Save();
    }
}

/// <summary>
/// 账号存储 —— 同样保存在便携 config/ 目录（accounts.json），仅保存微软账号的
/// refresh_token（可随时在设置中清除），不保存明文密码。
/// </summary>
public class StoredAccount
{
    public string Type { get; set; } = "offline"; // offline | microsoft | authlib
    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";
    /// <summary>微软账号存 refresh_token；第三方验证站存 accessToken（用于无感刷新）。</summary>
    public string RefreshToken { get; set; } = "";
    /// <summary>第三方验证站：Yggdrasil clientToken（与 accessToken 配对）。</summary>
    public string ClientToken { get; set; } = "";
    /// <summary>第三方验证站：Yggdrasil API Root（如 https://littleskin.cn/api/yggdrasil）。</summary>
    public string ServerUrl { get; set; } = "";
    /// <summary>第三方验证站：登录用的邮箱 / 用户名（记住密码时保存，用于下次自动填充）。</summary>
    public string LoginId { get; set; } = "";
    /// <summary>记住密码：true 时保存验证站密码于便携 config/accounts.json（仅本机便携配置）。</summary>
    public bool RememberPassword { get; set; }
    public string SavedPassword { get; set; } = "";
    /// <summary>自动登录：启动器打开后用记住的密码静默重登该验证站账号（PCL 同款）。</summary>
    public bool AutoLogin { get; set; }
    /// <summary>微软账号：缓存 Minecraft 访问令牌（由 refresh_token 换得）。刷新失败时用
    /// 它兜底启动，避免一次网络抖动 / Azure Client ID 丢失就永远无法进游戏（v0.1.3 修复）。</summary>
    public string AccessToken { get; set; } = "";
    /// <summary>离线账号：自定义皮肤 PNG 的本机路径（PCL 式离线皮肤，复制存于 config/skins/）。
    /// 启动时注入游戏 jar 的默认皮肤贴图，仅本机单机可见（离线模式没有服务端身份，无法同步他人）。</summary>
    public string SkinPath { get; set; } = "";
}

public class AccountStore
{
    private static string AccountsFile => Path.Combine(AppSettings.ConfigDir, "accounts.json");

    // [JsonInclude]： Accounts 为私有 setter，必须显式标注才能参与反序列化，
    // 否则启动时 accounts.json 读取后列表恒为空 —— 所有账号（含离线）重启即丢（v1.7.1 修复）
    [System.Text.Json.Serialization.JsonInclude]
    public List<StoredAccount> Accounts { get; private set; } = new();
    public string CurrentName { get; set; } = "";
    /// <summary>当前账号类型：离线 / 微软 / 第三方可能同名，单靠名字会指错（v0.1.3 修复）。</summary>
    public string CurrentType { get; set; } = "";

    public StoredAccount? Current =>
        Accounts.FirstOrDefault(a => a.Name == CurrentName && a.Type == CurrentType)
        ?? Accounts.FirstOrDefault(a => a.Name == CurrentName)
        ?? Accounts.FirstOrDefault();

    public static AccountStore Instance { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(AccountsFile))
                Instance = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(File.ReadAllText(AccountsFile)) ?? new();
        }
        catch (Exception ex)
        {
            Services.BootLogger.Error("accounts.json 解析失败，使用空账号列表: " + ex.Message);
            Instance = new();
        }
        try { Directory.CreateDirectory(AppSettings.ConfigDir); }
        catch (Exception ex) { Services.BootLogger.Error("config/ 目录创建失败: " + ex.Message); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppSettings.ConfigDir);
        File.WriteAllText(AccountsFile,
            System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>按（名字 + 类型）入列：同名离线 / 第三方账号互不顶替（v0.1.3 修复）。
    /// 重登同名账号时保留旧账号的自定义皮肤路径（登录系统全面检查：
    /// 此前 Upsert 新建对象会静默清掉 SkinPath，离线账号换个姿势重登皮肤就没了）。</summary>
    public void Upsert(StoredAccount acc)
    {
        var old = Accounts.FirstOrDefault(a => a.Name == acc.Name && a.Type == acc.Type);
        if (old != null)
        {
            if (string.IsNullOrWhiteSpace(acc.SkinPath) && !string.IsNullOrWhiteSpace(old.SkinPath))
                acc.SkinPath = old.SkinPath;
            Accounts.Remove(old);
        }
        Accounts.Add(acc);
        CurrentName = acc.Name;
        CurrentType = acc.Type;
        Save();
    }

    /// <summary>type 为 null 时按名字删除任意类型（兼容旧调用），否则只删对应类型。</summary>
    public void Remove(string name, string? type = null)
    {
        Accounts.RemoveAll(a => a.Name == name && (type == null || a.Type == type));
        var cur = Accounts.FirstOrDefault(a => a.Name == CurrentName) ?? Accounts.FirstOrDefault();
        CurrentName = cur?.Name ?? "";
        CurrentType = cur?.Type ?? "";
        Save();
    }
}
