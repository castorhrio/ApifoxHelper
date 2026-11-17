using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务详情区「交焦点」与「换落点」的协调（第三百七十三轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：详情区这一族从第三百六十八轮起连做三轮——<c>BannerButtonIdentityTests</c>
/// （横幅那两颗按钮出厂一次、刷新只换字与显隐）、<c>DetailScrollResetTests</c>（换任务回到顶部、
/// 刷新同一个任务不归位）、<c>StageStepsTests</c>（点阶段跳分区之后把键盘交给那一区）。
/// 三条门各钉一处，钉的却是同一件事的三个面：<b>详情区里「焦点落在哪」与「详情停在哪」谁先谁后、由谁说了算</b>。
/// 此前这两笔散在 <c>RefreshDetail</c>（归位）、<c>JumpToStage</c>（交焦点 + 定落点）、
/// <c>FocusFirstAction</c>（挑哪一颗）三处，每一处只知道自己那一半——下一轮再往详情区加一个入口，
/// 第四处就会自己再写一遍「先交焦点再滚」，而顺序写反了在 Linux 上没人看得见。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：落点算式把负值夹到 0；「第一颗可点」跳过禁用项、一颗都不可点时返回 −1；
/// ② 唯一落点（源码门）：<c>TasksPage.MoveDetailTo</c> 是详情区「交焦点 + 换落点」的唯一落点——
///    交焦点只调一次、落点只写一次、<c>JumpToStage</c> 自己不滚也不交焦点；
/// ③ 顺序（源码门）：交焦点发生在定落点<b>之前</b>，且这个顺序全文件只写一次；
/// ④ 算式只有一份：落点与「挑哪一颗」都取 <see cref="DetailFocusPlan"/>，界面不就地再写一遍。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「跳完在真机上看起来顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判运行时焦点真的落在哪一颗上——那是冒烟段 <c>RunStageJumpFocus</c> 的事（wine 里跑，本环境不是真机）。
/// </para>
/// </summary>
public class DetailFocusCoordinationTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private const string LandingHeader = "private void MoveDetailTo(Control? section, FlowLayoutPanel? actions)";

    private const string JumpHeader = "private void JumpToStage(string key)";

    private const string HandoffHeader = "private static void FocusFirstAction(Control row)";

    // ---------- ① 判定本身（真执行） ----------

    /// <summary>
    /// 落点算式：那一区标题的上沿再往上留一笔间距；负值一律夹到 0。
    /// <para>
    /// 夹到 0 这一半不是形式：详情刚归位时标题的 <c>Top</c> 可能小于那一笔间距，
    /// 不夹就会滚出一个负偏移（WinForms 会把它当 0，但算式本身就成了「有时是负数」）。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(779, 16, 763)]
    [InlineData(0, 16, 0)]
    [InlineData(8, 16, 0)]
    [InlineData(-40, 16, 0)]
    public void Landing_LeavesOneGapAboveTheSectionAndClampsAtZero(int sectionTop, int gap, int expected)
    {
        Assert.Equal(expected, DetailFocusPlan.Landing(sectionTop, gap));
    }

    /// <summary>
    /// 「第一颗可点」：跳过禁用项；一颗都不可点时返回 −1（焦点留在原处，不硬塞给别处）。
    /// </summary>
    [Fact]
    public void FirstSelectable_SkipsDisabledAndReportsNone()
    {
        Assert.Equal(0, DetailFocusPlan.FirstSelectable([true, true]));
        Assert.Equal(2, DetailFocusPlan.FirstSelectable([false, false, true]));
        Assert.Equal(-1, DetailFocusPlan.FirstSelectable([false, false]));
        Assert.Equal(-1, DetailFocusPlan.FirstSelectable([]));
    }

    // ---------- ②③④ 唯一落点、顺序、算式只有一份（源码门） ----------

    /// <summary>
    /// 仓里当前那一份：逐条对账，把每一处「协调规则被拆回去了」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void DetailFocus_IsCoordinatedInOnePlace()
    {
        Assert.Empty(Problems(TasksPage));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：五发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两发假变异、第二百四十三轮
    /// M4 那一发假绿）。五发分别是：① 顺序调过来（先定落点、再交焦点——交焦点那一下会把落点顶掉）；
    /// ② 在 <c>JumpToStage</c> 里再交一次焦点（第二处动键盘）；③ 去掉「认不出就不动作」那一句；
    /// ④ 把落点算式就地写回（<c>DetailFocusPlan</c> 不再被用，算式又变成两份）；⑤ 合规写法（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ⑤ 合规写法：仓里这一份一处都不报。
        Assert.Empty(Problems(TasksPage));

        // ① 顺序调过来：交焦点那一下（WinForms 把焦点控件滚进视野）会把刚定好的落点顶掉。
        var swapped = TasksPage.Replace(
            "        FocusFirstAction(actions);\n"
            + "        _detailScroll.AutoScrollPosition = new Point(0, DetailFocusPlan.Landing(section.Top, UiTokens.Lg));",
            "        _detailScroll.AutoScrollPosition = new Point(0, DetailFocusPlan.Landing(section.Top, UiTokens.Lg));\n"
            + "        FocusFirstAction(actions);",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, swapped);
        Assert.Contains(Problems(swapped), p => p.Contains("顺序", StringComparison.Ordinal));

        // ② 在 JumpToStage 里再交一次焦点：第二处动键盘，顺序就不再只有一个落点。
        var secondHandoff = TasksPage.Replace(
            "        MoveDetailTo(section, actions);",
            "        FocusFirstAction(_scanActions);\n        MoveDetailTo(section, actions);",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, secondHandoff);
        Assert.Contains(Problems(secondHandoff), p => p.Contains("交焦点", StringComparison.Ordinal));

        // ③ 去掉「认不出就不动作」那一句：认不出的键会把焦点塞给 null 那一行（或滚到一个空落点）。
        var noGuard = TasksPage.Replace(
            "        if (section is null || actions is null) return;\n",
            "",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, noGuard);
        Assert.Contains(Problems(noGuard), p => p.Contains("认不出", StringComparison.Ordinal));

        // ④ 落点算式就地写回：DetailFocusPlan 不再被用，算式又变成两份（界面一份、判定一份）。
        var inlined = TasksPage.Replace(
            "DetailFocusPlan.Landing(section.Top, UiTokens.Lg)",
            "Math.Max(0, section.Top - UiTokens.Lg)",
            StringComparison.Ordinal);
        Assert.NotEqual(TasksPage, inlined);
        Assert.Contains(Problems(inlined), p => p.Contains("算式", StringComparison.Ordinal));
    }

    /// <summary>逐条对账，返回每一处对不上的说明（空 = 对得上）。</summary>
    private static List<string> Problems(string tasksPage)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);
        var landing = SourceSlice.BodyOf(tasksPage, LandingHeader);
        var jump = SourceSlice.BodyOf(tasksPage, JumpHeader);
        var handoff = SourceSlice.BodyOf(tasksPage, HandoffHeader);

        // ② 唯一落点：交焦点只调一次、落点只写一次，两笔都在 MoveDetailTo 里。
        var handoffCalls = Regex.Matches(code, @"FocusFirstAction\(actions\);").Count;
        if (handoffCalls != 1)
        {
            problems.Add(
                $"详情区交焦点有 {handoffCalls} 处（要的是一处，在 MoveDetailTo 里）："
                + "多一处就意味着别的地方也在动键盘，顺序不再只有一个落点。");
        }

        if (!landing.Contains("FocusFirstAction(actions);", StringComparison.Ordinal))
        {
            problems.Add("MoveDetailTo 里没有把键盘交给那一区（FocusFirstAction）：跳完焦点仍留在已被滚出屏外的阶段按钮上。");
        }

        var landingWrites = Regex.Matches(landing, @"_detailScroll\.AutoScrollPosition\s*=").Count;
        if (landingWrites != 1)
        {
            problems.Add($"MoveDetailTo 里落点写了 {landingWrites} 次（要的是一次）：多写一次会把刚定好的落点再顶掉一次。");
        }

        // JumpToStage 只认键：自己不滚、也不自己交焦点。
        if (jump.Contains("AutoScrollPosition", StringComparison.Ordinal))
        {
            problems.Add("JumpToStage 里自己写了落点：落点算式与顺序要收在 MoveDetailTo 一处，这里只认键。");
        }

        if (jump.Contains("FocusFirstAction", StringComparison.Ordinal))
        {
            problems.Add("JumpToStage 里自己交了焦点：交焦点与定落点的顺序要收在 MoveDetailTo 一处，这里只认键。");
        }

        // 唯一调用点：定义一处、调用一处。
        var moveCalls = Regex.Matches(code, @"MoveDetailTo\(").Count;
        if (moveCalls != 2)
        {
            problems.Add(
                $"MoveDetailTo 出现 {moveCalls} 次（要的是两次：一处定义、一处调用）："
                + "多出来的调用点会让「谁在什么时候换落点」重新散开。");
        }

        // ③ 顺序：交焦点在定落点之前。
        var focusAt = landing.IndexOf("FocusFirstAction(actions);", StringComparison.Ordinal);
        var scrollAt = landing.IndexOf("_detailScroll.AutoScrollPosition =", StringComparison.Ordinal);
        if (focusAt < 0 || scrollAt < 0 || scrollAt < focusAt)
        {
            problems.Add(
                "MoveDetailTo 里交焦点与定落点的顺序不对（要的是先交焦点、再定落点）："
                + "反过来的话交焦点那一下会把刚定好的落点顶掉，详情停在焦点控件那一行、标题被滚出屏外。");
        }

        // 认不出的分区不动作：不猜一个分区，也不把焦点塞给别处。
        if (!Regex.IsMatch(landing, @"if\s*\(\s*section\s+is\s+null\s*\|\|\s*actions\s+is\s+null\s*\)\s*return\s*;"))
        {
            problems.Add("MoveDetailTo 里没有「认不出就不动作」那一句：认不出的键会把焦点塞给一个空行、或滚到一个空落点。");
        }

        // ④ 算式只有一份：落点与「挑哪一颗」都取 DetailFocusPlan。
        if (Regex.Matches(code, @"DetailFocusPlan\.Landing\(").Count != 1)
        {
            problems.Add(
                $"落点算式没有唯一取 DetailFocusPlan.Landing（现在 {Regex.Matches(code, @"DetailFocusPlan\.Landing\(").Count} 处）："
                + "界面就地再写一份 Math.Max，判定与界面就会各说一套。");
        }

        if (!landing.Contains("DetailFocusPlan.Landing(section.Top, UiTokens.Lg)", StringComparison.Ordinal))
        {
            problems.Add("MoveDetailTo 里的落点不是 DetailFocusPlan.Landing 算出来的：算式又变成两份。");
        }

        if (Regex.Matches(code, @"DetailFocusPlan\.FirstSelectable\(").Count != 1)
        {
            problems.Add(
                $"「第一颗可点」的判定没有唯一取 DetailFocusPlan.FirstSelectable（现在 {Regex.Matches(code, @"DetailFocusPlan\.FirstSelectable\(").Count} 处）："
                + "界面就地再写一份循环，判定与界面就会各说一套。");
        }

        if (!handoff.Contains("DetailFocusPlan.FirstSelectable(", StringComparison.Ordinal))
        {
            problems.Add("FocusFirstAction 里没有取 DetailFocusPlan.FirstSelectable：挑哪一颗的判定又散回界面。");
        }

        // 一颗都不可点时不硬塞给别处（焦点留在原处）。
        if (!Regex.IsMatch(handoff, @"if\s*\(\s*index\s*<\s*0\s*\)\s*return\s*;"))
        {
            problems.Add("FocusFirstAction 里没有「一颗都不可点就不动键盘」那一句：焦点会被硬塞给别处。");
        }

        if (handoff.Contains("Parent", StringComparison.Ordinal) || handoff.Contains("SelectNextControl", StringComparison.Ordinal))
        {
            problems.Add("FocusFirstAction 里把焦点硬塞给了别处（Parent / SelectNextControl）：一颗都不可点时焦点该留在原处。");
        }

        return problems;
    }
}
