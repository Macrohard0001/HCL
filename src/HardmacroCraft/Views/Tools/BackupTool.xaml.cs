using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：实例备份 / 恢复（独立窗口版，自 ToolsPage 迁移）。</summary>
public partial class BackupTool : UserControl
{
    public BackupTool()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            try { CmbBackupVersion.ItemsSource = App.LauncherService.GetInstalledVersions(); }
            catch { /* 版本列表读取失败不阻塞备份 */ }
        };
    }

    private async void BtnBackupDo_Click(object sender, RoutedEventArgs e)
    {
        if (CmbBackupVersion.SelectedItem is not string ver)
        {
            TxtBackupStatus.Text = "请先选择要备份的版本。";
            return;
        }
        try
        {
            BtnBackupDo.IsEnabled = false;
            TxtBackupStatus.Text = $"备份「{ver}」中（整个版本目录打成 zip，含隔离实例数据）…";
            var path = await Task.Run(() => InstanceBackupService.Backup(ver));
            TxtBackupStatus.Text = "✓ 备份完成：" + path;
            HorizonService.RevealInExplorer(path);
        }
        catch (Exception ex) { TxtBackupStatus.Text = "备份失败：" + ex.Message; }
        finally { BtnBackupDo.IsEnabled = true; }
    }

    private async void BtnBackupRestore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择备份压缩包",
            Filter = "备份压缩包 (*.zip)|*.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            BtnBackupRestore.IsEnabled = false;
            TxtBackupStatus.Text = "恢复中（重名版本自动加 -restored 后缀，不会覆盖现有版本）…";
            var name = await Task.Run(() => InstanceBackupService.Restore(dlg.FileName));
            TxtBackupStatus.Text = $"✓ 已恢复为版本「{name}」——启动页 / 版本列表刷新后即可使用。";
        }
        catch (Exception ex) { TxtBackupStatus.Text = "恢复失败：" + ex.Message; }
        finally { BtnBackupRestore.IsEnabled = true; }
    }
}
