using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 差异确认清单的列排序断言（DESIGN.md §6「列表列排序」，第九十九轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）与接口追踪明细（第九十八轮）
/// 点击列头即按该列排序，而差异确认的条目清单（类型、资源、位置、状态四列）点击列头没有任何反应——
/// 这一屏与检查结果是同一只壳（关键词框 + 分类筛选 + 清单 + 详情区 + 动作行），
/// 条目按「接口 × 位置」展开，「把待处理的集中看」「按资源排」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="DecisionSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class DecisionSortTests
{
    /// <summary>一行在屏幕上被读作的那四格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Kind, string Resource, string Location, string State);

    private static IComparable? Key(Row row, int column) => DecisionSort.Key(row.Kind, row.Resource, row.Location, row.State, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFourCells()
    {
        var row = new Row("高风险变化", "GET /orders/{id}", "query.page", "待确认");
        // 类型列：与首格同一份中文映射（KindText 的四个取值之一）。
        Assert.Equal("高风险变化", DecisionSort.Key(row.Kind, row.Resource, row.Location, row.State, 0));
        // 资源列：与第二格同一份原文（Ctrl+C 交出去的也是这一格）。
        Assert.Equal("GET /orders/{id}", DecisionSort.Key(row.Kind, row.Resource, row.Location, row.State, 1));
        // 位置列：与第三格同一份原文。
        Assert.Equal("query.page", DecisionSort.Key(row.Kind, row.Resource, row.Location, row.State, 2));
        // 状态列：与第四格同一份文字（含「待确认」「已确认」「使用目标」等，不另算一套）。
        Assert.Equal("待确认", DecisionSort.Key(row.Kind, row.Resource, row.Location, row.State, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(DecisionSort.Key("说明冲突", "GET /x", "summary", "待选择", column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByStateGroupsTheSameStatesTogether()
    {
        // 状态列按状态格文字排：同一状态的行聚在一起（「把待处理的集中看」），
        // 文字比较取当前区域（与 ListSort 的自然序对纯文字段同一结果）。
        var rows = new[]
        {
            new Row("高风险变化", "GET /a", "query.a", "已确认"),
            new Row("说明冲突", "GET /b", "summary", "待选择"),
            new Row("模型绑定", "Order", "待绑定", "待绑定"),
            new Row("高风险变化", "GET /c", "query.c", "待确认"),
        };
        var expected = rows.OrderBy(r => r.State, StringComparer.CurrentCulture).Select(r => r.Resource).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 3, false, Key).Select(r => r.Resource));
        // 反向是升序的镜像（键互不相同，故不涉及同值稳定性）。
        Assert.Equal(Enumerable.Reverse(expected), ListSort.Order(rows, 3, true, Key).Select(r => r.Resource));
    }

    [Fact]
    public void Ordering_ByKindGroupsTheSameKindsTogether()
    {
        var rows = new[]
        {
            new Row("共享模型确认", "Order", "共享模型", "待确认"),
            new Row("高风险变化", "GET /a", "query.a", "待确认"),
            new Row("说明冲突", "GET /b", "summary", "待选择"),
            new Row("高风险变化", "GET /c", "query.c", "待确认"),
        };
        var ordered = ListSort.Order(rows, 0, false, Key).Select(r => r.Kind).ToList();
        // 同一类型的行聚在一起（相邻），且升序与降序互为镜像。
        Assert.Equal(ordered.OrderBy(k => k, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 0, true, Key).Select(r => r.Kind));
    }

    [Fact]
    public void Ordering_ByResourceUsesTheNaturalOrder()
    {
        // 资源列常带序号（「GET /orders/2」与「GET /orders/10」）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("高风险变化", "GET /orders/10", "query.a", "待确认"),
            new Row("高风险变化", "GET /orders/2", "query.b", "待确认"),
        };
        Assert.Equal(["GET /orders/2", "GET /orders/10"], ListSort.Order(rows, 1, false, Key).Select(r => r.Resource));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("高风险变化", "GET /a", "query.a", "待确认"),
            new Row("高风险变化", "GET /b", "query.b", "待确认"),
            new Row("高风险变化", "GET /c", "query.c", "待确认"),
        };
        Assert.Equal(["GET /a", "GET /b", "GET /c"], ListSort.Order(rows, 3, false, Key).Select(r => r.Resource));
        Assert.Equal(["GET /a", "GET /b", "GET /c"], ListSort.Order(rows, 3, true, Key).Select(r => r.Resource));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与两个主页面、检查结果、接口追踪明细同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（资源、位置、类型三格），故这里也只用这三格找行。
        var rows = new[]
        {
            new Row("高风险变化", "GET /a", "query.a", "待确认"),
            new Row("说明冲突", "GET /b", "summary", "待选择"),
            new Row("说明冲突", "GET /c", "summary", "待选择"),
        };
        var conflicts = ListFilter.Apply(rows, "说明冲突", r => [r.Resource, r.Location, r.Kind]);
        Assert.Equal(["GET /b", "GET /c"], conflicts.Select(r => r.Resource));
        // 排序只在筛出来的两行里发生：/a 不在结果里。
        var ordered = ListSort.Order(conflicts, 1, true, Key);
        Assert.Equal(["GET /c", "GET /b"], ordered.Select(r => r.Resource));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
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
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        var apply = Slice(source, "private void ApplyFilter(", "private Entry? Current()");
        // 筛选先于排序：Matches 在 ListSort.Order 之内（同一表达式），排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", apply);
        Assert.Matches(@"(?m)^\s*_entries\.Where\(Matches\)\.ToList\(\),$", apply);
        Assert.Contains("_sortColumn, _sortDescending,", apply, StringComparison.Ordinal);
        Assert.Contains("(entry, column) => DecisionSort.Key(KindText(entry.Kind), entry.Resource, entry.Location, entry.StateText, column));", apply, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        Assert.Matches(@"(?m)^\s*_list\.EndUpdate\(\);\s*\n\s*RefreshSortHeaders\(\);", apply);
        var refresh = apply.IndexOf("RefreshSortHeaders();", StringComparison.Ordinal);
        var emptyBranch = apply.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && emptyBranch > refresh, "表头标记要在筛到零行那一支之前刷新（那一支先返回）。");
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "DecisionSort.cs");
        Assert.Equal(4, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 4), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "DecisionSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByTheEntryItselfAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按条目自身恢复（重建前记下、重建后找回），详情与动作随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        var apply = Slice(source, "private void ApplyFilter(", "private Entry? Current()");
        var clear = apply.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = apply.IndexOf("Ui.SelectItem(item);", StringComparison.Ordinal);
        Assert.True(clear >= 0 && restore > clear, "选中要在重建之后经唯一入口恢复。");
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", sortBy, StringComparison.Ordinal);
        // 排序后重建把当前条目交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首。
        Assert.Matches(@"(?m)^\s*ApplyFilter\(Current\(\)\);$", sortBy);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\DecisionSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheDecisionsSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("差异确认清单的列排序", section);
        Assert.Contains("`DecisionSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("差异确认清单的列排序（§6）", ledger);
        Assert.Contains("DecisionSort", ledger);
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
