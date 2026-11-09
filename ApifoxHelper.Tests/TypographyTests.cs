using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// G-D4 中文排版与文案口径的机械检查（docs/方案.md §6.2 #11/#12、设计验收 D10/D11 的源码可判条款）：
/// 禁假斜体、中文引用只用「」、AI 腔禁词、字体栈有序回退、字号下限 12px、四档每级有使用点，
/// 以及中文文案的标点口径（全角标点、无 Markdown 强调记号）。
/// 扫描对象是 Desktop/Native 源码全文（含注释）：注释也面向后续读者，同一口径更严格。
/// 行首行尾禁则、正文行长、中英文空隙属渲染期属性，源码判不了，随 G-D5 真机复核。
/// </summary>
public class TypographyTests
{
    // ── 文案禁则（D11）────────────────────────────────────────────────

    public static TheoryData<string> BannedWords => new() { "赋能", "闭环", "一站式", "无缝", "智能", "极致" };

    [Theory]
    [MemberData(nameof(BannedWords))]
    public void Copy_AvoidsEmptySloganWords(string word)
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(source.Contains(word),
                $"{source.Name}:出现「{word}」。界面文案不用空话（方案 §6.2 #12），说结果不说机制。");
        }
    }

    [Fact]
    public void Copy_ChineseQuotesOnlyUseCornerBrackets()
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(source.Contains("“") || source.Contains("”"),
                $"{source.Name}:出现弯引号。中文引用记号只用「」（DESIGN.md §3 排版附加条款）。");
        }
    }

    // ── 工程进度自述（D11：不向用户自述工程进度）────────────────────

    public static TheoryData<string> ProgressNarration => new()
    {
        "开发中", "迭代中", "本切片", "后续切片", "尚未接入", "待接入",
    };

    [Theory]
    [MemberData(nameof(ProgressNarration))]
    public void Copy_DoesNotNarrateOwnProgress(string phrase)
    {
        foreach (var source in Repo.NativeSources())
        {
            foreach (var line in source.Text.Split('\n'))
            {
                if (!line.Contains(phrase, StringComparison.Ordinal)) continue;
                Assert.True(IsDomainStatusLine(line),
                    $"{source.Name}:出现「{phrase}」。界面不向用户自述工程进度（设计验收 D11、方案 §6.2 #12）；"
                    + "描述业务对象状态的领域值（如接口状态枚举）须登记在 IsDomainStatusLine 白名单。");
            }
        }
    }

    /// <summary>领域状态白名单（按行首内容精确匹配）：Apifox 接口状态枚举「开发中」描述接口自身，不是工具的工程进度。</summary>
    private static bool IsDomainStatusLine(string line) =>
        line.TrimStart().StartsWith("(\"developing\", \"开发中\")", StringComparison.Ordinal);

    // ── 中文标点（D10：全角标点）──────────────────────────────────────

    /// <summary>
    /// 中文文案里不得出现的半角标点（DESIGN.md §3「中文排版附加条款：全角标点」）。
    /// 判定口径是「紧挨中文」——半角标点夹在代码、路径、数字之间（<c>app.js</c>、<c>1.5</c>、
    /// <c>GET /api/x</c>）不算违规，那些不是中文句子里的标点。
    /// </summary>
    public static TheoryData<string> HalfWidthMarks => new() { ",", ";", "?", "!", "." };

    [Theory]
    [MemberData(nameof(HalfWidthMarks))]
    public void ChineseCopy_UsesFullWidthPunctuation(string mark)
    {
        foreach (var (source, line, text) in ChineseLiterals())
        {
            Assert.True(IndexOfHalfWidthMark(text, mark) < 0,
                $"{source}:{line}:「{text}」里的半角「{mark}」紧挨中文。中文标点用全角（DESIGN.md §3 排版附加条款）。");
        }
    }

    /// <summary>
    /// 半角冒号只允许出现在技术记号里（DESIGN.md §3 的「代码/路径/数字上下文」例外）：
    /// 「文件:行」是路径与行号的写法、「时:分」是时间格式，改成全角反而读错。
    /// 白名单是封闭清单——新增一条要先说明它为什么不是中文句子里的标点。
    /// </summary>
    private static readonly string[] TechnicalColonNotations = ["文件:行", "时:分"];

    [Fact]
    public void ChineseCopy_HalfWidthColonOnlyInTechnicalNotations()
    {
        foreach (var (source, line, text) in ChineseLiterals())
        {
            var rest = text;
            foreach (var notation in TechnicalColonNotations)
            {
                rest = rest.Replace(notation, string.Empty, StringComparison.Ordinal);
            }
            Assert.True(IndexOfHalfWidthMark(rest, ":") < 0,
                $"{source}:{line}:「{text}」里的半角冒号不在技术记号白名单内。"
                + "中文句子里的冒号用全角；确属路径/时间写法须登记在 TechnicalColonNotations。");
        }
    }

    /// <summary>
    /// 白名单不能靠「留着让它绿」活着：每条技术记号都必须在**界面文案**（字面量）里真的出现。
    /// 只查字面量、不查全文——注释里引用这几个字是说明理由，白名单靠注释活着等于它已经烂了。
    /// </summary>
    [Fact]
    public void TechnicalColonNotations_StillExistInCopy()
    {
        var copy = ChineseLiterals().Select(l => l.Text).ToList();
        foreach (var notation in TechnicalColonNotations)
        {
            Assert.True(copy.Any(text => text.Contains(notation, StringComparison.Ordinal)),
                $"白名单里的技术记号「{notation}」在界面文案里已不存在，从 TechnicalColonNotations 删掉。");
        }
    }

    /// <summary>界面文案是纯文本，不认 Markdown：<c>**粗体**</c> 会原样显示成星号（禁假斜体的同一族）。</summary>
    [Fact]
    public void ChineseCopy_HasNoMarkdownEmphasis()
    {
        foreach (var (source, line, text) in ChineseLiterals())
        {
            Assert.False(text.Contains("**", StringComparison.Ordinal),
                $"{source}:{line}:「{text}」里出现 Markdown 强调记号。界面文案是纯文本，强调用字重不用记号（DESIGN.md §3）。");
        }
    }

    /// <summary>
    /// 界面源码里的中文字面量（文件名 + 行号 + 内容）。注释里的中文不在扫描范围——注释不是界面文案，
    /// 且注释里引用文档原句（含半角标点）是说明理由，不是交付给用户的字。
    /// </summary>
    private static IEnumerable<(string Source, int Line, string Text)> ChineseLiterals()
    {
        var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        foreach (var source in Repo.NativeSources())
        {
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in literal.Matches(lines[i]))
                {
                    var text = match.Groups[1].Value;
                    if (text.Any(IsChinese)) yield return (source.Name, i + 1, text);
                }
            }
        }
    }

    /// <summary>半角标点紧挨中文的位置（-1 表示没有）。</summary>
    private static int IndexOfHalfWidthMark(string text, string mark)
    {
        for (var i = 0; i + mark.Length <= text.Length; i++)
        {
            if (string.CompareOrdinal(text, i, mark, 0, mark.Length) != 0) continue;
            var before = i > 0 && IsChinese(text[i - 1]);
            var after = i + mark.Length < text.Length && IsChinese(text[i + mark.Length]);
            if (before || after) return i;
        }
        return -1;
    }

    private static bool IsChinese(char c) => c is >= '\u4e00' and <= '\u9fff';

    // ── 排版（D10/D4）────────────────────────────────────────────────

    [Fact]
    public void NoFakeItalic()
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(source.Contains("Italic"),
                $"{source.Name}:出现 Italic。中文禁假斜体（DESIGN.md §3），强调用字重不用倾斜。");
        }
    }

    [Fact]
    public void FontStack_MatchesDesignDocOrder()
    {
        var doc = Regex.Match(Repo.DesignSection(3), @"字体栈（[^）]*）：([^\n。]+)");
        Assert.True(doc.Success, "DESIGN.md §3 缺少字体栈行。");
        var expected = doc.Groups[1].Value.Trim().Split("→", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", "SimHei"], expected);

        var code = Repo.Source("Desktop", "Native", "UiTokens.cs");
        var match = Regex.Match(code, @"FamilyStack = \[([^\]]*)\]");
        Assert.True(match.Success, "UiTokens.cs 缺少 FamilyStack 字体栈。");
        var actual = Regex.Matches(match.Groups[1].Value, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToArray();
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void FontFloor_Is12Px()
    {
        var sizes = Regex.Matches(Repo.DesignSection(3), @"^\|\s*\w+\s*\|\s*(\d+)px", RegexOptions.Multiline)
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();
        Assert.NotEmpty(sizes);
        Assert.Equal(12, sizes.Min());
    }

    /// <summary>每级有使用点（DESIGN.md §3：无死级别）——四档工厂在界面代码中至少各有一处调用。</summary>
    [Theory]
    [InlineData("Ui.Caption(")]
    [InlineData("Ui.Body(")]
    [InlineData("Ui.Section(")]
    [InlineData("Ui.Title(")]
    public void EveryFontTier_HasAUsagePoint(string factory)
    {
        var users = Repo.NativeSources()
            .Where(s => !s.IsUi && !s.IsUiTokens && s.Contains(factory))
            .Select(s => s.Name)
            .ToList();
        Assert.True(users.Count > 0, $"字体档工厂 {factory} 没有任何使用点：新增档位必须先有使用点，不用的档位从令牌表移除（无死级别）。");
    }
}
