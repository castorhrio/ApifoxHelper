using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 两个主页面的名字只有一个落点（<see cref="PageNames"/>，第二百五十七轮，具名 pass：clarify + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：首程（一台什么都没装的机器上第一次打开）这一屏的下一步全靠一句话指路——
/// 任务页空态说「先在「项目设置」新建配置」、没有配置时点「新建任务」的状态行说「请先在「项目设置」新建配置」、
/// 响应契约与接口追踪的提示说「请在项目设置中填写…」、七个任务阶段对话框说「在任务页点「开始检查」」。
/// 这些句子指名的那个地方，就是侧栏上那颗按钮。而按钮文字此前是 <c>NavButton("项目设置")</c> 就地写的一串字面量，
/// 引导文案里又是另一串字面量：<b>改一处不改另一处，界面就会指着一个不存在的页面</b>——
/// 与第四十六轮「文字不指向不存在的按钮」、第五十轮「提示所指引的恢复路径不可达」是同一族，
/// 只是这次指向的是页面而不是按钮。更细的一层：侧栏写「检查任务」，文案写「任务页」——
/// 用户按字面在侧栏上找不到那一页，这一族连「名字对不对得上」都没人判过。
/// </para>
/// <para>
/// <b>这一族判什么</b>：① 名字本身（两个、不空、不带「」、不带首尾空白）；② 名字与<b>页面键</b>是两件事
/// （<see cref="LastPageKeys"/> 是落盘的值，改名字不该让上次停留的页面失效）；③ 侧栏按钮、菜单项与
/// 每一处引导文案都从这一份取，且<b>各几处</b>逐处钉死（第二百四十四、二百四十五轮的课：
/// 接线型断言只判「有没有」时，新加一处没接线的文案不会被发现）；④ 界面源码里不许再出现
/// 「项目设置」「检查任务」的引号字面量，也不许出现「任务页」这种侧栏上找不到的简称。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「这句话该不该指这一页」——那是产品判断，只有人读得出来。
/// 也不判真机上那颗按钮的观感（随 G-D5 真机复核）。
/// </para>
/// </summary>
public class PageNamesTests
{
    /// <summary>
    /// 引导文案里点名某一页的地方：文件 → 该文件里必须出现的次数（第二百五十七轮逐处数出来的）。
    /// <para>
    /// 次数是<b>判据</b>不是读数：新加一处「请到某一页去做某件事」而没接上共享口径，这里当场红并点名文件；
    /// 删掉一处文案也要回来改这个数——「少了一处」与「多了一处」都该有人看一眼。
    /// </para>
    /// </summary>
    private static readonly (string File, string Name, int Count)[] GuideSites =
    [
        // 侧栏按钮 + 视图菜单项：两处都从共享口径取。
        ("NativeShellForm.cs", "PageNames.Projects", 2),
        ("NativeShellForm.cs", "PageNames.Tasks", 2),
        // 首程指路：任务页空态与「还没有配置」的状态行。
        ("TasksPage.cs", "PageNames.Projects", 2),
        // 响应契约 / 接口追踪：分析设置在哪一页。
        ("ContractsDialog.cs", "PageNames.Projects", 1),
        ("EndpointTrace.cs", "PageNames.Projects", 1),
        ("EndpointTraceDialog.cs", "PageNames.Projects", 1),
        // 七个任务阶段对话框：「先在某一页点某个按钮」。
        ("DecisionsDialog.cs", "PageNames.Tasks", 1),
        ("PropagationDialog.cs", "PageNames.Tasks", 2),
        ("CheckResultsDialog.cs", "PageNames.Tasks", 3),
        ("SupplementsDialog.cs", "PageNames.Tasks", 2),
        ("CandidatesDialog.cs", "PageNames.Tasks", 2),
        ("BuildLogDialog.cs", "PageNames.Tasks", 2),
        ("SyncPreviewDialog.cs", "PageNames.Tasks", 2),
    ];

    /// <summary>
    /// 允许在字符串字面量里出现「任务页」的文件（封闭名单，每条带理由）。
    /// 名单里的文件<b>现在仍然</b>必须命中——不再命中即红，挂账不会烂在仓里。
    /// </summary>
    private static readonly (string File, string Reason)[] ShorthandAllowlist =
    [
        ("MeasuredScreens.cs", "冒烟报告里的屏名（「检查任务页读取失败态」），不是给用户看的引导文案；"
            + "它描述的是「哪一屏」，不指路，用户读不到"),
    ];

    // ── 名字本身 ─────────────────────────────────────────────────────

    [Fact]
    public void Names_AreTheTwoPagesOnTheSidebar()
    {
        Assert.Equal("项目设置", PageNames.Projects);
        Assert.Equal("检查任务", PageNames.Tasks);
        Assert.NotEqual(PageNames.Projects, PageNames.Tasks);
    }

    [Fact]
    public void Names_CarryNoBracketsOrPadding()
    {
        // 名字是「裸的」：调用处自己加「」或加「页」。名字自带引号会让「「项目设置」」这种双引号出现在界面上。
        foreach (var name in new[] { PageNames.Projects, PageNames.Tasks })
        {
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.Equal(name.Trim(), name);
            Assert.DoesNotContain("「", name);
            Assert.DoesNotContain("」", name);
            Assert.DoesNotContain("页", name);
        }
    }

    [Fact]
    public void Names_AreNotThePageKeys()
    {
        // 页面键是落盘的值（上次停留的页面），与给人看的名字是两件事：改名字不该让记忆失效。
        Assert.Equal("projects", LastPageKeys.Projects);
        Assert.Equal("tasks", LastPageKeys.Tasks);
        Assert.NotEqual(PageNames.Projects, LastPageKeys.Projects);
        Assert.NotEqual(PageNames.Tasks, LastPageKeys.Tasks);
    }

    // ── 接线：口径存在但界面没接线等于没有 ───────────────────────────

    [Fact]
    public void Shell_TakesBothNamesFromTheSharedSource()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 侧栏按钮与菜单项各一处，都从共享口径取。
        Assert.Matches(@"(?m)^\s*_navProjects = NavButton\(PageNames\.Projects\);", shell);
        Assert.Matches(@"(?m)^\s*_navTasks = NavButton\(PageNames\.Tasks\);", shell);
        Assert.Matches(@"(?m)^\s*BoundItem\(PageNames\.Projects \+ ""\(&P\)"", ShellShortcuts\.Projects, ShowProjects\),", shell);
        Assert.Matches(@"(?m)^\s*BoundItem\(PageNames\.Tasks \+ ""\(&T\)"", ShellShortcuts\.Tasks, ShowTasks\),", shell);
        // 就地写页面名的两种旧写法不得复活。
        Assert.DoesNotMatch(@"(?m)^\s*_nav\w+ = NavButton\(""", shell);
        Assert.DoesNotMatch(@"(?m)^\s*BoundItem\(""(项目设置|检查任务)", shell);
    }

    [Fact]
    public void GuideText_TakesThePageNameFromTheSharedSource()
    {
        // 逐文件数「有几处」：新加一处没接线的引导文案，这里当场点名是哪个文件。
        foreach (var (file, name, count) in GuideSites)
        {
            var source = Repo.Source("Desktop", "Native", file);
            var actual = Regex.Matches(source, Regex.Escape(name)).Count;
            Assert.True(actual == count,
                $"{file} 里 {name} 出现 {actual} 处，登记的是 {count} 处——"
                + "新加/删掉一处引导文案要回来改 PageNamesTests.GuideSites 这个数。");
        }
    }

    [Fact]
    public void NoGuideText_InlinesAPageName()
    {
        var problems = Repo.NativeSources()
            .Where(source => source.Name != "PageNames.cs")
            .SelectMany(source => Problems(source.Name, source.Code))
            .ToList();
        Assert.True(problems.Count == 0,
            "界面源码里还有就地写的页面名（页面名只从 PageNames 取）：\n" + string.Join("\n", problems));
    }

    [Fact]
    public void ShorthandAllowlist_IsStillNeeded()
    {
        // 名单里的文件现在仍然必须命中：不再命中说明那一处已经改掉，回来删掉这一行。
        foreach (var (file, reason) in ShorthandAllowlist)
        {
            var code = CodeScan.Text(Repo.Source("Desktop", "Native", file));
            // 判的是「简称还在不在」，不是「Problems 报不报」——名单里的文件本来就被豁免，问 Problems 永远问不出来。
            Assert.True(Regex.IsMatch(code, "任务页|配置页|设置页"),
                $"{file} 已经不再出现页面名简称，ShorthandAllowlist 里那一行（理由：{reason}）该删掉了。");
        }
    }

    [Fact]
    public void PageNames_IsInTheSharedCompileList()
    {
        // 口径文件必须经共享源码编入测试程序集：断言比的是主工程同一份代码，不是抄一份。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\PageNames.cs", csproj);
    }

    // ── 判据自己被抓得住 ─────────────────────────────────────────────

    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        // 四发注入：引号字面量里的页面名、侧栏简称、菜单项就地写名、合规写法不报。
        Assert.Single(Problems("X.cs", """ShowHint("请在「项目设置」中填写。");"""));
        Assert.Single(Problems("X.cs", """ShowHint("先在任务页点「开始检查」。");"""));
        Assert.Single(Problems("X.cs", """BoundItem("检查任务(&T)", ShellShortcuts.Tasks, ShowTasks);"""));
        Assert.Empty(Problems("X.cs", """ShowHint("先在「" + PageNames.Tasks + "」页点「开始检查」。");"""));
        // 注释里引述旧文案不算（去注释口径只有一份，见 CodeScan）。
        Assert.Empty(Problems("X.cs", """// 旧文案是「请在项目设置中填写」，本轮改掉了。"""));
    }

    /// <summary>
    /// 判据本体：文本进、问题出。三种形态各一条——引号字面量里的页面名（「项目设置」/「检查任务」）、
    /// 侧栏上找不到的简称（任务页 / 配置页 / 设置页）、菜单项就地写页面名。
    /// </summary>
    private static IReadOnlyList<string> Problems(string fileName, string code)
    {
        var problems = new List<string>();
        var allowShorthand = ShorthandAllowlist.Any(entry => entry.File == fileName);

        foreach (Match literal in Regex.Matches(code, @"""[^""\r\n]*"""))
        {
            var text = literal.Value;
            if (text.Contains("「项目设置」", StringComparison.Ordinal)
                || text.Contains("「检查任务」", StringComparison.Ordinal))
            {
                problems.Add($"{fileName}：字面量 {text} 就地写了页面名，应取 PageNames。");
            }
            if (!allowShorthand
                && (text.Contains("任务页", StringComparison.Ordinal)
                    || text.Contains("配置页", StringComparison.Ordinal)
                    || text.Contains("设置页", StringComparison.Ordinal)))
            {
                problems.Add($"{fileName}：字面量 {text} 用了侧栏上找不到的页面简称，应写「" + "」页 + PageNames。");
            }
        }

        foreach (Match menu in Regex.Matches(code, @"BoundItem\(""(项目设置|检查任务)"))
        {
            problems.Add($"{fileName}：菜单项 {menu.Value} 就地写了页面名，应取 PageNames。");
        }

        return problems;
    }
}
