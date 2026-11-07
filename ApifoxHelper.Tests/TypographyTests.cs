using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// G-D4 中文排版与文案口径的机械检查（docs/方案.md §6.2 #11/#12、设计验收 D10/D11 的源码可判条款）：
/// 禁假斜体、中文引用只用「」、AI 腔禁词、字体栈有序回退、字号下限 12px、四档每级有使用点。
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
