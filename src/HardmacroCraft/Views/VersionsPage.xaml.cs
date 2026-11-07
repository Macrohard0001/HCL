using System.Diagnostics;
using System.IO;
using System.Windows;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

public partial class VersionsPage : System.Windows.Controls.Page
{
    public VersionsPage()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        LstVersions.ItemsSource = App.LauncherService.GetInstalledVersions();
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (LstVersions.SelectedItem is not string v)
        {
            MessageBox.Show("请先选择一个版本。", "Hardmacro Craft Launcher");
            return;
        }
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
        if (LstVersions.SelectedItem is not string v)
        {
            MessageBox.Show("请先选择一个版本。", "Hardmacro Craft Launcher");
            return;
        }
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
        var path = LstVersions.SelectedItem is string v
            ? Path.Combine(AppSettings.Instance.GameRoot, "versions", v)
            : AppSettings.Instance.GameRoot;
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    /// <summary>打开所选版本的存档 / mods / 资源包目录（版本隔离时指向 versions/&lt;id&gt;/ 下）。</summary>
    private void OpenSubDir(string sub)
    {
        var root = AppSettings.Instance.GameRoot;
        var version = LstVersions.SelectedItem as string;
        var path = AppSettings.Instance.VersionIsolation && version != null
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
        if (LstVersions.SelectedItem is not string v)
        {
            MessageBox.Show("请先选择一个版本。", "Hardmacro Craft Launcher");
            return;
        }
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
