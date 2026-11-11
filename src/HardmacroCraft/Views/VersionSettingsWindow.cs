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
        // v0.1.9：推荐 Java 提示（当前版本支持使用的最高版本；同时告知最低要求）
        var javaRange = Services.JavaManager.SupportedJavaRange(
            Services.JavaManager.ReadVersionJson(AppSettings.Instance.GameRoot, version), version);
        var chainMin = Services.JavaManager.RequiredJavaMajorChain(AppSettings.Instance.GameRoot, version);
        if (chainMin > javaRange.Min) javaRange.Min = chainMin;
        var recMajor = Math.Max(javaRange.Min, javaRange.Max);
        root.Children.Add(Hint("S.Vs.JavaRec",
            $"推荐 Java {recMajor}（当前版本支持使用的最高版本；最低需要 Java {javaRange.Min}）。"));
        var javaRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        javaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        javaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txtJava = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 36,
            Text = vs.JavaPath,
            PlaceholderText = Find("S.Vs.JavaPh") ?? "留空使用全局设置（支持填 javaw.exe 或其所在文件夹）",
        };
        javaRow.Children.Add(txtJava);
        var btnJavaBrowse = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.JavaBrowse") ?? "浏览",
            MinHeight = 36, MinWidth = 74, Margin = new Thickness(8, 0, 0, 0),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.FolderOpen24 },
            ToolTip = Find("S.Vs.JavaBrowseTip") ?? "选择 javaw.exe（选文件夹会自动搜索）",
        };
        Grid.SetColumn(btnJavaBrowse, 1);
        javaRow.Children.Add(btnJavaBrowse);
        root.Children.Add(javaRow);
        btnJavaBrowse.Click += (_, _) =>
        {
            // v0.1.9（对齐 PCL 版本设置的 Java 选择）：文件 / 文件夹双入口
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Find("S.Vs.JavaBrowseTip") ?? "选择 javaw.exe（选文件夹会自动搜索）",
                Filter = "javaw.exe|javaw.exe|java.exe|java.exe|所有文件|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() == true) txtJava.Text = dlg.FileName;
        };

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

        // ---------- 分组：窗口大小（v0.1.10，对齐 PCL 版本设置） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupWindow", "窗口大小"));
        var winModeCombo = new ComboBox { MinHeight = 36, MinWidth = 240, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        var winModes = new List<(string Label, string Value)>
        {
            (Find("S.Vs.WinFollow") ?? "跟随全局设置", ""),
            (Find("S.Vs.WinDefault") ?? "默认 1280×720", "default"),
            (Find("S.Vs.WinMatch") ?? "与启动器窗口大小一致", "match-launcher"),
            (Find("S.Vs.WinCustom") ?? "自定义宽高", "custom"),
        };
        foreach (var it in winModes) winModeCombo.Items.Add(it.Label);
        winModeCombo.SelectedIndex = winModes.FindIndex(m => m.Value == (vs.WindowMode ?? ""));
        if (winModeCombo.SelectedIndex < 0) winModeCombo.SelectedIndex = 0;
        var winSizeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = vs.WindowMode == "custom" ? Visibility.Visible : Visibility.Collapsed,
        };
        var txtWinW = new Wpf.Ui.Controls.TextBox { MinHeight = 36, Width = 110, Text = vs.WindowWidth > 0 ? vs.WindowWidth.ToString() : "", TextAlignment = TextAlignment.Center };
        var txtWinH = new Wpf.Ui.Controls.TextBox { MinHeight = 36, Width = 110, Text = vs.WindowHeight > 0 ? vs.WindowHeight.ToString() : "", TextAlignment = TextAlignment.Center };
        winSizeRow.Children.Add(txtWinW);
        winSizeRow.Children.Add(new TextBlock { Text = "×", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        winSizeRow.Children.Add(txtWinH);
        winModeCombo.SelectionChanged += (_, _) =>
            winSizeRow.Visibility = winModes[Math.Max(0, winModeCombo.SelectedIndex)].Value == "custom"
                ? Visibility.Visible : Visibility.Collapsed;
        root.Children.Add(winModeCombo);
        root.Children.Add(winSizeRow);
        root.Children.Add(Hint("S.Vs.WinHint", "全屏启动开启时忽略窗口大小设置。"));

        // ---------- 分组：Mod 与内容（v0.1.10，对齐 PCL 版本设置的模组管理入口） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupMods", "Mod 与内容"));
        var modsRow = new StackPanel { Orientation = Orientation.Horizontal };
        var btnModsPage = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.OpenModsPage") ?? "打开 Mod 管理",
            MinHeight = 34, MinWidth = 120, Margin = new Thickness(0, 4, 8, 0),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.PuzzlePiece20 },
        };
        btnModsPage.Click += (_, _) =>
        {
            win.Close();
            if (System.Windows.Application.Current.MainWindow is MainWindow main)
            {
                main.RootNavigation.Navigate(typeof(ModsPage));
                // 导航同步完成后页面实例已就绪，直选该版本
                ModsPage.Current?.FocusTargetVersion(version);
            }
        };
        modsRow.Children.Add(btnModsPage);
        root.Children.Add(modsRow);
        root.Children.Add(Hint("S.Vs.ModsHint",
            "在该版本的 Mod 管理页可搜索 / 启停 / 删除 Mod，目标版本已自动选中；也可用下方目录入口直接打开 Mod 文件夹。"));

        // ---------- 分组：目录快捷入口（v0.1.9，对齐 PCL 版本设置的「打开文件夹」组） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupDirs", "目录"));
        var dirsRow = new StackPanel { Orientation = Orientation.Horizontal };
        void AddDirBtn(string key, string fallback, Action action)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = Find(key) ?? fallback,
                MinHeight = 34, MinWidth = 96, Margin = new Thickness(0, 4, 8, 0),
            };
            b.Click += (_, _) => { try { action(); } catch { } };
            dirsRow.Children.Add(b);
        }
        var gameRootDir = AppSettings.Instance.GameRoot;
        var verDir = System.IO.Path.Combine(gameRootDir, "versions", version);
        var isoOn = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation);
        var baseDir = isoOn ? verDir : gameRootDir;
        AddDirBtn("S.Vs.OpenVersionDir", "打开版本目录", () => OpenDir(verDir));
        AddDirBtn("S.Vs.OpenSavesDir", "打开存档", () => OpenDir(System.IO.Path.Combine(baseDir, "saves")));
        AddDirBtn("S.Vs.OpenModsDir", "打开 Mod 目录", () => OpenDir(System.IO.Path.Combine(baseDir, "mods")));
        AddDirBtn("S.Vs.OpenPacksDir", "打开资源包", () => OpenDir(System.IO.Path.Combine(baseDir, "resourcepacks")));
        AddDirBtn("S.Vs.OpenLogsDir", "打开日志", () => OpenDir(System.IO.Path.Combine(gameRootDir, "logs")));
        root.Children.Add(dirsRow);
        root.Children.Add(Hint("S.Vs.DirsHint",
            isoOn ? "该版本已开启独立隔离：存档 / Mod 在版本目录内。"
                  : "该版本与其他版本共用目录（可在上方「游戏」隔离项中改为独立）。"));

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
                // v0.1.10：窗口大小覆盖
                WindowMode = winModes[Math.Max(0, winModeCombo.SelectedIndex)].Value,
                WindowWidth = int.TryParse(txtWinW.Text.Trim(), out var ww) && ww > 0 ? ww : 0,
                WindowHeight = int.TryParse(txtWinH.Text.Trim(), out var wh) && wh > 0 ? wh : 0,
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

    /// <summary>v0.1.9：目录快捷入口共用（不存在时创建并打开）。</summary>
    private static void OpenDir(string dir)
    {
        try
        {
            System.IO.Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch { /* 用户取消 / 无权限时静默 */ }
    }

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
