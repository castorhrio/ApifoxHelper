using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表列排序断言（DESIGN.md §6「列表列排序」）：两个主页面的列表此前只有固定顺序
/// （任务=创建时间倒序、配置=名称升序），要按「更新时间」「接口数」「下一步」看队列只能从头扫；
/// 冻结页面的表格是只读流式布局、同样没有排序入口，原生补的是「点击列头即按该列排序」这一
/// Windows 列表惯例手势。口径单一落点在 <see cref="ListSort"/>（不依赖 WinForms，与测试工程共享编译），
/// 两页接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）；
/// 表头标记的实际绘制、鼠标手型与列头光标态随 G-D5 真机复核。
/// </summary>
public class ListSortTests
{
    private sealed record Row(string Text, int Count, DateTimeOffset At);

    private static readonly DateTimeOffset T0 = new(2026, 5, 1, 9, 30, 0, TimeSpan.FromHours(8));

    private static IComparable? Key(Row row, int column) => column switch
    {
        0 => row.Text,
        1 => row.Count,
        2 => row.At,
        _ => null,
    };

    private static List<Row> Rows(params (string Text, int Count, int Day)[] values) =>
        values.Select(v => new Row(v.Text, v.Count, T0.AddDays(v.Day))).ToList();

    // ---------- 状态口径：同一列反向、换一列从升序开始 ----------

    [Fact]
    public void Toggle_FirstClickOnAnyColumnSortsAscending()
    {
        // 未排序（None）与从其他列换过来，都从升序开始——第一次点击不猜用户想要哪个方向。
        Assert.Equal((3, false), ListSort.Toggle(ListSort.None, false, 3));
        Assert.Equal((3, false), ListSort.Toggle(ListSort.None, true, 3));
        Assert.Equal((0, false), ListSort.Toggle(6, true, 0));
    }

    [Fact]
    public void Toggle_RepeatedClickOnSameColumnFlipsDirection()
    {
        var (column, descending) = ListSort.Toggle(ListSort.None, false, 2);
        Assert.Equal((2, true), ListSort.Toggle(column, descending, 2));
        Assert.Equal((2, false), ListSort.Toggle(2, true, 2));
    }

    [Fact]
    public void Header_MarksOnlyTheSortedColumn()
    {
        Assert.Equal("任务 ▲", ListSort.Header("任务", 0, 0, false));
        Assert.Equal("任务 ▼", ListSort.Header("任务", 0, 0, true));
        // 其他列回到原标题：标记只有一处，用户能看出当前按哪一列排。
        Assert.Equal("更新时间", ListSort.Header("更新时间", 6, 0, true));
    }

    [Fact]
    public void Header_WhenNothingSorted_KeepsEveryOriginalTitle()
    {
        // None 与任何列号都不相等：不排序时表头不带标记，也不会把某列误显示成已排序。
        foreach (var column in new[] { 0, 1, 6 })
        {
            Assert.Equal("任务", ListSort.Header("任务", column, ListSort.None, descending: false));
            Assert.Equal("任务", ListSort.Header("任务", column, ListSort.None, descending: true));
        }
    }

    [Fact]
    public void Header_ReappliedFromOriginalTitle_DoesNotStackMarks()
    {
        // 每次重算都按建列时快照的原标题取值，反复刷新表头不叠加标记。
        const string original = "任务";
        var once = ListSort.Header(original, 0, 0, true);
        Assert.Equal(once, ListSort.Header(original, 0, 0, true));
        Assert.Equal(once, ListSort.Header(original, 0, 0, true));
        Assert.DoesNotContain("▲", once);
    }

    // ---------- 排序口径：只改顺序，同值不打乱 ----------

    [Fact]
    public void Order_WithoutSort_KeepsSourceOrder()
    {
        var rows = Rows(("b", 2, 1), ("a", 3, 0), ("c", 1, 2));
        Assert.Equal(rows.Select(r => r.Text), ListSort.Order(rows, ListSort.None, false, Key).Select(r => r.Text));
        // 未排序时方向位不参与：残留的降序位不会把顺序倒过来。
        Assert.Equal(rows.Select(r => r.Text), ListSort.Order(rows, ListSort.None, true, Key).Select(r => r.Text));
        // 不排序也返回新列表，页面拿到的顺序与数据源自身列表互不影响。
        Assert.NotSame(rows, ListSort.Order(rows, ListSort.None, false, Key));
    }

    [Fact]
    public void Order_ByColumn_SortsAscendingAndDescending()
    {
        var rows = Rows(("甲", 2, 1), ("乙", 3, 0), ("丙", 1, 2));
        Assert.Equal(["丙", "甲", "乙"], ListSort.Order(rows, 1, false, Key).Select(r => r.Text));
        Assert.Equal(["乙", "甲", "丙"], ListSort.Order(rows, 1, true, Key).Select(r => r.Text));
        // 时间列按完整时间戳，不只看单元格显示到分钟的文字。
        Assert.Equal(["乙", "甲", "丙"], ListSort.Order(rows, 2, false, Key).Select(r => r.Text));
    }

    [Fact]
    public void Order_KeepsEqualRowsInSourceOrder_BothDirections()
    {
        // 同值行（同一「下一步」、同一天更新）在升序与降序里都保持数据源相对顺序：
        // 反向只反转有差异的行，不把手风琴里同值的行打乱重排。
        var rows = Rows(("x", 5, 0), ("y", 5, 0), ("z", 1, 0));
        Assert.Equal(["z", "x", "y"], ListSort.Order(rows, 1, false, Key).Select(r => r.Text));
        Assert.Equal(["x", "y", "z"], ListSort.Order(rows, 1, true, Key).Select(r => r.Text));
    }

    [Fact]
    public void Order_TreatsMissingKeyAsSmallest()
    {
        // null 键（缺值、列号越界）视为最小：升序在前、降序在后，不抛异常也不丢行。
        var rows = Rows(("缺", 1, 0), ("b", 2, 0), ("a", 3, 0));
        IComparable? key(Row row, int column) => row.Text == "缺" ? null : row.Text;
        Assert.Equal(["缺", "a", "b"], ListSort.Order(rows, 0, false, key).Select(r => r.Text));
        Assert.Equal(["b", "a", "缺"], ListSort.Order(rows, 0, true, key).Select(r => r.Text));
        // 列号越界（界面加列而排序键未跟上）：全部行同键，按数据源顺序如实呈现。
        Assert.Equal(["缺", "b", "a"], ListSort.Order(rows, 9, false, (_, _) => null).Select(r => r.Text));
    }

    [Fact]
    public void Order_ComparesTextWithCurrentCultureNotCodePoints()
    {
        // 前置条件：本机区域比较真实生效（无 ICU 时两者同值，此处如实失败而不是空过）。
        Assert.NotEqual(
            string.Compare("a", "B", StringComparison.CurrentCulture),
            string.CompareOrdinal("a", "B"));
        // 中文与大小写混排的名称按当前区域比较，与资源管理器同序；
        // 按码位排会得到「B 在 a 前」这种与系统列表不一致的顺序。
        var rows = Rows(("avocado", 1, 0), ("Banana", 2, 0));
        Assert.Equal(["avocado", "Banana"], ListSort.Order(rows, 0, false, Key).Select(r => r.Text));
        Assert.Equal(["Banana", "avocado"], ListSort.Order(rows, 0, true, Key).Select(r => r.Text));
    }

    [Fact]
    public void Order_DoesNotMutateTheSourceList()
    {
        var rows = Rows(("b", 2, 1), ("a", 3, 0));
        ListSort.Order(rows, 0, false, Key);
        Assert.Equal(["b", "a"], rows.Select(r => r.Text));
    }

    // ---------- 共享编译与单一落点 ----------

    [Fact]
    public void SharedSource_IsCompiledIntoTestProject()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListSort.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListSort.cs", csproj);
    }

    [Fact]
    public void Pages_DoNotInlineSortMarks()
    {
        // 标记字面只在 ListSort 里；两页出现带 ▲/▼ 的字符串字面即口径漂移。
        // 只取单行内成对引号之间的内容，避免把注释里的标记跨行误判为字面。
        var marks = new Regex("\"[^\"\\r\\n]*[▲▼][^\"\\r\\n]*\"");
        foreach (var name in new[] { "TasksPage.cs", "ProjectsPage.cs" })
        {
            var source = Repo.Source("Desktop", "Native", name);
            Assert.False(marks.IsMatch(source), $"{name}:表头标记字面应在 ListSort 单一落点生成。");
        }
        Assert.Equal("▲", ListSort.AscendingMark);
        Assert.Equal("▼", ListSort.DescendingMark);
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void TasksPage_WiresColumnSort()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 初始未排序、原标题快照取一次、列头点击进排序状态。
        Assert.Matches(@"(?m)^\s*private int _sortColumn = ListSort\.None;", page);
        Assert.Matches(@"(?m)^\s*_columnTitles = _list\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", page);
        Assert.Matches(@"(?m)^\s*_list\.ColumnClick \+= \(_, e\) => SortBy\(e\.Column\);", page);
        Assert.Matches(@"(?m)^\s*\(_sortColumn, _sortDescending\) = ListSort\.Toggle\(_sortColumn, _sortDescending, column\);", page);
        // 重建走同一口径，表头标记在 EndUpdate 之后、列宽重算之前刷新。
        Assert.Matches(@"(?m)^\s*var tasks = ListSort\.Order\(_tasks\.List\(\), _sortColumn, _sortDescending, SortKey\);", page);
        var load = Slice(page, "private void LoadTasks()", "private void SelectTask(string taskId)");
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshSortHeaders\(\);\s*\n\s*FitColumns\(\);", load);
        Assert.Matches(@"(?m)^\s*_list\.Columns\[i\]\.Text = ListSort\.Header\(_columnTitles\[i\], i, _sortColumn, _sortDescending\);", page);
    }

    [Fact]
    public void ProjectsPage_WiresColumnSort()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*private int _sortColumn = ListSort\.None;", page);
        Assert.Matches(@"(?m)^\s*_columnTitles = _list\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", page);
        Assert.Matches(@"(?m)^\s*_list\.ColumnClick \+= \(_, e\) => SortBy\(e\.Column\);", page);
        Assert.Matches(@"(?m)^\s*\(_sortColumn, _sortDescending\) = ListSort\.Toggle\(_sortColumn, _sortDescending, column\);", page);
        Assert.Matches(@"(?m)^\s*var profiles = ListSort\.Order\(_profiles\.List\(\), _sortColumn, _sortDescending, SortKey\);", page);
        var load = Slice(page, "private void LoadProfiles()", "private void FitColumns()");
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshSortHeaders\(\);\s*\n\s*FitColumns\(\);", load);
        Assert.Matches(@"(?m)^\s*_list\.Columns\[i\]\.Text = ListSort\.Header\(_columnTitles\[i\], i, _sortColumn, _sortDescending\);", page);
    }

    [Fact]
    public void BothPages_SortKeyCoversEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        foreach (var name in new[] { "TasksPage.cs", "ProjectsPage.cs" })
        {
            var page = Repo.Source("Desktop", "Native", name);
            var columns = Regex.Matches(page, @"(?m)^\s*_list\.Columns\.Add\(").Count;
            Assert.True(columns > 0, $"{name}:未找到建列行。");
            var start = page.IndexOf("IComparable? SortKey(", StringComparison.Ordinal);
            Assert.True(start >= 0, $"{name}:缺少 SortKey。");
            var body = page[start..page.IndexOf("};", start, StringComparison.Ordinal)];
            var arms = Regex.Matches(body, @"(?m)^\s*(\d+) => ").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
            Assert.Equal(Enumerable.Range(0, columns), arms);
        }
    }

    [Fact]
    public void TasksPage_SortKeysMirrorDisplayedCells()
    {
        // 排序键与单元格文字同源（数量列取数值、时间列取完整时间戳），避免「显示与排序两套口径」。
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var body = SortKeyBody(page);
        Assert.Matches(@"(?m)^\s*3 => task\.Stages\.Source\.Endpoints,$", body);
        Assert.Matches(@"(?m)^\s*4 => task\.Stages\.Source\.Candidates,$", body);
        Assert.Matches(@"(?m)^\s*6 => task\.UpdatedAt,$", body);
        Assert.DoesNotMatch(@"UpdatedAt\.LocalDateTime\.ToString", body);
        // 「下一步」列按引导标题排（与单元格同一口径），Apifox 列按状态文字排。
        Assert.Matches(@"(?m)^\s*2 => NextStep\.Compute\(task\)\.Title,$", body);
        Assert.Matches(@"(?m)^\s*5 => TaskDisplay\.RemoteStatusText\(task\.Stages\.Remote\.Status\),$", body);
    }

    [Fact]
    public void ProjectsPage_SortKeysMirrorDisplayedCells()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        var body = SortKeyBody(page);
        Assert.Matches(@"(?m)^\s*1 => profile\.ApiProject,$", body);
        Assert.Matches(@"(?m)^\s*2 => profile\.Apifox\.ProjectId \?\? ""未绑定"",$", body);
        Assert.Matches(@"(?m)^\s*3 => profile\.Apifox\.BranchName \?\? ""主分支"",$", body);
        Assert.Matches(@"(?m)^\s*4 => SwaggerText\(profile\.Swagger\.Type\),$", body);
        Assert.Matches(@"(?m)^\s*5 => profile\.UpdatedAt,$", body);
        Assert.DoesNotMatch(@"UpdatedAt\.LocalDateTime\.ToString", body);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_DeclaresColumnSortRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("列排序", section);
        Assert.Contains("`ListSort`", section);
        Assert.Contains("▲", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("列表列排序", ledger);
        Assert.Contains("ListSort", ledger);
    }

    private static string SortKeyBody(string page)
    {
        var start = page.IndexOf("IComparable? SortKey(", StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少 SortKey。");
        return page[start..page.IndexOf("};", start, StringComparison.Ordinal)];
    }

    /// <summary>取 [start, end) 之间的源码段：把「重建后调用」的断言限定在加载方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
