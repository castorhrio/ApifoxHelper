using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释回写清单首次出现时的键盘归属（DESIGN.md §6「列表行」、§8 第九十三轮）：这一屏的清单不是打开时就有——
/// 计划在 OnLoad 之后异步生成，生成完成才出现文件清单，出现时已选中第一条、差异已显示；而键盘此前不在清单上
/// （点「关闭」之外没有别的落点，框架挑的那个控件不是清单）：方向键动的是别的控件、Ctrl+C 也复制不到那一行，
/// 「有一行被标出来」只成立了一半。另十张清单在打开时或这一页出现时都把键盘交给清单
/// （第八十八至九十二轮，见 OpenFocusTests 与 PlanOpenFocusTests），这一张没有——属同壳不同行为，
/// 按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 第九十二轮把这一处如实挂账为「数据在 OnLoad 之后异步到达，到达时用户可能已经在关键词框里打字」——
/// 那条理由在**首次到达**这一刻不成立：计划生成期间关键词框是停用的
/// （<c>ListFilter.KeywordEnabled(_plan is { Files.Count: &gt; 0 }, busy)</c>，首次生成时 <c>_plan</c> 还是 null），
/// 用户不可能正在那个框里打字。故本轮经同一处 <c>Ui.KeepKeyboardOnQueue</c> 把键盘交给文件清单，
/// 判定仍是 <see cref="ListFocus.ShouldHandToQueue"/>（选中行与焦点行是同一行才交，不猜第一行），
/// **只在计划首次到达那一次交**（<c>_plan is null</c> 判一次）：重新生成计划、回写后按磁盘计划重读、
/// 回写被拒后重读都是重建路径，那一屏的下一步是看结果或换关键词，交键盘会把用户正在改的那一格抢走
/// （与第五十八轮「保存被拒的路径不交回」、第九十二轮「重建路径不交」同一取舍）。
/// 真机上「打开注释回写、计划生成完不碰鼠标直接按 ↓ 就走到下一个文件、Ctrl+C 复制到的是那一行的路径」随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ApplyPlanOpenFocusTests
{
    private const string File = "ApplyPlanDialog.cs";

    // ---------- 真执行：计划到达之后那一行是不是键盘该去的那一行 ----------

    [Fact]
    public void PlanArrival_HandsTheKeyboardToTheRowItLandedOn()
    {
        // 计划到达时清单经 Ui.SelectItem 落第一条（Selected + Focused 一起落），随后问同一份判定：
        // 选中行与焦点行是同一行才交——这一刻正是这样，故放行。
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.True(ListFocus.ShouldHandToQueue(23, 23));
    }

    [Fact]
    public void PlanArrival_LeavesTheKeyboardAloneWhenThePlanHasNoFile()
    {
        // 计划没有可写入的文件（或筛到零行）时清单里没有选中行：交键盘等于把方向键交给一个没有落点的清单，
        // 那一屏由提示、跳过原因与计数行说话（ShowPlan 的空计划分支与 PopulateList 的筛到零行分支）。
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    [Fact]
    public void FirstArrival_IsTheOnlyMomentTheKeywordBoxCannotBeTypedIn()
    {
        // 「到达时用户可能已经在关键词框里打字」这条理由在首次到达时不成立，依据是共享口径本身：
        // 计划生成期间 busy 为真、_plan 还是 null，关键词框因此是停用的——用户不可能正在那个框里打字。
        Assert.False(ListFilter.KeywordEnabled(false, busy: true));
        Assert.False(ListFilter.KeywordEnabled(false, busy: false));
        // 有数据且不在作业中才可用（重建路径上框是活的，故那些路径不交键盘）。
        Assert.True(ListFilter.KeywordEnabled(true, busy: false));
        Assert.False(ListFilter.KeywordEnabled(true, busy: true));
    }

    // ---------- 接线：只在计划首次到达那一次交 ----------

    [Fact]
    public void PlanArrival_HandsTheKeyboardOnlyOnTheFirstArrival()
    {
        var body = ShowPlanBody();
        // 这一屏是不是刚出现：计划首次到达那一次才算（重建路径上 _plan 早已有值）。
        Assert.Single(Regex.Matches(body, @"(?m)^\s*var opening = _plan is null;$"));
        Assert.Single(Regex.Matches(body, @"(?m)^\s*if \(opening\) Ui\.KeepKeyboardOnQueue\(_list\);$"));
        var judge = body.IndexOf("var opening = _plan is null;", StringComparison.Ordinal);
        var assign = body.IndexOf("_plan = plan;", StringComparison.Ordinal);
        var hand = body.IndexOf("if (opening) Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        Assert.True(judge >= 0 && assign > judge, "应先判「这一屏是不是刚出现」，再把计划记下来（记下来之后判不出首次）。");
        Assert.True(hand > assign, "交键盘必须排在计划记下来之后：清单与差异都从这份计划来。");
    }

    [Fact]
    public void PlanArrival_HandsTheKeyboardLast()
    {
        // 交键盘放在方法末尾：清单、差异与按钮都已就位，此后没有别的赋值会再动焦点。
        var body = ShowPlanBody();
        var hand = body.IndexOf("if (opening) Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        var buttons = body.IndexOf("_regenButton.Enabled = plan.Status is", StringComparison.Ordinal);
        Assert.True(buttons >= 0 && hand > buttons, "交键盘应排在按钮态刷新之后（方法末尾），否则后面的赋值会把它盖掉。");
        Assert.True(hand > body.IndexOf("PopulateList();", StringComparison.Ordinal), "交键盘应在文件清单重建之后。");
    }

    [Fact]
    public void PlanArrival_HappensInExactlyOnePlace()
    {
        // 触发点只有一处：计划只在本方法里被记下来（LoadPlan 的首次生成、重新生成，RunApply 的回写结果，
        // RefreshPlanFromDisk 的按磁盘重读都经它），别处再写一句 `_plan = ...` 就等于多了一个不交键盘的入口。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_plan = "));
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\("));
    }

    [Fact]
    public void RebuildPaths_DoNotTakeTheKeyboard()
    {
        // 关键词框每敲一个字都重建文件清单（TextChanged → PopulateList），重新生成计划与回写后重读也走 ShowPlan——
        // 这些路径上关键词框是活的（用户可能正在里面打字），交键盘等于把下一步从他手里拿走（第五十八轮同一划界）。
        var body = Slice(
            Repo.Source("Desktop", "Native", File),
            "private void PopulateList()",
            "/// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）");
        Assert.DoesNotContain("KeepKeyboardOnQueue", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_TouchesNothingButTheKeyboard()
    {
        // 选中、滚动、重建各有自己的唯一入口：这一处只交键盘，不顺手写第二套（必然与那一套漂移）。
        var body = ShowPlanBody();
        var line = Regex.Match(body, @"(?m)^\s*if \(opening\) Ui\.KeepKeyboardOnQueue\(_list\);$").Value;
        Assert.DoesNotContain("SelectItem", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", line, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateList", line, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", line, StringComparison.Ordinal);
        // 整个对话框只经这一处入口交键盘，不自己写 .Focus()。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Empty(Regex.Matches(source, @"(?m)^\s*\w+\.Focus\(\);$"));
    }

    [Fact]
    public void Judgment_StaysInTheSharedRule()
    {
        // 判定只有一份：本对话框不自己补一套序号比较，也不自己判「选中行与焦点行是不是同一行」。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.DoesNotContain("ListFocus.ShouldHandToQueue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusedItem", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_DoesNotTakeTheKeyboardOnOpen()
    {
        // 对话框打开时清单还是空的（计划在 OnLoad 之后异步生成），故不接「打开时」那一口径（OnShown）——
        // 打开即选中一行的九张清单见 OpenFocusTests，同步预览的写入清单见 PlanOpenFocusTests。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.DoesNotContain("OnShown", source, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsThePlanArrivalFocus()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**注释回写清单首次出现时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第九十三轮）", section);
        Assert.Contains("计划首次到达", section);
        Assert.Contains("**重建路径不交**（重新生成计划、回写后按磁盘计划重读、回写被拒后重读都走 `ShowPlan`）", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 注释回写清单首次出现时的键盘归属（§6） \|", ledger);
        Assert.Contains("第九十三轮", ledger);
        Assert.Contains("KeepKeyboardOnQueue", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮没做的事不能被写成做了：补充说明仍不接（编辑位是这一屏的输入）、对话框打开时仍不接管初始焦点、
        // 也不得出现「全部对话框的键盘归属已接线」这类话。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 注释回写清单首次出现时的键盘归属（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("补充说明", row);
        Assert.Contains("打开时", row);
        Assert.DoesNotContain("全部对话框的键盘归属已接线", row, StringComparison.Ordinal);
        Assert.DoesNotContain("注释回写打开时即交键盘", row, StringComparison.Ordinal);
    }

    /// <summary>ShowPlan 的方法体（从签名到下一个方法的文档注释之前）。</summary>
    private static string ShowPlanBody() =>
        Slice(
            Repo.Source("Desktop", "Native", File),
            "private void ShowPlan(CommentPlan plan)",
            "/// 关键词框（DESIGN.md §6「列表筛选」）");

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
