using System.Diagnostics;
using HardmacroCraft.Services;

namespace HmCli;

/// <summary>
/// HmCli —— HmCore 无头验证工具（手机端 M1）。
/// 在 Linux / Termux 等无 WPF 环境直接驱动 HmCore 核心服务，
/// 验证共享内核的跨平台可用性。用法见 PrintHelp()。
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        PrintBanner();
        if (args.Length == 0) { PrintHelp(); return 0; }
        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "selftest" => SelfTest(),
                "offline-uuid" => OfflineUuid(args.Length > 1 ? args[1] : "Steve"),
                "versions" => Versions(args.Length > 1 ? args[1] : null),
                "meta" => await MetaAsync(),
                "help" or "--help" or "-h" => Help(),
                _ => Help(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void PrintBanner()
    {
        var v = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";
        Console.WriteLine($"HmCli {v} —— Hardmacro Craft Launcher 共享核心（HmCore）无头验证工具");
    }

    private static int Help() { PrintHelp(); return 0; }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            用法:
              selftest                 纯逻辑冒烟（便携配置 / 离线 UUID / CDN 规则 / 兼容文件挑选）
              offline-uuid <name>      计算离线玩家 UUID（UUID v3, OfflinePlayer:name）
              versions [root]          列出游戏根目录下已安装版本（默认读 config 游戏目录）
              meta                     拉取 Minecraft 版本清单（BMCLAPI 镜像，回退官方）——验证跨平台下载内核
              help                     显示本帮助
            """);
    }

    // ---------- selftest：无网络的纯逻辑验证 ----------

    private static int SelfTest()
    {
        var ok = 0; var fail = 0;
        void Check(string name, bool cond)
        {
            if (cond) { ok++; Console.WriteLine($"  ✓ {name}"); }
            else { fail++; Console.WriteLine($"  ✗ {name}"); }
        }

        Console.WriteLine("[1] 便携化配置（不写 AppData / 注册表）");
        Check("config 目录位于应用目录内",
            AppSettings.ConfigDir.StartsWith(AppSettings.AppDir, StringComparison.Ordinal));
        Console.WriteLine($"      AppDir = {AppSettings.AppDir}");
        Console.WriteLine($"      Config = {AppSettings.ConfigDir}");

        Console.WriteLine("[2] 离线 UUID（UUID v3, MD5(OfflinePlayer:name)）");
        var uuid = AuthService.OfflineUuid("Steve");
        Check($"OfflineUuid(Steve) = {uuid}", uuid == "5627dd98-e6be-3c21-b8a8-e92344183641");
        Check("不同玩家名 UUID 不同", AuthService.OfflineUuid("Alex") != uuid);

        Console.WriteLine("[3] CurseForge CDN 规则与分区映射");
        Check("BuildCdnUrl(999443) 含 files/999/443/",
            CurseForgeService.BuildCdnUrl(999443, "a.jar") == "https://mediafilez.forgecdn.net/files/999/443/a.jar");
        Check("ClassIdFor(mod)=6", CurseForgeService.ClassIdFor("mod") == 6);
        Check("ClassIdFor(shader)=6552", CurseForgeService.ClassIdFor("shader") == 6552);
        Check("CfLoaderId(fabric)=4", CurseForgeService.CfLoaderId("fabric") == 4);

        Console.WriteLine("[4] CurseForge 兼容文件挑选（纯函数）");
        var f1 = new CurseForgeService.CfFile(1, 100, "beta-new", "b.jar", 1, "beta",
            new[] { "1.20.1", "Fabric" }, DateTimeOffset.UtcNow, null,
            Array.Empty<CurseForgeService.CfDependency>());
        var f2 = new CurseForgeService.CfFile(2, 100, "release-old", "r.jar", 1, "release",
            new[] { "1.20.1", "Fabric" }, DateTimeOffset.UtcNow.AddDays(-30), null,
            Array.Empty<CurseForgeService.CfDependency>());
        var f3 = new CurseForgeService.CfFile(3, 100, "other-loader", "o.jar", 1, "release",
            new[] { "1.20.1", "Forge" }, DateTimeOffset.UtcNow, null,
            Array.Empty<CurseForgeService.CfDependency>());
        var pick = CurseForgeService.PickCompatibleFile(new[] { f1, f2, f3 }, "1.20.1", "fabric");
        Check("release 优先于 beta", pick?.Id == 2);
        var pickForge = CurseForgeService.PickCompatibleFile(new[] { f1, f2, f3 }, "1.20.1", "forge");
        Check("按加载器过滤", pickForge?.Id == 3);
        var pickNone = CurseForgeService.PickCompatibleFile(new[] { f3 }, "1.20.1", "fabric");
        Check("无兼容文件返回 null", pickNone == null);

        Console.WriteLine("[5] 启动参数解析");
        var jvm = LauncherService.ParseJvmArgs("-Xmx2G -Dfoo=bar");
        Check("ParseJvmArgs 拆分", jvm.Contains("-Xmx2G") && jvm.Contains("-Dfoo=bar"));

        Console.WriteLine(fail == 0 ? $"全部通过：{ok} 项" : $"失败 {fail} 项 / 共 {ok + fail} 项");
        return fail == 0 ? 0 : 1;
    }

    private static int OfflineUuid(string name)
    {
        Console.WriteLine(AuthService.OfflineUuid(name));
        return 0;
    }

    private static int Versions(string? root)
    {
        using var svc = new LauncherService();
        var versions = svc.GetInstalledVersions(root);
        if (versions.Count == 0)
        {
            Console.WriteLine("(未找到已安装版本)");
            return 0;
        }
        foreach (var v in versions) Console.WriteLine(v);
        Console.WriteLine($"共 {versions.Count} 个版本");
        return 0;
    }

    // ---------- meta：真实网络验证（镜像内核跨平台可用性） ----------

    private static async Task<int> MetaAsync()
    {
        using var svc = new LauncherService();
        var sw = Stopwatch.StartNew();
        var versions = await svc.GetAllVersionsAsync();
        var list = versions.ToList();
        sw.Stop();
        Console.WriteLine($"清单拉取成功：{list.Count} 个版本，耗时 {sw.ElapsedMilliseconds} ms");
        foreach (var v in list.Take(8))
            Console.WriteLine($"  {v.Name}");
        return list.Count > 0 ? 0 : 1;
    }
}
