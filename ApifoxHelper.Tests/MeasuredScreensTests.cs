using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「哪些屏真的显示过」这件事在仓里第一次有出处、有门（第二百四十六轮，简报 P0-1，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十三轮量到的那件事——冒烟器每一段都只「建句柄 + <c>CreateControl</c> +
/// <c>PerformLayout</c>」，<b>从不显示窗体</b>，而一张没显示过的清单 <c>ClientSize</c> 的高就是 0。
/// 于是「按预算估」冒充了「量过」。当时把这件事写进了 commit message 与 <c>docs/NOTES.md</c>，
/// 但<b>判据、读数、挂账三者在仓里没有一处对账</b>：哪一屏真的 <c>Show()</c> 过、量过的是哪一轮，
/// 全靠人读 <c>ApifoxHelper.Smoke/Program.cs</c> 那七千行去认。下一轮再加一屏，同样的事会再发生一次。
/// </para>
/// <para>
/// <b>这一条判什么</b>：拿 <see cref="MeasuredScreens.All"/> 这份登记与冒烟器源码对账，四件事——
/// ① 冒烟器里凡出现 <c>.Show();</c> 的那一段，登记里必须有一行认领它、标「已量」、且轮次非空；
/// ② 登记里标「未量」的屏，它那一段里不得出现 <c>.Show();</c>（标了没量却偷偷显示了，登记就是假的）；
/// ③ 每一行的出处必须真的在它引的那份台账里（轮次逐字可查）——只写「已量」而不写哪一轮，
/// 下一个人无法判断这条登记是否过期（本仓已有一次「台账滞后十二轮」的先例，见 <c>docs/DESIGN.md</c> §8）；
/// ④ 冒烟器声明要构造的每个对话框 / 页面类都必须被某一行认领，且登记里不许出现冒烟器不构造的类
/// （两侧对账，与 <c>NativeDialogSmokeTests</c> 对 <c>SmokeDialogNames</c> 的取法同一形状）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「量到的数对不对」——「显示过」与「量对了」是两件事，后者只有人读得出来。
/// 也不判「这一屏该不该显示」：登记的是<b>现状</b>，不是目标。
/// </para>
/// <para>
/// <b>为什么判据写成「文本进、问题出」的一段函数</b>：本仓的教训是「判据自己没被跑过」比「没有判据」更贵
/// （第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿都是这一种）。所以下面那条
/// <see cref="Detector_ReportsTheBreaksItMustCatch"/> 拿六发注入各喂一遍，当场验这一条抓不抓得住。
/// </para>
/// </summary>
public class MeasuredScreensTests
{
    /// <summary>
    /// 允许当「出处」的三份台账。<c>docs/DESIGN.md</c> 也在里面：§8 那张表是「哪一处收口了、哪一轮收的」
    /// 的正式台账，标识格那一行（第二百二十八轮）只在那里有记录，<c>docs/NOTES.md</c> 里没有。
    /// </summary>
    private static readonly string[] LedgerDocs = ["docs/NOTES.md", "docs/CRITIQUE.md", "docs/DESIGN.md"];

    /// <summary>冒烟器里「真的把窗体显示出来」的那一句。只认这一种写法：<c>ShowDialog()</c> 是另一件事。</summary>
    private const string ShowCall = ".Show();";

    /// <summary>冒烟器里的一个方法：叫什么、方法体（含花括号或表达式体）。</summary>
    private readonly record struct SmokeMethod(string Name, string Body);

    /// <summary>
    /// 仓里当前那一份：逐条对账，把每一处「登记与冒烟器对不上」说出来（空 = 对得上）。
    /// </summary>
    [Fact]
    public void EveryScreenTheSmokeShows_IsRegisteredAsMeasured()
    {
        var problems = Problems(Methods(), MeasuredScreens.All, DocText, DeclaredClasses());
        Assert.Empty(problems);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：六发注入各喂一次，每一发都必须报出自己那一处。
    /// <para>
    /// 六发分别是：① 显示了窗体却标「未量」；② 显示了窗体却根本没登记；③ 标了「已量」却没写轮次；
    /// ④ 出处指向一份台账里查不到的轮次；⑤ 冒烟器要构造的类没人认领；⑥ 合规写法（不报）。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // 夹具：一段真的显示了窗体、一段只建句柄；一行已量、一行未量；一份台账里写着第一百轮。
        var methods = new List<SmokeMethod>
        {
            new("RunShown", "using var dialog = new FooDialog();\ndialog.Show();\nApplication.DoEvents();"),
            new("RunQuiet", "using var dialog = new BarDialog();\n_ = dialog.Handle;\ndialog.CreateControl();"),
        };
        var docs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["docs/NOTES.md"] = "## 第一百轮\n- 这一段真的显示过。\n",
        };
        var declared = new[] { "FooDialog", "BarDialog" };

        MeasuredScreens.Row Shown(bool measured = true, string round = "第一百轮") =>
            new("显示过的那一屏", "RunShown", ["FooDialog"], measured, round, "docs/NOTES.md 第一百轮：这一段真的显示过");

        MeasuredScreens.Row Quiet() =>
            new("只构造过的那一屏", "RunQuiet", ["BarDialog"], false, "第一百轮", "docs/NOTES.md 第一百轮：这一段真的显示过");

        // ⑥ 合规写法：一行已量、一行未量，两侧都对得上——不报。
        Assert.Empty(Problems(methods, [Shown(), Quiet()], p => docs.GetValueOrDefault(p), declared));

        // ① 显示了窗体却标「未量」：两处各报一次（「没标已量」与「标了未量却出现 Show()」是两件事）。
        var mislabelled = Problems(methods, [Shown(measured: false), Quiet()], p => docs.GetValueOrDefault(p), declared);
        Assert.Equal(2, mislabelled.Count);
        Assert.Contains(mislabelled, p => p.Contains("RunShown", StringComparison.Ordinal) && p.Contains("未量", StringComparison.Ordinal));

        // ② 显示了窗体却根本没登记：冒烟器里那一段没人认领。
        var unregistered = Problems(methods, [Quiet()], p => docs.GetValueOrDefault(p), declared);
        Assert.Contains(unregistered, p => p.Contains("RunShown", StringComparison.Ordinal) && p.Contains("没有一行认领", StringComparison.Ordinal));

        // ③ 标了「已量」却没写轮次：下一个人无法判断这条登记是否过期。
        var noRound = Problems(methods, [Shown(round: "  "), Quiet()], p => docs.GetValueOrDefault(p), declared);
        Assert.Contains(noRound, p => p.Contains("轮次", StringComparison.Ordinal));

        // ④ 出处指向一份台账里查不到的轮次：登记说「第一百轮量过」，台账里没有这一轮。
        var badRound = Problems(methods, [Shown(round: "第九十九轮"), Quiet()], p => docs.GetValueOrDefault(p), declared);
        Assert.Contains(badRound, p => p.Contains("第九十九轮", StringComparison.Ordinal) && p.Contains("查不到", StringComparison.Ordinal));

        // ⑤ 冒烟器要构造的类没人认领：BarDialog 从登记里掉了。
        var orphan = Problems(methods, [Shown()], p => docs.GetValueOrDefault(p), declared);
        Assert.Contains(orphan, p => p.Contains("BarDialog", StringComparison.Ordinal) && p.Contains("没有一行认领", StringComparison.Ordinal));
    }

    /// <summary>
    /// 把一批冒烟方法与一批登记喂给判据，返回「哪几处对不上」的说明（空 = 全对得上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>WiringAssertionShapeTests.Problems</c>、<c>MutationVerificationTests.Mismatches</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> Problems(
        IReadOnlyList<SmokeMethod> methods,
        IReadOnlyList<MeasuredScreens.Row> rows,
        Func<string, string?> docText,
        IReadOnlyCollection<string> declaredClasses)
    {
        var problems = new List<string>();
        var bodies = methods.ToDictionary(m => m.Name, m => m.Body, StringComparer.Ordinal);

        // ① 显示了窗体的段，登记里必须有一行认领它、标「已量」、轮次非空。
        foreach (var method in methods)
        {
            if (!method.Body.Contains(ShowCall, StringComparison.Ordinal)) continue;

            var row = rows.FirstOrDefault(r => r.Section == method.Name);
            if (row is null)
            {
                problems.Add($"冒烟段 {method.Name} 里出现了 {ShowCall}，而 MeasuredScreens 里没有一行认领这一段："
                    + "「这一屏真的显示过」这件事没有出处，下一轮读登记的人会以为它没量过。");
                continue;
            }

            if (!row.Measured)
            {
                problems.Add($"冒烟段 {method.Name} 里出现了 {ShowCall}，而登记里「{row.Screen}」标的是未量："
                    + "标了没量却真的显示了，这条登记就是假的——要么改登记，要么把 Show() 去掉。");
            }

            if (string.IsNullOrWhiteSpace(row.Round))
            {
                problems.Add($"登记里「{row.Screen}」标了已量却没写轮次：下一个人无法判断这条登记是否过期"
                    + "（本仓已有一次「台账滞后十二轮」的先例，见 docs/DESIGN.md §8）。");
            }
        }

        // ② 标「未量」的屏，它那一段里不得出现 Show()。
        foreach (var row in rows.Where(r => !r.Measured))
        {
            if (row.Section == MeasuredScreens.NoSection) continue;
            if (!bodies.TryGetValue(row.Section, out var body))
            {
                problems.Add($"登记里「{row.Screen}」把段写成 {row.Section}，而冒烟器里没有这个方法："
                    + "挂账指到一个不存在的地方，等于没有挂账。");
                continue;
            }

            if (body.Contains(ShowCall, StringComparison.Ordinal))
            {
                problems.Add($"登记里「{row.Screen}」标的是未量，而它那一段 {row.Section} 里出现了 {ShowCall}："
                    + "这一屏其实显示过，登记该改成已量并写清是哪一轮量的。");
            }
        }

        // ③ 每一行的出处必须真的在它引的那份台账里（轮次逐字可查）。
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Round))
            {
                problems.Add($"登记里「{row.Screen}」的轮次是空的：出处无从可核。");
            }

            var space = row.Note.IndexOf(' ', StringComparison.Ordinal);
            var path = space < 0 ? row.Note : row.Note[..space];
            if (!LedgerDocs.Contains(path, StringComparer.Ordinal))
            {
                problems.Add($"登记里「{row.Screen}」的出处指向「{path}」，而台账只认 {string.Join(" / ", LedgerDocs)}："
                    + "别处的记录这张表核不到。");
                continue;
            }

            var text = docText(path);
            if (text is null)
            {
                problems.Add($"登记里「{row.Screen}」引的台账 {path} 在仓里不存在。");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.Round) && !text.Contains(row.Round, StringComparison.Ordinal))
            {
                problems.Add($"登记里「{row.Screen}」说「{row.Round}」量过，而 {path} 里查不到这一轮："
                    + "登记与台账对不上，读的人拿不到那一轮的读数。");
            }
        }

        // ④ 两侧对账：冒烟器声明要构造的类 ↔ 登记里认领的类。
        var claimed = rows.SelectMany(r => r.Classes).ToHashSet(StringComparer.Ordinal);
        foreach (var name in declaredClasses)
        {
            if (!claimed.Contains(name))
            {
                problems.Add($"冒烟器声明要构造 {name}，而 MeasuredScreens 里没有一行认领它："
                    + "这一屏是「显示过」还是「只构造过」，登记里查不到。");
            }
        }

        foreach (var name in claimed)
        {
            if (!declaredClasses.Contains(name))
            {
                problems.Add($"MeasuredScreens 里认领了 {name}，而冒烟器没有声明要构造它："
                    + "登记指着一个冒烟器不碰的类，这一行是凭空写的。");
            }
        }

        return problems;
    }

    /// <summary>台账正文（读不到返回 <c>null</c>，由判据报「不存在」而不是抛异常）。</summary>
    private static string? DocText(string relative)
    {
        var path = Path.Combine(Repo.Root, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>
    /// 冒烟器声明要构造的对话框与页面类名——<b>读冒烟器自己那份数据</b>，不在这里抄第二份。
    /// <para>
    /// <c>SmokeDialogNames</c> / <c>SmokePageNames</c> 是 <c>ApifoxHelper.Smoke</c> 里的 <c>internal</c> 数组，
    /// 测试工程不引用那个程序集（它是 <c>net8.0-windows</c>），所以从源码里取字面量。
    /// 取法与 <c>NativeDialogSmokeTests</c> 同一形状：只认 <c>"XxxDialog",</c> 这一种行。
    /// </para>
    /// </summary>
    private static List<string> DeclaredClasses()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var names = new List<string>();
        foreach (var field in new[] { "SmokeDialogNames", "SmokePageNames" })
        {
            var start = smoke.IndexOf($"internal static readonly string[] {field}", StringComparison.Ordinal);
            Assert.True(start >= 0, $"ApifoxHelper.Smoke/Program.cs 里找不到 {field}：冒烟器的类清单改名或搬走了，本门需要重定取法。");
            // 从等号之后找 '['：声明里的 string[] 自带一个方括号，从 start 起找会取到它。
            var equals = smoke.IndexOf('=', start);
            Assert.True(equals > start, $"{field} 的声明里找不到等号。");
            var open = smoke.IndexOf('[', equals);
            var close = smoke.IndexOf(']', open);
            Assert.True(open > equals && close > open, $"{field} 的数组字面量取不到（应为 [ … ]）。");
            foreach (Match match in Regex.Matches(smoke[(open + 1)..close], "\"(?<name>[A-Za-z0-9_]+)\""))
            {
                names.Add(match.Groups["name"].Value);
            }
        }

        Assert.True(names.Count > 0, "冒烟器的类清单解析出 0 个类名：取法漂了，本门会静默全绿。");
        return names;
    }

    /// <summary>
    /// 冒烟器里的方法（<c>private static … Name(</c>，缩进四个空格 = 顶层成员）。
    /// <para>
    /// <b>为什么按缩进取</b>：方法体里还有局部函数（<c>TaskList</c> / <c>JournalList</c> 那几个），
    /// 它们缩进更深、也不是「冒烟段」。按缩进取顶层成员，与 <c>Section(nameof(RunXxx))</c> 那一族
    /// 认的「段」是同一批东西。
    /// </para>
    /// <para>
    /// <b>为什么表达式体也要取</b>：<c>private static ListView TaskList(Control root) =&gt; …</c> 这种写法
    /// 没有花括号，只按花括号配对会把它后面那一大段算进它的方法体——第二百四十六轮第一版就错在这里，
    /// 于是 <c>TaskList</c> 被当成「显示了窗体的段」报了出来。
    /// </para>
    /// </summary>
    private static List<SmokeMethod> Methods()
    {
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        var methods = new List<SmokeMethod>();
        foreach (Match match in Regex.Matches(
            smoke,
            @"(?m)^    (?:private|internal) static [^\n]*?\b(?<name>[A-Za-z0-9_]+)\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(2)))
        {
            var body = Body(smoke, match.Index + match.Length);
            if (body.Length == 0) continue;
            methods.Add(new SmokeMethod(match.Groups["name"].Value, body));
        }

        Assert.True(methods.Count > 50, $"冒烟器只解析出 {methods.Count} 个方法：取法漂了，本门会静默全绿。");
        return methods;
    }

    /// <summary>
    /// 从参数表右括号之后取方法体：<c>{ … }</c> 按花括号配对，<c>=&gt; …;</c> 取到同层的分号。
    /// 取不到返回空串。
    /// </summary>
    private static string Body(string text, int from)
    {
        // 先跳过参数表：from 落在左括号之后，所以从深度 1 起配对圆括号，找到右括号。
        var depth = 1;
        var i = from;
        for (; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')')
            {
                depth--;
                if (depth == 0) break;
            }
        }

        if (i >= text.Length) return string.Empty;

        // 再跳过空白，看是块体还是表达式体。
        var j = i + 1;
        while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
        if (j >= text.Length) return string.Empty;

        if (text[j] == '{')
        {
            var braces = 0;
            for (var k = j; k < text.Length; k++)
            {
                if (text[k] == '{') braces++;
                else if (text[k] == '}')
                {
                    braces--;
                    if (braces == 0) return text[j..(k + 1)];
                }
            }

            return string.Empty;
        }

        if (text[j] == '=' && j + 1 < text.Length && text[j + 1] == '>')
        {
            var parens = 0;
            var braces = 0;
            for (var k = j + 2; k < text.Length; k++)
            {
                switch (text[k])
                {
                    case '(': parens++; break;
                    case ')': parens--; break;
                    case '{': braces++; break;
                    case '}': braces--; break;
                    case ';' when parens == 0 && braces == 0: return text[j..(k + 1)];
                }
            }
        }

        return string.Empty;
    }
}
