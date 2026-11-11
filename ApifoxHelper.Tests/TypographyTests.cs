using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// G-D4 中文排版与文案口径的机械检查（docs/方案.md §6.2 #11/#12、设计验收 D10/D11 的源码可判条款）：
/// 禁假斜体、中文引用只用「」、AI 腔禁词、字体栈有序回退、字号下限 12px、四档每级有使用点，
/// 以及中文文案的标点口径（全角标点、无 Markdown 强调记号）。
/// 扫描对象是 Desktop/Native 源码全文（含注释）：注释也面向后续读者，同一口径更严格。
/// 服务层用户可见文案（<c>UserException</c> 消息、<c>Describe()</c> 返回、被界面引用的文案常量）
/// 由 <see cref="ServiceCopyTests"/> 用同一套 <see cref="CopyRules"/> 判定——两处不各写一份口径。
/// <para>
/// <b>第一百六十九轮改了一句长期挂着的老话</b>：这里原先写着「行首行尾禁则、正文行长、中英文空隙
/// 属渲染期属性，源码判不了，随 G-D5 真机复核」。前两条判得了，只是判的地方不一样——
/// 中英文空隙是<b>作者打下的那几个字</b>的性质（剥掉插值空洞后静态可判，见
/// <see cref="ChineseCopy_LeavesASpaceBetweenChineseAndLatin"/>）；行首禁则与正文行长是<b>会折行的那份产物</b>
/// 的性质，全仓只有导出 HTML 会折行，所以它落在 <c>ReportRender.HtmlStyle</c> 的样式声明上（见
/// <c>ReportTypographyTests</c>）。留在真机复核的只剩「界面标签在实际字体度量下到底折不折行」这一条。
/// 五条硬项各自的落地面与执行门记在 <c>docs/DESIGN.md</c> §6「中文排版」。
/// </para>
/// </summary>
public class TypographyTests
{
    // ── 文案禁则（D11）────────────────────────────────────────────────

    public static TheoryData<string> BannedWords => Data(CopyRules.BannedWords);

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

    /// <summary>
    /// 状态行不许只说「正在做」，要说清在做什么（第一百九十一轮，简报 P0-2）。
    /// <para>
    /// 与 <see cref="Copy_AvoidsEmptySloganWords"/> 同一条判据（方案 §6.2 #12「说结果不说机制」）的延伸：
    /// 禁词表管的是「智能」「一键」这类空话，管不到「正在查找…」——它没有禁词，但用户读完不知道
    /// 在查什么、查的是哪一层。清单是封闭的：只列已经改掉的那几句，新增一句要先说明它为什么空。
    /// </para>
    /// </summary>
    public static TheoryData<string> VagueStatusLines => Data(
    [
        "正在查找…",
        "正在读取接口…",
        "正在处理…",
        "请稍候",
    ]);

    [Theory]
    [MemberData(nameof(VagueStatusLines))]
    public void Copy_StatusLines_SayWhatTheyAreWorkingOn(string phrase)
    {
        foreach (var source in Repo.NativeSources())
        {
            Assert.False(source.Contains(phrase),
                $"{source.Name}:出现「{phrase}」。状态行要说清在做什么（方案 §6.2 #12），"
                + "「正在查找…」读不出查的是什么，「正在仓库里查找 .csproj…」读得出。");
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

    public static TheoryData<string> ProgressNarration => Data(CopyRules.ProgressNarration);

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

    public static TheoryData<string> HalfWidthMarks => Data(CopyRules.HalfWidthMarks);

    [Theory]
    [MemberData(nameof(HalfWidthMarks))]
    public void ChineseCopy_UsesFullWidthPunctuation(string mark)
    {
        foreach (var (source, line, text) in ChineseLiterals())
        {
            Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0,
                $"{source}:{line}:「{text}」里的半角「{mark}」紧挨中文。中文标点用全角（DESIGN.md §3 排版附加条款）。");
        }
    }

    [Fact]
    public void ChineseCopy_HalfWidthColonOnlyInTechnicalNotations()
    {
        foreach (var (source, line, text) in ChineseLiterals())
        {
            Assert.True(CopyRules.IndexOfStrayColon(text) < 0,
                $"{source}:{line}:「{text}」里的半角冒号不在技术记号白名单内。"
                + "中文句子里的冒号用全角；确属路径/时间写法须登记在 CopyRules.TechnicalColonNotations。");
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
        foreach (var notation in CopyRules.TechnicalColonNotations)
        {
            Assert.True(copy.Any(text => text.Contains(notation, StringComparison.Ordinal)),
                $"白名单里的技术记号「{notation}」在界面文案里已不存在，从 CopyRules.TechnicalColonNotations 删掉。");
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
        foreach (var (source, line, text) in CopyLiterals())
        {
            if (CopyRules.HasChinese(text)) yield return (source, line, text);
        }
    }

    /// <summary>
    /// 同一批字面量，但先还原成「用户会读到的那几个字」（<see cref="CopyRules.LiteralText"/>：
    /// 剥掉插值空洞、解码转义）。「中英文之间留空隙」判的是作者真正打下的那几个字。
    /// </summary>
    private static IEnumerable<(string Source, int Line, string Text)> TypedCopyLiterals()
    {
        foreach (var (source, line, raw) in CopyLiterals())
        {
            var text = CopyRules.LiteralText(raw);
            if (CopyRules.HasChinese(text)) yield return (source, line, text);
        }
    }

    /// <summary>字面量提取只有一份（<c>ChineseLiterals</c> 与 <c>TypedCopyLiterals</c> 共用它）。</summary>
    private static IEnumerable<(string Source, int Line, string Text)> CopyLiterals()
    {
        var literal = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
        foreach (var source in Repo.NativeSources())
        {
            var lines = source.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in literal.Matches(lines[i]))
                {
                    yield return (source.Name, i + 1, match.Groups[1].Value);
                }
            }
        }
    }

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

        // 栈本身在 FontStack（不依赖 WinForms、与测试工程共享编译），界面层只提供「装没装」这一个事实。
        var code = Repo.Source("Desktop", "Native", "FontStack.cs");
        var match = Regex.Match(code, @"\bFamilies = \[([^\]]*)\]");
        Assert.True(match.Success, "FontStack.cs 缺少 Families 字体栈。");
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

    // ── 中英文之间留一个半角空隙（D10，§6「中文排版」第 ⑤ 条）───────────

    /// <summary>
    /// 半角字母或数字紧挨汉字、中间没有空隙——读起来是「路径前缀1」这种粘在一起的观感。
    /// <para>
    /// 第一百六十九轮建这条门时全仓只命中一处（<c>ProfileAdvancedDialog</c> 的「路径前缀1、路径前缀2」），
    /// 已改掉。这条门的存在理由不是那一处，而是「以后写文案的人不知道有这条规矩」：
    /// §3 一直写着它，而没有任何东西执行过它。
    /// </para>
    /// </summary>
    [Fact]
    public void ChineseCopy_LeavesASpaceBetweenChineseAndLatin()
    {
        foreach (var (source, line, text) in TypedCopyLiterals())
        {
            var index = CopyRules.IndexOfMissingLatinGap(text);
            Assert.True(index < 0,
                $"{source}:{line}:「{text}」里的半角字母或数字紧挨汉字（位置 {index}）。"
                + "中英文之间留一个半角空隙（DESIGN.md §3、§6「中文排版」第 ⑤ 条）；"
                + "确属标识符或单位写法须登记在 CopyRules.TechnicalSpacingExceptions 并说明理由。");
        }
    }

    /// <summary>例外清单不许靠「留着让它绿」活着：每一条都必须在界面文案里真的出现（半角冒号白名单同法）。</summary>
    [Fact]
    public void TechnicalSpacingExceptions_StillExistInCopy()
    {
        var copy = TypedCopyLiterals().Select(l => l.Text).ToList();
        foreach (var exception in CopyRules.TechnicalSpacingExceptions)
        {
            Assert.True(copy.Any(text => text.Contains(exception, StringComparison.Ordinal)),
                $"例外里的「{exception}」在界面文案里已不存在，从 CopyRules.TechnicalSpacingExceptions 删掉。");
        }
    }

    /// <summary>
    /// 判定本身的自测（这一族的老毛病：判据写错时门看起来是绿的、或者直接假红到被人注释掉）。
    /// <b>两种假红都要判</b>：插值空洞里可能是中文（「…没有{word}。」），源码里的 <c>\n</c> 是两个
    /// 字母而不是文案里的拉丁——第一百六十九轮第一版只剥空洞不解转义，就在
    /// <c>ConfirmEntries</c> 的「。\n\n退出时会请求停止」上报了一处假红，是下面第三条用例把它按住的。
    /// </summary>
    [Fact]
    public void LiteralText_KeepsTheJudgementSound()
    {
        // 空洞与转义都不该被当成「作者打下的拉丁字符」。
        Assert.Equal("共处", CopyRules.LiteralText("共{count}处"));
        Assert.Equal("这个筛选下没有。", CopyRules.LiteralText("这个筛选下没有{word}。"));
        Assert.Equal("\n\n退出时会请求停止，最多等待 ", CopyRules.LiteralText("\\n\\n退出时会请求停止，最多等待 "));
        Assert.Equal("", CopyRules.LiteralText("{n}"));
        // 转义大括号还原后不算空洞。
        Assert.Equal("{0}", CopyRules.LiteralText("{{0}}"));
        // 反斜杠本身先保护再解：\\n 是「一个反斜杠加字母 n」，不是换行。
        Assert.Equal("\\n", CopyRules.LiteralText("\\\\n"));

        // 真该判红的两种形状。
        Assert.True(CopyRules.IndexOfMissingLatinGap("路径前缀1") >= 0);
        Assert.True(CopyRules.IndexOfMissingLatinGap("共8处") >= 0);
        // 留了空隙、或紧挨的是标点，都不判红。
        Assert.Equal(-1, CopyRules.IndexOfMissingLatinGap("路径前缀 1"));
        Assert.Equal(-1, CopyRules.IndexOfMissingLatinGap("共 8 处"));
        Assert.Equal(-1, CopyRules.IndexOfMissingLatinGap("Apifox 助手报告——共 3 项，详见 /api/orders。"));
        // 整条链：源码字面量还原之后不再被判红。
        Assert.Equal(-1, CopyRules.IndexOfMissingLatinGap(CopyRules.LiteralText("。\\n\\n退出时会请求停止，最多等待 ")));
    }

    // ── 字号档 ≤4（§6「中文排版」第 ④ 条）────────────────────────────

    /// <summary>
    /// §3 的档位表必须<b>恰好</b>四档，且与 <c>UiTokens</c> 的磅值常量同名一一对应、磅 = 像素 × 0.75。
    /// <para>
    /// 原先只有两条相关的门：<see cref="FontFloor_Is12Px"/>（下限 12px）与
    /// <see cref="EveryFontTier_HasAUsagePoint"/>（每档有使用点）。「封闭四档」里那个「四」没有任何东西在数——
    /// 往 §3 表里加第五行、往 UiTokens 里加第五个磅值常量，两条门照样绿，而 §3 的承诺已经改成五档了。
    /// 这一条把「四」数出来，并钉住文档与令牌是同一份档位（不是各写一份）。
    /// </para>
    /// </summary>
    [Fact]
    public void DesignSection3_HasExactlyFourFontTiers()
    {
        var tiers = (from Match m in Regex.Matches(Repo.DesignSection(3), @"(?m)^\|\s*(\w+)\s*\|\s*(\d+)px")
                     select (Name: m.Groups[1].Value, Px: int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();

        Assert.True(tiers.Count == 4,
            $"§3 的档位表现在有 {tiers.Count} 档（{string.Join("、", tiers.Select(t => t.Name))}）。"
            + "「封闭四档」是 §3 的承诺：新增一档要先改 §3 并说明为什么四档不够，删一档要确认没有使用点残留。");
        Assert.Equal(["Caption", "Body", "Section", "Title"], tiers.Select(t => t.Name));

        // 令牌侧的磅值常量与 §3 同名同数：文档与实现不是两份档位。
        var points = (from Match m in Regex.Matches(Repo.Source("Desktop", "Native", "UiTokens.cs"),
                        @"public const float (\w+)Pt = ([\d.]+)f;")
                      select (Name: m.Groups[1].Value, Pt: float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)))
            .ToList();
        Assert.True(points.Count == 4, $"UiTokens 里有 {points.Count} 个档位磅值常量（应为四档）。");
        Assert.Equal(tiers.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal),
            points.Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));

        foreach (var (name, px) in tiers)
        {
            var pt = points.Single(p => p.Name == name).Pt;
            Assert.True(Math.Abs(pt - px * 0.75f) < 0.001f, $"{name} 档：§3 记 {px}px，令牌却是 {pt}pt（换算应为像素 × 0.75）。");
        }
    }

    // ── DESIGN §6「中文排版」条款与它的引用（§6，简报 P1-2）──────────

    /// <summary>
    /// §6 必须有固定条款名 <c>| **中文排版** |</c>，五项硬项齐、顺序固定。
    /// <para>
    /// 条款名固定成这个样子是第一百六十七轮那一处的同族防复发：代码注释写着「见 DESIGN.md §6『X』」
    /// 而 §6 从来没有 X 那一条，门也就没东西可判。这里的「第 ② 项确实是行首禁则」正是
    /// <c>ReportRender</c> 那句「§6『中文排版』第 ② 条」的落点——把它改成别的，引用就又空了。
    /// </para>
    /// </summary>
    [Fact]
    public void DesignDoc6_DeclaresTheTypographyClauseWithFiveItems()
    {
        var section6 = Repo.DesignSection(6).Split('\n');
        var at = Array.FindIndex(section6, l => l.StartsWith("| **中文排版**", StringComparison.Ordinal));
        Assert.True(at >= 0, "DESIGN.md §6 没有固定条款名「| **中文排版** |」——代码注释与 docs/NOTES.md 的引用没有落点。");
        var row = section6[at];

        foreach (var item in new[]
                 {
                     "**① 全角标点**", "**② 行首行尾禁则**", "**③ 正文行长**",
                     "**④ 字号档 ≤4**", "**⑤ 中英文之间留一个半角空隙**",
                 })
        {
            Assert.True(row.Contains(item, StringComparison.Ordinal), $"§6「中文排版」条款缺第 {item} 那一项。");
        }

        // 引用格式只有一种写法：序号两侧留空格（与「DPI 适配」现口径同法），两种字面混用会有一条门核不到。
        Assert.Contains("引用格式只有一种写法", row, StringComparison.Ordinal);
        // 条款正文不重抄 §3 的承诺，只记落地面与执行门。
        Assert.Contains("条款正文在 §3", row, StringComparison.Ordinal);
    }

    /// <summary>
    /// §6「中文排版」那一行引用的每一条门都必须真的存在。
    /// 这一行是「条款 ↔ 执行证据」的对照表，引用错了等于条款自己就是一份会漂的承诺——
    /// 而它看起来比谁都扎实。
    /// </summary>
    [Fact]
    public void DesignDoc6_TypographyClause_CitesGatesThatExist()
    {
        var row = Repo.DesignSection(6).Split('\n').Single(l => l.StartsWith("| **中文排版**", StringComparison.Ordinal));
        var cited = (from Match m in Regex.Matches(row, @"([A-Z][A-Za-z0-9_]*Tests)\.([A-Za-z_][A-Za-z0-9_]*)")
                     select (Class: m.Groups[1].Value, Method: m.Groups[2].Value))
            .Distinct().ToList();

        // 封闭地板：这一行至少要把五条硬项的执行门都点到位，写成散文一段就等于没登记。
        Assert.True(cited.Count >= 5, $"「中文排版」条款只引用到 {cited.Count} 条门（地板 5）。");
        foreach (var (cls, method) in cited)
        {
            var source = Repo.Source("ApifoxHelper.Tests", cls + ".cs");
            Assert.True(source.Contains($"void {method}(", StringComparison.Ordinal),
                $"「中文排版」条款引用 {cls}.{method}，测试源码里没有这一条——引用落空。");
        }
    }

    /// <summary>把口径清单包成 TheoryData（口径值只在 <see cref="CopyRules"/> 里写一次）。</summary>
    private static TheoryData<string> Data(IEnumerable<string> values)
    {
        var data = new TheoryData<string>();
        data.AddRange(values.ToArray());
        return data;
    }
}
