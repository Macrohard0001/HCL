using System.IO;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.9：外部版本导入窗口（代码构建，与 VersionSettingsWindow 同风格）。
/// 扫描常见启动器目录 + 自定义文件夹，列出候选版本；导入 =
/// 版本目录复制进 GameRoot（隔离时并入实例数据），完成后刷新版本列表。
/// </summary>
public static class MigrationWindow
{
    public static void Show(Window? owner, Action<string> onImported)
    {
        var win = new Window
        {
            Title = (string)Application.Current.TryFindResource("S.Mig.Title")!,
            Width = 620, Height = 460,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = (System.Windows.Media.Brush)Application.Current.TryFindResource("ApplicationBackgroundBrush")!,
        };

        var list = new ListBox
        {
            Margin = new Thickness(16, 12, 16, 8),
            MinHeight = 220,
            Background = System.Windows.Media.Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };

        var txtHint = new System.Windows.Controls.TextBlock
        {
            Margin = new Thickness(16, 4, 16, 8),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.8,
        };

        void Rescan()
        {
            var found = new List<MigrationCandidate>();
            foreach (var root in LauncherMigrationService.BuiltinRoots())
                found.AddRange(LauncherMigrationService.ScanFolder(root));
            list.ItemsSource = found;
            txtHint.Text = found.Count == 0
                ? (string)Application.Current.TryFindResource("S.Mig.None")!
                : string.Format(
                    (string)Application.Current.TryFindResource("S.Mig.Found")!,
                    found.Count);
        }

        var btnCustom = new System.Windows.Controls.Button
        {
            Content = (string)Application.Current.TryFindResource("S.Mig.CustomFolder")!,
            MinHeight = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(0, 0, 8, 0),
        };
        btnCustom.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = (string)Application.Current.TryFindResource("S.Mig.CustomFolder")!,
            };
            if (dlg.ShowDialog(win) != true) return;
            var found = LauncherMigrationService.ScanFolder(dlg.FolderName);
            var merged = new List<MigrationCandidate>();
            foreach (var root in LauncherMigrationService.BuiltinRoots())
                merged.AddRange(LauncherMigrationService.ScanFolder(root));
            merged.AddRange(found);
            list.ItemsSource = merged.DistinctBy(c => c.VersionDir).ToList();
            txtHint.Text = merged.Count == 0
                ? (string)Application.Current.TryFindResource("S.Mig.None")!
                : string.Format((string)Application.Current.TryFindResource("S.Mig.Found")!, merged.Count);
        };

        var btnImport = new Wpf.Ui.Controls.Button
        {
            Content = (string)Application.Current.TryFindResource("S.Mig.Import")!,
            MinHeight = 32, Padding = new Thickness(14, 0, 14, 0),
            Appearance = ControlAppearance.Primary,
        };
        btnImport.Click += (_, _) =>
        {
            if (list.SelectedItem is not MigrationCandidate candidate)
            {
                System.Windows.MessageBox.Show(
                    (string)Application.Current.TryFindResource("S.Mig.PickOne")!,
                    win.Title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            var isolation = AppSettings.Instance.VersionIsolation;
            var name = LauncherMigrationService.Import(candidate, isolation);
            var dataMerged = isolation && candidate.DataDir != null;
            System.Windows.MessageBox.Show(
                dataMerged
                    ? string.Format((string)Application.Current.TryFindResource("S.Mig.Done")!, name)
                    : string.Format((string)Application.Current.TryFindResource("S.Mig.DataSkipped")!, name),
                win.Title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            onImported(name);
            win.Close();
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 16, 12),
        };
        buttons.Children.Add(btnCustom);
        buttons.Children.Add(btnImport);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(txtHint, 0);
        Grid.SetRow(list, 1);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(txtHint);
        grid.Children.Add(list);
        grid.Children.Add(buttons);
        win.Content = grid;

        Rescan();
        win.ShowDialog();
    }
}
