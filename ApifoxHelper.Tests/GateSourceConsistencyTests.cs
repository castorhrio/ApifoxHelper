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
/// ④ 那个表达式必须是<b>符号</b>（带成员访问）而不是字面量——否则「同源」只是「两边抄了同一句话」；
/// ⑤ <b>没有漏屏</b>（第二百零三轮，简报 P0-1）：凡「冒烟器里有这一段 + 测试工程里有 <c>*SmokeTests.cs</c>
/// 引用它」这对特征成立的，就必须在 <see cref="Pairs"/> 里出现，或在 <see cref="Exemptions"/> 里登记一行说明。
/// 前四条判的是「已登记的那几对同不同源」，这一条判的是「该登记的有没有漏」——没有它，下一屏出现时
/// 仍然只有 wine 跑一次才看得见。
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

    // ── 第二百零三轮（简报 P0-1）：覆盖率门——「漏一屏」也要红 ──────────────

    /// <summary>
    /// 冒烟器里<b>声明</b>的段方法（<c>private static void RunXxx(</c>）。
    /// </summary>
    private static List<string> DeclaredSections() =>
        Regex.Matches(SmokeProgram, @"private static void (Run[A-Za-z0-9_]*)\(")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 冒烟器 <c>Main</c> 里<b>注册</b>过的段（<c>Section("…", nameof(RunXxx))</c>）。
    /// <para>
    /// 注册表是「段」的权威定义：只有注册过的段才会出现在报告的分段表里，演示脚本也按它逐屏对账。
    /// </para>
    /// </summary>
    private static List<string> RegisteredSections() =>
        Regex.Matches(SmokeProgram, @"Section\(""[^""]*"",\s*nameof\((Run[A-Za-z0-9_]*)\)\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 冒烟器里的构造辅助，不是段：<c>Run(name, build)</c> 造一个控件、<c>RunAction(name, action)</c> 跑一段动作。
    /// 它们没有 <c>Section</c> 注册，也不该出现在报告的分段表里——列在这里是为了让下面那条
    /// 「声明了就要注册」的门不把它们当成漏注册的段。
    /// </summary>
    private static readonly string[] SmokeHelpers = ["Run", "RunAction"];

    /// <summary>
    /// 「有冒烟段、也有 <c>*SmokeTests.cs</c> 引用它，但<b>不是</b>同源成对屏」的豁免登记。
    /// <para>
    /// 写法同 <c>AcceptanceChecklistTests</c> 的「DoD 豁免登记」：<b>登记 + 理由</b>两件事都要有。
    /// 只登记不写理由，下一个人推不出这一屏为什么不在 <see cref="Pairs"/> 里，就会以为漏了。
    /// </para>
    /// </summary>
    private sealed record Exemption(string[] Sections, string Reason);

    /// <summary>
    /// 已登记的豁免。新增一屏「有冒烟段 + 有 <c>*SmokeTests.cs</c> 引用、但没有同源表达式可判」时在这里加一行。
    /// </summary>
    private static readonly Exemption[] Exemptions =
    [
        new(
            [
                "RunEndToEnd", "RunWriteBackEndToEnd", "RunScanResultsInteraction", "RunFontStackRuntime",
                "RunCandidatesInteraction", "RunSupplementsInteraction", "RunProfileLoadFailure",
                "RunTaskLoadFailure", "RunReportsLoadFailure", "RunDecisionsLoadFailure",
                "RunJournalLoadFailure", "RunDetailBreak", "RunDialogStatusSweep",
            ],
            "NativeDialogSmokeTests.cs 判的是「段序 / 段数 / 段在 Main 里被调用」，不是「静态门与冒烟段断言同一表达式」——"
            + "它引用的这 13 段没有可判的同源表达式，硬凑一对只会把「同源」稀释成「两边都提到这个名字」。"),
        new(
            ["RunContractsInteraction"],
            "响应契约那一屏的同源判据落在 ReadFullTextAndCheck 段（Pairs 第三行：状态行完整错误入口），"
            + "RunContractsInteraction 判的是五段交互与落盘，不是同源表达式。"),
    ];

    /// <summary>
    /// <b>覆盖率门</b>：凡「冒烟器里有这一段 + 测试工程里有 <c>*SmokeTests.cs</c> 引用它」这对特征成立的，
    /// 就必须在 <see cref="Pairs"/> 里出现，或在 <see cref="Exemptions"/> 里登记一行。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="Pairs"/> 的类注释从第二百零二轮起就写着「新增一屏要在这里加一行」，
    /// 但<b>没有任何一条门判「是不是漏了」</b>——下一屏出现时，漏登记的表现与「这一屏本来就不该登记」
    /// 一模一样，仍然只有 wine 跑一次才看得见两边矛盾。这条门把「记得加」变成「不加就红」。
    /// </para>
    /// <para>
    /// <b>判据不发明新口径</b>：特征就是已有三屏各自的样子——冒烟器里有段、测试工程里有
    /// <c>*SmokeTests.cs</c> 提到它（<c>nameof(Xxx)</c> 或字面量 <c>"Xxx"</c>）。它<b>不要求</b>每一屏都进
    /// <see cref="Pairs"/>：确实不适合的，在 <see cref="Exemptions"/> 里登记一行说明即可。
    /// </para>
    /// </summary>
    [Fact]
    public void EverySmokeSectionWithAStaticGate_IsListedInPairs()
    {
        var smokeTests = Directory
            .GetFiles(Path.Combine(Repo.Root, "ApifoxHelper.Tests"), "*SmokeTests.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Name: Path.GetFileName(f), Text: File.ReadAllText(f)))
            .ToList();
        Assert.NotEmpty(smokeTests);

        var paired = Pairs.Select(p => p.SmokeSection).ToHashSet(StringComparer.Ordinal);
        var exempt = Exemptions.SelectMany(e => e.Sections).ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var section in RegisteredSections())
        {
            // 名字边界：`nameof(Xxx)` 或整串字面量 `"Xxx"`——子串取法会把 RunXxx 与 RunXxxY 混成一个。
            var pattern = @"nameof\(" + Regex.Escape(section) + @"\)|""" + Regex.Escape(section) + @"""";
            var referencing = smokeTests
                .Where(t => Regex.IsMatch(t.Text, pattern))
                .Select(t => t.Name)
                .ToList();
            if (referencing.Count == 0) continue; // 没有静态门那一半，不在本门判的范围
            if (paired.Contains(section) || exempt.Contains(section)) continue;
            missing.Add($"{section}（被 {string.Join(" / ", referencing)} 引用）");
        }

        Assert.True(missing.Count == 0,
            "下面这些冒烟段已经有 *SmokeTests.cs 引用它（= 静态门那一半已经在），却既不在 Pairs 里、也没登记豁免："
            + string.Join("；", missing)
            + "。在 Pairs 里加一行判同源，或在 Exemptions 里登记一行说明这一屏为什么不适合——"
            + "不登记就是这一屏没人判同源，与第二百零一轮之前一样：只有 wine 跑一次才看得见两边矛盾。");

        // 豁免登记本身也要是活的：每一段都真的还在冒烟器里、且没有与 Pairs 重复登记。
        var registered = RegisteredSections().ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exemption in Exemptions)
        {
            Assert.True(exemption.Reason.Length >= 20,
                "Exemptions 里有一条没写理由（或理由短到看不出为什么）：只登记不写理由，下一个人会以为这一屏漏了。");
            Assert.NotEmpty(exemption.Sections);
            foreach (var section in exemption.Sections)
            {
                Assert.True(registered.Contains(section),
                    $"Exemptions 登记了 {section}，冒烟器里没有这一段——豁免指向一个不存在的段，等于没有豁免。");
                Assert.False(paired.Contains(section),
                    $"{section} 既在 Pairs 里又在 Exemptions 里：同一屏两种登记，改了一处另一处不会红。");
                Assert.True(seen.Add(section), $"{section} 在 Exemptions 里登记了两次。");
            }
        }
    }

    /// <summary>
    /// 声明了就要注册：冒烟器里每一个 <c>private static void RunXxx(</c> 都必须在 <c>Main</c> 里
    /// 以 <c>Section("…", nameof(RunXxx))</c> 注册过，反过来注册表里也不许出现没有方法体的名字。
    /// <para>
    /// 只判「方法存在」不够：段写在文件里但没注册，报告里永远不会出现这一段，演示脚本的逐屏对账
    /// 也就少了一屏——而所有静态门照旧全绿。这是「漏屏」的另一种形态，与上面那条覆盖率门同一族。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDeclaredSection_IsRegisteredInMain()
    {
        var declared = DeclaredSections()
            .Where(n => !SmokeHelpers.Contains(n, StringComparer.Ordinal))
            .ToList();
        var registered = RegisteredSections();

        var unregistered = declared.Except(registered, StringComparer.Ordinal).ToList();
        Assert.True(unregistered.Count == 0,
            $"冒烟器里声明了 {string.Join(" / ", unregistered)}，但 Main 里没有 Section(…, nameof(…)) 注册："
            + "这一段永远不会跑，报告里也不会出现——要么注册，要么删掉。");

        var dangling = registered.Except(declared, StringComparer.Ordinal).ToList();
        Assert.True(dangling.Count == 0,
            $"Main 里注册了 {string.Join(" / ", dangling)}，冒烟器里没有这个方法：注册表指向不存在的段。");
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
