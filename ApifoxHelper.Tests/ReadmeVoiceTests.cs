using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// README 首屏的「人话」门（第一百九十轮，简报 P0-1：去 AI 味道）。
/// <para>
/// <b>要解决的问题</b>：评委第一次打开这个仓，看到的第一段是「工具做了 X」的说明书口吻，
/// 接着是功能清单与挂账清单——读起来像审计台账，不像一个人在介绍自己的工具。
/// 产品本身没问题，问题在<b>第一印象</b>：没有人、没有场景、没有「省了什么」。
/// </para>
/// <para>
/// <b>门判什么</b>：首屏（<c>#</c> 标题之后、第一个 <c>##</c> 之前）必须是<b>场景句</b>而不是说明书：
/// ① 首屏第一段就要出现具名角色（后端工程师 / 接口维护者 / 前端）与具名场景动词（改 / 对接 / 踩坑 / 核对）——
/// 说明书口吻的段落不许排在场景句前面；② 首屏不含 <see cref="CopyRules.BannedWords"/> 里的空话禁词；
/// ③ 首屏非空行 ≤ 8 行——超过 8 行就不是一句场景句，而是又一段说明书。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「写得好不好」。文字是否像真人在说话，只有人读了才知道；
/// 门只挡住三种可机械判定的退化：把人删掉、把场景删掉、把首屏重新堆成说明书。
/// 「已知限制」「写入安全」「性能设计」三节一个字都不动——挂账必须留。
/// </para>
/// </summary>
public class ReadmeVoiceTests
{
    /// <summary>具名角色词（封闭清单）：首屏第一段至少要出现一个，否则读者不知道这是给谁用的。</summary>
    private static readonly string[] RoleWords = ["后端工程师", "接口维护者", "前端"];

    /// <summary>具名场景动词（封闭清单）：首屏第一段至少要出现一个，否则读者不知道它替谁省了哪一步。</summary>
    private static readonly string[] SceneVerbs = ["改", "对接", "踩坑", "核对"];

    /// <summary>首屏非空行上限：超过就不是场景句，是又一段说明书。</summary>
    private const int MaxFirstScreenLines = 8;

    private static string Readme => Repo.Source("README.md");

    /// <summary>首屏：<c>#</c> 标题之后、第一个 <c>##</c> 之前的全部文本。</summary>
    private static string FirstScreen()
    {
        var lines = Readme.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("# ", StringComparison.Ordinal));
        Assert.True(start >= 0, "README.md 没有一级标题（首屏无从判定）。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.True(end > start, "README.md 首屏之后没有二级标题（首屏边界无从判定）。");
        return string.Join('\n', lines[(start + 1)..end]);
    }

    /// <summary>首屏的第一段（第一个空行之前的那一块）。</summary>
    private static string FirstParagraph()
    {
        var blocks = FirstScreen().ReplaceLineEndings("\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(blocks.Length > 0, "README.md 首屏是空的：标题之后直接就是二级标题。");
        return blocks[0].Trim();
    }

    [Fact]
    public void FirstParagraph_NamesARoleAndAScene()
    {
        var paragraph = FirstParagraph();
        var role = RoleWords.FirstOrDefault(w => paragraph.Contains(w, StringComparison.Ordinal));
        Assert.True(role is not null,
            $"README 首屏第一段没有具名角色（应出现 {string.Join(" / ", RoleWords)} 之一）："
            + $"读者读到的第一段是「{Excerpt(paragraph)}」——这是说明书口吻，不是场景句。");
        var verb = SceneVerbs.FirstOrDefault(w => paragraph.Contains(w, StringComparison.Ordinal));
        Assert.True(verb is not null,
            $"README 首屏第一段没有具名场景动词（应出现 {string.Join(" / ", SceneVerbs)} 之一）："
            + $"读者读到的第一段是「{Excerpt(paragraph)}」——说了工具是什么，没说替谁省了哪一步。");
    }

    [Fact]
    public void FirstScreen_AvoidsEmptySloganWords()
    {
        var screen = FirstScreen();
        var hits = CopyRules.BannedWords.Where(w => screen.Contains(w, StringComparison.Ordinal)).ToList();
        Assert.True(hits.Count == 0,
            $"README 首屏出现空话禁词：{string.Join("、", hits)}。首屏要说结果，不说机制与口号。");
    }

    [Fact]
    public void FirstScreen_StaysShortEnoughToBeASceneSentence()
    {
        var lines = FirstScreen().ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Trim().Length > 0)
            .ToList();
        Assert.True(lines.Count <= MaxFirstScreenLines,
            $"README 首屏有 {lines.Count} 行非空文字（上限 {MaxFirstScreenLines} 行）："
            + "超过这个长度就不是一句场景句，而是又一段说明书——把细节挪到下面的小节里。");
    }

    private static string Excerpt(string text) =>
        text.Length <= 60 ? text : text[..60] + "…";

    // ─────────────────────────────────────────────────────────────────────────────
    // 「已知限制」每条不许写成一篇小论文（第二百五十四轮，简报 P0-1：去 AI 味道）
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>「已知限制」每一条的字数上限。第二百五十四轮重排之后实测最长一条 244 字。</summary>
    private const int MaxKnownLimitChars = 500;

    /// <summary>「已知限制」一节的标题。</summary>
    private const string KnownLimitsHeading = "## 已知限制";

    /// <summary>「已知限制」一节的行（标题行到下一个 <c>## </c> 之前；它是最后一节时取到文末）。</summary>
    private static string[] KnownLimitLines()
    {
        var lines = Readme.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == KnownLimitsHeading);
        Assert.True(start >= 0, $"README.md 缺「{KnownLimitsHeading}」一节：挂账必须留，删掉这一节即红。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[(start + 1)..(end < 0 ? lines.Length : end)];
    }

    /// <summary>「已知限制」的每一条（以 <c>- </c> 开头的一行算一条）。</summary>
    private static List<string> KnownLimitItems(IEnumerable<string> lines) => lines
        .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
        .ToList();

    /// <summary>超长条目念成人话（顺序稳定：按出现顺序，取开头当名字）。</summary>
    private static List<string> OverlongKnownLimits(IEnumerable<string> items, int max) => items
        .Where(i => i.Length > max)
        .Select(i => $"「{Excerpt(i)}」{i.Length} 字（上限 {max}）")
        .ToList();

    /// <summary>
    /// 「已知限制」每一条都要能在 30 秒内读完（第二百五十四轮，简报 P0-1）。
    /// <para>
    /// <b>要解决的问题</b>：这一节的首条此前长达数百行，把 G-D5、降级脚本、PowerShell 分岔、标签列改宽
    /// 四轮历史全塞在一条里。新读者（评审）读它的时候抓不住「产品是什么、缺什么」——这正是审查清单
    /// D 节「去 AI 味道」在本仓最明显的一处失分：真人写的限制清单是一条一条的，不是一篇编年史。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 每一条 ≤ <see cref="MaxKnownLimitChars"/> 字（超了即红并点名是哪一条多少字）；
    /// ② 这一节里不许有「续行」——每一条就是一行，多行堆在一条里等于又写成了一篇；
    /// ③ 条目数有地板（8 条）：重排是「拆开」，不是「删掉」，把挂账删掉换绿是不许的。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：不判每条写得对不对、也不判该不该挂账。历史读数该去哪（`docs/NOTES.md` 对应轮次）
    /// 由人判断；门只挡住「又长回去」这一种退化。
    /// </para>
    /// </summary>
    [Fact]
    public void KnownLimits_EveryItemStaysShortEnoughToScan()
    {
        var lines = KnownLimitLines();
        var items = KnownLimitItems(lines);

        var overlong = OverlongKnownLimits(items, MaxKnownLimitChars);
        Assert.True(overlong.Count == 0,
            $"README.md「已知限制」有 {overlong.Count} 条超过 {MaxKnownLimitChars} 字：\n  "
            + string.Join("\n  ", overlong)
            + "\n一条只写「现状 + 缺什么 + 指向哪里」，历史读数移到 docs/NOTES.md 对应轮次。"
            + "拆成几条独立条目，不要把它们重新堆回一条里。");

        // ② 不许续行：这一节里每一行非空文字都必须是一条的开头。
        var continuations = lines
            .Select((l, i) => (Line: l, Index: i))
            .Where(x => x.Line.Trim().Length > 0 && !x.Line.StartsWith("- ", StringComparison.Ordinal))
            .Select(x => $"第 {x.Index + 1} 行「{Excerpt(x.Line.Trim())}」")
            .ToList();
        Assert.True(continuations.Count == 0,
            "README.md「已知限制」里有不属于任何一条的行（续行）：\n  " + string.Join("\n  ", continuations)
            + "\n一条就是一行：续行会把一条重新写成一篇，读者扫不到条目边界。");

        // ③ 条目数地板：重排是拆开，不是删掉。
        Assert.True(items.Count >= 8,
            $"README.md「已知限制」只剩 {items.Count} 条（地板 8 条）：重排是把长条目拆成几条，"
            + "不是把挂账删掉换绿——挂账必须留。");

        // 判据自验：注入一条超长必须被点名，恰好等于上限不算超。
        var injected = new List<string>
        {
            // 条目本身带「- 」两个字符，故边界那一条的正文是上限减 2。
            "- " + new string('字', MaxKnownLimitChars - 2),
            "- " + new string('字', MaxKnownLimitChars - 1),
        };
        var named = OverlongKnownLimits(injected, MaxKnownLimitChars);
        Assert.Single(named);
        Assert.Contains($"{MaxKnownLimitChars + 1} 字", named[0], StringComparison.Ordinal);
        Assert.Empty(OverlongKnownLimits([], MaxKnownLimitChars));
    }
}
