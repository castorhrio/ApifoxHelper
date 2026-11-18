using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 作业区那两颗钮（「开始扫描」与「停止」）「换显隐之后键盘落在哪一颗」（第三百九十二轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗按钮在第三百七十六轮接了「被藏起来就把键盘交给还在屏上的那一颗」
/// （<see cref="BannerFocusPlan"/>）。作业区这一对是<b>同一形状</b>：作业起来时「开始扫描」被藏起来、
/// 「停止」显出来；作业结束时「停止」被藏起来、「开始扫描」显回来——这一对此前没有落点纪律。
/// 焦点正落在被藏起来的那颗上时，框架当场把焦点挪走（wine 96 档实测落在父面那一行上），于是
/// 「按完开始扫描，键盘不在任何能按的东西上」：想停掉这一支作业，得先 Tab 一轮找回那颗「停止」。
/// 这一处此前没有任何门：横幅那一族判的是<b>横幅</b>那两颗，判不到这一对。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="JobFocusPlan.Landing"/> 的十六种组合逐格对账；
/// ② 顺序（源码门）：焦点必须在换显隐<b>之前</b>记下来（<c>Visible = false</c> 一落下去框架当场
///    就把焦点挪走了，之后再读 <c>Focused</c> 永远答「不用交」）；
/// ③ 唯一落点（源码门）：这一对的两颗 <c>Focus()</c> 全文件只出现在 <c>ShowJobState</c> 里、各一次；
/// ④ 算式只有一份（源码门）：横幅与作业区是同一条规则，两处都调 <c>FocusHandover.Landing</c>，
///    真值表在产品源码里只出现一份（<c>FocusHandover.cs</c>）；
/// ⑤ 冒烟段 <c>RunJobFocusHandover</c> 里真的量了这件事（真的起一支在跑的作业 → 读「键盘落到那颗『停止』上」
///    → 作业结束 → 读「键盘回到那颗『开始扫描』上」）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「作业该不该起」「停止」那颗该不该停用（那是各屏自己的口径）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）。
/// </para>
/// </summary>
public class JobFocusPlanTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string ShowJobStateHeader = "private void ShowJobState(JobInfo? job)";

    /// <summary>
    /// ① 判定本身：十六种组合逐格对账（真执行 <see cref="JobFocusPlan.Landing"/>，不是抄一份真值表）。
    /// <para>
    /// 每一格都写成「原本聚焦在哪一颗 / 换完显隐之后哪几颗还在屏上 → 该交给哪一颗」，
    /// 期望值按<b>理由</b>给：还在屏上就不动（它自己还拿着键盘）、被藏起来就交给另一颗、
    /// 两颗都不在屏上时不猜（这一行没有可交的落点）。
    /// </para>
    /// </summary>
    [Fact]
    public void Landing_AnswersWhichButtonKeepsTheKeyboard()
    {
        // 两颗都没有焦点：无论显隐如何都不动键盘（这一页不该替用户挑落点）。
        foreach (var scanVisible in new[] { true, false })
        {
            foreach (var stopVisible in new[] { true, false })
            {
                Assert.Equal(JobFocusPlan.None,
                    JobFocusPlan.Landing(false, false, scanVisible, stopVisible));
            }
        }

        // 焦点原本在「开始扫描」上：它还在屏上就什么都不做；被藏起来就交给「停止」；两颗都不在屏上时不猜。
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(true, false, true, true));
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(true, false, true, false));
        Assert.Equal(JobFocusPlan.Stop, JobFocusPlan.Landing(true, false, false, true));
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(true, false, false, false));

        // 焦点原本在「停止」上：同一套规则，方向相反——作业结束时键盘该回到「开始扫描」。
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(false, true, true, true));
        Assert.Equal(JobFocusPlan.Scan, JobFocusPlan.Landing(false, true, true, false));
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(false, true, false, true));
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(false, true, false, false));

        // 两颗都记到焦点（同一帧里不可能，但判定不许因此乱交）：按「开始扫描」那一支答，不猜第二颗。
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(true, true, true, true));
        Assert.Equal(JobFocusPlan.Stop, JobFocusPlan.Landing(true, true, false, true));
        Assert.Equal(JobFocusPlan.None, JobFocusPlan.Landing(true, true, false, false));
    }

    /// <summary>
    /// ④ 算式只有一份：横幅那一族与作业区这一族是同一条规则，两处都调 <c>FocusHandover.Landing</c>；
    /// 真值表在产品源码里只出现一份（在 <c>FocusHandover.cs</c>）。
    /// <para>
    /// 两处各抄一份真值表就是两份可能各自漂移的算式——所以这一条判「只有一份」而不是「哪一份对」。
    /// </para>
    /// </summary>
    [Fact]
    public void OneTruthTable_ServesBothRows()
    {
        var banner = Repo.Source("Desktop", "Native", "BannerFocusPlan.cs");
        var job = Repo.Source("Desktop", "Native", "JobFocusPlan.cs");
        var core = Repo.Source("Desktop", "Native", "FocusHandover.cs");

        // 两处各带一套词汇，各自带一次调用——不多不少。
        Assert.Contains("FocusHandover.Landing(primaryHadFocus, secondaryHadFocus, primaryVisible, secondaryVisible)", banner);
        Assert.Contains("FocusHandover.Landing(scanHadFocus, stopHadFocus, scanVisible, stopVisible)", job);

        // 真值表那一句只在算式那一份里；两处词汇文件里不许再抄。
        const string table = "if (firstHadFocus) return firstVisible ? None : (secondVisible ? Second : None);";
        Assert.Contains(table, core);
        Assert.DoesNotContain(table, banner);
        Assert.DoesNotContain(table, job);

        // 算式本身不许再长出第二份调用点（它是纯判定，不碰控件）。
        var coreCalls = Count(core, "FocusHandover.Landing(");
        Assert.Equal(0, coreCalls);
    }

    /// <summary>
    /// ② 顺序与 ③ 唯一落点（源码门）：焦点在换显隐<b>之前</b>记下来；这一对的两颗 <c>Focus()</c>
    /// 全文件只出现在 <c>ShowJobState</c> 里、各一次；交键盘只由判定决定。
    /// </summary>
    [Fact]
    public void ShowJobState_RecordsFocusBeforeToggling_AndHandsTheKeyboardOnlyByThePlan()
    {
        var problems = Problems(TasksPage, Smoke);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>
    /// 把两份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让自测能拿<b>注入过的</b>源码喂它——与
    /// <c>BannerFocusPlanTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(string tasksPage, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(tasksPage);

        var show = Body(code, ShowJobStateHeader);
        if (show is null)
        {
            problems.Add("TasksPage 里找不到 ShowJobState 的方法体：本门按它判「先记焦点、再换显隐、最后交键盘」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            // ② 顺序：焦点必须在换显隐之前记下来。
            var scanRecorded = show.IndexOf("var scanHadFocus = _scanButton.Focused;", StringComparison.Ordinal);
            var stopRecorded = show.IndexOf("var stopHadFocus = _stopButton.Focused;", StringComparison.Ordinal);
            var scanToggled = show.IndexOf("_scanButton.Visible =", StringComparison.Ordinal);
            var stopToggled = show.IndexOf("_stopButton.Visible =", StringComparison.Ordinal);

            if (scanRecorded < 0 || stopRecorded < 0)
            {
                problems.Add("ShowJobState 里没有在换显隐之前记下焦点（应为 var scanHadFocus = _scanButton.Focused; "
                    + "与 var stopHadFocus = _stopButton.Focused;）：不记就永远答「不用交」，"
                    + "被藏起来的那一颗拿着键盘时没人接。");
            }
            else if (scanToggled < 0 || stopToggled < 0)
            {
                problems.Add("ShowJobState 里找不到换显隐那两行（_scanButton.Visible = / _stopButton.Visible =）："
                    + "本门按「记焦点在换显隐之前」判顺序，那两行没了这一判就没有对象。");
            }
            else if (scanRecorded > scanToggled || stopRecorded > stopToggled)
            {
                problems.Add("ShowJobState 里「记焦点」排在换显隐之后：Visible = false 一落下去框架当场就把焦点挪走了，"
                    + "之后再读 Focused 读到的是「已经不在它身上」，那一判永远答「不用交」。"
                    + "焦点必须在换显隐之前记下来。");
            }

            // 交键盘只由判定决定，两颗各一次。
            foreach (var (field, constant) in new[]
                     {
                         ("_scanButton", "JobFocusPlan.Scan"),
                         ("_stopButton", "JobFocusPlan.Stop"),
                     })
            {
                var handoff = $"if (landing == {constant}) {field}.Focus();";
                if (!show.Contains(handoff, StringComparison.Ordinal))
                {
                    problems.Add($"ShowJobState 里没有按判定交键盘（应为 {handoff}）："
                        + "无条件交给某一颗会让「焦点原本在哪」这件事不再被读，"
                        + "下一轮把另一颗也接上时就会静默抢键盘。");
                }
            }

            if (!show.Contains("JobFocusPlan.Landing(scanHadFocus, stopHadFocus, "
                    + "_scanButton.Visible, _stopButton.Visible)", StringComparison.Ordinal))
            {
                problems.Add("ShowJobState 里没有把「换显隐之前记下的焦点」与「换显隐之后的可见性」一起喂给 "
                    + "JobFocusPlan.Landing：判定要的是这两半，少一半就答不出该交给谁。");
            }
        }

        // ③ 唯一落点：这一对的两颗 Focus() 全文件只出现在 ShowJobState 里、各一次。
        foreach (var field in new[] { "_scanButton", "_stopButton" })
        {
            var total = Count(code, $"{field}.Focus();");
            var inside = show is null ? 0 : Count(show, $"{field}.Focus();");
            if (total != 1 || inside != 1)
            {
                problems.Add($"TasksPage 里 {field}.Focus() 出现 {total} 处（其中在 ShowJobState 里 {inside} 处，"
                    + "应为 1 / 1）：作业区这一行的键盘落点只该有一处，第二处就是第二个说了算的人。");
            }
        }

        // 判定在产品源码里恰好调用一处。
        var landings = Count(code, "JobFocusPlan.Landing(");
        if (landings != 1)
        {
            problems.Add($"产品源码里调用 JobFocusPlan.Landing 出现 {landings} 处（应为 1 处，在 ShowJobState 里）："
                + "0 处 = 判定没人用；多于 1 处 = 有第二处也在决定键盘落点。");
        }

        // ⑤ 冒烟段真的量了这件事（两支都要在段体里：起作业那一支与作业结束那一支）。
        var body = Body(CodeScan.Text(smoke), "private static void RunJobFocusHandover(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunJobFocusHandover 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("scanButton.Focus();", "把键盘真的放到那颗「开始扫描」上"),
                         ("stopButton.Focused", "读「键盘落到了那颗『停止』上」"),
                         ("scanButton.Focused", "读「作业结束后键盘回到那颗『开始扫描』上」"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunJobFocusHandover 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「作业真在跑的时候键盘交给那颗『停止』」与「作业结束之后键盘回到那颗"
                        + "『开始扫描』」两支，少了这一句就量不到那一支。");
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
