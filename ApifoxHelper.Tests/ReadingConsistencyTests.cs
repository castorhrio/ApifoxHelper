using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「当前源码三档读数」在两份文件里必须是同一个数（第一百九十轮，简报 P0-3）。
/// <para>
/// <b>要解决的问题</b>：<c>README.md</c> 的「已知限制」写「当前源码三档读数 200 通过 / 0 失败 ×3」，
/// <c>docs/NOTES.md</c> 的 G-D3 行写「第一百八十五轮……206 通过 / 0 失败 ×3」——两处引用的是同一件事，
/// 数字却差 6。答辩现场被追问「两个数字哪个对」会直接失分，而这类矛盾不会让任何代码出错，
/// 所以只能靠机械门挡住。
/// </para>
/// <para>
/// <b>判据的边界</b>：只判<b>声称是当前源码读数</b>的那几处（「当前源码」之后同一句里的
/// <c>N 通过 / 0 失败 ×3</c>）。历史轮次的读数（第一百六十七轮 135、第一百七十七轮 180 等）
/// 是当时的证据，本来就该各不相同——把它们也拉平，等于把历史记录改坏。
/// </para>
/// <para>
/// <b>两个方向都能红</b>：两份文件里任一份不再声称当前源码读数 → 红（读数没有落点）；
/// 两份都声称但数字不同 → 红（就是本轮修掉的那个矛盾）。
/// </para>
/// </summary>
public class ReadingConsistencyTests
{
    /// <summary>「当前源码」之后同一句里出现的三档读数。</summary>
    private static readonly Regex CurrentReading = new(
        @"当前源码[^\n。]{0,60}?(?<n>\d+) 通过 / 0 失败 ×3",
        RegexOptions.Compiled);

    private static List<(string File, int Value)> Readings(string relative)
    {
        var text = Repo.Source(relative.Replace('/', Path.DirectorySeparatorChar));
        return CurrentReading.Matches(text)
            .Select(m => (relative, int.Parse(m.Groups["n"].Value)))
            .ToList();
    }

    [Fact]
    public void CurrentSourceReading_IsTheSameNumberInReadmeAndNotes()
    {
        var readme = Readings("README.md");
        var notes = Readings("docs/NOTES.md");

        Assert.True(readme.Count > 0,
            "README.md 里没有一处「当前源码……N 通过 / 0 失败 ×3」：使用者读的那份文件不再给出当前读数。");
        Assert.True(notes.Count > 0,
            "docs/NOTES.md 里没有一处「当前源码……N 通过 / 0 失败 ×3」：验收表的读数没有落点。");

        var all = readme.Concat(notes).ToList();
        var distinct = all.Select(r => r.Value).Distinct().ToList();
        Assert.True(distinct.Count == 1,
            "「当前源码三档读数」在两份文件里不是同一个数："
            + string.Join("；", all.Select(r => $"{r.File} = {r.Value}"))
            + "。同一件事只能有一个数——改数字时两份一起改，并在括号里写清它是怎么加出来的。");
    }

    /// <summary>三档读数的每一处出现（值 + 所在句子）。</summary>
    private static readonly Regex ThreeTier = new(@"\d+ 通过 / 0 失败 ×3", RegexOptions.Compiled);

    /// <summary>句子边界：句号、换行、表格单元格分隔符。</summary>
    private static readonly char[] SentenceBreaks = ['。', '\n', '|'];

    /// <summary>一处读数所在的句子（前后各切到最近的句子边界）。</summary>
    private static string SentenceAround(string text, Match match)
    {
        var start = text.LastIndexOfAny(SentenceBreaks, Math.Max(0, match.Index - 1));
        var end = text.IndexOfAny(SentenceBreaks, match.Index + match.Length);
        return text[(start + 1)..(end < 0 ? text.Length : end)];
    }

    /// <summary>
    /// 每一处三档读数都必须能追到「哪一轮」或「当前源码」（第一百九十三轮，简报 P1-1）。
    /// <para>
    /// <b>要解决的问题</b>：第一百九十轮修过一次 200/206 矛盾，说明这类漂移会真发生。但那条门只判
    /// 「<c>当前源码</c>」后面那个数——<b>不带这四个字的读数它一概不看</b>：一句「三档复跑 206 通过 / 0 失败 ×3」
    /// 摆在正文里，读的人分不清它是当前读数还是某一轮的历史读数，而它一旦被当成当前读数，就是第二个数。
    /// </para>
    /// <para>
    /// <b>判据</b>：同一句里必须出现「当前源码」或「第…轮」。历史读数本来就该各不相同（135 / 180 / 200 / 206 / 208），
    /// 所以这里<b>不拉平数字</b>，只要求每一处都说清自己是谁的读数。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryThreeTierReading_IsAttributedToARoundOrTheCurrentSource()
    {
        var unattributed = new List<string>();
        foreach (var file in new[] { "README.md", "docs/NOTES.md" })
        {
            var text = Repo.Source(file.Replace('/', Path.DirectorySeparatorChar));
            foreach (Match m in ThreeTier.Matches(text))
            {
                var sentence = SentenceAround(text, m);
                if (sentence.Contains("当前源码", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(sentence, @"第[一二三四五六七八九十百零]+轮")) continue;
                unattributed.Add($"{file}：{sentence.Trim()}");
            }
        }

        Assert.True(unattributed.Count == 0,
            "这些三档读数没说清是哪一轮的（同一句里既没有「当前源码」也没有「第…轮」）："
            + string.Join(" ／ ", unattributed)
            + "。历史读数本来就各不相同，但每一处都得说清自己是谁的读数——否则它会被读成第二个当前读数。");
    }

    /// <summary>上下文注解的三个关键词（第二百二十四轮，简报 P1-2）。</summary>
    private static readonly string[] ContextWords = ["慢一档", "进程 DPI", "上下文"];

    /// <summary>一处读数之后的下一句（切到下一个句子边界为止）。</summary>
    private static string NextSentence(string text, Match match)
    {
        var end = text.IndexOfAny(SentenceBreaks, match.Index + match.Length);
        if (end < 0) return string.Empty;
        var next = text.IndexOfAny(SentenceBreaks, end + 1);
        return text[(end + 1)..(next < 0 ? text.Length : next)];
    }

    /// <summary><c>docs/NOTES.md</c> 顶部验收表里 G-D5 那一行（勾与没勾都命中）。</summary>
    private static string Gd5Row()
    {
        var line = Repo.Source("docs", "NOTES.md").Split('\n')
            .FirstOrDefault(l => Regex.IsMatch(l, @"^\| \[.\] \| G-D5"));
        Assert.True(line is not null, "docs/NOTES.md 顶部验收表里找不到 G-D5 那一行（`| [ ] | G-D5 …`）。");
        return line!;
    }

    /// <summary>
    /// 三档读数必须与「上下文注解」同屏（第二百二十四轮，简报 P1-2）。
    /// <para>
    /// <b>要解决的问题</b>：<c>.winecheck/reading.txt</c> 记的三档读数出自「<b>慢一档</b>」那个上下文
    /// （<c>demo.sh</c> 那一跑里控件自报 DPI 恒为 96，字体放大而控件行高不放大），<b>不能读成</b>
    /// 「行高预算在 125% / 200% 也过」。这句话在 <c>.winecheck/README.md</c> 里写清了，但评审读的是
    /// <c>README.md</c> 与验收表——只看那一句数值，很容易把它当成「三档都过」。
    /// </para>
    /// <para>
    /// <b>判据</b>：<c>README.md</c> 全文与 <c>docs/NOTES.md</c> 的 G-D5 那一行里，每一处数字型三档读数
    /// （<c>N 通过 / 0 失败 ×3</c>）的<b>同一句或紧邻下一句</b>必须出现「慢一档」「进程 DPI」「上下文」之一。
    /// 只判这两处：它们是「被当成当前读数念出来」的地方；<c>docs/NOTES.md</c> 里其余几十处是逐轮日志，
    /// 各自带着自己的上下文，逐处强加这三个词只是文档噪音，不增加信息。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判读数对不对（那是 <see cref="CurrentSourceReading_IsTheSameNumberInReadmeAndNotes"/>
    /// 与 <c>reading.txt</c> 的账），也不判真机跑没跑。
    /// </para>
    /// </summary>
    [Fact]
    public void ThreeTierReading_KeepsItsContextAnnotationOnTheSameScreen()
    {
        var scopes = new (string Label, string Text)[]
        {
            ("README.md", Repo.Source("README.md")),
            ("docs/NOTES.md 的 G-D5 行", Gd5Row()),
        };

        var missing = new List<string>();
        var seen = 0;
        foreach (var (label, text) in scopes)
        {
            foreach (Match m in ThreeTier.Matches(text))
            {
                seen++;
                var sentence = SentenceAround(text, m);
                var next = NextSentence(text, m);
                if (ContextWords.Any(w => sentence.Contains(w, StringComparison.Ordinal)
                                          || next.Contains(w, StringComparison.Ordinal)))
                {
                    continue;
                }

                missing.Add($"{label}：{sentence.Trim()}");
            }
        }

        // 地板：两处都得真有读数——把读数删光，这条门就成了摆设。
        Assert.True(seen >= 2,
            $"只找到 {seen} 处三档读数（README.md 与 G-D5 行各至少一处）：读数被删光了，这条门判不到东西。");

        Assert.True(missing.Count == 0,
            "这些三档读数旁边没有上下文注解（同一句或紧邻下一句里既没有「慢一档」也没有「进程 DPI」「上下文」）："
            + string.Join(" ／ ", missing)
            + "。reading.txt 记的数出自「慢一档」那个上下文，不能读成「行高预算在 125% / 200% 也过」——"
            + "注解与数值必须同屏，否则评审只看数值就会误读。");
    }

    /// <summary>
    /// 读数后面的加法算式必须加得起来（第一百九十三轮，简报 P1-1）。
    /// <para>
    /// <b>要解决的问题</b>：三档读数是「一段一段加上去的」，所以正文里到处写着
    /// 「208 = 200 + 6 + 1 + 1」这种来历。这类算式<b>不会让任何代码出错</b>，但加不起来时读的人会以为
    /// 少了一段或多了一段——第一百九十三轮校对时就抓到一处：逐屏表第 17 行写「208 = 200 + 6 + 1」，
    /// 少的那一段正是同一轮新增的「等宽行 Enter 契约」。
    /// </para>
    /// <para>
    /// <b>判据</b>：每一处三档读数后面若跟着算式（<c>（= …</c> 或 <c> = …</c>），或同一行里写着
    /// <c>N = …</c>，则算式里的加数之和必须等于那个数。括号里的解释先剥掉（「（那一轮新增的 15 段…）」
    /// 里的 15 是解释不是加数），算式切到第一个逗号 / 分号 / 句号 / 单元格边界为止。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingBreakdown_AddsUpToTheNumberItExplains()
    {
        var broken = new List<string>();
        foreach (var file in new[] { "README.md", "docs/NOTES.md" })
        {
            var text = Repo.Source(file.Replace('/', Path.DirectorySeparatorChar));
            foreach (Match m in ThreeTier.Matches(text))
            {
                var value = int.Parse(Regex.Match(m.Value, @"\d+").Value);

                // ① 读数后面直接跟算式：「208 通过 / 0 失败 ×3（= 200 + …」或「… ×3 = 135 + 45」。
                var rest = text[(m.Index + m.Length)..];
                rest = rest.TrimStart('*');
                if (rest.StartsWith("（=", StringComparison.Ordinal) || rest.StartsWith(" = ", StringComparison.Ordinal))
                {
                    var expr = rest[(rest.IndexOf('=') + 1)..];
                    var sum = Addends(expr);
                    if (sum is not null && sum != value)
                    {
                        broken.Add($"{file}：「{m.Value}」后面的算式加起来是 {sum}（{expr.Trim()}）");
                    }
                }

                // ② 同一行里写着「N = a + b + …」。
                var lineStart = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                var lineEnd = text.IndexOf('\n', m.Index);
                var line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];
                foreach (Match eq in Regex.Matches(line, $@"(?<![\d.]){value} = "))
                {
                    var expr = line[(eq.Index + eq.Length)..];
                    var sum = Addends(expr);
                    if (sum is not null && sum != value)
                    {
                        broken.Add($"{file}：「{value} = {expr.Trim()}」加起来是 {sum}");
                    }
                }
            }
        }

        Assert.True(broken.Count == 0,
            "这些读数算式加不起来：" + string.Join(" ／ ", broken)
            + "。段数是「一段一段加上去的」，算式加不起来时读的人会以为少了一段或多了一段——先对齐，再念。");
    }

    /// <summary>
    /// 一段算式里的加数之和；不是算式（没有 <c> + </c>）时返回 null。
    /// <para>
    /// 括号里的解释先剥掉：<c>200 = 180 + 15（那一轮新增的 15 段「点开读全」）+ 5（第一百八十三轮新增）</c>
    /// 里的两个 15 是解释，不是加数——不剥的话这条门会把正确的算式判红。
    /// </para>
    /// </summary>
    private static int? Addends(string expression)
    {
        var flat = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var ch in expression)
        {
            if (ch is '（' or '(') { depth++; continue; }
            if (ch is '）' or ')') { if (depth > 0) depth--; continue; }
            if (depth == 0) flat.Append(ch);
        }

        var text = flat.ToString();
        var cut = text.IndexOfAny(['，', '；', ';', '。', '|', '\n']);
        if (cut >= 0) text = text[..cut];
        if (!text.Contains(" + ", StringComparison.Ordinal)) return null;

        var numbers = Regex.Matches(text, @"\d+").Select(x => int.Parse(x.Value)).ToList();
        return numbers.Count == 0 ? null : numbers.Sum();
    }
    // ---------- 逐屏对账表的段数（第一百九十四轮，简报 P1-2）----------

    /// <summary>逐屏对账表所在的一节（<c>docs/NOTES.md</c>「降级答辩脚本」）。</summary>
    private const string ScreenTableHeading = "## 降级答辩脚本";

    /// <summary>逐屏表的行：序号 + 证据出处一列（列数与 <c>DemoScriptTests.Rows()</c> 同一判据）。</summary>
    private static List<(string Num, string Evidence)> ScreenRows()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(ScreenTableHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/NOTES.md 缺少「{ScreenTableHeading}」一节。");
        var rest = notes[(start + ScreenTableHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        var section = end < 0 ? rest : rest[..end];

        var rows = new List<(string, string)>();
        foreach (var line in section.Split('\n'))
        {
            if (!Regex.IsMatch(line, @"^\| \d")) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            Assert.True(cells.Count == 5,
                $"逐屏表有一行不是 5 列（{cells.Count} 列）：{line[..Math.Min(40, line.Length)]}…");
            rows.Add((cells[0], cells[3]));
        }

        Assert.True(rows.Count >= 12, $"逐屏表只有 {rows.Count} 行（地板 12）。");
        return rows;
    }

    /// <summary>
    /// 冒烟器里每个 <c>RunXxx</c> 段方法体内有几段（= 几处 <c>RunAction(</c> 调用）。
    /// <para>
    /// 方法体按「下一个顶格 <c>private static</c> 声明」切边界，不做花括号配对——
    /// 段体里全是 lambda 与插值字符串，配对一旦被字符串里的花括号骗到，数出来的段数就是错的。
    /// </para>
    /// </summary>
    private static Dictionary<string, int> SegmentCounts()
    {
        var program = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var starts = Regex.Matches(program, @"(?m)^    private static void (Run[A-Za-z0-9]+)\(");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < starts.Count; i++)
        {
            var name = starts[i].Groups[1].Value;
            var from = starts[i].Index;
            var to = i + 1 < starts.Count ? starts[i + 1].Index : program.Length;
            counts[name] = Regex.Matches(program[from..to], @"\bRunAction\(").Count;
        }

        Assert.True(counts.Count >= 12, $"冒烟器里只解析出 {counts.Count} 个段方法（地板 12）。");
        return counts;
    }

    /// <summary>括号里的解释先剥掉：<c>（失败 → 重试 → 恢复）</c> 里的箭头与数字不是段数。</summary>
    private static string StripParentheticals(string text)
    {
        var flat = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var ch in text)
        {
            if (ch is '（' or '(') { depth++; continue; }
            if (ch is '）' or ')') { if (depth > 0) depth--; continue; }
            if (depth == 0) flat.Append(ch);
        }
        return flat.ToString();
    }

    /// <summary>中文数字（一…二十）。逐屏表里的段数最大是八，留到二十是给以后加段留余量。</summary>
    private static int? ChineseNumber(string text) => text switch
    {
        "一" => 1, "二" => 2, "三" => 3, "四" => 4, "五" => 5,
        "六" => 6, "七" => 7, "八" => 8, "九" => 9, "十" => 10,
        _ => text.StartsWith('十') && text.Length == 2 && "一二三四五六七八九".Contains(text[1])
            ? 10 + "一二三四五六七八九".IndexOf(text[1]) + 1
            : null,
    };

    /// <summary>
    /// 逐屏对账表里写的段数必须与冒烟器里那一段真有几段对得上（第一百九十四轮，简报 P1-2）。
    /// <para>
    /// <b>要解决的问题</b>：<see cref="ReadingBreakdown_AddsUpToTheNumberItExplains"/> 判的是「三档读数后面的
    /// 加法算式加得起来」，第一百九十三轮正是靠它抓到逐屏表第 17 行少了一段。但那条门只看
    /// <c>N 通过 / 0 失败 ×3</c> 这种读数——<b>逐屏表每一行自己写的段数它一概不看</b>：
    /// 「<c>RunNoticeCopyClick</c> 六段」这种话，写的时候是真的，后来那一段补了两段就没人回头改，
    /// 而答辩现场念的正是这个数。念错一个数，前面十几轮攒的「读数可核对」就白攒了。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 逐屏表每一行的证据出处里，凡「一个段名 + 一个中文段数」同处一句的，
    /// 那个段数必须等于冒烟器里该段方法体内的 <c>RunAction(</c> 调用数；② 「A 屏 × B 段 = C 段」
    /// 这类乘积必须乘得起来。括号里的解释先剥掉（「（失败 → 重试 → 恢复）」不是段数），
    /// 「第 8 段」这种序号不算段数（那是「第几段」，不是「共几段」）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判这些段在真机上跑过</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），
    /// 也不判「这一屏该有几段」——那是设计判断，不是机械可判的事。它只保证「表里写的数与冒烟器里的段数
    /// 是同一个数」：改一段而不改表，即红。
    /// </para>
    /// </summary>
    [Fact]
    public void ScreenTable_EveryStatedSegmentCountMatchesTheSmokeProgram()
    {
        var counts = SegmentCounts();
        var wrong = new List<string>();

        foreach (var (num, evidence) in ScreenRows())
        {
            var flat = StripParentheticals(evidence);

            foreach (var clause in flat.Split(['、', '，', '；', '。', '+', '\n']))
            {
                var segments = Regex.Matches(clause, @"Run[A-Za-z0-9]+")
                    .Select(m => m.Value).Distinct().ToList();
                var stated = Regex.Matches(clause, @"(?<![第这那每同逐])([一二三四五六七八九十]+)段")
                    .Select(m => ChineseNumber(m.Groups[1].Value)).Where(n => n is not null).Select(n => n!.Value).ToList();
                // 一句里只有一个段名、一个段数时才判：两个段名共用一句时，那个数属于谁读不出来。
                if (segments.Count != 1 || stated.Count != 1) continue;

                var actual = counts.TryGetValue(segments[0], out var n) ? n : -1;
                if (actual != stated[0])
                {
                    wrong.Add($"第 {num} 行：`{segments[0]}` 写的是 {stated[0]} 段，冒烟器里是 {actual} 段");
                }
            }

            foreach (Match m in Regex.Matches(flat, @"(\d+) 屏 × (\d+) 段 = (\d+) 段"))
            {
                var (screens, perScreen, total) = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
                if (screens * perScreen != total)
                {
                    wrong.Add($"第 {num} 行：{screens} 屏 × {perScreen} 段 = {total} 段，乘不起来");
                }
            }
        }

        Assert.True(wrong.Count == 0,
            "逐屏对账表里这些段数与冒烟器对不上：" + string.Join(" ／ ", wrong)
            + "。答辩现场念的就是这个数——改一段就回来改表，别让表比代码旧。");
    }

    // ---------- 第二百零七轮（简报 P0-1）：念出来的数要有「跑出来的出处」 ----------

    /// <summary>
    /// 读数记录 <c>.winecheck/reading.txt</c>——由 <c>demo.sh</c> / <c>demo.ps1</c> 跑完三档写出，
    /// 不是人抄的。下面四条门把「文档念的数」接到这份记录上，不再接到人写的加法上。
    /// </summary>
    private const string ReadingPath = ".winecheck/reading.txt";

    /// <summary>一次跑出来的一档：档位 + 通过 / 失败 + 冒烟器退出码。</summary>
    private sealed record TierRun(int Tier, int Passed, int Failed, int Exit);

    /// <summary>一次跑出来的一段：段名 + 档位 + 通过 / 失败。</summary>
    private sealed record SectionRun(string Section, int Tier, int Passed, int Failed);

    /// <summary>一份读数记录（来历四行 + 每档总计 + 每段计数）。</summary>
    private sealed record Reading(string Script, string Tool, string Date, string Source,
        List<TierRun> Tiers, List<SectionRun> Sections);

    /// <summary>
    /// 读那份记录。缺文件即红并给出「怎么补出来」的一条命令——这份记录是跑出来的，
    /// 不在磁盘上就说明有人删了它，或者上一轮改了冒烟器却没重跑（两种都不能拿现在的文档数去答辩）。
    /// </summary>
    private static Reading ReadingRecord()
    {
        var path = Path.Combine(Repo.Root, ReadingPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            $"{ReadingPath} 不在——README 与验收表里那个「当前源码三档读数」没有跑出来的出处了。"
            + "补一次：bash .winecheck/demo.sh（三档跑完它自己写回原位）。");

        var script = ""; var tool = ""; var date = ""; var source = "";
        var tiers = new List<TierRun>();
        var sections = new List<SectionRun>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var at = line.IndexOf('=');
            if (at <= 0) continue;
            var key = line[..at];
            var value = line[(at + 1)..];
            switch (key)
            {
                case "script": script = value; break;
                case "tool": tool = value; break;
                case "date": date = value; break;
                case "source": source = value; break;
                case "tier":
                    var t = Regex.Match(line, @"^tier=(\d+) passed=(\d+) failed=(\d+) exit=(-?\d+)$");
                    Assert.True(t.Success, $"{ReadingPath} 有一行 tier= 不是「tier=N passed=N failed=N exit=N」：{line}");
                    tiers.Add(new TierRun(int.Parse(t.Groups[1].Value), int.Parse(t.Groups[2].Value),
                        int.Parse(t.Groups[3].Value), int.Parse(t.Groups[4].Value)));
                    break;
                case "section":
                    var s = Regex.Match(line, @"^section=(Run[A-Za-z0-9_]+) tier=(\d+) passed=(\d+) failed=(\d+)$");
                    Assert.True(s.Success, $"{ReadingPath} 有一行 section= 不是「section=RunXxx tier=N passed=N failed=N」：{line}");
                    sections.Add(new SectionRun(s.Groups[1].Value, int.Parse(s.Groups[2].Value),
                        int.Parse(s.Groups[3].Value), int.Parse(s.Groups[4].Value)));
                    break;
                default:
                    Assert.Fail($"{ReadingPath} 出现没有约定的键 {key}（这一份只能由两支演示脚本写，手改就是把出处变成自述）：{line}");
                    break;
            }
        }

        return new Reading(script, tool, date, source, tiers, sections);
    }

    /// <summary>
    /// 文档里「当前源码…N 通过 / M 失败 ×3」这一处<b>声称</b>（带行号，红了要点到人）。
    /// 只判带「当前源码」四个字的那几处——历史读数（135 / 180 / 200 / 206 / 208 / 218 的旧值）
    /// 是当时的证据，本来就该各不相同，把它们也拉平等于把记录改坏（与本文件第一条门同一边界）。
    /// </summary>
    private static readonly Regex CurrentReadingWithFailures = new(
        @"当前源码[^\n。]{0,60}?(?<n>\d+) 通过 / (?<f>\d+) 失败 ×3",
        RegexOptions.Compiled);

    private static List<(string File, int Line, int Passed, int Failed)> CurrentReadingClaims()
    {
        var claims = new List<(string, int, int, int)>();
        foreach (var file in new[] { "README.md", "docs/NOTES.md" })
        {
            var lines = Repo.Source(file.Replace('/', Path.DirectorySeparatorChar)).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match m in CurrentReadingWithFailures.Matches(lines[i]))
                {
                    claims.Add((file, i + 1, int.Parse(m.Groups["n"].Value), int.Parse(m.Groups["f"].Value)));
                }
            }
        }

        return claims;
    }

    /// <summary>
    /// 读数记录必须自己说清是哪一支脚本、哪一台机器、哪一天、哪一版源码跑出来的，
    /// 并且自己写明<b>这不是真机结果</b>——这份记录将来会被拿去答辩，来历不明的读数最危险。
    /// <para>
    /// <b>它不判什么</b>：<b>不判这份记录比当前源码新</b>。那是 <c>demo.sh</c> 在跑的时候按源码内容指纹
    /// 拦的事（<c>smoke-source.sha</c> 对不上它就拒绝念）。把同一条判据搬进 <c>dotnet test</c>，
    /// 等于「改一行产品代码、没先跑一次 wine，整个测试工程就红」——一道当场解不开的闸门比没有闸门更糟，
    /// 而这一条在演示脚本里已经解得开（重跑一次就行）。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingRecord_NamesItsScriptToolDateSourceAndKeepsTheSimulatorCaliber()
    {
        var record = ReadingRecord();
        Assert.True(record.Script == "demo.sh" || record.Script == "demo.ps1",
            $"{ReadingPath} 的 script= 是「{record.Script}」——这一份只能由 .winecheck/demo.sh 或 demo.ps1 写出。");
        Assert.Matches(@"^[0-9a-f]{40}$", record.Source);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", record.Date);
        Assert.False(string.IsNullOrWhiteSpace(record.Tool), $"{ReadingPath} 没有 tool=：念数的人不知道这是在哪台机器上跑的。");
        Assert.NotEmpty(record.Tiers);
        Assert.Contains("不是 Windows 真机", Repo.Source(ReadingPath), StringComparison.Ordinal);
    }

    /// <summary>
    /// 记录里跑出来的段集合 == 冒烟器 <c>Main</c> 里注册的段集合（<see cref="Repo.SmokeSectionNames"/>，
    /// 与 <c>GateSourceConsistencyTests</c> 同一条取法，本仓不养第二份段名单）。
    /// <para>
    /// 这是「把冒烟器删掉一段即红」那一处变异验证的落点：<b>不用重跑</b>。注册表少一段而记录里还有它，
    /// 说明有人拿着旧读数念新代码；反过来记录里少一段，说明这一段没跑成。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingRecord_RunsExactlyTheSectionsTheSmokeProgramRegisters()
    {
        var record = ReadingRecord();
        var registered = Repo.SmokeSectionNames();
        Assert.True(registered.Count >= 12, $"冒烟器只解析出 {registered.Count} 个注册段（地板 12）——取法退化了。");

        var problems = new List<string>();
        foreach (var tier in record.Tiers)
        {
            var ran = record.Sections.Where(s => s.Tier == tier.Tier).Select(s => s.Section)
                .Distinct(StringComparer.Ordinal).ToList();
            var missing = registered.Except(ran, StringComparer.Ordinal).ToList();
            var extra = ran.Except(registered, StringComparer.Ordinal).ToList();
            if (missing.Count > 0) problems.Add($"{tier.Tier} 档没有跑过 {string.Join(" / ", missing)}（冒烟器注册了它们）");
            if (extra.Count > 0) problems.Add($"{tier.Tier} 档跑出了 {string.Join(" / ", extra)}，冒烟器里没有注册这些段");
        }

        Assert.True(problems.Count == 0,
            "读数记录里的段与冒烟器注册的段不是同一批：" + string.Join("；", problems)
            + "。段集合对不上时，那份记录的总数就不属于当前这段代码——重跑 bash .winecheck/demo.sh 再改文档。");
    }

    /// <summary>
    /// <b>本门的主判据</b>：文档里每一处「当前源码…N 通过 / M 失败 ×3」必须逐字等于最近一次跑出来的读数，
    /// 一处对不上即红，并点名是哪份文件的哪一行。
    /// <para>
    /// <b>补的是哪个缺口</b>：那串加法（218 = 200 + 6 + 1 + 1 + 9 + 1）第一百九十三轮已经判过「加得起来」，
    /// 但<b>加得起来的算式照样可以是另一个数的算式</b>——真正的风险是文档里的 N 与当场跑出来的 N 不同，
    /// 而那件事此前只能靠人每轮记得回头改。这是 G-D5 不可得时答辩现场唯一能念的数，值得脱离人手。
    /// </para>
    /// <para>
    /// <b>「×3」也判</b>：文档写 ×3，记录里就得真有三档、且三档同一个数；只跑到一档时这一条会红，
    /// 而不是让「×3」变成一句没有出处的话。
    /// </para>
    /// </summary>
    [Fact]
    public void CurrentSourceReading_EqualsTheRunThatProducedTheRecord()
    {
        var record = ReadingRecord();
        var claims = CurrentReadingClaims();
        Assert.NotEmpty(claims);

        var passed = record.Tiers.Select(t => t.Passed).Distinct().ToList();
        var failed = record.Tiers.Select(t => t.Failed).Distinct().ToList();
        Assert.True(record.Tiers.Count == 3,
            $"{ReadingPath} 记了 {record.Tiers.Count} 档（文档写的是 ×3）——补跑：bash .winecheck/demo.sh");
        Assert.True(record.Tiers.Select(t => t.Tier).Distinct().Count() == 3,
            $"{ReadingPath} 里出现重复档位：{string.Join(" / ", record.Tiers.Select(t => t.Tier))}");

        var drift = new List<string>();
        foreach (var (file, line, claimedPassed, claimedFailed) in claims)
        {
            if (passed.Count != 1 || failed.Count != 1 || passed[0] != claimedPassed || failed[0] != claimedFailed)
            {
                drift.Add($"{file}:{line} 念的是「{claimedPassed} 通过 / {claimedFailed} 失败 ×3」");
            }
        }

        Assert.True(drift.Count == 0,
            "文档念的数与最近一次跑出来的数不是同一个。跑出来的是："
            + string.Join("；", record.Tiers.Select(t => $"{t.Tier} 档 {t.Passed} 通过 / {t.Failed} 失败（退出码 {t.Exit}）"))
            + $"（出处 {ReadingPath}，{record.Script} · {record.Date} · 源码 {record.Source[..10]}）。"
            + "对不上的行：" + string.Join(" ／ ", drift)
            + "。要么按跑出来的改文档（并在括号里写清这一段是哪一轮加上去的），要么重跑 "
            + "bash .winecheck/demo.sh——别把上一版的读数念成当前的。");
    }

    /// <summary>
    /// 冒烟器<b>第一段之前</b>那批的条数（全部静态可数，一个都不写死）：
    /// 真实控件构造清单 <c>Cases()</c> 的 <c>yield return</c> 数 + 控件工厂 <c>ControlCases()</c> 的
    /// <c>yield return</c> 数 + 「字段宽度实测」调用次数 + 「DPI 缩放实测」调用次数 × 源码里那个缩放档数组的长度。
    /// <para>
    /// 这一段不进任何 <c>Section</c>，所以读数记录的逐段之和里没有它们；两边一减一加才对得上总数。
    /// 结尾那两条「冒烟清单覆盖」按报告的切段口径落在最后一个段名下，不在这里重复计一次。
    /// </para>
    /// </summary>
    private static int SmokeBucketCount()
    {
        int Declared(string signature) =>
            Regex.Matches(BodyOf(signature), @"(?m)^\s*yield return").Count;

        var cases = Declared("private static IEnumerable<(string Name, Func<Control> Build)> Cases(");
        var controls = Declared("private static IEnumerable<(string Name, Func<Control> Build)> ControlCases(");
        var width = Regex.Matches(Repo.SmokeProgram, @"(?m)^\s+MeasureFieldWidths\(").Count;
        var scaled = Regex.Matches(Repo.SmokeProgram, @"(?m)^\s+MeasureFieldWidthsAtScale\(").Count;
        var tiers = Regex.Matches(Repo.SmokeProgram, @"foreach \(var percent in new\[\] \{([^}]*)\}\)")
            .Select(m => Regex.Matches(m.Groups[1].Value, @"\d+").Count)
            .ToList();

        Assert.True(cases >= 12 && controls >= 1 && width >= 1 && scaled == tiers.Count,
            $"冒烟器结构的取法退化了（构造清单 {cases}、控件工厂 {controls}、字段宽度 {width}、缩放 {scaled}/{tiers.Count}）——"
            + "这时算出来的桶数不可信，别拿它去核读数记录。");

        return cases + controls + width + tiers.Sum();
    }

    /// <summary>取一个声明到下一个顶格 <c>private static</c> 之间的正文（段体里有 lambda，不配花括号）。</summary>
    private static string BodyOf(string signature)
    {
        var from = Repo.SmokeProgram.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"冒烟器里找不到 {signature}。");
        var next = Repo.SmokeProgram.IndexOf("\n    private static", from + signature.Length, StringComparison.Ordinal);
        return next < 0 ? Repo.SmokeProgram[from..] : Repo.SmokeProgram[from..next];
    }

    /// <summary>
    /// 每一档的总数必须等于「第一段之前那批（静态可数）」+「该档各段之和（跑出来的）」。
    /// <para>
    /// 这条门是记录与源码之间的<b>加法对账</b>：往构造清单里加一条用例、给字段宽度多加一档、
    /// 注册表里多一段，都会让这一处立刻对不上（不必等 wine 重跑），而「答辩念的数与当场跑出来的对不上」
    /// 最难发现的一类正是这种<b>谁都没改错、只是没人回头加一遍</b>的漂。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingRecord_AddsUpToTheSmokeProgramsOwnStructure()
    {
        var record = ReadingRecord();
        var buckets = SmokeBucketCount();
        var wrong = new List<string>();

        foreach (var tier in record.Tiers)
        {
            var attempted = record.Sections.Where(s => s.Tier == tier.Tier)
                .Sum(s => s.Passed + s.Failed);
            var reported = tier.Passed + tier.Failed;
            if (reported != buckets + attempted)
            {
                wrong.Add($"{tier.Tier} 档报 {reported} 条，源码结构算出来是 {buckets + attempted} 条"
                    + $"（第一段之前 {buckets} + 各段之和 {attempted}）");
            }
        }

        Assert.True(wrong.Count == 0,
            "读数记录加不到冒烟器的结构上：" + string.Join("；", wrong)
            + $"。两种原因：冒烟器的结构变了而记录还是上一次那一次跑的（重跑 bash .winecheck/demo.sh），"
            + $"或者记录被手改过（{ReadingPath} 只能由两支演示脚本写出）。");
    }
}
