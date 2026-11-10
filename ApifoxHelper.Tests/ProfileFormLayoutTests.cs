using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 配置编辑对话框的表单口径断言（第一百四十九轮）。
/// <para>
/// 缺陷本体：新建配置对话框在真机截图里是「名称、仓库根目录之后一大段空白，四个高级按钮彼此间距极大，
/// 右侧还出滚动条」，用户原话「很抽象、没法用」。根因不是审美——空标签格里放的是默认尺寸的
/// <c>Panel</c>（200×100），七行「没有标签」的行把表单撑出 700px 死空白；四个高级入口各占一整行；
/// 「读取项目列表」在未保存时是启用的，点下去只得到一句「请先保存配置」。
/// </para>
/// <para>
/// 本文件把「一行该多高、哪些行属于首用路径、高级入口收在哪」翻译成可执行断言：
/// 口径 <see cref="ProfileFormLayout"/> 经共享源码编入测试程序集，这里跑的是主工程同一份代码；
/// 界面接线（分区标题、行容器、按钮门控）以源码切片与行首正则钉住。
/// 真实行高与控件位置由冒烟器在 wine 下量（<c>RunProfileFormLayout</c>），本文件不冒充那一层证据。
/// </para>
/// </summary>
public class ProfileFormLayoutTests
{
    private static string ProjectsPage => Repo.Source("Desktop", "Native", "ProjectsPage.cs");

    // ── 行高口径 ────────────────────────────────────────────────────────

    [Fact]
    public void MaxRowHeight_IsBodyLinePlusRowPadding()
    {
        // 正文行高 17（wine 实测 10.5pt 的行高）+ 行内留白 24 = 41：超过它的行说明那一行里
        // 混进了不该有的东西（默认尺寸的占位面板、多行说明、被拉高的容器）。
        Assert.Equal(17 + ProfileFormLayout.RowPadding, ProfileFormLayout.MaxRowHeight(17));
        Assert.Equal(41, ProfileFormLayout.MaxRowHeight(17));
        // 行内留白取 DESIGN.md §4 的 Xs + Sm + Md = 4 + 8 + 12，不是随手挑的数。
        Assert.Equal(4 + 8 + 12, ProfileFormLayout.RowPadding);
    }

    [Fact]
    public void SpacerSize_IsZero()
    {
        // 空标签格的占位必须是零尺寸：此前放的是默认尺寸的 Panel（200×100），行高因此被撑到 100px。
        Assert.Equal(0, ProfileFormLayout.SpacerSize);
    }

    [Fact]
    public void AddRow_DoesNotAddDefaultSizedSpacer()
    {
        // 没有标签的行不再放占位控件（放了就是 200×100 的死空白）。
        var body = MethodBody("private static void AddRow(TableLayoutPanel grid, string label, Control field)");
        Assert.DoesNotMatch(@"new Panel\s*\{", body);
        Assert.Contains("if (label.Length > 0)", body);
    }

    // ── 首用路径：必填三项连续成组 ──────────────────────────────────────

    [Fact]
    public void CoreFields_AreTheThreeNeededToCreate()
    {
        Assert.Equal(["名称", "仓库根目录", "API 项目"], ProfileFormLayout.CoreFields);
        // 必填与可选不重叠：同一字段不能既说「填完就能保存」又说「可保存后再补」。
        Assert.Empty(ProfileFormLayout.CoreFields.Intersect(ProfileFormLayout.OptionalFields));
    }

    [Fact]
    public void CoreFields_AreAddedConsecutivelyInTheGrid()
    {
        // 三个必填字段在网格里连续成组（中间不夹说明行、不夹可选字段），首用才是一条能走完的路。
        var rows = ProfileFormLayout.CoreFields
            .Select(field => Regex.Match(ProjectsPage, $@"AddRow\(grid, ""{Regex.Escape(field)}"""))
            .ToArray();
        Assert.All(rows, match => Assert.True(match.Success, "必填字段没有经 AddRow 加进网格。"));
        var order = rows.Select(match => match.Index).ToArray();
        Assert.Equal(order.OrderBy(index => index), order);
        // 三个 AddRow 之间不得夹别的 AddRow/AddSection（连续成组）。
        var between = ProjectsPage[order[0]..order[^1]];
        Assert.Equal(2, Regex.Matches(between, @"(?m)^\s*AddRow\(grid,").Count);
        Assert.DoesNotMatch(@"(?m)^\s*AddSection\(grid,", between);
    }

    [Fact]
    public void Sections_AreInFirstUseOrder()
    {
        // 分区顺序：必填 → 可选设置 → 高级设置。高级设置排在最后，不占首用视线。
        var core = ProjectsPage.IndexOf("AddSection(grid, ProfileFormLayout.CoreSectionTitle)", StringComparison.Ordinal);
        var optional = ProjectsPage.IndexOf("AddSection(grid, ProfileFormLayout.OptionalSectionTitle)", StringComparison.Ordinal);
        var advanced = ProjectsPage.IndexOf("AddSection(grid, ProfileFormLayout.AdvancedSectionTitle)", StringComparison.Ordinal);
        Assert.True(core >= 0 && optional > core && advanced > optional,
            $"分区顺序不对：必填 {core}、可选 {optional}、高级 {advanced}。");
        // 分区标题文案本身要说得清「可保存后再补」，而不是让用户猜。
        Assert.Contains("可保存后再补", ProfileFormLayout.OptionalSectionTitle);
        Assert.Contains("可选", ProfileFormLayout.AdvancedSectionTitle);
    }

    // ── 高级入口：四个收在同一行 ────────────────────────────────────────

    [Fact]
    public void AdvancedEntries_AreTheFourSubDialogs()
    {
        Assert.Equal(4, ProfileFormLayout.AdvancedEntries.Length);
        // 每个入口的文字都在界面里以 Ghost 按钮出现（文案单一落点：界面不另写字面）。
        foreach (var entry in ProfileFormLayout.AdvancedEntries)
        {
            Assert.Contains($"Ui.Ghost(\"{entry}\")", ProjectsPage);
        }
    }

    [Fact]
    public void AdvancedEntries_ShareOneRowContainer()
    {
        // 四个入口经同一个容器加进网格（AdvancedEntries(...)），不再各占一整行。
        var match = Regex.Match(ProjectsPage, @"AddWide\(grid, AdvancedEntries\(([^)]*)\)\)");
        Assert.True(match.Success, "四个高级入口没有收进同一个行容器。");
        var names = match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
        Assert.Equal(4, names.Length);
        // 间距上限取 DESIGN.md §4 的 Sm = 8：此前四个入口各占一行、彼此隔着 100px 的空白。
        Assert.Equal(8, ProfileFormLayout.AdvancedEntryGap);
    }

    // ── 「读取项目列表」不是死胡同 ──────────────────────────────────────

    [Fact]
    public void ProjectList_IsGatedAndExplained()
    {
        // 未保存时停用（不是点了才报错），已保存时可用。
        Assert.False(ProfileFormLayout.ProjectListEnabled(saved: false));
        Assert.True(ProfileFormLayout.ProjectListEnabled(saved: true));
        // 两态各一句，界面不就地拼字。
        Assert.NotEqual(ProfileFormLayout.ProjectListHint(false), ProfileFormLayout.ProjectListHint(true));
        Assert.Equal(ProfileFormLayout.ProjectListHintUnsaved, ProfileFormLayout.ProjectListHint(false));
        Assert.Equal(ProfileFormLayout.ProjectListHintSaved, ProfileFormLayout.ProjectListHint(true));
        // 未保存那一句要讲清下一步（保存后可读 / 也可以直接填 ID），而不是「请先保存配置」这种循环。
        Assert.Contains("保存后", ProfileFormLayout.ProjectListHintUnsaved);
        Assert.Contains("直接填写", ProfileFormLayout.ProjectListHintUnsaved);
    }

    [Fact]
    public void ProjectListButton_StartsDisabledForNewProfile()
    {
        // 新建时按钮构造即停用；读完之后回到门控值，而不是无条件启用。
        Assert.Contains("_loadProjectsButton.Enabled = ProfileFormLayout.ProjectListEnabled(_editing is not null);", ProjectsPage);
        Assert.DoesNotMatch(@"(?m)^\s*if \(!IsDisposed\) _loadProjectsButton\.Enabled = true;", ProjectsPage);
        // 说明行取同一份文案（界面不另写一句）。
        Assert.Contains("Ui.Caption(ProfileFormLayout.ProjectListHint(_editing is not null))", ProjectsPage);
    }

    // ── 保存/取消随时可点 ──────────────────────────────────────────────

    [Fact]
    public void SaveAndCancel_AreInAFixedBottomBar()
    {
        // 底栏停靠底部、在滚动宿主之外：滚动时也点得到，不必在空白和高级区里找。
        var body = MethodBody("public ProfileDialog(ProfileStore profiles, ApifoxCli cli, CredentialStore credentials, ProjectProfile? editing)");
        Assert.Contains("Dock = DockStyle.Bottom", body);
        Assert.Contains("buttons.Controls.Add(saveButton);", body);
        Assert.Contains("buttons.Controls.Add(cancelButton);", body);
        // 加入顺序：滚动宿主先加、底栏与状态行后加——停靠按集合倒序生效，底栏才拿得到自己的高度。
        var scroll = body.IndexOf("Controls.Add(gridScroll);", StringComparison.Ordinal);
        var bar = body.IndexOf("Controls.Add(buttons);", StringComparison.Ordinal);
        Assert.True(scroll >= 0 && bar > scroll, "底栏必须在滚动宿主之后加入，否则会被滚动区盖住。");
    }

    /// <summary>取 <c>ProjectsPage.cs</c> 里某个方法/构造函数的正文（到下一个同缩进的成员声明为止）。</summary>
    private static string MethodBody(string signature)
    {
        var start = ProjectsPage.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"ProjectsPage.cs 里找不到「{signature}」。");
        var rest = ProjectsPage[start..];
        var end = rest.IndexOf("\n    private ", 1, StringComparison.Ordinal);
        var end2 = rest.IndexOf("\n    public ", 1, StringComparison.Ordinal);
        var candidates = new[] { end, end2 }.Where(index => index > 0).ToArray();
        return candidates.Length == 0 ? rest : rest[..candidates.Min()];
    }
}
