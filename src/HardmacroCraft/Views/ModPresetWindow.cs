using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 模组预设管理弹窗（v0.1.5，PCL 式一键装机单）：
/// - 列出全部预设（名称 + 条目数 + 保存时间）；
/// - 「安装」：返回所选预设，由 ModsPage 按当前游戏版本 / 加载器自动匹配下载；
/// - 「删除」：就地删除并落盘。
/// 代码构建设计：与 AccountManager / ModVersionWindow 一致，随 Fluent 主题深浅色。
/// </summary>
public static class ModPresetWindow
{
    public static ModPreset? Show(Window? owner, List<ModPreset> presets)
    {
        ModPreset? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = App.Current.TryFindResource("S.Mod.Presets") as string ?? "模组预设",
            Width = 540,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;
        // v0.1.12：Esc 关闭（PCL 同款人性化优化）
        win.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) win.Close(); };

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        var title = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Mod.Presets") as string ?? "模组预设",
            FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var subtitle = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Mod.PresetsSub") as string ?? "",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 14),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(title);
        root.Children.Add(subtitle);

        var list = new StackPanel();
        scroll.Content = list;
        root.Children.Add(scroll);

        void Rebuild()
        {
            list.Children.Clear();
            if (presets.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = App.Current.TryFindResource("S.Mod.PresetEmpty") as string ?? "还没有预设。",
                    FontSize = 13, Margin = new Thickness(0, 10, 0, 10),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                list.Children.Add(empty);
                return;
            }
            foreach (var preset in presets)
                list.Children.Add(BuildRow(win, preset, presets, Rebuild, p => { result = p; win.Close(); }));
        }

        Rebuild();
        Services.FluentDialog.SetContent(win, root);
        win.ShowDialog();
        return result;
    }

    private static UIElement BuildRow(Window win, ModPreset preset, List<ModPreset> presets,
        Action onChanged, Action<ModPreset> onInstall)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0x88, 0x88, 0x88)),
        };
        Grid.SetColumnSpan(card, 3);
        grid.Children.Add(card);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(preset.Name) ? "?" : preset.Name,
            FontSize = 14.5, FontWeight = FontWeights.SemiBold,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var meta = new TextBlock
        {
            Text = string.Format(
                App.Current.TryFindResource("S.Mod.PresetMeta") as string ?? "{0} 个条目 · 保存于 {1:yyyy-MM-dd}",
                preset.Items.Count, preset.CreatedAt.ToLocalTime()),
            FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0),
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        info.Children.Add(name);
        info.Children.Add(meta);
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var btnInstall = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Mod.PresetInstallOne") as string ?? "安装",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 30, Padding = new Thickness(14, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
        };
        btnInstall.Click += (_, _) => onInstall(preset);
        Grid.SetColumn(btnInstall, 1);
        grid.Children.Add(btnInstall);

        var btnDelete = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Acct.Delete") as string ?? "删除",
            MinHeight = 30, Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
        };
        btnDelete.Click += (_, _) =>
        {
            if (MessageBox.Show(win,
                    string.Format(App.Current.TryFindResource("S.Mod.PresetDeleteConfirm") as string
                        ?? "确定删除预设「{0}」吗？", preset.Name),
                    "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            presets.Remove(preset);
            try { ModPresetStore.Save(ModPresetStore.PresetsPath, presets); } catch { }
            onChanged();
        };
        Grid.SetColumn(btnDelete, 2);
        grid.Children.Add(btnDelete);

        return grid;
    }
}
