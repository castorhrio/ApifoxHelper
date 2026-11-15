using System.Text.Json;
using System.Text.RegularExpressions;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「3 分钟看到价值」的演示路径（第三百四十一轮，简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：本仓此前<b>没有任何一条从零到「看到差异被同步」的演示路径</b>——
/// 真机到手那一刻也要现搭环境。这一节把演示路径落成三件实物：<c>samples/demo-workspace/</c>（一个能真跑的最小 C# 项目，
/// 故意留两处缺口）、<c>docs/DEMO.md</c>（3 分钟剧本，每一站对应 <c>README.md</c>「使用流程」的那一站）、
/// 以及这一节本身（对账门）。
/// </para>
/// <para>
/// <b>这一节凭什么不是自述</b>：两处「故意留的缺口」不是由测试按文件名猜的，而是<b>真的跑一遍扫描链路</b>——
/// <see cref="ProjectLoader"/> → <see cref="EndpointDiscovery"/> → <see cref="SlotScanner"/>，
/// 正是 <see cref="SourceScanService"/> 里那三步（本工程共享编译同一批源码，见 csproj 里那一组 <c>Link</c>）。
/// 扫不出来就是红，不是跳过。剧本那一头判的是「<c>docs/DEMO.md</c> 点到的每一站都能在 <c>README.md</c>「使用流程」里找到对应条目」，
/// 判的是<b>被承诺的实物</b>（对账门，不属「判据判自己」那一族）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判演示现场跑得快不快、不判 wine 上点得动点不动（那是 <c>.winecheck/</c> 那一族的事）；
/// 不判「这两处缺口该不该是这两处」——那是产品判断，机械门只判「剧本承诺的实物在不在」。
/// 也不判远端写入：演示只到同步预览，<c>docs/DEMO.md</c> 里明写不执行写入。
/// </para>
/// </summary>
public class DemoWorkspaceTests
{
    /// <summary>演示工作区（只放源码与一份远端快照，不放任何构建产物）。</summary>
    private static string DemoRoot => Path.Combine(Repo.Root, "samples", "demo-workspace");

    private static string DemoDoc => Repo.Source("docs", "DEMO.md");

    private static string Readme => Repo.Source("README.md");

    private static string RemoteSnapshot => Repo.Source("samples", "demo-workspace", "apifox-remote.json");

    /// <summary>
    /// 真的跑一遍扫描链路，返回扫到的接口与注释位置。
    /// <para>
    /// 用的是 <see cref="SourceScanService"/> 里那三步的同一批组件（加载 → 发现 → 生成注释位置），
    /// 只是不经过 <c>TaskStore</c> 与后台作业——演示工作区没有任务目录，也不需要。
    /// </para>
    /// </summary>
    private static (List<EndpointInfo> Endpoints, List<DocSlot> Slots, AnalysisLevel Level) Scan()
    {
        var profile = new ProjectProfile
        {
            Id = "demo",
            Name = "演示工作区",
            WorkspaceRoot = DemoRoot,
            ApiProject = "Demo.Api.csproj",
            // 演示第 1 站就在「项目设置」里填这一格：不填，缺口 ① 只能导出补丁、回写那一站走不下去。
            AllowedSourcePaths = ["Controllers", "Models"],
        };
        var workspace = new ProjectLoader().Load(profile, CancellationToken.None);
        var discovery = new EndpointDiscovery(workspace, profile);
        var endpoints = discovery.Discover(CancellationToken.None);

        var scanner = new SlotScanner(workspace, profile);
        foreach (var endpoint in endpoints) scanner.AddEndpoint(endpoint, Array.Empty<ITypeSymbol>());

        return (endpoints.Select(e => e.Info).ToList(), scanner.Slots.ToList(), workspace.Level);
    }

    /// <summary>
    /// 演示工作区是<b>真的能扫</b>的：两个接口都发现得到，且不是靠「语法降级」蒙出来的。
    /// <para>
    /// 这一条是下面两条的前提——扫不出接口，「缺口被扫出来」就无从谈起；
    /// 而如果加载降级到 <c>SyntaxOnly</c>，<c>ActionResult&lt;T&gt;</c> 里的 <c>T</c> 追不到，
    /// 属性槽位一个都不会出现，演示里「字段说明冲突」那一处也就看不见了。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoWorkspace_IsReallyScannable()
    {
        var (endpoints, slots, level) = Scan();

        Assert.Contains(endpoints, e => e.Key == "GET /api/orders");
        Assert.Contains(endpoints, e => e.Key == "GET /api/orders/{id}");
        Assert.True(slots.Count > 0, "演示工作区一个注释位置都没扫出来——扫描链路在这个样本上没跑通。");
        Assert.True(slots.Any(s => s.SymbolKind == "property"),
            "演示工作区没有扫出任何属性槽位——响应类型没追到，演示里「字段说明冲突」那一处看不见。");
        // 不是语法降级：演示工作区没有 obj/project.assets.json（演示不 restore，也不该 restore），
        // 所以加载级别如实是 Partial——但框架引用从共享框架目录解析得到，ActionResult<T> 里的 T 追得下去，
        // 属性槽位就是这么来的。降到 SyntaxOnly 的话，下面那条「字段说明冲突」当场就看不见了。
        Assert.NotEqual(AnalysisLevel.SyntaxOnly, level);
    }

    /// <summary>
    /// 缺口 ①：<c>ListOrders</c> 缺 <c>///</c> 注释，扫描器把它报成 <c>missing</c>。
    /// <para>
    /// 这是演示第一眼要看到的东西——工具自己找出来，不用人翻代码。样本里那句
    /// 「演示缺口 ①」的注释是给人看的，判据不读它：判的是<b>扫描器真的报了这一条</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoWorkspace_TheScannerFindsTheMissingDocComment()
    {
        var (_, slots, _) = Scan();

        var gap = slots.Where(s => s.SymbolKind == "method" && s.Tag == "summary" && s.Status == "missing").ToList();
        Assert.True(gap.Count == 1,
            $"演示工作区应当恰好有 1 处「方法缺 summary」的缺口，实际扫到 {gap.Count} 处："
            + string.Join(" / ", gap.Select(s => $"{s.Display}（{s.File}:{s.Line}）")));
        Assert.Contains("ListOrders", gap[0].Display, StringComparison.Ordinal);
        Assert.EndsWith("Controllers/OrdersController.cs", gap[0].File.Replace('\\', '/'), StringComparison.Ordinal);
        Assert.True(gap[0].Editable, "这一处缺口不可回写——演示里「接受 → 回写」那一站就走不下去。");

        // 另一个方法有完整注释：演示要展示的是「一处缺口」，不是「整份代码都没注释」。
        Assert.Contains(slots, s => s.SymbolKind == "method" && s.Tag == "summary"
            && s.Status == "valid" && s.Display.Contains("GetOrder", StringComparison.Ordinal));
    }

    /// <summary>
    /// 缺口 ②：源码说「单位：元」，远端快照说「单位：分」——同一字段两处说明不一致。
    /// <para>
    /// 这一处判两头：① 源码那一侧的注释原文由扫描器读出来（<c>ExistingText</c>）；
    /// ② 远端快照那一侧的 <c>description</c> 由本测试从 JSON 里读出来。两边都真的读，不是抄一份常量。
    /// 演示里「1 处说明冲突」就是这一处。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoWorkspace_TheRemoteSnapshotDisagreesWithTheSourceOnOneField()
    {
        var (_, slots, _) = Scan();

        var amount = slots.SingleOrDefault(s => s.SymbolKind == "property" && s.Display.Contains("TotalAmount", StringComparison.Ordinal));
        Assert.NotNull(amount);
        Assert.Equal("valid", amount!.Status);
        Assert.Contains("单位：元", amount.ExistingText ?? "", StringComparison.Ordinal);

        using var snapshot = JsonDocument.Parse(RemoteSnapshot);
        var remote = snapshot.RootElement
            .GetProperty("components").GetProperty("schemas").GetProperty("OrderSummary")
            .GetProperty("properties").GetProperty("totalAmount").GetProperty("description").GetString();
        Assert.NotNull(remote);
        Assert.Contains("单位：分", remote!, StringComparison.Ordinal);

        // 两处说明确实不是同一句话：演示里那一处「冲突」是实物，不是剧本自己说的。
        Assert.NotEqual(amount.ExistingText, remote);
    }

    /// <summary>
    /// 剧本点到的每一站，都必须是 <c>README.md</c>「使用流程」里已有的那一站——不新增步骤。
    /// <para>
    /// 判法：<c>docs/DEMO.md</c> 的演示表里，每一行的「对应 README 使用流程」一格写成
    /// <c>第 N 条「标题」</c>；<c>README.md</c> 里第 N 条必须真的以 <c>N. **标题**</c> 开头。
    /// 剧本自己加一站、或 README 改了标题而剧本没跟，这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_EveryStopIsAStopInTheReadmeFlow()
    {
        var stops = DemoStops();
        Assert.True(stops.Count >= 6, $"docs/DEMO.md 的演示表只有 {stops.Count} 站（地板 6）——3 分钟走不完主链路。");

        var readmeSteps = ReadmeSteps();
        foreach (var (num, action, step, title) in stops)
        {
            Assert.True(readmeSteps.TryGetValue(step, out var readmeTitle),
                $"docs/DEMO.md 第 {num} 站「{action}」指向 README「使用流程」第 {step} 条，但 README 里没有这一条。");
            Assert.Equal(readmeTitle, title);
        }
    }

    /// <summary>
    /// 剧本明写「不执行远端写入，只到同步预览」——这是 SHARED 不变量在演示路径上的落点。
    /// <para>
    /// 判的是那一句在不在，以及演示表里<b>没有任何一站</b>声称执行了写入。
    /// 剧本可以讲「同步预览」，不能讲「同步执行」。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_StopsAtTheSyncPreviewAndSaysSo()
    {
        var doc = DemoDoc;
        Assert.Contains("不执行远端写入", doc, StringComparison.Ordinal);
        Assert.Contains("同步预览", doc, StringComparison.Ordinal);

        // 判「有没有哪一站声称执行了写入」：先把明写的否定句摘掉，剩下的再判。
        // 不摘的话「（**不执行写入**）」这句自己会被子串匹配判红——那是判据写错，不是剧本写错。
        foreach (var (num, action, _, _) in DemoStops())
        {
            var claim = action.Replace("不执行写入", "", StringComparison.Ordinal)
                .Replace("不执行同步", "", StringComparison.Ordinal)
                .Replace("不写入远端", "", StringComparison.Ordinal);
            foreach (var verb in new[] { "执行同步", "写入远端", "执行写入" })
            {
                Assert.False(claim.Contains(verb, StringComparison.Ordinal),
                    $"docs/DEMO.md 第 {num} 站「{action}」声称「{verb}」——演示只到同步预览，不执行远端写入。");
            }
        }
    }

    /// <summary>
    /// 剧本点到哪个归档目录，那个目录就得真有东西——「承诺的实物在不在」（第三百四十二轮，简报 P0-1）。
    /// <para>
    /// <b>要修的那处真实不一致</b>：这一页此前写着「这一页在 Linux 容器里按 wine 10.0 + Xvfb 模拟器走过一遍，
    /// 截图归档在 <c>docs/evidence/demo-run/</c>」——而那个目录<b>不存在</b>，那一跑也<b>没有发生</b>
    /// （第三百四十一轮自己的账本里写着「wine 未跑」）。一句没有实物的话写在答辩材料里，现场一翻就穿。
    /// 本轮按简报的收法 B 把那一句改成如实描述（「本页未附截图归档，演示现场按本页剧本走」），
    /// 并把「不许再写回去」落成这一条门。
    /// </para>
    /// <para>
    /// <b>判什么</b>：<c>docs/DEMO.md</c> 正文里出现的每一个 <c>docs/evidence/&lt;子目录&gt;/</c> 路径，
    /// 磁盘上必须真有这个目录、且目录里除隐藏占位（<c>.gitkeep</c> 这类以 <c>.</c> 开头的文件）之外有文件。
    /// 判的是<b>被承诺的实物</b>（目录与文件在它自己之外），不是判据判自己——按第二百八十二轮五步法：
    /// ① 口径名 = 剧本里那个路径字面；② 它的定义在磁盘上，不在剧本里，答「不是」；③ 按第 ③ 步当对账门处理；
    /// ④ 注入实测见 <see cref="ArchiveClaimDetector_ReportsTheBreaksItMustCatch"/>；⑤ 结论写在这一句。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判截图里画的是什么、不判那一跑是不是真跑出来的（门看不出来），
    /// 也不判真机——真机 0 台，<c>G-D5</c> 仍是 <c>[ ]</c>。它只保证「写了归档目录，就得有东西」。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_ClaimsNoArchiveDirectoryItDoesNotHold()
    {
        var claims = ArchiveClaims(DemoDoc);
        var problems = ArchiveProblems(claims, Repo.Root);
        Assert.True(problems.Count == 0,
            "docs/DEMO.md 点到的归档目录没有实物：\n  " + string.Join("\n  ", problems)
            + "\n两条合法的收法：① 真跑一遍把截图放进那个目录；② 把那一句改成如实描述"
            + "（「本页未附截图归档，演示现场按本页剧本走」）——不许两处都空。");
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住：三发注入各报一处，合规的两发不报。
    /// <para>
    /// 这一族的老毛病是「判据写错时门看起来是绿的」：正则写宽了会把正文里任何一句都读成归档承诺，
    /// 写窄了则一个都读不到——两种都让上面那条门永远绿。所以这里拿临时假树把两个方向都跑活。
    /// </para>
    /// </summary>
    [Fact]
    public void ArchiveClaimDetector_ReportsTheBreaksItMustCatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "ah-demo-claim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // ① 写了归档目录、目录不存在 → 报。
            var missing = ArchiveProblems(ArchiveClaims("截图归档在 `docs/evidence/demo-run/`。"), root);
            Assert.Single(missing);
            Assert.Contains("docs/evidence/demo-run/", missing[0], StringComparison.Ordinal);

            // ② 目录在、但只有隐藏占位 → 照样报（空目录 = 未达成，与真机证据那一页同一口径）。
            Directory.CreateDirectory(Path.Combine(root, "docs", "evidence", "demo-run"));
            File.WriteAllText(Path.Combine(root, "docs", "evidence", "demo-run", ".gitkeep"), "");
            var placeholder = ArchiveProblems(ArchiveClaims("截图归档在 `docs/evidence/demo-run/`。"), root);
            Assert.Single(placeholder);
            Assert.Contains("只有隐藏占位", placeholder[0], StringComparison.Ordinal);

            // ③ 目录里真有文件 → 不报。
            File.WriteAllText(Path.Combine(root, "docs", "evidence", "demo-run", "站1-项目设置.png"), "x");
            Assert.Empty(ArchiveProblems(ArchiveClaims("截图归档在 `docs/evidence/demo-run/`。"), root));

            // ④ 正文里没点任何归档目录 → 不报（本轮改成的写法就是这一种）。
            Assert.Empty(ArchiveClaims("本页未附截图归档，演示现场按本页剧本走。"));
            Assert.Empty(ArchiveProblems(ArchiveClaims("本页未附截图归档，演示现场按本页剧本走。"), root));

            // ⑤ 只写 `docs/evidence/` 这个前缀（不带子目录）不算承诺：那是路径前缀，不是归档落点。
            Assert.Empty(ArchiveClaims("本页写到的每一个 `docs/evidence/` 下的子目录都得真有文件。"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>剧本正文里点到的归档目录（去重、顺序稳定）：<c>docs/evidence/&lt;子目录&gt;/</c>。</summary>
    private static List<string> ArchiveClaims(string doc) => Regex
        .Matches(doc, @"docs/evidence/(?<dir>[^/\s`）)]+)/")
        .Select(m => $"docs/evidence/{m.Groups["dir"].Value}/")
        .Distinct(StringComparer.Ordinal)
        .OrderBy(p => p, StringComparer.Ordinal)
        .ToList();

    /// <summary>归档承诺的实物核对：目录在不在、里面除隐藏占位之外有没有文件。</summary>
    private static List<string> ArchiveProblems(IEnumerable<string> claims, string root)
    {
        var problems = new List<string>();
        foreach (var claim in claims)
        {
            var dir = Path.Combine(root, claim.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(dir))
            {
                problems.Add($"「{claim}」在磁盘上不存在——剧本说截图归档在这里，这里什么都没有。");
                continue;
            }

            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .ToList();
            if (files.Count == 0)
            {
                problems.Add($"「{claim}」里只有隐藏占位、没有文件——空目录 = 未达成，与真机证据那一页同一口径。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 演示工作区里只有源码与一份远端快照：不放构建产物、不放任务目录。
    /// <para>
    /// 这一条挡的是「把某次跑出来的 <c>bin/</c> / <c>obj/</c> / 任务资料一起提交」——
    /// 那样样本就不再是「从零开始」的演示，而是「某台机器上的残留」。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoWorkspace_ContainsOnlySourcesAndTheRemoteSnapshot()
    {
        var files = Directory.GetFiles(DemoRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(DemoRoot, f).Replace('\\', '/'))
            .Where(f => !f.StartsWith("bin/", StringComparison.Ordinal) && !f.StartsWith("obj/", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            ["Controllers/OrdersController.cs", "Demo.Api.csproj", "Models/OrderSummary.cs", "apifox-remote.json"],
            files);
    }

    /// <summary>演示表：<c>| 站 | 演示动作 | 对应 README 使用流程 | 3 分钟内看到什么 |</c>。</summary>
    private static List<(string Num, string Action, string Step, string Title)> DemoStops()
    {
        var rows = new List<(string, string, string, string)>();
        foreach (var line in DemoDoc.Split('\n'))
        {
            if (!Regex.IsMatch(line, @"^\| \d")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 4, $"docs/DEMO.md 的演示表有一行不是 4 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");
            var match = Regex.Match(cells[2], @"^第 (\d+) 条「(.+)」$");
            Assert.True(match.Success,
                $"docs/DEMO.md 第 {cells[0]} 站的「对应 README 使用流程」一格写成「{cells[2]}」，"
                + "必须是「第 N 条「标题」」——否则这一站对不上 README 的那一条。");
            rows.Add((cells[0], cells[1], match.Groups[1].Value, match.Groups[2].Value));
        }

        return rows;
    }

    /// <summary>README「使用流程」那一节的编号条目：<c>N. **标题**：…</c>。</summary>
    private static Dictionary<string, string> ReadmeSteps()
    {
        var lines = Readme.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("## 使用流程", StringComparison.Ordinal));
        Assert.True(start >= 0, "README.md 缺「## 使用流程」一节。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));

        var steps = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines[start..(end < 0 ? lines.Length : end)])
        {
            var match = Regex.Match(line, @"^(\d+)\. \*\*(.+?)\*\*");
            if (match.Success) steps[match.Groups[1].Value] = match.Groups[2].Value;
        }

        Assert.True(steps.Count >= 6, $"README「使用流程」只解析到 {steps.Count} 条（地板 6）——解析口径或那一节形状变了。");
        return steps;
    }
}
