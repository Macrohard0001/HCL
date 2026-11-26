using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：基岩版工具（独立窗口版，自 ToolsPage 迁移）——
/// Store 版检测/启动 + BDS 服务端下载 + minecraft:// 深链加服 + LeviLauncher 集成。</summary>
public partial class BedrockTool : UserControl
{
    public BedrockTool()
    {
        InitializeComponent();
    }

    private async void BtnBedrockDetect_Click(object sender, RoutedEventArgs e)
    {
        TxtBedrockStatus.Text = "正在检测 Microsoft Store 版基岩版…";
        try
        {
            var inst = await BedrockService.DetectStoreInstallAsync();
            TxtBedrockStatus.Text = inst is null
                ? "未检测到已安装的 Store 版基岩版——请用「购买/安装（官方）」获取。"
                : $"已安装：版本 {inst.Version}\n位置：{inst.InstallLocation}\n微软账号随系统登录，单机/离线由商店版自身支持。";
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "检测失败：" + ex.Message; }
    }

    private async void BtnBedrockLaunch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await BedrockService.LaunchStoreAsync();
            TxtBedrockStatus.Text = "已通过 shell:AppsFolder 启动商店版基岩版。";
        }
        catch (System.IO.FileNotFoundException) { TxtBedrockStatus.Text = "未检测到已安装的 Store 版基岩版——请先「购买/安装（官方）」。"; }
        catch (Exception ex) { TxtBedrockStatus.Text = "启动失败：" + ex.Message; }
    }

    private async void BtnBedrockBds_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnBedrockBds.IsEnabled = false;
            TxtBedrockStatus.Text = "正在爬取官方 BDS 下载页…";
            var bds = await BedrockService.FetchLatestBdsAsync(App.LauncherService.Http)
                ?? throw new InvalidOperationException("官方页面结构变化，未找到 BDS 直链——请到 minecraft.net 官方页下载。");
            BedrockProgress.IsIndeterminate = true;
            BedrockProgress.Visibility = Visibility.Visible;
            TxtBedrockStatus.Text = $"BDS {bds.Version} 开始下载（重试/续传已启用）…";
            BedrockProgress.IsIndeterminate = false;
            var progress = new Progress<long>(b => BedrockProgress.Value = Math.Min(100, 100d * b / 80_000_000));
            var dest = await BedrockService.DownloadBdsAsync(App.LauncherService.Http, bds.Url, progress);
            BedrockProgress.Value = 100;
            TxtBedrockStatus.Text = "服务端已下载：" + dest + "\n解压后运行 bedrock_server.exe 即可开服（局域网离线联机）。";
            BedrockService.RevealInExplorer(dest);
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "下载失败：" + ex.Message; }
        finally { BtnBedrockBds.IsEnabled = true; }
    }

    private void BtnBedrockStore_Click(object sender, RoutedEventArgs e) =>
        ToolHostWindow.OpenUrl(BedrockService.StoreUrl);

    private async void BtnBedrockJoin_Click(object sender, RoutedEventArgs e)
    {
        var addr = TxtJoinAddr.Text.Trim();
        var name = string.IsNullOrWhiteSpace(TxtJoinName.Text) ? "Bedrock Server" : TxtJoinName.Text.Trim();
        if (addr.Length == 0 || !addr.Contains(':'))
        {
            TxtBedrockStatus.Text = "请填写 服务器地址:端口（例如 play.example.net:19132）。";
            return;
        }
        try
        {
            await BedrockService.JoinServerAsync(name, addr);
            TxtBedrockStatus.Text = $"已通过 minecraft:// 深链注册「{name}」（{addr}）并启动基岩版——游戏内「服务器」列表可见。";
        }
        catch (System.IO.FileNotFoundException) { TxtBedrockStatus.Text = "未检测到已安装的 Store 版基岩版——请先「购买/安装（官方）」。"; }
        catch (Exception ex) { TxtBedrockStatus.Text = "启动失败：" + ex.Message; }
    }

    private async void BtnLeviDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnLeviDownload.IsEnabled = false;
            TxtBedrockStatus.Text = "正在获取 LeviLauncher 最新 Release（api.github.com）…";
            var rel = await BedrockService.FetchLatestLeviAsync(App.LauncherService.Http)
                ?? throw new InvalidOperationException("未找到可用资产——请到 GitHub Releases 手动下载。");
            TxtBedrockStatus.Text = $"LeviLauncher {rel.Version}（{rel.AssetName}）开始下载…";
            var dest = await BedrockService.DownloadLeviAsync(App.LauncherService.Http, rel, null);
            TxtBedrockStatus.Text = "已下载：" + dest + "\nLeviLauncher 要求正版授权副本与微软账号登录（见其 README）——解压后按文档使用。";
            BedrockService.RevealInExplorer(dest);
        }
        catch (Exception ex) { TxtBedrockStatus.Text = "下载失败：" + ex.Message; }
        finally { BtnLeviDownload.IsEnabled = true; }
    }

    private void BtnLeviDocs_Click(object sender, RoutedEventArgs e) =>
        ToolHostWindow.OpenUrl(BedrockService.LeviDocsUrl);
}
