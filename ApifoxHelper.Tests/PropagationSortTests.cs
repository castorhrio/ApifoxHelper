using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 生成文档核对条目清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零三轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）
/// 与注释缺口清单（第一百零二轮）点击列头即按该列排序，而生成文档核对的条目清单
/// （位置、标注、源码、XML、Swagger 五列）点击列头没有任何反应——这一屏存在的理由正是
/// 「注释有没有真的写进源码、出现在 XML、被 Swagger 读到」，「把 XML 未收录的那几条集中看」只能逐行扫，
/// 属同壳不同行为。排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，
/// 「这一列按什么排」收进 <see cref="PropagationSort"/> 单一口径（与单元格取值同源），
/// 界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// </summary>
public class PropagationSortTests
{
    /// <summary>一行在屏幕上被读作的那五格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(string Display, string Tag, string Source, string Xml, string Swagger);

    private static IComparable? Key(Row row, int column) =>
        PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheFiveCells()
    {
        var row = new Row("OrderService.GetAsync", "参数 id", "已写入", "未收录", "未出现");
        // 位置列：与首格同一份取值（entry.Display）。
        Assert.Equal("OrderService.GetAsync", PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, 0));
        // 标注列：与第二格同一份（PropagationEntries.TagText，参数另附参数名）。
        Assert.Equal("参数 id", PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, 1));
        // 源码列：与第三格同一份中文映射（TaskDisplay.PropagationSourceText）。
        Assert.Equal("已写入", PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, 2));
        // XML 列：与第四格同一份（TaskDisplay.PropagationXmlText）。
        Assert.Equal("未收录", PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, 3));
        // Swagger 列：与第五格同一份（TaskDisplay.PropagationSwaggerText）。
        Assert.Equal("未出现", PropagationSort.Key(row.Display, row.Tag, row.Source, row.Xml, row.Swagger, 4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(PropagationSort.Key("OrderService.GetAsync", "说明", "已写入", "未收录", "未出现", column));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByXmlStateGroupsTheSameStatesTogether()
    {
        // XML 列按状态格文字排：同一状态的行聚在一起（「把 XML 未收录的那几条集中看」）。
        var rows = new[]
        {
            new Row("A", "说明", "已写入", "已收录", "已出现"),
            new Row("B", "说明", "已写入", "未收录", "未出现"),
            new Row("C", "说明", "已写入", "未生成", "未出现"),
            new Row("D", "说明", "已写入", "未收录", "已出现"),
        };
        var ordered = ListSort.Order(rows, 3, false, Key).Select(r => r.Xml).ToList();
        // 同一状态的行相邻，且升序与降序互为镜像。
        Assert.Equal(ordered.OrderBy(xml => xml, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 3, true, Key).Select(r => r.Xml));
    }

    [Fact]
    public void Ordering_BySourceStateGroupsTheSameStatesTogether()
    {
        var rows = new[]
        {
            new Row("A", "说明", "已写入", "已收录", "已出现"),
            new Row("B", "说明", "未写入", "未收录", "未出现"),
            new Row("C", "说明", "已写入", "未收录", "未出现"),
            new Row("D", "说明", "符号不存在", "未收录", "未出现"),
        };
        var ordered = ListSort.Order(rows, 2, false, Key).Select(r => r.Source).ToList();
        Assert.Equal(ordered.OrderBy(source => source, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 2, true, Key).Select(r => r.Source));
    }

    [Fact]
    public void Ordering_ByTagGroupsTheSameTagsTogether()
    {
        // 标注列：同一标注（说明/参数/返回）的行聚在一起。
        var rows = new[]
        {
            new Row("A", "说明", "已写入", "已收录", "已出现"),
            new Row("B", "参数 id", "已写入", "已收录", "已出现"),
            new Row("C", "说明", "已写入", "已收录", "已出现"),
            new Row("D", "返回", "已写入", "已收录", "已出现"),
        };
        var ordered = ListSort.Order(rows, 1, false, Key).Select(r => r.Tag).ToList();
        Assert.Equal(ordered.OrderBy(tag => tag, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 1, true, Key).Select(r => r.Tag));
    }

    [Fact]
    public void Ordering_ByDisplayUsesTheNaturalOrder()
    {
        // 位置列常带序号（同一类型的第 2 个与第 10 个方法）：数字段按数值排，与资源管理器名称列同序。
        var rows = new[]
        {
            new Row("OrderService.Handle10", "说明", "已写入", "已收录", "已出现"),
            new Row("OrderService.Handle2", "说明", "已写入", "已收录", "已出现"),
        };
        Assert.Equal(["OrderService.Handle2", "OrderService.Handle10"], ListSort.Order(rows, 0, false, Key).Select(r => r.Display));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同值的行打乱重排）。
        var rows = new[]
        {
            new Row("A", "说明", "已写入", "未收录", "未出现"),
            new Row("B", "说明", "已写入", "未收录", "未出现"),
            new Row("C", "说明", "已写入", "未收录", "未出现"),
        };
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 3, false, Key).Select(r => r.Display));
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 3, true, Key).Select(r => r.Display));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另七处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（位置、标注、源码、XML、Swagger 五格）。
        var rows = new[]
        {
            new Row("OrderService.GetAsync", "说明", "已写入", "未收录", "未出现"),
            new Row("PingService.Ping", "返回", "已写入", "已收录", "已出现"),
            new Row("OrderService.ListAsync", "说明", "已写入", "未收录", "未出现"),
        };
        var visible = ListFilter.Apply(rows, "未收录", r => [r.Display, r.Tag, r.Source, r.Xml, r.Swagger]);
        Assert.Equal(["OrderService.GetAsync", "OrderService.ListAsync"], visible.Select(r => r.Display));
        // 排序只在筛出来的行里发生：另一行不在结果里。
        Assert.Equal(["OrderService.ListAsync", "OrderService.GetAsync"], ListSort.Order(visible, 0, true, Key).Select(r => r.Display));
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
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
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "protected override bool ProcessDialogKey");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", populate);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_entries, _keyword\.Text, entry => PropagationEntries\.KeywordFields\($", populate);
        Assert.Contains("_sortColumn, _sortDescending,", populate, StringComparison.Ordinal);
        Assert.Contains("(entry, column) => PropagationSort.Key(", populate, StringComparison.Ordinal);
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
        var dialog = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "PropagationSort.cs");
        Assert.Equal(5, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 5), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "PropagationSort\\.Key").Cast<Match>());
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemoryKeys 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SortingDoesNotTouchTheCountsLine()
    {
        // 计数行按整份核对数据统计、不随关键词变化（「XML 0」正是要找的那批的凭据）——
        // 排序同样不碰它：重建里写一次就等于让排序把聚合数字改掉。
        var populate = Slice(Repo.Source("Desktop", "Native", "PropagationDialog.cs"), "private void PopulateList(", "protected override bool ProcessDialogKey");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("CountsLine", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByTheEntryItselfAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按条目自身恢复（重建前记下、重建后找回），详情随选中事件跟上。
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "protected override bool ProcessDialogKey");
        var clear = populate.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = populate.IndexOf("SelectEntry(keep.Display)", StringComparison.Ordinal);
        Assert.True(clear >= 0 && restore > clear, "选中要在重建之后按条目自身恢复。");
        // 恢复仍经唯一入口（SelectEntry 里是 Ui.SelectItem），不就地设 Selected。
        Assert.DoesNotContain(".Selected = true", populate, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("_tasks.", sortBy, StringComparison.Ordinal);
        // 排序后重建把当前这一条交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首。
        Assert.Matches(@"(?m)^\s*PopulateList\(Current\(\)\);$", sortBy);
        // 恢复入口本身仍经唯一入口，不自己写选中。
        var select = Slice(source, "private bool SelectEntry(", "private void ShowHint(");
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);$", select);
        Assert.DoesNotContain(".Selected = true", select, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\PropagationSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsThePropagationSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("生成文档核对条目清单的列排序", section);
        Assert.Contains("`PropagationSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("生成文档核对条目清单的列排序（§6）", ledger);
        Assert.Contains("PropagationSort", ledger);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = Repo.DesignSection(8)
            .Split('\n')
            .SingleOrDefault(l => l.StartsWith("| 生成文档核对条目清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.True(row is not null, "DESIGN.md §8 缺少本轮台账行。");
        Assert.Contains("第一百零三轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改核对口径、不改计数行文案、不写任务数据。
        Assert.Contains("不改核对口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已接入筛选下拉", row, StringComparison.Ordinal);
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
