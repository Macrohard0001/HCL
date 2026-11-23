using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.10：种子工具页 —— 史莱姆挂机点查询（算法见 SlimeChunkFinder）。
/// 网格渲染：每区块一格，绿=史莱姆区块；统计占比 + 3×3 / 2×2 全绿连片中心推荐。
/// </summary>
public partial class ToolsPage : Page
{
    private const int CellPx = 6;
    private const uint SlimeColor = 0xFF3CA84C;   // BGRA：史莱姆绿
    private const uint BaseColor = 0xFF2A2A2A;    // BGRA：底格深灰

    public ToolsPage()
    {
        InitializeComponent();
    }

    private void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        // 种子：接受任意 64 位整数（含负数；MC /seed 输出的形式）
        if (!long.TryParse(TxtSeed.Text.Trim(), NumberFormatInfo.InvariantInfo, out var seed))
        {
            TxtStatus.Text = (string?)TryFindResource("S.Tool.InvalidSeed") ?? "种子无效";
            MapImage.Visibility = Visibility.Collapsed;
            return;
        }
        if (!int.TryParse(TxtRange.Text.Trim(), out var radius) || radius is < 5 or > 64)
            radius = 40; // 缺省半径 40 区块（±320 格），够挑挂机塔位置

        var size = radius * 2 + 1;
        var map = SlimeChunkFinder.Map(seed, -radius, radius, -radius, radius, out var slimeCount);

        // WriteableBitmap 逐格渲染（BGRA32）
        var bmp = new WriteableBitmap(size * CellPx, size * CellPx, 96, 96, PixelFormats.Bgra32, null);
        var stride = size * CellPx * 4;
        var pixels = new uint[size * CellPx * size * CellPx];
        for (var cz = 0; cz < size; cz++)
            for (var cx = 0; cx < size; cx++)
            {
                var color = map[cz, cx] ? SlimeColor : BaseColor;
                for (var py = 0; py < CellPx; py++)
                {
                    var rowBase = ((cz * CellPx) + py) * stride / 4 + cx * CellPx;
                    for (var px = 0; px < CellPx; px++)
                        pixels[rowBase + px] = color;
                }
            }
        bmp.WritePixels(new Int32Rect(0, 0, size * CellPx, size * CellPx), pixels, stride, 0);
        MapImage.Source = bmp;
        MapImage.Visibility = Visibility.Visible;

        var total = size * size;
        var statsTemplate = (string?)TryFindResource("S.Tool.Stats") ?? "史莱姆区块 {0} 个 / 共 {1} 个（{2}%）";
        var stats = string.Format(statsTemplate, slimeCount, total,
            (100.0 * slimeCount / total).ToString("0.0", CultureInfo.CurrentCulture));

        var spot = SlimeChunkFinder.BestFarmSpot(map, -radius, -radius);
        if (spot is var (centerX, centerZ, spotSize))
        {
            // 区块 → 方块坐标（区块左上角 16×16 范围）
            var bestTemplate = (string?)TryFindResource("S.Tool.Best") ??
                "最佳挂机点：区块 ({0}, {1})，{2}×{2} 全绿连片，方块坐标 ({3}, {4})——挂机层 Y<40 有效";
            TxtStatus.Text = stats + "\n" + string.Format(bestTemplate,
                centerX, centerZ, spotSize, centerX * 16, centerZ * 16);
        }
        else
        {
            TxtStatus.Text = stats + "\n" + ((string?)TryFindResource("S.Tool.BestNone")
                ?? "范围内没有 2×2 连片的史莱姆区块（可扩大半径再试）");
        }
    }
}
