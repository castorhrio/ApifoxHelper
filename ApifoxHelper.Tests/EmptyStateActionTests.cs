using System.Reflection;
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
        // 配置页自第一百五十轮起有第三种状态（读取失败，ProfileLoadState 口径）：失败时另两颗按钮一起收掉，
        // 只留「重试」——失败时给「新建配置」等于把用户往另一条路上引（见 ProfileLoadFailureTests）。
        // 任务页自第一百五十二轮起同一口径（LoadFailureState）：失败态优先于「筛到零行」与「还没有任务」。
        foreach (var (page, load, refresh) in PageAnchors)
        {
            var source = Repo.Source("Desktop", "Native", page);
            var body = Slice(source, refresh, "private static string?[] FilterFields(");
            Assert.Matches(@"(?m)^\s*var filtered = ListFilter\.IsActive\(_filter\.Text\);", body);
            if (page == "ProjectsPage.cs")
            {
                Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !_loadFailed && !filtered;", body);
                Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = !_loadFailed && filtered;", body);
                Assert.Matches(@"(?m)^\s*_emptyRetry\.Visible = ProfileLoadState\.RetryVisible\(_loadFailed\);", body);
            }
            else if (page == "TasksPage.cs")
            {
                Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !_loadFailed && !filtered;", body);
                Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = !_loadFailed && filtered;", body);
                Assert.Matches(@"(?m)^\s*_emptyRetry\.Visible = LoadFailureState\.RetryVisible\(_loadFailed\);", body);
            }
            else
            {
                Assert.Matches(@"(?m)^\s*_emptyCreate\.Visible = !filtered;", body);
                Assert.Matches(@"(?m)^\s*_emptyClear\.Visible = filtered;", body);
            }
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
            var escEntry = Slice(source, "public bool TryClearListFilter()", "private void ClearListFilter()");
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

    // ---------- 判据自己被抓得住（第二百五十八轮，简报 P1-1） ----------

    [Fact]
    public void Resolve_UnknownPageName_IsRedInsteadOfSilentlyTasks()
    {
        // 注入自验：写一个 PageNames 里没有的名字，必须当场红——上一轮的写法会把它静默还原成「检查任务」。
        var thrown = Assert.ThrowsAny<Exception>(() => Resolve("", "\"先在「\" + PageNames.Projcts + \"」\""));
        Assert.Contains("PageNames.Projcts", thrown.Message);
        Assert.Contains("PageNames.Projects", thrown.Message);
        Assert.Contains("PageNames.Tasks", thrown.Message);
    }

    [Fact]
    public void Resolve_KnownPageNames_MapToTheSharedConstants()
    {
        // 两个已知名字走映射，且映射的就是 PageNames 里那一份（不是这里抄的第二份）。
        Assert.Equal(PageNames.Projects, Resolve("", "PageNames.Projects"));
        Assert.Equal(PageNames.Tasks, Resolve("", "PageNames.Tasks"));
        // 登记表与 PageNames 的公开常量逐一对上：往 PageNames 里加一个页面名，这里会点名要它一起被认。
        Assert.Equal(
            typeof(PageNames).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string))
                .Select(field => "PageNames." + field.Name)
                .OrderBy(id => id, StringComparer.Ordinal),
            PageNameIds());
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

    /// <summary>
    /// 取 <c>const string NAME = …;</c> 的<b>最终文字</b>（第二百五十七轮）。
    /// <para>
    /// 此前只认「右边是一个字面量」这一种写法。第二百五十七轮把空态说明里的页面名收进
    /// <see cref="PageNames"/> 之后，右边变成「字面量 + 共享常量 + 字面量」的拼接——
    /// 旧提取器会只取到第一段（<c>先在「</c>），于是「说明文字指名的入口就是那个按钮」
    /// 这条断言退化成「说明文字以『先在「』开头」，永远为真。门钉住的是一种写法，不是一条规则
    /// （第一百六十轮同一课）。本轮把提取口径扩到拼接式：字面量原样、<c>PageNames.X</c> 取页面名、
    /// 同文件里的 <c>const string</c> 递归展开——判的仍是「最终那句话里有没有那个按钮的名字」。
    /// </para>
    /// </summary>
    private static string Literal(string source, string name)
    {
        var match = Regex.Match(source, $@"const string {name}\s*=\s*(?<rhs>[^;]+);");
        Assert.True(match.Success, $"缺少常量 {name}。");
        return Resolve(source, match.Groups["rhs"].Value);
    }

    /// <summary>
    /// 把常量右值还原成最终文字：字面量原样、<c>PageNames.X</c> 取页面名、同文件常量递归展开。
    /// <para>
    /// <b>第二百五十八轮（简报 P1-1）修的是这里的一处静默落点</b>：上一轮把页面名收进
    /// <see cref="PageNames"/> 之后，这一句写成了 <c>id == "PageNames.Projects" ? Projects : Tasks</c>——
    /// 于是 <c>PageNames.Projcts</c>（拼错）、<c>PageNames.Profiles</c>（旧名）、甚至
    /// <c>PageNames.Whatever</c> 全都被<b>当成「检查任务」</b>还原出来。空态说明里点错一个页面名，
    /// 这条断言不但不红，还会拿一个错的页面名去比「说明文字里有没有那个按钮的名字」——
    /// 门钉住的是一种写法，不是一条规则（第一百六十轮同一课），而这一次它连「写错了」都判不出来。
    /// 现在改成：<b>已知的页面名走映射，未知的当场断言红</b>，并把「已知的是哪几个」写进失败信息。
    /// </para>
    /// </summary>
    private static string Resolve(string source, string rhs)
    {
        var text = new StringBuilder();
        foreach (Match part in Regex.Matches(rhs, @"""(?<lit>[^""]*)""|(?<id>[A-Za-z_][\w.]*)"))
        {
            if (part.Groups["lit"].Success)
            {
                text.Append(part.Groups["lit"].Value);
                continue;
            }
            var id = part.Groups["id"].Value;
            if (id.StartsWith("PageNames.", StringComparison.Ordinal))
            {
                text.Append(PageNameOf(id));
                continue;
            }
            text.Append(Literal(source, id));
        }
        return text.ToString();
    }

    /// <summary>
    /// <c>PageNames.X</c> → 页面名。映射<b>不在这里抄第二份</b>：直接读 <see cref="PageNames"/> 的公开常量，
    /// 于是「往 PageNames 里加一个页面名」不需要回来改这里，而「写了一个 PageNames 里没有的名字」当场红。
    /// </summary>
    private static string PageNameOf(string id)
    {
        var field = typeof(PageNames).GetField(
            id["PageNames.".Length..], BindingFlags.Public | BindingFlags.Static);
        if (field is null || !field.IsLiteral || field.FieldType != typeof(string))
        {
            Assert.Fail($"{id} 不是 PageNames 里的页面名常量——空态说明点名的页面必须真的存在"
                + "（第四十六轮「文字不指向不存在的按钮」同族，只是这次指向的是页面）。"
                + $"已知的是：{string.Join("、", PageNameIds())}。");
        }
        return (string)field!.GetRawConstantValue()!;
    }

    /// <summary><see cref="PageNames"/> 里登记的全部页面名标识（<c>PageNames.X</c> 形状，按序数排序）。</summary>
    private static string[] PageNameIds() =>
        typeof(PageNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => "PageNames." + field.Name)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

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
