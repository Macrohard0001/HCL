namespace HardmacroCraft.Services;

/// <summary>
/// 启动参数构建（fix8，PCL 同款）：默认 JVM 优化参数、自定义 JVM 参数、
/// 自定义游戏参数的解析与合并。全部为纯函数，无 IO / 无 UI 依赖，
/// 通过源码链接进入 HmCore 供跨平台测试（单一事实来源）。
/// </summary>
public static class LaunchArgs
{
    /// <summary>
    /// 默认 JVM 优化参数（PCL / HMCL 同款，推荐开启）：
    /// · G1 垃圾回收 + 关闭自适应SizePolicy + 关闭快速抛出省略堆栈 —— 减少游戏卡顿、崩溃日志更完整；
    /// · Forge 兼容性两个 -D：忽略损坏证书 / 补丁差异（老 Forge 整合包常见报错根因）；
    /// · log4j2.formatMsgNoLookups=true —— Log4Shell（CVE-2021-44228）缓解，老版本联机更安全。
    /// 版本自带的 JVM 参数之后追加；与用户自定义参数冲突时自定义在后、优先生效。
    /// </summary>
    public static readonly string[] OptimizedJvmArgs =
    {
        "-XX:+UseG1GC",
        "-XX:-UseAdaptiveSizePolicy",
        "-XX:-OmitStackTraceInFastThrow",
        "-Dfml.ignoreInvalidMinecraftCertificates=true",
        "-Dfml.ignorePatchDiscrepancies=true",
        "-Dlog4j2.formatMsgNoLookups=true",
    };

    /// <summary>
    /// 解析参数串：空白分隔；支持双引号包裹含空格的参数（PCL 行为）；
    /// 引号本身剥除；空串返回空列表。（LauncherService.ParseJvmArgs 委托至此。）
    /// </summary>
    public static List<string> ParseArgs(string? input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return result;
        var cur = new System.Text.StringBuilder();
        var inQuote = false;
        var hasToken = false;
        foreach (var ch in input)
        {
            if (ch == '"') { inQuote = !inQuote; hasToken = true; continue; }
            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0 || hasToken)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                    hasToken = false;
                }
                continue;
            }
            cur.Append(ch);
        }
        if (cur.Length > 0 || hasToken) result.Add(cur.ToString());
        return result;
    }

    /// <summary>
    /// 参数「键」：-Dkey=value / --flag=value 取 '=' 前的键部分，其余取全串。
    /// 用于去重与冲突判定：同一键的参数后声明者生效（JVM 规则），因此
    /// 自定义 -Dfml.xxx=false 应压过优化参数 -Dfml.xxx=true。
    /// </summary>
    private static string ArgKey(string a)
    {
        var eq = a.IndexOf('=');
        return eq > 0 ? a[..eq] : a;
    }

    /// <summary>
    /// 构建 JVM 附加参数（fix8）：
    /// 1) optimize=true 时先加默认优化参数；但若用户自定义里已选了其它 GC 收集器
    ///    （-XX:+Use…GC），跳过 UseG1GC 避免 GC 冲突警告 —— 自定义优先；
    /// 2) 自定义中已出现同一「参数键」（含 -Dkey= 反值）时跳过对应优化参数；
    /// 3) 最后追加用户自定义参数（在后 = JVM 中后声明优先）。
    /// </summary>
    public static List<string> BuildJvmExtra(bool optimize, string? customArgs)
    {
        var custom = ParseArgs(customArgs);
        var customKeys = custom.Select(ArgKey).ToHashSet(StringComparer.Ordinal);
        var result = new List<string>();
        if (optimize)
        {
            var customHasGcSelector = custom.Any(a => a.StartsWith("-XX:+Use", StringComparison.Ordinal)
                && a.EndsWith("GC", StringComparison.Ordinal));
            foreach (var a in OptimizedJvmArgs)
            {
                if (customHasGcSelector && a == "-XX:+UseG1GC") continue; // 用户已自选 GC
                if (customKeys.Contains(ArgKey(a))) continue; // 用户已写同一键（含反值覆盖）
                result.Add(a);
            }
        }
        result.AddRange(custom);
        return result;
    }

    /// <summary>构建游戏附加参数（如 --fullscreen --demo），与 JVM 参数同一解析规则。</summary>
    public static List<string> BuildGameExtra(string? customArgs) => ParseArgs(customArgs);
}
