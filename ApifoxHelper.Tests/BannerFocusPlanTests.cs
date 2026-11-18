using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 引导横幅「刷新之后键盘落在哪一颗」（第三百七十六轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第三百六十八轮把横幅那两颗按钮改成「出厂一次、刷新只换字与显隐」，
/// 当时的理由是「藏起来的控件不参与 Tab 序、也不占位，与摘掉在屏上一样，但焦点不会因此丢」。
/// 那句话只对<b>没被聚焦</b>的那一颗成立：用户 Tab 到次动作那颗、按空格之后，那一次动作会改任务状态，
/// 紧接着的 <c>RefreshDetail</c> 把次动作<b>藏起来</b>——而此刻键盘正落在它身上。控件被藏起来时框架
/// 会把焦点挪走，挪到哪一颗不由这一页决定（wine 96 档实测落在父面那一行上），于是「按完一次就得
/// 重新 Tab 回来」。这一处此前没有任何门：<c>BannerButtonIdentityTests</c> 判的是「同一颗实例还在不在」，
/// 判不到「键盘落在哪一颗」——那要真的把窗体显示出来、真的把焦点放上去、真的走一次刷新。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="BannerFocusPlan.Landing"/> 的十六种组合逐格对账——
///    原本聚焦的那一颗还在屏上就什么都不做、被藏起来就交给另一颗、两颗都不在屏上时不猜；
/// ② 顺序（源码门）：焦点必须在换显隐<b>之前</b>记下来（<c>Visible = false</c> 一落下去框架当场
///    就把焦点挪走了，之后再读 <c>Focused</c> 永远答「不用交」）；
/// ③ 唯一落点（源码门）：交键盘只由 <see cref="BannerFocusPlan.Landing"/> 的返回值决定，
///    两颗按钮的 <c>Focus()</c> 全文件只出现在 <c>RefreshNextBanner</c> 里、各一次；
/// ④ 算式只有一份：<c>BannerFocusPlan.Landing</c> 在产品源码里恰好调用一处，界面不就地再写一遍真值表；
/// ⑤ 冒烟段 <c>RunBannerFocus</c> 里真的量了这件事（把焦点放到次动作那颗 → 走一次把它藏起来的刷新 →
///    读「键盘落到还在屏上的那一颗」）；
/// ⑥ 夹具口径（第三百七十七轮）：三发注入喂的是<b>去注释后的代码</b>，且每一发都当场核「差异落在代码区」——
///    判据读的是 <see cref="CodeScan.Text"/>，注入也必须落在同一层。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）；
/// 也不判横幅该给哪两颗动作（那是 <c>NextStep</c> 的口径，另有门）。
/// </para>
/// </summary>
public class BannerFocusPlanTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string RefreshHeader = "private void RefreshNextBanner(NextStep.Step next)";

    /// <summary>
    /// ① 判定本身：十六种组合逐格对账（真执行 <see cref="BannerFocusPlan.Landing"/>，不是抄一份真值表）。
    /// <para>
    /// 每一格都写成「原本聚焦在哪一颗 / 刷新之后哪几颗还在屏上 → 该交给哪一颗」，
    /// 期望值按<b>理由</b>给：还在屏上就不动（它自己还拿着键盘）、被藏起来就交给另一颗、
    /// 两颗都不在屏上时不猜（这一行没有可交的落点）。
    /// </para>
    /// </summary>
    [Fact]
    public void Landing_AnswersWhichButtonKeepsTheKeyboard()
    {
        // 两颗都没有焦点：无论显隐如何都不动键盘（这一页不该替用户挑落点）。
        foreach (var primaryVisible in new[] { true, false })
        {
            foreach (var secondaryVisible in new[] { true, false })
            {
                Assert.Equal(BannerFocusPlan.None,
                    BannerFocusPlan.Landing(false, false, primaryVisible, secondaryVisible));
            }
        }

        // 焦点原本在主动作上：它还在屏上就什么都不做；被藏起来就交给次动作；两颗都不在屏上时不猜。
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(true, false, true, true));
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(true, false, true, false));
        Assert.Equal(BannerFocusPlan.Secondary, BannerFocusPlan.Landing(true, false, false, true));
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(true, false, false, false));

        // 焦点原本在次动作上：同一套规则，方向相反。
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(false, true, true, true));
        Assert.Equal(BannerFocusPlan.Primary, BannerFocusPlan.Landing(false, true, true, false));
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(false, true, false, true));
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(false, true, false, false));

        // 两颗都记到焦点（同一帧里不可能，但判定不许因此乱交）：按主动作那一支答，不猜第二颗。
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(true, true, true, true));
        Assert.Equal(BannerFocusPlan.Secondary, BannerFocusPlan.Landing(true, true, false, true));
        Assert.Equal(BannerFocusPlan.None, BannerFocusPlan.Landing(true, true, false, false));
    }

    /// <summary>
    /// ②③④ 仓里当前那一份：逐条对账，把每一处「形状被改回去了」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void RefreshNextBanner_RecordsFocusBeforeTogglingAndHandsItOff()
    {
        var problems = Problems(TasksPage, Smoke);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// ① 把「记焦点」那两行删掉（判定永远读到「没有焦点」→ 永远不交键盘）；
    /// ② 把「记焦点」挪到换显隐<b>之后</b>（顺序反了，判定永远答「不用交」——这一发是本门最想钉住的）；
    /// ③ 把交键盘改成无条件交给主动作（不再读判定，下一轮接上次动作时就会静默抢键盘）；
    /// ④ 合规写法（不报）。
    /// </para>
    /// <para>
    /// <b>第三百七十七轮改的是夹具的取法，判据一条未动</b>：三发注入此前直接作用在<b>原文</b>（含注释）上，
    /// 而判据读的是去注释之后的代码——两处口径不同，注入命中的若是注释里那一句，原文变了、代码一个字没动，
    /// 那一发就白喂了。现在注入的输入是 <see cref="CodeScan.Text"/> 的结果，且每一发都经 <see cref="Inject"/>
    /// 当场核「去注释之后仍然不同」；第 ⑤ 段拿一份「同一句只在注释里」的夹具验这道守卫自己抓不抓得住。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ④ 合规写法：仓里这一份一处都不报。
        Assert.Empty(Problems(TasksPage, Smoke));

        // 注入的输入是**去注释后的代码**：判据读的是 CodeScan.Text，注入必须落在同一层。
        var code = CodeScan.Text(TasksPage);

        // ① 删掉「记焦点」：判定读到的是「两颗都没有焦点」，被藏起来的那一颗拿着键盘时没人接。
        var forgotten = Inject(code,
            "        var primaryHadFocus = _nextPrimary.Focused;\n        var secondaryHadFocus = _nextSecondary.Focused;\n",
            "");
        Assert.Contains(Problems(forgotten, Smoke),
            p => p.Contains("primaryHadFocus", StringComparison.Ordinal));

        // ② 顺序反了：把「记焦点」挪到换显隐之后——Visible = false 一落下去焦点就没了，那一判永远答「不用交」。
        var reordered = Inject(
            Inject(code,
                "        var primaryHadFocus = _nextPrimary.Focused;\n        var secondaryHadFocus = _nextSecondary.Focused;\n",
                ""),
            "        _nextSecondary.Visible = next.SecondaryLabel is not null;\n",
            "        _nextSecondary.Visible = next.SecondaryLabel is not null;\n"
            + "        var primaryHadFocus = _nextPrimary.Focused;\n        var secondaryHadFocus = _nextSecondary.Focused;\n");
        Assert.Contains(Problems(reordered, Smoke),
            p => p.Contains("换显隐之前", StringComparison.Ordinal));

        // ③ 无条件交给主动作：判定还在、但没人读它——次动作那颗拿着键盘时会被静默抢走。
        var unconditional = Inject(code,
            "        if (landing == BannerFocusPlan.Primary) _nextPrimary.Focus();\n"
            + "        else if (landing == BannerFocusPlan.Secondary) _nextSecondary.Focus();",
            "        _nextPrimary.Focus();");
        Assert.Contains(Problems(unconditional, Smoke),
            p => p.Contains("landing", StringComparison.Ordinal));

        // ⑤ 守卫自检：同一句只出现在**注释**里时，代码区一个字没变——Inject 必须当场拒绝，
        //    否则「注入命中的是注释」这件事会静默变成一发白喂的注入。
        var commentOnly = "class C\n{\n    // var primaryHadFocus = _nextPrimary.Focused;\n    void M() { }\n}\n";
        Assert.ThrowsAny<Exception>(() => Inject(commentOnly,
            "var primaryHadFocus = _nextPrimary.Focused;", ""));
    }

    /// <summary>
    /// 把 <paramref name="from"/> 换成 <paramref name="to"/>，并当场核「差异落在<b>代码区</b>」。
    /// <para>
    /// 第三百七十七轮补这一道。此前三发注入直接作用在<b>原文</b>（含注释）上，而判据读的是
    /// <see cref="CodeScan.Text"/> 去注释之后的代码——两处口径不同：注入命中的若是注释里那一句，
    /// <c>Assert.NotEqual(原文, 注入后)</c> 照样通过（原文确实变了），判据看到的代码却一个字没动，
    /// 那一发要么假绿（判据恰好因为别的原因报出同一句话）、要么假红（判据什么都不报）。
    /// 现在两件事一起做：注入的输入本身就是去注释后的代码，这一道再当场核一次「去注释之后仍然不同」。
    /// </para>
    /// </summary>
    private static string Inject(string code, string from, string to)
    {
        var injected = code.Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(code, injected);
        Assert.NotEqual(CodeScan.Text(code), CodeScan.Text(injected));
        return injected;
    }

    /// <summary>
    /// 把两份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>源码喂它——与
    /// <c>BannerButtonIdentityTests.Problems</c>、<c>DetailFocusCoordinationTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(string tasksPage, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);

        var refresh = Body(code, RefreshHeader);
        if (refresh is null)
        {
            problems.Add("TasksPage 里找不到 RefreshNextBanner 的方法体：本门按它判「先记焦点、再换显隐、最后交键盘」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            // ② 顺序：焦点必须在换显隐之前记下来。
            var primaryRecorded = refresh.IndexOf("var primaryHadFocus = _nextPrimary.Focused;", StringComparison.Ordinal);
            var secondaryRecorded = refresh.IndexOf("var secondaryHadFocus = _nextSecondary.Focused;", StringComparison.Ordinal);
            var primaryToggled = refresh.IndexOf("_nextPrimary.Visible =", StringComparison.Ordinal);
            var secondaryToggled = refresh.IndexOf("_nextSecondary.Visible =", StringComparison.Ordinal);

            if (primaryRecorded < 0 || secondaryRecorded < 0)
            {
                problems.Add("RefreshNextBanner 里没有在换显隐之前记下焦点（应为 var primaryHadFocus = _nextPrimary.Focused; "
                    + "与 var secondaryHadFocus = _nextSecondary.Focused;）：不记就永远答「不用交」，"
                    + "被藏起来的那一颗拿着键盘时没人接。");
            }
            else if (primaryToggled < 0 || secondaryToggled < 0)
            {
                problems.Add("RefreshNextBanner 里找不到换显隐那两行（_nextPrimary.Visible = / _nextSecondary.Visible =）："
                    + "本门按「记焦点在换显隐之前」判顺序，那两行没了这一判就没有对象。");
            }
            else if (primaryRecorded > primaryToggled || secondaryRecorded > secondaryToggled)
            {
                problems.Add("RefreshNextBanner 里「记焦点」排在换显隐之后：Visible = false 一落下去框架当场就把焦点挪走了，"
                    + "之后再读 Focused 读到的是「已经不在它身上」，那一判永远答「不用交」。"
                    + "焦点必须在换显隐之前记下来。");
            }

            // ③ 交键盘只由判定决定，两颗各一次。
            foreach (var (field, constant) in new[]
                     {
                         ("_nextPrimary", "BannerFocusPlan.Primary"),
                         ("_nextSecondary", "BannerFocusPlan.Secondary"),
                     })
            {
                var handoff = $"if (landing == {constant}) {field}.Focus();";
                if (!refresh.Contains(handoff, StringComparison.Ordinal))
                {
                    problems.Add($"RefreshNextBanner 里没有按判定交键盘（应为 {handoff}）："
                        + "无条件交给某一颗会让「焦点原本在哪」这件事不再被读，下一轮接上另一颗时就会静默抢键盘。");
                }
            }

            if (!refresh.Contains("BannerFocusPlan.Landing(primaryHadFocus, secondaryHadFocus, "
                    + "_nextPrimary.Visible, _nextSecondary.Visible)", StringComparison.Ordinal))
            {
                problems.Add("RefreshNextBanner 里没有把「换显隐之前记下的焦点」与「换显隐之后的可见性」一起喂给 "
                    + "BannerFocusPlan.Landing：判定要的是这两半，少一半就答不出该交给谁。");
            }
        }

        // ③ 唯一落点：两颗按钮的 Focus() 全文件只出现在 RefreshNextBanner 里、各一次。
        foreach (var field in new[] { "_nextPrimary", "_nextSecondary" })
        {
            var total = Count(code, $"{field}.Focus();");
            var inside = refresh is null ? 0 : Count(refresh, $"{field}.Focus();");
            if (total != 1 || inside != 1)
            {
                problems.Add($"TasksPage 里 {field}.Focus() 出现 {total} 处（其中在 RefreshNextBanner 里 {inside} 处，"
                    + "应为 1 / 1）：横幅这一行的键盘落点只该有一处，第二处就是第二个说了算的人。");
            }
        }

        // ④ 算式只有一份：判定在产品源码里恰好调用一处。
        var landings = Count(code, "BannerFocusPlan.Landing(");
        if (landings != 1)
        {
            problems.Add($"产品源码里调用 BannerFocusPlan.Landing 出现 {landings} 处（应为 1 处，在 RefreshNextBanner 里）："
                + "0 处 = 判定没人用；多于 1 处 = 有第二处也在决定键盘落点。");
        }

        // ⑤ 冒烟段真的量了这件事（正向与反向两支都要在段体里：判定有两条落点，读数只有一支等于另一支没量过）。
        var body = Body(CodeScan.Text(smoke), "private static void RunBannerFocus(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunBannerFocus 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("secondary.Focus();", "把键盘真的放到次动作那颗上"),
                         ("if (secondary.Visible)", "读「刷新之后次动作那颗真的被藏起来了」"),
                         ("primary.Focused", "读「键盘落到了还在屏上的主动作那颗」"),
                         ("if (!secondary.Visible)", "读「这一次刷新没有把它藏起来」（第三百八十九轮补的反向那一支的前提）"),
                         ("那是白交一次", "读「仍在屏上时不许把键盘交出去」（反向那一支的判据：什么都不做）"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunBannerFocus 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「被藏起来的那一颗正拿着键盘时，键盘交给还在屏上的那一颗」与"
                        + "「那颗仍在屏上时什么都不做」两支，少了这一句就量不到那一支。");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> / <c>foreach</c> 的花括号，
    /// 按行首关键字切会把方法体截短，而截短之后「里面没有某一行」会假绿。
    /// </para>
    /// </summary>
    private static string? Body(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var open = code.IndexOf('{', start);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}')
            {
                depth--;
                if (depth == 0) return code[open..(i + 1)];
            }
        }

        return null;
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        var at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += needle.Length;
        }

        return count;
    }
}
