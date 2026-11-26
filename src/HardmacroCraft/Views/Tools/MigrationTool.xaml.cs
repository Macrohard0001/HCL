using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：从其他启动器导入（独立窗口版，自 ToolsPage 迁移）。</summary>
public partial class MigrationTool : UserControl
{
    public MigrationTool()
    {
        InitializeComponent();
    }

    private async void BtnMigScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnMigScan.IsEnabled = false;
            TxtMigStatus.Text = "正在扫描内置位置（官方 .minecraft / Prism / PolyMC / MultiMC 及常见嵌套布局）…";
            var cands = await Task.Run(() =>
                LauncherMigrationService.BuiltinRoots()
                    .SelectMany(LauncherMigrationService.ScanFolder)
                    .GroupBy(c => (c.LauncherKind, c.VersionName))
                    .Select(g => g.First())
                    .ToList());
            CmbMigCandidates.ItemsSource = cands;
            if (cands.Count > 0)
            {
                CmbMigCandidates.SelectedIndex = 0;
                ChkMigIsolation.IsChecked = AppSettings.Instance.VersionIsolation;
            }
            TxtMigStatus.Text = cands.Count == 0
                ? "内置位置没有发现可导入的版本——常见启动器布局（官方 .minecraft / instances 容器 / 二级版本目录）均已支持，也可把对应目录放进上述位置再扫。"
                : $"发现 {cands.Count} 个候选版本，下拉选择后点「导入所选」。";
        }
        catch (Exception ex) { TxtMigStatus.Text = "扫描失败：" + ex.Message; }
        finally { BtnMigScan.IsEnabled = true; }
    }

    private async void BtnMigImport_Click(object sender, RoutedEventArgs e)
    {
        if (CmbMigCandidates.SelectedItem is not MigrationCandidate cand)
        {
            TxtMigStatus.Text = "请先「扫描」并选择一个候选版本。";
            return;
        }
        try
        {
            BtnMigImport.IsEnabled = false;
            var isolation = ChkMigIsolation.IsChecked == true;
            TxtMigStatus.Text = $"导入中：{cand.VersionName}（{cand.LauncherKind}，{(isolation ? "带实例数据" : "仅版本")}）…";
            var name = await Task.Run(() => LauncherMigrationService.Import(cand, isolation));
            TxtMigStatus.Text = $"✓ 导入完成，新版本名「{name}」——启动页 / 版本列表刷新后即可使用。";
        }
        catch (Exception ex) { TxtMigStatus.Text = "导入失败：" + ex.Message; }
        finally { BtnMigImport.IsEnabled = true; }
    }
}
