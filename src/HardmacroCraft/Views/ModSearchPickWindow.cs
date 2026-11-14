using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.0 一键配置「搜索选择」弹窗（PCL 式：不从游戏导入，直接在启动器里搜索并挑选）：
/// - 平台（Modrinth / CurseForge）+ 类型（Mod / 光影 / 资源包 / 数据包）+ 关键词搜索；
/// - 勾选跨页保持（PCL 式翻页条，复用 PclPager / PagerBar）；
/// - 「添加所选」返回勾选项目清单，由 ModSetupPage 并入当前预设。
/// 代码构建设计：与 ModPresetWindow / AccountManager 一致，随 Fluent 主题深浅色。
/// </summary>
public sealed class ModSearchPickWindow
{
    public sealed record PickedItem(ModrinthService.SearchHit Hit, string Type);

    private readonly HashSet<string> _pickedIds = new();
    private readonly Dictionary<string, ModrinthService.SearchHit> _pickedHits = new();
    private List<ModrinthService.SearchHit> _pageHits = new();
    private int _page = 1;
    private int _totalHits;
    private bool _busy;
    private string _resultType;

    private Window _win = null!;
    private TextBox _txtQuery = null!;
    private ComboBox _cmbPlatform = null!;
    private ComboBox _cmbType = null!;
    private StackPanel _pnlRows = null!;
    private StackPanel _pnlPager = null!;
    private TextBlock _txtTotal = null!;
    private TextBlock _txtStatus = null!;
    private Border _pnlSearching = null!;
    private Wpf.Ui.Controls.Button _btnConfirm = null!;
    private TextBlock _txtPicked = null!;

    private List<PickedItem> _result = new();

    private ModSearchPickWindow(string defaultType) => _resultType = defaultType;

    public static List<PickedItem> Show(Window? owner, string defaultType)
    {
        var w = new ModSearchPickWindow(defaultType);
        w.Build(owner);
        w._win.ShowDialog();
        return w._result;
    }

    private static string S(string key) => App.Current?.TryFindResource(key) as string ?? "";

    private void Build(Window? owner)
    {
        _win = new Wpf.Ui.Controls.FluentWindow
        {
            Title = S("S.Msu.PickTitle"),
            Width = 760,
            Height = 580,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            MinWidth = 620,
            MinHeight = 460,
            WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica,
            ExtendsContentIntoTitleBar = true,
        };
        owner ??= App.Current.Windows.OfType<Window>().LastOrDefault();
        if (owner != null && !ReferenceEquals(owner, _win)) _win.Owner = owner;
        _win.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) _win.Close(); };

        var root = new Grid { Margin = new Thickness(24, 16, 24, 16) };
        for (var i = 0; i < 5; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        // 标题
        var title = new TextBlock
        {
            Text = S("S.Msu.PickTitle"),
            FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2),
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var subtitle = new TextBlock
        {
            Text = S("S.Msu.PickSubtitle"),
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 12),
        };
        subtitle.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Grid.SetRow(title, 0);
        Grid.SetRow(subtitle, 0);
        var head = new StackPanel();
        head.Children.Add(title);
        head.Children.Add(subtitle);
        root.Children.Add(head);

        // 工具条：关键词 + 平台 + 类型 + 搜索
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        for (var i = 0; i < 4; i++)
            toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _txtQuery = new Wpf.Ui.Controls.TextBox { MinHeight = 32, Margin = new Thickness(0, 0, 8, 0), PlaceholderText = S("S.Msu.PickSearchHint") };
        _txtQuery.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) _ = SearchAsync(reset: true); };
        Grid.SetColumn(_txtQuery, 0);
        toolbar.Children.Add(_txtQuery);

        _cmbPlatform = new ComboBox { MinHeight = 32, MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
        _cmbPlatform.Items.Add(new ComboBoxItem { Content = "Modrinth", Tag = "modrinth", IsSelected = true });
        _cmbPlatform.Items.Add(new ComboBoxItem { Content = "CurseForge", Tag = "curseforge" });
        Grid.SetColumn(_cmbPlatform, 1);
        toolbar.Children.Add(_cmbPlatform);

        _cmbType = new ComboBox { MinHeight = 32, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (tag, key) in new[] { ("mod", "S.Msu.TypeMod"), ("shader", "S.Msu.TypeShader"), ("resourcepack", "S.Msu.TypeResourcepack"), ("datapack", "S.Msu.TypeDatapack") })
        {
            var it = new ComboBoxItem { Content = App.Current.TryFindResource(key) as string ?? tag, Tag = tag };
            if (tag == _resultType) it.IsSelected = true;
            _cmbType.Items.Add(it);
        }
        Grid.SetColumn(_cmbType, 2);
        toolbar.Children.Add(_cmbType);

        var btnSearch = new Wpf.Ui.Controls.Button
        {
            Content = S("S.Mod.Search"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Search24 },
            MinHeight = 32, MinWidth = 88,
        };
        btnSearch.Click += (_, _) => _ = SearchAsync(reset: true);
        Grid.SetColumn(btnSearch, 3);
        toolbar.Children.Add(btnSearch);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        // 结果列表（滚动）
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        _pnlRows = new StackPanel();
        scroll.Content = _pnlRows;
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // 居中加载过渡层（覆盖列表区）
        _pnlSearching = new Border
        {
            Visibility = Visibility.Collapsed,
            Opacity = 0.82,
            CornerRadius = new CornerRadius(8),
        };
        _pnlSearching.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
        var searching = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        searching.Children.Add(new Wpf.Ui.Controls.ProgressRing { Width = 42, Height = 42, IsIndeterminate = true });
        _pnlSearching.Child = searching;
        Grid.SetRow(_pnlSearching, 2);
        root.Children.Add(_pnlSearching);

        // 翻页条 + 状态
        var pagerRow = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var pagerLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        _pnlPager = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _txtTotal = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        _txtTotal.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        pagerLine.Children.Add(_pnlPager);
        pagerLine.Children.Add(_txtTotal);
        _txtStatus = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 50,
            Visibility = Visibility.Collapsed,
            FontSize = 12,
        };
        _txtStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        pagerRow.Children.Add(pagerLine);
        pagerRow.Children.Add(_txtStatus);
        Grid.SetRow(pagerRow, 3);
        root.Children.Add(pagerRow);

        // 底部：已选计数 + 全选/清空 + 添加所选
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        _txtPicked = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            FontSize = 13, FontWeight = FontWeights.SemiBold,
        };
        _txtPicked.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        bottom.Children.Add(_txtPicked);

        var btnAll = new Wpf.Ui.Controls.Button { Content = S("S.Mod.BatchAll"), MinHeight = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
        btnAll.Click += (_, _) => { foreach (var h in _pageHits) MarkPicked(h, true); RebuildRows(); UpdatePickedUi(); };
        bottom.Children.Add(btnAll);
        var btnNone = new Wpf.Ui.Controls.Button { Content = S("S.Mod.BatchNone"), MinHeight = 30, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 8, 0) };
        btnNone.Click += (_, _) => { _pickedIds.Clear(); _pickedHits.Clear(); RebuildRows(); UpdatePickedUi(); };
        bottom.Children.Add(btnNone);

        _btnConfirm = new Wpf.Ui.Controls.Button
        {
            Content = S("S.Msu.PickConfirm"),
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 },
            MinHeight = 30, MinWidth = 130, IsEnabled = false,
        };
        _btnConfirm.Click += (_, _) => Confirm();
        bottom.Children.Add(_btnConfirm);
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);

        UpdatePickedUi();
        Services.FluentDialog.SetContent(_win, root);
        _ = SearchAsync(reset: true); // 打开即展示推荐内容（下载量排序，PCL 式）
    }

    private void Confirm()
    {
        _result = _pickedIds
            .Where(_pickedHits.ContainsKey)
            .Select(id => new PickedItem(_pickedHits[id], _resultType))
            .ToList();
        _win.Close();
    }

    private void MarkPicked(ModrinthService.SearchHit hit, bool picked)
    {
        if (picked)
        {
            if (_pickedIds.Add(hit.ProjectId)) _pickedHits[hit.ProjectId] = hit;
        }
        else
        {
            _pickedIds.Remove(hit.ProjectId);
            _pickedHits.Remove(hit.ProjectId);
        }
    }

    private void UpdatePickedUi()
    {
        _txtPicked.Text = string.Format(
            App.Current.TryFindResource("S.Msu.PickCount") as string ?? "已选 {0} 项",
            _pickedIds.Count);
        _btnConfirm.IsEnabled = _pickedIds.Count > 0;
    }

    private void Log(string s)
    {
        _txtStatus.Text = s;
        _txtStatus.Visibility = string.IsNullOrEmpty(s) ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task SearchAsync(bool reset, int? gotoPage = null, bool recommend = false)
    {
        if (_busy) return;
        try
        {
            _busy = true;
            _pnlSearching.Visibility = Visibility.Visible;
            _resultType = (_cmbType.SelectedItem as ComboBoxItem)?.Tag as string ?? "mod";
            var platform = (_cmbPlatform.SelectedItem as ComboBoxItem)?.Tag as string ?? "modrinth";
            var page = Math.Max(1, gotoPage ?? 1);
            Log(recommend ? S("S.Msu.PickLoading") : S("S.Mod.Searching"));
            var offset = PclPager.OffsetOf(page, PclPager.SearchPageSize);
            var (hits, total) = platform == "curseforge"
                ? await App.CurseForge.SearchAsync(_txtQuery.Text ?? "", _resultType, "relevance", offset)
                : await App.Modrinth.SearchAsync(_txtQuery.Text ?? "", _resultType, "relevance", offset);
            _page = page;
            _totalHits = total;
            _pageHits = hits.ToList();

            // CurseForge 未配置 API Key 时直接给引导
            if (platform == "curseforge" && !App.CurseForge.HasKey)
                Log(S("S.Mod.NeedKey"));

            RebuildRows();
            UpdatePager();
            UpdatePickedUi();
            Log("");
        }
        catch (Exception ex)
        {
            Log(string.Format(S("S.Sh.SearchFail"), ex.Message));
        }
        finally
        {
            _busy = false;
            _pnlSearching.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdatePager()
    {
        var totalPages = PclPager.TotalPages(_totalHits, PclPager.SearchPageSize);
        if (totalPages <= 1)
        {
            _pnlPager.Visibility = Visibility.Collapsed;
            _txtTotal.Text = _totalHits > 0 ? string.Format(S("S.Pg.Total"), _totalHits) : "";
            return;
        }
        _pnlPager.Visibility = Visibility.Visible;
        PagerBar.Build(_pnlPager, PclPager.BuildButtons(_page, totalPages),
            p => _ = SearchAsync(reset: false, gotoPage: p));
        _txtTotal.Text = string.Format(S("S.Pg.PageOf"), _page, totalPages) + " · " + string.Format(S("S.Pg.Total"), _totalHits);
    }

    private void RebuildRows()
    {
        _pnlRows.Children.Clear();
        if (_pageHits.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = S("S.Mod.NoResults").Replace("{0}", _txtQuery.Text ?? ""),
                FontSize = 13, Margin = new Thickness(0, 16, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            _pnlRows.Children.Add(empty);
            return;
        }
        foreach (var h in _pageHits)
            _pnlRows.Children.Add(BuildRow(h));
    }

    private UIElement BuildRow(ModrinthService.SearchHit h)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 0x88, 0x88, 0x88)),
        };
        Grid.SetColumnSpan(card, 2);
        grid.Children.Add(card);

        var chk = new CheckBox
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
            IsChecked = _pickedIds.Contains(h.ProjectId),
        };
        chk.Checked += (_, _) => { MarkPicked(h, true); UpdatePickedUi(); };
        chk.Unchecked += (_, _) => { MarkPicked(h, false); UpdatePickedUi(); };
        grid.Children.Add(chk);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
        var title = new TextBlock
        {
            Text = h.Title,
            FontSize = 13.5, FontWeight = FontWeights.SemiBold,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
        var meta = new TextBlock
        {
            Text = string.Join("  ·  ", ModsPage.BuildBadges(h).Take(4)),
            FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        meta.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var desc = new TextBlock
        {
            Text = h.Description,
            FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        desc.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorTertiaryBrush");
        info.Children.Add(title);
        info.Children.Add(meta);
        if (h.Description.Length > 0) info.Children.Add(desc);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        return grid;
    }
}
