using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「强调色只出现在哪几处」这一句在<b>三个说话位</b>上的字面对账（第三百六十九轮，控制仓简报 P0-1 甲案）。
/// <para>
/// <b>补的是哪个缺口</b>：第三百六十六轮把 <c>docs/DESIGN.md</c> §1 原则 1 那句改成「强调色只出现在主行动、
/// 焦点与品牌资产」（品牌资产那一类是同轮的定夺，机械判据在 <c>AppIconTests</c>），而<b>同一句话还写在另外两处</b>——
/// <c>docs/方案.md</c> §6.2「继承来源与采用规则」表第 2 行的采用规则那一格，与
/// <c>docs/功能清单与验收标准.md</c> 设计验收 D2（铬色克制）那一格。那两处当时按「历史记录 / 验收清单原文」留着旧字面，
/// 后果写在 DESIGN §8 那一格第 ⑤ 条：<b>在「品牌资产算不算 §1 管辖」这一问上，那两份比 DESIGN 旧一档</b>，
/// 而全仓没有一条门断言那两处字面——下一个人先读到的若是方案或验收清单，读到的就是一条与实物矛盾的规则。
/// </para>
/// <para>
/// <b>本文件判什么</b>：三处<b>逐字说同一句话</b>——① 每一处都含现行口径那一串；② 每一处都不含第三百六十六轮
/// 之前那一版字面（「主行动与焦点」）；③ 三处取到的都是<b>那一格本身</b>而不是整份文件（整份文件里
/// <c>DESIGN.md</c> §8 那一格逐字引着旧字面当历史记录，按整份文件判会把「记着历史」读成「还在用旧口径」）。
/// 一句话改一处、另两处没跟上，这一条当场红——与 <c>TokenWiringTests</c> 同一族：<b>一个说话位改了三处一起红</b>。
/// </para>
/// <para>
/// <b>按第二百八十二轮五步法判族：对账门</b>。第 ② 步答「不是」——判的是三份仓内文档里那三格自己的字面，
/// 而「现行口径」这一串的定义不在被它判的那三格里的任何一格（它由 DESIGN §1 那一句定，本文件只把三处对起来）。
/// 第 ④ 步的验证：把本文件整条删掉，其余各门照旧全绿（<c>AppIconTests</c> 判的是资产实物、与这句措辞无关）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「品牌资产该不该算一处强调色落点」（那是第三百六十六轮已经定夺过的事，理由在
/// DESIGN §8 那一格）；不判界面屏上那两处有没有用错（<c>TokenWiringTests</c> 判令牌接线、<c>AppIconTests</c> 判资产实物）；
/// 也不判这三处之外的散文里有没有再抄一遍这句话——那只能靠人读，写成门只会把「门绿」当成「口径统一」。
/// </para>
/// </summary>
public class AccentScopeWordingTests
{
    /// <summary>现行口径：DESIGN.md §1 原则 1 那一句里的落点串（三处必须逐字相同）。</summary>
    private const string Canonical = "强调色只出现在主行动、焦点与品牌资产";

    /// <summary>第三百六十六轮之前那一版字面。三处都不许再出现它。</summary>
    private const string Retired = "强调色只出现在主行动与焦点";

    /// <summary>一个说话位：名字（进断言消息）、文件、以及从该文件里取出那一格的取法。</summary>
    private sealed record Speaker(string Name, string File, string Cell);

    /// <summary>
    /// 三个说话位。新增一处「同一句话还写在这里」时在这里加一行——<b>不加就是那一处没人判</b>，
    /// 与第三百六十六轮之后的状态一样：改了 DESIGN 那一句，另两处漂着也没人红。
    /// </summary>
    private static Speaker[] Speakers() =>
    [
        new("docs/DESIGN.md §1 原则 1", "docs/DESIGN.md",
            CellOf(Repo.DesignDocText, "1. 灰阶铬色 + 恰一个强调色")),
        new("docs/方案.md §6.2 采用规则表第 2 行", "docs/方案.md",
            CellOf(Repo.Source("docs", "方案.md"), "| 2 | AutoSnap §2")),
        new("docs/功能清单与验收标准.md 设计验收 D2", "docs/功能清单与验收标准.md",
            CellOf(Repo.Source("docs", "功能清单与验收标准.md"), "| D2 | 铬色克制")),
    ];

    /// <summary>
    /// 取那一格：按行首前缀找<b>一行</b>，找不到即红（改名、挪行、删行都会在这里露出来）。
    /// <para>
    /// 只取一行而不是整份文件，是因为 <c>DESIGN.md</c> §8 那一格逐字引着旧字面当历史记录——
    /// 按整份文件判「不许出现旧字面」，会把「记着历史」读成「还在用旧口径」，那是判据错不是实物错。
    /// </para>
    /// </summary>
    private static string CellOf(string text, string linePrefix)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith(linePrefix, StringComparison.Ordinal));
        Assert.True(line is not null,
            $"找不到以「{linePrefix}」开头的那一行——这一处说话位改名或挪走了，本门要跟着改，"
            + "否则它判的是一处已经不存在的地方。");
        return line!;
    }

    /// <summary>
    /// 三处逐字说同一句话：都含现行口径、都不含旧字面。
    /// </summary>
    [Fact]
    public void ThreeSpeakers_SayTheSameThing()
    {
        var problems = Problems(Speakers());
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。第一发是「方案那一格改回旧字面」，第二发是「验收清单那一格改回旧字面」，
    /// 第三发是「DESIGN 那一句改成第三种说法」（既不是现行口径也不是旧字面——漂到第三种写法比漂回旧字面更难被眼看出来），
    /// 第四发是合规三格（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        var good = Speakers();

        // ① 方案那一格改回旧字面。
        var plan = good.Select(s => s.Name.Contains("方案.md", StringComparison.Ordinal)
            ? s with { Cell = s.Cell.Replace(Canonical, Retired, StringComparison.Ordinal) }
            : s).ToArray();
        Assert.Contains(Problems(plan), p => p.Contains("方案.md", StringComparison.Ordinal));

        // ② 验收清单那一格改回旧字面。
        var checklist = good.Select(s => s.Name.Contains("功能清单", StringComparison.Ordinal)
            ? s with { Cell = s.Cell.Replace(Canonical, Retired, StringComparison.Ordinal) }
            : s).ToArray();
        Assert.Contains(Problems(checklist), p => p.Contains("功能清单", StringComparison.Ordinal));

        // ③ DESIGN 那一句漂到第三种说法：既不是现行口径、也不是旧字面。
        var design = good.Select(s => s.Name.Contains("DESIGN.md", StringComparison.Ordinal)
            ? s with { Cell = s.Cell.Replace(Canonical, "强调色只出现在主行动", StringComparison.Ordinal) }
            : s).ToArray();
        Assert.Contains(Problems(design), p => p.Contains("DESIGN.md", StringComparison.Ordinal));

        // ④ 合规三格：一处都不报。
        Assert.Empty(Problems(good));
    }

    /// <summary>
    /// 判据本体（纯函数，注入测试与正式断言共用一条取法——各写一份就会有一边改了不红）。
    /// </summary>
    private static List<string> Problems(IReadOnlyList<Speaker> speakers)
    {
        var problems = new List<string>();
        foreach (var speaker in speakers)
        {
            if (!speaker.Cell.Contains(Canonical, StringComparison.Ordinal))
            {
                problems.Add($"{speaker.Name}（{speaker.File}）里没有现行口径「{Canonical}」——"
                    + "这一句在三个说话位上必须逐字相同，改一处就要三处一起改。");
            }
            if (speaker.Cell.Contains(Retired, StringComparison.Ordinal))
            {
                problems.Add($"{speaker.Name}（{speaker.File}）里还留着第三百六十六轮之前那一版字面「{Retired}」——"
                    + "它把「品牌资产」那一类排除在外，与 DESIGN §1 原则 1 现行口径矛盾。");
            }
        }
        return problems;
    }
}
