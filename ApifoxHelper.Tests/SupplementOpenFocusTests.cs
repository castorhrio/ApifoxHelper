using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 补充说明对话框打开时的键盘归属（DESIGN.md §6「列表行」、§8 第九十五轮）：
/// 这一屏打开即选中第一个接口（<c>PopulateList</c> 末尾经 <c>Ui.SelectItem</c> 落在第一条）、编辑位已按它填好，
/// 而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——方向键动的是别的控件、Ctrl+C 也复制不到那一行，
/// 「打开就有一行被标出来」只成立了一半。另十一张清单在打开时或这一页出现时都把键盘交给清单
/// （第八十八至九十三轮），这一张没有，属同壳不同行为。
/// 第八十八轮挂账的理由（「编辑位是这一屏的输入」）在打开这一刻不成立：这一屏自己的状态行写着
/// 「选择接口编辑说明补充」，第一步就是选接口，而选接口的地方就是这张清单；编辑位是选中之后的事，
/// 打开时还没有任何输入会被抢走。故经 <c>Ui.KeepKeyboardOnQueue</c> 同一处把键盘交给清单，
/// 判定仍是 <see cref="ListFocus.ShouldHandToQueue"/>（选中行与焦点行是同一行才交），接在 OnShown 而不是 OnLoad。
/// 真机上打开补充说明后方向键是否直接走清单随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class SupplementOpenFocusTests
{
    private const string File = "SupplementsDialog.cs";

    // ---------- 真执行：打开时那一行是不是键盘该去的那一行 ----------

    [Fact]
    public void OpenPath_HandsTheKeyboardToTheRowTheDialogSelected()
    {
        // 打开路径先经 Ui.SelectItem 选中第一个接口（Selected + Focused 一起落），随后问同一份判定：
        // 选中行与焦点行是同一行才交——打开时那一行正是这样，故放行。
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.True(ListFocus.ShouldHandToQueue(17, 17));
    }

    [Fact]
    public void OpenPath_LeavesTheKeyboardAloneWhenTheScreenOpensEmpty()
    {
        // 打开时那一屏是提示或空态（尚未检查、读取失败、检查结果里没有接口）：清单里没有选中行，
        // 交键盘等于把方向键交给一个没有落点的清单——那一步的下一步在提示与按钮上。
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    // ---------- 接线：OnShown 里经同一处入口交键盘 ----------

    [Fact]
    public void Dialog_HandsTheKeyboardOnShownAfterTheBaseCall()
    {
        var body = OnShownBody();
        // 先 base.OnShown：窗体显示之后再交键盘（OnLoad 时窗体还没显示，Focus 的落点取决于框架激活时的选择）。
        Assert.Matches(@"(?m)^\s*base\.OnShown\(e\);$", body);
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(_list\);$"));
        var baseCall = body.IndexOf("base.OnShown(e);", StringComparison.Ordinal);
        var hand = body.IndexOf("Ui.KeepKeyboardOnQueue(_list);", StringComparison.Ordinal);
        Assert.True(baseCall >= 0 && hand > baseCall, "应在 base.OnShown 之后交键盘。");
    }

    [Fact]
    public void Dialog_TouchesNothingButTheKeyboard()
    {
        // 选中、滚动、重建各有自己的唯一入口：打开时这一处只交键盘，不顺手写第二套（必然与那一套漂移）。
        var body = OnShownBody();
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateList", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadEditors", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_SelectsTheFirstRowOnOpenSoThereIsARowToHand()
    {
        // 交键盘的前提是打开时真的有一行被选中：落点仍在 PopulateList 末尾经 Ui.SelectItem 唯一入口，
        // 本轮的 OnShown 不自己落点（否则「打开即选中一行」会有两处说法）。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Contains("Ui.SelectItem(target ?? _list.Items[0]);", source);
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_list\);"));
    }

    [Fact]
    public void Judgment_StaysInTheSharedRule()
    {
        // 判定只有一份：界面侧调用它的地方只有工厂入口，本对话框不自己补一套序号比较。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListFocus.ShouldHandToQueue"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    [Fact]
    public void Dialog_DoesNotMoveFocusByHand()
    {
        // 交键盘只有一处入口：本对话框不自己写 .Focus()。
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Empty(Regex.Matches(source, @"(?m)^\s*\w+\.Focus\(\);$"));
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheSupplementOpenFocus()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**补充说明打开时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第九十五轮）", section);
        Assert.Contains("打开即选中第一个接口", section);
        Assert.Contains("接在 `OnShown`（窗体首次显示之后）而不是 `OnLoad`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 补充说明打开时的键盘归属（§6） \|", ledger);
        Assert.Contains("第九十五轮", ledger);
        Assert.Contains("OnShown", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只接了补充说明这一张：不改选中落点、不改筛选与排序、不写任务与配置数据、服务层一行未动。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 补充说明打开时的键盘归属（§6） |", "\n以上每项");
        Assert.Contains("不改选中落点", row);
        Assert.Contains("服务层一行未动", row);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
    }

    /// <summary>OnShown 的方法体（无花括号的线性序列，切片取到方法末尾的右括号为止）。</summary>
    private static string OnShownBody()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var match = Regex.Match(source, @"protected override void OnShown\(EventArgs e\)\s*\{(?<body>[^}]*)\}");
        Assert.True(match.Success, "缺少 OnShown（打开时把键盘交给清单的唯一时机）。");
        return match.Groups["body"].Value;
    }

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
