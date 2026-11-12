using System.Diagnostics;
using System.Windows;
using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft.Views;

/// <summary>
/// PCL 式微软正版登录：设备码 + 复制 + 一键打开浏览器 + 轮询授权。
/// </summary>
public partial class MicrosoftLoginWindow : FluentWindow
{
    private readonly string _clientId;
    private CancellationTokenSource? _cts;
    public string? ResultName { get; private set; }
    public bool Success { get; private set; }

    public MicrosoftLoginWindow(string clientId)
    {
        InitializeComponent();
        _clientId = clientId;
        // fix7：Esc 关闭窗口（PCL 同款）
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        try
        {
            TxtStatus.Text = FindResource("S.Login.Requesting") as string ?? "";
            var device = await App.Auth.RequestDeviceCodeAsync(_clientId, _cts.Token);
            TxtUserCode.Text = device.UserCode;
            _url = device.VerificationUri;
            TxtStatus.Text = FindResource("S.Login.Waiting") as string ?? "";

            var result = await App.Auth.PollTokenAsync(_clientId, device, _cts.Token);
            AccountStore.Instance.Upsert(new StoredAccount
            {
                Type = "microsoft",
                Name = result.Name,
                Uuid = result.Uuid,
                RefreshToken = result.RefreshToken,
                AccessToken = result.AccessToken, // 缓存 MC 令牌：启动时刷新失败可兜底（v0.1.3）
            });
            ResultName = result.Name;
            Success = true;
            TxtStatus.Text = string.Format(FindResource("S.Login.Done") as string ?? "{0}", result.Name);
            PbLogin.IsIndeterminate = false;
            PbLogin.Value = 100;
            await Task.Delay(900);
            Close();
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            PbLogin.IsIndeterminate = false;
            TxtStatus.Text = (FindResource("S.Login.Failed") as string ?? "登录失败") + " " + ex.Message;
        }
    }

    private string? _url;

    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(TxtUserCode.Text);
            TxtStatus.Text = FindResource("S.Login.Copied") as string ?? "";
        }
        catch { /* 剪贴板偶发占用 */ }
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "打开浏览器失败，请手动访问: " + _url + " (" + ex.Message + ")";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
