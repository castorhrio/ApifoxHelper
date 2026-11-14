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

    /// <summary>
    /// 文案常量：<c>public const string NoSlotsMessage = "…";</c>（界面与异常共用同一句）。
    /// 名字带一个捕获组，是为了让 <see cref="ScannedConstNames"/> 能报出「扫描面这一轮判到了谁」——
    /// 整段匹配的位置与长度不变，<see cref="UserVisibleLiterals"/> 的取法照旧。
    /// </summary>
    private static readonly Regex ConstMessage = new(@"const\s+string\s+(\w*Message)\s*=\s*");

    /// <summary>
    /// 声明侧的取法：一个文件里<b>任何</b>字符串常量声明（<c>const</c> 与 <c>static readonly</c> 都算，
    /// 名字怎么写不管）。它与 <see cref="ConstMessage"/> 的差集，就是「写在文案口径文件里、却不在扫描面之内」的那几句。
    /// </summary>
    private static readonly Regex DeclaredStringConst = new(@"(?:const|static\s+readonly)\s+string\s+(\w+)\s*=\s*(.*)");

    /// <summary>值转发给另一个常数：<c>= JsonErrorCopy.InvalidJsonMessage;</c>（口径本体在别处，由别处进入扫描面）。</summary>
    private static readonly Regex ForwardedValue = new(@"^[A-Za-z_][\w.]*\s*;\s*$");

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

    /// <summary>扫描面在这个文件里实际判到的文案常量名（用的就是 <see cref="ConstMessage"/> 那一把，不另写一份取法）。</summary>
    private static List<string> ScannedConstNames(ServiceSource source) =>
        source.Text.Split('\n')
            .Select(line => ConstMessage.Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .ToList();

    /// <summary>
    /// <c>Api/ErrorCopy.cs</c> 声明的文案常量 ↔ 本类扫描面实际判到的常量，两侧对账。
    /// <para>
    /// <b>补的是哪个缺口</b>：第三百零一轮把路由上就地拼接的六句收进 <c>ErrorCopy</c>，它们落进扫描面
    /// <b>靠的是一条命名约定</b>（常量名以 <c>Message</c> 收尾），而这一层边界当时没有任何东西判它。
    /// 下一轮有人在这里加一条 <c>ProjectsReadFailedPrefix</c>，或把某条改写成 <c>static readonly</c>，
    /// 扫描面漏掉而本类其余各条照旧全绿——中文标点、空话禁词、中英文空隙那三条口径就在<b>错误路径上
    /// 静默失效</b>，而那正是第三百零一轮点名要收的「就地拼接的前缀两头都不占」的同一个形状：
    /// 收口靠约定，约定本身没人守。
    /// </para>
    /// <para>
    /// <b>判三件事</b>：① 声明侧每一条都要被扫描面判到（改名去掉 <c>Message</c>、写成 <c>static readonly</c>
    /// 都当场红——声明侧 <b>不看名字怎么写</b>，这是它与 <c>ErrorCopyTests.RouteCopy_ConstantsFallIntoTheTypographyScan</c>
    /// 的分工：那一条拿六个写死的名字判「收尾是不是 Message」，这一条判「这个文件里还有谁没被扫到」）；
    /// ② 扫描面在这个文件里判到的每一条都确有其名（把 <see cref="ConstMessage"/> 改坏到凭空判出一个名字，同样红）；
    /// ③ 每条声明要么在同一行带上它的字面量，要么把值显式转发给另一个常数——跨行书写时 ① 抓不住
    /// （名字照样以 <c>Message</c> 收尾），可扫描面按行取字，会读到名字、漏掉那一句：那与
    /// <see cref="UserExceptionCalls_AreSingleLine"/> 挡的是同一件事，只是换了个出口。
    /// </para>
    /// <para>
    /// <b>按那五步判这一条属不属于「假门防护」那一族</b>（五步写在 <c>docs/NOTES.md</c> 顶部「假门防护这一族：
    /// 收尾登记」那一行末尾——第二百七十轮定下「判完把结论写进 commit message」，第二百八十二轮把它收成五步）：
    /// ① 断言里出现的口径名是
    /// <c>Api/ErrorCopy.cs</c> 里的声明与本类 <see cref="ConstMessage"/> 的取法；② 前者不在被它判的这个对象里
    /// （产品文件的声明由产品文件说了算）——答「不是」；③ 按第 ③ 步当<b>对账门</b>处理，不属那一族，
    /// 照常随实物一起长；④ 实测：把这一条整条 <c>Skip</c> 掉再跑全量，<b>3135 通过 / 1 跳过 / 0 失败</b>，
    /// 本族其余各条照旧全绿（它不参与任何恒等式）。
    /// 结论同时写进本轮 commit message。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：只判 <c>Api/ErrorCopy.cs</c> 这一个文件。服务层那十个目录里加起来另有 52 处不以
    /// <c>Message</c> 收尾的 <c>const string</c> 声明（CSP 头、JSON 字段名、HTML 样式），它们大多不是界面要念出口
    /// 的那一句；把那些一起拉进对账得先给「什么算文案」立一条口径，那是另一件事，不在这里顺手做。
    /// <b>也不判转发那一句的本体合不合规</b>——转发目标自己也在扫描面之内（<c>Infrastructure/JsonErrorCopy.cs</c>），
    /// 由它自己那一行受同样的三条口径。<b>两侧都是按行取文本</b>，与既有扫描面一样看不见注释：注释里写一句
    /// <c>const string XxxMessage = "…";</c>，两侧会同时看到它，于是对得上。这不是本门新引入的盲区，
    /// 是那条扫描面从一开始的取法（注释里重复的那句字面量另有 <c>ErrorCopyTests.RouteCopyDetector_ReportsTheBreaksItMustCatch</c> 判）。
    /// </para>
    /// </summary>
    [Fact]
    public void ErrorCopy_DeclaredConsts_AreTheOnesTheScanSurfaceReads()
    {
        var source = Repo.ServiceSources().SingleOrDefault(s => s.Relative == "Api/ErrorCopy.cs");
        Assert.NotNull(source);

        var problems = ErrorCopyScanDrift(source!);
        Assert.True(problems.Count == 0,
            "Api/ErrorCopy.cs 的文案常量与 ServiceCopyTests 的扫描面对不上：\n  "
            + string.Join("\n  ", problems)
            + "\n这个文件是错误响应文案的单一落点，这里的每一句都会原样进原生界面的状态行与内联错误"
            + "（见 Api/ErrorCopy.cs 抬头那段「存在理由」）。");
    }

    /// <summary>上面那条判据的取法本体（写成纯函数，是为了能拿注入过的源码当场验它抓不抓得住）。</summary>
    private static List<string> ErrorCopyScanDrift(ServiceSource source)
    {
        var problems = new List<string>();
        var scanned = ScannedConstNames(source);
        var lines = source.Text.Split('\n');

        var declared = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            var match = DeclaredStringConst.Match(lines[i]);
            if (!match.Success) continue;

            var name = match.Groups[1].Value;
            declared.Add(name);

            if (!scanned.Contains(name, StringComparer.Ordinal))
            {
                problems.Add($"{source.Relative}:{i + 1}:「{name}」不在扫描面之内（{ConstMessage} 判不到它）。"
                    + "这句会原样进界面状态行，却不受中文标点、空话禁词、中英文空隙那三条口径约束"
                    + "——常量名以 Message 收尾才是它被扫到的条件，改名或改写成 static readonly 都会让它静默出局。");
            }

            // 同一行既没有字面量、也不是显式转发：那一句写在下一行，按行取的扫描面读不到它。
            var tail = match.Groups[2].Value.Trim();
            if (!tail.Contains('"') && !ForwardedValue.IsMatch(tail))
            {
                problems.Add($"{source.Relative}:{i + 1}:「{name}」的声明在同一行既读不到字面量、也不是转发给另一个常数。"
                    + "扫描面按行取文案，跨行书写会让它判到名字、漏掉那一句（同 UserExceptionCalls_AreSingleLine 那一课）。");
            }
        }

        // 地板：两侧同时取空是一种「绿得没有内容」——先问取法还在不在，再问两边走不走得通。
        Assert.True(declared.Count >= 10,
            $"{source.Relative} 只取到 {declared.Count} 条文案常量声明（地板 10）。"
            + "取法退化会让两侧都是空集合，而空集合对空集合是绿的。");

        foreach (var name in scanned.Where(n => !declared.Contains(n, StringComparer.Ordinal)))
        {
            problems.Add($"{source.Relative}: 扫描面判到「{name}」，这个文件里却没有它的声明。"
                + "扫描面多判出来的东西不进任何口径断言，只会把「这一条被扫到了」变成一句自证的话。");
        }

        return problems;
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
