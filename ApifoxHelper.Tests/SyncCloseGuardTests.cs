using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览那一屏（<c>SyncPreviewDialog</c>）「同步正在跑的时候关窗该不该拦下」
/// （第四百零三轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：那颗「停止」是这一屏<b>唯一的停止入口</b>，而这一屏的「确认同步」是
/// <b>不可逆写入</b>。此前 Esc 走 <c>CancelButton</c>（那颗「关闭」）、标题栏 × 走同一条路——
/// 同步正在跑的时候按一下 Esc，窗口关掉、那颗「停止」跟着消失，而作业在服务层继续跑
/// （<c>JobRunner</c> 不因窗口关闭而取消）。用户以为「我取消了」，实际是「我把唯一的停止入口关掉了」。
/// 探测那一屏（第四百零二轮）是同一形状的第一处，这一屏是第二处。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 那一句的形状（真执行）：<see cref="CloseGuardPlan.SyncBusyMessage"/> 是全角标点、说得出下一步
///    （「停止」）、说得出后果（关掉窗口就没有停止入口）、不是一句没有落点的「请稍候」，
///    且<b>指得对屏上那件事</b>（说的是「同步」，不是照抄探测那一屏的「写入兼容探测」）；
/// ② 接线（源码门）：判定只接在 <c>OnFormClosing</c> 一处、拦下时 <c>e.Cancel = true</c> 且<b>不调</b>
///    <c>base.OnFormClosing</c>、不拦时照常调；「忙」问的是<b>这一屏自己</b>（<c>_busy</c>）而不是任务作用域；
///    Esc / × / <c>CancelButton</c> 三条路都汇到 <c>FormClosing</c>（<c>CancelButton</c> 那颗钮点下去走 <c>Close()</c>）；
///    那一句的字面只在 <see cref="CloseGuardPlan"/> 里出现一份（界面读常量、不抄字面）；
///    忙态那一句在 500ms 轮询里不被抹掉（<c>OnTick</c> 的忙态分支读 <c>_closeRefusedWhileBusy</c>），
///    作业一结束就收掉（<c>SetBusy(false)</c> 清那一笔）；
/// ③ 冒烟段 <c>RunSyncCloseGuard</c> 里真的量了这件事（真的点一次「生成同步预览」把这一屏推进忙态 →
///    真的关一次窗 → 读「窗口没关」与「状态行说了那一句」→ 作业结束 → 读「关窗照常」），
///    且忙态那一支<b>不泵消息</b>（泵一次就可能让 500ms 轮询把那一行换回去，量到的就不是「点下去那一刻」的屏）；
/// ④ 判据自己被抓得住（<see cref="Gate_ReportsTheBreaksItMustCatch"/>，八发注入各报一处、合规写法不报）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「同步计划生成得对不对」（那要真实 Apifox，冒烟段只借这条路径把这一屏推进忙态）；
/// 不判真机上按 Esc 的手感（G-D5，真机 0 台）；也不判判定算式本身——算式只有一份
/// （<see cref="CloseGuardPlan.RefuseWhileBusy"/>），四种组合逐格对账在 <c>CloseGuardPlanTests</c> 里，
/// 本文件不抄第二份真值表。
/// </para>
/// </summary>
public class SyncCloseGuardTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string OnFormClosingHeader = "protected override void OnFormClosing(FormClosingEventArgs e)";

    /// <summary>
    /// ① 那一句的形状：全角标点、说得出下一步（「停止」）、说得出后果、指得对屏上那件事。
    /// <para>
    /// 这一句是「拦下之后唯一的解释」——它必须让用户知道下一步按哪一颗，而不是让他等。
    /// 与 <c>docs/DESIGN.md</c> §7「禁用不得移除下一步」同一口径。
    /// </para>
    /// </summary>
    [Fact]
    public void SyncBusyMessage_SaysWhatToPressNext_AndNamesThisScreen()
    {
        var message = CloseGuardPlan.SyncBusyMessage;

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

        // 指得对屏上那件事：这一屏跑的是同步，说「同步」才指得对；照抄探测那一屏的词汇就指错了事。
        Assert.Contains("同步", message, StringComparison.Ordinal);
        Assert.DoesNotContain("探测", message, StringComparison.Ordinal);
        Assert.NotEqual(CloseGuardPlan.ProbeBusyMessage, message);
    }

    /// <summary>
    /// ② 接线与 ③ 冒烟段（源码门）：判定只接在 <c>OnFormClosing</c> 一处、拦下时不调 <c>base</c>、
    /// 「忙」问的是这一屏自己、三条关窗路都汇到 <c>FormClosing</c>、那一句的字面只有一份、
    /// 忙态那一句不被轮询抹掉且作业结束就收掉、冒烟段真的量了两支。
    /// </summary>
    [Fact]
    public void OnFormClosing_RefusesOnlyWhileBusy_AndTheSmokeMeasuresBothBranches()
    {
        var problems = Problems(Dialog, Smoke);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>
    /// ④ 判据自己被抓得住：把每一种该报的破坏各注入一次，判据必须报出来；合规写法不报。
    /// <para>
    /// 与 <c>CloseGuardPlanTests</c> / <c>ProbeFocusPlanTests</c> 同一形状：判据写成「纯文本进、问题出」，
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
            "            _statusHost.SetText(CloseGuardPlan.SyncBusyMessage, UiTokens.WarnText);\n            return;",
            "            _statusHost.SetText(CloseGuardPlan.SyncBusyMessage, UiTokens.WarnText);\n            base.OnFormClosing(e);\n            return;",
            StringComparison.Ordinal);
        Assert.Contains("base.OnFormClosing", string.Join("；", Problems(callsBase, smoke)), StringComparison.Ordinal);

        // ④ 判定不看「是不是用户按出来的」：系统关机也会被拦下。
        var ignoresReason = dialog.Replace(
            "CloseGuardPlan.RefuseWhileBusy(_busy, e.CloseReason == CloseReason.UserClosing)",
            "CloseGuardPlan.RefuseWhileBusy(_busy, true)",
            StringComparison.Ordinal);
        Assert.Contains("CloseReason", string.Join("；", Problems(ignoresReason, smoke)), StringComparison.Ordinal);

        // ⑤ 「忙」改问任务作用域：同一个任务里别的屏起的作业会把这一屏拦下，那一句就指错了事。
        var asksScope = dialog.Replace(
            "CloseGuardPlan.RefuseWhileBusy(_busy, e.CloseReason == CloseReason.UserClosing)",
            "CloseGuardPlan.RefuseWhileBusy(_jobs.Running(_taskId) is not null, e.CloseReason == CloseReason.UserClosing)",
            StringComparison.Ordinal);
        Assert.Contains("_busy", string.Join("；", Problems(asksScope, smoke)), StringComparison.Ordinal);

        // ⑥ 界面自己抄了一份那句话的字面（常量不再是唯一落点）。
        var copiesLiteral = dialog.Replace(
            "_statusHost.SetText(CloseGuardPlan.SyncBusyMessage, UiTokens.WarnText);",
            "_statusHost.SetText(\"同步正在执行，先按「停止」再关闭窗口；关掉窗口就没有停止入口了。\", UiTokens.WarnText);",
            StringComparison.Ordinal);
        Assert.Contains("字面", string.Join("；", Problems(copiesLiteral, smoke)), StringComparison.Ordinal);

        // ⑦ 忙态分支不再读 _closeRefusedWhileBusy：那一句被 500ms 轮询抹掉，只闪半秒。
        var wiped = dialog.Replace(
            "            if (_closeRefusedWhileBusy) _statusHost.SetText(CloseGuardPlan.SyncBusyMessage, UiTokens.WarnText);\n            else ShowRunningStatus(job);",
            "            ShowRunningStatus(job);",
            StringComparison.Ordinal);
        Assert.Contains("_closeRefusedWhileBusy", string.Join("；", Problems(wiped, smoke)), StringComparison.Ordinal);

        // ⑧ 作业结束之后不清那一笔：那一句会一直挂在状态行上，作业早就不在跑了。
        var neverCleared = dialog.Replace("        _closeRefusedWhileBusy = false;\n", "", StringComparison.Ordinal);
        Assert.Contains("SetBusy", string.Join("；", Problems(neverCleared, smoke)), StringComparison.Ordinal);

        // ⑨ 冒烟段不再真的关一次窗（只读代码形状，量不到「窗口没关」）。
        var noClose = smoke.Replace("                dialog.Close();\n", "", StringComparison.Ordinal);
        Assert.Contains("dialog.Close()", string.Join("；", Problems(dialog, noClose)), StringComparison.Ordinal);

        // ⑩ 冒烟段在忙态那一支泵了消息：500ms 轮询会把那一行换回去，量到的不是「点下去那一刻」的屏。
        var pumps = smoke.Replace(
            "                generate.PerformClick();\n",
            "                generate.PerformClick();\n                Application.DoEvents();\n",
            StringComparison.Ordinal);
        Assert.Contains("DoEvents", string.Join("；", Problems(dialog, pumps)), StringComparison.Ordinal);
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
            problems.Add("SyncPreviewDialog 里找不到 OnFormClosing 的方法体：同步正在跑的时候关窗没人拦，"
                + "那颗「停止」会跟着窗口一起消失，而作业在服务层继续跑——这一屏的「确认同步」还是不可逆写入。");
        }
        else
        {
            // 判定只在这一处接一次，两个入参都要在；「忙」问的是这一屏自己（_busy），不是任务作用域。
            if (!closing.Contains("CloseGuardPlan.RefuseWhileBusy(_busy, "
                    + "e.CloseReason == CloseReason.UserClosing)", StringComparison.Ordinal))
            {
                problems.Add("OnFormClosing 里没有把「这一屏自己在不在忙」（_busy）与「这一次关窗是不是用户按出来的」"
                    + "一起喂给 CloseGuardPlan.RefuseWhileBusy：少一半就答不出该不该拦——"
                    + "不看 CloseReason 会把系统关机也拦下（拦下来只会让系统关不掉一个进程）；"
                    + "拿任务作用域去问会把「别的屏在跑」读成「这一屏在跑」，拦下时那一句就指错了事。");
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
            if (!closing.Contains("CloseGuardPlan.SyncBusyMessage", StringComparison.Ordinal))
            {
                problems.Add("OnFormClosing 拦下之后没有写 CloseGuardPlan.SyncBusyMessage："
                    + "拦下不说一句为什么，用户按 Esc 只会觉得窗口坏了。");
            }
        }

        // 那一句的字面只在 CloseGuardPlan 里出现一份（界面抄一份就是第二个说了算的人）。
        const string literal = "同步正在执行，先按「停止」再关闭窗口；关掉窗口就没有停止入口了。";
        if (dialog.Contains(literal, StringComparison.Ordinal))
        {
            problems.Add("SyncPreviewDialog 里出现了那一句的字面：文案的唯一落点是 CloseGuardPlan.SyncBusyMessage，"
                + "界面抄一份之后改一处就会两处说法不同。");
        }

        // 判定在<b>这一屏</b>的源码里恰好调用一处（同一支判定被两屏共用，所以数的是「这一份源码里几处」）。
        var calls = Count(code, "CloseGuardPlan.RefuseWhileBusy(");
        if (calls != 1)
        {
            problems.Add($"SyncPreviewDialog 里调用 CloseGuardPlan.RefuseWhileBusy 出现 {calls} 处（应为 1 处，在 OnFormClosing 里）："
                + "0 处 = 判定没人用；多于 1 处 = 这一屏有第二处也在决定关不关窗。");
        }

        // 三条关窗路都汇到 FormClosing：Esc 走 CancelButton，CancelButton 就是那颗「关闭」，它点下去走 Close()。
        if (!code.Contains("CancelButton = _closeButton;", StringComparison.Ordinal))
        {
            problems.Add("这一屏不再把 CancelButton 指到那颗「关闭」：Esc 与那颗钮就不再是同一条路，"
                + "守卫接到的关窗会少一条（Esc 会绕开 FormClosing 直接关窗）。");
        }
        if (!code.Contains("_closeButton.Click += (_, _) => Close();", StringComparison.Ordinal))
        {
            problems.Add("那颗「关闭」点下去不再走 Close()：Hide / Dispose 都跳过 FormClosing，"
                + "守卫接不到那一次关窗，同步跑着的时候点「关闭」照样把停止入口关掉。");
        }

        // 忙态那一句不被 500ms 轮询抹掉：OnTick 的忙态分支要读 _closeRefusedWhileBusy。
        var tick = Body(code, "private void OnTick()");
        if (tick is null)
        {
            problems.Add("SyncPreviewDialog 里找不到 OnTick 的方法体：本门按它判「忙态那一句不被轮询抹掉」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!tick.Contains("_closeRefusedWhileBusy", StringComparison.Ordinal))
        {
            problems.Add("OnTick 的忙态分支不读 _closeRefusedWhileBusy：这一处每 500ms 走一次，"
                + "无条件换成进度会让「先按『停止』再关闭窗口」那句话只闪半秒——用户按完 Esc 什么都没看见。");
        }

        // 作业一结束就收掉那一笔：SetBusy(false) 那一支要清 _closeRefusedWhileBusy。
        var setBusy = Body(code, "private void SetBusy(bool busy)");
        if (setBusy is null)
        {
            problems.Add("SyncPreviewDialog 里找不到 SetBusy 的方法体：本门按它判「作业结束就收掉那一笔」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else if (!setBusy.Contains("_closeRefusedWhileBusy = false;", StringComparison.Ordinal))
        {
            problems.Add("SetBusy 的忙完那一支不清 _closeRefusedWhileBusy：那一句只在作业跑着的时候成立，"
                + "作业一结束就该让位给结果那一句——不清它，状态行会一直挂着「先按『停止』再关闭窗口」。");
        }

        // 冒烟段真的量了两支。
        var body = Body(CodeScan.Text(smoke), "private static void RunSyncCloseGuard(");
        if (body is null)
        {
            problems.Add("冒烟器里找不到 RunSyncCloseGuard 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("generate.PerformClick();", "真的点一次「生成同步预览」把这一屏推进忙态（不是造一个假的忙态）"),
                         ("dialog.Close();", "真的关一次窗（Esc 与标题栏 × 在真机上走的就是这一条路）"),
                         ("!dialog.Visible", "读「窗口没关」"),
                         ("CloseGuardPlan.SyncBusyMessage", "读「状态行说的是那一句」"),
                         ("shown.Close();", "读「作业结束之后关窗照常」"),
                     })
            {
                if (!body.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunSyncCloseGuard 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「同步真在跑的时候关窗被拦下并说一句为什么」与「作业结束之后关窗照常」两支，"
                        + "少了这一句就量不到那一支。");
                }
            }

            // 忙态那一支不泵消息：泵一次就可能让 500ms 轮询把那一行换回去，量到的就不是「点下去那一刻」的屏。
            var clickAt = body.IndexOf("generate.PerformClick();", StringComparison.Ordinal);
            var closeAt = body.IndexOf("dialog.Close();", StringComparison.Ordinal);
            if (clickAt >= 0 && closeAt > clickAt
                && body[clickAt..closeAt].Contains("Application.DoEvents()", StringComparison.Ordinal))
            {
                problems.Add("RunSyncCloseGuard 在「点下『生成同步预览』」与「关一次窗」之间泵了消息（Application.DoEvents()）："
                    + "这一屏的 500ms 轮询会把状态行换回进度，量到的就不是「点下去那一刻」的屏，"
                    + "「状态行说的是那一句」这一支会假红或假绿。");
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
