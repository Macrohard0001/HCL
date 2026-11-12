using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// Mod 版本选择对话框（PCL 式）：列出某项目的全部可用版本，
/// 徽章行展示发布类型 / 游戏版本 / 加载器 / 日期 / 大小，双击或点"下载"选定。
/// 返回选中的 ModVersionRow，取消返回 null。下载本身由 ModsPage 负责（统一进度与日志）。
/// </summary>
public static class ModVersionWindow
{
    public static ModVersionRow? Show(Window? owner, string projectTitle, IReadOnlyList<ModVersionRow> rows)
    {
        ModVersionRow? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = string.Format(
                App.Current.TryFindResource("S.ModPicker.Title") as string ?? "选择版本 - {0}", projectTitle),
            Width = 620,
            Height = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            MinWidth = 520,
            MinHeight = 380,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new Grid { Margin = new Thickness(20, 16, 20, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var hint = new TextBlock
        {
            Text = App.Current.TryFindResource("S.ModPicker.Hint") as string ?? "选择要下载的版本：",
            FontSize = 12.5, Margin = new Thickness(0, 0, 0, 10),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(hint);

        var list = new ListBox
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        System.Windows.Controls.ScrollViewer.SetCanContentScroll(list, false); // fix5：像素滚动，保证滚轮手感一致
        // 直接构建行 UI（列表一般 ≤ 100 行，无需 DataTemplate）
        foreach (var row in rows)
            list.Items.Add(BuildItem(row));
        root.Children.Add(list);
        Grid.SetRow(list, 1);

        var btnPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.ModPicker.Cancel") as string ?? "取消",
            MinHeight = 36, MinWidth = 90, Margin = new Thickness(0, 0, 10, 0),
        };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.ModPicker.Download") as string ?? "下载",
            MinHeight = 36, MinWidth = 120,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
        };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnOk);
        root.Children.Add(btnPanel);
        Grid.SetRow(btnPanel, 2);

        btnOk.Click += (_, _) =>
        {
            if (list.SelectedItem is ListBoxItem { Tag: ModVersionRow r }) result = r;
            win.DialogResult = true;
            win.Close();
        };
        btnCancel.Click += (_, _) => { win.DialogResult = false; win.Close(); };
        list.MouseDoubleClick += (_, _) => btnOk.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) btnOk.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            else if (e.Key == Key.Escape) { win.DialogResult = false; win.Close(); }
        };

        Services.FluentDialog.SetContent(win, root); // fix5：挂标题栏（含关闭按钮）
        win.Loaded += (_, _) => { if (list.Items.Count > 0) list.SelectedIndex = 0; };
        win.ShowDialog();
        return result;
    }

    private static ListBoxItem BuildItem(ModVersionRow row)
    {
        var grid = new Grid { Margin = new Thickness(8, 6, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var (bg, fg) = ReleaseBrushes(row.ReleaseType);
        var badge = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = bg,
            Child = new TextBlock
            {
                Text = row.ReleaseType,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
            },
        };
        Grid.SetColumn(badge, 0);

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = row.Title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)App.Current.TryFindResource("TextFillColorPrimaryBrush") ?? Brushes.White,
        });
        stack.Children.Add(new TextBlock
        {
            Text = row.BadgeText,
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)App.Current.TryFindResource("TextFillColorSecondaryBrush") ?? Brushes.Gray,
        });
        Grid.SetColumn(stack, 1);

        grid.Children.Add(badge);
        grid.Children.Add(stack);
        return new ListBoxItem { Tag = row, Content = grid };
    }

    /// <summary>发布类型徽章配色：release 绿 / beta 黄 / alpha 红。</summary>
    private static (Brush Background, Brush Foreground) ReleaseBrushes(string releaseType)
    {
        var bg = releaseType switch
        {
            "beta" => App.Current.TryFindResource("SystemFillColorCautionBrush") as Brush,
            "alpha" => App.Current.TryFindResource("SystemFillColorCriticalBrush") as Brush,
            _ => App.Current.TryFindResource("SystemFillColorSuccessBrush") as Brush,
        } ?? new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32));
        return (bg, Brushes.White);
    }
}
