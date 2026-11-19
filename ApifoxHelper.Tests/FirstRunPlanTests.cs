using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 首程（一台什么都没装的机器上第一次打开）任务页空态那颗按钮指向哪一步（第四百零五轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：任务页空态的说明写着「先在「项目设置」新建配置，然后点「新建任务」开始源码扫描」，
/// 而空态上<b>唯一那颗按钮</b>是「新建任务」。第一次打开的人照着屏上唯一可点的东西按下去，
/// <c>ShowNewTaskDialog</c> 读到配置清单是空的，往状态行写一句「还没有项目配置。请先在「项目设置」新建配置，
/// 再创建任务。」就返回了——<b>屏上没有任何一步能到「项目设置」</b>：侧栏那颗导航按钮在，但空态那句话
/// 与那颗按钮都没有把用户送过去。这与第四十二轮「禁用不得移除下一步」、第五十轮「提示所指引的恢复路径不可达」、
/// 第四十六轮「文字不指向不存在的按钮」是同一族：<b>这一次是「空态唯一那颗按钮点下去被拒，而拒绝之后屏上没有下一步」</b>。
/// </para>
/// <para>
/// <b>这一条判什么</b>：① 判定本身（<see cref="FirstRunPlan.EmptyActionFor"/> 的四种入参逐格对账，真执行）；
/// ② 文字与判定同一份（<see cref="FirstRunPlan.Label"/> / <see cref="FirstRunPlan.Hint"/> 与判定同源，
/// 说明里点名的入口就是那一刻屏上真的那一颗）；③ 接线（源码门：空态那颗按钮按记下的那一笔分发、
/// 两条路各自汇到既有唯一入口、壳把「去建配置」这一跳接到项目设置页的落点上）；
/// ④ 判据自己被抓得住（拿注入的坏源码喂一遍，每一发都必须报出自己那一处）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上切页之后键盘落在哪、对话框弹出来的位置（随 G-D5 真机复核）；
/// 也不判「配置页那个新建对话框长什么样」（那是 <c>ProfileDialog</c> 自己的事）。
/// </para>
/// </summary>
public class FirstRunPlanTests
{
    // ---------- ① 判定：四种入参逐格对账 ----------

    [Fact]
    public void EmptyActionFor_IsTheTruthTableOfTheFirstRunStep()
    {
        // 一份配置都没有 = 首程：那颗按钮指向「新建配置」。
        Assert.Equal(FirstRunPlan.EmptyAction.NewProfile, FirstRunPlan.EmptyActionFor(0));
        // 有配置（一份或多份）= 缺的只是任务：指向「新建任务」。
        Assert.Equal(FirstRunPlan.EmptyAction.NewTask, FirstRunPlan.EmptyActionFor(1));
        Assert.Equal(FirstRunPlan.EmptyAction.NewTask, FirstRunPlan.EmptyActionFor(7));
        // 认不出（读不出来）= 按「有配置」算：不把「读不出来」说成「还没有配置」。
        Assert.Equal(FirstRunPlan.EmptyAction.NewTask, FirstRunPlan.EmptyActionFor(null));
    }

    [Fact]
    public void Label_IsTheSameDecisionAsTheAction()
    {
        // 文字与判定同源：界面不另判一次「有没有配置」，否则屏上那颗字会与点下去那件事对不上。
        Assert.Equal("新建配置", FirstRunPlan.Label(FirstRunPlan.EmptyAction.NewProfile));
        Assert.Equal("新建任务", FirstRunPlan.Label(FirstRunPlan.EmptyAction.NewTask));
        Assert.Equal(FirstRunPlan.NewProfileLabel, FirstRunPlan.Label(FirstRunPlan.EmptyAction.NewProfile));
        Assert.Equal(FirstRunPlan.NewTaskLabel, FirstRunPlan.Label(FirstRunPlan.EmptyAction.NewTask));
    }

    // ---------- ② 文字：说明点名的入口就是那一刻屏上真的那一颗 ----------

    [Fact]
    public void Hint_NamesTheButtonThatIsOnScreenAtThatMoment()
    {
        // 首程那一句：第一步就是空态那颗按钮（「新建配置」），第二步是它建完配置回来之后同一位置换回的那一颗。
        var firstRun = FirstRunPlan.Hint(FirstRunPlan.EmptyAction.NewProfile);
        Assert.Contains(FirstRunPlan.NewProfileLabel, firstRun);
        Assert.Contains(FirstRunPlan.NewTaskLabel, firstRun);
        // 有配置那一句保持原样：那时空态那颗按钮本来就是「新建任务」，页面名取共享常量。
        var withProfile = FirstRunPlan.Hint(FirstRunPlan.EmptyAction.NewTask);
        Assert.Contains(PageNames.Projects, withProfile);
        Assert.Contains(FirstRunPlan.NewTaskLabel, withProfile);
        // 两句不是同一句：首程那一态说的是「先建配置」，有配置那一态说的是「去建任务」。
        Assert.NotEqual(firstRun, withProfile);
        // 首程那一句不再把用户指去侧栏（那是「自己想到去点左边那一栏」），它点名的是屏上那颗按钮。
        Assert.DoesNotContain(PageNames.Projects, firstRun);
    }

    [Fact]
    public void NoProfileStatus_SaysWhatHappenedAndWhatIsNext()
    {
        // 一份配置都没有时点「新建任务」那一句：说「还没有配置」与「先建配置」两件事，
        // 不冒充「创建失败」——这一次根本没有走到创建那一步；页面名取共享常量。
        Assert.Contains(PageNames.Projects, FirstRunPlan.NoProfileStatus);
        Assert.Contains(FirstRunPlan.NewProfileLabel, FirstRunPlan.NoProfileStatus);
        Assert.DoesNotContain("失败", FirstRunPlan.NoProfileStatus);
    }

    // ---------- ③ 接线：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void TasksPage_RoutesTheEmptyButtonThroughTheSingleDecision()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 点击只读 RefreshEmptyState 记下的那一笔，不在点击里再判一次「有没有配置」。
        Assert.Matches(@"(?m)^\s*_emptyCreate\.Click \+= \(_, _\) => RunEmptyCreate\(\);", page);
        var dispatch = Slice(page, "private void RunEmptyCreate()", "private void ShowNewTaskDialog()");
        Assert.Contains("if (_emptyAction == FirstRunPlan.EmptyAction.NewProfile)", dispatch);
        Assert.Contains("NewProfileRequested?.Invoke();", dispatch);
        Assert.Contains("ShowNewTaskDialog();", dispatch);
        // 判定与文字同一轮推导：空态刷新里三件事一起换（判定、说明、按钮文字）。
        var refresh = Slice(page, "private void RefreshEmptyState()", "private int? ProfileCount()");
        Assert.Contains("_emptyAction = FirstRunPlan.EmptyActionFor(ProfileCount());", refresh);
        Assert.Contains("FirstRunPlan.Hint(_emptyAction)", refresh);
        Assert.Contains("_emptyCreate.Text = FirstRunPlan.Label(_emptyAction);", refresh);
        // 读不出来答 null（不把「读不出来」说成「读到了空」），且不缓存。
        var count = Slice(page, "private int? ProfileCount()", "private void RunEmptyCreate()");
        Assert.Contains("return _profiles.List().Count;", count);
        Assert.Contains("return null;", count);
        // 那一句状态行的字面只有一份（在 FirstRunPlan 里），本页只引用。
        Assert.Contains("SetStatus(FirstRunPlan.NoProfileStatus, UiTokens.WarnText);", page);
        Assert.DoesNotContain("还没有项目配置。请先在", page);
    }

    [Fact]
    public void Shell_WiresTheFirstRunJumpToTheProjectsPage()
    {
        // 两页互不引用：请求由任务页发出，切页与落点由壳接（与「打开配置」那一跳同一纪律）。
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        var wiring = Slice(shell, "_tasksPage.NewProfileRequested += () =>", "_content.Controls.Clear();");
        Assert.Contains("ShowProjects();", wiring);
        Assert.Contains("_projectsPage.OpenNewProfile();", wiring);
        // 落点仍走配置页那一个唯一入口（工具栏、空态与这一跳共用），不复制创建逻辑。
        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*public void OpenNewProfile\(\) => ShowDialog\(null\);", projects);
        // 任务页不自己切页、不引用另一页。
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.DoesNotContain("ProjectsPage", page);
        Assert.DoesNotContain("ShowProjects", page);
    }

    // ---------- ④ 判据自己被抓得住 ----------

    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // 夹具：一份合规的任务页片段，与四发注入（每一发只坏一处）。
        const string good = """
            _emptyCreate.Click += (_, _) => RunEmptyCreate();
            private void RunEmptyCreate()
            {
                if (_emptyAction == FirstRunPlan.EmptyAction.NewProfile)
                {
                    NewProfileRequested?.Invoke();
                    return;
                }
                ShowNewTaskDialog();
            }
            private void RefreshEmptyState()
            {
                _emptyAction = FirstRunPlan.EmptyActionFor(ProfileCount());
                _emptyHint.Text = FirstRunPlan.Hint(_emptyAction);
                _emptyCreate.Text = FirstRunPlan.Label(_emptyAction);
            }
            private int? ProfileCount()
            {
                try { return _profiles.List().Count; }
                catch (Exception) { return null; }
            }
            private void ShowNewTaskDialog()
            {
                SetStatus(FirstRunPlan.NoProfileStatus, UiTokens.WarnText);
            }
            """;
        Assert.Empty(Breaks(good));

        // ① 点击绕过唯一分发（回到「一律新建任务」）。
        Assert.Contains(Breaks(good.Replace(
            "_emptyCreate.Click += (_, _) => RunEmptyCreate();",
            "_emptyCreate.Click += (_, _) => ShowNewTaskDialog();")), p => p.Contains("RunEmptyCreate", StringComparison.Ordinal));

        // ② 分发里没有首程那一支（首程那颗按钮点下去还是「新建任务」）。
        Assert.Contains(Breaks(good.Replace(
            "if (_emptyAction == FirstRunPlan.EmptyAction.NewProfile)",
            "if (false)")), p => p.Contains("NewProfileRequested", StringComparison.Ordinal));

        // ③ 按钮文字不随判定换（屏上那颗字与点下去那件事对不上）。
        Assert.Contains(Breaks(good.Replace(
            "_emptyCreate.Text = FirstRunPlan.Label(_emptyAction);",
            "")), p => p.Contains("Label", StringComparison.Ordinal));

        // ④ 读不出来答 0（把「读不出来」说成「读到了空」）。
        Assert.Contains(Breaks(good.Replace(
            "catch (Exception) { return null; }",
            "catch (Exception) { return 0; }")), p => p.Contains("null", StringComparison.Ordinal));
    }

    /// <summary>
    /// 拿一段任务页源码文本判「接线对不对」，返回每一处不符（空 = 对得上）。
    /// <para>
    /// 判据写成「文本进、问题出」的一段函数，是为了让上面那条注入自验能真的喂坏源码进来——
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异）。
    /// </para>
    /// </summary>
    private static List<string> Breaks(string page)
    {
        var problems = new List<string>();
        if (!Regex.IsMatch(page, @"(?m)^\s*_emptyCreate\.Click \+= \(_, _\) => RunEmptyCreate\(\);"))
        {
            problems.Add("空态那颗按钮没有走 RunEmptyCreate 这一处唯一分发。");
        }
        if (!page.Contains("if (_emptyAction == FirstRunPlan.EmptyAction.NewProfile)", StringComparison.Ordinal)
            || !page.Contains("NewProfileRequested?.Invoke();", StringComparison.Ordinal))
        {
            problems.Add("分发里没有首程那一支：NewProfileRequested 没有被发出去。");
        }
        if (!page.Contains("_emptyCreate.Text = FirstRunPlan.Label(_emptyAction);", StringComparison.Ordinal))
        {
            problems.Add("按钮文字没有随 FirstRunPlan.Label 换：屏上那颗字会与点下去那件事对不上。");
        }
        if (!page.Contains("return null;", StringComparison.Ordinal))
        {
            problems.Add("配置清单读不出来时没有答 null：把「读不出来」说成了「读到了空」。");
        }
        return problems;
    }

    /// <summary>取 [start, end) 之间的源码段：把断言限定在方法体里。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
