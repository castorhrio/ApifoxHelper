using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「静态门 + 冒烟段」成对出现的屏：两边断言的必须是<b>同一个表达式</b>（第二百零二轮，简报 P1-2）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百零一轮那处矛盾——<c>BuildLogDialog</c> 的空态把命令行清成空串、
/// 静态门照着实现把空串钉住、冒烟段按口径断言占位——<b>Linux 侧 2639 条全绿，只有 wine 里真构造这一屏才红</b>。
/// 当时的解法是给那一屏单独补一条「两边同源」的断言（<see cref="BuildCommandCopySmokeTests.HintState_TakesThePlaceholderFromTheSharedCopy"/>）。
/// 但那是<b>一屏一解</b>：下一屏再出现同型缺陷，仍然要等 wine 跑一次才看得见。
/// </para>
/// <para>
/// <b>本文件判什么</b>：把「同源」抽成一条通用判据，对已经成对出现的三屏逐对核——
/// ① 两半都在（静态门方法在测试文件里、冒烟段在冒烟器里且真的被 <c>Main</c> 调用）；
/// ② 静态门与冒烟段<b>都出现同一个表达式</b>（例如都取 <c>BuildCommandCopy.Line(null)</c>）；
/// ③ 两边都<b>不许出现那个表达式算出来的字面量</b>（各写一套字面量正是上一轮那处矛盾的成因）；
/// ④ 那个表达式必须是<b>符号</b>（带成员访问）而不是字面量——否则「同源」只是「两边抄了同一句话」。
/// </para>
/// <para>
/// <b>它不跑 wine</b>（跑法是 <c>.winecheck/demo.sh</c>）：它只保证「两边口径同源、且没被掏空」，
/// 真跑出来的读数记在 <c>docs/NOTES.md</c>。模拟器读数不是真机读数，G-D5 仍 <c>[ ]</c>。
/// </para>
/// </summary>
public class GateSourceConsistencyTests
{
    /// <summary>
    /// 一对「静态门 + 冒烟段」。
    /// <para>
    /// <paramref name="Shared"/>：两边都必须出现的表达式（同源的证据）。
    /// <paramref name="Forbidden"/>：两边都不许出现的字面量（各写一套的痕迹）。
    /// </para>
    /// </summary>
    private sealed record Pair(
        string Screen,
        string GateFile,
        string GateMethod,
        string SmokeSection,
        string[] Shared,
        string[] Forbidden);

    /// <summary>
    /// 已覆盖的三屏。新增一屏「静态门 + 冒烟段」时在这里加一行——<b>不加就是这一屏没人判同源</b>，
    /// 与第二百零一轮之前的状态一样：只有 wine 跑一次才看得见两边矛盾。
    /// </summary>
    private static readonly Pair[] Pairs =
    [
        new(
            "定向构建输出「复制命令」",
            "BuildCommandCopySmokeTests.cs",
            "HintState_TakesThePlaceholderFromTheSharedCopy",
            "RunBuildCommandCopyClick",
            ["BuildCommandCopy.Line(null)"],
            ["\"命令：—\""]),
        new(
            "提示对话框「复制路径」",
            "NoticeCopyTests.cs",
            "NoticeDialog_MeasuresTheMonoLineWithTheGridsActualAvailableWidth",
            "RunNoticeCopyClick",
            ["Ui.ContentWidth(grid)", "Ui.MonoBoxHeight("],
            // 这一屏的「同源」是算式不是取值：没有可判的字面量（可用宽由 Ui.ContentWidth 一处算）。
            []),
        new(
            "完整错误对话框「查看完整错误」",
            "StatusLineTextTests.cs",
            "Dialog_TakesItsCopyFromTheSharedSource",
            "ReadFullTextAndCheck",
            ["StatusLineText.FullTextHeading"],
            ["完整错误信息"]),
    ];

    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>静态门那一半：测试文件里那个方法的方法体。</summary>
    private static string GateBody(Pair pair) =>
        MethodBody(Repo.Source("ApifoxHelper.Tests", pair.GateFile), "void " + pair.GateMethod + "(");

    /// <summary>冒烟段那一半：冒烟器里那个方法的方法体。</summary>
    private static string SmokeBody(Pair pair) =>
        MethodBody(SmokeProgram, "private static void " + pair.SmokeSection + "(");

    /// <summary>
    /// 两半都在，且冒烟段真的接在冒烟器上（<c>nameof</c> 注册 + 一行真调用）。
    /// <para>
    /// 只判「方法存在」不够：段在文件里但没在 <c>Main</c> 里注册，报告里永远不会出现这一段，
    /// 而所有静态门照旧全绿——「量过」这件事就只剩一句自述。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryPair_HasBothHalvesAndTheSmokeSectionIsWired()
    {
        var program = SmokeProgram;
        foreach (var pair in Pairs)
        {
            // 静态门那一半：方法在测试文件里。
            Assert.False(string.IsNullOrWhiteSpace(GateBody(pair)),
                $"「{pair.Screen}」的静态门 {pair.GateFile} 里找不到 {pair.GateMethod}：同源判据没有可判的一侧。");

            // 冒烟段那一半：方法在冒烟器里。
            Assert.False(string.IsNullOrWhiteSpace(SmokeBody(pair)),
                $"「{pair.Screen}」的冒烟段 {pair.SmokeSection} 不在 ApifoxHelper.Smoke/Program.cs 里："
                + "静态门判的那一半没有运行时对照。");

            // 真调用：一行以段名开头的调用（方法声明行是 `private static void X(`，不匹配）。
            // 段在 Main 里的注册由每一屏自己的门判（例如 BuildCommandCopySmokeTests.Smoke_RunsTheBuildCommandCopySection）——
            // 本文件判的是「两半同源」，不重复判注册。
            var calls = program.Split('\n')
                .Count(l => Regex.IsMatch(l, @"^\s+" + Regex.Escape(pair.SmokeSection) + @"\("));
            Assert.True(calls > 0,
                $"「{pair.Screen}」的冒烟段 {pair.SmokeSection} 没有真的被调用（注释掉的调用不算接线）："
                + "报告里不会出现这一段，而静态门照旧全绿。");
        }
    }

    /// <summary>
    /// 两边断言的是<b>同一个表达式</b>：静态门里出现它，冒烟段里也出现它。
    /// <para>
    /// 这是「同源」的正面判据。第二百零一轮那处矛盾就是两边各写一套：对话框写空串、静态门钉空串、
    /// 冒烟段按口径断言占位——三处各自自洽，合起来矛盾，而 Linux 侧一条都不红。
    /// </para>
    /// </summary>
    [Fact]
    public void BothSides_TakeTheSameExpression()
    {
        foreach (var pair in Pairs)
        {
            var gate = GateBody(pair);
            var smoke = SmokeBody(pair);
            foreach (var expression in pair.Shared)
            {
                Assert.Contains(expression, gate, StringComparison.Ordinal);
                Assert.Contains(expression, smoke, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 两边都<b>不许</b>出现那个表达式算出来的字面量。
    /// <para>
    /// 同源的反面：静态门判「口径算出来的那一句」，冒烟段判「手写的同一句」——今天数值相等，
    /// 口径一改就分叉，而分叉的那一刻两边都还是绿的。字面量出现在任何一侧都即红。
    /// </para>
    /// </summary>
    [Fact]
    public void NeitherSide_WritesItsOwnLiteral()
    {
        // 至少一对有字面量可判：全空的话这一条是空转的。
        Assert.Contains(Pairs, p => p.Forbidden.Length > 0);
        foreach (var pair in Pairs)
        {
            var gate = GateBody(pair);
            var smoke = SmokeBody(pair);
            foreach (var literal in pair.Forbidden)
            {
                Assert.DoesNotContain(literal, gate, StringComparison.Ordinal);
                Assert.DoesNotContain(literal, smoke, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// 「同源」的表达式必须是<b>符号</b>（带成员访问），不是字面量。
    /// <para>
    /// 否则「两边都出现 <c>"命令：—"</c>」也能通过上面两条——那是「两边抄了同一句话」，
    /// 不是「两边取同一个口径」。这一条把「同源」的定义钉在符号上：口径文件改了，两边一起变。
    /// </para>
    /// </summary>
    [Fact]
    public void SharedExpressions_AreSymbolsNotLiterals()
    {
        foreach (var pair in Pairs)
        {
            Assert.NotEmpty(pair.Shared);
            foreach (var expression in pair.Shared)
            {
                Assert.Contains('.', expression);
                Assert.DoesNotContain('"', expression);
            }
        }
    }

    /// <summary>方法体（按大括号配平取，与本仓其它门同一套取法）。</summary>
    private static string MethodBody(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var open = source.IndexOf('{', from);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"{signature} 的方法体没有闭合。");
        return "";
    }
}
