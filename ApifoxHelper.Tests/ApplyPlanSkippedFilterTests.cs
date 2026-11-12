using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第一百一十二轮：注释回写跳过清单的关键词框（DESIGN.md §6「列表筛选」）。
/// 同一个对话框里文件清单自第七十九轮起就有关键词框、跳过清单没有——而这一张是整份计划里
/// 没能生成修改的注释位置（每个被跳过的候选一行，真实项目几十条：「注释位置已不存在」
/// 「扫描后源码或证据已变化」「无法定位声明」「文件不是 UTF-8 编码」「无法无损往返编码」…），
/// 说明行写着「跳过的位置（原因如下；处理后重新生成计划）」，要按原因处理就得先把同一原因的集中看
/// （第一百零八轮补的列排序只能聚同值、藏不掉不关心的原因，也搜不到原因里的具体文字），
/// 属同壳不同行为。这一屏没有筛选下拉（跳过原因是逐条结果、不是可选状态），故走只有关键词框的那一支；
/// 匹配的是那一行在屏幕上被读作的那两格（跳过的位置、原因）——原因进关键词是这一屏的要害。
/// 同一页两个框：Enter 与 Esc 看焦点各接一次，Ctrl+F 只交给文件清单那个框。
/// 口径是真执行（<see cref="ApplyPlanEntries"/> 与 <see cref="ListFilter"/> 与测试工程共享编译），
/// 界面接线属 WinForms 控件触达、本环境不可执行，全部以源码切片与行首正则钉住，不冒充跑过。
/// 真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ApplyPlanSkippedFilterTests
{
    private const string File = "ApplyPlanDialog.cs";

    /// <summary>一行被跳过的位置在屏幕上被读作的那两格（与对话框建行处同一份取值）。</summary>
    private sealed record Skip(string Display, string Reason);

    private static string?[] Fields(Skip skip) => ApplyPlanEntries.SkippedKeywordFields(skip.Display, skip.Reason);

    // ---------- 真执行：匹配的是那一行在屏幕上被读作的那两格 ----------

    [Fact]
    public void KeywordFields_AreTheDisplayAndTheReason()
    {
        var fields = ApplyPlanEntries.SkippedKeywordFields("Orders/OrderController.Create", "文件不是 UTF-8 编码，不自动修改");
        Assert.Equal(2, fields.Length);
        Assert.Equal("Orders/OrderController.Create", fields[0]);
        Assert.Equal("文件不是 UTF-8 编码，不自动修改", fields[1]);
    }

    [Theory]
    [InlineData("orders", true)]            // 位置（忽略大小写）
    [InlineData("OrderController", true)]   // 位置片段
    [InlineData("UTF-8", true)]             // 原因——这一屏的要害：用户问的正是「为什么这个位置没被处理」
    [InlineData("不自动修改", true)]          // 原因片段（这一行的原因里确有这四个字）
    [InlineData("证据已变化", false)]         // 另一条的原因不在这一行里
    [InlineData("不存在的条目", false)]
    [InlineData("", true)]                  // 空关键词即全部行
    public void Matches_TheTwoCellsOnly(string query, bool expected)
    {
        var fields = ApplyPlanEntries.SkippedKeywordFields("Orders/OrderController.Create", "文件不是 UTF-8 编码，不自动修改");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Theory]
    [InlineData("扫描后源码或证据文件已变化", true)]
    [InlineData("Payments", false)]         // 另一条的位置不在这一行里
    [InlineData("", true)]
    public void Matches_TheEvidenceChangedRowToo(string query, bool expected)
    {
        var fields = ApplyPlanEntries.SkippedKeywordFields("Orders/OrderService.List", "扫描后源码或证据文件已变化，请重新扫描并审阅：a.cs");
        Assert.Equal(expected, ListFilter.Matches(query, fields));
    }

    [Fact]
    public void Apply_KeepsSourceOrderAndFiltersOnlyTheVisibleRows()
    {
        // 行载荷取本文件的行记录（与对话框建行处同一份两格取值），不引用主工程的计划模型
        // ——测试工程只共享纯逻辑文件，PlanSkip 不在其中。
        var skipped = new List<Skip>
        {
            new("A.Create", "无法定位声明"),
            new("B.Create", "文件不是 UTF-8 编码，不自动修改"),
            new("C.Create", "无法定位声明"),
        };
        var all = ListFilter.Apply(skipped, "", s => ApplyPlanEntries.SkippedKeywordFields(s.Display, s.Reason));
        Assert.Equal(skipped, all);
        // 数据源自身不被改写（与 ListSort.Order 同一纪律）。
        Assert.Equal(["A.Create", "B.Create", "C.Create"], skipped.Select(s => s.Display));
        // 按原因筛：同一原因的那几条聚在一起（「按原因处理」正是这一屏的下一步）。
        var located = ListFilter.Apply(skipped, "无法定位声明", s => ApplyPlanEntries.SkippedKeywordFields(s.Display, s.Reason));
        Assert.Equal(["A.Create", "C.Create"], located.Select(s => s.Display));
        var encoding = ListFilter.Apply(skipped, "UTF-8", s => ApplyPlanEntries.SkippedKeywordFields(s.Display, s.Reason));
        Assert.Equal(["B.Create"], encoding.Select(s => s.Display));
        var none = ListFilter.Apply(skipped, "不存在的条目", s => ApplyPlanEntries.SkippedKeywordFields(s.Display, s.Reason));
        Assert.Empty(none);
        // 筛到零行是「有跳过位置、只是不命中」，不是「计划没有跳过位置」——两者的下一步不同。
        Assert.True(ListFilter.IsFilteredEmpty(skipped.Count, none.Count));
        Assert.False(ListFilter.IsFilteredEmpty(0, 0));
    }

    [Fact]
    public void FilteredEmptySentence_PointsAtTheKeywordBoxOnly()
    {
        // 这一屏没有筛选下拉：说「换一个筛选」就是指向界面上按不动的控件。
        Assert.Equal("这个关键词下没有跳过位置。换一个关键词再试。", ListFilter.FilteredEmptyKeywordHint(ListFilter.SkippedNoun));
        Assert.Equal("筛选跳过位置（位置、原因）", ListFilter.SkippedSearchPlaceholder);
        // 与文件清单的名词分开：同一个对话框里两张清单清的不是同一样东西。
        Assert.NotEqual(ListFilter.ApplyPlanNoun, ListFilter.SkippedNoun);
    }

    // ---------- 接线：口径存在但对话框没接线等于没有 ----------

    [Fact]
    public void Dialog_TakesTheSecondKeywordBoxFromTheFactories()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 关键词框经工厂出厂（§6「输入框」），筛选行经工厂组装（§6「列表筛选」），界面不各拼一遍。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _skippedKeyword = Ui\.Input\(\);$", source);
        Assert.Matches(@"(?m)^\s*_skippedKeyword\.PlaceholderText = ListFilter\.SkippedSearchPlaceholder;$", source);
        Assert.Matches(@"(?m)^\s*_skippedKeyword\.TextChanged \+= \(_, _\) =>$", source);
        Assert.Matches(@"(?m)^\s*var skippedFilterRow = Ui\.FilterRow\(_skippedKeyword, counts: null\);$", source);
        // 界面不自己拼这一行、不自己量宽度、不就地拼占位文字。
        Assert.DoesNotContain("skippedFilterRow.Controls.Add(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedKeyword.Width", source, StringComparison.Ordinal);
        Assert.DoesNotContain("筛选跳过位置（", source, StringComparison.Ordinal);
        // 筛选行排在标题行之后加入（停靠按集合倒序生效，标题行在最上、筛选行在其下、清单填满剩余）。
        var filterAt = source.IndexOf("_skippedHost.Controls.Add(skippedFilterRow);", StringComparison.Ordinal);
        var captionAt = source.IndexOf("_skippedHost.Controls.Add(skippedCaption);", StringComparison.Ordinal);
        Assert.True(filterAt >= 0 && captionAt > filterAt, "筛选行要排在标题行之前加入（倒序停靠后标题行在最上）。");
    }

    [Fact]
    public void FilteredEmptyBranch_KeepsTheBoxAndSaysTheNextStep()
    {
        var body = Slice(Repo.Source("Desktop", "Native", File), "private void PopulateSkipped(", "private void SortSkipped(");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(branch >= 0, "筛到零行要经共享判定，不就地写「行数 == 0」。");
        var tail = body[branch..];
        // 那句话与下一步：只指那个框（这一屏没有筛选下拉），关键词框留着（§7「禁用不得移除下一步」）。
        Assert.Contains("ListFilter.FilteredEmptyKeywordHint(ListFilter.SkippedNoun)", tail, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedKeyword.Enabled", tail, StringComparison.Ordinal);
        // 清单藏起来、提示显示出来；分支先返回、回显在后。
        Assert.Contains("_skippedList.Visible = false;", tail, StringComparison.Ordinal);
        Assert.Contains("_skippedHint.Visible = true;", tail, StringComparison.Ordinal);
        Assert.True(tail.IndexOf("_skippedHint.Visible = false;", StringComparison.Ordinal) > tail.IndexOf("return;", StringComparison.Ordinal),
            "筛到零行的分支要排在清单回显之前。");
        // 跳过清单没有选中语义：不恢复选中、不碰键盘焦点、不自己滚动。
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void FilterRunsBeforeSorting_ThroughTheSharedEntries()
    {
        var body = Slice(Repo.Source("Desktop", "Native", File), "private void PopulateSkipped(", "private void SortSkipped(");
        // 筛选先于排序（ListFilter → ListSort，与文件清单同一顺序）：筛掉的行不参与排序。
        var order = body.IndexOf("ListSort.Order(", StringComparison.Ordinal);
        var apply = body.IndexOf("ListFilter.Apply(", StringComparison.Ordinal);
        Assert.True(order >= 0 && apply > order, "筛选要包在排序里（ListSort.Order(ListFilter.Apply(...))）。");
        // 匹配字段与排序键都取共享口径（与两格同源），界面不就地拼字段。
        Assert.Contains("skip => ApplyPlanEntries.SkippedKeywordFields(skip.Display, skip.Reason)),", body, StringComparison.Ordinal);
        Assert.Contains("(skip, column) => ApplyPlanSort.SkippedKey(skip.Display, skip.Reason, column));", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, @"ApplyPlanEntries\.SkippedKeywordFields\(").Cast<Match>());
    }

    [Fact]
    public void Counts_AreStillComputedOnceFromTheWholePlan()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 计数行（含「跳过 N」）只在 ShowPlan 里按整份计划算一次：放进重建里就等于随关键词变化。
        Assert.Single(Regex.Matches(source, @"ApplyPlanEntries\.CountsLine\(").Cast<Match>());
        Assert.DoesNotContain("_counts", Slice(source, "private void PopulateSkipped(", "private void SortSkipped("), StringComparison.Ordinal);
    }

    [Fact]
    public void BoxIsDisabledWhenTheWholeDataIsUnavailable()
    {
        var source = Repo.Source("Desktop", "Native", File);
        // 只有关键词框的那一支：停用与恢复都经共享口径，界面不就地写 true/false 字面。
        Assert.Contains("_skippedKeyword.Enabled = ListFilter.KeywordEnabled(plan.Skipped.Count > 0);", source, StringComparison.Ordinal);
        Assert.Contains("_skippedKeyword.Enabled = ListFilter.KeywordEnabled(_plan is { Skipped.Count: > 0 }, busy);", source, StringComparison.Ordinal);
        Assert.Contains("_skippedKeyword.Enabled = ListFilter.KeywordEnabled(false);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedKeyword.Enabled = false;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedKeyword.Enabled = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotRecognizeTheGesturesItself()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var body = Slice(source, "protected override bool ProcessDialogKey(Keys keyData)", "return base.ProcessDialogKey(keyData);");
        // 第二百二十六轮起：同一页两个框登记在同一张表里（主框是文件清单那个），落点由 FilterGesturePlan 判定——
        // Enter 与 Esc 看焦点（一次按键只有一个含义），Ctrl+F 只交给主框。
        Assert.Matches(@"(?m)^\s*if \(_gestures\.Dispatch\(keyData\)\) return true;$", body);
        Assert.Contains("new FilterGestures(_keyword, _list).Then(_skippedKeyword, _skippedList)", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryFocusFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEnterFilter", CodeLines(source), StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.TryEscapeFilter", CodeLines(source), StringComparison.Ordinal);
        // 对话框不就地认键、不自己清空关键词。
        var code = CodeLines(source);
        Assert.DoesNotContain("Keys.F", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Enter", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Keys.Escape", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_skippedKeyword.Clear()", code, StringComparison.Ordinal);
        // 对话框的 Enter=确认、Esc=取消在焦点不在框上时逐字不变。
        Assert.Matches(@"(?m)^\s*AcceptButton = closeButton;$", source);
        Assert.Matches(@"(?m)^\s*CancelButton = closeButton;$", source);
    }

    [Fact]
    public void Gauges_AreSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ApplyPlanEntries.cs", csproj, StringComparison.Ordinal);
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheSkippedKeywordBox()
    {
        var row = DesignRow(6, "列表筛选");
        Assert.Contains("跳过清单", row, StringComparison.Ordinal);
        Assert.Contains("第一百一十二轮", row, StringComparison.Ordinal);
        Assert.Contains("SkippedSearchPlaceholder", row, StringComparison.Ordinal);
        // 这一屏与另十四张的差别要写清：同一页两张清单、Ctrl+F 只交给文件清单那个框。
        Assert.Contains("Ctrl+F", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_LedgerRecordsThisRound()
    {
        var row = DesignRow(8, "注释回写跳过清单的关键词框（§6）");
        Assert.Contains("第一百一十二轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改回写口径、不改计数行、不写任何数据。
        Assert.Contains("不改回写口径", row, StringComparison.Ordinal);
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

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }

    private static string CodeLines(string source) => CodeScan.Text(source);
}
