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
/// <para>
/// <b>这套门判的是「文档与脚本产出逐字一致」，不判「报告是真跑出来的」（第二百三十二轮，简报 P1）</b>：
/// 它把 <c>.winecheck/reading.txt</c> 里那几个数、<c>README.md</c> 与 <c>docs/NOTES.md</c> 里念的那几个数、
/// 冒烟器源码里注册的段集合三者对上——<b>对得上</b>是它判的全部。它<b>判不到</b>那份报告是不是真跑出来的：
/// <c>reading.txt</c> 与报告原文都可以被手写，门看不出来。这是「机器产出」与「真机证据」之间的固有边界，
/// 不是本门的疏漏，也不该靠再加一条断言去补（加一条判「像不像真跑」的断言，只会把边界挪到一句更不可靠的话上）。
/// 答辩现场若被追问「怎么证明三档是真跑的」，如实回答就是这一句：门证明的是三处说法一致；
/// 真跑的证据是 <c>.winecheck/demo.sh</c> 那一跑当场打出的控制台与 <c>/tmp/ah-demo/report-&lt;档位&gt;.txt</c>；
/// 而<b>真机</b>证据只有 <c>docs/evidence/real-machine/</c> 里归档的截图与读数——那九个目录现在都还空着，
/// 所以 G-D5 仍是 <c>[ ]</c>。
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

    /// <summary>那一处读数所在的句子范围（下标左闭右开，与 <see cref="SentenceAround"/> 同一取法）。</summary>
    private static (int Start, int End) SentenceBounds(string text, Match match)
    {
        var start = text.LastIndexOfAny(SentenceBreaks, Math.Max(0, match.Index - 1));
        var end = text.IndexOfAny(SentenceBreaks, match.Index + match.Length);
        return (start + 1, end < 0 ? text.Length : end);
    }

    /// <summary>一处读数所在的句子（前后各切到最近的句子边界）。</summary>
    private static string SentenceAround(string text, Match match)
    {
        var (start, end) = SentenceBounds(text, match);
        return text[start..end];
    }

    /// <summary>散文里的轮次字面（与上面判「有没有说轮次」那半句用同一个形状）。</summary>
    private static readonly Regex RoundAttribution = new(@"第[一二三四五六七八九十百零]+轮", RegexOptions.Compiled);

    /// <summary>
    /// 离那一处读数<b>最近</b>的轮次（同句之内），没有则 null。
    /// <para>
    /// <b>为什么不是「同句里第一处」</b>：G-D5 那一格里「第一百六十七轮 wine 三档 135/0 ×3（模拟器，不是真机）；
    /// <b>读数更新（第三百七十六轮）：当前源码三档 418 通过 / 0 失败 ×3</b>…」是<b>同一句</b>（句子按
    /// <c>。 | \n</c> 切，那里只隔一个分号）——取第一处就把当前读数归到那笔历史读数上，红的是门自己判错。
    /// 取最近的那一处，归的才是那句话真正拿来标注这笔读数的轮次。
    /// </para>
    /// </summary>
    private static string? NearestRound(string text, int start, int end, int reading)
    {
        string? nearest = null;
        var best = int.MaxValue;
        foreach (Match m in RoundAttribution.Matches(text, start))
        {
            if (m.Index >= end) break;
            var distance = Math.Abs(m.Index - reading);
            if (distance >= best) continue;
            best = distance;
            nearest = m.Value;
        }

        return nearest;
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
    /// <para>
    /// <b>第三百八十一轮在同一句判据上扩的那一句（简报 P0-2）：那一笔「当前源码」读数只允许有一个轮次归属</b>。
    /// 上面那半只判「<b>有没有</b>说轮次」，判不到「<b>说的是不是同一轮</b>」——第三百七十八轮重烧之后，
    /// <c>README.md</c>「已知限制」第 3 条与 <c>docs/NOTES.md</c> 的 G-D5 那一行都还写着「第三百七十六轮」，
    /// 而那一跑真正的归属是第三百七十八轮；三处字面各自都「有轮次」，这一族没有一条门看得见。
    /// 现在补的是这一件事：顶部「真机供给状态」那一段里的<b>那一行</b>（<see cref="LastFullRunEntry"/>，
    /// 唯一落点）念的轮次，必须与 <c>README.md</c> 全文、与「G-D5 真机观感」那一行里
    /// 凡把某笔三档读数念成「当前源码」<b>并且点了轮次</b>的每一句逐字相同（比的轮次取<b>同句里离那一处
    /// 读数最近的那一处</b>，见 <see cref="NearestRound"/>——那一格里还写着别轮的旧读数）；那一行念的日期与指纹，
    /// 必须逐字等于 <c>.winecheck/defense.txt</c> 的 <c>date=</c> / <c>source=</c>——那两行由
    /// <c>defense-day.sh</c> 四步走完自己写出，不是手抄，于是「文档说的那一跑」被接到「机器记的那一跑」上。
    /// <b>范围</b>：README.md 全文、「G-D5 真机观感」那一行，以及 NOTES 里每一处用<b>登记在册</b>的那几句
    /// 「当前读数」形状的地方——「登记在册」这四个字第三百八十二轮起有出处了：形状册写在
    /// <c>docs/NOTES.md</c> 顶部判据层那一行（<see cref="ClaimShapeRegistryEntry"/>，唯一落点），
    /// 本门按 <see cref="CurrentClaimShapes"/> 现读它，不再在本文件里硬写那两句字面（第三百八十一轮之前
    /// <c>CurrentClaimPhrases</c> 就是那两个字面量，简报把它点成「口径漂移的下一个入口」：下一轮换一种说法
    /// 写「当前读数」，门看不见，而人看得见）。<b>判据本体一条没放宽</b>：被念成当前读数的每一句，
    /// 轮次仍须与顶部那一行相同、日期与指纹仍须与 <c>defense.txt</c> 相同；改的只是「哪几句算当前声称」
    /// 从哪里读。删掉登记行即红（范围没有落点），登记一句仓里没处在用的形状同样红（表不许养死条目），
    /// 六发注入见 <see cref="ClaimShapeRegistryDetector_ReportsTheBreaksItMustCatch"/>。
    /// 上面列的那几处才是「被当成当前读数念出来」的地方；逐轮日志的散文不用登记在册的形状，历史轮次照旧不拉平——
    /// 而且扫描在账本那第一个 <c>## 第…轮</c> 标题之前停住（<see cref="LedgerStartLine"/>：账本里会原样引用
    /// 改准前那句字面，那是那一轮那一跑的账，不是当前声称）。
    /// 这一族要一起改这件事，第三百四十五轮那一行账已经写过（「同一族跟着改的三处归属」），
    /// 本轮起它从散文变成可判。
    /// <b>它不判什么</b>：不判那个轮次号写得对不对（门读不到 <c>git log</c>，判的是「三处说法一致 +
    /// 那一跑的日期与指纹确由脚本写出」），也不判那一跑是不是真跑出来的——与 <c>reading.txt</c>
    /// 那一条固有边界同一口径（见本类注释）；<b>也不判一种没有登记的新说法</b>——门只判它认得的形状，
    /// 那是判据的边界，不是这一族的疏漏；要换说法就得先改登记，而改登记这件事在 NOTES 那一行上看得见。
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
                if (RoundAttribution.IsMatch(sentence)) continue;
                unattributed.Add($"{file}：{sentence.Trim()}");
            }
        }

        Assert.True(unattributed.Count == 0,
            "这些三档读数没说清是哪一轮的（同一句里既没有「当前源码」也没有「第…轮」）："
            + string.Join(" ／ ", unattributed)
            + "。历史读数本来就各不相同，但每一处都得说清自己是谁的读数——否则它会被读成第二个当前读数。");

        // —— 第三百八十一轮（简报 P0-2）：同一笔「当前源码」读数只允许有一个轮次归属 ——
        var run = LastFullRunAttribution();
        var defense = DefenseRecordFile();
        var drift = new List<string>();
        if (run.Date != defense.Date)
        {
            drift.Add($"顶部那一行念的日期是 {run.Date}，而 {DefenseRecordPath} 的 date= 是 {defense.Date}");
        }
        if (!defense.Source.StartsWith(run.Fingerprint, StringComparison.Ordinal))
        {
            drift.Add($"顶部那一行念的指纹是 {run.Fingerprint}…，而 {DefenseRecordPath} 的 source= 是 {Short(defense.Source)}");
        }

        var attributed = 0;
        // 范围就是「被当成当前读数念出来」的那几处：README 全文、G-D5 那一行，
        // 以及 NOTES 里（逐轮账本之前）每一处用上登记在册的那几句「当前读数」形状的地方
        // ——第三百四十五轮那一条账把这几处点成「同一族跟着改的三处归属」。
        // 登记行自己是那张表、不是一处声称，故排除在范围之外；逐轮账本里的散文不用那些形状，照旧不在范围内。
        var shapes = CurrentClaimShapes();
        var scopes = new List<(string Label, string Text)>
        {
            ("README.md", Repo.Source("README.md")),
            ("docs/NOTES.md 的 G-D5 行", Gd5Row()),
        };
        scopes.AddRange(PreLedgerNotesLines()
            .Where(x => !ClaimShapeRegistryEntry.IsMatch(x.Text))
            .Select(x => (x.Line, x.Text, Phrase: shapes.FirstOrDefault(
                p => x.Text.Contains(p, StringComparison.Ordinal))))
            .Where(s => s.Phrase is not null)
            .Select(s => ($"docs/NOTES.md:{s.Line}「{s.Phrase}」那一行", s.Text)));

        foreach (var (label, text) in scopes)
        {
            foreach (Match m in CurrentReading.Matches(text))
            {
                var (start, end) = SentenceBounds(text, m);
                var round = NearestRound(text, start, end, m.Index);
                if (round is null) continue;
                attributed++;
                if (round != run.Round)
                {
                    drift.Add($"{label} 把那一笔当前源码读数归到 {round}，顶部那一行归的是 {run.Round}");
                }
            }
        }

        // 地板：至少有一处把当前读数归到某轮——把轮次删光不算「对上了」。
        Assert.True(attributed >= 1,
            "那一族里（README.md 全文、G-D5 那一行、NOTES 里每一处用登记在册形状的地方，登记表见 "
            + "docs/NOTES.md 顶部「当前读数声称的合法形状登记」那一行）没有任何一处"
            + "把那笔当前读数归到某轮：顶部那一行没有对照物，把轮次删光就能绿。");

        Assert.True(drift.Count == 0,
            "「最近一次三档整支跑是哪一轮、哪一天、哪一版源码」在这几处不是同一个答案："
            + string.Join(" ／ ", drift)
            + "。改法是现读 `.winecheck/defense.txt` 的 date= / source= 与那一跑自己的轮次，把三处一起改成同一个——"
            + "别只改被点到的那一处，更别把轮次号改成没跑过的日子（重烧一次：PUBLISH=1 bash .winecheck/defense-day.sh）。");
    }

    /// <summary>
    /// 顶部「真机供给状态」那一段里那一行的<b>行首形状</b>——「最近一次三档整支跑是哪一轮」的唯一落点。
    /// <para>
    /// <b>为什么锚在行首而不是找子串</b>：下面逐轮账本里会把那一行<b>原样引一次</b>（第三百八十一轮那条就是这么
    /// 念的）——那是引用，不是第二个落点。只认「列表项 + 加粗起头是这一句」那种行。
    /// </para>
    /// </summary>
    private static readonly Regex LastFullRunEntry = new(
        @"^\s*>?\s*-\s+\*\*最近一次三档整支跑", RegexOptions.Compiled);

    /// <summary>
    /// 「当前读数声称的合法形状登记」那一行的<b>行首形状</b>——那张表的唯一落点（第三百八十二轮，简报 P0-1）。
    /// <para>
    /// <b>为什么锚在行首</b>：与 <see cref="LastFullRunEntry"/> 同一取法。逐轮账本里会把这句话原样引一次，
    /// 那是引用、不是第二张表；只认「blockquote 里加粗起头是这一句」那种行。
    /// </para>
    /// </summary>
    private static readonly Regex ClaimShapeRegistryEntry = new(
        @"^\s*>\s*\*\*当前读数声称的合法形状登记", RegexOptions.Compiled);

    /// <summary>
    /// 登记行里那串形状的起头。它之后<b>到第一个「。」为止</b>就是登记表——所以形状本身不许含「。」，
    /// 也不许跨行（一行就是一张表）。
    /// </summary>
    private const string ClaimShapeMarker = "形状登记：";

    /// <summary>登记表里一个形状的写法：一对反引号之间的字面（与「证据出处」那一族的引用写法同一分寸）。</summary>
    private static readonly Regex ClaimShapeItem = new(@"`([^`]*)`", RegexOptions.Compiled);

    /// <summary>账本之前的那些 NOTES 行（行号 + 原文）——当前声称的扫描面止于此处。</summary>
    private static List<(int Line, string Text)> PreLedgerNotesLines() =>
        Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n')
            .Take(LedgerStartLine())
            .Select((text, i) => (Line: i + 1, Text: text))
            .ToList();

    /// <summary>
    /// 那一行登记<b>抓不抓得住</b>——判据本体（收成一处，好让
    /// <see cref="ClaimShapeRegistryDetector_ReportsTheBreaksItMustCatch"/> 把每一发破坏各喂一遍）。
    /// <paramref name="registryLines"/> 是匹配 <see cref="ClaimShapeRegistryEntry"/> 的行，
    /// <paramref name="usageLines"/> 是仓里其余可能被念成当前声称的地方（登记行自身不在里面）。
    /// 返回问题清单（空 = 这一行是张能用的表）与解析出来的形状。
    /// </summary>
    private static (List<string> Problems, List<string> Shapes) ReadClaimShapeRegistry(
        IReadOnlyList<string> registryLines, IReadOnlyList<string> usageLines)
    {
        if (registryLines.Count != 1)
        {
            return ([$"「当前读数声称的合法形状登记」那一行读到 {registryLines.Count} 处，要的是恰好一处："
                    + "0 处 = 门的范围没有落点（「哪几句算当前声称」退回只有代码知道的事）；"
                    + "2 处以上 = 那张表有了两个答案，改一处不动另一处就分叉。"
                    + "改的地方在 docs/NOTES.md 顶部判据层那一行（加粗起头「当前读数声称的合法形状登记」）。"], []);
        }

        var line = registryLines[0];
        var at = line.IndexOf(ClaimShapeMarker, StringComparison.Ordinal);
        if (at < 0)
        {
            return ([$"那一登记行里没有「{ClaimShapeMarker}」那一起头：表写在散文里读不出来，"
                     + "形状册等于没有。形状要写成「形状登记：`一句形`、`另一句形`」这一种可以现读的样子。"], []);
        }

        var list = line[(at + ClaimShapeMarker.Length)..];
        var stop = list.IndexOf('。');
        if (stop >= 0) list = list[..stop];

        var shapes = ClaimShapeItem.Matches(list)
            .Select(m => m.Groups[1].Value)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (shapes.Count == 0)
        {
            return ([$"那一登记行的「{ClaimShapeMarker}」后面一个形状都没有（取到的是「{list.Trim()}」）："
                     + "表是空的，门就没有范围——凡把某笔三档读数念成当前读数的句子全落在门外。"], []);
        }

        // 死条目：登记了一句没有处在用的形状。这一半把「表」与「实物」两侧对账，
        // 挡的是「改了一句形却忘了删旧的」——那种条目让范围悄悄变大，而看起来仍然是一条条规矩。
        var dead = shapes
            .Where(shape => !usageLines.Any(text => text.Contains(shape, StringComparison.Ordinal)))
            .Select(shape => $"登记的形状「{shape}」在仓里没有任何一处在用（README.md 全文与账本之前的 NOTES 行都扫过）："
                             + "要么那一处声称被改写成了别的说法（那就把新说法登记进来，并写明用在哪几处），"
                             + "要么这一条该从表里删掉——表不许养死条目。")
            .ToList();

        return (dead, shapes);
    }

    /// <summary>
    /// 现读那一行登记，得到「被念成当前读数」的那几句形状。<b>本门不再在本文件里抄第二份字面</b>
    /// （第三百八十一轮之前那两句是硬编码的 <c>string[]</c>，简报把它点成「口径漂移的下一个入口」）。
    /// 表不在、有两处、没有登记表、表是空的、或有一句没处在用 → 当场红，红话就是
    /// <see cref="ReadClaimShapeRegistry"/> 念出来的那一句。
    /// </summary>
    private static string[] CurrentClaimShapes()
    {
        var preLedger = PreLedgerNotesLines();
        var registry = preLedger.Where(x => ClaimShapeRegistryEntry.IsMatch(x.Text)).Select(x => x.Text).ToList();
        // 用法那一侧：账本之前的 NOTES 行（登记行自身除外——它是表，不是一处声称）+ README 全文。
        // G-D5 那一行本就在账本之前，不重复塞第二份。
        var usage = preLedger.Where(x => !ClaimShapeRegistryEntry.IsMatch(x.Text)).Select(x => x.Text).ToList();
        usage.Add(Repo.Source("README.md"));

        var (problems, shapes) = ReadClaimShapeRegistry(registry, usage);
        Assert.True(problems.Count == 0,
            "「哪几句算把某笔三档读数念成当前读数」这件事，在 docs/NOTES.md 顶部那一行登记上读不出来了："
            + string.Join(" ／ ", problems));
        return shapes.ToArray();
    }

    /// <summary>
    /// 上面那张表<b>抓不抓得住</b>——七发各喂一次（第三百八十二轮立六发，第三百八十四轮补第七发）。
    /// <para>
    /// 只在真仓上判的门，抓不抓得住全靠那天有没有人想到去试；本仓对每一条判据都跑一遍反向，
    /// 为的就是让「它绿着」不等于「它什么都看不见」。夹具里的形状字面是<b>自造的</b>（「甲句形」这类）：
    /// 判据自测不该把仓里那两句真形状再抄一份进测试，那正是本轮要拆掉的东西。
    /// </para>
    /// <para>
    /// 第七发补的是这一族此前唯一的空档：① 与 ⑤ 都是<b>反向</b>（删登记行红、旧说法没人用红），
    /// 而「往表里加一个新形状、并且真有一处在用它」这条<b>正向</b>路从来没实测——下一轮要登记新句式时，
    /// 除了猜没有别的依据。
    /// </para>
    /// </summary>
    [Fact]
    public void ClaimShapeRegistryDetector_ReportsTheBreaksItMustCatch()
    {
        var registryLine = "> **当前读数声称的合法形状登记**（判据）：形状登记：`甲句形`、`乙句形`。凡用登记在册的任一句形……";

        // ① 登记行不在（0 处）：门的范围没有落点。
        var missing = ReadClaimShapeRegistry([], [registryLine]);
        Assert.Single(missing.Problems);
        Assert.Contains("读到 0 处", missing.Problems[0], StringComparison.Ordinal);

        // ② 登记行有两处：那张表有了两个答案。
        var doubled = ReadClaimShapeRegistry([registryLine, registryLine], []);
        Assert.Single(doubled.Problems);
        Assert.Contains("读到 2 处", doubled.Problems[0], StringComparison.Ordinal);

        // ③ 那一行在，但没有「形状登记：」起头：表写在散文里读不出来。
        var noMarker = ReadClaimShapeRegistry(
            ["> **当前读数声称的合法形状登记**（判据）：这一行只写了「当前源码最近那一句形」这样一句话。"], []);
        Assert.Single(noMarker.Problems);
        Assert.Contains("那一起头", noMarker.Problems[0], StringComparison.Ordinal);

        // ④ 起头在，反引号里一个形状都没有：表是空的，范围就成了「哪儿都不判」。
        var emptyTable = ReadClaimShapeRegistry(
            ["> **当前读数声称的合法形状登记**（判据）：形状登记：见下面那两句。"], []);
        Assert.Single(emptyTable.Problems);
        Assert.Contains("一个形状都没有", emptyTable.Problems[0], StringComparison.Ordinal);

        // ⑤ 登记了一句仓里没处在用的形状（甲在用、乙没处在用）：只报乙那一处，不牵连甲。
        var dead = ReadClaimShapeRegistry([registryLine], ["……这一行里有甲句形 418 通过 / 0 失败 ×3……"]);
        Assert.Single(dead.Problems);
        Assert.Contains("乙句形", dead.Problems[0], StringComparison.Ordinal);
        Assert.DoesNotContain("甲句形", dead.Problems[0], StringComparison.Ordinal);

        // ⑥ 合规那一发：不报，且解析出登记在册的两个形状（顺序按那一行写的顺序）。
        var clean = ReadClaimShapeRegistry(
            [registryLine],
            ["……甲句形……", "……乙句形 418 通过 / 0 失败 ×3……"]);
        Assert.Empty(clean.Problems);
        Assert.Equal(["甲句形", "乙句形"], clean.Shapes);

        // ⑦ 正向那一发（第三百八十四轮）：往表里加一个新形状，且真有一处在用它——门要现读出那三个形状、不报。
        var widened = ReadClaimShapeRegistry(
            ["> **当前读数声称的合法形状登记**（判据）：形状登记：`甲句形`、`乙句形`、`丙句形`。凡用登记在册的任一句形……"],
            ["……甲句形……", "……乙句形 418 通过 / 0 失败 ×3……", "……丙句形 418 通过 / 0 失败 ×3……"]);
        Assert.Empty(widened.Problems);
        Assert.Equal(["甲句形", "乙句形", "丙句形"], widened.Shapes);
    }

    /// <summary>逐轮账本的开头：NOTES 里第一处 <c>## 第…轮</c> 标题——那以下每一行都是「某一轮那一跑」的账。</summary>
    private static readonly Regex RoundLedgerHeading = new(@"^## 第[一二三四五六七八九十百零]+轮", RegexOptions.Compiled);

    /// <summary>
    /// 账本从第几行开始（找不到标题就当作没有账本，返回全文长度）。
    /// <para>
    /// <b>为什么范围要在账本之前停住</b>：账本里会<b>原样引用</b>红消息与旧说法——第三百八十一轮那条就逐字引了
    /// 「读数更新（第三百七十六轮）：当前源码三档 418 通过 / 0 失败 ×3」那一处改准前的字面。
    /// 那种句子带着「当前读数」的形状，但它属于<b>那一轮那一跑</b>，按本文件从头到尾的边界（历史读数不拉平）
    /// 不该被当成当前声称——不停住，门就会把「把当时那一红记下来」这件事判成又一处漂移。
    /// </para>
    /// </summary>
    private static int LedgerStartLine()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var at = Array.FindIndex(lines, l => RoundLedgerHeading.IsMatch(l));
        return at < 0 ? lines.Length : at;
    }

    /// <summary>那一行必须一起说全的三样：轮次、日期、指纹前 10 位（形状写歪即红，不许只留一句好看的话）。</summary>
    private static readonly Regex LastFullRunShape = new(
        @"\*\*最近一次三档整支跑[^\n]*?：(?<round>第[一二三四五六七八九十百零]+轮) · (?<date>\d{4}-\d{2}-\d{2}) · 指纹 `(?<fp>[0-9a-f]{10})…",
        RegexOptions.Compiled);

    /// <summary>
    /// 读顶部那一行（第三百八十一轮）。读到 0 处即红（那一跑没有落点），读到 2 处以上即红
    /// （「最近一次」有了两个答案），三样缺一即红。
    /// </summary>
    private static (string Round, string Date, string Fingerprint) LastFullRunAttribution()
    {
        var hits = Repo.Source("docs", "NOTES.md").Split('\n')
            .Where(l => LastFullRunEntry.IsMatch(l)).ToList();
        Assert.True(hits.Count == 1,
            $"顶部「真机供给状态」那一段里那一行（列表项加粗起头「最近一次三档整支跑」）读到 {hits.Count} 处，要的是恰好一处："
            + "「最近一次三档整支跑是哪一轮、哪一天、哪一版源码」要么没有落点，要么有了两个答案。");

        var m = LastFullRunShape.Match(hits[0]);
        Assert.True(m.Success,
            "那一行没有按「**最近一次三档整支跑…：第 N 轮 · YYYY-MM-DD · 指纹 `hex…`」把三样一起说全："
            + hits[0].Trim()
            + "。轮次 / 日期 / 指纹少任何一样，这一跑都追不回去。");
        return (m.Groups["round"].Value, m.Groups["date"].Value, m.Groups["fp"].Value);
    }

    /// <summary>
    /// 上下文注解必须<b>同时</b>出现的两个词（第二百二十四轮新增，第二百二十五轮收紧，简报 P0-2）。
    /// <para>
    /// <b>为什么从「三选一」收紧成「两个都要」</b>：第二百二十四轮那一版判的是「『慢一档』『进程 DPI』
    /// 『上下文』任一出现」——<b>这正是同一轮自己在 NOTES 里记下的假绿形状</b>（Presence 型断言在同一个
    /// 字符串里被另一处满足）：把注解换成「快一档」照样绿，只写一句「上下文」也照样绿。
    /// </para>
    /// <para>
    /// 这两个词共同表达的是<b>同一个事实</b>：控件自报 DPI 停在上一档、而字体按进程 DPI 放大——
    /// 于是行高不跟着放大。只写其中一个，读的人会以为「控件自报 DPI 跟着档位走」（那是另一支读数），
    /// 或者以为「进程 DPI 也停在上一档」（那是把两个数混成一个）。所以两个都得在。
    /// </para>
    /// </summary>
    private static readonly string[] ContextWords = ["慢一档", "进程 DPI"];

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
    /// （<c>N 通过 / 0 失败 ×3</c>）的<b>同一句或紧邻下一句</b>必须<b>同时</b>出现「慢一档」与「进程 DPI」
    /// （第二百二十五轮收紧：原来判的是「三选一」，把注解换成「快一档」照样绿——那是 Presence 型假绿）。
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
                // 两个词都要在（同一句或紧邻下一句这个窗口里）：只出现一个不算注解。
                if (ContextWords.All(w => sentence.Contains(w, StringComparison.Ordinal)
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
            "这些三档读数旁边没有完整的上下文注解（同一句或紧邻下一句里必须同时有「慢一档」与「进程 DPI」——"
            + "只出现其中一个不算：这两个词共同表达「控件自报 DPI 停在上一档、字体按进程 DPI 放大而行高不放大」这一件事）："
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

    // ---------- 验收表「GUI 证据」那一格的读数口径（第二百七十轮，简报 P0-1）----------

    /// <summary>
    /// 验收表每一格的「GUI 证据」只许给出<b>一个</b>代表当前源码的读数（第二百七十轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：G-D3 那一格此前写着「最近一次真跑：第二百四十三轮……246 通过 / 0 失败 ×3」，
    /// 同一格里又留着「读数出处：第一百七十七轮……180 通过 / 0 失败 ×3」，而全仓当前那一笔是 257——
    /// <b>三笔并列、没有一笔声称自己代表当前源码</b>。被追问「三个数哪个对」时答不上来的不是代码，是这一格。
    /// </para>
    /// <para>
    /// <b>本仓此前的门为什么抓不住</b>：<see cref="CurrentSourceReading_IsTheSameNumberInReadmeAndNotes"/>
    /// 与 <see cref="EveryThreeTierReading_IsAttributedToARoundOrTheCurrentSource"/> 认的是
    /// 「当前源码」与「第…轮」两种句式，而 <c>最近一次真跑：第二百四十三轮</c> 里既有轮次、又不声称是当前
    /// 读数——两条门都判不到它。<b>这一条补的正是那一种句式</b>：GUI 证据格里不许再出现
    /// 「最近一次真跑：第 N 轮」，要么写成「当前源码最近一次三档复跑为第 N 轮 …」，要么把那笔移进
    /// 「X：详细口径」小节、格子里只留一句指向。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那一笔数字对不对（<see cref="ReadingRecord_SourceIsTheCurrentSource"/> 判
    /// <c>reading.txt</c> 的 <c>source=</c> 是不是当前源码）；不判详细口径小节里能并列几笔历史读数
    /// （那是历史账，本来就该各不相同）；也不判表格以外的正文——只有<b>验收表那一列</b>是答辩现场念出来的东西。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryGuiEvidenceCell_NamesTheCurrentSourceReadingOrPointsToTheDetailSection() =>
        Assert.Empty(GuiEvidenceCellProblems(GuiEvidenceCells(Repo.Source("docs", "NOTES.md"))));

    /// <summary>
    /// 验收表里可勾选行的「条目名 + GUI 证据」两列（第 5 列）。取法与
    /// <c>AcceptanceChecklistTests</c> 同一形状：五列、首格是勾选框，列数不对的行不归这一族管。
    /// </summary>
    private static List<(string Item, string Gui)> GuiEvidenceCells(string notes) => notes
        .ReplaceLineEndings("\n").Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.StartsWith('|'))
        .Select(line => line.Trim('|').Split('|').Select(cell => cell.Trim()).ToList())
        .Where(cells => cells.Count == 5 && (cells[0] == "[x]" || cells[0] == "[ ]"))
        .Select(cells => (Item: cells[1], Gui: cells[4]))
        .ToList();

    /// <summary>「最近一次真跑：第 N 轮」那一种句式（中文数字与阿拉伯数字都算）。</summary>
    private static readonly Regex StaleGuiReading = new(
        @"最近一次真跑：第[一二三四五六七八九十百千零〇\d]+轮", RegexOptions.Compiled);

    /// <summary>判据本体：格子进、问题出（空清单 = 每一格都只认一个当前读数）。</summary>
    private static List<string> GuiEvidenceCellProblems(IEnumerable<(string Item, string Gui)> cells) => cells
        .Where(cell => StaleGuiReading.IsMatch(cell.Gui))
        .Select(cell => $"「{cell.Item}」的 GUI 证据格写着「{StaleGuiReading.Match(cell.Gui).Value}…」：那一格里没有一处读数声称自己代表当前源码，"
            + "读者只能自己猜哪一个才是现在的数。改成「当前源码最近一次三档复跑为第 N 轮 …」，"
            + "或把那笔历史读数移进「X：详细口径」小节、格子里只留一句指向。")
        .ToList();

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住（第二百七十轮）：三发各喂一次——旧形状那一发必须报出自己那一格，
    /// 当前源码句式与「只留指向」两发不许报。
    /// </summary>
    [Fact]
    public void GuiEvidenceCellDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 旧形状：第二百七十轮之前 G-D3 那一格的原样（同一格两笔并列、没有一笔声称是当前源码）。
        var stale = GuiEvidenceCellProblems(new[]
        {
            ("G-D3 状态完备", "最近一次真跑：第二百四十三轮（三档各跑一次，246 通过 / 0 失败 ×3）；读数出处：第一百七十七轮（180 通过 / 0 失败 ×3）。"),
        });
        Assert.Single(stale);
        Assert.Contains("G-D3 状态完备", stale[0], StringComparison.Ordinal);
        Assert.Contains("当前源码", stale[0], StringComparison.Ordinal);

        // ② 当前源码句式：不报（这一句自己就写着它是谁的读数）。
        Assert.Empty(GuiEvidenceCellProblems(new[]
        {
            ("G-D4 中文排版", "当前源码最近一次三档复跑为第二百七十轮 257 通过 / 0 失败 ×3、逐屏对账 0 处对不上。"),
        }));

        // ③ 只留指向：不报（历史读数移进详细口径小节之后，格子里不再有那一句式）。
        Assert.Empty(GuiEvidenceCellProblems(new[]
        {
            ("G-D3 状态完备", "当前源码最近一次三档复跑为第二百七十轮 257 通过 / 0 失败 ×3；此前各次读数见「G-D3 状态完备：详细口径」。"),
        }));
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

    /// <summary>
    /// 中文数字（一…二十，另认「两」）。逐屏表里的段数最大是八，留到二十是给以后加段留余量；
    /// 「两」是第三百八十四轮补的：表里写「两段」的地方真实存在（第 1 行 <c>RunProfileJumpFilterKeep</c>），
    /// 而它此前不在这张表里，那一格每次都被解析成「没有段数」放过——值写错了门也不会红。
    /// </summary>
    private static int? ChineseNumber(string text) => text switch
    {
        "一" => 1, "二" => 2, "两" => 2, "三" => 3, "四" => 4, "五" => 5,
        "六" => 6, "七" => 7, "八" => 8, "九" => 9, "十" => 10,
        _ => text.StartsWith('十') && text.Length == 2 && "一二三四五六七八九".Contains(text[1])
            ? 10 + "一二三四五六七八九".IndexOf(text[1]) + 1
            : null,
    };

    /// <summary>
    /// 段数的取法。字符类与 <see cref="ChineseNumber"/> 必须是同一份——少一个写法，那一格就安静地落在门外。
    /// 「第 / 这 / 那 / 每 / 同 / 逐」开头的排除掉：那是「第几段」或指代，不是「共几段」。
    /// </summary>
    private static readonly Regex StatedSegmentCount = new(
        @"(?<![第这那每同逐])([一二三四五六七八九十两]+)段", RegexOptions.Compiled);

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
        var wrong = ScreenSegmentCountProblems(ScreenRows(), SegmentCounts());
        Assert.True(wrong.Count == 0,
            "逐屏对账表里这些段数与冒烟器对不上：" + string.Join(" ／ ", wrong)
            + "。答辩现场念的就是这个数——改一段就回来改表，别让表比代码旧。");
    }

    /// <summary>
    /// 判据本体：逐屏表的行 + 冒烟器每段真有几段 进，对不上的清单出（空 = 表里每一个数都是冒烟器里的那一个）。
    /// <para>
    /// 收成一处是为了让
    /// <see cref="ScreenSegmentCountDetector_ReportsTheBreaksItMustCatch"/> 能把「两段」那一发真喂进去。
    /// 第三百八十三轮挂在账上的正是这件事：真实仓上红不下来的原因在<b>解析器</b>里，
    /// 而喂真实数据的那一条 [Fact] 只会一直绿——判据本体不接受外部数据时，那一族破坏永远喂不进去。
    /// </para>
    /// </summary>
    private static List<string> ScreenSegmentCountProblems(
        IReadOnlyList<(string Num, string Evidence)> rows, IReadOnlyDictionary<string, int> counts)
    {
        var wrong = new List<string>();

        foreach (var (num, evidence) in rows)
        {
            var flat = StripParentheticals(evidence);

            foreach (var clause in flat.Split(['、', '，', '；', '。', '+', '\n']))
            {
                var segments = Regex.Matches(clause, @"Run[A-Za-z0-9]+")
                    .Select(m => m.Value).Distinct().ToList();
                var stated = StatedSegmentCount.Matches(clause)
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

        return wrong;
    }

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住（第三百八十四轮，简报 P1-3）：六发各喂一次夹具。
    /// <para>
    /// 前两发是本轮那一改的正面证据：①「两段」写对不许报、②「两段」写错必须报。改之前这两发<b>是同一个答案</b>
    /// （都不报）——因为「两」不在取法的字符类里，那一格被解析成「没有段数」，
    /// 再被「一个段名 + 一个段数才判」的跳过条件放过；本轮收的就是这一格从「门跳过」变成「门覆盖」。
    /// ③ 到 ⑥ 是不许跟着一起松的四发：中文「三段」那一族照旧咬得住、「第三段」是序号不是段数、
    /// 一句里两个段名仍判不出归属（跳过，不猜）、「A 屏 × B 段 = C 段」乘不起来仍报。
    /// </para>
    /// <para>
    /// <b>按第二百七十轮五步法判过</b>：① 断言里的名字 = <c>docs/NOTES.md</c>「降级答辩脚本」逐屏表那一格的字面、
    /// <c>ApifoxHelper.Smoke/Program.cs</c> 里的 <c>RunAction(</c> 站点数；② 这两样都不在被这条门定义的对象里
    /// （门自己不产出表，也不产出冒烟器）——答「不是」；③ 故按<b>对账门</b>处理，照常随实物一起长，
    /// 本轮认「两」是把判据范围从 42 格扩到 43 格（现读实测新增受覆盖的就是第 1 行那一格，且它值对得上），
    /// 不是放宽；④ 实测见本方法六发与本轮账本里那两发真实仓注入；⑤ 结论写进本轮账本与 commit message。
    /// </para>
    /// </summary>
    [Fact]
    public void ScreenSegmentCountDetector_ReportsTheBreaksItMustCatch()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["RunFixtureFilterKeep"] = 2,
            ["RunFixtureBanner"] = 3,
            ["RunFixtureNotice"] = 4,
            ["RunFixtureSort"] = 5,
        };

        // ① 「两段」值对（2 对 2）：不报。这一格本轮起才第一次被门读到，所以要先确认「读到了也不冤枉」。
        Assert.Empty(ScreenSegmentCountProblems(
            [("1", "wine 冒烟 `RunFixtureFilterKeep` 两段（词命中那一行 → 词还在）")], counts));

        // ② 「两段」值错（表里 2、冒烟器里 3）：必须报，并且点名是哪一行哪一段、两个数各是多少。
        //    改之前这一发与 ① 同答案（都不报）——那正是第三百八十一轮挂账、本轮收口的那一处盲区。
        var liang = ScreenSegmentCountProblems([("1", "wine 冒烟 `RunFixtureFilterKeep` 两段")],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["RunFixtureFilterKeep"] = 3 });
        Assert.Single(liang);
        Assert.Contains("RunFixtureFilterKeep", liang[0], StringComparison.Ordinal);
        Assert.Contains("写的是 2 段", liang[0], StringComparison.Ordinal);
        Assert.Contains("冒烟器里是 3 段", liang[0], StringComparison.Ordinal);

        // ③ 旧形状没被削弱：「三段」写错照旧报（表里 3、冒烟器里 4）。
        var san = ScreenSegmentCountProblems([("2", "wine 冒烟 `RunFixtureBanner` 三段")],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["RunFixtureBanner"] = 4 });
        Assert.Single(san);
        Assert.Contains("RunFixtureBanner", san[0], StringComparison.Ordinal);

        // ④ 「第三段」是「第几段」不是「共几段」：排除项在位，不许凭空报出一格。
        Assert.Empty(ScreenSegmentCountProblems(
            [("3", "wine 冒烟 `RunFixtureNotice` 第三段量的是提示行行高")], counts));

        // ⑤ 一句里两个段名共用一个数：归属读不出来 → 跳过、不猜（认了「两」也不许把它读成某一段的数）。
        Assert.Empty(ScreenSegmentCountProblems(
            [("4", "wine 冒烟 `RunFixtureNotice` / `RunFixtureSort` 两段")], counts));

        // ⑥ 乘积算式：15 屏 × 4 段 = 11 段 乘不起来 → 报。
        var product = ScreenSegmentCountProblems([("5", "15 屏 × 4 段 = 11 段")], counts);
        Assert.Single(product);
        Assert.Contains("乘不起来", product[0], StringComparison.Ordinal);
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

    /// <summary>
    /// 一档的行高预算读数（第二百三十一轮）：配置表单过预算的行数、提示行过预算的条数，
    /// 以及<b>同一行报出来的那两个预算</b>（<c>行高预算 Npx</c> / <c>阈值 Npx</c>）。
    /// <para>
    /// 为什么把预算本身也记进来：答辩口径「行高预算判定（细则）」里念的那两个数
    /// （125% 档 50px、200% 档 65px）此前只有报告原文可查，报告落在 <c>/tmp</c>、跑完就没了；
    /// 记进这份入库的读数记录之后，文档念的数与跑出来的数才第一次能逐字对上。
    /// </para>
    /// </summary>
    private sealed record OverBudgetRun(int Tier, int Form, int FormBudget, int Hint, int HintBudget);

    /// <summary>一份读数记录（来历四行 + 每档总计 + 每档行高预算 + 每段计数）。</summary>
    private sealed record Reading(string Script, string Tool, string Date, string Source,
        List<TierRun> Tiers, List<OverBudgetRun> OverBudget, List<SectionRun> Sections);

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
        var overBudget = new List<OverBudgetRun>();
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
                case "overbudget":
                    var o = Regex.Match(line,
                        @"^overbudget=tier=(\d+) form=(\d+) formbudget=(\d+)px hint=(\d+) hintbudget=(\d+)px$");
                    Assert.True(o.Success,
                        $"{ReadingPath} 有一行 overbudget= 不是「overbudget=tier=N form=N formbudget=Npx hint=N hintbudget=Npx」：{line}");
                    overBudget.Add(new OverBudgetRun(int.Parse(o.Groups[1].Value), int.Parse(o.Groups[2].Value),
                        int.Parse(o.Groups[3].Value), int.Parse(o.Groups[4].Value), int.Parse(o.Groups[5].Value)));
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

        return new Reading(script, tool, date, source, tiers, overBudget, sections);
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
    /// <b>它不判什么</b>：<b>不判这份记录比当前源码新</b>——那一件事第二百五十九轮起由
    /// <see cref="ReadingRecord_SourceIsTheCurrentSource"/> 判（<c>source=</c> 必须等于当前源码的指纹）。
    /// 本条只管「来历四行齐不齐、口径写没写清」。第二百零七轮这里曾写着「不判记录比当前源码新」，
    /// 理由是「一道当场解不开的闸门比没有闸门更糟」；第二百五十九轮把那条判据搬进测试时，
    /// 解不开的那一半已经解开了（失败信息里就写着补救命令，且它不需要 wine），理由与取舍见那一条的注释。
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

    // ---------- 第二百三十一轮（简报 P0-1）：行高预算那一格的两个数第一次有机器产出的出处 ----------

    /// <summary>「行高预算判定（细则）」那一节——答辩当天照它判「超了算不算过」。</summary>
    private const string OverBudgetDetailHeading = "### 行高预算判定（细则";

    /// <summary>那一节的正文（从标题到下一个同级标题）。</summary>
    private static string OverBudgetDetailSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(OverBudgetDetailHeading, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $"docs/NOTES.md 里找不到「{OverBudgetDetailHeading}」这一节：行高预算的判定细则没有落点，"
            + "答辩当天「超了算不算过」只能现编。");
        var rest = notes[(start + OverBudgetDetailHeading.Length)..];
        var end = rest.IndexOf("\n### ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>
    /// 细则里念的那两个预算数必须等于读数记录里跑出来的那两个（第二百三十一轮，简报 P0-1）。
    /// <para>
    /// <b>要解决的问题</b>：细则写着「要比的那个数就取同一行报出来的那一个（模拟器那一跑是
    /// 125% 档 50px、200% 档 65px）」——这句话是答辩当天唯一能照着念的预算数，而它此前<b>只有报告原文可查</b>：
    /// 报告落在 <c>/tmp/ah-demo/report-&lt;档位&gt;.txt</c>，跑完就没了，下一轮谁也没法回头核这两个数还是不是那两个。
    /// 第二百三十一轮让两支演示脚本把这两个数（连同过预算的行数 / 条数）写进入库的读数记录，
    /// 于是「文档念的数」与「跑出来的数」第一次能逐字对上。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 每一档都有一行 <c>overbudget=</c>，档位集合与 <c>tier=</c> 那批逐档相同（缺一档即红）；
    /// ② 同一档的 <c>formbudget</c> 与 <c>hintbudget</c> 必须相等——预算只有
    /// <c>ProfileFormLayout.MaxRowHeight</c> 一处算式，两个数不等说明有人抄了第二份；
    /// ③ 细则里那两个数（<c>125% 档 Npx、200% 档 Mpx</c>）必须逐字等于记录里 120 / 192 两档的预算；
    /// ④ 两支演示脚本都得写这个键（只改一支，另一支跑出来的记录会被这条门判成缺档）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判过预算的行数是不是 0</b>——那一件从第二百三十五轮起由本文件
    /// <see cref="OverBudgetReading_ConfigFormRows_AreZeroAtEveryTier"/> 判，本条只管「细则念的那两个数」有出处、
    /// 且与出处一致；也不判真机跑没跑（真机 0 台，G-D5 仍是 <c>[ ]</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void OverBudgetReading_MatchesTheNumbersTheDefenseNarrativeReads()
    {
        var record = ReadingRecord();

        // ① 每档一行，档位集合与 tier= 那批相同。
        var tierSet = record.Tiers.Select(t => t.Tier).OrderBy(n => n).ToList();
        var overSet = record.OverBudget.Select(o => o.Tier).OrderBy(n => n).ToList();
        Assert.True(overSet.Count == tierSet.Count && overSet.SequenceEqual(tierSet),
            $"{ReadingPath} 的 overbudget= 档位（{string.Join(" / ", overSet)}）与 tier= 那批"
            + $"（{string.Join(" / ", tierSet)}）不是同一批：行高预算那一格缺档时，细则里念的数就没有出处。"
            + "补一次：bash .winecheck/demo.sh");

        // ② 同一档两个预算必须相等（算式只有一处）。
        foreach (var run in record.OverBudget)
        {
            Assert.True(run.FormBudget == run.HintBudget,
                $"{run.Tier} 档的配置表单预算 {run.FormBudget}px 与提示行预算 {run.HintBudget}px 不相等："
                + "预算只有 Desktop/Native/ProfileFormLayout.cs 的 MaxRowHeight 一处算式，"
                + "两个数不等说明有人抄了第二份预算。");
        }

        // ③ 细则里念的那两个数 == 记录里 120 / 192 两档的预算。
        var detail = OverBudgetDetailSection();
        var stated = Regex.Match(detail, @"125% 档 (?<a>\d+)px、200% 档 (?<b>\d+)px");
        Assert.True(stated.Success,
            "「行高预算判定（细则）」里找不到「125% 档 Npx、200% 档 Mpx」这两个数："
            + "答辩当天要比的那个数没有落点（这一段是唯一能照着念的预算数）。");
        var stated125 = int.Parse(stated.Groups["a"].Value);
        var stated200 = int.Parse(stated.Groups["b"].Value);

        var run125 = record.OverBudget.FirstOrDefault(o => o.Tier == 120);
        var run200 = record.OverBudget.FirstOrDefault(o => o.Tier == 192);
        Assert.True(run125 is not null && run200 is not null,
            $"{ReadingPath} 里没有 120 / 192 两档的 overbudget= 行（现有 {string.Join(" / ", overSet)}）："
            + "细则念的正是这两档的预算。");
        Assert.True(run125!.FormBudget == stated125,
            $"细则写「125% 档 {stated125}px」，读数记录里 120 档跑出来的是 {run125.FormBudget}px："
            + "文档念的数与跑出来的数不是同一个——改文档，或者重跑 bash .winecheck/demo.sh。");
        Assert.True(run200!.FormBudget == stated200,
            $"细则写「200% 档 {stated200}px」，读数记录里 192 档跑出来的是 {run200.FormBudget}px："
            + "文档念的数与跑出来的数不是同一个——改文档，或者重跑 bash .winecheck/demo.sh。");

        // ④ 两支演示脚本都得写这个键（只改一支 = 另一支跑出来的记录缺档）。
        foreach (var script in new[] { ".winecheck/demo.sh", ".winecheck/demo.ps1" })
        {
            var text = Repo.Source(script.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(text.Contains("overbudget=tier=", StringComparison.Ordinal),
                $"{script} 不写 overbudget= 这一行：用它跑出来的读数记录会缺行高预算那一格，"
                + "而细则念的那两个数就只剩一支脚本能产出。");
        }
    }

    /// <summary>
    /// 入库读数里「配置表单超预算行数」<b>三档必须全为 0</b>（第二百三十五轮，简报 P0-1 的门）。
    /// <para>
    /// <b>补的是哪个缺口</b>：这条数在第二百三十一轮进门时是<b>只登记不判</b>的——上一条门管「细则念的那两个
    /// 预算数」有出处，而「过预算的行数」当时是 0、却没有任何东西拦住它变成非 0。第二百三十四轮量到根因
    /// （标签列 120px 不随字号放大，125% 档破 4 行、200% 档破 10 行），第二百三十五轮把它改成随字号放大
    /// （<c>ProfileGridColumns.ScaledLabelWidth</c> 与 <c>Ui.cs</c> 的 <c>FieldGridPanel</c> 接线）。
    /// 改完不立门，下一轮谁把那处接线改回固定像素，三档读数照样全绿——因为<b>这一个上下文</b>（慢一档，
    /// <c>demo.sh</c> 一跑）里行高不随字体放大，超没超都量不到；真 DPI 上下文那一半由
    /// <c>DemoTierSwitchTests.PostFixOverbudgetReading_KeepsTheLabelColumnTiersAtZero</c> 判 README 那一段。
    /// 两道门各判一个上下文，合起来才是「三档归零」这句话的出处。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判 <c>hint=</c>（提示行那一列是第二百三十四轮登记的另一个决定项：预算只按正文档算，
    /// 两行提示进不进预算还没定案），也不判真机（真机 0 台，G-D5 仍是 <c>[ ]</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void OverBudgetReading_ConfigFormRows_AreZeroAtEveryTier()
    {
        var record = ReadingRecord();
        Assert.True(record.OverBudget.Count > 0,
            $"{ReadingPath} 里没有 overbudget= 行：这一门就没有可判的数，补一次 bash .winecheck/demo.sh。");
        foreach (var run in record.OverBudget)
        {
            Assert.True(run.Form == 0,
                $"{run.Tier} 档的配置表单超预算行数是 {run.Form}（应为 0）：那一跑里 {run.FormBudget}px 的预算被"
                + $"{run.Form} 行破掉，甲组那一族（标签列不随字号放大）又回来了。"
                + "查 Desktop/Native/ProfileGridColumns.cs 的 ScaledLabelWidth 与 Ui.cs 的 FieldGridPanel 接线，"
                + "改完重跑 bash .winecheck/demo.sh（三档跑完它自己写回原位）。");
        }
    }

    // ---------- 第二百五十九轮（简报 P0-1）：读数对应哪份源码，第一次可判 ----------

    /// <summary>
    /// 读数记录里的 <c>source=</c> 必须等于<b>当前源码</b>的指纹（第二百五十九轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>.winecheck/reading.txt</c> 的 <c>source=</c> 记的是「那份三档读数出自哪一版源码」，
    /// 而此前<b>没有任何东西判它还是不是当前源码</b>——第二百一十四轮是人工复算了一遍
    /// （<c>53143628…</c> 与记录一字相同，于是判定「不需要重跑」），那一轮是对的，但它不是判据：
    /// 下一轮没人复算，一份过期的读数照样全绿，而答辩现场念的正是那几个数。
    /// 简报把这一条写成「<c>source=</c> 必须是当前 HEAD 或其后代可达的 commit」——本仓的 <c>source=</c>
    /// <b>不是 commit SHA，是源码内容指纹</b>（<c>demo.sh</c> 的 <c>source_fingerprint()</c> 写出），
    /// 所以等价的判据是「<c>source=</c> 必须等于当前源码算出来的那个指纹」，比 commit 更严：
    /// 同一版源码里改一个字节，指纹就变，而 commit 不变。
    /// </para>
    /// <para>
    /// <b>它推翻了本文件第二百零七轮写下的一句取舍，理由要写清</b>：那一轮在
    /// <see cref="ReadingRecord_NamesItsScriptToolDateSourceAndKeepsTheSimulatorCaliber"/> 的注释里写着
    /// 「不判这份记录比当前源码新……把同一条判据搬进 <c>dotnet test</c>，等于『改一行产品代码、没先跑一次 wine，
    /// 整个测试工程就红』——一道当场解不开的闸门比没有闸门更糟」。那句话在当时是对的：它防的是
    /// <b>把「跑没跑」也塞进测试</b>。但本轮要判的不是「跑没跑」，是「记录说的是哪一版源码」——
    /// 这一件事<b>不需要 wine</b>（纯文件哈希），而且<b>解得开</b>：失败信息里就写着那一条命令
    /// （<c>bash .winecheck/demo.sh</c>，三档跑完它自己写回原位）。真正解不开的闸门是「没有补救路径」，
    /// 不是「补救要跑一次演示」——本仓每一轮改产品代码本来就要跑那一次。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那份报告是不是真跑出来的（<c>reading.txt</c> 与报告原文都可以被手写，
    /// 门看不出来——这是「机器产出」与「真机证据」之间的固有边界，见本文件开头那段），
    /// 也不判真机跑没跑（真机 0 台，G-D5 仍是 <c>[ ]</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void ReadingRecord_SourceIsTheCurrentSource()
    {
        var record = ReadingRecord();
        var now = SourceFingerprint.OfRepo();
        var drift = SourceDrift(record.Source, now);
        Assert.True(drift is null, drift);
    }

    /// <summary>
    /// 「记录里的 <c>source=</c> 与当前源码对不上」这一件事的<b>唯一判法</b>（收成一处，
    /// 好让 <see cref="SourceDriftDetector_ReportsTheBreaksItMustCatch"/> 把每一发破坏各喂一遍）。
    /// 返回 null = 对得上；返回一句话 = 对不上，那句话就是红的时候念出来的那一句。
    /// </summary>
    private static string? SourceDrift(string recorded, string current)
    {
        if (string.IsNullOrWhiteSpace(recorded))
        {
            return $"{ReadingPath} 的 source= 是空的：读数没有说清自己出自哪一版源码。"
                + "补一次：bash .winecheck/demo.sh（三档跑完它自己写回原位）。";
        }

        if (!Regex.IsMatch(recorded, "^[0-9a-f]{40}$"))
        {
            return $"{ReadingPath} 的 source=「{recorded}」不是 40 位小写十六进制：它不是源码指纹，"
                + "这一份只能由 .winecheck/demo.sh 或 demo.ps1 写出（手改就是把出处变成自述）。";
        }

        if (!string.Equals(recorded, current, StringComparison.Ordinal))
        {
            return $"{ReadingPath} 的 source= 是 {Short(recorded)}，当前源码算出来是 {Short(current)}："
                + "读数对应的源码不是当前源码，请重跑 bash .winecheck/demo.sh 写回。"
                + "（这一条不判那份报告是不是真跑出来的，只判它说的是哪一版源码。）";
        }

        return null;
    }

    /// <summary>指纹的前 10 位（红的时候念出来够定位，又不至于把一整串塞进消息里）。</summary>
    private static string Short(string fingerprint) =>
        fingerprint.Length <= 10 ? fingerprint : fingerprint[..10] + "…";

    /// <summary>
    /// 上面那条判法<b>抓不抓得住</b>——四发破坏各喂一遍（第二百五十九轮）。
    /// <para>
    /// 只在真仓上判的门，抓不抓得住全靠那天有没有人想到去试；本仓对每一条判据都跑一遍反向，
    /// 为的就是让「它绿着」不等于「它什么都看不见」。
    /// </para>
    /// </summary>
    [Fact]
    public void SourceDriftDetector_ReportsTheBreaksItMustCatch()
    {
        var now = SourceFingerprint.OfRepo();

        // 前提：当前记录是干净的——下面每一发「变红」才只可能来自注入的破坏。
        Assert.Null(SourceDrift(now, now));

        // ① 一个不存在的指纹：形状对（40 位小写十六进制）、内容不对。
        var nonexistent = SourceDrift(new string('0', 40), now);
        Assert.NotNull(nonexistent);
        Assert.Contains("读数对应的源码不是当前源码", nonexistent, StringComparison.Ordinal);
        Assert.Contains("bash .winecheck/demo.sh", nonexistent, StringComparison.Ordinal);

        // ② 另一批源码的指纹：临时小树上改一个字节就算出另一个值——这正是「改了产品代码没重跑」那一发。
        var other = SourceDrift(FingerprintOfTempTree(edited: true), now);
        Assert.NotNull(other);
        Assert.Contains("读数对应的源码不是当前源码", other, StringComparison.Ordinal);

        // ③ 空值：读数没有说清出自哪一版源码。
        Assert.NotNull(SourceDrift("", now));

        // ④ 不是指纹（截断的 SHA）：形状就不对，不许被当成「对得上」。
        Assert.NotNull(SourceDrift(now[..8], now));
    }

    /// <summary>
    /// 指纹本身的两条性质：<b>改一个字节就变</b>、<b>bin / obj / 测试工程不参与</b>（第二百五十九轮）。
    /// <para>
    /// 第二条是 <c>demo.sh</c> 那五行里最容易读漏的一处：剪掉 <c>ApifoxHelper.Tests/</c> 是刻意的
    /// （测试工程不编进冒烟产物，改了它不该要求重发冒烟器），剪掉 <c>bin</c> / <c>obj</c> 也是刻意的
    /// （产物不是源码）。这两条一旦被改掉，指纹就会在「只改了测试」时也变——那时
    /// <see cref="ReadingRecord_SourceIsTheCurrentSource"/> 会红在一个与读数无关的改动上。
    /// </para>
    /// </summary>
    [Fact]
    public void SourceFingerprint_ChangesWithOneByteAndIgnoresBinObjAndTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ah-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Desktop"));
            File.WriteAllText(Path.Combine(dir, "Program.cs"), "class A { }");
            File.WriteAllText(Path.Combine(dir, "Desktop", "B.cs"), "class B { }");

            var clean = SourceFingerprint.Of(dir);
            Assert.Equal(clean, SourceFingerprint.Of(dir)); // 同一棵树算两次必须同值

            // ① 改一个字节就变。
            File.WriteAllText(Path.Combine(dir, "Desktop", "B.cs"), "class B { int x; }");
            var edited = SourceFingerprint.Of(dir);
            Assert.NotEqual(clean, edited);

            // ② bin / obj / 测试工程里的 .cs 不参与（塞进去指纹不动）。
            foreach (var ignored in new[] { "bin", "obj", Path.Combine("ApifoxHelper.Tests", "bin") })
            {
                var sub = Path.Combine(dir, ignored);
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, "Ignored.cs"), "class Ignored { }");
            }

            Directory.CreateDirectory(Path.Combine(dir, "ApifoxHelper.Tests"));
            File.WriteAllText(Path.Combine(dir, "ApifoxHelper.Tests", "SomeTests.cs"), "class T { }");
            Assert.Equal(edited, SourceFingerprint.Of(dir));

            // ③ 新增一个产品源码文件就变（不是只认已有文件的内容）。
            File.WriteAllText(Path.Combine(dir, "Desktop", "C.cs"), "class C { }");
            Assert.NotEqual(edited, SourceFingerprint.Of(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* 临时目录清不掉不影响判据 */ }
        }
    }

    /// <summary>一棵临时小树算出来的指纹（<paramref name="edited"/> 决定那个文件是原样还是改过一个字节）。</summary>
    private static string FingerprintOfTempTree(bool edited)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ah-fp-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "Desktop"));
            File.WriteAllText(Path.Combine(dir, "Program.cs"), "class A { }");
            File.WriteAllText(Path.Combine(dir, "Desktop", "B.cs"), edited ? "class B { int x; }" : "class B { }");
            return SourceFingerprint.Of(dir);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (IOException) { /* 同上 */ }
        }
    }

    /// <summary>
    /// C# 这一份指纹与 <c>demo.sh</c> 那一份<b>逐字相同</b>（第二百五十九轮）。
    /// <para>
    /// <b>为什么必须真跑一遍 shell</b>：两份实现各自「自洽」是最难发现的一类漂——C# 这边改了剪枝规则，
    /// 它自己算得稳稳的，而 <c>reading.txt</c> 是 shell 那一支写的，于是
    /// <see cref="ReadingRecord_SourceIsTheCurrentSource"/> 会红在一个其实没改过源码的仓上。
    /// 这一条把 <c>demo.sh</c> 里那个函数<b>原文取出来</b>交给 bash 跑（不在测试里抄第三份管道），
    /// 与本类算出来的值逐字比。
    /// </para>
    /// <para>
    /// <b>判不到的那台机器</b>：没有 bash 时这一条直接返回（同
    /// <c>DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere</c> 的做法）。那时
    /// <see cref="ReadingRecord_SourceIsTheCurrentSource"/> 仍按 C# 这一份判——它判的是「记录与当前源码」，
    /// 与「两份实现是否一致」是两件事，后者判不到就说判不到。
    /// </para>
    /// </summary>
    [Fact]
    public void SourceFingerprint_AgreesWithTheShellOneWhenBashIsHere()
    {
        var bash = RealMachineEvidenceTests.LocateBash();
        if (bash is null) return; // 这台机器上没有 bash：判不到，不静默绿也不假装判过。

        var demo = Repo.Source(".winecheck", "demo.sh");
        var function = Regex.Match(demo, @"(?ms)^source_fingerprint\(\) \{.*?^\}");
        Assert.True(function.Success,
            "取法退化：.winecheck/demo.sh 里找不到 source_fingerprint() 那个函数——"
            + "这一条门就失去了「与 shell 那一支逐字比」的对象。");

        Assert.Equal(ShellFingerprint(bash, function.Value, Repo.Root), SourceFingerprint.OfRepo());
    }

    /// <summary>
    /// 把 <c>demo.sh</c> 里那个 <c>source_fingerprint()</c> 原文交给 bash 跑一遍，返回它算出来的值。
    /// <para>
    /// <b>为什么原文取出来跑</b>：测试里抄第三份管道，就等于让「两份实现是否一致」这条门自己也有第三份口径。
    /// </para>
    /// </summary>
    private static string ShellFingerprint(string bash, string function, string repo)
    {
        var psi = new ProcessStartInfo(bash)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Repo.Root,
        };
        psi.ArgumentList.Add("-s");
        psi.Environment["LANG"] = "C.UTF-8";

        using var process = Process.Start(psi)!;
        process.StandardInput.Write($"REPO={repo}\n{function}\nsource_fingerprint\n");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "demo.sh 的 source_fingerprint() 跑了 120 秒还没退出——它只该读一遍源码树。");

        Assert.True(process.ExitCode == 0,
            $"demo.sh 的 source_fingerprint() 退出码 {process.ExitCode}：{error.Trim()}");
        return output;
    }

    /// <summary>
    /// <b>同一版源码在两个不同路径下算出同一个指纹</b>（第三百九十九轮，简报 P0）。
    /// <para>
    /// <b>补的是哪个缺口</b>：旧取法把 <c>sha1sum</c> 打出来的<b>绝对路径</b>一起哈希进去，于是
    /// <c>/opt/ApifoxHelper</c> 与 <c>/tmp/ah-cold</c> 各得一个指纹——同一版源码、换一个 clone 路径，
    /// <c>preflight.sh</c> 第 ② 条就判「读数属于上一版代码」。答辩现场换一台机器 clone 一次即红，
    /// 而源码一个字节没变。这一条把「指纹与 clone 路径无关」变成机械可判。
    /// </para>
    /// <para>
    /// <b>为什么用两棵临时小树而不是两次 clone</b>：判的是<b>算法</b>与路径无关，不是这个仓有多大。
    /// 两棵树内容逐字相同、路径不同（名字长度也不同，绝对路径的字节数因此不同），并且各自带一份
    /// <c>bin/</c>、<c>obj/</c> 与 <c>ApifoxHelper.Tests/</c>——剪枝规则也一起判到。
    /// 旧算法在这两棵树上必然算出两个值（绝对路径不同），所以这条门在磁盘变异实测下会红。
    /// </para>
    /// <para>
    /// <b>判三件事</b>：① C# 这一份两棵树同值；② shell 那一份两棵树同值；③ 两份实现同值
    /// （同一棵树）。没有 bash 的机器上 ②③ 判不到，只判 ①——判不到就说判不到，不静默绿。
    /// </para>
    /// </summary>
    [Fact]
    public void SourceFingerprint_IsTheSameForTheSameSourceAtTwoDifferentPaths()
    {
        var a = TempSourceTree("ah-fp-path-a-");
        var b = TempSourceTree("ah-fp-path-b-with-a-longer-name-");
        try
        {
            // ① C# 这一份：两棵树同值。
            Assert.Equal(SourceFingerprint.Of(a), SourceFingerprint.Of(b));

            var bash = RealMachineEvidenceTests.LocateBash();
            if (bash is null) return; // 这台机器上没有 bash：②③ 判不到，不静默绿也不假装判过。

            var demo = Repo.Source(".winecheck", "demo.sh");
            var function = Regex.Match(demo, @"(?ms)^source_fingerprint\(\) \{.*?^\}");
            Assert.True(function.Success,
                "取法退化：.winecheck/demo.sh 里找不到 source_fingerprint() 那个函数——"
                + "这一条门就失去了「两个路径各算一次」的对象。");

            // ② shell 那一份：两棵树同值（旧算法在这里红——绝对路径进了哈希）。
            var shellA = ShellFingerprint(bash, function.Value, a);
            var shellB = ShellFingerprint(bash, function.Value, b);
            Assert.Equal(shellA, shellB);

            // ③ 两份实现同值：C# 与 shell 只有一处能改。
            Assert.Equal(shellA, SourceFingerprint.Of(a));
        }
        finally
        {
            foreach (var dir in new[] { a, b })
            {
                try { Directory.Delete(dir, true); } catch (IOException) { /* 临时目录清不掉不影响判据 */ }
            }
        }
    }

    /// <summary>
    /// 一棵临时小树：内容固定、路径由调用方给（两棵树内容逐字相同，只有所在路径不同）。
    /// 带 <c>bin/</c>、<c>obj/</c> 与 <c>ApifoxHelper.Tests/</c> 各一份，剪枝规则一起判到。
    /// </summary>
    private static string TempSourceTree(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "Desktop", "Native"));
        Directory.CreateDirectory(Path.Combine(dir, "Api"));
        Directory.CreateDirectory(Path.Combine(dir, "bin", "Debug"));
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        Directory.CreateDirectory(Path.Combine(dir, "ApifoxHelper.Tests"));
        File.WriteAllText(Path.Combine(dir, "Program.cs"), "class A { }");
        File.WriteAllText(Path.Combine(dir, "Api", "ApiRoutes.cs"), "class R { }");
        File.WriteAllText(Path.Combine(dir, "Desktop", "Native", "Ui.cs"), "class U { }");
        // 下面三份必须被剪掉：进了哈希就说明剪枝规则坏了（那时两棵树仍同值，但值会与仓里那一份不同）。
        File.WriteAllText(Path.Combine(dir, "bin", "Debug", "Generated.cs"), "class G { }");
        File.WriteAllText(Path.Combine(dir, "obj", "Temp.cs"), "class T { }");
        File.WriteAllText(Path.Combine(dir, "ApifoxHelper.Tests", "Tests.cs"), "class X { }");
        return dir;
    }

    // ---------- 第二百八十五轮（简报 P0-2）：散文里那个指纹与文件里那个数 ----------

    /// <summary>
    /// <c>docs/NOTES.md</c> 顶部「到期日核对」那一段（含它后面续写的各行，到下一个 <c>&gt; **</c> 条目为止）。
    /// <para>
    /// <b>为什么不能只取一行</b>：那一段在仓里是「一行 blockquote + 若干续写行」的形状（第二百八十一轮、
    /// 第二百八十五轮那两段都没有 <c>&gt;</c> 前缀），只取一行的话，续写行里的指纹就落在判据外面——
    /// 而「散文里那个数不是文件里那个数」正是这一条门要挡的形状。
    /// </para>
    /// </summary>
    private static string ExpiryCheckBlock()
    {
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("> **到期日核对", StringComparison.Ordinal));
        Assert.True(start >= 0,
            "docs/NOTES.md 顶部找不到「> **到期日核对…」那一行：到期日这件事只剩一条脚本，跑过之后结论落在哪里没人说。");
        var end = start + 1;
        while (end < lines.Length)
        {
            var trimmed = lines[end].Trim();
            if (trimmed.StartsWith('>') && trimmed.Length > 1) break;
            end++;
        }

        return string.Join('\n', lines[start..end]);
    }

    /// <summary>散文里写指纹的形状：10 位小写十六进制 + 省略号（正文里一律只念前 10 位，全串只在 <c>reading.txt</c> 里）。</summary>
    private static readonly Regex ProseFingerprint = new(@"(?<![0-9a-f])[0-9a-f]{10}…", RegexOptions.Compiled);

    /// <summary>
    /// 判据本体：一段散文 + 读数记录里的 <c>source=</c> 进，问题出（<c>null</c> = 对得上）。
    /// <para>
    /// <b>只判最后那一处</b>：那一段按时间顺序往后记，最后出现的那一个指纹就是当前值；此前各段里的旧指纹
    /// 是各自那一跑的读数，本来就该各不相同——把它们也拉平，等于把历史读数改坏（与本文件第一条门同一边界）。
    /// </para>
    /// </summary>
    private static string? ProseFingerprintDrift(string block, string recorded)
    {
        var matches = ProseFingerprint.Matches(block);
        if (matches.Count == 0)
        {
            return "「到期日核对」那一段里一个指纹都没有：读数记录里那个 source= 在散文里没有落点，"
                + "读到这一段的人分不清「当前源码是哪一版」与「上一次跑的是哪一版」。"
                + "补一句：把 .winecheck/reading.txt 的 source= 前 10 位写进那一段（并写清它是哪一轮那一跑的）。";
        }

        var last = matches[^1].Value.TrimEnd('…');
        if (!Regex.IsMatch(recorded, "^[0-9a-f]{40}$"))
        {
            return $"{ReadingPath} 的 source=「{recorded}」不是 40 位小写十六进制：它不是源码指纹，"
                + "这一份只能由 .winecheck/demo.sh 或 demo.ps1 写出（手改就是把出处变成自述）。";
        }

        var current = recorded[..10];
        if (!string.Equals(last, current, StringComparison.Ordinal))
        {
            return $"「到期日核对」那一段最后出现的指纹是 {last}…，而 {ReadingPath} 的 source= 是 {current}…："
                + "散文里那个数与文件里那个数不是同一个。要么重跑 bash .winecheck/demo.sh 让脚本自己写回读数记录、"
                + "再把那一段最后那一处改成它现读的值；要么把散文里那一处改回记录里的值——"
                + "别让「当前源码是哪一版」在同一个仓里有两个答案。";
        }

        return null;
    }

    /// <summary>
    /// 「到期日核对」那一段最后出现的指纹必须逐字等于读数记录的 <c>source=</c>（第二百八十五轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百八十四轮改了产品源码、重跑了三档，<c>reading.txt</c> 的 <c>source=</c>
    /// 由脚本自己重烧成 <c>bb04988266…</c>，而顶部那一段最后写的还是 <c>510a0bdab3…</c>——
    /// <b>散文没跟着写回</b>。本文件已有的几条门判的是「N 通过 / 0 失败 ×3」与段集合，
    /// <b>判不到散文里那个 10 位指纹</b>：这正是历史上反复出现的「散文里那个数不是文件里那个数」的形状。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那份报告是不是真跑出来的（<c>reading.txt</c> 与报告原文都可以被手写，门看不出来），
    /// 不判真机跑没跑（真机 0 台，G-D5 仍是 <c>[ ]</c>），也不判那一段里其余各处的旧指纹——
    /// 那些是各自那一跑的读数，留在原处才对。
    /// </para>
    /// </summary>
    [Fact]
    public void CurrentSourceFingerprint_MatchesTheReadingRecord()
    {
        var record = ReadingRecord();
        var drift = ProseFingerprintDrift(ExpiryCheckBlock(), record.Source);
        Assert.True(drift is null, drift);
    }

    /// <summary>
    /// 上面那条判法<b>抓不抓得住</b>——四发破坏各喂一遍（第二百八十五轮）。
    /// <para>
    /// 只在真仓上判的门，抓不抓得住全靠那天有没有人想到去试；本仓对每一条判据都跑一遍反向，
    /// 为的就是让「它绿着」不等于「它什么都看不见」。
    /// </para>
    /// </summary>
    [Fact]
    public void ProseFingerprintDetector_ReportsTheBreaksItMustCatch()
    {
        var current = new string('b', 40);
        var older = new string('a', 40);

        // 前提：最后那一处等于记录时是干净的——下面每一发「变红」才只可能来自注入的破坏。
        Assert.Null(ProseFingerprintDrift($"…由 `{older[..10]}…` 重烧成 `{current[..10]}…`——这是一版新读数。", current));

        // ① 散文最后那一处是旧指纹（第二百八十四轮那一发的原样）：红，且两处字面都打出来。
        var stale = ProseFingerprintDrift($"…`source=` 由 `{older[..10]}…` 重烧成 `{current[..10]}…`。", older);
        Assert.NotNull(stale);
        Assert.Contains($"{current[..10]}…", stale, StringComparison.Ordinal);
        Assert.Contains($"{older[..10]}…", stale, StringComparison.Ordinal);
        Assert.Contains("bash .winecheck/demo.sh", stale, StringComparison.Ordinal);

        // ② 一个指纹都没有：读数记录里的 source= 在散文里没有落点。
        var none = ProseFingerprintDrift("这一段只说「真机仍 0 台」，一个指纹都没写。", current);
        Assert.NotNull(none);
        Assert.Contains("一个指纹都没有", none, StringComparison.Ordinal);

        // ③ 记录里的 source= 不是指纹（截断的 SHA）：形状就不对，不许被当成「对得上」。
        Assert.NotNull(ProseFingerprintDrift($"…`{current[..10]}…`。", current[..8]));

        // ④ 历史旧指纹排在前面、当前值在最后：不报（历史读数本来就该各不相同）。
        Assert.Null(ProseFingerprintDrift(
            $"…`{older[..10]}…` 是那一跑的读数；`{current[..10]}…` 是当前值。", current));
    }

    // ---------- 第三百轮（简报 P0-1）：读数与「整支跑通」之间第一次有可判的连线 ----------

    /// <summary>
    /// 「最近一次整支跑通」的记录 <c>.winecheck/defense.txt</c>——由 <c>defense-day.sh</c> 四步走完之后写出。
    /// <para>
    /// <b>为什么不写进 <c>reading.txt</c></b>（简报原话是「<c>reading.txt</c> 顶部加一行」）：那一份是
    /// <b>整份重写</b>的（<c>demo.sh</c> 与 <c>demo.ps1</c> 各自 <c>&gt;"$READING"</c> 一次写全），
    /// 而且它的解析器对没约定的键当场判红。把这一行放进去，就得同时改那两支脚本让它们「原样带过去」，
    /// 于是<b>单独跑一次 <c>demo.sh</c> 也会把整支跑通的记录抹掉</b>——那正是这一条门要防的「过期读数被当成
    /// 当前读数」的另一种形状（记录被抹掉之后，门只能判「没有记录」，判不出「记录是旧的」）。
    /// 放在同一目录的兄弟文件里，写它的只有一支脚本（<c>defense-day.sh</c>），读它的只有这一条门。
    /// </para>
    /// <para>
    /// <b>它补的是哪个缺口</b>：<see cref="ReadingRecord_SourceIsTheCurrentSource"/> 判的是
    /// 「<c>reading.txt</c> 的 <c>source=</c> 等于当前源码」——<b>单独跑一次 <c>demo.sh</c> 就能满足它</b>。
    /// 而到期日当天要念的那几个数，前提是「整支链（四步）在这一版源码上跑通过」：预检四条、三档冒烟、
    /// 逐屏对账、念序，任何一步没过，那份读数就不该被当成当天的读数。这一条把「整支跑通」写成一行
    /// 机器可读的记录，于是「上一版读数被当成当前读数」从「靠人读 NOTES 的考古层」变成机械可判。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那份记录是不是真跑出来的（与 <c>reading.txt</c> 同一条固有边界：
    /// 文件可以被手写，门看不出来），不判真机跑没跑（真机 0 台，G-D5 仍是 <c>[ ]</c>），
    /// 也不判 <c>defense-day.sh</c> 的四步各自对不对（那是 <c>WineCheckScriptsTests.DefenseDayScript_*</c> 的事）。
    /// </para>
    /// </summary>
    private const string DefenseRecordPath = ".winecheck/defense.txt";

    /// <summary>一份「整支跑通」记录（来历三行）。</summary>
    private sealed record DefenseRecord(string Script, string Date, string Source);

    /// <summary>
    /// 读那份记录。缺文件即红并给出「怎么补出来」的一条命令——与 <see cref="ReadingRecord"/> 同一口径：
    /// 这份记录是跑出来的，不在磁盘上就说明整支链没跑过（或有人删了它）。
    /// </summary>
    private static DefenseRecord DefenseRecordFile()
    {
        var path = Path.Combine(Repo.Root, DefenseRecordPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(path),
            $"{DefenseRecordPath} 不在——「最近一次整支跑通的是哪一版源码」没有落点，"
            + "于是「上一版读数被当成当前读数」只能靠人读 docs/NOTES.md 的考古层。"
            + "补一次：PUBLISH=1 bash .winecheck/defense-day.sh（四步走完它自己写出这一份）。");

        var script = ""; var date = ""; var source = "";
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
                case "date": date = value; break;
                case "source": source = value; break;
                default:
                    Assert.Fail($"{DefenseRecordPath} 出现没有约定的键 {key}（这一份只能由 .winecheck/defense-day.sh 写出，"
                        + "手改就是把出处变成自述）：{line}");
                    break;
            }
        }

        return new DefenseRecord(script, date, source);
    }

    /// <summary>
    /// 「读数记录与整支跑通记录不是同一版源码」这一件事的<b>唯一判法</b>（收成一处，好让
    /// <see cref="DefenseRecordDetector_ReportsTheBreaksItMustCatch"/> 把每一发破坏各喂一遍）。
    /// 返回 null = 对得上；返回一句话 = 对不上，那句话就是红的时候念出来的那一句。
    /// </summary>
    private static string? DefenseDrift(string readingSource, string defenseSource, string defenseDate)
    {
        if (string.IsNullOrWhiteSpace(defenseSource))
        {
            return $"{DefenseRecordPath} 的 source= 是空的：整支跑通记录没有说清自己出自哪一版源码。"
                + "补一次：PUBLISH=1 bash .winecheck/defense-day.sh。";
        }

        if (!Regex.IsMatch(defenseSource, "^[0-9a-f]{40}$"))
        {
            return $"{DefenseRecordPath} 的 source=「{defenseSource}」不是 40 位小写十六进制：它不是源码指纹，"
                + "这一份只能由 .winecheck/defense-day.sh 写出（手改就是把出处变成自述）。";
        }

        if (!Regex.IsMatch(defenseDate, @"^\d{4}-\d{2}-\d{2}$"))
        {
            return $"{DefenseRecordPath} 的 date=「{defenseDate}」不是 YYYY-MM-DD：读它的人不知道整支链是哪一天跑通的。";
        }

        if (!string.Equals(readingSource, defenseSource, StringComparison.Ordinal))
        {
            return $"{ReadingPath} 的 source= 是 {Short(readingSource)}，而最近一次整支跑通（{defenseDate}）的是 "
                + $"{Short(defenseSource)}：最近一次 defense-day.sh 跑的不是当前源码——"
                + "那份读数出自另一版源码，不能当成当天的读数念。重跑 PUBLISH=1 bash .winecheck/defense-day.sh。"
                + "（这一条不判那份报告是不是真跑出来的，只判「整支跑通」与「读数」是不是同一版源码。）";
        }

        return null;
    }

    /// <summary>
    /// 读数记录与整支跑通记录必须是<b>同一版源码</b>（第三百轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="ReadingRecord_SourceIsTheCurrentSource"/> 只判「读数等于当前源码」，
    /// 而 <c>demo.sh</c> 单独跑一次就满足它——<b>整支链（预检四条 + 三档 + 逐屏对账 + 念序）有没有在这一版
    /// 源码上跑通过，此前没有任何东西判</b>。到期日当天念的正是那几个数，而「上一次整支跑通是上一版源码」
    /// 这件事只有人读 NOTES 顶部那一段上千字的考古层才看得出来。
    /// </para>
    /// <para>
    /// <b>两个方向都能红</b>：整支跑通记录不在 → 红（没有落点）；在但与读数不是同一版源码 → 红（就是本轮要挡的那件事）。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseRecord_SourceIsTheSameSourceAsTheReadingRecord()
    {
        var reading = ReadingRecord();
        var defense = DefenseRecordFile();

        Assert.True(defense.Script == "defense-day.sh",
            $"{DefenseRecordPath} 的 script= 是「{defense.Script}」——这一份只能由 .winecheck/defense-day.sh 写出。");

        var drift = DefenseDrift(reading.Source, defense.Source, defense.Date);
        Assert.True(drift is null, drift);
    }

    /// <summary>
    /// 上面那条判法<b>抓不抓得住</b>——四发破坏各喂一遍（第三百轮）。
    /// <para>
    /// 只在真仓上判的门，抓不抓得住全靠那天有没有人想到去试。四发分别是：整支跑通记录是上一版源码、
    /// 记录里的指纹不是指纹、日期不是日期、指纹是空的。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseRecordDetector_ReportsTheBreaksItMustCatch()
    {
        var current = new string('c', 40);
        var older = new string('a', 40);

        // 前提：同一版源码时是干净的——下面每一发「变红」才只可能来自注入的破坏。
        Assert.Null(DefenseDrift(current, current, "2026-11-14"));

        // ① 整支跑通记录停在上一版源码（本轮要挡的那件事）：红，两处字面都打出来，并给出补救命令。
        var stale = DefenseDrift(current, older, "2026-11-13");
        Assert.NotNull(stale);
        Assert.Contains($"{current[..10]}…", stale, StringComparison.Ordinal);
        Assert.Contains($"{older[..10]}…", stale, StringComparison.Ordinal);
        Assert.Contains("PUBLISH=1 bash .winecheck/defense-day.sh", stale, StringComparison.Ordinal);

        // ② 记录里的 source= 不是指纹（截断的 SHA）：形状就不对，不许被当成「对得上」。
        Assert.NotNull(DefenseDrift(current, current[..8], "2026-11-14"));

        // ③ 日期不是日期：读它的人不知道整支链是哪一天跑通的。
        Assert.NotNull(DefenseDrift(current, current, "昨天"));

        // ④ 指纹是空的：整支跑通记录没有说清自己出自哪一版源码。
        Assert.NotNull(DefenseDrift(current, "", "2026-11-14"));
    }

    /// <summary>
    /// 那一格记录必须由 <c>defense-day.sh</c> 自己写出，且落点跟着读数记录走（第三百轮）。
    /// <para>
    /// <b>为什么这条要单独判</b>：上面两条判的是「记录与读数是不是同一版源码」，它们<b>判不到记录是谁写的</b>——
    /// 一份手写的记录照样能让它们绿。这一条判的是「写它的那一支脚本里真有那几行」：落点从 <c>demo.sh</c> 的
    /// <c>READING</c> 缺省现读（不抄第二份路径）、指纹从第 ③ 步刚写回的那一份现读（不算第二份指纹算法）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判那几行跑起来对不对（那是 <c>WineCheckScriptsTests.DefenseDayScript_*</c> 与
    /// 到期日当天那一跑的事），也不判记录里的日期是不是今天。
    /// </para>
    /// </summary>
    [Fact]
    public void DefenseRecord_IsWrittenByTheDefenseDayScriptFromTheReadingRecord()
    {
        var script = Repo.Source(".winecheck", "defense-day.sh");

        Assert.Contains("defense.txt", script, StringComparison.Ordinal);
        Assert.Contains("source=$SRC", script, StringComparison.Ordinal);
        Assert.Contains(@"s/^READING=${READING:-\(.*\)}$/\1/p", script, StringComparison.Ordinal);
        Assert.Contains(@"s/^source=//p", script, StringComparison.Ordinal);

        // 落点不许在脚本里写死一份：它必须从 demo.sh 的 READING 缺省现读（与 OUT / TIERS 同一取法）。
        Assert.DoesNotContain(".winecheck/defense.txt", script, StringComparison.Ordinal);
    }
}
