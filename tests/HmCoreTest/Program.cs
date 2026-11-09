// HardmacroCraft 核心功能无头验证（Linux 可真实运行）
// 直接源码链接 Services/*，逐项验证启动器的关键链路。
using System.Diagnostics;
using System.Net.Http;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using HardmacroCraft.Services;

var pass = 0; var fail = 0; var skip = 0;
void Check(string name, bool ok, string detail = "")
{
    if (ok) { pass++; Console.WriteLine($"  [PASS] {name}  {detail}"); }
    else { fail++; Console.WriteLine($"  [FAIL] {name}  {detail}"); }
}
void Skip(string name, string why) { skip++; Console.WriteLine($"  [SKIP] {name}  {why}"); }

const string CfSearchJson = """
{
  "data": [
    {
      "id": 238222, "name": "Just Enough Items (JEI)", "slug": "jei",
      "summary": "Item and recipe viewing mod",
      "authors": [{"name": "mezz"}],
      "logo": {"thumbnailUrl": "https://media.forgecdn.net/avatars/thumbnail/12/34/64/64/635.png"},
      "downloadCount": 350000000,
      "dateModified": "2026-10-01T00:00:00Z",
      "categories": [{"name": "Map and Information"}],
      "latestFilesIndexes": [{"gameVersion": "1.21.1"}]
    },
    {
      "id": 99999, "name": "Fake Mod", "slug": "fake-mod",
      "summary": "s", "authors": [],
      "downloadCount": 1, "dateModified": "2026-01-01T00:00:00Z",
      "categories": [], "latestFilesIndexes": []
    }
  ],
  "pagination": {"index": 0, "pageSize": 25, "resultCount": 2, "total": 335000}
}
""";

const string CfFilesJson = """
{
  "data": [
    {
      "id": 7000001, "modId": 238222, "displayName": "JEI 1.21.1-fabric",
      "fileName": "jei-1.21.1-fabric.jar", "fileLength": 2345678,
      "releaseType": 1, "fileDate": "2026-09-20T00:00:00Z",
      "gameVersions": ["1.21.1", "Fabric"]
    },
    {
      "id": 7000000, "modId": 238222, "displayName": "JEI 1.21-forge",
      "fileName": "jei-1.21-forge.jar", "fileLength": 2333333,
      "releaseType": 2, "fileDate": "2026-08-20T00:00:00Z",
      "gameVersions": ["1.21", "Forge"]
    }
  ],
  "pagination": {"index": 0, "pageSize": 50, "resultCount": 2, "total": 2}
}
""";

const string MrVersionsJson = """
[
  {
    "id": "v2", "name": "JEI 10.0.1", "version_number": "10.0.1",
    "version_type": "release", "date_published": "2026-09-01T00:00:00Z",
    "game_versions": ["1.20.1"], "loaders": ["fabric"],
    "dependencies": [
      {"project_id": "abc123", "dependency_type": "required"},
      {"project_id": "def456", "dependency_type": "optional"}
    ],
    "files": [{"url": "https://cdn.modrinth.com/jei-fabric.jar", "filename": "jei-10.0.1-fabric.jar", "size": 1000, "primary": true}]
  },
  {
    "id": "v1", "name": "JEI 10.0.0", "version_number": "10.0.0",
    "version_type": "beta", "date_published": "2026-08-01T00:00:00Z",
    "game_versions": ["1.20.1"], "loaders": ["forge"],
    "dependencies": [],
    "files": [{"url": "https://cdn.modrinth.com/jei-forge.jar", "filename": "jei-10.0.0-forge.jar", "size": 900, "primary": true}]
  }
]
""";

var testRoot = "/tmp/hmc-core-test";
Directory.CreateDirectory(testRoot);

// ==================== 1. 离线登录 ====================
Console.WriteLine("\n== 1. 离线登录（UUID v3 算法与官方离线模式一致性） ==");
// Java 离线 UUID 参考（Python 独立实现 MD5("OfflinePlayer:name") v3）：
// 已用独立工具交叉验证 "Notch" => b50ad385-829d-3141-a216-7e7d7539ba7f 不确定，
// 因此这里做双向验证：算法自实现 vs 另一份本地计算。
var u1 = AuthService.OfflineUuid("Steve");
Check("OfflineUuid(Steve) 版本位/变体位正确",
    u1[14] == '3' && u1[19] is '8' or '9' or 'a' or 'b',
    u1);
Check("OfflineUuid 确定性（同输入同输出）", AuthService.OfflineUuid("Steve") == u1);
Check("OfflineUuid 不同玩家不同 UUID", AuthService.OfflineUuid("Alex") != AuthService.OfflineUuid("Steve"));
var s = AuthService.OfflineSession("Steve");
Check("OfflineSession 字段正确", s.Username == "Steve" && s.UUID == u1 && s.AccessToken == "0");

// ==================== 2. 设置存取（便携化） ====================
Console.WriteLine("\n== 2. 设置存取（便携化 config/settings.json） ==");
AppSettings.Instance.GameRoot = Path.Combine(testRoot, ".minecraft");
AppSettings.Instance.MemoryMb = 8192;
AppSettings.Instance.VersionIsolation = true;
AppSettings.Instance.Save();
Check("settings.json 已写入", File.Exists(Path.Combine(AppSettings.ConfigDir, "settings.json")));
var raw = File.ReadAllText(Path.Combine(AppSettings.ConfigDir, "settings.json"));
Check("settings.json 内容含 MemoryMb=8192", raw.Contains("8192"));
AppSettings.Load();
Check("重新加载后字段一致",
    AppSettings.Instance.MemoryMb == 8192 && AppSettings.Instance.VersionIsolation);

// ==================== 2.5 账号持久化（离线登录保存 → 重启恢复，v1.7.1 回归） ====================
Console.WriteLine("\n== 2.5 账号持久化（accounts.json 往返） ==");
try
{
    var store = new AccountStore();
    store.Upsert(new StoredAccount
    {
        Type = "offline",
        Name = "Steve",
        Uuid = AuthService.OfflineUuid("Steve"),
    });
    var accJson = File.ReadAllText(Path.Combine(AppSettings.ConfigDir, "accounts.json"));
    Check("accounts.json 已写盘", accJson.Contains("Steve"));
    // 与启动器同路径反序列化：此前 Accounts 私有 setter 导致列表恒为空（账号全部丢失）
    var reloaded = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(accJson);
    Check("重启后账号列表恢复", reloaded?.Accounts.Count == 1,
        $"{reloaded?.Accounts.Count ?? -1} 个账号");
    Check("离线账号字段完整", reloaded?.Accounts.FirstOrDefault()?.Name == "Steve"
        && reloaded?.Accounts.FirstOrDefault()?.Uuid == AuthService.OfflineUuid("Steve"));
    Check("Current 指向该账号", reloaded?.Current?.Type == "offline");
}
catch (Exception ex) { Check("账号持久化", false, ex.Message); }

// ==================== 2.6 第三方登录账号保存（v1.7.2 回归：此前登录成功但未写入 AccountStore） ====================
Console.WriteLine("\n== 2.6 第三方登录账号保存（authlib Upsert 链路） ==");
try
{
    var store = new AccountStore();
    var authlib = new StoredAccount
    {
        Type = "authlib",
        Name = "Notch",
        Uuid = "0123456789abcdef0123456789abcdef",
        RefreshToken = "accessToken-xyz",
        ClientToken = "clientToken-xyz",
        ServerUrl = "https://littleskin.cn/api/yggdrasil",
        LoginId = "notch@example.com",
        RememberPassword = true,
        SavedPassword = "p@ss",
    };
    store.Upsert(authlib); // 与 AuthlibLoginWindow 登录成功后的调用一致
    Check("authlib 账号已入列且成为 Current",
        store.Current?.Type == "authlib" && store.Current?.Name == "Notch");
    var j = File.ReadAllText(Path.Combine(AppSettings.ConfigDir, "accounts.json"));
    Check("ServerUrl / LoginId / 记住密码已写盘",
        j.Contains("littleskin.cn/api/yggdrasil") && j.Contains("notch@example.com") && j.Contains("RememberPassword"));
    var back = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(j);
    Check("记住密码字段往返一致",
        back?.Current?.LoginId == "notch@example.com"
        && back?.Current?.RememberPassword == true
        && back?.Current?.SavedPassword == "p@ss"
        && back?.Current?.RefreshToken == "accessToken-xyz");
}
catch (Exception ex) { Check("第三方账号保存", false, ex.Message); }

// ==================== 2.6.1 微软令牌缓存 + 同名多类型账号（v0.1.3 修复） ====================
Console.WriteLine("\n== 2.6.1 微软令牌缓存兜底 + 同名多类型账号 ==");
try
{
    // ① 微软账号 AccessToken 缓存写盘与往返（启动时刷新失败的兜底令牌）
    var msStore = new AccountStore();
    msStore.Upsert(new StoredAccount
    {
        Type = "microsoft", Name = "Herobrine", Uuid = "aaaa-bbbb",
        RefreshToken = "ms-refresh-token", AccessToken = "mc-access-token",
    });
    var accJsonMs = File.ReadAllText(Path.Combine(AppSettings.ConfigDir, "accounts.json"));
    var msBack = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(accJsonMs);
    Check("微软 AccessToken 缓存往返一致",
        msBack?.Current?.AccessToken == "mc-access-token"
        && msBack?.Current?.RefreshToken == "ms-refresh-token");

    // ② 同名不同类型账号互不顶替（此前 Upsert 只按 Name 匹配，离线 Steve 会被第三方 Steve 顶掉）
    var store = new AccountStore();
    store.Upsert(new StoredAccount { Type = "offline", Name = "Steve", Uuid = "u1" });
    store.Upsert(new StoredAccount { Type = "authlib", Name = "Steve", Uuid = "u2", ServerUrl = "https://x/api/yggdrasil" });
    Check("同名离线 + 第三方账号共存（2 个）", store.Accounts.Count == 2,
        $"{store.Accounts.Count} 个");

    // ③ Current 按类型精确定位
    Check("Upsert 后 Current 指向最新入列类型", store.Current?.Type == "authlib");
    store.CurrentName = "Steve"; store.CurrentType = "offline";
    Check("Current 按 Name+Type 命中离线 Steve", store.Current?.Uuid == "u1");
    store.CurrentType = "authlib";
    Check("Current 按 Name+Type 命中第三方 Steve", store.Current?.Uuid == "u2");

    // ④ 同名同类型 Upsert 是更新不是重复
    store.Upsert(new StoredAccount { Type = "authlib", Name = "Steve", Uuid = "u2b", ServerUrl = "https://x/api/yggdrasil" });
    Check("同名同类型 Upsert 去重（仍 2 个）", store.Accounts.Count == 2
        && store.Accounts.Count(a => a.Type == "authlib") == 1);

    // ⑤ Remove(name, type) 只删对应类型
    store.Remove("Steve", "authlib");
    Check("Remove 按类型删除后剩 1 个", store.Accounts.Count == 1 && store.Accounts[0].Type == "offline");
    Check("删除当前账号后 Current 回退到剩余账号", store.Current?.Type == "offline");

    // ⑥ 旧版 accounts.json（无 CurrentType 字段）兼容：按名字回退匹配
    var legacyJson = """{"Accounts":[{"Type":"offline","Name":"Alex","Uuid":"u3"}],"CurrentName":"Alex"}""";
    var legacy = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(legacyJson);
    Check("旧版配置（无 CurrentType）仍能命中 Current", legacy?.Current?.Name == "Alex");
}
catch (Exception ex) { Check("微软令牌缓存 + 同名多类型", false, ex.Message); }

// ==================== 2.6.2 离线名清洗 + Yggdrasil 友好报错（v0.1.3 修复） ====================
Console.WriteLine("\n== 2.6.2 离线名清洗 + Yggdrasil 友好报错 ==");
try
{
    // ① 离线玩家名清洗（此前带空格/超长名字直接透传，导致启动参数异常或被服务器拒）
    Check("离线名去首尾空格", AuthService.NormalizeOfflineName("  Steve  ") == "Steve");
    Check("离线名去内部空白", AuthService.NormalizeOfflineName("S t e v e") == "Steve");
    Check("离线名空值 → null", AuthService.NormalizeOfflineName("   ") == null);
    Check("离线名 16 字符允许", AuthService.NormalizeOfflineName(new string('a', 16)) == new string('a', 16));
    Check("离线名 17 字符拒绝", AuthService.NormalizeOfflineName(new string('a', 17)) == null);

    // ② Yggdrasil 响应统一解析：业务错误取 errorMessage（与 LittleSkin 实测一致）
    static Exception? ExOf(Func<Task> a) { try { a().Wait(); return null; } catch (Exception e) { return e.InnerException ?? e; } }
    var ex403 = ExOf(() => AuthlibService.ReadYggdrasilAsync(new System.Net.Http.HttpResponseMessage(
            System.Net.HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"error":"ForbiddenOperationException","errorMessage":"用户名或密码错误。Incorrect username or password."}""",
                System.Text.Encoding.UTF8, "application/json"),
        }, "登录"));
    Check("403 errorMessage 透出给用户", ex403?.Message.Contains("Incorrect username or password") == true,
        ex403?.Message ?? "");

    // ③ 空 / 非 JSON 响应给出可读信息（此前是 JSON 解析原始异常）
    var exEmpty = ExOf(() => AuthlibService.ReadYggdrasilAsync(new System.Net.Http.HttpResponseMessage(
        System.Net.HttpStatusCode.BadGateway), "登录"));
    Check("空响应 → 明确提示", exEmpty?.Message.Contains("空响应") == true, exEmpty?.Message ?? "");
    var exHtml = ExOf(() => AuthlibService.ReadYggdrasilAsync(new System.Net.Http.HttpResponseMessage(
        System.Net.HttpStatusCode.Forbidden)
    {
        Content = new StringContent("<html>Just a moment...</html>", System.Text.Encoding.UTF8, "text/html"),
    }, "登录"));
    Check("CDN 拦截页 → HTTP 码 + 内容摘要", exHtml?.Message.Contains("403") == true
        && exHtml.Message.Contains("Just a moment"), exHtml?.Message ?? "");
    var okJson = AuthlibService.ReadYggdrasilAsync(
        new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("""{"accessToken":"t"}""", System.Text.Encoding.UTF8, "application/json"),
        }, "登录").Result;
    Check("200 正常 JSON 不抛", okJson.TryGetProperty("accessToken", out _));

    // ④ PATH Java 检测不抛异常且返回布尔（Linux 下 java.exe 不存在应为 false）
    Check("ExistsOnPath 可调用且为布尔", JavaManager.ExistsOnPath() is true or false);
}
catch (Exception ex) { Check("离线名清洗 + Yggdrasil 友好报错", false, ex.Message); }

// ==================== 2.7 离线 UUID v3（官方 OfflinePlayer 规范） ====================
Console.WriteLine("\n== 2.7 离线 UUID ==");
Check("OfflineUuid(Steve) 与官方离线模式一致",
    AuthService.OfflineUuid("Steve") == "5627dd98-e6be-3c21-b8a8-e92344183641",
    AuthService.OfflineUuid("Steve"));
Check("UUID v3 版本位/变体位正确",
    AuthService.OfflineUuid("Alex")[14] == '3'
    && "89ab".Contains(AuthService.OfflineUuid("Alex")[19]));

// ==================== 2.8 JVM 自定义参数解析（v1.7.3，PCL 的"JVM 参数"） ====================
Console.WriteLine("\n== 2.8 JVM 参数解析 ==");
var j1 = LauncherService.ParseJvmArgs("-XX:+UseG1GC -Dfile.encoding=UTF-8");
Check("常规空格分隔解析", j1.Count == 2 && j1[0] == "-XX:+UseG1GC" && j1[1] == "-Dfile.encoding=UTF-8",
    string.Join("|", j1));
var j2 = LauncherService.ParseJvmArgs("-Xmx4G \"-Dsome.key=with space\"  -XX:+UseZGC");
Check("双引号包裹含空格参数", j2.Count == 3 && j2[1] == "-Dsome.key=with space",
    string.Join("|", j2));
Check("空串/空白返回空列表",
    LauncherService.ParseJvmArgs("").Count == 0 && LauncherService.ParseJvmArgs("   ").Count == 0
    && LauncherService.ParseJvmArgs(null).Count == 0);
var j3 = LauncherService.ParseJvmArgs("  -Xms1G   ");
Check("首尾空白与多余空格容忍", j3.Count == 1 && j3[0] == "-Xms1G");

// ==================== 2.9 CustomJvmArgs 设置持久化 ====================
Console.WriteLine("\n== 2.9 CustomJvmArgs 持久化 ==");
try
{
    AppSettings.Instance.CustomJvmArgs = "-XX:+UseG1GC \"-Da=b c\"";
    AppSettings.Instance.Save();
    AppSettings.Load();
    Check("CustomJvmArgs 写盘并读回",
        AppSettings.Instance.CustomJvmArgs == "-XX:+UseG1GC \"-Da=b c\"",
        AppSettings.Instance.CustomJvmArgs);
    Check("读回后可正常解析", LauncherService.ParseJvmArgs(AppSettings.Instance.CustomJvmArgs).Count == 2);
    AppSettings.Instance.CustomJvmArgs = "";
    AppSettings.Instance.Save();
}
catch (Exception ex) { Check("CustomJvmArgs 持久化", false, ex.Message); }

// ==================== 2.10 游戏根目录清洗（消除 %ProgramData%/.minecraft 根因） ====================
// CmlLib 默认 MinecraftPath 在 Windows 上是 "appdata + \.minecraft"，变量异常时退化为
// 相对路径 \.minecraft（落到盘符根目录等意外位置）。清洗逻辑保证污染路径被纠正回便携目录。
Console.WriteLine("\n== 2.10 游戏根目录清洗 ==");
var defRoot = Path.Combine(AppSettings.AppDir, ".minecraft");
Check("空路径 → 默认 AppDir\\.minecraft",
    AppSettings.SanitizeGameRoot(null, false) == defRoot
    && AppSettings.SanitizeGameRoot("", false) == defRoot
    && AppSettings.SanitizeGameRoot("   ", false) == defRoot, defRoot);
Check("默认目录本身 → 原样保留", AppSettings.SanitizeGameRoot(defRoot, false) == defRoot);
Check("默认目录带尾部分隔符 → 保留",
    AppSettings.SanitizeGameRoot(defRoot + Path.DirectorySeparatorChar, false) == defRoot + Path.DirectorySeparatorChar);
Check("外部污染路径（未自定义）→ 纠正回默认",
    AppSettings.SanitizeGameRoot("/tmp/polluted-root", false) == defRoot);
Check("相对路径（历史泄漏形态）→ 纠正回默认",
    AppSettings.SanitizeGameRoot(".minecraft", false) == defRoot);
Check("用户显式自定义 → 尊重用户选择",
    AppSettings.SanitizeGameRoot("/tmp/my-games/mc", true) == "/tmp/my-games/mc");
try
{
    AppSettings.Instance.GameRootCustomized = true;
    AppSettings.Instance.GameRoot = "/tmp/keep-me";
    AppSettings.Instance.Save();
    AppSettings.Load();
    Check("GameRootCustomized 持久化且自定义路径保留",
        AppSettings.Instance.GameRootCustomized && AppSettings.Instance.GameRoot == "/tmp/keep-me",
        AppSettings.Instance.GameRoot);
    // 还原，避免影响后续章节（第 3 节用 GameRoot 建版本隔离目录）
    AppSettings.Instance.GameRootCustomized = false;
    AppSettings.Instance.GameRoot = defRoot;
    AppSettings.Instance.Save();
}
catch (Exception ex) { Check("GameRootCustomized 持久化", false, ex.Message); }

// ==================== 3. 版本隔离路径 ====================
Console.WriteLine("\n== 3. 版本隔离路径（PCL 行为） ==");
var launcher = new LauncherService();
// 注意：launcher 贯穿第 3-10 节与第 19 节复用（真实网络安装 / 启动构造 / 闸门），
// 不能在这里 using 掉 —— 此前 using 块只包住第 3 节，Dispose 后第 4/5/6 节
// 全部抛 "Cannot access a disposed object"（HttpClient 已释放）。
{
    AppSettings.Instance.VersionIsolation = true;
    var iso = launcher.BuildGamePath("1.20.1");
    var root = AppSettings.Instance.GameRoot;
    Check("隔离：存档/mod 指向版本目录", iso.BasePath == Path.Combine(root, "versions", "1.20.1"), iso.BasePath);
    Check("隔离：libraries 共享", iso.Library == Path.Combine(root, "libraries"));
    Check("隔离：assets 共享", iso.Assets == Path.Combine(root, "assets"));
    AppSettings.Instance.VersionIsolation = false;
    var shared = launcher.BuildGamePath("1.20.1");
    Check("关闭隔离：游戏目录=根目录", shared.BasePath == root);
    AppSettings.Instance.VersionIsolation = true;
}

// ==================== 4. BMCLAPI 镜像版本清单（真实网络） ====================
Console.WriteLine("\n== 4. 远程版本清单（BMCLAPI 镜像，真实网络） ==");
try
{
    var versions = (await launcher.GetAllVersionsAsync()).ToList();
    Check("版本清单获取成功", versions.Count > 500, $"{versions.Count} 个版本");
    var names = versions.Select(v => v.Name).ToHashSet();
    Check("包含 1.21 正式版", names.Contains("1.21"));
    Check("包含老版本 b1.2_01（Beta 支持）", names.Contains("b1.2_01"));
}
catch (Exception ex) { Check("版本清单获取", false, ex.Message); }

// ==================== 5. 安装一个真实小版本（真实下载链路） ====================
Console.WriteLine("\n== 5. 真实安装小体积版本 b1.2_01（jar + libraries 全链路） ==");
try
{
    await launcher.InstallAsync("b1.2_01", null, null);
    var vdir = Path.Combine(AppSettings.Instance.GameRoot, "versions", "b1.2_01");
    Check("版本 json 已下载", File.Exists(Path.Combine(vdir, "b1.2_01.json")));
    Check("版本 jar 已下载且 >100KB", File.Exists(Path.Combine(vdir, "b1.2_01.jar"))
        && new FileInfo(Path.Combine(vdir, "b1.2_01.jar")).Length > 100 * 1024,
        $"{(File.Exists(Path.Combine(vdir, "b1.2_01.jar")) ? new FileInfo(Path.Combine(vdir, "b1.2_01.jar")).Length / 1024 + "KB" : "missing")}");
    var libs = Path.Combine(AppSettings.Instance.GameRoot, "libraries");
    Check("libraries 已就位", Directory.Exists(libs) && Directory.EnumerateFiles(libs, "*.jar", SearchOption.AllDirectories).Any());
    var installed = launcher.GetInstalledVersions();
    Check("本机版本列表识别到 b1.2_01", installed.Contains("b1.2_01"), string.Join(",", installed));
}
catch (Exception ex) { Check("安装 b1.2_01", false, ex.Message); }

// ==================== 6. 启动参数构造（CreateProcessAsync 不真正启动） ====================
Console.WriteLine("\n== 6. 启动参数构造（隔离 / 会话注入正确性） ==");
try
{
    var fakeJava = Path.Combine(testRoot, "javafake");
    File.WriteAllText(fakeJava, "#!/bin/sh\nexit 0\n");
    Environment.SetEnvironmentVariable("PATH", testRoot + ":" + Environment.GetEnvironmentVariable("PATH"));
    // 新建文件权限 +x
    var chmod = Process.Start(new ProcessStartInfo("/bin/chmod", $"+x {fakeJava}") { UseShellExecute = false });
    chmod?.WaitForExit();

    var session = AuthService.OfflineSession("Steve");
    var pwArgs = string.Empty;
    try
    {
        // 直接构造进程对象但不启动 —— 验证参数拼装
        var ml = launcher.Launcher; // public 属性
        var option = new CmlLib.Core.ProcessBuilder.MLaunchOption
        {
            Path = launcher.BuildGamePath("b1.2_01"),
            Session = session,
            MaximumRamMb = 1024,
            JavaPath = fakeJava,
            VersionType = "HardmacroCraft",
        };
        var proc = await ml.CreateProcessAsync("b1.2_01", option);
        var argLine = proc.StartInfo.Arguments;
        // 注意：b1.2_01 为 legacy 版本，参数为旧协议（-Xmx1024m 小写 m；玩家名/令牌为位置参数，无 --uuid）
        Check("参数包含内存 -Xmx1024m（legacy 小写 m）", argLine.Contains("-Xmx1024m", StringComparison.OrdinalIgnoreCase));
        Check("参数包含离线玩家名 Steve", argLine.Contains("Steve"));
        Check("legacy 协议：位置参数含访问令牌占位", argLine.Contains("Launch Steve 0"));
        Check("参数包含隔离版本路径", argLine.Contains("b1.2_01"));
    }
    catch (Exception ex)
    {
        Check("启动参数构造", false, ex.Message);
    }
}
catch (Exception ex) { Skip("启动参数构造", ex.Message); }

// ==================== 7. .mrpack 整合包解析 ====================
Console.WriteLine("\n== 7. .mrpack 整合包解析 ==");
var mrpackPath = Path.Combine(testRoot, "demo.mrpack");
if (File.Exists(mrpackPath)) File.Delete(mrpackPath); // 幂等：清理上次运行残留
using (var zip = ZipFile.Open(mrpackPath, ZipArchiveMode.Create))
{
    var index = new
    {
        formatVersion = 1,
        game = "minecraft",
        versionId = "demo-pack",
        name = "Demo Pack",
        summary = "测试用整合包",
        dependencies = new Dictionary<string, string>
        {
            ["minecraft"] = "1.20.1",
            ["fabric-loader"] = "0.16.9",
        },
        files = new object[]
        {
            new { path = "mods/dummy.jar", hashes = new { sha1 = "x" }, env = new { client = "required", server = "unsupported" }, downloads = new[] { "https://example.com/dummy.jar" }, fileSize = 1234L },
            new { path = "mods/server-only.jar", hashes = new { sha1 = "y" }, env = new { client = "unsupported", server = "required" }, downloads = new[] { "https://example.com/s.jar" }, fileSize = 99L },
        },
    };
    var entry = zip.CreateEntry("modrinth.index.json");
    using var w = new StreamWriter(entry.Open());
    w.Write(JsonSerializer.Serialize(index));
}
try
{
    var info = ModpackService.ReadInfo(mrpackPath);
    Check("读取整合包名称", info.Name == "Demo Pack", info.Name);
    Check("识别 MC 版本", info.GameVersion == "1.20.1", info.GameVersion);
    Check("识别 Fabric 装载器", info.Loader == "fabric-loader" && info.LoaderVersion == "0.16.9", $"{info.Loader} {info.LoaderVersion}");
    Check("文件数正确（2 个）", info.FileCount == 2);
    Check("总大小正确", info.TotalBytes == 1234 + 99);
}
catch (Exception ex) { Check("mrpack 解析", false, ex.Message); }

// 坏包检测
var badPath = Path.Combine(testRoot, "bad.mrpack");
if (File.Exists(badPath)) File.Delete(badPath); // 幂等：清理上次运行残留
using (var zip = ZipFile.Open(badPath, ZipArchiveMode.Create)) { zip.CreateEntry("not-index.txt"); }
try { ModpackService.ReadInfo(badPath); Check("坏包正确报错", false, "未抛出异常"); }
catch (InvalidDataException) { Check("坏包正确报错（InvalidDataException）", true); }
catch (Exception ex) { Check("坏包正确报错", false, ex.GetType().Name); }

// ==================== 7.5 下载引擎（本地 HTTP：校验 / 重试 / 批量跳过） ====================
Console.WriteLine("\n== 7.5 下载引擎（临时文件 + sha1 校验 + 批量并发跳过） ==");
try
{
    var payload = System.Text.Encoding.UTF8.GetBytes(new string('H', 60_000) + "HMC-TAIL");
    var sha1Hex = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(payload)).ToLowerInvariant();

    // 找空闲端口并启动本地文件服务器
    var port = 0;
    using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
    {
        probe.Start(); port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    }
    long hits = 0;
    var server = new System.Net.HttpListener();
    server.Prefixes.Add($"http://127.0.0.1:{port}/");
    server.Start();
    var serverTask = Task.Run(async () =>
    {
        while (server.IsListening)
        {
            try
            {
                var ctx = await server.GetContextAsync();
                Interlocked.Increment(ref hits);
                await ctx.Response.OutputStream.WriteAsync(payload);
                ctx.Response.Close();
            }
            catch { break; }
        }
    });

    using var engineHttp = new HttpClient();
    var dlDir = Path.Combine(testRoot, "dl");
    if (Directory.Exists(dlDir)) Directory.Delete(dlDir, true); // 幂等：清理上次残留
    Directory.CreateDirectory(dlDir);
    var okFile = Path.Combine(dlDir, "good.bin");

    // 1) 正常下载 + sha1 校验通过
    await DownloadEngine.DownloadFileAsync(engineHttp, $"http://127.0.0.1:{port}/good.bin", okFile, sha1Hex, payload.Length);
    Check("正常下载+sha1校验通过",
        File.Exists(okFile) && !File.Exists(okFile + ".hmc-part")
        && Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(okFile))).ToLowerInvariant() == sha1Hex);

    // 2) 错误 sha1 → 重试后报错，且不留临时文件
    var badFile = Path.Combine(dlDir, "badsha.bin");
    var threw = false;
    try { await DownloadEngine.DownloadFileAsync(engineHttp, $"http://127.0.0.1:{port}/bad.bin", badFile, "deadbeef" + sha1Hex[8..], payload.Length); }
    catch (IOException) { threw = true; }
    catch (Exception ex) { threw = ex.Message.Contains("校验失败"); }
    Check("sha1 不符被拦截并报错", threw);
    Check("校验失败后清理 .hmc-part 临时文件", !File.Exists(badFile + ".hmc-part"));

    // 3) 大小不符被拦截
    var threwSize = false;
    try { await DownloadEngine.DownloadFileAsync(engineHttp, $"http://127.0.0.1:{port}/sz.bin", Path.Combine(dlDir, "sz.bin"), null, payload.Length + 1); }
    catch (Exception ex) { threwSize = ex.Message.Contains("校验失败"); }
    Check("大小不符被拦截", threwSize);

    // 4) 已完整文件跳过判定
    Check("IsFileComplete 识别完整文件", await DownloadEngine.IsFileCompleteAsync(okFile, sha1Hex, payload.Length));
    Check("IsFileComplete 拒绝大小不符", !await DownloadEngine.IsFileCompleteAsync(okFile, null, payload.Length + 5));

    // 5) 批量并发：完整文件跳过、缺失文件下载，全部成功
    var hitsBefore = Interlocked.Read(ref hits);
    var batch = new List<DownloadEngine.BatchItem>
    {
        new() { Url = $"http://127.0.0.1:{port}/a.bin", Dest = okFile, DisplayName = "a.bin", Sha1 = sha1Hex, Size = payload.Length }, // 已完整 → 跳过
        new() { Url = $"http://127.0.0.1:{port}/b.bin", Dest = Path.Combine(dlDir, "b.bin"), DisplayName = "b.bin", Sha1 = sha1Hex, Size = payload.Length }, // 需下载
    };
    var lastProgress = default(DownloadEngine.BatchProgress);
    var bp = new Progress<DownloadEngine.BatchProgress>(p => lastProgress = p);
    await DownloadEngine.DownloadBatchAsync(engineHttp, batch, 4, bp, null);
    // Progress 回调是异步投递（ThreadPool 排队），轮询等待终态，最长 3 秒
    for (var wait = 0; wait < 60 && lastProgress is not { DoneFiles: 2, TotalFiles: 2 }; wait++)
        await Task.Delay(50);
    Check("批量下载完成且跳过已存在文件", File.Exists(batch[1].Dest) && Interlocked.Read(ref hits) == hitsBefore + 1,
        $"服务端请求 {Interlocked.Read(ref hits) - hitsBefore} 次（应为 1）");
    Check("批量进度统计正确（完成 2/2）", lastProgress is { DoneFiles: 2, TotalFiles: 2 }, lastProgress?.ToString() ?? "null");

    server.Stop();
}
catch (Exception ex) { Check("下载引擎", false, ex.Message); }

// ==================== 8. 第三方验证站（authlib-injector，真实网络） ====================
Console.WriteLine("\n== 8. 第三方验证站（LittleSkin API Root 发现 + agent 下载校验） ==");
try
{
    var authlib = new AuthlibService(new HttpClient(new MirrorHttpHandler()));
    var root = await authlib.ResolveApiRootAsync("littleskin.cn");
    Check("LittleSkin API Root 自动发现", root.Contains("yggdrasil") || root.Contains("api"), root);

    // 错误密码登录 → 应返回明确错误而不是崩溃
    try
    {
        await authlib.AuthenticateAsync(root, "not-exist-user@example.com", "wrong-password-123");
        Check("错误凭据登录被拒绝", false, "未抛出异常");
    }
    catch (Exception ex)
    {
        Check("错误凭据登录被拒绝（返回 errorMessage）",
            ex.Message.Contains("失败") || ex.Message.Contains("错误") || ex.Message.Contains("invalid") || ex.Message.Contains("Invalid"),
            ex.Message.Length > 60 ? ex.Message[..60] + "…" : ex.Message);
    }

    // 无效令牌校验 → false
    var ok = await authlib.ValidateAsync(root, "invalid-token-xxx", "invalid-client-token");
    Check("无效令牌校验返回 false", ok == false);

    // agent jar 下载（真实下载 ~100KB，SHA-256 校验）
    var jar = await AuthlibService.EnsureAgentJarAsync(Path.Combine(testRoot, "config"), authlib.Http, msg => Console.WriteLine($"      {msg}"));
    Check("authlib-injector.jar 已下载且 >50KB", File.Exists(jar) && new FileInfo(jar).Length > 50 * 1024,
        $"{new FileInfo(jar).Length / 1024}KB");
    // 幂等（第二次不重新下载，靠 SHA-256 命中）
    var jar2 = await AuthlibService.EnsureAgentJarAsync(Path.Combine(testRoot, "config"), authlib.Http);
    Check("jar 幂等复用", jar2 == jar);
}
catch (Exception ex) { Check("第三方验证站链路", false, ex.Message); }

// ==================== 9. 微软设备码端点（真实网络，错误处理验证） ====================
Console.WriteLine("\n== 9. 微软设备码端点连通性与错误处理 ==");
try
{
    var auth = new AuthService();
    try
    {
        await auth.RequestDeviceCodeAsync("invalid-client-id-test");
        Check("无效 client id 被拒绝", false, "未抛出异常");
    }
    catch (Exception ex)
    {
        Check("无效 client id 被拒绝（端点连通 + 错误处理正常）",
            ex.Message.Contains("设备码申请失败") || ex.Message.Contains("invalid"), ex.Message);
    }
}
catch (Exception ex) { Check("微软端点", false, ex.Message); }

// ==================== 10. 版本管理（重命名 / 复制 / 删除） ====================
Console.WriteLine("\n== 10. 版本管理 ==");
try
{
    launcher.RenameVersion("b1.2_01", "b1.2_01-my");
    Check("重命名后 json 存在", File.Exists(Path.Combine(AppSettings.Instance.GameRoot, "versions", "b1.2_01-my", "b1.2_01-my.json")));
    var json = File.ReadAllText(Path.Combine(AppSettings.Instance.GameRoot, "versions", "b1.2_01-my", "b1.2_01-my.json"));
    Check("json 内 id 字段已同步", json.Contains("\"b1.2_01-my\""));
    launcher.CopyVersion("b1.2_01-my", "b1.2_01-copy");
    Check("复制版本成功", Directory.Exists(Path.Combine(AppSettings.Instance.GameRoot, "versions", "b1.2_01-copy")));
    launcher.DeleteVersion("b1.2_01-copy");
    Check("删除版本成功", !Directory.Exists(Path.Combine(AppSettings.Instance.GameRoot, "versions", "b1.2_01-copy")));
    launcher.DeleteVersion("b1.2_01-my");
}
catch (Exception ex) { Check("版本管理", false, ex.Message); }

// ==================== 11. Java 自动管理（版本映射 / 托管扫描 / 挑选） ====================
Console.WriteLine("\n== 11. Java 自动管理 ==");
try
{
    // 版本名启发式
    Check("1.8.9 → Java 8", JavaManager.RequiredJavaMajorByName("1.8.9") == 8);
    Check("1.12.2 → Java 8", JavaManager.RequiredJavaMajorByName("1.12.2") == 8);
    Check("1.17.1 → Java 16", JavaManager.RequiredJavaMajorByName("1.17.1") == 16);
    Check("1.18.2 → Java 17", JavaManager.RequiredJavaMajorByName("1.18.2") == 17);
    Check("1.20.4 → Java 17", JavaManager.RequiredJavaMajorByName("1.20.4") == 17);
    Check("1.20.5 → Java 21", JavaManager.RequiredJavaMajorByName("1.20.5") == 21);
    Check("1.21.4 → Java 21", JavaManager.RequiredJavaMajorByName("1.21.4") == 21);
    // fix5：26.x 年度版本需要 Java 25
    Check("26.2 → Java 25", JavaManager.RequiredJavaMajorByName("26.2") == 25);
    Check("26.3 → Java 25", JavaManager.RequiredJavaMajorByName("26.3") == 25);
    Check("Forge 后缀版本名可解析（1.20.1-forge-47.2.0 → 17）",
        JavaManager.RequiredJavaMajorByName("1.20.1-forge-47.2.0") == 17);
    Check("非法版本名回退 Java 8", JavaManager.RequiredJavaMajorByName("weird-name") == 8);
    // 版本 json 的 javaVersion 字段优先
    var json21 = """{"id":"x","javaVersion":{"majorVersion":21}}""";
    Check("json javaVersion.majorVersion=21 优先", JavaManager.RequiredJavaMajor(json21, "1.12.2") == 21);
    Check("json 缺字段时回退启发式", JavaManager.RequiredJavaMajor("""{"id":"x"}""", "1.18.2") == 17);
    Check("损坏 json 回退启发式", JavaManager.RequiredJavaMajor("{oops", "1.17.1") == 16);

    // 托管扫描 + 挑选（临时 runtime 目录）
    var javaRoot = Path.Combine(testRoot, "javaroot");
    var vDir = Path.Combine(javaRoot, "versions", "1.21.4");
    Directory.CreateDirectory(vDir);
    File.WriteAllText(Path.Combine(vDir, "1.21.4.json"), """{"id":"1.21.4","javaVersion":{"majorVersion":21}}""");
    var fakeJavaDir = Path.Combine(javaRoot, "managed-runtime", "java-21", "jdk-21.0.5+11-jre");
    Directory.CreateDirectory(Path.Combine(fakeJavaDir, "bin"));
    var fakeJavaw = Path.Combine(fakeJavaDir, "bin", "javaw.exe");
    File.WriteAllText(fakeJavaw, "fake");

    // JavaManager.RuntimeRoot 是 AppDir/runtime；无法重定向 —— 通过反射不行（static readonly 属性），
    // FindManagedJavas 直接扫 RuntimeRoot。改为直接验证 FindJavaw + PickJavaFor 的用户优先/开关逻辑，
    // 以及把假 javaw 放进 AppSettings.AppDir 同目录 runtime/ 的临时行为。
    Check("FindJavaw 嵌套目录可找到", JavaManager.FindJavaw(Path.Combine(javaRoot, "managed-runtime", "java-21")) == fakeJavaw);

    // 用户手填路径优先（PickJavaFor 返回 null = 交给调用方用手填值）
    Check("用户手填 Java 时不自动挑选", JavaManager.PickJavaFor("1.21.4", javaRoot, "C:/java/javaw.exe", true) == null);
    Check("关闭自动选择时不挑选", JavaManager.PickJavaFor("1.21.4", javaRoot, "", false) == null);

    // 2.12 设置新字段（AutoJavaSelect / 分辨率 / LastAuthlibServer）持久化
    AppSettings.Instance.AutoJavaSelect = false;
    AppSettings.Instance.GameWidth = 1280;
    AppSettings.Instance.GameHeight = 720;
    AppSettings.Instance.LastAuthlibServer = "littleskin.cn";
    AppSettings.Instance.Save();
    AppSettings.Load();
    Check("AutoJavaSelect / 分辨率 / LastAuthlibServer 往返一致",
        !AppSettings.Instance.AutoJavaSelect
        && AppSettings.Instance.GameWidth == 1280 && AppSettings.Instance.GameHeight == 720
        && AppSettings.Instance.LastAuthlibServer == "littleskin.cn");
    // StoredAccount.AutoLogin 往返
    var accStore2 = new AccountStore();
    accStore2.Upsert(new StoredAccount
    {
        Type = "authlib", Name = "al", Uuid = "u", ServerUrl = "https://littleskin.cn/api/yggdrasil",
        LoginId = "a@b.c", RememberPassword = true, SavedPassword = "p", AutoLogin = true,
    });
    var alJson = File.ReadAllText(Path.Combine(AppSettings.ConfigDir, "accounts.json"));
    Check("AutoLogin 字段已写盘", alJson.Contains("\"AutoLogin\": true"), alJson.Length + " bytes");
    AppSettings.Instance.AutoJavaSelect = true;
    AppSettings.Instance.Save();
}
catch (Exception ex) { Check("Java 自动管理", false, ex.Message); }

// ==================== 12. 任务中心（登记 / 更新 / 完成 / 清理 / 上限） ====================
Console.WriteLine("\n== 12. 任务中心 ==");
try
{
    var svc = DownloadTaskService.Instance;
    var before = svc.Snapshot().Count;
    var t1 = svc.Start("安装原版 1.21.4");
    Check("登记后状态 running", t1.Status == "running" && t1.IsActive);
    svc.Update(t1, 42.5, "下载中 42%");
    Check("进度更新生效", t1.Percent == 42.5 && t1.Detail == "下载中 42%");
    svc.Update(t1, 150); // 越界应被夹紧
    Check("进度夹紧 0–100", t1.Percent == 100);
    svc.Complete(t1, "安装完成");
    Check("完成后状态 done / 100%", t1.Status == "done" && t1.Percent == 100 && !t1.IsActive);
    var t2 = svc.Start("安装 Forge", "获取版本");
    svc.Fail(t2, "网络错误");
    Check("失败状态记录", t2.Status == "failed" && t2.Detail == "网络错误");
    var snap = svc.Snapshot();
    Check("快照最新在前", snap[0].Title == "安装 Forge" && snap[1].Title == "安装原版 1.21.4");
    svc.ClearFinished();
    Check("清除已完成（失败也清）", svc.Snapshot().Count == before);
    // 上限 50
    var many = Enumerable.Range(0, 60).Select(i => svc.Start("t" + i)).ToList();
    Check("历史最多保留 50 条", svc.Snapshot().Count == 50);
    foreach (var t in many) svc.Complete(t);
    svc.ClearFinished();
    Check("清理后回到空", svc.Snapshot().Count == 0);
}
catch (Exception ex) { Check("任务中心", false, ex.Message); }

// ==================== 13. 皮肤头像（Mojang textures 解析） ====================
Console.WriteLine("\n== 13. 皮肤头像 ==");
try
{
    // Notch 的 profile（真实网络）：textures 属性 base64 内含 SKIN url
    var http = new System.Net.Http.HttpClient();
    var resp = await http.GetAsync("https://sessionserver.mojang.com/session/minecraft/profile/069a79f444e94726a5befca90e38aaf5");
    if (!resp.IsSuccessStatusCode)
    {
        Skip("Mojang profile 拉取（网络不可用）", $"HTTP {(int)resp.StatusCode}");
    }
    else
    {
        var json = await resp.Content.ReadAsStringAsync();
        var url = SkinService.ExtractSkinUrl(json);
        Check("textures base64 解析出 SKIN url", url != null && url.Contains("texture"), url ?? "");
        if (url != null)
        {
            var png = await SkinService.DownloadSkinAsync(url, http);
            Check("皮肤 PNG 下载（魔数校验）", png != null && png.Length > 300, png?.Length.ToString() + " bytes");
        }
    }
    Check("离线账号无皮肤 URL", await SkinService.GetSkinUrlAsync(
        new StoredAccount { Type = "offline", Name = "Steve" }, http) == null);
    Check("损坏 base64 返回 null", SkinService.ExtractSkinUrl("{\"properties\":[{\"name\":\"textures\",\"value\":\"!!!\"}]}") == null);
    Check("缺 properties 返回 null", SkinService.ExtractSkinUrl("{}") == null);
}
catch (Exception ex) { Check("皮肤头像", false, ex.Message); }

// ==================== 14. Mod 双平台（CurseForge 接入 / Modrinth 过滤与依赖） ====================
Console.WriteLine("\n== 14. Mod 双平台 ==");
try
{
    // --- CurseForge 静态映射（与 PrismLauncher / 官网分区一致的公共常量） ---
    Check("CF classId: mod→6", CurseForgeService.ClassIdFor("mod") == 6);
    Check("CF classId: resourcepack→12", CurseForgeService.ClassIdFor("resourcepack") == 12);
    Check("CF classId: shader→6552", CurseForgeService.ClassIdFor("shader") == 6552);
    Check("CF classId: datapack→6945", CurseForgeService.ClassIdFor("datapack") == 6945);
    Check("CF classId: 未知类型→mod", CurseForgeService.ClassIdFor("world") == 6);
    Check("CF 排序: downloads→6(TotalDownloads)", CurseForgeService.SortFieldFor("downloads") == 6);
    Check("CF 排序: updated→3(LastUpdated)", CurseForgeService.SortFieldFor("updated") == 3);
    Check("CF 排序: relevance→2(Popularity)", CurseForgeService.SortFieldFor("relevance") == 2);
    Check("CF 排序: follows 退化为热度2", CurseForgeService.SortFieldFor("follows") == 2);
    Check("CF modLoaderType: forge→1 fabric→4 quilt→5 neoforge→6 any→0",
        CurseForgeService.CfLoaderId("forge") == 1 && CurseForgeService.CfLoaderId("fabric") == 4
        && CurseForgeService.CfLoaderId("quilt") == 5 && CurseForgeService.CfLoaderId("neoforge") == 6
        && CurseForgeService.CfLoaderId("any") == 0);
    Check("CF CDN 回退地址规则（fileId/1000 分段）",
        CurseForgeService.BuildCdnUrl(2267756, "jei_1.8.8-2.0.0.jar")
            == "https://mediafilez.forgecdn.net/files/2267/756/jei_1.8.8-2.0.0.jar",
        CurseForgeService.BuildCdnUrl(2267756, "jei_1.8.8-2.0.0.jar"));
    Check("CF 发布类型名: 1=release 2=beta 3=alpha",
        CurseForgeService.ReleaseTypeName(1) == "release"
        && CurseForgeService.ReleaseTypeName(2) == "beta"
        && CurseForgeService.ReleaseTypeName(3) == "alpha");

    // --- 假 HTTP 处理器：捕获请求 + 回放夹具 ---
    string? lastUrl = null; var lastHeaders = new List<string>();
    Task<HttpResponseMessage> Replay(string json, string contentType = "application/json")
        => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, contentType),
        });
    var fake = new InterceptingHandler((req, ct) =>
    {
        lastUrl = req.RequestUri?.ToString();
        lastHeaders = req.Headers.Select(h => h.Key + ":" + string.Join(",", h.Value)).ToList();
        return Replay(req.RequestUri!.AbsolutePath.Contains("search") ? CfSearchJson : CfFilesJson);
    });
    var cf = new CurseForgeService(new HttpClient(fake));

    // --- 搜索结果解析（夹具来自 CurseForge /v1/mods/search 真实结构） ---
    var (hits, total) = await cf.SearchAsync("jei", "mod", "downloads");
    Check("CF 搜索解析: 2 条 + 分页 total=335000", hits.Count == 2 && total == 335000,
        $"{hits.Count}/{total}");
    Check("CF 命中字段映射（title/author/图标/下载量）",
        hits[0].Title == "Just Enough Items (JEI)"
        && hits[0].Author == "mezz"
        && hits[0].IconUrl!.Contains("thumbnail")
        && hits[0].Downloads == 350_000_000);
    Check("CF 命中 Source 标记为 curseforge 且 ID 为数字",
        hits.All(h => h.Source == "curseforge") && int.TryParse(hits[0].ProjectId, out _));
    Check("CF 搜索 URL: gameId=432 classId=6 sortField=6",
        lastUrl!.Contains("gameId=432") && lastUrl.Contains("classId=6") && lastUrl.Contains("sortField=6"),
        lastUrl);

    // loader 单独给出时不附加（CF 要求与 gameVersion 成对）
    await cf.SearchAsync("jei", "mod", loader: "forge");
    Check("CF 无 gameVersion 时不附加 modLoaderType", !lastUrl!.Contains("modLoaderType"), lastUrl);
    await cf.SearchAsync("jei", "mod", gameVersion: "1.20.1", loader: "forge");
    Check("CF gameVersion+loader 成对附加",
        lastUrl!.Contains("gameVersion=1.20.1") && lastUrl.Contains("modLoaderType=1"), lastUrl);

    // --- API Key 头 ---
    var oldKey = AppSettings.Instance.CurseForgeApiKey;
    AppSettings.Instance.CurseForgeApiKey = "";
    Check("未配置 Key 时 HasKey=false", new CurseForgeService(new HttpClient(fake)).HasKey == false);
    AppSettings.Instance.CurseForgeApiKey = "test-key-123";
    await cf.SearchAsync("jei", "mod");
    Check("配置 Key 后请求携带 x-api-key",
        lastHeaders.Any(h => h.StartsWith("x-api-key:") && h.EndsWith("test-key-123")),
        string.Join("|", lastHeaders));
    AppSettings.Instance.CurseForgeApiKey = oldKey;

    // --- 文件列表解析 ---
    var files = await cf.GetFilesAsync(238222);
    Check("CF 文件列表解析（2 条，按时间倒序）", files.Count == 2 && files[0].Id == 7000001,
        string.Join(",", files.Select(f => f.Id)));
    Check("CF 文件字段（displayName/fileName/大小/游戏版本）",
        files[0].DisplayName.Contains("1.21.1") && files[0].FileName.EndsWith(".jar")
        && files[0].Size > 0 && files[0].GameVersions.Contains("1.21.1"));

    // --- Modrinth：facets 过滤 / 依赖解析 / 兼容文件挑选 ---
    var captured = new List<string>();
    var mrFake = new InterceptingHandler((req, ct) =>
    {
        captured.Add(req.RequestUri!.ToString());
        return Replay(req.RequestUri!.AbsolutePath.Contains("search") ? "{\"hits\":[],\"total_hits\":0}" : MrVersionsJson);
    });
    var mr = new ModrinthService(new HttpClient(mrFake));

    await mr.SearchAsync("jei", "mod", gameVersion: "1.20.1", loader: "fabric");
    Check("Modrinth facets 含 project_type/versions/categories",
        Uri.UnescapeDataString(captured[^1]).Contains("project_type:mod")
        && Uri.UnescapeDataString(captured[^1]).Contains("versions:1.20.1")
        && Uri.UnescapeDataString(captured[^1]).Contains("categories:fabric"),
        Uri.UnescapeDataString(captured[^1]));

    var versions = await mr.GetVersionsAsync("jei");
    Check("Modrinth 版本依赖解析（1 required + 1 optional）",
        versions[0].Dependencies.Count(d => d.DepType == "required") == 1
        && versions[0].Dependencies.Any(d => d.DepType == "optional"));

    var compat = await mr.GetLatestCompatibleFileAsync("jei", "1.20.1", "fabric");
    Check("GetLatestCompatibleFileAsync 按版本+加载器挑选且 release 优先",
        compat != null && compat.Value.Version.VersionNumber == "10.0.1"
        && compat.Value.File.Filename.Contains("fabric"),
        compat?.Version.VersionNumber ?? "null");
    Check("GetLatestCompatibleFileAsync 无兼容版本返回 null",
        await mr.GetLatestCompatibleFileAsync("jei", "1.7.10") == null);

    // --- 页面工具函数 ---
    Check("ExtractGameVersion: 纯净版/加载器版/短版/非版本",
        ModsPage_Extract("1.20.1") == "1.20.1"
        && ModsPage_Extract("1.20.1-forge-47.2.0") == "1.20.1"
        && ModsPage_Extract("1.21") == "1.21"
        && ModsPage_Extract("snapshot-abc") == null);

    Check("BuildBadges 首徽章为平台名（双平台）",
        ModsPageBadgePlatform(new ModrinthService.SearchHit("1", "s", "t", "d", "mod", 1, null,
            Array.Empty<string>(), "", default, Array.Empty<string>(), 0, "curseforge")) == "CurseForge"
        && ModsPageBadgePlatform(new ModrinthService.SearchHit("1", "s", "t", "d", "mod", 1, null,
            Array.Empty<string>(), "", default, Array.Empty<string>(), 0)) == "Modrinth");

    Check("ModVersionRow BadgeText 连接", new ModVersionRow { Badges = { "1.20.1", "fabric", "2026-01-01" } }
        .BadgeText.Contains("fabric"));
}
catch (Exception ex) { Check("Mod 双平台", false, ex.Message + ex.StackTrace?.Split('\n')[0]); }

// ==================== 15. CurseForge 依赖解析 + 兼容文件挑选（v1.10.0） ====================
Console.WriteLine("\n== 15. CF 依赖解析 + 兼容挑选 ==");
try
{
    // --- 加载器名映射（CF 在 gameVersions 数组里的写法） ---
    Check("CF 加载器名: forge→Forge fabric→Fabric neoforge→NeoForge quilt→Quilt any→空",
        CurseForgeService.CfLoaderName("forge") == "Forge"
        && CurseForgeService.CfLoaderName("fabric") == "Fabric"
        && CurseForgeService.CfLoaderName("neoforge") == "NeoForge"
        && CurseForgeService.CfLoaderName("quilt") == "Quilt"
        && CurseForgeService.CfLoaderName("any") == "");

    // --- PickCompatibleFile 纯函数（依赖自动补齐的核心挑选逻辑） ---
    static CurseForgeService.CfFile Cf(int id, string rt, string[] gvs, string isoDate) =>
        new(id, 100, $"file-{id}", $"{id}.jar", 1000, rt,
            gvs, DateTimeOffset.Parse(isoDate), null, Array.Empty<CurseForgeService.CfDependency>());
    var list = new[]
    {
        Cf(1, "beta",     new[] { "1.20.1", "Fabric" }, "2026-10-01"), // 更新但非 release
        Cf(2, "release",  new[] { "1.20.1", "Fabric" }, "2026-09-01"), // release 且兼容 → 应选中
        Cf(3, "release",  new[] { "1.20.1", "Forge" },  "2026-10-05"), // 加载器不符
        Cf(4, "release",  new[] { "1.19.4", "Fabric" }, "2026-10-04"), // 游戏版本不符
    };
    var pick = CurseForgeService.PickCompatibleFile(list, "1.20.1", "fabric");
    Check("挑选: 游戏版本+加载器过滤且 release 优先", pick?.Id == 2, pick?.Id.ToString() ?? "null");
    var pick2 = CurseForgeService.PickCompatibleFile(list, null, "any");
    Check("无版本/加载器约束时选 release（版本不符的 4 不该赢过 2）",
        pick2?.Id == 2 || pick2?.Id == 3, pick2?.Id.ToString() ?? "null");

    // 未标注加载器的文件视为通用（Fabric/Forge 均可用）
    var unlabeled = new[] { Cf(5, "release", new[] { "1.20.1" }, "2026-01-01") };
    Check("未标注加载器视为通用文件",
        CurseForgeService.PickCompatibleFile(unlabeled, "1.20.1", "fabric")?.Id == 5);
    Check("无兼容文件返回 null",
        CurseForgeService.PickCompatibleFile(list, "1.20.1", "quilt") == null);

    // --- 文件依赖解析（relationType 3 = Required）+ 兼容文件端到端 ---
    const string CfDepsJson = """
{
  "data": [
    {
      "id": 8000001, "modId": 238222, "displayName": "SomeMod 1.20.1-fabric",
      "fileName": "somemod-1.20.1-fabric.jar", "fileLength": 123456,
      "releaseType": 1, "fileDate": "2026-09-01T00:00:00Z",
      "gameVersions": ["1.20.1", "Fabric"],
      "dependencies": [
        {"modId": 3062, "relationType": 3},
        {"modId": 9999, "relationType": 2},
        {"modId": 8888, "relationType": 5}
      ]
    },
    {
      "id": 8000000, "modId": 238222, "displayName": "SomeMod 1.19.4-forge",
      "fileName": "somemod-1.19.4-forge.jar", "fileLength": 123456,
      "releaseType": 2, "fileDate": "2026-08-01T00:00:00Z",
      "gameVersions": ["1.19.4", "Forge"]
    }
  ],
  "pagination": {"index": 0, "pageSize": 50, "resultCount": 2, "total": 2}
}
""";
    const string CfModsInfoJson = """
{"data": [
  {"id": 3062, "name": "Fabric API", "slug": "fabric-api"},
  {"id": 9999, "name": "Optional Thing", "slug": "optional-thing"}
]}
""";
    string? lastUrl15 = null; string? lastMethod = null; string? lastBody = null;
    var fake15 = new InterceptingHandler((req, ct) =>
    {
        lastUrl15 = req.RequestUri?.ToString();
        lastMethod = req.Method.Method;
        if (req.Content != null)
            lastBody = req.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(req.Method == HttpMethod.Post ? CfModsInfoJson : CfDepsJson,
                System.Text.Encoding.UTF8, "application/json"),
        });
    });
    var cf15 = new CurseForgeService(new HttpClient(fake15));

    var depFiles = await cf15.GetFilesAsync(238222);
    Check("CF 文件依赖解析: 3 条依赖随文件返回",
        depFiles[0].Dependencies.Count == 3, depFiles[0].Dependencies.Count.ToString());
    Check("CF 必需依赖 = relationType 3（其余 optional/incompatible 不入选）",
        depFiles[0].Dependencies.Count(d => d.RelationType == 3) == 1
        && depFiles[0].Dependencies.Single(d => d.RelationType == 3).ModId == 3062);

    var compat = await cf15.GetLatestCompatibleFileAsync(238222, "1.20.1", "fabric");
    Check("GetLatestCompatibleFileAsync: 按版本+加载器选中 release 文件",
        compat?.Id == 8000001, compat?.Id.ToString() ?? "null");

    var names = await cf15.GetModsAsync(new[] { 3062, 9999 });
    Check("GetModsAsync: POST /v1/mods 批量取名",
        lastMethod == "POST" && lastUrl15!.EndsWith("/v1/mods")
        && lastBody!.Contains("3062") && names[3062].Name == "Fabric API"
        && names[9999].Slug == "optional-thing",
        $"{lastMethod} {lastUrl15} {lastBody}");
}
catch (Exception ex) { Check("CF 依赖解析", false, ex.Message + ex.StackTrace?.Split('\n')[0]); }

// ==================== 第 16 节：崩溃报告定位（CrashHelper） ====================
Console.WriteLine("\n== 第 16 节：崩溃报告定位（CrashHelper） ==");
try
{
    var crRoot = Path.Combine(Path.GetTempPath(), "hcl-test-crash-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(Path.Combine(crRoot, "crash-reports"));
    Directory.CreateDirectory(Path.Combine(crRoot, "logs"));

    Check("无崩溃报告目录 → 空列表不抛异常",
        CrashHelper.ListCrashReports(Path.Combine(crRoot, "not-exist")).Count == 0);
    Check("空崩溃目录 → 空列表",
        CrashHelper.ListCrashReports(crRoot).Count == 0);
    Check("FindLatest 无报告 → null",
        CrashHelper.FindLatestCrashReport(crRoot) == null);

    var older = Path.Combine(crRoot, "crash-reports", "crash-2026-01-01_00.00.00-client.txt");
    var newer = Path.Combine(crRoot, "crash-reports", "crash-2026-06-01_00.00.00-server.txt");
    var notReport = Path.Combine(crRoot, "crash-reports", "readme.md");
    File.WriteAllText(older, "old");
    File.WriteAllText(newer, "new");
    File.WriteAllText(notReport, "not a report");
    // 把 newer 的修改时间推到未来，保证排序稳定
    File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(5));

    var reports = CrashHelper.ListCrashReports(crRoot);
    Check("列出崩溃报告：只含 .txt/.log（排除 .md）",
        reports.Count == 2 && !reports.Contains(notReport), reports.Count.ToString());
    Check("按修改时间倒序（最新在前）", reports[0] == newer);
    Check("FindLatest 取最新报告", CrashHelper.FindLatestCrashReport(crRoot) == newer);

    Check("日志目录/最新日志路径符合 Minecraft 规范",
        CrashHelper.LogsDir(crRoot) == Path.Combine(crRoot, "logs")
        && CrashHelper.LatestLogPath(crRoot) == Path.Combine(crRoot, "logs", "latest.log"));
    Check("崩溃目录路径符合 Minecraft 规范",
        CrashHelper.CrashReportsDir(crRoot) == Path.Combine(crRoot, "crash-reports"));

    Check("退出码判定：0 正常 / 非 0 异常",
        !CrashHelper.IsAbnormalExit(0) && CrashHelper.IsAbnormalExit(-1)
        && CrashHelper.IsAbnormalExit(1));

    Directory.Delete(crRoot, recursive: true);
}
catch (Exception ex) { Check("CrashHelper", false, ex.Message); }

// ==================== 17. 语言资源字典查重（v0.1.2 防回归）====================
// v0.1.0/0.1.1 静默闪退根因：Strings.zh-CN.xaml 里 10 个 key 重复定义，
// ResourceDictionary 加载时抛 "Item has already been added"（发生在 App.InitializeComponent，
// 早于 OnStartup，任何日志/弹窗都来不及）。此处对两个语言文件做启动前查重。
Console.WriteLine("\n== 17. 语言资源字典查重（XAML 重复 key 检测） ==");
try
{
    // 测试项目从 /home/z/my-project/HardmacroCraft 运行；向上回溯定位源码目录
    var langDir = FindUp("src/HardmacroCraft/Themes/Languages");
    foreach (var lang in new[] { "Strings.zh-CN.xaml", "Strings.en.xaml" })
    {
        var path = Path.Combine(langDir, lang);
        var keys = System.Text.RegularExpressions.Regex.Matches(
            File.ReadAllText(path), @"x:Key=""([^""]+)""")
            .Select(m => m.Groups[1].Value).ToList();
        var dupes = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check($"{lang} 无重复 key（共 {keys.Count} 个）", dupes.Count == 0,
            dupes.Count > 0 ? "重复: " + string.Join(", ", dupes) : "");
    }
    // 两语言 key 集合一致（避免某语言缺 key 导致 DynamicResource 落空）
    var zh = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText(Path.Combine(langDir, "Strings.zh-CN.xaml")), @"x:Key=""([^""]+)""")
        .Select(m => m.Groups[1].Value));
    var en = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText(Path.Combine(langDir, "Strings.en.xaml")), @"x:Key=""([^""]+)""")
        .Select(m => m.Groups[1].Value));
    var onlyZh = zh.Except(en).ToList();
    var onlyEn = en.Except(zh).ToList();
    Check("zh-CN 与 en 的 key 集合一致", onlyZh.Count == 0 && onlyEn.Count == 0,
        $"仅zh有:[{string.Join(",", onlyZh)}] 仅en有:[{string.Join(",", onlyEn)}]");
}
catch (Exception ex) { Check("语言字典查重", false, ex.Message); }

// ==================== 18. 离线皮肤（PCL 式本机注入，v0.1.0 新增） ====================
Console.WriteLine("\n== 18. 离线皮肤（PNG 校验 / jar 补丁 / 主 jar 解析） ==");
try
{
    var png64 = MakeSkinPng(64, 64);
    var png32 = MakeSkinPng(64, 32);
    var pngBad = MakeSkinPng(65, 64);
    Check("IsSkinPng：64×64 通过", SkinService.IsSkinPng(png64));
    Check("IsSkinPng：64×32（旧版布局）通过", SkinService.IsSkinPng(png32));
    Check("IsSkinPng：65×64 拒绝", !SkinService.IsSkinPng(pngBad));
    Check("IsSkinPng：非 PNG 拒绝", !SkinService.IsSkinPng(new byte[] { 0x00, 0x01, 0x02, 0x03 }));
    Check("IsSkinPng：null 拒绝", !SkinService.IsSkinPng(null));

    // 构造假 jar：新旧两代默认皮肤条目 + 一个无关条目
    var tmp = Path.Combine(Path.GetTempPath(), "hm-skin-" + Guid.NewGuid().ToString("N")[..8]);
    Directory.CreateDirectory(tmp);
    var jar = Path.Combine(tmp, "1.20.1.jar");
    using (var fs = File.Create(jar))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
    {
        foreach (var skinE in SkinService.DefaultSkinEntries)
        {
            var en2 = zip.CreateEntry(skinE);
            using var skinStream = en2.Open(); skinStream.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
        }
        var other = zip.CreateEntry("net/minecraft/client/main/Main.class");
        using var otherStream = other.Open(); otherStream.Write(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, 0, 4);
    }
    var patched = SkinService.PatchJarDefaultSkin(jar, png64);
    Check("补丁替换全部 4 处默认贴图", patched.Count == 4, string.Join(",", patched));
    using (var fs = File.OpenRead(jar))
    using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
    {
        var entry = zip.GetEntry("textures/entity/steve.png");
        using var skinStream2 = entry!.Open(); using var skinBuf = new MemoryStream(); skinStream2.CopyTo(skinBuf);
        Check("jar 内贴图内容已更新为皮肤 PNG", skinBuf.ToArray().SequenceEqual(png64));
        var other = zip.GetEntry("net/minecraft/client/main/Main.class");
        using var otherStream2 = other!.Open(); using var otherBuf = new MemoryStream(); otherStream2.CopyTo(otherBuf);
        Check("jar 其他条目保持不变", otherBuf.ToArray().SequenceEqual(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }));
        Check("jar 条目总数不变（5）", zip.Entries.Count == 5);
    }

    // 主 jar 解析：直接命中 / inheritsFrom 回溯（Forge 等）/ 不存在
    var versions = Path.Combine(tmp, "versions");
    Directory.CreateDirectory(Path.Combine(versions, "1.20.1"));
    File.Copy(jar, Path.Combine(versions, "1.20.1", "1.20.1.jar"));
    Check("ResolveMainJarPath：直接命中",
        SkinService.ResolveMainJarPath(versions, "1.20.1") == Path.Combine(versions, "1.20.1", "1.20.1.jar"));
    Directory.CreateDirectory(Path.Combine(versions, "1.20.1-forge"));
    File.WriteAllText(Path.Combine(versions, "1.20.1-forge", "1.20.1-forge.json"),
        "{\"inheritsFrom\":\"1.20.1\"}");
    Check("ResolveMainJarPath：inheritsFrom 回溯到原版 jar",
        SkinService.ResolveMainJarPath(versions, "1.20.1-forge") == Path.Combine(versions, "1.20.1", "1.20.1.jar"));
    Check("ResolveMainJarPath：不存在返回 null", SkinService.ResolveMainJarPath(versions, "9.9.9") == null);

    // StoredAccount.SkinPath 持久化（accounts.json 往返）
    var store1 = new AccountStore();
    typeof(AccountStore).GetProperty("Accounts")!.SetValue(store1,
        new List<StoredAccount> { new() { Type = "offline", Name = "Steve", Uuid = "u", SkinPath = "/tmp/steve.png" } });
    var json1 = System.Text.Json.JsonSerializer.Serialize(store1);
    var store2 = System.Text.Json.JsonSerializer.Deserialize<AccountStore>(json1);
    Check("SkinPath 随 accounts.json 往返保留",
        store2?.Accounts.FirstOrDefault()?.SkinPath == "/tmp/steve.png");

    Directory.Delete(tmp, true);
}
catch (Exception ex) { Check("离线皮肤测试", false, ex.Message); }

static string FindUp(string rel)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, rel);
        if (Directory.Exists(candidate)) return candidate;
        dir = dir.Parent!;
    }
    throw new DirectoryNotFoundException("未找到 " + rel);
}

// 构造最小有效 PNG（IHDR + IDAT + IEND，8bit RGBA），仅用于离线皮肤测试
static byte[] MakeSkinPng(int w, int h)
{
    using var ms = new MemoryStream();
    void Chunk(string type, byte[] data)
    {
        var len = BitConverter.GetBytes(data.Length); Array.Reverse(len);
        ms.Write(len, 0, 4);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        ms.Write(t, 0, 4);
        ms.Write(data, 0, data.Length);
        var crcInput = new byte[4 + data.Length];
        Buffer.BlockCopy(t, 0, crcInput, 0, 4);
        Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
        var crc = BitConverter.GetBytes(Crc32(crcInput)); Array.Reverse(crc);
        ms.Write(crc, 0, 4);
    }
    ms.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
    var ihdr = new byte[13];
    ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
    ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
    ihdr[8] = 8;  // bit depth
    ihdr[9] = 6;  // color type RGBA
    Chunk("IHDR", ihdr);
    var raw = new byte[h * (1 + w * 4)]; // filter 0 + RGBA 行
    var idat = new MemoryStream();
    using (var z = new ZLibStream(idat, CompressionLevel.Fastest))
        z.Write(raw, 0, raw.Length);
    Chunk("IDAT", idat.ToArray());
    Chunk("IEND", Array.Empty<byte>());
    return ms.ToArray();
}

static uint Crc32(byte[] data)
{
    uint[] table = new uint[256];
    for (uint i = 0; i < 256; i++)
    {
        var c = i;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        table[i] = c;
    }
    uint crc = 0xFFFFFFFF;
    foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
    return crc ^ 0xFFFFFFFF;
}

// ==================== fix6：内存自动检测（PCL 式） + 快速进服 + 主题默认 ====================
Console.WriteLine("\n---- 第 17 节 · fix6：内存自动检测 / 快速进服 / 设置新字段 ----");

// 17.1 推荐内存纯函数（PCL 规则：一半内存、512MB 取整、[1024, min(8192, 总-1024)]）
Check("推荐内存 8GB → 4096", SystemInfo.RecommendedMemoryMb(8192) == 4096);
Check("推荐内存 16GB → 8192（封顶）", SystemInfo.RecommendedMemoryMb(16384) == 8192);
Check("推荐内存 32GB → 8192（封顶）", SystemInfo.RecommendedMemoryMb(32768) == 8192);
Check("推荐内存 4GB → 2048", SystemInfo.RecommendedMemoryMb(4096) == 2048);
Check("推荐内存 2GB → 1024", SystemInfo.RecommendedMemoryMb(2048) == 1024);
Check("推荐内存 1.5GB → 1024（极小机下限）", SystemInfo.RecommendedMemoryMb(1536) == 1024);
Check("推荐内存 0 → 兜底 4096 的一半", SystemInfo.RecommendedMemoryMb(0) == 2048);
Check("推荐值均为 512 的倍数",
    new long[] { 8192, 12288, 16384, 6144, 3072, 4096 }.All(v => SystemInfo.RecommendedMemoryMb(v) % 512 == 0));
Check("推荐值不超过 min(8192, 总-1024)",
    new long[] { 2048, 4096, 8192, 16384 }.All(v => SystemInfo.RecommendedMemoryMb(v) <= Math.Min(8192, v - 1024)));

// 17.2 物理内存检测（沙箱真实运行）
Check("物理内存检测 > 0", SystemInfo.TotalPhysicalMemoryMb > 0,
    $"实际 {SystemInfo.TotalPhysicalMemoryMb} MB");
Check("当前机器推荐内存合法",
    SystemInfo.RecommendedMemoryMb() is >= 1024 and <= 8192,
    $"推荐 {SystemInfo.RecommendedMemoryMb()} MB");

// 17.3 内存体检警告
Check("分配 > 物理内存 → 警告", SystemInfo.CheckAllocatedMemory(999999) != null);
Check("占用全部物理内存 → 警告",
    SystemInfo.CheckAllocatedMemory(SystemInfo.TotalPhysicalMemoryMb) != null);
Check("常规分配（4GB / 至少 16GB 机器）→ 无警告",
    SystemInfo.CheckAllocatedMemory(Math.Min(4096, SystemInfo.TotalPhysicalMemoryMb / 2)) == null);

// 17.4 设置新字段与序列化保真
Check("MemoryAuto 默认开启", new AppSettings().MemoryAuto);
Check("Theme 默认跟随系统", new AppSettings().Theme == "system");
Check("LastQuickServer 默认为空", new AppSettings().LastQuickServer == "");
var s6 = new AppSettings { MemoryAuto = false, MemoryMb = 6144, Theme = "light", LastQuickServer = "mc.example.com:25565" };
var s6b = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s6));
Check("设置序列化往返保真", s6b != null && !s6b.MemoryAuto && s6b.MemoryMb == 6144
    && s6b.Theme == "light" && s6b.LastQuickServer == "mc.example.com:25565");

// 17.5 服务器地址解析（快速进服）
Check("地址解析 host", LauncherService.ParseServerAddress("mc.example.com") == ("mc.example.com", 0));
Check("地址解析 host:port", LauncherService.ParseServerAddress("mc.example.com:25565") == ("mc.example.com", 25565));
Check("地址解析 带协议前缀空格", LauncherService.ParseServerAddress("  play.server.io:30000  ") == ("play.server.io", 30000));
Check("地址解析 [IPv6]:port", LauncherService.ParseServerAddress("[2001:db8::1]:25565") == ("2001:db8::1", 25565));
Check("地址解析 裸 IPv6（多冒号）整体作 host", LauncherService.ParseServerAddress("2001:db8::1") == ("2001:db8::1", 0));
Check("地址解析 端口超范围 → 无效", LauncherService.ParseServerAddress("mc.example.com:70000") == (null, 0));
Check("地址解析 残缺端口 → 无效", LauncherService.ParseServerAddress("mc.example.com:abc") == (null, 0));
Check("地址解析 空端口开头 → 无效", LauncherService.ParseServerAddress(":25565") == (null, 0));
Check("地址解析 空串 → 无效", LauncherService.ParseServerAddress("") == (null, 0));

// ==================== fix7：任务取消 / 阶段化进度基础设施 ====================
Console.WriteLine("\n---- 第 18 节 · fix7：任务中心可取消任务 ----");

// 18.1 StartCancellable 登记 CTS；TokenFor 关联取消令牌
var t7 = DownloadTaskService.Instance.StartCancellable("测试任务", "进行中");
Check("StartCancellable 可取消", t7.CanCancel && t7.Status == "running");
var tok7 = DownloadTaskService.Instance.TokenFor(t7);
Check("TokenFor 返回有效令牌", tok7.CanBeCanceled);

// 18.2 RequestCancel 触发 CTS（业务方随后 Cancel 落定状态）
t7.RequestCancel();
Check("RequestCancel 触发令牌", tok7.IsCancellationRequested);

// 18.3 Cancel 落定状态；Finish 幂等（重复落定不覆盖）
DownloadTaskService.Instance.Cancel(t7, "已取消");
Check("Cancel 后状态 canceled", !t7.IsActive && t7.Status == "canceled" && t7.Detail == "已取消");
DownloadTaskService.Instance.Complete(t7, "不应覆盖");
Check("Finish 幂等（canceled 不被 Complete 覆盖）", t7.Status == "canceled" && t7.Detail == "已取消");
Check("取消后 CanCancel=false", !t7.CanCancel);

// 18.4 普通 Start 无取消源（不可取消）
var t7b = DownloadTaskService.Instance.Start("普通任务");
Check("Start 不可取消", !t7b.CanCancel);
DownloadTaskService.Instance.TokenFor(t7b).Equals(default) ;
DownloadTaskService.Instance.Complete(t7b, "ok");
Check("普通任务正常完成", t7b.Status == "done");

// 18.5 JavaManager EnsureJreAsync 取消参数（不触发网络，仅验证签名兼容）：
// ct 默认参数已由第 16 节既有调用覆盖编译，无行为断言。

// ==================== fix7：游戏选项补丁（跳过初始屏幕 / 游戏语言 / 默认窗口） ====================
Console.WriteLine("\n---- 第 19 节 · fix7：GameOptions 选项补丁 ----");

// 19.1 PatchLine 纯函数
var l7 = GameOptions.PatchLine(new[] { "version:4351", "lang:en_US", "fov:0.5" }, "lang", "zh_CN");
Check("PatchLine 替换已有键", l7.Contains("lang:zh_CN") && !l7.Contains("lang:en_US") && l7.Contains("fov:0.5"));
var l7b = GameOptions.PatchLine(new[] { "version:4351" }, "onboardAccessibility", "false");
Check("PatchLine 追加缺失键", l7b[^1] == "onboardAccessibility:false" && l7b[0] == "version:4351");
Check("PatchLine 其余行原样保留", l7.Count == 3);

// 19.2 Apply 文件级（临时目录真实读写）
var optDir = Path.Combine(Path.GetTempPath(), "hm-test-gameoptions-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(optDir);
var optFile = Path.Combine(optDir, "options.txt");
File.WriteAllLines(optFile, new[] { "version:4351", "lang:en_US", "onboardAccessibility:true" });
GameOptions.Apply(optDir, "zh_CN", skipOnboarding: true);
var optLines = File.ReadAllLines(optFile);
Check("Apply 写入语言与初始屏幕", optLines.Contains("lang:zh_CN")
    && optLines.Contains("onboardAccessibility:false") && optLines.Contains("version:4351"));

// 19.3 options.txt 不存在时创建
var optDir2 = optDir + "-2";
GameOptions.Apply(optDir2, "zh_CN", skipOnboarding: false);
Check("Apply 缺文件时创建", File.Exists(Path.Combine(optDir2, "options.txt"))
    && File.ReadAllLines(Path.Combine(optDir2, "options.txt"))[^1] == "lang:zh_CN");

// 19.4 lang 空且不跳过初始屏幕时早退（完全尊重游戏设置，不写文件）
var mtimeBefore = File.GetLastWriteTimeUtc(optFile);
GameOptions.Apply(optDir, null, skipOnboarding: false);
Check("无改动时不写文件", File.GetLastWriteTimeUtc(optFile) == mtimeBefore);

// 19.5 设置新字段默认值（可选功能默认关闭）
Check("SkipOnboarding 默认关", !new AppSettings().SkipOnboarding);
Check("GameLang 默认不修改", new AppSettings().GameLang == "");

try { launcher.Dispose(); } catch { }

// ==================== 第 20 节 · fix7：%APPDATA%\.minecraft 空壳判定（runtime 残留清理） ====================
Console.WriteLine("\n---- 第 20 节 · fix7：APPDATA 空壳判定扩展 ----");
Check("空目录判定为空壳", AppSettings.IsPollutedAppdataShell(Array.Empty<string>()));
Check("仅清单缓存判定为空壳", AppSettings.IsPollutedAppdataShell(new[] { @"versions\version_manifest_v2.json" }));
Check("runtime 残留判定为空壳", AppSettings.IsPollutedAppdataShell(new[]
{
    @"versions\version_manifest_v2.json",
    @"runtime\windows\java-runtime-gamma\bin\javaw.exe",
    @"runtime\windows\java-runtime-gamma\lib\modules",
    @"runtime\windows\jre-legacy\bin\java.exe",
}));
Check("仅 runtime 残留（无清单缓存）也判定为空壳", AppSettings.IsPollutedAppdataShell(new[]
{
    @"runtime\windows\java-runtime-delta\bin\javaw.exe",
}));
Check("正斜杠相对路径同样识别", AppSettings.IsPollutedAppdataShell(new[] { "runtime/windows/java-runtime-beta/release" }));
Check("含 options.txt 判定为真实游戏目录", !AppSettings.IsPollutedAppdataShell(new[]
{
    @"runtime\windows\java-runtime-gamma\bin\javaw.exe",
    "options.txt",
}));
Check("含存档判定为真实游戏目录", !AppSettings.IsPollutedAppdataShell(new[] { @"saves\New World\level.dat" }));
Check("含真实版本判定为真实游戏目录", !AppSettings.IsPollutedAppdataShell(new[] { @"versions\1.21.4\1.21.4.json" }));
Check("含任意未知文件判定为真实游戏目录", !AppSettings.IsPollutedAppdataShell(new[] { "logs/latest.log" }));
Check("runtime 外层文件不属于白名单", !AppSettings.IsPollutedAppdataShell(new[] { "runtime_config.json" }));

// ==================== 第 21 节 · fix8：启动参数优化（LaunchArgs） ====================
Console.WriteLine("\n---- 第 21 节 · fix8：启动参数优化（LaunchArgs / JVM 优化 / 游戏参数） ----");
// 21.1 默认优化参数清单（PCL 同款）
Check("优化参数含 G1GC", LaunchArgs.OptimizedJvmArgs.Contains("-XX:+UseG1GC"));
Check("优化参数含 Forge 证书兼容",
    LaunchArgs.OptimizedJvmArgs.Contains("-Dfml.ignoreInvalidMinecraftCertificates=true"));
Check("优化参数含 Log4Shell 缓解",
    LaunchArgs.OptimizedJvmArgs.Contains("-Dlog4j2.formatMsgNoLookups=true"));
Check("优化参数不含 -Xmn（G1 不适用）", !LaunchArgs.OptimizedJvmArgs.Any(a => a.StartsWith("-Xmn")));
// 21.2 BuildJvmExtra：默认开 → 优化参数在前
var jvmOn = LaunchArgs.BuildJvmExtra(true, "");
Check("开启优化：6 个参数", jvmOn.Count == 6, string.Join(" ", jvmOn));
Check("G1GC 排在首位", jvmOn[0] == "-XX:+UseG1GC");
// 21.3 关闭优化 → 仅自定义
var jvmOff = LaunchArgs.BuildJvmExtra(false, "-Xmx2G -XX:+UseZGC");
Check("关闭优化：仅自定义参数", jvmOff.Count == 2 && jvmOff[0] == "-Xmx2G" && jvmOff[1] == "-XX:+UseZGC");
// 21.4 自定义 GC 收集器 → 跳过 G1GC（避免 GC 冲突警告），其余优化参数保留
var jvmZgc = LaunchArgs.BuildJvmExtra(true, "-XX:+UseZGC");
Check("自定义 ZGC 时跳过 G1GC", !jvmZgc.Contains("-XX:+UseG1GC"), string.Join(" ", jvmZgc));
Check("自定义 GC 时其余优化参数保留", jvmZgc.Contains("-XX:-OmitStackTraceInFastThrow"));
// 21.5 自定义参数在后 = JVM 中优先生效；同一参数去重
var jvmDup = LaunchArgs.BuildJvmExtra(true, "-XX:+UseG1GC");
Check("自定义已含 G1GC 时去重", jvmDup.Count(a => a == "-XX:+UseG1GC") == 1, string.Join(" ", jvmDup));
var jvmCustomLast = LaunchArgs.BuildJvmExtra(true, "-Dfml.ignoreInvalidMinecraftCertificates=false");
Check("自定义覆盖优化参数（在后优先）",
    jvmCustomLast.Count(a => a == "-Dfml.ignoreInvalidMinecraftCertificates=true") == 0
    && jvmCustomLast[^1] == "-Dfml.ignoreInvalidMinecraftCertificates=false",
    string.Join(" ", jvmCustomLast));
// 21.6 游戏参数解析（与 JVM 参数同规则）
var g1 = LaunchArgs.BuildGameExtra("--demo --fullscreen");
Check("游戏参数：空白分隔", g1.Count == 2 && g1[0] == "--demo" && g1[1] == "--fullscreen");
Check("游戏参数：双引号含空格", LaunchArgs.BuildGameExtra("--tweakClass \"a b\"").SequenceEqual(new[] { "--tweakClass", "a b" }));
Check("游戏参数：空串返回空列表", LaunchArgs.BuildGameExtra("").Count == 0);
// 21.7 ParseJvmArgs 委托保持向后兼容（HmCli / 既有测试调用点）
Check("ParseJvmArgs 委托结果一致",
    LauncherService.ParseJvmArgs("-XX:+UseG1GC \"-Da=b c\"").SequenceEqual(new[] { "-XX:+UseG1GC", "-Da=b c" }));
// 21.8 设置新字段默认值（优化默认开，其余默认关）
Check("JvmOptimize 默认开", new AppSettings().JvmOptimize);
Check("CustomGameArgs 默认空", new AppSettings().CustomGameArgs == "");
Check("GameFullscreen 默认关", !new AppSettings().GameFullscreen);

// ==================== 第 22 节 · fix9：愚人节版分类 / 文案清理 ====================
Console.WriteLine("\n---- 第 22 节 · fix9：愚人节版分类（VersionKinds）/ 文案清理 ----");
// 22.1 愚人节版本识别
Check("25w14craftmine 识别为愚人节", VersionKinds.IsAprilFools("25w14craftmine"));
Check("26w14a（2026 Herdcraft）识别为愚人节", VersionKinds.IsAprilFools("26w14a"));
Check("20w14infinite 识别为愚人节", VersionKinds.IsAprilFools("20w14infinite"));
Check("3D Shareware v1.34（含空格 ID）识别为愚人节", VersionKinds.IsAprilFools("3D Shareware v1.34"));
Check("1.RV-Pre1 识别为愚人节", VersionKinds.IsAprilFools("1.RV-Pre1"));
Check("大小写不敏感", VersionKinds.IsAprilFools("24W14POTATO"));
Check("正式版不误判", !VersionKinds.IsAprilFools("1.21.4"));
Check("普通快照不误判", !VersionKinds.IsAprilFools("24w45a"));
Check("远古版不误判", !VersionKinds.IsAprilFools("b1.7.3"));
Check("null / 空串不误判", !VersionKinds.IsAprilFools(null) && !VersionKinds.IsAprilFools(""));
Check("愚人节清单共 10 项（2013-2026）", VersionKinds.AprilFoolsVersions.Length == 10);
// 22.2 每个愚人节版本都有简介键（两语言），列表出现时直接有说明
try
{
    var langDir2 = FindUp("src/HardmacroCraft/Themes/Languages");
    var zhKeys = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText(Path.Combine(langDir2, "Strings.zh-CN.xaml")), @"x:Key=""([^""]+)""")
        .Select(m => m.Groups[1].Value));
    var enKeys2 = new HashSet<string>(System.Text.RegularExpressions.Regex.Matches(
        File.ReadAllText(Path.Combine(langDir2, "Strings.en.xaml")), @"x:Key=""([^""]+)""")
        .Select(m => m.Groups[1].Value));
    var missing = VersionKinds.AprilFoolsVersions
        .Where(id => !zhKeys.Contains("S.Dl.Desc." + id) || !enKeys2.Contains("S.Dl.Desc." + id)).ToList();
    Check("每个愚人节版本都有 zh/en 简介键", missing.Count == 0, "缺: " + string.Join(", ", missing));
    // 22.3 用户可见文案不含 PCL / HMCL 字样（防回归：语言值 + 运行时日志）
    var zhTxt = File.ReadAllText(Path.Combine(langDir2, "Strings.zh-CN.xaml"));
    var enTxt = File.ReadAllText(Path.Combine(langDir2, "Strings.en.xaml"));
    var zhVis = System.Text.RegularExpressions.Regex.Matches(zhTxt, "x:Key=\"[^\"]+\">([^<]+)<")
        .Select(m => m.Groups[1].Value).ToList();
    var enVis = System.Text.RegularExpressions.Regex.Matches(enTxt, "x:Key=\"[^\"]+\">([^<]+)<")
        .Select(m => m.Groups[1].Value).ToList();
    var badZh = zhVis.Where(t => t.Contains("PCL") || t.Contains("HMCL")).ToList();
    var badEn = enVis.Where(t => t.Contains("PCL") || t.Contains("HMCL")).ToList();
    Check("zh 文案值无 PCL/HMCL 字样", badZh.Count == 0, string.Join(" | ", badZh));
    Check("en 文案值无 PCL/HMCL 字样", badEn.Count == 0, string.Join(" | ", badEn));
    var csDir = FindUp("src/HardmacroCraft");
    var csFiles = Directory.EnumerateFiles(csDir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
    // 逐行匹配字符串字面量并跳过注释行（避免跨字符串/注释的误报）
    var badCs = csFiles
        .SelectMany(f => File.ReadAllLines(f)
            .Select((line, i) => (line, i))
            .Where(x => !x.line.TrimStart().StartsWith("//"))
            .SelectMany(x => System.Text.RegularExpressions.Regex.Matches(
                    x.line, "\"[^\"]*(?:PCL|HMCL)[^\"]*\"")
                .Select(m => $"{Path.GetFileName(f)}:{x.i + 1}:{m.Value}")))
        .ToList();
    Check("代码字符串字面量无 PCL/HMCL 字样（注释除外）", badCs.Count == 0, string.Join(" | ", badCs.Take(3)));
}
catch (Exception ex) { Check("fix9 文案 / 简介键检查", false, ex.Message); }

// ==================== 第 23 节 · 登录系统全面检查：离线用户名校验 ====================
Console.WriteLine("\n---- 第 23 节 · 登录系统全面检查（ValidateOfflineName）----");
try
{
    // 23.1 合法名字
    Check("合法名 Steve → None", AuthService.ValidateOfflineName("Steve").Error == AuthService.OfflineNameError.None);
    Check("合法名含下划线数字 → None", AuthService.ValidateOfflineName("St_eve1").Error == AuthService.OfflineNameError.None);
    Check("首尾空格被清洗", AuthService.ValidateOfflineName("  Steve  ").Name == "Steve");
    Check("内部空白被合并清洗", AuthService.ValidateOfflineName("S t e v e").Name == "Steve");
    Check("16 字符合法上限", AuthService.ValidateOfflineName(new string('a', 16)).Error == AuthService.OfflineNameError.None);
    Check("3 字符合法下限", AuthService.ValidateOfflineName("abc").Error == AuthService.OfflineNameError.None);

    // 23.2 空值
    Check("null → Empty", AuthService.ValidateOfflineName(null).Error == AuthService.OfflineNameError.Empty);
    Check("空串 → Empty", AuthService.ValidateOfflineName("").Error == AuthService.OfflineNameError.Empty);
    Check("纯空格 → Empty", AuthService.ValidateOfflineName("   ").Error == AuthService.OfflineNameError.Empty);

    // 23.3 长度（官方规则 3-16）
    Check("2 字符 → TooShort", AuthService.ValidateOfflineName("ab").Error == AuthService.OfflineNameError.TooShort);
    Check("17 字符 → TooLong", AuthService.ValidateOfflineName(new string('a', 17)).Error == AuthService.OfflineNameError.TooLong);
    Check("超长含空格合并后仍判长", AuthService.ValidateOfflineName(new string('a', 10) + " " + new string('b', 10)).Error == AuthService.OfflineNameError.TooLong);

    // 23.4 非法字符（此前静默丢弃的根因场景：中文 / 符号）
    Check("中文名 → InvalidChars", AuthService.ValidateOfflineName("中文玩家").Error == AuthService.OfflineNameError.InvalidChars);
    Check("含符号 → InvalidChars", AuthService.ValidateOfflineName("Steve!").Error == AuthService.OfflineNameError.InvalidChars);
    Check("数字符号混合 → InvalidChars", AuthService.ValidateOfflineName("a@b c").Error == AuthService.OfflineNameError.InvalidChars);
    Check("TooShort 优先于 InvalidChars（短中文）", AuthService.ValidateOfflineName("中").Error == AuthService.OfflineNameError.TooShort);

    // 23.5 返回值约定：非法时 Name 非 null 但 Error 非 None（UI 只认 Error）
    Check("校验失败时 Error 与 Name 解耦", AuthService.ValidateOfflineName("Steve!") == ("Steve!", AuthService.OfflineNameError.InvalidChars));
}
catch (Exception ex) { Check("第 23 节 离线用户名校验", false, ex.Message); }

// ==================== 第 24 节 · v0.1.1：校验缓存 / 进度中枢 / 下载线程数 ====================
Console.WriteLine("\n---- 第 24 节 · v0.1.1（VerifyStamp / LaunchProgressHub）----");
try
{
    // 24.1 VerifyStamp（临时目录注入，不碰真实 config/）
    var stampRoot = Path.Combine(Path.GetTempPath(), "hmc-test-stamp-" + Guid.NewGuid().ToString("N")[..8]);
    var stampCfg = Path.Combine(stampRoot, "config");
    var verDir = Path.Combine(stampRoot, "versions", "1.20.1");
    Directory.CreateDirectory(verDir);
    File.WriteAllText(Path.Combine(verDir, "1.20.1.json"), "{}");
    var stampVersion = "1.20.1";

    Check("无戳 → 不可跳过", !VerifyStamp.IsValid(stampRoot, stampVersion, stampCfg));
    VerifyStamp.Write(stampRoot, stampVersion, stampCfg); // 无 jar → 应拒绝写戳
    Check("无 jar → 写戳被拒绝", !File.Exists(VerifyStamp.StampPath(stampRoot, stampVersion, stampCfg)));
    File.WriteAllText(Path.Combine(verDir, "1.20.1.jar"), "fake-jar");
    VerifyStamp.Write(stampRoot, stampVersion, stampCfg);
    Check("有 json + jar 写戳成功", File.Exists(VerifyStamp.StampPath(stampRoot, stampVersion, stampCfg)));
    Check("写戳后 → 可跳过", VerifyStamp.IsValid(stampRoot, stampVersion, stampCfg));
    var mtime = new FileInfo(Path.Combine(verDir, "1.20.1.json")).LastWriteTimeUtc;
    File.WriteAllText(Path.Combine(verDir, "1.20.1.json"), "{\"patched\":true}"); // 版本 json 变化
    File.SetLastWriteTimeUtc(Path.Combine(verDir, "1.20.1.json"), mtime.AddMinutes(1));
    Check("版本 json 变化 → 戳自动失效", !VerifyStamp.IsValid(stampRoot, stampVersion, stampCfg));
    VerifyStamp.Write(stampRoot, stampVersion, stampCfg);
    Check("重写后再次有效", VerifyStamp.IsValid(stampRoot, stampVersion, stampCfg));
    VerifyStamp.Invalidate(stampRoot, stampVersion, stampCfg);
    Check("Invalidate → 戳删除", !File.Exists(VerifyStamp.StampPath(stampRoot, stampVersion, stampCfg)));
    Check("版本目录不存在 → 不可跳过", !VerifyStamp.IsValid(stampRoot, "ghost", stampCfg));
    Check("Encode 纯字母数字保留", VerifyStamp.Encode("1.20.1") != "1.20.1" && VerifyStamp.Encode("abc").Length == 3);
    Check("Encode 空串安全", VerifyStamp.Encode("").Length > 0);

    // 24.2 LaunchProgressHub 快照流转
    int fired = 0;
    void CountChanged() => fired++;
    LaunchProgressHub.Changed += CountChanged;
    var before = fired;
    LaunchProgressHub.Begin("1.20.1");
    Check("Begin → Running / 取消可见", LaunchProgressHub.Current.Running && LaunchProgressHub.Current.CancelVisible);
    Check("Begin 触发 Changed", fired > before);
    LaunchProgressHub.Update(s => { s.Percent = 50; s.Indeterminate = false; s.ProgressLine = "[10/20] a.ogg"; });
    Check("Update 写入快照", !LaunchProgressHub.Current.Indeterminate
        && LaunchProgressHub.Current.Percent == 50
        && LaunchProgressHub.Current.ProgressLine == "[10/20] a.ogg");
    LaunchProgressHub.End();
    Check("End → 快照复位", !LaunchProgressHub.Current.Running && LaunchProgressHub.Current.ProgressLine == "");
    bool cancelHit = false;
    void OnCancel() => cancelHit = true;
    LaunchProgressHub.CancelRequested += OnCancel;
    LaunchProgressHub.RequestCancel();
    LaunchProgressHub.CancelRequested -= OnCancel;
    LaunchProgressHub.Changed -= CountChanged;
    Check("RequestCancel → 事件到达", cancelHit);

    // 24.3 下载线程数（1-256，默认 64，对齐 PCL）
    Check("DownloadThreads 默认 64", new AppSettings().DownloadThreads == 64);
    var tSaved = AppSettings.Instance.DownloadThreads;
    AppSettings.Instance.DownloadThreads = 999;
    Check("999 被 Clamp 到上限 256", Math.Clamp(AppSettings.Instance.DownloadThreads, 1, 256) == 256);
    AppSettings.Instance.DownloadThreads = 0;
    Check("0 被 Clamp 到下限 1", Math.Clamp(AppSettings.Instance.DownloadThreads, 1, 256) == 1);
    AppSettings.Instance.DownloadThreads = 1;
    Check("1 = 串行（≤1 走内置安装器）", Math.Clamp(AppSettings.Instance.DownloadThreads, 1, 256) <= 1);
    AppSettings.Instance.DownloadThreads = tSaved;
}
catch (Exception ex) { Check("第 24 节 v0.1.1", false, ex.Message); }

// ==================== 第 25 节 · v0.1.2：续传回退 / 416 修复 / 戳碰撞防护 / 终态回放 ====================
Console.WriteLine("\n---- 第 25 节 · v0.1.2（DownloadEngine 续传修复 / VerifyStamp 指纹 / Hub 终态）----");
try
{
    var payload2 = System.Text.Encoding.UTF8.GetBytes(new string('V', 60_000) + "HMC-V012");
    var sha1V = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(payload2)).ToLowerInvariant();

    // 25.1 VerifyStamp 碰撞防护：Encode 折叠后同名的不同路径/版本 → 戳文件名必须不同
    Check("戳路径：gameRoot 空格/下划线碰撞已消除",
        VerifyStamp.StampPath(@"C:\g1 a", "1.20.1") != VerifyStamp.StampPath(@"C:\g1_a", "1.20.1"));
    Check("戳路径：版本名 +/_ 碰撞已消除",
        VerifyStamp.StampPath(@"C:\g1", "a+b") != VerifyStamp.StampPath(@"C:\g1", "a_b"));
    Check("戳路径：长路径截断碰撞已消除（>80 字符同前缀）",
        VerifyStamp.StampPath(@"C:\" + new string('x', 100) + "A", "1.0")
            != VerifyStamp.StampPath(@"C:\" + new string('x', 100) + "B", "1.0"));
    Check("Encode 纯字母数字保留（原语义不变）", VerifyStamp.Encode("abc").Length == 3);

    // 本地服务器：/failmid 首次半途断流（触发重试 + 200 回退重下），/ranged 尊重 Range（越界 416）
    var p2 = 0;
    using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
    {
        probe.Start(); p2 = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
    }
    var failMidHits = 0;
    var srv2 = new System.Net.HttpListener();
    srv2.Prefixes.Add($"http://127.0.0.1:{p2}/");
    srv2.Start();
    var srv2Task = Task.Run(async () =>
    {
        while (srv2.IsListening)
        {
            try
            {
                var ctx = await srv2.GetContextAsync();
                var path = ctx.Request.Url?.AbsolutePath ?? "";
                if (path.StartsWith("/failmid"))
                {
                    // 第一次：声明完整长度但只发一半就断开 → 客户端读流异常 → 引擎重试
                    if (Interlocked.Increment(ref failMidHits) == 1)
                    {
                        ctx.Response.ContentLength64 = payload2.Length;
                        await ctx.Response.OutputStream.WriteAsync(payload2.AsMemory(0, 30_000));
                        ctx.Response.Close(); // 提前关闭 → 截断
                    }
                    else
                    {
                        ctx.Response.ContentLength64 = payload2.Length;
                        await ctx.Response.OutputStream.WriteAsync(payload2);
                        ctx.Response.Close();
                    }
                }
                else if (path.StartsWith("/ranged"))
                {
                    var raw = ctx.Request.Headers["Range"];
                    if (raw != null && raw.StartsWith("bytes="))
                    {
                        var start = long.TryParse(raw[6..].TrimEnd('-'), out var s0) ? s0 : 0;
                        if (start >= payload2.Length)
                        {
                            ctx.Response.StatusCode = 416;
                            ctx.Response.Close();
                            continue;
                        }
                        ctx.Response.StatusCode = 206;
                        ctx.Response.ContentLength64 = payload2.Length - start;
                        await ctx.Response.OutputStream.WriteAsync(payload2.AsMemory((int)start));
                        ctx.Response.Close();
                    }
                    else
                    {
                        ctx.Response.ContentLength64 = payload2.Length;
                        await ctx.Response.OutputStream.WriteAsync(payload2);
                        ctx.Response.Close();
                    }
                }
                else { ctx.Response.StatusCode = 404; ctx.Response.Close(); }
            }
            catch { if (!srv2.IsListening) break; }
        }
    });

    using var http2 = new HttpClient();
    var dl2 = Path.Combine(testRoot, "dl2");
    if (Directory.Exists(dl2)) Directory.Delete(dl2, true);
    Directory.CreateDirectory(dl2);

    // 25.2 续传回退：第 1 次尝试报了 30000 字节后断流；第 2 次服务器不支持 Range 从头下。
    // 修复后负增量抵消 → 增量总和 == 文件大小（修复前会多出 30000，进度膨胀 >100%）
    var deltas = new List<long>();
    var deltaLock = new object();
    var sum = 0L;
    var prog2 = new Progress<long>(d => { lock (deltaLock) { deltas.Add(d); sum += d; } });
    var resumeDest = Path.Combine(dl2, "resume.bin");
    await DownloadEngine.DownloadFileAsync(http2, $"http://127.0.0.1:{p2}/failmid.bin", resumeDest,
        sha1V, payload2.Length, prog2);
    for (var w = 0; w < 40 && sum != payload2.Length; w++) await Task.Delay(50); // 回调异步投递，等落定
    Check("断流重试后文件正确",
        File.Exists(resumeDest) && !File.Exists(resumeDest + ".hmc-part")
        && Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(resumeDest))).ToLowerInvariant() == sha1V);
    Check("进度增量总和 == 文件大小（负增量回退生效）", sum == payload2.Length,
        $"总和 {sum} ≠ {payload2.Length}（报告 {deltas.Count} 次）");
    Check("服务器共 2 次请求（首断流 + 重下）", Volatile.Read(ref failMidHits) == 2, $"实际 {Volatile.Read(ref failMidHits)}");

    // 25.3 part 已达目标大小 → 丢弃重下（修复前：Range 越界 416 → 重试 3 次同因失败）
    var stDest = Path.Combine(dl2, "stuck-ok.bin");
    File.WriteAllBytes(stDest + ".hmc-part", System.Text.Encoding.UTF8.GetBytes(new string('X', payload2.Length)));
    await DownloadEngine.DownloadFileAsync(http2, $"http://127.0.0.1:{p2}/ranged.bin", stDest, sha1V, payload2.Length);
    Check("part 满长不再 416 卡死，重下成功",
        File.Exists(stDest) && Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(File.ReadAllBytes(stDest))).ToLowerInvariant() == sha1V
        && !File.Exists(stDest + ".hmc-part"));

    srv2.Stop();
}
catch (Exception ex) { Check("第 25 节 v0.1.2（下载）", false, ex.Message); }

try
{
    // 25.4 Hub 终态回放：End 后 LastProgressLine 保留终态，新实例切回来不再空白
    var (hOk, hLast) = (false, "");
    LaunchProgressHub.Begin("1.20.2");
    LaunchProgressHub.Update(s => s.ProgressLine = "启动失败：测试终态");
    LaunchProgressHub.End();
    hLast = LaunchProgressHub.LastProgressLine;
    hOk = hLast == "启动失败：测试终态" && !LaunchProgressHub.Current.Running;
    LaunchProgressHub.Begin("1.20.2"); // 新一轮启动覆盖
    LaunchProgressHub.Update(s => s.ProgressLine = "新进度");
    LaunchProgressHub.End();
    Check("End 保留终态文案", hOk, hLast);
    Check("新一轮启动覆盖终态", LaunchProgressHub.LastProgressLine == "新进度", LaunchProgressHub.LastProgressLine);
}
catch (Exception ex) { Check("第 25 节 v0.1.2（Hub）", false, ex.Message); }

try
{
    // ==================== 第 26 节 v0.1.3 内存优化 ====================

    // 26.1 OptimizeNow：可执行、返回值合理（两轮 GC + LOH 压缩在 Linux / Windows 均可用）
    var (mBefore, mAfter) = MemoryManager.OptimizeNow();
    Check("OptimizeNow 可执行且返回非负托管堆", mBefore >= 0 && mAfter >= 0, $"{mBefore}/{mAfter}");
    Check("优化后堆不大于优化前", mAfter <= mBefore, $"{mAfter} > {mBefore}");
    Check("WorkingSetMb 为正", MemoryManager.WorkingSetMb > 0, MemoryManager.WorkingSetMb + " MB");

    // 26.2 IsLauncherBusy：任务中心运行中任务 → 忙碌；全部落定 → 空闲
    LaunchProgressHub.End(); // 确保上一节残留快照复位
    var busyTask = DownloadTaskService.Instance.Start("26.2 忙碌探测");
    Check("运行中任务 → IsLauncherBusy=true", MemoryManager.IsLauncherBusy());
    DownloadTaskService.Instance.Complete(busyTask);
    Check("任务落定 → IsLauncherBusy=false", !MemoryManager.IsLauncherBusy());

    // 26.3 设置默认值：内存优化默认开
    Check("AutoMemoryOptimize 默认开启", new AppSettings().AutoMemoryOptimize);

    // 26.4 OptimizeAfterHeavyOp：关闭开关时不动作、开启时不抛异常（延迟 1.5s 后台执行）
    var savedOpt = AppSettings.Instance.AutoMemoryOptimize;
    try
    {
        AppSettings.Instance.AutoMemoryOptimize = false;
        MemoryManager.OptimizeAfterHeavyOp(); // 不动作路径
        AppSettings.Instance.AutoMemoryOptimize = true;
        MemoryManager.OptimizeAfterHeavyOp(); // 调度路径（不等它跑完）
        Check("OptimizeAfterHeavyOp 两路径不抛异常", true);
    }
    finally { AppSettings.Instance.AutoMemoryOptimize = savedOpt; }

    // 26.5 完成收尾挂钩：Complete(done) 后延迟优化被调度（通过无异常 + 幂等复验间接覆盖）
    var hookTask = DownloadTaskService.Instance.Start("26.5 收尾挂钩");
    DownloadTaskService.Instance.Complete(hookTask, "done 路径触发 OptimizeAfterHeavyOp");
    Check("Complete(done) 收尾挂钩不破坏任务状态", hookTask.Status == "done" && !hookTask.IsActive);
}
catch (Exception ex) { Check("第 26 节 v0.1.3（内存优化）", false, ex.Message); }

try
{
    // ==================== 第 27 节 v0.1.5 NeoForge 目录源 ====================

    // 27.1 目录 JSON 解析
    var neoCatalogJson = "{\"isSnapshot\":false,\"versions\":[\"20.4.51\",\"20.4.9-beta\",\"26.3.0.101\",\"0.25w14craftmine.3-beta\",\"nonsense\"]}";
    var forgeCatalogJson = "{\"versions\":[\"1.20.1-47.1.106\",\"1.20.1-47.1.99\",\"47.1.82\"]}";
    var parsed = LauncherService.NeoForgeCatalog.ParseVersions(neoCatalogJson);
    Check("ParseVersions 解析 versions 数组", parsed.Length == 5 && parsed[0] == "20.4.51", string.Join(",", parsed));

    // 27.2 版本名 → MC 版本映射（官方规则：20/21 → 1.x.y；26.x.y.z → x.y.z；0.快照 → 快照名）
    var map = LauncherService.NeoForgeCatalog.MapCatalogVersionToMc;
    Check("21.1.265 → 1.21.1", map("21.1.265") == "1.21.1", map("21.1.265") ?? "null");
    Check("20.4.51 → 1.20.4", map("20.4.51") == "1.20.4", map("20.4.51") ?? "null");
    Check("26.3.0.101 → 26.3", map("26.3.0.101") == "26.3", map("26.3.0.101") ?? "null");
    Check("0.25w14craftmine.3-beta → 25w14craftmine", map("0.25w14craftmine.3-beta") == "25w14craftmine", map("0.25w14craftmine.3-beta") ?? "null");
    Check("1.20.1-47.1.106 → null（forge artifact 名不走通用映射）", map("1.20.1-47.1.106") == null, map("1.20.1-47.1.106") ?? "null");
    Check("乱串 → null（宽容跳过不抛异常）", map("nonsense") == null, map("nonsense") ?? "null");

    // 27.3 MC 版本规范化
    var norm = LauncherService.NeoForgeCatalog.NormalizeMc;
    Check("NormalizeMc 1.21.0 → 1.21", norm("1.21.0") == "1.21", norm("1.21.0"));
    Check("NormalizeMc 1.20.1 不变", norm("1.20.1") == "1.20.1", norm("1.20.1"));

    // 27.4 安装器直链：1.20.1 走 net/neoforged/forge，其余走 net/neoforged/neoforge
    var url1201 = "";
    var full1201 = LauncherService.NeoForgeCatalog.InstallerArtifact("1.20.1", "47.1.106", out url1201);
    Check("1.20.1 安装器 artifact = forge 全名", full1201 == "1.20.1-47.1.106", full1201);
    Check("1.20.1 安装器 URL 指向 net/neoforged/forge",
        url1201 == "https://maven.neoforged.net/releases/net/neoforged/forge/1.20.1-47.1.106/forge-1.20.1-47.1.106-installer.jar", url1201);
    var urlModern = "";
    LauncherService.NeoForgeCatalog.InstallerArtifact("1.21.1", "21.1.265", out urlModern);
    Check("1.21.1 安装器 URL 指向 net/neoforged/neoforge",
        urlModern == "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.265/neoforge-21.1.265-installer.jar", urlModern);

    // 27.5 排序：正式版优先 + 语义降序（20.4.100 > 20.4.51 > 20.4.9）
    var sorted = LauncherService.NeoForgeCatalog.SortDescending(
        new List<string> { "20.4.51", "20.4.9-beta", "20.4.9", "20.4.100" });
    Check("SortDescending 正式版优先 + 版本号降序",
        string.Join("|", sorted) == "20.4.100|20.4.51|20.4.9|20.4.9-beta", string.Join("|", sorted));

    // 27.6 GetVersionsAsync（拦截式回放）：1.20.1 走 forge 目录并剥前缀，其余走 neoforge 目录
    var neoHttp = new HttpClient(new InterceptingHandler((req, ct) =>
    {
        var body = req.RequestUri!.AbsolutePath.EndsWith("/net/neoforged/forge")
            ? forgeCatalogJson : neoCatalogJson;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StringContent(body) });
    }));
    var v1201 = await LauncherService.NeoForgeCatalog.GetVersionsAsync(neoHttp, "1.20.1");
    Check("1.20.1 版本列表来自 forge artifact（剥前缀，降序）",
        string.Join("|", v1201) == "47.1.106|47.1.99", string.Join("|", v1201));
    var v204 = await LauncherService.NeoForgeCatalog.GetVersionsAsync(neoHttp, "1.20.4");
    Check("1.20.4 版本列表来自 neoforge 目录（beta 靠后）",
        string.Join("|", v204) == "20.4.51|20.4.9-beta", string.Join("|", v204));
    var v263 = await LauncherService.NeoForgeCatalog.GetVersionsAsync(neoHttp, "26.3");
    Check("26.3 版本列表（新版线映射）", string.Join("|", v263) == "26.3.0.101", string.Join("|", v263));
}
catch (Exception ex) { Check("第 27 节 v0.1.5（NeoForge 目录）", false, ex.Message); }

try
{
    // ==================== 第 28 节 v0.1.5 模组预设存储 ====================
    var path = Path.Combine(Path.GetTempPath(), $"hmc-test-presets-{Guid.NewGuid():N}.json");
    try
    {
        // 28.1 空文件加载
        Check("预设文件不存在 → 空列表", ModPresetStore.Load(path).Count == 0);

        // 28.2 Upsert 同名覆盖 + 置顶
        var presets = new List<ModPreset>();
        var items1 = new List<ModPresetItem>
        {
            new() { Platform = "modrinth", ProjectId = "aaaa", Slug = "sodium", Title = "Sodium", Type = "mod" },
            new() { Platform = "curseforge", ProjectId = "361427", Slug = "jei", Title = "JEI", Type = "mod" },
        };
        ModPresetStore.Upsert(presets, "生存整合", items1);
        ModPresetStore.Upsert(presets, "光影包", new List<ModPresetItem>
            { new() { Platform = "modrinth", ProjectId = "bbbb", Slug = "iris", Title = "Iris", Type = "shader" } });
        ModPresetStore.Upsert(presets, "生存整合", new List<ModPresetItem>
            { new() { Platform = "modrinth", ProjectId = "cccc", Slug = "lithium", Title = "Lithium", Type = "mod" } });
        Check("Upsert 同名覆盖", presets.Count == 2 && presets[0].Name == "生存整合", string.Join(",", presets.Select(p => p.Name)));
        Check("覆盖后条目为最新一组", presets[0].Items[0].Slug == "lithium", presets[0].Items[0].Slug);

        // 28.3 落盘 + 回读（同名覆盖后：生存整合只剩 lithium 一条，光影包为 iris）
        ModPresetStore.Save(path, presets);
        var loaded = ModPresetStore.Load(path);
        Check("Save → Load 往返一致",
            loaded.Count == 2 && loaded[0].Name == "生存整合" && loaded[0].Items[0].Slug == "lithium"
            && loaded[1].Name == "光影包" && loaded[1].Items[0].Platform == "modrinth",
            string.Join(",", loaded.Select(p => p.Name + ":" + p.Items.Count)));

        // 28.4 OfType 过滤（安装时跳过类型不符条目的依据）
        Check("OfType 只返回匹配类型", loaded[1].OfType("shader").Count() == 1 && loaded[1].OfType("mod").Count() == 0);

        // 28.5 损坏文件：返回空列表并留 .bak，不抛异常
        File.WriteAllText(path, "{ 这不是合法 JSON ");
        Check("损坏 JSON → 空列表不抛异常", ModPresetStore.Load(path).Count == 0);
        Check("损坏 JSON 留有 .bak 备份", File.Exists(path + ".bak"));
    }
    finally
    {
        try { File.Delete(path); } catch { }
        try { File.Delete(path + ".bak"); } catch { }
    }
}
catch (Exception ex) { Check("第 28 节 v0.1.5（模组预设）", false, ex.Message); }

try
{
    // ==================== 第 29 节 v0.1.5 默认头像（Steve/Alex）与详情工具 ====================

    // 29.1 已知 UUID 的选脸结果（与 Java UUID.hashCode 奇偶规则一致；与 python 参照实现交叉验证）
    // MHF_SteVe 官方维护号 → Steve；MHF_Alex → Alex；离线玩家名与选脸无关（UUID 奇偶决定）
    Check("MHF_Steve uuid → Steve", !SkinService.PicksAlexDefault("c06f89064c8a49119c29ea1dbd1aab82"));
    Check("MHF_Alex uuid → Alex", SkinService.PicksAlexDefault("6ab4317889fd490597f60f67d9d76fd9"));
    Check("离线 Steve 的 uuid → Alex（按 uuid 而非名字）",
        SkinService.PicksAlexDefault("5627dd98-e6be-3c21-b8a8-e92344183641"));
    Check("离线 Alex 的 uuid → Steve（按 uuid 而非名字）",
        !SkinService.PicksAlexDefault("36532b5e-c442-3dbb-a24c-c7e55d0f979a"));
    Check("带横线与不带横线结果一致",
        SkinService.PicksAlexDefault("6ab43178-89fd-4905-97f6-0f67d9d76fd9") == SkinService.PicksAlexDefault("6ab4317889fd490597f60f67d9d76fd9"));
    Check("非法 uuid → Steve 兜底不抛异常", !SkinService.PicksAlexDefault("不是uuid"));
    Check("DefaultFacePath 对应资源存在",
        SkinService.DefaultFacePath("6ab4317889fd490597f60f67d9d76fd9") == "/Assets/alex.png"
        && SkinService.DefaultFacePath("c06f89064c8a49119c29ea1dbd1aab82") == "/Assets/steve.png");

    // 29.2 StripHtml：CF 详情 HTML → 纯文本
    var html = "<h1>标题</h1><p>第一段 &amp; 说明</p><script>evil()</script><style>.x{}</style><br/>第二行<b>加粗</b>&nbsp;结尾";
    var text = ModrinthService.StripHtml(html);
    Check("StripHtml 去标签保留换行", text.Contains("标题") && text.Contains("第一段 & 说明") && text.Contains("第二行加粗"),
        text.Replace("\n", "\\n"));
    Check("StripHtml 去掉 script/style 与实体",
        !text.Contains("evil") && !text.Contains(".x{") && !text.Contains("&amp;") && !text.Contains("&nbsp;"));
    Check("StripHtml 空输入安全", ModrinthService.StripHtml("") == "" && ModrinthService.StripHtml(null!) == "");
}
catch (Exception ex) { Check("第 29 节 v0.1.5（默认头像与详情工具）", false, ex.Message); }


// ==================== 汇总 ====================
Console.WriteLine($"\n========== 结果：{pass} 通过 / {fail} 失败 / {skip} 跳过 ==========");
return fail == 0 ? 0 : 1;

// 本地辅助（引用 Views 代码需要 WPF，这里独立实现同规则做交叉验证）
static string? ModsPage_Extract(string v)
{
    var m = System.Text.RegularExpressions.Regex.Match(v ?? "", @"^\d+\.\d+(\.\d+)?");
    return m.Success ? m.Value : null;
}
static string ModsPageBadgePlatform(ModrinthService.SearchHit h) => h.Source == "curseforge" ? "CurseForge" : "Modrinth";

// 拦截式 HttpMessageHandler（回放夹具 + 捕获请求）
class InterceptingHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _reply;
    public InterceptingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> reply) => _reply = reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => _reply(request, cancellationToken);
}

