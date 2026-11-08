using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 列表排序状态跨启动记忆的口径断言（ListSortMemory，共享源码单一口径）：
/// 记的是本页那一列的<b>标题</b>与方向，不是列号——列号会在界面增删列之后指向另一列，
/// 那等于把用户没点过的那一列当成上次排的那一列。认不出标题、页键不认识、内容损坏、无记录
/// 一律回退未排序（任务=创建时间倒序、配置=名称升序，即第四十七轮的原有行为）；
/// 保存失败不影响本次排序。与 LastPage / WindowPlacement 同族：只记便利设置，不记业务数据。
/// 两页与壳的接线由行首正则与源码切片机械检查钉住——口径存在但界面没接线等于没有。
/// </summary>
public class ListSortMemoryTests : IDisposable
{
    private static readonly string[] TaskTitles = ["任务", "配置", "下一步", "接口", "注释候选", "Apifox", "更新时间"];

    private static readonly string[] ProfileTitles = ["名称", "API 项目", "Apifox 项目", "分支", "Swagger", "更新时间"];

    private readonly string _root;
    private readonly string _path;

    public ListSortMemoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-listsortmemorytests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "desktop-list-sort-native.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private ListSortMemory Memory() => new(_path);

    // ---------- 读取口径：认不出就回退未排序 ----------

    [Fact]
    public void MissingFile_ReadsUnsorted()
    {
        Assert.Equal((ListSort.None, false), Memory().Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveThenRead_RoundTripsColumnAndDirection(bool descending)
    {
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending);
        Assert.Equal((6, descending), memory.Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void SavedShape_IsColumnTitleNotColumnNumber()
    {
        // 落盘的是标题文字：改成落列号后，这一条与「插入列后仍指向同一列」两条一起红。
        Memory().Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        var json = File.ReadAllText(_path);
        Assert.Matches(@"""column""\s*:\s*""更新时间""", json);
        Assert.DoesNotMatch(@"""column""\s*:\s*\d", json);
    }

    [Fact]
    public void Read_InsertedColumnBeforeIt_StillFindsSameColumn()
    {
        // 记标题的实际收益：界面在中间加一列，用户上次排的仍是同一列，而不是它左边那一列。
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 3, descending: false);
        IReadOnlyList<string> afterInsert = ["任务", "配置", "下一步", "模块", "接口", "注释候选", "Apifox", "更新时间"];
        Assert.Equal((4, false), memory.Read(ListSortMemoryKeys.Tasks, afterInsert));
    }

    [Fact]
    public void Read_RememberedTitleMissingFromColumns_FallsBackToUnsorted()
    {
        // 列被删掉或文案改动：回退数据源顺序，方向位一并归零——不猜一个相邻列接着排。
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        Assert.Equal((ListSort.None, false), memory.Read(ListSortMemoryKeys.Tasks, ["任务", "配置", "更新时间（改）"]));
    }

    [Fact]
    public void Read_UnknownPageKey_FallsBackToUnsorted()
    {
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        Assert.Equal((ListSort.None, false), memory.Read("reports", TaskTitles));
    }

    [Fact]
    public void Read_ColumnStoredAsNumber_IsNotATitle()
    {
        // 列号形状（旧写法或手工改坏的文件）不当标题用：形状不认识即回退未排序，不抛给界面。
        File.WriteAllText(_path, """{ "tasks": { "column": 6, "descending": true } }""");
        Assert.Equal((ListSort.None, false), Memory().Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Read_EmptyTitle_FallsBackToUnsorted()
    {
        File.WriteAllText(_path, """{ "tasks": { "column": "", "descending": true } }""");
        Assert.Equal((ListSort.None, false), Memory().Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Read_CorruptJson_FallsBackToUnsorted()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Equal((ListSort.None, false), Memory().Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Read_DuplicateTitles_TakeTheFirstColumn()
    {
        // 两列同名时取第一列：结果确定、不随数据源顺序浮动（两主页面的列标题本不重复，此条钉住取值规则）。
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Profiles, ["名称", "更新时间", "分支", "更新时间"], 1, descending: true);
        Assert.Equal((1, true), memory.Read(ListSortMemoryKeys.Profiles, ["名称", "更新时间", "分支", "更新时间"]));
        Assert.Equal((1, true), Memory().Read(ListSortMemoryKeys.Profiles, ["名称", "更新时间", "分支", "更新时间"]));
    }

    // ---------- 保存口径：认不出就不写，写坏不影响本次排序 ----------

    [Fact]
    public void Save_ForeignKey_WritesNothing()
    {
        Memory().Save("reports", TaskTitles, 0, descending: false);
        Assert.False(File.Exists(_path), "非法页面键不应落盘。");
    }

    [Fact]
    public void Save_ForeignKey_DoesNotClobberSavedValue()
    {
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        memory.Save("reports", TaskTitles, 0, descending: false);
        Assert.Equal((6, true), memory.Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Save_ColumnOutOfRange_WritesNothingAndKeepsEarlierEntry()
    {
        // 列号落在本页列头之外（界面减列而调用面未跟上）：宁可忘记，也不写一个猜出来的列。
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 99, descending: false);
        Assert.False(File.Exists(_path), "越界列号不应落盘。");
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 99, descending: false);
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, ListSort.None, descending: false);
        Assert.Equal((6, true), memory.Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Save_EmptyTitleColumn_WritesNothing()
    {
        Memory().Save(ListSortMemoryKeys.Tasks, ["", "任务"], 0, descending: true);
        Assert.False(File.Exists(_path), "空标题不是可认出的列，不应落盘。");
    }

    [Fact]
    public void TwoPages_RememberTheirOwnColumns()
    {
        // 两页各排各的列：后保存的一页不得抹掉另一页的记忆——两个方向都验，
        // 否则「保存本页时把另一页写成空」这一半缺陷会逃过检查。
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        memory.Save(ListSortMemoryKeys.Profiles, ProfileTitles, 1, descending: false);
        Assert.Equal((6, true), memory.Read(ListSortMemoryKeys.Tasks, TaskTitles));
        Assert.Equal((1, false), memory.Read(ListSortMemoryKeys.Profiles, ProfileTitles));

        var again = Memory();
        again.Save(ListSortMemoryKeys.Profiles, ProfileTitles, 5, descending: true);
        again.Save(ListSortMemoryKeys.Tasks, TaskTitles, 3, descending: false);
        Assert.Equal((3, false), again.Read(ListSortMemoryKeys.Tasks, TaskTitles));
        Assert.Equal((5, true), again.Read(ListSortMemoryKeys.Profiles, ProfileTitles));
    }

    [Fact]
    public void Save_AgainSamePage_ReplacesEarlierEntry()
    {
        var memory = Memory();
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 6, descending: true);
        memory.Save(ListSortMemoryKeys.Tasks, TaskTitles, 3, descending: false);
        Assert.Equal((3, false), memory.Read(ListSortMemoryKeys.Tasks, TaskTitles));
    }

    [Fact]
    public void Keys_AreTheTwoPagesOnly()
    {
        // 页键单一落点：两页键之外的值在 Save 里被忽略（上面的非法键用例即靠它）。
        Assert.Equal("tasks", ListSortMemoryKeys.Tasks);
        Assert.Equal("profiles", ListSortMemoryKeys.Profiles);
        Assert.Equal(2, Regex.Matches(Repo.Source("Desktop", "Native", "ListSortMemory.cs"), @"(?m)^\s*public const string ").Count);
    }

    [Fact]
    public void Memory_HoldsNothingButSortState()
    {
        // 只记「按哪一列、朝哪个方向」：不记筛选关键词、不记选中行、不记列宽列序，也不碰任务与配置数据。
        var source = Repo.Source("Desktop", "Native", "ListSortMemory.cs");
        Assert.Matches(@"(?m)^\s*private sealed record Entry\(string\? Column, bool Descending\);", source);
        foreach (var forbidden in new[] { "keyword", "filter", "width", "selected", "TaskStore", "ProfileStore", "Revision" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    [Fact]
    public void Shell_BuildsMemoryFromStoreRoot_AndPassesToBothPages()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 落点与窗口位置、页面键同目录同族。
        Assert.Matches(@"(?m)^\s*_sortMemory = new ListSortMemory\(Path\.Combine\(host\.Store\.Root, ""desktop-list-sort-native\.json""\)\);", shell);
        // 两页构造都把记忆交给页面（漏一页即该页每次启动回到固定顺序）。
        Assert.Matches(@"(?m)^\s*_host\.Services\.GetRequiredService<CredentialStore>\(\),\s*\n\s*_sortMemory\);", shell);
        Assert.Matches(@"(?m)^\s*_host\.Services\.GetRequiredService<Sync\.SyncExecutor>\(\),\s*\n\s*_sortMemory\);", shell);
    }

    [Fact]
    public void TasksPage_ReadsBeforeFirstLoad_AndSavesOnClick()
    {
        PageWiresMemory("TasksPage.cs", "Tasks", "LoadTasks");
    }

    [Fact]
    public void ProjectsPage_ReadsBeforeFirstLoad_AndSavesOnClick()
    {
        PageWiresMemory("ProjectsPage.cs", "Profiles", "LoadProfiles");
    }

    [Fact]
    public void Pages_DoNotTouchTheMemoryFileOrSpellOutPageKeys()
    {
        // 记忆文件只经壳那一处出厂的 ListSortMemory 读写；页键取共享常量，两页不就地拼字面。
        foreach (var name in new[] { "TasksPage.cs", "ProjectsPage.cs" })
        {
            var page = Repo.Source("Desktop", "Native", name);
            Assert.DoesNotContain("desktop-list-sort-native.json", page);
            Assert.DoesNotContain("\"tasks\"", page);
            Assert.DoesNotContain("\"profiles\"", page);
        }
    }

    [Fact]
    public void SharedSource_IsCompiledIntoTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\ListSortMemory.cs", csproj);
    }

    [Fact]
    public void DesignDoc_DeclaresCrossLaunchSortRule()
    {
        var section = Repo.DesignSection(6);
        // 本轮条款的特有字面：条款引导（含口径类名）、「按列标题记忆」与「不记筛选关键词」这条边界。
        // §6 别处（列表筛选行）也提到 ListSortMemory，所以断言取的是本轮那句的引导字面——
        // 把条款改写成「不跨启动记忆」或整段删掉都算缺失。
        Assert.Contains("跨启动记忆（`ListSortMemory`", section);
        Assert.Contains("列标题", section);
        Assert.Contains("不记筛选关键词", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("列表列排序", ledger);
        Assert.Contains("ListSortMemory", ledger);
        Assert.Contains("第五十五轮", ledger);
    }

    /// <summary>
    /// 一页的接线检查：构造期按本页标题读取记忆（在任何加载方法之前）、点击列头后先记录再重建，
    /// 且记忆只在读与写两处触达。
    /// </summary>
    private static void PageWiresMemory(string file, string pageKey, string loadMethod)
    {
        var page = Repo.Source("Desktop", "Native", file);
        Assert.Matches($@"(?m)^\s*\(_sortColumn, _sortDescending\) = _sortMemory\.Read\(ListSortMemoryKeys\.{pageKey}, _columnTitles\);", page);
        Assert.Matches($@"(?m)^\s*_sortMemory\.Save\(ListSortMemoryKeys\.{pageKey}, _columnTitles, _sortColumn, _sortDescending\);", page);
        // 读取在建列快照之后：标题数组还没取到时读到的是空集，任何记忆都会被判成认不出。
        var snapshot = page.IndexOf("_columnTitles = _list.Columns", StringComparison.Ordinal);
        var read = page.IndexOf($"_sortMemory.Read(ListSortMemoryKeys.{pageKey}", StringComparison.Ordinal);
        Assert.True(snapshot >= 0 && read > snapshot, $"{file}:读取记忆的行应在建列快照之后。");
        // 构造期读取：必须排在加载方法定义之前，否则第一次加载已按默认顺序呈现过了。
        var load = page.IndexOf($"private void {loadMethod}()", StringComparison.Ordinal);
        Assert.True(load > read, $"{file}:读取记忆应在加载方法之前（构造期）接线。");
        // 记录紧挨在重建之前：先落状态再刷新，表头标记与顺序来自同一一次点击。
        Assert.Matches($@"(?m)^\s*_sortMemory\.Save\(ListSortMemoryKeys\.{pageKey}, _columnTitles, _sortColumn, _sortDescending\);\s*\n\s*{loadMethod}\(\);", page);
        Assert.Equal(2, Regex.Matches(page, @"_sortMemory\.").Count);
    }
}
