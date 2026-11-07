using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 原生壳键盘快捷键的口径断言（ShellShortcuts，共享源码单一口径）：
/// 手势只取 Windows 惯例（Ctrl+数字切页、F5 刷新当前页、F1 帮助/关于），
/// 不占用文本框编辑键；「退出」「打开资料目录/日志目录」不发明手势（挂账）。
/// 壳的菜单接线与页面刷新分发由源码机械检查钉住——口径存在但没人接线等于没有。
/// </summary>
public class ShellShortcutsTests
{
    [Fact]
    public void All_RegistersFourConventionalEntries()
    {
        Assert.Equal(4, ShellShortcuts.All.Count);
        Assert.Equal(new[] { "projects", "tasks", "refresh", "about" },
            ShellShortcuts.All.Select(entry => entry.Action).ToArray());
        Assert.Equal(ShellShortcuts.All.Select(entry => entry.Action).Distinct(),
            ShellShortcuts.All.Select(entry => entry.Action));
    }

    [Fact]
    public void Gestures_AreWindowsConventions()
    {
        Assert.Equal("Ctrl+1", ShellShortcuts.Projects.Gesture);
        Assert.Equal("Ctrl+2", ShellShortcuts.Tasks.Gesture);
        Assert.Equal("F5", ShellShortcuts.Refresh.Gesture);
        Assert.Equal("F1", ShellShortcuts.About.Gesture);
    }

    [Fact]
    public void GestureOf_ResolvesRegisteredActions_UnknownReturnsNull()
    {
        Assert.Equal("F5", ShellShortcuts.GestureOf("refresh"));
        Assert.Null(ShellShortcuts.GestureOf("exit"));
    }

    [Fact]
    public void Gestures_AvoidEditingKeys()
    {
        Assert.All(ShellShortcuts.All,
            entry => Assert.DoesNotContain(ShellShortcuts.ReservedEditingKeys, key => entry.Gesture == key));
    }

    // ── 接线机械检查 ──────────────────────────────────────────────────

    [Fact]
    public void Shell_BindsMenuItemsFromSharedEntries()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 快捷键只能从共享口径取（entry.Gesture），界面代码不得就地写手势。
        Assert.Matches(@"(?m)^\s*item\.ShortcutKeys = \(Keys\)new KeysConverter\(\)\.ConvertFromString\(entry\.Gesture\)!;", shell);
        Assert.Matches(@"(?m)^\s*BoundItem\(""项目设置\(&P\)"", ShellShortcuts\.Projects, ShowProjects\),", shell);
        Assert.Matches(@"(?m)^\s*BoundItem\(""检查任务\(&T\)"", ShellShortcuts\.Tasks, ShowTasks\),", shell);
        Assert.Matches(@"(?m)^\s*BoundItem\(""刷新当前页\(&R\)"", ShellShortcuts\.Refresh, RefreshVisiblePage\),", shell);
        Assert.Matches(@"(?m)^\s*help\.DropDownItems\.Add\(BoundItem\(""关于 Apifox 助手\(&A\)"", ShellShortcuts\.About, ShowAbout\)\);", shell);
    }

    [Fact]
    public void Shell_SidebarAndMenuShareOnePageDispatch()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 侧栏按钮与菜单项都经 ShowProjects/ShowTasks 单一分发，切页口径（页面 + 选中标记）不出现第二份。
        Assert.Matches(@"(?m)^\s*_navProjects\.Click \+= \(_, _\) => ShowProjects\(\);", shell);
        Assert.Matches(@"(?m)^\s*_navTasks\.Click \+= \(_, _\) => ShowTasks\(\);", shell);
        Assert.Matches(@"(?m)^\s*private void ShowProjects\(\) => ShowPage\(_projectsPage, _navProjects\);", shell);
        Assert.Matches(@"(?m)^\s*private void ShowTasks\(\) => ShowPage\(_tasksPage, _navTasks\);", shell);
        // 刷新当前页：只刷可见页，且经页面协议 RefreshData（与该页「刷新」按钮同一口径）。
        Assert.Matches(@"(?m)^\s*if \(_projectsPage is \{ Visible: true \}\) _projectsPage\.RefreshData\(\);", shell);
        Assert.Matches(@"(?m)^\s*if \(_tasksPage is \{ Visible: true \}\) _tasksPage\.RefreshData\(\);", shell);
    }

    [Fact]
    public void Pages_ImplementRefreshData_AndButtonsUseIt()
    {
        var interfaceText = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.Matches(@"(?m)^\s*void RefreshData\(\);", interfaceText);

        var projects = Repo.Source("Desktop", "Native", "ProjectsPage.cs");
        Assert.Matches(@"(?m)^\s*public void RefreshData\(\) => LoadProfiles\(\);", projects);
        Assert.Matches(@"(?m)^\s*refreshButton\.Click \+= \(_, _\) => RefreshData\(\);", projects);

        var tasks = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Matches(@"(?m)^\s*public void RefreshData\(\) => LoadTasks\(\);", tasks);
        Assert.Matches(@"(?m)^\s*refreshButton\.Click \+= \(_, _\) => RefreshData\(\);", tasks);
    }
}
