using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 键盘归属断言（DESIGN.md §6「列表行」、§8 本轮条款）：第五十六轮的续接落点与第五十七轮的选中即定位
/// 都写着「方向键从该行继续」，可这句话的前提——清单控件持有键盘——此前没有一处满足：
/// Selected 与 Focused 是清单内部的项级标记，而三个审阅视图的处理动作都是点按钮完成的，
/// 焦点停在那个按钮上，方向键动的是按钮所在的控件，不是队列。
/// 代码可核对的事实：全原生界面的 <c>.Focus()</c> 共六处，都在两个主页——四处交给筛选框（Ctrl+F、清空筛选后回框），
/// 两处交给清单（第五十一轮「筛选框 Enter 到结果第一行」的先取焦点，两页各一处），21 个对话框类一处都没有——
/// 同一条规矩在两页成立、在队列上不成立。
/// 现收成一条口径：判定在 <see cref="ListFocus"/>（不依赖 WinForms、与测试工程共享编译），
/// 触达界面只在 <c>Ui.KeepKeyboardOnQueue</c> 一处，接在三处保存成功路径的落点之后（第八十八轮又接到五张只读长清单的打开时，见 OpenFocusTests）；
/// 保存被拒与重新读取失败的路径不交回（用户要改的就是手里那一格），重建清单的路径不交回（用户可能正在筛选框里打字）。
/// 键盘交回后方向键的实际路由随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListFocusTests
{
    // ---------- 口径真值表：这一行是不是键盘该去的那一行 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(59)]
    public void Queue_TakesKeyboardWhenSelectedAndFocusedRowsAreTheSameRow(int index)
    {
        Assert.True(ListFocus.ShouldHandToQueue(index, index));
    }

    [Theory]
    // 焦点行缺失：清单拿到键盘后方向键没有起点，不猜第一行。
    [InlineData(0)]
    [InlineData(3)]
    public void Queue_KeepsKeyboardWhenFocusedRowIsMissing(int? selectedIndex)
    {
        Assert.False(ListFocus.ShouldHandToQueue(selectedIndex, null));
    }

    [Theory]
    // 选中行缺失：详情区与审阅按钮跟着的是选中行，没有选中行就没有「刚处理完一条」这回事。
    [InlineData(null, 0)]
    [InlineData(null, 3)]
    [InlineData(null, null)]
    public void Queue_KeepsKeyboardWhenNoRowIsSelected(int? selectedIndex, int? focusedIndex)
    {
        Assert.False(ListFocus.ShouldHandToQueue(selectedIndex, focusedIndex));
    }

    [Theory]
    // 选中行与焦点行不是同一行：交回键盘会让方向键从另一行继续，而详情与审阅按钮跟着选中的那一行——
    // 交出去的那一条不是屏幕上正在看的那一条，比不交更糟。
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    [InlineData(0, 1)]
    public void Queue_KeepsKeyboardWhenTheTwoRowsDiffer(int? selectedIndex, int? focusedIndex)
    {
        Assert.False(ListFocus.ShouldHandToQueue(selectedIndex, focusedIndex));
    }

    [Theory]
    // 行已离开控件时序号为负（ListViewItem.Index：未加入清单即 -1）；两个 -1 也不算落点——
    // 那不是「同一行」，是两样都不存在。
    [InlineData(-1, -1)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(int.MinValue, int.MinValue)]
    public void Queue_KeepsKeyboardForRowThatLeftTheList(int? selectedIndex, int? focusedIndex)
    {
        Assert.False(ListFocus.ShouldHandToQueue(selectedIndex, focusedIndex));
    }

    [Fact]
    public void Guard_KeepsOnlyTheRowTheQueueActuallyLandedOn()
    {
        // 逐点核对判定范围：放行当且仅当「序号非负且两行同一行」。
        // 多一分是把键盘交给一个没有落点的清单（方向键从第一行开始，那是猜落点），
        // 少一分是处理完一条后键盘留在按钮上——本条口径要修的正是这件事。
        for (var selected = -3; selected <= 43; selected++)
        {
            for (var focused = -3; focused <= 43; focused++)
            {
                var expected = selected >= 0 && selected == focused;
                Assert.Equal(expected, ListFocus.ShouldHandToQueue(selected, focused));
            }
        }
    }

    // ---------- 工厂入口：只交键盘，不代替别的唯一入口 ----------

    /// <summary>KeepKeyboardOnQueue 的方法体（无花括号的线性序列，切片取到方法末尾的右括号为止）。</summary>
    private static string KeepBody()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var helper = Regex.Match(ui, @"public static void KeepKeyboardOnQueue\(ListView list\)\s*\{(?<body>[^}]*)\}");
        Assert.True(helper.Success, "Ui 缺少 KeepKeyboardOnQueue（审阅完成后把键盘交回清单的唯一入口）。");
        return helper.Groups["body"].Value;
    }

    [Fact]
    public void Entry_JudgesBySharedRuleBeforeHandingTheKeyboard()
    {
        var body = KeepBody();
        Assert.Matches(@"(?m)^\s*var selected = SelectedIndex\(list\);$", body);
        Assert.Matches(@"(?m)^\s*if \(!ListFocus\.ShouldHandToQueue\(selected, list\.FocusedItem\?\.Index\)\) return;$", body);
        Assert.Matches(@"(?m)^\s*list\.Focus\(\);$", body);
        // 先判定后交：不满足条件时键盘停在原处。
        var judge = body.IndexOf("ListFocus.ShouldHandToQueue(selected, list.FocusedItem?.Index)", StringComparison.Ordinal);
        var hand = body.IndexOf("list.Focus();", StringComparison.Ordinal);
        Assert.True(judge >= 0 && hand > judge, "应先由共享口径判定，再交回键盘。");
    }

    [Fact]
    public void Entry_TouchesNothingElse()
    {
        // 选中、滚动、落点数学各有自己的唯一入口，本处只把控件键盘焦点交给清单；
        // 在这里顺手写第二套必然与那一套漂移（第五十二、五十三轮的「剪贴板只触达一次」同法）。
        var body = KeepBody();
        Assert.DoesNotMatch(@"(?m)^\s*\S+\.(Selected|Focused) = ", body);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexAfterRemoval", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
    }

    [Fact]
    public void KeyboardHandoff_IsTouchedOnce()
    {
        // Ui.cs 里把键盘交给清单的入口恰两个、各一处：本轮的「处理完一条交回队列」（KeepKeyboardOnQueue）
        // 与第六十九轮的「筛选框 Enter 到结果第一行」（FocusFirstResult，两页与五张长清单共用）。
        // 两处都是「只交键盘」的入口，谁也不在别处再写一句 list.Focus()。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Equal(2, Regex.Matches(ui, @"(?m)^\s*list\.Focus\(\);$").Count);
        Assert.Single(Regex.Matches(KeepBody(), @"(?m)^\s*list\.Focus\(\);$"));
        var find = Regex.Match(ui, @"public static void FocusFirstResult\(ListView list\)\s*\{(?<body>[^}]*)\}");
        Assert.True(find.Success, "Ui 缺少 FocusFirstResult 口径。");
        Assert.Single(Regex.Matches(find.Groups["body"].Value, @"(?m)^\s*list\.Focus\(\);$"));
    }

    [Fact]
    public void SelectionEntry_DoesNotTakeTheKeyboardItself()
    {
        // 程序化选中遍布重建路径（用户在筛选框里打字时也在重建清单），选中入口自己不交控件焦点——
        // 交回只发生在「刚处理完一条」那一处，否则输入到一半键盘就跑了。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var helper = Regex.Match(ui, @"public static void SelectItem\(ListViewItem item\)\s*\{(?<body>[^}]*)\}");
        Assert.True(helper.Success, "Ui 缺少 SelectItem 程序选中口径。");
        Assert.DoesNotContain(".Focus(", helper.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void AdvanceEntry_DoesNotHandTheKeyboard()
    {
        // 续接落点只管「哪一行」；键盘由审阅路径完成后统一交回（命中与续接两条分支都要走到，
        // 写在落点之内只覆盖其中一条，又是一处同壳不同行为）。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(ui, "public static void AdvanceAfterRemoval(ListView list, int handledIndex)", "/// 把键盘交给清单（唯一触达点）");
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Judgment_LivesInTheSharedRuleOnly()
    {
        // 判定只有一份：界面侧调用它的地方只有工厂入口，页面与对话框不自己补一套序号比较。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListFocus.ShouldHandToQueue"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    // ---------- 三处审阅路径：落点之后才交，被拒分支不交 ----------

    [Theory]
    [InlineData("CandidatesDialog.cs", "private async void RunReview(string reviewState)", "/// 导入编码助手结果：选择结果 JSON", "Ui.AdvanceAfterRemoval(_list, handledAt);")]
    [InlineData("ContractsDialog.cs", "private async void RunReview(string reviewState)", "private bool SelectContract(string endpointKey)", "Ui.AdvanceAfterRemoval(_list, handledAt);")]
    [InlineData("DecisionsDialog.cs", "private async void SaveDecision(string description, Action<DecisionStore> mutate)", "private static DecisionStore CloneStore(DecisionStore store)", "ApplyFilter(entry, handledAt);")]
    public void ReviewPaths_HandKeyboardAfterTheLanding(string file, string signature, string endAnchor, string landing)
    {
        var body = Slice(Repo.Source("Desktop", "Native", file), signature, endAnchor);
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(_list\);$"));
        var at = body.IndexOf(landing, StringComparison.Ordinal);
        var hand = body.IndexOf("Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        Assert.True(at >= 0 && hand > at, $"{file}:应在选中落点定了之后交回键盘，不是之前。");
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("DecisionsDialog.cs")]
    public void RejectedSaves_LeaveTheKeyboardWithTheUser(string file)
    {
        // 保存被拒（文案未答、示例非法）与重新读取失败都不交回键盘：用户要改的就是手里那一格
        // （待确认问题的回答、说明冲突的选择、筛选器），把键盘交走等于把下一步从他手里拿走。
        var body = Repo.Source("Desktop", "Native", file);
        var rejects = Regex.Matches(body, @"catch \((?:UserException|Exception) ex\)\s*\{(?<b>[^}]*)\}");
        Assert.True(rejects.Count >= 2, $"{file}:应能取到两条被拒分支（业务拒绝与非预期异常），取不到就是切片漂了。");
        foreach (Match block in rejects)
        {
            Assert.DoesNotContain("KeepKeyboardOnQueue", block.Groups["b"].Value, StringComparison.Ordinal);
        }

        var reload = file switch
        {
            "ContractsDialog.cs" => @"if \(!ReloadContracts\(out var error\)\)\s*\{(?<b>[^}]*)\}",
            "DecisionsDialog.cs" => @"if \(!ReloadDecisions\(out var error\)\)\s*\{(?<b>[^}]*)\}",
            _ => null,
        };
        if (reload is null) return;
        var match = Regex.Match(body, reload);
        Assert.True(match.Success, $"{file}:缺少重新读取失败分支（写入成功而回读失败时如实说明，不显示成审阅失败）。");
        Assert.DoesNotContain("KeepKeyboardOnQueue", match.Groups["b"].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CandidatesDialog.cs", "private void ApplyFilter()", "private void ShowDetail()")]
    [InlineData("ContractsDialog.cs", "private void ApplyFilter()", "private void ShowDetail()")]
    [InlineData("DecisionsDialog.cs", "private void ApplyFilter(Entry? keep = null, int handledIndex = ListAdvance.None)", "private Entry? Current()")]
    public void RebuildingTheList_DoesNotTakeTheKeyboard(string file, string signature, string endAnchor)
    {
        // 切换筛选与打开时的重建也走这条路径，用户可能正把焦点放在筛选器上准备换一个分类——
        // 交回只发生在「刚处理完一条」那一条路上（第五十六轮同一划界）。
        var body = Slice(Repo.Source("Desktop", "Native", file), signature, endAnchor);
        Assert.DoesNotContain("KeepKeyboardOnQueue", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_IsWiredOnTheThreeReviewPathsAndTheNineOpenPaths()
    {
        // 第八十八轮把同一处入口接到五张只读长清单的打开时（OnShown），触达点因此由三处变八处：
        // 三处审阅路径（处理完一条之后交回）与五处只读清单（打开时交给它），判定仍是同一份 ListFocus。
        // 第九十、九十一轮把注释候选、响应契约与注释缺口清单的打开落点补上后一并接上，触达点变九处。
        // 第九十二轮把同步预览的写入清单接上（预览页由隐藏变可见那一次，不是 OnShown——对话框打开时落在勾选页），
        // 触达点变十处。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("Ui.KeepKeyboardOnQueue"))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(
            new[]
            {
                "CandidatesDialog.cs", "CheckResultsDialog.cs", "ContractsDialog.cs", "DecisionsDialog.cs",
                "EndpointTraceDialog.cs", "EvidenceDialog.cs", "JournalDialog.cs", "PropagationDialog.cs",
                "ScanResultsDialog.cs", "SyncPreviewDialog.cs",
            },
            callers);
    }

    [Theory]
    [InlineData("TasksPage.cs")]
    [InlineData("ProjectsPage.cs")]
    [InlineData("SupplementsDialog.cs")]
    [InlineData("ApplyPlanDialog.cs")]
    public void OtherViews_NeverTakeTheKeyboard(string file)
    {
        // 两个主页面每次刷新与筛选都要重建（第五十一轮在筛选框 Enter 那一条路上才显式取焦点）；
        // 补充说明（编辑位是这一屏的输入）与注释回写（数据在 OnLoad 之后异步到达，打开时清单还是空的）
        // 都不接此口径——打开即选中一行的九张清单（含注释缺口，第九十一轮）的打开时交键盘见 OpenFocusTests，
        // 同步预览的写入清单在预览页出现时交键盘（第九十二轮，不是 OnShown）见 PlanOpenFocusTests。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.DoesNotContain("KeepKeyboardOnQueue", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialogs_DoNotMoveFocusByHand()
    {
        // 交回键盘只有一处入口：对话框不各自写 .Focus()，两页的筛选框与结果第一行是第五十一轮既有口径。
        foreach (var source in Repo.NativeSources().Where(s =>
                     !s.IsUi && s.Name != "TasksPage.cs" && s.Name != "ProjectsPage.cs"))
        {
            Assert.Empty(source.Matches(@"(?m)^\s*\w+\.Focus\(\);$"));
        }
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesListFocus()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListFocus.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListFocus.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsKeyboardHandoff()
    {
        // 断言取本轮条款独有的字面：「键盘交回队列」在 §6 出现一次，而「方向键从该行继续」
        // 在本节已被第三十三、五十六、五十七轮的条款各写过一次——只断言那句等于没断言本轮条款。
        var section = Repo.DesignSection(6);
        Assert.Contains("**键盘交回队列**（`ListFocus` 单一口径，2026-11-08 第五十八轮）", section);
        Assert.Contains("是清单内部的项级标记", section);
        Assert.Contains("保存被拒与重新读取失败的路径不交回", section);
        Assert.Contains("触达界面只在 `Ui.KeepKeyboardOnQueue`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 审阅键盘归属（§6） \|", ledger);
        Assert.Contains("21 个对话框类一处都没有", ledger);
        Assert.Contains("ListFocus.ShouldHandToQueue(选中行序号, 焦点行序号)", ledger);
        Assert.Contains("第五十八轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimInitialFocusWasFixed()
    {
        // 本轮没做的事不能被写成做了：三个审阅对话框的打开时初始焦点仍属框架 tab 序、本环境不可观测，
        // §8 只在「未扩面」里如实挂账。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 审阅键盘归属（§6） |", "\n| 输入框（§6） |");
        Assert.Contains("不接管三个审阅对话框的打开时初始焦点", row);
        Assert.DoesNotContain("打开时初始焦点已接线", row, StringComparison.Ordinal);
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
