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
}
