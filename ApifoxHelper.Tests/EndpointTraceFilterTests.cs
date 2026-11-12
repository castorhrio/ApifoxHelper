using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 接口追踪明细的关键词框（第七十五轮，DESIGN.md §6「列表筛选」）：这一屏的接口清单是整份扫描的
/// 逐接口 Coverage——真实项目几十上百个接口，而这一屏存在的理由正是「预算超限或调用未解析时在这里可见」，
/// 要找到那几条只能逐行扫；另七张长清单在第六十五至七十三轮都有关键词框，同一个动作在别处有、
/// 在这一处没有，属同壳不同行为。这一屏与那七张有一处不同：**只读、没有筛选下拉**，
/// 故走只有关键词框的那一支（与同步预览的接口勾选页同一支），筛到零行那句话也只指那个框。
/// 口径是真执行（<see cref="EndpointTrace"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 接线以源码切片与行首正则钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class EndpointTraceFilterTests
{
    // ---------- 口径真执行：匹配的是那一行在屏幕上被读作的那两格 ----------

    [Fact]
    public void KeywordFields_CarryTheEndpointIdentityAndTheBudgetText()
    {
        var exceeded = EndpointTrace.KeywordFields("GET /api/orders", budgetExceeded: true);
        Assert.Contains("GET /api/orders", exceeded);
        Assert.Contains(EndpointTrace.BudgetExceededText, exceeded);
        // 正常行的「—」是占位不是内容，不进关键词（与计数列的「—」同一取舍）。
        var normal = EndpointTrace.KeywordFields("GET /api/orders", budgetExceeded: false);
        Assert.DoesNotContain("—", normal);
        Assert.DoesNotContain(EndpointTrace.BudgetExceededText, normal);
    }

    [Fact]
    public void Keyword_FindsTheExceededRowsAndTheEndpointByPathOrMethod()
    {
        var exceeded = EndpointTrace.KeywordFields("GET /api/orders", budgetExceeded: true);
        var normal = EndpointTrace.KeywordFields("POST /api/ping", budgetExceeded: false);
        // 「超限」是这一屏唯一能一眼认出的状态文字：敲它只留下超限的那几条。
        Assert.True(ListFilter.Matches("超限", exceeded));
        Assert.False(ListFilter.Matches("超限", normal));
        // 接口身份按方法或路径找，忽略大小写（与另七张长清单同一份匹配规则）。
        Assert.True(ListFilter.Matches("orders", exceeded));
        Assert.True(ListFilter.Matches("get", exceeded));
        Assert.False(ListFilter.Matches("orders", normal));
        // 空关键词命中全部行。
        Assert.True(ListFilter.Matches("", normal));
        Assert.True(ListFilter.Matches(null, normal));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        var rows = new (string Key, bool Exceeded)[]
        {
            ("GET /api/orders", true),
            ("POST /api/ping", false),
            ("GET /api/orders/{id}", true),
        };
        var all = ListFilter.Apply(rows, "", r => EndpointTrace.KeywordFields(r.Key, r.Exceeded));
        Assert.Equal(rows, all);
        var exceeded = ListFilter.Apply(rows, "超限", r => EndpointTrace.KeywordFields(r.Key, r.Exceeded));
        Assert.Equal(["GET /api/orders", "GET /api/orders/{id}"], exceeded.Select(r => r.Key));
        var none = ListFilter.Apply(rows, "不存在的接口", r => EndpointTrace.KeywordFields(r.Key, r.Exceeded));
        Assert.Empty(none);
        // 筛到零行是「有条目、只是不命中」，不是「没有追踪数据」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(rows.Length, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有接口。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun));
        Assert.Equal("筛选接口（方法、路径、预算）", ListFilter.TraceSearchPlaceholder);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _keyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.PlaceholderText = ListFilter\.TraceSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_keyword\.TextChanged \+= \(_, _\) => PopulateList\(\);$", source);
        Assert.Matches(@"(?m)^\s*var filterRow = Ui\.FilterRow\(_keyword, counts: null\);$", source);
        Assert.Single(Regex.Matches(source, @"Ui\.FilterRow\(").Cast<Match>());
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("ColumnCount = 5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("filterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选接口（方法、路径、预算）", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后停靠（顶部两行），列表最后填满剩余空间。
        var filterAt = source.IndexOf("Controls.Add(filterRow);", StringComparison.Ordinal);
        var headAt = source.IndexOf("Controls.Add(headRow);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && headAt > filterAt, "筛选行要排在标题行之前加入（停靠按集合倒序生效，标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheAggregateCountsAndTheKeywordBox()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs"), "private void PopulateList");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Enabled = false", tail, StringComparison.Ordinal);
        // 计数行按整份追踪数据统计，不随关键词变化：分支里不抹它。
        Assert.DoesNotContain("_counts.Text", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来，详情区与证据入口经既有的无选中路径收口（不另写一套赋值）。
        Assert.Contains("_list.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_hint.Visible = true;", tail, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(tail, @"(?m)^\s*ShowDetail\(\);$"));
        Assert.DoesNotContain("_evidenceButton.Enabled = false", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_detail.Text =", tail, StringComparison.Ordinal);
        // 分支先返回、回显在后：反过来那句话刚显示就被自己抹掉。
        var restore = tail.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(restore > tail.IndexOf("ShowDetail();", StringComparison.Ordinal), "筛到零行的分支要排在列表回显之前。");
    }

    [Fact]
    public void Counts_AreComputedOnceFromTheWholeTraceData()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        // 计数行只在 OnLoad 里按整份 Coverage 算一次：放进重建里就等于随关键词变化（「超限 N」被抹掉）。
        // （`_counts.Text = ""` 另有一处在「整份数据不可得」的提示出口里，那是收掉数字、不是算数字。）
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_counts\.Text = \$""接口 ").Cast<Match>());
        var onLoad = MethodSlice(source, "protected override void OnLoad");
        Assert.Contains("_counts.Text = ", onLoad, StringComparison.Ordinal);
        Assert.Contains("_coverages.Count(c => c.BudgetExceeded)", onLoad, StringComparison.Ordinal);
        var populate = MethodSlice(source, "private void PopulateList");
        Assert.DoesNotContain("_counts.Text", populate, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_IsRestoredByEndpointKeyThenFallsBackToTheFirstVisibleRow()
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs"), "private void PopulateList");
        // 先记下当前接口身份，再清空清单（顺序反过来记到的就是空）。
        var remember = body.IndexOf("var selectedKey = ", StringComparison.Ordinal);
        var clear = body.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(remember >= 0 && clear > remember, "接口身份要在清空清单之前记下。");
        // 恢复不到时落在新筛选第一条（与第五十六轮「切换筛选落在新筛选第一条」同一口径），选中仍经唯一入口。
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", body);
        Assert.DoesNotContain(".Selected = true", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        var body = MethodSlice(source, "protected override bool ProcessDialogKey(Keys keyData)");
        // 第二百二十六轮起：三个手势的落点由登记表判定（FilterGesturePlan），对话框只留一行分发——
        // 不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
        Assert.Matches(@"(?m)^\s*if \(_gestures\.Dispatch\(keyData\)\) return true;$", body);
        Assert.Contains("new FilterGestures(_keyword, _list)", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryFocusFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEnterFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEscapeFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.F", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("_keyword.Clear()", CodeLines(source), StringComparison.Ordinal);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\EndpointTrace.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheTraceKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("接口追踪明细", row, StringComparison.Ordinal);
        Assert.Contains("第七十五轮", row, StringComparison.Ordinal);
        Assert.Contains("TraceSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另七张的差别要写清：只读、没有筛选下拉，故那句话只指那个框。
        Assert.Contains("没有筛选下拉", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "接口追踪明细的关键词框（§6）");
        Assert.Contains("第七十五轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改追踪口径、不改计数行、不写任务数据。
        Assert.Contains("不改追踪口径", row, StringComparison.Ordinal);
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

    private static string CodeLines(string source) => CodeScan.Text(source);
}
