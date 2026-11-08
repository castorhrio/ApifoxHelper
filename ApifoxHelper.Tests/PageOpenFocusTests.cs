using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 主页面「这一页出现」时的键盘归属（DESIGN.md §6「列表行」、§8 第九十六轮）：
/// 两个主页面的列表在重建后按既有选中恢复（第三十四轮），切页回来时详情区已经显示那一行；
/// 而切页后键盘落在导航按钮上（点导航按钮时焦点就在那个按钮上），方向键动的是别的控件、
/// Ctrl+C 也复制不到那一行，「有一行被标出来」只成立了一半。
/// 另十二处清单在打开时或这一页出现时都把键盘交给清单（第八十八至九十五轮），两个主页面没有，
/// 属同壳不同行为，按第五十五、五十七、五十九轮那条判据判定为缺口，不是偏好。
/// 现经 <c>Ui.KeepKeyboardOnQueue</c> 同一处把键盘交给本页列表，判定仍是
/// <see cref="ListFocus.ShouldHandToQueue"/>（选中行与焦点行是同一行才交，不猜第一行）；
/// 壳在 <c>ShowPage</c> 里经 <c>INativePage</c> 分发（切页），并在窗体首次显示之后补一次（启动那一屏：
/// <c>ShowPage</c> 跑在 <c>OnLoad</c> 里，那时控件还不可见、Focus 不生效）。
/// 重建路径（刷新、筛选、作业结束）不交——那一屏的下一步是换关键词或改配置，交键盘会把用户正在改的那一格抢走。
/// 真机上切页后方向键是否直接走列表随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class PageOpenFocusTests
{
    private const string Shell = "NativeShellForm.cs";

    // ---------- 真执行：这一页出现时那一行是不是键盘该去的那一行 ----------

    [Fact]
    public void PageAppear_HandsTheKeyboardToTheRowThePageRestored()
    {
        // 页面重建后经 Ui.SelectItem 恢复选中（Selected + Focused 一起落），随后问同一份判定：
        // 选中行与焦点行是同一行才交——切页回来时那一行正是这样，故放行。
        Assert.True(ListFocus.ShouldHandToQueue(0, 0));
        Assert.True(ListFocus.ShouldHandToQueue(6, 6));
    }

    [Fact]
    public void PageAppear_LeavesTheKeyboardAloneWhenTheListIsEmpty()
    {
        // 这一页是空态（还没有配置、还没有任务）：清单里没有选中行，交键盘等于把方向键交给一个没有落点的清单——
        // 那一步的下一步在空态那个按钮上（第五十轮），不在清单上。
        Assert.False(ListFocus.ShouldHandToQueue(null, null));
        Assert.False(ListFocus.ShouldHandToQueue(null, 0));
        Assert.False(ListFocus.ShouldHandToQueue(0, null));
    }

    // ---------- 接线：两个主页面各一处、经共享入口 ----------

    [Theory]
    [InlineData("ProjectsPage.cs")]
    [InlineData("TasksPage.cs")]
    public void Pages_HandTheKeyboardThroughTheSharedEntry(string file)
    {
        // 交键盘只在页的一行方法里、经共享入口：页不自己写 .Focus()、不自己判「选中行与焦点行是不是同一行」。
        var source = Repo.Source("Desktop", "Native", file);
        Assert.Single(Regex.Matches(source, @"(?m)^\s*public void KeepKeyboardOnQueue\(\) => Ui\.KeepKeyboardOnQueue\(_list\);$"));
        Assert.Single(Regex.Matches(source, @"Ui\.KeepKeyboardOnQueue\(_list\);"));
    }

    [Theory]
    [InlineData("ProjectsPage.cs")]
    [InlineData("TasksPage.cs")]
    public void Pages_TouchNothingButTheKeyboard(string file)
    {
        // 选中、滚动、重建各有自己的唯一入口：这一处只交键盘，不顺手写第二套（必然与那一套漂移）。
        var body = HandoffBody(file);
        Assert.DoesNotContain("SelectItem", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyFilter", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadProfiles", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadTasks", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Clear", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ProjectsPage.cs", "private void LoadProfiles()")]
    [InlineData("TasksPage.cs", "private void LoadTasks()")]
    public void Pages_DoNotHandTheKeyboardOnRebuildPaths(string file, string reload)
    {
        // 重建路径（刷新、筛选、作业结束、保存返回）不交键盘：那一屏的下一步是换关键词或改配置，
        // 交键盘会把用户正在改的那一格抢走（与第五十八轮「保存被拒的路径不交回」同一取舍）。
        var body = MethodBody(file, reload);
        Assert.DoesNotContain("KeepKeyboardOnQueue", body, StringComparison.Ordinal);
    }

    // ---------- 接线：壳在「这一页出现」时经接口分发 ----------

    [Fact]
    public void Shell_HandsTheKeyboardAfterThePageIsShown()
    {
        // 顺序：先 OnPageShown（重读数据、重建清单、按既有选中恢复），再交键盘——
        // 反过来的话交的是一个还没有选中行的清单，判定会答「不交」，接上去是一句空话。
        var body = MethodBody(Shell, "private void ShowPage(Control? page, Button nav)");
        Assert.Single(Regex.Matches(body, @"(?m)^\s*shown\.OnPageShown\(\);$"));
        Assert.Single(Regex.Matches(body, @"(?m)^\s*shown\.KeepKeyboardOnQueue\(\);$"));
        var shown = body.IndexOf("shown.OnPageShown();", StringComparison.Ordinal);
        var hand = body.IndexOf("shown.KeepKeyboardOnQueue();", StringComparison.Ordinal);
        Assert.True(shown >= 0 && hand > shown, "应在 OnPageShown 之后交键盘。");
    }

    [Fact]
    public void Shell_HandsTheKeyboardThroughTheInterface()
    {
        // 分发经 INativePage 一处：壳不按具体页类型各写一句（两个页面各写一句必然与接口那一份漂移）。
        var body = MethodBody(Shell, "private void ShowPage(Control? page, Button nav)");
        Assert.DoesNotContain("_projectsPage.KeepKeyboardOnQueue", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_tasksPage.KeepKeyboardOnQueue", body, StringComparison.Ordinal);
        var source = Repo.Source("Desktop", "Native", Shell);
        Assert.Contains("void KeepKeyboardOnQueue();", source);
    }

    [Fact]
    public void Shell_HandsTheKeyboardAgainOnShownForTheStartupScreen()
    {
        // 启动那一屏：ShowPage 跑在 OnLoad 里（async void），本地服务同步完成时页面在窗体显示之前就建好，
        // 那时控件还不可见、Focus 不生效（与第八十八轮「接在 OnShown 而不是 OnLoad」同一取舍），
        // 故在窗体首次显示之后再交一次；两处都经同一处入口与同一份判定，重复调用幂等。
        var onShown = MethodBody(Shell, "protected override void OnShown(EventArgs e)");
        Assert.Matches(@"(?m)^\s*base\.OnShown\(e\);$", onShown);
        Assert.Single(Regex.Matches(onShown, @"(?m)^\s*KeepKeyboardOnVisiblePage\(\);$"));
        var baseCall = onShown.IndexOf("base.OnShown(e);", StringComparison.Ordinal);
        var hand = onShown.IndexOf("KeepKeyboardOnVisiblePage();", StringComparison.Ordinal);
        Assert.True(baseCall >= 0 && hand > baseCall, "应在 base.OnShown 之后交键盘。");

        var dispatch = MethodBody(Shell, "private void KeepKeyboardOnVisiblePage()");
        // 与「刷新当前页」「Ctrl+F」同一分发纪律：页面未创建、正在关闭时不动作。
        Assert.Contains("_shutdownStarted", dispatch, StringComparison.Ordinal);
        Assert.Contains("IsDisposed", dispatch, StringComparison.Ordinal);
        // 只交给可见的那一页（隐藏页的列表没有落点，交过去等于把键盘交给看不见的东西）。
        Assert.Contains("_projectsPage is { Visible: true }", dispatch, StringComparison.Ordinal);
        Assert.Contains("_tasksPage is { Visible: true }", dispatch, StringComparison.Ordinal);
        // 只交键盘：不选中、不滚动、不重建、不写数据。
        Assert.DoesNotContain("SelectItem", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVisible", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshData", dispatch, StringComparison.Ordinal);
        Assert.DoesNotContain(".Focus(", dispatch, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_DoesNotHandTheKeyboardOnRefreshOrFind()
    {
        // 刷新当前页（F5）与 Ctrl+F 各有自己的落点：刷新是重建（不交键盘），Ctrl+F 交给筛选框。
        Assert.DoesNotContain("KeepKeyboardOnQueue", MethodBody(Shell, "private void RefreshVisiblePage()"), StringComparison.Ordinal);
        Assert.DoesNotContain("KeepKeyboardOnQueue", MethodBody(Shell, "private void FocusListFilterOnVisiblePage()"), StringComparison.Ordinal);
    }

    [Fact]
    public void Judgment_StaysInTheSharedRule()
    {
        // 判定只有一份：界面侧调用它的地方只有工厂入口，两个主页面与壳都不自己补一套序号比较。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListFocus.ShouldHandToQueue"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    // ---------- 设计基线 ----------

    [Fact]
    public void DesignDoc_RecordsThePageOpenFocus()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("**主页面出现时的键盘归属**（`ListFocus` 同一口径，2026-11-08 第九十六轮）", section);
        Assert.Contains("切页后键盘落在导航按钮上", section);
        Assert.Contains("经 `INativePage` 分发", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 主页面出现时的键盘归属（§6） \|", ledger);
        Assert.Contains("第九十六轮", ledger);
        Assert.Contains("OnShown", ledger);
    }

    [Fact]
    public void DesignDoc_DoesNotClaimMoreThanWasDone()
    {
        // 本轮只接了「这一页出现」这一处：重建路径不交、不改选中落点、不改筛选与排序、不写任务与配置数据。
        var ledger = Repo.DesignSection(8);
        var row = Slice(ledger, "| 主页面出现时的键盘归属（§6） |", "\n以上每项");
        Assert.Contains("重建路径不交", row);
        Assert.Contains("不改选中落点", row);
        Assert.Contains("服务层一行未动", row);
        Assert.DoesNotContain("全部清单的键盘归属已接线", row, StringComparison.Ordinal);
    }

    // ---------- 切片工具 ----------

    /// <summary>页里那一行交键盘的方法体（唯一入口，整行即全部内容）。</summary>
    private static string HandoffBody(string file)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var match = Regex.Match(source, @"(?m)^\s*public void KeepKeyboardOnQueue\(\) => (?<body>.+);$");
        Assert.True(match.Success, $"{file}:缺少 KeepKeyboardOnQueue（这一页出现时交键盘的唯一入口）。");
        return match.Groups["body"].Value;
    }

    /// <summary>
    /// 方法体：从签名行到下一个同缩进（4 空格）的成员声明为止——方法体里含嵌套花括号，
    /// 按右括号切片会截在第一个内层块上，故按成员边界切。
    /// </summary>
    private static string MethodBody(string file, string signature)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"{file}:缺少 {signature}。");
        var rest = source[(from + signature.Length)..];
        var next = Regex.Match(rest, @"(?m)^    (private|public|internal|protected) ");
        return next.Success ? rest[..next.Index] : rest;
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
