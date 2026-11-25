using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

public partial class SettingsPage : Page
{
    private bool _loaded;
    private bool _applyingTheme;
    // v0.1.12：启动器内存占用实时刷新（此前只在进入设置页时取一次，长挂后数值陈旧）
    private System.Windows.Threading.DispatcherTimer? _memTimer;

    public SettingsPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            TxtGameRoot.Text = AppSettings.Instance.GameRoot;
            TxtJava.Text = AppSettings.Instance.JavaPath;
            TxtJavaDir.Text = AppSettings.Instance.JavaRuntimeDir;
            TxtJvmArgs.Text = AppSettings.Instance.CustomJvmArgs;
            // fix8：JVM 优化开关 / 游戏参数 / 全屏启动
            ChkJvmOptimize.IsChecked = AppSettings.Instance.JvmOptimize;
            TxtGameArgs.Text = AppSettings.Instance.CustomGameArgs;
            ChkFullscreen.IsChecked = AppSettings.Instance.GameFullscreen;
            // v0.1.1：下载线程数 / 每次启动重新校验
            NumDownloadThreads.Value = Math.Clamp(AppSettings.Instance.DownloadThreads, 1, 256);
            ChkVerifyFiles.IsChecked = AppSettings.Instance.VerifyFilesEveryLaunch;
            // v0.1.3：启动器内存优化 + 当前占用展示
            ChkLauncherMemOpt.IsChecked = AppSettings.Instance.AutoMemoryOptimize;
            // v0.16.2：定时自动清理周期
            foreach (ComboBoxItem i in CmbAutoCleanInterval.Items)
                if ((string)i.Tag == AppSettings.Instance.AutoCleanIntervalMinutes.ToString()) { CmbAutoCleanInterval.SelectedItem = i; break; }
            RefreshLauncherMem();
            // v0.1.11：相对路径增强 / 日志平滑滚动
            ChkRelativePaths.IsChecked = AppSettings.Instance.UseRelativePaths;
            ChkSmoothScroll.IsChecked = AppSettings.Instance.SmoothScrollLogs;
            // v0.1.12：一键配置 Mods 移除前备份
            ChkModSetupBackup.IsChecked = AppSettings.Instance.ModSetupBackup;
            // v0.16.0：网络代理 + 启动前/退出后命令
            ChkProxy.IsChecked = AppSettings.Instance.ProxyEnabled;
            foreach (ComboBoxItem pi in CmbProxyType.Items)
                if ((string)pi.Tag == AppSettings.Instance.ProxyType) { CmbProxyType.SelectedItem = pi; break; }
            TxtProxyHost.Text = AppSettings.Instance.ProxyHost;
            NumProxyPort.Value = AppSettings.Instance.ProxyPort is >= 1 and <= 65535
                ? AppSettings.Instance.ProxyPort : null;
            TxtPreCmd.Text = AppSettings.Instance.PreLaunchCommand;
            TxtPostCmd.Text = AppSettings.Instance.PostExitCommand;
            // v0.1.11：界面刷新频率
            foreach (ComboBoxItem i in CmbUiRefresh.Items)
                if ((string)i.Tag == AppSettings.Instance.UiRefreshMode) { CmbUiRefresh.SelectedItem = i; break; }
            NumUiFps.Value = Math.Clamp(AppSettings.Instance.UiRefreshFps, 1, 240);
            NumUiMaxFps.Value = Math.Clamp(AppSettings.Instance.UiRefreshMaxFps, 1, 240);
            RefreshUiRefreshPanels();
            // v0.1.10：窗口大小模式（PCL 同款）
            foreach (ComboBoxItem i in CmbWindowMode.Items)
                if ((string)i.Tag == AppSettings.Instance.GameWindowSizeMode) { CmbWindowMode.SelectedItem = i; break; }
            RefreshWindowSizePanel();
            SldMemory.Value = AppSettings.Instance.MemoryMb;
            ChkMemAuto.IsChecked = AppSettings.Instance.MemoryAuto;
            // fix6：自动检测模式下滑杆禁用，显示检测到的物理内存与推荐值
            RefreshMemoryDetected();
            // v0.1.14：下载源三档（auto / mirror / official），替代旧镜像开关
            foreach (ComboBoxItem i in CmbDownloadSource.Items)
                if ((string)i.Tag == AppSettings.Instance.EffectiveDownloadSource) { CmbDownloadSource.SelectedItem = i; break; }
            ChkIsolation.IsChecked = AppSettings.Instance.VersionIsolation;
            ChkHideOnStart.IsChecked = AppSettings.Instance.HideLauncherOnGameStart;
            ChkExitOnGameExit.IsChecked = AppSettings.Instance.ExitLauncherOnGameExit;
            // fix7：跳过初始屏幕 / 游戏语言
            ChkSkipOnboarding.IsChecked = AppSettings.Instance.SkipOnboarding;
            foreach (ComboBoxItem i in CmbGameLang.Items)
                if ((string)i.Tag == AppSettings.Instance.GameLang) { CmbGameLang.SelectedItem = i; break; }
            ChkAutoJava.IsChecked = AppSettings.Instance.AutoJavaSelect;
            TxtGameWidth.Text = AppSettings.Instance.GameWidth > 0
                ? AppSettings.Instance.GameWidth.ToString() : "";
            TxtGameHeight.Text = AppSettings.Instance.GameHeight > 0
                ? AppSettings.Instance.GameHeight.ToString() : "";
            TxtAzureId.Text = AppSettings.Instance.AzureClientId;
            TxtCfKey.Text = AppSettings.Instance.CurseForgeApiKey;
            RefreshManagedJavas();
            foreach (ComboBoxItem i in CmbLanguage.Items)
                if ((string)i.Tag == AppSettings.Instance.Language) { CmbLanguage.SelectedItem = i; break; }
            // v0.1.14：语言下拉即时生效（PCL 式）—— 此前要等点「保存」才应用，
            // 用户改了下拉以为没作用；现在选中即切换并落盘（保存按钮仍会兜底再存一次）
            CmbLanguage.SelectionChanged -= CmbLanguage_SelectionChanged;
            CmbLanguage.SelectionChanged += CmbLanguage_SelectionChanged;
            // 主题选项（不触发 SelectionChanged 的重复应用）
            _applyingTheme = true;
            foreach (ComboBoxItem i in CmbTheme.Items)
                if ((string)i.Tag == AppSettings.Instance.Theme) { CmbTheme.SelectedItem = i; break; }
            foreach (ComboBoxItem i in CmbThemeStyle.Items)
                if ((string)i.Tag == AppSettings.Instance.ThemeStyle) { CmbThemeStyle.SelectedItem = i; break; }
            _applyingTheme = false;
            // v0.16.6：滚轮设置（跟随系统 / 手动像素，默认 90px）
            foreach (ComboBoxItem i in CmbWheelMode.Items)
                if ((string)i.Tag == AppSettings.Instance.WheelScrollMode) { CmbWheelMode.SelectedItem = i; break; }
            SldWheelPx.Value = Math.Clamp(AppSettings.Instance.WheelScrollPx, 30, 300);
            RefreshWheelPanel();
            // v0.16.6：全部初值就位后再挂自动保存钩子（初值回放不触发保存）
            HookAutoSave();
        };
        // v0.1.12：每次进入设置页都重启内存读数定时器（Loaded/Unloaded 随导航反复触发，
        // 不能放进 _loaded 一次性守卫里，否则切页回来后读数不再刷新）
        // v0.16.6：自动保存防抖定时器 —— 文本框逐键输入每次都落盘太频繁（SSD 写放大 +
        // config 读写竞争），600ms 无新改动后统一提交一次
        _saveTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(600),
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveFromUi();
        };
        Loaded += (_, _) => { RefreshLauncherMem(); StartMemTimer(); };
        Unloaded += (_, _) =>
        {
            StopMemTimer();
            // 离开设置页时把手头未落盘的改动立即提交（防抖窗口内的最后一次编辑不丢）
            if (_saveTimer.IsEnabled)
            {
                _saveTimer.Stop();
                SaveFromUi();
            }
        };
    }

    // ---- v0.16.6：设置自动保存（「保存设置」按钮移除） ----

    private readonly System.Windows.Threading.DispatcherTimer _saveTimer = null!;

    /// <summary>
    /// 遍历设置页全部输入控件挂自动保存钩子（改动即保存，PCL 式）。
    /// 在 Loaded 初值回放完成后调用一次，避免回放本身触发保存。
    /// 具体业务处理器（主题 / 语言 / 刷新面板切换等）已在 XAML 挂载，这里叠加
    /// MarkDirty 只是多一次幂等的落盘，不冲突。
    /// </summary>
    private void HookAutoSave()
    {
        void Hook(DependencyObject root)
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                switch (child)
                {
                    case CheckBox chk:
                        chk.Checked += (_, _) => MarkDirty();
                        chk.Unchecked += (_, _) => MarkDirty();
                        break;
                    case ComboBox cmb:
                        cmb.SelectionChanged += (_, _) => MarkDirty();
                        break;
                    case System.Windows.Controls.Slider sld:
                        sld.ValueChanged += (_, _) => MarkDirty();
                        break;
                    case Wpf.Ui.Controls.NumberBox num:
                        num.ValueChanged += (_, _) => MarkDirty();
                        break;
                    case System.Windows.Controls.TextBox tb:
                        tb.TextChanged += (_, _) => MarkDirty();
                        break;
                }
                Hook(child);
            }
        }
        Hook(this);
    }

    /// <summary>任一设置控件改动 → 重置防抖窗口，600ms 后统一落盘。</summary>
    private void MarkDirty()
    {
        if (!_loaded) return; // XAML 解析 / 初值回放期不保存
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>滚轮模式切换：手动档显示像素滑杆；改动随自动保存落盘。</summary>
    private void CmbWheelMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PnlWheelPx == null) return; // XAML 加载期保护
        RefreshWheelPanel();
        MarkDirty();
    }

    private void RefreshWheelPanel()
    {
        if (CmbWheelMode.SelectedItem is not ComboBoxItem item || item.Tag is not string mode) return;
        PnlWheelPx.Visibility = mode == "manual" ? Visibility.Visible : Visibility.Collapsed;
        TxtWheelValue.Text = ((int)SldWheelPx.Value) + " px";
    }

    private void SldWheelPx_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtWheelValue == null) return; // XAML 加载期保护
        TxtWheelValue.Text = ((int)e.NewValue) + " px";
        MarkDirty();
    }

    /// <summary>
    /// 从 UI 收集全部设置并落盘（自动保存引擎；原「保存设置」按钮逻辑）。
    /// v0.16.6：不再弹窗 —— 常规保存只在提示行打勾；游戏目录非法时在提示行警示
    /// 并跳过本次落盘（原值继续生效），不打断输入。
    /// </summary>
    private void SaveFromUi()
    {
        var oldRoot = AppSettings.Instance.GameRoot;
        var s = AppSettings.Instance;
        var newRoot = string.IsNullOrWhiteSpace(TxtGameRoot.Text) ? s.GameRoot : TxtGameRoot.Text.Trim();
        try
        {
            // 提前校验目录可创建/可写，避免保存后启动阶段崩溃
            Directory.CreateDirectory(newRoot);
            var probe = Path.Combine(newRoot, ".hm_write_probe");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            TxtSaveHint.Text = "⚠ 游戏目录不可用（" + ex.Message + "），已保留原目录设置——请修正后再改其他项。";
            TxtSaveHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                "DangerButtonBackground");
            return;
        }
        TxtSaveHint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorTertiaryBrush");
        s.GameRoot = newRoot;
        // 用户显式选择了非默认目录 → 标记自定义，Load() 不再自动纠正
        try
        {
            s.GameRootCustomized = !string.Equals(
                Path.GetFullPath(newRoot),
                Path.GetFullPath(Path.Combine(AppSettings.AppDir, ".minecraft")),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { s.GameRootCustomized = true; }
        s.JavaPath = TxtJava.Text.Trim();
        s.JavaRuntimeDir = TxtJavaDir.Text.Trim();
        s.CustomJvmArgs = TxtJvmArgs.Text.Trim();
        // fix8：JVM 优化开关 / 游戏参数 / 全屏启动
        s.JvmOptimize = ChkJvmOptimize.IsChecked == true;
        s.CustomGameArgs = TxtGameArgs.Text.Trim();
        s.GameFullscreen = ChkFullscreen.IsChecked == true;
        // v0.1.1：下载线程数（1-256，非法输入回退默认 64）/ 每次启动重新校验
        s.DownloadThreads = NumDownloadThreads.Value is >= 1 and <= 256
            ? (int)NumDownloadThreads.Value!.Value : 64;
        s.VerifyFilesEveryLaunch = ChkVerifyFiles.IsChecked == true;
        // v0.1.3：启动器内存优化开关；关闭后立即做一次优化以便用户看到变化
        s.AutoMemoryOptimize = ChkLauncherMemOpt.IsChecked == true;
        // v0.1.11：相对路径增强 / 日志平滑滚动 / 界面刷新频率
        s.UseRelativePaths = ChkRelativePaths.IsChecked == true;
        s.SmoothScrollLogs = ChkSmoothScroll.IsChecked == true;
        // v0.1.12：一键配置 Mods 移除前备份
        s.ModSetupBackup = ChkModSetupBackup.IsChecked == true;
        // v0.16.0：网络代理（下载管线作用，重启生效）+ 启动前/退出后命令
        s.ProxyEnabled = ChkProxy.IsChecked == true;
        s.ProxyType = CmbProxyType.SelectedItem is ComboBoxItem pt && pt.Tag is string pTag
            ? pTag : "http";
        s.ProxyHost = TxtProxyHost.Text.Trim();
        s.ProxyPort = NumProxyPort.Value is >= 1 and <= 65535 ? (int)NumProxyPort.Value!.Value : 0;
        s.PreLaunchCommand = TxtPreCmd.Text.Trim();
        s.PostExitCommand = TxtPostCmd.Text.Trim();
        s.UiRefreshMode = CmbUiRefresh.SelectedItem is ComboBoxItem ur && ur.Tag is string urTag
            ? UiRefreshPolicy.Normalize(urTag) : UiRefreshPolicy.ModeAuto;
        s.UiRefreshFps = NumUiFps.Value is >= 1 and <= 240 ? (int)NumUiFps.Value!.Value : 30;
        s.UiRefreshMaxFps = NumUiMaxFps.Value is >= 1 and <= 240 ? (int)NumUiMaxFps.Value!.Value : 30;
        s.MemoryAuto = ChkMemAuto.IsChecked == true;
        s.MemoryMb = (int)SldMemory.Value;
        // v0.1.14：下载源三档（对齐 PCL）；切换即时生效（LauncherService 订阅了 Saved 事件）
        if (CmbDownloadSource.SelectedItem is ComboBoxItem ds && ds.Tag is string dsTag)
        {
            s.DownloadSource = dsTag;
            s.UseMirror = dsTag != "official"; // 旧字段同步，兼容旧版本读配置
        }
        s.VersionIsolation = ChkIsolation.IsChecked == true;
        s.HideLauncherOnGameStart = ChkHideOnStart.IsChecked == true;
        s.ExitLauncherOnGameExit = ChkExitOnGameExit.IsChecked == true;
        // fix7：跳过初始屏幕 / 游戏语言
        s.SkipOnboarding = ChkSkipOnboarding.IsChecked == true;
        if (CmbGameLang.SelectedItem is ComboBoxItem gli && gli.Tag is string gl)
            s.GameLang = gl;
        s.AutoJavaSelect = ChkAutoJava.IsChecked == true;
        s.GameWidth = int.TryParse(TxtGameWidth.Text.Trim(), out var gw) && gw > 0 ? gw : 0;
        s.GameHeight = int.TryParse(TxtGameHeight.Text.Trim(), out var gh) && gh > 0 ? gh : 0;
        // v0.1.10：窗口大小模式
        s.GameWindowSizeMode = CmbWindowMode.SelectedItem is ComboBoxItem wm && wm.Tag is string wmTag
            ? wmTag : "default";
        s.AzureClientId = TxtAzureId.Text.Trim();
        s.CurseForgeApiKey = TxtCfKey.Text.Trim();
        if (CmbLanguage.SelectedItem is ComboBoxItem li && li.Tag is string lang)
            s.Language = lang;
        // v0.16.6：滚轮设置
        if (CmbWheelMode.SelectedItem is ComboBoxItem wmi && wmi.Tag is string wmode)
            s.WheelScrollMode = wmode;
        s.WheelScrollPx = Math.Clamp((int)SldWheelPx.Value, 30, 300);
        try
        {
            s.Save();
        }
        catch (Exception ex)
        {
            TxtSaveHint.Text = "⚠ 设置写入失败（config/ 目录不可写？）：" + ex.Message;
            return;
        }

        if (s.Language != App.Language)
        {
            try { App.ApplyLanguage(s.Language); }
            catch { /* 语言切换失败不阻塞设置保存 */ }
        }

        TxtSaveHint.Text = "✓ 已自动保存 " + DateTime.Now.ToString("HH:mm:ss") +
            "（" + (s.GameRoot != oldRoot ? "游戏目录已变更，建议重启启动器刷新版本列表 · " : "") +
            "config/settings.json）";
    }

    private void SldMemory_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtMemValue == null) return; // XAML 加载期 Slider 初值触发时保护
        TxtMemValue.Text = ((int)e.NewValue) + " MB";
    }

    // fix6：内存自动检测（PCL 同款）—— 显示检测信息，自动模式下禁用手动滑杆
    private void ChkMemAuto_Changed(object sender, RoutedEventArgs e)
    {
        if (SldMemory == null || TxtMemValue == null) return; // XAML 加载期保护
        var auto = ChkMemAuto.IsChecked == true;
        SldMemory.IsEnabled = !auto;
        if (auto)
        {
            var rec = (int)Services.SystemInfo.RecommendedMemoryMb();
            TxtMemValue.Text = rec + " MB";
            SldMemory.Value = Math.Clamp(rec, SldMemory.Minimum, SldMemory.Maximum);
        }
        RefreshMemoryDetected();
    }

    private void RefreshMemoryDetected()
    {
        if (TxtMemDetected == null) return;
        var total = Services.SystemInfo.TotalPhysicalMemoryMb;
        var rec = Services.SystemInfo.RecommendedMemoryMb(total);
        var tpl = (string)TryFindResource("S.Set.MemDetected") ??
                  "检测到物理内存 {0} MB，推荐分配 {1} MB";
        TxtMemDetected.Text = string.Format(tpl, total, rec);
    }

    /// <summary>v0.1.3：显示启动器当前内存占用。v0.1.13 改为「专用工作集」口径（与
    /// 任务管理器默认「内存」列完全同口径：只统计本进程独占且驻留物理内存的页），
    /// 每 2 秒实时刷新 —— 此前的「专用内存（提交）」包含大量已提交但未驻留 / 共享映射的页，
    /// 用户对比任务管理器（如 31 MB）与启动器读数（500+ MB）觉得算错。</summary>
    private void RefreshLauncherMem()
    {
        if (TxtLauncherMem == null) return;
        var tpl = (string)TryFindResource("S.Set.LauncherMemNow") ??
                  "启动器当前内存占用：{0} MB（与任务管理器同口径）· 提交 {1} MB";
        TxtLauncherMem.Text = string.Format(tpl,
            Services.MemoryManager.PrivateWorkingSetMb, Services.MemoryManager.PrivateMb);
    }

    /// <summary>v0.1.12：页面可见期间每 2 秒刷新一次内存读数（Unloaded 停止，不空耗）。</summary>
    private void StartMemTimer()
    {
        if (_memTimer != null) return;
        _memTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _memTimer.Tick += (_, _) => RefreshLauncherMem();
        _memTimer.Start();
    }

    private void StopMemTimer()
    {
        _memTimer?.Stop();
        _memTimer = null;
    }

    /// <summary>v0.1.10：手动触发内存清理（PCL 式立即回收），并在标签上显示回收效果。
    /// v0.1.12：改用工作集 + 托管堆双口径展示真实收益（此前只报托管堆，
    /// 与任务管理器里的变化对不上）。</summary>
    /// <summary>v0.16.2：定时自动清理周期变更 —— 立即落盘并重建空闲定时器（0=停止定时）。</summary>
    private void CmbAutoCleanInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return; // 页面 XAML 解析期触发忽略
        if (CmbAutoCleanInterval.SelectedItem is ComboBoxItem it && int.TryParse((string)it.Tag, out var min))
        {
            AppSettings.Instance.AutoCleanIntervalMinutes = min;
            AppSettings.Instance.Save();
            MemoryManager.ApplyInterval(min);
        }
    }

    private void BtnMemClean_Click(object sender, RoutedEventArgs e)
    {
        var (wsBefore, wsAfter, mBefore, mAfter, _) = Services.MemoryManager.OptimizeNowDetailed();
        RefreshLauncherMem();
        var tpl = (string)TryFindResource("S.Set.MemCleanDone") ??
                  "已回收内存：物理工作集 {0} MB → {1} MB · 托管堆 {2} MB → {3} MB";
        TxtLauncherMem.Text = string.Format(tpl, wsBefore, wsAfter, mBefore, mAfter);
    }

    /// <summary>v0.1.10：窗口大小模式切换 —— 自定义模式才显示宽高输入框。</summary>
    private void CmbWindowMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (TxtGameWidth == null) return; // XAML 加载期保护
        RefreshWindowSizePanel();
    }

    /// <summary>v0.1.11：界面刷新频率切换 —— 手动 / 事件限频模式才显示 FPS 输入框。</summary>
    private void CmbUiRefresh_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NumUiFps == null) return; // XAML 加载期保护
        RefreshUiRefreshPanels();
    }

    private void RefreshUiRefreshPanels()
    {
        if (CmbUiRefresh.SelectedItem is not ComboBoxItem item || item.Tag is not string mode) return;
        PnlUiRefreshFps.Visibility = mode == UiRefreshPolicy.ModeManual ? Visibility.Visible : Visibility.Collapsed;
        PnlUiRefreshMaxFps.Visibility = mode == UiRefreshPolicy.ModeEventLimited ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshWindowSizePanel()
    {
        if (CmbWindowMode.SelectedItem is not ComboBoxItem item || item.Tag is not string mode) return;
        PnlWindowSize.Visibility = mode == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>v0.1.14：语言切换即时生效（PCL 式）—— 选中即应用并持久化，
    /// 不再等待「保存」按钮；修复「切换语言没有作用」（此前只改下拉毫无反馈）。</summary>
    private void CmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbLanguage.SelectedItem is not ComboBoxItem item || item.Tag is not string lang) return;
        if (string.Equals(lang, App.Language, StringComparison.Ordinal)) return; // 初始化回放不重复应用
        try
        {
            App.ApplyLanguage(lang);
            AppSettings.Instance.Language = lang;
            AppSettings.Instance.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"语言切换失败：{ex.Message}", "Hardmacro Craft Launcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>主题切换：即时应用并持久化，所有 DynamicResource 颜色（含字体）随之变化。</summary>
    // v0.15.3：主题风格切换（Fluent Win11 / Metro Win10 怀旧）——
    // 保存后整体重应用主题（含云母开关与 Metro 覆盖层挂载 / 移除），深浅色保持不变
    private async void CmbThemeStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingTheme) return;
        if (CmbThemeStyle.SelectedItem is not ComboBoxItem item || item.Tag is not string style) return;
        var oldStyle = AppSettings.Instance.ThemeStyle;
        if (oldStyle == style) return;
        AppSettings.Instance.ThemeStyle = style;
        AppSettings.Instance.Save();
        // v0.15.6：运行中热切全局样式集会在模板交换时崩溃（旧模板动画 NaN 目标值，
        // 见 v0.15.5 crash.log）—— 必须重启生效，询问立即重启或稍后
        var result = await ShowRestartDialogAsync();
        if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
        {
            App.RestartApplication(); // 保存后拉起新进程读取设置
            return;
        }
        // 稍后重启：本次会话沿用当前风格，回退设置与下拉回显（新风格下次启动生效）
        AppSettings.Instance.ThemeStyle = oldStyle;
        AppSettings.Instance.Save();
        _applyingTheme = true;
        foreach (ComboBoxItem i in CmbThemeStyle.Items)
            if ((string)i.Tag == oldStyle) { CmbThemeStyle.SelectedItem = i; break; }
        _applyingTheme = false;
    }

    /// <summary>
    /// v0.15.7：重启确认弹窗 —— Wpf.Ui ContentDialog（宿主为 MainWindow 的
    /// RootContentDialog），与全局主题样式统一；找不到宿主时退回 Win32 MessageBox。
    /// </summary>
    private async Task<Wpf.Ui.Controls.ContentDialogResult> ShowRestartDialogAsync()
    {
        var message = (string?)TryFindResource("S.Settings.RestartConfirm") ?? "主题风格切换需要重建全局资源字典，重启应用后生效。";
        var presenter = Window.GetWindow(this)?.FindName("RootContentDialog") as System.Windows.Controls.ContentPresenter;
        if (presenter is null)
        {
            var mb = System.Windows.MessageBox.Show(
                message + "\n\n立即重启吗？", "主题风格",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            return mb == MessageBoxResult.Yes
                ? Wpf.Ui.Controls.ContentDialogResult.Primary
                : Wpf.Ui.Controls.ContentDialogResult.None;
        }
        var dialog = new Wpf.Ui.Controls.ContentDialog
        {
            Title = (string?)TryFindResource("S.Settings.ThemeStyle") ?? "主题风格",
            Content = message,
            PrimaryButtonText = (string?)TryFindResource("S.Settings.RestartNow") ?? "立即重启",
            CloseButtonText = (string?)TryFindResource("S.Common.Later") ?? "稍后",
            DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary,
            DialogHost = presenter,
        };
        try
        {
            return await dialog.ShowAsync();
        }
        catch (Exception dialogEx)
        {
            // v0.15.8：主题化弹窗失败（个别环境模板解析异常）不能阻塞重启流程，
            // 退回 Win32 MessageBox（样式欠佳但功能保底），异常落 boot.log
            Services.BootLogger.Error("ContentDialog 显示失败，退回 MessageBox: "
                + Services.BootLogger.Format(dialogEx));
            var mb = System.Windows.MessageBox.Show(
                message + "\n\n立即重启吗？", "主题风格",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            return mb == MessageBoxResult.Yes
                ? Wpf.Ui.Controls.ContentDialogResult.Primary
                : Wpf.Ui.Controls.ContentDialogResult.None;
        }
    }

    private void CmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applyingTheme) return;
        if (CmbTheme.SelectedItem is not ComboBoxItem item || item.Tag is not string theme) return;
        try
        {
            AppSettings.Instance.Theme = theme;
            App.ApplyTheme(theme); // 含跟随系统的监听器管理
            AppSettings.Instance.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"主题切换失败：{ex.Message}", "Hardmacro Craft Launcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void BtnBrowseRoot_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "选择游戏目录" };
        if (dlg.ShowDialog() == true)
            TxtGameRoot.Text = dlg.FolderName;
    }

    private void BtnBrowseJava_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 javaw.exe",
            Filter = "javaw.exe|javaw.exe|java.exe|java.exe|所有文件|*.*",
        };
        if (dlg.ShowDialog() == true)
            TxtJava.Text = dlg.FileName;
    }

    // fix5：也允许直接选 Java 所在文件夹（启动时自动递归找 javaw.exe）
    private void BtnBrowseJavaFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 Java 所在文件夹（自动搜索 javaw.exe）",
        };
        if (dlg.ShowDialog() == true)
            TxtJava.Text = dlg.FolderName;
    }

    // fix5：Java 自动管理下载根目录
    private void BtnBrowseJavaDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 Java 下载安装位置（空 = 默认 exe 同目录 runtime/）",
        };
        if (dlg.ShowDialog() == true)
            TxtJavaDir.Text = dlg.FolderName;
    }

    private void RefreshManagedJavas()
    {
        var javas = Services.JavaManager.FindManagedJavas();
        TxtJavaManaged.Text = javas.Count == 0
            ? (string)TryFindResource("S.Set.NoManagedJava") ?? "尚未安装托管 Java。"
            : (string)TryFindResource("S.Set.ManagedList") ?? "已托管：" + string.Join(", ",
                javas.Select(j => $"Java {j.Item1}"));
    }

    private bool _javaDownloading;

    private async void BtnDownloadJava_Click(object sender, RoutedEventArgs e)
    {
        if (_javaDownloading) return;
        if (CmbJavaMajor.SelectedItem is not ComboBoxItem item || item.Tag is not string tag
            || !int.TryParse(tag, out var major)) return;
        _javaDownloading = true;
        BtnDownloadJava.IsEnabled = false;
        PbJava.Visibility = Visibility.Visible;
        PbJava.Value = 0;
        var centerTask = DownloadTaskService.Instance.StartCancellableKeyed($"java-install:{major}",
            $"下载 Java {major} (Temurin JRE)", "正在获取 Temurin JRE 元数据");
        var dlCt = DownloadTaskService.Instance.TokenFor(centerTask);
        try
        {
            // v0.1.10：Java 下载进度含速度与剩余时间（此前只有百分比）
            var progress = new Progress<JavaManager.JavaDownloadProgress>(p =>
                Dispatcher.Invoke(() =>
                {
                    PbJava.Value = p.Percent;
                    var eta = SpeedMeter.FormatEta(p.TotalBytes - p.DoneBytes, p.SpeedBps);
                    TxtJavaStatus.Text = $"下载中 {p.Percent:0}% · " +
                        $"{SpeedMeter.FormatBytes(p.DoneBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)} · " +
                        SpeedMeter.FormatSpeed(p.SpeedBps) + (eta.Length > 0 ? " · " + eta : "");
                    DownloadTaskService.Instance.Update(centerTask, p.Percent, TxtJavaStatus.Text);
                }));
            var javaw = await Services.JavaManager.EnsureJreAsync(major, App.LauncherService.Http,
                progress, msg => Dispatcher.Invoke(() => TxtJavaStatus.Text = msg), ct: dlCt);
            TxtJavaStatus.Text = (string)TryFindResource("S.Set.JavaDone") ?? "安装完成：" + javaw;
            DownloadTaskService.Instance.Complete(centerTask, "安装完成");
        }
        catch (OperationCanceledException) when (dlCt.IsCancellationRequested)
        {
            // fix7：任务中心 / 主页取消 Java 下载
            TxtJavaStatus.Text = (string)TryFindResource("S.Set.JavaCanceled") ?? "下载已取消";
            DownloadTaskService.Instance.Cancel(centerTask, "已取消");
        }
        catch (Exception ex)
        {
            TxtJavaStatus.Text = (string)TryFindResource("S.Set.JavaFail") ?? "下载失败：" + ex.Message;
            DownloadTaskService.Instance.Fail(centerTask, "下载失败：" + ex.Message);
        }
        finally
        {
            PbJava.Visibility = Visibility.Collapsed;
            BtnDownloadJava.IsEnabled = true;
            _javaDownloading = false;
            RefreshManagedJavas();
        }
    }

    /// <summary>打开 Azure Client ID 获取引导（v0.1.3）。</summary>
    private void BtnAzureGuide_Click(object sender, RoutedEventArgs e)
        => new AzureGuideWindow { Owner = Window.GetWindow(this) }.ShowDialog();

    private void BtnCfKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://console.curseforge.com") { UseShellExecute = true });
        }
        catch { }
    }
}
