using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务日志记录清单的列排序断言（DESIGN.md §6「列表列排序」，第一百零四轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）与生成文档核对的条目清单（第一百零三轮）点击列头即按该列排序，
/// 而任务日志的记录清单（时间、事件、摘要三列）点击列头没有任何反应——这一屏是第八十八轮那五张
/// 只读长清单里唯一还没接上的一张（检查结果、接口追踪明细、生成文档核对已接，代码证据按「条目少」不接），
/// 一份几十个动作的计划就是上百行，「把写入失败的那几条集中看」「按事件排」只能逐行扫，属同壳不同行为。
/// 排序状态与表头标记仍取 <see cref="ListSort"/> 那一份，「这一列按什么排」收进 <see cref="JournalSort"/>
/// 单一口径（与三格取值同源），界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// 口径是真执行：<see cref="JournalEntries"/>、<see cref="JournalSort"/> 与 <see cref="ListSort"/>
/// 经共享编译进测试程序集，排序跑的是主工程同一份代码。
/// </summary>
public class JournalSortTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 9, 30, 5, TimeSpan.FromHours(8));

    /// <summary>一行在屏幕上被读作的那三格（与对话框建行处同一份取值）。</summary>
    private sealed record Row(DateTimeOffset? At, string Event, string Summary);

    private static IComparable? Key(Row row, int column) => JournalSort.Key(row.At, row.Event, row.Summary, column);

    // ---------- 排序键与单元格同源（真执行） ----------

    [Fact]
    public void Key_MirrorsTheThreeCells()
    {
        var line = Node(JournalLines.ActionResultLine(At, "p", "a1", "Order", "verified", null, 7));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cells = JournalEntries.Row(line, ids);
        // 时间列：与时间格同一份解析（时间格就是它格式化出来的）。
        Assert.Equal(JournalEntries.TimeKey(line), JournalSort.Key(JournalEntries.TimeKey(line), cells[1], cells[2], 0));
        // 事件列：与第二格同一份中文映射。
        Assert.Equal(cells[1], JournalSort.Key(JournalEntries.TimeKey(line), cells[1], cells[2], 1));
        // 摘要列：与第三格同一份（含「结果未知」标记）。
        Assert.Equal(cells[2], JournalSort.Key(JournalEntries.TimeKey(line), cells[1], cells[2], 2));
        // 三格与 Row 同源：排序键不另算一套字段。
        Assert.Equal(JournalEntries.EventCell(line), cells[1]);
        Assert.Equal(JournalEntries.SummaryCell(line, ids), cells[2]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(9)]
    public void Key_OutsideTheColumnsIsNull(int column)
    {
        // 列号落在建列之外交 null：不索引不存在的列，也不把邻列的值当成这一列。
        Assert.Null(JournalSort.Key(At, "同步执行：结果", "Order", column));
    }

    [Fact]
    public void TimeText_IsFormattedFromTheSameTimestamp()
    {
        var line = Node(JournalLines.PlanStartLine(At, "p", "0123456789abcdef"));
        // 时间格与时间键同源：显示到秒、按本机时区。
        Assert.Equal(At, JournalEntries.TimeKey(line));
        Assert.Equal(At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), JournalEntries.TimeText(line));
        // 读不出时间（半行、缺字段）时时间键交 null、时间格给占位——不拿当前时间顶上。
        var broken = JsonNode.Parse("""{"kind":"intent"}""")!;
        Assert.Null(JournalEntries.TimeKey(broken));
        Assert.Equal(JournalEntries.MissingTimeText, JournalEntries.TimeText(broken));
    }

    [Fact]
    public void SummaryCell_CarriesTheUnfinishedMarkOnlyForUnfinishedIntents()
    {
        var ids = new HashSet<string>(["a2"], StringComparer.Ordinal);
        var unfinished = Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h"));
        var finished = Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h"));
        // 只有意图、没有结果的那一行在摘要格后缀标记（与那句话说的是同一件事）。
        Assert.EndsWith(JournalEntries.UnfinishedMark, JournalEntries.SummaryCell(unfinished, ids), StringComparison.Ordinal);
        Assert.Equal(JournalEntries.SummaryText(finished), JournalEntries.SummaryCell(finished, ids));
        // 不传集合时（关键词匹配之外的调用）不加标记。
        Assert.Equal(JournalEntries.SummaryText(unfinished), JournalEntries.SummaryCell(unfinished));
    }

    // ---------- 排序结果（真执行：ListSort.Order 跑主工程同一份代码） ----------

    [Fact]
    public void Ordering_ByTimeSortsChronologically()
    {
        // 时间列按时间戳排（不是按显示文字）：升序即时间先后，降序是它的镜像。
        var rows = new[]
        {
            new Row(At.AddMinutes(2), "同步执行：结果", "Order"),
            new Row(At, "同步执行：开始", "计划 p"),
            new Row(At.AddMinutes(1), "同步执行：准备写入", "Order"),
        };
        Assert.Equal(["计划 p", "Order", "Order"], ListSort.Order(rows, 0, false, Key).Select(r => r.Summary));
        Assert.Equal(
            ListSort.Order(rows, 0, false, Key).Select(r => r.Summary),
            Enumerable.Reverse(ListSort.Order(rows, 0, true, Key)).Select(r => r.Summary));
        // 读不出时间的那一行交 null（视为最小）：升序在最前，不按占位字「—」的码位排到中间。
        var withMissing = rows.Append(new Row(null, "未识别事件", "半行")).ToArray();
        Assert.Equal("半行", ListSort.Order(withMissing, 0, false, Key)[0].Summary);
        Assert.Equal("半行", ListSort.Order(withMissing, 0, true, Key)[^1].Summary);
    }

    [Fact]
    public void Ordering_ByEventGroupsTheSameEventsTogether()
    {
        // 事件列按事件名排：同一事件的行聚在一起（「把写入失败的那几条集中看」）。
        var rows = new[]
        {
            new Row(At, "同步执行：结果", "Order"),
            new Row(At, "注释回写：结果", "OrderService.cs"),
            new Row(At, "同步执行：结果", "Customer"),
            new Row(At, "同步执行：开始", "计划 p"),
        };
        var ordered = ListSort.Order(rows, 1, false, Key).Select(r => r.Event).ToList();
        Assert.Equal(ordered.OrderBy(e => e, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 1, true, Key).Select(r => r.Event));
    }

    [Fact]
    public void Ordering_BySummaryGroupsTheSameSummariesTogether()
    {
        // 摘要列按摘要格文字排：同一个接口的几步聚在一起（摘要里带着资源与状态）。
        var rows = new[]
        {
            new Row(At, "同步执行：结果", "Order · 已核对"),
            new Row(At, "同步执行：准备写入", "Customer · 新建"),
            new Row(At, "同步执行：结果", "Order · 写入失败"),
        };
        var ordered = ListSort.Order(rows, 2, false, Key).Select(r => r.Summary).ToList();
        Assert.Equal(ordered.OrderBy(s => s, StringComparer.CurrentCulture), ordered);
        Assert.Equal(Enumerable.Reverse(ordered), ListSort.Order(rows, 2, true, Key).Select(r => r.Summary));
    }

    [Fact]
    public void Ordering_IsStableForEqualKeysInBothDirections()
    {
        // 同值行在两个方向都保持数据源顺序（反向不把同一秒、同一事件的几行打乱重排）。
        var rows = new[]
        {
            new Row(At, "同步执行：结果", "A"),
            new Row(At, "同步执行：结果", "B"),
            new Row(At, "同步执行：结果", "C"),
        };
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 1, false, Key).Select(r => r.Summary));
        Assert.Equal(["A", "B", "C"], ListSort.Order(rows, 1, true, Key).Select(r => r.Summary));
    }

    [Fact]
    public void FilteringComesBeforeSorting()
    {
        // 筛选先于排序（与另八处同一顺序）：筛掉的行不参与排序。
        // 关键词字段与对话框同一份（事件格与摘要格）。
        var log = Log();
        var ids = new HashSet<string>(["a2"], StringComparer.Ordinal);
        var visible = ListFilter.Apply(log, JournalEntries.UnfinishedMark, line => JournalEntries.KeywordFields(line, ids));
        Assert.Single(visible);
        var ordered = ListSort.Order(
            visible, 1, false,
            (line, column) => JournalSort.Key(
                JournalEntries.TimeKey(line),
                JournalEntries.EventCell(line),
                JournalEntries.SummaryCell(line, ids),
                column));
        // 排序只在筛出来的行里发生：另一行不在结果里。
        Assert.Single(ordered);
        Assert.Equal("a2", ordered[0]?["actionId"]?.GetValue<string>());
    }

    // ---------- 对话框接线（机械检查） ----------

    [Fact]
    public void Dialog_WiresTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
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
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "protected override bool ProcessDialogKey");
        // 筛选先于排序：ListFilter.Apply 的同一表达式作为 ListSort.Order 的输入，排序键取共享口径。
        Assert.Matches(@"(?m)^\s*var visible = ListSort\.Order\($", populate);
        Assert.Matches(@"(?m)^\s*ListFilter\.Apply\(_lines, _keyword\.Text, line => JournalEntries\.KeywordFields\(line, _unfinishedIds\)\),$", populate);
        Assert.Contains("_sortColumn, _sortDescending,", populate, StringComparison.Ordinal);
        Assert.Contains("(line, column) => JournalSort.Key(", populate, StringComparison.Ordinal);
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
        var dialog = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        var shared = Repo.Source("Desktop", "Native", "JournalSort.cs");
        Assert.Equal(3, Regex.Matches(dialog, @"(?m)^\s*_list\.Columns\.Add\(").Count);
        Assert.Equal(Enumerable.Range(0, 3), Arms(shared));
    }

    [Fact]
    public void SortKeysLiveOnlyInTheSharedEntry()
    {
        // 排序键只在共享口径一处：对话框不就地写 switch、不就地拼排序键（两套必然漂移）。
        var dialog = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        Assert.DoesNotContain("IComparable?", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("column switch", dialog, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dialog, "JournalSort\\.Key").Cast<Match>());
        // 时间戳解析也只在口径文件一处：对话框不自己 DateTimeOffset.TryParse。
        Assert.DoesNotContain("TryParse", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SortStateStaysInTheDialogAndIsNotPersisted()
    {
        // 排序状态留在本次打开期间、关闭即忘：跨启动记忆只有两个常驻页面（ListSortMemory 只有两个页键）。
        var dialog = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        Assert.DoesNotContain("ListSortMemory", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("_tasks.Save", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void SortingDoesNotTouchTheCountsLineOrTheUnfinishedCaption()
    {
        // 计数行与未结束动作那句话按整份日志统计与列出（「记录 N 条」「有 N 个动作只有写入意图」
        // 正是要找那几条的凭据）——排序不碰它们：重建里写一次就等于让排序把聚合数字改掉。
        var populate = Slice(Repo.Source("Desktop", "Native", "JournalDialog.cs"), "private void PopulateList(", "protected override bool ProcessDialogKey");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("CountsLine", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("_unfinished.Text", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("UnfinishedCaption", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectionIsRestoredByTheSameLineAfterSorting()
    {
        // 排序只改呈现顺序：选中仍按行载荷（同一个日志节点）恢复（重建前记下、重建后找回）。
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        var populate = Slice(source, "private void PopulateList(", "protected override bool ProcessDialogKey");
        var clear = populate.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        var restore = populate.IndexOf("ReferenceEquals(item.Tag, selectedLine)", StringComparison.Ordinal);
        Assert.True(clear >= 0 && restore > clear, "选中要在重建之后按行载荷恢复。");
        // 恢复仍经唯一入口（Ui.SelectItem），不就地设 Selected、不自己滚动。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[\^1\]\);$", populate);
        Assert.DoesNotContain(".Selected = true", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", populate, StringComparison.Ordinal);
        // 排序入口只翻状态再重建，不自己选中、不自己滚动、不写数据。
        var sortBy = Slice(source, "private void SortBy(", "private void RefreshSortHeaders");
        Assert.DoesNotContain("SelectItem", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", sortBy, StringComparison.Ordinal);
        Assert.DoesNotContain("_tasks.", sortBy, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*PopulateList\(\);$", sortBy);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\JournalSort.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheJournalSorting()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("任务日志记录清单的列排序", section);
        Assert.Contains("`JournalSort`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("任务日志记录清单的列排序（§6）", ledger);
        Assert.Contains("JournalSort", ledger);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = Repo.DesignSection(8)
            .Split('\n')
            .SingleOrDefault(l => l.StartsWith("| 任务日志记录清单的列排序（§6） |", StringComparison.Ordinal));
        Assert.True(row is not null, "DESIGN.md §8 缺少本轮台账行。");
        Assert.Contains("第一百零四轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改写入形状、不改计数行文案、不写任务数据。
        Assert.Contains("不改日志写入形状", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已接入筛选下拉", row, StringComparison.Ordinal);
    }

    // ---------- 助手 ----------

    /// <summary>写入点写出的那一行 → 序列化 → 解析：排序跑的是真实落盘形状，不是手搓的 JSON。</summary>
    private static JsonNode Node(object line) => JsonNode.Parse(JsonSerializer.Serialize(line, JsonDefaults.Compact))!;

    /// <summary>一份真实形状的日志：计划开始、两个动作（一个已核对、一个只有意图）、计划结束。</summary>
    private static List<JsonNode> Log() =>
    [
        Node(JournalLines.PlanStartLine(At, "p", "0123456789abcdef")),
        Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h")),
        Node(JournalLines.ActionResultLine(At, "p", "a1", "Order", "verified", null, 7)),
        Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h")),
        Node(JournalLines.PlanEndLine(At, "p", "partial")),
    ];

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
