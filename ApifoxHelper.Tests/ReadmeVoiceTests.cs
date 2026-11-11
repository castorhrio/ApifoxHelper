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
}
