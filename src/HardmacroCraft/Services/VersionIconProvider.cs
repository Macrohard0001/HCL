using System.Windows.Media;

namespace HardmacroCraft.Services;

/// <summary>
/// 版本图标（v0.1.8 紧急修复，对齐主流启动器的「自动图标」规则）：
/// - 有加载器（NeoForge/Forge/Fabric/Quilt/…）→ 对应加载器图标（按版本 json 内容识别，
///   与版本名叫「默认名」还是「自定义名」无关，纯净版 json 特征不误判）；
/// - 纯净版 → 原版草方块（不再按创建来源显示启动器 Logo / 字母徽章 ——
///   来源信息已在副标题「创建来源」中说明，图标应表达「这个版本是什么」而不是「谁装的」）。
/// </summary>
public static class VersionIconProvider
{
    public static ImageSource? LoaderIcon(McLoaderKind k) => k switch
    {
        McLoaderKind.NeoForge => Load("NeoForge.png"),
        McLoaderKind.Forge => Load("Anvil.png"),
        McLoaderKind.Fabric => Load("Fabric.png"),
        McLoaderKind.LegacyFabric => Load("Fabric.png"),
        McLoaderKind.Quilt => Load("Quilt.png"),
        _ => null,
    };

    private static ImageSource? Load(string file)
    {
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Assets/" + file);
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>来源徽章（v0.1.8 起图标不再使用，保留给需要小字来源标记的场景）。</summary>
    public static (string Letter, Brush Brush)? SourceBadge(LauncherSource src) => src switch
    {
        LauncherSource.Pcl => ("P", new SolidColorBrush(Color.FromRgb(0xE0, 0x9C, 0x2C))),
        LauncherSource.Hmcl => ("H", new SolidColorBrush(Color.FromRgb(0x33, 0xA5, 0xDC))),
        _ => null,
    };

    /// <summary>图标决策（纯规则，便于测试）：有加载器 → 加载器图标；否则 → 草方块。</summary>
    public static string DecideIconKind(VersionInfo.VersionDescriptor desc)
        => desc.Loader == McLoaderKind.Vanilla ? "grass" : "loader:" + desc.Loader;

    /// <summary>版本列表条目视图模型（图标 / 徽章 / 名称一次成型，绑定零转换器）。</summary>
    public static (ImageSource? Icon, string? Badge, Brush? BadgeBrush) DescribeIcon(VersionInfo.VersionDescriptor desc)
    {
        var loaderIcon = LoaderIcon(desc.Loader);
        if (loaderIcon != null) return (loaderIcon, null, null);
        // 纯净版（含本启动器安装 / 其他启动器创建 / 无标记）一律原版草方块
        return (Load("grass.png"), null, null);
    }
}

/// <summary>版本下拉框 / 版本列表的条目视图模型（图标 + 徽章 + 名称 + 副标题，一次性绑定）。</summary>
public sealed class VersionItemViewModel
{
    public string Name { get; }
    public string Subtitle { get; }
    public ImageSource? Icon { get; }
    public string? Badge { get; }
    public Brush? BadgeBrush { get; }
    public System.Windows.Visibility IconVis =>
        Icon != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public System.Windows.Visibility BadgeVis =>
        Badge != null ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public VersionItemViewModel(string name, VersionInfo.VersionDescriptor desc, string subtitle)
    {
        Name = name;
        Subtitle = subtitle;
        (Icon, Badge, BadgeBrush) = VersionIconProvider.DescribeIcon(desc);
    }
}
