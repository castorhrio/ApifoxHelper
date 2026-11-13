using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 对外文本（<c>README.md</c> 与 <c>docs/NOTES.md</c>）的中文排版门（第二百三十三轮，简报 P1-1）。
/// <para>
/// <b>要补的缺口</b>：<c>TypographyTests</c> 判字号/行高/字体栈与界面文案的中英文空隙，
/// <c>ServiceCopyTests</c> 判服务层用户可见文案，<c>ReportTypographyTests</c> 判导出报告的渲染字节——
/// 但<b>仓库根那份 README 与开发笔记</b>这两份对外文本没有任何东西在判。它们是最先被读到的东西
/// （答辩现场念的就是它们），漂了只能靠人眼。
/// </para>
/// <para>
/// <b>判两条</b>（DESIGN.md §3「中文排版附加条款」、§6「中文排版」第 ⑤ 条的两侧）：
/// ① 中文与半角字母/数字相邻处必须有空隙（<see cref="CopyRules.IndexOfMissingLatinGap"/>）；
/// ② 中文与全角标点相邻处不得有空隙（<see cref="CopyRules.IndexOfSpaceAroundFullWidthPunct"/>）。
/// 判定口径与界面文案<b>同一份</b>（<see cref="CopyRules"/>），不在这里另写一套。
/// </para>
/// <para>
/// <b>曾经是「登记 + 棘轮」，第二百三十四轮起是硬判</b>：立门那天（第二百三十三轮）这两份文件在 HEAD 上
/// 本来就有一批违例，全部是<b>历史记录里对源码字面量的逐字引用</b>（「路径前缀1」这类序号紧贴中文的旧写法、
/// 「第X轮」这种格式占位、引用常量时那个在「」里看不见的尾随空格）。当时按棘轮判，是因为一次改几十处历史记录
/// 等于把当时的证据改坏。那 11 条已在第二百三十四轮<b>逐条修完</b>：能改字的改字（「订单 2」「报告 1/2/10/20」），
/// 改字就会动到证据的改成<b>行内代码逐字引</b>（源码字面量、格式占位、报告行标签）——一条没删、一条没编。
/// 登记表随之从本文件里删掉，本门改成<b>硬判零违例</b>：<b>此后新增一处即红，且没有「先登记着」这条路</b>，
/// 新写的字没有历史可挡；也不许再长回一张表（那一条由
/// <see cref="ScannedDocs_AreTheTwoExternalOnes_AndNoLegacyTableCameBack"/> 反过来判本文件自己的源码）。
/// </para>
/// <para>
/// <b>扫描范围与不判的东西</b>：只扫这两份文件的<b>正文</b>——围栏代码块（``` 之间）与行内代码
/// （反引号之间）不算正文，那里是命令、路径与标识符（<c>tasks/&lt;任务ID&gt;/</c> 这种写法在代码里是对的）。
/// 不判行首行尾禁则与正文行长（那是会折行的那份产物的性质，落在 <c>ReportTypographyTests</c> 与
/// <c>HtmlWrapTests</c>）；不判界面文案（<c>TypographyTests</c> 判）；不判 <c>.winecheck/README.md</c>
/// 这类开发期辅助文档（它们不是交付面，且大量引用脚本原文）。
/// </para>
/// </summary>
public class DocsSpacingTests
{
    /// <summary>被扫的两份对外文本（封闭名单：新增一份对外文本要在这里登记，否则它不会被判）。</summary>
    private static readonly string[] Docs = ["README.md", "docs/NOTES.md"];

    /// <summary>
    /// 当年那张登记表的形状。拆成三段拼出来：<b>判据不许与它判的东西同名</b>——判据里若原样写着那一串，
    /// 「把表加回来即红」这条门自己就是第一次触发它的那一处（第二百三十四轮实测：写成一个字面量时它当场判红自己）。
    /// </summary>
    private static string LegacyTableShape => "(" + "string File, string Snippet, int Count)" + "[]";

    /// <summary>一份文档的正文行（去掉围栏代码块与行内代码）。</summary>
    private static IEnumerable<string> ProseLines(string text)
    {
        var inFence = false;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence) continue;
            // 行内代码：成对的反引号之间是命令/路径/标识符，不是正文。
            // 换成占位符而不是删掉——删掉会把两侧的字拼到一起，凭空造出「中文紧贴拉丁」与
            // 「中文 空格 （」这种假违例（第二百三十三轮第一版实测：`与 \`demo.sh\` （…）` 被读成「与 （」）。
            yield return Regex.Replace(line, "`[^`]*`", "\u0001");
        }
    }

    /// <summary>实际违例：文件 + 片段 → 条数（片段取法见 <see cref="Registered"/> 的注释）。</summary>
    private static Dictionary<(string File, string Snippet), int> Actual()
    {
        var found = new Dictionary<(string, string), int>();
        void Bump(string file, string snippet) =>
            found[(file, snippet)] = found.GetValueOrDefault((file, snippet)) + 1;

        foreach (var file in Docs)
        {
            foreach (var line in ProseLines(Repo.Source(file.Split('/'))))
            {
                for (var i = 0; i + 1 < line.Length; i++)
                {
                    var (a, b) = (line[i], line[i + 1]);
                    if ((CopyRules.IsLatinSpacingMember(a) && CopyRules.IsChinese(b))
                        || (CopyRules.IsChinese(a) && CopyRules.IsLatinSpacingMember(b)))
                    {
                        Bump(file, string.Concat(a, b));
                    }
                }

                var at = CopyRules.IndexOfSpaceAroundFullWidthPunct(line);
                while (at >= 0)
                {
                    var end = at;
                    while (end < line.Length && line[end] == ' ') end++;
                    Bump(file, line[(at - 1)..(end + 1)]);
                    var next = CopyRules.IndexOfSpaceAroundFullWidthPunct(line[(end + 1)..]);
                    at = next < 0 ? -1 : end + 1 + next;
                }
            }
        }

        return found;
    }

    /// <summary>实测违例念成人话（顺序稳定：文件、片段各按字节序，同一份输入两次跑出同一串）。</summary>
    private static List<string> Violations(IReadOnlyDictionary<(string File, string Snippet), int> actual) =>
        actual.OrderBy(k => k.Key.File, StringComparer.Ordinal).ThenBy(k => k.Key.Snippet, StringComparer.Ordinal)
            .Select(k => $"{k.Key.File}：「{k.Key.Snippet}」{k.Value} 处")
            .ToList();

    [Fact]
    public void ExternalDocs_KeepTheSameSpacingRulesAsTheInterface()
    {
        var found = Violations(Actual());
        Assert.True(found.Count == 0,
            $"对外文本（README.md / docs/NOTES.md）的中文排版有 {found.Count} 处违例：\n  " + string.Join("\n  ", found)
            + "\n口径：中英文之间留一个半角空隙、中文与全角标点之间不留空隙（DESIGN.md §3、§6「中文排版」第 ⑤ 条）。"
            + "\n第二百三十四轮已把历史那 11 条清零、登记表删掉——这一条从此是硬判，没有「先登记着」那条路。"
            + "\n引用源码字面量、报告行标签或格式占位时写成行内代码（反引号）逐字引：那不算正文，字也一个没改。");
    }

    /// <summary>
    /// 名单是封闭的：新增一份对外文本要在这里登记，否则它不会被判。
    /// <para>
    /// <b>反向再钉一件（第二百三十四轮）</b>：这条门的绿从此只能靠「文件真的干净」，不能靠「违例被登记住了」。
    /// 谁把那张「文件 + 片段 + 条数」的表加回来——哪怕空着、哪怕一条都不判——这一条当场红。
    /// 立门那天用它兜的是<b>已经发生的历史</b>；历史清完之后还留着它，它就变成下一个人的免检通道。
    /// </para>
    /// </summary>
    [Fact]
    public void ScannedDocs_AreTheTwoExternalOnes_AndNoLegacyTableCameBack()
    {
        Assert.Equal(["README.md", "docs/NOTES.md"], Docs);
        foreach (var file in Docs)
        {
            Assert.True(File.Exists(Path.Combine(Repo.Root, file)), $"扫描名单里的「{file}」不在仓里。");
        }

        var itself = Repo.Source("ApifoxHelper.Tests", "DocsSpacingTests.cs");
        Assert.True(!itself.Contains(LegacyTableShape, StringComparison.Ordinal),
            "本文件里又出现了「文件 + 片段 + 条数」那张登记表：违例登记只在立门那天兜历史记录用，"
            + "第二百三十四轮已清零并改成硬判。要变绿就去改字，不要把表加回来。");
    }

    /// <summary>
    /// 判据自测（这一族的老毛病：判据写错时门看起来是绿的）。两种注入都必须红：
    /// ① 中英文紧贴；② 中文与全角标点之间加空格。另判三件不该红的事：留了空隙、行内代码、围栏代码块。
    /// </summary>
    [Fact]
    public void Detectors_ReportTheBreaksTheyMustCatch()
    {
        // ① 中英文紧贴。
        Assert.True(CopyRules.IndexOfMissingLatinGap("路径前缀1") >= 0);
        Assert.True(CopyRules.IndexOfMissingLatinGap("共8处") >= 0);
        Assert.Equal(-1, CopyRules.IndexOfMissingLatinGap("路径前缀 1"));

        // ② 中文与全角括号/引号之间加空格。
        Assert.True(CopyRules.IndexOfSpaceAroundFullWidthPunct("第 」") >= 0);
        Assert.True(CopyRules.IndexOfSpaceAroundFullWidthPunct("共 」") >= 0);
        Assert.True(CopyRules.IndexOfSpaceAroundFullWidthPunct("「 第") >= 0);
        Assert.True(CopyRules.IndexOfSpaceAroundFullWidthPunct("（ 说明") >= 0);
        Assert.True(CopyRules.IndexOfSpaceAroundFullWidthPunct("第  」") >= 0); // 一串空格同样算
        Assert.Equal(-1, CopyRules.IndexOfSpaceAroundFullWidthPunct("第条"));
        Assert.Equal(-1, CopyRules.IndexOfSpaceAroundFullWidthPunct("第 条")); // 句读后的空格是另一种写法，不判
        Assert.Equal(-1, CopyRules.IndexOfSpaceAroundFullWidthPunct("第 3 条")); // 空隙两侧不是全角标点
        Assert.Equal(-1, CopyRules.IndexOfSpaceAroundFullWidthPunct("报告 —— 通过")); // 破折号不在集合里
        Assert.Equal(-1, CopyRules.IndexOfSpaceAroundFullWidthPunct("「RunXxx」"));

        // 正文取法：行内代码与围栏代码块不算正文。
        var prose = ProseLines("正文 前缀1\n`tasks/<任务ID>/`\n```\n代码 前缀1\n```\n尾行").ToList();
        Assert.Contains("正文 前缀1", prose);
        Assert.DoesNotContain(prose, l => l.Contains("任务ID", StringComparison.Ordinal));
        Assert.DoesNotContain(prose, l => l.Contains("代码 前缀1", StringComparison.Ordinal));

        // 整条链：注入一处违例，违例清单必须点名它（真实文件此刻是干净的，注入的是「下一轮可能写出来的那句」）。
        var injected = new Dictionary<(string File, string Snippet), int>
        {
            [("README.md", "缀9")] = 1,
            [("docs/NOTES.md", "共 」")] = 2,
        };
        var named = Violations(injected);
        Assert.Equal(2, named.Count);
        Assert.Contains(named, v => v.Contains("缀9", StringComparison.Ordinal) && v.Contains("README.md", StringComparison.Ordinal));
        Assert.Contains(named, v => v.Contains("共 」", StringComparison.Ordinal) && v.EndsWith("2 处", StringComparison.Ordinal));

        // 空表念成「没有违例」，不是「判不动」。
        Assert.Empty(Violations(new Dictionary<(string File, string Snippet), int>()));

        // 真实文件上必须是零违例（否则上面那些「非空」只是因为它一直在报）。
        Assert.Empty(Violations(Actual()));
    }

    /// <summary>
    /// 这条门不能只活在测试里：<c>docs/NOTES.md</c> 必须点名它，并写出<b>跑出来的</b>违例数——
    /// 那个数由 <see cref="Actual"/> 现算，不是人抄的（抄的会漂，漂了还绿）。清零之后 NOTES 里那句
    /// 「对外文本排版登记 0 条」因此不是自述：下一轮谁写出一个新违例，这一条与主判据会一起红。
    /// </summary>
    [Fact]
    public void Gate_IsRegisteredInTheNotes()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("DocsSpacingTests", notes, StringComparison.Ordinal);
        var count = Violations(Actual()).Count;
        Assert.True(notes.Contains($"对外文本排版登记 {count} 条", StringComparison.Ordinal),
            $"docs/NOTES.md 里那句登记必须写「对外文本排版登记 {count} 条」（这一跑实测就是 {count} 处）："
            + "读答辩材料的人要从那一行知道判到哪一层，写着一个跑不出来的数就等于没写。");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // 验收表「一格不许写成一篇小论文」（第二百五十四轮，简报 P0-1：去 AI 味道）
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>验收表单元格的字数上限。第二百五十四轮重排之后实测最长一格 375 字（G-D4 的证据列）。</summary>
    private const int MaxAcceptanceCellChars = 400;

    /// <summary>验收口径一节的标题（与 <c>AcceptanceChecklistTests</c> 判的是同一节，取法各自独立）。</summary>
    private const string AcceptanceHeading = "## 本仓验收口径（可勾选）";

    /// <summary>被移出的详细口径小节：三级标题以这个前缀开头，四级小节以这个后缀结尾。</summary>
    private const string DetailSectionPrefix = "### 验收表逐格详细口径";

    private const string DetailSubsectionSuffix = "：详细口径";

    /// <summary>验收口径一节的行（标题行到下一个 <c>## </c> 之前，含被移出的详细口径小节）。</summary>
    private static string[] AcceptanceSectionLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == AcceptanceHeading);
        Assert.True(start >= 0, $"docs/NOTES.md 缺「{AcceptanceHeading}」一节：验收表字数门没有可判的对象。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>验收表里的一格：属于哪一条（条目名）、第几列、正文。</summary>
    private readonly record struct Cell(string Item, int Column, string Text);

    /// <summary>表里所有可勾选行的每一格（第 0 列是勾选框，不判）。</summary>
    private static List<Cell> AcceptanceCells(IEnumerable<string> sectionLines)
    {
        var cells = new List<Cell>();
        foreach (var line in sectionLines)
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var parts = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (parts.Count != 5 || (parts[0] != "[x]" && parts[0] != "[ ]")) continue;
            for (var i = 1; i < parts.Count; i++) cells.Add(new Cell(parts[1], i, parts[i]));
        }
        return cells;
    }

    /// <summary>超长格念成人话（顺序稳定：按条目名、列号）。</summary>
    private static List<string> OverlongCells(IEnumerable<Cell> cells, int max) => cells
        .Where(c => c.Text.Length > max)
        .OrderBy(c => c.Item, StringComparer.Ordinal).ThenBy(c => c.Column)
        .Select(c => $"「{c.Item}」第 {c.Column} 列 {c.Text.Length} 字（上限 {max}）")
        .ToList();

    /// <summary>
    /// 验收表每一格都不许写成一篇小论文（第二百五十四轮，简报 P0-1）。
    /// <para>
    /// <b>要解决的问题</b>：这张表是评审第一眼读到的东西，而它此前有单元格内嵌约 1500 字——判据层、读数层、
    /// 时效门、行动书全塞在一个 <c>| … |</c> 里，括号套括号、层数过深。真人写的验收表不会这样：
    /// 格子里是「结论 + 一句读数 + 指向哪里」，细节在下面的小节里。这不是排版洁癖——审查清单 A 节
    /// 「正文行长约 28–40 字」在这里被彻底违反，读的人第一眼就会把它读成生成物。
    /// </para>
    /// <para>
    /// <b>判什么</b>：表里每一格 ≤ <see cref="MaxAcceptanceCellChars"/> 字，超了即红并点名到哪一条哪一列多少字。
    /// 长内容不是删掉，是<b>移出去</b>——移到「验收表逐格详细口径」一节，由
    /// <see cref="DetailSubsections_ResolveFromTheCellsThatPointAtThem"/> 判「格子指向的小节真的在」。
    /// </para>
    /// <para>
    /// <b>判据自己被抓得住</b>：注入一格超长（上限 + 1 字）必须被点名，恰好等于上限不算超——
    /// 这一族的老毛病是判据写错时门看起来是绿的，所以夹具与边界都在同一条用例里跑。
    /// </para>
    /// </summary>
    [Fact]
    public void AcceptanceTable_EveryCellStaysShortEnoughToRead()
    {
        var overlong = OverlongCells(AcceptanceCells(AcceptanceSectionLines()), MaxAcceptanceCellChars);
        Assert.True(overlong.Count == 0,
            $"docs/NOTES.md 验收表有 {overlong.Count} 格超过 {MaxAcceptanceCellChars} 字：\n  "
            + string.Join("\n  ", overlong)
            + "\n格子里只留「结论 + 一句读数 + 指向哪里」，长读数与判据移到「验收表逐格详细口径」一节，"
            + "格子里写一句「见「X：详细口径」」。移出的是位置，不是内容——判它们的那几条门一条都不用动。");

        // 判据自验：注入一格超长必须被点名（否则这条门绿着也说明不了任何事）。
        var injected = new List<Cell>
        {
            new("G-D9 注入的条目", 3, new string('字', MaxAcceptanceCellChars + 1)),
            new("G-D9 注入的条目", 4, new string('字', MaxAcceptanceCellChars)),
        };
        var named = OverlongCells(injected, MaxAcceptanceCellChars);
        Assert.Single(named);
        Assert.Contains("G-D9 注入的条目", named[0], StringComparison.Ordinal);
        Assert.Contains("第 3 列", named[0], StringComparison.Ordinal);
        Assert.Contains($"{MaxAcceptanceCellChars + 1} 字", named[0], StringComparison.Ordinal);

        // 空表念成「没有超长格」，不是「判不动」。
        Assert.Empty(OverlongCells([], MaxAcceptanceCellChars));
    }

    /// <summary>详细口径一节里的四级小节名（去掉 <c>#### </c> 前缀）。</summary>
    private static List<string> DetailSubsections(IEnumerable<string> sectionLines) => sectionLines
        .Where(l => l.StartsWith("#### ", StringComparison.Ordinal))
        .Select(l => l[5..].Trim())
        .Where(t => t.EndsWith(DetailSubsectionSuffix, StringComparison.Ordinal))
        .ToList();

    /// <summary>格子里写的「见「X：详细口径」」指向（去重，顺序稳定）。</summary>
    private static List<(string Item, string Name)> Pointers(IEnumerable<Cell> cells) => cells
        .SelectMany(c => Regex.Matches(c.Text, "「(?<n>[^」]+" + DetailSubsectionSuffix + ")」")
            .Select(m => (Item: c.Item, Name: m.Groups["n"].Value)))
        .Distinct()
        .OrderBy(p => p.Item, StringComparer.Ordinal).ThenBy(p => p.Name, StringComparer.Ordinal)
        .ToList();

    /// <summary>指向了不存在的小节（悬空指针）。</summary>
    private static List<string> DanglingPointers(IEnumerable<Cell> cells, IReadOnlyCollection<string> headings) =>
        Pointers(cells).Where(p => !headings.Contains(p.Name))
            .Select(p => $"「{p.Item}」指向的「{p.Name}」在详细口径一节里没有对应小节")
            .ToList();

    /// <summary>没有任何一格指向的小节（孤儿小节：内容移出去了，但读者找不到入口）。</summary>
    private static List<string> OrphanSubsections(IEnumerable<Cell> cells, IReadOnlyCollection<string> headings)
    {
        var all = cells.ToList();
        return headings
            .Where(h => !all.Any(c => c.Text.Contains("「" + h + "」", StringComparison.Ordinal)))
            .Select(h => $"「{h}」没有任何一格指向它")
            .ToList();
    }

    /// <summary>
    /// 格子里那句「见「X：详细口径」」必须真的有小节，且没有没人指的小节（第二百五十四轮，简报 P0-1）。
    /// <para>
    /// <b>为什么这条门必须与上一条同时存在</b>：上一条只判「格子短了」。把长内容删掉、或把指针写成
    /// 一个不存在的小节名，格子一样短、上一条一样绿——<b>读者按指针翻下去会翻空</b>。
    /// 两条合起来才是「移出位置、不丢内容」：短了（上一条）+ 指得到（这一条）。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 每一处指向都有同名四级小节（悬空即红，点名是哪一条指向了哪个名字）；
    /// ② 每一个四级小节都至少被一格指向（孤儿即红——内容移出去了却没人引，等于埋了）。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：小节里的正文对不对、够不够长。那是各条判据自己的事（`AcceptanceChecklistTests`
    /// 判表格那一列的引用解析得开），这里只判「入口通不通」。
    /// </para>
    /// </summary>
    [Fact]
    public void DetailSubsections_ResolveFromTheCellsThatPointAtThem()
    {
        var lines = AcceptanceSectionLines();
        var cells = AcceptanceCells(lines);
        var headings = DetailSubsections(lines);

        Assert.True(headings.Count > 0,
            "docs/NOTES.md 的验收口径一节里没有「验收表逐格详细口径」小节：格子里的「见「X：详细口径」」会全部悬空。");

        var dangling = DanglingPointers(cells, headings);
        Assert.True(dangling.Count == 0,
            "验收表里有指向不存在小节的格子：\n  " + string.Join("\n  ", dangling)
            + "\n要么把小节名写对，要么把那一格的内容真的移出去——指针指空比不写指针更糟。");

        var orphans = OrphanSubsections(cells, headings);
        Assert.True(orphans.Count == 0,
            "详细口径一节里有没人指向的小节：\n  " + string.Join("\n  ", orphans)
            + "\n内容移出去了却没有任何一格引它，读者从表格翻不到——要么在对应格子里写上指针，要么把这一节删掉。");

        // 判据自验：悬空指针与孤儿小节都必须被点名（这一族的老毛病：判据写错时门看起来是绿的）。
        var fixtureCells = new List<Cell>
        {
            new("G-D9 注入的条目", 3, "见「G-D9 注入：详细口径」。"),
            new("G-D9 注入的条目", 4, "见「G-D3 状态完备：详细口径」。"),
        };
        var fixtureHeadings = new List<string> { "G-D3 状态完备：详细口径", "G-D9 没人指的小节：详细口径" };
        var fixtureDangling = DanglingPointers(fixtureCells, fixtureHeadings);
        Assert.Single(fixtureDangling);
        Assert.Contains("G-D9 注入：详细口径", fixtureDangling[0], StringComparison.Ordinal);
        var fixtureOrphans = OrphanSubsections(fixtureCells, fixtureHeadings);
        Assert.Single(fixtureOrphans);
        Assert.Contains("G-D9 没人指的小节：详细口径", fixtureOrphans[0], StringComparison.Ordinal);
    }
}
