using System.Text.Json.Nodes;
using ApifoxHelper.Reports;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// HTML 交付面的折行行为——用真浏览器量（DESIGN.md §3「行首行尾禁则」「正文容器 ≤45em」）。
/// <para>
/// <b>为什么这一条要请浏览器</b>：<see cref="ReportTypographyTests.HtmlStyle_EnforcesTheChineseLineBreakProhibition"/>
/// 判的是 <c>line-break:strict</c> 这个子串在不在样式里。子串在位不等于折行成立：§3 那句禁则约束的是
/// 用户实际拿到的那份文件排出来的行，而<b>全仓只有导出 HTML 会折行</b>（§6「中文排版」第 ② 条）。
/// 上一轮把声明写进去之后，这条承诺还停在「写了」这一层——本文件把它推到「排出来确实如此」。
/// </para>
/// <para>
/// <b>量的是产物，不是复刻</b>：<c>ReportRender.Html(ReportRender.Markdown(ReportTypographyTests.Report()))</c>
/// 落盘后<b>一个字节都不改</b>地交给 headless Chromium。产物自带的 CSP（<c>default-src 'none'</c>）禁止内联脚本，
/// 探针因此走 CDP 的 <c>Runtime.evaluate</c>（不受页面 CSP 约束）——不往文件里塞 <c>&lt;script&gt;</c>，
/// 也不关掉安全策略；改了任何一项，量的就不再是用户拿到的那份东西。
/// 窄容器与自测用的句子都取自产物正文，不另造文案：造的文案会漂，漂了就不知道在判什么。
/// </para>
/// <para>
/// <b>两档视口各判一件事</b>：1280 宽档判「列宽由 <c>max-width:40em</c> 决定，而不是由视口决定」；
/// 560 窄档判「正文真的折行时禁则成立」。只有宽档时 <c>wrappedNodes</c> 实测为 0——
/// 一句没折的「零违例」什么都不判，所以窄档不是加分项，是那一支的前提。
/// </para>
/// <para>
/// <b>仪器必须先证明自己看得见</b>：每一支零违例都配一条自测
/// （<see cref="TheDetector_ReportsTheBreakItForcedInsideTheArtifactOwnSentences"/>）——
/// 拿产物自己的句子人为插一道硬换行，把禁字顶到行首、把开号留在行尾，同一套判据必须报出违例。
/// 报不出来，上面的零违例就只是仪器是瞎的（本仓反复记的「空门」）。
/// </para>
/// <para>
/// <b>本机判不出来的那半截写在纸上</b>：实测 <c>auto</c> / <c>strict</c> / <c>loose</c> 与「不写」
/// 在同一批 17 档宽度上排版逐档相同（<see cref="ThisBrowser_CannotTellTheDeclarationFromItsAbsence_NotesSaysSo"/>），
/// 所以「禁则成立」<b>不能归因给那条声明</b>。这一条既不是缺陷也不是通过，是一笔要挂的账；
/// 浏览器缺位时门只退成「声明在位 + 挂账在位」，<b>不把没跑算成跑过</b>（与「wine 结果不等于真机结果」同一份纪律）。
/// </para>
/// </summary>
public class HtmlWrapTests
{
    private const string NotesHeading = "## 真浏览器布局门（HTML 折行）";

    /// <summary>列宽由 max-width 决定的那一档。</summary>
    private const int WideViewport = 1280;

    /// <summary>
    /// 正文被压得必须折行的那一档。再往下要留神 headless 的最小窗宽：夹住之后视口不由请求决定，
    /// 读数也就由不得样式决定了（560 实测是安全的）。
    /// </summary>
    private const int NarrowViewport = 560;

    /// <summary>量测每档取一次：五道门读的是同一份浏览器运行，分开起进程只会让失败更难复现。</summary>
    private static readonly Lazy<JsonObject?> Wide = new(() => MeasureAt(WideViewport));

    private static readonly Lazy<JsonObject?> Narrow = new(() => MeasureAt(NarrowViewport));

    private static JsonObject? MeasureAt(int windowWidth)
    {
        if (!ChromeLayout.Available) return null;
        var html = ReportRender.Html(ReportRender.Markdown(ReportTypographyTests.Report()));
        return ChromeLayout.Measure(html, windowWidth);
    }

    /// <summary>取一个整数；失败消息带上整份量测结果，换机器时不用重跑就能看出是哪一支偏了。</summary>
    private static int Value(JsonObject node, string key)
    {
        Assert.True(node.TryGetPropertyValue(key, out var value) && value is not null,
            $"浏览器量测结果里缺「{key}」：{node.ToJsonString()}");
        return value.GetValue<int>();
    }

    private static int Value(JsonObject node, string parent, string key) => Value(node[parent]!.AsObject(), key);

    private static double Number(JsonObject node, string key) => node[key]!.GetValue<double>();

    /// <summary>
    /// 浏览器不在的时候门退成「挂账在位」——这一支不判排版，判的是「没跑这件事有没有如实登记」。
    /// 登记里必须带查找顺序与到期日，否则换机器的人只看到一条红门，不知道该设什么。
    /// </summary>
    private static bool RequireBrowserOrRegisteredGap()
    {
        if (ChromeLayout.Available) return true;
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.True(notes.Contains(NotesHeading, StringComparison.Ordinal),
            $"{ChromeLayout.MissingReason}。浏览器不在时这一族门只许退成「挂账在位」——而 NOTES 里找不到「{NotesHeading}」，"
            + "等于「本机没跑」没有被记成账，那就不是降级，是静默通过。");
        Assert.Contains("2026-11-20", notes, StringComparison.Ordinal);
        Assert.Contains(ChromeLayout.LookupOrder, notes);
        return false;
    }

    /// <summary>
    /// 本机有没有可用浏览器要当场说清：有就真量出一页正文，没有就只许走登记过的挂账。
    /// </summary>
    [Fact]
    public void BrowserIsFoundOrItsAbsenceIsRegisteredInTheNotes()
    {
        if (!RequireBrowserOrRegisteredGap()) return;

        var wide = Wide.Value!;
        var narrow = Narrow.Value!;
        Assert.True(Value(wide, "viewport") >= WideViewport - 20,
            $"宽档视口只有 {Value(wide, "viewport")}px：视口没打开到位，列宽判定不成立。");
        Assert.True(Value(wide, "prose", "visible") >= 20,
            $"宽档只量到 {Value(wide, "prose", "visible")} 个有内容的文字节点：这份产物是不是空的？");
        Assert.True(Value(narrow, "viewport") >= 500,
            $"窄档视口只有 {Value(narrow, "viewport")}px：被 headless 的最小窗宽夹住了。");

        // 两档量的必须是同一份产物：文字节点数不同就是渲染了别的东西。
        Assert.Equal(Value(wide, "prose", "nodes"), Value(narrow, "prose", "nodes"));

        // 排版读数必须落在产物声明的那两个取值上；对不上就是量错了文档（例如样式没进 body）。
        Assert.Equal("strict", wide["lineBreakComputed"]!.GetValue<string>());
        Assert.Equal("normal", wide["wordBreakComputed"]!.GetValue<string>());
    }

    /// <summary>
    /// ① 交付的那份 HTML 在真浏览器里折行时，禁字没被拆到行首、开号没被留在行尾。
    /// <para>
    /// 三处都量：产物自己在窄视口下的真排版（真标签、真样式、真折行）、把产物最长那句塞进 100–420px
    /// 窄容器的 17 档扫描（真排版只折出 5 处，样本太少，这一支把折行数推上去），以及五个排版取值各自
    /// 的同一批扫描。三处的零违例都必须发生在「真的折了行」的前提上。
    /// </para>
    /// </summary>
    [Fact]
    public void DeliveredHtml_KeepsForbiddenPunctuationOffTheLineStart()
    {
        if (!RequireBrowserOrRegisteredGap()) return;

        var narrow = Narrow.Value!;
        Assert.True(Value(narrow, "sweepWidths") == 17, $"窄容器扫描只跑了 {Value(narrow, "sweepWidths")} 档宽度。");

        Assert.True(Value(narrow, "prose", "start") == 0,
            $"产物正文里有 {Value(narrow, "prose", "start")} 处禁字被折到行首（禁字集 {CopyRules.LineStartForbidden}）。");
        Assert.True(Value(narrow, "prose", "end") == 0,
            $"产物正文里有 {Value(narrow, "prose", "end")} 处开号被留在行尾（开号集 {CopyRules.LineEndForbidden}）。");
        Assert.True(Value(narrow, "sweep", "start") == 0,
            $"窄容器扫描里有 {Value(narrow, "sweep", "start")} 处禁字起行（17 档宽度合计）。");
        Assert.True(Value(narrow, "sweep", "end") == 0,
            $"窄容器扫描里有 {Value(narrow, "sweep", "end")} 处开号收尾。");

        // 零违例必须发生在「真折了行」之上，否则它只是没排过版。
        Assert.True(Value(narrow, "prose", "wrappedNodes") >= 1,
            $"窄视口下产物 {Value(narrow, "prose", "nodes")} 个文字节点仍全部一行到底（总行数 {Value(narrow, "prose", "lines")}）：这一支没在判折行。");
        Assert.True(Value(narrow, "sweep", "lines") >= 34,
            $"窄容器扫描 17 档只排出 {Value(narrow, "sweep", "lines")} 行：宽度档没起作用，零违例是空的。");

        var variants = narrow["variants"]!.AsArray();
        Assert.True(variants.Count == 5, $"排版取值跑了 {variants.Count} 组，期望 5 组（不写 / auto / strict / loose / break-all）。");
        foreach (var variant in variants.Cast<JsonObject>())
        {
            var name = variant["name"]!.GetValue<string>();
            var start = Value(variant, "start");
            var end = Value(variant, "end");
            Assert.True(start == 0 && end == 0, $"取值「{name}」下产物那句话有 {start} 处禁字起行、{end} 处开号收尾。");
        }
    }

    /// <summary>
    /// ② 正文列宽在真浏览器里确实由 <c>max-width:40em</c> 决定，而不是由视口决定。
    /// <para>
    /// §3「正文容器 ≤45em」第一次有排版证据。<c>HtmlStyle_ConstrainsProseWidth</c> 判的是样式串里那个数值
    /// 落在 30–45em；数值写着不等于浏览器按它排。这一支读的是浏览器把 <c>em</c> 解析完之后的实际像素：
    /// 视口 1280px，列宽必须等于 40×字号再加两侧内边距，且明显窄于视口。
    /// 把 <c>max-width</c> 删掉，computed 值就成了 <c>none</c>——当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DeliveredHtml_WrapsInsideTheFortyEmColumnNotTheViewport()
    {
        if (!RequireBrowserOrRegisteredGap()) return;

        var wide = Wide.Value!;
        var fontSize = Number(wide, "fontSizePx");
        Assert.True(Math.Abs(fontSize - 16) < 0.5, $"body 字号是 {fontSize}px，40em 的换算基线变了，下面的判定要连数字一起重算。");

        var column = 40 * fontSize;
        Assert.Equal($"{column:0.##}px", wide["maxWidthComputed"]!.GetValue<string>());

        var body = Value(wide, "bodyWidthPx");
        Assert.True(body >= column && body <= column + 33,
            $"body 实宽 {body}px，对不上 40em（{column}px）加两侧 16px 内边距：列宽不是这条声明定的。");
        Assert.True(body < Value(wide, "viewport") - 200,
            $"body 实宽 {body}px 对视口 {Value(wide, "viewport")}px：列宽跟着视口走，max-width 没生效。");
    }

    /// <summary>
    /// ③ 仪器自测：拿产物自己的句子人为插一道硬换行，把禁字顶到行首、把开号留在行尾，判据必须报出来。
    /// <para>
    /// 存在理由：上面那几栏「零违例」要是判据取错了（把段落首字也当折点、或行尾字取错了行），
    /// 它会长年绿而什么都不判。这条门用同一个函数、同一批句子，只把折行点搬到禁字上——
    /// <b>它红了说明仪器瞎了，不是产物坏了</b>；而它一红，上面那几栏的零违例也一并作废。
    /// </para>
    /// </summary>
    [Fact]
    public void TheDetector_ReportsTheBreakItForcedInsideTheArtifactOwnSentences()
    {
        if (!RequireBrowserOrRegisteredGap()) return;

        var narrow = Narrow.Value!;
        Assert.True(Value(narrow, "firstStartBadIndex") > 0,
            $"产物最长那句（{Value(narrow, "longestLength")} 字）里没有可用于自测的禁字。");
        Assert.True(Value(narrow, "firstEndBadIndex") > 0,
            $"产物正文里没找到含开号的那句（取样长度 {Value(narrow, "endSampleLength")}）：行尾对照组做不出来。");

        var forcedStart = narrow["forcedStart"]!.AsObject();
        Assert.True(Value(forcedStart, "start") >= 1,
            "人为把禁字顶到行首之后仪器仍报零违例——上面那几栏的「零违例」不作数，判据是瞎的。");
        var forcedEnd = narrow["forcedEnd"]!.AsObject();
        Assert.True(Value(forcedEnd, "end") >= 1,
            "人为把开号留在行尾之后仪器仍报零违例——上面那几栏的「零违例」不作数，判据是瞎的。");
    }

    /// <summary>
    /// ④ 本机这个浏览器判不出「写了 strict」与「压根没写」的差别——这一半只能记账，不许写成已验证。
    /// <para>
    /// 实测口径：同一句产物正文、同一批 17 档宽度，<c>auto</c> / <c>strict</c> / <c>loose</c> 与不写
    /// 排出来的行数与折点逐档相同。<c>word-break:break-all</c> 确实改变折点（词中硬切），但它同样
    /// 不把禁字顶到行首。所以 <c>line-break:strict</c> 在这一机与浏览器默认行为<b>重合</b>：
    /// 排版成立不能记在它头上，删掉它的后果也只能由声明门判、不能由排版判。
    /// </para>
    /// <para>
    /// 这条门钉的是<b>那笔账在不在纸上</b>：NOTES 那一节必须写明「不可判别」、点名比过哪几个取值、带上到期日。
    /// 哪一天浏览器改行为使三者不再相同，第二条断言会红——那时要改的是 NOTES 的措辞与禁则的归因，
    /// 不是把门放宽。<b>把「不能判别」写成「已验证」是本文件唯一不许犯的错。</b>
    /// </para>
    /// </summary>
    [Fact]
    public void ThisBrowser_CannotTellTheDeclarationFromItsAbsence_NotesSaysSo()
    {
        var section = NotesSection();
        Assert.Contains("不可判别", section);
        foreach (var value in new[] { "auto", "strict", "loose", "word-break:break-all" })
        {
            Assert.True(section.Contains(value, StringComparison.Ordinal), $"挂账里没写取值「{value}」，读数的人不知道比过哪几档。");
        }
        Assert.Contains("2026-11-20", section, StringComparison.Ordinal);

        if (!RequireBrowserOrRegisteredGap()) return;

        // 逐档折点必须相同才算「不可判别」：只比合计行数的话，两处不同而总数相同也能蒙过去。
        var fingerprints = Narrow.Value!["variants"]!.AsArray().Cast<JsonObject>()
            .ToDictionary(v => v["name"]!.GetValue<string>(), v => v["fingerprint"]!.GetValue<string>());
        var baseline = fingerprints["(unwritten)"];
        foreach (var value in new[] { "line-break:auto", "line-break:strict", "line-break:loose" })
        {
            Assert.True(fingerprints[value] == baseline,
                $"取值「{value}」的排版与不写不再相同：这一机的浏览器开始判得出这条声明了，"
                + "NOTES 那句「不可判别」要连数字一起改写，禁则的归因也要重讲。");
        }
    }

    /// <summary>NOTES 里那一节（标题行到下一个 <c>## </c> 之前）。标题不在就先红，并说要落什么。</summary>
    private static string NotesSection()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var start = notes.IndexOf(NotesHeading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"docs/NOTES.md 缺少「{NotesHeading}」一节（真浏览器布局门的读数与挂账落点）。");
        var rest = notes[(start + NotesHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }
}
