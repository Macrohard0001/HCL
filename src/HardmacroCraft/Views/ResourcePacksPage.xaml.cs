using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.0 资源包下载页（PCL 同款独立页，Modrinth resourcepack 分类）：
/// - 进入页面默认显示推荐内容（按下载量排序的热门资源包，PCL 式）；
/// - 目标版本下拉框决定安装目录（版本隔离时 versions/&lt;版本&gt;/resourcepacks，否则根 resourcepacks）；
/// - 下载经任务中心登记（含速度与当前文件）；加载指示器页面居中；PCL 式翻页。
/// </summary>
public partial class ResourcePacksPage : Page
{
    private List<ModrinthService.SearchHit> _pageHits = new();
    private int _page = 1;
    private int _totalHits;
    private bool _busy;
    private bool _recommendLoaded; // 推荐内容只在首次进入时拉取一次

    public ResourcePacksPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshVersions();
            if (!_recommendLoaded)
            {
                _recommendLoaded = true;
                // 默认推荐内容：空关键词 + 下载量排序（PCL 式推荐列表）
                _ = SearchCoreAsync(reset: true, indexOverride: "downloads", recommend: true);
            }
        };
    }

    private string SortIndex => "downloads"; // 资源包页固定按下载量推荐（PCL 推荐同款口径）

    private static string S(string key) => App.Current?.TryFindResource(key) as string ?? "";

    private void RefreshVersions()
    {
        var versions = App.LauncherService.GetInstalledVersions();
        CmbVersion.ItemsSource = versions;
        if (versions.Count > 0)
            CmbVersion.SelectedItem = versions.Contains(AppSettings.Instance.LastVersion)
                ? AppSettings.Instance.LastVersion : versions[^1];
    }

    private string TargetDir()
    {
        var version = CmbVersion.SelectedItem as string;
        var isolation = VersionSettingsStore.EffectiveIsolation(
            version, AppSettings.Instance.VersionIsolation);
        return ModTargetDir.Resolve(AppSettings.Instance.GameRoot, version, "resourcepack", isolation);
    }

    private void Log(string s)
    {
        // v0.15.0：底部常驻提示行移除（PCL 式）—— 状态文本只在有话要说时短暂出现
        Dispatcher.Invoke(() =>
        {
            TxtStatus.Text = s;
            TxtStatus.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
        });
    }

    private void SetBusy(bool on, bool isPaging = false)
    {
        Dispatcher.Invoke(() =>
        {
            _busy = on;
            PnlSearching.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on) TxtSearching.Text = isPaging ? S("S.Mod.LoadingMore") : S("S.Mod.Searching");
            BtnSearch.IsEnabled = !on;
            LstResults.IsEnabled = !on;
        });
    }

    private async void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void Pager_Go(int page) => _ = SearchCoreAsync(gotoPage: page);

    private async void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        var v = CmbVersion.SelectedItem as string;
        if (!string.IsNullOrEmpty(v) && AppSettings.Instance.LastVersion != v)
        {
            AppSettings.Instance.LastVersion = v;
            try { AppSettings.Instance.Save(); } catch { }
        }
    }

    private async Task SearchAsync() => await SearchCoreAsync(reset: true);

    private async Task SearchCoreAsync(bool reset = false, int? gotoPage = null, string? indexOverride = null, bool recommend = false)
    {
        if (_busy) return;
        try
        {
            _busy = true;
            SetBusy(true, isPaging: gotoPage is not null && gotoPage != 1);
            var page = Math.Max(1, gotoPage ?? 1);
            var index = indexOverride ?? SortIndex;
            Log(recommend
                ? App.Current.TryFindResource("S.Rp.LoadingRecommend") as string ?? "正在获取推荐资源包..."
                : App.Current.TryFindResource("S.Sh.Searching") as string ?? "正在搜索...");
            var offset = PclPager.OffsetOf(page, PclPager.SearchPageSize);
            var (hits, total) = await App.Modrinth.SearchAsync(
                TxtQuery.Text ?? "", "resourcepack", index, offset);
            _page = page;
            _totalHits = total;
            _pageHits = hits.ToList();

            LstResults.ItemsSource = _pageHits.Select(h => new
            {
                h.Title,
                h.Description,
                IconUrl = string.IsNullOrEmpty(h.IconUrl) ? null : h.IconUrl,
                AuthorText = string.IsNullOrEmpty(h.Author) ? "" : "@" + h.Author,
                Badges = ModsPage.BuildBadges(h),
                Hit = h,
            }).ToList();

            PnlEmpty.Visibility = _pageHits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_pageHits.Count == 0)
                TxtEmpty.Text = string.Format(
                    App.Current.TryFindResource("S.Mod.NoResults") as string ?? "没有找到与 {0} 相关的结果。",
                    TxtQuery.Text ?? "");

            UpdatePager();
        }
        catch (Exception ex)
        {
            Log(string.Format(
                App.Current.TryFindResource("S.Sh.SearchFail") as string ?? "搜索失败: {0}",
                ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>v0.15.0：PCL 式翻页条（总页数 > 1 时显示）。</summary>
    private void UpdatePager()
    {
        var totalPages = PclPager.TotalPages(_totalHits, PclPager.SearchPageSize);
        if (totalPages <= 1)
        {
            PnlPager.Visibility = Visibility.Collapsed;
            TxtTotal.Text = _totalHits > 0 ? string.Format(S("S.Pg.Total"), _totalHits) : "";
            return;
        }
        PnlPager.Visibility = Visibility.Visible;
        PagerBar.Build(PnlPager, PclPager.BuildButtons(_page, totalPages), Pager_Go);
        TxtTotal.Text = string.Format(S("S.Pg.PageOf"), _page, totalPages) + " · " + string.Format(S("S.Pg.Total"), _totalHits);
    }

    // ---------- 安装：下载主文件到目标版本的 resourcepacks 目录 ----------
    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if ((sender as FrameworkElement)?.DataContext is not { } tag) return;
        var hit = tag.GetType().GetProperty("Hit")!.GetValue(tag) as ModrinthService.SearchHit;
        if (hit == null) return;
        await InstallAsync(hit);
    }

    private async Task InstallAsync(ModrinthService.SearchHit hit)
    {
        var dir = TargetDir();
        Directory.CreateDirectory(dir);
        SetBusy(true);
        // 任务与令牌提到 try 外：catch 分支需要引用它们写终态
        var task = DownloadTaskService.Instance.StartCancellable(
            string.Format(App.Current.TryFindResource("S.Rp.TaskTitle") as string ?? "资源包下载：{0}", hit.Title), "");
        var dlCt = DownloadTaskService.Instance.TokenFor(task);
        try
        {
            Log($"{App.Current.TryFindResource("S.Sh.Fetching") as string ?? "获取版本列表"}: {hit.Title}...");
            var versions = await App.Modrinth.GetVersionsAsync(hit.Slug);
            // 资源包本体基本与游戏版本解耦（格式兼容时通用）：优先主文件最新的 release
            var primary = versions
                .OrderByDescending(v => v.VersionType == "release")
                .ThenByDescending(v => v.DatePublished)
                .FirstOrDefault(v => v.Files.FirstOrDefault(f => f.Primary != null) != null)
                ?? versions.FirstOrDefault()
                ?? throw new Exception(App.Current.TryFindResource("S.Sh.NoVersion") as string ?? "该资源包没有可下载的版本。");
            var file = primary.Files.FirstOrDefault(f => f.Primary != null) ?? primary.Files[0];

            var dest = Path.Combine(dir, file.Filename);
            if (File.Exists(dest))
            {
                Log(App.Current.TryFindResource("S.Sh.AlreadyExists") as string ?? "已存在（跳过下载）: " + file.Filename);
                DownloadTaskService.Instance.Complete(task, file.Filename);
                return;
            }

            task.Detail = file.Filename;
            task.TotalBytes = file.Size;

            long done = 0;
            var meter = new SpeedMeter();
            var progress = new Progress<long>(delta => Dispatcher.Invoke(() =>
            {
                done += delta;
                var speed = meter.Observe(done);
                task.SetProgress(file.Size > 0 ? done * 100.0 / file.Size : -1,
                    file.Filename, speed, done, file.Size);
            }));

            Log($"{App.Current.TryFindResource("S.Sh.Downloading") as string ?? "下载"} {file.Filename} → {dir}");
            await App.Modrinth.DownloadAsync(file.Url, dest, progress, dlCt);
            DownloadTaskService.Instance.Complete(task, file.Filename);
            Log(App.Current.TryFindResource("S.Sh.InstallDone") as string ?? "下载完成: " + dest);
        }
        catch (OperationCanceledException) when (dlCt.IsCancellationRequested)
        {
            DownloadTaskService.Instance.Cancel(task, "已取消");
            Log(App.Current.TryFindResource("S.Sh.Canceled") as string ?? "下载已取消。");
        }
        catch (Exception ex)
        {
            DownloadTaskService.Instance.Fail(task, ex.Message);
            Log((App.Current.TryFindResource("S.Sh.InstallFail") as string ?? "安装失败: ") + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = TargetDir();
        Directory.CreateDirectory(dir);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir)
            {
                UseShellExecute = true,
            });
        }
        catch { /* Linux 调试环境无 explorer 时忽略 */ }
    }
}
