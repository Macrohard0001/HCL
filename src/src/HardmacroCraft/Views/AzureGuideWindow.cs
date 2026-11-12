using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HardmacroCraft.Views;

/// <summary>
/// Azure Client ID 获取引导（v0.1.3 新增）：
/// 微软正版登录所需的"微软应用代码"需用户在 Azure 门户免费注册一次（微软政策，
/// 每个应用须有独立的 Client ID），本窗口分步图解流程并提供一键打开门户，
/// 注册后把 ID 粘贴到 设置 → Azure Client ID 即可永久使用。
/// </summary>
public class AzureGuideWindow : Wpf.Ui.Controls.FluentWindow
{
    public AzureGuideWindow()
    {
        Title = R("S.Azure.Title", "如何获取 Azure Client ID");
        Width = 640;
        Height = 600;
        MinWidth = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true; // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常

        var scroll = new ScrollViewer { Margin = new Thickness(28, 22, 24, 22), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var root = new StackPanel();

        var title = new TextBlock
        {
            Text = R("S.Azure.Title", "如何获取 Azure Client ID"),
            FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        root.Children.Add(title);

        var sub = new TextBlock
        {
            Text = R("S.Azure.Sub",
                "微软正版登录需要一个免费的 Azure Client ID（即\"微软应用代码\"），这是微软的开放政策：每个应用须注册自己的 ID。全程约 3 分钟，只需做一次。"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 0, 0, 14),
        };
        sub.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(sub);

        string[] steps =
        {
            R("S.Azure.Step1", "打开 Azure 门户（下方按钮），用你的微软账号登录（免费，无需绑卡）。"),
            R("S.Azure.Step2", "在顶部搜索框搜索\"应用注册\"（App registrations），点击\"新注册\"。"),
            R("S.Azure.Step3",
                "名称随意（如 HardmacroCraft）；\"受支持的账户类型\"选第 3 项：任何组织目录中的账户和个人 Microsoft 账户；重定向 URI 留空。"),
            R("S.Azure.Step4", "点击\"注册\"，在概述页复制\"应用程序(客户端) ID\"——这就是 Azure Client ID。"),
            R("S.Azure.Step5",
                "关键一步：回到该应用 → 左侧\"身份验证\" → 拉到最底部\"高级设置\" → \"允许公共客户端流\"选择\"启用\" → 保存。不做这一步，设备码登录会报错。"),
            R("S.Azure.Step6", "把 ID 粘贴到启动器 设置 → Azure Client ID，保存后即可使用微软正版登录。"),
        };
        for (var i = 0; i < steps.Length; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 11) };
            var numBorder = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x77, 0xCC)),
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0),
            };
            var numText = new TextBlock
            {
                Text = (i + 1).ToString(), FontSize = 12.5, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            numBorder.Child = numText;
            var txt = new TextBlock
            {
                Text = steps[i], TextWrapping = TextWrapping.Wrap, FontSize = 13.5,
                VerticalAlignment = VerticalAlignment.Top, MaxWidth = 500,
            };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            row.Children.Add(numBorder);
            row.Children.Add(txt);
            root.Children.Add(row);
        }

        var tip = new TextBlock
        {
            Text = R("S.Azure.HaveId", "拿到 ID 后：粘贴到 设置 → Azure Client ID → 保存，然后回到主页点\"微软登录\"。"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0),
        };
        tip.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(tip);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var btnPortal = new Wpf.Ui.Controls.Button
        {
            Content = R("S.Azure.OpenPortal", "打开 Azure 门户"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 36, Margin = new Thickness(0, 0, 10, 0),
        };
        btnPortal.Click += (_, _) => OpenUrl("https://portal.azure.com");
        var btnReg = new Wpf.Ui.Controls.Button
        {
            Content = R("S.Azure.OpenRegister", "打开\"应用注册\"页"),
            MinHeight = 36,
        };
        btnReg.Click += (_, _) => OpenUrl(
            "https://portal.azure.com/#blade/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");
        var btnClose = new Wpf.Ui.Controls.Button
        {
            Content = R("S.Azure.Close", "我知道了"),
            MinHeight = 36, Margin = new Thickness(10, 0, 0, 0),
        };
        btnClose.Click += (_, _) => Close();
        btns.Children.Add(btnPortal);
        btns.Children.Add(btnReg);
        btns.Children.Add(btnClose);
        root.Children.Add(btns);

        scroll.Content = root;
        Services.FluentDialog.SetContent(this, scroll); // fix5：挂标题栏（含关闭按钮）
    }

    private static string R(string key, string fallback)
        => App.Current.TryFindResource(key) as string ?? fallback;

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* 浏览器打开失败不影响引导文本 */ }
    }
}
