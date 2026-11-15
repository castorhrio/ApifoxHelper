using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 轮次账本不许断档（第二百六十一轮，控制仓简报 P0-1，具名 pass：harden + clarify）。
/// <para>
/// <b>要解决的问题</b>：<c>docs/NOTES.md</c> 的逐轮条目是答辩现场被追问「最近几轮做了什么」时唯一的索引。
/// 第二百五十五至二百六十轮各自提交了可交付切片，却<b>一轮都没写进账本</b>——账本从第二百五十四轮直接
/// 断到第二百六十一轮（第二百六十轮的 commit message 逐字承认「本轮未写」）。断档的代价不是「少几行字」：
/// 现场翻账本翻到第二百五十四轮就没了，最近六轮做过什么只能靠翻 <c>git log</c>，而 <c>git log</c> 不在
/// 答辩材料里。
/// </para>
/// <para>
/// <b>为什么以前没有门</b>：本仓已有的轮次相关门（<c>MeasuredScreensTests</c> 的「登记里说第 N 轮量过，
/// 而 NOTES 里查不到这一轮」）判的是<b>被引用的</b>轮次有没有落点，判不到「最近一轮有没有落点」——
/// 只要没人引用第二百六十轮，它缺席就没人红。这一条补的正是那个方向：<b>从最新一轮往回数</b>。
/// </para>
/// <para>
/// <b>判什么</b>：① 账本里最近 <see cref="RecentWindow"/> 轮（含最新一轮）每一轮都有一个
/// <c>## </c> 标题条目；② 账本的最新一轮<b>不落后于</b>代码里提到的最新一轮（测试 / 冒烟器 / 界面源码里
/// 带轮次标记的注释）——这一条是真正会抓住「写了 260 却漏了 258」与「一轮都没写」的那一条；
/// ③ 最近这几轮的条目里必须有「读数」与「本轮未做」两行（现场要问的就是这两件事：跑出来多少、
/// 还有什么没做）；④ 判据自己被抓得住（四发注入各报一处，合规行不报）。
/// </para>
/// <para>
/// <b>跳号与断档是两件事（第三百四十二轮补）</b>：本仓的轮次号从第三百三十三轮直接跳到第三百四十一轮——
/// 第三百三十四至三百四十轮<b>没有发生</b>（无提交、无账本条目、代码里也没有任何一处引用）。
/// 此前这条门把它们读成「断档」，于是 HEAD 上一直红着一条，而红的原因不是「漏记了一轮做过的事」，
/// 是「这七个数从来没被用过」。两种情形在答辩现场的含义完全不同：断档 = 翻不到做过什么，
/// 跳号 = 本来就没有那件事。所以 <c>docs/NOTES.md</c> 里有一行<b>跳号登记</b>（形如
/// 「跳号的是：第三百三十四轮、……、第三百四十轮。」），登记过的轮次不再报断档；
/// <b>登记之外的任何一轮缺席仍然红</b>，而「登记了却又有账本条目」同样红（死条目）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判条目写得对不对、也不判历史断档（第二百五十五轮之前本账本本来就有多处断档，
/// 那是历史事实，回填它们不是这一轮的事，也不该由一条门假装已经补上）；不判 commit 与账本是否一一对应
/// （测试进程不读 <c>git</c>——那会让这条门在无 <c>git</c> 的环境里静默绿）。
/// </para>
/// <para>
/// <b>夹具约定（写测试的人要守）</b>：要在夹具里用一个<b>不存在的</b>轮次时，取一个<b>比当前最新轮次小</b>
/// 的数（本仓既有做法是「第九十九轮」）。取一个更大的数会让这条门把夹具读成「有一轮没记账」——
/// 那不是误报，是这条门分不清「夹具」与「真的漏了」，而它宁可红。
/// </para>
/// </summary>
public class RoundLedgerTests
{
    /// <summary>最近几轮必须有账本条目。取 6：答辩现场被问「最近几轮」时，六轮足够答完一个迭代切片。</summary>
    private const int RecentWindow = 6;

    /// <summary>账本条目里必须出现的两行（现场要问的两件事：跑出来多少、还有什么没做）。</summary>
    private static readonly string[] RequiredLines = ["读数", "本轮未做"];

    private static string Notes => Repo.Source("docs", "NOTES.md");

    /// <summary>
    /// 账本里最近六轮都在，且账本不落后于代码里提到的最新一轮。
    /// </summary>
    [Fact]
    public void RecentRounds_AreAllInTheLedger()
    {
        var problems = Problems(Notes, CodeRoundNumbers(), RecentWindow);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。第一发是「中间漏一轮」，第二发是「账本落后于代码」，
    /// 第三发是「条目里没有读数」，第四发是合规账本（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void LedgerDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 中间漏一轮：账本写到 260，但 258 那一节被删了。
        var gap = Ledger(255, 256, 257, 259, 260);
        var gapProblems = Problems(gap, [260], RecentWindow);
        Assert.Contains(gapProblems, p => p.Contains("第二百五十八轮", StringComparison.Ordinal));
        Assert.Contains(gapProblems, p => p.Contains("断档", StringComparison.Ordinal));

        // ② 账本落后于代码：代码里已经提到第二百六十轮，账本最新只到第二百五十四轮。
        var stale = Ledger(249, 250, 251, 252, 253, 254);
        var staleProblems = Problems(stale, [260], RecentWindow);
        Assert.Contains(staleProblems, p => p.Contains("第二百六十轮", StringComparison.Ordinal)
                                            && p.Contains("落后", StringComparison.Ordinal));

        // ③ 条目里没有「读数」：六轮都在，但最新那一轮只写了标题。
        var noReadings = Ledger(255, 256, 257, 258, 259) + "## 第二百六十轮（2026-11-13）：只有标题\n\n- **本轮未做**：无。\n";
        var noReadingsProblems = Problems(noReadings, [260], RecentWindow);
        Assert.Contains(noReadingsProblems, p => p.Contains("读数", StringComparison.Ordinal));

        // ④ 合规账本：六轮都在、都有两行、账本不落后——一处都不报。
        Assert.Empty(Problems(Ledger(255, 256, 257, 258, 259, 260), [260], RecentWindow));

        // ⑤ 跳号登记过的轮次不报断档：账本写到 259，260 登记成「没发生」——一处都不报。
        var skipped = Ledger(255, 256, 257, 258, 259) + SkipLine(260);
        Assert.Empty(Problems(skipped, [259], RecentWindow));

        // ⑥ 登记了却又有账本条目 → 死条目，报。
        var deadEntry = Ledger(255, 256, 257, 258, 259, 260) + SkipLine(260);
        var deadProblems = Problems(deadEntry, [260], RecentWindow);
        Assert.Contains(deadProblems, p => p.Contains("第二百六十轮", StringComparison.Ordinal)
                                           && p.Contains("跳号", StringComparison.Ordinal));

        // ⑦ 登记只兜登记过的那几轮：登记 260、缺席的是 259，照样报 259（登记不是豁免）。
        var partial = Ledger(255, 256, 257, 258) + SkipLine(260);
        var partialProblems = Problems(partial, [260], RecentWindow);
        Assert.Contains(partialProblems, p => p.Contains("第二百五十九轮", StringComparison.Ordinal));
        Assert.DoesNotContain(partialProblems, p => p.Contains("第二百六十轮没有条目", StringComparison.Ordinal));
    }

    /// <summary>夹具：一行跳号登记（形状与 <c>docs/NOTES.md</c> 里那一行同一取法）。</summary>
    private static string SkipLine(params int[] rounds) =>
        "\n跳号的是：" + string.Join("、", rounds.Select(r => $"第{Chinese(r)}轮")) + "。\n";

    /// <summary>
    /// 中文轮次号的解析：账本里出现过的每一种写法都要读得出来。
    /// <para>
    /// 这一条单独存在，是因为上面两条判据全靠它——解析退化成「只认阿拉伯数字」时，
    /// 账本会被读成空集合，而「空集合里最近六轮都在」是一句永远为真的话（第二百零五轮
    /// 「相等在空集合上」的同一形态）。所以这里逐个钉死，并判「读不出来就抛，不静默返回 0」。
    /// </para>
    /// </summary>
    [Fact]
    public void ChineseRoundNumber_ParsesEveryFormTheLedgerUses()
    {
        Assert.Equal(4, RoundNumber("四"));
        Assert.Equal(10, RoundNumber("十"));
        Assert.Equal(23, RoundNumber("二十三"));
        Assert.Equal(111, RoundNumber("一百一十一"));
        Assert.Equal(172, RoundNumber("一百七十二"));
        Assert.Equal(199, RoundNumber("一百九十九"));
        Assert.Equal(202, RoundNumber("二百零二"));
        Assert.Equal(260, RoundNumber("二百六十"));
        Assert.Equal(999, RoundNumber("九百九十九"));

        // 读不出来就抛：静默返回 0 会让「最近六轮都在」在空集合上成立。
        Assert.ThrowsAny<Exception>(() => RoundNumber("甲"));
        Assert.ThrowsAny<Exception>(() => RoundNumber(""));
    }

    // ── 判据本体（纯函数：文本进、问题出） ──────────────────────────────

    /// <summary>
    /// 账本断档判据。返回问题清单，空清单 = 过得去。
    /// <para>
    /// 写成纯数据进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>LayoutMeasurementGateTests.Problems</c> / <c>DemoScriptTests.RowClaimMismatches</c> 同一形状。
    /// </para>
    /// </summary>
    /// <param name="notes">账本文本（<c>docs/NOTES.md</c> 全文）。</param>
    /// <param name="codeRounds">代码里提到过的轮次号（测试 / 冒烟器 / 界面源码的注释）。</param>
    /// <param name="window">最近几轮必须有条目。</param>
    private static List<string> Problems(string notes, IReadOnlyCollection<int> codeRounds, int window)
    {
        var problems = new List<string>();
        var entries = LedgerEntries(notes);
        var skips = RegisteredSkips(notes);
        if (entries.Count == 0)
        {
            problems.Add("docs/NOTES.md 里一个「## 第N轮」条目都读不出来——判据退化了，"
                         + "「最近六轮都在」会在空集合上成立（第二百零五轮「相等在空集合上」的同一形态）。");
            return problems;
        }

        var latest = entries.Keys.Concat(skips).Max();

        // ① 账本不落后于代码：代码里已经提到第 N 轮，账本最新一轮却还停在更早的地方。
        if (codeRounds.Count > 0)
        {
            var latestInCode = codeRounds.Max();
            if (latest < latestInCode)
            {
                problems.Add(
                    $"账本最新一轮是第{Chinese(latest)}轮，而代码里已经提到第{Chinese(latestInCode)}轮——"
                    + "账本落后于代码：那一轮做过的事在答辩现场翻不到（第二百五十五至二百六十轮就是这么断的）。");
            }
        }

        // ② 最近 window 轮每一轮都要有 ## 条目——除非它在跳号登记里（那一轮本来就没发生）。
        for (var round = latest - window + 1; round <= latest; round++)
        {
            if (!entries.ContainsKey(round) && !skips.Contains(round))
            {
                problems.Add(
                    $"账本最近 {window} 轮里第{Chinese(round)}轮没有条目（最新一轮是第{Chinese(latest)}轮）——"
                    + "轮次账本断档：现场被问「最近几轮做了什么」时，这一轮在账本里翻不到。"
                    + "（若这一轮本来就没发生，把它登记进 docs/NOTES.md 的「跳号的是：」那一行。）");
            }
        }

        // ②b 跳号登记不许留死条目：登记说这一轮没发生，账本里却有它的条目，两处必有一处是错的。
        foreach (var round in skips.Where(entries.ContainsKey).OrderBy(r => r))
        {
            problems.Add(
                $"第{Chinese(round)}轮被登记成「跳号、没发生」，账本里却有它的条目——"
                + "两处必有一处是错的：要么删掉那条登记，要么那一轮真的发生过、该按断档处理。");
        }

        // ③ 最近这几轮的条目里必须有「读数」与「本轮未做」。
        foreach (var (round, body) in entries.Where(e => e.Key > latest - window))
        {
            foreach (var line in RequiredLines)
            {
                if (!body.Contains(line, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"第{Chinese(round)}轮的条目里没有「{line}」——现场要问的就是这两件事"
                        + "（跑出来多少、还有什么没做），缺一行就答不上来。");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// 跳号登记：<c>docs/NOTES.md</c> 里「跳号的是：」之后、第一个句号之前点到的那些轮次。
    /// <para>
    /// <b>为什么要有这一格</b>：轮次号是<b>人写的</b>，它会跳号（第三百三十三轮之后直接是第三百四十一轮）。
    /// 没有这一格时，这条门把「跳号」读成「断档」，于是它红着，而红的原因不是漏记——一条永远红着的门
    /// 与没有门一样没用。登记的是<b>事实</b>（这七个数没被用过），不是豁免：登记之外的缺席照样红。
    /// </para>
    /// <para>
    /// <b>取法只有一处</b>：标记串 + 第一个句号，不认表格、不认列表——形状越窄，下一轮越不会「顺手」把
    /// 一个真的漏记的轮次塞进来。读不到标记时返回空集合（= 没有跳号登记，行为与加这一格之前一致）。
    /// </para>
    /// </summary>
    private static HashSet<int> RegisteredSkips(string notes)
    {
        var at = notes.IndexOf(SkipMarker, StringComparison.Ordinal);
        if (at < 0) return [];
        var rest = notes[(at + SkipMarker.Length)..];
        var end = rest.IndexOf('。');
        var segment = end < 0 ? rest : rest[..end];
        return Regex.Matches(segment, @"第(?<num>[零一二三四五六七八九十百]+)轮")
            .Select(m => RoundNumber(m.Groups["num"].Value))
            .ToHashSet();
    }

    /// <summary>跳号登记的标记串（<c>docs/NOTES.md</c> 里那一行的形状由它定）。</summary>
    private const string SkipMarker = "跳号的是：";

    /// <summary>
    /// 账本条目：<c>## </c> 标题行里<b>第一个</b>「第N轮」就是这一节的轮次，正文取到下一个 <c>## </c> 之前。
    /// <para>
    /// 只认第一个：标题里常引到别的轮次（「第二百五十六轮……（第二百五十五轮挂账的 P0-2）」），
    /// 把标题里所有轮次都算成「有条目」，会让「引一句就算写过」变成一条绕过账本的路。
    /// </para>
    /// </summary>
    private static Dictionary<int, string> LedgerEntries(string notes)
    {
        var lines = notes.Split('\n');
        var entries = new Dictionary<int, string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) continue;
            var match = Regex.Match(lines[i], @"第(?<num>[零一二三四五六七八九十百]+)轮");
            if (!match.Success) continue;
            var end = i + 1;
            while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal)) end++;
            var body = string.Join('\n', lines[(i + 1)..end]);
            var round = RoundNumber(match.Groups["num"].Value);
            // 同一轮出现两节时取正文更长的那一节：短的那一节多半是索引或摘录，不是条目本体。
            if (!entries.TryGetValue(round, out var existing) || body.Length > existing.Length)
            {
                entries[round] = body;
            }
        }

        return entries;
    }

    /// <summary>
    /// 代码里提到过的轮次号：测试工程、冒烟器与界面源码（<c>Desktop/</c>）里带轮次标记的注释。
    /// <para>
    /// 只扫这三处，不扫 <c>docs/</c>：文档里会用「第九百九十九轮」这种<b>故意不存在的</b>轮次当夹具
    /// （第二百一十四轮那一处变异实测），扫进来会把夹具读成「有一轮没记账」。
    /// </para>
    /// </summary>
    private static List<int> CodeRoundNumbers()
    {
        var roots = new[]
        {
            Path.Combine(Repo.Root, "ApifoxHelper.Tests"),
            Path.Combine(Repo.Root, "ApifoxHelper.Smoke"),
            Path.Combine(Repo.Root, "Desktop"),
        };
        var rounds = new List<int>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                // 本文件自己不算：它里面写着「第九百九十九轮」这种故意不存在的轮次当夹具
                // （见类注释「夹具约定」）。把判据自己的夹具读成「有一轮没记账」，这条门就永远红。
                if (Path.GetFileName(file) == "RoundLedgerTests.cs") continue;

                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"第(?<num>[零一二三四五六七八九十百]+)轮"))
                {
                    rounds.Add(RoundNumber(match.Groups["num"].Value));
                }
            }
        }

        return rounds;
    }

    // ── 中文数字 ──────────────────────────────────────────────────────

    private static readonly Dictionary<char, int> Digits = new()
    {
        ['零'] = 0, ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4,
        ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9,
    };

    /// <summary>
    /// 中文轮次号 → 整数（支持到九百九十九）。读不出来<b>抛</b>，不静默返回 0——
    /// 静默返回 0 会让「最近六轮都在」在空集合上成立。
    /// </summary>
    private static int RoundNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) throw new ArgumentException("轮次号是空的。", nameof(text));

        var total = 0;
        var rest = text;
        if (rest.Contains('百'))
        {
            var at = rest.IndexOf('百');
            total += (at == 0 ? 1 : Digit(rest[..at])) * 100;
            rest = rest[(at + 1)..];
        }

        if (rest.Contains('十'))
        {
            var at = rest.IndexOf('十');
            total += (at == 0 ? 1 : Digit(rest[..at])) * 10;
            rest = rest[(at + 1)..];
        }

        foreach (var ch in rest) total += Digit(ch.ToString());
        return total;
    }

    private static int Digit(string text)
    {
        var total = 0;
        foreach (var ch in text)
        {
            if (!Digits.TryGetValue(ch, out var value))
            {
                throw new ArgumentException($"「{text}」不是中文数字（读不出来的轮次号不许静默当成 0）。", nameof(text));
            }

            total += value;
        }

        return total;
    }

    /// <summary>整数 → 中文轮次号（只用于失败信息，读的人要能一眼对上账本里的写法）。</summary>
    private static string Chinese(int number)
    {
        if (number is < 0 or > 999) return number.ToString();
        var hundreds = number / 100;
        var tens = number % 100 / 10;
        var ones = number % 10;
        var text = new System.Text.StringBuilder();
        if (hundreds > 0)
        {
            if (hundreds > 1) text.Append(Digits.First(d => d.Value == hundreds).Key);
            text.Append('百');
        }

        if (tens > 0)
        {
            if (tens > 1) text.Append(Digits.First(d => d.Value == tens).Key);
            text.Append('十');
        }
        else if (hundreds > 0 && ones > 0)
        {
            text.Append('零');
        }

        if (ones > 0) text.Append(Digits.First(d => d.Value == ones).Key);
        return text.Length == 0 ? "零" : text.ToString();
    }

    /// <summary>夹具：造一份「这几轮都有条目、都有两行」的账本。</summary>
    private static string Ledger(params int[] rounds) =>
        string.Concat(rounds.Select(round =>
            $"## 第{Chinese(round)}轮（2026-11-13）：夹具条目\n\n"
            + $"- **读数**：夹具。\n- **本轮未做**：夹具。\n\n"));
}
