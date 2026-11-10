using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 初审答辩口径四块的机械门（<c>docs/NOTES.md</c>「初审答辩口径（真机不可得时）」，第一百七十轮立，第一百七十一轮补第四块，简报 P0-1）。
/// <para>
/// <b>为什么要有这一节</b>：G-D5 缺的是外部供给的机器，本仓交付不了。真机到手之前，答辩现场能拿出来的
/// 只有「一条命令复跑的 <c>.winecheck/demo.sh</c> + 一份讲得清的叙事」。脚本第一百六十七轮就有门了，
/// 叙事此前只在各轮记录里散着——散着的东西每轮重写一遍、每轮漂一点，到答辩那天就得现场编。
/// 这一节把它落成四块定稿：差异化、AI 必要性、可经营、排版归因。
/// </para>
/// <para>
/// <b>第四块补的是哪一处软肋</b>：第一百七十轮量出「四种取值排版相同」，于是「排版禁则为什么成立」这一问
/// 有了答案，而答案<b>不在口径里</b>——被追问时只能现场组织语言，而现场组织的语言容易把功劳记给
/// <c>line-break:strict</c>（本机恰恰判不出它）。第四块把这句正面写下来，判的还是同三件事：字数、禁词、出处。
/// </para>
/// <para>
/// <b>各块被什么钉住</b>：① 四块齐、每块 ≤80 字（超过 80 字就不是「当场念的一段」，是又一轮文档）；
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

    /// <summary>四块的块名是封闭的：改了名字等于换了四件事，门要认的是这四件。</summary>
    private static readonly string[] ExpectedBlocks = ["差异化", "AI 必要性", "可经营", "排版归因"];

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
}
