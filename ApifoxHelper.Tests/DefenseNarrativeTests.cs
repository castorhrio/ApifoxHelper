using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 初审答辩口径五块的机械门（<c>docs/NOTES.md</c>「初审答辩口径（真机不可得时）」，第一百七十轮立，第一百七十一轮补第四块，第一百七十五轮补第五块，简报 P0-1 / P1-1）。
/// <para>
/// <b>为什么要有这一节</b>：G-D5 缺的是外部供给的机器，本仓交付不了。真机到手之前，答辩现场能拿出来的
/// 只有「一条命令复跑的 <c>.winecheck/demo.sh</c> + 一份讲得清的叙事」。脚本第一百六十七轮就有门了，
/// 叙事此前只在各轮记录里散着——散着的东西每轮重写一遍、每轮漂一点，到答辩那天就得现场编。
/// 这一节把它落成五块定稿：差异化、AI 必要性、可经营、排版归因、产品完整度。
/// </para>
/// <para>
/// <b>第四块补的是哪一处软肋</b>：第一百七十轮量出「四种取值排版相同」，于是「排版禁则为什么成立」这一问
/// 有了答案，而答案<b>不在口径里</b>——被追问时只能现场组织语言，而现场组织的语言容易把功劳记给
/// <c>line-break:strict</c>（本机恰恰判不出它）。第四块把这句正面写下来，判的还是同三件事：字数、禁词、出处。
/// </para>
/// <para>
/// <b>第五块补的是哪一处必问</b>：前四块讲「凭什么值得做」，没有一块回答「做完了多少」——冲第一口径下
/// 这是评委必问而口径里没有的一块。它不发明新能力，只把既有三件事（十站主工作流、九条写入安全红线、
/// 可勾选的验收表）浓缩成一句，并把「真机未验的项一律留空框」一起念出来：完整度靠的是能勾到证据，
/// 不是靠把没验的项说成验过。
/// </para>
/// <para>
/// <b>各块被什么钉住</b>：① 块齐（块数只在 <see cref="ExpectedBlocks"/> 一处表达）、每块 ≤80 字（超过 80 字就不是「当场念的一段」，是又一轮文档）；
/// ② 各块里不得出现 <see cref="CopyRules.BannedWords"/>——空话禁词正是「去 AI 味道」这一项要防的；
/// ③ 每块必须引到 <c>README.md</c> 或 <c>docs/NOTES.md</c> 里<b>真实存在的小节</b>，引用不 resolve 即红。
/// 第 ③ 条是四块里唯一有分量的：一段没有出处的自述，写得再短也只是把「我记得」换个说法
/// （同一口径见 <see cref="AcceptanceChecklistTests"/> 对证据出处的要求）。
/// </para>
/// <para>
/// <b>字数怎么算</b>：只数「口径」那一列的字符（<c>string.Length</c>，一个汉字、一个全角标点、一个「」都算 1），
/// 表头、出处列与本文件的说明都不算——列在出处里，因为出处是要被核对的，不是要被念出来的。
/// </para>
/// </summary>
public class DefenseNarrativeTests
{
    private const string Heading = "## 初审答辩口径（真机不可得时）";

    /// <summary>每一块「口径」列的字符上限。简报给的数是 80。</summary>
    private const int Ceiling = 80;

    /// <summary>
    /// 块名是封闭的：改了名字等于换了要讲的事，门要认的是这几件。
    /// <para>
    /// <b>块数只在这里表达一处</b>（第一百七十一轮立的写法，第一百七十五轮补第五块时照用）：
    /// 用例名与断言文案都不写死块数，加一块只改这一行——否则「加一块」这件事会同时要改三处，
    /// 而漏掉的那一处正是判块数的那一条。
    /// </para>
    /// </summary>
    private static readonly string[] ExpectedBlocks = ["差异化", "AI 必要性", "可经营", "排版归因", "产品完整度"];

    /// <summary>只允许这两份文件当小节出处——「引到 README 或 NOTES 的实体段落」按字面执行。</summary>
    private static readonly string[] CitableDocs = ["README.md", "docs/NOTES.md"];

    private readonly record struct Block(string Name, string Narrative, string Citations);

    private static string Section()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(Heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/NOTES.md 缺少「{Heading}」一节（真机不可得时的答辩叙事落点）。");
        var rest = notes[(start + Heading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    private static List<Block> Blocks()
    {
        var rows = Section().Split('\n')
            .Where(l => l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .Where(c => c.Count == 3)
            .Where(c => c[0] != "块" && !c[0].All(ch => ch is '-' or ' ' or ':'))
            .Select(c => new Block(c[0], c[1], c[2]))
            .ToList();
        Assert.True(rows.Count == ExpectedBlocks.Length,
            $"这一节的口径块有 {rows.Count} 块，简报要的是 {ExpectedBlocks.Length} 块（{string.Join("、", ExpectedBlocks)}）。");
        return rows;
    }

    [Fact]
    public void Narrative_HasEveryExpectedBlockAndEachFitsTheEightyCharacterCeiling()
    {
        var rows = Blocks();
        Assert.Equal(ExpectedBlocks, rows.Select(r => r.Name));

        foreach (var row in rows)
        {
            Assert.True(row.Narrative.Length > 0, $"「{row.Name}」这块是空的：空的一块在答辩现场念不出任何东西。");
            Assert.True(row.Narrative.Length <= Ceiling,
                $"「{row.Name}」这块 {row.Narrative.Length} 字，超过 {Ceiling} 字上限：超过就不是一段能当场念完的口径，是又一份文档。");
            Assert.Contains($"≤{Ceiling} 字", Section(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Narrative_UsesNoEmptySloganWords()
    {
        foreach (var row in Blocks())
        {
            foreach (var word in CopyRules.BannedWords)
            {
                Assert.DoesNotContain(word, row.Narrative, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 每块的出处都要落到实物：``README.md``/``docs/NOTES.md`` 后跟「小节名」的，那一节必须真是那份文件里的标题；
    /// <c>类名.断言名</c> 的，测试工程里必须真有这一条断言。编一个不存在的小节或断言名，即红。
    /// </summary>
    [Fact]
    public void Narrative_EveryBlockCitesASectionThatActuallyExists()
    {
        foreach (var row in Blocks())
        {
            var matches = Regex.Matches(row.Citations, @"`([^`]+)`(?:「([^」]+)」)?");
            Assert.True(matches.Count > 0, $"「{row.Name}」没有出处：这一段只是又一次自述，听的人无法核对。");

            var docCitations = 0;
            foreach (Match match in matches)
            {
                var token = match.Groups[1].Value;

                // 冒烟段名（RunXxx）是第三种引用形状：它由 Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun
                // 判「真的接进了冒烟器的运行清单」，这里只跳过，不重复判（两处各判一半，改一处漏一处即红）。
                if (IsSmokeSegment(token)) continue;

                if (!match.Groups[2].Success)
                {
                    var parts = token.Split('.');
                    Assert.True(parts.Length == 2, $"「{row.Name}」的引用「{token}」既不是带小节的文档出处，也不是「类名.断言名」。");
                    var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                    Assert.True(File.Exists(testFile), $"「{row.Name}」引用的测试类不存在：{parts[0]}。");
                    Assert.True(Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                        $"「{row.Name}」引用的断言不存在：{token}。");
                    continue;
                }

                Assert.True(CitableDocs.Contains(token),
                    $"「{row.Name}」把出处指向「{token}」，小节出处只认 {string.Join(" / ", CitableDocs)}：别处的标题这张表核不到。");
                Assert.True(File.Exists(Path.Combine(Repo.Root, token)), $"「{row.Name}」引用的文件不存在：{token}。");

                var heading = match.Groups[2].Value;
                var titled = File.ReadAllLines(Path.Combine(Repo.Root, token)).Any(l =>
                    (l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal))
                    && l.Contains(heading, StringComparison.Ordinal));
                Assert.True(titled, $"「{row.Name}」引的小节在 {token} 里不是标题：「{heading}」。");
                docCitations++;
            }

            Assert.True(docCitations > 0, $"「{row.Name}」没有引到 README 或 NOTES 的实体段落：只有断言名的出处讲不出产品故事。");
        }
    }

    /// <summary>冒烟段名：<c>RunXxx</c>（<c>ApifoxHelper.Smoke/Program.cs</c> 里的段方法）。</summary>
    private static bool IsSmokeSegment(string token) => Regex.IsMatch(token, @"^Run[A-Za-z0-9]+$");

    /// <summary>
    /// 五块里至少这么多块必须点到<b>冒烟段名</b>（第一百八十九轮，简报 P0-1）。
    /// <para>
    /// 为什么是地板而不是「每块都要」：答辩现场能当场复跑的是冒烟段（<c>bash .winecheck/demo.sh</c> 逐屏对账），
    /// 但「AI 必要性」那一块讲的是模型那一支，本仓从未调用过模型服务，没有段可指——硬要它指一个段，
    /// 只会逼出一句假话。地板 3 的意思是：五块里至少三块在被追问「能不能当场跑一遍」时答得出东西。
    /// </para>
    /// </summary>
    private const int SmokeSegmentFloor = 3;

    /// <summary>
    /// 每一块都必须点到一个<b>当前源码里真能跑到的</b>证据：冒烟段名或断言名（第一百八十九轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="Narrative_EveryBlockCitesASectionThatActuallyExists"/> 判的是
    /// 「引的小节真的存在」与「引的断言真的存在」，但它<b>判不出「指向的证据能跑」</b>——冒烟段名这种形状
    /// 在它那里根本不被接受（<c>RunXxx</c> 没有点号，会被当成「既不是文档出处也不是断言名」判红），
    /// 于是「每块都指向真实存在的东西」这句话只兑现了一半：文档小节在、断言在，而答辩现场真正要复跑的那一段
    /// 没有任何一块指得到。降级答辩的全部说服力来自「每块都指向真实存在的东西」，这一条把「能跑」补上。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 每块的出处里至少有一条<b>可跑证据</b>——冒烟段名（<c>RunXxx</c>）或断言名
    /// （<c>类名.断言名</c>）；② 冒烟段名必须<b>真的接进了运行清单</b>：<c>ApifoxHelper.Smoke/Program.cs</c> 里
    /// 既有 <c>private static void RunXxx(</c> 的声明，又有 <c>nameof(RunXxx)</c> 的调用点——只声明不接进清单的
    /// 段在报告里不会出现，<c>demo.sh</c> 逐屏对账时找不到它，等于没有；③ 断言名必须在测试工程里真有这一条
    /// （与 <see cref="Narrative_EveryBlockCitesASectionThatActuallyExists"/> 同一判据，这里自判一次是为了
    /// 让「可跑证据」这一条自己站得住，不依赖另一条门是否还在）；④ 至少 <see cref="SmokeSegmentFloor"/> 块
    /// 点到冒烟段名。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判这些证据在真机上跑过</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），
    /// 也不判措辞与字数（那两条各有各的门）。它只保证「答辩现场指着念的每一条，都能在仓里当场跑起来」。
    /// </para>
    /// </summary>
    [Fact]
    public void Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var blocksWithSmokeSegment = 0;

        foreach (var row in Blocks())
        {
            var runnable = new List<string>();
            var smokeSegments = new List<string>();

            foreach (Match match in Regex.Matches(row.Citations, "`([^`]+)`"))
            {
                var token = match.Groups[1].Value;

                // 文档与源码路径不是「能跑的证据」：它们证明「东西在」，不证明「跑得起来」。
                if (token.EndsWith(".md", StringComparison.Ordinal) || token.EndsWith(".cs", StringComparison.Ordinal)) continue;

                if (IsSmokeSegment(token))
                {
                    Assert.True(smoke.Contains($"private static void {token}(", StringComparison.Ordinal),
                        $"「{row.Name}」引的冒烟段「{token}」在 ApifoxHelper.Smoke/Program.cs 里没有声明："
                        + "答辩现场照着念的段名必须真的存在，否则 demo.sh 逐屏对账时找不到它。");
                    Assert.True(smoke.Contains($"nameof({token})", StringComparison.Ordinal),
                        $"「{row.Name}」引的冒烟段「{token}」没有接进运行清单（找不到 nameof({token})）："
                        + "只声明不调用的段在报告里不会出现，等于没有这一段。");
                    runnable.Add(token);
                    smokeSegments.Add(token);
                    continue;
                }

                var parts = token.Split('.');
                if (parts.Length != 2) continue; // 其余形状由 Narrative_EveryBlockCitesASectionThatActuallyExists 判红。

                var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                Assert.True(File.Exists(testFile), $"「{row.Name}」引用的测试类不存在：{parts[0]}。");
                Assert.True(Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                    $"「{row.Name}」引用的断言不存在：{token}。");
                runnable.Add(token);
            }

            Assert.True(runnable.Count > 0,
                $"「{row.Name}」一块可跑证据都没有（冒烟段名 RunXxx 或断言名 类名.断言名）："
                + "只有文档小节与源码路径的出处，在被追问「能不能当场跑一遍」时答不出东西。");

            if (smokeSegments.Count > 0) blocksWithSmokeSegment++;
        }

        Assert.True(blocksWithSmokeSegment >= SmokeSegmentFloor,
            $"五块里只有 {blocksWithSmokeSegment} 块点到了冒烟段名（地板 {SmokeSegmentFloor}）："
            + "答辩现场能当场复跑的是冒烟段（bash .winecheck/demo.sh），只指断言的块在「现场复跑」这一问上答不出东西。");
    }
}
