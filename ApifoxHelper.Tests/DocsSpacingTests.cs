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
/// <b>为什么是「登记 + 棘轮」而不是「一次改干净」</b>：这两份文件在 HEAD 上本来就有一批违例
/// （历史记录里的「路径前缀1」「订单2」这类引用、以及「第 」这种漏字写法）。本轮的范围是<b>加门</b>，
/// 不是改文案——一次改几十处历史记录，等于把当时的证据改坏。所以门按<b>棘轮</b>判：
/// 下面那张登记表就是 HEAD 上的全部违例（文件 + 片段 + 条数），
/// <b>新增一处 → 红</b>（新写的字不合规），<b>修好一处却没从登记里删掉 → 也红</b>
/// （登记与实物漂移，正是本仓反复收掉的那一族）。修的时候顺手把那一行删掉即可。
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
    /// HEAD 上的全部违例登记（第二百三十三轮实测，逐条来自本门自己的报错输出，不是手抄的估计）。
    /// <para>
    /// 键是「文件 + 片段」，值是条数：片段取<b>紧贴的那两个字</b>（中英文空隙）或<b>「字 + 空隙 + 标点」三个字</b>
    /// （全角标点空隙），不记行号——这两份文件每轮都在长，行号会漂，片段不会。
    /// </para>
    /// <para>
    /// 修掉一处就把那一行删掉（或把条数减一）；<b>不要</b>为了让门变绿而把新违例加进来——
    /// 这张表是「还没修的历史记录」，不是免检通道。
    /// </para>
    /// </summary>
    private static readonly (string File, string Snippet, int Count)[] Registered =
    [
        // 中英文之间缺空隙（中文紧贴半角字母或数字）。全部在 docs/NOTES.md 的历史记录里，
        // README.md 这一侧是零——它本来就干净，所以这一份是硬判。
        ("docs/NOTES.md", "缀1", 5),   // 「路径前缀1」这类引用
        ("docs/NOTES.md", "缀2", 5),   // 「路径前缀2」同上
        ("docs/NOTES.md", "第X", 3),   // 「第X轮」占位写法
        ("docs/NOTES.md", "X轮", 3),   // 同上，同一个词的另一半
        ("docs/NOTES.md", "单2", 2),   // 「订单2」
        ("docs/NOTES.md", "单1", 2),   // 「订单10」
        ("docs/NOTES.md", "第1", 1),   // 「第16行」
        ("docs/NOTES.md", "6行", 1),   // 同上，同一个词的另一半
        ("docs/NOTES.md", "告1", 1),   // 「报告1/2/10/20」

        // 中文与全角括号/引号之间多了空隙（句读后的空格不算，见 CopyRules.FullWidthPunct 的注释）。
        ("docs/NOTES.md", "第 」", 1),
        ("docs/NOTES.md", "共 」", 1),
    ];

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

    /// <summary>登记表与实测的差集（两个方向都算差）。</summary>
    private static List<string> Drift(
        IReadOnlyDictionary<(string File, string Snippet), int> actual,
        IReadOnlyList<(string File, string Snippet, int Count)> registered)
    {
        var drift = new List<string>();
        var expected = registered.ToDictionary(r => (r.File, r.Snippet), r => r.Count);

        foreach (var ((file, snippet), count) in actual.OrderBy(k => k.Key.File, StringComparer.Ordinal)
                     .ThenBy(k => k.Key.Snippet, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue((file, snippet), out var want))
            {
                drift.Add($"{file}：新增违例「{snippet}」×{count}（登记表里没有这一条）。");
            }
            else if (count > want)
            {
                drift.Add($"{file}：「{snippet}」从 {want} 处涨到 {count} 处。");
            }
            else if (count < want)
            {
                drift.Add($"{file}：「{snippet}」从 {want} 处减到 {count} 处——修好了就把登记表那一行删掉（或改小条数）。");
            }
        }

        foreach (var (file, snippet, count) in registered)
        {
            if (!actual.ContainsKey((file, snippet)))
            {
                drift.Add($"{file}：登记表里的「{snippet}」×{count} 已经不存在了——修好了就把这一行删掉。");
            }
        }

        return drift;
    }

    [Fact]
    public void ExternalDocs_KeepTheSameSpacingRulesAsTheInterface()
    {
        var drift = Drift(Actual(), Registered);
        Assert.True(drift.Count == 0,
            "对外文本（README.md / docs/NOTES.md）的中文排版与登记表对不上：\n  " + string.Join("\n  ", drift)
            + "\n口径：中英文之间留一个半角空隙、中文与全角标点之间不留空隙（DESIGN.md §3、§6「中文排版」第 ⑤ 条）。"
            + "\n新写的字不合规就改字；修好了历史违例就把 DocsSpacingTests.Registered 里那一行删掉。");
    }

    /// <summary>
    /// 登记表不许靠「留着让它绿」活着：每一条都必须在文件里真的还在（<see cref="Drift"/> 的反向已判），
    /// 这里再钉住「登记表不是空的」——空表意味着这两份文件已经干净，那时这条门该改成硬判（零违例）。
    /// </summary>
    [Fact]
    public void RegisteredBaseline_IsNotEmptyAndStillMatchesTheFiles()
    {
        Assert.NotEmpty(Registered);
        Assert.Equal(Registered.Length, Registered.Distinct().Count());
        foreach (var (file, snippet, count) in Registered)
        {
            Assert.True(Docs.Contains(file, StringComparer.Ordinal), $"登记表里的文件「{file}」不在扫描名单里。");
            Assert.True(count > 0, $"登记表里「{file} / {snippet}」的条数是 {count}——0 条不该留在表里。");
            Assert.Contains(snippet, Repo.Source(file.Split('/')), StringComparison.Ordinal);
        }
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

        // 整条链：注入一处新违例，差集必须点名它。
        var actual = Actual();
        var injected = new Dictionary<(string File, string Snippet), int>(actual)
        {
            [("README.md", "缀9")] = 1,
        };
        var drift = Drift(injected, Registered);
        Assert.Contains(drift, d => d.Contains("缀9", StringComparison.Ordinal));

        // 反向：修好一处却没删登记，同样要红。
        var fixedOne = new Dictionary<(string File, string Snippet), int>(actual);
        fixedOne.Remove(("docs/NOTES.md", "缀1"));
        Assert.Contains(Drift(fixedOne, Registered), d => d.Contains("缀1", StringComparison.Ordinal));

        // 真实文件上必须是绿的（否则上面那些「非空」只是因为它一直在报）。
        Assert.Empty(Drift(actual, Registered));
    }

    /// <summary>
    /// 这张登记表不能只活在测试里：<c>docs/NOTES.md</c> 的本轮记录必须点名这条门与它的条数，
    /// 否则下一个人读到「对外文本已判」时，没有任何东西告诉他判到哪一层、还剩多少没修。
    /// </summary>
    [Fact]
    public void Baseline_IsRegisteredInTheNotes()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("DocsSpacingTests", notes, StringComparison.Ordinal);
        Assert.Contains($"对外文本排版登记 {Registered.Length} 条", notes, StringComparison.Ordinal);
    }
}
