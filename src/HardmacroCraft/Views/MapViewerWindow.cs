using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.16.5：世界地图查看窗口（工具页「世界地图」生成后弹出）——
/// 图片 + 滚轮缩放（1×–16×）+ 打开 PNG 所在文件夹。代码构建窗口，与项目惯例一致。
/// </summary>
public static class MapViewerWindow
{
    public static void Show(Window? owner, string pngPath, string info)
    {
        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = (App.Current.TryFindResource("S.Tool.MapWinTitle") as string ?? "世界地图") + " · " +
                    System.IO.Path.GetFileNameWithoutExtension(pngPath),
            Width = 900,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
        };
        if (owner?.IsLoaded == true) win.Owner = owner; else win.Owner = App.Current.MainWindow;

        var img = new Image
        {
            Source = BitmapFrame.Create(new Uri(pngPath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad),
            RenderTransformOrigin = new Point(0, 0),
        };
        var scale = new ScaleTransform(1, 1);
        img.RenderTransform = scale;

        var scroll = new ScrollViewer
        {
            Content = img,
            Background = new SolidColorBrush(Color.FromRgb(24, 24, 24)),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        scroll.PreviewMouseWheel += (_, e) =>
        {
            var factor = e.Delta > 0 ? 1.25 : 0.8;
            var next = Math.Clamp(scale.ScaleX * factor, 1, 16);
            if (Math.Abs(next - scale.ScaleX) > 0.001)
            {
                scale.ScaleX = next;
                scale.ScaleY = next;
                e.Handled = true;
            }
        };

        var zoomLabel = new TextBlock { Text = "100%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var slider = new Slider { Minimum = 1, Maximum = 16, Value = 1, Width = 140, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, _) =>
        {
            scale.ScaleX = slider.Value;
            scale.ScaleY = slider.Value;
            zoomLabel.Text = $"{slider.Value * 100:0}%";
        };
        var btnFolder = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Tool.MapOpenFolder") as string ?? "打开 PNG 所在文件夹",
            MinHeight = 30,
            Margin = new Thickness(8, 0, 0, 0),
        };
        btnFolder.Click += (_, _) => HorizonService.RevealInExplorer(pngPath);

        var bottom = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { new TextBlock { Text = info, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.75 },
                             slider, zoomLabel, btnFolder },
            },
        };
        Grid.SetRow(bottom, 1);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(scroll);
        grid.Children.Add(bottom);
        win.Content = grid;
        win.Show();
    }
}
