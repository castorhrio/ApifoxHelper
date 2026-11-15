namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「点列头」这个手势的<b>落点几何</b>（第三百二十七轮）：列头往右第几格、落点在那一格里的哪个位置。
/// <para>
/// <b>为什么要有这一层</b>：排序本身的口径在 <see cref="ListSort"/>（点哪一列就按哪一列排、再点反向），
/// 表头方向标记也在那儿；但「鼠标要落在表头的哪一点上才算是点了第三列」这件事此前没有落点——
/// 它只存在于冒烟器里的一段临时代码里（算列宽之和再取中点），而那段代码<b>没人能在 Linux 上断言</b>：
/// 算错一格，点下去的就是隔壁那一列，量到的却是「排序好像没生效」。
/// 现在算式收在这一处，冒烟器点列头时用的就是它，测试工程也共享编译它（见
/// <c>ApifoxHelper.Tests/ApifoxHelper.Tests.csproj</c> 的 <c>ColumnHeaderClick.cs</c> 一行），
/// 于是「落点算得对不对」在 Linux 上是一条会红的断言，不必等一台 Windows 机器。
/// </para>
/// <para>
/// <b>本文件只算几何，不动手</b>：不取窗口、不移光标、不发鼠标消息——那些是冒烟器那一侧的动作。
/// 这里只回答一句「第 <c>columnIndex</c> 列的落点相对表头客户区左上角在哪儿」。
/// </para>
/// <para>
/// <b>口径说明</b>：清单各列在表头里的位置是<b>列宽的累加</b>（第 N 列的左边界 = 前 N 列列宽之和），
/// 与 WinForms 自身排列表头的方式一致；因此落点取「本列左边界 + 本列宽的一半」，
/// 永远落在本列之内，不会点到隔壁列（隔壁列的左边界就是本列的右边界）。
/// </para>
/// </summary>
public static class ColumnHeaderClick
{
    /// <summary>
    /// 某一列左边界相对表头左边界的偏移（像素）：前面各列列宽之和。
    /// <para>
    /// 列宽非正即报错：列宽为 0 的列在表头上没有可点的地方（点下去落不到那一列上），
    /// 列宽为负说明列宽读错了——这两种情况都当「量不到」处理，不给出一个假落点。
    /// </para>
    /// </summary>
    public static int LeftOffset(IReadOnlyList<int> widths, int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (columnIndex < 0 || columnIndex >= widths.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnIndex),
                columnIndex,
                $"列号越界：这份清单有 {widths.Count} 列，没有第 {columnIndex} 列。");
        }

        var offset = 0;
        for (var i = 0; i < columnIndex; i++) offset += Width(widths, i);
        return offset;
    }

    /// <summary>
    /// 落点（相对表头客户区左上角）：横向取本列中点，纵向取表头高度的一半。
    /// <para>
    /// 纵向取一半而不是顶部一像素：表头顶部贴着边框，点在最上一行有落进边框、
    /// 被当成拖动列宽起手的可能；取中点与「人点列头」的位置一致。
    /// </para>
    /// </summary>
    public static (int X, int Y) PointIn(IReadOnlyList<int> widths, int columnIndex, int headerHeight)
    {
        ArgumentNullException.ThrowIfNull(widths);
        if (headerHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(headerHeight),
                headerHeight,
                "表头高度不是正数：取不到表头有多高，落点算不出来。");
        }

        var width = Width(widths, columnIndex);
        var offset = LeftOffset(widths, columnIndex);
        return (offset + width / 2, headerHeight / 2);
    }

    /// <summary>某一列的列宽（越界或非正即报错——不给假落点）。</summary>
    private static int Width(IReadOnlyList<int> widths, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= widths.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(columnIndex),
                columnIndex,
                $"列号越界：这份清单有 {widths.Count} 列，没有第 {columnIndex} 列。");
        }

        var width = widths[columnIndex];
        if (width <= 0)
        {
            throw new InvalidOperationException(
                $"第 {columnIndex} 列的列宽是 {width}px：表头上没有这一格可点，算不出落点。");
        }

        return width;
    }
}
