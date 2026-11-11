using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>搜索结果行模型（可勾选，供批量安装）。</summary>
public sealed class ModRowItem : INotifyPropertyChanged
{
    private bool _isSelected;
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? IconUrl { get; init; }
    public string AuthorText { get; init; } = "";
    public ModrinthService.SearchHit Hit { get; init; } = null!;
    public List<string> Badges { get; init; } = new();

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ModsPage : Page
{
    /// <summary>v0.1.10：当前首页实例（供版本设置「打开 Mod 管理」导航后直选版本）。</summary>
    public static ModsPage? Current { get; private set; }

    private bool _busy;
    private readonly List<ModrinthService.SearchHit> _allHits = new();
    private int _totalHits;
    /// <summary>分页勾选保持：已勾选项目的 ProjectId（加载更多时恢复，新搜索时清空）。</summary>
    private readonly HashSet<string> _selectedIds = new();

    public ModsPage()
    {
        InitializeComponent();
        Current = this;
        Unloaded += (_, _) => { if (Current == this) Current = null; };
        RestoreFilters();
        Loaded += (_, _) => { RefreshTargets(); RefreshLocalMods(); };
    }

    /// <summary>v0.1.5：恢复上次使用的平台 / 类型 / 加载器（PCL 式记住筛选）。</summary>
    private void RestoreFilters()
    {
        SelectItemByTag(CmbPlatform, AppSettings.Instance.LastModPlatform);
        SelectItemByTag(CmbType, AppSettings.Instance.LastModType);
        SelectItemByTag(CmbLoader, AppSettings.Instance.LastModLoader);
        RefreshPlatformUi();
        UpdateFilterSummary();
    }

    private static void SelectItemByTag(System.Windows.Controls.ComboBox cb, string? tag)
    {
        foreach (var i in cb.Items.OfType<ComboBoxItem>())
            if ((i.Tag as string) == tag) { cb.SelectedItem = i; return; }
    }

    /// <summary>记住当前筛选（变更即落盘，PCL 同款）。</summary>
    private void RememberFilters()
    {
        AppSettings.Instance.LastModPlatform = Platform;
        AppSettings.Instance.LastModType = (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
        AppSettings.Instance.LastModLoader = Loader;
        try { AppSettings.Instance.Save(); } catch { }
        UpdateFilterSummary();
    }

    /// <summary>v0.1.8：筛选按钮上的激活条件计数（平台 / 类型 / 加载器任一非默认即计数）。</summary>
    internal static int CountActiveFilters(string platform, string type, string loader)
    {
        int n = 0;
        if (platform != "modrinth") n++;
        if (type != "mod") n++;
        if (loader != "any") n++;
        return n;
    }

    private void UpdateFilterSummary()
    {
        var n = CountActiveFilters(Platform, LocalType, Loader);
        TxtFilterCount.Text = n > 0 ? $"（{n}）" : "";
    }

    /// <summary>读语言键的统一小助手（缺省空串）。</summary>
    internal static string S(string key) => App.Current?.TryFindResource(key) as string ?? "";

    private string SortIndex => (CmbSort.SelectedItem as ComboBoxItem)?.Tag as string ?? "relevance";
    private string Platform => (CmbPlatform.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";
    private string Loader => (CmbLoader.SelectedItem as ComboBoxItem)?.Tag as string ?? "any";
    private string PlatformName => Platform == "curseforge" ? "CurseForge" : "Modrinth";
    /// <summary>当前搜索的资源类型（本地管理列表跟随切换，PCL 同款）。</summary>
    private string LocalType => (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";

    private void RefreshTargets()
    {
        var versions = App.LauncherService.GetInstalledVersions();
        CmbTargetVersion.ItemsSource = versions;
        if (versions.Count > 0)
            CmbTargetVersion.SelectedItem = versions.Contains(AppSettings.Instance.LastVersion)
                ? AppSettings.Instance.LastVersion : versions[^1];
    }

    /// <summary>v0.1.10：外部入口（版本独立设置等）导航到本页后直接选中目标版本。</summary>
    public void FocusTargetVersion(string version)
    {
        if (CmbTargetVersion.ItemsSource is List<string> versions && versions.Contains(version))
            CmbTargetVersion.SelectedItem = version;
    }

    /// <summary>从所选版本名提取纯游戏版本号（如 1.20.1-forge-47.2.0 → 1.20.1）用于平台过滤。</summary>
    internal static string? ExtractGameVersion(string? versionName)
    {
        if (string.IsNullOrWhiteSpace(versionName)) return null;
        var m = Regex.Match(versionName, @"^\d+\.\d+(\.\d+)?");
        return m.Success ? m.Value : null;
    }

    private void RefreshPlatformUi()
    {
        // CurseForge 需要用户自己的 API Key，未配置时在副标题给出引导
        TxtSubtitle.Text = Platform == "curseforge"
            ? string.Format(App.Current.TryFindResource("S.Mod.SubtitleCF") as string
                ?? "从 CurseForge 搜索（需在设置中填入免费 API Key）。")
            : App.Current.TryFindResource("S.Mod.Subtitle") as string ?? "";
        TxtSubtitle.Text = Platform == "curseforge" && !App.CurseForge.HasKey
            ? TxtSubtitle.Text + (App.Current.TryFindResource("S.Mod.NeedKey") as string
                ?? "（尚未配置 API Key：请到「设置」填写，console.curseforge.com 免费申请）")
            : TxtSubtitle.Text;
    }

    private void CmbPlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshPlatformUi();
        RememberFilters();
    }

    private void CmbLoader_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RememberFilters();
    }

    private string TargetDir(string projectType)
    {
        // v0.1.9：版本独立设置可按版本覆盖全局隔离 —— Mod 目录必须跟随生效值
        // （此前只看全局开关，覆盖了「独立隔离」的版本其 mod 会被错误放进根目录）
        var version = CmbTargetVersion.SelectedItem as string;
        var isolation = VersionSettingsStore.EffectiveIsolation(
            version, AppSettings.Instance.VersionIsolation);
        return ModTargetDir.Resolve(AppSettings.Instance.GameRoot, version, projectType, isolation);
    }

    private void Log(string s) => Dispatcher.Invoke(() => TxtProgress.Text += s + Environment.NewLine);
    private void ClearLog() => Dispatcher.Invoke(() => TxtProgress.Text = "");

    // ---------- 本机 Mod 管理（v0.1.9 对齐 PCL：元数据列表 / 多选 / 搜索 / 双击启停；
    //           .disabled 后缀 = 禁用；列表跟随搜索类型与目标版本切换目录） ----------

    /// <summary>本机 Mod 条目视图模型（绑定零转换器）。</summary>
    public sealed class LocalModItem
    {
        public string FileName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string ModVersion { get; init; } = "";
        public string Description { get; init; } = "";
        public string Loader { get; init; } = "unknown";
        public bool Enabled { get; init; } = true;
        public string SizeText { get; init; } = "";
        public string LoaderText => Loader switch
        {
            "fabric" => "Fabric",
            "quilt" => "Quilt",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            _ => "",
        };
        public string StateText { get; init; } = "";
        /// <summary>加载器徽章可见性（unknown 不显示）。</summary>
        public System.Windows.Visibility LoaderVis =>
            LoaderText.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        /// <summary>禁用状态徽章可见性。</summary>
        public System.Windows.Visibility StateVis =>
            StateText.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void RefreshLocalMods()
    {
        try
        {
            var dir = TargetDir(LocalType);
            TxtLocalDir.Text = dir;
            var kw = (TxtLocalQuery.Text ?? "").Trim();
            var all = LocalModScanner.ScanDir(dir);
            if (kw.Length > 0)
                all = all.Where(m => m.FileName.Contains(kw, StringComparison.OrdinalIgnoreCase)
                                     || m.DisplayName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            LstLocalMods.ItemsSource = all.Select(m => new LocalModItem
            {
                FileName = m.FileName,
                DisplayName = m.DisplayName,
                ModVersion = m.ModVersion,
                Description = m.Description,
                Loader = m.Loader,
                Enabled = m.Enabled,
                SizeText = FormatSize(m.SizeBytes),
                StateText = m.Enabled ? "" : S("S.Mod.Disabled"),
            }).ToList();
        }
        catch (Exception ex)
        {
            LstLocalMods.ItemsSource = Array.Empty<LocalModItem>();
            Log("读取目录失败: " + ex.Message);
        }
    }

    private void TxtLocalQuery_TextChanged(object sender, TextChangedEventArgs e) => RefreshLocalMods();

    private void BtnLocalRefresh_Click(object sender, RoutedEventArgs e) => RefreshLocalMods();

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 搜索类型切换时本地管理列表跟随显示对应目录（mod / resourcepack / shaderpacks / datapacks）
        if (!IsLoaded) return;
        RefreshLocalMods();
        RememberFilters();
    }

    private void BtnOpenModDir_Click(object sender, RoutedEventArgs e)
    {
        var dir = TargetDir(LocalType);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    /// <summary>选中的本机 Mod（多选，含 Ctrl/Shift；返回文件名列表）。</summary>
    private List<string> SelectedLocalMods => LstLocalMods.SelectedItems
        ?.Cast<LocalModItem>().Select(i => i.FileName).ToList() ?? new List<string>();

    /// <summary>切换启用状态（.disabled 后缀翻转；PCL 同款）。</summary>
    private static void ToggleModFile(string dir, string name)
    {
        var full = Path.Combine(dir, name);
        if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            File.Move(full, full[..^".disabled".Length]);
        else
            File.Move(full, full + ".disabled");
    }

    private void BtnToggleMod_Click(object sender, RoutedEventArgs e)
    {
        var names = SelectedLocalMods;
        if (names.Count == 0) return;
        var dir = TargetDir(LocalType);
        var ok = 0;
        foreach (var name in names)
        {
            try
            {
                ToggleModFile(dir, name);
                ok++;
            }
            catch (Exception ex)
            {
                Log("切换失败: " + name + " — " + ex.Message);
            }
        }
        if (ok > 0) Log(ok == 1 ? "已切换: " + names[0] : $"已切换 {ok} 项的启用状态。");
        RefreshLocalMods();
    }

    /// <summary>双击条目直接切换启用状态（PCL 同款手感）。</summary>
    private void LstLocalMods_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (LstLocalMods.SelectedItem is LocalModItem) BtnToggleMod_Click(sender, e);
    }

    private void BtnDeleteMod_Click(object sender, RoutedEventArgs e)
    {
        var names = SelectedLocalMods;
        if (names.Count == 0) return;
        // v0.1.9：删除前确认（多选时列出数量，PCL 式防误删）
        var confirmMsg = names.Count == 1
            ? (S("S.Mod.DeleteConfirm1") + names[0])
            : string.Format(S("S.Mod.DeleteConfirmN"), names.Count);
        if (!HmMsg.Confirm(Window.GetWindow(this), S("S.Ver.Delete"), confirmMsg)) return;
        var dir = TargetDir(LocalType);
        var ok = 0;
        foreach (var name in names)
        {
            try
            {
                File.Delete(Path.Combine(dir, name));
                ok++;
            }
            catch (Exception ex)
            {
                Log("删除失败: " + name + " — " + ex.Message);
            }
        }
        if (ok > 0) Log(ok == 1 ? "已删除: " + names[0] : $"已删除 {ok} 项。");
        RefreshLocalMods();
    }

    private async void TxtQuery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SearchAsync();
    }

    private async void BtnSearch_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    /// <summary>v0.1.8：筛选按钮 → 弹出筛选卡片（PCL 式，再点一次或点外部关闭）。</summary>
    private void BtnFilter_Click(object sender, RoutedEventArgs e)
    {
        UpdateFilterSummary();
        PupFilter.IsOpen = !PupFilter.IsOpen;
    }

    /// <summary>v0.1.8：重置筛选（平台 / 类型 / 加载器回默认；目标版本与排序保留）。</summary>
    private void BtnFilterReset_Click(object sender, RoutedEventArgs e)
    {
        SelectItemByTag(CmbPlatform, "modrinth");
        SelectItemByTag(CmbType, "mod");
        SelectItemByTag(CmbLoader, "any");
        RememberFilters();
    }

    /// <summary>v0.1.8：记住目标版本，下次进页自动选中同一版本（PCL 同款）。</summary>
    private void CmbTargetVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        // v0.1.9：目标版本切换 → 本机列表 / 目录提示跟随刷新（目录可能随隔离状态变化）
        RefreshLocalMods();
        var v = CmbTargetVersion.SelectedItem as string;
        if (!string.IsNullOrEmpty(v) && AppSettings.Instance.LastVersion != v)
        {
            AppSettings.Instance.LastVersion = v;
            try { AppSettings.Instance.Save(); } catch { }
        }
    }

    private async void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 初始化时也会触发一次；列表为空则忽略，有结果则按新排序重查
        if (_allHits.Count > 0) await SearchAsync();
    }

    private async Task SearchAsync() => await SearchCoreAsync(reset: true);

    private async void BtnMore_Click(object sender, RoutedEventArgs e) => await SearchCoreAsync(reset: false);

    private async Task SearchCoreAsync(bool reset)
    {
        if (_busy) return;
        var type = (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
        var gameVersion = ExtractGameVersion(CmbTargetVersion.SelectedItem as string);
        try
        {
            if (reset)
            {
                _allHits.Clear();
                _totalHits = 0;
            }
            if (reset) _selectedIds.Clear();

            _busy = true;
            SetSearching(true, !reset);
            ClearLog();
            Log(reset ? $"搜索 {PlatformName}..." : "加载更多...");
            var (hits, total) = Platform == "curseforge"
                ? await App.CurseForge.SearchAsync(TxtQuery.Text ?? "", type, SortIndex, _allHits.Count,
                    gameVersion, Loader)
                : await App.Modrinth.SearchAsync(TxtQuery.Text ?? "", type, SortIndex, _allHits.Count,
                    gameVersion, Loader);
            _totalHits = total;
            _allHits.AddRange(hits.Where(h => !_allHits.Any(x => x.ProjectId == h.ProjectId)));

            LstResults.ItemsSource = _allHits.Select(h =>
            {
                var item = new ModRowItem
                {
                    Title = h.Title,
                    Description = h.Description,
                    IconUrl = string.IsNullOrEmpty(h.IconUrl) ? null : h.IconUrl,
                    AuthorText = string.IsNullOrEmpty(h.Author) ? "" : "@" + h.Author,
                    Hit = h,
                    Badges = BuildBadges(h),
                };
                // 分页勾选保持：加载更多后恢复之前勾选的行
                item.IsSelected = _selectedIds.Contains(h.ProjectId);
                return item;
            }).ToList();
            PnlBatch.Visibility = _allHits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateBatchUi();

            // v0.1.5：空结果给 PCL 式提示（图标 + 文案），有结果则隐藏
            PnlEmpty.Visibility = _allHits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_allHits.Count == 0)
                TxtEmpty.Text = string.Format(S("S.Mod.NoResults"), TxtQuery.Text ?? "");

            var moreText = App.Current?.TryFindResource("S.Mod.LoadMore") as string ?? "加载更多";
            var noMoreText = App.Current?.TryFindResource("S.Mod.NoMore") as string ?? "没有更多结果了";
            BtnMore.Visibility = _allHits.Count < _totalHits ? Visibility.Visible : Visibility.Collapsed;
            BtnMore.IsEnabled = _allHits.Count < _totalHits;
            BtnMore.Content = _allHits.Count < _totalHits
                ? $"{moreText}（{_allHits.Count}/{_totalHits}）"
                : noMoreText;
            Log($"已显示 {_allHits.Count} / {_totalHits} 个结果。");
        }
        catch (Exception ex)
        {
            Log("搜索失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
            SetSearching(false);
        }
    }

    /// <summary>搜索过渡层（v0.1.5，PCL 式「正在搜索」）：期间禁用结果列表与搜索按钮。</summary>
    private void SetSearching(bool on, bool isMore = false)
    {
        PnlSearching.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on)
        {
            TxtSearching.Text = isMore ? S("S.Mod.LoadingMore") : S("S.Mod.Searching");
            BtnSearch.IsEnabled = false;
            LstResults.IsEnabled = false;
        }
        else
        {
            BtnSearch.IsEnabled = true;
            LstResults.IsEnabled = true;
        }
    }

    /// <summary>PCL 式徽章行：平台 / 分类 / 下载量 / 关注 / 更新时间 / 支持版本</summary>
    internal static List<string> BuildBadges(ModrinthService.SearchHit h)
    {
        var res = App.Current?.TryFindResource("S.Mod.BadgeDownloads") as string ?? "{0:N0} 下载";
        var follows = App.Current?.TryFindResource("S.Mod.BadgeFollows") as string ?? "{0:N0} 关注";
        var updated = App.Current?.TryFindResource("S.Mod.BadgeUpdated") as string ?? "更新于 {0}";
        var supports = App.Current?.TryFindResource("S.Mod.BadgeSupports") as string ?? "支持 {0}+";

        var badges = new List<string> { h.Source == "curseforge" ? "CurseForge" : "Modrinth" };
        foreach (var c in h.Categories.Take(3))
            badges.Add(c);
        badges.Add(string.Format(res, h.Downloads));
        if (h.Follows > 0) badges.Add(string.Format(follows, h.Follows));
        if (h.DateModified != default)
            badges.Add(string.Format(updated, h.DateModified.ToLocalTime().ToString("yyyy-MM-dd")));
        var top = h.Versions
            .Where(v => v.StartsWith("1.", StringComparison.Ordinal))
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (top != null) badges.Add(string.Format(supports, top));
        return badges;
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        ModRowItem? item = null;
        if (LstResults.SelectedItem is ModRowItem sel) item = sel;
        else if (sender is System.Windows.Controls.Button btn) item = btn.DataContext as ModRowItem;
        if (item == null) return;
        await DownloadWithPickerAsync(item.Hit);
    }

    /// <summary>版本选择弹窗下载流程（结果行「下载」按钮与详情页共用，v0.1.5 抽取）。</summary>
    private async Task DownloadWithPickerAsync(ModrinthService.SearchHit hit)
    {
        var type = (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
        var gameVersion = ExtractGameVersion(CmbTargetVersion.SelectedItem as string);

        _busy = true;
        try
        {
            ClearLog();

            // 1. 拉取版本 / 文件列表 → 弹出版本选择对话框（PCL 式）
            var rows = Platform == "curseforge"
                ? await BuildCfRowsAsync(hit)
                : await BuildModrinthRowsAsync(hit);
            if (rows.Count == 0)
            {
                Log("没有可下载的版本。");
                return;
            }
            var picked = ModVersionWindow.Show(Window.GetWindow(this), hit.Title, rows);
            if (picked == null) return;

            // 2. 下载所选文件
            var dir = TargetDir(type);
            Directory.CreateDirectory(dir);
            if (type == "datapack")
                Log(App.Current.TryFindResource("S.Mod.DataDirHint") as string
                    ?? "提示：数据包需放入具体存档的 datapacks 文件夹才会生效。");

            await DownloadOneAsync(hit.Title, dir, picked);

            // 3. 必需依赖自动补齐（PCL 式批量；Modrinth / CurseForge 均支持）
            if (type == "mod")
            {
                if (Platform == "modrinth" && picked.MrVersion != null)
                    await ResolveDependenciesAsync(picked.MrVersion, dir, gameVersion);
                else if (Platform == "curseforge" && picked.Cf != null)
                    await ResolveCfDependenciesAsync(picked.Cf, dir,
                        gameVersion, Loader);
            }

            RefreshLocalMods();
        }
        catch (Exception ex)
        {
            Log("下载失败: " + ex.Message);
        }
        finally
        {
            _busy = false;
            Dispatcher.Invoke(() => PbProgress.Value = 0);
        }
    }

    private async Task<List<ModVersionRow>> BuildModrinthRowsAsync(ModrinthService.SearchHit hit)
    {
        Log($"获取 {hit.Title} 的版本（Modrinth）...");
        var versions = await App.Modrinth.GetVersionsAsync(hit.Slug);
        return versions.Select(v =>
        {
            var file = v.Files.FirstOrDefault(f => f.Primary != null) ?? v.Files.FirstOrDefault();
            var badges = new List<string>();
            if (v.GameVersions.Length > 0)
                badges.Add(v.GameVersions[0] + (v.GameVersions.Length > 1 ? "+" : ""));
            if (v.Loaders.Length > 0) badges.Add(string.Join("/", v.Loaders));
            if (v.DatePublished != default)
                badges.Add(v.DatePublished.ToLocalTime().ToString("yyyy-MM-dd"));
            if (file != null && file.Size > 0) badges.Add(FormatSize(file.Size));
            if (file?.Primary != null) badges.Add("primary");
            return new ModVersionRow
            {
                Title = v.VersionNumber,
                ReleaseType = v.VersionType,
                Badges = badges,
                MrVersion = v,
                MrFile = file,
            };
        }).Where(r => r.MrFile != null).ToList();
    }

    private async Task<List<ModVersionRow>> BuildCfRowsAsync(ModrinthService.SearchHit hit)
    {
        Log($"获取 {hit.Title} 的文件（CurseForge）...");
        if (!int.TryParse(hit.ProjectId, out var modId))
            throw new Exception("项目 id 无效。");
        var files = await App.CurseForge.GetFilesAsync(modId);
        return files.Select(f =>
        {
            var badges = new List<string>();
            var gvs = f.GameVersions.Where(v => v.StartsWith("1.", StringComparison.Ordinal)).ToList();
            if (gvs.Count > 0)
            {
                badges.Add(gvs[0] + (gvs.Count > 1 ? "+" : ""));
                foreach (var l in f.GameVersions.Except(gvs).Take(1)) badges.Add(l);
            }
            if (f.DateModified != default)
                badges.Add(f.DateModified.ToLocalTime().ToString("yyyy-MM-dd"));
            if (f.Size > 0) badges.Add(FormatSize(f.Size));
            return new ModVersionRow
            {
                Title = f.DisplayName,
                ReleaseType = f.ReleaseType,
                Badges = badges,
                Cf = f,
            };
        }).ToList();
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1 << 20 => $"{bytes / 1048576.0:0.#} MB",
        >= 1 << 10 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes} B",
    };

    /// <summary>下载单个文件到目录（统一进度条 / 任务中心 / 日志）。</summary>
    private async Task DownloadOneAsync(string projectTitle, string dir, ModVersionRow row)
    {
        string url, filename;
        Func<string, string, IProgress<long>?, System.Threading.CancellationToken, Task> doDownload;
        if (row.Cf != null)
        {
            filename = row.Cf.FileName;
            doDownload = (_, dest, bytes, ct) => App.CurseForge.DownloadAsync(row.Cf, dest, bytes, ct);
        }
        else
        {
            var f = row.MrFile!;
            url = f.Url;
            filename = f.Filename;
            doDownload = (_, dest, bytes, ct) => App.Modrinth.DownloadAsync(url, dest, bytes, ct);
        }

        var dest = Path.Combine(dir, filename);
        if (File.Exists(dest))
        {
            Log("已存在，跳过下载: " + filename);
            return;
        }

        var task = DownloadTaskService.Instance.StartCancellable(
            string.Format(App.Current.TryFindResource("S.Mod.TaskTitle") as string ?? "Mod 下载：{0}", projectTitle),
            filename);
        var dlCt = DownloadTaskService.Instance.TokenFor(task);
        long total = 1, done = 0;
        if (row.Cf != null) total = Math.Max(1, row.Cf.Size);
        else if (row.MrFile != null) total = Math.Max(1, row.MrFile.Size);

        var progress = new Progress<long>(delta => Dispatcher.Invoke(() =>
        {
            done += delta;
            PbProgress.IsIndeterminate = false;
            PbProgress.Value = Math.Min(100, done * 100.0 / total);
            DownloadTaskService.Instance.Update(task, done * 100.0 / total);
        }));

        Log($"下载 {filename} → {dir}");
        try
        {
            await doDownload(projectTitle, dest, progress, dlCt);
            DownloadTaskService.Instance.Complete(task, FormatSize(total));
            Log("下载完成: " + dest);
        }
        catch (OperationCanceledException) when (dlCt.IsCancellationRequested)
        {
            // fix7：用户在任务中心取消下载 —— 清理半成品并标记「已取消」（PCL 同款）
            try { File.Delete(dest); } catch { }
            DownloadTaskService.Instance.Cancel(task, "已取消");
            Log("下载已取消: " + filename);
        }
        catch (Exception ex)
        {
            DownloadTaskService.Instance.Fail(task, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Modrinth 必需依赖自动批量下载（PCL 式）：广度优先解析 required 依赖，
    /// 每个依赖取与所选游戏版本 / 加载器最兼容的文件，已存在同名文件自动跳过。
    /// </summary>
    private async Task ResolveDependenciesAsync(
        ModrinthService.ProjectVersion root, string modsDir, string? gameVersion)
    {
        var required = root.Dependencies.Where(d => d.DepType == "required" && d.ProjectId.Length > 0)
            .Select(d => d.ProjectId).Distinct().ToList();
        if (required.Count == 0) return;

        var loader = root.Loaders.Length > 0 ? root.Loaders[0] : null;
        var visited = new HashSet<string>();
        var queue = new Queue<string>(required);
        var depCount = 0;

        Log($"正在解析必需依赖（{required.Count} 个）...");
        while (queue.Count > 0 && visited.Count < 15)
        {
            var projectId = queue.Dequeue();
            if (!visited.Add(projectId)) continue;
            var found = await App.Modrinth.GetLatestCompatibleFileAsync(projectId, gameVersion, loader);
            if (found == null)
            {
                Log($"  跳过依赖 {projectId}（没有与所选版本兼容的文件）");
                continue;
            }
            var (v, f) = found.Value;
            await DownloadOneAsync(v.Name, modsDir, new ModVersionRow
            {
                Title = v.VersionNumber,
                ReleaseType = v.VersionType,
                Badges = new List<string> { "依赖" },
                MrVersion = v,
                MrFile = f,
            });
            depCount++;
            foreach (var d in v.Dependencies.Where(d => d.DepType == "required"))
                queue.Enqueue(d.ProjectId);
        }
        Log(depCount > 0
            ? string.Format(App.Current.TryFindResource("S.Mod.DepsDone") as string ?? "已补齐 {0} 个必需依赖。", depCount)
            : "没有需要补齐的依赖。");
    }

    /// <summary>
    /// CurseForge 必需依赖自动批量下载（v1.10.0 起，与 Modrinth 同款 BFS）：
    /// 文件依赖 relationType==3（Required）→ GetLatestCompatibleFileAsync 挑最兼容文件
    /// → 依赖的依赖继续入队，上限 15 个，同名文件自动跳过。
    /// </summary>
    private async Task ResolveCfDependenciesAsync(
        CurseForgeService.CfFile root, string modsDir, string? gameVersion, string loader)
    {
        var required = root.Dependencies.Where(d => d.RelationType == 3)
            .Select(d => d.ModId).Distinct().ToList();
        if (required.Count == 0) return;

        var names = await App.CurseForge.GetModsAsync(required);
        var visited = new HashSet<int>();
        var queue = new Queue<int>(required);
        var depCount = 0;

        Log(string.Format(
            App.Current.TryFindResource("S.Mod.CfDepsStart") as string
                ?? "正在解析 CurseForge 必需依赖（{0} 个）...", required.Count));
        while (queue.Count > 0 && visited.Count < 15)
        {
            var modId = queue.Dequeue();
            if (!visited.Add(modId)) continue;
            var file = await App.CurseForge.GetLatestCompatibleFileAsync(modId, gameVersion, loader);
            var depName = names.TryGetValue(modId, out var n) && n.Name.Length > 0 ? n.Name : $"#{modId}";
            if (file == null)
            {
                Log(string.Format(
                    App.Current.TryFindResource("S.Mod.DepSkip") as string
                        ?? "  跳过依赖 {0}（没有与所选版本兼容的文件）", depName));
                continue;
            }
            await DownloadOneAsync(depName, modsDir, new ModVersionRow
            {
                Title = file.DisplayName,
                ReleaseType = file.ReleaseType,
                Badges = new List<string> { "依赖" },
                Cf = file,
            });
            depCount++;
            foreach (var d in file.Dependencies.Where(d => d.RelationType == 3))
                queue.Enqueue(d.ModId);
        }
        Log(depCount > 0
            ? string.Format(App.Current.TryFindResource("S.Mod.DepsDone") as string
                ?? "已补齐 {0} 个必需依赖。", depCount)
            : "没有需要补齐的依赖。");
    }

    // ---------- 批量安装（PCL 式：勾选 → 一键全部安装最新兼容版） ----------

    private IEnumerable<ModRowItem> Rows => LstResults.ItemsSource as IEnumerable<ModRowItem>
        ?? Enumerable.Empty<ModRowItem>();

    private int SelectedCount => Rows.Count(r => r.IsSelected);

    private void UpdateBatchUi()
    {
        var n = SelectedCount;
        BtnBatchDownload.IsEnabled = n > 0 && !_busy;
        BtnSavePreset.IsEnabled = n > 0 && !_busy; // v0.1.5：保存预设同样需要勾选
        var go = App.Current?.TryFindResource("S.Mod.BatchGo") as string ?? "下载已选（{0}）";
        BtnBatchDownload.Content = string.Format(go, n);
    }

    private void ChkSelect_Changed(object sender, RoutedEventArgs e)
    {
        // 勾选状态同步到 _selectedIds（分页加载更多后可恢复）
        if (sender is System.Windows.Controls.CheckBox cb && cb.DataContext is ModRowItem item)
        {
            if (item.IsSelected) _selectedIds.Add(item.Hit.ProjectId);
            else _selectedIds.Remove(item.Hit.ProjectId);
        }
        UpdateBatchUi();
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in Rows)
        {
            r.IsSelected = true;
            _selectedIds.Add(r.Hit.ProjectId);
        }
        UpdateBatchUi();
    }

    private void BtnClearSelect_Click(object sender, RoutedEventArgs e)
    {
        foreach (var r in Rows)
        {
            r.IsSelected = false;
            _selectedIds.Remove(r.Hit.ProjectId);
        }
        UpdateBatchUi();
    }

    private async void BtnBatchDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Hit).ToList();
        if (selected.Count == 0) return;

        var type = (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
        var gameVersion = ExtractGameVersion(CmbTargetVersion.SelectedItem as string);
        var dir = TargetDir(type);
        Directory.CreateDirectory(dir);

        _busy = true;
        BtnBatchDownload.IsEnabled = false;
        int okCount = 0, failCount = 0;
        try
        {
            ClearLog();
            Log(string.Format(
                App.Current.TryFindResource("S.Mod.BatchStart") as string
                    ?? "批量安装 {0} 个项目到 {1} ...", selected.Count, dir));
            foreach (var hit in selected)
            {
                try
                {
                    await BatchInstallOneAsync(hit, type, gameVersion, dir);
                    okCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Log($"  ✗ {hit.Title}: {ex.Message}");
                }
            }
            Log(string.Format(
                App.Current.TryFindResource("S.Mod.BatchDone") as string
                    ?? "批量安装完成：{0} 成功，{1} 失败。", okCount, failCount));
            RefreshLocalMods();
        }
        finally
        {
            _busy = false;
            UpdateBatchUi();
            Dispatcher.Invoke(() => PbProgress.Value = 0);
        }
    }

    /// <summary>批量安装单个项目：自动挑最新兼容文件（不弹版本对话框），mod 类型自动补依赖。</summary>
    private async Task BatchInstallOneAsync(
        ModrinthService.SearchHit hit, string type, string? gameVersion, string dir)
    {
        Log($"▶ {hit.Title}");
        if (Platform == "curseforge")
        {
            if (!int.TryParse(hit.ProjectId, out var modId))
                throw new Exception("项目 id 无效");
            var file = await App.CurseForge.GetLatestCompatibleFileAsync(modId, gameVersion, Loader)
                ?? throw new Exception(App.Current.TryFindResource("S.Mod.BatchNoCompat") as string
                    ?? "没有与所选版本兼容的文件");
            await DownloadOneAsync(hit.Title, dir, new ModVersionRow
            {
                Title = file.DisplayName,
                ReleaseType = file.ReleaseType,
                Badges = new List<string> { "最新兼容" },
                Cf = file,
            });
            if (type == "mod" && file.Dependencies.Count > 0)
                await ResolveCfDependenciesAsync(file, dir, gameVersion, Loader);
        }
        else
        {
            var found = await App.Modrinth.GetLatestCompatibleFileAsync(hit.Slug, gameVersion, Loader)
                ?? throw new Exception(App.Current.TryFindResource("S.Mod.BatchNoCompat") as string
                    ?? "没有与所选版本兼容的文件");
            var (v, f) = found;
            await DownloadOneAsync(hit.Title, dir, new ModVersionRow
            {
                Title = v.VersionNumber,
                ReleaseType = v.VersionType,
                Badges = new List<string> { "最新兼容" },
                MrVersion = v,
                MrFile = f,
            });
            if (type == "mod")
                await ResolveDependenciesAsync(v, dir, gameVersion);
        }
    }

    // ==================== v0.1.5：详情页（PCL 式单击进入） ====================

    private ModrinthService.SearchHit? _detailHit;
    private string _detailUrl = "";

    /// <summary>单击结果行 → 详情页（勾选框 / 按钮点击不触发）。</summary>
    private void LstResults_PreviewMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d &&
            (FindAncestor<System.Windows.Controls.Button>(d) != null ||
             FindAncestor<System.Windows.Controls.CheckBox>(d) != null)) return;
        if (LstResults.SelectedItem is ModRowItem item && !_busy)
            _ = OpenDetailAsync(item.Hit);
    }

    private static T? FindAncestor<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        return d as T;
    }

    private async Task OpenDetailAsync(ModrinthService.SearchHit hit)
    {
        _detailHit = hit;
        PnlEmpty.Visibility = Visibility.Collapsed;
        PnlDetail.Visibility = Visibility.Visible;
        ImgDetailIcon.Visibility = Visibility.Collapsed;
        IcoDetailFallback.Visibility = Visibility.Visible;
        TxtDetailTitle.Text = hit.Title;
        TxtDetailAuthor.Text = string.IsNullOrEmpty(hit.Author) ? "" : "@" + hit.Author;
        TxtDetailBadges.Text = string.Join("  ·  ", BuildBadges(hit));
        TxtDetailBody.Text = S("S.Mod.DetailLoading");
        BtnDetailDownload.IsEnabled = false;
        _detailUrl = hit.Source == "curseforge"
            ? $"https://www.curseforge.com/minecraft/search?search={Uri.EscapeDataString(hit.Slug)}"
            : $"https://modrinth.com/mod/{hit.Slug}";
        try
        {
            var detail = Platform == "curseforge" && int.TryParse(hit.ProjectId, out var mid)
                ? await App.CurseForge.GetModDetailAsync(mid)
                : await App.Modrinth.GetProjectAsync(string.IsNullOrEmpty(hit.Slug) ? hit.ProjectId : hit.Slug);
            if (!ReferenceEquals(_detailHit, hit)) return; // 用户已切换到别的详情
            TxtDetailTitle.Text = detail.Title;
            TxtDetailAuthor.Text = string.IsNullOrEmpty(detail.Author) ? "" : "@" + detail.Author;
            if (!string.IsNullOrWhiteSpace(detail.Body))
                TxtDetailBody.Text = detail.Body;
            else
                TxtDetailBody.Text = S("S.Mod.DetailNoDesc");
            if (!string.IsNullOrEmpty(detail.IconUrl))
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(detail.IconUrl);
                bmp.EndInit();
                bmp.Freeze();
                ImgDetailIcon.Source = bmp;
                ImgDetailIcon.Visibility = Visibility.Visible;
                IcoDetailFallback.Visibility = Visibility.Collapsed;
            }
            if (!string.IsNullOrEmpty(detail.WebUrl)) _detailUrl = detail.WebUrl;
            BtnDetailDownload.IsEnabled = true;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_detailHit, hit))
                TxtDetailBody.Text = S("S.Mod.DetailFail") + ex.Message;
        }
    }

    private void BtnDetailBack_Click(object sender, RoutedEventArgs e)
    {
        _detailHit = null;
        PnlDetail.Visibility = Visibility.Collapsed;
        if (_allHits.Count == 0) PnlEmpty.Visibility = Visibility.Visible;
    }

    private void BtnDetailOpenWeb_Click(object sender, RoutedEventArgs e)
    {
        if (_detailUrl.Length == 0) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_detailUrl) { UseShellExecute = true }); }
        catch (Exception ex) { Log("打开网页失败: " + ex.Message); }
    }

    private async void BtnDetailDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _detailHit == null) return;
        await DownloadWithPickerAsync(_detailHit);
    }

    // ==================== v0.1.5：模组预设（PCL 式一键装机单） ====================

    private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
    {
        var selected = Rows.Where(r => r.IsSelected).Select(r => r.Hit).ToList();
        if (selected.Count == 0) return;
        var type = (CmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
        var name = PromptWindow.Show(Window.GetWindow(this),
            S("S.Mod.PresetSave"), S("S.Mod.PresetNamePh"));
        if (string.IsNullOrWhiteSpace(name)) return;
        var items = selected.Select(h => new ModPresetItem
        {
            Platform = h.Source == "curseforge" ? "curseforge" : "modrinth",
            ProjectId = h.ProjectId,
            Slug = h.Slug,
            Title = h.Title,
            Type = type,
        }).ToList();
        try
        {
            var presets = ModPresetStore.Load(ModPresetStore.PresetsPath);
            ModPresetStore.Upsert(presets, name.Trim(), items);
            ModPresetStore.Save(ModPresetStore.PresetsPath, presets);
            Log(string.Format(S("S.Mod.PresetSaved"), items.Count, name.Trim()));
        }
        catch (Exception ex)
        {
            Log("预设保存失败: " + ex.Message);
        }
    }

    private async void BtnPresets_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        List<ModPreset> presets;
        ModPreset? picked;
        try { presets = ModPresetStore.Load(ModPresetStore.PresetsPath); }
        catch (Exception ex) { Log("预设读取失败: " + ex.Message); return; }
        picked = ModPresetWindow.Show(Window.GetWindow(this), presets);
        if (picked == null) return;
        await InstallPresetAsync(picked);
    }

    /// <summary>
    /// 预设一键匹配安装（v0.1.10 增强）：
    /// - 按条目类型分别安装到对应目录（mod / resourcepack / shader / datapack），
    ///   不再因当前筛选类型不同而整条跳过；
    /// - 每条按当前目标版本 / 加载器自动匹配最新兼容文件（不弹窗）；
    /// - 已存在同名文件自动跳过（一键补装只装缺失的）；
    /// - mod 类型自动补必需依赖；全程登记任务中心任务（带进度日志，可点开看详情）。
    /// </summary>
    private async Task InstallPresetAsync(ModPreset preset)
    {
        if (_busy) return;
        var gameVersion = ExtractGameVersion(CmbTargetVersion.SelectedItem as string);

        _busy = true;
        BtnBatchDownload.IsEnabled = false;
        BtnSavePreset.IsEnabled = false;
        int okCount = 0, failCount = 0, skipCount = 0;
        var presetTask = DownloadTaskService.Instance.StartKeyed(
            $"preset-install:{preset.Name}",
            string.Format(S("S.Mod.PresetTaskTitle"), preset.Name),
            S("S.Task.CheckingFiles"));
        try
        {
            ClearLog();
            Log(string.Format(S("S.Mod.PresetInstalling"), preset.Name, preset.Items.Count, "对应类型的目录"));
            DownloadTaskService.Instance.Update(presetTask, 0, S("S.Task.CheckingFiles"));
            var done = 0;
            // v0.1.10：按类型分组安装（每组安装到该类型的目录）
            foreach (var group in preset.Items.GroupBy(i => string.IsNullOrWhiteSpace(i.Type) ? "mod" : i.Type))
            {
                var dir = TargetDir(group.Key);
                Directory.CreateDirectory(dir);
                Log(string.Format(S("S.Mod.PresetGroup"), group.Key, group.Count(), dir));
                foreach (var it in group)
                {
                    try
                    {
                        var hit = new ModrinthService.SearchHit(
                            it.ProjectId, it.Slug, it.Title, "", "mod", 0, null,
                            Array.Empty<string>(), "", default, Array.Empty<string>(), 0,
                            it.Platform == "curseforge" ? "curseforge" : "modrinth");
                        // v0.1.10：已安装同名文件 → 跳过（一键补装只装缺失的）
                        var existing = await FindAlreadyInstalledFileAsync(hit, gameVersion, dir);
                        if (existing != null)
                        {
                            skipCount++;
                            done++;
                            Log($"↷ {it.Title} 已安装（{existing}），跳过");
                            DownloadTaskService.Instance.Update(presetTask,
                                done * 100.0 / preset.Items.Count, $"{done}/{preset.Items.Count} · {it.Title}（已安装）");
                            continue;
                        }
                        await BatchInstallOneAsync(hit, group.Key, gameVersion, dir);
                        okCount++;
                        done++;
                        DownloadTaskService.Instance.Update(presetTask,
                            done * 100.0 / preset.Items.Count, $"{done}/{preset.Items.Count} · {it.Title}");
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        done++;
                        Log($"  ✗ {it.Title}: {ex.Message}");
                        DownloadTaskService.Instance.Update(presetTask,
                            done * 100.0 / preset.Items.Count, $"{done}/{preset.Items.Count} · {it.Title}（失败）");
                    }
                }
            }
            var doneMsg = string.Format(S("S.Mod.BatchDone"), okCount, failCount);
            if (skipCount > 0) doneMsg += " " + string.Format(S("S.Mod.PresetSkipped"), skipCount);
            Log(doneMsg);
            if (failCount == 0)
                DownloadTaskService.Instance.Complete(presetTask, doneMsg);
            else
                DownloadTaskService.Instance.Fail(presetTask, doneMsg);
            RefreshLocalMods();
        }
        finally
        {
            _busy = false;
            UpdateBatchUi();
            Dispatcher.Invoke(() => PbProgress.Value = 0);
        }
    }

    /// <summary>
    /// v0.1.10：解析项目当前最新兼容文件，若同名文件已存在于目标目录则返回文件名（=已安装），
    /// 否则返回 null（需要下载）。匹配失败按未安装处理（照常下载）。
    /// </summary>
    private async Task<string?> FindAlreadyInstalledFileAsync(
        ModrinthService.SearchHit hit, string? gameVersion, string dir)
    {
        try
        {
            if (hit.Source == "curseforge" && int.TryParse(hit.ProjectId, out var modId))
            {
                var file = await App.CurseForge.GetLatestCompatibleFileAsync(modId, gameVersion, Loader);
                if (file == null) return null;
                return File.Exists(System.IO.Path.Combine(dir, file.FileName)) ? file.FileName : null;
            }
            var found = await App.Modrinth.GetLatestCompatibleFileAsync(
                string.IsNullOrEmpty(hit.Slug) ? hit.ProjectId : hit.Slug, gameVersion, Loader);
            if (found == null) return null;
            return File.Exists(System.IO.Path.Combine(dir, found.Value.File.Filename))
                ? found.Value.File.Filename : null;
        }
        catch { return null; }
    }
}
