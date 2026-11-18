using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览那一屏（<c>SyncPreviewDialog</c>）「作业一起来 / 作业一结束，键盘落在哪一颗」（第三百九十六轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：引导横幅那两颗（第三百七十六轮，<see cref="BannerFocusPlan"/>）、任务页作业区那两颗
/// （第三百九十二轮，<see cref="JobFocusPlan"/>）、连接与写入探测那两颗（第三百九十四轮，<see cref="ProbeFocusPlan"/>）
/// 都接了「接不住键盘了就把键盘交给还在屏上的那一颗」。这一屏是<b>同一形状的第四处</b>，而它此前一处都没有：
/// 点下「生成同步预览」或「确认同步」之后，<c>SetBusy(true)</c> 把刚按过的那一颗<b>停用</b>、把「停止」显出来——
/// 停用的控件接不住键盘，框架当场把焦点挪走（挪到父面那一行上），于是「按完『确认同步』，键盘不在任何能按的东西上」。
/// 这一屏的停止是<b>正常操作</b>（执行说明那一行自己写着「可在动作之间停止」），而确认同步是不可逆写入。
/// </para>
/// <para>
/// <b>本文件判什么</b>：
/// ① 判定本身（真执行）：<see cref="SyncBusyFocusPlan.OnBusy"/> 与 <see cref="SyncBusyFocusPlan.OnIdle"/> 的八种组合逐格对账；
/// ② 顺序（源码门）：焦点必须在换可用性与换显隐<b>之前</b>记下来，交键盘排在最后；
/// ③ 唯一落点（源码门）：那几颗动作钮的 <c>Focus()</c> 全文件只出现在 <c>SetBusy</c> 里、各一次；
/// ④ 算式只有一份（源码门）：这一屏的两支都调 <c>FocusHandover.HandsAcross</c>，真值表在产品源码里只出现一份；
/// ⑤ 冒烟段 <c>RunSyncBusyFocusHandover</c> 里真的量了这件事；
/// ⑥ 「当前是哪一页」读的是勾选页那一侧（<c>_page1.Visible</c>），不是构造起就可见的预览页。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「作业该不该起」「那几颗钮该不该停用」（那是这一屏自己的口径）；
/// 不判「真机上焦点挪得顺不顺眼」——那要 Windows 真机（G-D5，真机 0 台）。
/// </para>
/// </summary>
public class SyncBusyFocusPlanTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");

    private static string Smoke => Repo.SmokeProgram;

    private const string SetBusyHeader = "private void SetBusy(bool busy)";

    /// <summary>
    /// ① 判定本身：八种组合逐格对账（真执行 <see cref="SyncBusyFocusPlan"/>，不是抄一份真值表）。
    /// </summary>
    [Fact]
    public void OnBusy_HandsTheKeyboardToStop_OnlyWhenThePressedButtonCannotTakeIt()
    {
        // 按下去的那一颗还接得住键盘：什么都不做（它自己还拿着键盘）。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(true, true, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(true, true, false));

        // 接不住了而「停止」在屏上：交给「停止」。
        Assert.Equal(SyncBusyFocusPlan.Stop, SyncBusyFocusPlan.OnBusy(true, false, true));

        // 接不住了而「停止」也不在屏上：不猜（这一屏没有可交的落点）。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(true, false, false));

        // 原本就没有焦点：无论「停止」在不在屏上都不动键盘（这一屏不该替用户挑落点）。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(false, true, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(false, false, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(false, true, false));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnBusy(false, false, false));
    }

    /// <summary>
    /// ① 判定本身（忙完那一支）：八种组合逐格对账。
    /// </summary>
    [Fact]
    public void OnIdle_HandsTheKeyboardBackToThePrimaryAction_OnlyWhenStopIsGone()
    {
        // 「停止」还在屏上：它自己还拿着键盘，不动。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(true, true, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(true, true, false));

        // 「停止」被藏起来、主动作可用：交回主动作。
        Assert.Equal(SyncBusyFocusPlan.Primary, SyncBusyFocusPlan.OnIdle(true, false, true));

        // 「停止」被藏起来、主动作也不可用：不猜（键盘留在框架给的位置上）。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(true, false, false));

        // 原本就没有焦点：不动键盘。
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(false, true, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(false, false, true));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(false, true, false));
        Assert.Equal(SyncBusyFocusPlan.None, SyncBusyFocusPlan.OnIdle(false, false, false));
    }

    /// <summary>
    /// ④ 算式只有一份：这一屏的两支都调 <c>FocusHandover.HandsAcross</c>，真值表那一句只在算式那一份里。
    /// </summary>
    [Fact]
    public void OneTruthTable_ServesThisRowToo()
    {
        var plan = Repo.Source("Desktop", "Native", "SyncBusyFocusPlan.cs");
        var core = Repo.Source("Desktop", "Native", "FocusHandover.cs");

        Assert.Contains("FocusHandover.HandsAcross(pressedHadFocus, pressedReachable, stopReachable)", plan);
        Assert.Contains("FocusHandover.HandsAcross(stopHadFocus, stopVisible, primaryReachable)", plan);

        const string table = "=> hadFocus && !stillReachable && targetReachable;";
        Assert.Contains(table, core);
        Assert.DoesNotContain(table, plan);

        // 算式本身不许再长出第二份调用点（它是纯判定，不碰控件）。
        Assert.Equal(0, Count(core, "FocusHandover.HandsAcross("));
    }

    /// <summary>② 顺序与 ③ 唯一落点（源码门）。</summary>
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
            problems.Add("SyncPreviewDialog 里找不到 SetBusy 的方法体：本门按它判「先记焦点、再换可用性与显隐、"
                + "最后交键盘」，方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            var pressedRecorded = body.IndexOf("var pressed = FocusedActionButton();", StringComparison.Ordinal);
            var stopRecorded = body.IndexOf("var stopHadFocus = _stopButton.Focused;", StringComparison.Ordinal);
            var stopToggled = body.IndexOf("_stopButton.Visible = busy;", StringComparison.Ordinal);
            var handed = body.IndexOf("SyncBusyFocusPlan.OnBusy(", StringComparison.Ordinal);

            if (pressedRecorded < 0 || stopRecorded < 0)
            {
                problems.Add("SetBusy 里没有在换可用性与换显隐之前记下焦点（应为 var pressed = FocusedActionButton(); "
                    + "与 var stopHadFocus = _stopButton.Focused;）：不记就永远答「不用交」，"
                    + "被停用的那一颗拿着键盘时没人接。");
            }
            else if (stopToggled < 0)
            {
                problems.Add("SetBusy 里找不到换显隐那一行（_stopButton.Visible = busy;）："
                    + "本门按「记焦点在换显隐之前」判顺序，那一行没了这一判就没有对象。");
            }
            else if (pressedRecorded > stopToggled || stopRecorded > stopToggled)
            {
                problems.Add("SetBusy 里「记焦点」排在换可用性与换显隐之后：Enabled = false 一落下去框架当场就把焦点挪走了，"
                    + "之后再读 Focused 读到的是「已经不在它身上」，那一判永远答「不用交」。");
            }

            if (handed < 0)
            {
                problems.Add("SetBusy 里没有按判定交键盘（应为 SyncBusyFocusPlan.OnBusy(...) / OnIdle(...)）："
                    + "无条件交给某一颗会让「焦点原本在哪」这件事不再被读。");
            }
            else if (handed < stopToggled)
            {
                problems.Add("SetBusy 里「交键盘」排在换显隐之前：可用性还没落定就把键盘交出去，"
                    + "交给一颗停用的钮是空操作，键盘照样丢在父面那一行上。");
            }

            if (!body.Contains("SyncBusyFocusPlan.OnIdle(stopHadFocus, _stopButton.Visible, target is not null)",
                    StringComparison.Ordinal))
            {
                problems.Add("SetBusy 里没有把「忙完之前记下的焦点」与「换完之后读到的可见性」一起喂给 "
                    + "SyncBusyFocusPlan.OnIdle：判定要的是这两半，少一半就答不出该交给谁。");
            }
        }

        // ③ 唯一落点：动作钮的 .Focus() 全文件只出现在 SetBusy 里，且只有两处
        //    （忙起来交给那颗「停止」、忙完交给 BusyReturnButton 挑出来的那一颗）。
        //    这里按「全文件的 .Focus() 都在 SetBusy 里」判，不按字段名逐个判：忙完那一支交的是
        //    BusyReturnButton() 挑出来的那一颗（预览页「确认同步」/ 勾选页「生成同步预览」/ 次动作），
        //    按字段名判会把「挑落点」这件事读成「没有落点」——判的是「有没有第二处也在动键盘」，不是「写没写死字段名」。
        var allFocus = Count(code, ".Focus();");
        var insideFocus = body is null ? 0 : Count(body, ".Focus();");
        if (allFocus != 2 || insideFocus != 2)
        {
            problems.Add($"SyncPreviewDialog 里 .Focus() 出现 {allFocus} 处（其中在 SetBusy 里 {insideFocus} 处，"
                + "应为 2 / 2）：这一屏的键盘落点只该有 SetBusy 那一处（忙起来交给「停止」、忙完交回主动作），"
                + "第二处就是第二个说了算的人。");
        }
        foreach (var needle in new[] { "_stopButton.Focus();", "target!.Focus();" })
        {
            if (body is null || !body.Contains(needle, StringComparison.Ordinal))
            {
                problems.Add($"SetBusy 里没有 {needle}：忙起来那一支要把键盘交给那颗「停止」，"
                    + "忙完那一支要交给 BusyReturnButton() 挑出来的那一颗——少一句就有一支没落点。");
            }
        }

        // ⑥ 「当前是哪一页」读的是勾选页那一侧：预览页构造起就 Visible（两页互斥靠 z 序），读它恒为真。
        //    第三百九十六轮第一版正是读 _page2.Visible，勾选页上作业结束时挑的是预览页那三颗
        //    （此刻全不可交），一个落点都挑不出来——冒烟段当场量到「焦点在 ListView」。
        var returnBody = Body(code, "private Button? BusyReturnButton()");
        if (returnBody is null)
        {
            problems.Add("SyncPreviewDialog 里找不到 BusyReturnButton 的方法体：本门按它判「当前是哪一页」，"
                + "方法改名或搬走之后这一条要跟着改取法。");
        }
        else
        {
            if (!returnBody.Contains("if (_page1.Visible)", StringComparison.Ordinal))
            {
                problems.Add("BusyReturnButton 里没有按勾选页的可见性分页（应为 if (_page1.Visible)）："
                    + "预览页 _page2 构造起就 Visible（两页互斥靠 z 序），读它恒为真，"
                    + "勾选页上作业结束时挑的是预览页那三颗、一个可交的落点都挑不出来。");
            }
            if (returnBody.Contains("_page2.Visible", StringComparison.Ordinal))
            {
                problems.Add("BusyReturnButton 里读了 _page2.Visible：那一页构造起就 Visible（两页互斥靠 z 序），"
                    + "读它恒为真——第三百二十轮在 ShowPlan 里踩过同一个坑，这里不许再踩一次。");
            }
        }

        // 判定在产品源码里恰好调用两处（忙起来一支、忙完一支），都在 SetBusy 里。
        var busyCalls = Count(code, "SyncBusyFocusPlan.OnBusy(");
        var idleCalls = Count(code, "SyncBusyFocusPlan.OnIdle(");
        if (busyCalls != 1 || idleCalls != 1)
        {
            problems.Add($"产品源码里调用 SyncBusyFocusPlan.OnBusy / OnIdle 出现 {busyCalls} / {idleCalls} 处"
                + "（应为 1 / 1，都在 SetBusy 里）：0 处 = 判定没人用；多于 1 处 = 有第二处也在决定键盘落点。");
        }

        // ⑤ 冒烟段真的量了这件事（两支都要在段体里）。
        var smokeBody = Body(CodeScan.Text(smoke), "private static void RunSyncBusyFocusHandover(");
        if (smokeBody is null)
        {
            problems.Add("冒烟器里找不到 RunSyncBusyFocusHandover 的方法体：注册了却没有段体，这一段等于没跑。");
        }
        else
        {
            foreach (var (needle, why) in new[]
                     {
                         ("generate.Focus();", "把键盘真的放到那颗「生成同步预览」上"),
                         ("generate.PerformClick();", "真的点一次那颗「生成同步预览」（走产品自己的 Generate → SetBusy 那条路）"),
                         ("stop.Focused", "读「忙起来那一刻键盘落到了那颗『停止』上」"),
                         ("generate.Focused", "读「作业结束之后键盘回到那颗『生成同步预览』上」"),
                     })
            {
                if (!smokeBody.Contains(needle, StringComparison.Ordinal))
                {
                    problems.Add($"RunSyncBusyFocusHandover 的段体里没有 {needle}（{why}）："
                        + "这一段量的是「忙起来时键盘交给那颗『停止』」与「作业结束之后键盘回到主动作」两支，"
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
