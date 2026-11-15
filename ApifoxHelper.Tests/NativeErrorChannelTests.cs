using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「原生界面每一处 catch 把异常怎么着了」的口径，与 <c>docs/NOTES.md</c>
/// 「原生界面错误文案的两条通道」那一节的普查表对账（第三百四十六轮，控制仓简报 P0-2 选 B 的那一半）。
/// <para>
/// <b>为什么要有这个文件</b>：第三百四十五轮把「那 59 处念脱敏消息要不要收成一处」登成一处本轮不收的分岔，
/// 简报第三百四十六轮要求在「A 收口 / B 登记」里选一个，<b>不允许既不收也不登记</b>。本轮选 B，
/// 理由与普查的数写在那一节；而登记要是不与实物绑着，它就只是一段会过期的散文——
/// 下一轮新增一处 <c>catch (Exception) { }</c>（宽口径吞掉、又不念、又不记），
/// 构建与全部用例照旧全绿，那一节的表却还写着上一轮数出来的那几个数。
/// </para>
/// <para>
/// <b>判两件事</b>：
/// ① <see cref="EverySite_NarrowsOrNamesOrLogs"/>——每一处 catch 至少要做到三件事之一：
/// <b>念一句原因</b>（脱敏后的异常消息，或那条授权文案通道）、<b>记一条日志</b>、或<b>把范围收窄</b>
/// （<c>catch</c> 的类型不是 <c>Exception</c>，或带 <c>when (…)</c> 筛选——两种方式任一，窄类型本身就是收窄）。
/// 今天 111 处都满足；三件都不做的写法——<c>catch (Exception) { }</c>、
/// <c>catch (Exception ex) { return null; }</c>——当场红并点名到行。
/// 「还要留一句为什么」这一件<b>不在门里</b>：注释好不好只有人读得出来，形状门判它只会多一条永远为真的断言
/// （同一取舍见 <c>docs/NOTES.md</c>「跨方法隐式不变量登记」那一节）。
/// ② <see cref="RegisteredCounts_MatchTheCensus"/>——那一节那张表的五个处置各有多少处，
/// 与现在源码里数出来的逐一对照；源码长了一处而登记没跟上即红，登记改了数而源码没动也即红。
/// </para>
/// <para>
/// <b>按顶那五步判属不属于「假门防护」那一族</b>（第二百五十六轮收口那一族）：① 断言里出现的口径名是
/// <c>docs/NOTES.md</c> 那张表的处置名与处数，以及 <c>Desktop/Native/*.cs</c> 里的 catch 站点；
/// ② 两者<b>互不在</b>对方里定义（一张表定义不了界面源码有几处 catch，一段源码也定义不了表里那个数）
/// → 第 ③ 步答「不是」→ <b>对账门</b>，随实物一起长，不属那一族。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判念出来的那一句在屏幕上放不放得下（随 G-D5 那三档截图）；不判收窄收得对不对
/// （<c>catch (InvalidOperationException)</c> 该不该在这儿吞掉，那是人读得出来的判断）；
/// 也不判那 61 处「念脱敏原因」的运行时行为——那一支的账写在本节「本节不判什么」里。
/// </para>
/// </summary>
public class NativeErrorChannelTests
{
    /// <summary>普查表里那五个处置名，逐字取自 <c>docs/NOTES.md</c> 那一节表的第一格。</summary>
    public const string StatusLine = "状态行念脱敏原因";

    public const string FailureState = "失败态整段念脱敏原因";
    public const string AuthorisedCopy = "原样念授权文案";
    public const string KeptQuiet = "不念异常消息（收窄后自己处理）";
    public const string LoggedOnly = "只进日志";

    /// <summary>一处 catch 站点：文件名、行号、catch 的类型、有没有 when 筛选、体、上面那一行。</summary>
    private sealed record Site(string File, int Line, string Type, bool Filtered, string Body, string LineAbove);

    /// <summary>
    /// catch 头的取法：<c>catch (Type)</c> 与 <c>catch (Type name)</c> 两种写法都要读到——
    /// 第一版只读到带变量名的那一种，于是 <c>catch (Exception)</c> 空着收尾那一发注入<b>读成「没有这一处」</b>，
    /// 判据绿着漏掉它要挡的那一件事。变量名可省，类型不可省；收窄判的是类型，不是名字。
    /// </summary>
    private static readonly Regex Header = new(@"catch\s*\(\s*([A-Za-z_][\w.<>]*)\s*(?:\w+)?\s*\)");

    /// <summary>现读：`Desktop/Native/` 下全部 catch 站点（类型不限，共 111 处）。</summary>
    private static List<Site> Sites() => FromText(Repo.NativeSources().Select(s => (s.Name, s.Text)));

    /// <summary>
    /// 站点读取本体：仓里全部界面源码与注入的片段走<b>同一把取法</b>。
    /// 分成两份（一份读仓、一份读片段）的注入实测判的是第二份，判不到仓里那一把写坏没坏。
    /// </summary>
    private static List<Site> FromText(IEnumerable<(string Name, string Text)> sources)
    {
        var found = new List<Site>();
        foreach (var (name, text) in sources)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var match = Header.Match(lines[i]);
                if (!match.Success) continue;
                var body = new List<string>();
                var depth = 0;
                for (var j = i + 1; j < lines.Length; j++)
                {
                    body.Add(lines[j]);
                    depth += lines[j].Count(c => c == '{') - lines[j].Count(c => c == '}');
                    if (depth <= 0 && lines[j].Contains('}')) break;
                }

                found.Add(new Site(name, i + 1, match.Groups[1].Value,
                    lines[i].Contains("when (", StringComparison.Ordinal),
                    string.Join('\n', body), i > 0 ? lines[i - 1] : ""));
            }
        }

        return found;
    }

    /// <summary>
    /// 每一处 catch：念原因、记日志、收窄——三件至少做了一件。
    /// </summary>
    [Fact]
    public void EverySite_NarrowsOrNamesOrLogs() => Assert.Empty(ChannelProblems(Sites()));

    /// <summary>判据本体（上面那条与下面的注入实测共用同一份，不各写一遍）。</summary>
    private static List<string> ChannelProblems(IEnumerable<Site> sites)
    {
        var problems = new List<string>();
        foreach (var site in sites)
        {
            if (Disposition(site) is not (KeptQuiet or LoggedOnly) && NamesReason(site)) continue;
            var narrowed = site.Type != "Exception" || site.Filtered;
            var logged = Logs(site);
            if (narrowed || logged || NamesReason(site)) continue;
            problems.Add($"Desktop/Native/{site.File}:{site.Line}：catch ({site.Type}) 三件事一件都没做——"
                + "没念原因（既没接 Redactor.Redact(ex.Message) 也没走 ShowFailure / LoadFailureState）、"
                + "没记日志、又没收窄（类型是 Exception 也没有 when 筛选）。"
                + "用户读到的是「什么都没发生」，排查的人连一条线索都没有。");
        }

        return problems;
    }

    /// <summary>
    /// 普查表里那五个数，与现在源码里数出来的逐一对照。两侧都对：表里多一行、源码里少一处，都红。
    /// </summary>
    [Fact]
    public void RegisteredCounts_MatchTheCensus() =>
        Assert.Empty(CensusProblems(RegisteredCensus(), CountByDisposition(Sites())));

    /// <summary>两侧对账的本体：登记读回来的数 ↔ 源码现读的数，逐处置名对照。</summary>
    private static List<string> CensusProblems(Dictionary<string, int> registered, Dictionary<string, int> actual)
    {
        var problems = new List<string>();
        foreach (var name in registered.Keys.Concat(actual.Keys).Distinct(StringComparer.Ordinal))
        {
            registered.TryGetValue(name, out var want);
            actual.TryGetValue(name, out var got);
            if (want == got) continue;
            problems.Add($"「{name}」：docs/NOTES.md 那一节的表登记 {want} 处，源码现读 {got} 处。"
                + "两侧必须一起动：源码长了而登记没跟上，那一节就成了一句过期散文；"
                + "登记改了而源码没动，就是拿表盖住实物。");
        }

        return problems;
    }

    /// <summary>处置归类：与那一节那张表同一把取法（先认授权文案通道，再认念不念原因，再认念不念日志）。</summary>
    private static string Disposition(Site site)
    {
        if (site.Type == "UserException") return AuthorisedCopy;
        if (NamesReason(site))
        {
            var viaSharedState = site.Body.Contains("LoadFailureState.", StringComparison.Ordinal)
                                 || site.Body.Contains("ShowFailure(", StringComparison.Ordinal);
            return viaSharedState ? FailureState : StatusLine;
        }

        return Logs(site) ? LoggedOnly : KeptQuiet;
    }

    private static Dictionary<string, int> CountByDisposition(IEnumerable<Site> sites)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var site in sites) counts[Disposition(site)] = counts.GetValueOrDefault(Disposition(site)) + 1;
        return counts;
    }

    /// <summary>念不念那一句原因：就地脱敏，或把异常交给本屏那个会脱敏的 ShowFailure。</summary>
    private static bool NamesReason(Site site) =>
        site.Body.Contains("Redactor.Redact(ex.Message)", StringComparison.Ordinal)
        || site.Body.Contains("ShowFailure(", StringComparison.Ordinal);

    private static bool Logs(Site site) =>
        site.Body.Contains("LogError", StringComparison.Ordinal)
        || site.Body.Contains("LogWarning", StringComparison.Ordinal)
        || site.Body.Contains("LogInformation", StringComparison.Ordinal);

    /// <summary>
    /// 从 <c>docs/NOTES.md</c> 那一节的表里读回五个数（行形如 <c>&gt; | 处置 | 处数 | …</c>）。
    /// 先认「这一节」再认表，所以别处的表不会混进来。
    /// </summary>
    private static Dictionary<string, int> RegisteredCensus()
    {
        var section = SectionAfterHeading(Repo.Source("docs", "NOTES.md"), "原生界面错误文案的两条通道");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in section.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('>') && line.TrimStart('>', ' ').StartsWith('|')) line = line.TrimStart('>', ' ');
            var match = Regex.Match(line, @"\|\s*([^|]+?)\s*\|\s*(\d+)\s*\|");
            if (!match.Success) continue;
            var name = match.Groups[1].Value.Trim();
            Assert.True(counts.TryAdd(name, int.Parse(match.Groups[2].Value)),
                $"那一节的表里「{name}」出现了两行——一张表两个数，读的人不知道信哪一个。");
        }

        Assert.Equal(5, counts.Count);
        return counts;
    }

    /// <summary>取那一处 <c>## </c> 标题起、到下一个 <c>## </c> 之前的正文。</summary>
    private static string SectionAfterHeading(string text, string heading)
    {
        var lines = text.Split('\n');
        var start = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal)
                                                && l.Contains(heading, StringComparison.Ordinal));
        Assert.True(start >= 0, $"docs/NOTES.md 里找不到「{heading}」那一节。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[start..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>
    /// 两条判据自己被抓得住：每一发注入都必须报出自己那一处，合规的写法一律不报。
    /// 注入喂的是<b>源码形状</b>的文本，走的是与仓里那一把<b>同一份取法</b>（<see cref="FromText"/>），
    /// 所以「取法读不到」不会被读成「没有问题」。
    /// </summary>
    [Fact]
    public void TheGates_ReportTheBreaksTheyMustCatch()
    {
        // ① 宽口径吞掉、变量名也省掉：三件都没做。报一条——第一版的取法在这里会读成「没有这一处」。
        Assert.Single(ChannelProblems(Injected(@"        catch (Exception)
        {
        }")));

        // ② 宽口径吞掉、带回退值：同样三件都没做，报一条。
        Assert.Single(ChannelProblems(Injected(@"        catch (Exception ex)
        {
            return null;
        }")));

        // ③ 窄类型吞掉：收窄成立，不报（今天那 3 处 InvalidOperationException 的形状）。
        Assert.Empty(ChannelProblems(Injected(@"        catch (InvalidOperationException)
        {
            // 窗口正在关闭。
        }")));

        // ④ 宽口径但带 when 筛选：收窄成立，不报（今天那 5 处便利设置的形状）。
        Assert.Empty(ChannelProblems(Injected(@"        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }")));

        // ⑤ 宽口径但念了脱敏原因：不报（今天那 61 处的形状）。
        Assert.Empty(ChannelProblems(Injected(@"        catch (Exception ex)
        {
            SetStatus(""无法开始扫描："" + Redactor.Redact(ex.Message));
        }")));

        // ⑥ 宽口径但记了日志：不报（NativeShellForm 退出时停本地服务那一处）。
        Assert.Empty(ChannelProblems(Injected(@"        catch (Exception ex)
        {
            _host.Logger.LogError(ex, ""退出时停止本地服务失败"");
        }")));

        // ⑦ 对账那一侧：把现读的某一处置数偷偷加一处，必须点名那一行、且只点名那一行。
        var actual = CountByDisposition(Sites());
        var drifted = new Dictionary<string, int>(actual) { [FailureState] = actual[FailureState] + 1 };
        var driftProblems = CensusProblems(RegisteredCensus(), drifted);
        Assert.Single(driftProblems);
        Assert.Contains(FailureState, driftProblems[0], StringComparison.Ordinal);

        // ⑧ 现读的五个数此刻必须与登记读回的五个数完全相等（与 RegisteredCounts 同一判据、同一取法）。
        Assert.Equal(RegisteredCensus(), actual);
    }

    private static List<Site> Injected(string block) => FromText([("Injected.cs", block)]);
}
