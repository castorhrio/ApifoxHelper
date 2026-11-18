using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 连接与写入探测那一屏（<c>CapabilitiesDialog</c>）「探测正在跑的时候关窗该不该拦下」
/// （第四百零二轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这一屏的「运行写入探测」会在用户的测试接口上做<b>三类往返写入</b>，
/// 而那颗「停止」是这一屏<b>唯一的停止入口</b>。此前 Esc 在 <c>ProcessDialogKey</c> 里直接 <c>Close()</c>、
/// 标题栏 × 走同一条路——探测正在跑的时候按一下 Esc，窗口关掉、那颗「停止」跟着消失，
/// 而作业在服务层继续跑（<c>JobRunner</c> 不因窗口关闭而取消）。用户以为「我取消了」，
/// 实际是「我把唯一的停止入口关掉了」。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="CloseGuardPlan.RefuseWhileBusy"/> 的四种组合逐格对账；
/// ② 那一句的形状（真执行）：<see cref="CloseGuardPlan.ProbeBusyMessage"/> 是全角标点、说得出下一步
///    （「停止」）、不是一句没有落点的「请稍候」；
/// ③ 接线（源码门）：判定只接在 <c>OnFormClosing</c> 一处、拦下时 <c>e.Cancel = true</c> 且<b>不调</b>
///    <c>base.OnFormClosing</c>、不拦时照常调；Esc 仍走 <c>Close()</c>（不绕过守卫）；
///    那一句的字面只在 <see cref="CloseGuardPlan"/> 里出现一份（界面读常量、不抄字面）；
///    忙态那一句在 500ms 轮询里不被抹掉（<c>ShowProbeRunning</c> 的忙态分支读 <c>_closeRefusedWhileBusy</c>）；
/// ④ 冒烟段 <c>RunProbeCloseGuard</c> 里真的量了这件事（真的起一支在跑的探测作业 → 真的关一次窗 →
///    读「窗口没关」与「状态行说了那一句」→ 作业结束 → 读「关窗照常」）；
/// ⑤ 判据自己被抓得住（<see cref="Gate_ReportsTheBreaksItMustCatch"/>，注入各报一处、合规写法不报）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「探测该不该起」「那颗『停止』该不该显出来」（那是这一屏自己的口径，
/// 见 <c>RunProbe</c> 与 <c>ShowProbeRunning</c>）；不判真机上按 Esc 的手感（G-D5，真机 0 台）；
/// 也不判「同步预览那一屏该不该同样拦」——那是同一形状的第二处，本轮不接，
/// 边界写在 <see cref="CloseGuardPlan"/> 的类注释里。
/// </para>
/// </summary>
public class CloseGuardPlanTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "CapabilitiesDialog.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string OnFormClosingHeader = "protected override void OnFormClosing(FormClosingEventArgs e)";

    /// <summary>
    /// ① 判定本身：四种组合逐格对账（真执行 <see cref="CloseGuardPlan.RefuseWhileBusy"/>，不是抄一份真值表）。
    /// <para>
    /// 期望值按<b>理由</b>给：作业在跑、且这一次关窗是用户按出来的 → 拦；作业没在跑 → 不拦（拦了窗口就再也关不上）；
    /// 不是用户按出来的（系统关机 / 任务管理器 / 父窗口关闭）→ 不拦（拦下来只会让系统关不掉一个进程，
    /// 而作业照样随进程结束）。
    /// </para>
    /// </summary>
    [Fact]
    public void RefuseWhileBusy_AnswersWhetherThisCloseShouldBeStopped()
    {
        // 作业在跑 + 用户按出来的关窗（Esc / × / CancelButton）：拦。
        Assert.True(CloseGuardPlan.RefuseWhileBusy(busy: true, userInitiated: true));

        // 作业在跑，但不是用户按出来的（系统关机 / 任务管理器 / 父窗口关闭）：不拦。
        Assert.False(CloseGuardPlan.RefuseWhileBusy(busy: true, userInitiated: false));

        // 作业没在跑：用户按出来的关窗照常关（拦了窗口就再也关不上）。
        Assert.False(CloseGuardPlan.RefuseWhileBusy(busy: false, userInitiated: true));

        // 作业没在跑、也不是用户按出来的：不拦。
        Assert.False(CloseGuardPlan.RefuseWhileBusy(busy: false, userInitiated: false));
    }

    /// <summary>
    /// ② 那一句的形状：全角标点、说得出下一步（「停止」）、不是一句没有落点的「请稍候」。
    /// <para>
    /// 这一句是「拦下之后唯一的解释」——它必须让用户知道下一步按哪一颗，而不是让他等。
    /// 与 <c>docs/DESIGN.md</c> §7「禁用不得移除下一步」同一口径。
    /// </para>
    /// </summary>
    [Fact]
    public void ProbeBusyMessage_SaysWhatToPressNext()
    {
        var message = CloseGuardPlan.ProbeBusyMessage;

        Assert.False(string.IsNullOrWhiteSpace(message));
        // 说得出下一步：那颗钮就叫「停止」，句子里必须点得出这个名字。
        Assert.Contains("「停止」", message, StringComparison.Ordinal);
        // 说得出后果：关掉窗口就没有停止入口了。
        Assert.Contains("关闭窗口", message, StringComparison.Ordinal);
        // 不是一句没有落点的等待话。
        Assert.DoesNotContain("请稍候", message, StringComparison.Ordinal);
        Assert.DoesNotContain("稍候", message, StringComparison.Ordinal);
        // 全角标点（与 ErrorCopy / ServiceCopyTests 同一口径）：半角 , . ; : ! ? ( ) 一个都不许有。
        foreach (var half in new[] { ',', '.', ';', ':', '!', '?', '(', ')' })
        {
            Assert.DoesNotContain(half, message);
        }
        // 以全角句号收尾（状态行那一句是完整的一句话）。
        Assert.EndsWith("。", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ③ 接线与 ④ 冒烟段（源码门）：判定只接在 <c>OnFormClosing</c> 一处、拦下时不调 <c>base</c>、
    /// Esc 仍走 <c>Close()</c>、那一句的字面只有一份、忙态那一句不被轮询抹掉、冒烟段真的量了两支。
    /// </summary>
    [Fact]
    public void OnFormClosing_RefusesOnlyWhileBusy_AndTheSmokeMeasuresBothBranches()
    {
        var problems = Problems(Dialog, Smoke);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>
    /// ⑤ 判据自己被抓得住：把每一种该报的破坏各注入一次，判据必须报出来；合规写法不报。
    /// <para>
    /// 与 <c>ProbeFocusPlanTests</c> / <c>BannerFocusPlanTests</c> 同一形状：判据写成「纯文本进、问题出」，
    /// 自测拿<b>注入过的</b>源码喂它。判据判不到自己该抓的东西，就是一条假门。
    /// </para>
    /// </summary>
    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        var dialog = Dialog;
        var smoke = Smoke;

        // 合规写法：一处都不报。
        Assert.Empty(Problems(dialog, smoke));

        // ① 守卫整个被删掉（回到「Esc 直接关窗」那一版）。
        var noGuard = dialog.Replace(OnFormClosingHeader, "protected override void OnFormClosingRemoved(FormClosingEventArgs e)", StringComparison.Ordinal);
        Assert.Contains("OnFormClosing", string.Join("；", Problems(noGuard, smoke)), StringComparison.Ordinal);

        // ② 拦下时忘了 e.Cancel = true：窗口照样关掉，守卫等于没接。
        var noCancel = dialog.Replace("            e.Cancel = true;\n", "", StringComparison.Ordinal);
        Assert.Contains("e.Cancel", string.Join("；", Problems(noCancel, smoke)), StringComparison.Ordinal);

        // ③ 拦下之后仍调 base.OnFormClosing：拦了又放行。
        var callsBase = dialog.Replace(
            "            _statusHost.SetText(CloseGuardPlan.ProbeBusyMessage, UiTokens.WarnText);\n            return;",
            "            _statusHost.SetText(CloseGuardPlan.ProbeBusyMessage, UiTokens.WarnText);\n            base.OnFormClosing(e);\n            return;",
            StringComparison.Ordinal);
        Assert.Contains("base.OnFormClosing", string.Join("；", Problems(callsBase, smoke)), StringComparison.Ordinal);

        // ④ 判定不看「是不是用户按出来的」：系统关机也会被拦下。
        var ignoresReason = dialog.Replace(
            "CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_scope) is not null, e.CloseReason == CloseReason.UserClosing)",
            "CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_scope) is not null, true)",
            StringComparison.Ordinal);
        Assert.Contains("CloseReason", string.Join("；", Problems(ignoresReason, smoke)), StringComparison.Ordinal);

        // ⑤ 界面自己抄了一份那句话的字面（常量不再是唯一落点）。
        var copiesLiteral = dialog.Replace(
            "_statusHost.SetText(CloseGuardPlan.ProbeBusyMessage, UiTokens.WarnText);",
            "_statusHost.SetText(\"写入兼容探测正在进行，先按「停止」再关闭窗口；关掉窗口就没有停止入口了。\", UiTokens.WarnText);",
            StringComparison.Ordinal);
        Assert.Contains("字面", string.Join("；", Problems(copiesLiteral, smoke)), StringComparison.Ordinal);

        // ⑥ 忙态分支不再读 _closeRefusedWhileBusy：那一句被 500ms 轮询抹掉，只闪半秒。
        var wiped = dialog.Replace(
            "_closeRefusedWhileBusy ? CloseGuardPlan.ProbeBusyMessage : \"\"",
            "\"\"",
            StringComparison.Ordinal);
        Assert.Contains("_closeRefusedWhileBusy", string.Join("；", Problems(wiped, smoke)), StringComparison.Ordinal);

        // ⑦ 冒烟段不再真的关一次窗（只读代码形状，量不到「窗口没关」）。
        var noClose = smoke.Replace("                    dialog.Close();\n", "", StringComparison.Ordinal);
        Assert.Contains("dialog.Close()", string.Join("；", Problems(dialog, noClose)), StringComparison.Ordinal);
    }

    /// <summary>
    /// 把两份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// </summary>
    private static List<string> Problems(string dialog, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(dialog);

        var closing = Body(code, OnFormClosingHeader);
        if (closing is null)
        {
            problems.Add("CapabilitiesDialog 里找不到 OnFormClosing 的方法体：探测正在跑的时候关窗没人拦，"
                + "那颗「停止」会跟着窗口一起消失，而作业在服务层继续跑。");
        }
        else
        {
            // 判定只在这一处接一次，两个入参都要在。
            if (!closing.Contains("CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_scope) is not null, "
                    + "e.CloseReason == CloseReason.UserClosing)", StringComparison.Ordinal))
            {
                problems.Add("OnFormClosing 里没有把「作业在不在跑」与「这一次关窗是不是用户按出来的」一起喂给 "
                    + "CloseGuardPlan.RefuseWhileBusy：少一半就答不出该不该拦——"
                    + "不看 CloseReason 会把系统关机也拦下（拦下来只会让系统关不掉一个进程）。");
            }

            // 拦下时：e.Cancel = true，且不调 base（调了就是拦了又放行）。
            var cancelAt = closing.IndexOf("e.Cancel = true;", StringComparison.Ordinal);
            var baseAt = closing.IndexOf("base.OnFormClosing(e);", StringComparison.Ordinal);
            var returnAt = closing.IndexOf("return;", StringComparison.Ordinal);
            if (cancelAt < 0)
            {
                problems.Add("OnFormClosing 的拦下分支里没有 e.Cancel = true;：不取消这一次关窗，守卫等于没接。");
            }
            else if (baseAt >= 0 && baseAt < cancelAt)
            {
                problems.Add("OnFormClosing 里 base.OnFormClosing(e); 排在 e.Cancel = true; 之前："
                    + "拦下之前就把关窗流程走完了，取消已经没有意义。");
            }
            else if (baseAt >= 0 && returnAt >= 0 && baseAt < returnAt)
            {
                problems.Add("OnFormClosing 的拦下分支里仍调了 base.OnFormClosing(e);：拦了又放行，窗口照样关掉。");
            }

            // 那一句由常量一处给，界面不抄字面。
            if (!closing.Contains("CloseGuardPlan.ProbeBusyMessage", StringComparison.Ordinal))
            {
                problems.Add("OnFormClosing 拦下之后没有写 CloseGuardPlan.ProbeBusyMessage："
                    + "拦下不说一句为什么，用户按 Esc 只会觉得窗口坏了。");
            }
        }

        // 那一句的字面只在 CloseGuardPlan 里出现一份（界面抄一份就是第二个说了算的人）。
        const string literal = "写入兼容探测正在进行，先按「停止」再关闭窗口；关掉窗口就没有停止入口了。";
        if (dialog.Contains(literal, StringComparison.Ordinal))
        {
            problems.Add("CapabilitiesDialog 里出现了那一句的字面：文案的唯一落点是 CloseGuardPlan.ProbeBusyMessage，"
                + "界面抄一份之后改一处就会两处说法不同。");
        }

        // 判定在产品源码里恰好调用一处。
        var calls = Count(code, "CloseGuardPlan.RefuseWhileBusy(");
        if (calls != 1)
        {
            problems.Add($"产品源码里调用 CloseGuardPlan.RefuseWhileBusy 出现 {calls} 处（应为 1 处，在 OnFormClosing 里）："
                + "0 处 = 判定没人用；多于 1 处 = 有第二处也在决定关不关窗。");
        }

        // Esc 仍走 Close()：绕开 Close() 的写法（Hide / Dispose）会跳过 FormClosing，守卫就接不到。
        var dialogKey = Body(code, "protected override bool ProcessDialogKey(Keys keyData)");
        if (dialogKey is null)
        {
            problems.Add("CapabilitiesDialog 里找不到 ProcessDialogKey 的方法体：本门按它判「Esc 仍走 Close()」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!dialogKey.Contains("Close();", StringComparison.Ordinal))
        {
            problems.Add("ProcessDialogKey 里 Esc 不再走 Close()：Hide / Dispose 都跳过 FormClosing，"
                + "守卫接不到那一次关窗，探测跑着的时候按 Esc 照样把停止入口关掉。");
        }

        // 忙态那一句不被 500ms 轮询抹掉：ShowProbeRunning 的忙态分支要读 _closeRefusedWhileBusy。
        var show = Body(code, "private void ShowProbeRunning(JobInfo? job)");
        if (show is null)
        {
            problems.Add("CapabilitiesDialog 里找不到 ShowProbeRunning 的方法体：本门按它判「忙态那一句不被轮询抹掉」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!show.Contains("_closeRefusedWhileBusy ? CloseGuardPlan.ProbeBusyMessage : \"\"", StringComparison.Ordinal))
        {
            problems.Add("ShowProbeRunning 的忙态分支不读 _closeRefusedWhileBusy：这一处每 500ms 走一次，"
                + "无条件清空状态行会让「先按『停止』再关闭窗口」那句话只闪半秒——用户按完 Esc 什么都没看见。");
        }

        // 冒烟段真的量了两支。
        var body = Body(CodeScan.Text(smoke), "private static void RunProbeCloseGuard(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunProbeCloseGuard 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("jobs.Start(scope, \"写入兼容探测\"", "真的起一支在跑的探测作业（不是造一个假的运行态）"),
                         ("dialog.Close();", "真的关一次窗（Esc 与标题栏 × 在真机上走的就是这一条路）"),
                         ("!dialog.Visible", "读「窗口没关」"),
                         ("CloseGuardPlan.ProbeBusyMessage", "读「状态行说的是那一句」"),
                         ("shown.Close();", "读「探测结束之后关窗照常」"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunProbeCloseGuard 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「探测真在跑的时候关窗被拦下并说一句为什么」与「探测结束之后关窗照常」两支，"
                        + "少了这一句就量不到那一支。");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// 从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。
    /// <para>
    /// 按花括号配对数取，不按「下一个 <c>private</c>」取：方法体里有 <c>if</c> 的花括号，
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
