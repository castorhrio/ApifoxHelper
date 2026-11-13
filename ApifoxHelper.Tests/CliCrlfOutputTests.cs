using ApifoxHelper.Apifox;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// CLI 输出换行符（LF / CRLF）下的解析等价性（第二百六十轮，控制仓简报 P0-1）。
/// <para>
/// <b>简报说的是什么</b>：<c>ApifoxCli.Parse</c> 用 <c>text.Split('\n')</c> 逐行找第一个 <c>{</c>，
/// 用 <c>offset += line.Length + 1</c> 累加偏移；简报判断「Windows 的 CRLF 输出下每行 <c>line</c> 尾部带
/// <c>\r</c>，<c>line.Length</c> 已含 <c>\r</c>，而 <c>+1</c> 只跳了 <c>\n</c>，所以 offset 少 1」，
/// 并据此要求「修正偏移累加」。
/// </para>
/// <para>
/// <b>本轮实测的结论：那个「少 1」不成立，代码本来就是对的。</b> 切的是 <c>'\n'</c>，所以
/// <c>line.Length</c> 里已经含了 <c>\r</c>，<c>+1</c> 补的正是被切掉的那一个 <c>\n</c>：
/// CRLF 行 <c>"abc\r"</c> 长 4，<c>+1 = 5</c>，而 <c>"abc\r\n"</c> 正好 5 个字符——不多不少。
/// 本文件第 ①②③ 条就是拿真执行把这件事钉住：CRLF 与 LF 两份输出喂进 <c>Parse</c>，
/// <c>ParseError</c> 都是 null、<c>Data</c> 逐字相同、连「JSON 格式无效（第 N 个字节处）」里的 N 也相同。
/// </para>
/// <para>
/// <b>那为什么还要立门</b>：简报给的那个「修正」（<c>line.Length + Environment.NewLine.Length</c>）
/// <b>本身就是一个只在 Windows 上发作的回归</b>——Windows 上 <c>Environment.NewLine</c> 是 2 个字符，
/// 而 <c>line.Length</c> 已经数过 <c>\r</c> 了，于是每行多算 1，JSON 起点右移一格，
/// <c>text[start..]</c> 从 <c>{</c> 之后开始，<c>JsonNode.Parse</c> 抛 <c>JsonException</c>，
/// 用户看到的是「输出无法可靠解析：JSON 格式无效」——<b>而 Linux 上 <c>Environment.NewLine</c> 恰好也是
/// 1 个字符，行为测试在 Linux 上一发都抓不住</b>（本文件第 ④ 条把这句话写成了判据：
/// 形状门是唯一能在 Linux 上拦住它的东西）。所以本轮<b>不改行为</b>，只做两件事：
/// 把「两种换行等价」变成真执行断言，把「偏移累加只许是 <c>line.Length + 1</c>」变成形状门。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机上 CLI 到底吐 LF 还是 CRLF（真机 0 台，G-D5 仍 <c>[ ]</c>），
/// 也不判 <c>node</c> 在 Windows 上写出的字节——它判的是「两种换行喂进来，解析结果必须一样」，
/// 以及「偏移累加不许被改成那个只在 Windows 上坏的写法」。
/// </para>
/// </summary>
public class CliCrlfOutputTests
{
    /// <summary>一份最小的成功输出（<c>success</c> + <c>data</c>），中文与数字都带上，好让字节偏移有区别。</summary>
    private const string Json = "{\"success\":true,\"data\":{\"name\":\"订单\",\"count\":2}}";

    /// <summary>CLI 真实输出的形状：几行前导说明（版本提示、警告）之后才是那一个 JSON 对象。</summary>
    private static readonly string[] Preamble = ["apifox-cli 2.2.6", "正在读取项目配置…"];

    private static ProcessResult Output(string text) => new(0, text, "", false, TimeSpan.Zero);

    private static string WithNewLine(string newLine) =>
        string.Join(newLine, Preamble) + newLine + Json + newLine;

    /// <summary>
    /// ① CRLF 输出（前导说明行 + JSON）解析成功，且 <c>Data</c> 就是那一份。
    /// <para>
    /// 这一条是简报点名的那个场景：JSON 不在第一行，偏移必须落在 <c>{</c> 上。偏移少 1 的话
    /// <c>text[start..]</c> 会从 <c>{</c> 之后开始，<c>ParseError</c> 变成「JSON 格式无效」。
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_OnCrlfOutputWithPreamble_ReadsTheJson()
    {
        var result = ApifoxCli.Parse(Output(WithNewLine("\r\n")));

        Assert.Null(result.ParseError);
        Assert.True(result.Success);
        Assert.True(result.Ok);
        Assert.Equal("订单", result.Data!["name"]!.ToString());
        Assert.Equal(2, (int)result.Data!["count"]!);
    }

    /// <summary>
    /// ② CRLF 与 LF 两份输出解析出<b>逐字相同</b>的结果（<c>Data</c> 的 JSON 文本相同、<c>ParseError</c> 都为空）。
    /// <para>
    /// 只判「CRLF 能解析」是不够的：把偏移改成「按 <c>\r\n</c> 跳两格」也能让 ① 绿（起点落在 <c>{</c> 上），
    /// 但那样 LF 输出会跳过 <c>{</c> 的第一个字符。两份一起判，才钉住「两种换行等价」。
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_OnCrlfAndLfOutput_AgreeOnTheSameData()
    {
        var crlf = ApifoxCli.Parse(Output(WithNewLine("\r\n")));
        var lf = ApifoxCli.Parse(Output(WithNewLine("\n")));

        Assert.Null(crlf.ParseError);
        Assert.Null(lf.ParseError);
        Assert.Equal(lf.Data!.ToJsonString(), crlf.Data!.ToJsonString());
        Assert.Equal(lf.Meta?.ToJsonString(), crlf.Meta?.ToJsonString());
        Assert.Equal(lf.Success, crlf.Success);
    }

    /// <summary>
    /// ③ 解析失败时，两种换行报出的<b>结构偏移也相同</b>（「第 N 个字节处」里的 N 一样）。
    /// <para>
    /// 这一条判的是「偏移是相对 JSON 起点算的，不是相对整段输出算的」：CRLF 的前导行比 LF 多两个字节，
    /// 若偏移把前导行的字节数带进来，两份输出的 N 就会差 2。它同时是 ①② 的反向：起点算错时
    /// 这里报的 N 会整体漂一格。
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_OnTruncatedJson_ReportsTheSameOffsetForBothNewLines()
    {
        const string truncated = "{\"success\":true,\"data\":{\"name\":\"订单\"";
        var crlf = ApifoxCli.Parse(Output(string.Join("\r\n", Preamble) + "\r\n" + truncated));
        var lf = ApifoxCli.Parse(Output(string.Join("\n", Preamble) + "\n" + truncated));

        Assert.NotNull(crlf.ParseError);
        Assert.NotNull(lf.ParseError);
        Assert.StartsWith("JSON 格式无效（第 ", crlf.ParseError, StringComparison.Ordinal);
        Assert.Equal(lf.ParseError, crlf.ParseError);
    }

    /// <summary>
    /// ④ 偏移累加只许是 <c>line.Length + 1</c>（形状门）。
    /// <para>
    /// <b>为什么必须是形状门而不是行为断言</b>：简报建议的「修正」是
    /// <c>line.Length + Environment.NewLine.Length</c>，而 Linux 上 <c>Environment.NewLine</c> 恰好也是
    /// 1 个字符——<b>这个改动在 Linux 上行为完全不变，①②③ 一发都不会红</b>，到 Windows 真机上才发作。
    /// 也就是说：这一族里唯一能在本机拦住它的判据，是「源码里那行加法长什么样」。
    /// </para>
    /// <para>
    /// 判据本体在 <see cref="OffsetArithmeticProblems"/>（纯函数，好让
    /// <see cref="OffsetArithmeticGate_ReportsTheBreaksItMustCatch"/> 把每一发破坏各喂一遍）。
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_KeepsTheCharOffsetArithmetic()
    {
        var problems = OffsetArithmeticProblems(ParseBody());
        Assert.True(problems.Count == 0,
            "ApifoxCli.Parse 的偏移累加被改坏了：\n  " + string.Join("\n  ", problems)
            + "\n口径：切的是 '\\n'，line.Length 已含行尾 '\\r'，所以 +1 只补那个 '\\n'，LF 与 CRLF 都对。"
            + "\n改成 Environment.NewLine.Length 在 Linux 上行为不变（也是 1 个字符）、在 Windows 上每行多算 1，"
            + "JSON 起点右移一格，真机第一次跑 CLI 就报「JSON 格式无效」。");
    }

    /// <summary>
    /// ⑤ 形状门自己抓得住它该抓的那几发（注入自验，同 <c>SourceDriftDetector_ReportsTheBreaksItMustCatch</c> 的做法）。
    /// <para>
    /// 三发破坏各喂一遍：① 换成 <c>Environment.NewLine.Length</c>（简报建议的那个「修正」）；
    /// ② 先 <c>Replace("\r\n", "\n")</c> 再切（另一种「修正」，它会让 ③ 的字节偏移与 LF 那份不再同源）；
    /// ③ 把 <c>+1</c> 写成 <c>+2</c>（按 CRLF 跳两格，LF 输出当场坏）。
    /// 合规的那一行不许被报——否则这条门只会被整条注释掉。
    /// </para>
    /// </summary>
    [Fact]
    public void OffsetArithmeticGate_ReportsTheBreaksItMustCatch()
    {
        const string good = "offset += line.Length + 1;";
        Assert.Empty(OffsetArithmeticProblems(good));

        var byNewLine = OffsetArithmeticProblems("offset += line.Length + Environment.NewLine.Length;");
        Assert.Contains(byNewLine, p => p.Contains("Environment.NewLine", StringComparison.Ordinal));

        var byNormalize = OffsetArithmeticProblems("var text = raw.Replace(\"\\r\\n\", \"\\n\");\n" + good);
        Assert.Contains(byNormalize, p => p.Contains("Replace", StringComparison.Ordinal));

        var byTwo = OffsetArithmeticProblems("offset += line.Length + 2;");
        Assert.Contains(byTwo, p => p.Contains("+ 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// 偏移累加那一行的判据本体：返回空 = 合规；返回一句话 = 不合规，那句话就是红的时候念出来的那一句。
    /// <para>
    /// 传进来的必须是<b>去掉行注释</b>的方法体（<see cref="ParseBody"/> 已经去过）：<c>Parse</c> 里那段
    /// 注释要解释「为什么不能改成 <c>Environment.NewLine.Length</c>」，那是理由而不是第二份实现。
    /// </para>
    /// </summary>
    private static List<string> OffsetArithmeticProblems(string codeOnlyBody)
    {
        var problems = new List<string>();

        if (!codeOnlyBody.Contains("offset += line.Length + 1;", StringComparison.Ordinal))
        {
            problems.Add("找不到「offset += line.Length + 1;」这一行：偏移累加的形状变了，"
                + "而它是 LF / CRLF 两种输出下唯一对得上的写法（line.Length 已含 '\\r'，+1 只补 '\\n'）。");
        }

        if (codeOnlyBody.Contains("Environment.NewLine", StringComparison.Ordinal))
        {
            problems.Add("偏移累加里出现了 Environment.NewLine：Windows 上它是 2 个字符，而 line.Length 已经数过 '\\r'，"
                + "每行会多算 1，JSON 起点右移一格——Linux 上这个改动行为不变，真机上第一次跑 CLI 就报「JSON 格式无效」。");
        }

        if (codeOnlyBody.Contains("Replace(\"\\r\\n\"", StringComparison.Ordinal))
        {
            problems.Add("Parse 里出现了 Replace(\"\\r\\n\", …) 这种「先归一化再切」的写法："
                + "它把「偏移按原文算」这件事换成了「偏移按归一化后的文本算」，"
                + "而 JsonErrorCopy 报的字节偏移是相对 JSON 起点算的——两处口径从此不同源。");
        }

        return problems;
    }

    /// <summary>
    /// <c>ApifoxCli.Parse</c> 的方法体（按花括号深度界定，不靠缩进猜），已去掉行注释。
    /// <para>
    /// 数括号必须在<b>抹掉字面量</b>的那一份上数：这个方法体里有 <c>StartsWith('{')</c> 这样的字符字面量，
    /// 直接数原文会把那个 <c>{</c> 当成开括号，方法体永远「没有闭合」（本文件第一版就是这么红的）。
    /// </para>
    /// </summary>
    private static string ParseBody()
    {
        var source = Repo.Source("Apifox", "ApifoxCli.cs");
        var masked = MaskLiterals(source);
        const string header = "public static CliResult Parse(ProcessResult process)";
        var start = masked.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Apifox/ApifoxCli.cs 里找不到「{header}」：偏移累加那一行没有落点了。");
        var open = masked.IndexOf('{', start);
        Assert.True(open > start, $"「{header}」后面没有方法体。");

        var depth = 0;
        for (var i = open; i < masked.Length; i++)
        {
            if (masked[i] == '{') depth++;
            else if (masked[i] == '}' && --depth == 0) return CodeOnly(source[open..(i + 1)]);
        }

        Assert.Fail($"「{header}」的方法体没有闭合。");
        return "";
    }

    /// <summary>
    /// 把字符串字面量、字符字面量与行注释抹成空格（长度与换行位置不变，下标仍对得上原文）。
    /// 只为本文件「数括号」这一件事服务，不是通用的 C# 词法器。
    /// </summary>
    private static string MaskLiterals(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            {
                while (i < chars.Length && chars[i] != '\n')
                {
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            if (chars[i] is '"' or '\'')
            {
                var quote = chars[i];
                chars[i] = ' ';
                i++;
                while (i < chars.Length)
                {
                    if (chars[i] == '\\')
                    {
                        chars[i] = ' ';
                        if (i + 1 < chars.Length) chars[i + 1] = ' ';
                        i += 2;
                        continue;
                    }

                    if (chars[i] == quote)
                    {
                        chars[i] = ' ';
                        i++;
                        break;
                    }

                    if (chars[i] == '\n') break; // 未闭合：不把整份文件吃掉
                    chars[i] = ' ';
                    i++;
                }

                continue;
            }

            i++;
        }

        return new string(chars);
    }

    /// <summary>去掉行注释（与 <c>CliParseErrorTests.CodeOnly</c> 同一口径：判「有没有读它」只看代码行）。</summary>
    private static string CodeOnly(string body) =>
        string.Join('\n', body.Split('\n').Select(l =>
        {
            var at = l.IndexOf("//", StringComparison.Ordinal);
            return at < 0 ? l : l[..at];
        }));
}
