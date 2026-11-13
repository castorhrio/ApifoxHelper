using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「从任务详情跳到它绑定的配置」这一跳（第二百六十三轮，具名 pass：clarify + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：任务详情顶部那行元信息写着「配置 X · Apifox Y · 在线 · 版本备注 Z」
/// （<see cref="TaskSummary.MetaText"/>），而这一行是<b>只读文字</b>——用户读到「这个任务绑在哪个配置上」，
/// 想改那个配置（换 Swagger 来源、改允许回写范围、补模块前缀）时，界面上没有任何一步能到那里：
/// 只能自己按 Ctrl+1 切到「项目设置」页，再在清单里按名字找那一行。配置一多，这一步就是
/// 「先记住名字、再在另一页找回来」，而任务详情已经把那个名字摆在眼前了。
/// </para>
/// <para>
/// <b>这一族判什么</b>：① 三件事的唯一落点（按钮文字 / 有没有落点 / 找不到时说什么）都在
/// <see cref="ProfileJump"/>，界面层不就地拼；② 三处接线各在它该在的地方（任务详情摆按钮并发请求、
/// 壳把请求接到另一页、配置页按 Id 选中那一行）；③ 按钮的可用状态只有一个落点（详情区动作的单一门控）；
/// ④ 判据自己被抓得住（注入各报一处）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「该不该跳」——那是产品判断；也不判真机上切页之后那一行滚到哪、键盘落在哪
/// （随 G-D5 真机复核）。本仓没有 Windows 运行时，这一跳的<b>点击</b>本身在 Linux 上跑不起来，
/// 所以这里判的是「三处接线都在、且都走同一份口径」，不是「点下去真的换了页」。
/// </para>
/// </summary>
public class ProfileJumpTests
{
    private static string TasksPage => Repo.Source("Desktop", "Native", "TasksPage.cs");
    private static string ProjectsPage => Repo.Source("Desktop", "Native", "ProjectsPage.cs");
    private static string Shell => Repo.Source("Desktop", "Native", "NativeShellForm.cs");
    private static string JumpSource => Repo.Source("Desktop", "Native", "ProfileJump.cs");

    // ── 口径本身 ─────────────────────────────────────────────────────

    /// <summary>
    /// 按钮文字：非空、不带句末标点、不点名页面（它是一颗按钮，不是一句指路文案）。
    /// </summary>
    [Fact]
    public void ButtonLabel_IsAShortActionWithoutPunctuation()
    {
        Assert.False(string.IsNullOrWhiteSpace(ProfileJump.ButtonLabel));
        Assert.Equal(ProfileJump.ButtonLabel, ProfileJump.ButtonLabel.Trim());
        Assert.DoesNotContain("。", ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("「", ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(PageNames.Projects, ProfileJump.ButtonLabel, StringComparison.Ordinal);
        Assert.DoesNotContain(PageNames.Tasks, ProfileJump.ButtonLabel, StringComparison.Ordinal);
    }

    /// <summary>
    /// 有没有落点：配置 Id 为空或全空白时没有——认不出该跳到哪一份配置时不跳，也不拿清单第一行顶替。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\t\n", false)]
    [InlineData("p-1", true)]
    [InlineData("  p-1  ", true)]
    public void CanJump_OnlyWhenTheProfileIdIsKnown(string? profileId, bool expected) =>
        Assert.Equal(expected, ProfileJump.CanJump(profileId));

    /// <summary>
    /// 找不到那一行时说的那句话：点名是哪一个配置、说清发生了什么、给出下一步，且页面名取自
    /// <see cref="PageNames"/>（改一处不改另一处就会指着一个不存在的页面——第二百五十七轮那一族）。
    /// </summary>
    [Fact]
    public void MissingStatus_NamesTheProfileAndTheNextStep()
    {
        var text = ProfileJump.MissingStatus("示例配置");

        Assert.Contains("示例配置", text, StringComparison.Ordinal);
        Assert.Contains("已不存在", text, StringComparison.Ordinal);
        Assert.Contains(PageNames.Projects, text, StringComparison.Ordinal);
        // 下一步要可执行：说清「在哪一页做什么」，不是一句「请检查配置」。
        Assert.Contains("新建配置", text, StringComparison.Ordinal);
        Assert.EndsWith("。", text, StringComparison.Ordinal);
        // 名字换了，那句话跟着换——不是一句写死的文案。
        Assert.NotEqual(text, ProfileJump.MissingStatus("另一个配置"));
    }

    /// <summary>
    /// 页面名不许在 <see cref="ProfileJump"/> 里写成第二份字面量：那一句里的「项目设置」必须来自
    /// <see cref="PageNames.Projects"/>。判法是「源码里不许出现带引号的页面名」——改名字时这一条会红，
    /// 而只改 <see cref="PageNames"/> 不改这里时，上面那条断言仍然绿（它比的是同一个常量）。
    /// </summary>
    [Fact]
    public void PageName_ComesFromTheSharedSource()
    {
        Assert.Contains("PageNames.Projects", JumpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + PageNames.Projects + "\"", JumpSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"" + PageNames.Tasks + "\"", JumpSource, StringComparison.Ordinal);
    }

    // ── 三处接线 ─────────────────────────────────────────────────────

    /// <summary>
    /// 任务详情：按钮文字取自共享口径、摆进详情区、点击发请求，且「有没有落点」由共享口径判。
    /// </summary>
    [Fact]
    public void TaskDetail_ShowsTheButtonAndRaisesTheRequest()
    {
        Assert.Contains("Ui.Ghost(ProfileJump.ButtonLabel)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("AddDetailRow(grid, _openProfileButton)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("_openProfileButton.Click += (_, _) => RequestOpenProfile();", TasksPage, StringComparison.Ordinal);
        Assert.Contains("ProfileJump.CanJump(task.ProfileSnapshot.Id)", TasksPage, StringComparison.Ordinal);
        Assert.Contains("OpenProfileRequested?.Invoke(task.ProfileSnapshot.Id, task.ProfileSnapshot.Name)", TasksPage, StringComparison.Ordinal);
        // 本页不自己切页：页面之间不互相引用（切页只在壳那一处）。
        Assert.DoesNotContain("ShowProjects()", TasksPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ProjectsPage", TasksPage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 按钮的可用状态只有一个落点（详情区动作的单一门控）：<c>_openProfileButton.Enabled</c> 在整份源码里
    /// 只出现一次。第二处赋值会让「没有选中任务时入口仍可点」这类不一致重新长出来。
    /// </summary>
    [Fact]
    public void ButtonEnablement_HasASingleLandingPoint()
    {
        var hits = Regex.Matches(TasksPage, @"_openProfileButton\.Enabled\s*=").Count;
        Assert.Equal(1, hits);
        // 那一处必须在 RefreshDetailActions 里（与其余详情区入口同一份门控），不在别处。
        var body = BodyOf(TasksPage, "private void RefreshDetailActions(JobInfo? job)");
        Assert.Contains("_openProfileButton.Enabled", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 壳：把任务详情的请求接到配置页的落点上——先切页（切页会重读清单），再按 Id 选中那一行。
    /// </summary>
    [Fact]
    public void Shell_WiresTheRequestToTheOtherPage()
    {
        Assert.Contains("_tasksPage.OpenProfileRequested += (profileId, profileName) =>", Shell, StringComparison.Ordinal);
        var body = BodyOf(Shell, "_tasksPage.OpenProfileRequested += (profileId, profileName) =>");
        Assert.Contains("ShowProjects();", body, StringComparison.Ordinal);
        Assert.Contains("_projectsPage.RevealProfile(profileId, profileName);", body, StringComparison.Ordinal);
        // 顺序是判据：先切页再选中——反过来会在切页重读清单时把选中冲掉。
        Assert.True(
            body.IndexOf("ShowProjects();", StringComparison.Ordinal) < body.IndexOf("RevealProfile(", StringComparison.Ordinal),
            "接线里必须先切页再选中那一行。");
    }

    /// <summary>
    /// 配置页的落点：认不认得出该跳哪一份由共享口径判、选中与滚动走 <see cref="Ui.SelectItem"/> 唯一入口、
    /// 找不到时那句话取自共享口径（不就地拼）。
    /// </summary>
    [Fact]
    public void ProjectsPage_RevealsTheRowOrSaysWhyItCannot()
    {
        var body = BodyOf(ProjectsPage, "public bool RevealProfile(string profileId, string profileName)");
        Assert.Contains("ProfileJump.CanJump(profileId)", body, StringComparison.Ordinal);
        Assert.Contains("Ui.SelectItem(item)", body, StringComparison.Ordinal);
        Assert.Contains("ProfileJump.MissingStatus(profileName)", body, StringComparison.Ordinal);
        // 只换选中与滚动：不打开编辑对话框、不写配置数据。
        Assert.DoesNotContain("ShowDialog(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("_profiles.Save", body, StringComparison.Ordinal);
        // 筛选把那一行筛掉时先清空筛选（清空走既有重建，不另写一条读取路径）。
        Assert.Contains("ListFilter.IsActive(_filter.Text)", body, StringComparison.Ordinal);
    }

    // ── 判据自己被抓得住 ──────────────────────────────────────────────

    /// <summary>
    /// 上面几条接线判据<b>自己</b>被抓得住：把每一处接线各拆一次，判据必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异）。
    /// 这里判的是纯函数 <see cref="WiringProblems"/>：文本进、问题出。
    /// </para>
    /// </summary>
    [Fact]
    public void WiringDetector_ReportsTheBreaksItMustCatch()
    {
        var good = Wiring(TasksPage, ProjectsPage, Shell);
        Assert.Empty(WiringProblems(good.Tasks, good.Projects, good.Shell));

        // ① 按钮文字就地写死（不再取共享口径）。
        var literal = good with { Tasks = good.Tasks.Replace("Ui.Ghost(ProfileJump.ButtonLabel)", "Ui.Ghost(\"打开配置\")", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(literal.Tasks, literal.Projects, literal.Shell), p => p.Contains("ButtonLabel", StringComparison.Ordinal));

        // ② 壳没接线（请求发出去没人接）。
        var unwired = good with { Shell = good.Shell.Replace("_tasksPage.OpenProfileRequested += (profileId, profileName) =>", "// 未接线", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(unwired.Tasks, unwired.Projects, unwired.Shell), p => p.Contains("OpenProfileRequested", StringComparison.Ordinal));

        // ③ 配置页就地拼那句话（不再取共享口径）。
        var inline = good with { Projects = good.Projects.Replace("ProfileJump.MissingStatus(profileName)", "\"找不到这个配置。\"", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(inline.Tasks, inline.Projects, inline.Shell), p => p.Contains("MissingStatus", StringComparison.Ordinal));

        // ④ 按钮可用状态长出第二处赋值。
        var twice = good with { Tasks = good.Tasks.Replace("_gapButton.Enabled = scanReady;", "_openProfileButton.Enabled = true;\n        _gapButton.Enabled = scanReady;", StringComparison.Ordinal) };
        Assert.Contains(WiringProblems(twice.Tasks, twice.Projects, twice.Shell), p => p.Contains("单一落点", StringComparison.Ordinal));
    }

    // ── 判据本体（纯函数：文本进、问题出） ────────────────────────────

    private sealed record WiringText(string Tasks, string Projects, string Shell);

    private static WiringText Wiring(string tasks, string projects, string shell) => new(tasks, projects, shell);

    /// <summary>
    /// 接线判据：三份源码进、问题清单出（空清单 = 过得去）。与 <c>SourceDriftDetector</c> 同一做法。
    /// </summary>
    private static List<string> WiringProblems(string tasks, string projects, string shell)
    {
        var problems = new List<string>();

        if (!tasks.Contains("Ui.Ghost(ProfileJump.ButtonLabel)", StringComparison.Ordinal))
            problems.Add("任务详情的按钮文字没有取自 ProfileJump.ButtonLabel（就地写死会让改一处不改另一处）。");
        if (!tasks.Contains("ProfileJump.CanJump(", StringComparison.Ordinal))
            problems.Add("任务详情没有用 ProfileJump.CanJump 判「这一跳有没有落点」。");
        if (!tasks.Contains("OpenProfileRequested?.Invoke(", StringComparison.Ordinal))
            problems.Add("任务详情没有发出 OpenProfileRequested 请求。");
        if (Regex.Matches(tasks, @"_openProfileButton\.Enabled\s*=").Count != 1)
            problems.Add("「打开配置」的可用状态不是单一落点（_openProfileButton.Enabled 赋值不止一处）。");

        if (!shell.Contains("_tasksPage.OpenProfileRequested +=", StringComparison.Ordinal))
            problems.Add("壳没有接线 _tasksPage.OpenProfileRequested（请求发出去没人接）。");
        if (!shell.Contains("_projectsPage.RevealProfile(", StringComparison.Ordinal))
            problems.Add("壳没有把请求接到配置页的 RevealProfile 落点上。");

        if (!projects.Contains("ProfileJump.CanJump(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.CanJump 判「该跳到哪一份配置」。");
        if (!projects.Contains("ProfileJump.MissingStatus(", StringComparison.Ordinal))
            problems.Add("配置页没有用 ProfileJump.MissingStatus 说「找不到那一行」（就地拼会让两处说法分家）。");
        if (!projects.Contains("Ui.SelectItem(", StringComparison.Ordinal))
            problems.Add("配置页没有走 Ui.SelectItem 唯一入口选中那一行。");

        return problems;
    }

    /// <summary>
    /// 取一段方法体（从签名那一行到与之配对的右花括号）。只认花括号计数，不认字符串里的花括号——
    /// 这几段方法体里没有带花括号的字面量，够用；不够用时它会「找不到闭合」而不是静默返回半段。
    /// </summary>
    private static string BodyOf(string source, string header)
    {
        var start = source.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"源码里找不到「{header}」——判据要判的那一段不在了。");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"「{header}」后面没有方法体。");
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[open..(i + 1)];
            }
        }
        Assert.Fail($"「{header}」的方法体没有闭合——判据会去判一段不是它的代码。");
        return "";
    }
}
