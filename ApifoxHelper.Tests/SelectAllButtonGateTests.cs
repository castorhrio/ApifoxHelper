using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览勾选页「全选可同步（N）」那颗按钮的启停（第二百九十五轮，具名 pass：clarify + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百八十七轮把「清空」按已勾数启停之后，同一行上三颗批量按钮里
/// 只剩「全选可同步（N）」还按「数据是否可用」启停——把关键词敲成只命中不可同步的行（或筛到零行），
/// 按钮上写着「全选可同步（0）」却仍是亮的，点下去什么也不会发生、计数行也不会变一个字。
/// 第四十轮对同一形状判过（「查看代码证据」残留启用、点击静默无响应），第二百八十七轮对「清空」判过第二次，
/// 本处是同一行上的第三颗。
/// </para>
/// <para>
/// <b>判据只有一份</b>：这颗按钮的作用域是<b>可见行</b>（与资源管理器 Ctrl+A 同序），
/// 所以「有没有可勾的东西」= 可见行里还剩几个可同步的，判定在 <see cref="SyncSelect.CanSelectAll"/>；
/// 界面只在 <c>SyncPreviewDialog.RefreshSelectAllButton</c> 一处落地（`!忙 ∧ 有数据 ∧ 有东西可勾`），
/// 按钮文字里的那个 N 也在同一处给——于是「文字里的 N」与「启停看的是不是同一个 N」不可能对不上。
/// 本文件前半段在 Linux 上真执行那一份判定，后半段按源码切片钉住「界面只在一处落地」。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上那颗按钮的观感（灰/亮两态的对比度、点击手感）——那随 G-D5 复核。
/// 「点下去勾上哪几行」第二百九十六轮起由冒烟段 <c>RunSelectKeyboard</c> 真的点一次量到
/// （本文件 <see cref="TheSmokeSegment_ReallyClicksTheSelectAllButton"/> 钉住那一下还在）。
/// </para>
/// </summary>
public class SelectAllButtonGateTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：CanSelectAll 与可见行里的可同步数同源 ----------

    [Fact]
    public void CanSelectAll_IsFalseWhenNothingVisibleIsSyncable()
    {
        // 可见行里一个可同步的都没有：这颗按钮没有可勾的东西，应当如实禁用。
        Assert.False(SyncSelect.CanSelectAll(0));
        Assert.True(SyncSelect.CanSelectAll(1));
        Assert.True(SyncSelect.CanSelectAll(7));
    }

    [Fact]
    public void VisibleSyncableCount_FollowsTheKeyword_NotTheWholeData()
    {
        // 关键词只改可见行（第七十二轮的口径）：按钮上那个数问的是「屏幕上还剩几个可同步的」。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("POST", "/orders", "new", "创建订单"),
            Endpoint("DELETE", "/orders", "unchanged", "删除订单"));
        var select = new SyncSelect();
        var decisions = new DecisionStore();

        Assert.Equal(2, select.VisibleSyncableCount(diff, decisions, false));

        select.Query = "创建";
        Assert.Equal(1, select.VisibleSyncableCount(diff, decisions, false));

        // 筛到只剩不可同步的那一行：可见行里可同步的是 0，按钮应当禁用（第三态）。
        select.Query = "删除";
        Assert.Equal(0, select.VisibleSyncableCount(diff, decisions, false));
        Assert.False(SyncSelect.CanSelectAll(select.VisibleSyncableCount(diff, decisions, false)));

        // 筛到零行：同样没有可勾的东西。
        select.Query = "没有这个接口";
        Assert.Empty(select.Visible(diff));
        Assert.False(SyncSelect.CanSelectAll(select.VisibleSyncableCount(diff, decisions, false)));
    }

    [Fact]
    public void CanSelectAll_IgnoresTheCheckedCount()
    {
        // 勾了 5 个、筛到只剩不可同步的行时，这颗按钮仍然没有可勾的东西——
        // 它问的是可见行，不是已勾数（与「清空」正好相反，那一颗问的是已勾数）。
        var diff = Diff(
            Endpoint("GET", "/orders", "new", "订单列表"),
            Endpoint("DELETE", "/orders", "unchanged", "删除订单"));
        var select = new SyncSelect();
        var decisions = new DecisionStore();
        select.Set("GET /orders", true);
        Assert.True(select.CanClear);

        select.Query = "删除";
        Assert.Equal(0, select.VisibleSyncableCount(diff, decisions, false));
        Assert.False(SyncSelect.CanSelectAll(select.VisibleSyncableCount(diff, decisions, false)));
        // 已勾的那一行还在（筛选不改勾选）：两颗按钮在这一态下给出两个不同的答案，各按自己的作用域。
        Assert.True(select.CanClear);
    }

    [Fact]
    public void VisibleSyncableCount_ExcludesPendingDecisions()
    {
        // 可同步口径仍取 SyncSelection（待确认项没确认的不算）：本判定不另算一份。
        var diff = Diff(Endpoint("GET", "/orders", "new", "订单列表"));
        diff.Items.Add(new DiffItem
        {
            Id = "i1",
            ResourceKey = "GET /orders",
            NeedsDecision = true,
            Category = "structure",
            Change = "changed",
        });
        var select = new SyncSelect();
        var decisions = new DecisionStore();

        Assert.Equal(0, select.VisibleSyncableCount(diff, decisions, false));
        Assert.False(SyncSelect.CanSelectAll(select.VisibleSyncableCount(diff, decisions, false)));

        decisions.Items["i1"] = "accepted";
        Assert.Equal(1, select.VisibleSyncableCount(diff, decisions, false));
        Assert.True(SyncSelect.CanSelectAll(select.VisibleSyncableCount(diff, decisions, false)));
    }

    // ---------- 接线：按钮文字与启停只在一处判定 ----------

    [Fact]
    public void Dialog_GatesTheSelectAllButtonThroughTheSharedJudge()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var code = CodeScan.Text(source);

        // 判定本体只有一处：忙 ∧ 有数据 ∧ 有东西可勾，最后那一项读的是 SyncSelect.CanSelectAll。
        var judge = Slice(source, "private void RefreshSelectAllButton()", "private void RefreshClearButton()");
        Assert.Contains("_selectAllButton.Enabled = !_busy && _diff is not null && SyncSelect.CanSelectAll(count);", judge, StringComparison.Ordinal);
        // 文字里的 N 与启停看的是同一个 count：同一方法里先算一次，两处都用它。
        Assert.Contains("var count = _diff is null ? 0 : _select.VisibleSyncableCount(_diff, _decisions, _task.OfflineMode);", judge, StringComparison.Ordinal);
        Assert.Contains("_selectAllButton.Text = \"全选可同步（\" + count + \"）\";", judge, StringComparison.Ordinal);
        // 不引入第二份计数：这一处不自己数可见行、也不读已勾数。
        Assert.DoesNotContain("CheckedCount", judge, StringComparison.Ordinal);
        Assert.DoesNotContain("_select.Visible(", judge, StringComparison.Ordinal);

        // 两个触发时刻都调它：清单重建（PopulateSelectList）与作业开始/结束（SetBusy）。
        var populate = Slice(source, "private void PopulateSelectList()", "/// 勾选页接口清单的列头点击");
        Assert.Contains("RefreshSelectAllButton();", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectAllButton.Enabled", populate, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectAllButton.Text", populate, StringComparison.Ordinal);
        var busy = Slice(source, "private void SetBusy(bool busy)", "private void RefreshPlanActionButtons()");
        Assert.Contains("RefreshSelectAllButton();", busy, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectAllButton.Enabled", busy, StringComparison.Ordinal);

        // 全文件的赋值只有三处：构造期与「整份数据不可得」各关一次，判定一处。
        var assignments = CodeScan.LinesContaining(code, "_selectAllButton.Enabled =");
        Assert.Equal(3, assignments.Count);
        Assert.Single(assignments, line => !line.Contains("= false;", StringComparison.Ordinal));
        Assert.DoesNotContain(assignments, line => line.Contains("= true;", StringComparison.Ordinal));
    }

    [Fact]
    public void Dialog_KeepsTheGestureAndTheButtonOnTheSameScope()
    {
        // 本轮只动启停：手势那一侧的门控（无数据 / 忙）与作用域（只作用于可见行）一字未改。
        var code = CodeScan.Text(Repo.Source("Desktop", "Native", File));
        Assert.Contains("_select.SelectAllSyncable(_diff, _decisions, _task.OfflineMode);", code, StringComparison.Ordinal);
        var gesture = Slice(code, "private void SelectAllSyncable()", "private void RefreshSelectionCount()");
        Assert.Contains("if (_diff is null || _busy) return;", gesture, StringComparison.Ordinal);
        // 按钮的 Click 接在同一个方法上（第二百九十六轮补）：鼠标那一支与 Ctrl+A 那一支
        // 走的是同一个落点，两条入口不可能给出两个答案。
        Assert.Contains("_selectAllButton.Click += (_, _) => SelectAllSyncable();", code, StringComparison.Ordinal);
        // 可同步口径不在本轮：仍取 SyncSelection，本文件不另算一份。
        var select = Repo.Source("Desktop", "Native", "SyncSelect.cs");
        Assert.Contains("public static bool CanSelectAll(int visibleSyncableCount) => visibleSyncableCount > 0;", CodeScan.Text(select), StringComparison.Ordinal);
        Assert.Contains("Visible(diff).Count(c => SyncSelection.SyncableNow(offlineMode, diff, decisions, c.Key))", CodeScan.Text(select), StringComparison.Ordinal);
    }

    // ---------- 冒烟段：三态都真的看过 ----------

    /// <summary>
    /// 冒烟段里「全选可同步」的三态都真的看过（第二百九十五轮）。
    /// <para>
    /// <b>为什么逐态点名而不是数行数</b>：第二百九十一轮在「清空」那一族上踩过这个坑——
    /// 一条按行数判的门，段体一长就必然要人跟着改数，改数的时候没人会去核「那几处是不是真的几态」。
    /// 这里每一态按它自己那句能读的话认，少一态即红。
    /// </para>
    /// </summary>
    [Fact]
    public void TheSmokeSegment_ReallyChecksEverySelectAllButtonState()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var body = Slice(
            smoke,
            "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)",
            "/// <summary>\n    /// 勾选页 ↔ 预览页往返");

        // 三态，每一态按它自己那句能读的话认（话在段体里，改话即红）。
        var states = new[]
        {
            "打开勾选页时可见行里有可同步的接口，「全选可同步」却是禁用的",   // ① 打开时可用
            "按钮上那个数与它实际会勾的那一批必须是同一个数",                 // ① 文字里的 N 对得上
            "按钮上那个数没跟着筛选走",                                       // ③ 筛到只剩几行时 N 跟着走
            "点下去一行也勾不上、计数行也不会变一个字",                       // ⑥ 可见行全不可同步时禁用
            "按钮上那个数必须如实是可见行里可同步的行数",                     // ⑥ 文字里的 N 是 0
        };
        foreach (var state in states)
        {
            Assert.Contains(state, body, StringComparison.Ordinal);
        }
        // 按钮按前缀认（文字里的 N 随可见行变，逐字认会随夹具漂）。
        Assert.Contains("button.Text.StartsWith(\"全选可同步\", StringComparison.Ordinal)", body, StringComparison.Ordinal);
        // 期望值仍取产品自己的口径，不另写一份「应该有几个可同步」。
        Assert.Contains("SyncSelection.SyncableKeys(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 冒烟段里那颗按钮<b>真的被点了一次</b>（第二百九十六轮）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百九十五轮把「按钮上那个数」与「可见行里可同步的行数」对上了，
    /// 但<b>那颗按钮一次都没被点过</b>——它的 <c>Click</c> 接在 <c>SelectAllSyncable</c> 上，
    /// 接错了（勾上全部行、勾完不刷新计数、勾完不放行下一步）那两条断言照样全绿。
    /// 这一段按它自己那几句能读的话认：点的是那颗按钮、期望值取产品口径、勾上的行逐字比对、
    /// 计数行与「生成同步预览」跟着走、点完清空回到空态。
    /// </para>
    /// <para>
    /// <b>为什么按话认而不数行数</b>：与上一条同一理由——按行数判的门，段体一长就必然要人跟着改数，
    /// 改数的时候没人会去核「那几处是不是真的那几件事」。
    /// </para>
    /// </summary>
    [Fact]
    public void TheSmokeSegment_ReallyClicksTheSelectAllButton()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var body = Slice(
            smoke,
            "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)",
            "/// <summary>\n    /// 勾选页 ↔ 预览页往返");

        // ① 走产品的点击链路：PerformClick 会触发按钮的 Click 事件处理器，不是直接调 SelectAllSyncable。
        Assert.Contains("selectAllButton.PerformClick();", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectAllSyncable();", body, StringComparison.Ordinal);

        // ② 点下去勾上哪几行，期望值仍取产品自己的口径（不另写一份「应该勾几个」）。
        Assert.Contains("按钮上写着几行，点下去就得勾上那几行——两个答案必须逐字相同", body, StringComparison.Ordinal);
        Assert.Contains("expectedAll.OrderBy(key => key, StringComparer.Ordinal).ToList()", body, StringComparison.Ordinal);

        // ③ 计数行与下一步跟着走（勾完却走不到下一步，是「点了没反应」的另一种形状）。
        Assert.Contains("计数没跟着按钮走", body, StringComparison.Ordinal);
        Assert.Contains("有东西可生成时它必须可用，否则用户勾完这一批却走不到下一步", body, StringComparison.Ordinal);

        // ④ 点这一下不改可见行：按钮上那个数不该跟着变。
        Assert.Contains("点这一下不改可见行，按钮上那个数不该跟着变", body, StringComparison.Ordinal);

        // ⑤ 点完清空回到空态：后面各段（Ctrl+A、空格、第三态）的前提是「一行都没勾」。
        Assert.Contains("后面各段的前提是「一行都没勾」，这一段的口径需要重定", body, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheSelectAllButtonGate()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**同步预览勾选页「全选可同步」按可见行里可同步的行数启停**", section, StringComparison.Ordinal);
        Assert.Contains("第二百九十五轮", section, StringComparison.Ordinal);
        Assert.Contains("`SyncSelect.CanSelectAll`", section, StringComparison.Ordinal);

        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 同步预览勾选页「全选可同步」按可见行里可同步的行数启停（§6） \|", ledger);
        Assert.Contains("第二百九十五轮", ledger, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 真机 0 台：这一格量到的是模拟器读数，不许写成真机已复核。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 同步预览勾选页「全选可同步」按可见行里可同步的行数启停（§6） |", "\n以上每项在达成时回写本文并注明证据。");
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
