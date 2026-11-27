using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Views.Tools;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.16.7：工具页 = 入口网格。全部工具在独立窗口打开（ToolHostWindow +
/// Views/Tools/*），不影响主窗口、可多开并排；主窗口不再承载工具本体。
/// </summary>
public partial class ToolsPage : Page
{
    public ToolsPage()
    {
        InitializeComponent();
    }

    private void OpenSeed_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.SlimeTitle") ?? "种子地图", new SeedMapTool());

    private void OpenWorldMap_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.MapTitle") ?? "世界地图", new WorldMapTool());

    private void OpenSpeedrun_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.SpeedTitle") ?? "速通工具箱", new SpeedrunTool());

    private void OpenServer_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.SrvTitle") ?? "服务器状态查询", new ServerStatusTool());

    private void OpenShots_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.ShotsTitle") ?? "截图管理", new ScreenshotsTool());

    private void OpenBackup_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.BackupTitle") ?? "实例备份 / 恢复", new BackupTool());

    private void OpenMigration_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.MigTitle") ?? "导入其他启动器版本", new MigrationTool());

    private void OpenHorizon_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.HorizonTitle") ?? "Horizon（Rust 版 MC）", new HorizonTool());

    private void OpenBedrock_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.BedrockTitle") ?? "基岩版", new BedrockTool());

    private void OpenDoctor_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.DoctorTitle") ?? "一键诊断", new DoctorTool());

    private void OpenWorlds_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.WorldsTitle") ?? "世界存档管理", new WorldsTool());

    private void OpenLog_Click(object sender, MouseButtonEventArgs e) =>
        ToolHostWindow.Show(Window.GetWindow(this),
            (string?)TryFindResource("S.Tool.LogTitle") ?? "日志查看器", new LogTool());
}
