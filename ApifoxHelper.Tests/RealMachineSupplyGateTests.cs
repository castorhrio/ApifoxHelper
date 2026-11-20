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

    // ---------- 第四百二十三轮（简报 P0-1）：真机未验证的交互点必须有界登记 ----------

    /// <summary>
    /// 登记表那一页（相对仓库根）。这一页是「哪些交互点还没在真机上验过」的<b>唯一落点</b>。
    /// </summary>
    private const string GapRegistryPath = "docs/real-machine-gaps.md";

    /// <summary>
    /// 「未验证的是哪一层」只认这三个词：<c>按键路由</c>（按下去有没有走到那一段代码）、
    /// <c>弹出位置</c>（菜单 / 提示弹在哪）、<c>观感</c>（灰阶、强调色、字号行高在真机上的样子）。
    /// <para>
    /// <b>为什么是封闭的三个词</b>：含糊话（「未验证」「待复核」）读不出「这一层到底量没量得到」，
    /// 下一轮读的人照样要重判一次——那正是这一页要挡的事。
    /// </para>
    /// </summary>
    private static readonly string[] GapLayers = ["按键路由", "弹出位置", "观感"];

    /// <summary>
    /// 连续未验证的上限。到第 <see cref="MaxConsecutiveUnverified"/> 轮为止可以只登记；
    /// <b>第 4 轮必须现读一次</b>——上限取 3 的理由与 <see cref="MaxConsecutiveUnread"/> 同一分寸。
    /// </summary>
    private const int MaxConsecutiveUnverified = 3;

    /// <summary>「从未为这一层跑过读数」的写法。</summary>
    private const string NeverReadMarker = "未现读";

    /// <summary>登记表里的一行（六格）。</summary>
    private sealed record GapRow(
        string Point, string Layer, string FirstRound, string LastRead, string Streak, string Reading);

    /// <summary>
    /// 每一轮新增的真机未验证交互点都必须在 <c>docs/real-machine-gaps.md</c> 登记，且写清未验证的是哪一层、
    /// 连着几轮没验（第四百二十三轮，控制仓简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第四百二十二轮给列表右键菜单补上了键盘入口（<c>Shift+F10</c> / 菜单键），
    /// 那是<b>真机按键路由</b>这一层第一次有新代码；而本文件此前只判「真机供给未读」的有界性
    /// （<see cref="SupplyStatus_IsReadOrBoundedUnread"/>），<b>判不到「真机未验证的功能点」有没有登记</b>——
    /// 一轮加了新交互、在条目里写一句「真机未验证」就过去了，下一轮读的人分不清「这一层验过了」还是
    /// 「从来没人验过」，也没有任何东西会红。
    /// </para>
    /// <para>
    /// <b>判据（三条，全部落在那一页的表上）</b>：① 每一行必须写清未验证的是哪一层（只认
    /// <see cref="GapLayers"/> 那三个词）；② 连续未验证轮次数必须<b>能从账本现算出来</b>——
    /// 那一格必须等于「账本最新一轮 − 最近一次现读轮次」（从未现读过的从首次登记轮次起算），自己填一个数不算数；
    /// ③ 上限 <see cref="MaxConsecutiveUnverified"/> 轮，超过即红，那一轮必须现读一次、把读数写进那一行。
    /// 另两条边界：表不许为空（0 行 = 这笔债被静默丢掉）；首次登记轮次必须是账本里真有的那一轮，
    /// 且那一轮的条目里写着「未验证」（登记一个从没承认过这件事的轮次，等于把登记变成自述）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判真机有没有到手（那是 <c>collect-real-machine.sh</c> 与
    /// <c>docs/evidence/real-machine/</c> 的地盘），不判 <c>G-D5</c> 那一格该不该勾，
    /// 也不判「这一层该不该现在验」——那是排期，不是判据。
    /// </para>
    /// <para>
    /// <b>为什么这一条不是「假门」</b>：它判的是<b>外部实物</b>——那一页的表是给人读的登记，定义不在判据里
    /// （按 <c>LayoutMeasurementGateTests</c> 那五步的第 ② 步，答「不是」），所以它是对账门。
    /// 抓不抓得住由 <see cref="UnverifiedPointDetector_ReportsTheBreaksItMustCatch"/> 拿九发注入当场验。
    /// </para>
    /// </summary>
    [Fact]
    public void UnverifiedInteractionPoints_MustBeRegisteredWithLayer()
    {
        var problems = UnverifiedPointProblems(
            Repo.Source("docs", "real-machine-gaps.md"), Repo.Source("docs", "NOTES.md"));
        Assert.True(problems.Count == 0,
            "真机未验证的交互点登记不成立：\n  · " + string.Join("\n  · ", problems)
            + "\n改法：在 " + GapRegistryPath + " 那张表里按行登记——未验证的是哪一层只认 "
            + string.Join(" / ", GapLayers) + " 这三个词；连续未验证轮次数按账本现算（最新一轮 − 最近一次现读轮次，"
            + "从未现读过的从首次登记轮次起算）；到第 " + MaxConsecutiveUnverified + " 轮为止可以只登记，"
            + "第 " + (MaxConsecutiveUnverified + 1) + " 轮必须现读一次并把读数写进那一行。");
    }

    /// <summary>
    /// 九发注入：合规两发必须<b>不报</b>，破坏七发必须<b>当场报</b>。
    /// <para>
    /// 与 <see cref="Detector_ReportsTheBreaksItMustCatch"/> 同一纪律：判据自己没被跑过，
    /// 比没有判据更贵——一条永远为真的门，绿得再好看也不挡任何事。
    /// </para>
    /// </summary>
    [Fact]
    public void UnverifiedPointDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：登记了、层写清了、计数按账本现算对得上、没到上限。
        Assert.Empty(UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1",
                "未现读：模拟器量不到按键路由这一层")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。"))));

        // ② 合规：现读过一次，读数写清了是模拟器还是真机，计数从现读那一轮起算。
        Assert.Empty(UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由、弹出位置", "第四百二十二轮", "第四百二十三轮", "0",
                "wine 模拟器 `RunListContextMenu` 四支通过 / 0 失败；按键路由这一层模拟器量不到")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮现读一次，真机未验证的交互点登记在 " + GapRegistryPath + "。"))));

        // ③ 层写成含糊话：读不出「这一层到底量没量得到」，下一轮照样要重判一次。
        var vague = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "未验证", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(vague);
        Assert.Contains("哪一层", vague[0], StringComparison.Ordinal);

        // ④ 计数自己填一个数：门按账本现算，填错即红。
        var miscounted = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "0", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(miscounted);
        Assert.Contains("现算", miscounted[0], StringComparison.Ordinal);

        // ⑤ 连续未验证到第 4 轮：超过上限，这一轮必须现读一次。
        var overCap = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "4", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。"),
                      (424, "本轮无新增。"), (425, "本轮无新增。"),
                      (426, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(overCap);
        Assert.Contains("上限", overCap[0], StringComparison.Ordinal);
        Assert.Contains("现读", overCap[0], StringComparison.Ordinal);

        // ⑥ 首次登记轮次在账本里查不到：登记一个不存在的轮次，等于把登记变成自述。
        var phantom = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十轮", NeverReadMarker, "3", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(phantom);
        Assert.Contains("账本", phantom[0], StringComparison.Ordinal);

        // ⑦ 首次登记那一轮的条目里没有「未验证」：那一轮从没承认过这件事。
        var unadmitted = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(unadmitted);
        Assert.Contains("未验证", unadmitted[0], StringComparison.Ordinal);

        // ⑧ 表为空：这笔债被静默丢掉。
        var empty = UnverifiedPointProblems(
            GapTable(),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(empty);
        Assert.Contains("一行登记都读不出来", empty[0], StringComparison.Ordinal);

        // ⑨ 最新一轮承认了「真机未验证」却没指回这一页：登记表不是唯一落点。
        var unpointed = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮真机未验证的交互点照旧。")));
        Assert.Single(unpointed);
        Assert.Contains(GapRegistryPath, unpointed[0], StringComparison.Ordinal);

        // ⑩ 现读过却没写清是模拟器还是真机：只写「已复核」不算读数。
        var uncalibrated = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", "第四百二十三轮", "0", "已复核")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮现读一次，真机未验证的交互点登记在 " + GapRegistryPath + "。")));
        Assert.Single(uncalibrated);
        Assert.Contains("模拟器", uncalibrated[0], StringComparison.Ordinal);
    }

    // ---------- 判据本体（收成一处，好让上面那一发发注入各喂一遍） ----------

    /// <summary>
    /// 判据本体：登记表全文 + 账本全文进，问题清单出（空 = 这一页成立）。
    /// <para>
    /// 收成一处而不是写在 <see cref="UnverifiedInteractionPoints_MustBeRegisteredWithLayer"/> 里，
    /// 是为了让 <see cref="UnverifiedPointDetector_ReportsTheBreaksItMustCatch"/> 能把每一发破坏各喂一遍——
    /// 注入验的就是仓里真跑的那一条。
    /// </para>
    /// </summary>
    private static List<string> UnverifiedPointProblems(string registry, string notes)
    {
        var problems = new List<string>();
        var rows = GapRows(registry);
        if (rows.Count == 0)
        {
            problems.Add($"{GapRegistryPath} 里一行登记都读不出来：真机未验证的交互点没有落点，"
                         + "「哪些交互点还没在真机上验过」这件事退回只有 commit message 知道。");
            return problems;
        }

        var entries = LedgerEntries(notes);
        if (entries.Count == 0)
        {
            problems.Add("docs/NOTES.md 里一个「## 第N轮」条目都读不出来：连续未验证轮次数无从现算，"
                         + "「有界」这件事退化成自述。");
            return problems;
        }

        var latest = entries.Keys.Max();

        foreach (var row in rows)
        {
            // ① 未验证的是哪一层必须写清。
            var layers = row.Layer.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (layers.Length == 0 || layers.Any(l => !GapLayers.Contains(l)))
            {
                problems.Add($"「{row.Point}」那一行没写清未验证的是哪一层（读到「{row.Layer}」）："
                             + "只认 " + string.Join(" / ", GapLayers) + " 这三个词，多个用「、」分隔——"
                             + "写「未验证」「待复核」这类含糊话，下一轮读的人分不清这一层量没量得到。");
                continue;
            }

            // ② 首次登记轮次必须是账本里真有的那一轮，且那一轮的条目里写着「未验证」。
            var first = RoundOf(row.FirstRound);
            if (first is null || !entries.ContainsKey(first.Value))
            {
                problems.Add($"「{row.Point}」那一行的首次登记轮次（读到「{row.FirstRound}」）在账本里查不到："
                             + "登记一个不存在的轮次，等于把登记变成自述——写清它是哪一轮真的承认过这件事的。");
                continue;
            }

            if (!entries[first.Value].Contains("未验证", StringComparison.Ordinal))
            {
                problems.Add($"「{row.Point}」那一行说它是第{Chinese(first.Value)}轮登记的，"
                             + "而那一轮的条目里没有「未验证」：那一轮从没承认过这件事，"
                             + "登记与账本必有一处是错的。");
            }

            // ③ 连续未验证轮次数必须能数出来：按账本现算，与那一格逐字对上。
            var neverRead = row.LastRead.Contains(NeverReadMarker, StringComparison.Ordinal);
            var lastRead = neverRead ? null : RoundOf(row.LastRead);
            if (!neverRead && lastRead is null)
            {
                problems.Add($"「{row.Point}」那一行的最近一次现读轮次（读到「{row.LastRead}」）既不是「{NeverReadMarker}」"
                             + "也不是「第N轮」：这一格读不出来，连续未验证轮次数就无从现算。");
                continue;
            }

            var baseline = lastRead ?? first.Value;
            var derived = latest - baseline;
            if (!int.TryParse(row.Streak, out var stated))
            {
                problems.Add($"「{row.Point}」那一行的连续未验证轮次数（读到「{row.Streak}」）不是整数："
                             + "这一格要能数出来，含糊话等于没有上界。");
                continue;
            }

            if (stated != derived)
            {
                problems.Add($"「{row.Point}」那一行写的是连续未验证第 {stated} 轮，按账本现算是第 {derived} 轮"
                             + $"（账本最新一轮是第{Chinese(latest)}轮，最近一次现读是"
                             + (lastRead is null ? "从未现读、从首次登记那一轮起算" : $"第{Chinese(lastRead.Value)}轮")
                             + "）：这一格不许自己填，门按账本现算。");
                continue;
            }

            // ④ 上限：到第 MaxConsecutiveUnverified 轮为止可以只登记，再往后必须现读一次。
            if (derived > MaxConsecutiveUnverified)
            {
                problems.Add($"「{row.Point}」那一行连续未验证已到第 {derived} 轮，超过上限 "
                             + $"{MaxConsecutiveUnverified} 轮：这一轮必须现读一次（模拟器能覆盖的那一层跑模拟器、"
                             + "真机到手就跑真机），把读数写进那一行、计数归零——「一直不验」不许没有上界。");
            }

            // ⑤ 现读过的必须写清读数：量到什么、是模拟器还是真机。
            if (lastRead is not null
                && !row.Reading.Contains("模拟器", StringComparison.Ordinal)
                && !row.Reading.Contains("真机", StringComparison.Ordinal))
            {
                problems.Add($"「{row.Point}」那一行说第{Chinese(lastRead.Value)}轮现读过，"
                             + "而现读读数里没说清是模拟器还是真机量的：只写「已复核」不算读数——"
                             + "下一轮读的人分不清这一层到底量到了什么。");
            }
        }

        // ⑥ 最新一轮的条目若承认「真机未验证」，必须指回这一页（唯一落点）。
        if (entries[latest].Contains("真机未验证", StringComparison.Ordinal)
            && !entries[latest].Contains(GapRegistryPath, StringComparison.Ordinal))
        {
            problems.Add($"第{Chinese(latest)}轮的条目里承认了「真机未验证」，却没指回 {GapRegistryPath}："
                         + "承认了却不登记，下一轮读的人分不清这一层验过了还是从来没人验过。");
        }

        return problems;
    }

    /// <summary>
    /// 登记表里的行：以 <c>|</c> 起头、切成六格、且第一格不是表头也不是分隔线。
    /// <para>
    /// <b>为什么按格数认</b>：格数不对的行读不出来，读不出来的行等于没登记——所以格数不对时
    /// 由 <see cref="GapRows"/> 直接跳过，而「一行都读不出来」那一支会红。
    /// </para>
    /// </summary>
    private static List<GapRow> GapRows(string registry)
    {
        var rows = new List<GapRow>();
        foreach (var line in registry.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('|')) continue;
            var cells = trimmed.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 6) continue;
            if (cells[0] == "交互点" || cells.All(c => c.Length > 0 && c.All(ch => ch is '-' or ':'))) continue;
            rows.Add(new GapRow(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
        }

        return rows;
    }

    /// <summary>
    /// 账本条目：<c>## </c> 标题行里<b>第一个</b>「第N轮」就是这一节的轮次，正文取到下一个 <c>## </c> 之前。
    /// <para>
    /// 与 <c>RoundLedgerTests.LedgerEntries</c> 同一取法（只认第一个：标题里常引到别的轮次，
    /// 把标题里所有轮次都算成「有条目」，会让「引一句就算写过」变成一条绕过账本的路）。
    /// </para>
    /// </summary>
    private static Dictionary<int, string> LedgerEntries(string notes)
    {
        var lines = notes.ReplaceLineEndings("\n").Split('\n');
        var entries = new Dictionary<int, string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("## ", StringComparison.Ordinal)) continue;
            var match = Regex.Match(lines[i], @"第(?<num>[零一二三四五六七八九十百]+)轮");
            if (!match.Success) continue;
            var end = i + 1;
            while (end < lines.Length && !lines[end].StartsWith("## ", StringComparison.Ordinal)) end++;
            var body = string.Join('\n', lines[(i + 1)..end]);
            var round = RoundNumber(match.Groups["num"].Value);
            if (round is null) continue;
            if (!entries.TryGetValue(round.Value, out var existing) || body.Length > existing.Length)
            {
                entries[round.Value] = body;
            }
        }

        return entries;
    }

    /// <summary>「第N轮」→ 整数；读不出来返回 <c>null</c>（不静默当成 0）。</summary>
    private static int? RoundOf(string text)
    {
        var match = Regex.Match(text, @"第(?<num>[零一二三四五六七八九十百]+)轮");
        return match.Success ? RoundNumber(match.Groups["num"].Value) : null;
    }

    private static readonly Dictionary<char, int> RoundDigits = new()
    {
        ['零'] = 0, ['一'] = 1, ['二'] = 2, ['三'] = 3, ['四'] = 4,
        ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9,
    };

    /// <summary>中文轮次号 → 整数（支持到九百九十九）；读不出来返回 <c>null</c>。</summary>
    private static int? RoundNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var total = 0;
        var rest = text;
        if (rest.Contains('百'))
        {
            var at = rest.IndexOf('百');
            var hundreds = at == 0 ? 1 : RoundDigit(rest[..at]);
            if (hundreds is null) return null;
            total += hundreds.Value * 100;
            rest = rest[(at + 1)..];
        }

        if (rest.Contains('十'))
        {
            var at = rest.IndexOf('十');
            var tens = at == 0 ? 1 : RoundDigit(rest[..at]);
            if (tens is null) return null;
            total += tens.Value * 10;
            rest = rest[(at + 1)..];
        }

        foreach (var ch in rest)
        {
            if (!RoundDigits.TryGetValue(ch, out var value)) return null;
            total += value;
        }

        return total;
    }

    private static int? RoundDigit(string text)
    {
        var total = 0;
        foreach (var ch in text)
        {
            if (!RoundDigits.TryGetValue(ch, out var value)) return null;
            total += value;
        }

        return total;
    }

    /// <summary>整数 → 中文轮次号（只用于失败信息，读的人要能一眼对上账本里的写法）。</summary>
    private static string Chinese(int number)
    {
        if (number is < 0 or > 999) return number.ToString();
        var hundreds = number / 100;
        var tens = number % 100 / 10;
        var ones = number % 10;
        var text = new System.Text.StringBuilder();
        if (hundreds > 0)
        {
            if (hundreds > 1) text.Append(RoundDigits.First(d => d.Value == hundreds).Key);
            text.Append('百');
        }

        if (tens > 0)
        {
            if (tens > 1) text.Append(RoundDigits.First(d => d.Value == tens).Key);
            text.Append('十');
        }
        else if (hundreds > 0 && ones > 0)
        {
            text.Append('零');
        }

        if (ones > 0) text.Append(RoundDigits.First(d => d.Value == ones).Key);
        return text.Length == 0 ? "零" : text.ToString();
    }

    /// <summary>夹具：造一张登记表（表头 + 分隔线 + 这几行）。</summary>
    private static string GapTable(params string[] rows) =>
        "| 交互点 | 未验证的是哪一层 | 首次登记轮次 | 最近一次现读轮次 | 连续未验证轮次 | 现读读数 |\n"
        + "| --- | --- | --- | --- | --- | --- |\n"
        + string.Concat(rows.Select(r => r + "\n"));

    /// <summary>夹具：造登记表里的一行。</summary>
    private static string GapRowLine(
        string point, string layer, string first, string lastRead, string streak, string reading) =>
        $"| {point} | {layer} | {first} | {lastRead} | {streak} | {reading} |";

    /// <summary>夹具：造一份「这几轮都有条目」的账本。</summary>
    private static string GapLedger(params (int Round, string Body)[] entries) =>
        string.Concat(entries.Select(e => $"## 第{Chinese(e.Round)}轮（2026-11-20）：夹具条目\n\n{e.Body}\n\n"));

    // ---------- 第四百二十四轮（简报 P0-1）：README「已知限制」点名的屏必须在登记表里有行 ----------

    /// <summary>
    /// 一张 README「已知限制」点名过的真机未验屏，与它在两侧的对账词。
    /// <para>
    /// <see cref="ReadmeKeys"/> 在「已知限制」里<b>带真机未验标记的条目</b>里找（见 <see cref="UnverifiedMarkers"/>），
    /// <see cref="RegistryKeys"/> 在登记表的「交互点」格里找。<see cref="ReadmeAnchored"/> 为 <c>false</c> 的屏
    /// （列表右键菜单的键盘入口）不走 README 这一侧——它的承认落点在账本条目（第四百二十二轮），
    /// 那一侧由 <see cref="UnverifiedPointProblems"/> 判。
    /// </para>
    /// </summary>
    private sealed record KnownLimitScreen(
        string Name, string[] ReadmeKeys, string[] RegistryKeys, bool ReadmeAnchored = true);

    /// <summary>
    /// README「已知限制」点名过的屏（封闭词表）。README 点名了一张词表外的新屏时，
    /// <b>同轮要把新屏加进这张词表并在登记表里补行</b>——词表管不住词表外的屏，这是对账门过散文的已知边界，
    /// 不是可以静默放过的事。
    /// </summary>
    private static readonly KnownLimitScreen[] KnownLimitScreens =
    [
        new("状态行的「查看完整错误」入口", ["查看完整错误"], ["查看完整错误"]),
        new("提示对话框的「复制路径」按钮", ["复制路径"], ["复制路径"]),
        new("200% 档提示行", ["200% 档提示行"], ["200% 档提示行"]),
        new("多显示器混合缩放与运行中改缩放", ["多显示器混合缩放"], ["多显示器混合缩放"]),
        new("状态行的「已取消」与「正在停止…」两句", ["已取消", "正在停止"], ["已取消", "正在停止"]),
        new("列表右键菜单的键盘入口", [], ["右键菜单"], ReadmeAnchored: false),
    ];

    /// <summary>
    /// 「这一条还在挂真机的账」的标记：已知限制的条目里出现任一个，这一条点名的屏就是仍未验的屏。
    /// <para>
    /// <b>为什么靠标记而不靠屏名</b>：第 7 条（作业结束状态行）也提到「已取消」，但那一屏<b>模拟器量过</b>、
    /// 不欠真机的账——只认「点名」会把量过的屏也抓来要行。标记（随 G-D5 / 真机复核 / 没量过 / 仍未验证 /
    /// 真机仍缺）是「这一条自己承认还没验」的写法，README 里写了这些词的条目才是点名。
    /// </para>
    /// </summary>
    private static readonly string[] UnverifiedMarkers =
        ["随 G-D5", "真机复核", "真机未验证", "没量过", "仍未验证", "真机仍缺"];

    /// <summary>
    /// README「已知限制」点名的屏必须在 <c>docs/real-machine-gaps.md</c> 的登记表里有行（第四百二十四轮，
    /// 控制仓简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：README「已知限制」第 1 条点名了多处「随 G-D5 复核」的屏——状态行「查看完整错误」
    /// 入口、提示对话框「复制路径」按钮、200% 档提示行、多显示器混合缩放、状态行「已取消」/「正在停止」两句——
    /// 这些在登记表里一处都没有：<see cref="UnverifiedInteractionPoints_MustBeRegisteredWithLayer"/> 管的是
    /// 「表里有这一行」的纪律（层名合法、轮次真有、计数现算），<b>管不到「表里应当有哪些行」</b>。
    /// </para>
    /// <para>
    /// <b>判据（两侧对账，两个方向都能红）</b>：① README 点名了却没登记 → 红；② 表里还有行而 README 已不再点名
    /// → 红（点名没了行还在，这一行就成了没人认领的债）。<b>为什么是对账门而不是本族</b>：README 与登记表都是
    /// 外部实物，名字的定义不在判据里（按 <c>LayoutMeasurementGateTests</c> 那五步的第 ② 步答「不是」）。
    /// <b>行质量那一半判据（层名、轮次、计数）与本文件上一条门共用同一份 <see cref="UnverifiedPointProblems"/>，
    /// 不养第二份</b>——这一条门喂的是同一张表，红的时候两条门一起红，说的也是同一件事。
    /// </para>
    /// <para>
    /// <b>两类行的承认落点</b>：README 点名的屏（<see cref="KnownLimitScreens"/> 里 <c>ReadmeAnchored</c> 的那几处）
    /// 存废跟着 README 走——验完了先摘掉 README 里的标记、再删行，同轮做；账本条目承认的新交互点
    /// （词表外，如列表右键菜单的键盘入口）的承认落点在账本：首次登记轮次的条目必须写着「未验证」
    /// （上一条门判）。它不判真机到没到手、不判 G-D5 该不该勾、不判「这一层该不该现在验」。
    /// </para>
    /// </summary>
    [Fact]
    public void KnownLimitationScreens_MustBeRegisteredInTheGapTable()
    {
        var problems = GapCoverageProblems(
            Repo.Source("README.md"), Repo.Source("docs", "real-machine-gaps.md"), Repo.Source("docs", "NOTES.md"));
        Assert.True(problems.Count == 0,
            "README「已知限制」点名的屏与登记表对不上：\n  · " + string.Join("\n  · ", problems)
            + "\n改法：README 点名了却没登记的在 " + GapRegistryPath + " 补行（层名按实际情况填、"
            + "首次登记轮次写账本里真承认过的那一轮）；验完了先摘掉 README 里的标记再删行，同轮做；"
            + "README 点名词表外的新屏时，同轮把新屏加进 KnownLimitScreens 并补行——两处一处都不能省。");
    }

    /// <summary>
    /// 七发注入：合规两发必须<b>不报</b>，破坏五发必须<b>当场报</b>。
    /// <para>
    /// 与 <see cref="UnverifiedPointDetector_ReportsTheBreaksItMustCatch"/> 同一纪律：判据自己没被跑过，
    /// 比没有判据更贵。③④⑤⑥ 五发里凡是层名、轮次那些行质量的破坏，走的是与上一条门共用的那一份
    /// <see cref="UnverifiedPointProblems"/>——这里各喂一遍，验的是「这一条门真跑的那份组合判据」。
    /// </para>
    /// </summary>
    [Fact]
    public void GapCoverageDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：README 点名两处、表里两行、账本承认——一处都不报。
        Assert.Empty(GapCoverageProblems(
            KnownLimitSection(
                "- **真机供给现状**：三档真机截图仍未验证。",
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读：模拟器量到过入口出现与点开读全，真机弹出位置与观感量不到"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读：模拟器真点过那颗按钮（鼠标路径），按键路由与真机观感量不到")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。"))));

        // ② 合规：现读过一次、读数写清了是模拟器量的、计数从现读那一轮起算——不报。
        Assert.Empty(GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置", "第四百二十四轮", "第四百二十四轮", "0",
                    "wine 模拟器 `RunDialogStatusSweep` 那一支通过 / 0 失败，弹出位置这一层模拟器量不到"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由", "第四百二十四轮", "第四百二十四轮", "0",
                    "wine 模拟器 `RunNoticeCopyClick` 那一支通过 / 0 失败，按键路由这一层模拟器量不到")),
            GapLedger((424, "本轮现读一次（模拟器），真机未验证的两处文字出口读数记进 " + GapRegistryPath + "。"))));

        // ③ README 点名一处但表里没有：点名了却没登记，下一轮分不清这一层是判过了还是没人验过。
        var unregistered = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(unregistered);
        Assert.Contains("查看完整错误", unregistered[0], StringComparison.Ordinal);

        // ④ 表里多一行 README 没点名：点名没了行还在，这一行就成了没人认领的债。
        var unclaimed = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("多显示器混合缩放与运行中改缩放", "观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的屏登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(unclaimed);
        Assert.Contains("多显示器混合缩放", unclaimed[0], StringComparison.Ordinal);

        // ⑤ 层名写含糊话：行质量那一半判据（与上一条门共用）必须报。
        var vague = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "未验证", "第四百二十四轮", NeverReadMarker, "0", "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(vague);
        Assert.Contains("哪一层", vague[0], StringComparison.Ordinal);

        // ⑥ 首次登记轮次在账本里查不到：登记一个不存在的轮次，等于把登记变成自述。
        var phantom = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(phantom);
        Assert.Contains("账本", phantom[0], StringComparison.Ordinal);

        // ⑦ README 缺「已知限制」一节：点名的落点没了，两侧对账无从谈起。
        var noSection = GapCoverageProblems(
            "前文。\n\n## 使用流程\n\n正文。",
            GapTable(
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的屏登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(noSection);
        Assert.Contains("已知限制", noSection[0], StringComparison.Ordinal);
    }

    // ---------- 判据本体（第四百二十四轮，收成一处好让注入各喂一遍） ----------

    /// <summary>
    /// 判据本体：README 全文 + 登记表全文 + 账本全文进，问题清单出（空 = 两侧对得上）。
    /// <para>
    /// 覆盖那一半（README 点名的屏 ↔ 登记表的行）在这里判；行质量那一半（层名、轮次、计数）
    /// 原样走 <see cref="UnverifiedPointProblems"/>——同一张表不养两份判据。
    /// </para>
    /// </summary>
    private static List<string> GapCoverageProblems(string readme, string registry, string notes)
    {
        var problems = GapCoverageBreaks(readme, registry);
        problems.AddRange(UnverifiedPointProblems(registry, notes));
        return problems;
    }

    /// <summary>覆盖那一半：README「已知限制」点名的屏集合 ↔ 登记表的行集合，两个方向各判一次。</summary>
    private static List<string> GapCoverageBreaks(string readme, string registry)
    {
        var problems = new List<string>();
        var lines = readme.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## 已知限制");
        if (start < 0)
        {
            problems.Add("README.md 里找不到「## 已知限制」一节：真机缺口公开声明的落点没了，"
                         + "「README 点名的屏 ↔ 登记表的行」两侧对账无从谈起。");
            return problems;
        }

        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var markedItems = lines[(start + 1)..(end < 0 ? lines.Length : end)]
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Where(item => UnverifiedMarkers.Any(m => item.Contains(m, StringComparison.Ordinal)))
            .ToList();
        var rows = GapRows(registry);

        foreach (var screen in KnownLimitScreens)
        {
            if (!screen.ReadmeAnchored) continue;

            var named = markedItems.Any(item =>
                screen.ReadmeKeys.Any(k => item.Contains(k, StringComparison.Ordinal)));
            var hasRow = rows.Any(r =>
                screen.RegistryKeys.Any(k => r.Point.Contains(k, StringComparison.Ordinal)));

            if (named && !hasRow)
            {
                problems.Add($"README「已知限制」点名了{screen.Name}（条目里带着真机未验的标记），"
                             + "登记表里没有它的行：点名了却没登记，下一轮读的人分不清这一层是判过了"
                             + "还是从来没人验过——在表里补一行，层名按实际情况填。");
            }

            if (!named && hasRow)
            {
                problems.Add($"登记表里有{screen.Name}那一行，而 README「已知限制」带真机未验标记的条目"
                             + "已不再点名它：点名没了行还在，这一行就成了没人认领的债——"
                             + "验完了就把行删掉（真机证据归档进 docs/evidence/real-machine/ 之后），"
                             + "并在当轮条目里写清删的是哪一行。");
            }
        }

        return problems;
    }

    /// <summary>夹具：造一份「已知限制」一节（已知限制是最后一节，取到文末）。</summary>
    private static string KnownLimitSection(params string[] items) =>
        "前文。\n\n## 已知限制\n\n" + string.Concat(items.Select(i => i + "\n"));
}
