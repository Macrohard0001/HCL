using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// 账号管理面板（PCL 的"账户"同款体验）：
/// - 所有已保存账号列表（离线 / 微软正版 / 第三方验证站），当前账号高亮；
/// - 一键切换当前账号（启动时使用）；删除账号；
/// - 底部三种登录入口，登录成功后立即入列。
/// 代码构建设计：便于与 Fluent 主题资源（深浅色）保持一致。
/// </summary>
public static class AccountManager
{
    /// <summary>打开账号管理对话框；返回 true 表示账号列表发生过变化。</summary>
    public static bool Show(Window? owner)
    {
        var changed = false;

        var win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = App.Current.TryFindResource("S.Acct.Title") as string ?? "账号管理",
            Width = 520,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true, // Mica backdrop 要求开启，否则 OnSourceInitialized 抛异常
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault(w => !ReferenceEquals(w, win));
        if (owner != null && !ReferenceEquals(owner, win)) win.Owner = owner;

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

        var title = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Acct.Title") as string ?? "账号管理",
            FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var subtitle = new TextBlock
        {
            Text = App.Current.TryFindResource("S.Acct.Subtitle") as string ?? "",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 14),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(title);
        root.Children.Add(subtitle);

        // 账号列表容器（每次重建）
        var list = new StackPanel();
        root.Children.Add(list);

        // 登录入口
        var addPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var btnMs = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.MsLogin") as string ?? "微软登录",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            MinHeight = 36, Margin = new Thickness(0, 0, 8, 0),
        };
        var btnAuthlib = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.ThirdLogin") as string ?? "第三方登录",
            MinHeight = 36, Margin = new Thickness(0, 0, 8, 0),
        };
        var btnOffline = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Home.OfflineLogin") as string ?? "离线登录",
            MinHeight = 36,
        };
        addPanel.Children.Add(btnMs);
        addPanel.Children.Add(btnAuthlib);
        addPanel.Children.Add(btnOffline);
        root.Children.Add(addPanel);

        void Rebuild()
        {
            list.Children.Clear();
            var store = AccountStore.Instance;
            if (store.Accounts.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = App.Current.TryFindResource("S.Acct.NoAccounts") as string ?? "暂无账号",
                    FontSize = 13, Margin = new Thickness(0, 8, 0, 8),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                list.Children.Add(empty);
                return;
            }

            foreach (var acc in store.Accounts.ToList())
            {
                var row = BuildRow(win, acc, () => { changed = true; Rebuild(); });
                list.Children.Add(row);
            }
        }

        btnMs.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(AppSettings.Instance.AzureClientId))
            {
                // v0.1.3：弹出分步引导而不是一句提示
                new AzureGuideWindow { Owner = win }.ShowDialog();
                return;
            }
            var dlg = new MicrosoftLoginWindow(AppSettings.Instance.AzureClientId) { Owner = win };
            dlg.ShowDialog();
            changed = true;
            Rebuild();
        };
        btnAuthlib.Click += (_, _) =>
        {
            var dlg = new AuthlibLoginWindow(new AuthlibService(App.LauncherService.Http)) { Owner = win };
            dlg.ShowDialog();
            changed = true;
            Rebuild();
        };
        btnOffline.Click += (_, _) =>
        {
            // 登录系统全面检查：专用窗口实时校验 + 就地报错（此前非法输入被静默丢弃）
            var current = AccountStore.Instance.Current;
            var name = OfflineNameWindow.Show(win,
                current is { Type: "offline" } ? current.Name : null);
            if (name == null) return;
            var session = AuthService.OfflineSession(name);
            AccountStore.Instance.Upsert(new StoredAccount
            {
                Type = "offline", Name = session.Username, Uuid = session.UUID,
            });
            changed = true;
            Rebuild();
        };

        Rebuild();
        FluentDialog.SetContent(win, root); // fix5：挂标题栏（含关闭按钮）；顺带修复面板未挂载导致弹窗空白
        win.ShowDialog();
        return changed;
    }

    private static (string Badge, Brush BadgeBrush) TypeBadge(StoredAccount acc)
    {
        var text = acc.Type switch
        {
            "microsoft" => App.Current.TryFindResource("S.Home.AccountMs") as string ?? "微软正版",
            "authlib" => App.Current.TryFindResource("S.Home.AccountThird") as string ?? "第三方",
            _ => App.Current.TryFindResource("S.Home.AccountOffline") as string ?? "离线",
        };
        var brush = acc.Type switch
        {
            "microsoft" => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
            "authlib" => new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5)),
            _ => new SolidColorBrush(Color.FromRgb(0x8D, 0x6E, 0x63)),
        };
        return (text, brush);
    }

    /// <summary>
    /// 拉取皮肤裁剪头部头像（v0.1.5 全面对齐 PCL）：
    /// 有皮肤 → 皮肤脸部；无皮肤 / 离线无自定义皮肤 → Steve / Alex 默认脸（UUID 奇偶）；
    /// 全部失败 → 保持字母头像。
    /// </summary>
    private static async Task LoadAvatarIntoAsync(System.Windows.Controls.Image img,
        TextBlock fallback, StoredAccount acc)
    {
        try
        {
            System.Windows.Media.Imaging.BitmapSource? cropped = null;

            // 离线账号 + 本地自定义皮肤：直接读本机 PNG 头像（无需网络）
            if (acc.Type == "offline" && !string.IsNullOrWhiteSpace(acc.SkinPath) && File.Exists(acc.SkinPath))
            {
                var local = new System.Windows.Media.Imaging.BitmapImage();
                local.BeginInit();
                local.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                local.UriSource = new Uri(acc.SkinPath);
                local.EndInit();
                local.Freeze();
                if (local.PixelWidth >= 32 && local.PixelHeight >= 32)
                    cropped = new System.Windows.Media.Imaging.CroppedBitmap(local,
                        new Int32Rect(8, 8, Math.Min(8, local.PixelWidth / 8), Math.Min(8, local.PixelHeight / 8)));
            }
            else
            {
                var skinUrl = await SkinService.GetSkinUrlAsync(acc, App.LauncherService.Http);
                var skin = skinUrl == null ? null : await SkinService.DownloadSkinAsync(skinUrl, App.LauncherService.Http);
                if (skin != null)
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    using var ms = new MemoryStream(skin);
                    bmp.BeginInit();
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.StreamSource = ms;
                    bmp.EndInit();
                    bmp.Freeze();
                    if (bmp.PixelWidth >= 32 && bmp.PixelHeight >= 32)
                        cropped = new System.Windows.Media.Imaging.CroppedBitmap(bmp,
                            new Int32Rect(8, 8, Math.Min(8, bmp.PixelWidth / 8), Math.Min(8, bmp.PixelHeight / 8)));
                }
            }

            // v0.1.5：无皮肤回退 Steve / Alex 默认脸（与游戏内选脸规则一致，PCL 同款）
            if (cropped == null)
            {
                var face = new System.Windows.Media.Imaging.BitmapImage();
                face.BeginInit();
                face.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                face.UriSource = new Uri(SkinService.DefaultFacePath(acc.Uuid), UriKind.Relative);
                face.EndInit();
                face.Freeze();
                cropped = face;
            }

            System.Windows.Media.RenderOptions.SetBitmapScalingMode(img,
                BitmapScalingMode.NearestNeighbor); // 像素风锐利缩放（PCL 同款）
            img.Source = cropped;
            img.Visibility = Visibility.Visible;
            fallback.Visibility = Visibility.Collapsed;
        }
        catch { /* 网络异常保持字母头像 */ }
    }

    /// <summary>离线皮肤菜单：更换（选 PNG，复制进便携 config/skins/）/ 清除。</summary>
    private static void OpenSkinMenu(Window win, FrameworkElement target, StoredAccount acc, Action onChanged)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = target,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        var miChange = new MenuItem
        {
            Header = App.Current.TryFindResource("S.Acct.SkinChange") as string ?? "更换皮肤…",
        };
        miChange.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = App.Current.TryFindResource("S.Acct.Skin") as string ?? "皮肤",
                Filter = "PNG (*.png)|*.png|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog(win) != true) return;
            byte[] png;
            try { png = File.ReadAllBytes(dlg.FileName); }
            catch { return; }
            if (!SkinService.IsSkinPng(png))
            {
                MessageBox.Show(win,
                    App.Current.TryFindResource("S.Acct.SkinInvalid") as string
                        ?? "皮肤文件无效：需要 64×64 或 64×32 的 PNG 图片。",
                    "Hardmacro Craft Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            // 复制进便携 config/skins/：原文件被移动/删除后皮肤不失效，且随启动器目录整体携带
            var dir = Path.Combine(AppSettings.ConfigDir, "skins");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(acc.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (safe.Length == 0) safe = "skin";
            var dest = Path.Combine(dir, safe + ".png");
            File.WriteAllBytes(dest, png);
            acc.SkinPath = dest;
            AccountStore.Instance.Upsert(acc);
            onChanged();
        };
        menu.Items.Add(miChange);
        if (!string.IsNullOrWhiteSpace(acc.SkinPath) && File.Exists(acc.SkinPath))
        {
            var skinFile = acc.SkinPath;
            var miClear = new MenuItem
            {
                Header = App.Current.TryFindResource("S.Acct.SkinClear") as string ?? "清除皮肤",
            };
            miClear.Click += (_, _) =>
            {
                try { if (File.Exists(skinFile)) File.Delete(skinFile); } catch { /* 文件占用等：忽略 */ }
                acc.SkinPath = "";
                AccountStore.Instance.Upsert(acc);
                onChanged();
            };
            menu.Items.Add(miClear);
        }
        menu.IsOpen = true;
    }

    private static UIElement BuildRow(Window win, StoredAccount acc, Action onChanged)
    {
        var isCurrent = AccountStore.Instance.Current?.Name == acc.Name;
        var (badge, badgeBrush) = TypeBadge(acc);

        var isOffline = acc.Type == "offline";
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (isOffline)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // 皮肤按钮（离线账号专属）

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Background = new SolidColorBrush(isCurrent
                ? Color.FromArgb(0x22, 0x00, 0x77, 0xCC)
                : Color.FromArgb(0x0D, 0x88, 0x88, 0x88)),
        };
        Grid.SetColumnSpan(card, isOffline ? 5 : 4);
        grid.Children.Add(card);

        var avatar = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(19),
            Background = badgeBrush,
        };
        var avatarText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(acc.Name) ? "?" : acc.Name[..1].ToUpperInvariant(),
            FontSize = 16, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var avatarImg = new System.Windows.Controls.Image
        {
            Width = 38, Height = 38, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed,
        };
        var avatarGrid = new Grid();
        avatarGrid.Children.Add(avatarImg);
        avatarGrid.Children.Add(avatarText);
        avatar.Child = avatarGrid;
        grid.Children.Add(avatar);
        _ = LoadAvatarIntoAsync(avatarImg, avatarText, acc);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal };
        var name = new TextBlock { Text = acc.Name, FontSize = 14.5, FontWeight = FontWeights.SemiBold };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        namePanel.Children.Add(name);
        if (isCurrent)
        {
            var cur = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 1, 6, 2), Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x77, 0xCC)),
            };
            var curText = new TextBlock
            {
                Text = App.Current.TryFindResource("S.Acct.Current") as string ?? "当前",
                FontSize = 10.5, Foreground = Brushes.White,
            };
            cur.Child = curText;
            namePanel.Children.Add(cur);
        }
        var type = new TextBlock { Text = badge, FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) };
        type.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        info.Children.Add(namePanel);
        info.Children.Add(type);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        if (!isCurrent)
        {
            var btnUse = new Wpf.Ui.Controls.Button
            {
                Content = App.Current.TryFindResource("S.Acct.SetCurrent") as string ?? "切换",
                MinHeight = 30, Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            };
            btnUse.Click += (_, _) =>
            {
                AccountStore.Instance.CurrentName = acc.Name;
                AccountStore.Instance.CurrentType = acc.Type; // 同名不同类型账号需要类型定位（v0.1.3）
                AccountStore.Instance.Save();
                onChanged();
            };
            Grid.SetColumn(btnUse, 2);
            grid.Children.Add(btnUse);
        }

        if (isOffline)
        {
            // 离线皮肤（PCL 同款）：仅离线账号可设；点击弹菜单（更换/清除）
            var hasSkin = !string.IsNullOrWhiteSpace(acc.SkinPath) && File.Exists(acc.SkinPath);
            var btnSkin = new Wpf.Ui.Controls.Button
            {
                Content = (App.Current.TryFindResource("S.Acct.Skin") as string ?? "皮肤") + (hasSkin ? " ✓" : ""),
                MinHeight = 30, Padding = new Thickness(12, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            };
            btnSkin.Click += (_, _) => OpenSkinMenu(win, btnSkin, acc, onChanged);
            Grid.SetColumn(btnSkin, 3);
            grid.Children.Add(btnSkin);
        }

        var btnDel = new Wpf.Ui.Controls.Button
        {
            Content = App.Current.TryFindResource("S.Acct.Delete") as string ?? "删除",
            MinHeight = 30, Padding = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Appearance = Wpf.Ui.Controls.ControlAppearance.Danger,
        };
        btnDel.Click += (_, _) =>
        {
            if (MessageBox.Show(win,
                    string.Format(App.Current.TryFindResource("S.Acct.DeleteConfirm") as string
                        ?? "确定删除账号 {0} 吗？", acc.Name),
                    "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            AccountStore.Instance.Remove(acc.Name, acc.Type); // 只删对应类型，同名其他类型账号保留
            onChanged();
        };
        Grid.SetColumn(btnDel, isOffline ? 4 : 3);
        grid.Children.Add(btnDel);

        return grid;
    }
}
