using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 选中即定位断言（DESIGN.md §6「列表行」、§8 本轮条款）：清单重建（刷新、切页、筛选、保存对话框返回、
/// 处理完一条续接下一条）会把列表的滚动位置甩回顶部，而程序化选中只改 Selected 与 Focused——
/// 选中停在第 45 行时屏幕上什么也看不出来，详情与按钮跟着那一行走，方向键也从一行看不见的行继续。
/// 这件事第五十一轮已经在两页各写了一次（筛选框 Enter 到结果第一行），第三十三/三十四轮的「重建后恢复选中」
/// 与第五十六轮的「队列续接落点」却没有——同一条路两种结果，故收成一条口径：选中经 Ui.SelectItem 唯一入口，
/// 定位也在这一处触达一次。是否请求滚动由 <see cref="ListReveal"/> 判定（不依赖 WinForms、与测试工程共享编译）：
/// 控件对越界序号先抛异常再判句柄，本环境没有原生运行时可跑，守卫只能按框架源码事实钉住。
/// 滚动后的实际对位（置顶还是最小滚动）随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class ListRevealTests
{
    // ---------- 口径真值表：这一行在不在这份清单之内 ----------

    [Theory]
    [InlineData(0, 1)]
    [InlineData(0, 60)]
    [InlineData(44, 60)]
    [InlineData(59, 60)]
    public void Reveal_RequestedForAnyRowInsideTheList(int index, int displayedCount)
    {
        Assert.True(ListReveal.ShouldReveal(index, displayedCount));
    }

    [Theory]
    // 空清单没有可定位的行；序号落在行数之外不是「差一点」，控件那一边是先抛异常。
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(int.MaxValue, 60)]
    public void Reveal_SkippedWhenTheIndexIsNotInTheList(int index, int displayedCount)
    {
        Assert.False(ListReveal.ShouldReveal(index, displayedCount));
    }

    [Theory]
    // 行已离开控件时序号为负（ListViewItem.Index：未加入清单即 -1），此时连清单都不存在，不请求滚动。
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void Reveal_SkippedForRowThatLeftTheControl(int index)
    {
        Assert.False(ListReveal.ShouldReveal(index, 50));
    }

    [Fact]
    public void Guard_CoversExactlyTheFrameworkThrowWindow()
    {
        // 框架源码事实（ListView.EnsureVisible）：ThrowIfNegative(index) 与 ThrowIfGreaterThanOrEqual(index, Items.Count)
        // 都在检查窗口句柄之前执行——守卫必须与这个窗口互补，多一分是把定位悄悄丢掉，少一分是界面上的一次崩溃。
        for (var count = 0; count <= 40; count++)
        {
            for (var index = -3; index <= count + 3; index++)
            {
                var throws = index < 0 || index >= count;
                Assert.Equal(!throws, ListReveal.ShouldReveal(index, count));
            }
        }
    }

    [Fact]
    public void EmptyList_HasNoRevealableRowAtAll()
    {
        // 清单清空（筛到零行、队列清完）时任何序号都不该请求滚动：各视图的空态出口负责下一步，不是滚动。
        for (var index = -2; index <= 5; index++)
        {
            Assert.False(ListReveal.ShouldReveal(index, 0));
        }
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    /// <summary>Ui.SelectItem 的方法体（无花括号的单一序列，切片取到方法末尾的右括号为止）。</summary>
    private static string SelectItemBody()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var helper = Regex.Match(ui, @"public static void SelectItem\(ListViewItem item\)\s*\{(?<body>[^}]*)\}");
        Assert.True(helper.Success, "Ui 缺少 SelectItem 程序选中口径（列表选中唯一入口）。");
        return helper.Groups["body"].Value;
    }

    [Fact]
    public void SelectItem_RevealsTheRowItSelects()
    {
        var body = SelectItemBody();
        Assert.Matches(@"(?m)^\s*item\.Selected = true;$", body);
        Assert.Matches(@"(?m)^\s*item\.Focused = true;$", body);
        // 定位的是被选中的这一行自己：序号取自行所属控件的位置，不硬写 0、不另取焦点行。
        Assert.Matches(@"(?m)^\s*var index = item\.Index;$", body);
        Assert.Matches(@"(?m)^\s*if \(!ListReveal\.ShouldReveal\(index, list\.Items\.Count\)\) return;$", body);
        Assert.Matches(@"(?m)^\s*list\.EnsureVisible\(index\);$", body);
        Assert.DoesNotContain("Items[0]", body, StringComparison.Ordinal);
        // 先选中再定位：滚动不改变选中，反过来做会让「选中即定位」这句话依赖一次多余的重绘顺序。
        var select = body.IndexOf("item.Selected = true;", StringComparison.Ordinal);
        var reveal = body.IndexOf("list.EnsureVisible(index);", StringComparison.Ordinal);
        Assert.True(select >= 0 && reveal > select, "应在选中之后请求滚动。");
        // 守卫排在请求之前：越过守卫的序号在控件那一边先抛异常。
        var guard = body.IndexOf("ListReveal.ShouldReveal(index, list.Items.Count)", StringComparison.Ordinal);
        Assert.True(guard >= 0 && reveal > guard, "应先由共享口径判定，再请求滚动。");
    }

    [Fact]
    public void SelectItem_ResolvesTheOwningListBeforeRevealing()
    {
        // 行不属于任何控件时无从滚动：这里不猜一个清单，也不把请求发给别的列表。
        var body = SelectItemBody();
        Assert.Matches(@"(?m)^\s*var list = item\.ListView;$", body);
        Assert.Matches(@"(?m)^\s*if \(list is null\) return;$", body);
        var detach = body.IndexOf("if (list is null) return;", StringComparison.Ordinal);
        var reveal = body.IndexOf("list.EnsureVisible(index);", StringComparison.Ordinal);
        Assert.True(detach >= 0 && reveal > detach, "未取得清单时不得请求滚动。");
    }

    [Fact]
    public void Scrolling_IsTouchedOnceAndOnlyInTheSelectionEntry()
    {
        // 触达界面只有一处：滚动写在唯一入口之内，页面、对话框与续接落点都不各写一套
        // （第五十一轮在两页各写一次、第五十六轮挂账的「不滚动到落点所在」由此收口成一条）。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        Assert.Single(Regex.Matches(ui, @"(?m)^\s*list\.EnsureVisible\(index\);$"));
        foreach (var source in Repo.NativeSources().Where(s => !s.IsUi))
        {
            Assert.False(Regex.IsMatch(source.Text, @"(?m)^\s*\w+\.EnsureVisible\("),
                $"{source.Name}:自己请求滚动——选中即定位只在 Ui.SelectItem 一处触达，各写一套必然写漏。");
        }
    }

    [Theory]
    [InlineData("TasksPage.cs")]
    [InlineData("ProjectsPage.cs")]
    public void Pages_NoLongerScrollByHand(string file)
    {
        // 筛选框 Enter 到结果第一行仍经唯一入口选中，只是不再在页内补一句滚动。
        var body = Slice(Repo.Source("Desktop", "Native", file), "private void FocusFirstResult()", "private void ClearListFilter()");
        Assert.Matches(@"(?m)^\s*Ui\.SelectItem\(item\);$", body);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void QueueAdvance_DoesNotScrollTwice()
    {
        // 续接的落点随行入口一并定位；本处再写一次就是两处口径。
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var body = Slice(ui, "public static void AdvanceAfterRemoval(ListView list, int handledIndex)", "/// <summary>");
        Assert.Matches(@"(?m)^\s*SelectItem\(list\.Items\[index\]\);$", body);
        Assert.DoesNotContain("EnsureVisible", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Judgment_LivesInTheSharedRuleOnly()
    {
        // 判定只有一份：界面侧调用它的地方只有选中入口，页面与对话框不自己补一套边界数学。
        var callers = Repo.NativeSources()
            .Where(s => s.Contains("ListReveal.ShouldReveal"))
            .Select(s => s.Name)
            .ToList();
        Assert.Equal(new[] { "Ui.cs" }, callers);
    }

    [Fact]
    public void SharedCompile_IncludesListReveal()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListReveal.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListReveal.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsSelectionReveal()
    {
        // 断言取本轮条款独有的字面：「选中即定位」「滚入可见区」在 §6 各出现两次（本轮条款 + 本节「列表筛选」行的指引），
        // 「列表选中定位」在 §8 也出现两次（本轮行首 + 第五十六轮行「见下一行」的引用）——只断言这些词等于没断言。
        var section = Repo.DesignSection(6);
        Assert.Contains("**选中即定位**（`ListReveal` 单一口径，2026-11-08 第五十七轮）", section);
        Assert.Contains("滚动只在 `Ui.SelectItem` 唯一入口触达一次", section);
        Assert.Contains("宁可少滚一次也不让一次选中变成崩溃", section);
        Assert.Contains("在检查窗口句柄之前先抛", section);
        var ledger = Repo.DesignSection(8);
        Assert.Matches(@"(?m)^\| 列表选中定位（§6） \|", ledger);
        Assert.Contains("14 处程序化选中只有 2 处滚", ledger);
        Assert.Contains("ListReveal.ShouldReveal(index, displayedCount)", ledger);
        Assert.Contains("第五十七轮", ledger);
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
