using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「注释缺口筛到零行的自有形状」断言（DESIGN.md §6「列表筛选」、§7「禁用不得移除下一步」、§8 本轮条款）。
/// 第五十九轮把筛到零行的判定与文案收成一条（<see cref="ListFilter.IsFilteredEmpty"/> 与
/// <see cref="ListFilter.FilteredEmptyHint"/>），四处视图接上、注释缺口清单如实挂账未接——理由是它的提示出口
/// <c>ShowHint</c> 一并藏起注释覆盖率行与「分析警告与降级」「接口追踪明细」两个聚合入口，直接套同一个出口
/// 等于把这一步之后还能看的东西一起删掉。本轮给这一屏它自己的形状：只把列表区换成那句话，
/// 整份数据的聚合入口一概不动，筛选器保持可用，详情区与三个选中动作经既有的「无选中」路径
/// （<c>ShowDetail</c>）收掉而不另写一套；「整份数据不可得」那条出口逐字不变。
/// 判定与句子跑的是主工程同一份代码（ListFilter.cs 与测试工程共享编译）；
/// 真机上「筛到零行时那句话显示出来、覆盖率行与计数行仍在、两个聚合入口还按得动」随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ScanResultsFilteredEmptyTests
{
    private const string File = "ScanResultsDialog.cs";

    // ---------- 句子：跑共享口径的真代码 ----------

    [Fact]
    public void SlotNoun_IsTheNounThisListReads()
    {
        // 列表首列「位置（说明点）」、筛选项「全部位置」、计数行「位置 N」讲的是同一批条目（DocSlot）。
        Assert.Equal("注释位置", ListFilter.SlotNoun);
        Assert.Equal("这个筛选下没有注释位置。换一个筛选再试。", ListFilter.FilteredEmptyHint(ListFilter.SlotNoun));
    }

    [Fact]
    public void Hint_EndsWithTheSingleSharedPointer()
    {
        // 下一句不在本视图拼写：与前四处同一个收尾（第四十二轮起它就是恢复路径）。
        Assert.EndsWith(ListFilter.FilteredEmptyPointer, ListFilter.FilteredEmptyHint(ListFilter.SlotNoun), StringComparison.Ordinal);
    }

    [Fact]
    public void SlotNoun_IsUsedByThisViewAlone()
    {
        // 各视图的名词不串用：把「注释位置」搬到契约或差异确认那一屏，说的就不是那一屏的条目了。
        var users = Repo.NativeSources()
            .Where(s => s.Contains("SlotNoun"))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "ListFilter.cs", File }, users);
    }

    // ---------- 接线：判定在清单重建之后，分支先返回、回显在后 ----------

    [Fact]
    public void ApplyFilter_JudgesWithTheSharedPredicateAfterRebuild()
    {
        var body = ApplyFilterBody();
        Assert.Single(Regex.Matches(body, @"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\("));
        Assert.Matches(@"(?m)^\s*if \(ListFilter\.IsFilteredEmpty\(_slots\.Count, _list\.Items\.Count\)\)$", body);
        // 重建之后才判：判在清空之前，量到的是上一轮筛选的行数。
        var rebuild = body.IndexOf("_list.EndUpdate();", StringComparison.Ordinal);
        var judged = body.IndexOf("ListFilter.IsFilteredEmpty(", StringComparison.Ordinal);
        Assert.True(rebuild >= 0 && judged > rebuild, "判定要排在清单重建之后。");
        // 分支先返回、回显在后面（顺序反过来等于那句话刚显示就被自己抹掉）。
        var branch = body.IndexOf("ShowFilteredEmpty();", StringComparison.Ordinal);
        var restore = body.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(branch >= 0 && restore > branch, "提示分支要排在列表回显之前。");
        Assert.Matches(@"(?m)^\s*return;$", body);
        Assert.Matches(@"(?m)^\s*_list\.Visible = true;$", body);
    }

    [Fact]
    public void ApplyFilter_BranchDelegatesToTheOneExit_AndSpellsNothingThere()
    {
        var body = ApplyFilterBody();
        Assert.DoesNotContain("FilteredEmptyHint", body, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(body, "ShowFilteredEmpty\\(\\);"));
        // 分支里不收控件、不抹聚合数字：那些都在出口一处给（两处写必然漂移）。
        Assert.DoesNotContain("_counts", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*_filter\.Enabled", body);
        Assert.DoesNotMatch(@"(?m)^\s*_(coverage|warningsButton|traceButton)\.Visible", body);
        // 本视图不套前四处的 keepAggregate 参数——它有自己的形状（见 ShowFilteredEmpty）。
        Assert.DoesNotContain("keepAggregate", body, StringComparison.Ordinal);
    }

    // ---------- 自有形状：只换列表区，整份数据的入口一概不动 ----------

    [Fact]
    public void FilteredEmptyShape_KeepsEveryAggregateEntryAndTheFilterUsable()
    {
        var body = ShapeBody();
        // 这一屏有关键词框了（第六十六轮）：那句话的下一步因此有两个真实入口（关键词或筛选），
        // 关键词为空时仍只指筛选器——文字不指向界面上按不动的控件。
        Assert.Matches(@"(?m)^\s*_hint\.Text = ListFilter\.FilteredEmptyHint\(ListFilter\.SlotNoun, ListFilter\.IsActive\(_keyword\.Text\)\);$", body);
        Assert.Matches(@"(?m)^\s*_hint\.ForeColor = UiTokens\.Muted;$", body);
        Assert.Matches(@"(?m)^\s*_hint\.Visible = true;$", body);
        Assert.Matches(@"(?m)^\s*_list\.Visible = false;$", body);
        // 这些一概不动：它们讲的是整份扫描，不因当前筛选不命中而失效（§7「禁用不得移除下一步」）。
        foreach (var untouched in new[] { "_filter", "_counts", "_coverage", "_warningsButton", "_traceButton", "_degradations" })
        {
            Assert.DoesNotContain(untouched, body, StringComparison.Ordinal);
        }
        // 关键词框是那句话指名的另一半入口，它同样得保持按得动：这一支不碰它。
        Assert.DoesNotMatch(@"(?m)^\s*_keyword\.Enabled", body);
    }

    [Fact]
    public void FilteredEmptyShape_DisablesSelectionActionsThroughTheExistingNoSelectionPath()
    {
        var body = ShapeBody();
        // 「没有选中行怎么收掉」界面上已经有一条路（ShowDetail 的空选中分支：清详情、禁用三个选中动作）；
        // 在出口里再写一遍就是第二套，两套必然漂移。
        Assert.Single(Regex.Matches(body, @"(?m)^\s*ShowDetail\(\);$"));
        foreach (var ownedByThatPath in new[] { "_evidenceButton", "_manualButton", "_removeStaleButton", "_detail" })
        {
            Assert.DoesNotContain(ownedByThatPath, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FilteredEmptyExit_IsCalledFromTheBranchOnly()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, "ShowFilteredEmpty\\(\\);"));
        Assert.Single(Regex.Matches(source, @"(?m)^\s*private void ShowFilteredEmpty\(\)$"));
    }

    // ---------- 两种空的两条出口不合并 ----------

    [Fact]
    public void TheNoDataExit_StillTearsTheAggregateDown()
    {
        // 读取失败与没有扫描产物时，覆盖率、降级与追踪本身也不可得，随提示一起收掉才是如实。
        var body = Slice("private void ShowHint(string message, Color color)", "/// <summary>计数行按全部位置统计");
        Assert.DoesNotContain("keepAggregate", body, StringComparison.Ordinal);
        Assert.DoesNotContain("FilteredEmptyHint", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowFilteredEmpty", body, StringComparison.Ordinal);
        foreach (var teardown in new[]
                 {
                     "_coverage.Text = \"\";",
                     "_warningsButton.Visible = false;",
                     "_traceButton.Visible = false;",
                     "_counts.Text = \"\";",
                     "_filter.Enabled = false;",
                     "_list.Visible = false;",
                 })
        {
            Assert.Contains(teardown, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FilterIsDisabled_OnTheNoDataExitOnly()
    {
        // 筛到零行不动筛选器（那句话指名的下一步就是它，第四十二轮）：全文件禁用筛选器只有那一条路。
        Assert.Single(Regex.Matches(Repo.Source("Desktop", "Native", File), @"_filter\.Enabled = false"));
    }

    [Fact]
    public void CountsRow_KeepsItsOwnSentence_ForTheDataEmptyCase()
    {
        // 「没有注释位置」讲的是整份数据（_slots.Count == 0 时判定为假，列表区不给那句话），
        // 它是计数行的文案，本轮逐字不动，也不与列表区那句话合并。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, "\"没有注释位置\""));
        Assert.DoesNotContain("这个筛选下", source, StringComparison.Ordinal);
        Assert.DoesNotContain("换一个筛选再试", source, StringComparison.Ordinal);
        // 判定不在计数行了结这一屏：整份数据没有条目时走的仍是原路径（列表空、无提示）。
        Assert.DoesNotContain("IsFilteredEmpty", Slice("private void UpdateCounts()", "private bool Matches"), StringComparison.Ordinal);
    }

    [Fact]
    public void StateTable_RecordsTheFilteredEmptyState()
    {
        // §7 每屏必交状态表：这一屏的空态此前不在状态表里。
        var summary = Slice("/// 注释缺口清单：", "internal sealed class ScanResultsDialog");
        Assert.Contains("筛到零行", summary, StringComparison.Ordinal);
        Assert.Contains("ShowFilteredEmpty", summary, StringComparison.Ordinal);
        Assert.Contains("整份数据不可得", summary, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheOwnShapeRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("自有形状", section);
        Assert.Contains("ShowFilteredEmpty", section);
        Assert.Contains("两种空各留一条出口", section);
        Assert.Contains("第六十轮", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 注释缺口筛到零行的自有形状（§7） \|", ledger);
    }

    [Fact]
    public void DesignDoc_RecordsWhatThisRoundDidNotDo()
    {
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 注释缺口筛到零行的自有形状（§7） |", "\n| 输入框（§6） |");
        Assert.Contains("已接线（2026-11-08 第六十轮", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        // 没做的事不能被写成做了。
        Assert.Contains("提示语不加动作按钮", row, StringComparison.Ordinal);
        Assert.DoesNotContain("计数行文案统一", row, StringComparison.Ordinal);
        Assert.DoesNotContain("两条出口已合并", row, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static string ApplyFilterBody() => Slice("private void ApplyFilter(", "private void ShowFilteredEmpty()");

    private static string ShapeBody() => Slice("private void ShowFilteredEmpty()", "private void ShowDetail()");

    private static string Slice(string start, string end) => Slice(Repo.Source("Desktop", "Native", File), start, end);

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
