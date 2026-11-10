using System.Windows.Media;

namespace HardmacroCraft.Services;

/// <summary>
/// v0.1.6 版本图标（对齐主流启动器）：
/// - 有加载器（NeoForge/Forge/Fabric/Quilt/…）→ 对应加载器图标（一眼看出是什么环境）；
/// - 纯净版 → 按创建来源显示：本启动器安装 → 本启动器 logo；其他启动器创建 → 首字母徽章
///   （「方便查看是哪种启动器」，只以单字母徽章区分，不在任何文案中出现名称字样）；
/// - 无标记的纯净版（官方启动器 / 手动放置）→ 草方块。
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

    /// <summary>来源徽章：字母 + 主题色（仅纯净版 + 有来源标记时显示）。</summary>
    public static (string Letter, Brush Brush)? SourceBadge(LauncherSource src) => src switch
    {
        LauncherSource.Pcl => ("P", new SolidColorBrush(Color.FromRgb(0xE0, 0x9C, 0x2C))),
        LauncherSource.Hmcl => ("H", new SolidColorBrush(Color.FromRgb(0x33, 0xA5, 0xDC))),
        _ => null,
    };

    /// <summary>版本列表条目视图模型（图标 / 徽章 / 名称一次成型，绑定零转换器）。</summary>
    public static (ImageSource? Icon, string? Badge, Brush? BadgeBrush) DescribeIcon(VersionInfo.VersionDescriptor desc)
    {
        var loaderIcon = LoaderIcon(desc.Loader);
        if (loaderIcon != null) return (loaderIcon, null, null);

        if (desc.Source == LauncherSource.Hcl)
            return (Load("icon.png"), null, null);
        if (desc.Source == LauncherSource.None)
            return (Load("grass.png"), null, null);

        var badge = SourceBadge(desc.Source);
        return (null, badge?.Letter, badge?.Brush);
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
