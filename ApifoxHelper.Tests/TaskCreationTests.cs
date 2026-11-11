using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Profiles;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 新建检查任务的「指定接口」与「版本备注」口径（冻结页面 newTask 表单与原生 NewTaskDialog
/// 同一规则，TaskCreation 单一落点）：多行文本按行解析与冻结页面 lines() 同一结果；
/// 「GET /path」的规整、去重与非法行丢弃、版本备注的配置级回退在 TaskStore.Create 单一口径，
/// 由真实 FileStore/ProfileStore/TaskStore 常驻断言（真实工作区夹具，落盘后重读）。
/// 对话框接线由行首正则机械检查钉住：口径存在但界面没调用等于没有。
/// </summary>
public class TaskCreationTests : IDisposable
{
    private readonly string _root;
    private readonly ProfileStore _profiles;
    private readonly ProjectProfile _profile;

    public TaskCreationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-taskcreationtests-" + Guid.NewGuid().ToString("N"));
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

    private CheckTask Create(List<string>? keys = null, string? note = null, List<string>? prefixes = null) =>
        new TaskStore(new FileStore(_root), _profiles).Create(new CreateTaskRequest
        {
            ProfileId = _profile.Id,
            Name = "示例任务",
            PathPrefixes = prefixes ?? [],
            EndpointKeys = keys ?? [],
            SourceVersionNote = note,
        });

    // ── TaskCreation：行解析与版本备注取值 ───────────────────────────

    [Fact]
    public void LineValues_SplitsTrimsAndDropsEmptyLines()
    {
        // 与冻结页面 lines() 同一结果：按 \n 分行（行尾 \r 随 trim 去掉）、丢弃空行。
        var values = TaskCreation.LineValues("GET /api/a\r\nPOST /api/b\n\n   \nget /api/c  \n");
        Assert.Equal(["GET /api/a", "POST /api/b", "get /api/c"], values);
    }

    [Fact]
    public void LineValues_BlankText_YieldsEmptyList()
    {
        Assert.Empty(TaskCreation.LineValues(null));
        Assert.Empty(TaskCreation.LineValues(""));
        Assert.Empty(TaskCreation.LineValues("\r\n  \n"));
    }

    [Fact]
    public void VersionNote_TrimsAndReturnsNullWhenBlank()
    {
        Assert.Null(TaskCreation.VersionNote(null));
        Assert.Null(TaskCreation.VersionNote("   "));
        Assert.Equal("abc 001", TaskCreation.VersionNote("  abc 001  "));
    }

    [Fact]
    public void Hints_MatchFrozenPageLiterals()
    {
        // 提示文字与冻结页面 newTask 表单的占位文字同一字面（改冻结页面文案必须两边同改）。
        Assert.Equal("每行一个，例如 GET /api/finance/BillList", TaskCreation.KeysHint);
        Assert.Equal("提交号或构建号（可选）", TaskCreation.NotePlaceholder);
    }

    // ── TaskStore.Create：规整、去重、非法行丢弃与备注回退（真实落盘） ──

    [Fact]
    public void Create_NormalizesKeysDropsInvalidAndDedupes()
    {
        // 「GET /path」规整（方法大写、路径补斜杠）在 TaskStore 单一口径；非法行丢弃、重复行去重。
        var task = Create(keys: ["get api/users", "", "POST api/orders", "post api/orders", "只有一段"]);
        Assert.Equal(["GET /api/users", "POST /api/orders"], task.EndpointKeys);
    }

    [Fact]
    public void Create_BlankNote_FallsBackToProfileNote()
    {
        // 任务级备注留空（null 或空白）回退到配置级备注——原生对话框把空交成 null 即走同一回退。
        Assert.Equal("cfg-123", Create(note: null).SourceVersionNote);
        Assert.Equal("cfg-123", Create(note: "   ").SourceVersionNote);
    }

    [Fact]
    public void Create_FilledNote_OverridesProfileNote()
    {
        Assert.Equal("feat-abc 20261107", Create(note: " feat-abc 20261107 ").SourceVersionNote);
    }

    [Fact]
    public void Create_PersistsAndRereads()
    {
        var task = Create(keys: ["GET /api/a"], note: "abc 001");
        var reread = new TaskStore(new FileStore(_root), _profiles).Get(task.Id);
        Assert.Equal(["GET /api/a"], reread.EndpointKeys);
        Assert.Equal("abc 001", reread.SourceVersionNote);
    }

    [Fact]
    public void Create_UnknownModule_IsRejectedWithFrozenMessage()
    {
        var ex = Assert.Throws<UserException>(() => new TaskStore(new FileStore(_root), _profiles).Create(
            new CreateTaskRequest { ProfileId = _profile.Id, ModuleNames = ["不存在"] }));
        Assert.Equal("配置中没有模块「不存在」。", ex.Message);
    }

    // ── 范围判定：指定接口与路径前缀取并集 ──────────────────────────

    [Fact]
    public void InScope_KeysOnly_MatchesExactIdentityOrdinal()
    {
        // 接口身份按原大小写精确匹配（方案 5.3：身份 = 方法 + 精确路径）；未列出的接口不在范围内。
        var task = Create(keys: ["GET /api/users"]);
        Assert.True(TaskStore.InScope(task, "GET /api/users", "/api/users"));
        Assert.False(TaskStore.InScope(task, "get /api/users", "/api/users"));
        Assert.False(TaskStore.InScope(task, "GET /api/orders", "/api/orders"));
    }

    [Fact]
    public void InScope_PrefixesAndKeys_TakeUnion()
    {
        // 指定接口与路径前缀取并集：前缀按忽略大小写匹配（与冻结页面同一语义）。
        var task = Create(keys: ["GET /api/users"], prefixes: ["/api/finance"]);
        Assert.True(TaskStore.InScope(task, "GET /api/finance/bill", "/api/finance/Bill"));
        Assert.True(TaskStore.InScope(task, "GET /api/users", "/api/users"));
        Assert.False(TaskStore.InScope(task, "GET /api/orders", "/api/orders"));
    }

    [Fact]
    public void InScope_EmptyScopes_MeanWholeProject()
    {
        Assert.True(TaskStore.InScope(Create(), "GET /api/anything", "/api/anything"));
    }

    // ── 对话框接线机械检查 ──────────────────────────────────────────

    [Fact]
    public void NewTaskDialog_WiresKeysAndNoteViaSharedEntries()
    {
        var dialog = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 提交口径经共享 TaskCreation（不复制解析）：接线用行首正则断言——接线被注释掉也算删除，变异可红。
        Assert.Matches(@"(?m)^\s*EndpointKeys = TaskCreation\.LineValues\(_keys\.Text\),", dialog);
        Assert.Matches(@"(?m)^\s*SourceVersionNote = TaskCreation\.VersionNote\(_note\.Text\),", dialog);
        // 两个输入框都经工厂出厂并包 1px 描边（DESIGN.md §6）；提示与占位取共享常量，不复制字面。
        Assert.Contains("AddRow(grid, \"指定接口\", Ui.Field(_keys));", dialog);
        Assert.Contains("AddRow(grid, \"版本备注\", Ui.Field(_note));", dialog);
        Assert.Matches(@"(?m)^\s*Ui\.HintRow\(grid, TaskCreation\.KeysHint\);", dialog);
        Assert.Contains("_note.PlaceholderText = TaskCreation.NotePlaceholder;", dialog);
        Assert.Matches(@"(?m)^\s*Ui\.HintRow\(grid, TaskCreation\.NoteFallbackHint\);", dialog);
    }
}
