using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「点列头」这个手势的落点几何（第三百二十七轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：点列头排序这条手势此前只有两处证据——<c>ListSort</c> 把自然序、方向标记与表头文案
/// 算对了（<c>ListSortTests</c> 逐条断言），<c>TasksPage</c> 也确实把 <c>ColumnClick</c> 接到了 <c>SortBy</c> 上；
/// 但<b>没有任何一处真的点过一次列头</b>。冒烟器第三百二十七轮补上了那一次真鼠标点击，而点击要落到
/// 「第几列」上，靠的是一段几何算式：<b>列宽累加取中点</b>。这段算式此前只活在冒烟器的一段临时代码里，
/// 在 Linux 上没人能断言——算错一格，点下去的就是隔壁那一列，量到的却是「排序好像没生效」。
/// </para>
/// <para>
/// <b>本文件判两件事</b>：① 几何本身（<see cref="ColumnHeaderClick.LeftOffset"/> 与
/// <see cref="ColumnHeaderClick.PointIn"/> 的真值、边界与「落点必在本列之内」）；
/// ② <b>冒烟器用的就是这一处算式</b>——它自己不许再写一份（对账门：两侧各有一个定义，
/// 一处改了另一处不跟着改，这一条当场红）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「真鼠标点下去 Windows 会不会把这一下算成列头点击」——那要一台 Windows 机器
/// （G-D5 仍 0 台）。模拟器上那一次真点击在冒烟器 <c>RunColumnSortGesture</c> 里，与这里判的算式是两半，
/// 谁也不替谁作证。
/// </para>
/// </summary>
public class ColumnHeaderClickTests
{
    /// <summary>冒烟器里点列头的那一段：它必须调 <see cref="ColumnHeaderClick.PointIn"/>，不许自己算。</summary>
    private const string SmokeClickSignature = "private static void ClickColumnHeaderOnce(";

    /// <summary>
    /// 左边界 = 前面各列列宽之和。三列 120 / 200 / 80 时，第 0 / 1 / 2 列的左边界分别是 0 / 120 / 320。
    /// </summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 120)]
    [InlineData(2, 320)]
    public void LeftOffset_SumsTheWidthsBeforeTheColumn(int columnIndex, int expected)
    {
        Assert.Equal(expected, ColumnHeaderClick.LeftOffset([120, 200, 80], columnIndex));
    }

    /// <summary>
    /// 落点横向取本列中点、纵向取表头高度的一半：三列 120 / 200 / 80、表头高 24 时，
    /// 第 0 / 1 / 2 列的落点是 (60, 12) / (220, 12) / (360, 12)。
    /// </summary>
    [Theory]
    [InlineData(0, 60)]
    [InlineData(1, 220)]
    [InlineData(2, 360)]
    public void PointIn_TakesTheMiddleOfTheColumn(int columnIndex, int expectedX)
    {
        var (x, y) = ColumnHeaderClick.PointIn([120, 200, 80], columnIndex, 24);
        Assert.Equal(expectedX, x);
        Assert.Equal(12, y);
    }

    /// <summary>
    /// <b>落点必在本列之内</b>：对每一列，<c>左边界 ≤ x &lt; 左边界 + 列宽</c>。
    /// <para>
    /// 这一条是这段几何真正要保证的事——「点第三列会不会落到第二列上」。上面两条真值只钉住了三组数，
    /// 这一条钉住的是<b>任意列宽组合</b>下的性质：奇数宽、极窄列、极宽列都不许越界。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(new[] { 120, 200, 80 })]
    [InlineData(new[] { 1, 1, 1 })]
    [InlineData(new[] { 7, 3, 999, 41 })]
    [InlineData(new[] { 2000 })]
    [InlineData(new[] { 5, 5, 5, 5, 5, 5, 5, 5 })]
    public void PointIn_AlwaysFallsInsideTheClickedColumn(int[] widths)
    {
        for (var column = 0; column < widths.Length; column++)
        {
            var (x, _) = ColumnHeaderClick.PointIn(widths, column, 24);
            var left = ColumnHeaderClick.LeftOffset(widths, column);
            Assert.True(x >= left && x < left + widths[column],
                $"第 {column} 列的落点是 {x}，而这一列占 [{left}, {left + widths[column]})："
                + "落点跑到隔壁列上去了，点下去排的是另一列。");
        }
    }

    /// <summary>
    /// 列号越界即报错（不给假落点）：这份清单有几列就只认第 0…n-1 列。
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void PointIn_RejectsAColumnThatIsNotThere(int columnIndex)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnHeaderClick.PointIn([120, 200, 80], columnIndex, 24));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnHeaderClick.LeftOffset([120, 200, 80], columnIndex));
    }

    /// <summary>
    /// 列宽非正即报错：列宽为 0 的列在表头上没有可点的地方，列宽为负说明读错了——两种都当「量不到」。
    /// </summary>
    [Theory]
    [InlineData(new[] { 120, 0, 80 }, 1)]
    [InlineData(new[] { -5, 200 }, 0)]
    [InlineData(new[] { 120, 200, -1 }, 2)]
    public void PointIn_RejectsANonPositiveWidth(int[] widths, int columnIndex)
    {
        Assert.Throws<InvalidOperationException>(
            () => ColumnHeaderClick.PointIn(widths, columnIndex, 24));
    }

    /// <summary>
    /// 表头高度非正即报错：取不到表头有多高，落点算不出来（纵向取一半，0 高没有一半）。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-24)]
    public void PointIn_RejectsANonPositiveHeaderHeight(int headerHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ColumnHeaderClick.PointIn([120, 200, 80], 0, headerHeight));
    }

    /// <summary>
    /// 空清单与 null 都报错：没有列就没有落点，不许返回一个 (0, 0) 让调用方以为点到了第一列。
    /// </summary>
    [Fact]
    public void PointIn_RejectsAnEmptyOrNullList()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ColumnHeaderClick.PointIn([], 0, 24));
        Assert.Throws<ArgumentNullException>(() => ColumnHeaderClick.PointIn(null!, 0, 24));
        Assert.Throws<ArgumentNullException>(() => ColumnHeaderClick.LeftOffset(null!, 0));
    }

    /// <summary>
    /// <b>对账门</b>：冒烟器点列头那一段必须调 <see cref="ColumnHeaderClick.PointIn"/>，
    /// 且不许自己再写一份「列宽累加取中点」的算式。
    /// <para>
    /// 两侧各有一个定义：算式在 <c>Desktop/Native/ColumnHeaderClick.cs</c>，点击动作在冒烟器里。
    /// 冒烟器自己算一份的话，本文件上面那些断言判的就不是「点下去用的是哪套几何」——
    /// 那正是第三百二十七轮之前的状态（算式只活在冒烟器里，Linux 上没人能断言）。
    /// </para>
    /// </summary>
    [Fact]
    public void SmokeProgram_UsesTheSharedGeometryInsteadOfItsOwn()
    {
        var body = SmokeBody(SmokeClickSignature);
        Assert.Contains("ColumnHeaderClick.PointIn(", body, StringComparison.Ordinal);

        // 自己算一份的形状：把列宽累加进一个局部变量再取中点。出现即红。
        Assert.DoesNotContain("widths.Sum(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("widths.Take(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Aggregate(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 上面那条对账门<b>自己</b>被抓得住：拿「自己算一份」的写法喂一遍，必须报出来。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。这里把判据抽成一段纯函数，两发注入各喂一次。
    /// </para>
    /// </summary>
    [Fact]
    public void GeometryDetector_ReportsTheBreaksItMustCatch()
    {
        Assert.Empty(GeometryProblems("var (x, y) = ColumnHeaderClick.PointIn(widths, columnIndex, h);"));
        Assert.Contains(GeometryProblems("var left = widths.Take(columnIndex).Sum();"),
            p => p.Contains("自己算", StringComparison.Ordinal));
        Assert.Contains(GeometryProblems("var x = 0; var y = 0;"),
            p => p.Contains("PointIn", StringComparison.Ordinal));
    }

    /// <summary>判据本体（文本进、问题出）：冒烟器那一段的正文有没有用共享算式。</summary>
    private static List<string> GeometryProblems(string body)
    {
        var problems = new List<string>();
        if (!body.Contains("ColumnHeaderClick.PointIn(", StringComparison.Ordinal))
        {
            problems.Add("冒烟器点列头那一段没有调 ColumnHeaderClick.PointIn：落点几何又回到冒烟器里自己算，"
                + "Linux 上没人能断言它算得对不对。");
        }

        if (Regex.IsMatch(body, @"widths\.(Sum|Take|Aggregate)\("))
        {
            problems.Add("冒烟器点列头那一段自己算了一份列宽累加：算式有两个定义，改一处另一处不跟着改。");
        }

        return problems;
    }

    /// <summary>冒烟器里一个方法从签名到下一个顶格 <c>private static</c> 之间的正文。</summary>
    private static string SmokeBody(string signature)
    {
        var text = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var from = text.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"冒烟器里找不到 {signature}——点列头那一段被删了或改了签名。");
        var next = text.IndexOf("\n    private static", from + signature.Length, StringComparison.Ordinal);
        return next < 0 ? text[from..] : text[from..next];
    }
}
