using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using ApifoxHelper.Apifox;
using ApifoxHelper.Contracts;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 本地 mock CLI 端到端的夹具（第一百七十二轮，简报 P0-1）：把「确认 → 写入 → 回读核对」这条对外承诺
/// 第一次跑到可复跑、可断言、可提交的份上。
/// <para>
/// <b>真实的是哪几段</b>：<see cref="ProcessRunner"/> 真的启动一个子进程；<see cref="ApifoxCli"/> 真的按
/// 「前导说明行 + 单个完整 JSON 对象」解析 stdout；<see cref="SyncExecutor"/> 一行没改地在写入前重读远端、
/// 核对指纹、写后回读、失败即停。任务目录、计划落盘、执行日志、本地锁、写入兼容登记全是真实实现。
/// 唯一换掉的只有 Apifox 服务端：<see cref="ScriptName"/> 那份 mock CLI 在临时目录里回答命令，不联网、
/// 不碰任何真实项目。
/// </para>
/// <para>
/// <b>接缝本来就存在，不是为了让测试而新加的</b>：CLI 的入口路径来自配置（<c>ApifoxTarget.NodePath</c> 与
/// <c>CliScriptPath</c>）。生产填 <c>node</c> + <c>cli.js</c>，这里填 <c>node</c> + mock 脚本，
/// 走的仍是同一条 <c>Resolve → RunAsync → Parse</c>。产品源码一行未动。
/// </para>
/// <para>
/// <b>计划由本夹具造，不由 <see cref="SyncPlanService"/> 生成</b>：动作字段（前态指纹、受保护投影、托管字段、
/// 写入内容与其哈希、校验键、CLI 校验结果）全部按生产同一批函数与同一次真实校验现算，计划哈希照它的匿名形状
/// 重算；生成侧的勾选与排除口径另有 <c>SyncSelectionTests</c> 管。
/// </para>
/// <para>
/// <b>跑成了不等于真机跑过</b>：这条链证明的是「执行器的写入边界、指纹核对、回读核对、失败即停在真实进程边界上
/// 成立」，不是「真实 Apifox 接受这些写入」。验收表里 SHARED-3/4/7/8 仍留 <c>[ ]</c>，关闭它们要真机与专用测试项目
/// （见 docs/NOTES.md「G-D5 真机复核清单（可勾选）」第 6 条）。
/// </para>
/// </summary>
internal sealed class MockCliRun : IDisposable
{
    /// <summary>mock CLI 报告的版本，同时也是写入兼容登记里通过的版本。</summary>
    public const string CliVersion = "2.2.6";

    public const string ProjectId = "77";
    public const string Branch = "dev";
    public const string ScriptName = "MockApifoxCli.js";

    /// <summary>
    /// node 的查找顺序。查找本身只有 <see cref="ChromeLayout.NodePath"/> 这一处落点（第一百七十轮立），
    /// 本机没有 node 时降级支要求 NOTES 按这一句把账登记在案。
    /// </summary>
    public const string NodeLookupHint = "APIFOXHELPER_NODE → PATH(node|nodejs)";

    /// <summary>真实 CLI 用 404000 明确回答「不存在」；其他失败都不许当成不存在。</summary>
    public const string NotFoundCode = "404000";

    public string Root { get; }

    public string StateDir { get; }

    public FileStore Store { get; }

    public TaskStore Tasks { get; }

    public CheckTask Task { get; }

    public ApifoxCli Cli { get; } = new();

    /// <summary>执行器：与桌面壳、REST 路由共用同一个类，这里只是把它的依赖指向临时目录与 mock。</summary>
    public SyncExecutor Executor { get; }

    public string CompatPath => Path.Combine(Store.CompatDir, "write-compat.json");

    private readonly string _remoteFile;
    private readonly string _callsFile;
    private readonly string _payloadDir;
    private readonly string _scriptPath;
    private readonly List<PlanAction> _actions = new();

    public MockCliRun()
    {
        Assert.True(Node is not null, $"本地 mock CLI 端到端跑不起来：{NodeLookupHint} 都没找到 node。");
        Assert.True(File.Exists(SourceScript), $"mock CLI 脚本不在仓库里：{SourceScript}。");
        Root = Path.Combine(Path.GetTempPath(), "ah-mockcli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        StateDir = Path.Combine(Root, "state");
        Directory.CreateDirectory(StateDir);
        // 脚本原样复制过去：跑的就是仓库里那份字节，运行时一个字都不改。
        _scriptPath = Path.Combine(Root, ScriptName);
        File.Copy(SourceScript, _scriptPath);
        _remoteFile = Path.Combine(StateDir, "remote.json");
        _callsFile = Path.Combine(StateDir, "calls.jsonl");

        var workspace = Path.Combine(Root, "workspace");
        Directory.CreateDirectory(workspace);
        FileStore.WriteText(Path.Combine(workspace, "Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        Store = new FileStore(Path.Combine(Root, "data"));
        var profiles = new ProfileStore(Store);
        var profile = profiles.Save(new ProjectProfile
        {
            Name = "mock 端到端",
            WorkspaceRoot = workspace,
            ApiProject = "Api.csproj",
            Apifox = new ApifoxTarget
            {
                ProjectId = ProjectId,
                ProjectName = "mock 项目",
                BranchName = Branch,
                NodePath = Node,
                CliScriptPath = _scriptPath,
            },
        }, true);

        Tasks = new TaskStore(Store, profiles);
        Task = Tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "mock 写入" });
        var compat = new CompatService(Store, Cli);
        Executor = new SyncExecutor(Tasks, Store, Cli, compat, new OpenApiSource());
        _payloadDir = Tasks.FilePath(Task.Id, TaskFiles.PlanPayloads);
        Directory.CreateDirectory(_payloadDir);
        WriteRemote(new JsonObject
        {
            ["endpoints"] = new JsonObject(),
            ["schemas"] = new JsonObject(),
            ["nextId"] = 900,
            ["clock"] = 0,
        });
        RegisterVerifiedCliVersion();
    }

    /// <summary>仓库里那份 mock 脚本的路径。</summary>
    public static string SourceScript => Path.Combine(Repo.Root, "ApifoxHelper.Tests", ScriptName);

    /// <summary>node 可执行文件；null 表示按 <see cref="NodeLookupHint"/> 找过、没找到。</summary>
    public static string? Node => ChromeLayout.NodePath;

    /// <summary>配置里的 CLI 入口（写断言时用它调只读命令，路径与执行器用的同一份）。</summary>
    public ApifoxTarget Target => Task.ProfileSnapshot.Apifox;

    /// <summary>在远端放一个接口并返回它的 ID。写入的顶层字段之外的内容就是「受保护设置」。</summary>
    public long PutEndpoint(string method, string path, string description)
    {
        var remote = ReadRemote();
        var id = remote["nextId"]!.GetValue<int>();
        remote["nextId"] = id + 1;
        remote["endpoints"]!.AsObject()[Name(id)] = new JsonObject
        {
            ["id"] = id,
            ["name"] = path,
            ["type"] = "http",
            ["method"] = method.ToLowerInvariant(),
            ["path"] = path,
            ["status"] = "released",
            ["description"] = description,
            ["folderId"] = 12,
            ["tags"] = new JsonArray(JsonValue.Create("订单")),
            ["parameters"] = new JsonObject { ["query"] = new JsonArray() },
            ["requestBody"] = new JsonObject(),
            ["responses"] = new JsonArray(new JsonObject { ["code"] = "200", ["description"] = "成功" }),
            ["script"] = new JsonObject { ["postTransactionScript"] = new JsonArray() },
            ["mock"] = new JsonObject { ["enable"] = false },
        };
        WriteRemote(remote);
        return id;
    }

    /// <summary>mock 眼里的远端详情：回读核对之后远端到底长什么样，从这里量。</summary>
    public JsonObject Endpoint(long id)
    {
        var detail = ReadRemote()["endpoints"]!.AsObject()[Name(id)] as JsonObject;
        Assert.True(detail is not null, $"mock 的远端状态里没有 ID {id}。");
        // 脱父节点：调用方要把它当独立对象改完再挂回去（JsonNode 同时只能有一个父节点）。
        return (JsonObject)detail!.DeepClone();
    }

    /// <summary>
    /// 直接改远端状态，模拟「计划生成之后别人也改了同一个接口」。改的是 mock 的数据而不是指纹——
    /// 指纹由执行方在执行前重读现算，没有第二个落点可以作弊。
    /// </summary>
    public void OverrideEndpoint(long id, Action<JsonObject> change)
    {
        var detail = Endpoint(id);
        change(detail);
        var remote = ReadRemote();
        remote["endpoints"]!.AsObject()[Name(id)] = detail;
        WriteRemote(remote);
    }

    /// <summary>让指定 ID 的写入必然失败（服务端校验不过）：远端保持原样。</summary>
    public void FailWrites(params long[] ids) => WriteFlag("fail-writes.json", ids);

    /// <summary>让指定 ID 的写入丢掉受保护设置（前后置脚本与 Mock）：整段替换的副作用。</summary>
    public void DropProtectedOnWrite(params long[] ids) => WriteFlag("drop-protected.json", ids);

    /// <summary>让创建成功但不回传新资源 ID——真实 CLI 是否回传从未验证过，这一支就是那条未知。</summary>
    public void CreateReturnsNoId() => FileStore.WriteText(Path.Combine(StateDir, "create-returns-no-id.json"), "true");

    /// <summary>让 stdout 在 JSON 之后再多吐一行：输出不再可靠，任何成功判定都不许据此打勾。</summary>
    public void EmitTrailingJunk() => FileStore.WriteText(Path.Combine(StateDir, "trailing-junk.json"), "true");

    /// <summary>
    /// 加一个更新动作：前态指纹、受保护投影、托管字段与写入内容哈希都按生产同一批函数现算
    /// （口径同 <c>SyncPlanService</c> 的 updateEndpoint 分支）。
    /// </summary>
    public PlanAction AddUpdate(long id, JsonObject payload)
    {
        var detail = Endpoint(id);
        var action = NewAction("updateEndpoint", Key(detail), id, payload, "endpoint-update");
        action.BeforeHash = NativeUtil.Hash(detail);
        action.ManagedFields = payload.Select(p => p.Key).ToList();
        action.ProtectedHash = NativeUtil.ProtectedHash(detail, action.ManagedFields);
        return action;
    }

    /// <summary>加一个新建动作：没有远端 ID，前态指纹与受保护投影按生产口径留空。</summary>
    public PlanAction AddCreate(JsonObject payload)
    {
        var action = NewAction("createEndpoint", Key(payload), null, payload, "endpoint-create");
        action.ManagedFields = payload.Select(p => p.Key).ToList();
        return action;
    }

    /// <summary>
    /// 收尾成一份可执行计划：写入文件先过真实 CLI 校验（同 <c>SyncPlanService</c>），再按它的匿名形状算
    /// 计划哈希并落盘。<c>awaitingConfirm</c> 就是「已预览、等用户确认」那一态。
    /// </summary>
    public async Task<SyncPlan> ConfirmPlan()
    {
        var plan = new SyncPlan
        {
            Id = "sp-mock",
            TaskId = Task.Id,
            TaskRevision = Task.Revision,
            Status = "awaitingConfirm",
            ProjectId = ProjectId,
            ProjectName = "mock 项目",
            Branch = Branch,
            CliVersion = CliVersion,
            CreatedAt = DateTimeOffset.Now,
            Actions = _actions,
            PreservedSettings = ["目录", "鉴权与安全方案", "前后置脚本", "Mock", "用例", "已有示例"],
        };
        foreach (var action in plan.Actions)
        {
            var (valid, message) = await Cli.ValidateAsync(Target, action.ValidationKey, action.PayloadFile, CancellationToken.None);
            Assert.True(valid, $"夹具的写入文件没过 CLI 校验：{action.ResourceKey} — {message}");
            action.Validation = message;
        }

        plan.Hash = Canonical.HashOf(new
        {
            plan.ProjectId,
            plan.Branch,
            plan.CliVersion,
            plan.SwaggerHash,
            plan.SupplementHash,
            actions = plan.Actions.Select(a => new { a.Kind, a.ResourceKey, a.RemoteId, a.BeforeHash, a.ProtectedHash, a.DesiredHash, a.ManagedFields, a.DependsOn }),
        });
        Tasks.Write(Task.Id, TaskFiles.Plan, plan);
        return plan;
    }

    /// <summary>
    /// 按<b>调用方手上那一份</b>哈希执行。原生壳「确认同步」传给执行器的就是这一个字符串
    /// （<c>Desktop/Native/SyncPreviewDialog.cs</c> 的 <c>var hash = plan.Hash;</c>），
    /// 所以「确认之后计划又被换掉」这件事只能从这里量：把捕获到的旧哈希交回去，看它挡不挡得住。
    /// </summary>
    public async Task<SyncPlan> ExecuteWithHash(string hash)
    {
        await Executor.ExecuteAsync(Task.Id, hash, Job("sync"));
        return ReadPlan();
    }

    /// <summary>执行已确认的计划；断言只看落盘结果与 mock 记下的调用，不看返回值对象。</summary>
    public async Task<SyncPlan> Execute(SyncPlan plan) => await ExecuteWithHash(plan.Hash);

    /// <summary>
    /// 按<b>过渡壳的 REST 请求形状</b>执行。<c>POST /api/tasks/{id}/plan/apply</c> 的路由体只做一件事
    /// （<c>Api/ApifoxRoutes.cs</c>）：把请求体里的 <see cref="ApplyPlanRequest.PlanHash"/> 原样交给同一个
    /// <see cref="SyncExecutor"/>。这里照搬那一句，<b>不加判定分支、不加假件</b>——为的是让
    /// 「HTTP 请求体里那串哈希」与「执行器比对的那串哈希」之间没有任何测试自造的东西。
    /// 与 <see cref="ExecuteWithHash"/> 的分工：那一支管原生壳传出去的那一个字符串，这一支管 JSON 绑定之后剩下的形状。
    /// </summary>
    public async Task<SyncPlan> ExecuteWithRequest(ApplyPlanRequest request)
    {
        await Executor.ExecuteAsync(Task.Id, request.PlanHash, Job("sync"));
        return ReadPlan();
    }

    /// <summary>「重新核对（只读）」：不写入，只按远端实际状态刷新动作。</summary>
    public async Task<SyncPlan> Verify()
    {
        await Executor.VerifyAsync(Task.Id, Job("verify"));
        return ReadPlan();
    }

    public SyncPlan ReadPlan()
    {
        var plan = Tasks.Read<SyncPlan>(Task.Id, TaskFiles.Plan);
        Assert.NotNull(plan);
        return plan!;
    }

    public CheckTask ReadTask() => Tasks.Get(Task.Id);

    /// <summary>mock 记下的每一句命令。写入边界是不是「只写计划内资源」，以及失败后有没有重发，都从这里量。</summary>
    public List<JsonObject> Calls() =>
        File.Exists(_callsFile)
            ? File.ReadAllLines(_callsFile).Where(l => l.Trim().Length > 0).Select(l => JsonNode.Parse(l)!.AsObject()).ToList()
            : new List<JsonObject>();

    /// <summary>真正到达服务端的写入（update / create），按发生顺序给出「命令 + 目标 ID + 本次写入的顶层字段」。</summary>
    public List<string> WriteCalls() => Calls()
        .Where(c => c["cmd"]!.GetValue<string>() is "endpoint.update" or "endpoint.create")
        .Select(c => c["cmd"]!.GetValue<string>() + " " + TargetOf(c) + " [" + KeysOf(c) + "]")
        .ToList();

    /// <summary>某个命令被真实发出了几次（读取可有限重试、写入绝不自动重发，两条都数得出来）。</summary>
    public int CallCount(string command) => Calls().Count(c => c["cmd"]!.GetValue<string>() == command);

    /// <summary>失败消息里带上完整调用记录，换机器时不用重跑也能看出是哪一支命令没打到。</summary>
    public string CallLog() => string.Join('\n', Calls().Select(c => c.ToJsonString()));

    private static JobContext Job(string kind) =>
        new(new JobInfo { Id = "job-" + kind, Scope = "task:" + Guid.NewGuid().ToString("N"), Kind = kind, StartedAt = DateTimeOffset.Now });

    /// <summary>写入兼容登记：未登记通过的 CLI 版本执行器会直接拒绝，那条门禁本身另有 <c>CompatEntriesTests</c> 管。</summary>
    private void RegisterVerifiedCliVersion()
    {
        FileStore.WriteJson(CompatPath, new WriteCompatStore
        {
            Records =
            {
                new WriteCompatRecord
                {
                    CliVersion = CliVersion,
                    ProjectId = ProjectId,
                    Branch = Branch,
                    Passed = true,
                    At = DateTimeOffset.Now,
                    Checks = { new ProbeCheck { Name = "本地 mock 登记", Passed = true, Detail = "端到端用例用的登记，不代表真实 Apifox 写入兼容" } },
                },
            },
        });
    }

    private PlanAction NewAction(string kind, string resourceKey, long? remoteId, JsonObject payload, string validationKey)
    {
        var action = new PlanAction
        {
            ActionId = $"a{_actions.Count + 1:D3}",
            Kind = kind,
            ResourceKey = resourceKey,
            RemoteId = remoteId,
            ValidationKey = validationKey,
            State = "pending",
        };
        action.PayloadFile = Path.Combine(_payloadDir, action.ActionId + ".json");
        FileStore.WriteJson(action.PayloadFile, payload);
        action.DesiredHash = Canonical.Hash(payload);
        _actions.Add(action);
        return action;
    }

    private static string TargetOf(JsonObject call) => call["id"] is JsonNode id ? id.ToString() : "新建";

    private static string KeysOf(JsonObject call) =>
        string.Join(",", ((call["keys"] as JsonArray) ?? new JsonArray()).Select(k => k!.ToString()).OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>接口身份：方法大写 + 精确路径（与 <c>TaskStore.NormalizeEndpointKey</c> 同一形状）。</summary>
    private static string Key(JsonObject detail) =>
        (detail["method"]?.ToString() ?? "").ToUpperInvariant() + " " + (detail["path"]?.ToString() ?? "");

    private static string Name(long id) => id.ToString(CultureInfo.InvariantCulture);

    private void WriteFlag(string name, long[] ids) =>
        FileStore.WriteText(Path.Combine(StateDir, name), new JsonArray(ids.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()).ToJsonString());

    private JsonNode ReadRemote()
    {
        var root = JsonNode.Parse(File.ReadAllText(_remoteFile, Encoding.UTF8)) as JsonObject;
        Assert.True(root is not null, "mock 的远端状态文件不是对象，夹具初始化没跑成。");
        return root;
    }

    // mock 用 JSON.parse 读每一份夹具：BOM 会让它在第一行就崩（本轮第一次真跑就是这么抓到的）。
    // 落盘一律走 FileStore.WriteText —— 与产品「UTF-8 无 BOM」同一份口径。
    private void WriteRemote(JsonNode remote) => FileStore.WriteText(_remoteFile, remote.ToJsonString());

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录留着不影响判定，下一次运行换一个新的。
        }
    }
}
