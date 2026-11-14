namespace HardmacroCraft.Services;

/// <summary>
/// v0.15.0 PCL 式翻页纯逻辑（可测试）：
/// 超大列表不再"加载更多"无限累积，改为 PCL 同款的页码翻页条 ——
/// 给定总条数 / 页大小 / 当前页，产出翻页条按钮序列：
/// « 首页 · ‹ 上一页 · [1] [2] … [n] · 下一页 › · 末页 »，
/// 页码过多时当前页附近优先显示、两端折叠为省略号。
/// </summary>
public static class PclPager
{
    /// <summary>搜索结果每页条数（Modrinth / CurseForge 搜索 API 的 limit 都是 25）。</summary>
    public const int SearchPageSize = 25;

    /// <summary>本机版本列表每页条数（PCL 式版本浏览器同量级）。</summary>
    public const int LocalPageSize = 50;

    /// <summary>翻页条单个按钮的描述。</summary>
    public sealed record PagerButton(
        string Label,
        int? TargetPage,
        bool IsCurrent,
        bool IsEllipsis,
        bool IsEnabled,
        bool IsNav = false) // « ‹ › » 这类导航按钮（图标式，PCL 同款）
    {
        public static PagerButton Ellipsis() => new("…", null, false, true, false);
    }

    public static int TotalPages(int totalItems, int pageSize)
        => totalItems <= 0 ? 1 : (int)Math.Ceiling(totalItems / (double)Math.Max(1, pageSize));

    public static int ClampPage(int page, int totalPages)
        => Math.Max(1, Math.Min(Math.Max(1, page), Math.Max(1, totalPages)));

    /// <summary>页码对应的偏移量（搜索 API 的 offset 参数；页码最小为 1）。</summary>
    public static int OffsetOf(int page, int pageSize) => (Math.Max(1, page) - 1) * Math.Max(1, pageSize);

    /// <summary>数字页码按钮窗口：最多 maxNumbers 个，当前页附近优先（向两端钳制），PCL 同款。</summary>
    public static List<int> NumberWindow(int currentPage, int totalPages, int maxNumbers = 7)
    {
        totalPages = Math.Max(1, totalPages);
        var count = Math.Min(maxNumbers, totalPages);
        var start = ClampPage(currentPage - count / 2, totalPages - count + 1);
        return Enumerable.Range(start, count).ToList();
    }

    /// <summary>完整翻页条按钮序列（首页 / 上一页 / 数字页（含省略号）/ 下一页 / 末页）。
    /// 单页（总页数 ≤ 1）时不渲染数字页，仅保留禁用的导航按钮。</summary>
    public static List<PagerButton> BuildButtons(int currentPage, int totalPages)
    {
        totalPages = Math.Max(1, totalPages);
        currentPage = ClampPage(currentPage, totalPages);
        var list = new List<PagerButton>
        {
            new("«", 1, false, false, currentPage > 1, IsNav: true),
            new("‹", currentPage - 1, false, false, currentPage > 1, IsNav: true),
        };
        if (totalPages <= 1)
        {
            list.Add(new PagerButton("›", currentPage + 1, false, false, false, IsNav: true));
            list.Add(new PagerButton("»", totalPages, false, false, false, IsNav: true));
            return list;
        }
        var numbers = NumberWindow(currentPage, totalPages);
        for (var i = 0; i < numbers.Count; i++)
        {
            if (i == 0 && numbers[0] > 1) list.Add(PagerButton.Ellipsis());
            list.Add(new PagerButton(numbers[i].ToString(), numbers[i],
                numbers[i] == currentPage, false, numbers[i] != currentPage));
            if (i == numbers.Count - 1 && numbers[^1] < totalPages) list.Add(PagerButton.Ellipsis());
        }
        list.Add(new PagerButton("›", currentPage + 1, false, false, currentPage < totalPages, IsNav: true));
        list.Add(new PagerButton("»", totalPages, false, false, currentPage < totalPages, IsNav: true));
        return list;
    }
}
