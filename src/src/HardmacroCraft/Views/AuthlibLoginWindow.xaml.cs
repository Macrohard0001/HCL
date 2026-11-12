using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 第三方验证站登录窗口（authlib-injector / Yggdrasil，PCL 的"第三方登录"同款体验）：
/// - 默认 LittleSkin，支持任意兼容验证站；
/// - 多角色账号登录后弹出角色选择（PCL 同款）；
/// - 记住密码（便携 config/accounts.json），下次打开自动填充；
/// - 按验证站 meta 展示"注册账号"链接；
/// - 登录成功立即写入 AccountStore（v1.7.2 修复：此前未保存导致无法启动游戏）。
/// </summary>
public partial class AuthlibLoginWindow
{
    /// <summary>登录成功后的账号；取消或失败为 null。</summary>
    public StoredAccount? Result { get; private set; }

    private readonly AuthlibService _auth;
    private string _registerUrl = "";

    public AuthlibLoginWindow(AuthlibService auth)
    {
        InitializeComponent();
        _auth = auth;
        // fix7：Esc 关闭窗口（PCL 同款）
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        BuildServerPresets();
        PrefillFromStore();
        // 记住密码开启后才允许自动登录（无密码无法静默重登）
        ChkRemember.Checked += (_, _) => ChkAutoLogin.IsEnabled = true;
        ChkRemember.Unchecked += (_, _) => { ChkAutoLogin.IsEnabled = false; ChkAutoLogin.IsChecked = false; };
        Loaded += (_, _) => TxtUser.Focus();
    }

    /// <summary>服务器预设（PCL 同款：常用验证站 + 本机用过的站点），仍可任意输入。</summary>
    private void BuildServerPresets()
    {
        var items = new List<string> { "littleskin.cn" };
        // 本机登录过的验证站加入预设（去 API Root 为域名形式，直接填也能被 ResolveApiRootAsync 解析）
        foreach (var acc in AccountStore.Instance.Accounts)
        {
            if (acc.Type != "authlib" || string.IsNullOrWhiteSpace(acc.ServerUrl)) continue;
            try
            {
                var uri = new Uri(acc.ServerUrl);
                var host = uri.Host;
                // API Root 常带 /api/yggdrasil 路径；显示完整值，认证时会正确解析
                if (!items.Contains(host)) items.Add(host);
                if (!items.Contains(acc.ServerUrl)) items.Add(acc.ServerUrl);
            }
            catch { /* 非法 URL 跳过 */ }
        }
        // 上次使用的服务器排最前
        var last = AppSettings.Instance.LastAuthlibServer;
        if (!string.IsNullOrWhiteSpace(last))
        {
            items.Remove(last);
            items.Insert(0, last);
        }
        foreach (var it in items) CmbServer.Items.Add(it);
        CmbServer.Text = items.Count > 0 ? "littleskin.cn" : "";
    }

    /// <summary>记住密码的上次账号自动填充（PCL 同款体验）。</summary>
    private void PrefillFromStore()
    {
        var acc = AccountStore.Instance.Current;
        if (acc is not { Type: "authlib" }) return;
        if (!string.IsNullOrWhiteSpace(acc.ServerUrl))
        {
            // 显示站点根域名更友好；API Root 直接填也能被正确解析
            CmbServer.Text = acc.ServerUrl;
        }
        if (!string.IsNullOrWhiteSpace(acc.LoginId))
            TxtUser.Text = acc.LoginId;
        if (acc.RememberPassword)
        {
            ChkRemember.IsChecked = true;
            ChkAutoLogin.IsEnabled = true;
            ChkAutoLogin.IsChecked = acc.AutoLogin;
            if (!string.IsNullOrWhiteSpace(acc.SavedPassword))
                TxtPassword.Password = acc.SavedPassword;
        }
    }

    private void SetStatus(string msg)
    {
        TxtStatus.Text = msg;
        TxtStatus.Foreground = FindResource("TextFillColorSecondaryBrush")
            as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
    }

    private async void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        var server = CmbServer.Text.Trim();
        var user = TxtUser.Text.Trim();
        var pass = TxtPassword.Password;

        if (server.Length == 0 || user.Length == 0 || pass.Length == 0)
        {
            SetStatus((string)FindResource("S.Authlib.ErrEmpty"));
            return;
        }

        BtnLogin.IsEnabled = false;
        PbLogin.Visibility = Visibility.Visible;
        try
        {
            SetStatus((string)FindResource("S.Authlib.Resolving"));
            var apiRoot = await _auth.ResolveApiRootAsync(server,
                log => Dispatcher.Invoke(() => SetStatus(log)));

            // 按验证站 meta 显示注册链接（PCL 同款）
            var meta = await _auth.GetMetaAsync(apiRoot);
            if (!string.IsNullOrWhiteSpace(meta.RegisterUrl))
            {
                _registerUrl = meta.RegisterUrl;
                Dispatcher.Invoke(() => TxtRegister.Visibility = Visibility.Visible);
            }

            SetStatus((string)FindResource("S.Authlib.Logging"));
            var login = await _auth.AuthenticateAsync(apiRoot, user, pass, SelectCharacterAsync);

            Result = new StoredAccount
            {
                Type = "authlib",
                Name = login.Name,
                Uuid = login.Uuid,
                RefreshToken = login.AccessToken,
                ClientToken = login.ClientToken,
                ServerUrl = login.ApiRoot,
                LoginId = user,
                RememberPassword = ChkRemember.IsChecked == true,
                SavedPassword = ChkRemember.IsChecked == true ? pass : "",
                AutoLogin = ChkRemember.IsChecked == true && ChkAutoLogin.IsChecked == true,
            };
            // v1.7.2 关键修复：登录成功立即保存账号（此前只放在 Result 里没人消费，
            // 账号列表始终"未登录"，启动被"请先登录账号"拦截）
            AccountStore.Instance.Upsert(Result);
            // 记住上次使用的服务器（下次打开默认选中）
            try
            {
                AppSettings.Instance.LastAuthlibServer = new Uri(login.ApiRoot).Host;
                AppSettings.Instance.Save();
            }
            catch { /* 非法 URL 不影响登录 */ }

            SetStatus(string.Format((string)FindResource("S.Authlib.Done"), login.Name));
            await Task.Delay(400);
            Close();
        }
        catch (Exception ex)
        {
            SetStatus((string)FindResource("S.Authlib.Failed") + ex.Message);
            TxtStatus.Foreground = TryFindResource("SystemFillColorCriticalBrush")
                as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.OrangeRed;
        }
        finally
        {
            BtnLogin.IsEnabled = true;
            PbLogin.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>多角色选择（PCL 同款：登录后弹出角色列表）。取消选择则中止登录。</summary>
    private async Task<(string Id, string Name)> SelectCharacterAsync(
        System.Collections.Generic.IReadOnlyList<(string Id, string Name)> profiles)
    {
        // Dispatcher.Invoke 为同步调用；AuthenticateAsync 已在后台线程，
        // 仅对话框需要封送回 UI 线程
        var chosen = Dispatcher.Invoke(() =>
        {
            var dlg = new Wpf.Ui.Controls.FluentWindow
            {
                Title = (string)FindResource("S.Authlib.Title"),
                Width = 380,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
                ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
            };
            var owner = System.Windows.Application.Current.Windows.OfType<Window>()
                .FirstOrDefault(w => !ReferenceEquals(w, dlg));
            if (owner != null) dlg.Owner = owner;

            var sp = new StackPanel { Margin = new Thickness(20) };
            var tip = new TextBlock
            {
                Text = string.Format((string)FindResource("S.Authlib.PickCharacter"), profiles.Count),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
            };
            tip.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            var list = new ListBox { MinHeight = 140 };
            foreach (var p in profiles)
                list.Items.Add(new ListBoxItem { Content = p.Name, Tag = p, IsSelected = p == profiles[0] });
            var ok = new Wpf.Ui.Controls.Button
            {
                Content = (string)FindResource("S.Authlib.Confirm"),
                Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
                MinHeight = 34,
                MinWidth = 120,
                Margin = new Thickness(0, 12, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsEnabled = false,
            };
            list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedItem != null;
            (string Id, string Name)? result = null;
            ok.Click += (_, _) =>
            {
                if (list.SelectedItem is ListBoxItem it) result = ((string, string))it.Tag!;
                dlg.Close();
            };
            sp.Children.Add(tip);
            sp.Children.Add(list);
            sp.Children.Add(ok);
            Services.FluentDialog.SetContent(dlg, sp); // fix5：挂标题栏（含关闭按钮）
            dlg.ShowDialog();
            return result;
        });
        return chosen ?? throw new OperationCanceledException("已取消角色选择。");
    }

    private void LnkRegister_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_registerUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_registerUrl) { UseShellExecute = true });
        }
        catch { /* 浏览器打开失败时静默 */ }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
