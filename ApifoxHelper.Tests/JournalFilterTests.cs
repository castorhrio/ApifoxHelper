using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务日志记录清单的关键词框（第八十四轮，DESIGN.md §6「列表筛选」）：同步执行每个动作写两行
/// （意图 + 结果），一份几十个动作的计划就是上百行，要找到「写入失败的那几条」「那个接口那一步」
/// 只能逐行扫；另十一张长清单在第六十五至八十轮都有关键词框，同一个动作在别处有、在这一处没有，
/// 属同壳不同行为。这一屏与那十一张有一处不同：**只读、没有筛选下拉**，故走只有关键词框的那一支；
/// 另一处不同是选中落点——这一屏的答案在末尾（打开即选中最后一条），筛掉原来那一步之后落在
/// **新筛选的最后一行**（最近的那一次），而不是另十一张的「新筛选第一条」。
/// 口径是真执行（<see cref="JournalEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 接线以源码切片与行首正则钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class JournalFilterTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 8, 9, 30, 5, TimeSpan.FromHours(8));

    // ---------- 口径真执行：匹配的是那一行在屏幕上被读作的那两格 ----------

    [Fact]
    public void KeywordFields_CarryTheEventAndTheSummary()
    {
        var intent = Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h"));
        var ids = new HashSet<string>(["a2"], StringComparer.Ordinal);
        var fields = JournalEntries.KeywordFields(intent, ids);
        // 两格：事件名与摘要（摘要即标识列，Ctrl+C 复制到的就是这一格）。
        Assert.Equal(2, fields.Length);
        Assert.Contains(JournalEntries.EventText(JournalLines.Intent), fields);
        Assert.Contains(JournalEntries.SummaryText(intent) + " · " + JournalEntries.UnfinishedMark, fields);
        // 与单元格同源：Row 的后两格就是这两格（不另造一套字段，两处必然一起漂或一起对）。
        Assert.Equal(JournalEntries.Row(intent, ids).Skip(1), fields);
        // 时间列是时间戳、不进关键词（与两个主页面「时间列不进关键词」同一取舍）。
        Assert.DoesNotContain(fields, field => field.Contains("2026-11-08", StringComparison.Ordinal));
    }

    [Fact]
    public void Keyword_FindsTheUnfinishedRowsAndTheEventByKeyword()
    {
        var ids = new HashSet<string>(["a2"], StringComparer.Ordinal);
        var unfinished = JournalEntries.KeywordFields(
            Node(JournalLines.IntentLine(At, "p", "a2", "createEndpoint", "GET /orders", null, "h")), ids);
        var finished = JournalEntries.KeywordFields(
            Node(JournalLines.IntentLine(At, "p", "a1", "createModel", "Order", null, "h")), ids);
        var result = JournalEntries.KeywordFields(
            Node(JournalLines.ActionResultLine(At, "p", "a1", "Order", "verified", null, 7)), ids);
        // 「结果未知」是这一屏唯一能一眼认出的标记：敲它只留下只有意图、没有结果的那几行。
        Assert.True(ListFilter.Matches(JournalEntries.UnfinishedMark, unfinished));
        Assert.False(ListFilter.Matches(JournalEntries.UnfinishedMark, finished));
        Assert.False(ListFilter.Matches(JournalEntries.UnfinishedMark, result));
        // 事件名进关键词：敲「同步执行：结果」只留下结果行。
        Assert.True(ListFilter.Matches(JournalEntries.EventText(JournalLines.ActionResult), result));
        Assert.False(ListFilter.Matches(JournalEntries.EventText(JournalLines.ActionResult), unfinished));
        // 摘要里的资源按路径或动作类型找，忽略大小写（与另十一张长清单同一份匹配规则）。
        Assert.True(ListFilter.Matches("orders", unfinished));
        Assert.True(ListFilter.Matches("新建接口", unfinished));
        Assert.False(ListFilter.Matches("orders", finished));
        // 空关键词命中全部行。
        Assert.True(ListFilter.Matches("", finished));
        Assert.True(ListFilter.Matches(null, finished));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var log = Log();
        var ids = new HashSet<string>(["a2"], StringComparer.Ordinal);
        var all = ListFilter.Apply(log, "", line => JournalEntries.KeywordFields(line, ids));
        Assert.Equal(log, all);
        var unfinished = ListFilter.Apply(log, JournalEntries.UnfinishedMark, line => JournalEntries.KeywordFields(line, ids));
        Assert.Single(unfinished);
        Assert.Equal("a2", unfinished[0]?["actionId"]?.GetValue<string>());
        var none = ListFilter.Apply(log, "不存在的记录", line => JournalEntries.KeywordFields(line, ids));
        Assert.Empty(none);
        // 筛到零行是「有记录、只是不命中」，不是「没有记录」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(log.Count, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有记录。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.JournalNoun));
        Assert.Equal("筛选记录（事件、摘要）", ListFilter.JournalSearchPlaceholder);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.JournalSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => PopulateList\(\);$", source);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, counts: null\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选记录（事件、摘要）", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后停靠（顶部两行），列表最后填满剩余空间。
        var filterAt = source.IndexOf("Controls.Add(filterRow);", StringComparison.Ordinal);
        var headAt = source.IndexOf("Controls.Add(headRow);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && headAt > filterAt, "筛选行要排在标题行之前加入（停靠按集合倒序生效，标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheAggregateCountsAndTheUnfinishedCaption()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "JournalDialog.cs"), "private void PopulateList");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.JournalNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = false", tail, StringComparison.Ordinal);
        // 计数行与未结束动作那句话按整份日志统计与列出，不随关键词变化：分支里不抹它们。
        Assert.DoesNotContain("_counts.Text", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_unfinished.Visible = false", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来。
        Assert.Contains("_list.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_hint.Visible = true;", tail, StringComparison.Ordinal);
        // 分支先返回、回显在后：反过来那句话刚显示就被自己抹掉。
        var restore = tail.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(restore > tail.IndexOf("return;", StringComparison.Ordinal), "筛到零行的分支要排在列表回显之前。");
    }

    [Fact]
    public void Counts_AreComputedOnceFromTheWholeLog()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        // 计数行只在 OnLoad 里按整份日志算一次：放进重建里就等于随关键词变化（「记录 N 条」被抹掉）。
        // （`_counts.Text = ""` 另有一处在「整份数据不可得」的提示出口里，那是收掉数字、不是算数字。）
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_counts\.Text = JournalEntries\.CountsLine\(").Cast<Match>());
        var onLoad = MethodSlice(source, "private void LoadJournal(");
        Assert.Contains("_counts.Text = JournalEntries.CountsLine(lines.Count);", onLoad, StringComparison.Ordinal);
        var populate = MethodSlice(source, "private void PopulateList");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
        // 未结束动作那句话同样只在 OnLoad 里算一次（整份日志的聚合，不随关键词变化）。
        Assert.Contains("_unfinished.Text = unfinishedText;", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("_unfinished.Text", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_IsRestoredByTheSameLineThenFallsBackToTheLastVisibleRow()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "JournalDialog.cs"), "private void PopulateList");
        // 先记下当前那一行（行载荷就是整份日志里的同一个节点），再清空清单（顺序反过来记到的就是空）。
        var remember = body.IndexOf("var selectedLine = Ui.SelectedItem(_list)?.Tag as JsonNode;", StringComparison.Ordinal);
        var clear = body.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "当前那一行要在清空清单之前记下。");
        // 恢复按行载荷（同一个节点）而不是屏幕文字：同一秒可能有多条、摘要也可能重复。
        Assert.Contains("ReferenceEquals(item.Tag, selectedLine)", body, StringComparison.Ordinal);
        // 恢复不到时落在新筛选的最后一行：这一屏的答案在末尾（打开即选中最后一条），
        // 与另十一张长清单「落在新筛选第一条」的差别就在这里。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[\^1\]\);$", body);
        Assert.DoesNotContain("_list.Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", "JournalDialog.cs");
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 三个手势只经共享入口（另十一张长清单同一处接线），对话框不就地认键、不自己清空关键词。
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryFocusFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEnterFilter\(keyData, _keyword, _list\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(Ui\.TryEscapeFilter\(keyData, _keyword\)\) return true;$", body);
        Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Clear()", CodeLines(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\JournalEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheJournalKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("任务日志", row, StringComparison.Ordinal);
        Assert.Contains("第八十四轮", row, StringComparison.Ordinal);
        Assert.Contains("JournalSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另十一张的差别要写清：只读、没有筛选下拉，故那句话只指那个框；选中落在最后一行。
        Assert.Contains("没有筛选下拉", row, StringComparison.Ordinal);
        Assert.Contains("最后一行", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "任务日志的关键词框（§6）");
        Assert.Contains("第八十四轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改写入形状、不改事件名映射与计数行、不写任何数据。
        Assert.Contains("不改写入形状", row, StringComparison.Ordinal);
        Assert.Contains("不重发", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已接入筛选下拉", row, StringComparison.Ordinal);
    }

    // ---------- 助手 ----------

    /// <summary>写入点写出的那一行 → 序列化 → 解析：筛选跑的是真实落盘形状，不是手搓的 JSON。</summary>
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

    private static string DesignRow(int section, string clause)
    {
        var lines = Repo.DesignSection(section).Split('\n');
        var row = lines.SingleOrDefault(l => l.StartsWith($"| {clause} |", StringComparison.Ordinal));
        Assert.True(row is not null, $"DESIGN.md §{section} 缺少「{clause}」这一行。");
        return row;
    }

    /// <summary>方法体切片：签名行到下一个「    }」为止（本文件只切两个方法，缩进固定四格）。</summary>
    private static string MethodSlice(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var to = source.IndexOf("\n    }", from, StringComparison.Ordinal);
        Assert.True(to > from, $"{signature} 的方法体没有收尾。");
        return source[from..to];
    }

    private static string CodeLines(string source) => CodeScan.Text(source);
}
