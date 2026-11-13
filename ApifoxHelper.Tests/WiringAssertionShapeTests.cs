using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 接线型断言必须判「<b>有几处</b>」，不许只判「<b>有没有</b>」（第二百四十五轮，简报 P1-3，具名 pass：harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十三轮那一发变异（M4）<b>第一次是假绿</b>——把 <c>dialog.Show();</c>
/// 从产品路径上挪走，门照样全绿，因为那条断言写的是 <c>Assert.Contains("dialog.Show();", 整份源文)</c>，
/// 而同一文件别处还有一处 <c>dialog.Show();</c> 把它满足了。改成判<b>次数</b>（<c>Count(...)</c> 恰等于几）
/// 之后才红。那一课当时只写进了 commit message 与 <c>docs/NOTES.md</c>，<b>仓里没有一条门防它复发</b>：
/// 下一轮再写一条 <c>Contains</c> 形态的接线断言，构建与全部用例照旧全绿，而它判的仍然是「这份文件里
/// 某个地方有这句话」——那与「这一处接上了」不是同一件事。
/// </para>
/// <para>
/// <b>这一条判什么</b>：扫 <c>ApifoxHelper.Tests</c> 下所有<b>名字里带 <c>Wiring</c> / <c>Wired</c> / <c>接线</c></b>
/// 的测试方法（「接线」这件事的自述就在名字里，名字是这一族唯一的登记处），逐条判两件事：
/// ① <b>至少有一句判数的断言</b>（<see cref="CountingForms"/> 里那几种形态之一）——只有
/// <c>Assert.Contains</c> / <c>Assert.True(x &gt; 0)</c> 的测试判的是「有没有」；
/// ② <b>不许出现「有没有」形态</b>（<see cref="PresenceShapes"/>：<c>Assert.True(x &gt; 0)</c>、
/// <c>Assert.True(x &gt;= 1)</c>、<c>Assert.True(x != 0)</c>、<c>Assert.True(x.Any())</c>、
/// <c>Assert.False(x == 0)</c>）——这一族与 <c>Contains</c> 是同一个意思的两种写法，多一处就多一次假绿。
/// </para>
/// <para>
/// <b>它不判什么</b>：<b>不判断言写得对不对</b>——「判了 1 次」与「判的是该判的那一处」是两件事，
/// 后者只有人读得出来。<b>也不判名字起得对不对</b>：一条接线测试改名叫 <c>Foo</c> 就绕过了这一条，
/// 而绕过它的人得先想清楚「我为什么不想让它被数一遍」——这一条挡的是<b>顺手</b>，不是<b>故意</b>。
/// </para>
/// <para>
/// <b>为什么判据写成「文本进、问题出」的一段函数</b>：本仓的教训是「判据自己没被跑过」比「没有判据」更贵
/// （第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿都是这一种）。所以下面那条
/// <see cref="ShapeDetector_ReportsTheBreaksItMustCatch"/> 拿 M4 那一发的<b>原样</b>当夹具，
/// 当场验这一条抓不抓得住。
/// </para>
/// </summary>
public class WiringAssertionShapeTests
{
    /// <summary>
    /// 判数的形态：出现任一种，这条接线测试就至少有一处判的是「有几处」。
    /// <para>
    /// <c>Assert.DoesNotContain</c> 也在里面，但只对<b>否定式</b>接线测试成立（「这一屏不接这个口径」）——
    /// 那种断言在整份源文上判「一处都没有」，与 <c>Assert.Equal(0, Count(...))</c> 是同一个意思，
    /// 不存在「被别处满足」的问题。肯定式接线测试若只写 <c>DoesNotContain</c>，它压根没在断言接线。
    /// </para>
    /// </summary>
    private static readonly string[] CountingForms =
    [
        "Assert.Single(",
        "Assert.Empty(",
        "Assert.Matches(",
        "Assert.DoesNotMatch(",
        "Assert.Equal(",
        "Assert.DoesNotContain(",
    ];

    /// <summary>
    /// 「有没有」形态：<c>Assert.True</c> / <c>Assert.False</c> 的条件里出现这些比较，判的就是「至少一处」。
    /// <para>
    /// 只认这五种写法是故意的：<c>Assert.True(x == 0)</c> 与 <c>Assert.True(x == 1)</c> 判的是<b>数</b>，
    /// 不在其中；<c>Assert.True(hint &gt;= 0 &amp;&amp; restore &gt; hint)</c> 这种位置比较也不在其中
    /// （它判的是先后，不是条数）。
    /// </para>
    /// </summary>
    private static readonly Regex PresenceShapes = new(
        @"(?:>\s*0\b|>=\s*1\b|!=\s*0\b|\.Any\(\))",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary><c>Assert.False</c> 那一侧的「有没有」：<c>Assert.False(x == 0)</c> 与 <c>Assert.False(x.Any())</c>。</summary>
    private static readonly Regex PresenceShapesWhenFalse = new(
        @"(?:==\s*0\b|<\s*1\b|\.Any\(\))",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>一条 <c>Assert.True(</c> / <c>Assert.False(</c> 的第一个参数（条件本身，不含失败消息）。</summary>
    private static readonly Regex BooleanAssertion = new(
        @"Assert\.(?<kind>True|False)\((?<cond>[^,;]*?)(?:,|\)\s*;)",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>测试方法名里出现这三个词之一，就算「接线型」——名字是这一族唯一的登记处。</summary>
    private static readonly Regex WiringName = new(
        @"Wiring|Wired|接线",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// 白名单：方法名 → 理由。<b>现在是空的，空着是故意的</b>。
    /// <para>
    /// 加一条必须写清「为什么这一条判不了几处」——不是「来不及改」。这一族最贵的绿正是「先挂白名单、
    /// 下一轮忘了」；空表意味着今天每一条接线测试都真的在数。
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Whitelist = new(StringComparer.Ordinal);

    /// <summary>一条测试方法：哪个文件、叫什么、方法体（含花括号）。</summary>
    private readonly record struct TestBody(string File, string Name, string Body);

    /// <summary>
    /// 仓里当前那一份：逐条判，把每一处「只判有没有」说出来（空 = 全都在数）。
    /// </summary>
    [Fact]
    public void EveryWiringNamedTest_JudgesHowManyNotWhether()
    {
        var problems = Problems(TestMethods(), Whitelist);
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 第一发就是第二百四十三轮 M4 那一发的<b>原样</b>（<c>Assert.Contains("dialog.Show();", 整份源文)</c>）——
    /// 它当时是绿的，这一条必须红。第二发是同一个意思的另一种写法（<c>Assert.True(calls &gt; 0)</c>），
    /// 第三发是白名单机制本身（登记了就不报），第四发是合规写法（判了次数就不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void ShapeDetector_ReportsTheBreaksItMustCatch()
    {
        // ① M4 那一发的原样：整份源文上 Contains，别处还有一处把它满足了。
        var m4 = new TestBody(
            "SyncPreviewPanesTests.cs",
            "Dialog_IsWiredOnTheProductPath",
            """
            var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
            Assert.Contains("dialog.Show();", source, StringComparison.Ordinal);
            """);
        Assert.Single(Problems([m4], Whitelist));

        // ② 同一个意思的另一种写法：数出来再判「大于零」。
        var presence = new TestBody(
            "GateSourceConsistencyTests.cs",
            "EveryPair_HasBothHalvesAndTheSmokeSectionIsWired",
            """
            var calls = program.Split('\n').Count(l => Regex.IsMatch(l, @"^\s+" + name + @"\("));
            Assert.True(calls > 0, "冒烟段没有真的被调用。");
            """);
        // 这一发同时踩两条：既没有判数的断言，又有一句「数出来再判大于零」。
        Assert.Equal(2, Problems([presence], Whitelist).Count);

        // ③ 白名单机制本身：登记了就不报（否则「显式白名单 + 理由」这句话是空的）。
        var registered = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Dialog_IsWiredOnTheProductPath"] = "夹具：这一条判的是白名单机制，不是产品。",
        };
        Assert.Empty(Problems([m4], registered));

        // ④ 合规写法：判了次数就不报（这一条挡的是「顺手」，不是「一律不许 Contains」）。
        var counted = new TestBody(
            "SyncPreviewPanesTests.cs",
            "Dialog_IsWiredOnTheProductPath",
            """
            var source = Repo.Source("Desktop", "Native", "SyncPreviewDialog.cs");
            Assert.Equal(1, Count(source, "dialog.Show();"));
            """);
        Assert.Empty(Problems([counted], Whitelist));

        // ⑤ 名字里没有那三个词的测试不受这一条管（这一条只判「接线」这一族，不判全部断言）。
        var unrelated = new TestBody("SomeTests.cs", "Copy_IsShort", m4.Body);
        Assert.Empty(Problems([unrelated], Whitelist));
    }

    /// <summary>
    /// 把一批测试喂给判据，返回「哪几处只判有没有」的说明（空 = 全都在数）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>MutationVerificationTests.Mismatches</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(IEnumerable<TestBody> tests, IReadOnlyDictionary<string, string> whitelist)
    {
        var problems = new List<string>();
        foreach (var test in tests)
        {
            if (!WiringName.IsMatch(test.Name)) continue;
            if (whitelist.ContainsKey(test.Name)) continue;

            if (!CountingForms.Any(form => test.Body.Contains(form, StringComparison.Ordinal)))
            {
                problems.Add($"{test.File} 的 {test.Name} 里没有一句判数的断言（{string.Join(" / ", CountingForms)} 一个都没有）："
                    + "它判的是「这份文件里某个地方有这句话」，而别处还有一处就能把它满足——"
                    + "第二百四十三轮 M4 那一发假绿正是这么来的。");
            }

            foreach (Match match in BooleanAssertion.Matches(test.Body))
            {
                var condition = match.Groups["cond"].Value;
                var shape = match.Groups["kind"].Value == "True" ? PresenceShapes : PresenceShapesWhenFalse;
                if (!shape.IsMatch(condition)) continue;
                problems.Add($"{test.File} 的 {test.Name} 里有一句「有没有」形态的断言（Assert.{match.Groups["kind"].Value}({condition.Trim()})）："
                    + "数出来再判「大于零」与直接 Contains 是同一个意思，两处都判不出「接了几处」。");
            }
        }

        return problems;
    }

    /// <summary>
    /// <c>ApifoxHelper.Tests</c> 下所有测试方法（<c>public void X(</c> / <c>public async Task X(</c>），
    /// 方法体按花括号配对取到闭合那一处。
    /// <para>
    /// 只扫这一层目录、不递归：<c>bin/</c> 与 <c>obj/</c> 里是编译产物，扫进去等于把同一份源码数两遍。
    /// </para>
    /// </summary>
    private static List<TestBody> TestMethods()
    {
        var directory = Path.Combine(Repo.Root, "ApifoxHelper.Tests");
        var methods = new List<TestBody>();
        foreach (var path in Directory.GetFiles(directory, "*.cs").OrderBy(p => p, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);
            foreach (Match match in Regex.Matches(
                text,
                @"public\s+(?:async\s+)?(?:void|Task)\s+(?<name>[A-Za-z0-9_]+)\s*\(",
                RegexOptions.None,
                TimeSpan.FromSeconds(2)))
            {
                var body = Body(text, match.Index + match.Length);
                if (body.Length == 0) continue;
                methods.Add(new TestBody(Path.GetFileName(path), match.Groups["name"].Value, body));
            }
        }

        return methods;
    }

    /// <summary>从 <paramref name="from"/> 之后第一个 <c>{</c> 起，按花括号配对取到闭合那一处（取不到返回空串）。</summary>
    private static string Body(string text, int from)
    {
        var open = text.IndexOf('{', from);
        if (open < 0) return string.Empty;
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text[open..(i + 1)];
            }
        }

        return string.Empty;
    }
}
