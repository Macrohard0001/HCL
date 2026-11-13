using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.13 光影包下载页（PCL 同款独立页，Modrinth shader 分类）：
/// - 进入页面默认显示推荐内容（按下载量排序的热门光影包，PCL 式）；
/// - 目标版本下拉框决定安装目录（版本隔离时 versions/&lt;版本&gt;/shaderpacks，否则根 shaderpacks）；
/// - 下载经任务中心登记（含速度与当前文件，任务卡片 / 详情页展示）；
/// - 底部状态为容器内旋转加载指示器 + 文本（不再放进度条）。
/// </summary>
public partial class ShadersPage : Page
{
    private readonly List<ModrinthService.SearchHit> _hits = new();
    private int _totalHits;
    private bool _busy;
    private bool _recommendLoaded; // 推荐内容只在首次进入时拉取一次

    public ShadersPage()
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

    private string SortIndex => "downloads"; // 光影包页固定按下载量推荐（PCL 推荐同款口径）

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
        return ModTargetDir.Resolve(AppSettings.Instance.GameRoot, version, "shader", isolation);
    }

    private void Log(string s)
    {
        Dispatcher.Invoke(() => TxtStatus.Text = s);
    }

    private void SetBusy(bool on)
    {
        Dispatcher.Invoke(() =>
        {
            _busy = on;
            RingStatus.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BtnSearch.IsEnabled = !on;
            LstResults.IsEnabled = !on;
        });
    }

    private async void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void BtnMore_Click(object sender, RoutedEventArgs e)
        => await SearchCoreAsync(reset: false);

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

    private async Task SearchCoreAsync(bool reset, string? indexOverride = null, bool recommend = false)
    {
        if (_busy) return;
        try
        {
            if (reset)
            {
                _hits.Clear();
                _totalHits = 0;
            }
            SetBusy(true);
            var index = indexOverride ?? SortIndex;
            Log(recommend
                ? App.Current.TryFindResource("S.Sh.LoadingRecommend") as string ?? "正在获取推荐光影包..."
                : App.Current.TryFindResource("S.Sh.Searching") as string ?? "正在搜索...");
            var (page, total) = await App.Modrinth.SearchAsync(
                TxtQuery.Text ?? "", "shader", index, _hits.Count);
            _totalHits = total;
            _hits.AddRange(page.Where(h => !_hits.Any(x => x.ProjectId == h.ProjectId)));

            LstResults.ItemsSource = _hits.Select(h => new
            {
                h.Title,
                h.Description,
                IconUrl = string.IsNullOrEmpty(h.IconUrl) ? null : h.IconUrl,
                AuthorText = string.IsNullOrEmpty(h.Author) ? "" : "@" + h.Author,
                Badges = ModsPage.BuildBadges(h),
                Hit = h,
            }).ToList();

            PnlEmpty.Visibility = _hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_hits.Count == 0)
                TxtEmpty.Text = string.Format(
                    App.Current.TryFindResource("S.Mod.NoResults") as string ?? "没有找到与 {0} 相关的结果。",
                    TxtQuery.Text ?? "");

            var moreText = App.Current?.TryFindResource("S.Mod.LoadMore") as string ?? "加载更多";
            BtnMore.Visibility = _hits.Count < _totalHits ? Visibility.Visible : Visibility.Collapsed;
            BtnMore.IsEnabled = _hits.Count < _totalHits;
            BtnMore.Content = _hits.Count < _totalHits
                ? $"{moreText}（{_hits.Count}/{_totalHits}）"
                : App.Current?.TryFindResource("S.Mod.NoMore") as string ?? "没有更多结果了";
            Log(string.Format(
                App.Current.TryFindResource("S.Sh.ShownCount") as string ?? "已显示 {0} / {1} 个光影包。",
                _hits.Count, _totalHits));
        }
        catch (Exception ex)
        {
            Log(App.Current.TryFindResource("S.Sh.SearchFail") as string ?? "搜索失败: " + ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---------- 安装：下载主文件到目标版本的 shaderpacks 目录 ----------
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
            string.Format(App.Current.TryFindResource("S.Sh.TaskTitle") as string ?? "光影包下载：{0}", hit.Title), "");
        var dlCt = DownloadTaskService.Instance.TokenFor(task);
        try
        {
            Log($"{App.Current.TryFindResource("S.Sh.Fetching") as string ?? "获取版本列表"}: {hit.Title}...");
            var versions = await App.Modrinth.GetVersionsAsync(hit.Slug);
            // 光影包本体基本与游戏版本解耦：优先主文件最新的 release
            var primary = versions
                .OrderByDescending(v => v.VersionType == "release")
                .ThenByDescending(v => v.DatePublished)
                .FirstOrDefault(v => v.Files.FirstOrDefault(f => f.Primary != null) != null)
                ?? versions.FirstOrDefault()
                ?? throw new Exception(App.Current.TryFindResource("S.Sh.NoVersion") as string ?? "该光影包没有可下载的版本。");
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
                Log($"{file.Filename} · {SpeedMeter.FormatBytes(done)}/{SpeedMeter.FormatBytes(file.Size)} · {SpeedMeter.FormatSpeed(speed)}");
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
