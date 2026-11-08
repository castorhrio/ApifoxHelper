using System.Text.RegularExpressions;
using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 补充说明接口清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零七轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）、
/// 注释回写的文件清单（第一百零五轮）与同步预览的两张清单（第一百零六轮）点击列头即按该列排序，
/// 而补充说明的接口清单（接口、名称、补充三列）点击列头没有任何反应——这一屏的清单是整份检查结果的接口，
/// 「把已经补过的那几个集中看」「按名称排」「把还没补的挑出来」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="SupplementSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// </summary>
public class SupplementSortTests
{
    /// <summary>一行在屏幕上被读作的那三格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Endpoint, string Name, int Entries);

    private static IComparable? Key(Row row, int column) => SupplementSort.Key(row.Endpoint, row.Name, row.Entries, column);

    private static EndpointCheck Check(string method, string path, string? name = null) =>
        new() { Key = method + " " + path, Method = method, Path = path, RemoteName = name };

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheThreeCells()
    {
        var row = new Row("GET /api/orders", "订单列表", 3);
        // 接口列：与首格同一份拼法（SupplementEntries.EndpointCell）。
        Assert.Equal("GET /api/orders", SupplementSort.Key(row.Endpoint, row.Name, row.Entries, 0));
        // 名称列：与第二格同一份（SupplementEntries.NameCell）。
        Assert.Equal("订单列表", SupplementSort.Key(row.Endpoint, row.Name, row.Entries, 1));
        // 补充列：条目数本身（数值），不是那一格的「N 处」文字。
        Assert.Equal(3, SupplementSort.Key(row.Endpoint, row.Name, row.Entries, 2));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(7)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(SupplementSort.Key("GET /api/orders", "订单列表", 2, column));
    }

    [Fact]
    public void CellText_ComesFromTheSharedEntry()
    {
        // 单元格文字与排序键同源：三格都由 SupplementEntries 给出，对话框不就地拼。
        var check = Check("GET", "/api/orders", "订单列表");
        Assert.Equal("GET /api/orders", SupplementEntries.EndpointCell(check));
        Assert.Equal("订单列表", SupplementEntries.NameCell(check));
        // 远端没有这个接口时名称格是空串（排序键也交空串，升序排在最前）。
        var sourceOnly = Check("POST", "/api/ping");
        Assert.Equal("", SupplementEntries.NameCell(sourceOnly));
        // 关键词匹配的两格与清单首两格同一份（第七十六轮的口径不因本轮改动而漂移）。
        Assert.Equal([SupplementEntries.EndpointCell(check), SupplementEntries.NameCell(check)], SupplementEntries.KeywordFields(check));
        // 补充格由同一个数派生：排序按数值、显示带单位。
        var store = new SupplementStore
        {
            Entries =
            [
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "operation", Field = "name", Value = "订单列表" },
                new SupplementEntry { ResourceKey = "GET /api/orders", Location = "operation", Field = "description", Value = "查询订单" },
            ],
        };
        Assert.Equal(2, SupplementEntries.EndpointEntryCount(store, "GET /api/orders"));
        Assert.Equal("2 处", SupplementEntries.EntryCountCell(store, "GET /api/orders"));
        Assert.Equal("0 处", SupplementEntries.EntryCountCell(store, "POST /api/ping"));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByEntriesUsesNumericOrder()
    {
        // 补充列按数值排（与两个主页面的数量列同一口径）：按文字排会把「10 处」排到「2 处」前。
        var rows = new[]
        {
            new Row("GET /api/a", "甲", 10),
            new Row("GET /api/b", "乙", 2),
        };
        Assert.Equal(["GET /api/b", "GET /api/a"], ListSort.Order(rows, 2, false, Key).Select(r => r.Endpoint));
        Assert.Equal(["GET /api/a", "GET /api/b"], ListSort.Order(rows, 2, true, Key).Select(r => r.Endpoint));
    }

    [Fact]
    public void Ordering_ByEntriesGroupsTheAlreadySupplementedRows()
    {
        // 升序把「还没补的」（0 处）排在最前，降序把补得最多的排在最前——两个方向各自聚同值。
        var rows = new[]
        {
            new Row("GET /api/a", "甲", 2),
            new Row("GET /api/b", "乙", 0),
            new Row("GET /api/c", "丙", 2),
            new Row("GET /api/d", "丁", 0),
        };
        Assert.Equal([0, 0, 2, 2], ListSort.Order(rows, 2, false, Key).Select(r => r.Entries));
        Assert.Equal([2, 2, 0, 0], ListSort.Order(rows, 2, true, Key).Select(r => r.Entries));
    }

    [Fact]
    public void Ordering_ByNamePutsTheSourceOnlyRowsFirst()
    {
        // 名称列按那一格的文字排：远端没有这个接口的行那一格是空串，升序排在最前
        // （「源里新增、远端还没有」正是这一屏要补的那批），有名称的各自聚在一起。
        var rows = new[]
        {
            new Row("GET /api/a", "订单列表", 0),
            new Row("GET /api/b", "", 0),
            new Row("GET /api/c", "用户详情", 0),
        };
        var ordered = ListSort.Order(rows, 1, false, Key).Select(r => r.Name).ToList();
        Assert.Equal("", ordered[0]);
        Assert.Equal(ordered.OrderBy(text => text, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 1, true, Key).Select(r => r.Name));
    }

    [Fact]
    public void Ordering_ByEndpointUsesTheNaturalOrder()
    {
        // 接口列常带序号（同一资源下第 2 个与第 10 个接口）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("GET /api/orders10", "甲", 0),
            new Row("GET /api/orders2", "乙", 0),
        };
        Assert.Equal(["GET /api/orders2", "GET /api/orders10"], ListSort.Order(rows, 0, false, Key).Select(r => r.Endpoint));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("GET /api/a", "甲", 1),
            new Row("GET /api/b", "甲", 1),
            new Row("GET /api/c", "甲", 1),
        };
        Assert.Equal(["GET /api/a", "GET /api/b", "GET /api/c"], ListSort.Order(rows, 1, false, Key).Select(r => r.Endpoint));
        Assert.Equal(["GET /api/a", "GET /api/b", "GET /api/c"], ListSort.Order(rows, 1, true, Key).Select(r => r.Endpoint));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另十一处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（接口身份 + 名称两格）。
        var rows = new[]
        {
            new Row("GET /api/orders", "订单列表", 3),
            new Row("POST /api/ping", "", 0),
            new Row("GET /api/orders/{id}", "订单详情", 1),
        };
        var visible = ListFilter.Apply(rows, "orders", r => [r.Endpoint, r.Name]);
        Assert.Equal(["GET /api/orders", "GET /api/orders/{id}"], visible.Select(r => r.Endpoint));
        // 排序只在筛出来的行里发生：按补充数升序，另一行不在结果里。
        Assert.Equal(["GET /api/orders/{id}", "GET /api/orders"], ListSort.Order(visible, 2, false, Key).Select(r => r.Endpoint));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
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
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var apply = Slice(source, "private void PopulateList(", "private void ShowFilteredEmpty(");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", apply);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_result!\.Endpoints, _keyword\.Text, SupplementEntries\.KeywordFields\),$", apply);
        Assert.Contains("_sortColumn, _sortDescending,", apply, StringComparison.Ordinal);
        Assert.Contains("(check, column) => SupplementSort.Key(", apply, StringComparison.Ordinal);
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
        var dialog = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "SupplementSort.cs");
        Assert.Equal(3, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 3), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "SupplementSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByEndpointKeyAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按接口身份恢复（重建前记下、重建后找回），编辑位随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "SupplementsDialog.cs");
        var apply = Slice(source, "private void PopulateList(", "private void ShowFilteredEmpty(");
        var remember = apply.IndexOf("var previousKey = Current()?.Key;", StringComparison.Ordinal);
        var clear = apply.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "接口身份要在清空清单之前取局部。");
        // 恢复仍经唯一入口（Selected + Focused + 定位），不就地设 Selected、不自己滚动。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", apply);
        Assert.DoesNotContain(".Selected = true", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", apply, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.Matches(@"(?m)^\s*PopulateList\(\);$", sortBy);
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", sortBy, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SupplementSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSupplementSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("补充说明接口清单的列排序", section);
        Assert.Contains("`SupplementSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("补充说明接口清单的列排序（§6）", ledger);
        Assert.Contains("SupplementSort", ledger);
        // 本轮没做的事不能被写成做了。
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 补充说明接口清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.Contains("第一百零七轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.Contains("不改保存切片", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已真机验证", row, StringComparison.Ordinal);
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
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
