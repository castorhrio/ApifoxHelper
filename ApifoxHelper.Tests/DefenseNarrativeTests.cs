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
        var blocksWithSmokeSegment = 0;

        foreach (var row in Blocks())
        {
            var (runnable, smokeSegments) = RunnableEvidence(row.Name, row.Citations);

            Assert.True(runnable.Count > 0,
                $"「{row.Name}」一块可跑证据都没有（冒烟段名 RunXxx 或断言名 类名.断言名）："
                + "只有文档小节与源码路径的出处，在被追问「能不能当场跑一遍」时答不出东西。");

            if (smokeSegments.Count > 0) blocksWithSmokeSegment++;
        }

        Assert.True(blocksWithSmokeSegment >= SmokeSegmentFloor,
            $"五块里只有 {blocksWithSmokeSegment} 块点到了冒烟段名（地板 {SmokeSegmentFloor}）："
            + "答辩现场能当场复跑的是冒烟段（bash .winecheck/demo.sh），只指断言的块在「现场复跑」这一问上答不出东西。");
    }
    /// <summary>
    /// 一块出处里「能跑的证据」——<b>两条门共用这一份清单</b>（第一百九十四轮抽出）。
    /// <para>
    /// 返回 <c>(可跑证据, 其中的冒烟段)</c>：冒烟段名（<c>RunXxx</c>）必须真有声明、真有 <c>nameof</c> 调用点；
    /// 断言名（<c>类名.断言名</c>）必须真在测试工程里。文档与源码路径不算「能跑的证据」——
    /// 它们证明「东西在」，不证明「跑得起来」。
    /// </para>
    /// <para>
    /// <b>为什么抽出来</b>：<see cref="Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun"/> 与
    /// <see cref="ScriptTemplate_EveryBlockHasAPronounceableSentence"/> 判的是同一件事的两份材料
    /// （口径表与讲稿模板）。各写一份解析，改一处漏一处时两份材料就会各绿各的——
    /// 而答辩现场念的是讲稿模板、被追问时翻的是口径表，两份对不上正是最难当场发现的那种错。
    /// </para>
    /// </summary>
    private static (List<string> Runnable, List<string> SmokeSegments) RunnableEvidence(string blockName, string citations)
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var runnable = new List<string>();
        var smokeSegments = new List<string>();

        foreach (Match match in Regex.Matches(citations, "`([^`]+)`"))
        {
            var token = match.Groups[1].Value;

            // 文档与源码路径不是「能跑的证据」：它们证明「东西在」，不证明「跑得起来」。
            if (token.EndsWith(".md", StringComparison.Ordinal) || token.EndsWith(".cs", StringComparison.Ordinal)) continue;

            if (IsSmokeSegment(token))
            {
                Assert.True(smoke.Contains($"private static void {token}(", StringComparison.Ordinal),
                    $"「{blockName}」引的冒烟段「{token}」在 ApifoxHelper.Smoke/Program.cs 里没有声明："
                    + "答辩现场照着念的段名必须真的存在，否则 demo.sh 逐屏对账时找不到它。");
                Assert.True(smoke.Contains($"nameof({token})", StringComparison.Ordinal),
                    $"「{blockName}」引的冒烟段「{token}」没有接进运行清单（找不到 nameof({token})）："
                    + "只声明不调用的段在报告里不会出现，等于没有这一段。");
                runnable.Add(token);
                smokeSegments.Add(token);
                continue;
            }

            var parts = token.Split('.');
            if (parts.Length != 2) continue; // 其余形状由 Narrative_EveryBlockCitesASectionThatActuallyExists 判红。

            var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
            Assert.True(File.Exists(testFile), $"「{blockName}」引用的测试类不存在：{parts[0]}。");
            Assert.True(Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                $"「{blockName}」引用的断言不存在：{token}。");
            runnable.Add(token);
        }

        return (runnable, smokeSegments);
    }

    /// <summary>讲稿模板那一小节的标题前缀（第一百九十四轮，简报 P0-1）。</summary>
    private const string TemplateHeading = "### 五块讲稿模板";

    /// <summary>
    /// 指向 wine 冒烟段的句子必须带的限定词：模拟器读数不许说成真机结果
    /// （与 <c>WordingHonestyTests.RealMachineClaims_RequireEvidencePaths</c> 同一口径）。
    /// </summary>
    private const string WineQualifier = "在 Linux 容器内 wine 模拟器上";

    /// <summary>
    /// 把模拟器读数说成真机结果的四种措辞（封闭清单）。句子可以提「真机未验」——那是实话；
    /// 不许说的是「真机量到」这一类。
    /// </summary>
    private static readonly string[] RealMachineClaims = ["真机量到", "真机实测", "真机验证", "真机跑过"];

    private readonly record struct TemplateRow(string Name, string Sentence, string Evidence, string FollowUp);

    /// <summary>讲稿模板表的行（块 / 照抄念的句子 / 指向的证据 / 被追问时接什么）。</summary>
    private static List<TemplateRow> TemplateRows()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(TemplateHeading, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺少「{TemplateHeading}」一小节：五块口径是「要点」，答辩人要念的是句子。");
        var rest = notes[(start + TemplateHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];

        var rows = section.Split('\n')
            .Where(l => l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .Where(c => c.Count == 4)
            .Where(c => c[0] != "块" && !c[0].All(ch => ch is '-' or ' ' or ':'))
            .Select(c => new TemplateRow(c[0], c[1], c[2], c[3]))
            .ToList();
        Assert.True(rows.Count == ExpectedBlocks.Length,
            $"讲稿模板有 {rows.Count} 块，简报要的是 {ExpectedBlocks.Length} 块（{string.Join("、", ExpectedBlocks)}）。");
        return rows;
    }

    /// <summary>
    /// 五块讲稿模板：每块一句能当场念完的话，且它指向的证据在当前源码里真能跑到（第一百九十四轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun"/> 判住了
    /// 「口径表每块都指向能跑的证据」，但口径表写的是<b>要点</b>（「导入按源文件重写一遍，人工写好的说明会被覆盖」），
    /// 不是<b>能念出口的句子</b>。答辩人照着要点现场组织语言，最容易把「wine 模拟器量到」说成「跑过了」——
    /// 而这句话一旦说出口，前面十几轮攒下的诚实口径就白攒了。这一条把「照抄即可念」落成材料并加门。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 五块齐且顺序与口径表一致（块名只在 <see cref="ExpectedBlocks"/> 一处表达）；
    /// ② 每句非空、≤<see cref="Ceiling"/> 字、不含 <see cref="CopyRules.BannedWords"/>、不含反引号
    /// （带反引号的句子念不出来，那是给读的人看的）；③ 「指向的证据」列至少一条可跑证据，且解析走
    /// <see cref="RunnableEvidence"/> 这<b>同一份清单</b>（冒烟段须真有声明与 <c>nameof</c> 调用点、
    /// 断言须真在测试工程里）；④ 指向 wine 冒烟段的句子必须带 <see cref="WineQualifier"/>；
    /// ⑤ 句子不许出现 <see cref="RealMachineClaims"/> 里的四种措辞；⑥ 「被追问时接什么」不许空着。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），也不判措辞好不好听——
    /// 措辞会漂，钉措辞的门最后只会被改措辞绕过。它只保证「照抄念出来的每一句，指向的东西都在仓里跑得起来，
    /// 且没有一句把模拟器读数说成真机结果」。
    /// </para>
    /// </summary>
    [Fact]
    public void ScriptTemplate_EveryBlockHasAPronounceableSentence()
    {
        var rows = TemplateRows();
        Assert.Equal(ExpectedBlocks, rows.Select(r => r.Name));

        foreach (var row in rows)
        {
            Assert.True(row.Sentence.Length > 0, $"「{row.Name}」的讲稿模板是空的：空的一句在答辩现场念不出任何东西。");
            Assert.True(row.Sentence.Length <= Ceiling,
                $"「{row.Name}」的讲稿句子 {row.Sentence.Length} 字，超过 {Ceiling} 字上限：超过就不是一句能当场念完的话。");
            Assert.DoesNotContain("`", row.Sentence, StringComparison.Ordinal);
            foreach (var word in CopyRules.BannedWords)
            {
                Assert.DoesNotContain(word, row.Sentence, StringComparison.Ordinal);
            }

            var (runnable, smokeSegments) = RunnableEvidence(row.Name, row.Evidence);
            Assert.True(runnable.Count > 0,
                $"「{row.Name}」的讲稿模板没有指向任何可跑证据（冒烟段名 RunXxx 或断言名 类名.断言名）："
                + "念完指不出东西，等于又一次自述。");

            if (smokeSegments.Count > 0)
            {
                Assert.Contains(WineQualifier, row.Sentence, StringComparison.Ordinal);
            }

            foreach (var claim in RealMachineClaims)
            {
                Assert.DoesNotContain(claim, row.Sentence, StringComparison.Ordinal);
            }

            Assert.True(row.FollowUp.Length > 0, $"「{row.Name}」没有写「被追问时接什么」：现场被追问时只能临时组织语言。");
        }
    }

    // ---------- 第二百一十四轮（简报 P0-2）：一口气念完的总字数 + 念出来的语气 ----------

    /// <summary>五块连起来念一遍的总字数上限（简报与上面「口径」那段给的数都是 400）。</summary>
    private const int OneBreathCeiling = 400;

    /// <summary>
    /// 五块连起来念得完：总字数 ≤<see cref="OneBreathCeiling"/>、本节写的那个读数与现算值逐字相同、
    /// 且同一段可跑证据不许被两块引用。
    /// <para>
    /// <b>补的是哪一处债</b>：<c>docs/NOTES.md</c>「初审答辩口径（真机不可得时）」的「口径」那一段从第一百九十八轮起
    /// 就写着「加门：<c>DefenseNarrativeTests.Narrative_FiveBlocksReadInOneBreath</c>」，而<b>测试工程里从来没有这一条</b>
    /// （第二百一十四轮逐块改文案时去核才发现）。这不是少写一条门那么轻：那一段声明的两件事——总字数上限、
    /// 证据不许重复引用——正是<b>只有把五块连起来念才会暴露</b>的两种毛病（单块 ≤80 字各自都绿，
    /// 念到第五块已经两分半；两块指同一段冒烟证据，念出来是同一句话说了两遍）。散文里写着有、代码里没有，
    /// 下一轮照着那段话读就会把它当成已核过的事实。本轮把这条实现补上，而不是把那句话删掉。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 五块「口径」列字符数之和 ≤400；② 本节散文里那句「当前 N 字」的 N 必须等于现算之和
    /// （读数与门算的不是同一个数时，读的人拿它当另一个口径）；③ 每块的可跑证据取法仍然只有
    /// <see cref="RunnableEvidence"/> 这一份，两块之间不许出现同一条（冒烟段名或断言名都算）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判念起来好不好听、不判单块字数（那条在
    /// <see cref="Narrative_HasEveryExpectedBlockAndEachFitsTheEightyCharacterCeiling"/>）、不判证据存不存在
    /// （那两条在 <see cref="Narrative_EveryBlockCitesASectionThatActuallyExists"/> 与
    /// <see cref="Narrative_EveryBlockPointsAtEvidenceThatCanActuallyRun"/>）。
    /// </para>
    /// </summary>
    [Fact]
    public void Narrative_FiveBlocksReadInOneBreath()
    {
        var rows = Blocks();
        var total = rows.Sum(row => row.Narrative.Length);

        // ① 总字数。
        Assert.True(total <= OneBreathCeiling,
            $"五块连起来 {total} 字，超过 {OneBreathCeiling} 字上限：单块各不超一句，加起来却念不完一遍——"
            + "初审现场给答辩人的是「一口气」，不是五分钟。逐块字数：「"
            + string.Join("」「", rows.Select(row => $"{row.Name} {row.Narrative.Length}")) + "」。");

        // ② 本节写的那个读数必须等于现算值。
        var stated = Regex.Matches(Section(), @"当前 (?<n>\d+) 字");
        Assert.True(stated.Count == 1,
            $"这一节的散文里有 {stated.Count} 处「当前 N 字」，要恰好一处：多于一个就有两个读数可挑，"
            + "零个则本条 ② 无从可核（改了文案要一起改这一句，这一句就是那条门钉着的话）。");
        var declared = int.Parse(stated[0].Groups["n"].Value, CultureInfo.InvariantCulture);
        Assert.True(declared == total,
            $"这一节写「当前 {declared} 字」，五块之和现算是 {total} 字：改了口径列没改这一句，"
            + "下一轮读这段话的人会拿它当另一个口径（这一条正是「散文写着有、门算的是另一回事」那种漂移）。");

        // ③ 同一段可跑证据不许被两块引用。
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var (runnable, _) = RunnableEvidence(row.Name, row.Citations);
            foreach (var evidence in runnable)
            {
                if (!owners.TryGetValue(evidence, out var previous))
                {
                    owners[evidence] = row.Name;
                    continue;
                }

                Assert.Fail(
                    $"「{row.Name}」与「{previous}」引用了同一段可跑证据 {evidence}：两块指同一个东西，"
                    + "念出来就是同一句话说两遍——要么这两块在说同一件事（该合），要么其中一块本来就没有自己的证据可指。");
            }
        }
    }

    /// <summary>
    /// 念出来的十句里不许有套话结构（<see cref="CopyRules.EssayStructures"/>），并自证这份词表在本仓自己的文案面上是空的。
    /// <para>
    /// <b>补的是哪一处软肋</b>：这一族的旧门判<b>字数</b>（≤80）、判<b>禁词</b>（<see cref="CopyRules.BannedWords"/>）、
    /// 判<b>出处</b>（能不能 resolve），三样都是可数的。<b>「这段话像不像一个真实产品的作者在说话」没有一条门判</b>——
    /// 而冲第一口径里「去 AI 味道」与「初审答辩要能演示」这两条要的正是它。禁词表管不到「不仅…而且」「旨在」
    /// 「大幅提升」「打造/助力/生态/抓手」这类<b>结构</b>：它们没有一个算「空话词」，念出来却像宣传册。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 五块口径 + 五句讲稿模板（<see cref="TemplateRows"/>，同一批东西的口播版）共十句里
    /// 不得出现词表中的任何一条；② <b>这份词表自己得站得住</b>——每一条在「本仓真实文案面」
    /// （<c>Desktop/Native/</c> 全部源码 + <c>Api/ErrorCopy.cs</c>）出现次数必须为 0。
    /// 词表的取法就是「只收本仓一次都没写过的」，② 把这句取法变成可核对的：收错一个词（本仓正文其实天天在用），
    /// 这条门会先红在自己身上，而不是等到有人写出一句正常话被判成违例。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判措辞好不好听、不判句子长短、不判引用是否 resolve（各有各的门）。它只在有人
    /// （包括下一轮的自己）往这十句里塞回宣传腔结构时红一次。<b>钉措辞的门最后只会被改措辞绕过</b>，
    /// 所以这里钉的是<b>结构</b>：词表是一份封闭清单，不在清单上的说法一概不管。
    /// </para>
    /// </summary>
    [Fact]
    public void Narrative_BlocksSayNothingAnEssayWouldSay()
    {
        var spoken = Blocks().Select(row => (row.Name, Surface: "口径", Text: row.Narrative))
            .Concat(TemplateRows().Select(row => (row.Name, Surface: "讲稿", Text: row.Sentence)))
            .ToList();
        Assert.Equal(ExpectedBlocks.Length * 2, spoken.Count);

        foreach (var (name, surface, text) in spoken)
        {
            foreach (var structure in CopyRules.EssayStructures)
            {
                Assert.True(!text.Contains(structure, StringComparison.Ordinal),
                    $"「{name}」的{surface}里出现「{structure}」：这一句念出来像宣传册，不像做这东西的人在讲自己做了什么。"
                    + "禁的是结构（成效副词、愿望式开头、三方堆叠），不是把中文写成电报——"
                    + "要绕过去请换成具体动作与具体数字，不要换一个同义的宣传词。");
            }
        }

        // ② 词表自证：本仓自己的文案面必须一次都没写过这些词。
        var ownVoice = Repo.NativeSources().Select(source => source.Text)
            .Append(Repo.Source("Api", "ErrorCopy.cs"));
        foreach (var structure in CopyRules.EssayStructures)
        {
            var hits = ownVoice.Count(text => text.Contains(structure, StringComparison.Ordinal));
            Assert.True(hits == 0,
                $"词表收了「{structure}」，但它在仓内 {hits} 个界面或错误文案文件里出现过：这条门会把仓里已有的说法判成违例。"
                + "取法是「只收本仓一次都没写过的」——要么把它从词表去掉，要么改成搭配（如「有效提升」而不是「有效」）。");
        }
    }

    // ---------- 第一百九十五轮（简报 P0-1）：被追问真机时的三句答法 ----------

    /// <summary>「被追问真机时的三句答法」那一小节的标题前缀（第一百九十五轮，简报 P0-1）。</summary>
    private const string DeferredAnswerHeading = "### 被追问真机时的三句答法";

    /// <summary>三句答法的字数上限。简报给的数是 60，反引号里的命令不计（命令是给读的人敲的，不是念的）。</summary>
    private const int DeferredAnswerCeiling = 60;

    /// <summary>三句答法的一行：序号 / 照抄念的句子 / 指向的实物。</summary>
    private readonly record struct DeferredAnswer(string Marker, string Sentence, string Evidence);

    /// <summary>三句答法那一小节的正文（标题行到下一个二级标题之前）。</summary>
    private static string DeferredAnswerSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(DeferredAnswerHeading, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺少「{DeferredAnswerHeading}」一小节：本文件里只有「不许把 wine 说成真机」的禁令，"
            + "没有「模拟器与真机差多少」的正面答法——被追问时现场只能现编。");
        var rest = notes[(start + DeferredAnswerHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    private static List<DeferredAnswer> DeferredAnswers()
    {
        var rows = DeferredAnswerSection().Split('\n')
            .Where(l => l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .Where(c => c.Count == 3)
            .Where(c => c[0] != "序号" && !c[0].All(ch => ch is '-' or ' ' or ':'))
            .Select(c => new DeferredAnswer(c[0], c[1], c[2]))
            .ToList();
        Assert.True(rows.Count == 3,
            $"「{DeferredAnswerHeading}」有 {rows.Count} 句，简报要的是 3 句（如实说 / 说清缺什么 / 带回可复跑的）。");
        return rows;
    }

    /// <summary>反引号里的引用是否落到实物：仓内文件存在，或 <c>类名.断言名</c> 在测试工程里真有这一条。</summary>
    private static bool CitationResolves(string citation)
    {
        if (citation.EndsWith(".md", StringComparison.Ordinal) || citation.EndsWith(".cs", StringComparison.Ordinal)
            || citation.EndsWith(".sh", StringComparison.Ordinal) || citation.EndsWith(".ps1", StringComparison.Ordinal))
        {
            return File.Exists(Path.Combine(Repo.Root, citation.Replace('\\', '/')));
        }

        var parts = citation.Split('.');
        if (parts.Length != 2) return false;
        var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
        return File.Exists(testFile)
            && Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]");
    }

    /// <summary>冒烟段名是否真能跑到：既有声明，又接进了运行清单（<c>nameof</c> 调用点）。</summary>
    private static bool SmokeSegmentExists(string name)
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        return smoke.Contains($"private static void {name}(", StringComparison.Ordinal)
            && smoke.Contains($"nameof({name})", StringComparison.Ordinal);
    }

    /// <summary>
    /// 被追问「模拟器与真机差多少」时的三句答法：三句齐、每句 ≤60 字、无空话禁词、出处落到实物、
    /// 且至少一句点到可复跑命令或冒烟段名（第一百九十五轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="ScriptTemplate_EveryBlockHasAPronounceableSentence"/> 判住了
    /// 「五块讲稿每句都指向能跑的证据」，但五块讲的是<b>凭什么值得做</b>；「模拟器与真机差多少」这一问
    /// 此前只有禁令（<c>WordingHonestyTests.RealMachineClaims_RequireEvidencePaths</c> 判住「不许把 wine 说成真机」）。
    /// 禁令管的是「不许说什么」，不管「该说什么」——被追问时现场只能现编，而现编的那句话最容易把
    /// 「模拟器量到」说成「跑过了」。三句定稿把「如实说 / 说清缺什么 / 带回可复跑的」写成能照抄念的句子。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 三句齐且顺序固定（① 如实说 → ② 说清缺什么 → ③ 带回可复跑的）；
    /// ② 每句非空、≤<see cref="DeferredAnswerCeiling"/> 字（反引号里的命令不计）、不含
    /// <see cref="CopyRules.BannedWords"/>；③ 每句的「指向的实物」至少一条引用，且<b>真的 resolve</b>
    /// （文件存在或断言真有这一条）；④ 至少一句点到可复跑命令（<c>.winecheck/demo.sh</c>）或冒烟段名
    /// （<c>RunXxx</c>，须真有声明与 <c>nameof</c> 调用点）——三句全是自述、一句都指不出能跑的东西，等于没答。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），也不判措辞好不好听——
    /// 措辞会漂，钉措辞的门最后只会被改措辞绕过。它只保证「被追问时照抄念的三句，指的东西都在仓里跑得起来」。
    /// </para>
    /// </summary>
    [Fact]
    public void DeferredAnswer_WhenPressedForRealMachine_HasThreeSentencesAndPointsAtRunnable()
    {
        var rows = DeferredAnswers();

        // ① 三句齐、顺序固定：顺序就是答话的顺序（先如实说，再说缺什么，最后把能复跑的递过去）。
        Assert.Equal(["①", "②", "③"], rows.Select(r => r.Marker));

        // 字数上限必须写在文档里：读的人要知道「念多长」，不是靠数。
        Assert.Contains($"≤{DeferredAnswerCeiling} 字", DeferredAnswerSection(), StringComparison.Ordinal);

        var runnable = 0;
        foreach (var row in rows)
        {
            var spoken = row.Sentence.Replace("`", string.Empty);
            Assert.True(spoken.Length > 0, $"第 {row.Marker} 句是空的：空的一句在被追问时念不出任何东西。");
            Assert.True(spoken.Length <= DeferredAnswerCeiling,
                $"第 {row.Marker} 句 {spoken.Length} 字，超过 {DeferredAnswerCeiling} 字上限：超过就不是一句能当场念完的答话。");

            foreach (var word in CopyRules.BannedWords)
            {
                Assert.DoesNotContain(word, spoken, StringComparison.Ordinal);
            }

            // ③ 出处必须落到实物：指一个不存在的小节或断言，等于又一次自述。
            var citations = Regex.Matches(row.Evidence, "`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
            Assert.True(citations.Count > 0, $"第 {row.Marker} 句没有指向任何实物：念完指不出东西，听的人无法核对。");
            foreach (var citation in citations)
            {
                Assert.True(CitationResolves(citation),
                    $"第 {row.Marker} 句指向的「{citation}」在仓里找不到（既不是存在的文件，也不是测试工程里真有的断言）。");
            }

            // ④ 可跑证据：可复跑命令，或真接进运行清单的冒烟段名。
            if (spoken.Contains(".winecheck/demo.sh", StringComparison.Ordinal))
            {
                runnable++;
                continue;
            }

            if (Regex.Matches(spoken, @"Run[A-Za-z0-9]+").Any(m => SmokeSegmentExists(m.Value))) runnable++;
        }

        Assert.True(runnable >= 1,
            "三句答法里没有一句点到可复跑命令（.winecheck/demo.sh）或冒烟段名（RunXxx）："
            + "被追问「能不能当场跑一遍」时，三句全是自述，答不出东西。");
    }

    // ---------- 第二百零四轮（简报 P0-1）：降级答辩逐屏演示脚本 ----------

    /// <summary>「降级答辩逐屏演示脚本」那一节的标题前缀（二级标题，落在「降级答辩脚本」之后）。</summary>
    private const string DemoScriptHeading = "## 降级答辩逐屏演示脚本（可当场跑";

    /// <summary>每屏台词的字数上限。简报给的数是 60。</summary>
    private const int DemoLineCeiling = 60;

    /// <summary>屏数区间（简报给的数是 7–10 屏）。</summary>
    private const int DemoScreenFloor = 7;

    private const int DemoScreenCeiling = 10;

    /// <summary>演示脚本的一行：序号 / 屏 / 一句台词 / 当场跑的命令 / 念哪一行读数。</summary>
    private readonly record struct DemoScreen(string Number, string Screen, string Line, string Command, string Reading);

    /// <summary>演示脚本那一节的正文行（标题行到下一个二级标题之前）。</summary>
    private static string[] DemoScriptLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(DemoScriptHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺「{DemoScriptHeading}…）」一节：答辩现场要做的动作是「跑一条命令 → 念报告里那一行读数」，"
            + "而这三步此前没有一页能照着走——讲的人得自己从逐屏表里挑屏、自己拼命令、自己到讲稿里找那一行。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<DemoScreen> DemoScreens()
    {
        var rows = new List<DemoScreen>();
        foreach (var line in DemoScriptLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count == 0 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            Assert.True(cells.Count == 5,
                $"演示脚本有一行不是 5 列（实际 {cells.Count} 列，应为 序号 / 屏 / 一句台词 / 当场跑的命令 / 念哪一行读数）：{cells[0]}。");
            rows.Add(new DemoScreen(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return rows;
    }

    /// <summary>
    /// 上一节「降级答辩脚本」逐屏表「证据出处」一列里引用的冒烟段名。
    /// <para>
    /// <c>.winecheck/demo.sh</c> 只给<b>逐屏表引到的段</b>写讲稿行（它按 <c>^\| \d</c> 取行、从第 4 格
    /// <c>grep -oE 'Run[A-Za-z0-9]+'</c>）。所以「这一屏念哪一行读数」点到的段若不在这一份清单里，
    /// 讲稿里根本不会有那一行——照着念就是空手。取法与脚本同一口径（只取表格行、只取证据出处那一格）。
    /// </para>
    /// </summary>
    private static HashSet<string> CitedSmokeSegments()
    {
        var notes = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(notes, l => l.StartsWith("## 降级答辩脚本", StringComparison.Ordinal));
        Assert.True(start >= 0, "docs/NOTES.md 缺「## 降级答辩脚本」一节（演示脚本的屏单出处）。");
        var end = Array.FindIndex(notes, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var cited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in notes[start..(end < 0 ? notes.Length : end)])
        {
            if (!Regex.IsMatch(line, @"^\| \d")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count != 5) continue;
            foreach (Match m in Regex.Matches(cells[3], @"Run[A-Za-z0-9]+")) cited.Add(m.Value);
        }

        return cited;
    }

    /// <summary>
    /// 降级答辩逐屏演示脚本：每屏一句能念的台词 + 一条当场能跑的命令 + 一行真能念到的读数
    /// （第二百零四轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="ScriptTemplate_EveryBlockHasAPronounceableSentence"/> 判住了
    /// 「五块讲稿每句都指向能跑的证据」，<see cref="DeferredAnswer_WhenPressedForRealMachine_HasThreeSentencesAndPointsAtRunnable"/>
    /// 判住了「被追问时的三句答法」——但两处都是<b>要点与答话</b>，不是<b>现场动作</b>。答辩现场真正要做的
    /// 是「打开终端 → 跑一条命令 → 念报告里那一行读数」，而这三步此前没有一页能照着走：讲的人得自己从
    /// 逐屏表里挑屏、自己拼命令、自己到 <c>/tmp/ah-demo/narration-&lt;档位&gt;.txt</c> 里找那一行。
    /// 现场拼出来的命令最容易写错一个档位或一个段名，而写错之后讲稿里那一行根本不存在——照着念就是空手。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 屏数在 <see cref="DemoScreenFloor"/>–<see cref="DemoScreenCeiling"/> 之间、
    /// 编号从 1 起连续、每屏四格不空；② 每句台词非空、≤<see cref="DemoLineCeiling"/> 字、不含
    /// <see cref="CopyRules.BannedWords"/>、不含反引号（带反引号的句子念不出来）；③ 每屏「当场跑的命令」
    /// 必须点到 <c>.winecheck/demo.sh</c>；④ 每屏「念哪一行读数」点到的 <c>RunXxx</c> 必须<b>既在
    /// <c>ApifoxHelper.Smoke/Program.cs</c> 里有声明与 <c>nameof</c> 调用点、又被上一节逐屏表引用</b>
    /// （<see cref="CitedSmokeSegments"/>）——只满足一半的段在讲稿里不会出现；⑤ 这一节写着限定词
    /// <see cref="WineQualifier"/>（模拟器读数不许说成真机结果），并点名本门。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），也不判台词好不好听——
    /// 措辞会漂，钉措辞的门最后只会被改措辞绕过。它只保证「现场照着念的每一屏，都能在仓里当场跑出那一行读数」。
    /// </para>
    /// </summary>
    [Fact]
    public void DemoScript_EveryScreenNamesACommandThatRuns()
    {
        var rows = DemoScreens();

        // ① 屏数在区间内、编号从 1 起连续。
        Assert.True(rows.Count >= DemoScreenFloor && rows.Count <= DemoScreenCeiling,
            $"演示脚本有 {rows.Count} 屏，简报要的是 {DemoScreenFloor}–{DemoScreenCeiling} 屏："
            + "少于下限现场讲不完主链路，多于上限一屏一句就变成又一份文档。");
        Assert.Equal(Enumerable.Range(1, rows.Count).Select(n => n.ToString()), rows.Select(r => r.Number));

        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var cited = CitedSmokeSegments();

        foreach (var row in rows)
        {
            // ① 四格不空：空着的那一格正是现场要临时编的东西。
            foreach (var (label, cell) in new[]
                     {
                         ("屏", row.Screen), ("一句台词", row.Line), ("当场跑的命令", row.Command), ("念哪一行读数", row.Reading),
                     })
            {
                Assert.True(cell.Length >= 4, $"演示脚本第 {row.Number} 屏的「{label}」格太短或空着（{cell}）：现场照着念不出来。");
            }

            // ② 台词：能念、不空、不超长、不带反引号、不含空话禁词。
            Assert.True(row.Line.Length <= DemoLineCeiling,
                $"演示脚本第 {row.Number} 屏的台词 {row.Line.Length} 字，超过 {DemoLineCeiling} 字上限：超过就不是一句能当场念完的话。");
            Assert.DoesNotContain("`", row.Line, StringComparison.Ordinal);
            foreach (var word in CopyRules.BannedWords)
            {
                Assert.DoesNotContain(word, row.Line, StringComparison.Ordinal);
            }

            // ③ 命令必须点到那条唯一可复跑的脚本。
            Assert.Contains(".winecheck/demo.sh", row.Command, StringComparison.Ordinal);

            // ④ 读数：段名必须真能跑到——既有声明与 nameof 调用点，又被逐屏表引用（讲稿里才会有那一行）。
            var segments = Regex.Matches(row.Reading, @"Run[A-Za-z0-9]+").Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
            Assert.True(segments.Count > 0,
                $"演示脚本第 {row.Number} 屏的「念哪一行读数」一个冒烟段名（RunXxx）都没点到："
                + "念完指不出一行读数，等于又一次自述。");
            foreach (var segment in segments)
            {
                Assert.True(smoke.Contains($"private static void {segment}(", StringComparison.Ordinal),
                    $"演示脚本第 {row.Number} 屏引的冒烟段「{segment}」在 ApifoxHelper.Smoke/Program.cs 里没有声明："
                    + "现场照着念的段名必须真的存在。");
                Assert.True(smoke.Contains($"nameof({segment})", StringComparison.Ordinal),
                    $"演示脚本第 {row.Number} 屏引的冒烟段「{segment}」没有接进运行清单（找不到 nameof({segment})）："
                    + "只声明不调用的段在报告里不会出现。");
                Assert.True(cited.Contains(segment),
                    $"演示脚本第 {row.Number} 屏引的冒烟段「{segment}」没有被「降级答辩脚本」逐屏表引用："
                    + "demo.sh 只给逐屏表引到的段写讲稿行，这一屏照着念时讲稿里不会有那一行。");
            }
        }

        // ⑤ 限定词与自指：模拟器读数不许说成真机结果；这一节点名本门。
        var section = string.Join('\n', DemoScriptLines());
        Assert.Contains(WineQualifier, section, StringComparison.Ordinal);
        var self = $"{nameof(DefenseNarrativeTests)}.{nameof(DemoScript_EveryScreenNamesACommandThatRuns)}";
        Assert.Contains(self, section, StringComparison.Ordinal);
    }
    // ---------- 第二百二十一轮（简报 P0-1）：答辩当天照这个顺序点 ----------

    /// <summary>「答辩当天执行顺序」那一小节的标题前缀。</summary>
    private const string RunOrderHeading = "### 答辩当天执行顺序";

    /// <summary>当天动作单的步数区间（简报给的数是 ≥5、≤10）。</summary>
    private const int RunOrderFloor = 5;

    /// <summary>步数上限：再多就不是「当天照着点」，是又一份文档。</summary>
    private const int RunOrderCeiling = 10;

    /// <summary>这一小节允许当「路径出处」的两处：NOTES 本身，或 <c>.winecheck/</c> 下的文件。</summary>
    private const string RunOrderNotesPath = "docs/NOTES.md";

    /// <summary><c>.winecheck/</c> 目录前缀（另一处允许的路径出处）。</summary>
    private const string RunOrderScriptDir = ".winecheck/";

    /// <summary>
    /// 「答辩当天执行顺序」那一小节的正文（标题行到下一个二级或三级标题之前）。
    /// <para>
    /// 切到下一个 <c>### </c> 也停：这一小节是「初审答辩口径」一节的最后一段，但下一轮若在它后面
    /// 再加一段，本门不该把别人的字算进这一页（步数会凭空变多，而多出来的那几步没人照着走过）。
    /// </para>
    /// </summary>
    private static string RunOrderSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(RunOrderHeading, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺少「{RunOrderHeading}」一小节：五块口径、逐屏脚本、三句答法三份材料都在，"
            + "但「当天从哪一步开始、按什么顺序走」没有一页写死——答辩现场只能临时拼。");
        var rest = notes[(start + RunOrderHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];
        var sub = section.IndexOf("\n### ", StringComparison.Ordinal);
        return sub < 0 ? section : section[..sub];
    }

    /// <summary>当天动作单的每一步：编号 + 正文（只认顶格的 <c>N. </c> 有序列表项）。</summary>
    private static List<(int Number, string Text)> RunOrderSteps()
    {
        var steps = new List<(int, string)>();
        foreach (var line in RunOrderSection().ReplaceLineEndings("\n").Split('\n'))
        {
            var match = Regex.Match(line, @"^(?<n>\d+)\.\s+(?<t>.+)$");
            if (!match.Success) continue;
            steps.Add((int.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture), match.Groups["t"].Value.Trim()));
        }

        return steps;
    }

    /// <summary>
    /// 答辩当天照这个顺序点：动作单在位、5–10 步、每一步都点到仓里真实存在的路径（第二百二十一轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：真机 0 台、到期日 2026-11-20，降级答辩是关闭 G-D5 的唯一路径。仓里为此攒了三份材料
    /// ——五块口径（凭什么值得做）、逐屏演示脚本（每一屏念哪一行读数）、三句答法（被追问时怎么答）——
    /// 但<b>当天从哪一步开始、按什么顺序走</b>此前只活在人的记忆里：讲的人得自己把三份材料拼成一条线，
    /// 而现场拼出来的顺序最容易漏掉「先预热一次」那一步（不带 <c>PUBLISH</c> 时脚本会当场判
    /// 「冒烟器不是当前源码发出来的」并非零退出，第二百一十八轮真拦过一次），也最容易把「真机呢」答成
    /// 「跑过了」。这一条把「当天照这个顺序点」落成材料并加门。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 这一小节在位；② 有序列表 <see cref="RunOrderFloor"/>–<see cref="RunOrderCeiling"/> 项、
    /// 编号从 1 起连续（多一步是没人走过的动作，少一步是断链）；③ <b>每一步</b>都点到仓里真实存在的路径，
    /// 且那处路径只能是 <c>docs/NOTES.md</c> 或 <c>.winecheck/</c> 下的文件——写错一个路径即红
    /// （当天照着敲就是一条空命令）；④ 五块块名按口径表顺序出现（念稿顺序就是那张表的顺序）、
    /// 至少一步点到冒烟段名（现场演示那一屏，段名须真有声明与 <c>nameof</c> 调用点）、
    /// 且这一小节写着 <c>PUBLISH=1</c> 与「G-D5」「未达成」（预热命令与真机口径是这一页的两处硬事实）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），
    /// 也不判每一步的措辞好不好听——措辞会漂，钉措辞的门最后只会被改措辞绕过。
    /// 它只保证「当天照着点的每一步，指的东西都在仓里，且顺序与念稿顺序一致」。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseRunOrder_EveryStepNamesARealPath()
    {
        var section = RunOrderSection();
        var steps = RunOrderSteps();

        // ① 步数区间 + 编号从 1 起连续。
        Assert.True(steps.Count >= RunOrderFloor && steps.Count <= RunOrderCeiling,
            $"「{RunOrderHeading}」有 {steps.Count} 步，简报要的是 {RunOrderFloor}–{RunOrderCeiling} 步："
            + "少于下限当天走不完主链路，多于上限就不是「照着点」，是又一份文档。");
        Assert.Equal(Enumerable.Range(1, steps.Count), steps.Select(s => s.Number));

        // ② 每一步都点到仓里真实存在的路径（只认 docs/NOTES.md 与 .winecheck/ 下的文件）。
        foreach (var (number, text) in steps)
        {
            var paths = Regex.Matches(text, @"(?:docs/NOTES\.md|\.winecheck/[A-Za-z0-9._/-]+)")
                .Select(m => m.Value.TrimEnd('.'))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.True(paths.Count > 0,
                $"当天动作单第 {number} 步没有点到任何仓内路径（docs/NOTES.md 或 .winecheck/ 下的文件）："
                + "当天照着这一步做时，指不出该翻哪一页、该跑哪一支脚本。");
            foreach (var path in paths)
            {
                Assert.True(File.Exists(Path.Combine(Repo.Root, path.Replace('/', Path.DirectorySeparatorChar))),
                    $"当天动作单第 {number} 步点的路径「{path}」在仓里不存在："
                    + "当天照着敲就是一条空命令，而这一页正是「不现场设计」的那一页。");
            }
        }

        // ③ 五块按口径表顺序出现：念稿顺序就是那张表的顺序，顺序漂了现场会念成另一套。
        var cursor = 0;
        foreach (var block in ExpectedBlocks)
        {
            var at = section.IndexOf(block, cursor, StringComparison.Ordinal);
            Assert.True(at >= 0,
                $"当天动作单里「{block}」没有按口径表顺序出现（上一块之后找不到它）："
                + "五块的顺序在口径表里定，动作单照那个顺序念，现场不重排。");
            cursor = at + block.Length;
        }

        // ④ 预热命令、真机口径、现场演示那一屏——**只在这一页的步骤里判**，不判整节。
        //    判整节会自证：这一页末尾那段「走完之后」本来就写着「这一小节写着 PUBLISH=1 与「G-D5」「未达成」」，
        //    于是把第 2 步（预热命令）与第 6 步（真机口径）整条删掉，门照样绿——那正是本仓反复收口的
        //    「同一条目内两处措辞互相满足」的假绿形状。步骤才是当天照着点的东西，事实必须在步骤里。
        var stepText = string.Join('\n', steps.Select(s => s.Text));
        Assert.Contains("PUBLISH=1", stepText, StringComparison.Ordinal);
        Assert.Contains("G-D5", stepText, StringComparison.Ordinal);
        Assert.Contains("未达成", stepText, StringComparison.Ordinal);

        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        // 段名前面不许紧贴字母：本门自己的名字（DefenseRunOrder_…）里也有「Run」，那不是冒烟段。
        var segments = Regex.Matches(stepText, @"(?<![A-Za-z0-9_])Run[A-Za-z0-9]+")
            .Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(segments.Count > 0,
            $"「{RunOrderHeading}」一个冒烟段名（RunXxx）都没点到：现场演示那一屏指不出来，"
            + "而这一页要回答的正是「现场演示哪一屏」。");
        foreach (var segment in segments)
        {
            Assert.True(smoke.Contains($"private static void {segment}(", StringComparison.Ordinal),
                $"当天动作单引的冒烟段「{segment}」在 ApifoxHelper.Smoke/Program.cs 里没有声明："
                + "现场照着念的段名必须真的存在。");
            Assert.True(smoke.Contains($"nameof({segment})", StringComparison.Ordinal),
                $"当天动作单引的冒烟段「{segment}」没有接进运行清单（找不到 nameof({segment})）："
                + "只声明不调用的段在报告里不会出现，照着念就是空手。");
        }

        // ⑤ 这一节点名本门：门被删掉时，读这一页的人还知道该去核哪一条。
        Assert.Contains($"{nameof(DefenseNarrativeTests)}.{nameof(DefenseRunOrder_EveryStepNamesARealPath)}",
            section, StringComparison.Ordinal);
    }

}
