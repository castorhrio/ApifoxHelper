using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 清单「名称格」的两个表头名（第二百七十三轮，简报 P1-1：给决定，不给实现）。
/// <para>
/// <b>补的是哪个缺口</b>：屏幕上同一串「GET /路径」有两个名字——检查结果、响应契约、接口追踪明细、
/// 补充说明、同步预览勾选页那五张清单的首列表头写「接口」，差异确认与同步预览的写入清单 / 未纳入清单
/// 那一格写「资源」。第二百零五轮起这件事挂在 <c>docs/CRITIQUE.md</c> 第 7 站（「要不要合并」），
/// 到本轮为止只有一个「不折」的判断活在轮次记录与那份清单里，<b>没有一条判据说这两个名字各按什么取</b>——
/// 下一轮要么再判一次，要么顺手把其中一个改成另一个，而那时没有人能说出改错了没有。
/// </para>
/// <para>
/// <b>决定与它的理由（判据本体在 docs/NOTES.md「清单名称格的两个名字（可机械核对）」）</b>：
/// 两名不是同一东西的两种叫法，而是按<b>那一格可能装什么</b>取的：<i>只</i>装接口身份的写「接口」；
/// 既可能装接口身份、又可能装模型名的写「资源」。后面那一支不是措辞偏好——服务层在同一格里交出
/// <c>model:</c> 开头的那一条（<c>Sync/SyncPlanService.cs</c> 的 createModel / updateModel 与 PlanExclusion），
/// 那一格叫「接口」就把模型那一行说成它不是的东西。反过来把只有接口的清单统一成「资源」，
/// 是把已经读得准的名字改宽。<b>名字由那一格的取值范围决定，不由口味决定。</b>
/// </para>
/// <para>
/// <b>这一族判什么</b>：① 登记表 ↔ 源码双向（表里每一行在那一屏找得到那一列、那一取法；源码里每一处
/// 「接口 / 资源」表头都在表里——新增一屏没登记即红）；② 名字与「装什么」互推（混合那一格写成「接口」即红，
/// 只装接口那一格写成「资源」即红）；③ 混合那一支的宣称回到服务层实物（计划里不再出现 <c>model:</c> 键，
/// 「资源」这个宣称就红，而不是留下一句没人读的散文）；④ 计数格那一族（任务列表的「接口」列装的是接口数）
/// 不被当成名称格——同名不等于同族，这一条是 ② 的护栏；⑤ 判据与门互指（NOTES 那一节点到的本类断言名
/// 每一条都真的存在，本类每一条 [Fact] 也都在那一节被点到）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上那一格读起来像不像名字（随 G-D5 真机复核）；不判「资源」这个词好不好听
/// （那是决定，写在上面，不由门判）；不判改名要连动的三处口径（排序键 / 关键词字段 / 标识列声明）——
/// 本轮一行产品源码都没改，改名仍按第二百四十三轮的判断另起一轮；也不判别的表头名（本节只管名称格这一族，
/// 与 <c>ListColumnWidthsTests.SingleScreenColumns_StayOutOfTheCaliber</c> 管的是两件事：
/// 那一条管<b>宽度</b>按内容族给，这一条管<b>名字</b>按取值范围给）。
/// </para>
/// <para>
/// <b>判不到的那一截（变异验完如实登记在这里，不装进门）</b>：某一屏的名称格<b>事实上</b>开始多出模型条目、
/// 而取法锚点那一句没变时，这张表看不出来——登记的是「那一格从哪个表达式取值」，不是「那个表达式在运行时
/// 会交出几种东西」。要机械判它就得去追每一屏的数据流，那是猜，而且猜错的方向是「绿着说没有」；
/// 第二百五十六轮那一族的收口口径正为此而写：判不到的事写在账上，不为了好看造一条门。
/// 真机能读出来的那一半（用户在那一格里究竟看到接口还是模型）随 G-D5 复核。
/// </para>
/// </summary>
public class IdentityHeaderNamesTests
{
    /// <summary>只装接口身份（「GET /路径」）的那一格用的名字。</summary>
    private const string EndpointName = "接口";

    /// <summary>既可能装接口身份、也可能装模型名的那一格用的名字。</summary>
    private const string SupersetName = "资源";

    /// <summary>名称格那一格实际装什么——名字由它决定，不由写代码的人决定。</summary>
    private enum Holds
    {
        /// 只装接口身份：模型条目在另一张清单上（或那一屏根本没有模型）。
        EndpointOnly,

        /// 接口身份或模型名：那一格必须用超集名。
        EndpointOrModel,

        /// 不是名称格（计数格），登记它只为让覆盖完整，并钉住它没被当成名称格。
        Count,
    }

    /// <param name="File">界面源码文件名。</param>
    /// <param name="Field">那一屏上那张清单的字段名。</param>
    /// <param name="Header">那一列的表头名。</param>
    /// <param name="Holds">那一格装什么。</param>
    /// <param name="Anchor">名称格取法在源码里的实物（那一格的内容从哪个表达式来）。</param>
    private sealed record Row(string File, string Field, string Header, Holds Holds, string Anchor);

    /// <summary>
    /// 登记表：<b>这一份就是登记处</b>，别在别处另开一份（与 <c>PageNamesTests.GuideSites</c> 同一做法）。
    /// 加一屏、改一名、或那一屏的清单开始 / 不再装模型条目，都要回来改这一张——那张判据在 NOTES，改它得同时改这里。
    /// </summary>
    private static readonly Row[] Table =
    [
        new("CheckResultsDialog.cs", "_list", EndpointName, Holds.EndpointOnly,
            """new ListViewItem(check.Method + " " + check.Path)"""),
        new("ContractsDialog.cs", "_list", EndpointName, Holds.EndpointOnly,
            "new ListViewItem(contract.EndpointKey)"),
        new("EndpointTraceDialog.cs", "_list", EndpointName, Holds.EndpointOnly,
            "new ListViewItem(coverage.EndpointKey)"),
        new("SupplementsDialog.cs", "_list", EndpointName, Holds.EndpointOnly,
            "new ListViewItem(SupplementEntries.EndpointCell(check))"),
        new("SyncPreviewDialog.cs", "_selectList", EndpointName, Holds.EndpointOnly,
            "new ListViewItem(SyncSelectSort.EndpointCell(check))"),
        new("DecisionsDialog.cs", "_list", SupersetName, Holds.EndpointOrModel,
            "Ui.IdentityCell(item, entry.Resource)"),
        new("SyncPreviewDialog.cs", "_actionList", SupersetName, Holds.EndpointOrModel,
            "row.SubItems.Add(action.ResourceKey)"),
        new("SyncPreviewDialog.cs", "_excludedList", SupersetName, Holds.EndpointOrModel,
            "new ListViewItem(exclusion.ResourceKey)"),
        // 任务列表那一列的表头也是「接口」，装的是接口数：同一个词的另一族内容，不是名称格。
        new("TasksPage.cs", "_list", EndpointName, Holds.Count,
            "new ListViewItem(task.Name)"),
    ];

    /// <summary>NOTES 里那一节（判据正文），由 <see cref="Criterion_IsRegisteredAndNamesEveryFactInThisClass"/> 读。</summary>
    private const string CriterionHeading = "## 清单名称格的两个名字（可机械核对）";

    // ── ① 双向覆盖 ────────────────────────────────────────────────────

    /// <summary>
    /// 表里每一行在那一屏找得到那一列（表头名逐字、且恰一处），且那一格的取法锚点在源码里；
    /// 反方向：源码里每一处「接口 / 资源」表头都在表里——新加一屏没登记即红，改了名没改表也即红。
    /// </summary>
    [Fact]
    public void Table_CoversEveryNameCellAndBack()
    {
        foreach (var row in Table)
        {
            var code = Code(row.File);
            var cells = Count(code, $@"^\s*{Regex.Escape(row.Field)}\.Columns\.Add\(""{Regex.Escape(row.Header)}"",");
            Assert.True(cells == 1,
                $"{row.File} 的 {row.Field} 上表头「{row.Header}」量到 {cells} 处（登记表认 1 处）——"
                + "改名或加一列要连这张登记表一起改，判据在 docs/NOTES.md「清单名称格的两个名字（可机械核对）」。");
            Assert.Contains(row.Anchor, code, StringComparison.Ordinal);
        }

        var problems = UnregisteredNameCells(Repo.NativeSources());
        Assert.True(problems.Count == 0,
            "源码里有「接口 / 资源」表头没进登记表（登记表在 IdentityHeaderNamesTests.Table）：\n" + string.Join("\n", problems));
    }

    // ── ②④ 名字由取值范围决定 ─────────────────────────────────────────

    /// <summary>
    /// 名字与「那一格装什么」互推：混合的那一支必须用超集名，只装接口的必须用「接口」，
    /// 计数格不得借用超集名、且它所在清单不得把自己声明成名称格（否则 Ctrl+C 复制到的是那个数）。
    /// </summary>
    [Fact]
    public void Name_FollowsWhatTheCellCanHold()
    {
        foreach (var row in Table)
        {
            var expected = row.Holds switch
            {
                Holds.EndpointOrModel => SupersetName,
                _ => EndpointName,
            };
            Assert.True(row.Header == expected,
                $"{row.File} 的 {row.Field}（{Describe(row.Holds)}）登记的名字是「{row.Header}」，判据要的是「{expected}」。");
        }

        foreach (var row in Table.Where(r => r.Holds == Holds.Count))
        {
            // 计数格所在那张清单走默认标识列（首列），所以「接口」那一格不是 Ctrl+C 交出去的那一格。
            Assert.DoesNotMatch(@"(?m)^\s*private readonly ListView _list = Ui\.List\([^)]*identityColumn:",
                Code(row.File));
            Assert.DoesNotContain(SupersetName, row.Header, StringComparison.Ordinal);
        }
    }

    // ── ③ 混合那一支的宣称回到服务层实物 ──────────────────────────────

    /// <summary>
    /// 「资源」不是省事的名字：计划那一格里真的两类键都在——接口键（「GET /路径」）与模型键（「model:名称」），
    /// 写入动作与未纳入条目都是。服务层哪天不再往那一格交模型键，这一条就红，
    /// 而不是让「资源」这个宣称继续以散文活着（那时该回到 NOTES 改判）。
    /// </summary>
    [Fact]
    public void TheSupersetName_HasModelKeysInThePlanThoseScreensRead()
    {
        var plan = Repo.Source("Sync", "SyncPlanService.cs");
        // 写入动作：接口那两支与模型那两支各在其位，且模型那一支的 ResourceKey 就是 "model:" + 名称。
        Assert.Contains("""NewAction(plan, "createEndpoint", key""", plan, StringComparison.Ordinal);
        Assert.Contains("""NewAction(plan, "updateEndpoint", key""", plan, StringComparison.Ordinal);
        Assert.Contains("""NewAction(plan, "createModel", "model:" + name""", plan, StringComparison.Ordinal);
        Assert.Contains("""NewAction(plan, "updateModel", "model:" + name""", plan, StringComparison.Ordinal);
        // 未纳入条目：接口键与模型键同样两支都在。
        Assert.Contains("new PlanExclusion { ResourceKey = key", plan, StringComparison.Ordinal);
        Assert.Contains("""new PlanExclusion { ResourceKey = "model:" + name""", plan, StringComparison.Ordinal);

        // 差异确认那一屏的模型条目就在同一张清单里（不是另一张）：那一格因此必须叫「资源」。
        var decisions = Code("DecisionsDialog.cs");
        Assert.Contains("EntryKind.ModelChange", decisions, StringComparison.Ordinal);
        Assert.Contains("""result.Models""", decisions, StringComparison.Ordinal);
    }

    // ── ⑤ 判据与门互指 ───────────────────────────────────────────────

    /// <summary>
    /// NOTES 那一节在位、写全四件（决定「不合并」/ 取名判据 / 延后的那一半与它的节点 / 三处口径），
    /// 并且它与本类<b>互指</b>：那一节点到的本类断言名每一条都真的存在，本类每一条 [Fact] 也都在那一节被点到。
    /// 双向都有，才叫「改它得同时改门」——只判一句话在不在，那是自述，不是门。
    /// </summary>
    [Fact]
    public void Criterion_IsRegisteredAndNamesEveryFactInThisClass()
    {
        var section = CriterionSection();

        Assert.Contains("不合并", section, StringComparison.Ordinal);
        Assert.Contains("取值范围", section, StringComparison.Ordinal);
        Assert.Contains("G-D5", section, StringComparison.Ordinal);
        foreach (var caliber in new[] { "排序键", "关键词字段", "标识列声明" })
        {
            Assert.Contains(caliber, section, StringComparison.Ordinal);
        }

        // 只认大写开头的名字：断言名都是 PascalCase，而「IdentityHeaderNamesTests.cs」那种文件引用不是。
        var named = Regex.Matches(section, $@"{nameof(IdentityHeaderNamesTests)}\.([A-Z]\w*)")
            .Cast<Match>().Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(named);

        var facts = FactNames();
        var missing = named.Except(facts, StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0,
            $"NOTES 那一节点到的断言在本类里不存在：{string.Join("、", missing)}——指针指空比不写指针更糟。");

        var unlisted = facts.Except(named, StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0,
            $"本类有断言没被 NOTES 那一节点到：{string.Join("、", unlisted)}——登记漏了一条，读的人不知道它被什么判住。");
    }

    // ── 判据自己被抓得住 ─────────────────────────────────────────────

    /// <summary>
    /// 五发注入各喂一次（合规的两发不报）：改一屏的名字而表没跟上、新增一屏没登记、
    /// 把混合那一格折成「接口」、让计数格自己声明成标识列、把 NOTES 那一节的门名指到不存在的断言。
    /// </summary>
    [Fact]
    public void Gate_ReportsTheBreaksItMustCatch()
    {
        // ① 改名没登记：差异确认那一格改成「接口」——双向覆盖当场红。
        Assert.Contains(UnregisteredNameCells([Native("DecisionsDialog.cs",
            """_list.Columns.Add("接口", 240);""")]), p => p.Contains("DecisionsDialog.cs", StringComparison.Ordinal));

        // ② 新增一屏没登记。
        Assert.Contains(UnregisteredNameCells([Native("NewPage.cs",
            """_list.Columns.Add("资源", 240);""")]), p => p.Contains("NewPage.cs", StringComparison.Ordinal));

        // ③ 合规：登记过的两屏各写各自的名字，一处都不报。
        Assert.Empty(UnregisteredNameCells([
            Native("ContractsDialog.cs", """_list.Columns.Add("接口", 300);"""),
            Native("SyncPreviewDialog.cs", """_excludedList.Columns.Add("资源", 240);"""),
        ]));

        // ④ 名字与取值范围互推：混合那一支折成「接口」，判据本体要能点出来。
        Assert.False(NameFollowsHold(Holds.EndpointOrModel, EndpointName));
        Assert.True(NameFollowsHold(Holds.EndpointOrModel, SupersetName));
        Assert.False(NameFollowsHold(Holds.EndpointOnly, SupersetName));
        Assert.True(NameFollowsHold(Holds.EndpointOnly, EndpointName));
        // 计数格借用超集名同样不行（那一格根本不是名称格）。
        Assert.False(NameFollowsHold(Holds.Count, SupersetName));

        // ⑤ 门名指空：NOTES 那一节改成一条不存在的断言，双向里「指空」那一半必须红。
        var fake = CriterionSection()
            .Replace(nameof(Table_CoversEveryNameCellAndBack), "Table_CoversEveryNameCellAndBackRenamed", StringComparison.Ordinal);
        var named = Regex.Matches(fake, $@"{nameof(IdentityHeaderNamesTests)}\.([A-Z]\w*)")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("Table_CoversEveryNameCellAndBackRenamed", named);
        Assert.NotEmpty(named.Except(FactNames(), StringComparer.Ordinal));

        // 反向那一半也抓得住：把门名从那一节里删掉，剩下的名单就不覆盖本类全部断言。
        var dropped = CriterionSection().Replace(nameof(Name_FollowsWhatTheCellCanHold), "", StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(Name_FollowsWhatTheCellCanHold), dropped, StringComparison.Ordinal);
        Assert.NotEmpty(FactNames().Except(
            Regex.Matches(dropped, $@"{nameof(IdentityHeaderNamesTests)}\.([A-Z]\w*)").Cast<Match>().Select(m => m.Groups[1].Value),
            StringComparer.Ordinal));
    }

    // ── 判据本体（纯函数） ────────────────────────────────────────────

    /// <summary>
    /// 反向覆盖判据：源码里的「接口 / 资源」表头逐处与登记表对，返回没登记的那些。
    /// 表头名从<b>剥掉注释后的代码</b>里取（<see cref="NativeSource.Code"/>）——注释里引述这两个名字
    /// 是顺着注释找实物的路，不是第二处构造。
    /// </summary>
    private static List<string> UnregisteredNameCells(IReadOnlyList<NativeSource> sources)
    {
        var problems = new List<string>();
        foreach (var source in sources)
        {
            foreach (Match match in Regex.Matches(source.Code, @"(\w+)\.Columns\.Add\(""(" + EndpointName + @"|" + SupersetName + @")"","))
            {
                var field = match.Groups[1].Value;
                var header = match.Groups[2].Value;
                var registered = Table.Any(row => row.File == source.Name && row.Field == field && row.Header == header);
                if (!registered)
                {
                    problems.Add($"{source.Name}：{field} 的表头「{header}」没在登记表里——"
                        + "新增一屏、改一个名，都要回 IdentityHeaderNamesTests.Table 登记，并照 NOTES 那一节的判据先答「那一格装什么」。");
                }
            }
        }

        return problems;
    }

    /// <summary>名字 ↔ 取值范围的互推（判据本体，注入 ④ 直接问它）。</summary>
    private static bool NameFollowsHold(Holds holds, string header)
        => header == (holds == Holds.EndpointOrModel ? SupersetName : EndpointName);

    private static string Describe(Holds holds) => holds switch
    {
        Holds.EndpointOnly => "只装接口身份",
        Holds.EndpointOrModel => "接口身份或模型名",
        _ => "计数格，不是名称格",
    };

    private static string Code(string file) => Repo.Source("Desktop", "Native", file);

    /// <summary>注入用：把一段代码当成某个文件的源码（不落盘，只走同一条判据）。</summary>
    private static NativeSource Native(string file, string code) => new("Desktop/Native/" + file, code);

    private static int Count(string text, string pattern)
        => Regex.Matches(text, pattern, RegexOptions.Multiline).Count;

    private static string CriterionSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var lines = notes.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == CriterionHeading);
        Assert.True(start >= 0, $"docs/NOTES.md 缺「{CriterionHeading}」一节：判据只活在轮次记录里，下一轮就会把这一站重判一遍。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>本类的 [Fact] 方法名（互指判据用它，不靠正则猜自己）。</summary>
    private static IReadOnlyList<string> FactNames() =>
        typeof(IdentityHeaderNamesTests)
            .GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<FactAttribute>() is not null)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
