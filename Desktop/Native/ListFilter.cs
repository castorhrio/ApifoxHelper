namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 主页列表筛选的口径（docs/DESIGN.md §6「列表筛选」）：任务与配置会一直累积，
/// 固定顺序下要找到「上次那个任务」「某个配置下的任务」只能逐行扫一列；两个主页面列表上方
/// 各有一个筛选框，输入即按关键词筛选行，Esc 清空回到全部。
/// 规则：关键词按空白切成多个词，每个词都要在至少一个字段里出现（词间「且」、字段间「或」），
/// 按当前区域忽略大小写比较，空关键词即全部行；筛选先于排序（<see cref="Apply"/> → <see cref="ListSort.Order"/>）。
/// 筛选只改呈现顺序与可见行：不写任务与配置数据，选中按 Id 恢复。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ListFilter
{
    // 文案单一落点：占位文字与「筛到零行」的空态文字都从这里取，界面不就地拼写。
    public const string TaskPlaceholder = "筛选任务（名称、配置、下一步）";
    public const string ProfilePlaceholder = "筛选配置（名称、API 项目、Apifox 目标）";
    public const string TaskNoMatchTitle = "没有匹配的任务";
    public const string TaskNoMatchHint = "换一个关键词，或按 Esc 清空筛选框查看全部任务。";
    public const string ProfileNoMatchTitle = "没有匹配的配置";
    public const string ProfileNoMatchHint = "换一个关键词，或按 Esc 清空筛选框查看全部配置。";

    /// <summary>关键词去首尾空白；null 与全空白都视为「没有筛选」。</summary>
    public static string Normalize(string? query) => query?.Trim() ?? "";

    /// <summary>筛选框是否有内容（决定空态文字与状态行的计数口径）。</summary>
    public static bool IsActive(string? query) => Normalize(query).Length > 0;

    /// <summary>
    /// 一行是否命中关键词：词间「且」、字段间「或」，按当前区域忽略大小写；
    /// 空关键词命中全部行；null 字段按空处理（不抛异常、不误命中）。
    /// </summary>
    public static bool Matches(string? query, params string?[] fields) => MatchesAll(Terms(query), fields);

    /// <summary>
    /// 按关键词筛选行：没有关键词时返回数据源顺序的副本（与 <see cref="ListSort.Order"/> 同一纪律，
    /// 页面拿到的列表与数据源自身列表互不影响）；有关键词时保持数据源相对顺序，交给排序口径再排。
    /// </summary>
    public static List<T> Apply<T>(IEnumerable<T> rows, string? query, Func<T, string?[]> fields)
    {
        var terms = Terms(query);
        return terms.Length == 0 ? rows.ToList() : rows.Where(row => MatchesAll(terms, fields(row))).ToList();
    }

    private static string[] Terms(string? query) =>
        Normalize(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchesAll(string[] terms, string?[] fields)
    {
        foreach (var term in terms)
        {
            var hit = false;
            foreach (var field in fields)
            {
                if (field is null || !field.Contains(term, StringComparison.CurrentCultureIgnoreCase)) continue;
                hit = true;
                break;
            }
            if (!hit) return false;
        }
        return true;
    }
}
