using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// <c>collect-real-machine.sh</c> 的<b>变异验证质量门</b>：脚本里每一条 <c>exit 2</c> 分支与
/// <c>.winecheck/README.md</c>「跑到了哪一层」那张表里的一行读数，必须一一对得上（第二百一十轮，简报 P1-1）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百零九轮自述「十六处变异实测可红，其中两处第一次是假的」——一处是被改坏的
/// 测试工程根本编译不过、却被当成「门红了」，一处是脚本里一条永远走不到的分支（假旋钮）。「实测可红」这句话
/// 此前只活在 commit message 与 NOTES 的人写文字里，<b>没有任何一条门判它</b>：那张表少写一行、多写一行，
/// 或把某条分支的读数写成脚本压根不会打出来的那句话，构建与全部用例照旧全绿。这一族也不是第一次翻车
/// （第一百六十九轮、第一百七十九轮都记过「子串取法假绿」）。
/// </para>
/// <para>
/// <b>这一条判什么</b>：把「自述的实测条数与源码分支数对得上」做成一段可跑的对应关系——
/// ① 脚本里每一条 <c>exit 2</c> 的失败消息都被表的「分支」那一列<b>恰好</b>点到一次（少一行 = 那条分支的读数
/// 没人跑过；两行点同一条 = 两条读数替一条分支作保，剩下那条仍然没人跑过）；
/// ② 表里那一段字面量必须<b>真在某一条分支消息里</b>，且不能被两条分支同时含住；
/// ③ 那一行的读数格必须也带着同一段字面量（防止「分支」与「读数」两格各说一件事）；
/// ④ 退出码只许 0 / 1 / 2，其中退 0 与退 1 各至少一行且都不许带分支字面量（它们不是失败分支）；
/// ⑤ <see cref="BranchFloor"/> 那八条封闭清单：<b>脚本与表同时少一条也要红</b>——只判两边「互相对得上」的话，
/// 连分支带它那一行一起删掉是绿的，而那天那条分支就不再有任何读数（同一族见
/// <c>RealMachineEvidenceTests.ExpectedDirectories</c> 为什么要写死那九个名字）。
/// </para>
/// <para>
/// <b>它不判什么</b>：<b>不判变异是怎么跑的</b>——那是人做的事，跑法在
/// <c>RealMachineEvidenceTests.CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory</c>
/// 的 ⑤⑥⑦⑧⑨⑩ 段里，那一段每一轮都真执行这一支脚本。本文件判的是「跑过并且留下读数的条数，与源码里分支的
/// 条数是不是同一批」。<b>也不判读数对不对</b>：脚本输出的文字对不对、归档内容对不对，仍是人看。
/// </para>
/// </summary>
public class MutationVerificationTests
{
    /// <summary>
    /// 八条 <c>exit 2</c> 分支的封闭清单，每条取一段只在该分支消息里出现的字面量。
    /// <para>
    /// 这一格写死是故意的：本文件其余判据都是「两边互相对得上」，而那一种判据挡不住两边同时少一条。
    /// 删掉脚本里任一条分支，必须先在这里删掉同一句、再在 README 删掉对应那一行——三处一起改才算数，
    /// 而三处一起改的时候人是会去读那一句为什么不可达的（第二百零九轮就删过一条，并把理由写进脚本注释）。
    /// </para>
    /// </summary>
    private static readonly string[] BranchFloor =
    [
        "找不到产出物清单",
        "时 awk 本身就失败了",
        "清单里一行都没解析出来",
        "条动作（应为 7 条）",
        "清单把同一个归档目录点到两次",
        "个归档目录（地板 9 个",
        "清单点到的目录在磁盘上不存在",
        "磁盘上有清单没点到的目录",
    ];

    /// <summary>表的一行：场景 / 读数 / 「分支」那一格里唯一的反引号字面量（可空）/ 退出码。</summary>
    private readonly record struct Row(string Scenario, string Reading, string? Literal, string Code);

    /// <summary>脚本里每一条 <c>exit 2</c> 分支自己念的那句话（<c>echo "① / ② 未通过…"</c> 的正文）。</summary>
    private static List<string> BranchMessages(string script) =>
        script.Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#'))
            .Select(l => Regex.Match(l, @"^\s*echo ""[①②] 未通过：(.*)$"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();

    /// <summary>
    /// 「跑到了哪一层」那张表的行：从表头那一行起、连续取到第一个不是表格行为止。
    /// 列数不对当场说出来——格式错的行被解析器静默跳过，正是这一族最贵的那种绿。
    /// </summary>
    private static List<Row> ReadingsRows(string readme, List<string> problems)
    {
        var lines = readme.Split('\n');
        var header = Array.FindIndex(lines, l => l.TrimStart().StartsWith("| 场景 | 读数 |", StringComparison.Ordinal));
        if (header < 0)
        {
            problems.Add("README 里找不到那张「跑到了哪一层」的表（表头 | 场景 | 读数 | …）：整张登记没了，"
                + "八条分支的读数就回到只有 commit message 里那句自述。");
            return [];
        }

        var rows = new List<Row>();
        foreach (var line in lines.Skip(header + 1))
        {
            if (!line.TrimStart().StartsWith('|')) break; // 表到此为止，后面的散文不算这一张。
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.All(c => c.Length == 0 || c.All(ch => ch is '-' or ' '))) continue;
            if (cells.Count != 4)
            {
                problems.Add($"那张表有一行不是 4 列（实际 {cells.Count} 列，应为 场景 / 读数 / 分支 / 退出码）：{cells[0]}。");
                continue;
            }

            var literals = Spans(cells[2]);
            var codes = Spans(cells[3]);
            if (codes.Count != 1)
            {
                problems.Add($"那一行的退出码格不是一个反引号数字（读到 {codes.Count} 个：{cells[3]}）："
                    + "退出码是当天唯一要读的东西，它写成散文就数不出来。");
                continue;
            }

            if (literals.Count > 1)
            {
                problems.Add($"那一行的「分支」格里有 {literals.Count} 段字面量（{cells[2]}）：一行只能对应一条分支，"
                    + "两段就等于这一行同时替两条分支作保。");
            }

            rows.Add(new Row(cells[0], cells[1], literals.Count == 1 ? literals[0] : null, codes[0]));
        }

        return rows;
    }

    private static List<string> Spans(string cell) =>
        Regex.Matches(cell, @"`([^`]+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

    /// <summary>
    /// 把两边对一遍，返回「哪几处对不上」的说明（空 = 对上了）。
    /// 判据本体写成一段纯文本进文本出的函数，是为了让下面那条自测能拿<b>注入过的</b>脚本与表喂它——
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异都是这一种）。
    /// </summary>
    private static List<string> Mismatches(string script, string readme)
    {
        var problems = new List<string>();
        var branches = BranchMessages(script);
        var rows = ReadingsRows(readme, problems);
        if (branches.Count == 0)
        {
            problems.Add("脚本里一条「未通过」分支都没有：那条核对跑不动时只会退场，说不出自己是哪一种跑不动。");
            return problems;
        }

        // ④ 退出码的形状：只许 0 / 1 / 2；0 与 1 各至少一行，且都不许带分支字面量。
        foreach (var row in rows.Where(r => r.Code is not ("0" or "1" or "2")))
        {
            problems.Add($"那一行写了退出码 `{row.Code}`：这支脚本只有 0 / 1 / 2 三种结论，多出来的一种是编的。");
        }

        foreach (var code in new[] { "0", "1" })
        {
            var at = rows.Where(r => r.Code == code).ToList();
            if (at.Count == 0)
            {
                problems.Add($"那张表里没有退出码 {code} 的那一行：0 说的是「九个目录都不空了」、1 说的是「还差东西」，"
                    + "少一行就只剩失败那一类有读数。");
            }

            foreach (var row in at.Where(r => r.Literal is not null))
            {
                problems.Add($"退出码 {code} 那一行（{row.Scenario}）却带了「分支」字面量 `{row.Literal}`："
                    + "它不是失败分支，写进来就等于替一条不存在的分支作保。");
            }
        }

        var twoRows = rows.Where(r => r.Code == "2").ToList();

        // ①②③ 每一条退出码 2 的行：有且只有一段字面量，那段恰好落在一条分支消息里，读数格也带着它。
        foreach (var row in twoRows)
        {
            if (row.Literal is null)
            {
                problems.Add($"那一行（{row.Scenario}）退出码 2，「分支」那一格却没有一段反引号字面量："
                    + "这一行只在说「我跑过一次」，谁也没法拿它对着脚本数。");
                continue;
            }

            var hits = branches.Where(b => b.Contains(row.Literal, StringComparison.Ordinal)).ToList();
            if (hits.Count == 0)
            {
                problems.Add($"表里那句 `{row.Literal}` 在脚本任何一条「未通过」消息里都找不到："
                    + "那天照着表去找这一条，找不到脚本会打出的这句话。");
                continue;
            }

            if (hits.Count > 1)
            {
                problems.Add($"表里那句 `{row.Literal}` 同时出现在 {hits.Count} 条分支消息里："
                    + "两边说不清这一行替哪一条作保，逐条对齐就当没发生。");
            }

            if (!row.Reading.Contains(row.Literal, StringComparison.Ordinal))
            {
                problems.Add($"那一行（{row.Scenario}）的读数格没带着它自己点的字面量 `{row.Literal}`："
                    + "分支与读数两格各说一件事，那一行念出来的就不是这一条分支的输出。");
            }
        }

        // ① 反向：脚本里每一条分支都必须被恰好一行点到。
        foreach (var branch in branches)
        {
            var claimed = twoRows.Where(r => r.Literal is not null && branch.Contains(r.Literal, StringComparison.Ordinal)).ToList();
            if (claimed.Count == 0)
            {
                problems.Add($"脚本里这条分支「未通过：{Truncate(branch)}」在表里没有对应的一行："
                    + "这一条的退出码 2 从来没被跑出来过，而把它念成「跑过」的人不会知道自己念的是第几条。");
                continue;
            }

            if (claimed.Count > 1)
            {
                problems.Add($"脚本里这条分支「未通过：{Truncate(branch)}」被表里 {claimed.Count} 行同时点到"
                    + $"（{string.Join("、", claimed.Select(c => c.Scenario))}）："
                    + "两条读数指着同一件事时，没人知道当天跑出来的到底是哪一个数，而剩下那条仍然没人跑过。");
            }
        }

        // ⑤ 封闭地板：两边同时少一条也要红。
        foreach (var floor in BranchFloor)
        {
            var inScript = branches.Count(b => b.Contains(floor, StringComparison.Ordinal));
            if (inScript != 1)
            {
                problems.Add($"地板那一条「{floor}」在脚本里被 {inScript} 条分支含住（应为恰好 1 条）："
                    + "那条分支被改写或删除了，而这张表的形状已经不是它原来登记的那一套。");
            }

            var inTable = twoRows.Count(r => r.Literal == floor);
            if (inTable != 1)
            {
                problems.Add($"地板那一条「{floor}」在表里被 {inTable} 行的「分支」格逐字点到（应为恰好 1 行）："
                    + "那条分支的读数没了出处。");
            }
        }

        // 表必须说出「逐条对齐由谁判」，否则这一节又会退回一句人写的自述。
        if (!readme.Contains("MutationVerificationTests", StringComparison.Ordinal))
        {
            problems.Add(".winecheck/README.md 那张表没说出逐条对齐由哪条门判：下一轮读到的仍是「人写的加法」，"
                + "而不是有人替他数过一遍。");
        }

        return problems;
    }

    private static string Truncate(string text) => text.Length <= 34 ? text : text[..34] + "…";

    /// <summary>仓里当前那一份脚本与那一张表：对不上就把每一处说出来，而不是只给一句「数量不符」。</summary>
    [Fact]
    public void CollectScript_EveryExitTwoBranchHasExactlyOneRowInTheReadingsTable()
    {
        var problems = Mismatches(Repo.Source(".winecheck", "collect-real-machine.sh"), Repo.Source(".winecheck", "README.md"));
        Assert.True(problems.Count == 0,
            "collect-real-machine.sh 的 exit 2 分支与 .winecheck/README.md「跑到了哪一层」那张表对不上：\n  · "
            + string.Join("\n  · ", problems));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：六种破坏各注入一次，每一次都必须报出自己那一处。
    /// <para>
    /// 这一条存在的理由就是本轮要补的那个缺口本身——第二百零九轮有两处「变异实测可红」第一次是假的，
    /// 其中一处是测试工程编译不过却被当成门红。与其把「我试过能打红」写进 commit message，
    /// 不如让这句话变成一条每次构建都跑的断言（同一形状见
    /// <c>WineCheckScriptsTests.DemoPs1_StructureDetector_ReportsTheBreaksItMustCatch</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void MutationTableDetector_ReportsTheBreaksItMustCatch()
    {
        var script = Repo.Source(".winecheck", "collect-real-machine.sh");
        var readme = Repo.Source(".winecheck", "README.md");
        var baseline = Mismatches(script, readme);
        Assert.True(baseline.Count == 0,
            "基线就不干净，下面每一种「变红了」都可能只是本来就在响：\n  · " + string.Join("\n  · ", baseline));

        // ① 表里删掉一条分支那一行的字面量 → 那条分支没有读数了（地板同一句也要响）。
        var dropped = readme.Replace("| `清单里一行都没解析出来` | `2` |", "| 还没跑 | `2` |", StringComparison.Ordinal);
        Assert.True(dropped != readme, "取法变了：表里「一行都没解析出来」那一行不再是原来那一格，先改这里。");
        Assert.True(HasBreak(script, dropped, "在表里没有对应的一行") && HasBreak(script, dropped, "在表里被 0 行"),
            "从表里删掉一条分支那一行，判据既没说那条分支没有读数、也没说地板那一条消失了。");

        // ② 脚本里删掉一条分支的点名句 → 表里那一行悬空，而地板数到 0 条。
        var branchCut = script.Replace("① 未通过：清单里一行都没解析出来", "① 未通过：清单形状不对", StringComparison.Ordinal);
        Assert.True(branchCut != script, "取法变了：脚本里那句「一行都没解析出来」不在了，这一族的地板判据要先改这里。");
        Assert.True(HasBreak(branchCut, readme, "消息里都找不到")
                    && HasBreak(branchCut, readme, "在脚本里被 0 条分支含住"),
            "把脚本里一条 exit 2 分支的点名改掉，判据既没说表里那句话悬空、也没说地板那一条消失了："
            + "那就仍然是一句自述。");

        // ③ 表里那句字面量改成脚本不会打的话。
        var invented = readme.Replace("`清单把同一个归档目录点到两次`", "`清单把同一个归档目录点了三遍`", StringComparison.Ordinal);
        Assert.True(HasBreak(script, invented, "消息里都找不到"), "表里点一段脚本不会打的话，判据没报悬空引用。");

        // ④ 两行点到同一条分支（剩下那条就没人跑过）。
        var doubled = readme.Replace("| `时 awk 本身就失败了` |", "| `找不到产出物清单` |", StringComparison.Ordinal);
        Assert.True(HasBreak(script, doubled, "被表里 2 行同时点到") && HasBreak(script, doubled, "在表里被 0 行"),
            "两行都点到同一条分支时判据没出声：那等于两条读数替一条分支作保，而「清单读不动」那条又没人跑过。");

        // ⑤ 读数格不再带着它自己点的那段字面量。
        var desynced = readme.Replace("`② 未通过：磁盘上有清单没点到的目录：150/`", "`磁盘上多了一个没人认的目录`", StringComparison.Ordinal);
        Assert.True(HasBreak(script, desynced, "读数格没带着它自己点的字面量"), "读数与分支两格各说一件事，判据没报这一处。");

        // ⑥ 给非失败分支那一行（退 1）也塞一段字面量。
        var nonFail = readme.Replace("| 不是失败分支：退 1 说的是「活儿还没干完」 |", "| `找不到产出物清单` |", StringComparison.Ordinal);
        Assert.True(HasBreak(script, nonFail, "它不是失败分支"), "退 1 那一行被安上一条分支的字面量，判据没报「替一条不存在的分支作保」。");

        // ⑦ 整张表被删掉。
        var gone = Regex.Replace(readme, @"(?s)\| 场景 \| 读数 \|.*?\n\n", "\n", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.True(gone != readme && HasBreak(script, gone, "找不到那张「跑到了哪一层」的表"), "整张表没了，判据却没说登记不见了。");
    }

    /// <summary>把注入过的两边喂给判据，看它报出来的那几处里有没有期望的那一句。</summary>
    private static bool HasBreak(string script, string readme, string expected) =>
        Mismatches(script, readme).Any(p => p.Contains(expected, StringComparison.Ordinal));
}
