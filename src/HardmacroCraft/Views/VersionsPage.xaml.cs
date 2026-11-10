using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 版本管理页（v0.1.6 重构，对齐主流启动器）：
/// 搜索过滤 + 图标列表（加载器 / 创建来源一眼可辨）+ 单击进版本设置 + 双击启动。
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
        LstVersions.ItemsSource = kw.Length == 0
            ? _all
            : _all.Where(i => i.Name.Contains(kw, StringComparison.OrdinalIgnoreCase)).ToList();
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

    // ---------- 文件操作 ----------

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelected()) return;
        var v = SelectedVersion!;
        var newName = Prompt.Show("新版本名（存档 / mods / 资源包目录将一并改名）：", "重命名版本");
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            App.LauncherService.RenameVersion(v, newName.Trim());
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show("重命名失败: " + ex.Message, "Hardmacro Craft Launcher");
        }
    }

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureSelected()) return;
        var v = SelectedVersion!;
        var newName = Prompt.Show("新版本名（将完整复制该版本的文件与隔离内容）：", "复制版本");
        if (string.IsNullOrWhiteSpace(newName)) return;
        try
        {
            App.LauncherService.CopyVersion(v, newName.Trim());
            Refresh();
        }
        catch (Exception ex)
        {
            MessageBox.Show("复制失败: " + ex.Message, "Hardmacro Craft Launcher");
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = SelectedVersion is string v
            ? Path.Combine(AppSettings.Instance.GameRoot, "versions", v)
            : AppSettings.Instance.GameRoot;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>打开所选版本的存档 / mods / 资源包目录（版本隔离时指向 versions/&lt;id&gt;/ 下）。</summary>
    private void OpenSubDir(string sub)
    {
        var root = AppSettings.Instance.GameRoot;
        var version = SelectedVersion;
        var path = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation)
            && version != null
            ? Path.Combine(root, "versions", version, sub)
            : Path.Combine(root, sub);
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void BtnOpenSaves_Click(object sender, RoutedEventArgs e) => OpenSubDir("saves");

    private void BtnOpenMods_Click(object sender, RoutedEventArgs e) => OpenSubDir("mods");

    private void BtnOpenPacks_Click(object sender, RoutedEventArgs e) => OpenSubDir("resourcepacks");

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
