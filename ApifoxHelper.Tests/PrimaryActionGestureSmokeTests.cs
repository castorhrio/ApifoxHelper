using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 对话框主动作的键盘入口（<c>Ctrl+Enter</c>）在冒烟器里<b>真的被按过</b>（第三百五十九轮）。
/// <para>
/// <b>补的是哪个缺口</b>：三个审阅对话框（注释候选 / 响应契约 / 差异确认）的主动作是
/// 「接受并下一条」「确认」「确认纳入」——逐条清队列时这一个动作要按几十次。它此前只有两条路：
/// 鼠标点，或 Tab 到那颗按钮再按空格。而这三个对话框的 <c>AcceptButton</c> 是「关闭」
/// （<c>Enter</c> 在对话框里是「确认/关闭」的既有语义），于是「键盘上按一下就走下一条」
/// 在整只壳里没有入口：审阅五十条候选要按五十次 Tab。
/// </para>
/// <para>
/// <b>为什么还要一段冒烟</b>：<see cref="PrimaryActionKeysTests"/> 判的是手势与「该不该动作」的真值表
/// （Linux 上真执行）与接线形状（读源码）；但「按下去真的走了下一条」这件事只有真控件上按一次才知道——
/// <c>SendKeys</c> 经系统键入，按键先到窗体的 <c>ProcessCmdKey</c>，与 Windows 上用户按 Ctrl+Enter
/// 走的是同一条路。这一段量四件事：前提（选中、键盘、按钮可用可见）、按一次真的走掉一条、
/// 带 Shift 不动作、按钮禁用时不动作。
/// </para>
/// <para>
/// <b>这一份判什么</b>：① 冒烟段 <c>RunPrimaryActionGesture</c> 真的经 <c>PressKey</c> 按了
/// <c>^{ENTER}</c>（两次：走掉一条、按钮禁用时再按一次）与 <c>^+{ENTER}</c>（带 Shift 那一次）——
/// 不是直接调事件处理器、也不是直接调 <c>PerformClick</c>；② 结果从<b>磁盘</b>读回来比对
/// （<c>drafts.Load(task.Id)</c>），不是断言「调过写入口」；③ 状态行的期望值取产品口径
/// （<c>CandidateEdit.ReviewDoneText</c>），界面若自己拼一句就是第二份文案；④ 四件事各有一句能读的断言；
/// ⑤ 这一段在 <see cref="MeasuredScreens.All"/> 里有登记、轮次可查。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判 wine 上按下去的手感对不对（那是冒烟器那一跑的事，读数在
/// <c>.winecheck/reading.txt</c> 与 <c>docs/NOTES.md</c> 第三百五十九轮那一节）；不判中文输入法是否吞掉
/// Ctrl+Enter、不判 200% 档下按钮的观感——Windows GUI 在本机不可验证，随 G-D5 复核。
/// </para>
/// </summary>
public class PrimaryActionGestureSmokeTests
{
    private const string Segment = "RunPrimaryActionGesture";

    private static string Smoke => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>
    /// 四件事各一句能读的话：按一次真的走掉一条 / 选中交给下一条 / 带 Shift 不动作 / 按钮禁用时不动作。
    /// </summary>
    private static readonly string[] RequiredSentences =
    [
        "主动作手势没有落到那颗按钮上。",
        "「接受并下一条」承诺的就是接下来发生的事",
        "带 Shift 的组合不属本手势，不该替用户接受一条。",
        "按钮禁用时按下去什么都不该发生。",
        "这一次按键该交回基类、什么都不发生，不该替用户关掉这一屏。",
    ];

    /// <summary>
    /// 真的按过的键：Ctrl+Enter 两次（走掉一条、按钮禁用时再按一次——一次按键只有一个含义）、
    /// Ctrl+Shift+Enter 一次（带 Shift 的组合不属本手势）。
    /// </summary>
    private static readonly string[] RequiredKeyPresses =
    [
        "PressKey(dialog, list, \"^{ENTER}\");",
        "PressKey(dialog, list, \"^+{ENTER}\");",
    ];

    [Fact]
    public void TheSmokeSegment_ReallyPressesCtrlEnterAndReadsTheDiskBack()
    {
        var body = Body();

        Assert.Empty(Problems(body));

        // Ctrl+Enter 两次：走掉一条、按钮禁用时再按一次。少一次就少一件事的读数。
        var presses = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^{ENTER}\");")).Count;
        Assert.True(presses >= 2,
            $"段体里只按了 {presses} 次 Ctrl+Enter：走掉一条、按钮禁用时再按一次，两次各量一件事。");
    }

    /// <summary>
    /// 这一段在「哪些屏真的显示过」那份登记里有认领，且轮次可查——否则下一轮读登记的人
    /// 不知道这一屏是哪一轮量到的（<c>MeasuredScreensTests</c> 判的是同一件事的另一半）。
    /// </summary>
    [Fact]
    public void TheSegment_IsRegisteredAsMeasured()
    {
        var row = MeasuredScreens.All.SingleOrDefault(screen => screen.Section == Segment);
        Assert.NotNull(row);
        Assert.True(row!.Measured, "这一段真的 Show() 过 CandidatesDialog，登记里必须标「已量」。");
        Assert.Equal("第三百五十九轮", row.Round);
        Assert.Contains("CandidatesDialog", row.Classes);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各报一处，合规段体不报。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。这里喂的是<b>注入的夹具</b>，不是判据自己的定义。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        var good = Body();
        Assert.Empty(Problems(good));

        // ① 把「按钮禁用时再按一次」那一下删掉：那一态没有读数了。
        var noDisabled = good.Replace(
            "PressKey(dialog, list, \"^{ENTER}\");\n                Application.DoEvents();\n\n                if (!dialog.Visible)",
            "Application.DoEvents();\n\n                if (!dialog.Visible)",
            StringComparison.Ordinal);
        Assert.Contains(Problems(noDisabled), p => p.Contains("^{ENTER}", StringComparison.Ordinal));

        // ② 把带 Shift 那一下删掉：「带 Shift 不动作」这一条量不到。
        var noShift = good.Replace("PressKey(dialog, list, \"^+{ENTER}\");", "", StringComparison.Ordinal);
        Assert.Contains(Problems(noShift), p => p.Contains("^+{ENTER}", StringComparison.Ordinal));

        // ③ 状态行的期望值改成就地拼一句：第二份文案。
        var localCopy = good.Replace("CandidateEdit.ReviewDoneText(", "LocalText(", StringComparison.Ordinal);
        Assert.Contains(Problems(localCopy), p => p.Contains("CandidateEdit.ReviewDoneText", StringComparison.Ordinal));

        // ④ 不再从磁盘读回来（改成断言「调过写入口」）：走掉一条这件事没有读数了。
        var noDisk = good.Replace("drafts.Load(task.Id)", "path", StringComparison.Ordinal);
        Assert.Contains(Problems(noDisk), p => p.Contains("drafts.Load(task.Id)", StringComparison.Ordinal));
    }

    // ---------- 判据本体（纯函数：段体进、问题出） ----------

    /// <summary>
    /// 段体进、问题出（空 = 过得去）。写成纯函数是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>MeasuredScreensTests.Problems</c> / <c>ListCopyGestureSyncSelectSmokeTests.Problems</c> 同一形状。
    /// </summary>
    private static List<string> Problems(string body)
    {
        var problems = new List<string>();

        foreach (var press in RequiredKeyPresses)
        {
            if (!body.Contains(press, StringComparison.Ordinal))
            {
                problems.Add($"段体里没有 `{press}`：这个手势一次都没被真的按过，"
                             + "「按下去落在哪」这件事仍然只有源码形状在管。");
            }
        }

        // Ctrl+Enter 必须真的按过两次（走掉一条、按钮禁用时再按一次）：少一次就少一件事的读数。
        // 只判「这个字符串在不在」不够——段体里本来就有两处，删掉其中一处照样「在」，
        // 而「按钮禁用时什么都不发生」那件事就没有读数了。
        var presses = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^{ENTER}\");")).Count;
        if (presses < 2)
        {
            problems.Add($"段体里只按了 {presses} 次 `PressKey(dialog, list, \"^{{ENTER}}\");`：走掉一条、"
                         + "按钮禁用时再按一次，两次各量一件事——少一次那件事就没有读数。");
        }

        foreach (var sentence in RequiredSentences)
        {
            if (!body.Contains(sentence, StringComparison.Ordinal))
            {
                problems.Add($"段体里读不到「{sentence}」这一句：那件事没有读数，改坏了它这一段照样绿。");
            }
        }

        // 结果必须从磁盘读回来：断言「调过写入口」量不到那一条到底有没有被接受。
        if (!body.Contains("drafts.Load(task.Id)", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `drafts.Load(task.Id)`：审阅状态必须从磁盘读回来比对，"
                         + "只断言「调过写入口」量不到那一条到底有没有被接受。");
        }

        // 状态行的期望值取产品口径：界面若自己拼一句就是第二份文案。
        if (!body.Contains("CandidateEdit.ReviewDoneText(", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `CandidateEdit.ReviewDoneText(`：状态行的期望值必须取产品口径，"
                         + "就地拼一句就是第二份文案。");
        }

        return problems;
    }

    /// <summary>
    /// 取 <c>RunPrimaryActionGesture</c> 的方法体：从签名那一行到下一个方法注释之前。
    /// 与 <c>ListCopyGestureSyncSelectSmokeTests.Body</c> 同一取法（按大括号配平，不按缩进猜）。
    /// </summary>
    private static string Body()
    {
        const string signature = "private static void RunPrimaryActionGesture(IServiceProvider services, string dataRoot)";
        var start = Smoke.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"冒烟器里找不到 `{signature}`：这一段被改名或删掉了，本文件的口径需要重定。");

        var open = Smoke.IndexOf('{', start);
        Assert.True(open > start, "签名后面找不到方法体的左大括号。");

        var depth = 0;
        for (var i = open; i < Smoke.Length; i++)
        {
            if (Smoke[i] == '{') depth++;
            else if (Smoke[i] == '}')
            {
                depth--;
                if (depth == 0) return Smoke[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("方法体的大括号没有配平。");
    }
}
