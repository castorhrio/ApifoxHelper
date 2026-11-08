using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「接受并下一条」落点断言（第一百一十一轮，DESIGN.md §6「候选文字编辑」的「接受并下一条」条款）：
/// 冻结页面的候选卡片写的就是「接受并下一条」——接受一条之后选中交给下一条，即使这一条仍在当前筛选里
/// （停在「全部候选」上时它不会消失）也不留在原处；搁置 / 驳回 / 保存修改 / 撤销接受仍留在原处
/// （那几步的下一步是看这一条的结果，不是下一条）。
/// 落点数学收在 <see cref="ListAdvance.NextAfterAccept"/> 一处（不依赖 WinForms、与测试工程共享编译），
/// 触达界面的落点只在 <c>Ui.AdvanceAfterAccept</c> 一处、选中仍经既有唯一入口 <c>Ui.SelectItem</c>；
/// 对话框不自己算落点、不自己写「接受」这个状态字面。
/// 真机上的选中落点与滚动对位随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class CandidateAcceptAdvanceTests
{
    // ---------- 口径真值表 ----------

    [Theory]
    // 这一条还在清单上：落到它的下一行（不留在原处）。
    [InlineData(0, 0, 9, 1)]
    [InlineData(3, 3, 9, 4)]
    [InlineData(37, 37, 62, 38)]
    public void NextAfterAccept_StillListed_LandsOnTheRowBelow(int handledIndex, int newIndex, int displayedCount, int expected)
    {
        Assert.Equal(expected, ListAdvance.NextAfterAccept(handledIndex, newIndex, displayedCount));
    }

    [Theory]
    // 本来就在末行：下一行不存在，留在末行（不借第一行、不越界）。
    [InlineData(8, 8, 9, 8)]
    [InlineData(0, 0, 1, 0)]
    public void NextAfterAccept_LastRow_StaysOnTheLastRow(int handledIndex, int newIndex, int displayedCount, int expected)
    {
        Assert.Equal(expected, ListAdvance.NextAfterAccept(handledIndex, newIndex, displayedCount));
    }

    [Theory]
    // 已不在当前筛选上（例如停在「待审阅」上接受）：落到接替他位置的那一行——与 IndexAfterRemoval 同一落点。
    [InlineData(3, 9)]
    [InlineData(0, 9)]
    [InlineData(9, 9)]
    [InlineData(20, 3)]
    public void NextAfterAccept_NoLongerListed_LandsWhereTheRemovalRuleLands(int handledIndex, int displayedCount)
    {
        Assert.Equal(
            ListAdvance.IndexAfterRemoval(handledIndex, displayedCount),
            ListAdvance.NextAfterAccept(handledIndex, ListAdvance.None, displayedCount));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, ListAdvance.None)]
    public void NextAfterAccept_NoLandingWhenNothingIsDisplayed(int handledIndex, int newIndex)
    {
        // 一行不剩：没有落点，空筛选的下一步由各视图自己的提示出口承担。
        Assert.Equal(ListAdvance.None, ListAdvance.NextAfterAccept(handledIndex, newIndex, 0));
    }

    [Fact]
    public void NextAfterAccept_UnknownHandledRow_ContinuesFromTheTop()
    {
        // 旧清单上本来就没有选中（例如刚切换筛选）：从队列开头继续，不猜中间位置。
        Assert.Equal(0, ListAdvance.NextAfterAccept(ListAdvance.None, ListAdvance.None, 5));
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void UiFactory_HoldsTheOnlyTouchpointAndUsesTheSharedRule()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Single(Regex.Matches(ui, @"(?m)^\s*public static void AdvanceAfterAccept\(ListView list, int handledIndex, int newIndex\)"));
        var body = Slice(ui, "public static void AdvanceAfterAccept(ListView list, int handledIndex, int newIndex)", "/// <summary>");
        // 落点取共享口径，界面不另算一套；没有落点即不动作。
        Assert.Matches(@"(?m)^\s*var index = ListAdvance\.NextAfterAccept\(handledIndex, newIndex, list\.Items\.Count\);$", body);
        Assert.Matches(@"(?m)^\s*if \(index < 0\) return;$", body);
        // 选中经唯一入口（Selected + Focused 一起恢复，方向键从该行继续）。
        Assert.Matches(@"(?m)^\s*SelectItem\(list\.Items\[index\]\);$", body);
        Assert.DoesNotContain("Math.Min", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*item\.(Selected|Focused) = ", body);
    }

    [Fact]
    public void Dialog_AcceptsAndAdvancesThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", "CandidatesDialog.cs");
        // 主按钮文案与冻结页面候选卡片同一句：按钮承诺的就是接下来发生的事。
        Assert.Matches(@"(?m)^\s*_acceptButton = Ui\.Primary\(""接受并下一条""\);", source);
        // 只有接受走「下一条」：搁置 / 驳回 / 保存修改 / 撤销接受留在原处。
        Assert.Matches(@"(?m)^\s*advanceToNext: reviewState == ""accepted""\);$", source);
        Assert.Single(Regex.Matches(source, @"advanceToNext:"));
        // 落点经唯一入口，对话框不自己算下一行。
        Assert.Single(Regex.Matches(source, @"Ui\.AdvanceAfterAccept\(_list, handledAt, landedAt \?\? ListAdvance\.None\);"));
        // 落点数学只在共享口径一处：对话框不调用它（注释里的 <see cref> 是引用不是调用）。
        Assert.DoesNotMatch(@"ListAdvance\.NextAfterAccept\(", source);
        Assert.DoesNotMatch(@"(?m)^\s*_list\.Items\[[^\]]*\]\.(Selected|Focused) = ", source);
    }

    [Fact]
    public void Dialog_LandsAfterTheRebuildAndKeepsTheRemovalBranch()
    {
        var body = Slice(
            Repo.Source("Desktop", "Native", "CandidatesDialog.cs"),
            "private async void RunCandidateUpdate(CandidateUpdateRequest request, Func<string> doneText, bool advanceToNext = false)",
            "private static Color ToneColor(string tone)");
        var rebuild = body.IndexOf("ApplyFilter();", StringComparison.Ordinal);
        var accept = body.IndexOf("Ui.AdvanceAfterAccept(_list, handledAt, landedAt ?? ListAdvance.None);", StringComparison.Ordinal);
        var removal = body.IndexOf("Ui.AdvanceAfterRemoval(_list, handledAt);", StringComparison.Ordinal);
        // 落点必须排在清单重建之后（否则数的是旧行数），且两条分支都在重建之后。
        Assert.True(rebuild >= 0 && accept > rebuild, "接受并下一条的落点必须排在清单重建之后。");
        Assert.True(removal > rebuild, "续接落点必须排在清单重建之后。");
        // 其余动作仍走「消失才续接」那一支：接受分支不得吞掉它。
        Assert.Matches(@"(?m)^\s*else if \(!kept\)$", body);
        // 交回键盘在落点之后（方向键从落点那一行继续）。
        var keyboard = body.IndexOf("Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        Assert.True(keyboard > accept && keyboard > removal, "交回键盘必须排在落点之后。");
    }

    [Fact]
    public void SharedCompile_IncludesListAdvance()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListAdvance.cs", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsAcceptAndAdvance()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("接受并下一条", section);
        Assert.Contains("NextAfterAccept", section);
        var gaps = Repo.DesignSection(8);
        Assert.Contains("接受并下一条", gaps);
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少 " + start);
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, "源码缺少 " + start + " 或其后续锚点 " + end);
        return source[from..to];
    }
}
