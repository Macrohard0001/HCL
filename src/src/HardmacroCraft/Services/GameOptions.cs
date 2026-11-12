using System.IO;

namespace HardmacroCraft.Services;

/// <summary>
/// 游戏启动前选项补丁（fix7，PCL 同款）：
/// 1) 跳过初始屏幕 —— 首次启动的欢迎 / 辅助功能引导界面由 options.txt 的
///    onboardAccessibility 控制（默认 true 显示；写 false 跳过，第三方启动器通用做法）；
/// 2) 游戏语言 —— 写入 lang:zh_CN 等值，进游戏直接是中文。
/// 两者都在启动前修改版本游戏目录下的 options.txt，其余行原样保留；
/// 文件不存在时创建（游戏退出后会自己重写该文件，之后仍以本处值为准）。
/// </summary>
public static class GameOptions
{
    /// <summary>
    /// 纯函数：在 options.txt 行列表中替换 key:value，不存在则追加。
    /// 仅替换第一处出现（游戏自身保存时也会去重）。
    /// </summary>
    public static List<string> PatchLine(IEnumerable<string> lines, string key, string value)
    {
        var list = new List<string>(lines);
        var prefix = key + ":";
        for (var i = 0; i < list.Count; i++)
        {
            var line = list[i];
            var colon = line.IndexOf(':');
            if (colon > 0 && line[..colon] == key)
            {
                list[i] = prefix + value;
                return list;
            }
        }
        list.Add(prefix + value);
        return list;
    }

    /// <summary>
    /// 启动前把「跳过初始屏幕 / 游戏语言」写入 dir/options.txt。
    /// lang 为空且不跳过初始屏幕时不做任何事（完全尊重游戏自身设置）。
    /// 失败只抛给调用方（启动流程里记日志不阻断）。
    /// </summary>
    public static void Apply(string dir, string? lang, bool skipOnboarding)
    {
        if (string.IsNullOrWhiteSpace(lang) && !skipOnboarding) return;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "options.txt");
        var lines = File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
        if (skipOnboarding)
            lines = PatchLine(lines, "onboardAccessibility", "false").ToArray();
        if (!string.IsNullOrWhiteSpace(lang))
            lines = PatchLine(lines, "lang", lang).ToArray();
        File.WriteAllLines(path, lines);
    }

    /// <summary>启动前按设置应用游戏选项（版本隔离时写入 versions/&lt;id&gt;/options.txt）。</summary>
    public static void ApplyForVersion(string gameRoot, string versionName, bool versionIsolation,
        string? lang, bool skipOnboarding)
    {
        if (string.IsNullOrWhiteSpace(lang) && !skipOnboarding) return;
        var dir = versionIsolation
            ? Path.Combine(gameRoot, "versions", versionName)
            : gameRoot;
        Apply(dir, lang, skipOnboarding);
    }
}
