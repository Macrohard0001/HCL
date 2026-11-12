using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HardmacroCraft.Views;

/// <summary>
/// Java 缺失询问弹窗（fix3，PCL 同款行为）：启动时发现没有所需 Java 时，
/// 先弹窗告知所需版本 / 默认安装位置，由用户确认后才下载安装——
/// 而不是不打招呼就直接开始下载。
/// Ask 返回选定的安装目录（用户确认）；用户取消返回 null。
/// </summary>
public static class JavaInstallWindow
{
    /// <summary>
    /// 用户确认安装：返回选定的 Java 大版本与安装目录；取消返回 null（fix5：新增版本选择下拉）。
    /// v0.1.9：默认选中与「推荐」标记改为推荐 Java（当前版本支持使用的最高版本），
    /// 而非最低所需版本；requiredMajor 仅用于文案说明兼容下限。
    /// </summary>
    public static (int Major, string Dir)? Ask(Window? owner, int requiredMajor, string mcVersion, int? recommendedMajor = null)
    {
        var major = recommendedMajor ?? requiredMajor;
        (int, string)? result = null;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = string.Format(
                App.Current.TryFindResource("S.Home.JavaAskTitle") as string ?? "需要 Java 环境", major),
            Width = 560,
            SizeToContent = SizeToContent.Height, // fix7：自适应内容高度，修复按钮被截断不显示
            MinHeight = 280,
            MaxHeight = 640,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        // 标题行（fix7：文案改为「需要 Java 环境」，具体版本号在正文说明）
        var title = new System.Windows.Controls.TextBlock
        {
            Text = App.Current.TryFindResource("S.Home.JavaAskTitle") as string ?? "需要 Java 环境",
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
        };
        title.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorPrimaryBrush");
        sp.Children.Add(title);

        var body = new System.Windows.Controls.TextBlock
        {
            Text = string.Format(
                App.Current.TryFindResource("S.Home.JavaAskBody") as string ?? "",
                mcVersion, major),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13.5,
            Margin = new Thickness(0, 0, 0, 4),
        };
        body.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorPrimaryBrush");
        sp.Children.Add(body);

        var hint = new System.Windows.Controls.TextBlock
        {
            Text = App.Current.TryFindResource("S.Home.JavaAskHint") as string ?? "",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 14),
        };
        hint.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorSecondaryBrush");
        sp.Children.Add(hint);

        // Java 版本选择（fix5）：默认推荐所需版本，也允许选择其它常见大版本
        var verLabel = new System.Windows.Controls.TextBlock
        {
            Text = App.Current.TryFindResource("S.Home.JavaAskVersion") as string ?? "Java 版本：",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 6),
        };
        verLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorPrimaryBrush");
        sp.Children.Add(verLabel);

        var cmbMajor = new System.Windows.Controls.ComboBox { MinHeight = 36, Margin = new Thickness(0, 0, 0, 12) };
        var majors = new[] { 8, 11, 17, 21, 25 }.Union(new[] { requiredMajor, major }).OrderBy(m => m).Distinct().ToArray();
        var selectedMajor = major;
        foreach (var m in majors)
        {
            var item = new System.Windows.Controls.ComboBoxItem
            {
                Content = $"Java {m}" + (m == major ?
                    (App.Current.TryFindResource("S.Home.JavaAskRecommended") as string ?? "（推荐）") : ""),
                Tag = m,
            };
            cmbMajor.Items.Add(item);
            if (m == major) cmbMajor.SelectedItem = item;
        }
        sp.Children.Add(cmbMajor);

        // 兼容下限说明（v0.1.9：推荐项是最高可用版本，同时告知最低要求）
        if (requiredMajor > 0 && requiredMajor != major)
        {
            var minNote = new System.Windows.Controls.TextBlock
            {
                Text = string.Format(
                    App.Current.TryFindResource("S.Home.JavaAskMinNote") as string
                        ?? "该版本最低需要 Java {0}；推荐安装 Java {1}（当前版本支持的最高版本）。", requiredMajor, major),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12),
            };
            minNote.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
                "TextFillColorSecondaryBrush");
            sp.Children.Add(minNote);
        }

        // 安装位置（可修改，默认值 = 托管 runtime 目录）
        var defaultDir = Services.JavaManager.ManagedDir(major);
        var locLabel = new System.Windows.Controls.TextBlock
        {
            Text = App.Current.TryFindResource("S.Home.JavaAskLocation") as string ?? "安装位置：",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(0, 0, 0, 6),
        };
        locLabel.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            "TextFillColorPrimaryBrush");
        sp.Children.Add(locLabel);

        var txtDir = new Wpf.Ui.Controls.TextBox
        {
            Text = defaultDir,
            MinHeight = 36,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
        };
        sp.Children.Add(txtDir);

        // 版本切换后，若用户未手动改过目录，默认目录跟随所选 Java 大版本
        var dirTouched = false;
        txtDir.TextChanged += (_, _) => dirTouched = true;
        cmbMajor.SelectionChanged += (_, _) =>
        {
            if (cmbMajor.SelectedItem is System.Windows.Controls.ComboBoxItem { Tag: int m })
            {
                selectedMajor = m;
                if (!dirTouched) txtDir.Text = Services.JavaManager.ManagedDir(m);
            }
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var btnCancel = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Common.Cancel") as string ?? "取消",
            MinHeight = 36,
            MinWidth = 100,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var btnOk = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.JavaAskInstall") as string ?? "安装并启动",
            MinHeight = 36,
            MinWidth = 130,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
        };
        btnRow.Children.Add(btnCancel);
        btnRow.Children.Add(btnOk);
        sp.Children.Add(btnRow);

        btnOk.Click += (_, _) =>
        {
            var dir = txtDir.Text?.Trim() ?? "";
            if (dir.Length == 0)
            {
                txtDir.Text = Services.JavaManager.ManagedDir(selectedMajor);
                return;
            }
            try
            {
                dir = Path.GetFullPath(dir);
                result = (selectedMajor, dir);
                win.DialogResult = true;
                win.Close();
            }
            catch
            {
                txtDir.Text = Services.JavaManager.ManagedDir(selectedMajor);
            }
        };
        btnCancel.Click += (_, _) => { win.DialogResult = false; win.Close(); };

        Services.FluentDialog.SetContent(win, sp); // fix5：挂标题栏（含关闭按钮）
        win.ShowDialog();
        return result;
    }
}
