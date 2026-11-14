using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 版本独立设置窗口（对齐主流启动器的「版本设置」）：
/// 版本隔离覆盖 / 版本专用 Java / 内存覆盖 / JVM 参数 / 窗口大小 / Mod 管理 /
/// 一键进服 / 目录快捷入口。
/// v0.1.11：
/// - 主容器滚动：窗口改为可调高度 + ScrollViewer（设置项多时不再撑出屏幕外）；
/// - 按钮行全部改为 WrapPanel（窄窗口自动换行，不再水平溢出）；
/// - Mod 管理自「Mod 资源」页迁移至此（PCL 式版本内 Mod 管理：列表 / 搜索 /
///   双击启停 / 批量删除 / 目录直达，类型可切 Mod / 资源包 / 光影 / 数据包）。
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
            Width = 620,
            Height = 680,
            MinHeight = 480,
            MinWidth = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>()
            .LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        // ---------- 主容器（ScrollViewer）：设置项超出一屏时在窗口内滚动 ----------
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0),
        };
        var root = new StackPanel { Margin = new Thickness(24, 12, 24, 16) };
        scroll.Content = root;
        // 滚轮平滑滚动（跟随全局开关）
        scroll.PreviewMouseWheel += (_, e) =>
        {
            if (!AppSettings.Instance.SmoothScrollLogs) return;
            if (e.Delta == 0) return;
            SmoothScroll.ScrollTo(scroll, scroll.VerticalOffset - e.Delta);
            e.Handled = true;
        };

        // ---------- 分组：版本（v0.1.14：自定义图标 + 重命名 / 复制 / 删除，自版本管理页收纳至此） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupVersion", "版本"));
        var verOps = new WrapPanel { Orientation = Orientation.Horizontal };
        var iconPreviewBorder = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8),
            Background = (Brush)System.Windows.Application.Current.Resources["SubtleFillColorTertiaryBrush"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 4),
        };
        var iconPreview = new System.Windows.Controls.Image
        {
            Width = 30, Height = 30,
            Stretch = Stretch.Uniform,
        };
        RenderOptions.SetBitmapScalingMode(iconPreview, BitmapScalingMode.NearestNeighbor);
        UpdateIconPreview();
        iconPreviewBorder.Child = iconPreview;
        verOps.Children.Add(iconPreviewBorder);
        void UpdateIconPreview()
        {
            var path = VersionIconProvider.CustomIconPath(version);
            iconPreview.Source = path != null ? VersionIconProvider.LoadFile(path) : null;
            iconPreview.Visibility = path != null ? Visibility.Visible : Visibility.Collapsed;
            iconPreviewBorder.ToolTip = path ?? Find("S.Vs.IconNone") ?? "未设置自定义图标（使用加载器 / 草方块自动图标）";
        }
        var btnIconPick = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.IconPick") ?? "自定义图标",
            MinHeight = 30, MinWidth = 104, Margin = new Thickness(0, 4, 8, 4),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Image24 },
        };
        btnIconPick.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Find("S.Vs.IconPick") ?? "自定义图标",
                Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico",
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                Directory.CreateDirectory(VersionIconProvider.IconDir);
                var ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                if (!new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" }.Contains(ext)) ext = ".png";
                // 先清掉旧扩展名的图标文件，再拷贝新图标（避免旧 png 残留遮蔽新 jpg）
                foreach (var e2 in new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" })
                {
                    var old = Path.Combine(VersionIconProvider.IconDir, version + e2);
                    if (File.Exists(old)) File.Delete(old);
                }
                File.Copy(dlg.FileName, Path.Combine(VersionIconProvider.IconDir, version + ext), overwrite: true);
                UpdateIconPreview();
            }
            catch (Exception ex)
            {
                HmMsg.Error(win, Find("S.Vs.IconFail") ?? "图标设置失败", ex.Message);
            }
        };
        verOps.Children.Add(btnIconPick);
        var btnIconClear = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.IconClear") ?? "恢复默认图标",
            MinHeight = 30, MinWidth = 116, Margin = new Thickness(0, 4, 8, 4),
        };
        btnIconClear.Click += (_, _) =>
        {
            try
            {
                foreach (var e2 in new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico" })
                {
                    var old = Path.Combine(VersionIconProvider.IconDir, version + e2);
                    if (File.Exists(old)) File.Delete(old);
                }
            }
            catch { }
            UpdateIconPreview();
        };
        verOps.Children.Add(btnIconClear);
        root.Children.Add(verOps);
        root.Children.Add(Hint("S.Vs.IconHint",
            "自定义图标显示在版本列表 / 首页下拉框 / 最近启动（优先于加载器自动图标），保存在 config/version-icons/。"));

        var actRow = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        void AddOp(string key, string fallback, Wpf.Ui.Controls.ControlAppearance app, Action action)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = Find(key) ?? fallback,
                Appearance = app,
                MinHeight = 30, MinWidth = 96, Margin = new Thickness(0, 4, 8, 4),
            };
            b.Click += (_, _) => { try { action(); } catch (Exception ex) { HmMsg.Error(win, fallback, ex.Message); } };
            actRow.Children.Add(b);
        }
        AddOp("S.Ver.Rename", "重命名版本", Wpf.Ui.Controls.ControlAppearance.Secondary, () =>
        {
            var newName = Prompt.Show("新版本名（存档 / mods / 资源包目录将一并改名）：", "重命名版本");
            if (string.IsNullOrWhiteSpace(newName)) return;
            App.LauncherService.RenameVersion(version, newName.Trim());
            win.Close();
        });
        AddOp("S.Ver.Copy", "复制版本", Wpf.Ui.Controls.ControlAppearance.Secondary, () =>
        {
            var newName = Prompt.Show("新版本名（将完整复制该版本的文件与隔离内容）：", "复制版本");
            if (string.IsNullOrWhiteSpace(newName)) return;
            App.LauncherService.CopyVersion(version, newName.Trim());
        });
        AddOp("S.Ver.Delete", "删除版本", Wpf.Ui.Controls.ControlAppearance.Danger, () =>
        {
            if (!HmMsg.Confirm(win, Find("S.Ver.Delete") ?? "删除版本",
                    $"确定删除版本 [{version}] 吗？\n（版本隔离开启时，其存档 / mod 一并删除）")) return;
            App.LauncherService.DeleteVersion(version);
            VersionSettingsStore.Delete(version);
            win.Close();
        });
        root.Children.Add(actRow);

        // ---------- 分组：游戏 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupGame", "游戏"));
        var isoCombo = new ComboBox { MinHeight = 32, Margin = new Thickness(0, 4, 0, 0), MinWidth = 320, HorizontalAlignment = HorizontalAlignment.Left };
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
            MinHeight = 32,
            Text = vs.JavaPath,
            PlaceholderText = Find("S.Vs.JavaPh") ?? "留空使用全局设置（支持填 javaw.exe 或其所在文件夹）",
        };
        javaRow.Children.Add(txtJava);
        var btnJavaBrowse = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.JavaBrowse") ?? "浏览",
            MinHeight = 32, MinWidth = 74, Margin = new Thickness(8, 0, 0, 0),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.FolderOpen24 },
            ToolTip = Find("S.Vs.JavaBrowseTip") ?? "选择 javaw.exe（选文件夹会自动搜索）",
        };
        Grid.SetColumn(btnJavaBrowse, 1);
        javaRow.Children.Add(btnJavaBrowse);
        root.Children.Add(javaRow);
        btnJavaBrowse.Click += (_, _) =>
        {
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
            chkMemory.IsChecked = true;
            sliderMem.Value = vs.MemoryMb > 0 ? vs.MemoryMb : 4096;
        }
        root.Children.Add(chkMemory);
        root.Children.Add(memRow);
        root.Children.Add(Hint("S.Vs.MemoryHint", "勾选后：滑块数值即该版本固定分配；取消勾选：跟随全局（含全局的自动检测）。"));

        // ---------- 分组：窗口大小（v0.1.10，对齐主流启动器） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupWindow", "窗口大小"));
        var winModeCombo = new ComboBox { MinHeight = 32, MinWidth = 240, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
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
        var txtWinW = new Wpf.Ui.Controls.TextBox { MinHeight = 32, Width = 110, Text = vs.WindowWidth > 0 ? vs.WindowWidth.ToString() : "", TextAlignment = TextAlignment.Center };
        var txtWinH = new Wpf.Ui.Controls.TextBox { MinHeight = 32, Width = 110, Text = vs.WindowHeight > 0 ? vs.WindowHeight.ToString() : "", TextAlignment = TextAlignment.Center };
        winSizeRow.Children.Add(txtWinW);
        winSizeRow.Children.Add(new TextBlock { Text = "×", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) });
        winSizeRow.Children.Add(txtWinH);
        winModeCombo.SelectionChanged += (_, _) =>
            winSizeRow.Visibility = winModes[Math.Max(0, winModeCombo.SelectedIndex)].Value == "custom"
                ? Visibility.Visible : Visibility.Collapsed;
        root.Children.Add(winModeCombo);
        root.Children.Add(winSizeRow);
        root.Children.Add(Hint("S.Vs.WinHint", "全屏启动开启时忽略窗口大小设置。"));

        // ---------- 分组：JVM 参数 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupJvm", "JVM 参数"));
        var txtJvm = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 60, Margin = new Thickness(0, 4, 0, 0),
            Text = vs.CustomJvmArgs, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            PlaceholderText = Find("S.Vs.JvmPh") ?? "留空使用全局设置，如 -XX:+UseZGC",
            FontFamily = new FontFamily("Consolas"),
        };
        root.Children.Add(txtJvm);

        // ---------- 分组：一键进服 ----------
        root.Children.Add(GroupTitle("S.Vs.GroupServer", "一键进服"));
        var txtServer = new Wpf.Ui.Controls.TextBox
        {
            MinHeight = 32, Margin = new Thickness(0, 4, 0, 0),
            Text = vs.QuickServer,
            PlaceholderText = Find("S.Vs.ServerPh") ?? "该版本默认进服地址（一键进服弹窗自动填充），留空不预填",
        };
        root.Children.Add(txtServer);

        // ---------- 分组：Mod 管理（v0.1.11 自「Mod 资源」页迁移至此） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupMods", "Mod 管理"));
        root.Children.Add(Hint("S.Vs.ModsHint",
            "该版本的 Mod / 资源包 / 光影 / 数据包管理与目录直达（目录随隔离状态变化）；下载新 Mod 仍可在「Mod 资源」页完成。"));
        root.Children.Add(BuildModsPanel(version, win));

        // ---------- 分组：目录快捷入口（v0.1.9，对齐主流启动器的「打开文件夹」组） ----------
        root.Children.Add(GroupTitle("S.Vs.GroupDirs", "目录"));
        var dirsRow = new WrapPanel { Orientation = Orientation.Horizontal };
        void AddDirBtn(string key, string fallback, Action action)
        {
            var b = new Wpf.Ui.Controls.Button
            {
                Content = Find(key) ?? fallback,
                MinHeight = 30, MinWidth = 96, Margin = new Thickness(0, 4, 8, 4),
            };
            b.Click += (_, _) => { try { action(); } catch { } };
            dirsRow.Children.Add(b);
        }
        var gameRootDir = AppSettings.Instance.GameRoot;
        var verDir = Path.Combine(gameRootDir, "versions", version);
        var isoOn = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation);
        var baseDir = isoOn ? verDir : gameRootDir;
        AddDirBtn("S.Vs.OpenVersionDir", "打开版本目录", () => OpenDir(verDir));
        AddDirBtn("S.Vs.OpenSavesDir", "打开存档", () => OpenDir(Path.Combine(baseDir, "saves")));
        AddDirBtn("S.Vs.OpenModsDir", "打开 Mod 目录", () => OpenDir(Path.Combine(baseDir, "mods")));
        AddDirBtn("S.Vs.OpenPacksDir", "打开资源包", () => OpenDir(Path.Combine(baseDir, "resourcepacks")));
        AddDirBtn("S.Vs.OpenLogsDir", "打开日志", () => OpenDir(Path.Combine(gameRootDir, "logs")));
        root.Children.Add(dirsRow);
        root.Children.Add(Hint("S.Vs.DirsHint",
            isoOn ? "该版本已开启独立隔离：存档 / Mod 在版本目录内。"
                  : "该版本与其他版本共用目录（可在上方「游戏」隔离项中改为独立）。"));

        // ---------- 底部按钮（WrapPanel：窄窗口自动换行防溢出） ----------
        var btnRow = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0),
        };
        var btnReset = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.Reset") ?? "恢复默认",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            MinHeight = 32, MinWidth = 104, Margin = new Thickness(0, 4, 10, 4),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Common.Cancel") ?? "取消",
            MinHeight = 32, MinWidth = 96, Margin = new Thickness(0, 4, 10, 4),
        };
        var btnSave = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Vs.Save") ?? "保存",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 32, MinWidth = 120, Margin = new Thickness(0, 4, 0, 4),
        };
        btnRow.Children.Add(btnReset);
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnSave);

        btnSave.Click += (_, _) =>
        {
            var s = new VersionSettings
            {
                IsolationOverride = isoCombo.SelectedIndex switch { 1 => true, 2 => false, _ => (bool?)null },
                JavaPath = txtJava.Text.Trim(),
                CustomJvmArgs = txtJvm.Text.Trim(),
                QuickServer = txtServer.Text.Trim(),
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

        FluentDialog.SetContent(win, scroll);
        win.ShowDialog();
    }

    // ---------- v0.1.11：内嵌 Mod 管理面板（自主页「Mod 资源」页迁移） ----------

    /// <summary>
    /// 构建该版本的 Mod 管理面板：类型切换（Mod / 资源包 / 光影 / 数据包）+ 搜索 +
    /// 元数据列表（名字 / 版本 / 加载器 / 启用状态）+ 启停 / 删除 / 打开目录 / 刷新。
    /// 目录位置严格按「版本隔离覆盖 &gt; 全局隔离」生效值（与游戏运行时一致）。
    /// </summary>
    private static UIElement BuildModsPanel(string version, Window win)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        var iso = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation);
        string DirFor(string type) => ModTargetDir.Resolve(AppSettings.Instance.GameRoot, version, type, iso);

        // 类型 + 搜索 + 刷新 行（WrapPanel 防溢出）
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        var cmbType = new ComboBox { MinHeight = 32, MinWidth = 110, Margin = new Thickness(0, 2, 8, 2) };
        var types = new List<(string Label, string Value)>
        {
            (Find("S.Mod.TypeMod") ?? "Mod", "mod"),
            (Find("S.Mod.TypeResource") ?? "资源包", "resourcepack"),
            (Find("S.Mod.TypeShader") ?? "光影", "shader"),
            (Find("S.Mod.TypeData") ?? "数据包", "datapack"),
        };
        foreach (var t in types) cmbType.Items.Add(t.Label);
        cmbType.SelectedIndex = 0;
        toolbar.Children.Add(cmbType);
        var txtQuery = new Wpf.Ui.Controls.TextBox { MinHeight = 32, MinWidth = 150, Margin = new Thickness(0, 2, 8, 2) };
        txtQuery.SetResourceReference(Wpf.Ui.Controls.TextBox.PlaceholderTextProperty,
            System.Windows.Application.Current.TryFindResource("S.Mod.LocalSearch") is string ph ? ph : "搜索本机 Mod…");
        toolbar.Children.Add(txtQuery);
        var btnOpen = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Mod.OpenDir") ?? "打开目录",
            MinHeight = 32, MinWidth = 88, Margin = new Thickness(0, 2, 8, 2),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.FolderOpen24 },
        };
        toolbar.Children.Add(btnOpen);
        var btnRefresh = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Ver.Refresh") ?? "刷新",
            MinHeight = 32, MinWidth = 70, Margin = new Thickness(0, 2, 0, 2),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowClockwise24 },
        };
        toolbar.Children.Add(btnRefresh);
        panel.Children.Add(toolbar);

        var txtDir = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 6, 0, 4),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        txtDir.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        panel.Children.Add(txtDir);

        // 本机 Mod 列表（元数据：名字 / 版本 / 加载器 / 状态）
        var lst = new ListBox
        {
            MinHeight = 160, MaxHeight = 220,
            SelectionMode = SelectionMode.Extended,
            FontSize = 12.5,
        };
        panel.Children.Add(lst);

        // 操作行（WrapPanel）
        var ops = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var btnToggle = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Mod.Toggle") ?? "启用 / 禁用",
            MinHeight = 32, MinWidth = 104, Margin = new Thickness(0, 2, 8, 2),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ArrowSwap24 },
        };
        ops.Children.Add(btnToggle);
        var btnDelete = new Wpf.Ui.Controls.Button
        {
            Content = Find("S.Ver.Delete") ?? "删除",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
            MinHeight = 32, MinWidth = 84, Margin = new Thickness(0, 2, 8, 2),
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Delete24 },
        };
        ops.Children.Add(btnDelete);
        var hint = new TextBlock
        {
            FontSize = 11, VerticalAlignment = VerticalAlignment.Center,
            Text = Find("S.Vs.ModsHint2") ?? "双击条目直接切换启用状态；Ctrl / Shift 可多选。",
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        ops.Children.Add(hint);
        panel.Children.Add(ops);

        void Refresh()
        {
            var type = types[Math.Max(0, cmbType.SelectedIndex)].Value;
            var dir = DirFor(type);
            var kw = (txtQuery.Text ?? "").Trim();
            var all = LocalModScanner.ScanDir(dir);
            if (kw.Length > 0)
                all = all.Where(m => m.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                     || m.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            // v0.1.14：目录行带条目数（PCL 式一览），长路径省略号截断
            txtDir.Text = $"{dir}  ·  {all.Count} 项";
            lst.ItemsSource = all.Select(m => new ModRow(m)).ToList();
        }

        cmbType.SelectionChanged += (_, _) => Refresh();
        txtQuery.TextChanged += (_, _) => Refresh();
        btnRefresh.Click += (_, _) => Refresh();
        btnOpen.Click += (_, _) => OpenDir(DirFor(types[Math.Max(0, cmbType.SelectedIndex)].Value));

        static void ToggleFile(string dir, string name)
        {
            var full = Path.Combine(dir, name);
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                File.Move(full, full[..^".disabled".Length]);
            else
                File.Move(full, full + ".disabled");
        }

        List<string> Selected() => lst.SelectedItems?.Cast<ModRow>().Select(r => r.FileName).ToList() ?? new();

        btnToggle.Click += (_, _) =>
        {
            var names = Selected();
            var dir = DirFor(types[Math.Max(0, cmbType.SelectedIndex)].Value);
            foreach (var n in names)
            {
                try { ToggleFile(dir, n); } catch { /* 单个失败不打断 */ }
            }
            Refresh();
        };
        lst.MouseDoubleClick += (_, _) => btnToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));

        btnDelete.Click += (_, _) =>
        {
            var names = Selected();
            if (names.Count == 0) return;
            var msg = names.Count == 1
                ? (Find("S.Mod.DeleteConfirm1") ?? "确定删除？") + names[0]
                : string.Format(Find("S.Mod.DeleteConfirmN") ?? "确定删除这 {0} 项？", names.Count);
            if (!HmMsg.Confirm(win, Find("S.Ver.Delete") ?? "删除", msg)) return;
            var dir = DirFor(types[Math.Max(0, cmbType.SelectedIndex)].Value);
            foreach (var n in names)
            {
                try { File.Delete(Path.Combine(dir, n)); } catch { }
            }
            Refresh();
        };

        Refresh();
        return panel;
    }

    /// <summary>本机 Mod 条目视图模型（窗口内私有，绑定零转换器）。</summary>
    private sealed class ModRow
    {
        public ModRow(LocalModMeta m)
        {
            FileName = m.FileName;
            DisplayName = m.DisplayName.Length > 0 ? m.DisplayName : Path.GetFileNameWithoutExtension(m.FileName);
            Detail = m.ModVersion.Length > 0 ? "v" + m.ModVersion : "";
            LoaderText = m.Loader switch
            {
                "fabric" => "Fabric",
                "quilt" => "Quilt",
                "forge" => "Forge",
                "neoforge" => "NeoForge",
                _ => "",
            };
            StateText = m.Enabled ? "" : Find("S.Mod.Disabled") ?? "已禁用";
            SizeText = m.SizeBytes > 0 ? SpeedMeter.FormatBytes(m.SizeBytes) : "";
        }

        public string FileName { get; }
        public string DisplayName { get; }
        public string Detail { get; }
        public string LoaderText { get; }
        public string StateText { get; }
        public string SizeText { get; }
        public string Line => string.Join(" · ", new[] { DisplayName, Detail, LoaderText, StateText, SizeText }.Where(s => s.Length > 0));
    }

    // ---------- 公共小件 ----------

    private static string? Find(string key)
        => System.Windows.Application.Current.TryFindResource(key) as string;

    /// <summary>v0.1.9：目录快捷入口共用（不存在时创建并打开）。</summary>
    private static void OpenDir(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
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
