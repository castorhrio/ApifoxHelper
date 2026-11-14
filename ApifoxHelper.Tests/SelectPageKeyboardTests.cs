using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览**勾选页**（生成计划前选哪些接口）的落点与键盘归属（DESIGN.md §6「列表行」、§8 第二百八十六轮）。
/// <para>
/// <b>补的是哪个缺口</b>：这一页此前是全应用最后一张「打开即选中一行、键盘却不在清单上」的清单——
/// 打开时清单里一行都没选中（方向键没有落点、空格勾不上任何一行、第二百八十四轮接上的 Ctrl+A 全选手势
/// 也没有一个被标出来的落点）；同一对话框的预览页（写入清单）在「这一页由隐藏变可见」那一次交键盘（第九十二轮），
/// 其余十张清单打开时都把键盘交给清单（第八十八至九十五轮）。同一个动作（打开一张要逐行勾的清单）
/// 在别处有落点、在这一处没有，属同壳不同行为。
/// </para>
/// <para>
/// <b>门分两段，各自只证明自己那一段</b>：① 真执行段跑的是主工程那一份判定（口径文件与测试工程共享编译），
/// 在 Linux 上真的执行；② 接线段读的是源码形状（落点与交键盘各经哪一处入口、对话框不就地写 <c>.Focus()</c>、
/// 重建路径按什么恢复）——WinForms 的按键投递在 Linux 上跑不起来，故这一段只读形状，<b>不声称它运行过</b>。
/// 真机上「打开勾选页不碰鼠标直接按 ↓ 就走到下一个接口、Ctrl+A 真的全选可见行」随 G-D5 复核。
/// </para>
/// </summary>
public class SelectPageKeyboardTests
{
    private const string File = "SyncPreviewDialog.cs";

    // ---------- 真执行：落点与交键盘的判定仍是那一份 ----------

    /// <summary>
    /// 打开时有行就落在第一条（位次 0）：勾选页与另十张清单同一处判定。
    /// </summary>
    [Fact]
    public void OpenPath_LandsOnTheFirstRowExactlyWhenTheListHasRows()
    {
        Assert.Equal(0, ListSelection.FirstRowOnOpen(12));
        Assert.Null(ListSelection.FirstRowOnOpen(0));
    }

    /// <summary>
    /// 交键盘的判定仍是那一份：选中行与焦点行是同一行才交，空态不交。
    /// </summary>
    [Fact]
    public void TheHandoffRule_IsTheSharedOne()
    {
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    /// <summary>
    /// 这一屏的重建路径（关键词框每敲一个字）不落点：落点是打开那一次的事，
    /// 重建路径只**恢复**选中（按接口键找回原来那一行，找不回才落第一条）。
    /// </summary>
    [Fact]
    public void TheRebuildPath_RestoresTheSelectionRatherThanLandingOnTheFirstRow()
    {
        var body = PopulateBody();
        // 重建前记下选中的接口键，重建后按同一个键找回——不是「每次重建都跳回第一条」。
        Assert.Contains("(Ui.SelectedItem(_selectList)?.Tag as EndpointCheck)?.Key", body, StringComparison.Ordinal);
        Assert.Contains("(i.Tag as EndpointCheck)?.Key == selectedKey", body, StringComparison.Ordinal);
        // 找回不到才落第一条，且仍经打开那一条唯一入口（不另写一套「落第一条」）。
        Assert.Contains("Ui.SelectFirstOnOpen(_selectList);", body, StringComparison.Ordinal);
    }

    // ---------- 接线：落点与交键盘各经唯一入口 ----------

    /// <summary>
    /// 打开即选中第一条：经 <c>Ui.SelectFirstOnOpen</c> 唯一入口，有行才落。
    /// </summary>
    [Fact]
    public void OpenPath_SelectsTheFirstRowThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Matches(@"(?m)^\s*Ui\.SelectFirstOnOpen\(_selectList\);$", source);
        // 不就地索引第一行（空清单上索引不存在的第一行，那一次打开变成未处理异常）。
        Assert.DoesNotContain("_selectList.Items[0]", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// 交键盘经 <c>Ui.KeepKeyboardOnQueue</c> 唯一入口，接在 OnShown（窗体首次显示之后）而不是 OnLoad。
    /// </summary>
    [Fact]
    public void TheKeyboard_IsHandedToTheListThroughTheSharedEntry()
    {
        var source = Repo.Source("Desktop", "Native", File);
        var onShown = Regex.Match(source, @"protected override void OnShown\(EventArgs e\)\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        Assert.True(onShown.Length > 0, "缺少 OnShown（打开时把键盘交给勾选清单的唯一时机）。");
        Assert.Matches(@"(?m)^\s*base\.OnShown\(e\);$", onShown);
        Assert.Single(Regex.Matches(onShown, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(_selectList\);$"));
        // 判定只有一份：本对话框不自己判「选中行与焦点行是不是同一行」。
        Assert.DoesNotContain("ListFocus.ShouldHandToQueue", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FocusedItem", source, StringComparison.Ordinal);
        // 也不自己写 .Focus()：键盘归属只有那一处入口。
        Assert.Empty(Regex.Matches(source, @"(?m)^\s*\w+\.Focus\(\);$"));
    }

    /// <summary>
    /// 回到勾选页（点「重新选择接口」）时把键盘交回勾选清单——那是预览页交出键盘的镜像时刻。
    /// </summary>
    [Fact]
    public void ComingBackFromThePlanPage_HandsTheKeyboardBack()
    {
        var body = Slice(
            Repo.Source("Desktop", "Native", File),
            "private void ShowSelectPage()",
            "选中动作的写入内容");
        Assert.Single(Regex.Matches(body, @"(?m)^\s*Ui\.KeepKeyboardOnQueue\(_selectList\);$"));
        // 这一处只交键盘，不顺手选中、不滚动、不重建清单。
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PopulateSelectList", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 这一对话框有两页各一张清单，交键盘按清单认：勾选页两处（打开时、回到勾选页时）、
    /// 预览页一处（那一页由隐藏变可见时，第九十二轮）。别处再写一句就是多了一个不交键盘的入口。
    /// </summary>
    [Fact]
    public void TheTwoPages_KeepOneRuleEach()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Equal(2, Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_selectList\);").Count);
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_actionList\);"));
    }

    /// <summary>
    /// 勾选状态仍按接口键记在 <see cref="SyncSelect"/>，重建从它恢复——本轮不动那一份口径。
    /// </summary>
    [Fact]
    public void TheCheckedState_StillLivesInTheSharedStore()
    {
        var source = Repo.Source("Desktop", "Native", File);
        Assert.Contains("_select.IsChecked(check.Key)", source, StringComparison.Ordinal);
        Assert.Contains("_select.Set(", source, StringComparison.Ordinal);
    }

    // ---------- 冒烟段：真的按一次 Ctrl+A ----------

    /// <summary>
    /// 冒烟器里有一段真的按一次 Ctrl+A（真键经系统键入），且它真的显示了这一屏。
    /// 这一段是「键到不到清单、作用域对不对」的唯一仓内读数（模拟器，不是真机）。
    /// </summary>
    [Fact]
    public void TheSmokeSegment_ReallyPressesCtrlA()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        Assert.Contains("nameof(RunSelectKeyboard)", smoke, StringComparison.Ordinal);
        Assert.Contains("private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)", smoke, StringComparison.Ordinal);
        // 真键：SendKeys 经系统键入，修饰键的真实状态随之而来（手工拼一条 WM_KEYDOWN 带不出 Ctrl）。
        // 第二百九十一轮起「按一次键」只有一处落点（PressKey），Ctrl+A 与裸空格不再各写一份形似的实现；
        // 判的是「这一段真的经那个落点按了 Ctrl+A」+「那个落点真的走 SendKeys」，两处缺一即红。
        var body = Slice(smoke, "private static void RunSelectKeyboard(IServiceProvider services, string dataRoot)", "/// <summary>\n    /// 勾选页的关键词框");
        Assert.Contains("PressKey(dialog, null, \"^a\");", body, StringComparison.Ordinal);
        var helper = Slice(smoke, "private static void PressKey(Form dialog, ListView? list, string keys)", "[DllImport(\"user32.dll\")]");
        Assert.Contains("SendKeys.SendWait(keys);", helper, StringComparison.Ordinal);
        Assert.Contains(".Show();", body, StringComparison.Ordinal);
        // 期望值取产品自己的口径，界面与它对不上就是界面错了。
        Assert.Contains("SyncSelection.SyncableKeys(", body, StringComparison.Ordinal);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsTheSelectPageLanding()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**同步预览勾选页打开即选中第一条、键盘交给清单**", section);
        Assert.Contains("第二百八十六轮", section);
        Assert.Contains("`Ui.SelectFirstOnOpen` 唯一入口", section);
        Assert.Contains("`Ui.KeepKeyboardOnQueue` 交键盘", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 同步预览勾选页打开即选中第一条、键盘交给清单（§6） \|", ledger);
        Assert.Contains("第二百八十六轮", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮没做的事不能被写成做了：真机 0 台、中文输入法与多显示器仍挂 G-D5，
        // 预览页（写入清单）仍不接「打开时」这一口径（它在「这一页由隐藏变可见」那一次交，第九十二轮）。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 同步预览勾选页打开即选中第一条、键盘交给清单（§6） |", "\n以上每项在达成时回写本文并注明证据。");
        Assert.Contains("中文输入法", row);
        Assert.Contains("G-D5", row);
        Assert.Contains("模拟器", row);
        Assert.DoesNotContain("真机已复核", row, StringComparison.Ordinal);
        Assert.DoesNotContain("全部对话框打开时初始焦点已接线", row, StringComparison.Ordinal);
    }

    /// <summary>PopulateSelectList 的方法体（从签名到下一个方法的文档注释之前）。</summary>
    private static string PopulateBody() =>
        Slice(
            Repo.Source("Desktop", "Native", File),
            "private void PopulateSelectList()",
            "/// 勾选页接口清单的列头点击");

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
