using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 服务层用户可见文案的中文排版机械检查（DESIGN.md §3「中文排版附加条款」、设计验收 D10/D11）。
///
/// 为什么扫服务层：<c>UserException.Message</c> 会原样进原生界面的状态行与内联错误
/// （如 <c>Api/ApifoxRoutes.cs</c> 的「读取项目列表失败：」、<c>ApifoxCli.Describe()</c> 的失败描述），
/// 而此前只有 <c>Desktop/Native</c> 被扫——错误路径上的字恰恰是用户最需要读懂的那一句，
/// 半角标点与弯引号在那里漏网，等于「界面合规、错误路径不合规」。
///
/// 扫描范围是**界面可见的服务层文案来源**，清单封闭（三处，见 <see cref="UserVisibleLiterals"/>）：
/// ① <c>new UserException("…")</c> 的字面量；② <c>Describe()</c> 方法体里的字面量；
/// ③ 被界面与异常直接引用的文案常量（<c>const string …Message = "…"</c>）。
/// 判定口径与界面层共用 <see cref="CopyRules"/>，不另写一份。
/// </summary>
public class ServiceCopyTests
{
    private static readonly Regex Literal = new("\"((?:[^\"\\\\]|\\\\.)*)\"");

    /// <summary>文案常量：<c>public const string NoSlotsMessage = "…";</c>（界面与异常共用同一句）。</summary>
    private static readonly Regex ConstMessage = new(@"const\s+string\s+\w*Message\s*=\s*");

    // ── 提取 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 界面可见的服务层文案（相对路径 + 行号 + 字面量内容）。只取含中文的字面量：
    /// 纯英文/纯符号的字面量（枚举值、字段名、格式串）不是中文句子，不参与中文标点判定。
    /// </summary>
    private static IEnumerable<(string Source, int Line, string Text)> UserVisibleLiterals()
    {
        foreach (var source in Repo.ServiceSources())
        {
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                var call = line.IndexOf("new UserException(", StringComparison.Ordinal);
                if (call >= 0)
                {
                    foreach (var text in LiteralsIn(line[call..]))
                    {
                        if (CopyRules.HasChinese(text)) yield return (source.Relative, i + 1, text);
                    }
                }

                var constant = ConstMessage.Match(line);
                if (constant.Success)
                {
                    foreach (var text in LiteralsIn(line[(constant.Index + constant.Length)..]))
                    {
                        if (CopyRules.HasChinese(text)) yield return (source.Relative, i + 1, text);
                    }
                }
            }

            foreach (var (line, text) in DescribeLiterals(source))
            {
                if (CopyRules.HasChinese(text)) yield return (source.Relative, line, text);
            }
        }
    }

    /// <summary><c>Describe()</c> 方法体里的字面量（行号 + 内容）。方法体按花括号深度界定，不靠缩进猜。</summary>
    private static IEnumerable<(int Line, string Text)> DescribeLiterals(ServiceSource source)
    {
        var lines = source.Text.Split('\n');
        var start = Array.FindIndex(lines, l => l.Contains("string Describe()", StringComparison.Ordinal));
        if (start < 0) yield break;

        var depth = 0;
        var opened = false;
        for (var i = start; i < lines.Length; i++)
        {
            foreach (var text in LiteralsIn(lines[i])) yield return (i + 1, text);

            foreach (var c in lines[i])
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}') depth--;
            }
            if (opened && depth <= 0) yield break;
        }
    }

    private static IEnumerable<string> LiteralsIn(string text) =>
        Literal.Matches(text).Select(m => m.Groups[1].Value);

    // ── 扫描自身的盲区守卫（用例绿不等于不变量成立）──────────────────

    /// <summary>
    /// 提取按行做，多行写法的 <c>new UserException(</c> 会漏掉整段文案。
    /// 这条守卫把「漏」变成红：新增多行写法时先改提取器，再写文案。
    /// </summary>
    [Fact]
    public void UserExceptionCalls_AreSingleLine()
    {
        foreach (var source in Repo.ServiceSources())
        {
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var call = lines[i].IndexOf("new UserException(", StringComparison.Ordinal);
                if (call < 0) continue;
                Assert.True(lines[i][call..].Contains(')'),
                    $"{source.Relative}:{i + 1}:`new UserException(` 跨行书写，文案提取会漏掉这一段。"
                    + "改成单行，或先扩展 ServiceCopyTests 的提取器。");
            }
        }
    }

    /// <summary><c>Describe()</c> 是失败描述的唯一出口，提取不到就等于这条检查空转。</summary>
    [Fact]
    public void Describe_IsFoundAndScanned()
    {
        var owners = Repo.ServiceSources()
            .Where(s => s.Contains("string Describe()"))
            .Select(s => s.Relative)
            .ToList();
        Assert.Equal(["Apifox/ApifoxCli.cs"], owners);

        var literals = DescribeLiterals(Repo.ServiceSources().Single(s => s.Relative == "Apifox/ApifoxCli.cs")).ToList();
        Assert.True(literals.Count >= 4,
            $"Describe() 里只提取到 {literals.Count} 条字面量，提取器可能已失效（失败描述会原样进界面状态行）。");
    }

    /// <summary>
    /// 扫描面不能悄悄变空：文案条数掉到地板以下，说明提取器或目录清单坏了，而不是文案变干净了。
    /// </summary>
    [Fact]
    public void Scan_IsNotSilentlyEmpty()
    {
        var literals = UserVisibleLiterals().ToList();
        Assert.True(literals.Count >= 100,
            $"只提取到 {literals.Count} 条服务层文案（地板 100）。提取器或 Repo.ServiceDirectories 可能已失效。");
    }

    /// <summary>
    /// 扫描目录清单是封闭的：<c>new UserException(</c> 只允许出现在已登记的服务层目录里，
    /// 或界面层（<c>Desktop/Native</c> 由 <see cref="TypographyTests"/> 全文扫描；
    /// <c>Desktop/MainForm.cs</c> 是方案 §2.4 冻结的过渡壳，D1 §5.1 明确不在新原生界面范围内）。
    /// 新增服务层目录时先登记进 <see cref="Repo.ServiceDirectories"/>，否则它的文案不会被检查。
    /// </summary>
    [Fact]
    public void UserExceptionCalls_LiveOnlyInScannedSources()
    {
        var strays = Directory.GetFiles(Repo.Root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}ApifoxHelper.Tests{Path.DirectorySeparatorChar}"))
            .Select(f => Path.GetRelativePath(Repo.Root, f).Replace('\\', '/'))
            .Where(relative => File.ReadAllText(Path.Combine(Repo.Root, relative)).Contains("new UserException(", StringComparison.Ordinal))
            .Where(relative => !Repo.ServiceDirectories.Any(dir => relative.StartsWith(dir + "/", StringComparison.Ordinal)))
            .Where(relative => !relative.StartsWith("Desktop/", StringComparison.Ordinal))
            .ToList();

        Assert.True(strays.Count == 0,
            "这些文件里的 UserException 文案不在中文排版检查范围内：" + string.Join("、", strays)
            + "。服务层目录登记进 Repo.ServiceDirectories，界面层放进 Desktop/。");
    }

    // ── 口径（与界面层同一套 CopyRules）──────────────────────────────

    public static TheoryData<string> HalfWidthMarks => Data(CopyRules.HalfWidthMarks);

    [Theory]
    [MemberData(nameof(HalfWidthMarks))]
    public void ServiceCopy_UsesFullWidthPunctuation(string mark)
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0,
                $"{source}:{line}:「{text}」里的半角「{mark}」紧挨中文。这句会原样进界面状态行，"
                + "中文标点用全角（DESIGN.md §3 排版附加条款）。");
        }
    }

    [Fact]
    public void ServiceCopy_HalfWidthColonOnlyInTechnicalNotations()
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.True(CopyRules.IndexOfStrayColon(text) < 0,
                $"{source}:{line}:「{text}」里的半角冒号不在技术记号白名单内。"
                + "中文句子里的冒号用全角；确属路径/时间写法须登记在 CopyRules.TechnicalColonNotations。");
        }
    }

    /// <summary>
    /// 中英文之间留一个半角空隙（DESIGN.md §3、§6「中文排版」第 ⑤ 条）。
    /// 判定与界面层共用 <see cref="CopyRules.IndexOfMissingLatinGap"/>，扫描对象也是同一批文案来源：
    /// 这几句会原样进原生状态行与内联错误，「120秒」「3项」这种紧贴在这一面上一样读得难受。
    /// </summary>
    [Fact]
    public void ServiceCopy_LeavesASpaceBetweenChineseAndLatin()
    {
        foreach (var (source, line, raw) in UserVisibleLiterals())
        {
            var text = CopyRules.LiteralText(raw);
            var index = CopyRules.IndexOfMissingLatinGap(text);
            Assert.True(index < 0,
                $"{source}:{line}:「{text}」里的半角字母或数字紧挨汉字（位置 {index}）。"
                + "中英文之间留一个半角空隙；确属标识符或单位写法须登记在 CopyRules.TechnicalSpacingExceptions 并说明理由。");
        }
    }

    [Fact]
    public void ServiceCopy_ChineseQuotesOnlyUseCornerBrackets()
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.False(text.Contains('“') || text.Contains('”'),
                $"{source}:{line}:「{text}」里出现弯引号。中文引用记号只用「」（DESIGN.md §3 排版附加条款）。");
        }
    }

    [Fact]
    public void ServiceCopy_HasNoMarkdownEmphasis()
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.False(text.Contains("**", StringComparison.Ordinal),
                $"{source}:{line}:「{text}」里出现 Markdown 强调记号。界面文案是纯文本，强调用字重不用记号（DESIGN.md §3）。");
        }
    }

    public static TheoryData<string> BannedWords => Data(CopyRules.BannedWords);

    [Theory]
    [MemberData(nameof(BannedWords))]
    public void ServiceCopy_AvoidsEmptySloganWords(string word)
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.False(text.Contains(word, StringComparison.Ordinal),
                $"{source}:{line}:「{text}」里出现「{word}」。界面文案不用空话（方案 §6.2 #12），说结果不说机制。");
        }
    }

    public static TheoryData<string> ProgressNarration => Data(CopyRules.ProgressNarration);

    [Theory]
    [MemberData(nameof(ProgressNarration))]
    public void ServiceCopy_DoesNotNarrateOwnProgress(string phrase)
    {
        foreach (var (source, line, text) in UserVisibleLiterals())
        {
            Assert.False(text.Contains(phrase, StringComparison.Ordinal),
                $"{source}:{line}:「{text}」里出现「{phrase}」。界面不向用户自述工程进度（设计验收 D11、方案 §6.2 #12）。");
        }
    }

    private static TheoryData<string> Data(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        data.AddRange(values.ToArray());
        return data;
    }
}
