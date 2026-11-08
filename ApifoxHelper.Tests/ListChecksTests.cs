using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 复选清单断言（DESIGN.md §6「列表行」、§8 本轮条款）：全原生界面最后一张绕过 <c>Ui.List</c> 工厂的清单
/// 是新建任务的模块清单——<c>CheckedListBox</c> 全仓恰一处、控件级 <c>BorderStyle.FixedSingle</c> 也恰此一处，
/// 于是 §6 的「选中行底 Bg + 左侧 3px Accent 标记（不用系统高亮蓝）」「列表不得绕过工厂创建」在这一屏不成立，
/// 而同一个动作（勾选一批条目当范围）在同步预览的接口清单上早已是工厂的样子：同壳两种样子。
/// 并进工厂的前提是把 <c>CheckedListBox</c> 本来自带的整行点选手势一并搬进工厂（判定在 <see cref="ListChecks"/>，
/// 不依赖 WinForms、与测试工程共享编译），否则自绘之后勾选目标从整行缩成工厂画出来的那一小格。
/// 一次点击只翻一次由「让开系统自己会翻的那一格」保证；模块行改挂行载荷，勾中序号按下标取另一份清单的写法随之消失。
/// 真机上的实际命中与路由随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListChecksTests
{
    // ---------- 手势口径真值表：这一次点击归谁翻 ----------

    [Theory]
    // 左键、命中在一行上、落点不是系统自己会翻的那一格——只有这一种归本手势。
    [InlineData(true, true, false)]
    public void LeftClickOnARow_IsTheOnlyFlip(bool leftClick, bool hitOnRow, bool hitOnCheckbox) =>
        Assert.True(ListChecks.ShouldToggle(leftClick, hitOnRow, hitOnCheckbox));

    [Theory]
    // 右键与中键不是点选手势：界面上也没有上下文菜单可给它翻（第二十二、五十二轮「不做右键菜单」的挂账不变）。
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void NonLeftClick_NeverFlips(bool hitOnRow, bool hitOnCheckbox)
    {
        Assert.False(ListChecks.ShouldToggle(false, hitOnRow, hitOnCheckbox));
    }

    [Theory]
    // 命中不在行上：列头与清单外空白不动作，与第三十九轮「命中哪行激活哪行、双击空白区不动作」同一纪律。
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ClickOffTheRow_NeverFlips(bool leftClick, bool hitOnCheckbox) =>
        Assert.False(ListChecks.ShouldToggle(leftClick, false, hitOnCheckbox));

    [Theory]
    // 系统自己会翻的那一格让开：本手势与系统的翻转在同一行上必须互斥，否则一次点击翻两下（看上去是没反应）。
    [InlineData(true)]
    [InlineData(false)]
    public void OneClickOnARow_FlipsExactlyOnce(bool hitOnCheckbox) =>
        Assert.NotEqual(hitOnCheckbox, ListChecks.ShouldToggle(true, true, hitOnCheckbox));

    [Fact]
    public void Rule_CoversEveryClickCombination()
    {
        // 八个组合逐一对着「左键 ∧ 命中行 ∧ 非勾选框」核一遍：多一分是一次点击翻两下，少一分是看着框点下去没反应。
        for (var mask = 0; mask < 8; mask++)
        {
            var left = (mask & 4) != 0;
            var row = (mask & 2) != 0;
            var checkbox = (mask & 1) != 0;
            Assert.Equal(left && row && !checkbox, ListChecks.ShouldToggle(left, row, checkbox));
        }
    }

    [Fact]
    public void Rule_DoesNotDependOnWinForms()
    {
        // 口径与测试工程共享编译、在 Linux 真执行：引用控件类型就编不进去，真值表也就跑不起来。
        var text = Repo.Source("Desktop", "Native", "ListChecks.cs");
        Assert.DoesNotContain("System.Windows.Forms", text, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Drawing", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ListView", text, StringComparison.Ordinal);
    }

    // ---------- 工厂接线：手势只装在复选清单出厂的那一处 ----------

    /// <summary>工厂里 <c>checkBoxes</c> 分支的源码（到出厂为止）。</summary>
    private static string CheckboxBranch()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var from = ui.IndexOf("if (checkBoxes)", StringComparison.Ordinal);
        Assert.True(from >= 0, "Ui.List 缺少 checkBoxes 分支——整行勾选无处可装。");
        var to = ui.IndexOf("return list;", from, StringComparison.Ordinal);
        Assert.True(to > from, "Ui.List 取不到出厂处，切片漂了。");
        return ui[from..to];
    }

    [Fact]
    public void Factory_WiresHitTestThenJudgesThenFlips()
    {
        var branch = CheckboxBranch();
        Assert.Matches(@"(?m)^\s*list\.MouseDown \+= \(_, e\) =>$", branch);
        Assert.Matches(@"(?m)^\s*var hit = list\.HitTest\(e\.Location\);$", branch);
        Assert.Matches(@"(?m)^\s*if \(hit\.Item is not ListViewItem row\) return;$", branch);
        Assert.Matches(
            @"(?m)^\s*if \(!ListChecks\.ShouldToggle\(e\.Button == MouseButtons\.Left, true, hit\.Location == ListViewHitTestLocations\.StateImage\)\) return;$",
            branch);
        Assert.Matches(@"(?m)^\s*row\.Checked = !row\.Checked;$", branch);

        var hit = branch.IndexOf("list.HitTest(e.Location)", StringComparison.Ordinal);
        var guard = branch.IndexOf("hit.Item is not ListViewItem row", StringComparison.Ordinal);
        var judge = branch.IndexOf("ListChecks.ShouldToggle(", StringComparison.Ordinal);
        var flip = branch.IndexOf("row.Checked = !row.Checked;", StringComparison.Ordinal);
        Assert.True(hit < guard && guard < judge && judge < flip,
            "应先取命中、再断言命中在一行上、再由共享口径判定、最后翻那一行。");
    }

    [Fact]
    public void Factory_JudgmentIsNotRewrittenInPlace()
    {
        // 修饰键与落点的判定只有一份：工厂里 StateImage 只出现在交给口径的那一句，不就地再比一套。
        var branch = CheckboxBranch();
        Assert.Single(Regex.Matches(branch, @"ListViewHitTestLocations\.StateImage"));
        Assert.Single(Regex.Matches(branch, @"MouseButtons\.Left"));
        Assert.DoesNotContain("e.Button ==", branch[..branch.IndexOf("ListChecks.ShouldToggle(", StringComparison.Ordinal)],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Factory_FlipsTheCheckAndNothingElse()
    {
        // 选中、焦点标记、滚动、键盘归属各有自己的唯一入口，本处只翻那一行的勾（第五十二、五十七、五十八轮同法）。
        var branch = CheckboxBranch();
        Assert.DoesNotMatch(@"(?m)^\s*\S+\.(Selected|Focused) = ", branch);
        Assert.DoesNotContain("EnsureVisible", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectItem", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", branch, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus()", branch, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void Flip_IsTouchedOnceAcrossTheWholeSurface()
    {
        // 「从自身取值翻一下」这种写法全原生界面只该有一处（同步预览的全选/清空是按载荷置位，不是翻转，不在此列）。
        // 不锚行首也不要求 `.Checked` 前面是单词字符：变异验证实测过 `if (...) list.Items[0].Checked = !...` 这种行内写法，
        // 两种写法都能躲过上一版的正则（与第五十二轮「被注释掉的调用同样计入命中」同族教训——扫描形状变了，锚点要跟着变）。
        var hits = Repo.NativeSources()
            .Where(s => Regex.IsMatch(CodeLines(s.Text), @"\.Checked\s*=\s*!"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, hits);
        Assert.Single(Regex.Matches(CodeLines(Repo.Source("Desktop", "Native", "Ui.cs")), @"\.Checked\s*=\s*!"));
    }

    [Fact]
    public void HitTest_StaysInsideTheFactory()
    {
        var callers = Repo.NativeSources()
            .Where(s => Regex.IsMatch(CodeLines(s.Text), @"\w+\.HitTest\("))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    [Theory]
    [InlineData("SyncPreviewDialog.cs")]
    [InlineData("TasksPage.cs")]
    public void CheckboxLists_DoNotInstallTheirOwnGesture(string file)
    {
        // 两张复选清单都从工厂出厂，谁都不再自己装一套——两套必然漂移（第五十二轮「剪贴板只触达一次」同法）。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.DoesNotContain("ListChecks.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MouseDown", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StateImage", source, StringComparison.Ordinal);
    }

    // ---------- 模块清单：出厂形状 ----------

    [Fact]
    public void ModuleChecklist_LeavesTheFactoryLikeEveryOtherList()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*private readonly ListView _modules = Ui\.List\(checkBoxes: true\);$", tasks);
        Assert.Matches(@"(?m)^\s*var moduleHost = Ui\.Hairline\(_modules\);$", tasks);
        Assert.Matches(@"(?m)^\s*moduleHost\.Height = 96;$", tasks);
        Assert.Matches(@"(?m)^\s*AddRow\(grid, ""模块"", moduleHost\);$", tasks);
        // 首列就是这一行在屏幕上被读作的名字，走默认标识列，不声明。
        Assert.DoesNotContain("identityColumn", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleColumnWidth_StaysInsideTheFieldFloor()
    {
        // 列宽若超过 AddRow 给字段的下限，单列清单会逼出横向滚动条——多出来的宽度本该是整行勾选的可点面。
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var column = Regex.Match(tasks, @"_modules\.Columns\.Add\(""模块"", (\d+)\);");
        var floor = Regex.Match(tasks, @"field\.Width = Math\.Max\(field\.Width, (\d+)\);");
        Assert.True(column.Success, "模块清单缺少「模块」列。");
        Assert.True(floor.Success, "AddRow 的字段下限形状变了，本条检查要跟着改。");
        Assert.True(int.Parse(column.Groups[1].Value) <= int.Parse(floor.Groups[1].Value),
            $"列宽 {column.Groups[1].Value} 超出字段下限 {floor.Groups[1].Value}——会逼出横向滚动条。");
    }

    [Fact]
    public void ModuleRows_CarryTheirOwnPayload()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var body = Slice(tasks, "private void LoadModules()", "private void AddRow(");
        Assert.Matches(@"_modules\.Items\.Add\(new ListViewItem\(TaskCreation\.ModuleRowText\(module\)\) \{ Tag = module \}\);", body);
        Assert.Matches(@"(?m)^\s*_modules\.BeginUpdate\(\);$", body);
        Assert.Matches(@"(?m)^\s*_modules\.EndUpdate\(\);$", body);
        // 另存一份模块清单按勾中序号取值：两份清单必须一直对齐才不错行，错行交出去的是另一个模块。
        Assert.DoesNotContain("_currentModules", tasks, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckedIndices", tasks, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckedModules_AreReadFromTheRowsThemselves()
    {
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var body = Slice(tasks, "private void Confirm(bool startScan)", "private void SetStatus(");
        Assert.Matches(@"(?m)^\s*var moduleNames = _modules\.Items\.OfType<ListViewItem>\(\)$", body);
        Assert.Matches(@"(?m)^\s*\.Where\(i => i\.Checked && i\.Tag is ModuleScope\)$", body);
        Assert.Matches(@"(?m)^\s*\.Select\(i => \(\(ModuleScope\)i\.Tag!\)\.Name\)$", body);
        // 交出去的是模块名，不是屏幕上那一格文字（那一格里还带着路径前缀）。
        var from = body.IndexOf("var moduleNames", StringComparison.Ordinal);
        var statement = body[from..(body.IndexOf(';', from) + 1)];
        Assert.DoesNotContain(".Text", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void NoChecklistBypassesTheFactoryAnymore()
    {
        // 只算代码行：口径文件在注释里点名它替掉的东西，是在说明这一手势为什么存在（第五十九、六十二轮同法）。
        foreach (var source in Repo.NativeSources())
        {
            var code = CodeLines(source.Text);
            Assert.False(code.Contains("CheckedListBox"),
                $"{source.Name}:又出现了 CheckedListBox——它绕过 Ui.List 工厂，选中行会退回系统高亮蓝（DESIGN.md §6「列表行」）。");
            Assert.Empty(Regex.Matches(code, @"(?<!Form)BorderStyle\.Fixed(?:Single|3D)"));
        }
    }

    private static string CodeLines(string text) =>
        string.Join('\n', text.Split('\n')
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal) && !l.StartsWith("*", StringComparison.Ordinal)));

    // ---------- 行文字口径 ----------

    [Theory]
    [InlineData("订单", new string[] { }, "订单")]
    [InlineData("订单", new[] { "/api/orders" }, "订单（/api/orders）")]
    [InlineData("订单", new[] { "/api/orders", "/api/bill" }, "订单（/api/orders、/api/bill）")]
    [InlineData("财务", new[] { "/api/finance" }, "财务（/api/finance）")]
    public void ModuleRowText_ShowsTheNameAndItsPrefixes(string name, string[] prefixes, string expected)
    {
        var module = new ModuleScope { Name = name, PathPrefixes = prefixes.ToList() };
        Assert.Equal(expected, TaskCreation.ModuleRowText(module));
    }

    [Fact]
    public void ModuleRowText_NeverLeavesAnEmptyBracket()
    {
        // 只有名称没有前缀的模块是合法配置（ProfileAdvanced.ParseModules 认没有等号的行），
        // 那一格括号里没有任何东西时，括号本身既不是信息也不是缺省。
        var text = TaskCreation.ModuleRowText(new ModuleScope { Name = "订单" });
        Assert.Equal("订单", text);
        Assert.DoesNotContain("（）", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleRowText_KeepsTheCheckedNameReachable()
    {
        // 显示文字怎么变都不影响交出去的模块名：名字在行载荷上，不在这一格文字里。
        var module = new ModuleScope { Name = "订单", PathPrefixes = ["/api/orders"] };
        Assert.Equal(module.Name, TaskCreation.ModuleRowText(module).Split('（')[0]);
    }

    // ---------- 共享编译与设计基线 ----------

    [Fact]
    public void SharedCompile_IncludesListChecks()
    {
        // 口径只有一份：真值表跑的就是主工程同一份代码（第五十四、五十五、五十九轮同一记录方式）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListChecks.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsTheChecklistGesture()
    {
        // 断言取本轮条款独有的字面，不取那些节里早已被别轮写过的那几句。
        var section = Repo.DesignSection(6);
        Assert.Contains("**复选清单的整行勾选**（`ListChecks` 单一口径，2026-11-08 第六十三轮", section);
        Assert.Contains("让开系统自己会翻的那一格", section);
        Assert.Contains("右键不勾选", section);
        Assert.Contains("同步预览的接口清单未改一行代码", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 复选清单入厂与整行勾选（§6） \|", ledger);
        Assert.Contains("ListChecks.ShouldToggle(左键, 命中行, 命中勾选框)", ledger);
        Assert.Contains("一格空括号既不是信息也不是缺省", ledger);
    }

    [Fact]
    public void DesignDoc_PointsThePriorDeferralsAtThisRound()
    {
        // 已被取代的挂账必须指向收口，否则下一轮读到的是「仍然如此」（第六十、六十二轮同一条纪律）。
        var ledger = Repo.DesignSection(8);
        var rows = ledger.Split('\n').Where(l => !l.StartsWith("| 复选清单入厂与整行勾选（§6） |", StringComparison.Ordinal)).ToList();
        foreach (var anchor in new[] { "新建任务的模块清单是 CheckedListBox 不经工厂", "ComboBox 与 CheckedListBox 不在本轮", "CheckedListBox 与 ComboBox 不在本入口" })
        {
            var row = rows.Single(l => l.Contains(anchor));
            Assert.Contains("第六十三轮", row);
        }

        var own = Slice(ledger, "| 复选清单入厂与整行勾选（§6） |", "\n| 输入框（§6） |");
        Assert.Contains("真机", own);
        Assert.Contains("ComboBox 仍在工厂与本入口之外", own);
        Assert.DoesNotContain("已全量接线", own, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_TableRowsAreAllClosed()
    {
        // 表格行缺收尾竖线会让整张表从那一行断成两段（第六十二轮核到第五十五轮那行如此，点名留给本轮收口）。
        foreach (var number in new[] { 6, 7, 8 })
        {
            foreach (var line in Repo.DesignSection(number).Split('\n').Where(l => l.StartsWith("| ", StringComparison.Ordinal)))
            {
                Assert.True(line.EndsWith("|", StringComparison.Ordinal), $"DESIGN.md §{number} 有表格行缺收尾竖线：{line[..Math.Min(40, line.Length)]}…");
            }
        }
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
