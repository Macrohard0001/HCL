using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：服务器状态查询（独立窗口）——
/// Server List Ping（TCP）：MOTD / 版本 / 在线人数 / 延迟。</summary>
public partial class ServerStatusTool : UserControl
{
    public ServerStatusTool()
    {
        InitializeComponent();
    }

    private async void BtnQuery_Click(object sender, RoutedEventArgs e)
    {
        string host;
        ushort port;
        try
        {
            (host, port) = ServerStatusService.ParseAddress(TxtAddress.Text);
        }
        catch (FormatException ex)
        {
            TxtStatus.Text = "地址格式：" + ex.Message;
            return;
        }
        try
        {
            BtnQuery.IsEnabled = false;
            TxtStatus.Text = (string?)TryFindResource("S.Tool.SrvQuerying") ?? "正在查询…";
            var st = await ServerStatusService.QueryAsync(host, port);
            var players = string.Format(
                (string?)TryFindResource("S.Tool.SrvPlayers") ?? "在线 {0} / {1}",
                st.PlayersOnline, st.PlayersMax);
            TxtStatus.Text = $"MOTD：{st.Motd}\n版本：{st.Version}\n{players} · 延迟 {st.LatencyMs} ms"
                + (string.IsNullOrEmpty(st.PlayerSample) ? "" : "\n在线玩家：" + st.PlayerSample);
        }
        catch (TimeoutException ex) { TxtStatus.Text = "✗ " + ex.Message; }
        catch (Exception ex) { TxtStatus.Text = "✗ 查询失败：" + ex.Message; }
        finally { BtnQuery.IsEnabled = true; }
    }
}
