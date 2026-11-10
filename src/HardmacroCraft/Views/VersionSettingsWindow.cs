using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.6 版本独立设置窗口（对齐主流启动器的「版本设置」）：
/// 版本隔离覆盖 / 版本专用 Java / 内存覆盖 / JVM 参数 / 固定进服地址。
/// 未覆盖的项显示为「跟随全局设置」，保存时留空项回落 null / 空串。
/// </summary>
public static class VersionSettingsWindow
{
    public static void Show(Window? owner, string version)
    {
        var vs = VersionSettingsStore.Get(version) ?? new VersionSettings();

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = (Find("S.Vs.Title") ?? "版本设置") + " · " + version,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>()
            .LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(24, 16, 24, 20) };

        // ---------- 分组：游戏 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupGame", "游戏"));
        var isoCombo = new ComboBox { MinHeight = 36, Margin = new Thickness(0, 4, 0, 0) };
        var isoItems = new List<(string Label, bool? Value)>
        {
            (Find("S.Vs.IsoFollow") ?? "跟随全局设置", null),
            (Find("S.Vs.IsoOn") ?? "独立（存档 / mod 放在该版本目录内）", true),
            (Find("S.Vs.IsoOff") ?? "共用（与其他版本共用存档 / mod）", false),
        };
        foreach (var it in isoItems) isoCombo.Items.Add(it.Label);
        isoCombo.SelectedIndex = vs.IsolationOverride == null ? 0
            : vs.IsolationOverride == true ? 1 : 2;
        root.Children.Add(isoCombo);
        root.Children.Add(Hint("S.Vs.IsoHint", "覆盖设置里的「版本隔离」。全局开关在 设置 → 游戏目录 中修改。"));

        // ---------- 分组：Java ----------
        root.Children.Add(GroupTitle("S.Vs.GroupJava", "Java"));
        var txtJava = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 36, Margin = new Thickness(0, 4, 0, 0),
            Text = vs.JavaPath,
            PlaceholderText = Find("S.Vs.JavaPh") ?? "留空使用全局设置（支持填 javaw.exe 或其所在文件夹）",
        };
        root.Children.Add(txtJava);

        // ---------- 分组：内存 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupMemory", "内存"));
        var chkMemory = new CheckBox
        {
            Content = MakeText(Find("S.Vs.MemoryOverride") ?? "为此版本单独分配内存", 13, Primary(), bottom: 6),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var memRow = new Grid { Visibility = Visibility.Collapsed };
        memRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        memRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sliderMem = new Slider
        {
            Minimum = 1024, Maximum = 32768, SmallChange = 512, LargeChange = 2048,
            TickFrequency = 512, IsSnapToTickEnabled = true,
            Value = vs.MemoryMb > 0 ? vs.MemoryMb : 4096,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var lblMem = new TextBlock
        {
            FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0), MinWidth = 74, TextAlignment = TextAlignment.Right,
        };
        Grid.SetColumn(lblMem, 1);
        memRow.Children.Add(sliderMem);
        memRow.Children.Add(lblMem);
        void UpdateMemLabel() => lblMem.Text = string.Format(Find("S.Vs.MemoryValue") ?? "{0} MB", (int)sliderMem.Value);
        UpdateMemLabel();
        sliderMem.ValueChanged += (_, _) => UpdateMemLabel();
        void UpdateMemRow()
        {
            memRow.Visibility = chkMemory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            sliderMem.IsEnabled = chkMemory.IsChecked == true;
        }
        chkMemory.Checked += (_, _) => UpdateMemRow();
        chkMemory.Unchecked += (_, _) => UpdateMemRow();
        if (vs.MemoryAuto == false)
        {
            // 仅「手动指定」时勾选；MemoryAuto==true（跟随全局自动检测的临时语义）不作勾选展示
            chkMemory.IsChecked = true;
            sliderMem.Value = vs.MemoryMb > 0 ? vs.MemoryMb : 4096;
        }
        root.Children.Add(chkMemory);
        root.Children.Add(memRow);
        root.Children.Add(Hint("S.Vs.MemoryHint", "勾选后：滑块数值即该版本固定分配；取消勾选：跟随全局（含全局的自动检测）。"));

        // ---------- 分组：JVM 参数 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupJvm", "JVM 参数"));
        var txtJvm = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 68, Margin = new Thickness(0, 4, 0, 0),
            Text = vs.CustomJvmArgs, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            PlaceholderText = Find("S.Vs.JvmPh") ?? "留空使用全局设置，如 -XX:+UseZGC",
            FontFamily = new FontFamily("Consolas"),
        };
        root.Children.Add(txtJvm);

        // ---------- 分组：进服 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupServer", "一键进服"));
        var txtServer = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 36, Margin = new Thickness(0, 4, 0, 0),
            Text = vs.QuickServer,
            PlaceholderText = Find("S.Vs.ServerPh") ?? "该版本默认进服地址（一键进服弹窗自动填充），留空不预填",
        };
        root.Children.Add(txtServer);

        // ---------- 底部按钮 ----------
        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var btnReset = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.Reset") ?? "恢复默认",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            MinHeight = 36, MinWidth = 104, Margin = new Thickness(0, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var spacer = new Border { Width = 0 };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Common.Cancel") ?? "取消",
            MinHeight = 36, MinWidth = 96, Margin = new Thickness(0, 0, 10, 0),
        };
        var btnSave = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.Save") ?? "保存",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 36, MinWidth = 120,
        };
        btnPanel.Children.Add(btnCancel);
        btnPanel.Children.Add(btnSave);
        btnRow.Children.Add(btnReset);
        btnRow.Children.Add(spacer);
        btnRow.Children.Add(btnPanel);

        btnSave.Click += (_, _) =>
        {
            // 内存覆盖语义：勾选 = 手动指定该版本内存（MemoryAuto=false, MemoryMb=滑块值）；
            // 未勾选 = 跟随全局（MemoryAuto=null）。滑块仅在勾选时生效。
            var s = new VersionSettings
            {
                IsolationOverride = isoCombo.SelectedIndex switch { 1 => true, 2 => false, _ => (bool?)null },
                JavaPath = txtJava.Text.Trim(),
                CustomJvmArgs = txtJvm.Text.Trim(),
                QuickServer = txtServer.Text.Trim(),
            };
            if (chkMemory.IsChecked == true)
            {
                s.MemoryAuto = false;
                s.MemoryMb = (int)sliderMem.Value;
            }
            VersionSettingsStore.Save(version, s);
            win.Close();
        };
        btnReset.Click += (_, _) =>
        {
            VersionSettingsStore.Delete(version);
            win.Close();
        };
        btnCancel.Click += (_, _) => win.Close();
        root.Children.Add(btnRow);

        FluentDialog.SetContent(win, root);
        win.ShowDialog();
    }

    private static string? Find(string key)
        => System.Windows.Application.Current.TryFindResource(key) as string;

    private static Brush Primary() => (Brush)System.Windows.Application.Current.Resources["TextFillColorPrimaryBrush"];

    private static TextBlock MakeText(string text, double size, Brush brush, double bottom = 0)
        => new() { Text = text, FontSize = size, Foreground = brush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, bottom) };

    private static TextBlock GroupTitle(string key, string fallback) => new()
    {
        Text = Find(key) ?? fallback,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Foreground = Primary(),
        Margin = new Thickness(0, 16, 0, 4),
    };

    private static TextBlock Hint(string key, string fallback) => new()
    {
        Text = Find(key) ?? fallback,
        FontSize = 11,
        Foreground = (Brush)System.Windows.Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
    };
}
