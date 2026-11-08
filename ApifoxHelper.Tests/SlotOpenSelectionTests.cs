using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 注释缺口清单打开即选中第一条（DESIGN.md §6「列表行」、§8 第九十一轮）：这一屏的详情区与三个选中动作
/// （查看代码证据、手工填写说明、删除失效标签）都挂在选中上，而它打开时清单里一行都没选中——详情区是
/// 「没有选中注释位置…」那句话、三个动作全灰；另八张清单（五张只读长清单、三个审阅视图）打开时都落在第一条，
/// 同一个动作在别处有、在这一处没有，属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 现打开时经 <c>Ui.SelectFirstOnOpen</c> 唯一入口选中第一条（落点由 <see cref="ListSelection.FirstRowOnOpen"/> 判定：
/// 有行才落，一行都没有就不动作、不索引不存在的第一行），随后在 OnShown 经 <c>Ui.KeepKeyboardOnQueue</c>
/// 把键盘交给清单（第八十八、八十九、九十轮同一处入口、同一份 <see cref="ListFocus"/> 判定）。
/// 落点只在打开这一处：重建路径（切换筛选、关键词框每敲一个字）不落点——那一屏的下一步是换一个筛选或关键词，
/// 落点会把用户正在筛的那一屏选中重置掉（与第五十八轮「不抢用户正在改的那一格」同一取舍）。
/// 真机上「打开注释缺口即看到第一条的详情、三个动作可用、不碰鼠标直接按 ↓ 就走到下一条」随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </summary>
public class SlotOpenSelectionTests
{
    private const string File = "ScanResultsDialog.cs";

    // ---------- 真执行：打开这一屏时落在哪一行 ----------

    [Fact]
    public void OpenLanding_IsTheFirstRowWhenTheScanHasSlots()
    {
        // 一次扫描的全部注释位置：有行就落在第一条（位次 0），与另八张清单同一处口径。
        Assert.Equal(0, ListSelection.FirstRowOnOpen(1));
        Assert.Equal(0, ListSelection.FirstRowOnOpen(128));
    }

    [Fact]
    public void OpenLanding_HasNoSpotWhenTheScanHasNoSlots()
    {
        // 全部有效或没有扫描产物时清单是空的：不索引不存在的第一行（与第五十六轮「一行不剩则没有落点」同一取舍）。
        Assert.Null(ListSelection.FirstRowOnOpen(0));
    }

    // ---------- 接线：打开时落一次，重建路径不落 ----------

    [Fact]
    public void Open_LandsOnTheFirstRowAfterTheListIsBuilt()
    {
        var onLoad = MethodBody("OnLoad");
        // 打开时落一次，且经唯一入口（不就地索引 Items[0]、不自己写 > 0 判定）。
        Assert.Single(Regex.Matches(onLoad, @"(?m)^\s*Ui\.SelectFirstOnOpen\(_list\);$"));
        Assert.DoesNotContain("Items[0]", onLoad, StringComparison.Ordinal);
        // 落点在清单重建之后：先 ApplyFilter 把行填进去，再落点（顺序反了就是在一张空清单上落点）。
        var filter = onLoad.IndexOf("ApplyFilter();", StringComparison.Ordinal);
        var land = onLoad.IndexOf("Ui.SelectFirstOnOpen(_list);", StringComparison.Ordinal);
        Assert.True(filter >= 0 && land > filter, "应在 ApplyFilter 之后落点。");
        // 落点排在两条早退之后：读取失败与没有扫描产物时清单是空的，落点在那里没有意义（也不该先落再收）。
        var readFail = onLoad.IndexOf("读取扫描结果失败", StringComparison.Ordinal);
        var noOutput = onLoad.IndexOf("没有扫描结果", StringComparison.Ordinal);
        Assert.True(readFail >= 0 && noOutput > readFail && land > noOutput, "落点应排在两条早退之后。");
    }

    [Fact]
    public void Open_LandsOnTheSelectionOnly()
    {
        // 打开时这一处只落选中：不取键盘（那是 OnShown 的 KeepKeyboardOnQueue）、不自己滚动、不重建清单、不写数据。
        var onLoad = MethodBody("OnLoad");
        Assert.DoesNotContain("KeepKeyboardOnQueue", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", onLoad, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", onLoad, StringComparison.Ordinal);
    }

    [Fact]
    public void Rebuild_DoesNotLandAgain()
    {
        // 重建路径（切换筛选、关键词框每敲一个字）不落点：那一屏的下一步是换一个筛选或关键词，
        // 落点会把用户正在筛的那一屏选中重置掉（与第五十八轮「不抢用户正在改的那一格」同一取舍）。
        var applyFilter = MethodBody("ApplyFilter");
        Assert.DoesNotContain("Ui.SelectFirstOnOpen(", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Ui.SelectItem(", applyFilter, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", applyFilter, StringComparison.Ordinal);
        // 重建后仍问一次详情：没选中时那一屏由详情区那句话交代（第六十二轮口径）。
        Assert.Contains("ShowDetail();", applyFilter);
    }

    [Fact]
    public void Open_HandsTheKeyboardToTheListOnShown()
    {
        var onShown = MethodBody("OnShown");
        // 先 base.OnShown：窗体显示之后再交键盘（OnLoad 时窗体还没显示，Focus 的落点取决于框架激活时的选择）。
        Assert.Matches(@"(?m)^\s*base\.OnShown\(e\);$", onShown);
        Assert.Single(Regex.Matches(onShown, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(_list\);$"));
        var baseCall = onShown.IndexOf("base.OnShown(e);", StringComparison.Ordinal);
        var hand = onShown.IndexOf("Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        Assert.True(baseCall >= 0 && hand > baseCall, "应在 base.OnShown 之后交键盘。");
        // 只交键盘：选中、滚动、重建各有自己的唯一入口，这里不顺手写第二套。
        Assert.DoesNotContain("SelectItem", onShown, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", onShown, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", onShown, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFilter", onShown, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", onShown, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_LandsAndHandsTheKeyboardExactlyOnceEach()
    {
        // 整份源码各恰一处：落点与交键盘都不重复（重复落点会把用户刚移动的选中拽回去）。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, @"Ui\.SelectFirstOnOpen\(_list\);"));
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_list\);").Cast<Match>());
    }

    [Fact]
    public void StateTable_RecordsTheOpenLanding()
    {
        // §7 每屏必交状态表：打开即选中第一条是这一屏的一个态，写进类注释的状态表里。
        var summary = Slice("/// 注释缺口清单：", "internal sealed class ScanResultsDialog");
        Assert.Contains("打开即选中第一条", summary, StringComparison.Ordinal);
        Assert.Contains("第九十一轮", summary, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheSlotOpenLanding()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**注释缺口清单打开即选中第一条**（`ListSelection` 同一口径，2026-11-08 第九十一轮）", section);
        Assert.Contains("经 `Ui.SelectFirstOnOpen` 唯一入口选中第一条", section);
        Assert.Contains("**落点只在打开这一处**", section);

        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 注释缺口清单打开即选中第一条（§6） \|", ledger);
        Assert.Contains("第九十一轮", ledger);
        Assert.Contains("`Ui.SelectFirstOnOpen`", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只改落点与键盘归属：不改选中落点数学、不改筛选与排序口径、不写任务与配置数据、服务层一行未动。
        var ledger = Repo.DesignSection(8);
        var row = DesignSlice(ledger, "| 注释缺口清单打开即选中第一条（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("不改选中落点数学", row);
        Assert.Contains("不改筛选与排序口径", row);
        Assert.Contains("不写任务与配置数据", row);
        Assert.Contains("服务层一行未动", row);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_PointsThePriorDeferralAtThisRound()
    {
        // 第八十八轮那条台账如实写着「注释缺口（本视图不自动选中任何一行）不接此口径」——
        // 本轮补上落点后一并接上，旧账不得被改写成「当时就接了」，须指向本轮。
        var ledger = Repo.DesignSection(8);
        var row = DesignSlice(ledger, "| 只读清单打开时的键盘归属（§6） |", "\n| 审阅清单打开时的键盘归属（§6） |");
        Assert.Contains("注释缺口（本视图不自动选中任何一行）不接此口径", row);
        Assert.Contains("第九十一轮", row);
    }

    /// <summary>某个方法的方法体（按花括号配对取，方法体里可以有 try/catch 的嵌套花括号）。</summary>
    private static string MethodBody(string method)
    {
        var source = Repo.Source("Desktop", "Native", File);
        var match = Regex.Match(source, @"(?:protected override void|private void) " + Regex.Escape(method) + @"\([^)]*\)\s*\{");
        Assert.True(match.Success, $"{File}:缺少 {method}。");
        var start = match.Index + match.Length;
        var depth = 1;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..i];
        }
        Assert.Fail($"{File}:{method} 的花括号不配对。");
        return "";
    }

    /// <summary>设计基线里的一段（按起止锚点取，锚点必须都在）。</summary>
    private static string DesignSlice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"设计基线缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"设计基线缺少 {end}。");
        return source[from..to];
    }

    private static string Slice(string start, string end)
    {
        var source = Repo.Source("Desktop", "Native", File);
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
