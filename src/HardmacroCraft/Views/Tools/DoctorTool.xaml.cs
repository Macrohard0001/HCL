using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views.Tools;

/// <summary>v0.16.7：一键诊断报告（独立窗口版，自 ToolsPage 迁移）。</summary>
public partial class DoctorTool : UserControl
{
    public DoctorTool()
    {
        InitializeComponent();
    }

    private async void BtnDoctor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            BtnDoctor.IsEnabled = false;
            TxtDoctorStatus.Text = "正在收集环境信息…";
            var path = await DoctorService.WriteReportAsync();
            TxtDoctorStatus.Text = "✓ 报告已生成：" + path;
            HorizonService.RevealInExplorer(path);
        }
        catch (Exception ex) { TxtDoctorStatus.Text = "诊断失败：" + ex.Message; }
        finally { BtnDoctor.IsEnabled = true; }
    }
}
