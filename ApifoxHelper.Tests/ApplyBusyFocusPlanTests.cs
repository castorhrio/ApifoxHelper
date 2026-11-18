using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写那一屏（<c>ApplyPlanDialog</c>）「忙起来那一刻键盘落在哪」（第三百九十八轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅（第三百七十六轮）、任务页作业区（第三百九十二轮）、连接与写入探测
/// （第三百九十四轮）、同步预览（第三百九十六轮）四处都接了「接不住键盘了就把键盘交给还在屏上的那一颗」。
/// 这一屏是<b>同一形状的第五处</b>，而它此前一处都没有：点下「确认回写」之后 <c>SetBusy(true)</c> 把刚按过的
/// 那一颗<b>停用</b>，停用的控件接不住键盘，框架当场把焦点挪走。
/// </para>
/// <para>
/// <b>挪到哪去了是量出来的</b>：本轮先写冒烟段、后改产品，同一版源码在 wine 96 档当场读到
/// 「点下『确认回写』之后键盘没有落在文件清单上（焦点在 「关闭」）」——框架把焦点交给了那颗「关闭」，
/// 而它同时是这一屏的 <c>AcceptButton</c> 与 <c>CancelButton</c>：回写正在跑的时候一次回车就把窗口关掉。
/// 这一屏的「确认回写」是不可逆写入，而这一屏没有「停止」（<c>CommentPlanService</c> 不接受取消），
/// 所以落点是<b>文件清单</b>——回写结果逐行写进「回写结果」那一列。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="ApplyBusyFocusPlan.OnBusy"/> 的八种组合逐格对账；
/// ② 算式只有一份（源码门）：这一屏调 <c>FocusHandover.HandsAcross</c>，真值表那一句只在算式那一份里；
/// ③ 顺序与唯一落点（源码门）：焦点必须在换可用性<b>之前</b>记下来，交键盘排在最后，全文件只有一处 <c>.Focus()</c>；
/// ④ 这一屏只有一支判定（源码门）：没有「忙完」那一支，理由写在 <see cref="ApplyBusyFocusPlan"/> 的类注释里；
/// ⑤ 冒烟段 <c>RunApplyBusyFocusHandover</c> 里真的量了这件事。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「回写该不该起」「那几颗钮该不该停用」（那是这一屏自己的口径）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）。
/// </para>
/// </summary>
public class ApplyBusyFocusPlanTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "ApplyPlanDialog.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string SetBusyHeader = "private void SetBusy(bool busy)";

    /// <summary>
    /// ① 判定本身：八种组合逐格对账（真执行 <see cref="ApplyBusyFocusPlan"/>，不是抄一份真值表）。
    /// </summary>
    [Fact]
    public void OnBusy_HandsTheKeyboardToTheList_OnlyWhenThePressedButtonCannotTakeIt()
    {
        // 按下去的那一颗还接得住键盘：什么都不做（它自己还拿着键盘）。
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(true, true, true));
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(true, true, false));

        // 接不住了而文件清单在屏上：交给清单。
        Assert.Equal(ApplyBusyFocusPlan.List, ApplyBusyFocusPlan.OnBusy(true, false, true));

        // 接不住了而清单也不在屏上（整屏停在提示态）：不猜。
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(true, false, false));

        // 原本就没有焦点：无论清单在不在屏上都不动键盘（这一屏不该替用户挑落点）。
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(false, true, true));
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(false, false, true));
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(false, true, false));
        Assert.Equal(ApplyBusyFocusPlan.None, ApplyBusyFocusPlan.OnBusy(false, false, false));
    }

    /// <summary>
    /// ② 算式只有一份：这一屏调 <c>FocusHandover.HandsAcross</c>，真值表那一句只在算式那一份里。
    /// </summary>
    [Fact]
    public void OneTruthTable_ServesThisRowToo()
    {
        var plan = Repo.Source("Desktop", "Native", "ApplyBusyFocusPlan.cs");
        var core = Repo.Source("Desktop", "Native", "FocusHandover.cs");

        Assert.Contains("FocusHandover.HandsAcross(pressedHadFocus, pressedReachable, listReachable)", plan);

        const string table = "=> hadFocus && !stillReachable && targetReachable;";
        Assert.Contains(table, core);
        Assert.DoesNotContain(table, plan);

        // 算式本身不许再长出第二份调用点（它是纯判定，不碰控件）。
        Assert.Equal(0, Count(core, "FocusHandover.HandsAcross("));
    }

    /// <summary>③ 顺序与唯一落点、④ 只有一支判定（源码门）。</summary>
    [Fact]
    public void SetBusy_RecordsFocusBeforeToggling_AndHandsTheKeyboardOnlyByThePlan()
    {
        var problems = Problems(Dialog, Smoke);
        Assert.True(problems.Count == 0, string.Join("；", problems));
    }

    /// <summary>把两份源码喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。</summary>
    private static List<string> Problems(string dialog, string smoke)
    {
        var problems = new List<string>();
        var code = CodeScan.Text(dialog);

        var body = Body(code, SetBusyHeader);
        if (body is null)
        {
            problems.Add("ApplyPlanDialog 里找不到 SetBusy 的方法体：本门按它判「先记焦点、再换可用性、最后交键盘」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            var recorded = body.IndexOf("var pressed = FocusedActionButton();", StringComparison.Ordinal);
            var toggled = body.IndexOf("_applyButton.Enabled = !busy", StringComparison.Ordinal);
            var handed = body.IndexOf("ApplyBusyFocusPlan.OnBusy(", StringComparison.Ordinal);

            if (recorded < 0)
            {
                problems.Add("SetBusy 里没有在换可用性之前记下焦点（应为 var pressed = FocusedActionButton();）："
                    + "不记就永远答「不用交」，被停用的那一颗拿着键盘时没人接。");
            }
            else if (toggled < 0)
            {
                problems.Add("SetBusy 里找不到换可用性那一行（_applyButton.Enabled = !busy …）："
                    + "本门按「记焦点在换可用性之前」判顺序，那一行没了这一判就没有对象。");
            }
            else if (recorded > toggled)
            {
                problems.Add("SetBusy 里「记焦点」排在换可用性之后：Enabled = false 一落下去框架当场就把焦点挪走了，"
                    + "之后再读 Focused 读到的是「已经不在它身上」，那一判永远答「不用交」。");
            }

            if (handed < 0)
            {
                problems.Add("SetBusy 里没有按判定交键盘（应为 ApplyBusyFocusPlan.OnBusy(...)）："
                    + "无条件交给清单会让「焦点原本在哪」这件事不再被读。");
            }
            else if (handed < toggled)
            {
                problems.Add("SetBusy 里「交键盘」排在换可用性之前：可用性还没落定就把键盘交出去，"
                    + "交给一颗停用的控件是空操作，键盘照样丢在「关闭」上。");
            }

            if (!body.Contains("ApplyBusyFocusPlan.OnBusy(pressed is not null, pressed?.Enabled == true, _list.Visible && _list.Enabled)",
                    StringComparison.Ordinal))
            {
                problems.Add("SetBusy 里没有把「换可用性之前记下的焦点」「换完之后读到的可用性」「清单此刻在不在屏上」"
                    + "三半一起喂给 ApplyBusyFocusPlan.OnBusy：判定要的是这三半，少一半就答不出该交给谁。");
            }
        }

        // ③ 唯一落点：全文件只有一处自己写 .Focus()，且就是 SetBusy 里那一句交给文件清单的。
        var allFocus = Count(code, ".Focus();");
        var insideFocus = body is null ? 0 : Count(body, ".Focus();");
        if (allFocus != 1 || insideFocus != 1)
        {
            problems.Add($"ApplyPlanDialog 里 .Focus() 出现 {allFocus} 处（其中在 SetBusy 里 {insideFocus} 处，应为 1 / 1）："
                + "这一屏的键盘落点只该有 SetBusy 那一处，第二处就是第二个说了算的人"
                + "（计划到达那一处仍经 Ui.KeepKeyboardOnQueue 同一入口，不自己写 .Focus()）。");
        }
        if (body is null || !body.Contains("_list.Focus();", StringComparison.Ordinal))
        {
            problems.Add("SetBusy 里没有 _list.Focus();：忙起来那一支要把键盘交给文件清单——"
                + "少了这一句，键盘就停在框架挑的那颗「关闭」上（它同时是 AcceptButton 与 CancelButton）。");
        }

        // ④ 这一屏只有一支判定：没有「忙完」那一支（忙完时键盘已经在清单上，再交一次就是抢走用户正在看的那一行）。
        var plan = Repo.Source("Desktop", "Native", "ApplyBusyFocusPlan.cs");
        if (plan.Contains("OnIdle", StringComparison.Ordinal))
        {
            problems.Add("ApplyBusyFocusPlan 里出现了 OnIdle：这一屏忙完时键盘已经在文件清单上，"
                + "再交一次就是把他正在看的那一行抢走——要加这一支，先把类注释里那条理由改掉。");
        }
        var busyCalls = Count(code, "ApplyBusyFocusPlan.OnBusy(");
        if (busyCalls != 1)
        {
            problems.Add($"产品源码里调用 ApplyBusyFocusPlan.OnBusy 出现 {busyCalls} 处（应为 1，在 SetBusy 里）："
                + "0 处 = 判定没人用；多于 1 处 = 有第二处也在决定键盘落点。");
        }

        // ⑤ 冒烟段真的量了这件事（两支都要在段体里）。
        var smokeBody = Body(CodeScan.Text(smoke), "private static void RunApplyBusyFocusHandover(");
        if (smokeBody is null)
        {
            problems.Add("冒烟器里找不到 RunApplyBusyFocusHandover 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("apply.Focus();", "把键盘真的放到那颗「确认回写」上"),
                         ("apply.PerformClick();", "真的点一次那颗「确认回写」（走产品自己的 RunApply → SetBusy 那条路）"),
                         ("list.Focused", "读「忙起来那一刻键盘落到了文件清单上」与「回写结束之后键盘仍留在清单上」"),
                         ("PumpUntil(shown.PendingApply)", "等这一次回写真的跑完，才量得到「忙完不抢键盘」那一支"),
                     })
            {
                if (!smokeBody.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunApplyBusyFocusHandover 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「忙起来时键盘交给文件清单」与「回写结束之后键盘留在清单上」两支，"
                        + "少了这一句就量不到那一支。");
                }
            }
        }

        return problems;
    }

    /// <summary>从去注释后的源码里取一个方法体（从签名那一行到与之配对的右花括号）。</summary>
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
