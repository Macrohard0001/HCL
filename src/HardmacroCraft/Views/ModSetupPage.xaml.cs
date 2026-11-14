using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.1.12 一键配置 Mods 页（对齐 PCL 的"一键配置/自动安装"体验）：
/// - 预设 = 一组云端 Mod 清单（Modrinth / CurseForge 项目），安装时按所选版本自动匹配最新兼容文件；
/// - 预设来源：Mod 页勾选保存 / 本页「从已装版本导入」（逐 jar 算 SHA1 反查 Modrinth）；
/// - 「开始配置」：预设全部条目安装到所选版本（同名已装自动跳过，可取消，登记任务中心）；
/// - 「开始移除」：把该预设安装到所选版本的文件移除（先备份到 config/mod-backup/ 再删）；
/// - 预设管理：重命名 / 删除 / 新建导入，持久化在便携 config/mod-presets.json。
/// </summary>
public partial class ModSetupPage : Page
{
    private List<ModPreset> _presets = new();
    private ModPreset? _selected;
    private bool _busy;

    public ModSetupPage()
    {
        InitializeComponent();
        Loaded += (_, _) => { Reload(); };
    }

    // ---------- 基础 ----------

    private string? Version => CmbVersion.SelectedItem as string;

    private string? GameVersion => ExtractGameVersion(Version);

    private string TargetDir(string type)
    {
        var isolation = VersionSettingsStore.EffectiveIsolation(
            Version, AppSettings.Instance.VersionIsolation);
        return ModTargetDir.Resolve(AppSettings.Instance.GameRoot, Version, type, isolation);
    }

    private void Log(string s) => TxtLog.Text = s;

    private static string? ExtractGameVersion(string? versionName)
    {
        if (string.IsNullOrWhiteSpace(versionName)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(versionName, @"^\d+\.\d+(\.\d+)?");
        return m.Success ? m.Value : null;
    }

    private void Reload()
    {
        try { _presets = ModPresetStore.Load(ModPresetStore.PresetsPath); }
        catch (Exception ex) { _presets = new List<ModPreset>(); Log("预设读取失败: " + ex.Message); }

        var versions = App.LauncherService.GetInstalledVersions();
        CmbVersion.ItemsSource = versions;
        if (versions.Count > 0)
            CmbVersion.SelectedItem = versions.Contains(AppSettings.Instance.LastVersion)
                ? AppSettings.Instance.LastVersion : versions[^1];

        RebuildPresetList();
    }

    // ---------- 左侧预设列表 ----------

    private void RebuildPresetList()
    {
        PnlPresets.Children.Clear();
        if (_presets.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = App.Current.TryFindResource("S.Msu.Empty") as string ?? "还没有预设。",
                FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0),
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            PnlPresets.Children.Add(empty);
        }
        foreach (var p in _presets) PnlPresets.Children.Add(BuildPresetRow(p));
        UpdateDetail();
    }

    private UIElement BuildPresetRow(ModPreset preset)
    {
        var isSel = ReferenceEquals(preset, _selected);
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Background = new SolidColorBrush(isSel
                ? Color.FromArgb(0x22, 0x00, 0x77, 0xCC)
                : Color.FromArgb(0x0D, 0x88, 0x88, 0x88)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = preset,
        };
        var stack = new StackPanel();
        var name = new TextBlock
        {
            Text = preset.Name, FontSize = 14, FontWeight = FontWeights.SemiBold,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var meta = new TextBlock
        {
            Text = string.Format(
                App.Current.TryFindResource("S.Mod.PresetMeta") as string
                    ?? "{0} 个条目 · 保存于 {1:yyyy-MM-dd}",
                preset.Items.Count, preset.CreatedAt.ToLocalTime()),
            FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0),
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        stack.Children.Add(name);
        stack.Children.Add(meta);
        card.Child = stack;
        card.MouseLeftButtonUp += (_, _) => { _selected = preset; RebuildPresetList(); };
        return card;
    }

    // ---------- 右侧详情 ----------

    private void UpdateDetail()
    {
        PnlItems.Children.Clear();
        if (_selected == null)
        {
            TxtPresetName.Text = App.Current.TryFindResource("S.Msu.NoPreset") as string ?? "未选择预设";
            TxtPresetMeta.Text = App.Current.TryFindResource("S.Msu.NoPresetHint") as string
                ?? "在左侧选择一个预设，或从已装版本导入新预设。";
            SetButtonsEnabled(false);
            return;
        }
        SetButtonsEnabled(true);
        TxtPresetName.Text = _selected.Name;
        TxtPresetMeta.Text = string.Format(
            App.Current.TryFindResource("S.Mod.PresetMeta") as string
                ?? "{0} 个条目 · 保存于 {1:yyyy-MM-dd}",
            _selected.Items.Count, _selected.CreatedAt.ToLocalTime());

        foreach (var group in _selected.Items.GroupBy(i => string.IsNullOrWhiteSpace(i.Type) ? "mod" : i.Type))
        {
            var header = new TextBlock
            {
                Text = $"{TypeName(group.Key)} · {group.Count()}",
                FontSize = 12.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4),
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            PnlItems.Children.Add(header);
            foreach (var it in group)
            {
                var files = it.InstalledFiles.Count > 0
                    ? string.Join(", ", it.InstalledFiles.Take(2)) + (it.InstalledFiles.Count > 2 ? " …" : "")
                    : "";
                var row = new TextBlock
                {
                    Text = "· " + it.Title + (files.Length > 0 ? $"（{files}）" : ""),
                    FontSize = 12.5, Margin = new Thickness(10, 1, 0, 1),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                row.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                row.ToolTip = files;
                PnlItems.Children.Add(row);
            }
        }
    }

    private static string TypeName(string t) => t switch
    {
        "resourcepack" => App.Current.TryFindResource("S.Msu.TypeResourcepack") as string ?? "资源包",
        "shader" => App.Current.TryFindResource("S.Msu.TypeShader") as string ?? "光影",
        "datapack" => App.Current.TryFindResource("S.Msu.TypeDatapack") as string ?? "数据包",
        _ => App.Current.TryFindResource("S.Msu.TypeMod") as string ?? "Mod",
    };

    private void SetButtonsEnabled(bool on)
    {
        var enabled = on && !_busy;
        BtnApply.IsEnabled = enabled;
        BtnRemove.IsEnabled = enabled;
        BtnRename.IsEnabled = enabled && _selected != null;
        BtnDelete.IsEnabled = enabled && _selected != null;
        BtnImport.IsEnabled = !_busy;
        BtnAddItems.IsEnabled = !_busy;
        BtnNewPreset.IsEnabled = !_busy;
        CmbVersion.IsEnabled = !_busy;
    }

    // ---------- 新建预设（从版本导入） ----------

    private void CmbVersion_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    /// <summary>
    /// 「从已装版本导入」：直接把所选版本 mods 目录反查生成预设（自动命名「{版本} 导入」），
    /// 与「新建预设」的区别是无需手输名字，一键完成。
    /// </summary>
    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (Version == null)
        {
            Log(App.Current.TryFindResource("S.Msu.NeedVersion") as string ?? "请先在上方选择一个已安装的游戏版本。");
            return;
        }
        _ = ImportFromVersionAsync($"{Version} 导入");
    }

    private void BtnNewPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var name = PromptWindow.Show(Window.GetWindow(this),
            App.Current.TryFindResource("S.Msu.PresetNameTitle") as string ?? "新建预设",
            App.Current.TryFindResource("S.Mod.PresetNamePh") as string ?? "给这组预设起个名字");
        if (string.IsNullOrWhiteSpace(name)) return;
        // v0.15.0：新建预设 = 命名后直接在启动器内搜索挑选（不再强制从游戏导入）
        var items = PickItems();
        _presets = ModPresetStore.Upsert(_presets, name.Trim(), items);
        ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
        _selected = _presets.FirstOrDefault(p => p.Name == name.Trim());
        RebuildPresetList();
        Log(items.Count > 0
            ? string.Format(App.Current.TryFindResource("S.Msu.PresetCreated") as string
                ?? "已创建预设「{0}」：{1} 个条目。", name.Trim(), items.Count)
            : App.Current.TryFindResource("S.Msu.PresetEmptyHint") as string
                ?? "预设已创建（空清单）。点「添加条目」搜索并挑选要安装的内容。");
    }

    /// <summary>
    /// v0.15.0：「添加条目」—— 不从游戏导入，直接在启动器里搜索 Modrinth / CurseForge
    /// 并挑选（Mod / 光影 / 资源包 / 数据包），勾选跨页保持，一键并入所选预设。
    /// </summary>
    private void BtnAddItems_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (_selected == null)
        {
            Log(App.Current.TryFindResource("S.Msu.NeedPreset") as string
                ?? "请先在左侧选择一个预设，或新建一个预设。");
            return;
        }
        var items = PickItems();
        if (items.Count == 0) return;
        ModPresetStore.Upsert(_presets, _selected.Name, _selected.Items.Concat(items).ToList());
        ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
        _selected = _presets.FirstOrDefault(p => p.Name == _selected.Name);
        RebuildPresetList();
        Log(string.Format(App.Current.TryFindResource("S.Msu.ItemsAdded") as string
            ?? "已向预设「{0}」添加 {1} 个条目。", _selected.Name, items.Count));
    }

    /// <summary>弹出搜索选择窗（PCL 式），返回挑选的条目清单（类型由窗内下拉决定）。</summary>
    private static List<ModPresetItem> PickItems()
    {
        var picked = ModSearchPickWindow.Show(App.Current.Windows.OfType<Window>().LastOrDefault(), "mod");
        return picked.Select(p => new ModPresetItem
        {
            Platform = p.Hit.Source == "curseforge" ? "curseforge" : "modrinth",
            ProjectId = p.Hit.ProjectId,
            Slug = p.Hit.Slug,
            Title = p.Hit.Title,
            Type = p.Type,
        }).ToList();
    }

    /// <summary>从所选已装版本导入预设：mods 目录逐 jar 算 SHA1 反查 Modrinth 项目。</summary>
    private async Task ImportFromVersionAsync(string presetName)
    {
        if (Version == null)
        {
            Log(App.Current.TryFindResource("S.Msu.NeedVersion") as string ?? "请先在上方选择一个已安装的游戏版本。");
            return;
        }
        _busy = true;
        SetButtonsEnabled(false);
        Log(App.Current.TryFindResource("S.Msu.Importing") as string ?? "正在扫描版本目录并反查 Modrinth ...");
        try
        {
            var modsDir = TargetDir("mod");
            var jars = ModPresetImport.ListModJars(modsDir);
            if (jars.Count == 0)
            {
                Log(App.Current.TryFindResource("S.Msu.ImportNoMods") as string
                    ?? $"该版本的 mods 目录没有可用 jar（{modsDir}）。");
                return;
            }
            var items = new List<ModPresetItem>();
            var skipped = new List<string>();
            for (var i = 0; i < jars.Count; i++)
            {
                Log($"[{i + 1}/{jars.Count}] {Path.GetFileName(jars[i])}");
                var item = await ModPresetImport.ImportOneAsync(jars[i], async (sha1, ct) =>
                {
                    var m = await App.Modrinth.GetProjectBySha1Async(sha1, ct);
                    return m == null ? null : (m.ProjectId, m.Slug, m.Title, m.ProjectType);
                });
                if (item == null) skipped.Add(Path.GetFileName(jars[i]));
                else items.Add(item);
            }
            if (items.Count == 0)
            {
                Log(App.Current.TryFindResource("S.Msu.ImportNoMatch") as string
                    ?? "没有任何文件能反查到 Modrinth 项目（CurseForge 专属 / 本地 mod 无法导入预设）。");
                return;
            }
            _presets = ModPresetStore.Upsert(_presets, presetName, items);
            ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
            _selected = _presets.FirstOrDefault(p => p.Name == presetName);
            RebuildPresetList();
            Log(string.Format(App.Current.TryFindResource("S.Msu.ImportDone") as string
                ?? "已生成预设「{0}」：{1} 个条目；另有 {2} 个文件未被 Modrinth 收录，已跳过。",
                presetName, items.Count, skipped.Count));
        }
        catch (Exception ex)
        {
            Log(App.Current.TryFindResource("S.Msu.ImportFailed") as string ?? "导入失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    // ---------- 开始配置（安装） ----------

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selected == null) return;
        if (Version == null)
        {
            Log(App.Current.TryFindResource("S.Msu.NeedVersion") as string ?? "请先在上方选择一个已安装的游戏版本。");
            return;
        }
        var preset = _selected;
        var version = Version!;
        _busy = true;
        SetButtonsEnabled(false);

        var task = DownloadTaskService.Instance.StartCancellableKeyed(
            $"preset-setup:{preset.Name}:{version}",
            string.Format(App.Current.TryFindResource("S.Msu.TaskTitle") as string
                ?? "一键配置 {0} → {1}", preset.Name, version),
            App.Current.TryFindResource("S.Task.CheckingFiles") as string ?? "检查文件");
        var ct = DownloadTaskService.Instance.TokenFor(task);
        int ok = 0, fail = 0, skip = 0;
        var total = preset.Items.Count;
        var done = 0;
        try
        {
            foreach (var it in preset.Items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var dir = TargetDir(string.IsNullOrWhiteSpace(it.Type) ? "mod" : it.Type);
                    Directory.CreateDirectory(dir);
                    var hit = await ResolveOneAsync(it, ct);
                    if (hit?.File == null)
                    {
                        fail++; done++;
                        Log($"✗ {it.Title}: " + (App.Current.TryFindResource("S.Mod.BatchNoCompat") as string
                            ?? "没有与所选版本兼容的文件"));
                        DownloadTaskService.Instance.Update(task, done * 100.0 / total, $"{done}/{total} · {it.Title}（失败）");
                        continue;
                    }
                    var dest = Path.Combine(dir, hit.Value.File.Filename);
                    if (File.Exists(dest))
                    {
                        ModPresetStore.RecordInstalled(it, hit.Value.File.Filename);
                        skip++; done++;
                        Log($"↷ {it.Title} 已安装（{hit.Value.File.Filename}），跳过");
                        DownloadTaskService.Instance.Update(task, done * 100.0 / total, $"{done}/{total} · {it.Title}（已安装）");
                        continue;
                    }
                    Log($"▶ {it.Title} → {hit.Value.File.Filename}");
                    await App.Modrinth.DownloadAsync(hit.Value.File.Url, dest, null, ct);
                    ModPresetStore.RecordInstalled(it, hit.Value.File.Filename);
                    ok++; done++;
                    DownloadTaskService.Instance.Update(task, done * 100.0 / total, $"{done}/{total} · {it.Title}");
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    fail++; done++;
                    Log($"✗ {it.Title}: {ex.Message}");
                    DownloadTaskService.Instance.Update(task, done * 100.0 / total, $"{done}/{total} · {it.Title}（失败）");
                }
            }
            ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
            RebuildPresetList();
            var msg = string.Format(App.Current.TryFindResource("S.Mod.BatchDone") as string
                ?? "完成：成功 {0} 个，失败 {1} 个", ok, fail);
            if (skip > 0) msg += " " + string.Format(App.Current.TryFindResource("S.Mod.PresetSkipped") as string
                ?? "（另有 {0} 个条目已安装过，自动跳过）", skip);
            Log(msg);
            if (fail == 0) DownloadTaskService.Instance.Complete(task, msg);
            else DownloadTaskService.Instance.Fail(task, msg);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
            DownloadTaskService.Instance.Cancel(task, App.Current.TryFindResource("S.Common.Canceled") as string ?? "已取消");
            Log(App.Current.TryFindResource("S.Msu.ApplyCanceled") as string ?? "一键配置已取消（已下载的文件保留）。");
        }
        catch (Exception ex)
        {
            DownloadTaskService.Instance.Fail(task, ex.Message);
            Log(App.Current.TryFindResource("S.Msu.ApplyFailed") as string ?? "一键配置失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    /// <summary>解析条目当前最新兼容文件（Modrinth 直接查；CurseForge 条目按标题在 Modrinth
    /// 反查不到时明确报错 —— CurseForge 文件清单需要 API Key，预设安装走 Modrinth 优先）。</summary>
    private async Task<(ModrinthService.VersionFile File, string Slug)?> ResolveOneAsync(
        ModPresetItem it, CancellationToken ct)
    {
        if (it.Platform == "curseforge")
        {
            // CurseForge 条目：按 slug/title 在 Modrinth 兜底反查；查不到再走 CF API（需 Key）
            var mr = await App.Modrinth.GetLatestCompatibleFileAsync(
                string.IsNullOrWhiteSpace(it.Slug) ? it.Title : it.Slug, GameVersion, null, ct);
            if (mr != null) return (mr.Value.File, it.Slug);
            if (int.TryParse(it.ProjectId, out var modId) && !string.IsNullOrWhiteSpace(AppSettings.Instance.CurseForgeApiKey))
            {
                var f = await App.CurseForge.GetLatestCompatibleFileAsync(modId, GameVersion, null);
                if (f != null) return (new ModrinthService.VersionFile(f.DownloadUrl ?? "", f.FileName, f.Size, "1"), it.Slug);
            }
            return null;
        }
        var found = await App.Modrinth.GetLatestCompatibleFileAsync(
            string.IsNullOrWhiteSpace(it.Slug) ? it.ProjectId : it.Slug, GameVersion, null, ct);
        return found == null ? null : (found.Value.File, it.Slug);
    }

    // ---------- 开始移除 ----------

    private async void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selected == null) return;
        if (Version == null)
        {
            Log(App.Current.TryFindResource("S.Msu.NeedVersion") as string ?? "请先在上方选择一个已安装的游戏版本。");
            return;
        }
        var preset = _selected;
        var version = Version!;
        var confirm = MessageBox.Show(Window.GetWindow(this),
            string.Format(App.Current.TryFindResource("S.Msu.RemoveConfirm") as string
                ?? "将从 {0} 移除预设「{1}」安装的文件（先备份再删除）。继续吗？", version, preset.Name),
            "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _busy = true;
        SetButtonsEnabled(false);
        var task = DownloadTaskService.Instance.StartKeyed(
            $"preset-remove:{preset.Name}:{version}",
            string.Format(App.Current.TryFindResource("S.Msu.RemoveTaskTitle") as string
                ?? "移除预设 {0}（{1}）", preset.Name, version),
            App.Current.TryFindResource("S.Task.CheckingFiles") as string ?? "检查文件");
        int removed = 0;
        try
        {
            var backupRoot = Path.Combine(AppSettings.ConfigDir, "mod-backup",
                string.Concat(version.Where(c => !Path.GetInvalidFileNameChars().Contains(c))),
                DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            foreach (var group in preset.Items.GroupBy(i => string.IsNullOrWhiteSpace(i.Type) ? "mod" : i.Type))
            {
                var dir = TargetDir(group.Key);
                foreach (var it in group)
                {
                    // 移除依据：安装时记录的文件名优先；无记录时回退"当前最新兼容文件名"
                    var names = new List<string>(it.InstalledFiles);
                    try
                    {
                        var hit = await ResolveOneAsync(it, CancellationToken.None);
                        if (hit?.File?.Filename != null && !names.Contains(hit.Value.File.Filename))
                            names.Add(hit.Value.File.Filename);
                    }
                    catch { /* 反查失败只用已记录文件名 */ }

                    foreach (var n in names)
                    {
                        var path = Path.Combine(dir, n);
                        if (!File.Exists(path)) continue;
                        // 设置 → 一键配置：移除前备份（默认开）；备份失败不中断删除
                        if (AppSettings.Instance.ModSetupBackup)
                        {
                            try
                            {
                                Directory.CreateDirectory(backupRoot);
                                File.Copy(path, Path.Combine(backupRoot, n), overwrite: true);
                            }
                            catch { /* 备份失败（磁盘满等）仍继续删除；PCL 同款不中断 */ }
                        }
                        try
                        {
                            File.Delete(path);
                            removed++;
                            Log($"− {n}");
                        }
                        catch (Exception dex)
                        {
                            Log($"✗ {n}: {dex.Message}");
                        }
                    }
                }
            }
            ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
            var msg = string.Format(App.Current.TryFindResource("S.Msu.RemoveDone") as string
                ?? "移除完成：删除 {0} 个文件（备份在 {1}）", removed, backupRoot);
            Log(msg);
            DownloadTaskService.Instance.Complete(task, msg);
        }
        catch (Exception ex)
        {
            DownloadTaskService.Instance.Fail(task, ex.Message);
            Log(App.Current.TryFindResource("S.Msu.RemoveFailed") as string ?? "移除失败：" + ex.Message);
        }
        finally
        {
            _busy = false;
            SetButtonsEnabled(true);
        }
    }

    // ---------- 重命名 / 删除预设 ----------

    private void BtnRename_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selected == null) return;
        var newName = PromptWindow.Show(Window.GetWindow(this),
            App.Current.TryFindResource("S.Msu.RenameTitle") as string ?? "重命名预设",
            App.Current.TryFindResource("S.Mod.PresetNamePh") as string ?? "预设名称",
            _selected.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Trim() == _selected.Name) return;
        var oldName = _selected.Name;
        _selected.Name = newName.Trim();
        ModPresetStore.Upsert(_presets, newName.Trim(), _selected.Items);
        _presets.RemoveAll(p => p.Name == oldName && !ReferenceEquals(p, _selected));
        // Upsert 已插入新对象；保持引用一致：直接以名字定位
        _selected = _presets.FirstOrDefault(p => p.Name == newName.Trim());
        ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
        RebuildPresetList();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _selected == null) return;
        var preset = _selected;
        if (MessageBox.Show(Window.GetWindow(this),
                string.Format(App.Current.TryFindResource("S.Mod.PresetDeleteConfirm") as string
                    ?? "确定删除预设「{0}」吗？", preset.Name),
                "Hardmacro Craft Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        _presets.Remove(preset);
        _selected = null;
        ModPresetStore.Save(ModPresetStore.PresetsPath, _presets);
        RebuildPresetList();
    }
}
