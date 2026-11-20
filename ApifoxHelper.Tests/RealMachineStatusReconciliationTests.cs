using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「真机供给现状」两侧对账门（第四百二十五轮，控制仓简报 P0-2）。
/// <para>
/// <b>要补的缺口</b>：真机断链的三件现状——<b>台数、到期日、供给侧答复未到</b>——念在两份文件里：
/// <c>docs/NOTES.md</c> 顶部「真机供给状态」那一段（每轮开工第一眼要看的那一段）与 <c>README.md</c>
/// 「已知限制」第 1 条（使用者读的那一份）。两侧各念各的，此前<b>没有任何东西判它们念的是不是同一件事</b>：
/// 第四百二十二轮那一跑之后两侧各改过好几轮，哪一轮只改一处、另一处照抄旧数，不会有任何东西红——
/// 静默漂移正是这一族要挡的事（与「当前源码三档读数」两边 200/206 那一次同一形状）。
/// </para>
/// <para>
/// <b>判据（两侧对账，两个方向都能红）</b>：三件事在两侧各有一份<b>封闭词形</b>（见下面三个词表），
/// 门把两侧各解析出一个值、逐字对上——
/// ① 台数：NOTES 侧「当前台数：N 台」↔ README 侧「真机到手仍 N 台」，两个数必须相等；
/// ② 到期日：NOTES 侧「到期日：YYYY-MM-DD」↔ README 侧「到期日 YYYY-MM-DD」，两个日期必须逐字相同；
/// ③ 答复未到：两侧都必须带着「答复未到」这四个字（这是一个有没有的事实，不是数值）。
/// 任一侧认不出词形（换成词表外的说法）、任一侧少了其中一件、两侧数值对不上，都红。
/// </para>
/// <para>
/// <b>为什么只对 README 第 1 条</b>：「已知限制」第 2 条也念了「0 台」与「2026-11-20」——但那一念是
/// 「初审答辩按降级脚本走」这个<b>决定</b>的引子，不是现状句；对账的对象是现状句 ↔ 现状句。
/// 第 1 条按位置认（「## 已知限制」下的第一条列表项）：真机那一条不再是第一条时门会红，
/// 谁挪了顺序谁同轮把词表跟着挪——挪动这件事要看得见。
/// </para>
/// <para>
/// <b>词表封闭，与第四百二十四轮 <c>KnownLimitScreens</c> 同一纪律</b>：门只认得上面那几个词形。
/// README 或 NOTES 换一种说法念这三件事（比如「真机 0 台」「机器仍缺」），门认不出、当场红——
/// 这不是判据的疏漏，是刻意的：同轮要么把两侧改回词形，要么把新词形加进这张词表并在当轮条目里写明它用在哪侧。
/// 不登记就自创新说法，那一侧落在门外——「两侧对账」就退回只有人眼在盯。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机到底有没有到手（那是 <c>collect-real-machine.sh</c> 与
/// <c>docs/evidence/real-machine/</c> 的地盘）、不判 G-D5 那一格（勾选框只由真机证据关闭）、
/// 不判验收表里的到期日与顶部那一段是不是同一份日期（那两侧各有自己的门）——
/// 它判的只有一件事：<b>同一件现状在两份文件里念出来必须是同一个答案</b>。
/// 抓不抓得住由 <see cref="StatusReconciliationDetector_ReportsTheBreaksItMustCatch"/> 拿九发注入当场验。
/// </para>
/// </summary>
public class RealMachineStatusReconciliationTests
{
    // ---------- 三件事的封闭词表（两侧各一份；换说法先改这里并在当轮条目写明） ----------

    /// <summary>NOTES 顶部那一段念台数的词形：「当前台数：N 台」。</summary>
    private static readonly Regex NotesMachineCount = new(@"当前台数：(?<n>\d+) 台", RegexOptions.Compiled);

    /// <summary>README「已知限制」第 1 条念台数的词形：「真机到手仍 N 台」。</summary>
    private static readonly Regex ReadmeMachineCount = new(@"真机到手仍 (?<n>\d+) 台", RegexOptions.Compiled);

    /// <summary>NOTES 顶部那一段念到期日的词形：「到期日：YYYY-MM-DD」。</summary>
    private static readonly Regex NotesDueDate = new(@"到期日：(?<d>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    /// <summary>README「已知限制」第 1 条念到期日的词形：「到期日 YYYY-MM-DD」。</summary>
    private static readonly Regex ReadmeDueDate = new(@"到期日 (?<d>\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// 「供给侧答复未到」这一件在两侧共同的词形：「答复未到」。这是一个有没有的事实——
    /// 答复到了那天两侧都要改写，那时这两个词会一起消失、门当场红，改写的人同轮把新词形登记进来。
    /// </summary>
    private const string PendingAnswerMarker = "答复未到";

    // ---------- 判据本体 ----------

    [Fact]
    public void SupplyStatusFacts_AgreeBetweenNotesTopAndReadmeKnownLimitItem1()
    {
        var problems = StatusReconciliationProblems(NotesTopBlock(), ReadmeKnownLimitFirstItem());
        Assert.True(problems.Count == 0,
            "真机供给的三件现状在 docs/NOTES.md 顶部与 README.md「已知限制」第 1 条之间对不上：\n  · "
            + string.Join("\n  · ", problems)
            + "\n改法：两侧按同一件事改——只改一处不动另一处就是静默漂移。"
            + "任一侧要换说法（词表外）时，同轮把新词形加进本文件的词表并在当轮条目里写明它用在哪一侧。");
    }

    // ---------- 判据自己抓不抓得住 ----------

    /// <summary>
    /// 九发注入：合规一发必须<b>不报</b>，破坏八发必须<b>当场报</b>——两个方向（改 NOTES / 改 README）各红一次，
    /// 三件事（台数 / 到期日 / 答复未到）逐件喂到。与 <c>RealMachineSupplyGateTests.Detector_ReportsTheBreaksItMustCatch</c>
    /// 同一纪律：判据自己没被跑过，比没有判据更贵。
    /// </summary>
    [Fact]
    public void StatusReconciliationDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：三件事两侧都在、数值相同——一处都不报。
        Assert.Empty(StatusReconciliationProblems(NotesFixture(), ReadmeFixture()));

        // ② 台数：NOTES 那一句改一个数（0 → 1）——NOTES 侧红一次。
        var notesCountDrift = StatusReconciliationProblems(
            NotesFixture("当前台数：1 台"), ReadmeFixture());
        Assert.Single(notesCountDrift);
        Assert.Contains("台数", notesCountDrift[0], StringComparison.Ordinal);
        Assert.Contains("NOTES", notesCountDrift[0], StringComparison.Ordinal);

        // ③ 台数：README 那一句改一个数（0 → 2）——README 侧红一次。
        var readmeCountDrift = StatusReconciliationProblems(
            NotesFixture(), ReadmeFixture("真机到手仍 2 台"));
        Assert.Single(readmeCountDrift);
        Assert.Contains("台数", readmeCountDrift[0], StringComparison.Ordinal);
        Assert.Contains("README", readmeCountDrift[0], StringComparison.Ordinal);

        // ④ 到期日：NOTES 侧改一天——两个日期对不上。
        var notesDateDrift = StatusReconciliationProblems(
            NotesFixture(dueDate: "2026-12-01"), ReadmeFixture());
        Assert.Single(notesDateDrift);
        Assert.Contains("到期日", notesDateDrift[0], StringComparison.Ordinal);

        // ⑤ 到期日：README 侧改一天——同一个方向也红。
        var readmeDateDrift = StatusReconciliationProblems(
            NotesFixture(), ReadmeFixture(dueDate: "2026-12-01"));
        Assert.Single(readmeDateDrift);
        Assert.Contains("到期日", readmeDateDrift[0], StringComparison.Ordinal);

        // ⑥ 答复未到：NOTES 侧删掉那半句——「答复到了还是没到」在 NOTES 侧没有落点。
        var notesAnswerGone = StatusReconciliationProblems(
            NotesFixture(dropAnswer: true), ReadmeFixture());
        Assert.Single(notesAnswerGone);
        Assert.Contains("答复未到", notesAnswerGone[0], StringComparison.Ordinal);
        Assert.Contains("NOTES", notesAnswerGone[0], StringComparison.Ordinal);

        // ⑦ 答复未到：README 侧删掉那半句——同一件事的 README 侧落点没了。
        var readmeAnswerGone = StatusReconciliationProblems(
            NotesFixture(), ReadmeFixture(dropAnswer: true));
        Assert.Single(readmeAnswerGone);
        Assert.Contains("答复未到", readmeAnswerGone[0], StringComparison.Ordinal);
        Assert.Contains("README", readmeAnswerGone[0], StringComparison.Ordinal);

        // ⑧ 词表外的新说法：NOTES 侧把「当前台数：0 台」改写成「真机 0 台」——门认不出，等于这一侧没有落点。
        var outOfVocabulary = StatusReconciliationProblems(
            NotesFixture("真机 0 台"), ReadmeFixture());
        Assert.Single(outOfVocabulary);
        Assert.Contains("当前台数：N 台", outOfVocabulary[0], StringComparison.Ordinal);

        // ⑨ README 第 1 条整个换成别的话：三件事一件都读不到——三发各报一次。
        var everythingGone = StatusReconciliationProblems(
            NotesFixture(), "- **这是另一条已知限制**：与真机无关。");
        Assert.Equal(3, everythingGone.Count);
    }

    // ---------- 判据本体（收成一处，好让上面那一发发注入各喂一遍） ----------

    /// <summary>
    /// 判据本体：NOTES 顶部那一段的行 + README「已知限制」第 1 条的原文进，问题清单出（空 = 两侧对得上）。
    /// <para>
    /// 收成一处而不是写在 <see cref="SupplyStatusFacts_AgreeBetweenNotesTopAndReadmeKnownLimitItem1"/> 里，
    /// 是为了让 <see cref="StatusReconciliationDetector_ReportsTheBreaksItMustCatch"/> 能把每一发破坏各喂一遍——
    /// 注入验的就是仓里真跑的那一条。
    /// </para>
    /// </summary>
    private static List<string> StatusReconciliationProblems(string notesTop, string readmeItem)
    {
        var problems = new List<string>();

        // ① 台数：两侧各解析一个数，逐字对上；任一侧认不出词形即红。
        var notesCount = NotesMachineCount.Match(notesTop);
        var readmeCount = ReadmeMachineCount.Match(readmeItem);
        if (!notesCount.Success)
        {
            problems.Add("docs/NOTES.md 顶部「真机供给状态」那一段里读不到「当前台数：N 台」："
                         + "真机台数在 NOTES 侧没有可机械读的落点——改回词形，或同轮把新词形加进本文件的词表。");
        }

        if (!readmeCount.Success)
        {
            problems.Add("README.md「已知限制」第 1 条里读不到「真机到手仍 N 台」："
                         + "真机台数在 README 侧没有可机械读的落点——改回词形，或同轮把新词形加进本文件的词表。");
        }

        if (notesCount.Success && readmeCount.Success
            && notesCount.Groups["n"].Value != readmeCount.Groups["n"].Value)
        {
            problems.Add($"真机台数两侧不是同一个数：NOTES 顶部写「当前台数：{notesCount.Groups["n"].Value} 台」，"
                         + $"README 第 1 条写「真机到手仍 {readmeCount.Groups["n"].Value} 台」——"
                         + "同一件现状只能有一个数，改数字时两侧一起改。");
        }

        // ② 到期日：与台数同一形状。
        var notesDate = NotesDueDate.Match(notesTop);
        var readmeDate = ReadmeDueDate.Match(readmeItem);
        if (!notesDate.Success)
        {
            problems.Add("docs/NOTES.md 顶部「真机供给状态」那一段里读不到「到期日：YYYY-MM-DD」："
                         + "到期日在 NOTES 侧没有可机械读的落点——改回词形，或同轮把新词形加进本文件的词表。");
        }

        if (!readmeDate.Success)
        {
            problems.Add("README.md「已知限制」第 1 条里读不到「到期日 YYYY-MM-DD」："
                         + "到期日在 README 侧没有可机械读的落点——改回词形，或同轮把新词形加进本文件的词表。");
        }

        if (notesDate.Success && readmeDate.Success
            && notesDate.Groups["d"].Value != readmeDate.Groups["d"].Value)
        {
            problems.Add($"到期日两侧不是同一天：NOTES 顶部写「{notesDate.Groups["d"].Value}」，"
                         + $"README 第 1 条写「{readmeDate.Groups["d"].Value}」——"
                         + "同一件现状只能有一个日期，改的时候两侧一起改。");
        }

        // ③ 答复未到：两侧都必须带着这四个字。
        if (!notesTop.Contains(PendingAnswerMarker, StringComparison.Ordinal))
        {
            problems.Add("docs/NOTES.md 顶部「真机供给状态」那一段里没有「答复未到」："
                         + "供给侧的答复到了还是没到，在 NOTES 侧没有落点——答复没到就写「供给侧答复未到」，"
                         + "到了那天两侧同轮改写并把新词形登记进本文件的词表。");
        }

        if (!readmeItem.Contains(PendingAnswerMarker, StringComparison.Ordinal))
        {
            problems.Add("README.md「已知限制」第 1 条里没有「答复未到」："
                         + "供给侧的答复到了还是没到，在使用者读的那份文件里没有落点——"
                         + "答复没到就写「供给请求的答复未到」，到了那天两侧同轮改写并把新词形登记进本文件的词表。");
        }

        return problems;
    }

    // ---------- 两侧现状句的取法 ----------

    /// <summary>
    /// docs/NOTES.md 顶部「真机供给状态」那一段：第一个 <c>## </c> 标题之前的全部行。
    /// <para>
    /// 与 <c>RealMachineSupplyGateTests.SupplySectionLines</c> 同一取法（那一段按
    /// <c>AcceptanceChecklistTests.SectionLines</c> 的口径必须排在验收表之前，第一个 <c>## </c> 就是边界）。
    /// </para>
    /// </summary>
    private static string NotesTopBlock()
    {
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        var end = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
        return string.Join('\n', lines[..(end < 0 ? lines.Length : end)]);
    }

    /// <summary>
    /// README.md「已知限制」的第 1 条：「## 已知限制」标题之后第一条以 <c>- </c> 起头的列表项。
    /// <para>
    /// 与 <c>RealMachineGapCoverageTests</c> 切「已知限制」一节同一取法。按位置认第一条：
    /// 真机那一条不再是第一条时，三件事一件都读不到、门当场红——挪顺序这件事要看得见。
    /// </para>
    /// </summary>
    private static string ReadmeKnownLimitFirstItem()
    {
        var lines = Repo.Source("README.md").ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## 已知限制");
        if (start < 0) return "";
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var firstItem = Array.Find(lines[(start + 1)..(end < 0 ? lines.Length : end)],
            l => l.StartsWith("- ", StringComparison.Ordinal));
        return firstItem ?? "";
    }

    // ---------- 夹具（词形与两侧真文件里的写法逐字同源） ----------

    private static string NotesFixture(
        string machineCountLine = "当前台数：0 台",
        string dueDate = "2026-11-20",
        bool dropAnswer = false) =>
        "> **真机供给状态**：这一段是现状。\n>\n"
        + $"> - **{machineCountLine}**（Windows 10/11）。\n"
        + $"> - **到期日：{dueDate}**（以验收表 G-D5 那一格为准）。\n"
        + "> - **本轮为真机供给做了什么：本轮未读**——理由：上一轮已判 0 信息量；距上次现读第 1 轮。"
        + (dropAnswer ? "九个归档目录无新文件。" : "供给侧答复未到、九个归档目录无新文件。") + "\n";

    private static string ReadmeFixture(
        string machineCountPhrase = "真机到手仍 0 台",
        string dueDate = "2026-11-20",
        bool dropAnswer = false) =>
        "前文。\n\n## 已知限制\n\n"
        + "- **原生桌面客户端未在 Windows 真机上运行过**（真机证据现状）：**" + machineCountPhrase
        + (dropAnswer ? "。**" : "，供给请求的答复未到**")
        + "：现读那一次的退出码记在 `docs/NOTES.md` 顶部。到期日 " + dueDate
        + " 已过期；G-D5 如实标为「未达成」。";
}
