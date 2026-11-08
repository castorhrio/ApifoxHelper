using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 主页列表筛选断言（DESIGN.md §6「列表筛选」）：任务与配置会一直累积，固定顺序下要找到
/// 「上次那个任务」「某个配置下的任务」只能逐行扫一列；两个主页面列表上方各有一个筛选框，
/// 输入即筛选、Esc 清空。口径单一落点在 <see cref="ListFilter"/>（不依赖 WinForms，与测试工程共享编译），
/// 两页接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）；
/// 筛选框的实际输入手感、Esc 键与占位文字的真机观感随 G-D5 复核。
/// </summary>
public class ListFilterTests
{
    private sealed record Row(string Name, string Profile, string Next);

    private static readonly Row[] Rows =
    [
        new("登录接口整理", "订单服务", "开始扫描"),
        new("订单字段核对", "订单服务", "开始检查"),
        new("用户中心注释", "用户服务", "查看报告"),
    ];

    private static string?[] Fields(Row row) => [row.Name, row.Profile, row.Next];

    // ---------- 命中口径 ----------

    [Fact]
    public void Matches_EmptyQueryMatchesEveryRow()
    {
        // 空关键词即全部行：null、空串与全空白都视为「没有筛选」，不把列表筛成空。
        Assert.True(ListFilter.Matches(null, "任意"));
        Assert.True(ListFilter.Matches("", "任意"));
        Assert.True(ListFilter.Matches("   ", "任意"));
        Assert.True(ListFilter.Matches("\t\r\n", "任意"));
        Assert.False(ListFilter.IsActive("  "));
        Assert.True(ListFilter.IsActive("任务"));
    }

    [Fact]
    public void Matches_TrimsQueryBeforeComparing()
    {
        // 首尾空白不参与比较：粘贴带空格的词也能命中。
        Assert.True(ListFilter.Matches("  登录  ", "登录接口整理"));
        Assert.Equal("登录", ListFilter.Normalize("  登录  "));
        Assert.Equal("", ListFilter.Normalize(null));
    }

    [Fact]
    public void Matches_AnyFieldCounts()
    {
        // 字段间「或」：命中任一字段即算命中（任务名、配置名、下一步标题都能找）。
        Assert.True(ListFilter.Matches("订单服务", Fields(Rows[0])));
        Assert.True(ListFilter.Matches("开始检查", Fields(Rows[1])));
        Assert.False(ListFilter.Matches("用户服务", Fields(Rows[0])));
    }

    [Fact]
    public void Matches_AllTermsMustHit()
    {
        // 词间「且」：多个词各命中一个字段才算命中，缩小范围而不是放大。
        Assert.True(ListFilter.Matches("订单 检查", Fields(Rows[1])));
        Assert.False(ListFilter.Matches("订单 报告", Fields(Rows[1])));
        // 同一个词可以落在不同字段上，不要求落在同一字段。
        Assert.True(ListFilter.Matches("登录 订单服务", Fields(Rows[0])));
    }

    [Fact]
    public void Matches_IsCaseInsensitiveAndSubstring()
    {
        // 按当前区域忽略大小写比较，且是子串匹配（不要求整词）：输入「api」能找到「API 项目」。
        Assert.True(ListFilter.Matches("api", "API 项目"));
        Assert.True(ListFilter.Matches("API", "api 项目"));
        Assert.True(ListFilter.Matches("登", "登录接口整理"));
    }

    [Fact]
    public void Matches_TreatsNullFieldsAsEmpty()
    {
        // 缺值字段（未绑定项目、未配置分支）按空处理：不抛异常，也不会被任何词命中。
        Assert.True(ListFilter.Matches("名称", null, "名称"));
        Assert.False(ListFilter.Matches("未绑定", null, "名称"));
        Assert.True(ListFilter.Matches("", null, null));
    }

    // ---------- 筛选与排序的组合 ----------

    [Fact]
    public void Apply_WithoutQueryKeepsEveryRowInSourceOrder()
    {
        var rows = ListFilter.Apply(Rows, null, Fields);
        Assert.Equal(Rows.Select(r => r.Name), rows.Select(r => r.Name));
        // 没有筛选也返回新列表：页面拿到的顺序与数据源自身列表互不影响。
        Assert.NotSame(Rows, rows);
    }

    [Fact]
    public void Apply_KeepsSourceOrderOfHits()
    {
        // 筛选只挑行、不改相对顺序：排序由 ListSort 单一口径负责，两者不互相越界。
        var rows = ListFilter.Apply(Rows, "订单服务", Fields);
        Assert.Equal(["登录接口整理", "订单字段核对"], rows.Select(r => r.Name));
        Assert.Empty(ListFilter.Apply(Rows, "不存在的关键词", Fields));
    }

    [Fact]
    public void Apply_ThenSort_ComposesWithoutLosingRows()
    {
        // 筛选先于排序：筛掉的行不参与排序，剩下的行按列排序口径重排。
        var filtered = ListFilter.Apply(Rows, "订单服务", Fields);
        var sorted = ListSort.Order(filtered, 0, true, (row, _) => row.Name);
        // 降序按当前区域比较（「订」在「登」之后，与资源管理器同序）。
        Assert.Equal(["订单字段核对", "登录接口整理"], sorted.Select(r => r.Name));
        Assert.Equal(2, filtered.Count);
    }

    // ---------- 文案单一落点 ----------

    [Fact]
    public void Copy_PlaceholdersAndEmptyTextsAreDistinct()
    {
        // 两页的占位文字与「筛到零行」空态文字各自不同，且都指向清空筛选这一恢复路径。
        Assert.NotEqual(ListFilter.TaskPlaceholder, ListFilter.ProfilePlaceholder);
        Assert.NotEqual(ListFilter.TaskNoMatchTitle, ListFilter.ProfileNoMatchTitle);
        Assert.Contains("Esc", ListFilter.TaskNoMatchHint);
        Assert.Contains("Esc", ListFilter.ProfileNoMatchHint);
        Assert.Contains("任务", ListFilter.TaskNoMatchTitle);
        Assert.Contains("配置", ListFilter.ProfileNoMatchTitle);
    }

    [Fact]
    public void Pages_DoNotInlineFilterTexts()
    {
        // 占位文字与空态文字只在 ListFilter 里：两页出现同样的字面即口径漂移。
        foreach (var name in new[] { "TasksPage.cs", "ProjectsPage.cs" })
        {
            var page = Repo.Source("Desktop", "Native", name);
            Assert.DoesNotContain(ListFilter.TaskPlaceholder, page);
            Assert.DoesNotContain(ListFilter.ProfilePlaceholder, page);
            Assert.DoesNotContain(ListFilter.TaskNoMatchTitle, page);
            Assert.DoesNotContain(ListFilter.ProfileNoMatchTitle, page);
            Assert.DoesNotContain(ListFilter.TaskNoMatchHint, page);
            Assert.DoesNotContain(ListFilter.ProfileNoMatchHint, page);
        }
    }

    [Fact]
    public void SharedSource_IsCompiledIntoTestProject()
    {
        // 口径只有一份：测试断言对准主工程真实实现（共享编译清单里必须有 ListFilter.cs）。
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListFilter.cs", csproj);
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void TasksPage_WiresListFilter()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 筛选框经工厂出厂、包 1px 描边、进工具栏；占位文字取共享常量。
        Assert.Matches(@"(?m)^\s*private readonly TextBox _filter = Ui\.Input\(\);", page);
        Assert.Matches(@"(?m)^\s*_filter\.PlaceholderText = ListFilter\.TaskPlaceholder;", page);
        Assert.Matches(@"(?m)^\s*var filterField = Ui\.Field\(_filter\);", page);
        Assert.Matches(@"(?m)^\s*buttons\.Controls\.Add\(filterField\);", page);
        // 输入即筛选、Esc 清空（有内容才清，空框按 Esc 不吞键）。
        Assert.Matches(@"(?m)^\s*_filter\.TextChanged \+= \(_, _\) => LoadTasks\(\);", page);
        Assert.Matches(@"(?m)^\s*if \(e\.KeyCode != Keys\.Escape \|\| _filter\.Text\.Length == 0\) return;", page);
        Assert.Matches(@"(?m)^\s*_filter\.Text = """";", page);

        var load = Slice(page, "private void LoadTasks()", "private void RefreshEmptyState()");
        // 筛选先于排序：同一行里 Apply 在 Order 之内，筛掉的行不参与排序。
        Assert.Matches(@"(?m)^\s*var all = _tasks\.List\(\);", load);
        Assert.Matches(@"(?m)^\s*var tasks = ListSort\.Order\(ListFilter\.Apply\(all, _filter\.Text, FilterFields\), _sortColumn, _sortDescending, SortKey\);", load);
        // 空态两态与状态行计数在重建里接线。
        Assert.Matches(@"(?m)^\s*RefreshEmptyState\(\);", load);
        Assert.Matches(@"(?m)^\s*SetStatus\(ListFilter\.IsActive\(_filter\.Text\) \? \$""筛选出 \{tasks\.Count\} 个任务（共 \{all\.Count\} 个）"" : null, UiTokens\.Muted\);", load);
        // 选中按 Id 恢复：局部取值必须在清空列表之前（否则被选中事件抹成 null）。
        // 两处锚点都要求存在——锚点缺失时 IndexOf 返回 -1，只比大小会被「挪走」的实现骗过。
        var capture = load.IndexOf("var selectedId = _selectedId;", StringComparison.Ordinal);
        var clear = load.IndexOf("_list.Items.Clear();", StringComparison.Ordinal);
        Assert.True(capture >= 0, "TasksPage:缺少选中 Id 的局部取值。");
        Assert.True(clear >= 0, "TasksPage:缺少列表清空。");
        Assert.True(capture < clear, "TasksPage:选中 Id 必须在清空列表之前取局部。");
        Assert.Matches(@"(?m)^\s*_selectedId = _selectedTask\?\.Id;", page);
        // 空态文字按筛选状态切换，不把筛选结果说成没有任务。
        Assert.Matches(@"(?m)^\s*_emptyTitle\.Text = filtered \? ListFilter\.TaskNoMatchTitle : EmptyTitle;", page);
        Assert.Matches(@"(?m)^\s*_emptyHint\.Text = filtered \? ListFilter\.TaskNoMatchHint : EmptyHint;", page);
    }

    [Fact]
    public void ProjectsPage_WiresListFilter()
    {
        var page = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*private readonly TextBox _filter = Ui\.Input\(\);", page);
        Assert.Matches(@"(?m)^\s*_filter\.PlaceholderText = ListFilter\.ProfilePlaceholder;", page);
        Assert.Matches(@"(?m)^\s*var filterField = Ui\.Field\(_filter\);", page);
        Assert.Matches(@"(?m)^\s*buttons\.Controls\.Add\(filterField\);", page);
        Assert.Matches(@"(?m)^\s*_filter\.TextChanged \+= \(_, _\) => LoadProfiles\(\);", page);
        Assert.Matches(@"(?m)^\s*if \(e\.KeyCode != Keys\.Escape \|\| _filter\.Text\.Length == 0\) return;", page);
        Assert.Matches(@"(?m)^\s*_filter\.Text = """";", page);

        var load = Slice(page, "private void LoadProfiles()", "private void RefreshEmptyState()");
        Assert.Matches(@"(?m)^\s*var all = _profiles\.List\(\);", load);
        Assert.Matches(@"(?m)^\s*var profiles = ListSort\.Order\(ListFilter\.Apply\(all, _filter\.Text, FilterFields\), _sortColumn, _sortDescending, SortKey\);", load);
        Assert.Matches(@"(?m)^\s*RefreshEmptyState\(\);", load);
        Assert.Matches(@"(?m)^\s*SetStatus\(ListFilter\.IsActive\(_filter\.Text\) \? \$""筛选出 \{profiles\.Count\} 个配置（共 \{all\.Count\} 个）"" : null, UiTokens\.Muted\);", load);
        Assert.Matches(@"(?m)^\s*_emptyTitle\.Text = filtered \? ListFilter\.ProfileNoMatchTitle : EmptyTitle;", page);
        Assert.Matches(@"(?m)^\s*_emptyHint\.Text = filtered \? ListFilter\.ProfileNoMatchHint : EmptyHint;", page);
    }

    [Fact]
    public void TasksPage_FilterFieldsMirrorDisplayedCells()
    {
        // 筛选字段与单元格同源：用户在列表上看到什么就能按什么找，不引入界面上不存在的字段。
        var body = FilterFieldsBody(Repo.Source("Desktop", "Native", "TasksPage.cs"));
        Assert.Contains("task.Name", body);
        Assert.Contains("task.ProfileSnapshot.Name", body);
        Assert.Contains("NextStep.Compute(task).Title", body);
        Assert.Contains("TaskDisplay.RemoteStatusText(task.Stages.Remote.Status)", body);
    }

    [Fact]
    public void ProjectsPage_FilterFieldsMirrorDisplayedCells()
    {
        var body = FilterFieldsBody(Repo.Source("Desktop", "Native", "ProjectsPage.cs"));
        Assert.Contains("profile.Name", body);
        Assert.Contains("profile.ApiProject", body);
        // 占位值与单元格同一取值（未绑定/主分支），不另造一套文字。
        Assert.Contains(@"profile.Apifox.ProjectId ?? ""未绑定""", body);
        Assert.Contains(@"profile.Apifox.BranchName ?? ""主分支""", body);
        Assert.Contains("SwaggerText(profile.Swagger.Type)", body);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_DeclaresListFilterRule()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("列表筛选", section);
        Assert.Contains("`ListFilter`", section);
        Assert.Contains("Esc", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("列表筛选", ledger);
        Assert.Contains("ListFilter", ledger);
    }

    private static string FilterFieldsBody(string page)
    {
        var start = page.IndexOf("string?[] FilterFields(", StringComparison.Ordinal);
        Assert.True(start >= 0, "缺少 FilterFields。");
        return page[start..page.IndexOf("];", start, StringComparison.Ordinal)];
    }

    /// <summary>取 [start, end) 之间的源码段：把「重建里接线」的断言限定在加载方法体内。</summary>
    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
