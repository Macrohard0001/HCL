using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.0 PCL 式翻页条构建器：把 PclPager.BuildButtons 的按钮序列渲染成
/// 一行水平按钮（首页 « / 上一页 ‹ / 数字页码 / 下一页 › / 末页 » / 省略号），
/// 各下载页与搜索选择弹窗共用，保证翻页体验一致。
/// </summary>
public static class PagerBar
{
    /// <summary>把翻页条按钮渲染进 host（host.Children.Clear 后重建）。</summary>
    public static void Build(StackPanel host, System.Collections.Generic.IReadOnlyList<Services.PclPager.PagerButton> buttons, Action<int> go)
    {
        host.Children.Clear();
        host.Visibility = buttons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var b in buttons)
        {
            if (b.IsEllipsis)
            {
                var dots = new TextBlock
                {
                    Text = "…",
                    Margin = new Thickness(3, 0, 3, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                };
                dots.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
                host.Children.Add(dots);
                continue;
            }

            var btn = new Wpf.Ui.Controls.Button
            {
                Content = b.Label,
                MinHeight = 28,
                Padding = new Thickness(b.IsNav ? 8 : 10, 0, b.IsNav ? 8 : 10, 0),
                Margin = new Thickness(3, 0, 3, 0),
                IsEnabled = b.IsEnabled,
                FontSize = 12.5,
            };
            if (b.IsCurrent) btn.Appearance = Wpf.Ui.Controls.ControlAppearance.Primary;
            if (b.IsNav) ToolTipService.SetToolTip(btn, b.Label switch
            {
                "«" => ModsPage.S("S.Pg.First"),
                "‹" => ModsPage.S("S.Pg.Prev"),
                "›" => ModsPage.S("S.Pg.Next"),
                "»" => ModsPage.S("S.Pg.Last"),
                _ => b.Label,
            });
            if (b.IsEnabled && b.TargetPage is int p) btn.Click += (_, _) => go(p);
            host.Children.Add(btn);
        }
    }
}
