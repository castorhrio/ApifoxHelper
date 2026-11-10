using ApifoxHelper.Api;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// Apifox 目标选择（F01「读取项目列表」「读取分支」）的口径（冻结页面 loadProjects/loadBranches 与
/// 原生配置对话框同一规则，ApifoxPicker 单一落点）：行解析与 /api/apifox/projects、/api/apifox/branches
/// 路由同一字段与缺省值；已归档分支不进列表；列表项文字与冻结页面同一格式；主分支选择后分支名留空；
/// 入口门控（未保存不能读取）与项目 ID 校验文案逐字一致。
/// </summary>
public class ApifoxPickerTests
{
    // ── 行解析（与路由同一字段与缺省值） ─────────────────────────────────

    [Fact]
    public void ProjectRows_ReadsIdNameAndTeamName()
    {
        var rows = ApifoxPicker.ProjectRows(new JsonArray(
            new JsonObject { ["id"] = "913", ["name"] = "商城接口", ["teamName"] = "平台组" }));
        var row = Assert.Single(rows);
        Assert.Equal("913", row.Id);
        Assert.Equal("商城接口", row.Name);
        Assert.Equal("平台组", row.TeamName);
    }

    [Fact]
    public void ProjectRows_TeamNameFallsBackToTeamDotName()
    {
        var rows = ApifoxPicker.ProjectRows(new JsonArray(
            new JsonObject { ["id"] = "913", ["name"] = "商城接口", ["team"] = new JsonObject { ["name"] = "后端组" } }));
        Assert.Equal("后端组", Assert.Single(rows).TeamName);
    }

    [Fact]
    public void ProjectRows_MissingFieldsFallBackToEmpty()
    {
        var rows = ApifoxPicker.ProjectRows(new JsonArray(new JsonObject()));
        var row = Assert.Single(rows);
        Assert.Equal("", row.Id);
        Assert.Equal("", row.Name);
        Assert.Null(row.TeamName);
    }

    [Fact]
    public void ProjectRows_SkipsNonObjectElementsAndHandlesNull()
    {
        Assert.Empty(ApifoxPicker.ProjectRows(null));
        Assert.Empty(ApifoxPicker.ProjectRows([]));
        Assert.Empty(ApifoxPicker.ProjectRows(new JsonArray("text", 42)));
    }

    [Fact]
    public void BranchRows_ReadsFlagsWithFalseDefaults()
    {
        var rows = ApifoxPicker.BranchRows(new JsonArray(
            new JsonObject { ["id"] = "1", ["name"] = "master", ["isMain"] = true },
            new JsonObject { ["id"] = "2", ["name"] = "feature-x", ["isArchived"] = true },
            new JsonObject { ["id"] = "3", ["name"] = "feature-y" }));
        Assert.Collection(rows,
            r => { Assert.True(r.IsMain); Assert.False(r.IsArchived); },
            r => { Assert.False(r.IsMain); Assert.True(r.IsArchived); },
            r => { Assert.False(r.IsMain); Assert.False(r.IsArchived); });
    }

    // ── 过滤与文字 ───────────────────────────────────────────────────────

    [Fact]
    public void VisibleBranches_FiltersArchivedKeepsMain()
    {
        var rows = ApifoxPicker.VisibleBranches(ApifoxPicker.BranchRows(new JsonArray(
            new JsonObject { ["id"] = "1", ["name"] = "master", ["isMain"] = true },
            new JsonObject { ["id"] = "2", ["name"] = "feature-x", ["isArchived"] = true },
            new JsonObject { ["id"] = "3", ["name"] = "feature-y" })));
        Assert.Equal(["master", "feature-y"], rows.Select(r => r.Name));
    }

    [Fact]
    public void ProjectText_NameIdAndTeamSuffix()
    {
        Assert.Equal("商城接口（913） · 平台组", ApifoxPicker.ProjectText(new("913", "商城接口", "平台组")));
        Assert.Equal("商城接口（913）", ApifoxPicker.ProjectText(new("913", "商城接口", null)));
    }

    [Fact]
    public void BranchText_MainBranchAnnotatedWithId()
    {
        Assert.Equal("master（主分支） · ID 1", ApifoxPicker.BranchText(new("1", "master", true, false)));
        Assert.Equal("feature-y · ID 3", ApifoxPicker.BranchText(new("3", "feature-y", false, false)));
    }

    [Fact]
    public void BranchValue_MainBranchMeansEmptyName()
    {
        Assert.Equal("", ApifoxPicker.BranchValue(new("1", "master", true, false)));
        Assert.Equal("feature-y", ApifoxPicker.BranchValue(new("3", "feature-y", false, false)));
    }

    // ── 入口门控与校验文案（与冻结页面逐字一致） ─────────────────────────

    [Fact]
    public void CanLoad_OnlySavedProfiles()
    {
        Assert.False(ApifoxPicker.CanLoad(false));
        Assert.True(ApifoxPicker.CanLoad(true));
        Assert.Equal("请先保存配置", ApifoxPicker.SavedRequired);
    }

    [Theory]
    [InlineData(false, "", "请先保存配置")]
    [InlineData(true, "", "请先填写项目 ID")]
    [InlineData(true, "  ", "请先填写项目 ID")]
    [InlineData(true, "12a", "项目 ID 无效。")]
    [InlineData(true, "abc", "项目 ID 无效。")]
    [InlineData(true, "913", null)]
    public void BranchGate_ValidationOrderAndTexts(bool saved, string projectId, string? expected)
    {
        Assert.Equal(expected, ApifoxPicker.BranchGate(saved, projectId));
    }

    [Fact]
    public void FailedPrefixes_MatchRoutes()
    {
        Assert.Equal("读取项目列表失败：", ApifoxPicker.ProjectsFailedPrefix);
        Assert.Equal("读取分支失败：", ApifoxPicker.BranchesFailedPrefix);
    }

    [Fact]
    public void LoadedTexts_ContainCountAndNextStep()
    {
        var projects = ApifoxPicker.ProjectsLoaded(3);
        Assert.Contains("3", projects);
        Assert.Contains("下拉列表", projects);
        Assert.DoesNotContain("失败", projects);

        var branches = ApifoxPicker.BranchesLoaded(2);
        Assert.Contains("2", branches);
        Assert.Contains("已归档", branches);
        Assert.Contains("留空", branches);
    }

    /// <summary>
    /// 前置条件文案<b>只有一个落点</b>（第一百七十四轮，简报 P1-1）。
    /// <para>
    /// <b>收的是哪个口</b>：「请先选择 Apifox 项目。」此前在 <c>Api/ApifoxRoutes.cs</c> 两条路由与
    /// <c>Desktop/Native/ProfileAdvancedDialog.cs</c> 各写一遍（共三处），「项目 ID 无效。」在路由与
    /// <c>ApifoxPicker.ProjectIdInvalid</c> 各写一遍（共两处）。字面相同所以今天看不出问题，
    /// 这条门挡的是「改一处忘其余」——同一句话在产品里出现两种说法不会报错，只会让人以为有两个规则。
    /// 旧注释「与 /api/apifox/branches 路由同文案」正是这种状态：用注释承认重复，而不是消除它。
    /// </para>
    /// <para>
    /// 核四件事：① 两句<b>逐字未变</b>——期望值写死在这里而不是引用常量，否则字面被改掉也查不出来；
    /// ② <c>ApifoxPicker.ProjectIdInvalid</c> 的值就是 <see cref="ErrorCopy"/> 那一份（名字留在界面侧，
    /// 字面只有一处）；③ 扫描面（<c>Api/</c> 与 <c>Desktop/</c> 全部 <c>.cs</c>）里这两句各只允许出现在
    /// <c>Api/ErrorCopy.cs</c>；④ 三处调用点引用的是常量，计数钉死（少一处引用即红）。
    /// </para>
    /// <para>
    /// <b>本文件自己那份真值表不在扫描面内</b>：<c>BranchGate_ValidationOrderAndTexts</c> 的
    /// <c>InlineData</c> 必须写着原句才能当判据——把它也改成引用常量就成了同义反复，
    /// 字面被改动时那条真值表反而不会红。判据指向实物、不指向被测物，与 <c>CodeScanTests</c> 同一族口径。
    /// </para>
    /// </summary>
    [Fact]
    public void PreconditionCopy_HasOneLandingSiteOnly()
    {
        Assert.Equal("请先选择 Apifox 项目。", ErrorCopy.NoApifoxProjectMessage);
        Assert.Equal("项目 ID 无效。", ErrorCopy.InvalidProjectIdMessage);
        Assert.Equal(ErrorCopy.InvalidProjectIdMessage, ApifoxPicker.ProjectIdInvalid);

        var scanned = Directory.GetFiles(Repo.Root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Repo.Root, f).Replace('\\', '/'))
            .Where(r => (r.StartsWith("Api/", StringComparison.Ordinal) || r.StartsWith("Desktop/", StringComparison.Ordinal))
                        && !r.Contains("/bin/", StringComparison.Ordinal)
                        && !r.Contains("/obj/", StringComparison.Ordinal))
            .ToList();
        Assert.True(scanned.Count > 60, $"扫描面只找到 {scanned.Count} 个文件，取法不对（Api/ 与 Desktop/ 合计应有 60+）。");

        foreach (var (text, routeUses, dialogUses) in new[]
                 {
                     (ErrorCopy.NoApifoxProjectMessage, 2, 1),
                     (ErrorCopy.InvalidProjectIdMessage, 1, 0),
                 })
        {
            var files = scanned
                .Where(r => File.ReadAllText(Path.Combine(Repo.Root, r)).Contains(text, StringComparison.Ordinal))
                .ToList();
            Assert.True(files.Count == 1 && files[0] == "Api/ErrorCopy.cs",
                $"「{text}」在 Api/ 与 Desktop/ 之下的落点是 {string.Join("、", files)}，只允许 Api/ErrorCopy.cs 一处："
                + "多出来的每一处，都是下一次改文案时会被忘掉的那一处。");

            // 调用点必须引用常量：计数钉死，新增一处就地写字面量的调用点会被上面那条抓住，
            // 而删掉一处引用（把某个屏接丢了）在这里红。
            var constant = text == ErrorCopy.NoApifoxProjectMessage ? nameof(ErrorCopy.NoApifoxProjectMessage) : nameof(ErrorCopy.InvalidProjectIdMessage);
            var routesSource = Repo.Source("Api", "ApifoxRoutes.cs");
            var dialogSource = Repo.Source("Desktop", "Native", "ProfileAdvancedDialog.cs");
            Assert.True(Count(routesSource, "ErrorCopy." + constant) == routeUses,
                $"Api/ApifoxRoutes.cs 引用 ErrorCopy.{constant} {Count(routesSource, "ErrorCopy." + constant)} 次，应为 {routeUses} 次。");
            Assert.True(Count(dialogSource, "ErrorCopy." + constant) == dialogUses,
                $"ProfileAdvancedDialog.cs 引用 ErrorCopy.{constant} {Count(dialogSource, "ErrorCopy." + constant)} 次，应为 {dialogUses} 次。");
        }
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
