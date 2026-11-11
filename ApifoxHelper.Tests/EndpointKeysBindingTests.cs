using System.Text.RegularExpressions;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 请求体里的清单与字典走到服务那一段（第二百一十四轮，简报 P0-1：承接第 212 轮那一发真实 500 的同类清扫）。
/// <para>
/// <b>补的是哪一处</b>：<see cref="CreatePlanRequest.EndpointKeys"/>、<see cref="CreateTaskRequest"/> 的三个范围清单、
/// <see cref="DecisionStore"/> 的三个字典、<see cref="SupplementStore.Entries"/> 都写成
/// <c>List&amp;lt;…&amp;gt; { get; set; } = new()</c>。那个初始值<b>只挡得住「请求体没写这个键」</b>：
/// 显式写 <c>"endpointKeys": null</c> 时 System.Text.Json 会用 null 覆盖它（本轮用最小 ASP.NET 工程按
/// <c>Api/HelperHost.cs</c> 同一份 <c>ConfigureHttpJsonOptions</c> 真跑过绑定，见下面的「证据到哪为止」）。
/// 于是各条链的第一句就抛 <see cref="NullReferenceException"/>，经 <c>Api/ErrorHandling.cs</c> 的 500 分支
/// 变成「内部错误，请重试」：
/// <code>
/// POST /api/tasks/{id}/plan → request.EndpointKeys.ToList()
/// POST /api/tasks           → TaskStore.Create 头三句清单操作
/// PUT  /api/tasks/{id}/decisions    → 三句 foreach body.X.Values 的逐值校验
/// PUT  /api/tasks/{id}/supplement   → foreach (var entry in body.Entries)
/// </code>
/// 前三条链本来都有<b>该给用户的拒绝话</b>（「请至少选择一个接口。」「配置中没有模块「…」。」「决策值无效。」），
/// 那一发 null 让它们一个都到不了。
/// </para>
/// <para>
/// <b>修法照第 212 轮，不照简报原文</b>：简报给的是「显式 null 归一为在用形状，不 throw」，本轮就是这一句
/// （每个 setter 一行 <c>value ?? new(…)</c>）。<b>不改语义、只堵 500</b>，且<b>每一处的「空」按它自己那一处的定义读</b>：
/// 同步计划的空清单是<b>必填未填</b>（<c>SyncPlanService.CreateAsync</c> 那句拒绝），草稿生成的空清单是<b>全部缺口</b>
/// （<c>DraftService.SelectSlots</c> 那一句），新建任务的空清单是<b>这一维不圈范围</b>（两者皆空即全项目）。
/// 同一个词在这三条链里不是同一件事，<see cref="EmptyMeansDifferentThings_AtEachEntryPoint"/> 把这三份定义钉住，
/// 免得下一轮把其中一处的「空 = 拒绝」套到另一处去。
/// </para>
/// <para>
/// <b>简报点名的 <c>ContractReviewRequest</c> 不构成 500（本轮量过，不是读源码推的）</b>：它只有两个
/// <c>string</c> 属性，显式 null 走到 <c>ContractReviewService</c> 后落在那两句本该给的拒绝里
/// （找不到契约 → 「响应契约不存在。」404；状态不合法 → 「审阅状态无效。」400）。
/// <see cref="ContractReview_NullStrings_LandOnTheGuardedBranches"/> 真跑那几发，把「这一处没有缺陷」
/// 从一句自述变成一条会红的门。同理逐处读到的标量族（<c>ApplyPlanRequest.PlanHash</c>、
/// <c>WriteProbeRequest.ProjectId</c>、<c>ModelKeyRequest.Key</c>、<c>ManualCandidateRequest</c> 的三个属性、
/// <c>PickFolderRequest.Initial</c>、<c>BuildRequest.Confirm</c>）都走 <c>==</c> 比较、
/// <c>string.IsNullOrWhiteSpace</c> 或 <c>is not null</c> 支路，null 到得了拒绝、到不了 NRE；
/// <see cref="BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred"/> 判这一族<b>至今没有清单属性</b>——
/// 哪天有人往里面加一个 <c>List</c>，这一条先红，因为那一发就又是一发 500。
/// </para>
/// <para>
/// <b>本轮明确没扫到的那一格（挂在门上，不挂在人身上）</b>：<c>Profiles/ProjectProfile.cs</c> 也是请求体本身
/// （<c>POST/PUT /api/profiles</c>），而 <c>ProfileStore.Normalize</c> 头三句就是 <c>p.Name.Trim()</c> 一类，
/// 显式 <c>"name": null</c> 同样是 500。<b>本轮不改它</b>：它是「落盘模型 + 请求体」两用类型，
/// 每一格的归一目标要单独定（<c>AnalysisSettings.PreprocessorSymbols</c> 的 null 是「未设置、按目标框架推导」，
/// 不是「空清单」；八个小节对象各有自己的默认值），一刀切归一会把「未设置」读成「设置成空」。
/// 这一格连同理由写进 <c>docs/NOTES.md</c>「请求体显式 null 的清扫（可核对的形状表）」，
/// 由 <see cref="BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred"/> 判它不许悄悄消失。
/// </para>
/// <para>
/// <b>证据到哪为止</b>：测试工程是 <c>net8.0</c>、没有 ASP.NET Core 框架引用，<b>本文件一发 HTTP 请求都没有真发过</b>。
/// 但边界比第 212 轮往前挪了一步：那一次 <c>Api/ApiRoutes.cs</c> 里的 <c>MaterialRequest</c> 只能判源码形状，
/// 而本轮 <c>Sync/DecisionService.cs</c>、<c>Sync/SupplementService.cs</c>、<c>Source/ContractReviewService.cs</c>
/// 三份已共享编入本工程，所以「绑定成什么」与「服务第一句拿它做什么」<b>两段都在同一发数据上真执行</b>
/// （<see cref="NullShapes_RunTheRealFirstLineOfEachService"/> 与
/// <see cref="ContractReview_NullStrings_LandOnTheGuardedBranches"/>）。中间那一段——ASP.NET 的模型绑定
/// 到底会不会把显式 null 送进 setter——用一份最小 ASP.NET 工程按产品同一序列化口径跑过，
/// 结论写在上面与本轮 NOTES 记录里，临时工程不进仓库。
/// </para>
/// </summary>
public class EndpointKeysBindingTests : IDisposable
{
    /// <summary>
    /// 产品自己那一份 HTTP 序列化口径：<c>Api/HelperHost.cs</c> 的
    /// <c>ConfigureHttpJsonOptions(o =&gt; JsonDefaults.Apply(o.SerializerOptions))</c> 起手于
    /// <c>JsonSerializerDefaults.Web</c>，取法与 <see cref="SerializationCaliberTests"/> 一致。
    /// </summary>
    private static JsonSerializerOptions HttpOptions()
    {
        var http = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonDefaults.Apply(http);
        return http;
    }

    // ── 形状表：本轮归一的属性（文件 / 类型 / 属性 / 该类型绑在哪条路由）──────────

    private readonly record struct Shape(string File, string Type, string Property, string Route);

    /// <summary>
    /// 已按 null 容忍 setter 归一的清单与字典。<b>封闭清单</b>：新增一条要么附上真往返断言（② 会自动扫到这一份），
    /// 要么把它登记进 <see cref="ScalarOnly"/> 或 <see cref="Deferred"/>——三处都不登记，
    /// <see cref="BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred"/> 即红。
    /// </summary>
    private static readonly Shape[] Normalized =
    [
        new("Sync/PlanModels.cs", "CreatePlanRequest", "EndpointKeys", "/api/tasks/{id}/plan"),
        new("Tasks/TaskModels.cs", "CreateTaskRequest", "ModuleNames", "/api/tasks"),
        new("Tasks/TaskModels.cs", "CreateTaskRequest", "PathPrefixes", "/api/tasks"),
        new("Tasks/TaskModels.cs", "CreateTaskRequest", "EndpointKeys", "/api/tasks"),
        new("Sync/CheckModels.cs", "DecisionStore", "Items", "/api/tasks/{id}/decisions"),
        new("Sync/CheckModels.cs", "DecisionStore", "DocChoices", "/api/tasks/{id}/decisions"),
        new("Sync/CheckModels.cs", "DecisionStore", "ModelBindings", "/api/tasks/{id}/decisions"),
        new("Sync/CheckModels.cs", "SupplementStore", "Entries", "/api/tasks/{id}/supplement"),
    ];

    /// <summary>
    /// 第 212 轮已归一、门在 <see cref="SlotIdsBindingTests"/> 的那两条。登记在这里只为了让这份清单
    /// <b>真的覆盖整族</b>（⑧ 逐个点名）并让形状检查（⑥）跑在同一把刀上；<b>不重跑一遍真往返</b>——
    /// 那一发的四种形状已由 <c>SlotIdsBindingTests.EveryShape_BindsToAListTheServiceCanCount</c> 钉住，
    /// 复制一份只会多出第二份要同步维护的期望值。
    /// </summary>
    private static readonly Shape[] FixedEarlierRound =
    [
        new("Comments/DraftModels.cs", "GenerateRequest", "SlotIds", "/api/tasks/{id}/comment-drafts"),
        new("Api/ApiRoutes.cs", "MaterialRequest", "SlotIds", "/api/tasks/{id}/comment-drafts/material"),
    ];

    /// <summary>只有标量属性的一族：本轮逐处读到归宿是 400/404 或 null 容忍支路，没有清单可归一。</summary>
    private static readonly string[] ScalarOnly =
    [
        "ContractReviewRequest", "BuildRequest", "ApplyPlanRequest", "ApplyCommentPlanRequest",
        "WriteProbeRequest", "ManualCandidateRequest", "CandidateUpdateRequest", "ModelKeyRequest", "PickFolderRequest",
    ];

    /// <summary>
    /// 同族但本轮不归一的那一格：<b>必须在 NOTES 里带着理由在位</b>（判据见
    /// <see cref="BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred"/> ④）。
    /// </summary>
    private static readonly string[] Deferred = ["ProjectProfile"];

    /// <summary>本轮清扫那一节的稳定标题（挂账不许悄悄消失的锚点）。</summary>
    private const string SweepSection = "## 请求体显式 null 的清扫（可核对的形状表）";

    // ── 真执行夹具：真实 FileStore / ProfileStore / TaskStore（与 TaskCreationTests 同一手法）──

    private readonly string _root;
    private readonly FileStore _store;
    private readonly ProfileStore _profiles;
    private readonly TaskStore _tasks;
    private readonly ProjectProfile _profile;

    public EndpointKeysBindingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "apifox-helper-endpointkeys-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(_root, "ws");
        Directory.CreateDirectory(workspace);
        File.WriteAllText(Path.Combine(workspace, "Api.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        _store = new FileStore(_root);
        _profiles = new ProfileStore(_store);
        _profile = new ProjectProfile
        {
            Name = "示例配置",
            WorkspaceRoot = workspace,
            ApiProject = "Api.csproj",
            Modules = [new ModuleScope { Name = "财务", PathPrefixes = ["/api/finance"] }],
            Apifox = new ApifoxTarget { ProjectId = "77", ProjectName = "示例项目" },
        };
        _profiles.Save(_profile, isNew: true);
        _tasks = new TaskStore(_store, _profiles);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>过渡壳实际送出的那些键名（从 <c>Web/app.js</c> 现读，不在测试里抄第二份）。</summary>
    private static List<string> PageKeys(string pattern)
    {
        var page = Repo.Source("Web", "app.js");
        return Regex.Matches(page, pattern).Cast<Match>().Select(m => m.Groups["k"].Value).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// ① 页面送的键就是绑定的字段：键名从 <c>Web/app.js</c> 现读，再拿产品自己那份序列化口径往返一次。
    /// 键名漂移这一类（改名、大小写、写成 <c>endpoint_ids</c>）只有往返一次看得见。
    /// </summary>
    [Fact]
    public void TheKeysThePageSends_AreThePropertiesThatBind()
    {
        var plan = PageKeys(@"'/plan', \{ (?<k>[A-Za-z0-9_]+):");
        Assert.True(plan.Count > 0, "从 Web/app.js 那句生成同步预览里取不出 endpointKeys 的键名：页面换了写法，本文件测的请求形状就是凭空的。");
        var http = HttpOptions();
        var planJson = JsonSerializer.Serialize(new CreatePlanRequest(), http);
        foreach (var key in plan)
        {
            Assert.Contains("\"" + key + "\":[]", planJson, StringComparison.Ordinal);
        }

        // 新建任务那一发：三个范围维度的键都在页面里，逐个要求序列化后的形状认得。
        var createBody = Regex.Match(Repo.Source("Web", "app.js"), @"api\('POST', '/api/tasks', \{(?<body>[^}]*)\}\)").Groups["body"].Value;
        Assert.True(createBody.Length > 0,
            "从 Web/app.js 取不出 POST /api/tasks 那一发的请求体：页面换了写法，本文件测的请求形状就是凭空的。");
        var taskKeys = Regex.Matches(createBody, @"(?<k>[A-Za-z][A-Za-z0-9_]+)\s*:").Cast<Match>().Select(m => m.Groups["k"].Value).ToList();
        var taskJson = JsonSerializer.Serialize(new CreateTaskRequest(), http);
        foreach (var key in new[] { "moduleNames", "pathPrefixes", "endpointKeys" })
        {
            Assert.True(taskKeys.Contains(key, StringComparer.Ordinal),
                $"页面 POST /api/tasks 那一发里没有「{key}」这个键（现读到 {string.Join("、", taskKeys)}）："
                + "页面不再送这一维了，本文件归一的这一条就得重判。");
            Assert.Contains("\"" + key + "\":", taskJson, StringComparison.Ordinal);
        }

        // 决策与补充那一发：页面把 GET /diffs 读回来的那份对象改完再 PUT 回去，
        // 所以它的属性名必须与序列化出来的键名同一批——两边都从真实类型现读，不抄第二份。
        var decisionKeys = PageKeys(@"decisions\.(?<k>[a-z][A-Za-z0-9]+)");
        var decisionJson = JsonSerializer.Serialize(new DecisionStore(), http);
        Assert.NotEmpty(decisionKeys);
        foreach (var key in decisionKeys)
        {
            Assert.Contains("\"" + key + "\":{}", decisionJson, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 那一发请求体在这个键上<b>真写了几个</b>：缺键与显式 null 都算零个——那正是本轮归一的目标形状。
    /// 期望值由这一份现读出来，不靠「字符串里有没有某个子串」猜（第一版就是这么猜的，
    /// 遇到「三个键都写成空清单」那一发当场猜错，红在期望 1 实得 0 上）。
    /// </summary>
    private static int Written(string json, string key)
    {
        var body = JsonNode.Parse(json)!.AsObject();
        if (!body.TryGetPropertyValue(key, out var value) || value is null) return 0;
        return value is JsonArray array ? array.Count : value.AsObject().Count;
    }

    /// <summary>
    /// ② 四种请求形状都绑得出<b>数得清</b>的清单或字典：没写那个键、显式写 null、写空清单三者同义，写了值就按值绑。
    /// <b>「显式 null」那一支就是本轮修之前每发都会撞 500 的那一发。</b>
    /// </summary>
    [Theory]
    [InlineData("{\"endpointKeys\":[\"GET /a\"]}", "plan")]
    [InlineData("{\"endpointKeys\":null}", "plan")]
    [InlineData("{\"endpointKeys\":[]}", "plan")]
    [InlineData("{}", "plan")]
    [InlineData("{\"moduleNames\":null,\"pathPrefixes\":null,\"endpointKeys\":null}", "task")]
    [InlineData("{\"moduleNames\":[],\"pathPrefixes\":[],\"endpointKeys\":[]}", "task")]
    [InlineData("{\"moduleNames\":[\"财务\"],\"pathPrefixes\":[\"/api/a\"],\"endpointKeys\":[\"GET /a\"]}", "task")]
    [InlineData("{\"items\":null,\"docChoices\":null,\"modelBindings\":null}", "decision")]
    [InlineData("{\"items\":{},\"docChoices\":{},\"modelBindings\":{}}", "decision")]
    [InlineData("{\"items\":{\"d1\":\"accepted\"}}", "decision")]
    [InlineData("{}", "decision")]
    [InlineData("{\"entries\":null}", "supplement")]
    [InlineData("{\"entries\":[]}", "supplement")]
    [InlineData("{\"entries\":[{\"resourceKey\":\"GET /a\",\"location\":\"summary\",\"field\":\"description\",\"value\":\"说明\"}]}", "supplement")]
    public void EveryShape_BindsToWhatTheServiceCanCount(string json, string kind)
    {
        var http = HttpOptions();
        if (kind == "plan")
        {
            var request = JsonSerializer.Deserialize<CreatePlanRequest>(json, http)!;
            // 这一句红 = POST /api/tasks/{id}/plan 又是一发 500：路由那句 ToList() 之前没有任何守卫。
            Assert.True(request.EndpointKeys is not null,
                $"{json} 绑定后 EndpointKeys 是 null：路由那句 request.EndpointKeys.ToList() 会抛未处理异常，"
                + "这一发拿到的是 500 分支那句「内部错误」，而不是它本该走到的「请至少选择一个接口。」");
            Assert.Equal(Written(json, "endpointKeys"), request.EndpointKeys.Count);
        }
        else if (kind == "task")
        {
            var request = JsonSerializer.Deserialize<CreateTaskRequest>(json, http)!;
            foreach (var (property, value) in new (string, List<string>)[]
                     {
                         ("moduleNames", request.ModuleNames), ("pathPrefixes", request.PathPrefixes),
                         ("endpointKeys", request.EndpointKeys),
                     })
            {
                Assert.True(value is not null,
                    $"{json} 绑定后 {property} 是 null：TaskStore.Create 头三句清单操作会抛未处理异常，"
                    + "那一发拿到的是 500 分支那句「内部错误」。");
                Assert.Equal(Written(json, property), value.Count);
            }
        }
        else if (kind == "decision")
        {
            var store = JsonSerializer.Deserialize<DecisionStore>(json, http)!;
            foreach (var (property, value) in new (string, Dictionary<string, string>)[]
                     {
                         ("items", store.Items), ("docChoices", store.DocChoices), ("modelBindings", store.ModelBindings),
                     })
            {
                Assert.True(value is not null,
                    $"{json} 绑定后 {property} 是 null：DecisionService 头三句 .Values 会抛未处理异常，"
                    + "那一发本可以走到逐值校验那三句 400。");
                Assert.Equal(Written(json, property), value.Count);
            }
        }
        else if (kind == "supplement")
        {
            var store = JsonSerializer.Deserialize<SupplementStore>(json, http)!;
            Assert.True(store.Entries is not null,
                $"{json} 绑定后 Entries 是 null：SupplementService 头一句 foreach 会抛未处理异常，"
                + "那一发本可以走到「示例不是合法 JSON。」「说明过长。」那两句拒绝。");
            Assert.Equal(Written(json, "entries"), store.Entries.Count);
        }
        else
        {
            Assert.Fail($"本门不认识这一类形状：{kind}。");
        }
    }

    /// <summary>
    /// ③ 三种「空」各按自己那一处的定义走：<b>必填未填 / 全部缺口 / 这一维不圈范围</b>。
    /// 这三份定义是本轮归一目标的依据——哪一处把定义改了，归一就得重判，所以钉在三处源码上而不写在测试里复述。
    /// </summary>
    [Fact]
    public void EmptyMeansDifferentThings_AtEachEntryPoint()
    {
        // 同步计划：空清单是「没选」，该拒绝——归一之后仍然走到这一句，语义一行未动。
        var plans = Repo.Source("Sync", "SyncPlanService.cs");
        Assert.Contains("if (endpointKeys.Count == 0) throw new UserException(\"请至少选择一个接口。\");", plans, StringComparison.Ordinal);

        // 草稿生成：空清单是「全部缺口」，第 212 轮就是靠这一句把简报给的「空就 throw」挡回去的。
        var drafts = Repo.Source("Comments", "DraftService.cs");
        Assert.Contains("slotIds.Count == 0 ? scan.Slots", drafts, StringComparison.Ordinal);

        // 新建任务：路径前缀与指定接口两者皆空 = 全项目。
        var tasks = Repo.Source("Tasks", "TaskStore.cs");
        Assert.Contains("task.PathPrefixes.Count == 0 && task.EndpointKeys.Count == 0", tasks, StringComparison.Ordinal);

        // 补充与决策：空 = 清空这一份，页面本来就能送出空对象，所以不是待拒的畸形输入。
        Assert.Contains("Content(existing) == Content(body)", Repo.Source("Sync", "SupplementService.cs"), StringComparison.Ordinal);
        Assert.Contains("Canonical.HashOf(existing) == Canonical.HashOf(body)", Repo.Source("Sync", "DecisionService.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// ④ <b>真执行</b>：把上面那些 <c>null</c> 形状绑出来，直接交给服务的第一句。
    /// 三条链都在真实 FileStore/TaskStore 上跑：新建任务、保存决策、保存补充。
    /// 修之前这三发全是 <see cref="NullReferenceException"/>，今天必须各走到自己的正常支路。
    /// </summary>
    [Fact]
    public async Task NullShapes_RunTheRealFirstLineOfEachService()
    {
        var http = HttpOptions();

        // POST /api/tasks：三个范围维度全 null → 建成一个全项目范围的任务，而不是 500。
        var create = JsonSerializer.Deserialize<CreateTaskRequest>(
            "{\"profileId\":\"" + _profile.Id + "\",\"name\":\"空形状\",\"moduleNames\":null,\"pathPrefixes\":null,\"endpointKeys\":null}", http)!;
        var task = _tasks.Create(create);
        Assert.Empty(task.PathPrefixes);
        Assert.Empty(task.EndpointKeys);
        Assert.True(TaskStore.InScope(task, "GET /any", "/api/anything"),
            "三个范围维度都为 null 形状建出来的任务不再覆盖全项目：归一目标变了，本文件 ② 的那三种同义就得重判。");

        // PUT /api/tasks/{id}/decisions：先存一份非空决策，再送三个 null —— 那一发是「清空」，正常形状。
        var decisions = new DecisionService(_tasks);
        var seeded = new DecisionStore();
        seeded.Items["d1"] = "accepted";
        await decisions.SaveAsync(task.Id, seeded, default);
        var revisionAfterSeed = _tasks.Get(task.Id).Revision;

        var clearing = JsonSerializer.Deserialize<DecisionStore>("{\"items\":null,\"docChoices\":null,\"modelBindings\":null}", http)!;
        var cleared = await decisions.SaveAsync(task.Id, clearing, default);
        Assert.Empty(cleared.Items);
        var reread = _tasks.Read<DecisionStore>(task.Id, TaskFiles.Decisions)!;
        Assert.Empty(reread.Items);
        Assert.Equal(revisionAfterSeed + 1, _tasks.Get(task.Id).Revision);

        // PUT /api/tasks/{id}/supplement：entries 为 null → 空条目集，正常保存。
        var supplements = new SupplementService(_tasks);
        var body = JsonSerializer.Deserialize<SupplementStore>("{\"entries\":null}", http)!;
        var saved = await supplements.SaveAsync(task.Id, body, default);
        Assert.Empty(saved.Entries);
    }

    /// <summary>
    /// ⑤ 简报点名的 <c>ContractReviewRequest</c>：<b>两个标量属性都不构成 500</b>——显式 null 落在本该给的拒绝上。
    /// 这一条真跑，是为了把「这一处读完确认无缺陷」写成一条会红的门，而不是 NOTES 里的一句话。
    /// </summary>
    [Fact]
    public async Task ContractReview_NullStrings_LandOnTheGuardedBranches()
    {
        var task = _tasks.Create(new CreateTaskRequest { ProfileId = _profile.Id, Name = "契约审阅形状" });
        WriteContracts(task.Id,
        [
            new ResponseContract { EndpointKey = "GET /a", WrapperType = "Result", DataMember = "Data", ReviewState = "pending" },
        ]);

        var reviews = new ContractReviewService(_tasks);

        // 状态为 null：走到那句拒绝，而不是往下赋值。
        var (keyWithState, nullState) = BoundScalars("{\"endpointKey\":\"GET /a\",\"reviewState\":null}");
        Assert.Equal("GET /a", keyWithState);
        // 递给服务的这对值就是路由递出去的那对：DTO 把它们声明成非可空 string，运行时里面是 null，
        // null-forgiving 的 ! 写的正是那一刻的形状，不是把警告压掉（本仓要求 0 警告）。
        var state = await Assert.ThrowsAsync<UserException>(
            () => reviews.ReviewAsync(task.Id, keyWithState!, nullState!, default));
        Assert.Equal("审阅状态无效。", state.Message);
        Assert.Equal(400, state.StatusCode);

        // 身份为 null：比不上任何一条契约，落在 404 那句，不抛 NRE。
        var (nullKey, confirmed) = BoundScalars("{\"endpointKey\":null,\"reviewState\":\"confirmed\"}");
        var missing = await Assert.ThrowsAsync<UserException>(
            () => reviews.ReviewAsync(task.Id, nullKey!, confirmed!, default));
        Assert.Equal("响应契约不存在。", missing.Message);
        Assert.Equal(404, missing.StatusCode);

        // 同一发数据整体上再走一次：两个标量都是 null 也不许变成 500。
        var (bothNullKey, bothNullState) = BoundScalars("{\"endpointKey\":null,\"reviewState\":null}");
        await Assert.ThrowsAsync<UserException>(
            () => reviews.ReviewAsync(task.Id, bothNullKey!, bothNullState!, default));

        // 缺键那一发：绑出来的是属性初始值（空串），走的仍是同一句拒绝。
        var (absentKey, absentState) = BoundScalars("{}");
        Assert.Equal("", absentKey);
        await Assert.ThrowsAsync<UserException>(
            () => reviews.ReviewAsync(task.Id, absentKey!, absentState!, default));
    }

    /// <summary>
    /// 把一发请求体取成路由真正传给 <c>ContractReviewService</c> 的那对值。
    /// <para>
    /// <c>ContractReviewRequest</c> 声明在 <c>Api/ApifoxRoutes.cs</c>，那份文件依赖 ASP.NET Core，
    /// Linux 侧编不进测试工程（与第 212 轮的 <c>MaterialRequest</c> 同一处境）：DTO 自身的形状由
    /// <see cref="BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred"/> 判，这里判的是拿到那对值之后服务怎么走。
    /// 取法照 System.Text.Json 绑定非可空 <c>string</c> 属性的同一条规则——缺键留属性初始值、显式 null 绑成 null，
    /// 不是另写一份口径；两者本来就在本轮那份最小 ASP.NET 工程里对过一次。
    /// </para>
    /// </summary>
    private static (string? EndpointKey, string? ReviewState) BoundScalars(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        return (Read(node, "endpointKey"), Read(node, "reviewState"));

        static string? Read(JsonObject body, string name) =>
            body.TryGetPropertyValue(name, out var value) ? value?.GetValue<string>() : "";
    }

    /// <summary>按 <c>Source/SourceModels.cs</c> 的落盘形状把契约写进任务目录（走 TaskStore.Write 这一条真实口径，不手抄 JSON）。</summary>
    private void WriteContracts(string taskId, List<ResponseContract> contracts) =>
        _tasks.Write(taskId, TaskFiles.ResponseContract, contracts);

    /// <summary>
    /// ⑥ 归一不是一句自述：每个登记过的属性在源码里都必须是 <c>value ?? new(…)</c> 的 setter，
    /// 且<b>不得退回带初始值的自动属性</b>——那正是本轮修掉、第 212 轮也修掉过的形状（初始值只挡得住缺键）。
    /// </summary>
    [Fact]
    public void RegisteredShapes_HaveNotSlidBackToAnInitializedAutoProperty()
    {
        foreach (var shape in Normalized.Concat(FixedEarlierRound))
        {
            var source = Repo.Source(shape.File.Split('/'));
            var body = ClassBody(source, shape.Type);
            var set = Regex.Match(body, @"public\s+(?:List|Dictionary)[^(]*?\b" + shape.Property + @"\s*\r?\n?\s*\{(?<access>[^}]*)\}", RegexOptions.Singleline).Groups["access"].Value;

            Assert.True(set.Length > 0,
                $"{shape.File} 的 {shape.Type}.{shape.Property} 取不出属性体：它不再是「List/Dictionary + 属性块」这个形状了，"
                + "本门与 ② 的归一目标都要重判。");
            Assert.Contains("get =>", set, StringComparison.Ordinal);
            Assert.Contains("set =>", set, StringComparison.Ordinal);
            Assert.Contains("value ?? new", set, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(body, @"public\s+(?:List|Dictionary)<[^>]*>\s+" + shape.Property + @"\s*\{\s*get;\s*set;\s*\}\s*=\s*new", RegexOptions.Singleline),
                $"{shape.Type}.{shape.Property} 又退回带初始值的自动属性：请求体显式写 null 时它会绑成 null，"
                + $"{shape.Route} 那一发就是一发 500（初始值只挡得住「没写这个键」，挡不住「写了 null」）。");
        }

        // 归一那一句里带着比较器：新实例与「显式 null 归一出来的那份」都必须还是序逐字那一份。
        // 带值时 System.Text.Json 自己建字典、不走这个 setter，那一条路不由本门管（字符串键的默认比较器同为序逐字）。
        var nulled = JsonSerializer.Deserialize<DecisionStore>(
            "{\"items\":null,\"docChoices\":null,\"modelBindings\":null}", HttpOptions())!;
        foreach (var (label, dictionary) in new[]
                 {
                     ("Items", new DecisionStore().Items), ("Items", nulled.Items),
                     ("DocChoices", new DecisionStore().DocChoices), ("DocChoices", nulled.DocChoices),
                     ("ModelBindings", new DecisionStore().ModelBindings), ("ModelBindings", nulled.ModelBindings),
                 })
        {
            Assert.True(ReferenceEquals(dictionary.Comparer, StringComparer.Ordinal),
                $"归一出来的 {label} 不再带逐字比较器：setter 里那句 value ?? new(StringComparer.Ordinal) 被改成默认构造，"
                + "决策键（diffId、DocChange.Id、源模型名）的比对口径就跟着当前区域动了。");
        }
    }

    /// <summary>
    /// ⑦ 各条链的<b>第一句仍然直接摸那些清单</b>：本轮的修法装在绑定处，路由没有多写一层守卫。
    /// 这一句在，⑥ 的形状才是这条链的守门人；这一句改了，本门要红一次，让下一轮重新看归一目标还在不在。
    /// </summary>
    [Fact]
    public void Routes_StillHandThoseCollectionsToTheServiceFirstLine()
    {
        var apifoxRoutes = Repo.Source("Api", "ApifoxRoutes.cs");
        Assert.Contains("request.EndpointKeys.ToList()", apifoxRoutes, StringComparison.Ordinal);

        var taskStore = Repo.Source("Tasks", "TaskStore.cs");
        Assert.Contains("foreach (var name in request.ModuleNames)", taskStore, StringComparison.Ordinal);
        Assert.Contains("request.PathPrefixes.Select(", taskStore, StringComparison.Ordinal);
        Assert.Contains("request.EndpointKeys.Select(", taskStore, StringComparison.Ordinal);

        var decisionService = Repo.Source("Sync", "DecisionService.cs");
        Assert.Equal(3, Regex.Matches(decisionService, @"body\.(?:Items|DocChoices|ModelBindings)\.Values").Count);

        var supplementService = Repo.Source("Sync", "SupplementService.cs");
        Assert.Contains("foreach (var entry in body.Entries)", supplementService, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑧ 这一族<b>扫过的面是封闭的</b>：把 <c>Api/*.cs</c> 路由里绑成请求体的形状类型全数点出来，
    /// 每一个必须落在 <see cref="Normalized"/> / <see cref="ScalarOnly"/> / <see cref="Deferred"/> 三处之一，
    /// 三处里的类型也必须<b>仍然真被绑在某条路由上</b>（改了路由名或删了 DTO，清单不许留着冒充还在扫）。
    /// <para>
    /// <b>它按命名判，不按 DI 判</b>：只认 <c>*Request</c> 后缀与点名的三个存储/配置类型。
    /// 一个叫 <c>Foo</c> 而不叫 <c>FooRequest</c> 的新请求体类型不会自动被这一条点名——这一句写在
    /// NOTES 那一节里，是为了让下一轮把它读成「这把门的已知边界」，不是读成「全仓已扫净」。
    /// </para>
    /// </summary>
    [Fact]
    public void BodyBoundShapes_AreEachRegisteredOrDeliberatelyDeferred()
    {
        var bound = BoundShapeTypes();

        var registered = Normalized.Concat(FixedEarlierRound).Select(s => s.Type).Concat(ScalarOnly).Concat(Deferred)
            .Distinct(StringComparer.Ordinal).ToList();
        foreach (var type in registered)
        {
            Assert.True(bound.Contains(type, StringComparer.Ordinal),
                $"形状表登记了 {type}，但 Api/*.cs 的路由里已经不再把它绑成请求体：清单要跟链路一起退场，"
                + "留着等于让下一轮以为这一族还在。");
        }

        foreach (var type in bound)
        {
            Assert.True(registered.Contains(type, StringComparer.Ordinal),
                $"{type} 被 Api/*.cs 绑成请求体，却不在本轮清扫的三份清单里的任何一处："
                + "它带不带清单？带上就得归一，不带就登记进标量族，都不写就是这一族悄悄漏出一格。");
        }

        // 标量族的判据：这一族之所以不归一，是因为它们至今没有清单属性。加一个就得归一。
        foreach (var type in ScalarOnly)
        {
            var body = ClassBodyOfShapeType(type);
            Assert.False(Regex.IsMatch(body, @"public\s+(?:List|Dictionary)<"),
                $"{type} 出现了清单属性，却被留在「只有标量」那一格里：那一格的依据就是「没有清单可归一」，"
                + "现在它有了，就得进归一清单并补上真往返断言。");
        }

        // 挂账那一格不许悄悄消失：NOTES 的清扫一节必须点名它，并带着「为什么不归一」。
        var notes = Repo.Source("docs", "NOTES.md");
        var section = Section(notes, SweepSection);
        foreach (var type in Deferred)
        {
            Assert.True(section.Contains(type, StringComparison.Ordinal),
                $"docs/NOTES.md「{SweepSection}」里没有 {type}：这一格本轮判定不改，但它同族、也真会 500，"
                + "账不在这一节里消失，就得在那一节里带着理由在位。");
        }

        Assert.Contains("归一目标不统一", section, StringComparison.Ordinal);
        Assert.Contains("它按命名判，不按 DI 判", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑨ 归一不得改网络形状：这八个属性序列化出去的键名与键序照旧，
    /// 原生壳与过渡壳读的都是同一批键（属性从自动属性改成带后备字段的属性，JSON 形状应当一字不动）。
    /// </summary>
    [Fact]
    public void Normalization_DoesNotChangeTheWireShape()
    {
        var http = HttpOptions();
        Assert.Equal("""{"endpointKeys":[]}""", JsonSerializer.Serialize(new CreatePlanRequest(), http));
        Assert.Equal("""{"profileId":"","moduleNames":[],"pathPrefixes":[],"endpointKeys":[],"offlineMode":false}""",
            JsonSerializer.Serialize(new CreateTaskRequest { ProfileId = "" }, http));
        Assert.Equal("""{"items":{},"docChoices":{},"modelBindings":{}}""", JsonSerializer.Serialize(new DecisionStore(), http));
        Assert.Equal("""{"entries":[]}""", JsonSerializer.Serialize(new SupplementStore(), http));

        // 原生壳读写的也是同一批键：这几处属性名改了，本门 ① 之外还要在这里红一次。
        var native = Repo.Source("Desktop", "Native", "TasksPage.cs");
        Assert.Contains("EndpointKeys = TaskCreation.LineValues(_keys.Text)", native, StringComparison.Ordinal);
    }

    /// <summary>ASP.NET Core 自己的参数类型：它们以 <c>Request</c> 结尾，但不是「从请求体绑出来的形状」。</summary>
    private static readonly string[] FrameworkParameters = ["HttpContext", "HttpRequest", "HttpResponse", "CancellationToken"];

    /// <summary>路由里绑成请求体的形状类型（按 <c>*Request</c> 后缀与点名的存储/配置类型认）。</summary>
    private static HashSet<string> BoundShapeTypes()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Repo.ServiceSources().Where(s => s.Relative.StartsWith("Api/", StringComparison.Ordinal)))
        {
            foreach (Match lambda in Regex.Matches(source.Text, @"app\.Map[A-Za-z]+\s*\(\s*""[^""]*""\s*,\s*(?:async\s*)?\((?<params>[^)]*)\)\s*=>"))
            {
                foreach (var parameter in lambda.Groups["params"].Value.Split(','))
                {
                    var type = parameter.Trim().Split(' ')[0];
                    // 框架类型以 Request 结尾纯属撞名：HttpRequest 是请求本身，不是从请求体绑出来的形状。
                    if (type.Length == 0 || FrameworkParameters.Contains(type, StringComparer.Ordinal)) continue;
                    if (type.EndsWith("Request", StringComparison.Ordinal)
                        || type is "DecisionStore" or "SupplementStore" or "ProjectProfile")
                    {
                        names.Add(type);
                    }
                }
            }
        }

        return names;
    }

    /// <summary>
    /// 在形状所在的那份文件里取类体（到<b>第一个</b>顶格 <c>}</c> 之前）。
    /// 懒惰匹配是必须的：贪婪会把后面别的类一起圈进来，而 <c>CheckTask.EndpointKeys</c> 至今仍是带初始值的
    /// 自动属性——那样 <see cref="RegisteredShapes_HaveNotSlidBackToAnInitializedAutoProperty"/>
    /// 会红在隔壁那一格上，念起来却像在说 <c>CreateTaskRequest</c>。
    /// </summary>
    private static string ClassBody(string source, string type)
    {
        var match = Regex.Match(source, @"\npublic sealed class " + type + @"\b(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(match.Groups["body"].Success, $"取不出 {type} 的类体：它不再是「public sealed class + 顶格收尾」这个形状。");
        return match.Groups["body"].Value;
    }

    /// <summary>标量族类型的类体（按类型名在各服务源码里找，找到即取）。</summary>
    private static string ClassBodyOfShapeType(string type)
    {
        foreach (var source in Repo.ServiceSources())
        {
            var match = Regex.Match(source.Text, @"\npublic sealed class " + type + @"\b(?<body>.*?)\n\}", RegexOptions.Singleline);
            if (match.Groups["body"].Success) return match.Groups["body"].Value;
        }

        Assert.Fail($"全仓找不到 {type} 的类声明：它被登记在「只有标量」那一格，却已经不在源码里了。");
        return "";
    }

    /// <summary>取某个二级标题那一节的正文（到下一个二级标题之前）。</summary>
    private static string Section(string notes, string heading)
    {
        var start = notes.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/NOTES.md 缺少「{heading}」一节（本轮清扫的形状表与挂账落点）。");
        var rest = notes[(start + heading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }
}
