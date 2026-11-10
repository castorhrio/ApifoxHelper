using System.Reflection;
using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「禁用要说明缺什么」收口断言（DESIGN.md §7「禁用 = 控件禁用 + 一句话说明缺什么」、§6「列表行」、§8 本轮条款）：
/// 第六十一轮如实挂账「不新增无选中时『缺什么』那句话……其余视图那一屏由谁说话要先定」，本轮定那个说话位并接线。
/// 基线形状可核对：五个带选中动作的视图（注释候选、响应契约、差异确认、接口追踪、注释缺口）的 ShowDetail
/// 无选中分支一律把详情区抹成空串并把动作禁用，一句话都没有；而第四十一轮的补充说明对话框同一态给得出那句话
/// （「选择接口后可编辑与保存说明补充。」）——同一只壳里同一个动作两种结果，按第五十五、五十七、五十九轮
/// 那条判据（是不是同壳不同行为，而不是偏好）判定为真实缺口。
/// 说话位定为详情区：提示出口正被另外两种空占着（整份数据不可得、筛到零行，第五十九、六十轮立的形状），
/// 状态行归动作结果（本轮把它写死成常态会把「保存成功」那样的话挤掉），计数行是聚合数字——
/// 而详情区恰恰就是因为没有选中才空的那一格，它说这句话不算借位。
/// 判定与句子在 <see cref="ListSelection"/>（不依赖 WinForms、与测试工程共享编译，下列真值表与整句真执行）；
/// 接线由行首正则与方法体切片机械检查钉住；真机观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class MissingSelectionTests
{
    private static readonly string[] WiredViews =
    [
        "CandidatesDialog.cs", "ContractsDialog.cs", "DecisionsDialog.cs",
        "EndpointTraceDialog.cs", "ScanResultsDialog.cs",
    ];

    // ---------- 判定真值表：有行可选才轮到详情区说话 ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(40)]
    [InlineData(int.MaxValue)]
    public void TheSentenceIsDue_WhenRowsAreThereToChoose(int displayedRowCount)
    {
        // 清单里有行、一行都没选中：缺的就是「选中」，这一屏得有人说出缺什么。
        Assert.True(ListSelection.ShouldExplainMissingSelection(displayedRowCount));
    }

    [Theory]
    [InlineData(0)]
    public void TheSentenceIsHeldBack_WhenThereIsNothingToChoose(int displayedRowCount)
    {
        // 一行都没有时缺的不是选中而是筛选或数据本身，那一屏由提示出口说话；
        // 详情区这时再讲「选中一行」就是把不存在的行说成还能选（与第四十二轮「禁用不得移除下一步」同侧）。
        Assert.False(ListSelection.ShouldExplainMissingSelection(displayedRowCount));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AnImpossibleRowCountIsTreatedAsNoRows(int displayedRowCount)
    {
        // 负数不可发生，发生了也按「没有行」处理（与 FirstPosition、第五十三轮、第五十六轮同一取舍）。
        Assert.False(ListSelection.ShouldExplainMissingSelection(displayedRowCount));
    }

    [Fact]
    public void Guard_ExplainsExactlyWhenARowCouldBeSelected()
    {
        // 逐点核对：放行恰等于「有行可选」，多一分是抢提示出口的活、少一分是禁用了动作却没人说缺什么。
        for (var rows = -3; rows <= 60; rows++)
        {
            Assert.Equal(rows > 0, ListSelection.ShouldExplainMissingSelection(rows));
        }
    }

    // ---------- 句子真执行：跑的是主工程同一份代码 ----------

    [Theory]
    [InlineData(ListFilter.CandidateNoun, "没有选中候选。在清单里选中一行，它的详情显示在这里，选中动作随之可用。")]
    [InlineData(ListFilter.ContractNoun, "没有选中契约。在清单里选中一行，它的详情显示在这里，选中动作随之可用。")]
    [InlineData(ListFilter.EndpointNoun, "没有选中接口。在清单里选中一行，它的详情显示在这里，选中动作随之可用。")]
    [InlineData(ListFilter.DecisionNoun, "没有选中条目。在清单里选中一行，它的详情显示在这里，选中动作随之可用。")]
    [InlineData(ListFilter.SlotNoun, "没有选中注释位置。在清单里选中一行，它的详情显示在这里，选中动作随之可用。")]
    public void Hint_NamesWhatIsMissingAndTheNextStep(string noun, string expected)
    {
        Assert.Equal(expected, ListSelection.MissingSelectionHint(noun));
    }

    [Fact]
    public void Hint_PointerIsTheSingleSourceForTheNextStep()
    {
        // 下半句只有一份：每个名词的组合都以同一个 pointer 收尾，改一处就全部一起改。
        // （第六个名词是第七十七轮并入词汇表的「核对条目」、第七个是第七十九轮的「源码文件」、
        // 第八个是第八十轮的「动作」、第九个是第八十四轮的「记录」、第十个是第八十五轮的「模型」、
        // 第十一个是第八十六轮的「未纳入条目」、第十二个是第一百一十二轮的「跳过位置」——
        // 那七屏没有因未选中而禁用的控件，不接这句话，但名词仍取同一份词汇表。）
        foreach (var noun in NounValues())
        {
            Assert.EndsWith(ListSelection.MissingSelectionPointer, ListSelection.MissingSelectionHint(noun), StringComparison.Ordinal);
        }

        Assert.Equal(12, NounValues().Distinct().Count());
    }

    [Fact]
    public void Pointer_SaysBothWhatAppearsAndWhatBecomesUsable()
    {
        // §7 要的那一半是「控件禁用 + 缺什么」：这句话必须同时讲清详情出现在哪、以及被禁用的东西随之可用，
        // 只剩前半句就成了「这里很空」而不是「缺什么、怎么补」。
        Assert.Contains("选中一行", ListSelection.MissingSelectionPointer, StringComparison.Ordinal);
        Assert.Contains("详情", ListSelection.MissingSelectionPointer, StringComparison.Ordinal);
        Assert.Contains("可用", ListSelection.MissingSelectionPointer, StringComparison.Ordinal);
    }

    [Fact]
    public void Nouns_ComeFromTheOneVocabulary_AndAreNotRestatedHere()
    {
        // 「这一行的东西叫什么」只有 ListFilter 一份词汇表；在 ListSelection 里另写五个名词就是第二套，两套必然漂移。
        // 只算代码行：注释里出现同一个词是在说明这句话给谁听，不是在再写一份词汇表。
        var code = CodeLines(Repo.Source("Desktop", "Native", "ListSelection.cs"));
        foreach (var noun in NounValues())
        {
            Assert.DoesNotContain(noun, code, StringComparison.Ordinal);
        }
    }

    // ---------- 界面不自己拼句子 ----------

    [Theory]
    [InlineData("没有选中")]
    [InlineData("选中动作随之可用")]
    public void Views_DoNotSpellTheSentenceThemselves(string fragment)
    {
        // 只算代码行：注释里引用这句话（说明为什么这么改）不算第二套拼写。
        foreach (var source in Repo.NativeSources())
        {
            Assert.Equal(source.Name == "ListSelection.cs" ? 1 : 0, CodeLinesContaining(source.Text, fragment));
        }
    }

    // ---------- 接线：五处无选中分支 ----------

    [Theory]
    // 三个审阅视图与两处清单：判定入参是这一屏的行数，名词是本视图那一个，零行仍留空。
    [InlineData("CandidatesDialog.cs", "CandidateNoun")]
    [InlineData("ContractsDialog.cs", "ContractNoun")]
    [InlineData("DecisionsDialog.cs", "DecisionNoun")]
    [InlineData("EndpointTraceDialog.cs", "EndpointNoun")]
    [InlineData("ScanResultsDialog.cs", "SlotNoun")]
    public void Views_AskTheSharedGuard_WithTheirOwnRowCountAndNoun(string file, string nounConst)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var branch = NoSelectionBranch(source);
        Assert.Matches(@"(?m)^\s*_detail\.Text = ListSelection\.ShouldExplainMissingSelection\(_list\.Items\.Count\)$", branch);
        Assert.Matches($@"(?m)^\s*\? ListSelection\.MissingSelectionHint\(ListFilter\.{nounConst}\)$", branch);
        Assert.Matches(@"(?m)^\s*: """";$", branch);
    }

    [Fact]
    public void Sentence_IsWiredOnExactlyTheViewsWithSelectionGatedActions()
    {
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListSelection.MissingSelectionHint"))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(WiredViews.OrderBy(n => n, StringComparer.Ordinal), callers);
    }

    [Theory]
    // 纯只读详情视图没有「因未选中而被禁用的控件」，§7 那一行不适用：本轮不为它们造一个并不存在的禁用说明。
    [InlineData("CheckResultsDialog.cs")]
    [InlineData("PropagationDialog.cs")]
    public void DetailOnlyViews_WithNothingDisabledBySelection_AreNotWired(string file)
    {
        Assert.DoesNotContain("MissingSelectionHint", Repo.Source("Desktop", "Native", file), StringComparison.Ordinal);
    }

    [Theory]
    // 两个主页面的下一步是空态那个按钮与关键词本身（第四十八、五十轮立的口径），不是「选中一行」。
    [InlineData("TasksPage.cs")]
    [InlineData("ProjectsPage.cs")]
    public void MainPages_KeepTheirOwnEmptyStateWords(string file)
    {
        Assert.DoesNotContain("MissingSelectionHint", Repo.Source("Desktop", "Native", file), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentence_DoesNotBorrowAnotherSpeakersControl()
    {
        // 说话位是详情区：提示出口归另外两种空（第五十九、六十轮），状态行归动作结果（第四十一轮那条句子是保存态），
        // 计数行是聚合数字——把这句话写进它们任一格都是抢位。
        foreach (var file in WiredViews)
        {
            var source = Repo.Source("Desktop", "Native", file);
            foreach (var other in new[] { "_hint.Text = ListSelection", "_status.Text = ListSelection", "_counts.Text = ListSelection", "_statusLine.Text = ListSelection" })
            {
                Assert.DoesNotContain(other, source, StringComparison.Ordinal);
            }
        }
    }

    // ---------- 重建之后再问：判在清空之前量到的是上一轮的行数 ----------

    [Theory]
    // 这三处的重建路径不落点（注释候选与响应契约的落点在打开时，第九十轮；注释缺口清单的落点同样只在打开时，
    // 第九十一轮）：换筛选后是「有行可选但没选中」，那句话必须由重建之后的那次询问给出。
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("ScanResultsDialog.cs")]
    public void ViewsWithoutAutoSelection_AskTheDetailAfterTheRebuild(string file)
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", file), "private void ApplyFilter");
        Assert.Single(Regex.Matches(body, @"(?m)^\s*ShowDetail\(\);$"));
        var rebuild = body.IndexOf("_list.EndUpdate();", StringComparison.Ordinal);
        var asked = body.IndexOf("ShowDetail();", StringComparison.Ordinal);
        Assert.True(rebuild >= 0 && asked > rebuild, $"{file}:询问详情要排在清单重建之后。");
        // 回显之后再问：反过来会把刚写的那句话留在一张不可见的表后面。
        var restore = body.IndexOf("_list.Visible = true;", StringComparison.Ordinal);
        Assert.True(restore >= 0 && asked > restore, $"{file}:询问详情要排在列表回显之后。");
        Assert.True(body.Contains("IsFilteredEmpty(", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("CandidatesDialog.cs")]
    [InlineData("ContractsDialog.cs")]
    [InlineData("ScanResultsDialog.cs")]
    public void MovingThatAsk_DoesNotTouchTheEmptyFilterBranch(string file)
    {
        // 搬家只搬「非空时问一次详情」，不搬筛到零行的分支：那一支仍先返回、回显在后。
        var body = MethodSlice(Repo.Source("Desktop", "Native", file), "private void ApplyFilter");
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        var restore = body.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        var asked = body.IndexOf("ShowDetail();", StringComparison.Ordinal);
        Assert.True(branch >= 0 && restore > branch, $"{file}:筛到零行的分支要排在列表回显之前。");
        Assert.True(asked > restore, $"{file}:询问详情要排在回显之后、不在分支里。");
    }

    [Theory]
    // 另两处在重建后自己选中一行（选中事件随即问到详情），本轮不为它们加第二次询问。
    [InlineData("DecisionsDialog.cs", "private void ApplyFilter")]
    public void ViewsWithAutoSelection_DoNotAskTwice(string file, string start)
    {
        var body = MethodSlice(Repo.Source("Desktop", "Native", file), start);
        Assert.DoesNotMatch(@"(?m)^\s*ShowDetail\(\);$", body);
        Assert.True(Regex.IsMatch(body, @"Ui\.SelectItem\(_list\.Items\[0\]\);|Ui\.SelectItem\(item\);"), $"{file}:重建后应有一处把选中交回清单。");
    }

    [Fact]
    public void TraceDialog_AsksOnlyInTheFilteredEmptyBranch()
    {
        // 接口追踪明细在第七十五轮把清单重建搬进 PopulateList（关键词框输入即筛选），锚点随真实形状走：
        // 重建后自己选中一行（选中事件随即问到详情），故非空路径不第二次询问；唯一那次 ShowDetail 在
        // 筛到零行那一支——清单已空，详情区与证据入口按既有的无选中路径收口，不另写一套收控件的赋值。
        var body = MethodSlice(Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs"), "private void PopulateList");
        Assert.Single(Regex.Matches(body, @"(?m)^\s*ShowDetail\(\);$"));
        var branch = body.IndexOf("IsFilteredEmpty(", StringComparison.Ordinal);
        var asked = body.IndexOf("ShowDetail();", StringComparison.Ordinal);
        var restore = body.IndexOf("_hint.Visible = false;", StringComparison.Ordinal);
        Assert.True(branch >= 0 && asked > branch, "询问详情要落在筛到零行那一支里。");
        Assert.True(restore > asked, "筛到零行的分支要排在列表回显之前（回显在后，那句话才不被自己抹掉）。");
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(target \?\? _list\.Items\[0\]\);$", body);
    }

    // ---------- 口径自身的形状 ----------

    [Fact]
    public void Guard_DoesNotDependOnWinForms()
    {
        // 判定与句子要在 Linux 真执行，就不许碰控件。
        var rule = Repo.Source("Desktop", "Native", "ListSelection.cs");
        Assert.DoesNotContain("System.Windows.Forms", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardIsCalledFromTheViews_AndFromNowhereElse()
    {
        // 判定只有一份：除了这五个视图的无选中分支，界面别处不得自己写「行数 > 0」再拼同一句话。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListSelection.ShouldExplainMissingSelection"))
            .Select(s => s.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(WiredViews.OrderBy(n => n, StringComparer.Ordinal), callers);
    }

    [Theory]
    [InlineData("ListSelection.cs")]
    [InlineData("ListFilter.cs")]
    public void SharedCompile_IncludesBothCalibrationFiles(string file)
    {
        // 真值表与整句跑的是主工程同一份代码：两份口径都必须在共享编译清单里。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains($@"..\Desktop\Native\{file}", csproj);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsWhoSpeaksTheMissingSelectionSentence()
    {
        var section = Repo.DesignSection(6);
        // 钉的是条款标题本体（带粗体），不是它被引用的那一次：第六十轮的同一条教训——
        // 只钉字面的话，把标题改名而留下交叉引用，检查照样绿。
        Assert.Contains("**未选中那一行的那句话**", section, StringComparison.Ordinal);
        Assert.Contains("ListSelection.MissingSelectionHint", section, StringComparison.Ordinal);
        Assert.Contains("重建之后再问一次详情", section, StringComparison.Ordinal);
        Assert.Contains("不自动选中的三处", section, StringComparison.Ordinal);
        Assert.Contains("ShouldExplainMissingSelection", section, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_PointsTheDisabledRowAtTheSpeaker()
    {
        // §7 的「禁用 = 控件禁用 + 一句话说明缺什么」原先不知道那一屏由谁说话；本轮把落点写进那一行。
        var disabledRow = Repo.DesignSection(7)
            .Split('\n')
            .First(l => l.Contains("| 禁用 |", StringComparison.Ordinal));
        Assert.Contains("一句话说明缺什么", disabledRow, StringComparison.Ordinal);
        Assert.Contains("详情区说", disabledRow, StringComparison.Ordinal);
        Assert.Contains("见 §6「列表行」", disabledRow, StringComparison.Ordinal);
        // 另外两种空仍归提示：把这半句抹掉就等于宣称详情区说什么都在说。
        Assert.Contains("提示出口说", disabledRow, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_PointsThePriorDeferralAtThisRound()
    {
        // 第六十一轮那行要同时留着「当时为什么不接」与「本轮收口在哪」：
        // 指向退回成裸旧句，下一轮会照着旧句以为还欠着（第六十轮立过的同一条纪律）。
        var row = Repo.DesignSection(8)
            .Split('\n')
            .Single(l => l.StartsWith("| 选中行读取（§6） |", StringComparison.Ordinal));
        Assert.Contains("不新增无选中时「缺什么」那句话", row, StringComparison.Ordinal);
        Assert.Contains("已于第六十二轮收口", row, StringComparison.Ordinal);
        Assert.DoesNotContain("不在本轮顺手写）、", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsThisRound()
    {
        // 台账行要按条款格取：整节包含「禁用要说明缺什么」不算数——那是 §7 的原话，第四十二轮那行也引用过。
        var row = Repo.DesignSection(8)
            .Split('\n')
            .Single(l => l.StartsWith("| 禁用要说明缺什么（§7） |", StringComparison.Ordinal));
        Assert.Contains("2026-11-08 第六十二轮", row, StringComparison.Ordinal);
        Assert.Contains("ListSelection", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsWhatWasNotDone()
    {
        // 本轮没做的事不能被写成做了：只读详情视图与两页没接，§8 那行必须留着这个边界与理由。
        // 按条款格取行——「禁用要说明缺什么」是 §7 的原话，第四十二轮那行也引用了它，按包含取会取错行。
        var rows = Repo.DesignSection(8)
            .Split('\n')
            .Where(l => l.StartsWith("| 禁用要说明缺什么（§7） |", StringComparison.Ordinal))
            .ToList();
        Assert.Single(rows);
        var row = rows[0];
        Assert.Contains("CheckResultsDialog", row, StringComparison.Ordinal);
        Assert.Contains("两个主页面", row, StringComparison.Ordinal);
        Assert.DoesNotContain("全界面都已接线", row, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static IEnumerable<string> NounValues() =>
        typeof(ListFilter)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.Name.EndsWith("Noun", StringComparison.Ordinal))
            .Select(f => (string?)f.GetValue(null) ?? "")
            .ToList();

    private static int CodeLinesContaining(string text, string fragment) =>
        CodeLines(text).Split('\n').Count(l => l.Contains(fragment, StringComparison.Ordinal));

    /// <summary>只留代码行：注释里引用一句话是说明改动理由，不算第二套拼写（与第五十九轮同一口径）。</summary>
    private static string CodeLines(string text) => CodeScan.Text(text);

    /// <summary>
    /// 取一个方法体的切片：起点是方法签名，终点是它自己的收尾大括号（独占一行、四格缩进）——
    /// 嵌套块收在更深的缩进上，不会被当成方法结束。
    /// </summary>
    private static string MethodSlice(string source, string start)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf("\n    }", from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"{start} 找不到收尾大括号。");
        return source[from..to];
    }

    /// <summary>
    /// 取 ShowDetail 开头的无选中分支：从签名到第一个 return;。
    /// 不按「到第一个 } 为止」切——差异确认的守卫写着 <c>is not { } entry</c>，那对花括号会先把切片截断。
    /// </summary>
    private static string NoSelectionBranch(string source)
    {
        const string signature = "private void ShowDetail()";
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少 ShowDetail。");
        var to = source.IndexOf("return;", from + signature.Length, StringComparison.Ordinal);
        Assert.True(to > from, "无选中分支缺少 return;。");
        return source[from..(to + "return;".Length)];
    }
}
