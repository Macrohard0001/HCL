using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.8：世界存档管理（独立窗口）—— 扫描 / 详情 / 备份 / 恢复 / 删除进回收站。</summary>
public partial class WorldsTool : UserControl
{
    public WorldsTool()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    private void BtnWorldsRefresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        try
        {
            var worlds = WorldsService.ListWorlds();
            CmbWorlds.ItemsSource = worlds;
            TxtWorldsStatus.Text = worlds.Count == 0
                ? (string?)TryFindResource("S.Tool.WorldsNone") ?? "没有找到存档（共享 saves 与隔离 versions/*/saves 都扫了）——先进游戏创建世界。"
                : string.Format((string?)TryFindResource("S.Tool.WorldsStatus") ?? "共 {0} 个存档（共享 saves + 隔离 versions/*/saves）", worlds.Count);
            if (worlds.Count > 0) CmbWorlds.SelectedIndex = 0;
            else { TxtWorldsInfo.Text = ""; }
        }
        catch (Exception ex) { TxtWorldsStatus.Text = "扫描失败：" + ex.Message; }
    }

    private void CmbWorlds_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateInfo();

    private void UpdateInfo()
    {
        if (CmbWorlds.SelectedItem is not WorldsService.WorldInfo w) { TxtWorldsInfo.Text = ""; return; }
        string L(string key, string fallback) => (string?)TryFindResource(key) ?? fallback;
        var mode = L($"S.Tool.WorldsMode.{w.GameType}", w.GameType);
        var diff = L($"S.Tool.WorldsDiff.{w.Difficulty}", w.Difficulty);
        TxtWorldsInfo.Text = string.Format(
            L("S.Tool.WorldsInfo", "世界名：{0}\n文件夹：{1}\n种子：{2}\n模式：{3}｜难度：{4}\n最后游玩：{5}\n游戏版本：{6}\n路径：{7}"),
            w.LevelName, w.Folder,
            w.Seed != 0 ? w.Seed.ToString() : "-",
            w.Hardcore ? mode + "（极限）" : mode, diff,
            w.LastPlayed?.ToString("yyyy-MM-dd HH:mm") ?? "-",
            w.VersionName.Length > 0 ? w.VersionName : "-",
            w.Dir);
    }

    private WorldsService.WorldInfo? Selected =>
        CmbWorlds.SelectedItem as WorldsService.WorldInfo;

    private async void BtnWorldsBackup_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } w)
        {
            TxtWorldsStatus.Text = "请先选择要备份的存档。";
            return;
        }
        try
        {
            BtnWorldsBackup.IsEnabled = false;
            TxtWorldsStatus.Text = $"备份「{w.Folder}」中（世界目录打成 zip 存到下载目录）…";
            var path = await Task.Run(() => WorldsService.BackupWorld(w.Dir));
            TxtWorldsStatus.Text = "✓ 备份完成：" + path;
            HorizonService.RevealInExplorer(path);
        }
        catch (Exception ex) { TxtWorldsStatus.Text = "备份失败：" + ex.Message; }
        finally { BtnWorldsBackup.IsEnabled = true; }
    }

    private async void BtnWorldsRestore_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择世界备份压缩包",
            Filter = "世界压缩包 (*.zip)|*.zip",
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            BtnWorldsRestore.IsEnabled = false;
            TxtWorldsStatus.Text = "恢复中（恢复到共享 saves，重名自动加 -restored 后缀，不会覆盖现有世界）…";
            var dir = await Task.Run(() => WorldsService.RestoreWorld(dlg.FileName));
            TxtWorldsStatus.Text = "✓ 已恢复到：" + dir;
            RefreshList();
        }
        catch (Exception ex) { TxtWorldsStatus.Text = "恢复失败：" + ex.Message; }
        finally { BtnWorldsRestore.IsEnabled = true; }
    }

    private void BtnWorldsOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } w) { TxtWorldsStatus.Text = "请先选择存档。"; return; }
        try { HorizonService.RevealInExplorer(w.Dir); }
        catch (Exception ex) { TxtWorldsStatus.Text = "打开失败：" + ex.Message; }
    }

    private async void BtnWorldsDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } w) { TxtWorldsStatus.Text = "请先选择要删除的存档。"; return; }
        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"确定删除世界「{w.LevelName}」吗？\n\n整目录将移入回收站 .hcl-trash（不直接物理删除，可到该目录找回）。",
            "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            BtnWorldsDelete.IsEnabled = false;
            var trash = await Task.Run(() => WorldsService.MoveToTrash(w.Dir));
            TxtWorldsStatus.Text = "✓ 已移入回收站：" + trash;
            RefreshList();
        }
        catch (Exception ex) { TxtWorldsStatus.Text = "删除失败：" + ex.Message; }
        finally { BtnWorldsDelete.IsEnabled = true; }
    }
}
