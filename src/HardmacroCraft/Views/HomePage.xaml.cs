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
    private object? _btnLaunchDefault;

    public HomePage()
    {
        InitializeComponent();
        _btnLaunchDefault = BtnLaunch.Content;
        // v0.1.1：订阅启动进度中枢 —— 启动中切页再回来，新实例立即回放当前进度
        LaunchProgressHub.Changed += ApplyLaunchUi;
        LaunchProgressHub.CancelRequested += OnHubCancelRequested;
        Unloaded += (_, _) =>
        {
            LaunchProgressHub.Changed -= ApplyLaunchUi;
            LaunchProgressHub.CancelRequested -= OnHubCancelRequested;
        };
        ApplyLaunchUi();
        Loaded += async (_, _) =>
        {
            if (!_loaded)
            {
                _loaded = true;
                await RefreshVersionsAsync();
                RefreshAccountUi();
                RefreshRecent();
                // fix6：快速进服地址记忆（PCL 同款）
                TxtQuickServer.Text = AppSettings.Instance.LastQuickServer;
                // 自动登录（PCL 同款）：记住密码 + 自动登录的验证站账号静默重登
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

    /// <summary>v0.1.1：把启动进度中枢的快照回放到本实例的控件上。</summary>
    private void ApplyLaunchUi()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.Invoke(ApplyLaunchUi); return; }
        var s = LaunchProgressHub.Current;
        if (!s.Running)
        {
            BtnLaunch.Content = _btnLaunchDefault;
            BtnLaunch.IsEnabled = true;
            BtnCancelLaunch.Visibility = Visibility.Collapsed;
            BtnCancelLaunch.IsEnabled = true;
            TxtStage.Visibility = Visibility.Collapsed;
            // v0.1.2：空闲时回放上次启动的终态文案（已取消 / 启动失败 / 游戏运行中），
            // 与启动结束时的旧实例显示保持一致（此前切页回来是空白）
            TxtProgress.Text = LaunchProgressHub.LastProgressLine;
            return;
        }
        BtnLaunch.Content = string.IsNullOrEmpty(s.ButtonText) ? _btnLaunchDefault : s.ButtonText;
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

    /// <summary>主页最近启动快捷键（PCL/HMCL 式）。</summary>
    private void RefreshRecent()
    {
        PnlRecent.Children.Clear();
        var recent = AppSettings.Instance.RecentVersions;
        CardRecent.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var v in recent)
        {
            var installed = App.LauncherService.GetInstalledVersions().Contains(v);
            var chip = new Wpf.Ui.Controls.Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new System.Windows.Controls.Image
                        {
                            Source = new System.Windows.Media.Imaging.BitmapImage(
                                new Uri("pack://application:,,,/Assets/grass.png")),
                            Width = 18, Height = 18,
                            Stretch = Stretch.Uniform,
                        },
                        new System.Windows.Controls.TextBlock
                        {
                            Text = v, VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(7, 0, 0, 0),
                            Opacity = installed ? 1.0 : 0.45,
                        },
                    },
                },
                MinHeight = 36,
                Margin = new Thickness(0, 0, 8, 8),
                ToolTip = installed ? v : v + "（已不存在，点击重试）",
            };
            chip.Click += async (_, _) =>
            {
                if (CmbVersions.Items.Contains(v)) CmbVersions.SelectedItem = v;
                await LaunchAsync(v);
            };
            PnlRecent.Children.Add(chip);
        }
    }

    private void Log(string line)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        });
    }

    private void RefreshAccountUi()
    {
        var acc = AccountStore.Instance.Current;
        if (acc == null)
        {
            TxtAvatar.Text = "?";
            ImgAvatar.Visibility = Visibility.Collapsed;
            TxtAccount.Text = (string)FindResource("S.Home.NoAccount")!;
            TxtAccountType.Text = (string)FindResource("S.Home.NoAccountHint")!;
        }
        else
        {
            TxtAvatar.Text = string.IsNullOrWhiteSpace(acc.Name) ? "?" : acc.Name[..1].ToUpperInvariant();
            TxtAccount.Text = acc.Name;
            TxtAccountType.Text = acc.Type switch
            {
                "microsoft" => (string)FindResource("S.Home.AccountMs")!,
                "authlib" => (string)FindResource("S.Home.AccountThird")!,
                _ => (string)FindResource("S.Home.AccountOffline")!,
            };
            _ = LoadAvatarAsync(acc);
        }
    }

    /// <summary>拉取皮肤并裁剪头部作为头像（失败静默回退首字母）。</summary>
    private async Task LoadAvatarAsync(StoredAccount acc)
    {
        try
        {
            var skinUrl = await SkinService.GetSkinUrlAsync(acc, App.LauncherService.Http);
            if (skinUrl == null) return;
            var skin = await SkinService.DownloadSkinAsync(skinUrl, App.LauncherService.Http);
            if (skin == null) return;
            // 封送回 UI 线程后解码裁剪：64×64 皮肤中头部位于 (8,8)-(16,16)
            var cropped = Dispatcher.Invoke(() =>
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
            if (cropped == null) return;
            ImgAvatar.Source = cropped;
            ImgAvatar.Visibility = Visibility.Visible;
            TxtAvatar.Visibility = Visibility.Collapsed;
        }
        catch { /* 网络异常等保持字母头像 */ }
    }

    private async Task RefreshVersionsAsync()
    {
        try
        {
            var versions = App.LauncherService.GetInstalledVersions();
            CmbVersions.ItemsSource = versions;
            if (versions.Count > 0)
            {
                var last = AppSettings.Instance.LastVersion;
                CmbVersions.SelectedItem = versions.Contains(last) ? last : versions[^1];
            }
        }
        catch (Exception ex)
        {
            Log("读取版本失败: " + ex.Message);
        }
        await Task.CompletedTask;
    }

    // ---------- 微软正版登录（PCL 式设备码） ----------
    private void BtnMicrosoftLogin_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(AppSettings.Instance.AzureClientId))
        {
            // v0.1.3：不再只丢一句日志——弹出分步引导，教用户免费注册拿 ID
            Log("未配置 Azure Client ID：已打开获取引导（免费，约 3 分钟）...");
            new AzureGuideWindow { Owner = Window.GetWindow(this) }.ShowDialog();
            return;
        }
        var dlg = new MicrosoftLoginWindow(AppSettings.Instance.AzureClientId) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        RefreshAccountUi();
        Log(dlg.ResultName != null ? $"微软正版登录成功: {dlg.ResultName}" : "微软登录已取消。");
    }

    // ---------- 第三方验证站登录（authlib-injector，PCL 同款） ----------
    private void BtnAuthlibLogin_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new AuthlibLoginWindow(new AuthlibService(App.LauncherService.Http)) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        RefreshAccountUi();
        Log(dlg.Result != null
            ? $"第三方登录成功: {dlg.Result.Name}（{(string)FindResource("S.Authlib.Title")}）"
            : "第三方登录已取消。");
    }

    private void BtnOfflineLogin_Click(object sender, RoutedEventArgs e)
    {
        // 登录系统全面检查：改用带实时校验的专用窗口（非法输入就地红字提示）。
        // 此前通用 Prompt + 静默丢弃非法名：填错点确定毫无反应，像按钮坏了。
        var current = AccountStore.Instance.Current;
        var name = OfflineNameWindow.Show(Window.GetWindow(this),
            current is { Type: "offline" } ? current.Name : null);
        if (name == null) return;
        var session = AuthService.OfflineSession(name);
        AccountStore.Instance.Upsert(new StoredAccount
        {
            Type = "offline", Name = session.Username, Uuid = session.UUID,
        });
        RefreshAccountUi();
        Log("离线登录: " + session.Username);
    }

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
    private async void BtnLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (CmbVersions.SelectedItem is not string version)
        {
            Log("请先选择或下载一个版本。");
            return;
        }
        await LaunchAsync(version);
    }

    /// <summary>快速进服（fix6，PCL 同款）：填了服务器地址时，启动后直接连入该服务器。</summary>
    private async void BtnQuickServer_Click(object sender, RoutedEventArgs e)
    {
        var server = TxtQuickServer.Text.Trim();
        if (server.Length == 0)
        {
            Log("请先填写服务器地址（如 mc.example.com 或 mc.example.com:25565）。");
            return;
        }
        if (CmbVersions.SelectedItem is not string version)
        {
            Log("请先选择或下载一个版本。");
            return;
        }
        await LaunchAsync(version, server);
    }

    private async Task LaunchAsync(string version, string? quickServer = null)
    {
        if (_launching) return;
        var acc = AccountStore.Instance.Current;
        if (acc == null)
        {
            Log("请先登录账号（微软正版或离线）。");
            return;
        }

        _launching = true;
        LaunchProgressHub.Begin(version); // v0.1.1：进度入库（跨页面存活），本实例与新实例都会回放
        try
        {
            // fix6：内存显示实际生效值（自动检测 / 手动），与启动日志一致
            var memShow = AppSettings.Instance.MemoryAuto
                ? (int)Services.SystemInfo.RecommendedMemoryMb()
                : AppSettings.Instance.MemoryMb;
            Log($"启动 {version}（隔离={AppSettings.Instance.VersionIsolation}，内存={memShow}MB" +
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
                            Log("微软令牌刷新失败且没有缓存令牌，已停止启动，请重新登录微软账号: " + ex.Message);
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
                    Log("微软账号缺少有效令牌：请重新登录微软账号（正版登录还需在设置中填写 Azure Client ID）。");
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
            var centerTask = DownloadTaskService.Instance.StartCancellable(
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
                SetStage(step, 3,
                    step == 2 ? "S.Home.Stage.Files" : "S.Home.Stage.Launch",
                    step == 2 ? "正在补全游戏文件" : "正在启动游戏"));

            var fileProgress = new Progress<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>(p =>
                Dispatcher.Invoke(() =>
                {
                    var pct = p.TotalTasks > 0 ? p.ProgressedTasks * 100.0 / p.TotalTasks : 0;
                    taskPct = pct;
                    // v0.1.1：写进进度中枢（本实例与导航切换后的新实例同步回放）
                    LaunchProgressHub.Update(s =>
                    {
                        s.Indeterminate = false;
                        s.Percent = pct;
                        s.ProgressLine = $"[{p.ProgressedTasks}/{p.TotalTasks}] {p.Name}";
                        s.ButtonText = $"下载中 {pct:0}%";
                    });
                    DownloadTaskService.Instance.Update(centerTask, pct, $"[{p.ProgressedTasks}/{p.TotalTasks}] {p.Name}");
                }));
            var meter = new SpeedMeter();
            var byteProgress = new Progress<CmlLib.Core.ByteProgress>(p =>
                Dispatcher.Invoke(() =>
                {
                    if (p.TotalBytes <= 0) return;
                    var pct = p.ProgressedBytes * 100.0 / p.TotalBytes;
                    LaunchProgressHub.Update(s =>
                    {
                        s.Indeterminate = false;
                        s.Percent = pct;
                        s.ProgressLine = $"下载中 {pct:0}% · {SpeedMeter.FormatBytes(p.ProgressedBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)} · {SpeedMeter.FormatSpeed(meter.Observe(p.ProgressedBytes))}";
                        s.ButtonText = $"下载中 {pct:0}%";
                    });
                    DownloadTaskService.Instance.Update(centerTask, pct,
                        $"{SpeedMeter.FormatBytes(p.ProgressedBytes)} / {SpeedMeter.FormatBytes(p.TotalBytes)}");
                }));

            SetStage(1, 3, "S.Home.Stage.CheckJava", "正在检查 Java 环境");
            // fix5：沿 inheritsFrom 链解析所需 Java 大版本（子版本 json 常缺 javaVersion）
            var requiredMajor = JavaManager.RequiredJavaMajorChain(
                AppSettings.Instance.GameRoot, version);
            string? javaToUse = null;
            var userJava = AppSettings.Instance.JavaPath;
            // fix5：手选 Java 支持填文件夹 / java.exe（自动递归找 javaw.exe），
            // 且选中后只要存在就直接使用，不再误入自动安装弹窗
            var resolvedUserJava = JavaManager.ResolveJavaPath(userJava);
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
                // fix5：弹窗内可选 Java 大版本（默认推荐所需版本）。
                var ask = JavaInstallWindow.Ask(Window.GetWindow(this), requiredMajor, version);
                if (ask == null)
                {
                    Log("已取消 Java 安装，本次未启动游戏。可在 设置 → Java 一键安装 手动安装，或在设置中填写本机已装的 Java 路径（支持文件夹）。");
                    return;
                }
                var (installMajor, askDir) = ask.Value;
                Log($"未找到可用的 Java {installMajor}，开始下载安装到 {askDir}（仅首次需要）...");
                var jTask = DownloadTaskService.Instance.Start($"下载 Java {installMajor} (Temurin JRE)",
                    (string)FindResource("S.Task.CheckingFiles"));
                try
                {
                    var jp = new Progress<double>(p => Dispatcher.Invoke(() =>
                    {
                        LaunchProgressHub.Update(s =>
                        {
                            s.Indeterminate = false;
                            s.Percent = p;
                            s.ProgressLine = $"下载 Java {installMajor}: {p:0}%";
                            s.ButtonText = $"Java {p:0}%";
                        });
                        DownloadTaskService.Instance.Update(jTask, p, $"下载中 {p:0}%");
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
                    Log("Java 自动安装失败: " + jex.Message);
                    Log("请到 设置 → Java 一键安装，或在 设置 → Java 路径 填写本机已安装的 Java 后重试。");
                    return;
                }
            }
            if (javaToUse == null && !JavaManager.ExistsOnPath())
            {
                Log("本机未找到 Java：请在 设置 → Java 一键安装（自动下载），或填写已安装的 Java 路径。");
                return;
            }

            Log("正在检查游戏文件（jar / 资源 / 库）...");
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
            Log("游戏已启动，PID = " + process.Process?.Id);
            LaunchProgressHub.Update(s =>
            {
                s.Indeterminate = false;
                s.Percent = 0;
                s.ProgressLine = "游戏运行中，可关闭窗口（日志继续记录）";
            });
            // 记录最近启动（主页快捷启动，最新在前）
            AppSettings.Instance.RecordLaunch(version);
            RefreshRecent();

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
            Log("启动失败: " + ex.Message);
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
            LaunchProgressHub.End(); // v0.1.1：复位快照 → 所有实例恢复空闲 UI（含切页后的新实例）
            _launchCt = default;
        }
    }

    /// <summary>fix7：取消启动（下载/安装/补全文件阶段生效；游戏进程创建后不可取消）。
    /// v0.1.1：改由进度中枢广播 —— 任何页面实例上的取消按钮都能命中同一个启动流程。</summary>
    private void BtnCancelLaunch_Click(object sender, RoutedEventArgs e)
    {
        LaunchProgressHub.RequestCancel();
    }

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

    // ===== 首页 UI（PCL 对齐）：快捷入口 + 日志区展开/收起 =====

    private void BtnQuickDownload_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        NavigateTo(typeof(DownloadGamePage));

    private void BtnQuickModpacks_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        NavigateTo(typeof(ModpacksPage));

    private void BtnQuickMods_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        NavigateTo(typeof(ModsPage));

    private void NavigateTo(Type page)
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow main)
        {
            try { main.RootNavigation.Navigate(page); } catch { /* 导航失败静默 */ }
        }
    }

    private void BtnToggleLog_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var open = PnlLogBox.Visibility == Visibility.Collapsed;
        PnlLogBox.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        IcoLogToggle.Symbol = open ? SymbolRegular.ChevronUp24 : SymbolRegular.ChevronDown24;
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
