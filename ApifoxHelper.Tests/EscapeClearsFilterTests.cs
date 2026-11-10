using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「Esc 退出筛选」的归属断言（DESIGN.md §6「列表筛选」的全键盘闭环）：Ctrl+F 把焦点交给框 → 输入 →
/// Enter 把选中交给结果第一行 → 方向键逐行看，到这一步想回到全部，此前只能回鼠标点框或点空态的
/// 「清空筛选」——Esc 只挂在筛选框自己的按键处理上，Enter 之后焦点已在列表，按它没有反应
/// （第四十八、五十、五十一轮逐轮如实挂账）。现收成一条归属：<see cref="ListFilter.EscClears"/> 判定
/// 「这一屏是不是这一次 Esc 的落点」（本页可见 ∧ 框里留着关键词），壳只分发到页，页经页面协议回答
/// 按键有没有被接住，清空仍走各页既有的 ClearListFilter 唯一路径；判定不在壳与页各写一份。
/// 真机上的按键路由（ProcessCmdKey 的冒泡次序、菜单打开时 Esc 仍收起菜单）随 G-D5 复核：
/// 本环境无原生运行时，接线一律以源码切片与行首正则钉住，不冒充跑过。
/// </summary>
public class EscapeClearsFilterTests
{
    private static readonly string[] Pages = ["Desktop/Native/ProjectsPage.cs", "Desktop/Native/TasksPage.cs"];

    // ---------- 口径真值表：这一次 Esc 归谁 ----------

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void EscapeGate_FourCombinations(bool pageVisible, bool hasKeyword, bool expected) =>
        Assert.Equal(expected, ListFilter.EscClears(pageVisible, hasKeyword));

    [Fact]
    public void HiddenPage_NeverTakesTheEscape()
    {
        // 另一页的关键词是用户留给那一屏的：在这一屏按 Esc 不构成清掉它的理由。
        Assert.False(ListFilter.EscClears(pageVisible: false, hasKeyword: true));
    }

    [Fact]
    public void EmptyKeyword_LeavesTheKeyAlone()
    {
        // 空框里按 Esc 什么都没做，就不把这一次按键说成做了什么（不吞键）。
        Assert.False(ListFilter.EscClears(pageVisible: true, hasKeyword: false));
    }

    [Fact]
    public void EscapeGate_ComposesWithTheKeywordTestInsteadOfReinventingIt()
    {
        // 「框里有没有关键词」只有一份问法：Esc 的落点判定接在 IsActive 上，全空白也算没有筛选。
        Assert.False(ListFilter.EscClears(true, ListFilter.IsActive("   ")));
        Assert.False(ListFilter.EscClears(true, ListFilter.IsActive(null)));
        Assert.True(ListFilter.EscClears(true, ListFilter.IsActive("订单")));
    }

    [Fact]
    public void Gauge_DoesNotReachForWinForms()
    {
        // 引用只算代码行：注释里写框架文档名不算引用，而 using 一行真写上就编不进测试工程（共享编译）。
        Assert.Empty(CodeLines(Repo.Source("Desktop", "Native", "ListFilter.cs"), "System.Windows.Forms"));
    }

    // ---------- 壳的分发：只接 Esc，只在页真的清空时接住 ----------

    [Fact]
    public void Shell_TakesEscapeOnlyWhenAPageReportsItCleared()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        var body = Slice(shell, "protected override bool ProcessCmdKey(ref Message msg, Keys keyData)", "private bool ClearFilterOnVisiblePage");
        Assert.Matches(@"(?m)^\s*if \(keyData == Keys\.Escape && ClearFilterOnVisiblePage\(\)\) return true;$", body);
        // 没接住就交回基类：菜单快捷键、其余按键一律不由本处改写。
        Assert.Matches(@"(?m)^\s*return base\.ProcessCmdKey\(ref msg, keyData\);$", body);
        var takes = body.IndexOf("return true;", StringComparison.Ordinal);
        var handsBack = body.IndexOf("return base.ProcessCmdKey", StringComparison.Ordinal);
        Assert.True(takes >= 0, "壳没有接住 Esc 的那一行。");
        Assert.True(handsBack >= 0, "壳没有把其余按键交回基类。");
        Assert.True(takes < handsBack, "壳必须先看这一次按键是不是 Esc，其余才交回基类。");
    }

    [Fact]
    public void Shell_HandlesNothingButEscape()
    {
        // 一个手势一处归属，且不给别的键发明新语义：切片里只允许出现一次按键比较、只有 Escape。
        var body = Slice(Repo.Source("Desktop", "Native", "NativeShellForm.cs"), "protected override bool ProcessCmdKey", "private bool ClearFilterOnVisiblePage");
        Assert.Single(Regex.Matches(body, @"keyData\s*=="));
        foreach (var other in new[] { "Keys.Enter", "Keys.Tab", "Keys.Control", "Keys.C", "Keys.F5" })
        {
            Assert.DoesNotContain(other, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Shell_DispatchesToBothPagesAndFallsBackToNotTakingIt()
    {
        var body = Slice(Repo.Source("Desktop", "Native", "NativeShellForm.cs"), "private bool ClearFilterOnVisiblePage()", "protected override async void OnLoad");
        Assert.Matches(@"(?m)^\s*if \(_shutdownStarted \|\| IsDisposed \|\| Disposing\) return false;$", body);
        Assert.Matches(@"(?m)^\s*if \(_projectsPage is not null && _projectsPage\.TryClearListFilter\(\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*if \(_tasksPage is not null && _tasksPage\.TryClearListFilter\(\)\) return true;$", body);
        Assert.Matches(@"(?m)^\s*return false;$", body);
        // 可见性与关键词的判定只有一份（在页那侧经共享口径），壳不得再量一次——两处各判必然漂移。
        Assert.DoesNotContain("Visible: true", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_ScreenHasNoCancelButtonForEscapeToCollideWith()
    {
        // 主窗体不设 CancelButton：Esc 在这里没有既有语义可抢，本轮接的这一步不与任何取消动作争。
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.DoesNotContain("CancelButton", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptButton", shell, StringComparison.Ordinal);
    }

    // ---------- 页的回答：判定走口径，清空走唯一路径 ----------

    [Fact]
    public void Pages_AnswerForThemselvesThroughTheSharedGauge()
    {
        foreach (var page in Pages)
        {
            var body = Slice(Repo.Source(page), "public bool TryClearListFilter()", "private void ClearListFilter()");
            Assert.Matches(@"(?m)^\s*if \(!ListFilter\.EscClears\(Visible, ListFilter\.IsActive\(_filter\.Text\)\)\) return false;$", body);
            Assert.Matches(@"(?m)^\s*ClearListFilter\(\);", body);
            Assert.Matches(@"(?m)^\s*return true;$", body);
            var decides = body.IndexOf("ListFilter.EscClears", StringComparison.Ordinal);
            var clears = body.IndexOf("ClearListFilter();", StringComparison.Ordinal);
            Assert.True(decides >= 0 && clears >= 0, $"{page[^9..]}:Esc 落点没有判定或没有清空。");
            Assert.True(decides < clears, $"{page[^9..]}:必须先判定是不是这一屏的落点，再清空。");
        }
    }

    [Fact]
    public void Pages_DoNotGateOnFocusOrOnTheirOwnKeywordTest()
    {
        foreach (var page in Pages)
        {
            var body = Slice(Repo.Source(page), "public bool TryClearListFilter()", "private void ClearListFilter()");
            // 焦点在不在框上不在判定之内；「框里有没有关键词」也不另量一次。
            Assert.DoesNotContain("_filter.Text.Length", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Focused", body, StringComparison.Ordinal);
            Assert.DoesNotContain("Keys.", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Protocol_DeclaresTheEntryOnce_AndBothPagesImplementIt()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.Single(Regex.Matches(shell, @"bool TryClearListFilter\(\);"));
        // 页面协议的既有成员不因此丢失：刷新与 Ctrl+F 仍在同一份协议上。
        Assert.Contains("void RefreshData();", shell);
        Assert.Contains("void FocusListFilter();", shell);
        Assert.Equal(2, Regex.Matches(string.Join('\n', Pages.Select(p => Repo.Source(p))), @"public bool TryClearListFilter\(\)").Count);
    }

    [Fact]
    public void BothTriggers_ConvergeOnTheSingleClearPath()
    {
        foreach (var page in Pages)
        {
            var source = Repo.Source(page);
            // 两个触发（Esc 与空态按钮）、一条清空路径：页内不得出现第二处把关键词赋空。
            Assert.Equal(2, CodeLines(source, "ClearListFilter();").Count);
            var writes = CodeLines(source, "_filter.Text = \"\";");
            Assert.Single(writes);
            var clear = Slice(source, "private void ClearListFilter()", "private void Load" + (page.EndsWith("TasksPage.cs", StringComparison.Ordinal) ? "Tasks" : "Profiles") + "()");
            Assert.Contains("_filter.Text = \"\";", clear, StringComparison.Ordinal);
        }
    }

    // ---------- 全仓归属：Esc 只有三处，语义不串 ----------

    [Fact]
    public void Escape_HasExactlyThreeOwnersInTheNativeUi()
    {
        // 壳：主窗口的 Esc 退出筛选（页级手势）；能力探测对话框：Esc=关闭（与标题栏 × 同语义）；
        // Ui：对话框筛选框里的 Esc 退出筛选（第七十一轮，焦点在框上且框里有字才接，其余仍是取消）。
        var owners = Repo.NativeSources()
            .Select(source => (source.Name, lines: CodeLines(source.Text, "Keys.Escape")))
            .Where(o => o.lines.Count > 0)
            .ToList();
        Assert.Equal(["CapabilitiesDialog.cs", "NativeShellForm.cs", "Ui.cs"], owners.Select(o => o.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.All(owners, o => Assert.Single(o.lines));
        // 三种语义各走各的通路：壳不用对话框的 ProcessDialogKey，对话框不抢壳的 ProcessCmdKey。
        Assert.DoesNotContain("ProcessCmdKey", Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ProcessDialogKey", Repo.Source("Desktop", "Native", "NativeShellForm.cs"), StringComparison.Ordinal);
        // 五张长清单对话框不自己认 Esc：它们经 Ui.TryEscapeFilter 一处接线（判定与清空都在那里）。
        foreach (var dialog in new[] { "CheckResultsDialog.cs", "DecisionsDialog.cs", "ScanResultsDialog.cs", "CandidatesDialog.cs", "ContractsDialog.cs" })
        {
            Assert.Empty(CodeLines(Repo.Source("Desktop", "Native", dialog), "Keys.Escape"));
        }
    }

    [Fact]
    public void FilterBox_KeepsNoEscapeBranchOfItsOwn()
    {
        foreach (var page in Pages)
        {
            Assert.Empty(CodeLines(Repo.Source(page), "Keys.Escape"));
        }
    }

    [Fact]
    public void CommentedGestureMentions_AreNotCountedAsWiring()
    {
        // 扫描只算代码行：注释里解释「Esc 不在这里接」不构成第二处接线（与第五十二、六十三轮同一教训）。
        var withComment = "// if (keyData == Keys.Escape) return true;\n";
        Assert.Empty(CodeLines(withComment, "Keys.Escape"));
        Assert.Single(CodeLines(withComment + "if (keyData == Keys.Escape) return true;\n", "Keys.Escape"));
    }

    [Fact]
    public void ShellShortcuts_DoesNotRegisterEscapeAsAMenuGesture()
    {
        // 本轮不把它做成菜单快捷键：菜单项要按「有没有关键词」动态启停，那是另一件事；
        // 这句提示由屏幕自己的文字与既有按钮承担，手势也不占用「菜单里显示出来的那一份」。
        var shortcuts = Repo.Source("Desktop", "Native", "ShellShortcuts.cs");
        Assert.DoesNotContain("\"Esc\"", shortcuts, StringComparison.Ordinal);
        Assert.DoesNotContain("Escape", shortcuts, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_DeclaresTheEscapeOwnership()
    {
        var filter = DesignRow(6, "列表筛选");
        Assert.Contains("第六十四轮起由壳分发到当前可见页", filter);
        Assert.Contains("`ListFilter.EscClears`", filter);
        Assert.Contains("另一页的关键词不动", filter);
        Assert.Contains("空关键词不接住这一次按键", filter);
        // 旧说法不得回退：框级归属与「聚焦时才算」已经不成立。
        Assert.DoesNotContain("筛选框聚焦时按 Esc", filter, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_KeepsTheEmptyStateWordingHonest()
    {
        // 空态那一行仍指屏上真实存在的按钮；它与 Esc 是同一条路体的两个触发，谁也不替代谁。
        var empty = DesignRow(6, "空态");
        Assert.Contains("唯一行动按钮", empty);
        Assert.Contains("ListFilter.ClearActionLabel", empty);
        Assert.DoesNotContain("筛选框聚焦时的 Esc", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheRoundAndWhatStillNeedsARealMachine()
    {
        var row = DesignRow(8, "Esc 退出筛选（§6）");
        Assert.Contains("第六十四轮", row);
        Assert.Contains("真机", row);
        Assert.Contains("ProcessCmdKey", row);
        Assert.Contains("G-D5", row);
    }

    [Fact]
    public void DesignDoc_PointsThePriorDeferralsAtThisRound()
    {
        // 已被取代的挂账必须指向收口，否则下一轮读到的是「仍然如此」（第六十、六十二轮同一条纪律）。
        var ledger = Repo.DesignSection(8);
        var rows = ledger.Split('\n').Where(l => !l.StartsWith("| Esc 退出筛选（§6） |", StringComparison.Ordinal)).ToList();
        var row = rows.Single(l => l.Contains("而 Esc 只挂在筛选框的按键处理上", StringComparison.Ordinal));
        Assert.Contains("第六十四轮", row);
    }

    // ---------- 助手 ----------

    private static string DesignRow(int section, string clause)
    {
        var lines = Repo.DesignSection(section).Split('\n');
        var row = lines.SingleOrDefault(l => l.StartsWith($"| {clause} |", StringComparison.Ordinal));
        Assert.True(row is not null, $"DESIGN.md §{section} 缺少「{clause}」这一行。");
        return row;
    }

    private static List<string> CodeLines(string text, string fragment) => CodeScan.LinesContaining(text, fragment);

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
