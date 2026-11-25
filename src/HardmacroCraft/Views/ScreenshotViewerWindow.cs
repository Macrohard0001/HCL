using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.16.6：截图管理窗口（工具页「截图管理」打开）—— 缩略图墙（按修改时间倒序）+
/// 双击系统查看器打开 / 删除（含确认）/ 打开所在文件夹。
/// 代码构建窗口，与项目惯例一致；缩略图按 DecodePixelWidth 解码，不整图驻留内存。
/// </summary>
public static class ScreenshotViewerWindow
{
    private const int ThumbPx = 160;   // 缩略图解码宽度
    private const int MaxShots = 500;  // 缩略图墙上限（再多只展示最新 500 张）

    public static void Show(Window? owner, List<ScreenshotService.ShotInfo> shots)
    {
        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = App.Current.TryFindResource("S.Tool.ShotsWinTitle") as string ?? "截图管理",
            Width = 920,
            Height = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendsContentIntoTitleBar = true,
        };
        if (owner?.IsLoaded == true) win.Owner = owner; else win.Owner = App.Current.MainWindow;

        // 缩略图墙：WrapPanel 布局的 ListBox，像素滚动（滚轮手感与全局一致）
        var list = new ListBox
        {
            Margin = new Thickness(12),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        ScrollViewer.SetCanContentScroll(list, false); // fix5 同款：像素滚动，保证滚轮手感一致
        var wrapFactory = new FrameworkElementFactory(typeof(WrapPanel));
        list.ItemsPanel = new ItemsPanelTemplate(wrapFactory);

        void Reload(List<ScreenshotService.ShotInfo> all)
        {
            list.Items.Clear();
            foreach (var shot in all.Take(MaxShots))
                list.Items.Add(BuildItem(shot));
        }
        Reload(shots);

        var status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            FontSize = 12,
            Opacity = 0.75,
        };
        void RefreshStatus(int total) =>
            status.Text = string.Format(
                App.Current.TryFindResource("S.Tool.ShotsWinStatus") as string ?? "共 {0} 张（显示最新 {1} 张）",
                total, Math.Min(total, MaxShots));
        RefreshStatus(shots.Count);

        // 工具栏：打开所在文件夹 / 删除所选
        var btnFolder = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Tool.ShotsOpenFolder") as string ?? "打开所在文件夹",
            MinHeight = 30,
            Margin = new Thickness(12, 8, 8, 0),
        };
        btnFolder.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: ScreenshotService.ShotInfo shot })
                ScreenshotService.RevealFolder(shot.Path);
            else if (shots.Count > 0)
                ScreenshotService.RevealFolder(shots[0].Path);
        };

        var btnDelete = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Tool.ShotsDelete") as string ?? "删除所选",
            MinHeight = 30,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            Margin = new Thickness(0, 8, 8, 0),
        };
        btnDelete.Click += (_, _) =>
        {
            if (list.SelectedItem is not ListBoxItem { Tag: ScreenshotService.ShotInfo sel }) return;
            var confirm = App.Current.TryFindResource("S.Tool.ShotsDeleteConfirm") as string ?? "删除这张截图？";
            if (MessageBox.Show(win, confirm + "\n" + sel.Path, win.Title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                ScreenshotService.Delete(sel.Path);
                var remaining = ScreenshotService.ListAll();
                Reload(remaining);
                RefreshStatus(remaining.Count);
            }
            catch (Exception ex)
            {
                MessageBox.Show(win, ex.Message, win.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Children = { btnFolder, btnDelete, status } };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(list, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(list);
        win.Content = grid;
        win.Show();
    }

    /// <summary>单个缩略图条目：图 + 文件名 / 修改时间两行说明，双击系统查看器打开。</summary>
    private static ListBoxItem BuildItem(ScreenshotService.ShotInfo shot)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(shot.Path);
        bmp.DecodePixelWidth = ThumbPx;
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze(); // UI 线程外也可安全持有

        var image = new Image
        {
            Source = bmp,
            Width = ThumbPx,
            Height = ThumbPx,
            Stretch = Stretch.Uniform,
            Margin = new Thickness(4),
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var name = new TextBlock
        {
            Text = Path.GetFileName(shot.Path),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(6, 0, 6, 0),
        };
        var meta = new TextBlock
        {
            Text = shot.Modified.ToString("yyyy-MM-dd HH:mm") + " · " + FormatBytes(shot.SizeBytes),
            FontSize = 11,
            Opacity = 0.65,
            Margin = new Thickness(6, 0, 6, 4),
        };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 8, 8) };
        stack.Children.Add(image);
        stack.Children.Add(name);
        stack.Children.Add(meta);
        return new ListBoxItem { Content = stack, Tag = shot, MaxWidth = ThumbPx + 16 };
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1 << 20 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1 << 10 => $"{bytes / 1024d:0} KB",
        _ => $"{bytes} B",
    };
}
