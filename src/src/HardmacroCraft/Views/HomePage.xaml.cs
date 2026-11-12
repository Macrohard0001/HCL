using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CmlLib.Core.Auth;
using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft.Views;

public partial class HomePage : Page
{
    private bool _loaded;
    // v0.1.1：启动状态改为静态 —— Wpf-UI 导航每次新建页面实例，
    // 实例字段会让"切回来"的新实例误以为没有启动在进行（进度丢失的根因之一）
    private static bool _launching;
    private static DownloadTaskService.TaskItem? _currentCenterTask;
    /// <summary>fix7：当前启动流程的取消令牌（取消按钮 / 任务中心都能触发）。</summary>
    private static System.Threading.CancellationToken _launchCt;
    /// <summary>v0.1.7：游戏进程已创建，正在等待窗口出现（进度区由后台等待器接管，启动流程本体已结束）。</summary>
    private static bool _waitingWindow;
    /// <summary>v0.1.7：窗口等待超时 —— 超时后按运行中处理（少数环境拿不到窗口句柄）。</summary>
    private static readonly TimeSpan WindowWaitTimeout = TimeSpan.FromSeconds(240);
    private object? _btnLaunchDefault;
    // v0.1.11：启动进度统一计算器与平滑显示值（随启动流程重置，静态与 _launching 同一生命周期）
    private static LaunchProgressCalc? _progressCalc;
    private static double _shownPct;

    /// <summary>v0.1.6：当前首页实例（供版本管理页「启动」跳转后直连启动，规避导航后取不到页面实例）。</summary>
    public static HomePage? Current { get; private set; }
    /// <summary>当前版本列表条目（含图标 / 副标题），供最近启动回选中。</summary>
    private List<VersionItemViewModel> _versionItems = new();

    /// <summary>下拉框当前选中版本（条目视图模型，SelectedValue 取 Name）。</summary>
    private string? SelectedVersion => CmbVersions.SelectedValue as string;

    public HomePage()
    {
        InitializeComponent();
        _btnLaunchDefault = BtnLaunch.Content;
        Current = this;
        // v0.1.1：订阅启动进度中枢 —— 启动中切页再回来，新实例立即回放当前进度
        LaunchProgressHub.Changed += ApplyLaunchUi;
        LaunchProgressHub.CancelRequested += OnHubCancelRequested;
        // v0.1.7：实例日志仍写入主页日志区（实例列表已迁移到「实例管理」页 v0.1.11）
        GameInstanceManager.Log += OnInstanceLog;
        Unloaded += (_, _) =>
        {
            LaunchProgressHub.Changed -= ApplyLaunchUi;
            LaunchProgressHub.CancelRequested -= OnHubCancelRequested;
            GameInstanceManager.Log -= OnInstanceLog;
            if (Current == this) Current = null;
        };
        ApplyLaunchUi();
        // v0.1.11：恢复共享日志（切页不再清空）与展开状态（持久化，重启也恢复）
        lock (LogLock)
        {
            if (LogLines.Count > 0) TxtLog.Text = string.Join(Environment.NewLine, LogLines);
        }
        TxtLog.ScrollToEnd();
        SetLogOpen(AppSettings.Instance.HomeLogOpen);
        Loaded += async (_, _) =>
        {
            // v0.1.8 紧急修复：版本列表每次进入首页都刷新 —— 此前只在首次 Loaded 刷新，
            // 在下载页装完新版本回到首页，下拉框里根本没有它（选不了也启动不了）；
            // 主流启动器装完立即可见。账号 / 最近启动同步刷新。
            var keep = SelectedVersion;
            await RefreshVersionsAsync();
            if (keep != null && _versionItems.Any(i => i.Name == keep))
                CmbVersions.SelectedValue = keep;
            RefreshAccountUi();
            RefreshRecent();
            if (!_loaded)
            {
                _loaded = true;
                // 自动登录（记住密码 + 自动登录的验证站账号静默重登）
                _ = TryAutoLoginAsync();
            }
        };
    }

    /// <summary>用记住的密码静默重登验证站账号（失败仅记日志，不打扰用户）。</summary>
    private async Task TryAutoLoginAsync()
    {
        var acc = AccountStore.Instance.Current;
        if (acc is not { Type: "authlib", AutoLogin: true, RememberPassword: true }
            || string.IsNullOrWhiteSpace(acc.ServerUrl)
            || string.IsNullOrWhiteSpace(acc.SavedPassword)
            || string.IsNullOrWhiteSpace(acc.LoginId))
            return;
        try
        {
            Log($"正在自动登录 {acc.LoginId}（{acc.ServerUrl}）...");
            var authlib = new AuthlibService(App.LauncherService.Http);
            var login = await authlib.AuthenticateAsync(acc.ServerUrl, acc.LoginId, acc.SavedPassword);
            AccountStore.Instance.Upsert(new StoredAccount
            {
                Type = "authlib",
                Name = login.Name,
                Uuid = login.Uuid,
                RefreshToken = login.AccessToken,
                ClientToken = login.ClientToken,
                ServerUrl = login.ApiRoot,
                LoginId = acc.LoginId,
                RememberPassword = true,
                SavedPassword = acc.SavedPassword,
                AutoLogin = true,
            });
            Log($"自动登录成功: {login.Name}");
            RefreshAccountUi();
        }
        catch (Exception ex)
        {
            Log("自动登录失败（可在登录窗口重新登录）: " + ex.Message);
        }
    }

    /// <summary>
    /// v0.1.11：启动进度统一出口 —— 平滑显示值（PCL 式逼近，永不回跳到真实值之下后横跳）、
    /// 写进度中枢（跨页面回放）与任务中心任务。所有进度回调（文件尺 / 字节尺）都走这里。
    /// </summary>
    private static void ShowLaunchProgress(double actual, string line, DownloadTaskService.TaskItem? task)
    {
        _shownPct = LaunchProgressCalc.Smooth(_shownPct, actual);
        LaunchProgressHub.Update(s =>
        {
            s.Indeterminate = false;
            s.Percent = _shownPct;
            s.ProgressLine = line;
            s.ButtonText = $"下载中 {actual:0}%";
        });
        if (task != null)
            DownloadTaskService.Instance.Update(task, _shownPct, line);
    }

    /// <summary>v0.1.1：把启动进度中枢的快照回放到本实例的控件上。</summary>
    private void ApplyLaunchUi()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ApplyLaunchUi); return; }
        var s = LaunchProgressHub.Current;
        if (!s.Running)
        {
            // v0.1.6 重排：空闲时恢复版本选择行 + 按钮行，隐藏进度区
            PnlVersionRow.Visibility = Visibility.Visible;
            PnlButtons.Visibility = Visibility.Visible;
            PnlLaunchProgress.Visibility = Visibility.Collapsed;
            BtnLaunch.IsEnabled = true;
            BtnCancelLaunch.Visibility = Visibility.Collapsed;
            BtnCancelLaunch.IsEnabled = true;
            TxtStage.Visibility = Visibility.Collapsed;
            // v0.1.2：空闲时回放上次启动的终态文案（已取消 / 启动失败 / 游戏运行中），
            // v0.1.6 改为按钮行下方的一行小字（进度区已隐藏）
            var idle = LaunchProgressHub.LastProgressLine;
            TxtIdleStatus.Text = idle;
            TxtIdleStatus.Visibility = string.IsNullOrEmpty(idle) ? Visibility.Collapsed : Visibility.Visible;
            return;
        }
        // v0.1.6 重排：启动中隐藏版本选择行 / 按钮行，显示进度区（进度条 + 文字说明 + 取消）
        PnlVersionRow.Visibility = Visibility.Collapsed;
        PnlButtons.Visibility = Visibility.Collapsed;
        PnlLaunchProgress.Visibility = Visibility.Visible;
        TxtIdleStatus.Visibility = Visibility.Collapsed;
        BtnLaunch.IsEnabled = false;
        if (s.Indeterminate) PbProgress.IsIndeterminate = true;
        else { PbProgress.IsIndeterminate = false; PbProgress.Value = s.Percent; }
        TxtProgress.Text = s.ProgressLine;
        if (!string.IsNullOrEmpty(s.StageLine))
        {
            TxtStage.Text = s.StageLine;
            TxtStage.Visibility = Visibility.Visible;
        }
        else TxtStage.Visibility = Visibility.Collapsed;
        BtnCancelLaunch.Visibility = s.CancelVisible ? Visibility.Visible : Visibility.Collapsed;
        BtnCancelLaunch.IsEnabled = s.CancelEnabled;
    }

    /// <summary>v0.1.1：任意页面实例的取消请求统一从中枢进来。</summary>
    private void OnHubCancelRequested()
    {
        if (!_launching) return;
        Log("正在取消...");
        _currentCenterTask?.RequestCancel();
        LaunchProgressHub.Update(s => s.CancelEnabled = false);
    }

    // ===== v0.1.7 运行实例日志（实例列表 UI 已迁移到「实例管理」页 v0.1.11） =====

    private void OnInstanceLog(string line)
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(() => Log(line)); return; }
        Log(line);
    }

    /// <summary>主页最近启动快捷键（图标对齐版本列表：加载器图标 / 原版草方块）。</summary>
    private void RefreshRecent()
    {
        PnlRecent.Children.Clear();
        var recent = AppSettings.Instance.RecentVersions;
        CardRecent.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var v in recent)
        {
            var installed = App.LauncherService.GetInstalledVersions().Contains(v);
            // v0.1.11：图标不再硬编码草方块 —— 与首页下拉框同一套规则（加载器图标 / 原版草方块），
            // 修复「最近启动的 Forge / Fabric 版本也显示草方块」的问题
            ImageSource? icon = null;
            string? badge = null;
            Brush? badgeBrush = null;
            try
            {
                var desc = VersionInfo.Describe(AppSettings.Instance.GameRoot, v);
                (icon, badge, badgeBrush) = VersionIconProvider.DescribeIcon(desc);
            }
            catch { /* 版本目录读取失败时回退草方块 */ }
            icon ??= new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/Assets/grass.png"));
            var chipContent = new StackPanel { Orientation = Orientation.Horizontal };
            chipContent.Children.Add(new System.Windows.Controls.Image
            {
                Source = icon,
                Width = 16, Height = 16,
                Stretch = Stretch.Uniform,
                                VerticalAlignment = VerticalAlignment.Center,
            });
            chipContent.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = v, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0),
                Opacity = installed ? 1.0 : 0.45,
                FontSize = 12,
            });
            if (chipContent.Children[0] is System.Windows.Controls.Image chipImg)
                RenderOptions.SetBitmapScalingMode(chipImg, BitmapScalingMode.NearestNeighbor);
            var chip = new Wpf.Ui.Controls.Button
            {
                Content = chipContent,
                MinHeight = 30,
                Padding = new Thickness(8, 3, 10, 3),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = installed ? v : v + "（已不存在，点击重试）",
            };
            chip.Click += async (_, _) =>
            {
                if (_versionItems.Any(i => i.Name == v)) CmbVersions.SelectedValue = v;
                await LaunchAsync(v);
            };
            PnlRecent.Children.Add(chip);
        }
    }

    private const int LogCapLines = 500;   // v0.1.3：日志超过 600 行时修剪回 500 行，防长会话撑爆内存
    private const int LogTrimToLines = 500;
    // v0.1.11：日志缓冲改为静态（跨页面实例共享）—— 此前 _logLines 是实例字段，
    // Wpf-Ui 导航每次新建页面实例，切个标签回来日志就被清空并折叠。
    // 现在全实例共享同一条环形缓冲，展开状态另持久化到 config（HomeLogOpen）。
    private static readonly List<string> LogLines = new();
    private static readonly object LogLock = new();

    private void Log(string line)
    {
        string entry;
        List<string>? full = null;
        lock (LogLock)
        {
            entry = $"[{DateTime.Now:HH:mm:ss}] {line}";
            LogLines.Add(entry);
            if (LogLines.Count > LogCapLines + 100)
            {
                // v0.1.3 环形修剪：只保留最近 500 行（游戏输出 / 补全日志可达数千行）
                LogLines.RemoveRange(0, LogLines.Count - LogTrimToLines);
                full = LogLines.ToList();
            }
        }
        void Apply()
        {
            if (full != null) TxtLog.Text = string.Join(Environment.NewLine, full);
            else TxtLog.AppendText(entry + Environment.NewLine);
            TxtLog.ScrollToEnd();
        }
        if (Dispatcher.CheckAccess()) Apply(); else Dispatcher.BeginInvoke(Apply);
    }

    private void RefreshAccountUi()
    {
        var acc = AccountStore.Instance.Current;
        if (acc == null)
        {
            TxtAvatar.Text = "?";
            ImgAvatar.Visibility = Visibility.Collapsed;
            TxtAvatar.Visibility = Visibility.Visible; // v0.1.5：切到无账号态时恢复字母头像可见性
            TxtAccount.Text = (string)FindResource("S.Home.NoAccount")!;
            TxtAccountType.Text = (string)FindResource("S.Home.NoAccountHint")!;
        }
        else
        {
            TxtAvatar.Text = string.IsNullOrWhiteSpace(acc.Name) ? "?" : acc.Name[..1].ToUpperInvariant();
            TxtAccount.Text = acc.Name;
            // v0.1.6：第二行账号类型（离线账号 / 正版登录 / 验证站主机名 + 登录）
            TxtAccountType.Text = acc.Type switch
            {
                "microsoft" => (string)FindResource("S.Home.AccountMs")!,
                "authlib" => string.Format((string)FindResource("S.Home.AccountThirdFmt")!, AuthlibHost(acc.ServerUrl)),
                _ => (string)FindResource("S.Home.AccountOffline")!,
            };
            _ = LoadAvatarAsync(acc);
        }
    }

    /// <summary>
    /// 拉取皮肤并裁剪头部作为头像（v0.1.5 全面对齐 PCL）：
    /// 有皮肤 → 皮肤脸部（NearestNeighbor 像素风缩放）；
    /// 无皮肤 / 无网络 → 按 UUID 奇偶回退 Steve / Alex 默认脸（与游戏内一致）；
    /// 全部失败 → 首字母圆形。
    /// </summary>
    private async Task LoadAvatarAsync(StoredAccount acc)
    {
        try
        {
            var skinUrl = await SkinService.GetSkinUrlAsync(acc, App.LauncherService.Http);
            byte[]? skin = skinUrl == null ? null : await SkinService.DownloadSkinAsync(skinUrl, App.LauncherService.Http);
            System.Windows.Media.Imaging.CroppedBitmap? cropped = null;
            if (skin != null)
            {
                // 封送回 UI 线程后解码裁剪：64×64 皮肤中头部位于 (8,8)-(16,16)
                cropped = Dispatcher.Invoke(() =>
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    using var ms = new MemoryStream(skin);
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    if (bmp.PixelWidth < 32 || bmp.PixelHeight < 32) return null;
                    return new System.Windows.Media.Imaging.CroppedBitmap(bmp,
                        new Int32Rect(8, 8, Math.Min(8, bmp.PixelWidth / 8), Math.Min(8, bmp.PixelHeight / 8)));
                });
            }
            // 无皮肤：Steve / Alex 默认脸（UUID 奇偶，PCL 同款；离线账号 uuid 亦适用）
            var facePath = SkinService.DefaultFacePath(acc.Uuid);
            var fallback = new System.Windows.Media.Imaging.BitmapImage();
            fallback.BeginInit();
            fallback.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            fallback.UriSource = new Uri(facePath, UriKind.Relative);
            fallback.EndInit();
            fallback.Freeze();
            Dispatcher.Invoke(() =>
            {
                RenderOptions.SetBitmapScalingMode(ImgAvatar, BitmapScalingMode.NearestNeighbor);
                ImgAvatar.Source = cropped ?? (ImageSource)fallback;
                ImgAvatar.Visibility = Visibility.Visible;
                TxtAvatar.Visibility = Visibility.Collapsed;
            });
        }
        catch { /* 网络异常等保持字母头像 */ }
    }

    /// <summary>从验证站地址提取主机名（如 littleskin.cn），用于账号类型第二行展示。</summary>
    private static string AuthlibHost(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl)) return "authlib-injector";
        try
        {
            var host = new Uri(serverUrl).Host;
            return string.IsNullOrWhiteSpace(host) ? serverUrl : host;
        }
        catch
        {
            // 非 URL 形式：去掉协议头后原样返回
            return serverUrl.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        }
    }

    private async Task RefreshVersionsAsync()
    {
        try
        {
            // v0.1.6：下拉框带图标与副标题（加载器类型 · 创建来源）
            var versions = App.LauncherService.GetInstalledVersions();
            var items = new List<VersionItemViewModel>();
            foreach (var v in versions)
            {
                var desc = VersionInfo.Describe(AppSettings.Instance.GameRoot, v);
                items.Add(new VersionItemViewModel(v, desc, BuildVersionSubtitle(desc)));
            }
            _versionItems = items;
            CmbVersions.ItemsSource = items;
            if (items.Count > 0)
            {
                var last = AppSettings.Instance.LastVersion;
                CmbVersions.SelectedValue = items.Any(i => i.Name == last) ? last : items[^1].Name;
            }
        }
        catch (Exception ex)
        {
            Log("读取版本失败: " + ex.Message);
        }
        await Task.CompletedTask;
    }

    /// <summary>版本副标题：加载器类型（原版 / Forge / …）+ 创建来源说明。</summary>
    private string BuildVersionSubtitle(VersionInfo.VersionDescriptor desc)
    {
        var loader = (string)FindResource(VersionInfo.LoaderKey(desc.Loader))!;
        return desc.Source switch
        {
            LauncherSource.Hcl => loader + " · " + (string)FindResource("S.Ver.FromHcl")!,
            LauncherSource.Pcl or LauncherSource.Hmcl => loader + " · " + (string)FindResource("S.Ver.FromOther")!,
            _ => loader,
        };
    }

    // ---------- 登录入口（v0.1.5 变更） ----------
    // 微软正版 / 第三方验证站 / 离线 三种登录已全部并入「账号管理」子页（AccountManager），
    // 主页只保留一个入口，与 PCL 的账户体验一致。原三个独立按钮及其事件处理已移除。

    // ---------- 账号管理面板（PCL 式：多账号切换 / 删除） ----------
    private void BtnManageAccounts_Click(object sender, RoutedEventArgs e)
    {
        var changed = AccountManager.Show(Window.GetWindow(this));
        if (changed) RefreshAccountUi();
    }

    // 点击头像 / 账号信息区域同样打开管理面板
    private void AccountCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => BtnManageAccounts_Click(sender, e);

    // ---------- 启动 ----------

    /// <summary>
    /// v0.1.8 紧急修复：启动前置失败 / 启动失败的反馈 —— PCL 式弹窗提示 + 自动展开日志。
    /// 此前这类原因只写进默认折叠的日志框，用户点「启动」看起来毫无反应。
    /// </summary>
    private void LaunchBlock(string msg)
    {
        Log(msg);
        SetLogOpen(true); // 日志自动展开，失败原因直接可见
        try { HmMsg.Info(Window.GetWindow(this), "无法启动", msg); } catch { /* 弹窗失败至少日志已展开 */ }
    }

    private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        var version = SelectedVersion;
        if (version == null)
        {
            LaunchBlock("请先选择或下载一个版本：可在「下载游戏」页安装，或点右上角刷新版本列表。");
            return;
        }
        await LaunchAsync(version);
    }

    /// <summary>v0.1.6：版本独立设置（版本选择行右侧齿轮，版本管理页同款入口）。</summary>
    private void BtnVersionSettings_Click(object sender, RoutedEventArgs e)
    {
        var version = SelectedVersion;
        if (version == null)
        {
            Log("请先选择一个版本，再打开版本设置。");
            return;
        }
        VersionSettingsWindow.Show(Window.GetWindow(this), version);
    }

    /// <summary>一键进服（v0.1.6 弹窗式）：地址 → 可选保存到多人游戏列表（servers.dat）→ 启动直连。</summary>
    private async void BtnQuickServer_Click(object sender, RoutedEventArgs e)
    {
        var version = SelectedVersion;
        if (version == null)
        {
            LaunchBlock("请先选择或下载一个版本。");
            return;
        }
        // 预填：版本独立设置的进服地址 > 上次使用的地址
        var preset = VersionSettingsStore.Get(version)?.QuickServer;
        if (string.IsNullOrWhiteSpace(preset)) preset = AppSettings.Instance.LastQuickServer;
        var r = JoinServerWindow.Show(Window.GetWindow(this), preset);
        if (r == null) return; // 用户取消

        if (r.SaveName != null)
            SaveServerToGameList(version, r.SaveName, r.Address);
        await LaunchAsync(version, r.Address);
    }

    /// <summary>把服务器写入游戏的多人游戏列表（servers.dat，NBT 大端序；版本隔离时写入版本目录内）。</summary>
    private void SaveServerToGameList(string version, string name, string ip)
    {
        try
        {
            var iso = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation);
            var path = ServerList.ServersDatPath(AppSettings.Instance.GameRoot, iso, version);
            var existing = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var updated = ServerList.AddOrUpdate(existing, name, ip);
            if (updated == null)
            {
                Log("[进服] servers.dat 无法解析，为保护现有列表未写入（不影响本次进服）。");
                return;
            }
            var err = ServerList.TryWrite(path, updated);
            if (err == null)
                Log($"[进服] 已保存到多人游戏列表：{name} → {ip}");
            else
                Log($"[进服] 保存多人游戏列表失败：{err}（不影响本次进服）");
        }
        catch (Exception ex)
        {
            Log("[进服] 保存多人游戏列表失败：" + ex.Message + "（不影响本次进服）");
        }
    }

    /// <summary>
    /// 启动指定版本（v0.1.6 开放为 public：版本管理页 / 快捷入口可直连调用）。
    /// </summary>
    public async Task LaunchAsync(string version, string? quickServer = null)
    {
        if (_launching) return;
        var acc = AccountStore.Instance.Current;
        if (acc == null)
        {
            LaunchBlock("请先登录账号：主页账号卡「账号管理」（微软正版 / 第三方验证站 / 离线均可）。");
            return;
        }

        _launching = true;
        LaunchProgressHub.Begin(version); // v0.1.1：进度入库（跨页面存活），本实例与新实例都会回放
        // v0.1.11：统一进度源（修复进度条乱蹦）—— 字节尺 / 文件尺合并 + PCL 式显示平滑
        _progressCalc = new LaunchProgressCalc();
        _shownPct = 0;
        try
        {
            // fix6：内存显示实际生效值（自动检测 / 手动；v0.1.6 起含版本独立设置覆盖），与启动日志一致
            var (memShow, memAutoShow) = VersionSettingsStore.EffectiveMemory(version, AppSettings.Instance);
            var isoShow = VersionSettingsStore.EffectiveIsolation(version, AppSettings.Instance.VersionIsolation);
            Log($"启动 {version}（隔离={isoShow}，内存={memShow}MB" +
                (memAutoShow ? "（自动检测）" : "") +
                (quickServer != null ? $"，进服={quickServer}" : "") + "）...");
            AppSettings.Instance.LastVersion = version;
            AppSettings.Instance.Save();

            var session = new MSession(acc.Name, acc.Type == "offline" ? "0" : acc.RefreshToken, acc.Uuid);
            string? authlibServer = null;
            // 微软账号：优先用 refresh_token 换新令牌；失败时回退缓存令牌兜底启动（v0.1.3 修复——
            // 此前任何一次刷新失败都直接 return，网络抖动 / Azure Client ID 丢失即永远无法进游戏）
            if (acc.Type == "microsoft")
            {
                var canRefresh = !string.IsNullOrWhiteSpace(AppSettings.Instance.AzureClientId)
                                 && !string.IsNullOrWhiteSpace(acc.RefreshToken);
                if (canRefresh)
                {
                    try
                    {
                        var r = await App.Auth.RefreshAsync(AppSettings.Instance.AzureClientId, acc.RefreshToken);
                        session = AuthService.ToSession(r);
                        var oldName = acc.Name;
                        acc.Name = r.Name;
                        acc.Uuid = r.Uuid;
                        acc.RefreshToken = r.RefreshToken;
                        acc.AccessToken = r.AccessToken;
                        if (oldName != r.Name) AccountStore.Instance.Remove(oldName, "microsoft");
                        AccountStore.Instance.Upsert(acc);
                    }
                    catch (Exception ex)
                    {
                        if (!string.IsNullOrWhiteSpace(acc.AccessToken))
                        {
                            Log("微软令牌刷新失败，改用缓存令牌启动（若游戏内提示无效会话，请重新登录微软账号）: " + ex.Message);
                            session = new MSession(acc.Name, acc.AccessToken, acc.Uuid);
                        }
                        else
                        {
                            LaunchBlock("微软令牌刷新失败且没有缓存令牌，已停止启动：请重新登录微软账号（正版登录还需在设置中填写 Azure Client ID）。\n\n" + ex.Message);
                            return;
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(acc.AccessToken))
                {
                    Log("未配置 Azure Client ID 或 refresh_token 已缺失，使用缓存令牌启动。");
                    session = new MSession(acc.Name, acc.AccessToken, acc.Uuid);
                }
                else
                {
                    LaunchBlock("微软账号缺少有效令牌：请重新登录微软账号（正版登录还需在设置中填写 Azure Client ID）。");
                    return;
                }
            }
            // 第三方验证站：校验令牌，失效则无感刷新
            else if (acc.Type == "authlib" && !string.IsNullOrWhiteSpace(acc.ServerUrl))
            {
                authlibServer = acc.ServerUrl;
                try
                {
                    var authlib = new AuthlibService(App.LauncherService.Http);
                    var ok = await authlib.ValidateAsync(acc.ServerUrl, acc.RefreshToken, acc.ClientToken);
                    if (!ok)
                    {
                        var r = await authlib.RefreshAsync(acc.ServerUrl, acc.RefreshToken, acc.ClientToken, null);
                        session = new MSession(r.Name, r.AccessToken, r.Uuid);
                        acc.RefreshToken = r.AccessToken;
                        AccountStore.Instance.Upsert(acc);
                        Log("第三方令牌已自动续期。");
                    }
                }
                catch (Exception ex)
                {
                    Log("第三方令牌校验/续期失败，请重新登录（仍会尝试以缓存令牌启动）: " + ex.Message);
                }
            }

            var taskPct = 0.0;
            // v0.1.10：任务带去重 Key —— 同一版本的启动任务重复登记时复用（修复任务中心重复任务）
            var centerTask = DownloadTaskService.Instance.StartCancellableKeyed(
                "launch:" + version,
                string.Format((string)FindResource("S.Task.Prepare"), version),
                (string)FindResource("S.Task.CheckingFiles"));
            _currentCenterTask = centerTask;
            _launchCt = DownloadTaskService.Instance.TokenFor(centerTask);
            var launchCt = _launchCt;
            BtnCancelLaunch.Visibility = Visibility.Visible;

            // fix7：阶段化进度（PCL 式）——进度区第二行显示「当前阶段【第几步/共几步】」；
            // 阶段 2/3 由 LauncherService 在补全文件 / 创建进程时回调触发
            void SetStage(int step, int total, string resourceKey, string fallback)
            {
                var name = App.Current.TryFindResource(resourceKey) as string ?? fallback;
                var txt = $"{name}【{step}/{total}】";
                LaunchProgressHub.Update(s => s.StageLine = txt);
                DownloadTaskService.Instance.Update(centerTask, taskPct, txt);
                Log(txt);
            }
            void SetStageStep(int step) => Dispatcher.Invoke(() =>
                SetStage(step, 4,
                    step == 2 ? "S.Home.Stage.Files"
                        : step == 3 ? "S.Home.Stage.Launch"
                        : "S.Home.Stage.WaitWindow",
                    step == 2 ? "正在补全游戏文件"
                        : step == 3 ? "正在启动游戏"
                        : "等待游戏窗口出现"));

            var filesSw = System.Diagnostics.Stopwatch.StartNew(); // v0.1.10：补全阶段耗时（估算剩余时间）
            var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(p =>
                Dispatcher.Invoke(() =>
                {
                    // v0.1.11：文件尺先入统一计算器，百分比由字节尺 / 文件尺仲裁（避免两把尺交替横跳）
                    _progressCalc?.OnFiles(p.ProgressedTasks, p.TotalTasks);
                    var pct = _progressCalc?.Compute();
                    if (pct == null) return;
                    taskPct = pct.Value;
                    // v0.1.10：按已耗时估算到启动完成（补全阶段）的剩余时间（PCL 式）
                    var eta = filesSw.Elapsed.TotalSeconds > 3 && pct.Value >= 3
                        ? SpeedMeter.FormatEtaSeconds(filesSw.Elapsed.TotalSeconds / pct.Value * (100 - pct.Value))
                        : "";
                    var line = $"[{p.ProgressedTasks}/{p.TotalTasks}] {p.Name}" +
                               (eta.Length > 0 ? " · " + eta : "");
                    ShowLaunchProgress(pct.Value, line, centerTask);
                }));
            var meter = new SpeedMeter();
            var byteProgress = new Progress<CmlLib.Core.ByteProgress>(p =>
                Dispatcher.Invoke(() =>
                {
                    if (p.TotalBytes <= 0) return;
                    var speed = meter.Observe(p.ProgressedBytes);
                    _progressCalc?.OnBytes(p.ProgressedBytes, p.TotalBytes);
                    var pct = _progressCalc?.Compute();
                    if (pct == null) return;
                    taskPct = pct.Value;
                    // v0.1.10：剩余时间（按字节与当前速度估算，到启动完成的下载部分）
                    var eta = SpeedMeter.FormatEta(p.TotalBytes - p.ProgressedBytes, speed);
                    var line = $"{SpeedMeter.FormatBytes(p.ProgressedBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)} · {SpeedMeter.FormatSpeed(speed)}" +
                               (eta.Length > 0 ? " · " + eta : "");
                    ShowLaunchProgress(pct.Value, line, centerTask);
                }));

            SetStage(1, 4, "S.Home.Stage.CheckJava", "正在检查 Java 环境");
            // v0.1.10：启动文案更详细 —— 明确告知 Java 要求与推荐值
            // fix5：沿 inheritsFrom 链解析所需 Java 大版本（子版本 json 常缺 javaVersion）
            var requiredMajor = JavaManager.RequiredJavaMajorChain(
                AppSettings.Instance.GameRoot, version);
            Log($"[启动] 本版本需要 Java {requiredMajor}，推荐 Java {JavaManager.RecommendedJavaMajor(AppSettings.Instance.GameRoot, version)}（当前版本支持的最高版本）。");
            string? javaToUse = null;
            // v0.1.6：版本独立 Java 路径优先（版本设置中指定；无效时回落全局设置）
            var verJavaSetting = VersionSettingsStore.Get(version)?.JavaPath;
            if (!string.IsNullOrWhiteSpace(verJavaSetting))
            {
                var resolvedVerJava = JavaManager.ResolveJavaPath(verJavaSetting);
                if (resolvedVerJava != null)
                {
                    javaToUse = resolvedVerJava;
                    var verJavaMajor = JavaManager.GetJavaMajor(resolvedVerJava);
                    Log($"[启动] 使用版本独立 Java: {resolvedVerJava}" +
                        (verJavaMajor > 0 ? $" (Java {verJavaMajor})" : ""));
                    if (verJavaMajor > 0 && verJavaMajor < requiredMajor)
                        Log($"[警告] 版本独立 Java 为 {verJavaMajor}，低于本版本所需的 Java {requiredMajor}，游戏可能启动失败；可在版本设置中修改。");
                }
                else
                {
                    Log($"[警告] 版本独立 Java 路径无效（{verJavaSetting}），改用全局设置。可在版本设置中修改。");
                }
            }
            var userJava = AppSettings.Instance.JavaPath;
            // fix5：手选 Java 支持填文件夹 / java.exe（自动递归找 javaw.exe），
            // 且选中后只要存在就直接使用，不再误入自动安装弹窗
            // v0.1.6：版本独立 Java 已解析成功时跳过全局路径（不覆盖）
            var resolvedUserJava = javaToUse == null ? JavaManager.ResolveJavaPath(userJava) : null;
            if (resolvedUserJava != null)
            {
                javaToUse = resolvedUserJava;
                var foundMajor = JavaManager.GetJavaMajor(resolvedUserJava);
                Log($"[启动] 使用设置中指定的 Java: {resolvedUserJava}" +
                    (foundMajor > 0 ? $" (Java {foundMajor})" : ""));
                if (foundMajor > 0 && foundMajor < requiredMajor)
                    Log($"[警告] 所选 Java 为 {foundMajor}，低于本版本所需的 Java {requiredMajor}，" +
                        "游戏可能启动失败；如遇问题请在设置中更换或让启动器自动安装。");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(userJava))
                    Log($"[警告] 设置中的 Java 路径无效（{userJava}，可填 javaw.exe 或其所在文件夹），改为自动选择/下载。");
                javaToUse = JavaManager.PickJavaFor(version, AppSettings.Instance.GameRoot, null, true);
                if (javaToUse != null)
                    Log($"[启动] 已自动选择托管 Java: {javaToUse}");
            }
            if (javaToUse == null && AppSettings.Instance.AutoJavaSelect)
            {
                // fix3（PCL 同款）：缺 Java 时先弹窗确认——告知所需版本与默认安装位置，
                // 用户点「安装并启动」后才下载；此前是不打招呼直接开下（v0.1.3 更早版本则直接抛异常）。
                // fix5：弹窗内可选 Java 大版本。
                // v0.1.9：默认推荐「当前版本支持使用的最高 Java」（而非最低所需版本）。
                var recommendedMajor = JavaManager.RecommendedJavaMajor(
                    AppSettings.Instance.GameRoot, version);
                var ask = JavaInstallWindow.Ask(Window.GetWindow(this), requiredMajor, version, recommendedMajor);
                if (ask == null)
                {
                    Log("已取消 Java 安装，本次未启动游戏。可在 设置 → Java 一键安装 手动安装，或在设置中填写本机已装的 Java 路径（支持文件夹）。");
                    return;
                }
                var (installMajor, askDir) = ask.Value;
                Log($"未找到可用的 Java {installMajor}，开始下载安装到 {askDir}（仅首次需要）...");
                var jTask = DownloadTaskService.Instance.StartKeyed($"java-install:{installMajor}",
                    $"下载 Java {installMajor} (Temurin JRE)",
                    (string)FindResource("S.Task.CheckingFiles"));
                try
                {
                    // v0.1.10：Java 下载也显示速度与剩余时间（此前只有百分比）
                    var jp = new Progress<JavaManager.JavaDownloadProgress>(p => Dispatcher.Invoke(() =>
                    {
                        var eta = SpeedMeter.FormatEta(p.TotalBytes - p.DoneBytes, p.SpeedBps);
                        var line = $"下载 Java {installMajor}: {p.Percent:0}% · " +
                                   $"{SpeedMeter.FormatBytes(p.DoneBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)} · " +
                                   SpeedMeter.FormatSpeed(p.SpeedBps) +
                                   (eta.Length > 0 ? " · " + eta : "");
                        LaunchProgressHub.Update(s =>
                        {
                            s.Indeterminate = false;
                            s.Percent = p.Percent;
                            s.ProgressLine = line;
                            s.ButtonText = $"Java {p.Percent:0}%";
                        });
                        DownloadTaskService.Instance.Update(jTask, p.Percent, line);
                    }));
                    javaToUse = await JavaManager.EnsureJreAsync(installMajor, App.LauncherService.Http, jp, Log, askDir, launchCt);
                    DownloadTaskService.Instance.Complete(jTask, "Java 安装完成");
                    Log($"[启动] Java {installMajor} 自动安装完成。");
                }
                catch (OperationCanceledException) when (launchCt.IsCancellationRequested)
                {
                    DownloadTaskService.Instance.Cancel(jTask, "已取消");
                    Log("Java 安装已取消。");
                    throw; // 交给外层统一收尾（恢复按钮 / 隐藏取消按钮）
                }
                catch (Exception jex)
                {
                    DownloadTaskService.Instance.Fail(jTask, "Java 下载失败: " + jex.Message);
                    LaunchBlock("Java 自动安装失败：" + jex.Message +
                        "\n\n请到「设置 → Java 一键安装」重试，或在「设置 → Java 路径」填写本机已安装的 Java 后重试。");
                    return;
                }
            }
            if (javaToUse == null && !JavaManager.ExistsOnPath())
            {
                LaunchBlock("本机未找到 Java：请在「设置 → Java 一键安装」（自动下载），或在设置中填写已安装的 Java 路径。");
                return;
            }

            Log("正在检查游戏文件（主 jar / 资源 / 库）——首次启动约需下载 300–500 MB，可在任务中心查看实时速度与剩余时间...");
            LaunchProgressHub.Update(s => s.ButtonText = (string)FindResource("S.Home.Starting"));
            // 离线皮肤（PCL 式）：离线账号设置过皮肤时，文件校验完成后注入 client jar
            string? offlineSkinPath = acc.Type == "offline"
                && !string.IsNullOrWhiteSpace(acc.SkinPath) && System.IO.File.Exists(acc.SkinPath)
                ? acc.SkinPath : null;
            if (offlineSkinPath != null)
                Log("[皮肤] 已启用离线皮肤（仅本机单机可见；联机需皮肤站/模组）。");
            var process = await App.LauncherService.LaunchAsync(version, session, fileProgress, byteProgress,
                line => Log(line), authlibServer, javaToUse, offlineSkinPath, quickServer, SetStageStep, launchCt);
            // fix6：记住本次快速进服地址（下次打开自动填充）
            if (quickServer != null && AppSettings.Instance.LastQuickServer != quickServer)
            {
                AppSettings.Instance.LastQuickServer = quickServer;
                AppSettings.Instance.Save();
            }
            DownloadTaskService.Instance.Complete(centerTask, (string)FindResource("S.Task.Ready"));
            _currentCenterTask = null;
            Log("游戏进程已创建，PID = " + process.Process?.Id);
            // v0.1.7：注册进多实例管理（主页「运行中的实例」卡实时可见，可结束进程树）
            GameInstanceManager.Register(version, process);
            // 记录最近启动（主页快捷启动，最新在前）
            AppSettings.Instance.RecordLaunch(version);
            RefreshRecent();
            // v0.1.7：启动流程本体到此结束（可立即再启动第二个实例），
            // 进度区交给后台等待器接管 —— 等游戏窗口出现后再恢复空闲 UI，
            // 消除「显示已启动 → MC 窗口出现」之间没有任何提示的时间差
            _waitingWindow = true;
            _ = WatchGameWindowAsync(version, process);

            // 启动行为（HMCL 同款）：游戏启动后隐藏启动器 / 游戏退出后关闭启动器 / 异常退出时定位崩溃报告
            var launcherWindow = Window.GetWindow(this);
            if (AppSettings.Instance.HideLauncherOnGameStart)
            {
                Log("游戏已启动，启动器已隐藏（游戏退出时自动恢复）。可在设置中关闭此行为。");
                launcherWindow?.Hide();
            }
            process.Exited += (_, _) => Dispatcher.Invoke(() =>
            {
                var code = -1;
                try
                {
                    if (process.Process?.HasExited == true) code = process.Process.ExitCode;
                }
                catch { /* 进程对象已释放时按 -1 处理 */ }

                if (AppSettings.Instance.HideLauncherOnGameStart)
                {
                    launcherWindow?.Show();
                    launcherWindow?.Activate();
                }

                if (CrashHelper.IsAbnormalExit(code))
                {
                    var latest = CrashHelper.FindLatestCrashReport(AppSettings.Instance.GameRoot);
                    Log($"[退出] 游戏异常退出（退出代码 {code}）" +
                        (latest != null
                            ? $"，最新崩溃报告：{Path.GetFileName(latest)}"
                            : "，未在 crash-reports 目录找到崩溃报告"));
                    Log("[提示] 可点击日志区上方「崩溃报告」按钮查看完整报告。");
                }

                if (AppSettings.Instance.ExitLauncherOnGameExit)
                    Application.Current.Shutdown();
            });
        }
        catch (OperationCanceledException) when (_launchCt.IsCancellationRequested)
        {
            // fix7：用户取消启动（取消按钮 / 任务中心）—— 收尾并恢复 UI（PCL/HMCL 同款）
            Log("启动已取消。");
            LaunchProgressHub.Update(s =>
            {
                s.Indeterminate = false;
                s.Percent = 0;
                s.ProgressLine = "已取消";
            });
            if (_currentCenterTask != null)
                DownloadTaskService.Instance.Cancel(_currentCenterTask, "已取消");
            _currentCenterTask = null;
        }
        catch (Exception ex)
        {
            LaunchBlock("启动失败：" + ex.Message + "\n\n详情见下方日志（若反复失败，可把日志发给开发者排查）。");
            LaunchProgressHub.Update(s =>
            {
                s.Indeterminate = false;
                s.Percent = 0;
                s.ProgressLine = "启动失败：" + ex.Message;
            });
            if (_currentCenterTask != null)
                DownloadTaskService.Instance.Fail(_currentCenterTask, "启动失败: " + ex.Message);
            _currentCenterTask = null;
        }
        finally
        {
            _launching = false;
            // v0.1.7：进程已创建时进度区由窗口等待器接管，此处不复位（等待器结束后再恢复空闲 UI）
            if (!_waitingWindow)
                LaunchProgressHub.End(); // v0.1.1：复位快照 → 所有实例恢复空闲 UI（含切页后的新实例）
            _launchCt = default;
            // v0.1.3：启动流程（含补全 / 安装 / 取消）收尾后回收内存 —— 补全几千个文件的
            // 下载缓冲与 JSON 解析残留集中释放，不阻塞收尾渲染（内部延迟 1.5s）
            Services.MemoryManager.OptimizeAfterHeavyOp();
        }
    }

    /// <summary>
    /// v0.1.7：窗口等待器 —— 游戏进程创建后轮询等待窗口出现（对齐主流启动器的启动进度）。
    /// - 窗口出现：实例标记为运行中，进度区短暂展示「游戏运行中」后恢复空闲 UI；
    /// - 进程提前退出：进度区展示退出码后恢复空闲；
    /// - 超时（进程仍存活）：按运行中处理（少数环境拿不到窗口句柄）。
    /// 若期间用户又发起了新的启动（多实例），本等待器让出进度区，不再改写它。
    /// </summary>
    private async Task WatchGameWindowAsync(string version, CmlLib.Core.ProcessBuilder.ProcessWrapper process)
    {
        var ownsHub = () => LaunchProgressHub.Current.Running && LaunchProgressHub.Current.Version == version;
        try
        {
            var waitText = App.Current.TryFindResource("S.Home.WaitWindow") as string ?? "正在等待游戏窗口出现…";
            var waitHint = App.Current.TryFindResource("S.Home.WaitWindowHint") as string ?? "首次启动加载较慢，属正常现象";
            var stageText = $"{(App.Current.TryFindResource("S.Home.Stage.WaitWindow") as string ?? "等待游戏窗口出现")}【4/4】";
            if (ownsHub())
            {
                LaunchProgressHub.Update(s =>
                {
                    s.Indeterminate = true;
                    s.CancelVisible = false; // 游戏进程已创建，取消改为在实例卡里结束进程
                    s.ProgressLine = waitText;
                    s.StageLine = stageText;
                    s.ButtonText = App.Current.TryFindResource("S.Home.Starting") as string ?? "启动中";
                });
                Log(stageText);
                Log("[启动] " + waitHint);
            }

            var sw = Stopwatch.StartNew();
            bool windowSeen = false;
            while (true)
            {
                await Task.Delay(700);
                var decision = GameWindowWait.Decide(process.Process?.HasExited ?? true, windowSeen,
                    sw.Elapsed, WindowWaitTimeout);
                if (decision == GameWindowWait.Decision.KeepWaiting)
                {
                    if (!windowSeen)
                    {
                        var inst = GameInstanceManager.Instances
                            .FirstOrDefault(i => i.Pid == (process.Process?.Id ?? -1));
                        if (inst != null && inst.TryPeekWindow())
                            windowSeen = true;
                    }
                    continue;
                }
                if (decision == GameWindowWait.Decision.Exited)
                {
                    var code = -1;
                    try { if (process.Process?.HasExited == true) code = process.Process.ExitCode; }
                    catch { /* 进程对象不可用 */ }
                    Log($"[退出] 游戏在窗口出现前就退出了（退出代码 {code}）。可展开日志查看游戏输出排查原因。");
                    if (ownsHub())
                        LaunchProgressHub.Update(s =>
                        {
                            s.Indeterminate = false;
                            s.Percent = 0;
                            s.ProgressLine = $"游戏已退出（退出代码 {code}）";
                        });
                    return;
                }
                if (decision == GameWindowWait.Decision.TimedOut)
                    Log("[启动] 长时间未检测到游戏窗口，按运行中处理（游戏可能仍在后台加载）。");
                else
                    Log("[启动] 游戏窗口已出现。");
                // 窗口出现 / 超时：标记实例为运行中，短暂展示终态后恢复空闲 UI
                var inst2 = GameInstanceManager.Instances.FirstOrDefault(i => i.Pid == (process.Process?.Id ?? -1));
                inst2?.MarkRunning();
                var runningText = App.Current.TryFindResource("S.Home.GameRunning") as string
                    ?? "游戏运行中，可关闭窗口（日志继续记录）";
                if (ownsHub())
                {
                    LaunchProgressHub.Update(s =>
                    {
                        s.Indeterminate = false;
                        s.Percent = 100;
                        s.ProgressLine = runningText;
                    });
                    await Task.Delay(700);
                }
                return;
            }
        }
        catch { /* 等待器异常不外泄（进程退出事件会兜底处理收尾） */ }
        finally
        {
            _waitingWindow = false;
            if (ownsHub())
                LaunchProgressHub.End(); // LastProgressLine 保留终态（游戏运行中 / 已退出）供空闲回放
        }
    }

    /// <summary>fix7：取消启动（下载/安装/补全文件阶段生效；游戏进程创建后不可取消）。
    /// v0.1.1：改由进度中枢广播 —— 任何页面实例上的取消按钮都能命中同一个启动流程。</summary>
    private void BtnCancelLaunch_Click(object sender, RoutedEventArgs e)
    {
        LaunchProgressHub.RequestCancel();
    }

    /// <summary>v0.1.6：供版本管理页等外部入口直接启动指定版本（导航回主页后直连调用）。</summary>
    public Task LaunchFromExternalAsync(string version) => LaunchAsync(version);

    private async void BtnRefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        await RefreshVersionsAsync();
    }

    private void OpenFolderSafe(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("打开目录失败: " + ex.Message);
        }
    }

    private void BtnOpenLogs_Click(object sender, RoutedEventArgs e) =>
        OpenFolderSafe(CrashHelper.LogsDir(AppSettings.Instance.GameRoot));

    private void BtnOpenCrash_Click(object sender, RoutedEventArgs e) =>
        OpenFolderSafe(CrashHelper.CrashReportsDir(AppSettings.Instance.GameRoot));

    private void TxtLog_TextChanged(object sender, TextChangedEventArgs e)
    {
        // 依赖 TextChanged 让 TextBox 滚动到底（ScrollToEnd 已在 Log 中调用）
    }

    // ===== 首页 UI：日志区展开/收起 =====

    private void BtnToggleLog_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SetLogOpen(PnlLogBox.Visibility == Visibility.Collapsed);

    /// <summary>展开 / 收起日志卡（v0.1.8：从事件处理器拆出，供启动失败时自动展开）。
    /// v0.1.11：展开状态持久化到 config（切页 / 重启后恢复，不再切个标签就折叠）。</summary>
    private void SetLogOpen(bool open)
    {
        PnlLogBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        // v0.1.7：展开时日志卡撑满中部剩余空间；收起时回到一条紧凑标题栏
        CardLog.VerticalAlignment = open ? VerticalAlignment.Stretch : VerticalAlignment.Top;
        IcoLogToggle.Symbol = open ? SymbolRegular.ChevronUp24 : SymbolRegular.ChevronDown24;
        if (AppSettings.Instance.HomeLogOpen != open)
        {
            AppSettings.Instance.HomeLogOpen = open;
            AppSettings.Instance.Save();
        }
    }
}

/// <summary>简单输入框（FluentWindow：跟随应用深浅色主题）。</summary>
public static class Prompt
{
    public static string? Show(string caption, string title, Window? owner = null)
    {
        string? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = title,
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
        };
        // v1.7.2 修复：owner 必须显式传入并排除自身，否则会抛
        // "Cannot set Owner property to itself"（离线登录点击即崩的根因）
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>()
            .LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win))
            win.Owner = owner;

        var sp = new StackPanel { Margin = new Thickness(20) };
        var caption1 = new System.Windows.Controls.TextBlock
        {
            Text = caption,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
        };
        caption1.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorPrimaryBrush");
        var tb = new Wpf.Ui.Controls.TextBox { MinHeight = 36, Margin = new Thickness(0, 12, 0, 14) };
        var btn = new Wpf.Ui.Controls.Button
        {
            Content = "OK",
            Appearance = ControlAppearance.Primary,
            MinHeight = 36,
            MinWidth = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        sp.Children.Add(caption1);
        sp.Children.Add(tb);
        sp.Children.Add(btn);
        FluentDialog.SetContent(win, sp); // fix5：挂标题栏（含关闭按钮）

        btn.Click += (_, _) => { result = tb.Text; win.Close(); };
        tb.KeyDown += (_, e2) =>
        {
            if (e2.Key == System.Windows.Input.Key.Enter) { result = tb.Text; win.Close(); }
        };
        win.ShowDialog();
        return result;
    }
}

/// <summary>离线输入对话框内部（保留备用）。</summary>
public class OfflineInputDialog : System.Windows.Controls.UserControl
{
    public Wpf.Ui.Controls.TextBox NameBox { get; } = new() { MinHeight = 34 };
    public OfflineInputDialog()
    {
        Content = NameBox;
    }
}

/// <summary>
/// v0.1.8 紧急修复：消息 / 确认对话框（FluentWindow，跟随深浅色主题）。
/// 关键反馈（无法启动 / 启动失败 / 安装完成）必须弹窗可见 —— PCL 同款，
/// 不再只写进默认折叠的日志框让用户以为点了没反应。
/// </summary>
public static class HmMsg
{
    public static void Info(Window? owner, string title, string msg) => Show(owner, title, msg, true);

    public static bool Confirm(Window? owner, string title, string msg) => Show(owner, title, msg, false);

    /// <returns>info 模式恒为 true；confirm 模式返回用户是否点了「启动 / 确定」。</returns>
    private static bool Show(Window? owner, string title, string msg, bool infoOnly)
    {
        var confirmed = false;
        var height = 170 + Math.Min(160, 20 * Math.Max(1, msg.Count(c => c == '\n')));

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = title,
            Width = 480,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= System.Windows.Application.Current.Windows.OfType<Window>()
            .LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win))
            win.Owner = owner;

        var sp = new StackPanel { Margin = new Thickness(20) };
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = msg,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            Margin = new Thickness(0, 0, 0, 16),
        };
        tb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = infoOnly ? "确定" : "启动",
            Appearance = ControlAppearance.Primary,
            MinHeight = 34,
            MinWidth = 110,
        };
        btnRow.Children.Add(btnOk);
        if (!infoOnly)
        {
            var btnCancel = new Wpf.Ui.Controls.Button
            {
                Content = "取消",
                MinHeight = 34,
                MinWidth = 90,
                Margin = new Thickness(8, 0, 0, 0),
            };
            btnCancel.Click += (_, _) => win.Close();
            btnRow.Children.Add(btnCancel);
        }
        btnOk.Click += (_, _) => { confirmed = true; win.Close(); };

        sp.Children.Add(tb);
        sp.Children.Add(btnRow);
        FluentDialog.SetContent(win, sp); // 挂标题栏（含关闭按钮）
        win.ShowDialog();
        return confirmed;
    }
}
