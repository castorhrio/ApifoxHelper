using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「真机供给未读」的有界门（第四百二十一轮，控制仓简报 P0-1）。
/// <para>
/// <b>要补的缺口</b>：<c>docs/NOTES.md</c> 顶部「真机供给状态」那一段自第三百六十三轮起改成<b>条件触发</b>——
/// 没有新供给信号的那一轮只登记一句「本轮未读，理由：上一轮已判 0 信息量」，不再每轮现读
/// <c>bash .winecheck/collect-real-machine.sh</c>。这条口径本身是对的（再跑一次只把同一版九格空目录的数
/// 再念一遍，不给新信息），但它<b>没有上界</b>：只要没人来答复，下一轮就可以照抄同一句「本轮未读」，
/// 一路抄到答辩那天，而<b>仓里没有任何东西会红</b>。真机断链于是从「有界动作」退化成「自由裁量」——
/// 这正是简报 R3 / T1 点的那一格。
/// </para>
/// <para>
/// <b>判据（三条，全部落在顶部那一行上）</b>：
/// ① 那一行若写「本轮未读」，必须<b>逐字引用</b>上一轮的判定结论（<see cref="PreviousVerdictQuote"/>）——
/// 不引用就等于「这一轮没读」这件事没有理由，下一轮读的人分不清是「判过了」还是「忘了」；
/// ② 连续未读的轮次数必须<b>能从仓里数出来</b>：那一行要写「距上次现读第 N 轮」，N 是整数；
/// ③ 上限 <see cref="MaxConsecutiveUnread"/> 轮：N 超过上限即红——那一轮必须现读一次，
/// 把「一直不读」变成「最多连着不读三轮」。
/// 那一行若写「现读 N 次」，则必须记下读数（退出码 + 「有东西 N / 9 个目录」）——现读了却不记，
/// 下一轮照样没有可比的东西。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判那九个归档目录里到底有没有东西（那是 <c>collect-real-machine.sh</c> 与
/// <c>RealMachineEvidenceTests</c> 的地盘），不判供给侧该不该答复（本仓管不到），也不判
/// <c>G-D5</c> 那一格该不该勾（勾选框只由真机证据关闭）。它判的只有一件事：
/// <b>「这一轮读没读、读到什么、连着没读几轮」在仓里有一处可机械读的落点</b>。
/// </para>
/// <para>
/// <b>为什么这一条不是「假门」</b>：它判的是<b>外部实物</b>——那一行是给人读的登记，定义不在判据里
/// （按 <c>LayoutMeasurementGateTests</c> 那五步的第 ② 步，答「不是」），所以它是对账门，不是「判据判自己」。
/// 抓不抓得住由 <see cref="Detector_ReportsTheBreaksItMustCatch"/> 拿九发注入当场验。
/// </para>
/// </summary>
public class RealMachineSupplyGateTests
{
    /// <summary>
    /// 顶部「真机供给状态」那一段里<b>现状那一行</b>的行首形状——「这一轮读没读」的唯一落点。
    /// <para>
    /// <b>为什么锚在行首</b>：与 <c>ReadingConsistencyTests.LastFullRunEntry</c> 同一取法。逐轮账本里会把
    /// 这一行原样引一次，那是引用、不是第二个落点；只认「blockquote 里列表项、加粗起头是这一句」那种行。
    /// </para>
    /// </summary>
    private static readonly Regex SupplyStatusEntry = new(
        @"^\s*>\s*-\s+\*\*本轮为真机供给做了什么：", RegexOptions.Compiled);

    /// <summary>「这一轮真的现读了」的写法：<c>现读 N 次</c>，N 不小于 1。</summary>
    private static readonly Regex LiveReadMarker = new(
        @"^\s*>\s*-\s+\*\*本轮为真机供给做了什么：现读\s*(?<n>\d+)\s*次\*\*", RegexOptions.Compiled);

    /// <summary>「这一轮没读」的写法：<c>本轮未读</c>。</summary>
    private static readonly Regex UnreadMarker = new(
        @"^\s*>\s*-\s+\*\*本轮为真机供给做了什么：本轮未读\*\*", RegexOptions.Compiled);

    /// <summary>
    /// 未读时必须<b>逐字引用</b>的上一轮判定结论。这一句是第三百六十三轮那条口径里已经写下的原话
    /// （「再跑一次只把同一版九格空目录的数再念一遍，不给新信息」的判定结果），不是本轮新发明的措辞。
    /// </summary>
    private const string PreviousVerdictQuote = "上一轮已判 0 信息量";

    /// <summary>未读时必须写出的计数：<c>距上次现读第 N 轮</c>。</summary>
    private static readonly Regex UnreadCounter = new(
        @"距上次现读第\s*(?<n>\d+)\s*轮", RegexOptions.Compiled);

    /// <summary>
    /// 连续未读的上限。到第 <see cref="MaxConsecutiveUnread"/> 轮为止可以只登记；<b>第 4 轮必须现读</b>——
    /// 上限取 3 的理由与「最近 3 轮」那条截断口径同一分寸：三轮之内还看得出「上一轮判过什么」，
    /// 再往后读的人只能看见一串「未读」，分不清是判过了还是忘了。
    /// </summary>
    private const int MaxConsecutiveUnread = 3;

    /// <summary>现读时必须记下的第一样：退出码。</summary>
    private static readonly Regex LiveReadExitCode = new(@"退出码\s*`?\d+`?", RegexOptions.Compiled);

    /// <summary>现读时必须记下的第二样：「有东西 N / 9 个目录」。</summary>
    private static readonly Regex LiveReadDirectories = new(@"有东西\s*(?<n>\d+)\s*/\s*9\s*个目录", RegexOptions.Compiled);

    // ---------- 判据本体 ----------

    [Fact]
    public void SupplyStatus_IsReadOrBoundedUnread()
    {
        var problems = SupplyReadProblems(SupplySectionLines());
        Assert.True(problems.Count == 0,
            "docs/NOTES.md 顶部「真机供给状态」那一段的现状行不成立：\n  · "
            + string.Join("\n  · ", problems)
            + "\n改法：那一行按两种写法之一重写——本轮真的现读了就写「现读 N 次」并记下退出码与"
            + "「有东西 N / 9 个目录」；本轮没读就写「本轮未读」，逐字引用上一轮的判定结论"
            + "（「" + PreviousVerdictQuote + "」）并写出「距上次现读第 N 轮」，N 到 "
            + MaxConsecutiveUnread + " 为止，第 " + (MaxConsecutiveUnread + 1) + " 轮必须现读一次。");
    }

    [Fact]
    public void SupplyStatus_IsTheOnlyPlaceThatSaysWhetherThisRoundRead()
    {
        // 现状行恰好一处：0 处 = 「这一轮读没读」没有落点（门没有可判的对象）；
        // 2 处以上 = 同一件事有了两个答案，改一处不动另一处就分叉。
        var hits = SupplySectionLines().Count(l => SupplyStatusEntry.IsMatch(l));
        Assert.True(hits == 1,
            $"顶部「真机供给状态」那一段里现状行（列表项加粗起头「本轮为真机供给做了什么」）读到 {hits} 处，"
            + "要的是恰好一处。");
    }

    // ---------- 判据自己抓不抓得住 ----------

    /// <summary>
    /// 九发注入：合规两发（现读 / 未读各一）必须<b>不报</b>，破坏七发必须<b>当场报</b>。
    /// <para>
    /// 与 <c>LayoutMeasurementGateTests.ReasonDetector_ReportsTheBreaksItMustCatch</c> 同一纪律：
    /// 判据自己没被跑过，比没有判据更贵——一条永远为真的门，绿得再好看也不挡任何事。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：本轮真的现读了，读数记全。
        Assert.Empty(SupplyReadProblems([
            "> - **本轮为真机供给做了什么：现读 1 次**——`bash .winecheck/collect-real-machine.sh` "
            + "**退出码 `1`、「有东西 0 / 9 个目录」**，九个目录逐个被点名。",
        ]));

        // ② 合规：本轮没读，引用了上一轮结论、写了计数，且没到上限。
        Assert.Empty(SupplyReadProblems([
            "> - **本轮为真机供给做了什么：本轮未读**——理由：" + PreviousVerdictQuote
            + "；距上次现读第 1 轮。",
        ]));

        // ③ 未读但没引用上一轮的判定结论：这一轮「没读」没有理由，下一轮分不清是判过了还是忘了。
        var noQuote = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：本轮未读**——距上次现读第 1 轮。",
        ]);
        Assert.Single(noQuote);
        Assert.Contains(PreviousVerdictQuote, noQuote[0], StringComparison.Ordinal);

        // ④ 连续 4 轮未读：超过上限，这一轮必须现读一次。
        var overCap = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：本轮未读**——理由：" + PreviousVerdictQuote
            + "；距上次现读第 4 轮。",
        ]);
        Assert.Single(overCap);
        Assert.Contains("上限", overCap[0], StringComparison.Ordinal);
        Assert.Contains("现读", overCap[0], StringComparison.Ordinal);

        // ⑤ 未读但没写计数：连续未读了几轮数不出来，「有界」这件事就无从判。
        var noCounter = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：本轮未读**——理由：" + PreviousVerdictQuote + "。",
        ]);
        Assert.Single(noCounter);
        Assert.Contains("距上次现读第 N 轮", noCounter[0], StringComparison.Ordinal);

        // ⑥ 计数写成 0：第 0 轮不是「没读」，是「这一轮读了」——两种写法混在一起，计数就不可信。
        var zeroCounter = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：本轮未读**——理由：" + PreviousVerdictQuote
            + "；距上次现读第 0 轮。",
        ]);
        Assert.Single(zeroCounter);
        Assert.Contains("不小于 1", zeroCounter[0], StringComparison.Ordinal);

        // ⑦ 现读却没记读数：下一轮没有可比的东西，「读过了」只是一句话。
        var noReading = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：现读 1 次**——九个目录逐个被点名。",
        ]);
        Assert.Equal(2, noReading.Count);
        Assert.Contains(noReading, p => p.Contains("退出码", StringComparison.Ordinal));
        Assert.Contains(noReading, p => p.Contains("有东西", StringComparison.Ordinal));

        // ⑧ 标记写歪（既不是「现读 N 次」也不是「本轮未读」）：门认不出这一行，等于没有落点。
        var garbled = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：0 件**——本仓能给的那一半已穷尽。",
        ]);
        Assert.Single(garbled);
        Assert.Contains("现读 N 次", garbled[0], StringComparison.Ordinal);
        Assert.Contains("本轮未读", garbled[0], StringComparison.Ordinal);

        // ⑨ 现状行 0 处 / 2 处：没有落点，或者同一件事有了两个答案。
        var none = SupplyReadProblems(["> 这一段里没有现状行。"]);
        Assert.Single(none);
        Assert.Contains("0 处", none[0], StringComparison.Ordinal);

        var twice = SupplyReadProblems([
            "> - **本轮为真机供给做了什么：现读 1 次**——退出码 `1`、「有东西 0 / 9 个目录」。",
            "> - **本轮为真机供给做了什么：本轮未读**——理由：" + PreviousVerdictQuote
            + "；距上次现读第 1 轮。",
        ]);
        Assert.Single(twice);
        Assert.Contains("2 处", twice[0], StringComparison.Ordinal);
    }

    // ---------- 判据本体（收成一处，好让上面那一发发注入各喂一遍） ----------

    /// <summary>
    /// 判据本体：喂一段「真机供给状态」的行，返回问题清单（空 = 这一行成立）。
    /// <para>
    /// 收成一处而不是写在 <see cref="SupplyStatus_IsReadOrBoundedUnread"/> 里，是为了让
    /// <see cref="Detector_ReportsTheBreaksItMustCatch"/> 能把每一发破坏各喂一遍——
    /// 判据与「判据抓不抓得住」共用同一份逻辑，注入验的就是仓里真跑的那一条。
    /// </para>
    /// </summary>
    private static List<string> SupplyReadProblems(IReadOnlyList<string> sectionLines)
    {
        var problems = new List<string>();
        var entries = sectionLines.Where(l => SupplyStatusEntry.IsMatch(l)).ToList();
        if (entries.Count != 1)
        {
            problems.Add($"现状行（列表项加粗起头「本轮为真机供给做了什么」）读到 {entries.Count} 处，要的是恰好一处："
                         + "0 处 = 「这一轮读没读」没有落点；2 处以上 = 同一件事有了两个答案。");
            return problems;
        }

        var line = entries[0];
        var live = LiveReadMarker.Match(line);
        var unread = UnreadMarker.Match(line);
        if (live.Success == unread.Success)
        {
            problems.Add("现状行的标记既不是「现读 N 次」也不是「本轮未读」："
                         + "这一行是「这一轮读没读」的唯一落点，标记必须取这两种写法之一，"
                         + "否则门认不出它，等于没有落点。");
            return problems;
        }

        if (live.Success)
        {
            if (int.Parse(live.Groups["n"].Value) < 1)
            {
                problems.Add("「现读 0 次」不是一次现读：现读的 N 不小于 1，没读就写「本轮未读」。");
            }

            if (!LiveReadExitCode.IsMatch(line))
            {
                problems.Add("写了「现读」却没记退出码：现读了却不记读数，下一轮没有可比的东西。");
            }

            if (!LiveReadDirectories.IsMatch(line))
            {
                problems.Add("写了「现读」却没记「有东西 N / 9 个目录」："
                             + "这一笔是「九个归档目录里有没有东西」的当场读数，不记就等于没读。");
            }

            return problems;
        }

        // 未读：① 必须逐字引用上一轮的判定结论。
        if (!line.Contains(PreviousVerdictQuote, StringComparison.Ordinal))
        {
            problems.Add($"写了「本轮未读」却没逐字引用上一轮的判定结论（要的是「{PreviousVerdictQuote}」）："
                         + "不引用就等于「这一轮没读」没有理由，下一轮读的人分不清是判过了还是忘了。");
        }

        // ② 连续未读的轮次数必须能从仓里数出来。
        var counter = UnreadCounter.Match(line);
        if (!counter.Success)
        {
            problems.Add("写了「本轮未读」却没写「距上次现读第 N 轮」："
                         + "连续未读了几轮数不出来，「有界」这件事就无从判。");
            return problems;
        }

        var rounds = int.Parse(counter.Groups["n"].Value);
        if (rounds < 1)
        {
            problems.Add($"「距上次现读第 {rounds} 轮」不成立：计数从 1 起（不小于 1），"
                         + "第 0 轮的意思是「这一轮读了」，与「本轮未读」自相矛盾。");
            return problems;
        }

        // ③ 上限：到第 MaxConsecutiveUnread 轮为止可以只登记，再往后必须现读一次。
        if (rounds > MaxConsecutiveUnread)
        {
            problems.Add($"连续未读已到第 {rounds} 轮，超过上限 {MaxConsecutiveUnread} 轮："
                         + "这一轮必须现读一次（`bash .winecheck/collect-real-machine.sh`），"
                         + "把读数写进这一行，计数归零——「一直不读」不许没有上界。");
        }

        return problems;
    }

    /// <summary>
    /// 顶部「真机供给状态」那一段的行：<c>docs/NOTES.md</c> 里第一个 <c>## </c> 标题之前的全部行。
    /// <para>
    /// <b>为什么止于第一个 <c>## </c></b>：那一段按 <c>AcceptanceChecklistTests.SectionLines</c> 的口径
    /// 必须排在验收表之前（第一处 <c>## </c> 就是「本仓验收口径」），所以「第一个 <c>## </c> 之前」
    /// 就是这一段的范围，不另立一个结束标记。
    /// </para>
    /// </summary>
    private static List<string> SupplySectionLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        var end = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[..(end < 0 ? lines.Length : end)].ToList();
    }
}
