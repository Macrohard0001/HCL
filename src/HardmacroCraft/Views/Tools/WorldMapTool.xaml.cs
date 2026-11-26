using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：世界地图工具（独立窗口版，自 ToolsPage 迁移）——
/// 扫描存档 → Anvil 顶视图渲染（WorldMapRenderer）→ 查看窗口。</summary>
public partial class WorldMapTool : UserControl
{
    public WorldMapTool()
    {
        InitializeComponent();
        Loaded += (_, _) => BtnMapScan_Click(this, new RoutedEventArgs()); // 打开即扫一次
    }

    private void BtnMapScan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var worlds = WorldMapRenderer.FindWorlds();
            CmbMapWorlds.ItemsSource = worlds.Select(w => w.Name).ToList();
            CmbMapWorlds.SelectedIndex = worlds.Count > 0 ? 0 : -1;
            TxtMapStatus.Text = worlds.Count == 0
                ? "没有找到带 level.dat 的世界（共享 saves 与隔离 versions/*/saves 都扫了）——先进游戏创建存档。"
                : $"找到 {worlds.Count} 个世界，选择后点「生成顶视图」。";
        }
        catch (Exception ex) { TxtMapStatus.Text = "扫描失败：" + ex.Message; }
    }

    private async void BtnMapRender_Click(object sender, RoutedEventArgs e)
    {
        if (CmbMapWorlds.SelectedItem is not string name)
        {
            TxtMapStatus.Text = "请先「扫描世界」并选择一个存档。";
            return;
        }
        var dir = WorldMapRenderer.FindWorlds().FirstOrDefault(w => w.Name == name).Dir;
        if (string.IsNullOrEmpty(dir)) { TxtMapStatus.Text = "世界目录已不存在，请重新扫描。"; return; }
        try
        {
            BtnMapRender.IsEnabled = false;
            TxtMapStatus.Text = $"正在渲染「{name}」的顶视图（后台解析 region/*.mca）…";
            var pngPath = Path.Combine(HorizonService.DownloadDir, $"HmMap-{name}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var result = await Task.Run(() =>
            {
                var (png, w, h, info, ok, skip) = WorldMapRenderer.RenderWorld(dir);
                Directory.CreateDirectory(HorizonService.DownloadDir);
                File.WriteAllBytes(pngPath, png);
                return (Info: info, Chunks: ok, Skipped: skip);
            });
            var note = result.Skipped > 0 ? $"，{result.Skipped} 个旧格式区块已跳过" : "";
            TxtMapStatus.Text = $"✓ 渲染完成：{result.Chunks} 个区块{note} · {result.Info} → {pngPath}";
            MapViewerWindow.Show(Window.GetWindow(this), pngPath, $"{result.Info} · {result.Chunks} 区块{note}");
        }
        catch (Exception ex) { TxtMapStatus.Text = "渲染失败：" + ex.Message; }
        finally { BtnMapRender.IsEnabled = true; }
    }
}
