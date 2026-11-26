using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>
/// v0.16.7：种子地图工具（独立窗口版，自 ToolsPage 迁移）——
/// 纯算法图层（史莱姆区块 + 最佳挂机点黄色高亮）+ 拖动平移 / 滚轮缩放 /
/// 光标实时区块与方块坐标 / 大图窗口导出。算法见 SlimeChunkFinder。
/// </summary>
public partial class SeedMapTool : UserControl
{
    private const int ChunkPx = 4;                 // 底图每区块像素（放大交给 RenderTransform）
    private const uint SlimeColor = 0xFF3CA84C;    // BGRA：史莱姆绿
    private const uint BaseColor = 0xFF2A2A2A;     // BGRA：底格深灰
    private const uint FarmColor = 0xFFFFB900;     // BGRA：最佳挂机点高亮（黄）
    private const double ZoomMin = 1.0, ZoomMax = 12.0;

    private long _seed;
    private int _seedRadius;
    private bool[,] _seedMap = new bool[0, 0];
    private int _farmX = int.MinValue, _farmZ, _farmSize;
    private bool _panning;
    private Point _dragStart;
    private System.Windows.Vector _transStart;

    public SeedMapTool()
    {
        InitializeComponent();
    }

    private void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        // 种子：接受任意 64 位整数（含负数；MC /seed 输出的形式）
        if (!long.TryParse(TxtSeed.Text.Trim(), NumberFormatInfo.InvariantInfo, out var seed))
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.InvalidSeed") ?? "种子无效";
            SeedMapHost.Visibility = Visibility.Collapsed;
            TxtSeedPos.Visibility = Visibility.Collapsed;
            return;
        }
        if (!int.TryParse(TxtRange.Text.Trim(), out var radius) || radius is < 5 or > 64)
            radius = 40; // 缺省半径 40 区块（±320 格），够挑挂机塔位置

        var size = radius * 2 + 1;
        _seed = seed;
        _seedRadius = radius;
        _seedMap = SlimeChunkFinder.Map(seed, -radius, radius, -radius, radius, out var slimeCount);

        var bmp = new WriteableBitmap(size * ChunkPx, size * ChunkPx, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
        var stride = size * ChunkPx * 4;
        var pixels = new uint[size * ChunkPx * size * ChunkPx];
        for (var cz = 0; cz < size; cz++)
            for (var cx = 0; cx < size; cx++)
            {
                var color = _seedMap[cz, cx] ? SlimeColor : BaseColor;
                for (var py = 0; py < ChunkPx; py++)
                {
                    var rowBase = ((cz * ChunkPx) + py) * stride / 4 + cx * ChunkPx;
                    for (var px = 0; px < ChunkPx; px++)
                        pixels[rowBase + px] = color;
                }
            }
        _farmX = int.MinValue;
        if (SlimeChunkFinder.BestFarmSpot(_seedMap, -radius, -radius) is var (centerX, centerZ, spotSize))
        {
            _farmX = centerX; _farmZ = centerZ; _farmSize = spotSize;
            var half = spotSize / 2;
            for (var dz = -half; dz <= spotSize - 1 - half; dz++)
                for (var dx = -half; dx <= spotSize - 1 - half; dx++)
                {
                    var gx = (centerX + dx + radius) * ChunkPx;
                    var gz = (centerZ + dz + radius) * ChunkPx;
                    for (var py = 0; py < ChunkPx; py++)
                    {
                        var rowBase = (gz + py) * stride / 4 + gx;
                        for (var px = 0; px < ChunkPx; px++)
                            pixels[rowBase + px] = FarmColor;
                    }
                }
        }
        bmp.WritePixels(new Int32Rect(0, 0, size * ChunkPx, size * ChunkPx), pixels, stride, 0);
        SeedMapImage.Source = bmp;
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = 1;
        SeedMapTranslate.X = SeedMapTranslate.Y = 0;
        SeedMapHost.Visibility = Visibility.Visible;
        TxtSeedPos.Visibility = Visibility.Visible;
        TxtSeedPos.Text = "";

        var total = size * size;
        var statsTemplate = (string?)TryFindResource("S.Tool.Stats") ?? "史莱姆区块 {0} 个 / 共 {1} 个（{2}%）";
        var stats = string.Format(statsTemplate, slimeCount, total,
            (100.0 * slimeCount / total).ToString("0.0", CultureInfo.CurrentCulture));

        if (_farmX != int.MinValue)
        {
            var bestTemplate = (string?)TryFindResource("S.Tool.Best") ??
                "最佳挂机点：区块 ({0}, {1})，{2}×{2} 全绿连片，方块坐标 ({3}, {4})——挂机层 Y<40 有效";
            TxtStatus.Text = stats + "\n" + string.Format(bestTemplate,
                _farmX, _farmZ, _farmSize, _farmX * 16, _farmZ * 16);
        }
        else
        {
            TxtStatus.Text = stats + "\n" + ((string?)TryFindResource("S.Tool.BestNone")
                ?? "范围内没有 2×2 连片的史莱姆区块（可扩大半径再试）");
        }
    }

    /// <summary>宿主坐标 → 底图区块坐标（含变换反解与越界判定）。</summary>
    private (int ChunkX, int ChunkZ)? ChunkAt(Point hostPos)
    {
        if (SeedMapImage.Source is not BitmapSource bmp) return null;
        var px = (hostPos.X - SeedMapTranslate.X) / SeedMapScale.ScaleX;
        var py = (hostPos.Y - SeedMapTranslate.Y) / SeedMapScale.ScaleY;
        var size = _seedRadius * 2 + 1;
        var cx = (int)Math.Floor(px / ChunkPx);
        var cz = (int)Math.Floor(py / ChunkPx);
        if (cx < 0 || cz < 0 || cx >= size || cz >= size || px < 0 || py < 0) return null;
        return (cx - _seedRadius, cz - _seedRadius);
    }

    /// <summary>把平移量夹在「图像始终与宿主有交集」的范围内（拖丢不迷路）。</summary>
    private void ClampTranslate()
    {
        if (SeedMapImage.Source is not BitmapSource bmp) return;
        var w = bmp.PixelWidth * SeedMapScale.ScaleX;
        var h = bmp.PixelHeight * SeedMapScale.ScaleY;
        var minX = Math.Min(0, SeedMapHost.ActualWidth - w);
        var maxX = Math.Max(0, SeedMapHost.ActualWidth - w);
        var minY = Math.Min(0, SeedMapHost.ActualHeight - h);
        var maxY = Math.Max(0, SeedMapHost.ActualHeight - h);
        SeedMapTranslate.X = Math.Clamp(SeedMapTranslate.X, minX, maxX);
        SeedMapTranslate.Y = Math.Clamp(SeedMapTranslate.Y, minY, maxY);
    }

    private void SeedMap_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible) return;
        _panning = true;
        _dragStart = e.GetPosition(SeedMapHost);
        _transStart = new System.Windows.Vector(SeedMapTranslate.X, SeedMapTranslate.Y);
        SeedMapHost.CaptureMouse();
    }

    private void SeedMap_MouseMove(object sender, MouseEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible) return;
        if (ChunkAt(e.GetPosition(SeedMapHost)) is var (cx, cz))
        {
            var tpl = (string?)TryFindResource("S.Tool.SeedPos") ?? "区块 ({0}, {1}) · 方块 ({2}, {3})";
            TxtSeedPos.Text = string.Format(tpl, cx, cz, cx * 16, cz * 16);
        }
        else
        {
            TxtSeedPos.Text = "";
        }
        if (!_panning) return;
        var pos = e.GetPosition(SeedMapHost);
        SeedMapTranslate.X = _transStart.X + (pos.X - _dragStart.X);
        SeedMapTranslate.Y = _transStart.Y + (pos.Y - _dragStart.Y);
        ClampTranslate();
    }

    private void SeedMap_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _panning = false;
        SeedMapHost.ReleaseMouseCapture();
    }

    private void SeedMap_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        SeedMapHost.ReleaseMouseCapture();
    }

    private void SeedMap_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible || SeedMapImage.Source is null) return;
        var oldScale = SeedMapScale.ScaleX;
        var newScale = Math.Clamp(oldScale * (e.Delta > 0 ? 1.2 : 1 / 1.2), ZoomMin, ZoomMax);
        if (Math.Abs(newScale - oldScale) < 0.0001) { e.Handled = true; return; }
        // 以光标为锚点缩放：光标下的区块在缩放前后保持不动
        var pos = e.GetPosition(SeedMapHost);
        SeedMapTranslate.X = pos.X - (pos.X - SeedMapTranslate.X) * (newScale / oldScale);
        SeedMapTranslate.Y = pos.Y - (pos.Y - SeedMapTranslate.Y) * (newScale / oldScale);
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = newScale;
        ClampTranslate();
        e.Handled = true;
    }

    private void BtnSeedReset_Click(object sender, RoutedEventArgs e)
    {
        SeedMapScale.ScaleX = SeedMapScale.ScaleY = 1;
        SeedMapTranslate.X = SeedMapTranslate.Y = 0;
    }

    private void BtnSeedBig_Click(object sender, RoutedEventArgs e)
    {
        if (SeedMapHost.Visibility != Visibility.Visible || SeedMapImage.Source is not BitmapSource bmp)
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.SeedFirst") ?? "请先生成种子地图。";
            return;
        }
        try
        {
            Directory.CreateDirectory(HorizonService.DownloadDir);
            var path = Path.Combine(HorizonService.DownloadDir,
                $"HmSeedMap-{_seed}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(path);
            enc.Save(fs);
            var title = (string?)TryFindResource("S.Tool.SlimeTitle") ?? "种子地图";
            MapViewerWindow.Show(Window.GetWindow(this), path, $"{title} · seed={_seed} · ±{_seedRadius} 区块");
        }
        catch (Exception ex)
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.SeedExportFail") ?? "导出失败：" + ex.Message;
        }
    }
}
