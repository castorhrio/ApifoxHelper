using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Source;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 接口追踪明细清单的列排序断言（DESIGN.md §6「列表列排序」，第九十八轮）：
/// 检查结果对话框的两张清单自第九十七轮起点击列头即按该列排序，而接口追踪明细的接口清单
/// （五列）点击列头没有任何反应——这一屏与检查结果是同一只壳（只读工作台：关键词框 + 清单 +
/// 详情区 + 证据入口），「把预算超限的那几条集中看」「按未解析调用排」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="EndpointTraceSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class EndpointTraceSortTests
{
    private static EndpointCoverage Coverage(string key, int methods, int unresolved, int external, bool exceeded) => new()
    {
        EndpointKey = key,
        MethodsRead = Enumerable.Range(0, methods).Select(i => "M" + i).ToList(),
        Unresolved = Enumerable.Range(0, unresolved).Select(i => new UnresolvedCall { Expression = "u" + i, Reason = "r" }).ToList(),
        ExternalCalls = Enumerable.Range(0, external).Select(i => "E" + i).ToList(),
        BudgetExceeded = exceeded,
    };

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFiveCells()
    {
        var coverage = Coverage("GET /orders/{id}", 3, 2, 1, exceeded: true);
        // 接口列：与首格同一表达式。
        Assert.Equal("GET /orders/{id}", EndpointTraceSort.Key(coverage, 0));
        // 三个计数列取数值：单元格的「—」是零的占位，排序按 0 参与（不按「—」这个字的码位排）。
        Assert.Equal(3, EndpointTraceSort.Key(coverage, 1));
        Assert.Equal(2, EndpointTraceSort.Key(coverage, 2));
        Assert.Equal(1, EndpointTraceSort.Key(coverage, 3));
        Assert.Equal(0, EndpointTraceSort.Key(Coverage("GET /x", 0, 0, 0, exceeded: false), 1));
        // 预算列取「是否超限」：单元格的「超限」/「—」正是这一位。
        Assert.Equal(1, EndpointTraceSort.Key(coverage, 4));
        Assert.Equal(0, EndpointTraceSort.Key(Coverage("GET /x", 0, 0, 0, exceeded: false), 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(EndpointTraceSort.Key(Coverage("GET /x", 1, 1, 1, exceeded: false), column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByBudgetPutsTheExceededFirstWhenDescending()
    {
        var rows = new[]
        {
            Coverage("GET /a", 2, 0, 0, exceeded: false),
            Coverage("GET /b", 1, 0, 0, exceeded: true),
            Coverage("GET /c", 3, 0, 0, exceeded: false),
        };
        Assert.Equal(["GET /a", "GET /c", "GET /b"], ListSort.Order(rows, 4, false, EndpointTraceSort.Key).Select(r => r.EndpointKey));
        Assert.Equal(["GET /b", "GET /a", "GET /c"], ListSort.Order(rows, 4, true, EndpointTraceSort.Key).Select(r => r.EndpointKey));
    }

    [Fact]
    public void Ordering_ByUnresolvedPutsTheDegradedFirstWhenDescending()
    {
        var rows = new[]
        {
            Coverage("GET /a", 2, 0, 0, exceeded: false),
            Coverage("GET /b", 2, 3, 0, exceeded: false),
            Coverage("GET /c", 2, 1, 0, exceeded: false),
        };
        Assert.Equal(["GET /b", "GET /c", "GET /a"], ListSort.Order(rows, 2, true, EndpointTraceSort.Key).Select(r => r.EndpointKey));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            Coverage("GET /a", 1, 1, 0, exceeded: false),
            Coverage("GET /b", 1, 1, 0, exceeded: false),
            Coverage("GET /c", 1, 1, 0, exceeded: false),
        };
        Assert.Equal(["GET /a", "GET /b", "GET /c"], ListSort.Order(rows, 2, false, EndpointTraceSort.Key).Select(r => r.EndpointKey));
        Assert.Equal(["GET /a", "GET /b", "GET /c"], ListSort.Order(rows, 2, true, EndpointTraceSort.Key).Select(r => r.EndpointKey));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与两个主页面、检查结果同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（接口身份 + 预算列文字），故这里也只用这两格找行。
        var rows = new[]
        {
            Coverage("GET /a", 2, 0, 0, exceeded: false),
            Coverage("GET /b", 2, 3, 0, exceeded: true),
            Coverage("GET /c", 2, 1, 0, exceeded: true),
        };
        var exceeded = ListFilter.Apply(rows, EndpointTrace.BudgetExceededText, c => EndpointTrace.KeywordFields(c.EndpointKey, c.BudgetExceeded));
        Assert.Equal(["GET /b", "GET /c"], exceeded.Select(r => r.EndpointKey));
        // 排序只在筛出来的两行里发生：/a 不在结果里，两个超限行按未解析调用倒序。
        var ordered = ListSort.Order(exceeded, 2, true, EndpointTraceSort.Key);
        Assert.Equal(["GET /b", "GET /c"], ordered.Select(r => r.EndpointKey));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        Assert.Matches(@"(?m)^\s*private int _sortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _sortDescending;$", source);
        Assert.Matches(@"(?m)^\s*_titles = _list\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_list\.ColumnClick \+= \(_, e\) => SortBy\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_sortColumn, _sortDescending\) = ListSort\.Toggle\(_sortColumn, _sortDescending, column\);", source);
        Assert.DoesNotContain("_sortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不叠加、不就地拼方向字面）。
        Assert.Matches(@"(?m)^\s*_list\.Columns\[i\]\.Text = ListSort\.Header\(_titles\[i\], i, _sortColumn, _sortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_AppliesSortAfterFilterAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "private void SortBy");
        // 筛选先于排序：ListFilter.Apply 在 ListSort.Order 之内（同一表达式），排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", populate);
        Assert.Contains("ListFilter.Apply(_coverages, _keyword.Text, c => EndpointTrace.KeywordFields(c.EndpointKey, c.BudgetExceeded)),", populate, StringComparison.Ordinal);
        Assert.Contains("_sortColumn, _sortDescending, EndpointTraceSort.Key);", populate, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshSortHeaders\(\);", populate);
        var refresh = populate.IndexOf("RefreshSortHeaders();", StringComparison.Ordinal);
        var emptyBranch = populate.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && emptyBranch > refresh, "表头标记要在筛到零行那一支之前刷新（那一支先返回）。");
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "EndpointTraceSort.cs");
        Assert.Equal(5, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 5), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("=> coverage.", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "EndpointTraceSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByEndpointKeyAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按接口身份恢复（重建前记下、重建后找回），详情与证据入口随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "private void SortBy");
        var remember = populate.IndexOf("var selectedKey = (Ui.SelectedItem(_list)?.Tag as EndpointCoverage)?.EndpointKey;", StringComparison.Ordinal);
        var clear = populate.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = populate.IndexOf("Ui.SelectItem(target ?? _list.Items[0]);", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember && restore > clear, "选中要在清空之前记下、在重建之后经唯一入口恢复。");
        // 排序入口只翻状态再重建，不自己选中、不自己滚动。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\EndpointTraceSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheTraceSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("接口追踪明细清单的列排序", section);
        Assert.Contains("`EndpointTraceSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("接口追踪明细清单的列排序（§6）", ledger);
        Assert.Contains("EndpointTraceSort", ledger);
    }

    private static List<int> Arms(string source)
    {
        var start = source.IndexOf("IComparable? Key(", StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少 Key。");
        var body = source[start..source.IndexOf("};", start, StringComparison.Ordinal)];
        return Regex.Matches(body, @"(?m)^\s*(\d+) => ").Cast<Match>().Select(m => int.Parse(m.Groups[1].Value)).ToList();
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
