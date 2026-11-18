using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 连接与写入探测那一屏那两颗钮（「运行写入探测」与「停止」）「换显隐之后键盘落在哪一颗」
/// （第三百九十四轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗在第三百七十六轮接了「被藏起来就把键盘交给还在屏上的那一颗」
/// （<see cref="BannerFocusPlan"/>），任务页作业区那两颗在第三百九十二轮接了同一条规则
/// （<see cref="JobFocusPlan"/>）。这一屏是<b>同一形状的第三处</b>：探测一起来，「运行写入探测」被藏起来、
/// 「停止」显出来；探测结束之后反过来（<c>CapabilitiesDialog.ShowProbeRunning</c>）——而这一对至今没有落点纪律。
/// 焦点正落在被藏起来的那颗上时，框架当场把焦点挪走（挪到父面那一行上），于是
/// 「按完『运行写入探测』，键盘不在任何能按的东西上」：想停掉这一次探测得先 Tab 一轮找回那颗「停止」。
/// 这一屏的探测要往返写三段内容再逐项回读，<b>中途停下是正常操作</b>——找不到那颗「停止」的代价比别的屏更高。
/// 这一处此前没有任何门：横幅那一族判的是<b>横幅</b>那两颗、作业区那一族判的是<b>作业区</b>那两颗，
/// 都判不到这一对。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="ProbeFocusPlan.Landing"/> 的十六种组合逐格对账；
/// ② 顺序（源码门）：焦点必须在换显隐<b>之前</b>记下来（<c>Visible = false</c> 一落下去框架当场
///    就把焦点挪走了，之后再读 <c>Focused</c> 永远答「不用交」），且交键盘排在 <c>RefreshRunState</c> 之后
///    （可用性先落定，交给一颗停用的钮是空操作）；
/// ③ 唯一落点（源码门）：这一对的两颗 <c>Focus()</c> 全文件只出现在 <c>ShowProbeRunning</c> 里、各一次；
/// ④ 算式只有一份（源码门）：横幅、作业区、探测区是同一条规则，三处都调 <c>FocusHandover.Landing</c>，
///    真值表在产品源码里只出现一份（<c>FocusHandover.cs</c>）；
/// ⑤ 冒烟段 <c>RunProbeFocusHandover</c> 里真的量了这件事（真的起一支在跑的探测作业 → 读「键盘落到那颗『停止』上」
///    → 探测结束 → 读「键盘回到那颗『运行写入探测』上」）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「探测该不该起」「那颗钮该不该停用」（那是这一屏自己的口径）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）。
/// </para>
/// </summary>
public class ProbeFocusPlanTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string ShowProbeRunningHeader = "private void ShowProbeRunning(JobInfo? job)";

    /// <summary>
    /// ① 判定本身：十六种组合逐格对账（真执行 <see cref="ProbeFocusPlan.Landing"/>，不是抄一份真值表）。
    /// <para>
    /// 每一格都写成「原本聚焦在哪一颗 / 换完显隐之后哪几颗还在屏上 → 该交给哪一颗」，
    /// 期望值按<b>理由</b>给：还在屏上就不动（它自己还拿着键盘）、被藏起来就交给另一颗、
    /// 两颗都不在屏上时不猜（这一行没有可交的落点）。
    /// </para>
    /// </summary>
    [Fact]
    public void Landing_AnswersWhichButtonKeepsTheKeyboard()
    {
        // 两颗都没有焦点：无论显隐如何都不动键盘（这一屏不该替用户挑落点）。
        foreach (var runVisible in new[] { true, false })
        {
            foreach (var stopVisible in new[] { true, false })
            {
                Assert.Equal(ProbeFocusPlan.None,
                    ProbeFocusPlan.Landing(false, false, runVisible, stopVisible));
            }
        }

        // 焦点原本在「运行写入探测」上：它还在屏上就不动；被藏起来就交给「停止」；两颗都不在屏上时不猜。
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(true, false, true, true));
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(true, false, true, false));
        Assert.Equal(ProbeFocusPlan.Stop, ProbeFocusPlan.Landing(true, false, false, true));
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(true, false, false, false));

        // 焦点原本在「停止」上：它还在屏上就不动；被藏起来就交回「运行写入探测」；两颗都不在屏上时不猜。
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(false, true, true, true));
        Assert.Equal(ProbeFocusPlan.Run, ProbeFocusPlan.Landing(false, true, true, false));
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(false, true, false, true));
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(false, true, false, false));

        // 两颗都记成有焦点（框架挪焦点的那一瞬可能读到两处）：按「第一颗」那一支判，不猜第二颗。
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(true, true, true, true));
        Assert.Equal(ProbeFocusPlan.Stop, ProbeFocusPlan.Landing(true, true, false, true));
        Assert.Equal(ProbeFocusPlan.None, ProbeFocusPlan.Landing(true, true, false, false));
    }

    /// <summary>
    /// ④ 算式只有一份：横幅、作业区、探测区是同一条规则，三处都调 <c>FocusHandover.Landing</c>；
    /// 真值表在产品源码里只出现一份（在 <c>FocusHandover.cs</c>）。
    /// <para>
    /// 三处各抄一份真值表就是三份可能各自漂移的算式——所以这一条判「只有一份」而不是「哪一份对」。
    /// </para>
    /// </summary>
    [Fact]
    public void OneTruthTable_ServesAllThreeRows()
    {
        var banner = Repo.Source("Desktop", "Native", "BannerFocusPlan.cs");
        var job = Repo.Source("Desktop", "Native", "JobFocusPlan.cs");
        var probe = Repo.Source("Desktop", "Native", "ProbeFocusPlan.cs");
        var core = Repo.Source("Desktop", "Native", "FocusHandover.cs");

        // 三处各带一套词汇，各自带一次调用——不多不少。
        Assert.Contains("FocusHandover.Landing(primaryHadFocus, secondaryHadFocus, primaryVisible, secondaryVisible)", banner);
        Assert.Contains("FocusHandover.Landing(scanHadFocus, stopHadFocus, scanVisible, stopVisible)", job);
        Assert.Contains("FocusHandover.Landing(runHadFocus, stopHadFocus, runVisible, stopVisible)", probe);

        // 真值表那一句只在算式那一份里；三处词汇文件里不许再抄。
        const string table = "if (firstHadFocus) return firstVisible ? None : (secondVisible ? Second : None);";
        Assert.Contains(table, core);
        Assert.DoesNotContain(table, banner);
        Assert.DoesNotContain(table, job);
        Assert.DoesNotContain(table, probe);

        // 算式本身不许再长出第二份调用点（它是纯判定，不碰控件）。
        Assert.Equal(0, Count(core, "FocusHandover.Landing("));
    }

    /// <summary>
    /// ② 顺序与 ③ 唯一落点（源码门）：焦点在换显隐<b>之前</b>记下来、交键盘排在可用性之后；
    /// 这一对的两颗 <c>Focus()</c> 全文件只出现在 <c>ShowProbeRunning</c> 里、各一次；交键盘只由判定决定。
    /// </summary>
    [Fact]
    public void ShowProbeRunning_RecordsFocusBeforeToggling_AndHandsTheKeyboardOnlyByThePlan()
    {
        var problems = Problems(Dialog, Smoke);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>
    /// 把两份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让自测能拿<b>注入过的</b>源码喂它——与
    /// <c>BannerFocusPlanTests.Problems</c>、<c>JobFocusPlanTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(string dialog, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(dialog);

        var show = Body(code, ShowProbeRunningHeader);
        if (show is null)
        {
            problems.Add("CapabilitiesDialog 里找不到 ShowProbeRunning 的方法体：本门按它判「先记焦点、再换显隐、"
                + "最后交键盘」，方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            // ② 顺序：焦点必须在换显隐之前记下来。
            var runRecorded = show.IndexOf("var runHadFocus = _runButton.Focused;", StringComparison.Ordinal);
            var stopRecorded = show.IndexOf("var stopHadFocus = _stopButton.Focused;", StringComparison.Ordinal);
            var runToggled = show.IndexOf("_runButton.Visible =", StringComparison.Ordinal);
            var stopToggled = show.IndexOf("_stopButton.Visible =", StringComparison.Ordinal);

            if (runRecorded < 0 || stopRecorded < 0)
            {
                problems.Add("ShowProbeRunning 里没有在换显隐之前记下焦点（应为 var runHadFocus = _runButton.Focused; "
                    + "与 var stopHadFocus = _stopButton.Focused;）：不记就永远答「不用交」，"
                    + "被藏起来的那一颗拿着键盘时没人接。");
            }
            else if (runToggled < 0 || stopToggled < 0)
            {
                problems.Add("ShowProbeRunning 里找不到换显隐那两行（_runButton.Visible = / _stopButton.Visible =）："
                    + "本门按「记焦点在换显隐之前」判顺序，那两行没了这一判就没有对象。");
            }
            else if (runRecorded > runToggled || stopRecorded > stopToggled)
            {
                problems.Add("ShowProbeRunning 里「记焦点」排在换显隐之后：Visible = false 一落下去框架当场就把焦点挪走了，"
                    + "之后再读 Focused 读到的是「已经不在它身上」，那一判永远答「不用交」。"
                    + "焦点必须在换显隐之前记下来。");
            }

            // 交键盘只由判定决定，两颗各一次。
            foreach (var (field, constant) in new[]
                     {
                         ("_runButton", "ProbeFocusPlan.Run"),
                         ("_stopButton", "ProbeFocusPlan.Stop"),
                     })
            {
                var handoff = $"if (landing == {constant}) {field}.Focus();";
                if (!show.Contains(handoff, StringComparison.Ordinal))
                {
                    problems.Add($"ShowProbeRunning 里没有按判定交键盘（应为 {handoff}）："
                        + "无条件交给某一颗会让「焦点原本在哪」这件事不再被读，"
                        + "下一轮把另一颗也接上时就会静默抢键盘。");
                }
            }

            if (!show.Contains("ProbeFocusPlan.Landing(runHadFocus, stopHadFocus, "
                    + "_runButton.Visible, _stopButton.Visible)", StringComparison.Ordinal))
            {
                problems.Add("ShowProbeRunning 里没有把「换显隐之前记下的焦点」与「换显隐之后的可见性」一起喂给 "
                    + "ProbeFocusPlan.Landing：判定要的是这两半，少一半就答不出该交给谁。");
            }

            // 交键盘排在可用性之后：交给一颗停用的钮是空操作，可用性必须先落定。
            var refreshedAt = show.IndexOf("RefreshRunState();", StringComparison.Ordinal);
            var handedAt = show.IndexOf("ProbeFocusPlan.Landing(", StringComparison.Ordinal);
            if (refreshedAt < 0)
            {
                problems.Add("ShowProbeRunning 里找不到 RefreshRunState();：本门按「交键盘排在可用性之后」判顺序，"
                    + "那一行没了这一判就没有对象。");
            }
            else if (handedAt >= 0 && handedAt < refreshedAt)
            {
                problems.Add("ShowProbeRunning 里「交键盘」排在 RefreshRunState 之前：可用性还没落定就把键盘交出去，"
                    + "交给一颗停用的钮是空操作，键盘照样丢在父面那一行上。");
            }
        }

        // ③ 唯一落点：这一对的两颗 Focus() 全文件只出现在 ShowProbeRunning 里、各一次。
        foreach (var field in new[] { "_runButton", "_stopButton" })
        {
            var total = Count(code, $"{field}.Focus();");
            var inside = show is null ? 0 : Count(show, $"{field}.Focus();");
            if (total != 1 || inside != 1)
            {
                problems.Add($"CapabilitiesDialog 里 {field}.Focus() 出现 {total} 处（其中在 ShowProbeRunning 里 {inside} 处，"
                    + "应为 1 / 1）：这一行的键盘落点只该有一处，第二处就是第二个说了算的人。");
            }
        }

        // 判定在产品源码里恰好调用一处。
        var landings = Count(code, "ProbeFocusPlan.Landing(");
        if (landings != 1)
        {
            problems.Add($"产品源码里调用 ProbeFocusPlan.Landing 出现 {landings} 处（应为 1 处，在 ShowProbeRunning 里）："
                + "0 处 = 判定没人用；多于 1 处 = 有第二处也在决定键盘落点。");
        }

        // ⑤ 冒烟段真的量了这件事（两支都要在段体里：起探测那一支与探测结束那一支）。
        var body = Body(CodeScan.Text(smoke), "private static void RunProbeFocusHandover(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunProbeFocusHandover 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("runButton.Focus();", "把键盘真的放到那颗「运行写入探测」上"),
                         ("stopButton.Focused", "读「键盘落到了那颗『停止』上」"),
                         ("runButton.Focused", "读「探测结束之后键盘回到那颗『运行写入探测』上」"),
                         ("jobs.Start(scope, \"写入兼容探测\"", "真的起一支在跑的探测作业（不是造一个假的运行态）"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunProbeFocusHandover 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「探测真在跑的时候键盘交给那颗『停止』」与「探测结束之后键盘回到那颗"
                        + "『运行写入探测』」两支，少了这一句就量不到那一支。");
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

    /// <summary>数一段文本里某个片段出现的次数。</summary>
    private static int Count(string text, string fragment)
    {
        var total = 0;
        var index = text.IndexOf(fragment, StringComparison.Ordinal);
        while (index >= 0)
        {
            total++;
            index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal);
        }
        return total;
    }
}
