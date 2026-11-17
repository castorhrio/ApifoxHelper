using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务详情区「换任务回到顶部、刷新同一个任务不归位」（第三百七十轮，具名 pass：harden + clarify + evidence）。
/// <para>
/// <b>补的是哪个缺口</b>：详情区是固定控件、按任务刷新文字（<c>TasksPage.RefreshDetail</c>），换任务并不重建它——
/// 滚动位置因此会留着上一条任务的那一截。于是新任务的头（名称、配置、范围、下一步横幅、三阶段进度条）整段在屏外，
/// 用户看到的是新任务中段某一节，却不知道自己看的是哪一个任务。「选中即定位」在清单那一侧早已立过
/// （<c>DESIGN.md</c> §6「列表选中定位」，第五十七轮），详情这一侧此前没有对上的规矩；
/// 冻结页面在同一件事上也是回到顶部（<c>Web/app.js</c>：换一条注释缺口详情时 <c>detailHost.scrollTop = 0</c>），
/// 属同壳同行为，不是原生另立一套。
/// </para>
/// <para>
/// <b>为什么要有这一条源码门</b>：把归位写成「每次刷新都回到顶部」时，
/// 「换任务回到顶部」照样成立——而那一条会把用户做完动作后拉回屏顶，看不见刚做完那一步的结果。
/// 这一半只有<b>条件</b>说得清（归位必须与「换的是不是同一个任务」绑定），而判条件要真的显示窗体、
/// 真的换一行选中，那是冒烟段 <c>RunDetailScrollReset</c> 的事（wine 里跑，本环境不是真机）。
/// <b>形状</b>这一半必须由 <c>dotnet test</c> 当场钉住，否则下一次重构把条件去掉，要等到下一次重烧才有人发现。
/// </para>
/// <para>
/// <b>本文件判什么</b>（只看代码行，注释里引用同一句是在说理由——去注释口径只有 <see cref="CodeScan"/> 一份）：
/// ① <c>RefreshDetail</c> 里归位那三行必须<b>与条件绑在一起</b>——条件是「当前这一截位置属于的任务 Id 不是这一个」；
/// ② 记着「位置属于哪一个任务」的那一笔<b>全文件只赋值一次</b>，且赋的是<b>当前这一个任务 Id</b>——
/// 多一次赋值就意味着「没有选中任务时把它抹掉」，而那会让刷新也归位（抹掉之后重建时条件必然成立）；
/// ③ 详情滚动的落点全文件只有两处（归位一处、点阶段跳分区一处），不多不少；
/// ④ 冒烟段 <c>RunDetailScrollReset</c> 注册在案（冒烟器与 <c>MeasuredScreens</c> 两处），
/// 且段体里真的把详情滚到底、真的换一行选中、真的读回滚动位置，<b>并反过来量「刷新同一个任务不抹掉当前位置」</b>；
/// ⑤ 换任务那一半<b>键盘路径也量过</b>（第三百七十一轮）：段体里真的按一次 <c>End</c>（<c>PressKey</c>）——
/// 程序化选中（<c>item.Selected = true</c>）与用户按方向键换行走的是同一条 <c>SelectedIndexChanged</c>，
/// 但「键盘真的落在清单上、这个键真的换得动行」只有按一次才知道；只量程序化选中时，
/// 归位在键盘换任务上是否成立没人量过。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「归位在真机上看起来顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判归位之后新任务的头该怎么写（那是 <c>TaskSummary</c> / <c>StageSteps</c> 的口径，另有门）。
/// </para>
/// </summary>
public class DetailScrollResetTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private static string Smoke => Repo.SmokeProgram;

    /// <summary>
    /// 仓里当前那一份：逐条对账，把每一处「形状被改回去了」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void DetailScrollReset_IsBoundToTheTaskIdentity()
    {
        var problems = Problems(TasksPage, Smoke);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、第二百四十三轮
    /// M4 那一发假绿）。五发分别是：① 归位去掉条件（每次刷新都归位——用户做完动作被拉回屏顶）；
    /// ② 删掉「记下位置属于哪一个任务」那一笔（条件恒成立，同上一发）；
    /// ③ 没有选中任务时把那一笔抹掉（重建时条件必然成立，刷新也跟着归位）；
    /// ④ 删掉真实按键那一行（键盘换任务那一半没人量过，第三百七十一轮补的那一发）；
    /// ⑤ 合规写法（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ④ 合规写法：仓里这一份一处都不报。
        Assert.Empty(Problems(TasksPage, Smoke));

        // ① 归位去掉条件：刷新也归位，动作做完的结果被拉出屏外。
        var unconditional = TasksPage.Replace(
            "if (_detailTaskId != task.Id)",
            "if (true)",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, unconditional);
        Assert.Contains(Problems(unconditional, Smoke),
            p => p.Contains("条件", StringComparison.Ordinal));

        // ② 删掉「记下位置属于哪一个任务」那一笔：条件恒成立，同①的后果。
        var noRemember = TasksPage.Replace(
            "            _detailTaskId = task.Id;\n",
            "",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, noRemember);
        Assert.Contains(Problems(noRemember, Smoke),
            p => p.Contains("条件", StringComparison.Ordinal));

        // ③ 没有选中任务时把那一笔抹掉：重建时条件必然成立，刷新也跟着归位。
        var clearedOnNone = TasksPage.Replace(
            "            _detailScroll.Visible = false;\n            _detailHint.Visible = true;",
            "            _detailScroll.Visible = false;\n            _detailHint.Visible = true;\n            _detailTaskId = null;",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, clearedOnNone);
        Assert.Contains(Problems(clearedOnNone, Smoke),
            p => p.Contains("只赋值一次", StringComparison.Ordinal));

        // ⑤ 键盘路径那一半没量：把真实按键那一行删掉（只剩程序化选中），
        //    归位在「用户按方向键 / End 换任务」上是否成立就没人量过了。
        var noKey = Smoke.Replace("PressKey(window, list, \"{END}\");", "", StringComparison.Ordinal);
        Assert.NotEqual(Smoke, noKey);
        Assert.Contains(Problems(TasksPage, noKey),
            p => p.Contains("PressKey", StringComparison.Ordinal));
    }

    /// <summary>逐条对账，返回每一处对不上的说明（空 = 对得上）。</summary>
    private static List<string> Problems(string tasksPage, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);

        // ① 归位那三行必须与条件绑在一起：条件是「当前这一截位置属于的任务 Id 不是这一个」。
        //    少了条件（写成每次刷新都归位）或少了其中任一行，这一发即红。
        var resetBlock = @"if\s*\(\s*_detailTaskId\s*!=\s*task\.Id\s*\)\s*\{\s*"
            + @"_detailScroll\.AutoScrollPosition\s*=\s*Point\.Empty;\s*"
            + @"_detailTaskId\s*=\s*task\.Id;\s*\}";
        if (!Regex.IsMatch(code, resetBlock))
        {
            problems.Add(
                "RefreshDetail 里「换任务归位」那一处没有与条件绑在一起"
                + "（要的是：位置属于的任务 Id 不是这一个时才归位，条件与两行必须一起在）："
                + "条件一去掉，每次刷新都归位，用户做完动作被拉回屏顶。");
        }

        // ② 记着「位置属于哪一个任务」的那一笔全文件只赋值一次，且赋的是当前这一个任务 Id。
        var assignments = Regex.Matches(code, @"_detailTaskId\s*=\s*([^;]+);").Cast<Match>().ToList();
        if (assignments.Count != 1)
        {
            problems.Add(
                $"「位置属于哪一个任务」那一笔全文件只赋值一次（现在赋了 {assignments.Count} 次，要的是一次）："
                + "多一次就意味着有一处把它抹掉了，而抹掉之后重建时条件必然成立——刷新也跟着归位。");
        }
        else if (!assignments[0].Groups[1].Value.Trim().Equals("task.Id", StringComparison.Ordinal))
        {
            problems.Add(
                $"「位置属于哪一个任务」那一笔记的是「{assignments[0].Groups[1].Value.Trim()}」"
                + "（要的是当前这一个任务 Id）：记的不是它，条件就判不出「换没换任务」。");
        }

        // 那一笔得是个字段（不是局部变量）：局部变量每次进方法都是新的，条件恒成立。
        if (!Regex.IsMatch(code, @"private\s+string\?\s+_detailTaskId\s*;"))
        {
            problems.Add("「位置属于哪一个任务」不是字段：局部变量每次进方法都是新的，条件恒成立。");
        }

        // ③ 详情滚动的落点全文件只有两处：归位一处、点阶段跳分区一处。
        var scrollWrites = Regex.Matches(code, @"_detailScroll\.AutoScrollPosition\s*=").Count;
        if (scrollWrites != 2)
        {
            problems.Add(
                $"详情滚动的落点全文件有 {scrollWrites} 处（要的是两处：换任务归位、点阶段跳分区）："
                + "多出来的那一处会在不该滚的时候把详情滚走。");
        }

        // ④ 冒烟段注册在案，且段体里两件事都量了：归位与「刷新同一个任务不抹掉当前位置」。
        if (!smoke.Contains("nameof(RunDetailScrollReset)", StringComparison.Ordinal))
        {
            problems.Add("冒烟段 RunDetailScrollReset 没有在冒烟器里注册：这一段量不到，判据自己就没人跑。");
        }

        if (!MeasuredScreens.All.Any(row => row.Section == "RunDetailScrollReset"))
        {
            problems.Add("冒烟段 RunDetailScrollReset 没有在 MeasuredScreens 里登记：这一屏显示过没有、出处在哪，没人说得清。");
        }

        var segment = SourceSlice.BodyOf(smoke, "private static void RunDetailScrollReset");
        foreach (var needed in new[]
                 {
                     "AutoScrollPosition", // 真的读回滚动位置
                     "RefreshData",        // 真的走一次刷新（反向那一半）
                     "SelectTaskRow",      // 真的换一行选中
                     "ScrollDetailToBottom", // 真的把详情滚到底
                     "PressKey",           // 键盘路径：真的按一次键（第三百七十一轮）
                     "{END}",              // 按的是「跳到末行」那一个键，不是随便一个键
                 })
        {
            if (!segment.Contains(needed, StringComparison.Ordinal))
            {
                problems.Add($"冒烟段 RunDetailScrollReset 里没有「{needed}」：那一半没量到。");
            }
        }

        return problems;
    }
}
