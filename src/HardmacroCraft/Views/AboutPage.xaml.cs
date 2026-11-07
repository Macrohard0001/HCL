using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views;

public partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();

        // 版本号运行时从程序集读取，避免资源串硬编码过期（如 v1.3.1 忘改的情况）。
        // 优先读 InformationalVersion（含预发布后缀，如 0.1.0-beta），回退数值版本
        var informational = typeof(AboutPage).Assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var v = typeof(AboutPage).Assembly.GetName().Version;
        var shown = informational ?? (v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.1.0");
        var template = TryFindResource("S.About.Version") as string;
        if (string.IsNullOrEmpty(template))
            template = "v{0} · MIT License";
        TxtVersion.Text = string.Format(template, shown);
    }
}
