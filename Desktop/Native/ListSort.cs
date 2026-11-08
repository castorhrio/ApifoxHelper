namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表列排序的单一口径（DESIGN.md §6「列表列排序」）：点击列头按该列排序，再点同一列反向，
/// 换一列从升序开始；表头文字带方向标记（▲ 升序 / ▼ 降序），未排序的列保持原标题。
/// 未排序（<see cref="None"/>）时保持数据源自身顺序——任务列表即任务创建时间倒序，与冻结页面一致。
/// 排序只改呈现顺序：不写任务数据、不改选中（选中按任务 Id 恢复），也不改列宽（列宽仍由 ListColumns 口径算）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ListSort
{
    /// <summary>未排序：按数据源自身顺序呈现。</summary>
    public const int None = -1;

    public const string AscendingMark = "▲";
    public const string DescendingMark = "▼";

    /// <summary>点击列头后的排序状态：同一列反向，换一列从升序开始。</summary>
    public static (int Column, bool Descending) Toggle(int column, bool descending, int clicked) =>
        clicked == column ? (column, !descending) : (clicked, false);

    /// <summary>
    /// 表头文字：排序列追加方向标记，其余列保持原标题。标题本身不含标记，
    /// 每次按原标题重算，重复调用不叠加标记。
    /// </summary>
    public static string Header(string title, int column, int sortColumn, bool descending) =>
        column == sortColumn ? title + " " + (descending ? DescendingMark : AscendingMark) : title;

    /// <summary>
    /// 按列排序：<paramref name="key"/> 给出该行在该列的排序键（null 视为最小，升序在前）；
    /// 键相等的行保持数据源顺序——两个方向都稳定，反向不会打乱同值行。
    /// </summary>
    public static List<T> Order<T>(IReadOnlyList<T> rows, int column, bool descending, Func<T, int, IComparable?> key)
    {
        if (column < 0) return rows.ToList();
        var indexed = rows.Select((row, index) => (row, index)).ToList();
        indexed.Sort((left, right) =>
        {
            var result = Compare(key(left.row, column), key(right.row, column));
            if (descending) result = result < 0 ? 1 : result > 0 ? -1 : 0;
            return result != 0 ? result : left.index.CompareTo(right.index);
        });
        return indexed.Select(pair => pair.row).ToList();
    }

    private static int Compare(IComparable? left, IComparable? right) => (left, right) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        // 文字按当前区域比较：任务与配置名称是中文，按码位排会得到与资源管理器不同的顺序。
        (string a, string b) => string.Compare(a, b, StringComparison.CurrentCulture),
        _ => left.CompareTo(right),
    };
}
