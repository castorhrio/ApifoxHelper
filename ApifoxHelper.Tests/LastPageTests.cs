using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 壳启动回到上次使用页面的口径断言（LastPage，共享源码单一口径）：
/// 会话型工具的窗口位置记忆（WindowPlacement）同族——只记页面键，不记业务数据；
/// 无记录、内容损坏或值不认识时回退项目设置页（首次启动的缺省），保存失败不影响切换。
/// 壳的恢复与记录接线由行首正则机械检查钉住——口径存在但壳没调用等于没有。
/// </summary>
public class LastPageTests : IDisposable
{
    private readonly string _root;
    private readonly string _path;

    public LastPageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-lastpagetests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "desktop-page-native.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    // ── LastPage：读取与记录 ─────────────────────────────────────────

    [Fact]
    public void MissingFile_FallsBackToProjects()
    {
        var lastPage = new LastPage(_path);
        Assert.Equal(LastPageKeys.Projects, lastPage.Read());
    }

    [Theory]
    [InlineData(LastPageKeys.Projects)]
    [InlineData(LastPageKeys.Tasks)]
    public void SaveThenRead_RoundTrips(string page)
    {
        var lastPage = new LastPage(_path);
        lastPage.Save(page);
        Assert.Equal(page, lastPage.Read());
    }

    [Fact]
    public void UnknownValue_FallsBackToProjects()
    {
        File.WriteAllText(_path, """{ "page": "settings" }""");
        Assert.Equal(LastPageKeys.Projects, new LastPage(_path).Read());
    }

    [Fact]
    public void CorruptJson_FallsBackToProjects()
    {
        File.WriteAllText(_path, "{ not json");
        Assert.Equal(LastPageKeys.Projects, new LastPage(_path).Read());
    }

    [Fact]
    public void Save_ForeignKey_IsIgnored()
    {
        var lastPage = new LastPage(_path);
        lastPage.Save("help");
        Assert.False(File.Exists(_path), "非法页面键不应落盘。");
    }

    [Fact]
    public void Save_ForeignKey_DoesNotClobberSavedValue()
    {
        var lastPage = new LastPage(_path);
        lastPage.Save(LastPageKeys.Tasks);
        lastPage.Save("help");
        Assert.Equal(LastPageKeys.Tasks, lastPage.Read());
    }

    // ── 壳接线机械检查 ────────────────────────────────────────────────

    [Fact]
    public void Shell_RestoresOnLoad_AndRecordsOnSwitch()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 字段经共享口径出厂，落点与窗口位置记忆同目录同族。
        Assert.Matches(new Regex(@"^.*new LastPage\(Path\.Combine\(host\.Store\.Root, ""desktop-page-native\.json""\)\);", RegexOptions.Multiline), shell);
        // 启动恢复：按读取值分支，任务页分支显式指向任务页与对应导航按钮。
        Assert.Matches(new Regex(@"^.*if \(_lastPage\.Read\(\) == LastPageKeys\.Tasks\) ShowPage\(_tasksPage, _navTasks\);", RegexOptions.Multiline), shell);
        Assert.Contains("else ShowPage(_projectsPage, _navProjects);", shell);
        // 切换记录：ShowPage 内经唯一入口保存，键由导航按钮推导。
        Assert.Matches(new Regex(@"^.*_lastPage\.Save\(ReferenceEquals\(nav, _navTasks\) \? LastPageKeys\.Tasks : LastPageKeys\.Projects\);", RegexOptions.Multiline), shell);
    }

    [Fact]
    public void Shell_OnlyLastPageWritesPageStore()
    {
        // 页面记忆文件只经 LastPage 一处读写，壳内不出现第二个写入口。
        Assert.DoesNotContain("desktop-page-native.json", Repo.Source("Desktop", "Native", "TasksPage.cs"));
        Assert.DoesNotContain("desktop-page-native.json", Repo.Source("Desktop", "Native", "ProjectsPage.cs"));
    }
}
