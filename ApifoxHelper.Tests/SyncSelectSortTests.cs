using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览勾选页接口清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零九轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）、
/// 注释回写的两张清单（第一百零五、一百零八轮）、同步预览预览页的两张清单（第一百零六轮）与
/// 补充说明的接口清单（第一百零七轮）点击列头即按该列排序，
/// 而同步预览勾选页的接口清单（接口、名称、状态、待确认四列）点击列头没有任何反应——
/// 这一页是「生成计划前选哪些接口」的唯一入口，清单是整份检查结果的接口，
/// 「把不可同步的挑出来」「按状态排」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进
/// <see cref="SyncSelectSort"/> 单一口径（与单元格取值同源），界面接线由行首正则与源码切片
/// 机械检查钉住（被注释掉也算删除）。
/// 这一页与另十二处有一处不同：清单是复选清单，勾选状态按接口键记在 <see cref="SyncSelect"/>，
/// 排序重建清单时从它恢复——排序不丢勾选，由本文件在 Linux 上真执行钉住。
/// </summary>
public class SyncSelectSortTests
{
    private static EndpointCheck Check(
        string method,
        string path,
        string status = "structure",
        string? name = null,
        bool inRemote = true) =>
        new()
        {
            Key = method + " " + path,
            Method = method,
            Path = path,
            Status = status,
            RemoteName = name,
            InRemote = inRemote,
        };

    private static CheckResult Diff(params EndpointCheck[] endpoints) => new() { Endpoints = [.. endpoints] };

    private static List<string> OrderedKeys(IReadOnlyList<EndpointCheck> rows, int column, bool descending) =>
        ListSort.Order(rows, column, descending, (check, col) => SyncSelectSort.Key(check, 0, col))
            .Select(check => check.Key)
            .ToList();

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFourCells()
    {
        var check = Check("GET", "/api/orders", "structure", "订单列表");
        // 接口列：与首格同一份拼法（SyncSelectSort.EndpointCell）。
        Assert.Equal("GET /api/orders", SyncSelectSort.Key(check, 2, 0));
        // 名称列：远端名称原文（与第二格同一份取值）。
        Assert.Equal("订单列表", SyncSelectSort.Key(check, 2, 1));
        // 状态列：与第三格同一份中文映射。
        Assert.Equal(TaskDisplay.CheckStatusText("structure"), SyncSelectSort.Key(check, 2, 2));
        // 待确认列：计数本身（数值），不是那一格的「N」文字或「—」占位。
        Assert.Equal(2, SyncSelectSort.Key(check, 2, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(SyncSelectSort.Key(Check("GET", "/api/orders"), 1, column));
    }

    [Fact]
    public void CellText_ComesFromTheSharedEntry()
    {
        // 四格文字都由 SyncSelectSort 给出，对话框不就地拼（显示与排序同一份取值）。
        var check = Check("GET", "/api/orders", "structure", "订单列表");
        Assert.Equal("GET /api/orders", SyncSelectSort.EndpointCell(check));
        Assert.Equal("订单列表", SyncSelectSort.NameCell(check));
        Assert.Equal(TaskDisplay.CheckStatusText("structure"), SyncSelectSort.StatusCell(check));
        Assert.Equal("3", SyncSelectSort.PendingCell(3));
        Assert.Equal("—", SyncSelectSort.PendingCell(0));
        // 名称格的两种占位：远端有这个接口但没有名称 / 远端没有这个接口。
        Assert.Equal("—", SyncSelectSort.NameCell(Check("GET", "/api/a", inRemote: true)));
        Assert.Equal("Apifox 中不存在", SyncSelectSort.NameCell(Check("GET", "/api/b", inRemote: false)));
        // 关键词匹配的两格与清单首格、第三格同一份（第七十二轮的口径不因本轮改动而漂移）；
        // 名称格取原文，占位文字不进关键词（那是显示用的兜底，不是内容）。
        Assert.Equal(
            [SyncSelectSort.EndpointCell(check), "订单列表", SyncSelectSort.StatusCell(check)],
            SyncSelect.SearchFields(check));
        Assert.Equal("", SyncSelect.SearchFields(Check("GET", "/api/c", inRemote: false))[1]);
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByPendingUsesNumericOrder()
    {
        // 待确认列按数值排（与两个主页面的数量列同一口径）：按文字排会把「10」排到「2」前。
        var rows = new[]
        {
            Check("GET", "/api/a"),
            Check("GET", "/api/b"),
        };
        var keys = ListSort.Order(rows, 3, false, (check, column) => SyncSelectSort.Key(check, check.Path.EndsWith('a') ? 10 : 2, column))
            .Select(check => check.Key)
            .ToList();
        Assert.Equal(["GET /api/b", "GET /api/a"], keys);
        var reversed = ListSort.Order(rows, 3, true, (check, column) => SyncSelectSort.Key(check, check.Path.EndsWith('a') ? 10 : 2, column))
            .Select(check => check.Key)
            .ToList();
        Assert.Equal(["GET /api/a", "GET /api/b"], reversed);
    }

    [Fact]
    public void Ordering_ByPendingGroupsTheRowsWithoutPendingItems()
    {
        // 升序把「没有待确认项」（那一格是「—」，按 0 参与）排在最前，降序把待确认最多的排在最前——
        // 两个方向各自聚同值，正是「把还有待确认项的集中看」这一步。
        var rows = new[]
        {
            Check("GET", "/api/a"),
            Check("GET", "/api/b"),
            Check("GET", "/api/c"),
            Check("GET", "/api/d"),
        };
        var pending = new Dictionary<string, int>
        {
            ["GET /api/a"] = 2,
            ["GET /api/b"] = 0,
            ["GET /api/c"] = 2,
            ["GET /api/d"] = 0,
        };
        var ascending = ListSort.Order(rows, 3, false, (check, column) => SyncSelectSort.Key(check, pending[check.Key], column))
            .Select(check => pending[check.Key]).ToList();
        Assert.Equal([0, 0, 2, 2], ascending);
        var descending = ListSort.Order(rows, 3, true, (check, column) => SyncSelectSort.Key(check, pending[check.Key], column))
            .Select(check => pending[check.Key]).ToList();
        Assert.Equal([2, 2, 0, 0], descending);
    }

    [Fact]
    public void Ordering_ByNamePutsTheNamelessRowsFirst()
    {
        // 名称列取远端名称原文：没有名称的行交 null 视为最小（升序排在最前即「远端还没有名字」的那批），
        // 不按占位字「—」「Apifox 中不存在」的码位排。
        var rows = new[]
        {
            Check("GET", "/api/a", name: "orders"),
            Check("GET", "/api/b", name: null, inRemote: true),
            Check("GET", "/api/c", name: "users"),
            Check("GET", "/api/d", name: null, inRemote: false),
        };
        var ascending = OrderedKeys(rows, 1, false);
        Assert.Equal(["GET /api/b", "GET /api/d"], ascending.Take(2));
        Assert.Equal(["GET /api/a", "GET /api/c"], ascending.Skip(2));
        // 降序把有名称的排在最前，无名称的落到末尾——两个方向各自聚同值，
        // 同值行仍按数据源顺序（反向不把同值的行打乱重排）。
        var descending = OrderedKeys(rows, 1, true);
        Assert.Equal(["GET /api/c", "GET /api/a"], descending.Take(2));
        Assert.Equal(["GET /api/b", "GET /api/d"], descending.Skip(2));
    }

    [Fact]
    public void Ordering_ByStatusGroupsTheSameStatus()
    {
        // 状态列按那一格的中文映射排：同一状态的接口聚在一起（「把不可同步的挑出来」这一步）。
        var rows = new[]
        {
            Check("GET", "/api/a", "structure"),
            Check("GET", "/api/b", "blocked"),
            Check("GET", "/api/c", "structure"),
            Check("GET", "/api/d", "blocked"),
        };
        var ascending = ListSort.Order(rows, 2, false, (check, column) => SyncSelectSort.Key(check, 0, column))
            .Select(check => check.Status).ToList();
        // 同值聚在一起（两个状态各占两行），且两个方向互为镜像——具体哪个状态在前由中文映射的
        // 区域比较决定，本环境不假设它的次序，只钉「聚同值」与「方向相反」这两条不变量。
        Assert.Equal(ascending[0], ascending[1]);
        Assert.Equal(ascending[2], ascending[3]);
        Assert.NotEqual(ascending[1], ascending[2]);
        var descending = ListSort.Order(rows, 2, true, (check, column) => SyncSelectSort.Key(check, 0, column))
            .Select(check => check.Status).ToList();
        Assert.Equal(ascending[0], descending[3]);
        Assert.Equal(ascending[3], descending[0]);
    }

    [Fact]
    public void Ordering_ByEndpointUsesTheNaturalOrder()
    {
        // 接口列常带序号（同一资源下第 2 个与第 10 个接口）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            Check("GET", "/api/orders10"),
            Check("GET", "/api/orders2"),
        };
        Assert.Equal(["GET /api/orders2", "GET /api/orders10"], OrderedKeys(rows, 0, false));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            Check("GET", "/api/a", "structure"),
            Check("GET", "/api/b", "structure"),
            Check("GET", "/api/c", "structure"),
        };
        Assert.Equal(["GET /api/a", "GET /api/b", "GET /api/c"], OrderedKeys(rows, 2, false));
        Assert.Equal(["GET /api/a", "GET /api/b", "GET /api/c"], OrderedKeys(rows, 2, true));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另十二处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（接口身份 + 名称 + 状态文字三格）。
        var diff = Diff(
            Check("GET", "/api/orders", "structure", "订单列表"),
            Check("POST", "/api/ping", "new", null, inRemote: false),
            Check("GET", "/api/orders/{id}", "blocked", "订单详情"));
        var select = new SyncSelect { Query = "orders" };
        var visible = select.Visible(diff);
        Assert.Equal(["GET /api/orders", "GET /api/orders/{id}"], visible.Select(check => check.Key));
        // 排序只在筛出来的行里发生：按接口列降序，另一行（POST /api/ping）不在结果里。
        Assert.Equal(["GET /api/orders/{id}", "GET /api/orders"], OrderedKeys(visible, 0, true));
    }

    [Fact]
    public void SortingDoesNotLoseTheChecks()
    {
        // 这一页的清单是复选清单：排序要重建清单，勾选状态按接口键记在 SyncSelect（单一来源），
        // 重建时从它恢复——勾了 12 个、排一下只剩 3 个，点「生成同步计划」写出去的就是 3 个。
        var diff = Diff(
            Check("GET", "/api/a", "structure", "alpha"),
            Check("GET", "/api/b", "blocked", "beta"),
            Check("GET", "/api/c", "structure", "gamma"));
        var select = new SyncSelect();
        select.Set("GET /api/a", true);
        select.Set("GET /api/c", true);
        var before = select.CheckedKeys(diff);
        // 按名称降序重建（与对话框 PopulateSelectList 同一份顺序：筛选 → 排序 → 按接口键恢复勾选）。
        var ordered = ListSort.Order(select.Visible(diff), 1, true, (check, column) => SyncSelectSort.Key(check, 0, column));
        Assert.Equal(["GET /api/c", "GET /api/b", "GET /api/a"], ordered.Select(check => check.Key));
        Assert.Equal(before, select.CheckedKeys(diff));
        Assert.Equal([true, false, true], ordered.Select(check => select.IsChecked(check.Key)));
        Assert.Equal(2, select.CheckedCount);
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Matches(@"(?m)^\s*private int _selectSortColumn = ListSort\.None;$", source);
        Assert.Matches(@"(?m)^\s*private bool _selectSortDescending;$", source);
        Assert.Matches(@"(?m)^\s*_selectTitles = _selectList\.Columns\.Cast<ColumnHeader>\(\)\.Select\(column => column\.Text\)\.ToArray\(\);", source);
        Assert.Matches(@"(?m)^\s*_selectList\.ColumnClick \+= \(_, e\) => SortSelect\(e\.Column\);", source);
        // 方向翻转只在共享口径一处（对话框不自己写 !descending）。
        Assert.Matches(@"(?m)^\s*\(_selectSortColumn, _selectSortDescending\) = ListSort\.Toggle\(_selectSortColumn, _selectSortDescending, column\);", source);
        Assert.DoesNotContain("_selectSortDescending = !", source, StringComparison.Ordinal);
        // 表头标记按原标题重算（不叠加、不就地拼方向字面）。
        Assert.Matches(@"(?m)^\s*_selectList\.Columns\[i\]\.Text = ListSort\.Header\(_selectTitles\[i\], i, _selectSortColumn, _selectSortDescending\);", source);
        Assert.DoesNotContain("▲", source, StringComparison.Ordinal);
        Assert.DoesNotContain("▼", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_AppliesSortAfterFilterAndRefreshesHeadersAfterRebuild()
    {
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var populate = Slice(source, "private void PopulateSelectList(", "private void SortSelect(");
        // 筛选先于排序：SyncSelect.Visible（内部即 ListFilter.Apply）的同一表达式作为 ListSort.Order 的输入，
        // 排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", populate);
        Assert.Matches(@"(?m)^\s*_select\.Visible\(diff\),$", populate);
        Assert.Contains("_selectSortColumn, _selectSortDescending,", populate, StringComparison.Ordinal);
        Assert.Contains("(check, column) => SyncSelectSort.Key(", populate, StringComparison.Ordinal);
        // 表头标记在清单重建之后刷新（EndUpdate 之后），筛到零行那一支之前。
        Assert.Matches(@"(?m)^\s*_selectList\.EndUpdate\(\);\s*\n\s*_rebuilding = false;\s*\n\s*\}\s*\n\s*RefreshSelectSortHeaders\(\);", populate);
        var refresh = populate.IndexOf("RefreshSelectSortHeaders();", StringComparison.Ordinal);
        var counts = populate.IndexOf("_selectAllButton.Text =", StringComparison.Ordinal);
        Assert.True(refresh >= 0 && counts > refresh, "表头标记要在按钮与计数刷新之前（那一屏的其余赋值都在其后）。");
    }

    [Fact]
    public void SortArmsCoverEveryColumnExactlyOnce()
    {
        // 排序臂与建列一一对应：漏一列则该列点击后顺序不变（表头有标记、内容没排），
        // 多一臂或索引重复都是与界面脱节的死码。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "SyncSelectSort.cs");
        var selectColumns = Slice(dialog, "private readonly ListView _selectList = Ui.List(true);", "_keyword.PlaceholderText");
        Assert.Equal(4, Regex.Matches(selectColumns, @"(?m)^\s*_selectList\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 4), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "SyncSelectSort\\.Key").Cast<Match>());
        // 四格文字也只在共享口径一处：对话框不就地拼接口身份、不就地写两种名称占位。
        Assert.DoesNotContain("check.Method + \" \" + check.Path", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Apifox 中不存在", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void ChecksAreRestoredByEndpointKeyAfterSorting()
    {
        // 排序只改呈现顺序：勾选仍按接口键从 SyncSelect 恢复（重建期间不把行上的勾选回写，
        // 否则每行都刷一次计数是 O(n²)），排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        var populate = Slice(source, "private void PopulateSelectList(", "private void SortSelect(");
        Assert.Matches(@"(?m)^\s*row\.Checked = _select\.IsChecked\(check\.Key\);$", populate);
        Assert.Matches(@"(?m)^\s*_rebuilding = true;$", populate);
        Assert.DoesNotContain("_select.Set(", populate, StringComparison.Ordinal);
        var sortSelect = Slice(source, "private void SortSelect(", "private void RefreshSelectSortHeaders(");
        Assert.Matches(@"(?m)^\s*PopulateSelectList\(\);$", sortSelect);
        Assert.DoesNotContain("SelectItem", sortSelect, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortSelect, StringComparison.Ordinal);
        Assert.DoesNotContain("_select.", sortSelect, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", sortSelect, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\SyncSelectSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSelectListSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("同步预览勾选页接口清单的列排序", section);
        Assert.Contains("`SyncSelectSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("同步预览勾选页接口清单的列排序（§6）", ledger);
        Assert.Contains("SyncSelectSort", ledger);
        // 本轮没做的事不能被写成做了。
        var row = ledger.Split('\n').Single(l => l.StartsWith("| 同步预览勾选页接口清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.Contains("第一百零九轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.Contains("不改勾选口径", row, StringComparison.Ordinal);
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
