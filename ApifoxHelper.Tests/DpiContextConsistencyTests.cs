using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「控件自报 DPI」与「进程 DPI」必须是两个分开念的数（第二百零九轮，简报 P0-1）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>demo.ps1</c> 与 <c>demo.sh</c> 在 125% / 200% 两档给出不同读数，机制未归因
/// （第二百零七轮如实记录）。两边差的那件事有一个可数的候选：控件自报的 DPI 跟不跟档位走。
/// 若真机落进「控件自报跟着走」那一支，配置表单在 125% 档会量到 4 行、200% 档 10 行超出行高预算。
/// 本轮不归因、不改预算，只把「那一次量到的是哪一个上下文」变成读数自己能回答的事：
/// <c>RunProfileFormLayout</c> 那两段预算判定各记下两个数，取法收在唯一出口 <c>DpiContext</c>。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判两档真机上到底差在哪（那需要真机或更深的 wine 环境调查），
/// 也不判两个数谁对谁错。它只保证下一次两边再差时，报告里已经有可对照的两行。
/// </para>
/// </summary>
public class DpiContextConsistencyTests
{
    private const string Signature =
        "private static void RunProfileFormLayout(IServiceProvider services, ProjectProfile profile)";

    private static string Smoke => Repo.SmokeProgram;

    /// <summary>整段布局方法体，切到下一个顶格 <c>private</c> 声明之前（段体里有 lambda，花括号配对会被字符串骗）。</summary>
    private static string LayoutBody()
    {
        var start = Smoke.IndexOf(Signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里找不到 RunProfileFormLayout 的签名：取法变了要先改这一处，别让门静默绿着。");
        var end = Smoke.IndexOf("\n    private", start + Signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, "RunProfileFormLayout 切不出方法体边界：后面找不到下一个顶格声明。");
        return Smoke[start..end];
    }

    /// <summary>唯一出口的函数体（表达式体只有一行，取到行尾）。</summary>
    private static string HelperBody()
    {
        var match = Regex.Match(Smoke, @"(?m)^\s*private static string DpiContext\(Control control\) =>$");
        Assert.True(match.Success,
            "冒烟器里找不到 DpiContext 这个唯一出口：两个读数必须共用一处取法，否则两边各念一套，下一次差异仍然对不上。");
        return Smoke[(match.Index + match.Length)..].Trim().Split('\n')[0].Trim();
    }

    /// <summary>整段布局方法体的<b>代码行</b>（注释按本仓唯一口径 <see cref="CodeScan"/> 去掉——注释里引用原始 API 名是在说理由，不是在接线）。</summary>
    private static string LayoutCode() => CodeScan.Text(LayoutBody());

    /// <summary>取 <c>RunProfileFormLayout</c> 里某一段 RunAction 的正文（到下一段 RunAction 之前，不出整段边界）。</summary>
    private static string RunActionSlice(string title)
    {
        var body = LayoutCode();
        var start = body.IndexOf("RunAction(\"" + title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"冒烟器 RunProfileFormLayout 里找不到那一段「{title}」。");
        var next = body.IndexOf("RunAction(\"", start + 10, StringComparison.Ordinal);
        return next < 0 ? body[start..] : body[start..next];
    }

    [Fact]
    public void Helper_NamesBothReadingsUnderTheirOwnLabels()
    {
        var body = HelperBody();

        // ① 两个数都在，且各挂各的标签。
        Assert.Matches(@"控件自报 DPI \{control\.DeviceDpi\}", body);
        Assert.Matches(@"进程 DPI \{DpiScale\.ScreenDpi\(\)\}", body);
        var labels = new[] { "控件自报 DPI", "进程 DPI" };
        Assert.Equal(2, labels.Count(body.Contains));

        // ② 反向：不许把两个标签接反（接反等于混用，报告里念出来的就不是那一个数）。
        Assert.DoesNotMatch(@"控件自报 DPI \{DpiScale", body);
        Assert.DoesNotMatch(@"进程 DPI \{control\.DeviceDpi\}", body);

        // ③ 只念数：不判断、不换算、不取其一。两个数合成一个，下一次差异就又读不出来了。
        Assert.True(!Regex.IsMatch(body, @"Math\.|if\b|=="),
            "DpiContext 里出现了比较或换算：这一处只负责把两个数各按各的口径念出来，判断留给下一轮的归因。");
    }

    [Fact]
    public void LayoutSection_TakesBothReadingsThroughThatOneHelperOnly()
    {
        var body = LayoutCode();

        // 取法只有一份：这一族里不许有人绕过出口自己写 DeviceDpi / ScreenDpi。
        Assert.True(!body.Contains("DeviceDpi", StringComparison.Ordinal),
            "RunProfileFormLayout 里出现了未命名的 DeviceDpi：控件自报 DPI 的取法只有一份。");
        Assert.True(!body.Contains("ScreenDpi", StringComparison.Ordinal),
            "RunProfileFormLayout 里出现了未命名的 ScreenDpi：进程 DPI 的取法只有一份。");

        // 两个数分别进报告：① 行高段与 ⑧ 提示行段各一行，不合成一行。
        Assert.Equal(2, Regex.Matches(body, @"(?m)^\s*_lines\.Add\(.*DpiContext\(").Count);
    }

    [Fact]
    public void BothOverBudgetVerdicts_CarryBothReadings()
    {
        // ① 超行高预算那一句失败消息自己带两个数：真机当天先看的就是这一句。
        var rows = RunActionSlice("配置表单：新建对话框的行高没有死空白");
        var verdict = rows[rows.IndexOf("if (tall.Count > 0)", StringComparison.Ordinal)..];
        Assert.Single(Regex.Matches(verdict, @"DpiContext\(").Cast<Match>());
        // 同一段里报告行也念一遍：数在通过时也要留下，不然只有失败那跑才有对照。
        Assert.Equal(2, Regex.Matches(rows, @"DpiContext\(").Count);

        // ⑧ 超提示行预算：每一条超预算都带上自己那个对话框的两个数，而不是只在末尾指一句。
        var hints = RunActionSlice("配置表单：四个子对话框的提示行没有死空白");
        Assert.Matches(@"tall\.Add\(\$"".*DpiContext\(form\)", hints);
        Assert.DoesNotMatch(@"tall\.Add\(\$""「\{screen\}」第 \{row\} 行 \{heights\[row\]\}px（\{TallestInRow\(grid, row\)}）\)", hints);

        // 这一段的失败消息不再重复指一遍数（数已在每条里）。
        Assert.True(!Regex.IsMatch(hints, @"见上面那[四\d]+行"),
            "⑧ 的失败消息还指着上面那几行：数已经带在每条里了，指回去等于留一句过时的指引。");
    }

    [Fact]
    public void LayoutSection_IsRegisteredAndUnchangedInSegmentCount()
    {
        var body = LayoutCode();

        // 8 段判定一条不加：本轮只加读数，加一条判定就等于把三档读数改掉，而那是下一轮归因之后才干的事。
        Assert.Equal(8, Regex.Matches(body, @"(?m)^\s{12,}RunAction\(").Count);
        Assert.Matches(@"(?m)^\s*Section\(""配置表单的真实布局（新建 / 编辑）"", nameof\(RunProfileFormLayout\)\);", Smoke);

        // 段文档注释说得清「两个数分别记」，读演示脚本的人不用回头翻源码。
        var start = Smoke.IndexOf(Signature, StringComparison.Ordinal);
        var docStart = Smoke.LastIndexOf("/// 八段，全部量真实控件", start, StringComparison.Ordinal);
        Assert.True(docStart >= 0, "RunProfileFormLayout 的八段文档注释找不到：段数说明是答辩时念的那一句。");
        var doc = Smoke[docStart..start];
        Assert.Contains("两个数分别进报告", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_RecordsTheDivergenceAndPointsAtTheNewReportLine()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        var section = readme[readme.IndexOf("**125% / 200% 两档的读数不同", StringComparison.Ordinal)..];
        var end = section.IndexOf("\n\n", StringComparison.Ordinal);
        section = section[..(end < 0 ? section.Length : end)];

        Assert.Contains("机制未归因", section, StringComparison.Ordinal);
        Assert.Contains("DPI 上下文", section, StringComparison.Ordinal);
        Assert.True(section.Contains("DpiContext", StringComparison.Ordinal),
            ".winecheck/README.md 那段分歧记录没指出新的报告行叫什么：读到「两边读数不同」的人应该立刻知道下一次去哪一行对照。");
    }
}
