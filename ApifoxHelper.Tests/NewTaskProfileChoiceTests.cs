using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「新建任务」对话框里「项目配置」下拉默认选中哪一份（第四百零八轮，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：这个下拉此前一律 <c>SelectedIndex = 0</c>，而 <c>ProfileStore.List()</c> 是按
/// <b>配置名称</b>排的（不是按创建时间）。首程那条路上用户刚在项目设置页建好（或刚改好）一份配置、
/// 回到任务页点「新建任务」，只要清单里还有一份名字排在它前面的配置，对话框默认选中的就是<b>别人</b>——
/// 用户照着默认值点「创建并扫描」，任务就绑到了另一份配置上：扫的是另一个仓库、写的是另一个 Apifox 项目。
/// 这不是观感问题，是<b>默认值错了，一次回车就把任务建到错的项目上</b>。
/// </para>
/// <para>
/// <b>这一族判什么</b>：① 判定本身（<see cref="NewTaskProfileChoice.Index"/> 的四种入参逐格对账，真执行）；
/// ② 三处接线各在它该在的地方（配置页只在真的保存成功之后发那一笔、壳把那一笔交给任务页、
/// 任务页把那一笔带进对话框，且对话框不再自己写 <c>SelectedIndex = 0</c>）；
/// ③ 判据自己被抓得住（拿注入的坏源码喂一遍，每一发都必须报出自己那一处）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「清单该按什么排」（那是 <c>ProfileStore.List</c> 的事，本切片不动它）；
/// 不判真机上这个下拉弹开之后键盘落在哪、滚到哪（随 G-D5 真机复核）。「点下去真的选中了那一份」
/// 那半截由 <c>ApifoxHelper.Smoke</c> 的「新建任务对话框的「项目配置」默认选中项」那一段
/// 在 wine 里用真实控件跑（模拟器，不是真机）。
/// </para>
/// </summary>
public class NewTaskProfileChoiceTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");
    private static string ProjectsPage => Repo.Source("Desktop", "Native", "ProjectsPage.cs");
    private static string Shell => Repo.Source("Desktop", "Native", "NativeShellForm.cs");

    // ---------- ① 判定：四种入参逐格对账 ----------

    [Fact]
    public void Index_PicksTheJustSavedProfile_WhenItIsStillInTheList()
    {
        // 清单按名称排：刚保存的那一份排在第三位（名字排在别人后面），默认值就该是第三项。
        string[] ids = ["p-a", "p-b", "p-c"];
        Assert.Equal(2, NewTaskProfileChoice.Index(ids, "p-c"));
        // 排在第一位时也是它（不是「碰巧等于 0」）。
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, "p-a"));
        // 中间那一份。
        Assert.Equal(1, NewTaskProfileChoice.Index(ids, "p-b"));
    }

    [Fact]
    public void Index_FallsBackToTheFirstItem_WhenItCannotRecognizeThePreferredOne()
    {
        string[] ids = ["p-a", "p-b"];
        // 这一次启动还没保存过任何配置。
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, null));
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, ""));
        // 指向一份已经被删掉的配置：回退到第一项——就是这一屏此前的行为（认不出时不变差）。
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, "p-gone"));
        // 空白 Id 不是「一份配置」：同样回退。
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, "   "));
    }

    [Fact]
    public void Index_IsNone_WhenTheListIsEmpty()
    {
        // 清单是空的：没有可选项，调用方据此不设 SelectedIndex（设 0 会抛，设 -1 才是「不选」）。
        Assert.Equal(NewTaskProfileChoice.None, NewTaskProfileChoice.Index([], "p-a"));
        Assert.Equal(NewTaskProfileChoice.None, NewTaskProfileChoice.Index([], null));
        Assert.Equal(-1, NewTaskProfileChoice.None);
    }

    [Fact]
    public void Index_ComparesIdsOrdinally()
    {
        // 配置 Id 是落盘文件名，不是给人读的文字：大小写与文化差异都不该让「同一份配置」认成两份。
        string[] ids = ["P-1", "p-2"];
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, "P-1"));
        // 大小写不同 = 认不出（回退到第一项），而不是「按当前文化算相等」。
        Assert.Equal(0, NewTaskProfileChoice.Index(ids, "p-1"));
        // 认得出与认不出在「第一项恰好是它」时读数相同，所以再拿一份清单把两者分开：
        string[] other = ["p-2", "P-1"];
        Assert.Equal(1, NewTaskProfileChoice.Index(other, "P-1"));
        Assert.Equal(0, NewTaskProfileChoice.Index(other, "p-1"));
    }

    // ---------- ② 接线：三处各在它该在的地方 ----------

    [Fact]
    public void TasksPage_DoesNotHardcodeTheFirstItemAnyMore()
    {
        var code = CodeScan.Text(TasksPage);
        // 判定收在 NewTaskProfileChoice 一处：对话框只落它的答案。
        Assert.Contains("NewTaskProfileChoice.Index(", code);
        Assert.Contains("if (index != NewTaskProfileChoice.None) _profile.SelectedIndex = index;", code);
        // 此前那一句「一律选中第一项」不许复活——它正是这一轮修掉的那一处。
        Assert.DoesNotContain("_profile.SelectedIndex = 0", code);
        // 那一笔由壳交过来（本页不读另一页的选中行、也不缓存配置清单）。
        Assert.Contains("public string? PreferredProfileId { get; set; }", code);
        Assert.Contains("new NewTaskDialog(_profiles, PreferredProfileId)", code);
    }

    [Fact]
    public void ProjectsPage_AnnouncesTheSavedProfile_OnlyAfterASuccessfulSave()
    {
        var code = CodeScan.Text(ProjectsPage);
        Assert.Contains("public event Action<string>? ProfileSaved;", code);
        // 只在「对话框以确定结束、且真的拿到了保存结果」那一支里发：取消与校验失败时没有「刚保存的那一份」。
        Assert.Contains("if (dialog.Saved is { } saved)", code);
        Assert.Contains("ProfileSaved?.Invoke(saved.Id);", code);
        // 发出去的那一笔与「本页选中那一行」是同一个 Id（两处不许各取一次）。
        Assert.Contains("_selectedId = saved.Id;", code);
        // 本页不引用另一页：不出现任务页的类型名。
        Assert.DoesNotContain("TasksPage", code);
    }

    [Fact]
    public void Shell_WiresTheSavedProfileToTheTasksPage()
    {
        var code = CodeScan.Text(Shell);
        // 请求由页发出、落点由壳接：与「打开配置」「去建配置」两跳同一纪律。
        Assert.Contains("_projectsPage.ProfileSaved += profileId => _tasksPage.PreferredProfileId = profileId;", code);
    }

    [Fact]
    public void WiringDetector_ReportsTheBreaksItMustCatch()
    {
        // 夹具：三份合规片段（与产品源码同一形状），与四发注入（每一发只坏一处）。
        const string tasks = """
            public string? PreferredProfileId { get; set; }
            using var dialog = new NewTaskDialog(_profiles, PreferredProfileId);
            var index = NewTaskProfileChoice.Index(available.Select(p => p.Id).ToList(), preferredProfileId);
            if (index != NewTaskProfileChoice.None) _profile.SelectedIndex = index;
            """;
        const string projects = """
            public event Action<string>? ProfileSaved;
            if (dialog.Saved is { } saved)
            {
                _selectedId = saved.Id;
                ProfileSaved?.Invoke(saved.Id);
            }
            """;
        const string shell = """
            _projectsPage.ProfileSaved += profileId => _tasksPage.PreferredProfileId = profileId;
            """;
        Assert.Empty(WiringProblems(tasks, projects, shell));

        // ① 对话框退回「一律选中第一项」。
        Assert.Contains(WiringProblems(
                tasks.Replace("if (index != NewTaskProfileChoice.None) _profile.SelectedIndex = index;", "_profile.SelectedIndex = 0;"),
                projects, shell),
            p => p.Contains("SelectedIndex = 0", StringComparison.Ordinal));

        // ② 配置页在保存失败那一支也发（取消/校验失败时没有「刚保存的那一份」）。
        Assert.Contains(WiringProblems(
                tasks,
                projects.Replace("if (dialog.Saved is { } saved)", "ProfileSaved?.Invoke(\"x\");\n    if (dialog.Saved is { } saved)"),
                shell),
            p => p.Contains("保存成功", StringComparison.Ordinal));

        // ③ 壳没有把那一笔交给任务页（页发了请求，落点没人接）。
        Assert.Contains(WiringProblems(tasks, projects, ""),
            p => p.Contains("壳", StringComparison.Ordinal));

        // ④ 任务页自己又判了一次（不落 NewTaskProfileChoice 的答案）：判定那一行整条撤掉。
        Assert.Contains(WiringProblems(
                tasks.Replace(
                    "var index = NewTaskProfileChoice.Index(available.Select(p => p.Id).ToList(), preferredProfileId);\n"
                    + "if (index != NewTaskProfileChoice.None) _profile.SelectedIndex = index;",
                    "_profile.SelectedIndex = 0;"),
                projects, shell),
            p => p.Contains("NewTaskProfileChoice", StringComparison.Ordinal));
    }

    /// <summary>
    /// 拿三份源码文本判「这一笔接线是不是三处都在」，返回每一处不符（空 = 对得上）。
    /// <para>
    /// 与 <c>FirstRunPlanTests.ProjectsHintProblems</c> 同一形状：判据写成「文本进、问题出」，
    /// 上面那条注入自验才喂得进坏源码。
    /// </para>
    /// </summary>
    private static List<string> WiringProblems(string tasks, string projects, string shell)
    {
        tasks = CodeScan.Text(tasks);
        projects = CodeScan.Text(projects);
        shell = CodeScan.Text(shell);
        var problems = new List<string>();

        if (tasks.Contains("_profile.SelectedIndex = 0", StringComparison.Ordinal))
        {
            problems.Add("任务页对话框又写死了 SelectedIndex = 0：默认值回到「按名字排第一的那一份」。");
        }
        if (!tasks.Contains("NewTaskProfileChoice.Index(", StringComparison.Ordinal))
        {
            problems.Add("任务页没有落 NewTaskProfileChoice 的答案：默认选中项又变成界面自己判的事。");
        }
        if (!tasks.Contains("PreferredProfileId", StringComparison.Ordinal))
        {
            problems.Add("任务页没有 PreferredProfileId 这一笔：刚保存的那一份带不进对话框。");
        }

        if (!projects.Contains("public event Action<string>? ProfileSaved;", StringComparison.Ordinal))
        {
            problems.Add("配置页没有 ProfileSaved 这一笔：保存成功之后没人把那一份交出去。");
        }
        if (!projects.Contains("ProfileSaved?.Invoke(saved.Id);", StringComparison.Ordinal))
        {
            problems.Add("配置页没有在保存成功之后发那一笔：任务页的默认值永远认不出刚保存的那一份。");
        }
        // 发出去的那一笔必须在「真的保存成功」那一支里：取消与校验失败时没有「刚保存的那一份」。
        // 判的是**第一处** Invoke 的位置（不是「有没有一处合规的」）：在保存成功那一支之前先发一次，
        // 取消与校验失败也会让任务页的默认值指向一份没落盘的配置。
        var savedBranch = projects.IndexOf("if (dialog.Saved is { } saved)", StringComparison.Ordinal);
        var invoke = projects.IndexOf("ProfileSaved?.Invoke(", StringComparison.Ordinal);
        if (savedBranch < 0 || invoke < 0 || invoke < savedBranch)
        {
            problems.Add("配置页那一笔不是在「保存成功」那一支里发的：取消或校验失败也会让默认值指向一份没落盘的配置。");
        }

        if (!shell.Contains("_projectsPage.ProfileSaved += profileId => _tasksPage.PreferredProfileId = profileId;", StringComparison.Ordinal))
        {
            problems.Add("壳没有把配置页那一笔接到任务页：请求由页发出、落点没人接。");
        }

        return problems;
    }
}
