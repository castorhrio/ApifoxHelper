using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览预览页出现时的键盘归属（DESIGN.md §6「列表行」、§8 第九十二轮）：生成计划之后对话框切到预览页，
/// 写入清单已选中第一条、写入内容已显示——而点「生成同步计划」时焦点在那个按钮上，该按钮随即被隐藏
/// （<c>_generateButton.Visible = false</c>），键盘落在框架挑的下一个可聚焦控件上：方向键动的是别的控件、
/// Ctrl+C 也复制不到那一行，「有一行被标出来」只成立了一半。
/// 另九张清单在打开时都把键盘交给清单（第八十八、八十九、九十、九十一轮，见 OpenFocusTests），
/// 这一张在「这一页出现」时没有——属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 现经同一处 <c>Ui.KeepKeyboardOnQueue</c> 把键盘交给写入清单，判定仍是 <see cref="ListFocus.ShouldHandToQueue"/>
/// （选中行与焦点行是同一行才交，不猜第一行）；只在「预览页由隐藏变可见」那一次交（生成计划之后、查看已有计划），
/// 重建路径（关键词框每敲一个字、执行或核对后按磁盘计划重读）不交——那一屏的下一步是换关键词或看结果，
/// 交键盘会把用户正在改的那一格抢走（与第五十八轮「保存被拒的路径不交回」同一取舍）。
/// 真机上「生成计划后不碰鼠标直接按 ↓ 就走到下一个动作、Ctrl+C 复制到的是那一行的资源」随 G-D5 复核
/// （Linux 无 WinForms GUI 运行时）。
/// </summary>
public class PlanOpenFocusTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：预览页落点之后那一行是不是键盘该去的那一行 ----------

    [Fact]
    public void PreviewPage_HandsTheKeyboardToTheRowItLandedOn()
    {
        // 预览页的落点经 Ui.SelectItem 落（Selected + Focused 一起落），随后问同一份判定：
        // 选中行与焦点行是同一行才交——这一页出现时那一行正是这样，故放行。
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.True(ListFocus.ShouldHandToQueue(37, 37));
    }

    [Fact]
    public void PreviewPage_LeavesTheKeyboardAloneWhenThePlanHasNoAction()
    {
        // 计划没有动作（或筛到零行）时清单里没有选中行：交键盘等于把方向键交给一个没有落点的清单，
        // 那一屏由表头、排除清单与提示说话（PopulateActions 的两条早退分支）。
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    // ---------- 接线：只在「预览页由隐藏变可见」那一次交 ----------

    [Fact]
    public void PreviewPage_HandsTheKeyboardOnlyWhenItAppears()
    {
        var body = ShowPlanBody();
        // 这一页是不是刚出现：由隐藏变可见那一次才算（重建路径上它一直是可见的）。
        Assert.Single(Regex.Matches(body, @"(?m)^\s*var opening = !_page2\.Visible;$"));
        Assert.Single(Regex.Matches(body, @"(?m)^\s*if \(opening\) Ui\.KeepKeyboardOnQueue\(_actionList\);$"));
        var judge = body.IndexOf("var opening = !_page2.Visible;", StringComparison.Ordinal);
        var show = body.IndexOf("_page2.Visible = true;", StringComparison.Ordinal);
        var hand = body.IndexOf("if (opening) Ui.KeepKeyboardOnQueue(_actionList);", StringComparison.Ordinal);
        Assert.True(judge >= 0 && show > judge, "应先判「这一页是不是刚出现」，再把这一页显示出来。");
        Assert.True(hand > show, "交键盘必须在这一页可见之后：焦点交给一个不可见控件不会生效。");
    }

    [Fact]
    public void PreviewPage_HandsTheKeyboardLast()
    {
        // 交键盘放在方法末尾：清单、写入内容与按钮都已就位，此后没有别的赋值会再动焦点。
        var body = ShowPlanBody();
        var hand = body.IndexOf("if (opening) Ui.KeepKeyboardOnQueue(_actionList);", StringComparison.Ordinal);
        var buttons = body.IndexOf("RefreshPlanActionButtons();", StringComparison.Ordinal);
        Assert.True(buttons >= 0 && hand > buttons, "交键盘应排在按钮态刷新之后（方法末尾），否则后面的赋值会把它盖掉。");
        Assert.True(hand > body.IndexOf("PopulateActions();", StringComparison.Ordinal), "交键盘应在写入清单重建之后。");
    }

    [Fact]
    public void PreviewPage_AppearsInExactlyOnePlace()
    {
        // 触发点只有一处：这一页由隐藏变可见只发生在 ShowPlan 里（生成计划之后、查看已有计划），
        // 别处再写一句 `_page2.Visible = true;` 就等于多了一个不交键盘的入口。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_page2\.Visible = true;$"));
        Assert.Single(Regex.Matches(source, @"(?m)^\s*_page2\.Visible = false;$"));
    }

    [Fact]
    public void RebuildPaths_DoNotTakeTheKeyboard()
    {
        // 关键词框每敲一个字都重建写入清单（TextChanged → PopulateActions），执行或核对后按磁盘计划重读也走它——
        // 这些路径上用户可能正在关键词框里打字，交键盘等于把下一步从他手里拿走（第五十八轮同一划界）。
        var body = Slice(
            Repo.Source("Desktop", "Native", File),
            "private void PopulateActions()",
            "/// 未纳入清单的关键词框");
        Assert.DoesNotContain("KeepKeyboardOnQueue", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Handoff_TouchesNothingButTheKeyboard()
    {
        // 选中、滚动、重建各有自己的唯一入口：这一处只交键盘，不顺手写第二套（必然与那一套漂移）。
        var body = ShowPlanBody();
        var line = Regex.Match(body, @"(?m)^\s*if \(opening\) Ui\.KeepKeyboardOnQueue\(_actionList\);$").Value;
        Assert.DoesNotContain("SelectItem", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", line, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", line, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateActions", line, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", line, StringComparison.Ordinal);
        // 这一对话框有两页各一张清单，交键盘按清单认（第二百八十六轮勾选页也接上「打开时」这一口径）：
        // 勾选页两处（打开时、回到勾选页时）、预览页一处（那一页由隐藏变可见时），
        // 别处再写一句就是多了一个不交键盘的入口。整个对话框不自己写 .Focus()。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Equal(2, Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_selectList\);").Count);
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_actionList\);"));
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
    public void PreviewPage_DoesNotTakeTheKeyboardOnOpen()
    {
        // 打开时对话框落在勾选页，预览页那时还不存在（计划还没生成）——写入清单没有落点可交，
        // 它在「这一页由隐藏变可见」那一次交（第九十二轮，见 PreviewPage_HandsTheKeyboardOnlyWhenItAppears）。
        // 第二百八十六轮勾选页接上了「打开时」这一口径（它打开即选中第一条接口），故 OnShown 交的是勾选页那一处，
        // 不是写入清单——两张清单各有一处，别把一条口径接到两张清单上。
        var source = Repo.Source("Desktop", "Native", File);
        var onShown = Regex.Match(source, @"protected override void OnShown\(EventArgs e\)\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.True(onShown.Length > 0, "缺少 OnShown（打开时把键盘交给勾选清单的唯一时机）。");
        Assert.DoesNotContain("_actionList", onShown, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(onShown, @"Ui\.KeepKeyboardOnQueue\(_selectList\);"));
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsThePreviewPageOpenFocus()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**同步预览预览页出现时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第九十二轮）", section);
        Assert.Contains("预览页由隐藏变可见", section);
        Assert.Contains("**重建路径不交**（关键词框每敲一个字都重建写入清单、执行或核对后按磁盘计划重读也走它）", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 同步预览预览页出现时的键盘归属（§6） \|", ledger);
        Assert.Contains("第九十二轮", ledger);
        Assert.Contains("KeepKeyboardOnQueue", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮没做的事不能被写成做了：勾选页的清单没有选中语义、不接此口径；补充说明与注释回写仍不接；
        // 也不得出现「全部对话框打开时初始焦点已接线」这类话。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 同步预览预览页出现时的键盘归属（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("勾选页", row);
        Assert.Contains("补充说明", row);
        Assert.Contains("注释回写", row);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
        Assert.DoesNotContain("同步预览两页都已接线", row, StringComparison.Ordinal);
    }

    /// <summary>ShowPlan 的方法体（从签名到下一个方法的文档注释之前）。</summary>
    private static string ShowPlanBody() =>
        Slice(
            Repo.Source("Desktop", "Native", File),
            "private void ShowPlan(SyncPlan plan)",
            "/// 写入清单的关键词框（DESIGN.md §6「列表筛选」）");

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
