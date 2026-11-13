using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

public partial class ModpacksPage : Page
{
    private readonly List<ModrinthService.SearchHit> _hits = new();
    private int _totalHits;
    private bool _busy;

    private bool _recommendLoaded; // v0.1.13：推荐内容只在首次进入时拉取一次

    public ModpacksPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            // v0.1.13：默认显示推荐内容（空关键词 + 下载量排序的热门整合包，PCL 同款）
            if (!_recommendLoaded && _hits.Count == 0 && string.IsNullOrWhiteSpace(TxtQuery.Text))
            {
                _recommendLoaded = true;
                _ = SearchCoreAsync(reset: true, indexOverride: "downloads", recommend: true);
            }
        };
    }

    private string SortIndex => (CmbSort.SelectedItem as ComboBoxItem)?.Tag as string ?? "relevance";

    private void Log(string s)
    {
        Dispatcher.Invoke(() =>
        {
            // fix8：有输出时才展开进度卡，空闲时不占版面
            CardProgress.Visibility = Visibility.Visible;
            TxtProgress.Text += s + Environment.NewLine;
        });
    }

    private void ClearLog()
    {
        Dispatcher.Invoke(() =>
        {
            TxtProgress.Text = "";
            CardProgress.Visibility = Visibility.Collapsed;
        });
    }

    private async void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 初始化时也会触发一次；列表为空则忽略，有结果则按新排序重查
        if (_hits.Count > 0) await SearchAsync();
    }

    private async Task SearchAsync() => await SearchCoreAsync(reset: true);

    private async void BtnMore_Click(object sender, RoutedEventArgs e) => await SearchCoreAsync(reset: false);

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
            _busy = true;
            ClearLog();
            var index = indexOverride ?? SortIndex;
            Log(recommend
                ? App.Current.TryFindResource("S.Mp.LoadingRecommend") as string ?? "正在获取推荐整合包..."
                : reset ? "搜索 Modrinth 整合包..." : "加载更多...");
            var (page, total) = await App.Modrinth.SearchAsync(
                TxtQuery.Text ?? "", "modpack", index, _hits.Count);
            _totalHits = total;
            _hits.AddRange(page.Where(h => !_hits.Any(x => x.ProjectId == h.ProjectId)));

            LstResults.ItemsSource = _hits.Select(h => new
            {
                h.Title,
                h.Description,
                Slug = h.Slug,
                IconUrl = string.IsNullOrEmpty(h.IconUrl) ? null : h.IconUrl,
                AuthorText = string.IsNullOrEmpty(h.Author) ? "" : "@" + h.Author,
                Badges = ModsPage.BuildBadges(h),
            }).ToList();

            var moreText = App.Current?.TryFindResource("S.Mod.LoadMore") as string ?? "加载更多";
            var noMoreText = App.Current?.TryFindResource("S.Mod.NoMore") as string ?? "没有更多结果了";
            BtnMore.Visibility = _hits.Count < _totalHits ? Visibility.Visible : Visibility.Collapsed;
            BtnMore.IsEnabled = _hits.Count < _totalHits;
            BtnMore.Content = _hits.Count < _totalHits
                ? $"{moreText}（{_hits.Count}/{_totalHits}）"
                : noMoreText;
            Log($"已显示 {_hits.Count} / {_totalHits} 个结果。");
        }
        catch (Exception ex)
        {
            Log("搜索失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- 安装远程整合包 ----------
    private async void BtnInstallPack_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if ((sender as System.Windows.Controls.Button)?.Tag is not { } tag) return;
        await InstallFromModrinthAsync((string)tag.GetType().GetProperty("Slug")!.GetValue(tag)!);
    }

    private async Task InstallFromModrinthAsync(string slug)
    {
        _busy = true;
        try
        {
            ClearLog();
            Log($"获取 {slug} 版本列表...");
            var versions = await App.Modrinth.GetVersionsAsync(slug);
            var primary = versions.FirstOrDefault(v => v.Files.FirstOrDefault(f => f.Primary != null) != null)
                ?? versions.FirstOrDefault()
                ?? throw new Exception("该整合包没有可下载的版本。");
            var file = primary.Files.FirstOrDefault(f => f.Primary != null) ?? primary.Files[0];

            // 临时下载放到便携 config/temp/，不再写系统 %TEMP%（用户要求零外部写入）
            var tmp = Path.Combine(AppSettings.ConfigDir, "temp", file.Filename);
            Directory.CreateDirectory(Path.GetDirectoryName(tmp)!);
            Log($"下载 {file.Filename} ({SpeedMeter.FormatBytes(file.Size)})...");
            long done = 0;
            var meter = new SpeedMeter();
            // v0.1.13：整合包下载登记任务中心（速度 / 当前文件 / 字节，任务卡片与详情页展示）
            var dlTask = DownloadTaskService.Instance.StartCancellable(
                string.Format(App.Current.TryFindResource("S.Mp.TaskTitle") as string ?? "整合包下载：{0}", slug),
                file.Filename);
            dlTask.TotalBytes = file.Size;
            var progress = new Progress<long>(delta =>
                Dispatcher.Invoke(() =>
                {
                    done += delta;
                    // v0.1.13：进度条移除，旋转指示器随卡片显示；速度经任务条目展示
                    dlTask.SetProgress(file.Size > 0 ? done * 100.0 / file.Size : -1,
                        file.Filename, meter.Observe(done), done, file.Size);
                    TxtProgress.Text = $"下载整合包: {file.Filename} · {SpeedMeter.FormatBytes(done)}/{SpeedMeter.FormatBytes(file.Size)} · {SpeedMeter.FormatSpeed(meter.Observe(done))}";
                }));
            await App.Modrinth.DownloadAsync(file.Url, tmp, progress,
                DownloadTaskService.Instance.TokenFor(dlTask));
            DownloadTaskService.Instance.Complete(dlTask, file.Filename);

            Log($"下载完成: {tmp}");
            var info = ModpackService.ReadInfo(tmp);
            Log($"整合包: {info.Name} | MC {info.GameVersion} | 装载器 {info.Loader ?? "无"} {info.LoaderVersion ?? ""} | {info.FileCount} 个文件");
            await InstallMrpackAsync(tmp);
        }
        catch (Exception ex)
        {
            Log("安装失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---------- 安装本地 .mrpack ----------
    private async void BtnLocalInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dlg = new OpenFileDialog
        {
            Title = "选择 Modrinth 整合包",
            Filter = "Modrinth 整合包 (*.mrpack)|*.mrpack|所有文件|*.*",
        };
        if (dlg.ShowDialog() != true) return;
        _busy = true;
        try
        {
            ClearLog();
            await InstallMrpackAsync(dlg.FileName);
        }
        catch (Exception ex)
        {
            Log("安装失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task InstallMrpackAsync(string mrpackPath)
    {
        var info = ModpackService.ReadInfo(mrpackPath);
        if (!string.Equals(info.GameVersion, "1.0", StringComparison.OrdinalIgnoreCase) &&
            info.Loader == null && info.LoaderVersion == null)
        {
            Log("提示: 该整合包不含装载器。");
        }
        // v0.1.13：默认版本名对齐 PCL —— 直接用整合包名（保留大小写与空格，
        // 仅去除非法文件名字符）；此前 pack-xxx 小写连字符风格与 PCL 不一致
        var versionName = Sanitize(info.Name);
        Log($"开始安装整合包 [{info.Name}] → 版本 {versionName}");
        Log("（整合包默认使用版本隔离，mods / 存档均独立）");

        long done = 0;
        var meter = new SpeedMeter();
        // v0.1.13：整合包文件批量下载登记任务中心（速度 / 当前文件）
        var task = DownloadTaskService.Instance.StartCancellableKeyed(
            "modpack-install:" + versionName,
            string.Format(App.Current.TryFindResource("S.Mp.TaskInstall") as string ?? "安装整合包：{0}", info.Name),
            info.Name);
        var progress = new Progress<ModpackService.ModpackInstallProgress>(p =>
            Dispatcher.Invoke(() =>
            {
                done = p.DoneBytes;
                var speed = meter.Observe(p.DoneBytes);
                task.SetProgress(p.TotalBytes > 0 ? p.DoneBytes * 100.0 / p.TotalBytes : -1,
                    p.CurrentFile, speed, p.DoneBytes, p.TotalBytes);
                TxtProgress.Text = $"{p.CurrentFile} · 文件 {p.DoneFiles}/{p.TotalFiles} · {SpeedMeter.FormatBytes(p.DoneBytes)}/{SpeedMeter.FormatBytes(p.TotalBytes)} · {SpeedMeter.FormatSpeed(speed)}";
            }));

        try
        {
            var launchVersion = await App.Modpacks.InstallAsync(
                mrpackPath, versionName, App.LauncherService, progress, s => Log(s));
            DownloadTaskService.Instance.Complete(task, launchVersion);
            Log($"可启动版本: {launchVersion}");
        }
        catch (OperationCanceledException)
        {
            DownloadTaskService.Instance.Cancel(task, "已取消");
            throw;
        }
        catch
        {
            // 保证批量下载任务不会一直挂「进行中」
            if (task.IsActive) DownloadTaskService.Instance.Fail(task);
            throw;
        }
    }

    private static string Sanitize(string name)
    {
        // v0.1.13：仅去除非法文件名字符，保留空格与大小写（PCL 式默认实例名）
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Where(c => !bad.Contains(c)).ToArray()).Trim();
        return clean.Length == 0 ? "modpack" : clean;
    }
}
