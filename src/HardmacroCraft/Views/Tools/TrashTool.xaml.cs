using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.10：回收站管理（独立窗口）—— 恢复 / 永久删除 / 清空 / 定位。</summary>
public partial class TrashTool : UserControl
{
    public TrashTool()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshList();
    }

    private void BtnTrashRefresh_Click(object sender, RoutedEventArgs e) => RefreshList();

    private void RefreshList()
    {
        try
        {
            var entries = TrashService.ListTrash();
            CmbTrash.ItemsSource = entries;
            if (entries.Count > 0)
            {
                CmbTrash.SelectedIndex = 0;
                TxtTrashStatus.Text = $"共 {entries.Count} 条（移入时间倒序；世界条目会移回共享 saves，其余移回游戏根）。";
            }
            else
            {
                TxtTrashStatus.Text = "回收站是空的——删除世界时会先进这里，随时可找回。";
            }
        }
        catch (Exception ex) { TxtTrashStatus.Text = "扫描失败：" + ex.Message; }
    }

    private void CmbTrash_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* 详情在 Display 内 */ }

    private TrashService.TrashEntry? Selected =>
        CmbTrash.SelectedItem as TrashService.TrashEntry;

    private void BtnTrashRestore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } en) { TxtTrashStatus.Text = "请先选择要恢复的条目。"; return; }
        try
        {
            var dest = TrashService.Restore(en);
            TxtTrashStatus.Text = "✓ 已恢复到：" + dest;
            RefreshList();
        }
        catch (Exception ex) { TxtTrashStatus.Text = "恢复失败：" + ex.Message; }
    }

    private void BtnTrashDelete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } en) { TxtTrashStatus.Text = "请先选择要永久删除的条目。"; return; }
        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"永久删除「{en.Name}」？\n\n此操作不可找回（不走回收站）。",
            "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            TrashService.DeletePermanently(en);
            TxtTrashStatus.Text = "✓ 已永久删除：" + en.Name;
            RefreshList();
        }
        catch (Exception ex) { TxtTrashStatus.Text = "删除失败：" + ex.Message; }
    }

    private void BtnTrashEmpty_Click(object sender, RoutedEventArgs e)
    {
        var entries = TrashService.ListTrash();
        if (entries.Count == 0) { TxtTrashStatus.Text = "回收站已经是空的。"; return; }
        var confirm = MessageBox.Show(Window.GetWindow(this),
            $"清空回收站（{entries.Count} 条）？\n\n此操作不可找回（不走回收站）。",
            "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            var n = TrashService.EmptyTrash();
            TxtTrashStatus.Text = $"✓ 已清空（删除 {n} 条）。";
            RefreshList();
        }
        catch (Exception ex) { TxtTrashStatus.Text = "清空失败：" + ex.Message; }
    }

    private void BtnTrashReveal_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } en) { TxtTrashStatus.Text = "请先选择条目。"; return; }
        try { HorizonService.RevealInExplorer(en.Path); }
        catch (Exception ex) { TxtTrashStatus.Text = "打开失败：" + ex.Message; }
    }
}
