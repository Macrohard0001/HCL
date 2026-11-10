using System.Reflection;
using HardmacroCraft.Services;
using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        // 版本号运行时从程序集读取，避免资源串硬编码过期（如 v1.3.1 忘改的情况）。
        // 优先读 InformationalVersion（含预发布后缀，如 0.1.0-beta），回退数值版本。
        // v0.1.6 修复：.NET SDK 在 git 仓库构建时会把源码修订号（提交哈希）追加为
        // "<版本>+<40位sha>"（InformationalVersion），此前直接展示就出现「版本号后面一串乱码」。
        // 只取 '+' 之前的部分 —— 预发布后缀（-beta）保留，提交哈希舍弃。
        var informational = typeof(AboutPage).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var v = typeof(AboutPage).Assembly.GetName().Version;
        var raw = informational ?? (v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0");
        var shown = VersionInfo.CleanDisplayVersion(raw);
        var template = TryFindResource("S.About.Version") as string;
        if (string.IsNullOrEmpty(template))
            template = "v{0} · MIT License";
        TxtVersion.Text = string.Format(template, shown);
    }
}
