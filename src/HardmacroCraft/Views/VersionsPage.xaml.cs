using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 版本管理页（v0.1.6 重构，对齐主流启动器）：
/// 搜索过滤 + 图标列表（加载器 / 创建来源一眼可辨）+ 单击进版本设置 + 双击启动。
/// v0.1.14：
/// - 最近启动卡自首页迁移至此（版本相关操作集中在版本管理页）；
/// - 工具按钮收纳（重命名 / 复制 / 目录直达移入版本独立设置窗口），页面只留高频操作；
/// - 修复「打开版本目录」在 v0.1.6 后因 SelectedVersion 类型变化永远打开游戏根目录的问题
///   （版本目录入口收敛到版本独立设置窗口的「打开版本目录」，逻辑唯一且正确）。
/// </summary>
public partial class VersionsPage : Page
{
    /// <summary>全量版本条目（搜索框只过滤展示，不重新扫盘）。</summary>
    private List<VersionItemViewModel> _all = new();

    public VersionsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        try
        {
            _all = App.LauncherService.GetInstalledVersions()
                .Select(v =>
                {
                    var desc = VersionInfo.Describe(AppSettings.Instance.GameRoot, v);
                    return new VersionItemViewModel(v, desc, BuildSubtitle(desc));
                })
                .ToList();
            ApplyFilter();
            RefreshRecent();
        }
        catch (Exception ex)
        {
            MessageBox.Show("读取版本失败: " + ex.Message, "Hardmacro Craft Launcher");
        }
    }

    /// <summary>版本副标题：加载器类型 + 创建来源说明（与首页下拉框一致）。</summary>
    private string BuildSubtitle(VersionInfo.VersionDescriptor desc)
    {
        var loader = (string)FindResource(VersionInfo.LoaderKey(desc.Loader))!;
        return desc.Source switch
        {
            LauncherSource.Hcl => loader + " · " + (string)FindResource("S.Ver.FromHcl")!,
            LauncherSource.Pcl or LauncherSource.Hmcl => loader + " · " + (string)FindResource("S.Ver.FromOther")!,
            _ => loader,
        };
    }

    private void ApplyFilter()
    {
        var kw = TxtSearch.Text?.Trim() ?? "";
        var view = kw.Length == 0
            ? _all
            : _all.Where(i => i.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
        LstVersions.ItemsSource = view;
        // v0.1.7：版本计数（搜索时显示「匹配数 / 总数」，对齐同类启动器的列表头）
        var total = _all.Count;
        TxtVerCount.Text = string.Format(
            System.Windows.Application.Current.TryFindResource("S.Ver.Count") as string ?? "{0} 个版本",
            kw.Length == 0 ? total : $"{view.Count}/{total}");
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private string? SelectedVersion => (LstVersions.SelectedItem as VersionItemViewModel)?.Name;

    private bool EnsureSelected()
    {
        if (SelectedVersion != null) return true;
        MessageBox.Show("请先选择一个版本。", "Hardmacro Craft Launcher");
        return false;
    }

    // ---------- 启动 / 版本设置 ----------

    /// <summary>启动所选版本：切回主页并直连启动（复用主页启动管线与进度中枢）。</summary>
    private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        var version = SelectedVersion;
        if (version == null)
        {
            MessageBox.Show("请先选择一个版本。", "Hardmacro Craft Launcher");
            return;
        }
        await LaunchExternalAsync(version);
    }

    /// <summary>跨页启动：切回主页并直连启动（最近启动 chips / 启动按钮共用）。</summary>
    internal async Task LaunchExternalAsync(string version)
    {
        if (Application.Current.MainWindow is MainWindow main)
        {
            try
            {
                main.RootNavigation.Navigate(typeof(HomePage));
                // 导航同步完成后，新首页实例已就绪（HomePage.Current 在构造器赋值）
                var home = HomePage.Current;
                if (home != null)
                    await home.LaunchFromExternalAsync(version);
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动失败: " + ex.Message, "Hardmacro Craft Launcher");
            }
        }
    }

    /// <summary>v0.1.6：单击列表项 → 打开版本独立设置（对齐主流启动器的「点版本进设置」）。</summary>
    private void LstVersions_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => BtnSettings_Click(sender, e);

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelected()) return;
        VersionSettingsWindow.Show(Window.GetWindow(this), SelectedVersion!);
        // 版本设置可能改了隔离等展示信息（副标题不含这些，无需刷新；保持简单）
    }

    // ---------- 最近启动（v0.1.14 自首页迁移） ----------

    /// <summary>最近启动快捷 chips（图标对齐版本列表：加载器图标 / 原版草方块）。</summary>
    private void RefreshRecent()
    {
        PnlRecent.Children.Clear();
        var recent = AppSettings.Instance.RecentVersions;
        CardRecent.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var v in recent)
        {
            var installed = App.LauncherService.GetInstalledVersions().Contains(v);
            ImageSource? icon = null;
            string? badge = null;
            Brush? badgeBrush = null;
            try
            {
                var desc = VersionInfo.Describe(AppSettings.Instance.GameRoot, v);
                (icon, badge, badgeBrush) = VersionIconProvider.DescribeIcon(desc);
            }
            catch { /* 版本目录读取失败时回退草方块 */ }
            icon ??= new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/grass.png"));
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
            chipContent.Children.Add(new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 16, Height = 16,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
            });
            chipContent.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = v, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Opacity = installed ? 1.0 : 0.45,
                FontSize = 12,
            });
            if (chipContent.Children[0] is System.Windows.Controls.Image chipImg)
                RenderOptions.SetBitmapScalingMode(chipImg, BitmapScalingMode.NearestNeighbor);
            var chip = new Wpf.Ui.Controls.Button
            {
                Content = chipContent,
                MinHeight = 30,
                Padding = new Thickness(8, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = installed ? v : v + "（已不存在，点击重试）",
            };
            chip.Click += async (_, _) => await LaunchExternalAsync(v);
            PnlRecent.Children.Add(chip);
        }
    }

    // ---------- 删除（低频 destructive 操作保留在页面 + 版本设置窗口双入口） ----------

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelected()) return;
        var v = SelectedVersion!;
        var r = MessageBox.Show($"确定删除版本 [{v}] 吗？\n（版本隔离开启时，其存档 / mod 一并删除）",
            "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            App.LauncherService.DeleteVersion(v);
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show("删除失败: " + ex.Message, "Hardmacro Craft Launcher");
        }
    }
}
