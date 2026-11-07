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
}
