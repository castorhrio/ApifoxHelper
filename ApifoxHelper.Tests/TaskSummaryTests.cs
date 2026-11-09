using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Profiles;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务详情顶部两行元信息的口径（`TaskSummary` 单一落点，与测试工程共享编译）：
/// 范围行按模块 / 路径前缀 / 指定接口三个维度如实列出，三者皆空才说「API 项目全部接口」——
/// 与 `TaskStore.InScope` 的真实判定同一条件（真值表在 Linux 真执行，含真实落盘后重读的任务）；
/// 元信息行补上版本备注，留空时与报告同一句话「未绑定代码版本」。
/// 界面接线由行首正则机械检查钉住：口径存在但页面没调用等于没有。
/// </summary>
public class TaskSummaryTests : IDisposable
{
    private readonly string _root;
    private readonly ProfileStore _profiles;
    private readonly ProjectProfile _profile;

    public TaskSummaryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-tasksummarytests-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _profiles = new ProfileStore(new FileStore(_root));
        _profile = new ProjectProfile
        {
            Name = "示例配置",
            WorkspaceRoot = workspace,
            ApiProject = "Api.csproj",
            SourceVersionNote = "cfg-123",
            Modules = [new ModuleScope { Name = "财务", PathPrefixes = ["/api/finance"] }],
        };
        _profiles.Save(_profile, isNew: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private CheckTask Create(List<string>? modules = null, List<string>? prefixes = null, List<string>? keys = null, string? note = null) =>
        new TaskStore(new FileStore(_root), _profiles).Create(new CreateTaskRequest
        {
            ProfileId = _profile.Id,
            Name = "示例任务",
            ModuleNames = modules ?? [],
            PathPrefixes = prefixes ?? [],
            EndpointKeys = keys ?? [],
            SourceVersionNote = note,
        });

    private static CheckTask Bare() => new()
    {
        Id = "t1",
        Name = "示例任务",
        ProfileSnapshot = new ProjectProfile { Name = "示例配置" },
    };

    // ── 范围行：三个维度如实列出 ─────────────────────────────────────

    [Fact]
    public void RangeParts_AllThreeDimensions_AreListedInFixedOrder()
    {
        var task = Bare();
        task.ModuleNames.Add("财务");
        task.PathPrefixes.Add("/api/finance");
        task.EndpointKeys.Add("GET /api/orders");

        Assert.Equal(
            ["模块 财务", "路径前缀 /api/finance", "指定接口 1 个"],
            TaskSummary.RangeParts(task));
        Assert.Equal("范围：模块 财务 · 路径前缀 /api/finance · 指定接口 1 个", TaskSummary.RangeText(task));
    }

    [Fact]
    public void RangeParts_ModuleWithoutPrefixes_StillSaysAllEndpoints()
    {
        // 模块名只是选范围时用的名字：没有配前缀的模块不缩小任何范围（TaskStore.InScope 答「全部接口」），
        // 此时列模块名等于把「检查全部接口」说成「只检查这个模块」。
        var task = Bare();
        task.ModuleNames.Add("财务");

        Assert.Equal([TaskSummary.AllEndpointsLabel], TaskSummary.RangeParts(task));
        Assert.True(TaskStore.InScope(task, "GET /api/whatever", "/api/whatever"));
    }

    [Fact]
    public void RangeParts_ModuleWithPrefixes_ListsBoth()
    {
        var task = Bare();
        task.ModuleNames.Add("财务");
        task.PathPrefixes.Add("/api/finance");

        Assert.Equal(["模块 财务", "路径前缀 /api/finance"], TaskSummary.RangeParts(task));
        Assert.DoesNotContain(TaskSummary.AllEndpointsLabel, TaskSummary.RangeText(task));
    }

    [Fact]
    public void RangeParts_PrefixOnly_DoesNotClaimAllEndpoints()
    {
        var task = Bare();
        task.PathPrefixes.Add("/api/orders");

        Assert.Equal(["路径前缀 /api/orders"], TaskSummary.RangeParts(task));
        Assert.DoesNotContain(TaskSummary.AllEndpointsLabel, TaskSummary.RangeText(task));
    }

    [Fact]
    public void RangeParts_EndpointKeysOnly_AreCountedNotListed()
    {
        // 指定接口只给个数：这一行是概览，逐个列出会把两行文字撑成一段清单。
        var task = Bare();
        task.EndpointKeys.AddRange(["GET /api/a", "POST /api/b"]);

        Assert.Equal(["指定接口 2 个"], TaskSummary.RangeParts(task));
        Assert.DoesNotContain("/api/a", TaskSummary.RangeText(task));
    }

    [Fact]
    public void RangeParts_Empty_IsTheOnlyCaseThatSaysAllEndpoints()
    {
        Assert.Equal([TaskSummary.AllEndpointsLabel], TaskSummary.RangeParts(Bare()));
        Assert.Equal("范围：API 项目全部接口", TaskSummary.RangeText(Bare()));
    }

    [Fact]
    public void RangeParts_MultipleModulesAndPrefixes_AreJoinedWithTheSameSeparators()
    {
        var task = Bare();
        task.ModuleNames.AddRange(["财务", "订单"]);
        task.PathPrefixes.AddRange(["/api/finance", "/api/orders"]);

        Assert.Equal(["模块 财务、订单", "路径前缀 /api/finance /api/orders"], TaskSummary.RangeParts(task));
    }

    // ── 与 TaskStore.InScope 同一条件（真执行） ──────────────────────

    [Fact]
    public void AllEndpointsLabel_AppearsExactlyWhenInScopeAcceptsEverything()
    {
        var cases = new List<CheckTask> { Bare() };
        var moduleOnly = Bare();
        moduleOnly.ModuleNames.Add("财务");
        cases.Add(moduleOnly);
        var prefixOnly = Bare();
        prefixOnly.PathPrefixes.Add("/api/orders");
        cases.Add(prefixOnly);
        var keysOnly = Bare();
        keysOnly.EndpointKeys.Add("GET /api/orders");
        cases.Add(keysOnly);
        var both = Bare();
        both.PathPrefixes.Add("/api/orders");
        both.EndpointKeys.Add("GET /api/other");
        cases.Add(both);

        foreach (var task in cases)
        {
            // InScope 的「全部接口」条件是「路径前缀与指定接口都为空」——范围行必须与它同一条件：
            // 说「全部接口」当且仅当这两个维度都为空，且此时任意接口都在范围内。
            var saysAll = TaskSummary.RangeParts(task).Contains(TaskSummary.AllEndpointsLabel);
            Assert.Equal(task.PathPrefixes.Count == 0 && task.EndpointKeys.Count == 0, saysAll);
            if (saysAll) Assert.True(TaskStore.InScope(task, "GET /api/whatever", "/api/whatever"));
        }
    }

    [Fact]
    public void CreatedTask_WithEndpointKeysOnly_ReportsTheKeysNotAllEndpoints()
    {
        // 真实落盘后重读：第二十八轮补的「指定接口」是第三个范围维度，此前范围行完全不含它。
        var created = Create(keys: ["GET /api/orders"]);
        var reread = new TaskStore(new FileStore(_root), _profiles).Get(created.Id)!;

        Assert.Equal(["指定接口 1 个"], TaskSummary.RangeParts(reread));
        Assert.DoesNotContain(TaskSummary.AllEndpointsLabel, TaskSummary.RangeText(reread));
        Assert.True(TaskStore.InScope(reread, "GET /api/orders", "/api/orders"));
        Assert.False(TaskStore.InScope(reread, "GET /api/other", "/api/other"));
    }

    [Fact]
    public void CreatedTask_WithModule_ShowsBothModuleAndExpandedPrefix()
    {
        // 模块映射展开进路径前缀（TaskStore.Create）：模块名是选范围时用的名字，前缀是真正参与匹配的那一份，两者都列出。
        var created = Create(modules: ["财务"]);
        var reread = new TaskStore(new FileStore(_root), _profiles).Get(created.Id)!;

        Assert.Equal(["模块 财务", "路径前缀 /api/finance"], TaskSummary.RangeParts(reread));
    }

    [Fact]
    public void CreatedTask_WithoutScope_SaysAllEndpoints()
    {
        var created = Create();
        var reread = new TaskStore(new FileStore(_root), _profiles).Get(created.Id)!;

        Assert.Equal([TaskSummary.AllEndpointsLabel], TaskSummary.RangeParts(reread));
    }

    // ── 元信息行：配置、目标、在线/离线、版本备注 ────────────────────

    [Fact]
    public void MetaText_ListsProfileTargetModeAndVersionNote()
    {
        var task = Bare();
        task.ProfileSnapshot.Apifox.ProjectId = "12345";
        task.SourceVersionNote = "  v1.2.3  ";

        Assert.Equal("配置 示例配置 · Apifox 12345 · 在线 · 版本备注 v1.2.3", TaskSummary.MetaText(task));
    }

    [Fact]
    public void MetaText_UnboundProjectAndOfflineMode_UseTheSamePlaceholdersAsTheRestOfTheShell()
    {
        var task = Bare();
        task.OfflineMode = true;

        Assert.Equal("配置 示例配置 · Apifox 未绑定 · 离线模式 · 版本备注 未绑定代码版本", TaskSummary.MetaText(task));
    }

    [Fact]
    public void VersionNote_BlankOrWhitespace_IsTheSameSentenceAsTheReport()
    {
        var task = Bare();
        Assert.Equal(TaskSummary.NoVersionNoteLabel, TaskSummary.VersionNote(task));

        task.SourceVersionNote = "   ";
        Assert.Equal(TaskSummary.NoVersionNoteLabel, TaskSummary.VersionNote(task));

        task.SourceVersionNote = " build-42 ";
        Assert.Equal("build-42", TaskSummary.VersionNote(task));
    }

    [Fact]
    public void NoVersionNoteLabel_IsTheSameLiteralTheReportWrites()
    {
        // 界面与报告不各说一套：报告里那句话的数据来源就是这一句。
        var report = Repo.Source("Reports", "ReportService.cs");
        Assert.Contains(TaskSummary.NoVersionNoteLabel, report, StringComparison.Ordinal);
    }

    // ── 界面接线（口径存在但页面没调用等于没有） ─────────────────────

    [Fact]
    public void TasksPage_ReadsBothLinesFromTheSharedEntry()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");

        Assert.Matches(new Regex(@"^\s*_detailMeta\.Text = TaskSummary\.MetaText\(task\);", RegexOptions.Multiline), page);
        Assert.Matches(new Regex(@"^\s*_detailRange\.Text = TaskSummary\.RangeText\(task\);", RegexOptions.Multiline), page);
    }

    [Fact]
    public void TasksPage_NoLongerBuildsEitherLineInPlace()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");

        // 就地拼的两处（元信息行与范围行）已收进 TaskSummary：页面里不再出现这两处字面。
        Assert.DoesNotContain("\"范围：\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("范围：API 项目全部接口", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Apifox {(task.ProfileSnapshot.Apifox.ProjectId", page, StringComparison.Ordinal);
    }

    [Fact]
    public void RangeAndMetaText_LiveOnlyInTheSharedEntry()
    {
        // 两行文字只在 TaskSummary 一处拼装：界面不各拼一遍（拼一遍就有一处先漂）。
        var offenders = Repo.NativeSources()
            .Where(s => !s.Name.Equals("TaskSummary.cs", StringComparison.Ordinal))
            .Where(s => s.Contains("范围：") || s.Contains("版本备注 {"))
            .Select(s => s.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedCompileEntry_IsInTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\TaskSummary.cs", csproj, StringComparison.Ordinal);
    }

    // ── 文档条款 ─────────────────────────────────────────────────────

    [Fact]
    public void DesignDoc_RecordsTheClause()
    {
        var section6 = Repo.DesignSection(6);
        Assert.Contains("任务详情元信息", section6, StringComparison.Ordinal);
        Assert.Contains("TaskSummary", section6, StringComparison.Ordinal);

        var section8 = Repo.DesignSection(8);
        Assert.Contains("任务详情元信息", section8, StringComparison.Ordinal);
    }
}
