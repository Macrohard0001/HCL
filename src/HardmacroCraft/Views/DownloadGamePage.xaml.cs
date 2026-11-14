using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;
using Wpf.Ui.Controls;

namespace HardmacroCraft.Views;

public partial class DownloadGamePage : Page
{
    private List<CmlLib.Core.VersionMetadata.IVersionMetadata> _all = new();
    /// <summary>v0.15.0：当前筛选后的完整列表（翻页浏览，不再 Take(300) 截断）。</summary>
    private List<CmlLib.Core.VersionMetadata.IVersionMetadata> _filtered = new();
    private HashSet<string> _installed = new();
    private string _latestRelease = "";
    private string _latestSnapshot = "";
    private string _filter = "release";
    private int _vanillaPage = 1;
    private bool _loaded;
    private bool _loading;

    public DownloadGamePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => { if (!_loaded) { _loaded = true; await LoadVersionListAsync(); } };
    }

    // ---------- 版本清单加载 ----------
    private async Task LoadVersionListAsync()
    {
        if (_loading) return;
        _loading = true;
        BtnRefreshVersions.IsEnabled = false;
        // v0.15.0：加载指示器页面居中（过渡层），底部常驻提示行移除
        PnlLoading.Visibility = Visibility.Visible;
        TxtLoading.Text = (string)FindResource("S.Dl.Loading");
        try
        {
            _all = (await App.LauncherService.GetAllVersionsAsync()).ToList();
            _installed = new HashSet<string>(App.LauncherService.GetInstalledVersions());
            _latestRelease = _all.FirstOrDefault(v => v.Type == "release")?.Name ?? "";
            _latestSnapshot = _all.FirstOrDefault(v => v.Type == "snapshot")?.Name ?? "";

            ApplyVanillaFilter();
        }
        catch (Exception ex)
        {
            SetBusy((string)FindResource("S.Dl.LoadFail") + ex.Message);
            _loaded = false; // 下次进入页面自动重试
        }
        finally
        {
            _loading = false;
            BtnRefreshVersions.IsEnabled = true;
            PnlLoading.Visibility = Visibility.Collapsed;
        }
    }

    private async void BtnRefreshVersions_Click(object sender, RoutedEventArgs e)
    {
        await LoadVersionListAsync();
    }

    // ---------- 过滤 + 搜索（fix9：五分类，对齐 PCL：全部/正式/快照/愚人节/远古） ----------
    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        _filter = sender switch
        {
            { } b when ReferenceEquals(b, BtnFAll) => "all",
            { } b when ReferenceEquals(b, BtnFSnapshot) => "snapshot",
            { } b when ReferenceEquals(b, BtnFApril) => "april",
            { } b when ReferenceEquals(b, BtnFOld) => "old",
            _ => "release",
        };
        foreach (var btn in new[] { BtnFAll, BtnFRelease, BtnFSnapshot, BtnFApril, BtnFOld })
            btn.Appearance = ControlAppearance.Secondary;
        ((Wpf.Ui.Controls.Button)sender).Appearance = ControlAppearance.Primary;
        ApplyVanillaFilter();
    }

    private void TxtVanillaSearch_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyVanillaFilter();

    private void Pager_Go(int page)
    {
        _vanillaPage = page;
        RenderVanillaPage();
    }

    /// <summary>v0.15.0：PCL 式翻页条（版本列表每页 50 条，全量浏览不再截断）。</summary>
    private void UpdateVanillaPager()
    {
        var totalPages = PclPager.TotalPages(_filtered.Count, PclPager.LocalPageSize);
        if (totalPages <= 1)
        {
            PnlPager.Visibility = Visibility.Collapsed;
            TxtPageOf.Text = _filtered.Count > 0 ? string.Format((string)FindResource("S.Pg.Total"), _filtered.Count) : "";
            return;
        }
        PnlPager.Visibility = Visibility.Visible;
        Views.PagerBar.Build(PnlPager, PclPager.BuildButtons(_vanillaPage, totalPages), Pager_Go);
        TxtPageOf.Text = string.Format((string)FindResource("S.Pg.PageOf"), _vanillaPage, totalPages)
            + " · " + string.Format((string)FindResource("S.Pg.Total"), _filtered.Count);
    }

    private void ApplyVanillaFilter()
    {
        var kw = TxtVanillaSearch.Text?.Trim() ?? "";
        IEnumerable<CmlLib.Core.VersionMetadata.IVersionMetadata> q = _all;
        q = _filter switch
        {
            "all" => q,
            // fix9：愚人节版在清单里 type 也是 snapshot，快照分类下排除，避免重复出现
            "snapshot" => q.Where(v => v.Type == "snapshot" && !VersionKinds.IsAprilFools(v.Name)),
            "april" => q.Where(v => VersionKinds.IsAprilFools(v.Name)),
            "old" => q.Where(v => v.Type is "old_beta" or "old_alpha"),
            _ => q.Where(v => v.Type == "release"),
        };
        if (kw.Length > 0)
            q = q.Where(v => v.Name.Contains(kw, StringComparison.OrdinalIgnoreCase));

        // v0.15.0：完整物化筛选结果（翻页展示，PCL 式）—— 换筛选/搜索时回到第 1 页
        _filtered = q.ToList();
        _vanillaPage = 1;
        RenderVanillaPage();
    }

    private void RenderVanillaPage()
    {
        var kw = TxtVanillaSearch.Text?.Trim() ?? "";
        var pageItems = _filtered
            .Skip(PclPager.OffsetOf(_vanillaPage, PclPager.LocalPageSize))
            .Take(PclPager.LocalPageSize)
            .Select(v =>
        {
            var name = v.Name;
            var descKey = "S.Dl.Desc." + name;
            // 注意：FindResource 缺键时异常经 Dispatcher 异步投递，try/catch 抓不住
            // （v1.4.0 的 crash.log 即此因）→ 必须用 TryFindResource
            var desc = TryFindResource(descKey) as string ?? "";

            var isLatestRel = name == _latestRelease;
            var isLatestSnap = name == _latestSnapshot;
            if (isLatestRel && _filter == "release" && kw.Length == 0) desc = (string)FindResource("S.Dl.LatestRel");
            else if (isLatestSnap && kw.Length == 0) desc = (string)FindResource("S.Dl.LatestSnap");

            var isOld = v.Type is "old_beta" or "old_alpha";
            // fix9：愚人节版独立类型与徽章（清单里 type 与快照同为 snapshot，按已知 ID 识别）
            var isApril = VersionKinds.IsAprilFools(name);
            return new VanillaRow
            {
                Name = name,
                TypeText = isApril
                    ? (string)FindResource("S.Dl.AprilFools")
                    : v.Type switch
                    {
                        "release" => (string)FindResource("S.Dl.Release"),
                        "snapshot" => (string)FindResource("S.Dl.Snapshot"),
                        "old_beta" => (string)FindResource("S.Dl.Beta"),
                        _ => (string)FindResource("S.Dl.Alpha"),
                    },
                TypeBadgeFg = isApril
                    ? System.Windows.Media.Brushes.MediumVioletRed
                    : v.Type == "release"
                    ? System.Windows.Media.Brushes.MediumSeaGreen
                    : isOld ? System.Windows.Media.Brushes.Gray
                    : System.Windows.Media.Brushes.DarkOrange,
                TimeText = string.Format((string)FindResource("S.Dl.ReleasedOn"),
                    v.ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd")),
                Desc = desc,
                LatestVisibility = (isLatestRel || isLatestSnap) ? Visibility.Visible : Visibility.Collapsed,
                InstalledVisibility = _installed.Contains(name) ? Visibility.Visible : Visibility.Collapsed,
            };
        }).ToList();

        LstVanilla.ItemsSource = pageItems;
        TxtVanillaEmpty.Text = string.Format((string)FindResource("S.Dl.EmptyResult"), kw);
        TxtVanillaEmpty.Visibility = _filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateVanillaPager();
    }

    private sealed class VanillaRow
    {
        public string Name { get; set; } = "";
        public string TypeText { get; set; } = "";
        public System.Windows.Media.Brush TypeBadgeFg { get; set; } = System.Windows.Media.Brushes.Gray;
        public string TimeText { get; set; } = "";
        public string Desc { get; set; } = "";
        public Visibility LatestVisibility { get; set; }
        public Visibility InstalledVisibility { get; set; }
    }

    private void SetBusy(string? msg)
    {
        // v0.15.0：底部常驻提示行移除 —— 状态文本只在有话要说时短暂出现
        TxtStatus.Text = msg ?? "";
        TxtStatus.Visibility = string.IsNullOrEmpty(msg) ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- 版本行安装：PCL 式弹窗选择加载器 ----------
    // 点击版本行的「安装」按钮 → LoaderPickerWindow 弹窗：
    // 原版 / Forge / NeoForge / Fabric / Quilt / Legacy Fabric / LiteLoader 七选一，
    // 选择加载器版本后安装（v0.1.0-beta：装载器独立卡片迁移至此弹窗，并新增冷门装载器）。
    private async void BtnVanillaRowInstall_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VanillaRow row) return;
        var dlg = new LoaderPickerWindow(row.Name) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
        // DialogResult: true = 安装完成 / null = 已转后台安装（任务中心看进度）/ false = 用户取消
        if (dlg.DialogResult == false) return;
        _installed = new HashSet<string>(App.LauncherService.GetInstalledVersions());
        ApplyVanillaFilter();
        SetBusy(dlg.DialogResult == true
            ? "安装完成: " + row.Name
            : (string)FindResource("S.Dl.BackgroundInfo") + " " + row.Name);

        // v0.1.8 紧急修复：安装完成直接询问是否启动（对齐主流启动器下载完即玩的行为）。
        // 此前装完只更新徽章，用户回到首页还可能因为下拉框未刷新选不到新版本。
        if (dlg.DialogResult == true)
        {
            var wantLaunch = HmMsg.Confirm(Window.GetWindow(this), "安装完成",
                $"安装完成：{row.Name}\n\n是否立即启动该版本？");
            if (wantLaunch && Application.Current.MainWindow is MainWindow main)
            {
                try
                {
                    main.RootNavigation.Navigate(typeof(HomePage));
                    var home = HomePage.Current;
                    if (home != null) await home.LaunchFromExternalAsync(row.Name);
                }
                catch (Exception ex)
                {
                    HmMsg.Info(Window.GetWindow(this), "启动失败", "启动失败：" + ex.Message);
                }
            }
        }
    }
}
