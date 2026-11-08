using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 生成文档核对条目的关键词框（第七十七轮，DESIGN.md §6「列表筛选」）：这一屏的条目清单是整份核对结果
/// （逐注释位置 × 三环），真实项目几十上百条，而这一屏存在的理由正是「注释有没有真的写进源码、出现在 XML、
/// 被 Swagger 读到」——要找到「XML 未收录」的那几条只能逐行扫；另八张长清单在第六十五至七十六轮都有关键词框，
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。这一屏与那八张有一处不同：**只读、没有筛选下拉**，
/// 故走只有关键词框的那一支（与接口追踪明细、补充说明同一支），三环状态文字因此进关键词，
/// 筛到零行那句话也只指那个框。
/// 口径是真执行（<see cref="PropagationEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 接线以源码切片与行首正则钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class PropagationFilterTests
{
    // ---------- 口径真执行：匹配的是那一行在屏幕上被读作的那五格 ----------

    [Fact]
    public void KeywordFields_CarryTheFiveCellsTheRowIsReadAs()
    {
        var fields = PropagationEntries.KeywordFields(
            "OrderService.GetAsync",
            "参数 id",
            TaskDisplay.PropagationSourceText("written"),
            TaskDisplay.PropagationXmlText("missing"),
            TaskDisplay.PropagationSwaggerText("present"));
        Assert.Contains("OrderService.GetAsync", fields);
        Assert.Contains("参数 id", fields);
        Assert.Contains("已写入", fields);
        Assert.Contains("未收录", fields);
        Assert.Contains("已出现", fields);
        // 说明文字、Swagger 对应位置与备注在详情区而不在这一行，不进关键词。
        Assert.DoesNotContain("说明文字", fields);
        Assert.DoesNotContain("Swagger 对应位置", fields);
    }

    [Fact]
    public void Keyword_FindsTheRowsByStateTextAndByPosition()
    {
        var xmlMissing = PropagationEntries.KeywordFields(
            "OrderService.GetAsync", "说明",
            TaskDisplay.PropagationSourceText("written"),
            TaskDisplay.PropagationXmlText("missing"),
            TaskDisplay.PropagationSwaggerText("missing"));
        var allGood = PropagationEntries.KeywordFields(
            "PingService.Ping", "返回",
            TaskDisplay.PropagationSourceText("written"),
            TaskDisplay.PropagationXmlText("present"),
            TaskDisplay.PropagationSwaggerText("present"));
        // 三环状态文字进关键词：这一屏没有筛选下拉可管它们，不进关键词就等于筛不到。
        Assert.True(ListFilter.Matches("未收录", xmlMissing));
        Assert.False(ListFilter.Matches("未收录", allGood));
        Assert.True(ListFilter.Matches("未出现", xmlMissing));
        Assert.False(ListFilter.Matches("未出现", allGood));
        // 位置与标注按当前区域忽略大小写匹配（与另八张长清单同一份匹配规则）。
        Assert.True(ListFilter.Matches("orderservice", xmlMissing));
        Assert.True(ListFilter.Matches("说明", xmlMissing));
        Assert.False(ListFilter.Matches("说明", allGood));
        // 空关键词命中全部行。
        Assert.True(ListFilter.Matches("", allGood));
        Assert.True(ListFilter.Matches(null, allGood));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var rows = new (string Display, string XmlState)[]
        {
            ("OrderService.GetAsync", "missing"),
            ("PingService.Ping", "present"),
            ("OrderService.ListAsync", "missing"),
        };
        string?[] Fields((string Display, string XmlState) r) => PropagationEntries.KeywordFields(
            r.Display, "说明",
            TaskDisplay.PropagationSourceText("written"),
            TaskDisplay.PropagationXmlText(r.XmlState),
            TaskDisplay.PropagationSwaggerText("present"));

        var all = ListFilter.Apply(rows, "", Fields);
        Assert.Equal(rows, all);
        var missing = ListFilter.Apply(rows, "未收录", Fields);
        Assert.Equal(["OrderService.GetAsync", "OrderService.ListAsync"], missing.Select(r => r.Display));
        var none = ListFilter.Apply(rows, "不存在的条目", Fields);
        Assert.Empty(none);
        // 筛到零行是「有条目、只是不命中」，不是「没有核对条目」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(rows.Length, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有核对条目。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.PropagationNoun));
        Assert.Equal("筛选条目（位置、标注、三环状态）", ListFilter.PropagationSearchPlaceholder);
    }

    [Fact]
    public void CountsLine_IsTheWholeDataNumbers()
    {
        // 表头那一行：位置总数与三环各自「已到位」的条数，与改前逐字一致（只把拼装收进一处）。
        Assert.Equal("位置 12 · 源码 9 · XML 7 · Swagger 5", PropagationEntries.CountsLine(12, 9, 7, 5));
        Assert.Equal("位置 0 · 源码 0 · XML 0 · Swagger 0", PropagationEntries.CountsLine(0, 0, 0, 0));
    }

    [Fact]
    public void TagText_NamesTheParameterAndFallsBackToTheSlotTag()
    {
        // 参数位置带参数名（与改前逐字一致），其余取 TaskDisplay.SlotTagText，未知取值原样回退。
        Assert.Equal("参数 id", PropagationEntries.TagText("param", "id"));
        Assert.Equal("参数", PropagationEntries.TagText("param", null));
        Assert.Equal("说明", PropagationEntries.TagText("summary", null));
        Assert.Equal("返回", PropagationEntries.TagText("returns", null));
        Assert.Equal("mystery", PropagationEntries.TagText("mystery", null));
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.PropagationSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => PopulateList\(\);$", source);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, counts: null\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选条目（位置、标注、三环状态）", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后停靠（顶部两行），列表最后填满剩余空间。
        var filterAt = source.IndexOf("Controls.Add(filterRow);", StringComparison.Ordinal);
        var headAt = source.IndexOf("Controls.Add(headRow);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && headAt > filterAt, "筛选行要排在标题行之前加入（停靠按集合倒序生效，标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheAggregateCountsAndTheKeywordBox()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "PropagationDialog.cs"), "private void PopulateList");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.PropagationNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = false", tail, StringComparison.Ordinal);
        // 计数行按整份核对数据统计，不随关键词变化：分支里不抹它。
        Assert.DoesNotContain("_counts.Text", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来，详情区经既有的无选中路径收口（不另写一套赋值）。
        Assert.Contains("_list.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_hint.Visible = true;", tail, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(tail, @"(?m)^\s*ShowDetail\(\);$"));
        Assert.DoesNotContain("_detail.Text =", tail, StringComparison.Ordinal);
        // 分支先返回、回显在后：反过来那句话刚显示就被自己抹掉。
        var restore = tail.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(restore > tail.IndexOf("ShowDetail();", StringComparison.Ordinal), "筛到零行的分支要排在列表回显之前。");
    }

    [Fact]
    public void Counts_AreComputedOnceFromTheWholeData()
    {
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        // 计数行只在 OnLoad 里按整份核对数据算一次：放进重建里就等于随关键词变化（「XML 0」被抹掉）。
        // （`_counts.Text = ""` 另有一处在「整份数据不可得」的提示出口里，那是收掉数字、不是算数字。）
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_counts\.Text = PropagationEntries\.CountsLine\(").Cast<Match>());
        var onLoad = MethodSlice(source, "protected override void OnLoad");
        Assert.Contains("PropagationEntries.CountsLine(", onLoad, StringComparison.Ordinal);
        Assert.Contains("en.SourceState == \"written\"", onLoad, StringComparison.Ordinal);
        var populate = MethodSlice(source, "private void PopulateList");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_IsRestoredByPositionThenFallsBackToTheFirstVisibleRow()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "PropagationDialog.cs"), "private void PopulateList");
        // 先记下当前条目的位置，再清空清单（顺序反过来记到的就是空）。
        var remember = body.IndexOf("var selectedDisplay = ", StringComparison.Ordinal);
        var clear = body.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "条目位置要在清空清单之前记下。");
        // 恢复不到时落在新筛选第一条（与第五十六轮「切换筛选落在新筛选第一条」同一口径），选中仍经唯一入口。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", body);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", "PropagationDialog.cs");
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 三个手势只经共享入口（另八张长清单同一处接线），对话框不就地认键、不自己清空关键词。
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
        Assert.Contains(@"..\Desktop\Native\PropagationEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsThePropagationKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("生成文档核对", row, StringComparison.Ordinal);
        Assert.Contains("第七十七轮", row, StringComparison.Ordinal);
        Assert.Contains("PropagationSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另八张的差别要写清：只读、没有筛选下拉，故那句话只指那个框。
        Assert.Contains("没有筛选下拉", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "生成文档核对条目的关键词框（§6）");
        Assert.Contains("第七十七轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改核对口径、不改计数行文案、不写任务数据。
        Assert.Contains("不改核对口径", row, StringComparison.Ordinal);
        Assert.DoesNotContain("已接入筛选下拉", row, StringComparison.Ordinal);
    }

    // ---------- 助手 ----------

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

    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
