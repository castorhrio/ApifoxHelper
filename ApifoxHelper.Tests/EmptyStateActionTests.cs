using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 主页面空态唯一行动按钮断言（DESIGN.md §7「空态=说明现在有什么可做 + 唯一行动按钮」、§6「列表筛选」）：
/// 空态此前只有文字——任务页连动作都没有，配置页恒给一个实心「新建配置」（工具栏已有同一个主按钮，
/// 同屏两个实心主按钮；筛到零行时把「新建」当作下一步也是误导）。更要紧的是提示语当时写着「按 Esc 清空筛选框」，
/// 而 Esc 只挂在筛选框的按键处理上：焦点在工具栏、菜单或切页回来后落在别处时按它没有任何反应，
/// 提示所指引的恢复路径不可达（与第四十二轮「禁用不得移除下一步」、第四十六轮「文字不指向不存在的按钮」同族）。
/// 现在空态行动按筛选状态切换：没有数据=「新建任务/新建配置」（ghost，动作与工具栏同一入口），
/// 筛到零行=「清空筛选」（ghost）。Esc 的归属在第六十四轮单独收口（壳分发到可见页，焦点在屏内哪个控件上都算），
/// 与这里的按钮汇到同一条 ClearListFilter；提示语仍只指屏上那一个按钮——一句话给一个下一步。
/// 口径单一落点在 <see cref="ListFilter"/>；两页接线由行首正则与源码切片机械检查钉住；
/// 空态按钮的实际观感与键盘导航随 G-D5 真机复核。
/// </summary>
public class EmptyStateActionTests
{
    // 两页的切片锚点：加载方法、空态刷新方法与空态面板构造各自的起止。
    private static readonly (string Page, string Load, string Refresh)[] PageAnchors =
    [
        ("TasksPage.cs", "private void LoadTasks()", "private void RefreshEmptyState()"),
        ("ProjectsPage.cs", "private void LoadProfiles()", "private void RefreshEmptyState()"),
    ];

    // ---------- 口径：文案单一落点与提示语指向 ----------

    [Fact]
    public void ClearActionLabel_IsWhatTheEmptyStateNames()
    {
        // 按钮文字与提示语同源：提示语指名的入口必须是界面上真实存在的那个按钮。
        Assert.Equal("清空筛选", ListFilter.ClearActionLabel);
        Assert.Contains(ListFilter.ClearActionLabel, ListFilter.TaskNoMatchHint);
        Assert.Contains(ListFilter.ClearActionLabel, ListFilter.ProfileNoMatchHint);
        Assert.NotEqual(ListFilter.TaskNoMatchHint, ListFilter.ProfileNoMatchHint);
        // 提示语只指屏上真实存在的那个按钮：两种入口不并列写进同一句话（Esc 也已按得动，第六十四轮）。
        Assert.DoesNotContain("Esc", ListFilter.TaskNoMatchHint);
        Assert.DoesNotContain("Esc", ListFilter.ProfileNoMatchHint);
    }

    [Fact]
    public void Pages_DoNotInlineClearActionLabel()
    {
        // 「清空筛选」字面只在 ListFilter 里：两页出现带它的字符串字面即口径漂移（注释里的引述不算）。
        var literal = new Regex("\"[^\"\\r\\n]*清空筛选[^\"\\r\\n]*\"");
        foreach (var (page, _, _) in PageAnchors)
        {
            var source = Repo.Source("Desktop", "Native", page);
            Assert.Empty(literal.Matches(source));
            Assert.Contains("ListFilter.ClearActionLabel", source);
        }
    }

    // ---------- 接线：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void TasksPage_WiresBothEmptyStateActions()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*private readonly Button _emptyCreate = Ui\.Ghost\(NewTaskLabel\);", page);
        // 动作与工具栏同一入口：新建走 ShowNewTaskDialog，清空走 ClearListFilter，都不复制逻辑。
        Assert.Matches(@"(?m)^\s*_emptyCreate\.Click \+= \(_, _\) => ShowNewTaskDialog\(\);", page);
        Assert.Matches(@"(?m)^\s*_emptyClear\.Click \+= \(_, _\) => ClearListFilter\(\);", page);
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyCreate\);", page);
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyClear\);", page);
        Assert.Matches(@"(?m)^\s*_empty = BuildEmpty\(emptyActions\);", page);
        Assert.Matches(@"(?m)^\s*layout\.Controls\.Add\(actions, 0, 3\);", Slice(page, "private Panel BuildEmpty(", "private TableLayoutPanel BuildDetail()"));
    }

    [Fact]
    public void ProjectsPage_WiresBothEmptyStateActions()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*private readonly Button _emptyCreate = Ui\.Ghost\(NewProfileLabel\);", page);
        Assert.Matches(@"(?m)^\s*_emptyCreate\.Click \+= \(_, _\) => ShowDialog\(null\);", page);
        Assert.Matches(@"(?m)^\s*_emptyClear\.Click \+= \(_, _\) => ClearListFilter\(\);", page);
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyCreate\);", page);
        Assert.Matches(@"(?m)^\s*emptyActions\.Controls\.Add\(_emptyClear\);", page);
        Assert.Matches(@"(?m)^\s*_empty = BuildEmpty\(emptyActions\);", page);
        Assert.Matches(@"(?m)^\s*layout\.Controls\.Add\(actions, 0, 3\);", Slice(page, "private Panel BuildEmpty(", "private ProjectProfile? Selected()"));
    }

    [Fact]
    public void BothPages_DeclareTheClearActionFromSharedLabel()
    {
        foreach (var (page, _, _) in PageAnchors)
        {
            var source = Repo.Source("Desktop", "Native", page);
            Assert.Matches(@"(?m)^\s*private readonly Button _emptyClear = Ui\.Ghost\(ListFilter\.ClearActionLabel\);", source);
        }
    }

    [Fact]
    public void EmptyStateActions_SwitchOnFilterState()
    {
        // 同一时刻只显示一个行动按钮：门控取筛选状态的单一口径，不另判一次。
        foreach (var (page, load, refresh) in PageAnchors)
        {
            var source = Repo.Source("Desktop", "Native", page);
            var body = Slice(source, refresh, "private static string?[] FilterFields(");
            Assert.Matches(@"(?m)^\s*var filtered = ListFilter\.IsActive\(_filter\.Text\);", body);
            Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !filtered;", body);
            Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = filtered;", body);
            // 切换在重建里接线：刷新、作业结束与切页回来后按钮与文字同一轮推导。
            Assert.Matches(@"(?m)^\s*RefreshEmptyState\(\);", Slice(source, load, refresh));
        }
    }

    [Fact]
    public void ClearFilter_HasSinglePathForEscAndButton()
    {
        // Esc（壳分发到可见页）与空态按钮都走 ClearListFilter：清空后焦点回框，接着输入不用先点回去。
        foreach (var (page, load, _) in PageAnchors)
        {
            var source = Repo.Source("Desktop", "Native", page);
            var clear = Slice(source, "private void ClearListFilter()", load);
            Assert.Matches(@"(?m)^\s*_filter\.Text = """";", clear);
            Assert.Matches(@"(?m)^\s*_filter\.Focus\(\);", clear);
            // Esc 分支不再自己赋空值：两条入口必须汇到同一方法。
            Assert.DoesNotMatch(@"_filter\.Text = """";\s*\r?\n\s*e\.SuppressKeyPress", source);
            // 页侧的 Esc 落点也汇到同一条路：判定交共享口径，清空交唯一路径。
            var escEntry = Slice(source, "public bool TryClearListFilter()", "private void FocusFirstResult()");
            Assert.Matches(@"(?m)^\s*if \(!ListFilter\.EscClears\(Visible, ListFilter\.IsActive\(_filter\.Text\)\)\) return false;", escEntry);
            Assert.Matches(@"(?m)^\s*ClearListFilter\(\);", escEntry);
            // 一个手势一个归属：筛选框自己的按键处理里不再留一份 Esc（框内只剩 Enter 交给第一行）。
            var boxKeys = Slice(source, "_filter.KeyDown += (_, e) =>", "_list.SelectedIndexChanged");
            Assert.DoesNotContain("Keys.Escape", boxKeys, StringComparison.Ordinal);
            Assert.Matches(@"(?m)^\s*if \(e\.KeyCode == Keys\.Enter && ListKeys\.IsEnterGesture\(e\.Shift, e\.Control, e\.Alt\)\)$", boxKeys);
        }
    }

    [Fact]
    public void EmptyStateKeepsExactlyOneSolidPrimaryOnScreen()
    {
        // 实心主按钮只在工具栏：空态的新建是 ghost——此前配置页工具栏与空态各有一个实心「新建配置」。
        foreach (var (page, label) in new[] { ("TasksPage.cs", "NewTaskLabel"), ("ProjectsPage.cs", "NewProfileLabel") })
        {
            var source = Repo.Source("Desktop", "Native", page);
            Assert.Single(Regex.Matches(source, $@"Ui\.Primary\({label}\)"));
            Assert.DoesNotContain($@"Ui.Primary(""{Literal(source, label)}"")", source);
        }
    }

    [Fact]
    public void CreateActions_TakeLabelsFromASingleSource()
    {
        // 新建入口的文字常量单一落点：工具栏与空态按钮同取一个常量（对话框标题属另一界面，不在此口径）。
        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*private const string NewTaskLabel = ""新建任务"";", tasks);
        Assert.Matches(@"(?m)^\s*var newButton = Ui\.Primary\(NewTaskLabel\);", tasks);
        var profiles = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*private const string NewProfileLabel = ""新建配置"";", profiles);
        Assert.Matches(@"(?m)^\s*var newButton = Ui\.Primary\(NewProfileLabel\);", profiles);
    }

    [Fact]
    public void TasksPage_HintNamesTheEntryThatExists()
    {
        // 无数据空态的说明文字指名的入口，就是空态上那个按钮（不指向界面上不存在的东西）。
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Contains(Literal(page, "NewTaskLabel"), Literal(page, "EmptyHint"));
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_DeclaresEmptyStateActionRule()
    {
        var chrome = Repo.DesignSection(6);
        Assert.Contains("空态", chrome);
        Assert.Contains("唯一行动按钮", chrome);
        Assert.Contains("ListFilter.ClearActionLabel", chrome);
        var states = Repo.DesignSection(7);
        Assert.Contains("唯一行动按钮", states);
        Assert.Contains("筛选", states);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("空态唯一行动按钮", ledger);
        Assert.Contains("第五十轮", ledger);
    }

    private static string Literal(string source, string name)
    {
        var match = Regex.Match(source, $@"const string {name}\s*=\s*""([^""]*)""");
        Assert.True(match.Success, $"缺少常量 {name}。");
        return match.Groups[1].Value;
    }

    /// <summary>取 [start, end) 之间的源码段：把「在重建里接线」的断言限定在方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
