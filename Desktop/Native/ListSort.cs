namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 列表列排序的单一口径（DESIGN.md §6「列表列排序」）：点击列头按该列排序，再点同一列反向，
/// 换一列从升序开始；表头文字带方向标记（▲ 升序 / ▼ 降序），未排序的列保持原标题。
/// 未排序（<see cref="None"/>）时保持数据源自身顺序——任务列表即任务创建时间倒序，与冻结页面一致。
/// 排序只改呈现顺序：不写任务数据、不改选中（选中按任务 Id 恢复），也不改列宽（列宽仍由 ListColumns 口径算）。
/// 文字列按「自然序」比较，与资源管理器的名称列同序：连续数字段按数值排（「2」在「10」前，不再是「10」在「2」前），
/// 其余段仍按当前区域比较（中文与大小写混排的名称保持既有 locale 行为）——本工具的任务名、配置名、接口资源与
/// 项目 ID 常带序号，按文字排会把带不同位数的序号排错。
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
        // 文字按自然序与资源管理器同序：数字段按数值、其余按当前区域比较。
        (string a, string b) => CompareText(a, b),
        _ => left.CompareTo(right),
    };

    /// <summary>
    /// 自然序（logical/natural）文字比较，逐段推进：两侧同为 ASCII 数字时整段按数值比较（「2」在「10」前），
    /// 同为非数字时整段按当前区域比较（中文与大小写的 locale 行为不变），数字与非数字并排时按当前区域比较
    /// 这两个字符；一方先读完则短的在前。本工具的任务名/配置名/接口资源/项目 ID 常带序号，纯文字比较会把
    /// 「报告10」排到「报告2」前——与资源管理器显示的名称列不一致，故收成这一条口径。
    /// </summary>
    private static int CompareText(string left, string right)
    {
        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            var leftDigit = IsAsciiDigit(left[i]);
            var rightDigit = IsAsciiDigit(right[j]);
            if (leftDigit && rightDigit)
            {
                var leftEnd = DigitRunEnd(left, i);
                var rightEnd = DigitRunEnd(right, j);
                var byValue = CompareDigitRuns(left[i..leftEnd], right[j..rightEnd]);
                if (byValue != 0) return byValue;
                i = leftEnd;
                j = rightEnd;
            }
            else if (!leftDigit && !rightDigit)
            {
                var leftEnd = TextRunEnd(left, i);
                var rightEnd = TextRunEnd(right, j);
                var byCulture = string.Compare(left[i..leftEnd], right[j..rightEnd], StringComparison.CurrentCulture);
                if (byCulture != 0) return byCulture;
                i = leftEnd;
                j = rightEnd;
            }
            else
            {
                // 数字与文字在同一位置相遇：按当前区域比较这两个字符即可，双方各前进一格。
                var byChar = string.Compare(left[i..(i + 1)], right[j..(j + 1)], StringComparison.CurrentCulture);
                if (byChar != 0) return byChar;
                i++;
                j++;
            }
        }
        // 一方已读完：较短的一方在前。
        return (left.Length - i).CompareTo(right.Length - j);
    }

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private static int DigitRunEnd(string text, int start)
    {
        var end = start;
        while (end < text.Length && IsAsciiDigit(text[end])) end++;
        return end;
    }

    private static int TextRunEnd(string text, int start)
    {
        var end = start;
        while (end < text.Length && !IsAsciiDigit(text[end])) end++;
        return end;
    }

    /// <summary>
    /// 两段纯 ASCII 数字按数值比较：先去前导零再比剩余位数（位数多的数值大），等长时按字符序逐位比较——
    /// 全程不转整数，故不受 Int64 上限影响（超长的项目 ID 也如实比较）。数值相同时前导零多的排后，
    /// 保证「007」与「7」有确定先后，不随数据源顺序浮动。
    /// </summary>
    private static int CompareDigitRuns(string left, string right)
    {
        var l = left.TrimStart('0');
        var r = right.TrimStart('0');
        if (l.Length != r.Length) return l.Length.CompareTo(r.Length);
        var byDigits = string.CompareOrdinal(l, r);
        if (byDigits != 0) return byDigits;
        return left.Length.CompareTo(right.Length);
    }
}
