using System.Text.RegularExpressions;
using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览勾选页「清空」那颗按钮的启停（第二百八十七轮，具名 pass：clarify + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：勾选页按钮行上三颗批量按钮里，「生成同步预览」按已勾数量启停
/// （<c>RefreshSelectionCount</c>），「全选可同步（N）」按数据是否可用启停，唯独「清空」只要数据在就常亮——
/// 打开这一页（一行都没勾）点它，什么也没发生，计数行也不会变一个字。第四十轮对同一形状判过：
/// 候选审阅对话框清空选中后「查看代码证据」残留启用、点击静默无响应，那一次的处理是「空选中分支如实禁用」。
/// 同一个动作（一颗按下去什么也不会发生的按钮）在别处已经收口、在这一处没有，属同壳不同行为，
/// 按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// </para>
/// <para>
/// <b>判据只有一份</b>：能清的东西就是已勾的那一批（「清空」按<b>全部</b>清，不是只清可见行），
/// 所以「有没有可清的」= <see cref="SyncSelect.CanClear"/>，界面不另算一份计数。
/// 本文件前半段在 Linux 上真执行那一份判定，后半段按源码切片钉住「界面只在一处落地」。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上那颗按钮的观感（灰/亮两态的对比度、点击手感）——那随 G-D5 复核；
/// 也不判「清空之后计数行那句话对不对」，那一句由 <c>SyncSelectTests</c> 与冒烟段各自管着。
/// </para>
/// </summary>
public class ClearButtonGateTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：CanClear 与已勾数同源 ----------

    [Fact]
    public void CanClear_IsFalseOnAFreshPage()
    {
        // 打开这一页时一行都没勾：没有可清的东西，按钮应当如实禁用。
        var select = new SyncSelect();
        Assert.Equal(0, select.CheckedCount);
        Assert.False(select.CanClear);
    }

    [Fact]
    public void CanClear_FollowsTheCheckedCount_BothWays()
    {
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        Assert.True(select.CanClear);

        // 取消勾选（用户自己点掉那一行）之后又回到「没有可清的东西」。
        select.Set("GET /orders", false);
        Assert.False(select.CanClear);

        // 勾上再「清空」：清完仍是禁用——它清的就是这一批。
        select.Set("GET /orders", true);
        select.Set("POST /orders", true);
        Assert.True(select.CanClear);
        select.Clear();
        Assert.False(select.CanClear);
    }

    [Fact]
    public void CanClear_IgnoresTheKeyword()
    {
        // 筛选只改可见行、不改勾选（第七十二轮的口径）：它也不该改「有没有可清的东西」。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);

        select.Query = "创建";
        Assert.Single(select.Visible(diff));
        Assert.True(select.CanClear);

        select.Query = "没有这个接口";
        Assert.Empty(select.Visible(diff));
        Assert.True(select.CanClear);
    }

    [Fact]
    public void CanClear_CountsTheHiddenChecks()
    {
        // 「清空」按全部清：被关键词筛掉的那几个也算「有东西可清」——
        // 若按可见行算，筛到零行时按钮会灰掉，而实际上还留着勾选，用户清不掉它们。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"));
        var select = new SyncSelect();
        select.Set("GET /orders", true);
        select.Query = "创建";

        Assert.DoesNotContain(select.Visible(diff), e => select.IsChecked(e.Key));
        Assert.True(select.CanClear);
    }

    // ---------- 接线：按钮启停只在一处判定 ----------

    [Fact]
    public void Dialog_GatesTheClearButtonThroughTheSharedJudge()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var code = CodeScan.Text(source);

        // 判定本体只有一处：忙 ∧ 有数据 ∧ 有东西可清，最后那一项读的是 SyncSelect.CanClear。
        var judge = Slice(source, "private void RefreshClearButton()", "private void Generate()");
        Assert.Contains("_clearButton.Enabled = !_busy && _diff is not null && _select.CanClear;", judge, StringComparison.Ordinal);
        // 不引入第二份计数：这一处不自己写 CheckedCount > 0。
        Assert.DoesNotContain("CheckedCount", judge, StringComparison.Ordinal);

        // 两个触发时刻都调它：勾选变了（RefreshSelectionCount）与作业开始/结束（SetBusy）。
        var refresh = Slice(source, "private void RefreshSelectionCount()", "/// <summary>\n    /// 「清空」的启停");
        Assert.Contains("RefreshClearButton();", refresh, StringComparison.Ordinal);
        var busy = Slice(source, "private void SetBusy(bool busy)", "/// <summary>执行与核对的按钮可用性");
        Assert.Contains("RefreshClearButton();", busy, StringComparison.Ordinal);
        Assert.DoesNotContain("_clearButton.Enabled", busy, StringComparison.Ordinal);

        // 重建路径不再写死可用：启停由方法末尾的 RefreshSelectionCount 一处给。
        var populate = Slice(source, "private void PopulateSelectList()", "/// 勾选页接口清单的列头点击");
        Assert.DoesNotContain("_clearButton.Enabled", populate, StringComparison.Ordinal);
        Assert.Contains("RefreshSelectionCount();", populate, StringComparison.Ordinal);

        // 全文件的赋值只有三处：构造期与「整份数据不可得」各关一次，判定一处。
        var assignments = CodeScan.LinesContaining(code, "_clearButton.Enabled =");
        Assert.Equal(3, assignments.Count);
        Assert.Single(assignments, line => !line.Contains("= false;", StringComparison.Ordinal));
        Assert.DoesNotContain(assignments, line => line.Contains("= true;", StringComparison.Ordinal));
    }

    [Fact]
    public void Dialog_KeepsTheTwoBatchButtonsOnTheirOwnRules()
    {
        // 第二百八十七轮只动「清空」：另两颗按钮的启停口径当时一字未改（全选按数据可用、生成按已勾数量）。
        // 第二百九十五轮把「全选可同步」也收进它自己那一处判定（`RefreshSelectAllButton`，见
        // `SelectAllButtonGateTests`）——「生成同步预览」仍按已勾数量启停，这一条不变。
        var code = CodeScan.Text(Repo.Source("Desktop", "Native", File));
        Assert.Contains("_generateButton.Enabled = !busy && _select.CheckedCount > 0;", code, StringComparison.Ordinal);
        // 可同步口径与两个批量动作的作用域不在本轮：全选仍只作用于可见行。
        Assert.Contains("_select.SelectAllSyncable(_diff, _decisions, _task.OfflineMode);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedJudge_LivesInSyncSelect()
    {
        // 「有没有可清的东西」只有一份判定，且它与已勾数同源（不另存一份计数）。
        var select = Repo.Source("Desktop", "Native", "SyncSelect.cs");
        Assert.Contains("public bool CanClear => _checked.Count > 0;", CodeScan.Text(select), StringComparison.Ordinal);
        Assert.Contains("public void Clear() => _checked.Clear();", CodeScan.Text(select), StringComparison.Ordinal);
    }

    // ---------- 冒烟段：每一态都真的按过 ----------

    /// <summary>
    /// 冒烟段里「清空」的每一态都真的按过一次（第二百八十七轮立三态、第二百九十轮补到七态、第二百九十一轮补到九态）。
    /// <para>
    /// <b>为什么从「数三处」改成「逐态点名」（第二百九十一轮）</b>：第二百八十七轮那一条判的是
    /// 「<c>if (clearButton.Enabled)</c> 恰出现三处」——它判的是<b>行数</b>，不是<b>态</b>。
    /// 第二百九十轮把空格翻行与第三态接进同一段之后，那一段里这类判定变成七处，这条门当场红，
    /// 而它红的原因不是「少了一态」，是「多了一态」：一条按行数判的门，段体一长就必然要人跟着改数，
    /// 改数的时候没人会去核「那七处是不是真的七态」。现改成逐态点名——每一态按它自己那句能读的话认，
    /// 少一态即红、多一态也即红（总数仍钉住，段体再长要人看一眼这七态是不是还是这七态）。
    /// </para>
    /// </summary>
    [Fact]
    public void TheSmokeSegment_ReallyChecksEveryClearButtonState()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var body = Slice(
            smoke,
            "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)",
            "/// <summary>\n    /// 勾选页的关键词框");

        // 九态，每一态按它自己那句能读的话认（话在段体里，改话即红）。
        var states = new[]
        {
            "打开勾选页时一行都没勾，「清空」却是可用的",          // ①' 打开时禁用
            "「清空」却仍是禁用的",                                // ② Ctrl+A 勾上后可用
            "点过「清空」之后一行都没勾了",                        // ③ 清空后回到禁用
            "按一次空格勾上一行之后，「清空」仍是禁用的",          // ④ 空格勾上后可用
            "再按一次空格取消勾选之后一行都没勾了",                // ④' 空格翻回去后禁用
            "在不可同步的那一行上按空格之后一行都没勾上",          // ⑤ 不可同步行按空格后禁用
            "可见行全是不可同步的接口、一行都没勾上，「清空」却是可用的", // ⑥ 第三态
            "按一次 Ctrl+空格勾上一行之后，「清空」仍是禁用的",            // ④'' 组合键翻行后可用（第二百九十一轮）
            "再按一次 Ctrl+空格取消勾选之后一行都没勾了，「清空」却仍是可用的", // ④'' 组合键翻回去后禁用
        };
        foreach (var state in states)
        {
            Assert.Contains(state, body, StringComparison.Ordinal);
        }
        // 总数仍钉住：段体里「清空」的启停判定恰九处，多一处少一处都要人看一眼上面那九态还对不对。
        Assert.Equal(states.Length, Regex.Matches(body, @"if \((?:!)?clearButton\.Enabled\)").Count);

        Assert.Contains("clearButton.PerformClick();", body, StringComparison.Ordinal);
        // 期望值仍取产品自己的口径，不另写一份「应该勾几行」。
        Assert.Contains("SyncSelection.SyncableKeys(", body, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheClearButtonGate()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**同步预览勾选页「清空」按勾选数启停**", section, StringComparison.Ordinal);
        Assert.Contains("第二百八十七轮", section, StringComparison.Ordinal);
        Assert.Contains("`SyncSelect.CanClear`", section, StringComparison.Ordinal);

        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 同步预览勾选页「清空」按勾选数启停（§6） \|", ledger);
        Assert.Contains("第二百八十七轮", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 真机 0 台：这一格量到的是模拟器读数，不许写成真机已复核。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 同步预览勾选页「清空」按勾选数启停（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("模拟器", row, StringComparison.Ordinal);
        Assert.Contains("G-D5", row, StringComparison.Ordinal);
        Assert.DoesNotContain("真机已复核", row, StringComparison.Ordinal);
        // 本轮没做的事不能被写成做了：不改可同步口径、不改两个批量动作的作用域。
        Assert.Contains("不改可同步口径", row, StringComparison.Ordinal);
    }

    // ---------- 辅助 ----------

    private static CheckResult Diff(params EndpointCheck[] endpoints) => new() { Endpoints = [.. endpoints] };

    private static EndpointCheck Endpoint(string method, string path, string status, string? name = null) => new()
    {
        Key = method + " " + path,
        Method = method,
        Path = path,
        Status = status,
        RemoteName = name,
        InRemote = true,
    };

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
