using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「作业结束之后状态行说结果」这件事在冒烟器里<b>真的被量过</b>（第三百八十五轮，具名 pass：evidence + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第三百七十八轮把那一句写进了产品——判定在 <see cref="JobOutcome"/>、
/// 触达在 <c>TasksPage.ShowJobOutcome</c>，Linux 侧那一族门（<c>JobOutcomeTests</c>）也在，
/// 但冒烟器里 <c>JobOutcome</c> / <c>ShowJobOutcome</c> / <c>FinishedText</c> 三处零命中：
/// 那一片一直是<b>「有门无读数」</b>。被追问「作业跑完之后屏幕上真的说过结果吗」时，仓里没有任何一笔
/// wine 读数可念（登记在 <c>README.md</c>「已知限制」与 <c>docs/NOTES.md</c> 第三百八十一轮那一节）。
/// </para>
/// <para>
/// <b>为什么还要一段门判冒烟段</b>：读数只在 wine 跑的时候才存在，而「那一段还在、却不再量那件事」
/// 是 Linux 侧唯一能当场抓住的形状——把 <c>PerformClick</c> 换成直接调私有方法、把页面的轮询停掉、
/// 或者让期望改口去调 <see cref="JobOutcome.FinishedText"/>（断言与产品同源就恒真），
/// 三者在 wine 报告里都还是「PASS」，只有这一段被掏空这件事能被源码切片判出来。
/// </para>
/// <para>
/// <b>这一份判五件事</b>：① 点的是<b>扫描区</b>那一行的那颗「开始扫描」（真实 <c>PerformClick</c>），
/// 不是按文案在全页找（横幅在「未扫描」那一步里也写着同一句）；② 页面的<b> own 轮询</b>被起起来了
/// （<c>OnPageShown()</c> 一处——作业结束之后说结果的那一步 <c>OnTick → LoadTasks → ShowJobOutcome</c>
/// 只有它会走）、等的是它写出来的那一行（<c>PumpUntilCondition</c> 两处：等成功那一句、等失败那一句），第二支量
/// 「之后还在不在」用的是真的跨过轮询间隔的 <c>Thread.Sleep(600)</c>；③ 期望是<b>屏上那一句字面</b>与色调，段体里<b>不许</b>调
/// <see cref="JobOutcome.FinishedText"/> 或 <see cref="JobOutcome.Tone"/>；④ 「作业真跑过」<b>从磁盘读回来</b>
/// （<c>Stages.Source.ScannedAt</c>），不靠进度行亮没亮；⑤ 第三百八十五轮在 120 / 192 两档量到的那个<b>产品缺口</b>
/// 本轮已修（<c>TasksPage.StartTaskJob</c> 在起作业时当场记下欠账），第三支因此回到段里——这一族改判
/// 「失败那一支真的量了哪几件」：那一句的开头、我写下的那一个项目路径、色调 <c>BadText</c>、磁盘上没有这一次扫描的时间；
/// 上一轮那两档的逐字红话与本轮修法仍留在注释里（读过的人才知道这一支为什么存在，也不会把它当「一直在过」）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判那一句在屏幕上停留得够不够久、颜色好不好认——那是 wine 那一跑与人眼的事
/// （读数在 <c>.winecheck/reading.txt</c>，观感随 G-D5 真机复核，真机 0 台）；
/// 也不判作业本身跑得对不对（<c>SourceScanService</c> 另有自己的用例）。
/// </para>
/// </summary>
public class JobOutcomeStatusSmokeTests
{
    private const string Segment = "RunJobOutcomeStatus";

    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>段体（去注释后的代码，与判据读的是同一层）。</summary>
    private static string SegmentBody => Body(CodeScan.Text(SmokeProgram), $"private static void {Segment}(")
        ?? throw new InvalidOperationException($"冒烟器里找不到 {Segment} 的方法体：注册了却没有段体，这一段等于没跑。");

    /// <summary>
    /// ① 点的是扫描区那一行的那颗「开始扫描」，而且是真实 <c>PerformClick</c>。
    /// <para>
    /// 全页按文案点（<c>Click(page, "开始扫描")</c>）在这一屏会点到<b>横幅</b>那一颗——同一个入口、
    /// 但不是这一段要量的那一行（进度行收起这一判读的是扫描区那一行里的进度标签）。
    /// </para>
    /// </summary>
    [Fact]
    public void Segment_ClicksTheScanRowButton_ByPerformClick()
    {
        var body = SegmentBody;
        // 两次真点：成功那一支与「几十毫秒内就失败」那一支（第三百八十六轮补回的那一支）各一次。
        Assert.Equal(2, Count(body, "scanButton.PerformClick();"));
        Assert.DoesNotContain("Click(page,", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Click(shown,", body, StringComparison.Ordinal);
        // 那颗按钮与那颗进度标签都取自同一个行助手——不是在全页另找一颗同名按钮（三支各取一次）。
        Assert.Equal(3, Count(body, "ScanActionRow("));
    }

    /// <summary>
    /// ② 页面自己的轮询被起起来、等的是它写出来的那一行；第三支量的是「等过轮询间隔之后还在」。
    /// </summary>
    [Fact]
    public void Segment_RunsOnThePagesOwnPolling()
    {
        var body = SegmentBody;
        // 计时器在 OnPageShown 里 Start()：不起它，OnTick → LoadTasks → ShowJobOutcome 这一步永远不会走。
        Assert.Equal(1, Count(body, ".OnPageShown();"));
        // 泵两次：等成功那一句、等失败那一句（两支都不许改成「与上一句不同」）。
        Assert.Equal(2, Count(body, "PumpUntilCondition("));
        // 泵必须排在「真的点下去」之后，否则等的是上一屏留下的一句话。
        Assert.True(body.IndexOf("scanButton.PerformClick();", StringComparison.Ordinal)
            < body.IndexOf("PumpUntilCondition(", StringComparison.Ordinal));
        // 第三支是「之后还在不在」：要真的跨过轮询间隔等三次（一处 sleep 在跑三次的循环里），不是紧接着读一次。
        Assert.Equal(1, Count(body, "Thread.Sleep(600);"));
        Assert.Contains("for (var i = 0; i < 3; i++)", body, StringComparison.Ordinal);
        // 点之前先核「选中真的落到了要量的那一个任务」——不落时跑的是上一个任务，
        // 屏上会停在上一句，红话分不清是「没换选中」还是「没跑出那一句」（96 档第二发正是那样等的）。
        // 两支要点的那一支各核一次（第三支换了任务，更得核）。
        Assert.Equal(2, Count(body, "SelectedTaskId("));
    }

    /// <summary>
    /// ③ 期望是屏上那一句字面与两种色调；④ 「作业真跑过」从磁盘读回来。段体不许调产品口径（同源即恒真）。
    /// </summary>
    [Fact]
    public void Segment_ExpectsTheSentenceOnTheScreen_NotTheSharedCalculator()
    {
        var body = SegmentBody;
        // 等的必须是那一句本身：改成「与上一句不同」会被作业结束那一次刷新的中间值（清空、筛选计数）筛掉。
        Assert.Contains("return status.Text == \"源码扫描完成。\";", body, StringComparison.Ordinal);
        Assert.Contains("!= \"源码扫描完成。\"", body, StringComparison.Ordinal);
        Assert.Contains("said != \"源码扫描完成。\"", body, StringComparison.Ordinal);
        // 色调也判：成功是 muted（颜色 + 文字双通道，设计验收 D3——成功不该抢强调色）。
        Assert.Contains("UiTokens.Muted", body, StringComparison.Ordinal);
        // 作业真跑过这件事从磁盘读回来，不靠进度行亮没亮（第一发正是把它当前提才红的）。
        Assert.Contains("Stages.Source.ScannedAt", body, StringComparison.Ordinal);
        Assert.Contains("sawProgress |= progress.Visible;", body, StringComparison.Ordinal);
        // 一调口径，断言就与产品同源、恒真——这一条是这一族的关键（同 RunStatusFullText 的取舍）。
        Assert.DoesNotContain("JobOutcome.FinishedText", body, StringComparison.Ordinal);
        Assert.DoesNotContain("JobOutcome.Tone", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑤ 失败那一支（第三百八十五轮量到产品缺口、第三百八十六轮修完之后补回段里）真的在量东西：
    /// 段体里要有那一句的开头、我写下的那一个项目路径、色调 bad 那一判、以及「磁盘上没有这一次扫描的时间」那一判；
    /// 而上一轮那两档的逐字红话与本轮的修法必须仍留在同一个方法的注释里（读过的人才知道这一支为什么存在）。
    /// </summary>
    [Fact]
    public void Segment_MeasuresTheFastFailure_WithoutCallingTheSharedCalculator()
    {
        var body = SegmentBody;
        // 等的是那一句本身的开头，不是「与上一句不同」：上一支留下的正是「源码扫描完成。」。
        Assert.Contains("status.Text.StartsWith(\"源码扫描失败：\", StringComparison.Ordinal)", body, StringComparison.Ordinal);
        // 原因逐字来自服务层：冒烟侧只认自己写下的那一个路径，不调 JobOutcome.FinishedText 生成整句。
        Assert.Contains("status.Text.Contains(missingProject", body, StringComparison.Ordinal);
        Assert.Contains("UiTokens.BadText", body, StringComparison.Ordinal);
        // 那一句与落盘的结果对得上：屏上说着失败，磁盘上就不该留着这一次扫描的时间。
        Assert.Contains("Stages.Source.ScannedAt is { } at", body, StringComparison.Ordinal);
        // 这一支换了任务，点之前先核选中真的落在那一个要量的任务上（不落时跑的是上一个任务）。
        Assert.Contains("SelectTaskRow(shown, failedTask.Id);", body, StringComparison.Ordinal);
        Assert.Contains("selectedId != failedTask.Id", body, StringComparison.Ordinal);
        // 超时那一把的红话要把上一轮两档的位置指出来：读的人不必翻账本就知道这一红意味着什么。
        Assert.Contains("第三百八十五轮在 120 与 192 两档量到的正是这一发", body, StringComparison.Ordinal);

        // 注释里仍留着上一轮那两档的红话与本轮的修法：缺口为什么修掉、怎么修的，读这一段就能看到。
        var doc = SmokeProgram[(SmokeProgram.IndexOf($"private static void {Segment}(", StringComparison.Ordinal) - 8_000)..];
        Assert.Contains("120 与 192 两档同一版源码上各红一次", doc, StringComparison.Ordinal);
        Assert.Contains("那不是量法问题，是产品缺口", doc, StringComparison.Ordinal);
        Assert.Contains("StartTaskJob", doc, StringComparison.Ordinal);
        // 本轮踩过的那一发（在换任务那一处抹欠账）也留成文字，并且它已被 JobOutcomeTests 的第 ⑧ 发注入判住。
        Assert.Contains("OnSelectedChanged", doc, StringComparison.Ordinal);
        Assert.Contains("Items.Clear", doc, StringComparison.Ordinal);
    }

    /// <summary>三支各留一句能读的话（红话要被念得出来，不能只剩一个条件表达式）。</summary>
    [Fact]
    public void Segment_KeepsReadableFailures()
    {
        var body = SegmentBody;
        foreach (var sentence in new[]
                 {
                     "选中没有落到",
                     "那一句结果没出现在屏上",
                     "进度行收起之后屏上没有一句话说这一次是成功的",
                     "磁盘上却没有扫描时间",
                     "那一句报完就被抹掉了",
                     "三次轮询之后状态行从",
                     // 第三支（第三百八十六轮补回的那一支）：原因、色调、与落盘结果对不上这三样各有自己的话。
                     "里面没有我写下的那一个 API 项目路径",
                     "的色调不是 bad",
                     "屏上说着这一次失败，磁盘上却留着扫描时间",
                 })
        {
            Assert.Contains(sentence, body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 这一段有登记：在 <see cref="MeasuredScreens.All"/> 里、标着已量、轮次非空；且注册在 <c>Main</c> 里只调一次。
    /// </summary>
    [Fact]
    public void Segment_IsRegisteredAndCalledFromMainOnce()
    {
        var row = Assert.Single(MeasuredScreens.All, r => r.Section == Segment);
        Assert.True(row.Measured, $"登记里「{row.Screen}」标的是未量，而这一段真的 Show() 出了任务页。");
        Assert.False(string.IsNullOrWhiteSpace(row.Round));
        Assert.Contains("TasksPage", row.Classes);
        Assert.Equal(1, Regex.Count(SmokeProgram, @"(?m)^\s{8}RunJobOutcomeStatus\(services, dataRoot\);$"));
        Assert.Equal(1, Regex.Count(SmokeProgram, $@"nameof\({Segment}\)"));
    }

    /// <summary>从去注释后的源码里取一个方法体（按花括号配对，不按「下一个 private」切）。</summary>
    private static string? Body(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0) return null;
        var open = code.IndexOf('{', start);
        if (open < 0) return null;
        var depth = 0;
        for (var i = open; i < code.Length; i++)
        {
            if (code[i] == '{') depth++;
            else if (code[i] == '}' && --depth == 0) return code[open..(i + 1)];
        }

        return null;
    }

    private static int Count(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
