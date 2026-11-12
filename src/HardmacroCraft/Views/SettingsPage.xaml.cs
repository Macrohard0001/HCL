using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

public partial class SettingsPage : Page
{
    private bool _loaded;
    private bool _applyingTheme;

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
            RefreshLauncherMem();
            // v0.1.11：相对路径增强 / 日志平滑滚动
            ChkRelativePaths.IsChecked = AppSettings.Instance.UseRelativePaths;
            ChkSmoothScroll.IsChecked = AppSettings.Instance.SmoothScrollLogs;
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
            ChkMirror.IsChecked = AppSettings.Instance.UseMirror;
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
            // 主题选项（不触发 SelectionChanged 的重复应用）
            _applyingTheme = true;
            foreach (ComboBoxItem i in CmbTheme.Items)
                if ((string)i.Tag == AppSettings.Instance.Theme) { CmbTheme.SelectedItem = i; break; }
            _applyingTheme = false;
        };
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

    /// <summary>v0.1.3：显示启动器当前物理内存占用（每次进入设置页时刷新一次）。</summary>
    private void RefreshLauncherMem()
    {
        if (TxtLauncherMem == null) return;
        var tpl = (string)TryFindResource("S.Set.LauncherMemNow") ??
                  "启动器当前内存占用：{0} MB";
        TxtLauncherMem.Text = string.Format(tpl, Services.MemoryManager.WorkingSetMb);
    }

    /// <summary>v0.1.10：手动触发内存清理（PCL 式立即回收），并在标签上显示回收效果。</summary>
    private void BtnMemClean_Click(object sender, RoutedEventArgs e)
    {
        var (before, after) = Services.MemoryManager.OptimizeNow();
        RefreshLauncherMem();
        var tpl = (string)TryFindResource("S.Set.MemCleanDone") ??
                  "已回收内存（托管堆 {0} MB → {1} MB，物理内存占用已归还系统）";
        TxtLauncherMem.Text = string.Format(tpl, before / 1024 / 1024, after / 1024 / 1024);
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

    /// <summary>主题切换：即时应用并持久化，所有 DynamicResource 颜色（含字体）随之变化。</summary>
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

    private void BtnSave_Click(object sender, RoutedEventArgs e)
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
            MessageBox.Show($"游戏目录不可用：{ex.Message}\n已保留原目录设置。", "Hardmacro Craft Launcher",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
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
        s.UiRefreshMode = CmbUiRefresh.SelectedItem is ComboBoxItem ur && ur.Tag is string urTag
            ? UiRefreshPolicy.Normalize(urTag) : UiRefreshPolicy.ModeAuto;
        s.UiRefreshFps = NumUiFps.Value is >= 1 and <= 240 ? (int)NumUiFps.Value!.Value : 30;
        s.UiRefreshMaxFps = NumUiMaxFps.Value is >= 1 and <= 240 ? (int)NumUiMaxFps.Value!.Value : 30;
        s.MemoryAuto = ChkMemAuto.IsChecked == true;
        s.MemoryMb = (int)SldMemory.Value;
        s.UseMirror = ChkMirror.IsChecked == true;
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
        try
        {
            s.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"设置写入失败（config/ 目录不可写？）：{ex.Message}", "Hardmacro Craft Launcher",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (s.Language != App.Language)
            App.ApplyLanguage(s.Language);

        MessageBox.Show("设置已保存（保存在 exe 同目录 config/settings.json）。\n" +
            (s.GameRoot != oldRoot ? "游戏目录已变更，建议重启启动器以刷新版本列表。\n" : "") +
            "语言切换立即生效。", "Hardmacro Craft Launcher");
    }
}
