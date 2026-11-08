using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「重新加载 Swagger 并检查」入口（NextStep.CanReloadSwagger 单一口径，与冻结页面 recheck(true) 同一规则）：
/// 检查始终基于冻结副本，检查本身不悄悄换源——注释回写或定向构建后源码与 Swagger 已更新，
/// 已冻结过（SourceHash 非空）的任务需要显式重新冻结，否则原生只能永远比较旧副本
/// （冻结页面工具栏有「重新加载 Swagger 并检查」，原生此前没有对应入口）。
/// 门控对真实 CheckTask 类型断言；接线由行首正则机械检查钉住（被注释掉也算删除）。
/// </summary>
public class SwaggerReloadTests
{
    private static CheckTask Task(Action<CheckTask>? setup = null)
    {
        var task = new CheckTask { Revision = 3 };
        setup?.Invoke(task);
        return task;
    }

    private static CheckTask Frozen(Action<CheckTask>? setup = null) => Task(t =>
    {
        t.ProfileSnapshot.Swagger.Type = "file";
        t.Stages.Remote.SourceHash = "abc123";
        setup?.Invoke(t);
    });

    // ---------- 门控真值表：与 NeedsSwaggerLoad 同一族条件，方向相反 ----------

    [Fact]
    public void CanReloadSwagger_RequiresConfiguredFrozenOnlineTask()
    {
        // 已冻结 + 配置了 Swagger + 在线：入口可用。
        Assert.True(NextStep.CanReloadSwagger(Frozen()));
        Assert.True(NextStep.CanReloadSwagger(Frozen(t => t.ProfileSnapshot.Swagger.Type = "url")));
        // 未冻结过（SourceHash 为空）：由「加载 Swagger 并检查」承担，不重复出两个加载入口。
        Assert.False(NextStep.CanReloadSwagger(Task(t => t.ProfileSnapshot.Swagger.Type = "file")));
        // 未配置 Swagger：结构走远端现状，没有可冻结的副本。
        Assert.False(NextStep.CanReloadSwagger(Frozen(t => t.ProfileSnapshot.Swagger.Type = "none")));
        Assert.False(NextStep.CanReloadSwagger(Frozen(t => t.ProfileSnapshot.Swagger.Type = "")));
        // 离线任务：结构来源是导入的 JSON，重新冻结 Swagger 与检查无关。
        Assert.False(NextStep.CanReloadSwagger(Frozen(t => t.OfflineMode = true)));
    }

    [Fact]
    public void ReloadGate_IsComplementOfLoadGate_WhenConfiguredAndOnline()
    {
        // 配置了 Swagger 的在线任务：NeedsSwaggerLoad（未冻结）与 CanReloadSwagger（已冻结）
        // 恰好互补——检查按钮的加载语义始终恰有一个入口承担。
        var configured = Task(t => t.ProfileSnapshot.Swagger.Type = "file");
        Assert.True(NextStep.NeedsSwaggerLoad(configured));
        Assert.False(NextStep.CanReloadSwagger(configured));
        configured.Stages.Remote.SourceHash = "abc123";
        Assert.False(NextStep.NeedsSwaggerLoad(configured));
        Assert.True(NextStep.CanReloadSwagger(configured));
    }

    [Fact]
    public void ReloadLabel_MatchesFrozenPageCopy()
    {
        // 入口文案与冻结页面 recheck(true) 按钮逐字一致，单一落点在 NextStep。
        Assert.Equal("重新加载 Swagger 并检查", NextStep.ReloadSwaggerLabel);
    }

    // ---------- 接线机械检查：口径存在但界面没接线等于没有 ----------

    private static string Page => Repo.Source("Desktop", "Native", "TasksPage.cs");

    [Fact]
    public void TasksPage_WiresReloadButtonIntoRemoteActions()
    {
        // 按钮经工厂出厂、文案取共享口径、注册进远端动作区（与「开始检查/重新检查」并列）、点击强制重冻结。
        Assert.Matches(@"(?m)^\s*_reloadSwaggerButton = Ui\.Ghost\(NextStep\.ReloadSwaggerLabel\);", Page);
        Assert.Matches(@"(?m)^\s*_reloadSwaggerButton\.Visible = false;", Page);
        Assert.Matches(
            @"(?m)^\s*_reloadSwaggerButton\.Click \+= \(_, _\) => StartRemoteCheck\(reloadSwagger: true\);",
            Page);
        Assert.Matches(
            @"(?m)^\s*_remoteActions\.Controls\.Add\(_remoteCheckButton\);\s*\n\s*_remoteActions\.Controls\.Add\(_reloadSwaggerButton\);",
            Page);
    }

    [Fact]
    public void TasksPage_VisibilityAndEnableFollowSharedGate()
    {
        // 可见性按 CanReloadSwagger 推导（不得写死或借其他条件）；作业执行中与其他远端入口同一纪律禁用。
        Assert.Matches(
            @"(?m)^\s*_reloadSwaggerButton\.Visible = NextStep\.CanReloadSwagger\(task\);",
            Page);
        Assert.Matches(
            @"(?m)^\s*_remoteCheckButton\.Enabled = scanReady;\s*\n\s*_reloadSwaggerButton\.Enabled = scanReady;",
            Page);
    }

    [Fact]
    public void StartRemoteCheck_ReloadForcesLoadBeforeRun()
    {
        // 方法体切片：重冻结与首冻结共用一个检查作业，reloadSwagger 强制先加载冻结副本。
        var start = Page.IndexOf("private void StartRemoteCheck(bool reloadSwagger = false)", StringComparison.Ordinal);
        var end = Page.IndexOf("/// <summary>离线检查前置", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "TasksPage.cs 缺少 StartRemoteCheck 方法或其后续锚点。");
        var body = Page[start..end];
        Assert.Matches(@"(?m)^\s*var loadSwagger = reloadSwagger \|\| NextStep\.NeedsSwaggerLoad\(_selectedTask\);", body);
        Assert.Matches(@"(?m)^\s*if \(loadSwagger\) await _checks\.LoadSwaggerAsync\(taskId, job\.Token\);", body);
    }

    [Fact]
    public void DetailHint_ExplainsFreezeAndReload()
    {
        // 说明行讲清冻结语义与入口用途：为什么之后检查基于副本、什么时候用重新加载。
        Assert.Contains("配置了 Swagger 时首次检查先加载并冻结副本，之后的检查都基于冻结副本；源码或 Swagger 更新后用「重新加载 Swagger 并检查」重新冻结再检查。", Page);
    }

    [Fact]
    public void DesignDoc_RecordsTheEntry()
    {
        // DESIGN.md §8 记录入场：冻结副本刷新入口的缺口与口径如实回写（条款行在 §8 表格内，
        // 与同族条款同一断言形态——章节切片 + 内容断言，不依赖表格列宽）。
        var section = Repo.DesignSection(8);
        Assert.Contains("冻结副本刷新入口", section);
        Assert.Contains("重新加载 Swagger 并检查", section);
        Assert.Contains("CanReloadSwagger", section);
    }
}
