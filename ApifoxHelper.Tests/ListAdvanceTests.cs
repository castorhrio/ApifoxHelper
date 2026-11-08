using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 审阅队列选中续接断言（DESIGN.md §6「列表行」、§8 本轮条款）：三个逐条处理的审阅视图
/// （注释候选、响应契约、差异确认）都有「待审阅/待确认/待处理」筛选，队列几十上百条时用户就停在那个筛选上逐条清；
/// 每清一条，这一行不再属于当前筛选、从清单上消失，此前的落点是三种不同的错法——候选与契约两处选中直接丢失
/// （详情清空、审阅按钮禁用，必须回鼠标点下一行），差异确认则回跳队首第一行（等于每保存一条都要重新找位置）。
/// 口径收成一处 <see cref="ListAdvance"/>（不依赖 WinForms，与测试工程共享编译）：选中落到**接替他位置的那一行**
/// （原显示第 N 行，重建后第 N 行就是下一行；本来在末行则落到上一行；一行不剩则没有落点，不借第一行），
/// 取 Outlook/资源管理器「删除或归档当前项后选中相邻下一项」的既有惯例，不新造视觉语言、不加控件与提示。
/// 触达界面的落点只在 <c>Ui.AdvanceAfterRemoval</c> 一处、选中仍经既有唯一入口 <c>Ui.SelectItem</c>；
/// 各对话框不自己算落点（本轮三处接线由行首正则、方法体切片与顺序断言钉住）。
/// 真机上的选中落点与滚动对位随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListAdvanceTests
{
    // ---------- 口径真值表 ----------

    [Theory]
    // 中间行：重建后第 handledIndex 行就是原来的下一行。
    [InlineData(3, 9, 3)]
    [InlineData(0, 9, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(37, 62, 37)]
    public void IndexAfterRemoval_LandsOnTheRowThatTookItsPlace(int handledIndex, int displayedCount, int expected)
    {
        Assert.Equal(expected, ListAdvance.IndexAfterRemoval(handledIndex, displayedCount));
    }

    [Theory]
    // 本来在末行：下一行不存在，落到上一行（不越界）。
    [InlineData(9, 9, 8)]
    [InlineData(0, 1, 0)]
    [InlineData(4, 4, 3)]
    public void IndexAfterRemoval_LastRow_GoesToTheRowAbove(int handledIndex, int displayedCount, int expected)
    {
        Assert.Equal(expected, ListAdvance.IndexAfterRemoval(handledIndex, displayedCount));
    }

    [Theory]
    // 不止一行消失（同一次保存改动多条的归属）时取末行，绝不越出清单边界。
    [InlineData(7, 3, 2)]
    [InlineData(20, 1, 0)]
    public void IndexAfterRemoval_ClampsToTheLastRowWhenTheListShrankMore(int handledIndex, int displayedCount, int expected)
    {
        Assert.Equal(expected, ListAdvance.IndexAfterRemoval(handledIndex, displayedCount));
    }

    [Theory]
    // 一行不剩：没有落点。空筛选的下一步是各视图自己的提示出口（差异确认见第四十二轮），不借第一行、不选中不存在的一行。
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void IndexAfterRemoval_NoLandingWhenNothingIsDisplayed(int handledIndex)
    {
        Assert.Equal(ListAdvance.None, ListAdvance.IndexAfterRemoval(handledIndex, 0));
    }

    [Fact]
    public void IndexAfterRemoval_UnknownLanding_ContinuesFromTheTop()
    {
        // 旧清单上本来就没有选中（例如刚切换筛选）时从队列开头继续——与既有的「切换筛选后落在新筛选第一行」一致，不猜中间位置。
        Assert.Equal(0, ListAdvance.IndexAfterRemoval(ListAdvance.None, 5));
    }

    [Fact]
    public void None_IsNotAValidRowIndex()
    {
        // 「没有落点」用负数表示，与控件行序号不重叠；界面据此不动作。
        Assert.True(ListAdvance.None < 0);
        Assert.Equal(-1, ListAdvance.None);
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void ReviewDialogs_CaptureTheHandledRowBeforeSavingAndAdvanceWhenItIsGone()
    {
        foreach (var (file, selectName, awaitPattern) in new[]
        {
            ("CandidatesDialog.cs", "SelectCandidate", @"await _drafts\.UpdateAsync\(_task\.Id, candidate\.Id, update, CancellationToken\.None\);"),
            ("ContractsDialog.cs", "SelectContract", @"await _reviews\.ReviewAsync\(_task\.Id, endpointKey, reviewState, CancellationToken\.None\);"),
        })
        {
            var body = Slice(Repo.Source("Desktop", "Native", file),
                "private async void RunReview(string reviewState)",
                file == "CandidatesDialog.cs" ? "/// 导入编码助手结果：选择结果 JSON" : $"private bool {selectName}(");
            // 记住的是被处理那一行在旧清单上的显示序号：取自选中行本身，不另猜焦点行，
            // 且经 Ui.SelectedIndex 唯一入口——界面不自己索引选中集合（第六十一轮）。
            Assert.Matches(@"(?m)^\s*var handledAt = Ui\.SelectedIndex\(_list\) \?\? ListAdvance\.None;$", body);
            // 命中仍回到该条（原行为逐字保留），未命中才续接下一行。
            Assert.Matches(@"(?m)^\s*if \(!" + selectName + @"\(\w+(?:\.Id)?\)\) Ui\.AdvanceAfterRemoval\(_list, handledAt\);$", body);
            // 顺序：先记序号（await 期间用户可能移动选中，之后取的就是别的行的位置）→ 重建清单 → 再判落点。
            var capture = body.IndexOf("var handledAt = Ui.SelectedIndex(_list) ?? ListAdvance.None;", StringComparison.Ordinal);
            var save = Regex.Match(body, awaitPattern).Index;
            Assert.True(capture >= 0 && save > capture, $"{file}:序号必须在保存动作之前取。");
            var rebuild = body.IndexOf("ApplyFilter();", StringComparison.Ordinal);
            var advance = body.IndexOf("Ui.AdvanceAfterRemoval(_list, handledAt);", StringComparison.Ordinal);
            Assert.True(rebuild >= 0 && advance > rebuild, $"{file}:续接必须排在清单重建之后，否则数的是旧行数。");
        }
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", "SelectCandidate", "CommentCandidate candidate && candidate.Id == candidateId")]
    [InlineData("ContractsDialog.cs", "SelectContract", "ResponseContract contract && contract.EndpointKey == endpointKey")]
    public void SelectionHelpers_ReportWhetherTheRowSurvived(string file, string name, string matchPattern)
    {
        // 返回「是否还在清单上」是续接的判据：命中分支 return true、落空分支 return false，不静默什么都不说。
        var body = Slice(Repo.Source("Desktop", "Native", file), $"private bool {name}(", "/// <summary>");
        Assert.Matches(@"(?m)^\s*if \(item\.Tag is " + matchPattern + @"\)$", body);
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);\s*\n\s*return true;$", body);
        Assert.Matches(@"(?m)^\s*return false;\s*\}$", body);
    }

    [Fact]
    public void DecisionsDialog_KeepsAnInPlaceLandingInsteadOfJumpingToTheTop()
    {
        var source = Repo.Source("Desktop", "Native", "DecisionsDialog.cs");
        // 落点参数有缺省值：切换筛选与打开时不传，仍走既有的「新筛选第一条」。
        Assert.Matches(@"(?m)^\s*private void ApplyFilter\(Entry\? keep = null, int handledIndex = ListAdvance\.None\)$",
            Slice(source, "private void ApplyFilter(", "private Entry? Current()"));
        var body = Slice(source, "if (keep is not null)", "private Entry? Current()");
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);\s*\n\s*return;$", body);
        // 刚处理的那条已不在清单上：续接后返回，不得再落到末尾的「选中第一行」。
        Assert.Matches(@"(?m)^\s*Ui\.AdvanceAfterRemoval\(_list, handledIndex\);\s*\n\s*return;$", body);
        var advance = body.IndexOf("Ui.AdvanceAfterRemoval(_list, handledIndex);", StringComparison.Ordinal);
        var firstRow = body.IndexOf("Ui.SelectItem(_list.Items[0]);", StringComparison.Ordinal);
        Assert.True(advance >= 0 && firstRow > advance, "DecisionsDialog:未命中分支必须早于「选中第一行」返回。");
        // 保存路径把序号交进去；筛选切换路径不交（那里没有「刚处理的一条」）。
        Assert.Matches(@"(?m)^\s*ApplyFilter\(entry, handledAt\);$", source);
        Assert.Matches(@"(?m)^\s*var handledAt = Ui\.SelectedIndex\(_list\) \?\? ListAdvance\.None;$", source);
        Assert.Matches(@"(?m)^\s*_filter\.SelectedIndexChanged \+= \(_, _\) => ApplyFilter\(Current\(\)\);$", source);
    }

    [Fact]
    public void UiFactory_HoldsTheOnlyTouchpointAndUsesTheSharedRule()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Single(Regex.Matches(ui, @"(?m)^\s*public static void AdvanceAfterRemoval\(ListView list, int handledIndex\)"));
        var body = Slice(ui, "public static void AdvanceAfterRemoval(ListView list, int handledIndex)", "/// <summary>");
        // 落点取共享口径，界面不另算一套；没有落点即不动作。
        Assert.Matches(@"(?m)^\s*var index = ListAdvance\.IndexAfterRemoval\(handledIndex, list\.Items\.Count\);$", body);
        Assert.Matches(@"(?m)^\s*if \(index < 0\) return;$", body);
        // 选中经唯一入口（Selected + Focused 一起恢复，方向键从该行继续）。
        Assert.Matches(@"(?m)^\s*SelectItem\(list\.Items\[index\]\);$", body);
        Assert.DoesNotContain("Math.Min", ui, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*item\.(Selected|Focused) = ", body);
    }

    [Fact]
    public void Touchpoints_AreExactlyTheThreeReviewDialogs()
    {
        // 队列式逐条处理的视图只有这三个；主页面列表与只读清单不在此列（按 Id 恢复选中的既有口径不变）。
        var wired = Repo.NativeSources()
            .Where(s => s.Contains("Ui.AdvanceAfterRemoval("))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[] { "CandidatesDialog.cs", "ContractsDialog.cs", "DecisionsDialog.cs" },
            wired);
        foreach (var name in new[] { "TasksPage.cs", "ProjectsPage.cs", "CheckResultsDialog.cs", "SyncPreviewDialog.cs", "SupplementsDialog.cs" })
        {
            Assert.DoesNotContain("AdvanceAfterRemoval", Repo.Source("Desktop", "Native", name), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("DecisionsDialog.cs")]
    public void Dialogs_DoNotComputeTheLandingThemselves(string file)
    {
        // 落点数学只在 ListAdvance 与 Ui 各一处：对话框不得自己 clamp、自己数行数、或绕过唯一入口写选中。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.DoesNotContain("IndexAfterRemoval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^\s*_list\.Items\[(handledAt|index|next)\]", source);
        Assert.DoesNotMatch(@"(?m)^\s*_list\.Items\[[^\]]*\]\.(Selected|Focused) = ", source);
    }

    [Fact]
    public void SharedCompile_IncludesListAdvance()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListAdvance.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListAdvance.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsQueueContinuation()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("接替他位置的下一行", section);
        Assert.Contains("`ListAdvance`", section);
        Assert.Contains("不回跳队首", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("审阅队列选中续接", ledger);
        Assert.Contains("ListAdvance", ledger);
        Assert.Contains("第五十六轮", ledger);
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
