using System.Reflection;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「筛到零行」的状态完备断言（DESIGN.md §6「列表筛选」、§7「禁用要说明缺什么 / 禁用不得移除下一步」、§8 本轮条款）。
/// 五张带筛选下拉的清单此前对同一件事给出三种结果：两个审阅队列（注释候选、响应契约）筛到零行时只留下一张空表，
/// 一句话都没有；差异确认给一句话并把计数行抹成空白——「共 5 项 · 待处理 0」正是清完队列的凭据，却被这一屏的 empty 抹掉；
/// 检查结果两种空都分开说、计数行也不动，是四处里唯一已经成立的一处。两个主页面从一开始就同时给出两样
/// （空态「没有匹配的任务」+ 状态行「筛选出 0 个任务（共 42 个）」，第四十八、五十轮）。
/// 现收成一条口径：判定与文案都在 <see cref="ListFilter"/>（不依赖 WinForms、与测试工程共享编译）——
/// <c>IsFilteredEmpty</c> 分清「整份数据没有条目」与「有条目、只是当前筛选不命中」，
/// <c>FilteredEmptyHint</c> 给那一屏为什么空与下一步（换一个筛选）；
/// 提示出口因此不得禁用筛选器（第四十二轮的同一纪律补到另外三处），也不得抹掉聚合计数。
/// 第五十九轮如实挂账未接的第五张清单（注释缺口）已于第六十轮接上——它不套 <c>keepAggregate</c> 那个出口，
/// 而是走自己的形状（只换列表区那一屏，覆盖率行与两个聚合入口一概不动），断言见 <c>ScanResultsFilteredEmptyTests</c>。
/// 真机上「筛到零行的一句话显示出来、聚合数字仍在、换一个筛选立刻按得动」随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class FilteredEmptyStateTests
{
    // ---------- 口径真值表：这一屏的空属于哪一种 ----------

    [Theory]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(128, 0)]
    [InlineData(int.MaxValue, 0)]
    public void FilteredEmpty_WhenThereAreEntriesButNoneMatch(int totalEntries, int visibleEntries)
    {
        Assert.True(ListFilter.IsFilteredEmpty(totalEntries, visibleEntries));
    }

    [Theory]
    // 整份数据就没有条目：下一步不在筛选上，各视图另有说明原因与出路的提示，不能套这句话。
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    public void NotFilteredEmpty_WhenTheDataItselfHasNoEntries(int totalEntries, int visibleEntries)
    {
        Assert.False(ListFilter.IsFilteredEmpty(totalEntries, visibleEntries));
    }

    [Theory]
    // 筛掉一部分不是空：可见行哪怕只剩一行，这一屏也不需要这句话。
    [InlineData(5, 1)]
    [InlineData(5, 4)]
    [InlineData(5, 5)]
    public void NotFilteredEmpty_WhenSomeRowsAreVisible(int totalEntries, int visibleEntries)
    {
        Assert.False(ListFilter.IsFilteredEmpty(totalEntries, visibleEntries));
    }

    [Fact]
    public void Guard_OnlyTheExactIntersectionCountsAsFilteredEmpty()
    {
        // 逐点核对：放行当且仅当「有条目且可见条目为零」。
        // 多一分是把「没有条目」说成「换一个筛选就有」（那条路变不出条目），
        // 少一分是筛到零行时仍留下一张空表——本条口径要修的正是这两头。
        for (var total = -2; total <= 20; total++)
        {
            for (var visible = -2; visible <= 20; visible++)
            {
                Assert.Equal(total > 0 && visible == 0, ListFilter.IsFilteredEmpty(total, visible));
            }
        }
    }

    // ---------- 文案：说清为什么空，并指名下一步 ----------

    [Theory]
    [InlineData(ListFilter.CandidateNoun, "这个筛选下没有候选。换一个筛选再试。")]
    [InlineData(ListFilter.ContractNoun, "这个筛选下没有契约。换一个筛选再试。")]
    [InlineData(ListFilter.EndpointNoun, "这个筛选下没有接口。换一个筛选再试。")]
    [InlineData(ListFilter.DecisionNoun, "这个筛选下没有条目。换一个筛选再试。")]
    [InlineData(ListFilter.SlotNoun, "这个筛选下没有注释位置。换一个筛选再试。")]
    public void Hint_NamesWhatIsMissingAndTheNextStep(string noun, string expected)
    {
        Assert.Equal(expected, ListFilter.FilteredEmptyHint(noun));
    }

    [Fact]
    public void Hint_PointerIsTheSingleSourceForTheNextStep()
    {
        // 检查结果的原文案是「这个状态下没有接口」——控件名叫「筛选」，指的路径也是换一个筛选，
        // 本轮统一到共享文案；下一句「换一个筛选再试。」只有一份（第四十二轮已把它当作恢复路径）。
        Assert.Equal("换一个筛选再试。", ListFilter.FilteredEmptyPointer);
        foreach (var noun in new[] { ListFilter.CandidateNoun, ListFilter.ContractNoun, ListFilter.EndpointNoun, ListFilter.DecisionNoun, ListFilter.SlotNoun })
        {
            Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(noun), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Nouns_AreExactlyTheFiveFilteredViews()
    {
        var declared = typeof(ListFilter)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.EndsWith("Noun", StringComparison.Ordinal))
            .Select(f => (string?)f.GetValue(null) ?? "")
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        var expected = new[] { ListFilter.CandidateNoun, ListFilter.ContractNoun, ListFilter.EndpointNoun, ListFilter.DecisionNoun, ListFilter.SlotNoun }
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expected, declared);
        // 五个视图清的是五样东西：并成一份就等于把「没有契约」说成「没有候选」。
        Assert.Equal(5, declared.Distinct().Count());
    }

    [Theory]
    [InlineData("换一个筛选再试")]
    [InlineData("这个筛选下")]
    public void Views_DoNotSpellTheSentenceThemselves(string fragment)
    {
        // 只算代码行：注释里引用这句话（说明为什么这么改）不算第二套拼写。
        foreach (var source in Repo.NativeSources())
        {
            Assert.Equal(source.Name == "ListFilter.cs" ? 1 : 0, CodeLinesContaining(source.Text, fragment));
        }
    }

    [Fact]
    public void Hint_IsWiredOnExactlyTheFiveFilteredViews()
    {
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListFilter.FilteredEmptyHint"))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "CandidatesDialog.cs", "CheckResultsDialog.cs", "ContractsDialog.cs", "DecisionsDialog.cs", "ScanResultsDialog.cs" }, callers);
    }

    // ---------- 两个审阅队列：筛到零行不再只留一张空表 ----------

    [Theory]
    // 注释候选与响应契约此前没有这一支：点开一个没有条目的状态、或清完「待审阅」最后一条，
    // 屏幕上只剩表头与一张空表，聚合数字虽在却没人说这一屏为什么空。
    [InlineData("CandidatesDialog.cs", "private string SlotTitle", "_store.Candidates.Count", "CandidateNoun", "候选")]
    [InlineData("ContractsDialog.cs", "private void ShowDetail", "_contracts.Count", "ContractNoun", "契约")]
    public void ReviewViews_ExplainTheEmptyFilterBeforeRestoringVisibility(string file, string endAnchor, string total, string nounConst, string noun)
    {
        Assert.Equal(noun, NounValue(nounConst));
        var body = Slice(Repo.Source("Desktop", "Native", file), "private void ApplyFilter()", endAnchor);
        Assert.Single(Regex.Matches(body, @"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\("));
        Assert.Matches($@"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\({Regex.Escape(total)}, _list\.Items\.Count\)\)$", body);
        Assert.Matches($@"(?m)^\s*ShowHint\(ListFilter\.FilteredEmptyHint\(ListFilter\.{nounConst}\), UiTokens\.Muted, keepAggregate: true\);$", body);
        // 分支先返回，回显在后面：写在提示之前等于这句话刚显示就被自己抹掉。
        var hint = body.IndexOf("keepAggregate: true);", StringComparison.Ordinal);
        var restore = body.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(hint >= 0 && restore > hint, $"{file}:提示分支要排在列表回显之前。");
        Assert.Matches(@"(?m)^\s*_list\.Visible = true;$", body);
        // 这一支不就地抹聚合数字：聚合由计数行自己按全部条目算。
        Assert.DoesNotContain("_counts.Text", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    public void ReviewViews_KeepFilterAndCountsOnTheSameJudgment(string file)
    {
        var body = HintExit(file);
        Assert.Matches(@"(?m)^\s*_filter\.Enabled = keepAggregate;$", body);
        // 出口不再无条件禁用筛选器：提示语指的就是它，它不能被出口连坐禁用（第四十二轮同一条纪律）。
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled = false;$", body);
        Assert.Matches(@"(?m)^\s*if \(!keepAggregate\) _counts\.Text = """";$", body);
        Assert.DoesNotMatch(@"(?m)^\s*_counts\.Text = """";$", body);
    }

    // ---------- 差异确认：提示照给，聚合数字不再被抹掉 ----------

    [Fact]
    public void Decisions_KeepsAggregateCountsInTheEmptyFilterBranch()
    {
        var body = Slice(Repo.Source("Desktop", "Native", "DecisionsDialog.cs"), "private void ApplyFilter(", "private Entry? Current()");
        Assert.Matches(@"(?m)^\s*keepAggregate: filteredEmpty\);$", body);
        // 「没有需要处理的差异确认」那一条仍按原形状清计数：那时聚合数字本来就没有。
        Assert.Matches(@"(?m)^\s*: ""没有需要处理的差异确认。"",$", body);
        Assert.DoesNotContain(@"_counts.Text = """"", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Decisions_HintExit_KeepsCountsOnDemand()
    {
        var body = HintExit("DecisionsDialog.cs");
        Assert.Matches(@"(?m)^\s*if \(!keepAggregate\) _counts\.Text = """";$", body);
        Assert.DoesNotMatch(@"(?m)^\s*_counts\.Text = """";$", body);
    }

    // ---------- 检查结果：四处里原本就成立的一处，接进同一份判定与文案 ----------

    [Fact]
    public void CheckResults_SplitsTheTwoEmptiesAndKeepsTheNoDataSentence()
    {
        var body = Slice(Repo.Source("Desktop", "Native", "CheckResultsDialog.cs"), "private void PopulateList(", "private void ShowHint");
        Assert.Matches(@"(?m)^\s*ShowHint\(ListFilter\.IsFilteredEmpty\(_result\.Endpoints\.Count, endpoints\.Count\)$", body);
        // 第六十五轮起这一屏还有关键词框：那句话的下一步因此有两个真实入口（关键词或筛选）。
        Assert.Matches(@"(?m)^\s*\? ListFilter\.FilteredEmptyHint\(ListFilter\.EndpointNoun, ListFilter\.IsActive\(keyword\)\)$", body);
        // 「范围内没有接口」那句讲的是范围与扫描，不是筛选，本轮逐字不动。
        Assert.Matches(@"(?m)^\s*: ""范围内没有接口。请确认任务的模块与路径前缀范围，或先完成源码扫描。"",$", body);
        // 这一处的提示出口既不碰计数行也不碰筛选器，所以不需要 keepAggregate 参数——它本来就是本轮的形状。
        Assert.Empty(Regex.Matches(body, "keepAggregate"));
        Assert.Matches(@"(?m)^\s*_filter\.Enabled = _result\.Endpoints\.Count > 0;$", body);
    }

    [Fact]
    public void CheckResults_HintExit_TouchesNeitherCountsNorFilter()
    {
        var body = HintExit("CheckResultsDialog.cs");
        Assert.DoesNotContain("_counts", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_filter", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", 1)]
    [InlineData("ContractsDialog.cs", 1)]
    [InlineData("DecisionsDialog.cs", 1)]
    [InlineData("CheckResultsDialog.cs", 0)]
    // 注释缺口清单第六十轮接进同一份判定与文案，但不套这个出口——它有自己的形状（见 ScanResultsFilteredEmptyTests）。
    [InlineData("ScanResultsDialog.cs", 0)]
    public void OnlyTheFilteredEmptyBranch_KeepsAggregate(string file, int expected)
    {
        // 打开期读取失败与「本来就没有条目」保持原形状：聚合数字那时确实不可得或确实为零。
        Assert.Equal(expected, Regex.Matches(Repo.Source("Desktop", "Native", file), "keepAggregate: ").Count);
    }

    // ---------- 没做的事不能被写成做了 ----------

    // 第五张清单（注释缺口）第六十轮已按自己的形状接上：它不套本文件的提示出口，
    // 两条出口（筛到零行 / 整份数据不可得）不合并、覆盖率行与两个聚合入口不动等断言在 ScanResultsFilteredEmptyTests。

    [Theory]
    [InlineData("TasksPage.cs")]
    [InlineData("ProjectsPage.cs")]
    public void Pages_KeepTheirOwnEmptyStateAndRealNumbers(string file)
    {
        // 两页同时给出「为什么空」与真实数字（空态标题/提示 + 状态行「筛选出 N（共 M）」），本轮不动它们，
        // 也不把对话框那句话搬过去——它们的下一步是「清空筛选」按钮，不是换一个筛选。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.DoesNotContain("FilteredEmptyHint", source, StringComparison.Ordinal);
        Assert.Contains("ListFilter.IsActive", source, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsFilteredEmptyStateRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**筛到零行**（`ListFilter.IsFilteredEmpty` 单一口径，2026-11-08 第五十九轮）", section);
        Assert.Contains("整份数据没有条目", section);
        Assert.Contains("不随筛选变化", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 筛到零行的状态完备（§7） \|", ledger);
        Assert.Contains("三种结果", ledger);
        Assert.Contains("清完队列的凭据", ledger);
        Assert.Contains("第五十九轮", ledger);
    }

    [Fact]
    public void DesignDoc_RecordsTheSupersededDeferralWithItsReason()
    {
        // 台账留着「当时为什么不接」——那是本轮自有形状的来源；改口成「本来就该接」等于把判断过程抹掉。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 筛到零行的状态完备（§7） |", "\n| 注释缺口筛到零行的自有形状（§7） |");
        Assert.Contains("它的提示出口一并藏起注释覆盖率行", row, StringComparison.Ordinal);
        Assert.Contains("已于第六十轮收口", row, StringComparison.Ordinal);
        Assert.DoesNotContain("注释缺口对话框不接此口径", row, StringComparison.Ordinal);
        // §6 是规范性条款：被取代的挂账句留在正文里，下一轮会照着旧句实现。
        var section = Repo.DesignSection(6);
        Assert.DoesNotContain("注释缺口对话框本轮如实不接", section, StringComparison.Ordinal);
        Assert.DoesNotContain("要先有它自己的形状", section, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    /// <summary>出现该字面量的代码行数（注释行跳过：注释里引用这句话是说明理由，不是第二套拼写）。</summary>
    private static int CodeLinesContaining(string text, string fragment) =>
        text.Split('\n').Count(l =>
        {
            var trimmed = l.TrimStart();
            return trimmed.Contains(fragment, StringComparison.Ordinal)
                && !trimmed.StartsWith("//", StringComparison.Ordinal)
                && !trimmed.StartsWith("*", StringComparison.Ordinal);
        });

    private static string NounValue(string constantName)
    {
        var field = typeof(ListFilter).GetField(constantName, BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(field);
        return (string?)field!.GetValue(null) ?? "";
    }

    /// <summary>提示出口的方法体（含签名行：缺省值也要被钉住）。</summary>
    private static string HintExit(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var body = file switch
        {
            "CandidatesDialog.cs" => Slice(source, "private void ShowHint", "/// <summary>计数行按全部候选统计"),
            "ContractsDialog.cs" => Slice(source, "private void ShowHint", "/// <summary>计数行按全部契约统计"),
            "DecisionsDialog.cs" => Slice(source, "private void ShowHint", "/// <summary>计数行按全部条目统计"),
            _ => Slice(source, "private void ShowHint", "private void ShowDetail"),
        };
        Assert.Matches(@"(?m)^\s*private void ShowHint\(string message, Color color(?:, bool keepAggregate = false)?\)$", body);
        return body;
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
