using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释缺口清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零二轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）与响应契约的契约清单（第一百零一轮）
/// 点击列头即按该列排序，而注释缺口清单（位置、标注、状态、角色、文件:行五列）点击列头没有任何反应——
/// 这一屏是全应用最长的一张清单（一次扫描的全部注释位置），「把缺失的集中看」「按角色排」
/// 「把同一个文件的放一起」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="SlotSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class SlotSortTests
{
    /// <summary>一行在屏幕上被读作的那五格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Name, string Tag, string Status, string Role, string Location);

    private static IComparable? Key(Row row, int column) => SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFiveCells()
    {
        var row = new Row("OrderService.CreateOrder", "参数 orderId", "缺失", "请求", "Services/OrderService.cs:42");
        // 位置列：与首格同一份取值（SlotText.Name）。
        Assert.Equal("OrderService.CreateOrder", SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, 0));
        // 标注列：与第二格同一份（SlotText.Tag，参数另附参数名）。
        Assert.Equal("参数 orderId", SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, 1));
        // 状态列：与第三格同一份中文映射（TaskDisplay.SlotStatusText）。
        Assert.Equal("缺失", SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, 2));
        // 角色列：与第四格同一份（TaskDisplay.SlotRoleText）。
        Assert.Equal("请求", SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, 3));
        // 文件:行列：与第五格同一份（SlotText.Location）。
        Assert.Equal("Services/OrderService.cs:42", SlotSort.Key(row.Name, row.Tag, row.Status, row.Role, row.Location, 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(SlotSort.Key("OrderService.CreateOrder", "说明", "缺失", "接口", "Services/OrderService.cs:42", column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByStatusGroupsTheSameStatusesTogether()
    {
        // 状态列按状态格文字排：同一状态的行聚在一起（「把缺失的集中看」）。
        var rows = new[]
        {
            new Row("A", "说明", "有效", "接口", "A.cs:1"),
            new Row("B", "说明", "缺失", "接口", "B.cs:2"),
            new Row("C", "说明", "已失效", "请求", "C.cs:3"),
            new Row("D", "说明", "空", "响应", "D.cs:4"),
        };
        var expected = rows.OrderBy(r => r.Status, StringComparer.CurrentCulture).Select(r => r.Name).ToList();
        Assert.Equal(expected, ListSort.Order(rows, 2, false, Key).Select(r => r.Name));
        // 反向是升序的镜像（键互不相同，故不涉及同值稳定性）。
        Assert.Equal(Enumerable.Reverse(expected), ListSort.Order(rows, 2, true, Key).Select(r => r.Name));
    }

    [Fact]
    public void Ordering_ByRoleGroupsTheSameRolesTogether()
    {
        var rows = new[]
        {
            new Row("A", "说明", "缺失", "接口", "A.cs:1"),
            new Row("B", "说明", "缺失", "请求", "B.cs:2"),
            new Row("C", "说明", "缺失", "接口", "C.cs:3"),
            new Row("D", "说明", "缺失", "枚举", "D.cs:4"),
        };
        var ordered = ListSort.Order(rows, 3, false, Key).Select(r => r.Role).ToList();
        // 同一角色的行聚在一起（相邻），且升序与降序互为镜像。
        Assert.Equal(ordered.OrderBy(role => role, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 3, true, Key).Select(r => r.Role));
    }

    [Fact]
    public void Ordering_ByLocationUsesTheNaturalOrder()
    {
        // 文件:行列常带行号：数字段按数值排（第 9 行在第 42 行前），与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("A", "说明", "缺失", "接口", "Services/OrderService.cs:42"),
            new Row("B", "说明", "缺失", "接口", "Services/OrderService.cs:9"),
        };
        Assert.Equal(["B", "A"], ListSort.Order(rows, 4, false, Key).Select(r => r.Name));
    }

    [Fact]
    public void Ordering_ByNameUsesTheNaturalOrder()
    {
        // 位置列常带序号（同一类型的第 2 个与第 10 个方法）：数字段按数值排。
        var rows = new[]
        {
            new Row("OrderService.Handle10", "说明", "缺失", "接口", "A.cs:1"),
            new Row("OrderService.Handle2", "说明", "缺失", "接口", "B.cs:2"),
        };
        Assert.Equal(["OrderService.Handle2", "OrderService.Handle10"], ListSort.Order(rows, 0, false, Key).Select(r => r.Name));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("A", "说明", "缺失", "接口", "A.cs:1"),
            new Row("B", "说明", "缺失", "接口", "B.cs:2"),
            new Row("C", "说明", "缺失", "接口", "C.cs:3"),
        };
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 2, false, Key).Select(r => r.Name));
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 2, true, Key).Select(r => r.Name));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与两个主页面、检查结果、接口追踪明细、差异确认、注释候选、响应契约同一顺序）：
        // 筛掉的行不参与排序。关键词字段与对话框同一份（符号、标注、角色、文件位置四格）。
        var rows = new[]
        {
            new Row("OrderService.CreateOrder", "参数 orderId", "缺失", "请求", "Services/OrderService.cs:42"),
            new Row("UserService.GetUser", "说明", "有效", "接口", "Services/UserService.cs:17"),
            new Row("OrderService.Cancel", "说明", "缺失", "接口", "Services/OrderService.cs:88"),
        };
        var orders = ListFilter.Apply(rows, "orderservice", r => [r.Name, r.Tag, r.Role, r.Location]);
        Assert.Equal(["OrderService.CreateOrder", "OrderService.Cancel"], orders.Select(r => r.Name));
        // 排序只在筛出来的行里发生：另一行不在结果里。
        // 降序：自然序里「CreateOrder」在「Cancel」之后，故降序把它排在前。
        Assert.Equal(["OrderService.CreateOrder", "OrderService.Cancel"], ListSort.Order(orders, 0, true, Key).Select(r => r.Name));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
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
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var apply = Slice(source, "private void ApplyFilter(", "private void ShowFilteredEmpty()");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", apply);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_slots, _keyword\.Text, SlotText\.KeywordFields\)\.Where\(Matches\)\.ToList\(\),$", apply);
        Assert.Contains("_sortColumn, _sortDescending,", apply, StringComparison.Ordinal);
        Assert.Contains("(slot, column) => SlotSort.Key(", apply, StringComparison.Ordinal);
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
        var dialog = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "SlotSort.cs");
        Assert.Equal(5, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 5), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "SlotSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByTheSlotItselfAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按注释位置自身恢复（重建前记下、重建后找回），详情与三个动作随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        var apply = Slice(source, "private void ApplyFilter(", "private void ShowFilteredEmpty()");
        var clear = apply.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = apply.IndexOf("SelectSlot(keep.Id)", StringComparison.Ordinal);
        Assert.True(clear >= 0 && restore > clear, "选中要在重建之后按注释位置自身恢复。");
        // 恢复仍经唯一入口（SelectSlot 里是 Ui.SelectItem），不就地设 Selected。
        Assert.DoesNotContain(".Selected = true", apply, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("AddManualAsync", sortBy, StringComparison.Ordinal);
        // 排序后重建把当前这一条交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首。
        Assert.Matches(@"(?m)^\s*ApplyFilter\(Current\(\)\);$", sortBy);
        // 恢复入口本身仍经唯一入口，不自己写选中。
        var select = Slice(source, "private bool SelectSlot(", "private void ShowEvidence(");
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);$", select);
        Assert.DoesNotContain(".Selected = true", select, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SlotSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSlotsSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("注释缺口清单的列排序", section);
        Assert.Contains("`SlotSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("注释缺口清单的列排序（§6）", ledger);
        Assert.Contains("SlotSort", ledger);
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
