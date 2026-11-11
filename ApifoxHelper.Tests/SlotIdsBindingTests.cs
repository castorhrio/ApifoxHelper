using ApifoxHelper.Comments;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 请求体里的 <c>slotIds</c> 走到服务层那一段（第二百一十二轮，简报 P1-1 的修正版）。
/// <para>
/// <b>补的是哪个真实缺陷</b>：草稿生成与材料导出三个分支读的都是 <see cref="GenerateRequest.SlotIds"/>
/// 与 <c>MaterialRequest.SlotIds</c>，两个属性都写成 <c>List&lt;string&gt; { get; set; } = new()</c>。
/// 那个初始值只挡得住「请求体没写这个键」：<b>显式写 <c>"slotIds": null</c> 时 System.Text.Json
/// 会用 null 覆盖它</b>（本机用同一份序列化口径实测过，不是推断）。往下 <c>DraftService.SelectSlots</c>
/// 头一句就是 <c>slotIds.Count</c> —— 于是那一发请求抛 <see cref="NullReferenceException"/>，
/// 经 <c>Api/ErrorHandling.cs</c> 的 500 分支变成「内部错误，请重试」：一个用户能发出的形状，
/// 拿到的却是本仓特意为「不许把内部异常消息漏出去」而收敛过的那句固定话。错误路径的文案完备性在这一格漏了。
/// </para>
/// <para>
/// <b>修法与简报给的那一条不同，理由写在下面</b>：<see cref="EveryShape_BindsToAListTheServiceCanCount"/>
/// 判的是「null 归一成空清单」，而不是简报 P1-1 建议的「null 或空清单就 <c>throw new UserException</c>」。
/// 因为<b>空清单是这一条链此刻正在用的请求形状</b>：原生壳 <c>Desktop/Native/TasksPage.cs</c> 三处传的都是
/// <c>[]</c>（规则提取 / 导出材料 / 模型生成），过渡壳 <c>Web/app.js</c> 四句传的也全是 <c>[]</c>，
/// 语义由 <c>DraftService.SelectSlots</c> 那一行定：<c>slotIds.Count == 0 ? scan.Slots : …</c> ——
/// <b>空清单 = 全部缺口</b>。照简报原文加守卫，第一次点「规则提取候选」就会被自己的守卫拒掉，
/// 那是把一个不存在的 400 换掉一条在用的 200。<c>EmptyListIsTheShapeTheNativeShellSends</c> 钉的就是这条依据。
/// </para>
/// <para>
/// <b>断言分两层</b>（与 <see cref="ErrorCopyTests"/> 同一取舍）：<see cref="GenerateRequest"/>
/// 与 <c>DraftService</c> 的模型文件与测试工程共享编译，所以请求形状那几支是<b>真执行</b>；
/// <c>MaterialRequest</c> 住在 <c>Api/ApiRoutes.cs</c>，那个文件依赖 ASP.NET Core，Linux 侧编不进测试工程，
/// 只能判它的源码形状（<see cref="MaterialRequest_KeepsTheSameNullRule"/>）。
/// 两层都要有：只测源码切片，绑定行为可以改坏；只测共享的那一支，另一个 DTO 可以漂开。
/// </para>
/// <para>
/// <b>证据到哪为止（不说清的下一轮会当成已经发过请求）</b>：测试工程是 <c>net8.0</c>、没有 ASP.NET Core
/// 框架引用，起不了真的 HTTP 栈——<b>本文件一发请求都没有真发过</b>。链条的两端各自钉住：
/// 前端的「绑定成什么」用<b>产品自己那份序列化口径真跑一遍</b>（<see cref="HttpOptions"/> 与
/// <c>Api/HelperHost.cs</c> 的 <c>ConfigureHttpJsonOptions</c> 同款），后端的「服务头一句读什么」
/// 是 <c>DraftService.SelectSlots</c> 里那一句 <c>slotIds.Count</c>（<see cref="EmptyListIsTheShapeTheNativeShellSends"/>
/// 判它还在）；中间那一段路由体（<c>Api/ApiRoutes.cs</c>）在 Linux 侧只能判形状，见 ④。
/// </para>
/// </summary>
public class SlotIdsBindingTests
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

    /// <summary>过渡壳实际送出的那个键名（从 <c>Web/app.js</c> 现读，不在测试里抄第二份）。</summary>
    private static string PageKey()
    {
        var page = Repo.Source("Web", "app.js");
        var key = Regex.Match(page, @"mode:\s*'rule',\s*(?<k>[A-Za-z0-9_]+)\s*:\s*\[\]")
            .Groups["k"].Value;
        Assert.True(key.Length > 0,
            "从 Web/app.js 那句规则提取里取不出 slotIds 的键名：页面换了写法，本文件测的请求形状就是凭空的。");
        return key;
    }

    /// <summary>
    /// ① 页面送的键与 <see cref="GenerateRequest.SlotIds"/> 绑的是同一个字段（键名漂移这一类只有往返一次看得见）。
    /// </summary>
    [Fact]
    public void TheKeyThePageSends_IsThePropertyTheRouteReads()
    {
        var json = JsonSerializer.Serialize(new GenerateRequest(), HttpOptions());
        Assert.Contains("\"" + PageKey() + "\":[]", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// ② 四种请求形状都绑得出一份数得清的清单：没写那个键、显式写 null、写空清单，三者必须是同一个结果
    /// （空清单）；写了值就按值绑。<b>「显式 null」那一支就是本轮修之前会抛 <see cref="NullReferenceException"/> 的那一发。</b>
    /// </summary>
    [Fact]
    public void EveryShape_BindsToAListTheServiceCanCount()
    {
        var key = PageKey();
        var http = HttpOptions();

        var given = JsonSerializer.Deserialize<GenerateRequest>("{\"" + key + "\":[\"s1\",\"s2\"]}", http)!;
        Assert.Equal(new[] { "s1", "s2" }, given.SlotIds);

        foreach (var json in new[]
                 {
                     "{}",
                     "{\"" + key + "\":null}",
                     "{\"" + key + "\":[]}",
                     "{\"mode\":\"model\",\"" + key + "\":null,\"confirmSend\":true}",
                 })
        {
            var request = JsonSerializer.Deserialize<GenerateRequest>(json, http)!;
            // 这一句红 = 服务层那一句 slotIds.Count 又是一发 500。消息把因果念出来，不让人只看到一个 null。
            Assert.True(request.SlotIds is not null,
                $"{json} 绑定后 SlotIds 是 null：DraftService.SelectSlots 头一句 slotIds.Count 会抛未处理异常，"
                + "这一发请求拿到的会是 500 分支那句「内部错误」，而不是它本该有的行为（null = 未提供 = 全部缺口）。");
            Assert.Empty(request.SlotIds);
        }
    }

    /// <summary>
    /// ③ 空清单是<b>正在使用中的请求形状</b>，不是一条待拒的畸形输入：原生壳三处、过渡壳四处送的都是空清单，
    /// 而服务层把「数得出零个」定义成「全部缺口」。本门判这三处依据都还在——
    /// <b>哪一处把空清单改成拒绝，本门就红，因为那同时改掉了 ② 里 null 归一的目标语义。</b>
    /// </summary>
    [Fact]
    public void EmptyListIsTheShapeTheNativeShellSends()
    {
        var native = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var calls = Regex.Matches(native, @"_drafts\.(GenerateRuleAsync|BuildMaterial|GenerateWithModelAsync)\([^)]*\[\]")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.True(calls.Count >= 3,
            $"原生壳里以空清单调 DraftService 的入口只剩 {calls.Count} 处（应为规则提取 / 导出材料 / 模型生成三处）："
            + "原生那一侧不再用空清单了，本文件 ② 的归一目标就得重判。");

        var service = Repo.Source("Comments", "DraftService.cs");
        Assert.Contains("slotIds.Count == 0 ? scan.Slots", service, StringComparison.Ordinal);

        var page = Repo.Source("Web", "app.js");
        Assert.True(Regex.Matches(page, @"slotIds:\s*\[\]").Count >= 4,
            "过渡壳里 slotIds: [] 少于四处：页面换形状了，本文件 ① 现读键名的那句正则也一并要看。");
    }

    /// <summary>
    /// ④ <c>MaterialRequest</c>（材料导出那一支的请求体）保持同一条边界规则。
    /// 它所在的 <c>Api/ApiRoutes.cs</c> 依赖 ASP.NET Core、Linux 侧编不进测试工程，所以这里判源码形状；
    /// 真正被执行的那一半在共享编译的 <see cref="GenerateRequest"/> 上（②）。
    /// </summary>
    [Fact]
    public void MaterialRequest_KeepsTheSameNullRule()
    {
        var routes = Repo.Source("Api", "ApiRoutes.cs");
        var block = Regex.Match(routes,
            @"public sealed class MaterialRequest\s*\{(?<body>.*?)\n\}", RegexOptions.Singleline).Groups;
        Assert.True(block["body"].Success, "Api/ApiRoutes.cs 里取不出 MaterialRequest 的类体：形状变了要先看本门判的是什么。");
        var body = block["body"].Value;

        Assert.Matches(@"SlotIds\s*\n?\s*\{\s*\n?\s*get =>", body);
        Assert.Contains("value ?? new()", body, StringComparison.Ordinal);
        // 不许退回成「自动属性 + 初始值」——那正是本轮修掉的形状：初始值只挡住缺键，挡不住显式 null。
        Assert.False(Regex.IsMatch(body, @"public List<string> SlotIds\s*\{\s*get;\s*set;\s*\}\s*=\s*new"),
            "MaterialRequest.SlotIds 又回到带初始值的自动属性：请求体显式写 \"slotIds\": null 时它会绑成 null，"
            + "材料导出那一支就是一发 500（初始值只挡得住「没写这个键」，挡不住「写了 null」）。");
    }
}
