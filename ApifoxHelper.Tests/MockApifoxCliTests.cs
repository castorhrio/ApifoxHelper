using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ApifoxHelper.Apifox;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 写入安全红线的本地 mock CLI 端到端（第一百七十二轮，简报 P0-1）：
/// SHARED-3「只写计划内资源」、SHARED-4「执行前重读并核对指纹」、SHARED-7「创建防重与 ID 回绑」、
/// SHARED-5/8「部分失败即停」四条，第一次在同一条真实进程边界上跑通并常驻本仓。
/// <para>
/// <b>补的是哪个洞</b>：这四条是产品最核心的对外承诺，此前的证据全是「口径真值表」——勾选怎么算、
/// 文案怎么说、日志什么形状，都对，但没有一条把「确认之后的执行器真的只碰计划里那几个资源、
/// 远端被人改过就拒绝覆盖、创建没回 ID 就按身份绑回去、第二个动作失败第三个就不许发出」串起来跑过一次。
/// 之前的每一轮都靠 <c>/tmp</c> 一次性控制台跑一遍然后把工程删掉，于是「跑过」这件事每轮都得重新相信一次。
/// 这一轮把它落成仓内用例。
/// </para>
/// <para>
/// <b>真与假的分界</b>：真——<see cref="ProcessRunner"/> 起真子进程、<see cref="ApifoxCli"/> 真解析输出、
/// <see cref="SyncExecutor"/> 一行未改、任务目录 / 计划落盘 / 执行日志 / 本地锁 / 写入兼容登记全是真实实现。
/// 假——只有 Apifox 服务端，换成 <c>MockApifoxCli.js</c>：它按 <c>docs/NOTES.md</c>「已核实的环境事实」记下的
/// 协议回答命令（前导说明行 + 单个 JSON 对象、退出码 0/1/3、404000 表示不存在、update 整体替换顶层字段），
/// 并把每一句收到的命令记下来供断言。产品源码零改动；不联网、不碰任何真实项目。
/// </para>
/// <para>
/// <b>这条链不能说明什么</b>：它证明不了真实 Apifox 接受这些写入（受保护设置能不能保留、创建回不回 ID、
/// 错误与限流的完整表现形式，方案 §4.3 至今按「未知」处理）。所以验收表里 SHARED-3/4/7/8 仍留 <c>[ ]</c>，
/// 关闭它们的路径是「G-D5 真机复核清单」第 6 条。这里跑出来的每一条结论都按这个边界写，不越界一步。
/// </para>
/// <para>
/// <b>第一百七十三轮补的两支</b>：① <c>Shared2_StaleConfirmedHash_IsRefusedBeforeAnyCliCall</c>——执行器的
/// 确认哈希绑定（<c>Sync/SyncExecutor.cs:22</c>）在此之前整仓零断言，而它正是「确认的那一份就是执行的那一份」
/// 唯一的服务层落点；② <c>Boundary_TheNativeConfirmPathRunsThisVeryChain</c>——这一族证明的是
/// <see cref="SyncExecutor"/> 的行为，用户点的却是原生壳那一颗按钮，两者断了线的话全部「端到端」
/// 都在给一条没人走的路径作证。
/// </para>
/// <para>
/// <b>仪器先证明自己看得见</b>（与 <see cref="HtmlWrapTests"/> 同一纪律）：<see cref="Protocol_TheMockSpeaksTheRealCliShape"/>
/// 先量协议本身——版本号读得到、「不存在」确实是 404000、输出多一行就判不可靠解析、未知命令按退出码 3 失败。
/// 这四支任何一支不成立，上面那些「没写到 / 停住了 / 绑上了」都可能只是仪器是瞎的。
/// </para>
/// </summary>
public class MockApifoxCliTests
{
    private const string NotesHeading = "## 本地 mock CLI 端到端（写入安全红线）";

    /// <summary>node 缺席时挂账必须写明的那一行——与浏览器探针同一份纪律：没跑不许算成跑过。</summary>
    private const string LastRunMarker = "**上一次本地 mock CLI 端到端真跑**";

    /// <summary>这一族用例诞生的那一天（第一百七十二轮）。登记早于它，记的就是一次不可能发生过的运行。</summary>
    private const string LastRunFloorDate = "2026-11-11";

    private static readonly CancellationToken None = CancellationToken.None;

    /// <summary>
    /// 本机没有 node 时这一族门退成「挂账在位」：查的是这笔账有没有如实登记，
    /// 而不是把「本机没跑」算成「红线成立」。
    /// </summary>
    private static bool RequireNodeOrRegisteredGap()
    {
        if (MockCliRun.Node is not null) return true;
        AssertLocalRunIsRegistered(Repo.Source("docs", "NOTES.md"));
        return false;
    }

    /// <summary>
    /// 「上一次本地 mock CLI 端到端真跑」那一行<b>不分路径</b>都要核。
    /// <para>
    /// <b>存在的理由与第一百七十轮那一族相同</b>：node 长期在位时，降级支永远不会被执行，
    /// 那行账被写坏之后会一路绿到哪天真的没有 node——而那天正是它唯一有用的日子。
    /// 三件事依次核：那一行在、写成「第X轮 · YYYY-MM-DD」、这一对轮次与日期能在 NOTES 里指到一条
    /// <b>真实存在的轮次标题</b>。第三条是这一支唯一有分量的：把日期改大让门闭嘴，
    /// 得先凭空写出那一轮的记录（同一口径见 <see cref="AcceptanceChecklistTests"/>「引用必须落到实物」）。
    /// </para>
    /// </summary>
    [Fact]
    public void RegisteredGap_NamesTheLastLocalRun() => AssertLocalRunIsRegistered(Repo.Source("docs", "NOTES.md"));

    private static void AssertLocalRunIsRegistered(string notes)
    {
        Assert.True(notes.Contains(NotesHeading, StringComparison.Ordinal),
            $"NOTES 里找不到「{NotesHeading}」：跑不了本地 mock CLI 这件事没有被记成账，那就不是降级，是静默通过。");
        Assert.Contains(MockCliRun.NodeLookupHint, notes, StringComparison.Ordinal);

        var line = notes.Split('\n').FirstOrDefault(l => l.Contains(LastRunMarker, StringComparison.Ordinal));
        Assert.True(line is not null,
            $"「{NotesHeading}」一节里没有「{LastRunMarker}」那一行：这一族用例最近一次真跑没有登记——"
            + "node 在位时它是账本缺一行，node 不在位时它就是静默降级，两种都不许过。"
            + $"那一行写成「{LastRunMarker}：第X轮 · 四位年-月-日（…）」即可。");

        var match = Regex.Match(line, @"第(?<round>[零一二三四五六七八九十百]+)轮\s*·\s*(?<date>\d{4}-\d{2}-\d{2})");
        Assert.True(match.Success,
            $"「{LastRunMarker}」那一行没写成「第X轮 · YYYY-MM-DD」，读不出是哪一轮的哪一天：{line}");

        var round = match.Groups["round"].Value;
        var date = match.Groups["date"].Value;
        // ISO 日期按字典序比即按时间比；早于这一族用例诞生的那一天，记的就是一次不可能发生过的运行。
        Assert.True(string.CompareOrdinal(date, LastRunFloorDate) >= 0,
            $"登记的上一次真跑是 {date}，早于这一族用例诞生的 {LastRunFloorDate}：那一天还没有本地 mock CLI 端到端可记。");

        var pair = $"{date} 第{round}轮";
        Assert.True(notes.Split('\n').Any(l => l.StartsWith("## ", StringComparison.Ordinal) && l.Contains(pair, StringComparison.Ordinal)),
            $"登记的「第{round}轮 · {date}」在 NOTES 里没有以「{pair}」出现的轮次标题：这一轮没被记进日志，"
            + "端到端记录也就无处对账。");
    }

    /// <summary>
    /// 「不联网」的封闭禁用清单（第一百七十五轮，简报 P0-2）。
    /// <para>
    /// <b>为什么原来那一条不够</b>：原来判的是 <c>DoesNotContain("http")</c> 与
    /// <c>DoesNotContain("net.connect")</c>。字面 <c>http</c> 挡住了，而
    /// <c>require('dns')</c>、<c>tls.connect</c>、<c>WebSocket</c>、<c>fetch()</c> 一个都不在里面——
    /// 一个能被人加一句就绕开的门，等于在替「这个脚本会连外面的东西」作证。
    /// </para>
    /// </summary>
    private static readonly string[] OfflineForbiddenApis = ["net", "dns", "http", "https", "fetch", "WebSocket", "tls"];

    /// <summary>
    /// mock 脚本唯一允许加载的模块：文件系统与路径。多出一个就要在这里写下它为什么不算联网。
    /// <para>
    /// 这一份白名单补的是禁用清单<b>枚举不完</b>的那一半：第三方 HTTP 客户端（axios、undici、node-fetch）
    /// 的名字不可能全列出来，但它们在 JS 里只有一个入口——<c>require</c>。
    /// </para>
    /// </summary>
    private static readonly string[] OfflineAllowedModules = ["fs", "path"];

    /// <summary>
    /// mock 只许活在测试工程里：产品源码、主工程与冒烟工程都不许出现它的名字，脚本自己也不许联网。
    /// 这条不是形式主义——「测试用的假服务端被产品代码引用」一旦发生，上面所有断言就都不算了。
    /// <para>
    /// <b>两份联网判据各挡什么</b>：禁用清单挡<b>内置</b>网络 API（含 <c>node:</c> 前缀那种写法，
    /// 因为 <c>:</c> 是标识边界，<c>'net'</c> 照样被认出来）；模块白名单挡<b>任何</b> <c>require</c> 进来的
    /// 网络实现，并且要求参数是字符串字面量——<c>require('h' + 'ttp')</c> 与 <c>require(name)</c>
    /// 这种拼出来的模块名同样判红。<b>挡不住的那一种要如实说清</b>：脚本运行时自己生成再 eval 的字符串，
    /// 静态扫源码判不到；这一条与全部证据的边界同一（这是仓内 mock，不是网络审计工具）。
    /// </para>
    /// </summary>
    [Fact]
    public void Boundary_TheMockStaysInsideTheTestProject()
    {
        Assert.True(File.Exists(MockCliRun.SourceScript), $"mock CLI 脚本不在仓库里：{MockCliRun.SourceScript}。");
        var script = File.ReadAllText(MockCliRun.SourceScript);

        foreach (var api in OfflineForbiddenApis)
        {
            // 标识边界而不是子串：子串判法会因为脚本里出现 network 这种词而假红，
            // 而一条经常假红的门，下一轮只会被人放宽回没有门（本仓第一百六十九轮同一教训）。
            Assert.False(Regex.IsMatch(script, $@"\b{Regex.Escape(api)}\b", RegexOptions.IgnoreCase),
                $"mock 脚本里出现了网络 API「{api}」：这份假服务端必须只在本地文件系统上跑。");
        }

        var required = Regex.Matches(script, @"require\((?<arg>[^)]*)\)");
        foreach (Match match in required)
        {
            var arg = match.Groups["arg"].Value.Trim();
            var literal = Regex.Match(arg, @"^(['""])(?<name>[a-zA-Z0-9_/@\-.]+)\1$");
            Assert.True(literal.Success,
                $"mock 脚本里 require 的参数不是字符串字面量：{arg}——拼出来或传进来的模块名，白名单挡不住。");
            var name = literal.Groups["name"].Value;
            name = name.StartsWith("node:", StringComparison.Ordinal) ? name[5..] : name;
            Assert.Contains(name, OfflineAllowedModules, StringComparer.Ordinal);
        }

        Assert.Contains("不访问网络", script, StringComparison.Ordinal);

        var product = Directory.GetFiles(Repo.Root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Repo.Root, f).Replace('\\', '/'))
            .Where(r => !r.StartsWith("ApifoxHelper.Tests/", StringComparison.Ordinal)
                        && !r.Contains("/bin/", StringComparison.Ordinal)
                        && !r.Contains("/obj/", StringComparison.Ordinal))
            .ToList();
        Assert.True(product.Count > 100, $"扫到的产品源码文件只有 {product.Count} 个，扫描面不对。");
        foreach (var relative in product)
        {
            Assert.DoesNotContain("MockApifoxCli", File.ReadAllText(Path.Combine(Repo.Root, relative)), StringComparison.Ordinal);
        }
    }

    /// <summary>CLI 边界的实现必须真是产品那一份（共享编译），不是测试工程里的复刻。</summary>
    [Fact]
    public void Boundary_TheChainUnderTestIsTheProductsOwnCode()
    {
        var project = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        foreach (var shared in new[] { "Infrastructure\\ProcessRunner.cs", "Apifox\\ApifoxCli.cs", "Sync\\SyncExecutor.cs" })
        {
            Assert.Contains(shared, project, StringComparison.Ordinal);
        }

        Assert.True(typeof(SyncExecutor).Assembly == typeof(MockApifoxCliTests).Assembly,
            "SyncExecutor 不是共享编译进测试工程的同一份实现。");
        // 行首正则而不是子串 Contains：本文件自己就在消息里提到「class SyncExecutor」，
        // 子串判法会被自己的断言文字骗过（本仓第一百六十二轮同一教训）。
        var stubs = Directory.GetFiles(Path.Combine(Repo.Root, "ApifoxHelper.Tests"), "*.cs")
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"^[ \t]*(?:internal|public|file)\s+(?:sealed\s+)?class\s+SyncExecutor\b", RegexOptions.Multiline))
            .ToList();
        Assert.True(stubs.Count == 0,
            $"测试工程里出现了 SyncExecutor 的复刻（{string.Join("、", stubs.Select(Path.GetFileName))}）："
            + "端到端跑的就不再是产品那一份执行器了。");
    }

    /// <summary>
    /// 这条链的<b>作用范围</b>：用户点的是原生壳那一颗按钮，而上面六支证明的是 <see cref="SyncExecutor"/> 的行为，
    /// 中间断了线的话「端到端跑过」覆盖的就是条没人走的路。三件事一起钉——
    /// 那颗按钮调的是同一个 <c>ExecuteAsync</c>；它传的是计划自己的哈希（不是第二个现算的值）；
    /// 这一层自己一次都不直接碰 CLI 写入（写入边界的单一落点在服务层）。
    /// </summary>
    [Fact]
    public void Boundary_TheNativeConfirmPathRunsThisVeryChain()
    {
        var dialog = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
        Assert.Contains("_executor.ExecuteAsync(_taskId, hash, job)", dialog, StringComparison.Ordinal);
        Assert.Contains("var hash = plan.Hash;", dialog, StringComparison.Ordinal);
        foreach (var bypass in new[] { "UpdateEndpointAsync", "CreateEndpointAsync", "UpdateSchemaAsync", "CreateSchemaAsync", "Process.Start" })
        {
            Assert.DoesNotContain(bypass, dialog, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 同一条链的<b>另一半作用范围</b>（第一百七十四轮，简报 P0-1）：上一支钉的是原生壳那颗按钮，
    /// 而 <c>--web</c> 过渡壳的实际入口是 <c>Api/ApifoxRoutes.cs</c> 的两条 REST 路由。
    /// <para>
    /// <b>为什么这一支不是重复</b>：页面层已冻结不等于这条链不跑——答辩现场若用 <c>--web</c> 演示，
    /// 「确认同步」仍然要经过 <c>plan/apply</c>。冻结的是画面，不是执行路径。上一轮只补了原生那一头，
    /// REST 这一头当时是自查发现的缺口、留到本轮。
    /// </para>
    /// <para>
    /// 钉三件事：① <c>plan/apply</c> 调的就是端到端那一个 <c>executor.ExecuteAsync</c>（同一个注入的
    /// <see cref="SyncExecutor"/>，不是第二条实现）；② 交给它的哈希来自<b>请求体</b>那一个字段
    /// （<c>request.PlanHash</c>），不是在路由里现算的第二份——现算等于把「确认的那一份」重新定义成
    /// 「磁盘上此刻那一份」，SHARED-2 那条门禁当场失去对象；③ <c>verify</c> 只读核对不带哈希。
    /// 再加页面侧一句：过渡壳发的是 <c>{ planHash: p.hash }</c>，即它正在展示的那一份计划的哈希。
    /// </para>
    /// </summary>
    [Fact]
    public void Boundary_TheWebRoutePathRunsTheSameExecutorAndHash()
    {
        var routes = Repo.Source("Api", "ApifoxRoutes.cs");
        var apply = RouteBody(routes, "\"/api/tasks/{id}/plan/apply\"");
        var verify = RouteBody(routes, "\"/api/tasks/{id}/verify\"");

        Assert.Contains("SyncExecutor executor", apply, StringComparison.Ordinal);
        Assert.Contains("executor.ExecuteAsync(id, hash, job)", apply, StringComparison.Ordinal);
        Assert.Contains("var hash = request.PlanHash;", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Read<SyncPlan>", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("plan.Hash", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("HashOf(", apply, StringComparison.Ordinal);

        // 只读核对那一支：不带哈希（它按磁盘上的计划重读远端，不消费任何确认）。
        Assert.Contains("executor.VerifyAsync(id, job)", verify, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanHash", verify, StringComparison.Ordinal);

        // 这一层不许自己碰 CLI 写入——写入边界的单一落点在服务层（与原生那头同一判据）。
        foreach (var bypass in new[] { "UpdateEndpointAsync", "CreateEndpointAsync", "UpdateSchemaAsync", "CreateSchemaAsync", "Process.Start" })
        {
            Assert.DoesNotContain(bypass, apply, StringComparison.Ordinal);
            Assert.DoesNotContain(bypass, verify, StringComparison.Ordinal);
        }

        // 页面侧：过渡壳送出的是「它正在展示的那一份」的哈希，不是第二个现算的值。
        var page = Repo.Source("Web", "app.js");
        var applyCall = SingleJsLine(page, "'/plan/apply'");
        Assert.Contains("{ planHash: p.hash }", applyCall, StringComparison.Ordinal);
        var verifyCall = SingleJsLine(page, "'/verify'");
        Assert.DoesNotContain("planHash", verifyCall, StringComparison.Ordinal);
    }

    /// <summary>
    /// 请求体到执行器之间那一段<b>不是源文字对源文字</b>（第一百七十四轮，简报 P0-1）。
    /// <para>
    /// <c>{ planHash: p.hash }</c> 是 JS 对象字面量，路由读的是 <see cref="ApplyPlanRequest"/> 的属性；
    /// 两者中间隔着 ASP.NET Core 的请求体 JSON 绑定，而<b>上一支门对这一层是盲的</b>：页面把键名改成
    /// <c>planHashs</c>、或 DTO 属性改了名，两处源文都还在、那条 Contains 照样绿，而线上每一个确认都会
    /// 因为绑定不到而落成空串。空串本身不是漏洞（执行器照拒），漏洞是「静默地把每次确认都变成过期确认」。
    /// </para>
    /// <para>
    /// 所以这里用<b>产品自己那一份序列化口径</b>核：<c>Api/HelperHost.cs:42</c> 的
    /// <c>ConfigureHttpJsonOptions(o =&gt; JsonDefaults.Apply(o.SerializerOptions))</c> 起手于
    /// <c>JsonSerializerDefaults.Web</c>，与 <see cref="SerializationCaliberTests"/> 同一取法；
    /// 键名从 <c>Web/app.js</c> 那一句里正则取出来，不在测试里抄第二份。
    /// 如实说清边界：<c>Web</c> 默认大小写不敏感，所以键名<b>只差大小写</b>这一支本机判不出问题——
    /// 这一支挡的是名字漂移，不是拼写风格。
    /// </para>
    /// </summary>
    [Fact]
    public void Boundary_TheWebRequestBodyKeyBindsToTheFieldTheRouteReads()
    {
        var line = SingleJsLine(Repo.Source("Web", "app.js"), "'/plan/apply'");
        var key = Regex.Match(line, @"\{\s*(?<key>[A-Za-z0-9_]+)\s*:").Groups["key"].Value;
        Assert.True(key.Length > 0, $"从过渡壳那句 plan/apply 里取不出请求体的键名：{line}。");

        var http = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        JsonDefaults.Apply(http);
        var bound = JsonSerializer.Deserialize<ApplyPlanRequest>("{\"" + key + "\":\"abc123\"}", http);
        Assert.True(bound is not null);
        Assert.Equal("abc123", bound!.PlanHash);

        // 反向那一半：键名对不上时绑出来是空串而不是「凑巧一样的值」——下一支门钉的就是空串必须被拒绝。
        var mismatched = JsonSerializer.Deserialize<ApplyPlanRequest>("{\"notTheKey\":\"abc123\"}", http);
        Assert.Equal("", mismatched!.PlanHash);
    }

    /// <summary>
    /// 按<b>请求形状</b>跑到真实进程边界上（第一百七十四轮，简报 P0-1）：走的是
    /// <see cref="MockCliRun.ExecuteWithRequest"/>——它照搬路由体那一句，中间没有测试假件。
    /// 两支都要成立：<c>planHash</c> 缺失（请求体没带、绑定落成空串）时执行器在任何一条 CLI 命令发出之前
    /// 拒绝；带对了那一份就一路走到 <c>verified</c>。只测前一支会漏掉「整条 REST 路根本走不通」，
    /// 只测后一支会漏掉「空哈希被当成有效确认」。
    /// </summary>
    [Fact]
    public async Task Shared2_ARequestBodyWithoutTheConfirmedHash_IsRefusedAndWithItThePathCompletes()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var target = run.PutEndpoint("GET", "/api/orders", "订单列表");
        run.AddUpdate(target, Docs("订单列表（本次补充说明）"));
        var plan = await run.ConfirmPlan();
        var remoteBefore = run.Endpoint(target).ToJsonString();

        // ① 请求体没带 planHash：ASP.NET 绑定留下 DTO 默认值空串，执行器必须拒掉，且一条命令都不发。
        var empty = await Assert.ThrowsAsync<UserException>(() => run.ExecuteWithRequest(new ApplyPlanRequest()));
        Assert.Equal(409, empty.StatusCode);
        Assert.Equal("确认的计划与当前计划不一致，请重新预览。", empty.Message);
        Assert.Empty(run.WriteCalls());
        Assert.Equal(0, run.CallCount("endpoint.get"));
        Assert.Equal("awaitingConfirm", run.ReadPlan().Status);

        // ② 确认之后计划被换掉（加进第二个接口重新生成）：手上那串哈希就此过期，执行器照样拒。
        var extra = run.PutEndpoint("POST", "/api/orders", "创建订单");
        run.AddUpdate(extra, Docs("创建订单（确认之后加进来的）"));
        var stale = await run.ConfirmPlan();
        Assert.NotEqual(plan.Hash, stale.Hash);
        var ex = await Assert.ThrowsAsync<UserException>(() => run.ExecuteWithRequest(new ApplyPlanRequest { PlanHash = plan.Hash }));
        Assert.Equal(409, ex.StatusCode);
        Assert.Empty(run.WriteCalls());
        Assert.Equal(remoteBefore, run.Endpoint(target).ToJsonString());
        Assert.Equal("创建订单", run.Endpoint(extra)["description"]!.ToString());

        // ③ 带上当前这一份：同一条 REST 形状走完全程，两个动作都写入并回读核对通过。
        var done = await run.ExecuteWithRequest(new ApplyPlanRequest { PlanHash = stale.Hash });
        Assert.Equal("verified", done.Status);
        Assert.Equal(2, run.CallCount("endpoint.update"));
        Assert.Equal("订单列表（本次补充说明）", run.Endpoint(target)["description"]!.ToString());
        Assert.Equal("创建订单（确认之后加进来的）", run.Endpoint(extra)["description"]!.ToString());
    }

    /// <summary>
    /// 取出一整段路由体：从那条路由的字面量起，到下一条 <c>app.Map</c> 之前。
    /// 切片而不是全文 Contains——负向断言（「这一段里不许现算哈希」）只有在有界区间上才有意义：
    /// 全文判会把相邻无关路由判进去，改坏别处反而让这一条变红或变绿。
    /// </summary>
    private static string RouteBody(string routes, string routeLiteral)
    {
        var start = routes.IndexOf(routeLiteral, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Api/ApifoxRoutes.cs 里找不到路由 {routeLiteral}：过渡壳这条入口被改名或删掉了，那条链不再经过这里。");
        var next = routes.IndexOf("app.Map", start + routeLiteral.Length, StringComparison.Ordinal);
        return routes[start..(next < 0 ? routes.Length : next)];
    }

    /// <summary>页面里那条请求的那一行；出现 0 行或多行都判红——门不能对着一条不存在（或有两份）的调用说话。</summary>
    private static string SingleJsLine(string page, string needle)
    {
        var lines = page.Split('\n').Where(l => l.Contains(needle, StringComparison.Ordinal)).ToList();
        Assert.True(lines.Count == 1, $"Web/app.js 里含 {needle} 的行应有且只有一行，实际 {lines.Count} 行：{string.Join(" ／ ", lines)}。");
        return lines[0];
    }

    [Fact]
    public async Task Protocol_TheMockSpeaksTheRealCliShape()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();

        Assert.Equal(MockCliRun.CliVersion, await run.Cli.VersionAsync(run.Target, None));

        var missing = await run.Cli.GetEndpointAsync(run.Target, MockCliRun.ProjectId, MockCliRun.Branch, 999999, None);
        Assert.False(missing.Ok);
        Assert.Equal(MockCliRun.NotFoundCode, missing.ErrorCode);
        Assert.True(missing.IsNotFound, $"远端不存在应按 {MockCliRun.NotFoundCode} 回答，实际是：{missing.Describe()}");

        // 未知命令按「输入或校验失败」退出码 3 回答：不能当成不存在，也不能当成写成功。
        var folders = await run.Cli.ListFoldersAsync(run.Target, MockCliRun.ProjectId, MockCliRun.Branch, None);
        Assert.Equal(3, folders.ExitCode);
        Assert.False(folders.IsNotFound);
        Assert.False(folders.Ok);

        // 输出可靠性：JSON 之后多一行，整次回答就不可信——写入判定尤其不许拿它当依据。
        var probe = Path.Combine(run.Root, "probe.json");
        FileStore.WriteJson(probe, new JsonObject { ["description"] = "校验用的写入内容" });
        var (valid, message) = await run.Cli.ValidateAsync(run.Target, "endpoint-update", probe, None);
        Assert.True(valid, $"未加干扰时校验应通过，实际：{message}");
        run.EmitTrailingJunk();
        var (junkValid, junkMessage) = await run.Cli.ValidateAsync(run.Target, "endpoint-update", probe, None);
        Assert.False(junkValid);
        Assert.Contains("输出无法可靠解析", junkMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// SHARED-2「确认绑定计划」的实物那一半：原生壳传出去的只有 <c>plan.Hash</c> 这一个字符串，
    /// 而 <see cref="SyncExecutor"/> 靠它在加锁读计划的那一刻拒绝过期确认（<c>Sync/SyncExecutor.cs:22</c>）。
    /// 此前整仓对这条门禁没有任何断言——<c>SyncExecution.CanExecute</c> 的真值表管的是按钮亮不亮，
    /// 按钮挡不住「确认之后磁盘上的计划被另一处重新生成掉」。这一支把它跑到真实进程边界上：
    /// 拒绝必须发生在任何一条 CLI 命令发出<b>之前</b>，远端一个字节不动，计划状态也不许被推进到 <c>executing</c>。
    /// </summary>
    [Fact]
    public async Task Shared2_StaleConfirmedHash_IsRefusedBeforeAnyCliCall()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var first = run.PutEndpoint("GET", "/api/orders", "订单列表");
        var second = run.PutEndpoint("POST", "/api/orders", "创建订单");
        run.AddUpdate(first, Docs("订单列表（本次补充说明）"));
        run.AddUpdate(second, Docs("创建订单（本次补充说明）"));
        var captured = (await run.ConfirmPlan()).Hash;
        var remoteBefore = (run.Endpoint(first).ToJsonString(), run.Endpoint(second).ToJsonString());

        // 确认之后另一处重新生成了计划：落盘换了另一份，用户手上那串哈希就此过期。
        var third = run.PutEndpoint("GET", "/api/orders/{id}", "订单详情");
        run.AddUpdate(third, Docs("订单详情（本次补充说明）"));
        Assert.NotEqual(captured, (await run.ConfirmPlan()).Hash);

        var ex = await Assert.ThrowsAsync<UserException>(() => run.ExecuteWithHash(captured));

        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("确认的计划与当前计划不一致，请重新预览。", ex.Message);
        Assert.Empty(run.WriteCalls());
        Assert.Equal(remoteBefore, (run.Endpoint(first).ToJsonString(), run.Endpoint(second).ToJsonString()));
        Assert.Equal("awaitingConfirm", run.ReadPlan().Status);
        // 拒绝发生在读计划那一刻，连 CLI 版本都没去问：这条路径上一条命令都不该发出。
        Assert.Equal(0, run.CallCount("endpoint.get"));
    }

    /// <summary>
    /// SHARED-3 写入边界：远端有三个接口，计划只含两个——第三个连读都不该被读，
    /// 更不该被写；两个计划内资源写入后回读一致，且受保护设置（前后置脚本、Mock）原样还在。
    /// </summary>
    [Fact]
    public async Task Shared3_WritesHitExactlyThePlannedResources()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var first = run.PutEndpoint("GET", "/api/orders", "订单列表");
        var second = run.PutEndpoint("POST", "/api/orders", "创建订单");
        var unplanned = run.PutEndpoint("DELETE", "/api/orders/{id}", "删除订单");
        var unplannedBefore = run.Endpoint(unplanned).ToJsonString();

        run.AddUpdate(first, Docs("订单列表（本次补充说明）"));
        run.AddUpdate(second, Docs("创建订单（本次补充说明）"));
        var plan = await run.ConfirmPlan();
        var done = await run.Execute(plan);

        Assert.Equal("verified", done.Status);
        Assert.Equal(2, run.CallCount("endpoint.update"));
        Assert.All(run.WriteCalls(), call => Assert.Contains(" [description]", call, StringComparison.Ordinal));
        Assert.Contains(run.WriteCalls(), call => call.Contains(" " + first + " ", StringComparison.Ordinal));
        Assert.Contains(run.WriteCalls(), call => call.Contains(" " + second + " ", StringComparison.Ordinal));
        Assert.DoesNotContain(run.WriteCalls(), call => call.Contains(" " + unplanned + " ", StringComparison.Ordinal));
        Assert.True(run.Calls().All(call => call["id"]?.ToString() != unplanned.ToString()),
            $"计划外资源被 CLI 碰过：\n{run.CallLog()}");
        Assert.Equal(unplannedBefore, run.Endpoint(unplanned).ToJsonString());

        Assert.Equal("订单列表（本次补充说明）", run.Endpoint(first)["description"]!.ToString());
        Assert.True(run.Endpoint(first).ContainsKey("script"), "受保护的前后置脚本在写入后不见了。");
        Assert.True(run.Endpoint(first).ContainsKey("mock"), "受保护的 Mock 配置在写入后不见了。");
        Assert.Equal("成功", run.Endpoint(first)["responses"]!.AsArray()[0]!["description"]!.ToString());

        var task = run.ReadTask();
        Assert.Equal("verified", task.Stages.Remote.Status);
        Assert.Equal(2, task.Stages.Remote.ActionsVerified);
        Assert.Equal(0, task.Stages.Remote.ActionsNotRun);
        // 审计记录：计划首尾各一行，每个动作意图与结果各一行。
        Assert.Equal(6, File.ReadAllLines(run.Tasks.FilePath(run.Task.Id, ApifoxHelper.Tasks.TaskFiles.Journal)).Length);
    }

    /// <summary>
    /// SHARED-3 的另一半：平台没能保留受保护设置时，回读核对必须抓到它——这条红线是
    /// 「无法可靠保留受保护设置的相关写入能力不开放」。动作判 verifyFailed，计划不得显示成全部核对通过。
    /// </summary>
    [Fact]
    public async Task Shared3_ProtectedSettingsLost_VerifyFailsAndNeverClaimsSuccess()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var target = run.PutEndpoint("GET", "/api/orders", "订单列表");
        run.DropProtectedOnWrite(target);

        run.AddUpdate(target, Docs("订单列表（本次补充说明）"));
        var plan = await run.ConfirmPlan();
        var done = await run.Execute(plan);

        Assert.Equal("verifyFailed", done.Actions[0].State);
        Assert.Contains("未计划修改的字段发生了变化（受保护设置未能保留）", done.Actions[0].Message, StringComparison.Ordinal);
        Assert.Equal("failed", done.Status);
        Assert.Equal(1, run.CallCount("endpoint.update"));
        var (text, tone) = SyncExecution.ResultText(done.Status, Verified(done), Failed(done), NotRun(done));
        Assert.Equal("bad", tone);
        Assert.DoesNotContain("全部核对", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// SHARED-4 冲突检测：计划生成之后别人改了同一个接口。执行前重读、指纹不符，就必须拒绝覆盖——
    /// 一个写入字节都不许发出去，远端保持别人改过的那一版。
    /// </summary>
    [Fact]
    public async Task Shared4_RemoteChangedAfterPlanIsBuilt_ConflictsAndNeverOverwrites()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var target = run.PutEndpoint("GET", "/api/orders", "订单列表");
        run.AddUpdate(target, Docs("订单列表（本次补充说明）"));
        var plan = await run.ConfirmPlan();

        run.OverrideEndpoint(target, detail => detail["description"] = "别人在确认之后改了它");

        var done = await run.Execute(plan);
        Assert.Equal("conflict", done.Actions[0].State);
        Assert.Equal("目标在确认后被修改，计划过期，未覆盖新修改。", done.Actions[0].Message);
        Assert.Equal("failed", done.Status);
        Assert.Empty(run.WriteCalls());
        Assert.Equal("别人在确认之后改了它", run.Endpoint(target)["description"]!.ToString());
        // 重读确实发生了：没有这一步，「冲突」就只是凭空声称。
        Assert.True(run.CallCount("endpoint.get") >= 1, $"执行前没有重读远端：\n{run.CallLog()}");
    }

    /// <summary>
    /// SHARED-7 创建防重之一：创建成功但 CLI 没回新资源 ID（真实 CLI 到底回不回，方案 §4.3 至今按未知处理）。
    /// 执行必须按身份（方法 + 精确路径）回读出那唯一一条并把 ID 绑上，而不是停在「创建了但不知道是哪个」。
    /// </summary>
    [Fact]
    public async Task Shared7_CreateWithoutReturnedId_BindsTheIdentityFromReadback()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        run.PutEndpoint("GET", "/api/orders", "订单列表");
        run.CreateReturnsNoId();

        run.AddCreate(NewEndpoint("get", "/api/orders/export", "导出订单"));
        var plan = await run.ConfirmPlan();
        var done = await run.Execute(plan);

        Assert.Equal("verified", done.Actions[0].State);
        var createdId = done.Actions[0].CreatedId;
        Assert.NotNull(createdId);
        Assert.Equal("导出订单", run.Endpoint(createdId!.Value)["name"]!.ToString());
        Assert.True(run.CallCount("endpoint.list") >= 1, $"没有按身份回读过：\n{run.CallLog()}");
        Assert.Equal(1, run.CallCount("endpoint.create"));
        Assert.Equal("verified", done.Status);
        // 新建接口要绑出来才谈得上后续核对：重新核对（只读）在同一份远端上仍判通过。
        var reverified = await run.Verify();
        Assert.Equal("verified", reverified.Actions[0].State);
    }

    /// <summary>
    /// SHARED-7 创建防重之二：同身份资源已经存在时，创建动作一次都不许发出去——
    /// 「防重」的全部含义就是这一条：复查在先，宁可停下也不重复建。
    /// </summary>
    [Fact]
    public async Task Shared7_SameIdentityAlreadyRemote_NeverCreatesTwice()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var existing = run.PutEndpoint("POST", "/api/orders", "创建订单");

        run.AddCreate(NewEndpoint("post", "/api/orders", "创建订单"));
        var plan = await run.ConfirmPlan();
        var done = await run.Execute(plan);

        Assert.Equal("conflict", done.Actions[0].State);
        Assert.Contains($"远端已存在同身份资源（ID {existing}），未重复创建，请重新检查。", done.Actions[0].Message, StringComparison.Ordinal);
        Assert.Equal(0, run.CallCount("endpoint.create"));
        Assert.Equal(1, EndpointsWith(run, "post", "/api/orders"));
        Assert.Equal("failed", done.Status);
    }

    /// <summary>
    /// SHARED-5/8 部分失败：三个动作，第二个必然失败。第一个如实记已完成，第二个如实记失败，
    /// 第三个连命令都不许发出去；计划状态是 partial，状态行不得出现「全部核对」，失败的那一次也不许重发。
    /// </summary>
    [Fact]
    public async Task Shared5_SecondActionFails_ThirdNeverReachesTheCli()
    {
        if (!RequireNodeOrRegisteredGap()) return;
        using var run = new MockCliRun();
        var first = run.PutEndpoint("GET", "/api/orders", "订单列表");
        var second = run.PutEndpoint("POST", "/api/orders", "创建订单");
        var third = run.PutEndpoint("GET", "/api/orders/{id}", "订单详情");
        var thirdBefore = run.Endpoint(third).ToJsonString();

        run.AddUpdate(first, Docs("订单列表（本次补充说明）"));
        run.AddUpdate(second, Docs("创建订单（本次补充说明）"));
        run.AddUpdate(third, Docs("订单详情（本次补充说明）"));
        run.FailWrites(second);

        var plan = await run.ConfirmPlan();
        var done = await run.Execute(plan);

        Assert.Equal("verified", done.Actions[0].State);
        Assert.Equal("writeFailed", done.Actions[1].State);
        Assert.Contains("写入失败：", done.Actions[1].Message, StringComparison.Ordinal);
        Assert.Equal("notRun", done.Actions[2].State);
        Assert.Equal("前序动作未通过，停止后续写入", done.Actions[2].Message);
        Assert.Equal("partial", done.Status);

        Assert.Equal(2, run.CallCount("endpoint.update"));
        Assert.DoesNotContain(run.WriteCalls(), call => call.Contains(" " + third + " ", StringComparison.Ordinal));
        Assert.Equal(thirdBefore, run.Endpoint(third).ToJsonString());
        Assert.Equal("订单详情", run.Endpoint(third)["description"]!.ToString());
        Assert.Equal("订单列表（本次补充说明）", run.Endpoint(first)["description"]!.ToString());
        // 写入绝不自动重发：失败那一次只到 CLI 一轮。
        Assert.Single(run.Calls(), c => c["cmd"]!.GetValue<string>() == "endpoint.update" && c["id"]!.ToString() == second.ToString());

        var task = run.ReadTask();
        Assert.Equal("partial", task.Stages.Remote.Status);
        Assert.Equal(1, task.Stages.Remote.ActionsVerified);
        Assert.Equal(1, task.Stages.Remote.ActionsFailed);
        Assert.Equal(1, task.Stages.Remote.ActionsNotRun);
        Assert.Equal("存在未完成动作，详见同步结果。", task.Stages.Remote.Message);

        var (text, tone) = SyncExecution.ResultText(done.Status, Verified(done), Failed(done), NotRun(done));
        Assert.Equal("warn", tone);
        Assert.Contains("部分完成：已核对 1 个，未完成 2 个", text, StringComparison.Ordinal);
        Assert.DoesNotContain("全部核对", text, StringComparison.Ordinal);
        // 部分完成不回滚：第一个动作写进去的内容仍在远端。
        Assert.Equal("订单列表（本次补充说明）", run.Endpoint(first)["description"]!.ToString());
    }

    /// <summary>已核对通过的动作数（与 SyncExecutor 的统计同一判据）。</summary>
    private static int Verified(SyncPlan plan) => plan.Actions.Count(a => a.State == "verified");

    private static int Failed(SyncPlan plan) => plan.Actions.Count(a => a.State is "writeFailed" or "verifyFailed" or "unknown" or "conflict");

    private static int NotRun(SyncPlan plan) => plan.Actions.Count(a => a.State == "notRun");

    /// <summary>远端同一身份（方法 + 精确路径）现在有几条。</summary>
    private static int EndpointsWith(MockCliRun run, string method, string path)
    {
        var file = Path.Combine(run.StateDir, "remote.json");
        var endpoints = JsonNode.Parse(File.ReadAllText(file))!["endpoints"]!.AsObject();
        return endpoints.Count(entry =>
            entry.Value is JsonObject detail &&
            detail["method"]!.ToString() == method &&
            detail["path"]!.ToString() == path);
    }

    private static JsonObject Docs(string description) => new() { ["description"] = description };

    /// <summary>新建接口的写入内容：与 <c>SyncPlanService.BuildCreateEndpoint</c> 同一批顶层字段。</summary>
    private static JsonObject NewEndpoint(string method, string path, string name) => new()
    {
        ["name"] = name,
        ["type"] = "http",
        ["method"] = method,
        ["path"] = path,
        ["status"] = "released",
        ["description"] = "",
        ["folderId"] = 12,
        ["parameters"] = new JsonObject { ["query"] = new JsonArray() },
        ["requestBody"] = new JsonObject(),
        ["responses"] = new JsonArray(new JsonObject { ["code"] = "200", ["description"] = "成功" }),
    };
}
