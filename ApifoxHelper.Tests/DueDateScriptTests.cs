using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 到期日核对脚本（<c>.winecheck/due-date.sh</c>，第二百二十五轮，简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：到期日这件事在仓里被写进了三处（<c>docs/NOTES.md</c> 的 G-D5 行、
/// 「到期日判定（细则）」子段、「到期日当天执行单」那页），但<b>没有任何东西能在当天告诉人
/// 「现在该走降级还是该判过期」</b>——两条既有门判的是「到期日不是过去的也不是未来的」
/// （<see cref="AcceptanceChecklistTests.Gd5DueDate_IsNotStaleOrFuture"/> ①②④）与
/// 「那一段在位且日期一致」（<see cref="AcceptanceChecklistTests.Gd5_ExpiryVerdict_IsRecordedWithTheDegradationRunPath"/>），
/// <b>都判不到「今天是不是到期日」</b>。当天的人只能自己看日历。
/// </para>
/// <para>
/// <b>本文件判两层</b>：① <b>源码形状</b>——到期日<b>现读</b>（脚本里不许出现第二个日期字面量）、
/// 三个分支各带自己的退出码、读 G-D5 那一行的模式与门同一个；② <b>真的跑</b>——在临时假树上把
/// 未到 / 当天 / 已过 / 核对跑不动四条分支各跑活一次，证明它判的就是它说在判的那件事。
/// </para>
/// <para>
/// <b>跨门对齐</b>：已过那一支打出来的「已过期」与「降级」两个词，<b>不是这里抄的</b>——
/// <see cref="PastDue_SaysExpiredAndDegradedWithTheSameWordsAsTheSiblingGate"/> 从兄弟门
/// <c>Gd5DueDate_IsNotStaleOrFuture</c> ③ 的源码里现读那两个词再对。第二百一十一轮踩过这个坑：
/// 预登记写「已到期」而兄弟门读「已过期」，到期日第二天红的会是那一格，而预登记看起来是齐的。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机跑没跑、不替人打勾、不改 G-D5 那一格，也不判「当天该选降级还是延后」
/// （那是答辩负责人的决定）。它只回答「今天该走哪一支」。
/// </para>
/// </summary>
public class DueDateScriptTests
{
    private const string ScriptRelative = ".winecheck/due-date.sh";

    private static string ScriptPath => Path.Combine(Repo.Root, ".winecheck", "due-date.sh");

    private static string ScriptText
    {
        get
        {
            Assert.True(File.Exists(ScriptPath),
                $"缺 {ScriptRelative}：到期日当天没有一条能照着走的核对，那天的人只能自己看日历。");
            return File.ReadAllText(ScriptPath);
        }
    }

    /// <summary>
    /// 到期日<b>现读</b>，不抄第二份：脚本里不许出现任何 <c>YYYY-MM-DD</c> 字面量。
    /// <para>
    /// 这是这一支最容易退化的地方——把日期写进脚本，改期那天就只改到文档、脚本还按旧日期判，
    /// 而它<b>照样绿</b>（判的是自己那个字面量）。取法与 <c>AcceptanceChecklistTests</c> 的
    /// 「到期日全仓只有一处出口」同一族纪律。
    /// </para>
    /// </summary>
    [Fact]
    public void Script_ReadsTheDueDateFromNotesInsteadOfCarryingASecondCopy()
    {
        var literals = Regex.Matches(ScriptText, @"\d{4}-\d{2}-\d{2}")
            .Select(m => m.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(literals.Count == 0,
            $"{ScriptRelative} 里写死了日期字面量（{string.Join("、", literals)}）：到期日只能从 docs/NOTES.md 的 G-D5 行现读，"
            + "抄第二份之后改期那天脚本会按旧日期判，而它照样绿。");

        // 读 G-D5 那一行的模式与门同一个（勾与没勾都命中）——两处各写一个模式，改一处另一处不会红。
        Assert.Contains(@"^\| \[.\] \| G-D5", ScriptText, StringComparison.Ordinal);

        // 三个分支各带自己的退出码：0 = 未到或当天、1 = 已过、2 = 核对跑不动。
        Assert.Contains("exit 0", ScriptText, StringComparison.Ordinal);
        Assert.Contains("exit 1", ScriptText, StringComparison.Ordinal);
        Assert.Contains("exit 2", ScriptText, StringComparison.Ordinal);
        Assert.Contains("set -u", ScriptText, StringComparison.Ordinal);
    }

    // ── 真的跑：四条分支各跑活一次 ──────────────────────────────────

    /// <summary>找 bash（PATH，再退三个常规位置）。找不到返回 null，调用方走「这台机器判不到」那一支。</summary>
    private static string? LocateBash()
    {
        var dirs = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path)) dirs.AddRange(path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        dirs.AddRange(["/bin", "/usr/bin", "/usr/local/bin"]);

        foreach (var dir in dirs.Distinct(StringComparer.Ordinal))
        {
            try
            {
                var candidate = Path.Combine(dir, "bash");
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
                // 这一台上不合法的目录写法跳过就行。
            }
        }

        return null;
    }

    /// <summary>跑一次核对；<paramref name="env"/> 里的键值覆盖到子进程环境上。</summary>
    private static (int Exit, string Output) RunDueDate(params (string Key, string Value)[] env) =>
        RunDueDateWithArgs([], env);

    /// <summary>跑一次核对并带上脚本参数（如 <c>--writeback-plan</c>）。</summary>
    private static (int Exit, string Output) RunDueDateWithArgs(string[] args, params (string Key, string Value)[] env)
    {
        // 只用 ArgumentList：两参数的 ProcessStartInfo 构造会填 Arguments，之后再 Add 就撞
        // 「Only one of Arguments or ArgumentList may be used」（第二百三十九轮第一跑当场撞上）。
        var psi = new ProcessStartInfo
        {
            FileName = LocateBash()!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Repo.Root,
        };
        psi.ArgumentList.Add(ScriptPath);
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        psi.Environment["LANG"] = "C.UTF-8";
        foreach (var (key, value) in env) psi.Environment[key] = value;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(60_000), "due-date.sh 跑了 60 秒还没退出——它只该读一个文件、比两个日期。");
        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// 临时假树：一份只带 G-D5 那一行的 <c>docs/NOTES.md</c>。
    /// <para>
    /// 假树是这一条能判「三个分支」的前提：在真仓上跑，结果取决于今天是不是 2026-11-20——
    /// 那样的断言在换一天时判的就不是脚本了。日期由 <c>TODAY</c> 旋钮给，不靠等日历。
    /// </para>
    /// </summary>
    private sealed class FakeNotes : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ah-duedate-" + Guid.NewGuid().ToString("N")[..12]);

        public string Notes => Path.Combine(Root, "docs", "NOTES.md");

        public FakeNotes(string due)
        {
            Directory.CreateDirectory(Path.Combine(Root, "docs"));
            Write(due);
        }

        /// <summary>G-D5 那一行：形状与真仓一致（<c>| [ ] | G-D5 … |</c>，日期写在「到期日」之后）。</summary>
        public void Write(string due) =>
            File.WriteAllText(Notes,
                "| [ ] | G-D5 真机观感 | **未达成（环境供给缺口）**：到期日 " + due + "（第一百八十轮记） |\n");

        public (string Key, string Value)[] Env(string today) =>
        [
            ("REPO", Root),
            ("NOTES", Notes),
            ("TODAY", today),
        ];

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响判据。
            }
        }
    }

    /// <summary>
    /// 未到：打「距到期还有 N 天，按现有路径继续」，退出 0。
    /// <para>
    /// 天数<b>不在这里另算一遍</b>（那等于把日期减法抄第二份）：假树里写 2026-11-20、今天给 2026-11-15，
    /// 期望值就是 5——这个 5 是这一条自己给的输入，不是从脚本里读回来的。
    /// </para>
    /// </summary>
    [Fact]
    public void NotYetDue_SaysHowManyDaysAreLeftAndExitsZero()
    {
        if (LocateBash() is null) return; // 这台机器上没有 bash：判不到。

        using var tree = new FakeNotes("2026-11-20");
        var (exit, output) = RunDueDate(tree.Env("2026-11-15"));

        Assert.True(exit == 0, $"到期日还没到时核对却非零退出（{exit}）：那天的人会以为已经过期。输出：{output}");
        Assert.Contains("距到期还有 5 天", output, StringComparison.Ordinal);
        Assert.Contains("按现有路径继续", output, StringComparison.Ordinal);
        Assert.DoesNotContain("已过期", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// 当天：打「今天是到期日」与降级答辩三步（<c>PUBLISH=1 bash .winecheck/demo.sh</c>、
    /// 念 <c>narration-*.txt</c>、如实标 G-D5 未达成），退出 0。
    /// <para>
    /// 三步各判一次：只判「今天是到期日」不够——那天真正要照着做的是这三步，少一步现场就得现编。
    /// </para>
    /// </summary>
    [Fact]
    public void DueToday_PointsAtTheThreeDegradationStepsAndExitsZero()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeNotes("2026-11-20");
        var (exit, output) = RunDueDate(tree.Env("2026-11-20"));

        Assert.True(exit == 0, $"到期日当天核对却非零退出（{exit}）：那天该走降级答辩，不是判过期。输出：{output}");
        Assert.Contains("今天是到期日 2026-11-20", output, StringComparison.Ordinal);
        Assert.Contains("PUBLISH=1 bash .winecheck/demo.sh", output, StringComparison.Ordinal);
        Assert.Contains("narration-", output, StringComparison.Ordinal);
        Assert.Contains("如实标 G-D5 未达成", output, StringComparison.Ordinal);
        Assert.DoesNotContain("已过期", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// 已过：退出 1，且打出来的词与兄弟门 <c>Gd5DueDate_IsNotStaleOrFuture</c> ③ 读的<b>逐字相同</b>。
    /// <para>
    /// <b>为什么从兄弟门源码里现读</b>：第二百一十一轮踩过这个坑——预登记写「已到期」，而兄弟门在
    /// 到期日过去之后读的是「已过期」与「降级」，照原文写下去 2026-11-21 红的会是 G-D5 那一格，
    /// 而预登记看起来是齐的。把词表抄进本文件等于把那个坑复制一份：兄弟门改了词，这里照样绿。
    /// 现读之后，兄弟门换词 → 这一条红 → 逼着脚本跟着换。
    /// </para>
    /// </summary>
    [Fact]
    public void PastDue_SaysExpiredAndDegradedWithTheSameWordsAsTheSiblingGate()
    {
        if (LocateBash() is null) return;

        var words = SiblingGateExpiryWords();
        Assert.True(words.Length >= 2,
            $"从兄弟门 Gd5DueDate_IsNotStaleOrFuture ③ 里只读到 {words.Length} 个词（{string.Join("、", words)}）："
            + "取法退化了，这一条就变成「在空集合上相等」。");

        using var tree = new FakeNotes("2026-11-20");
        var (exit, output) = RunDueDate(tree.Env("2026-11-21"));

        Assert.True(exit == 1, $"到期日已过时核对退出码是 {exit}，不是 1：当天的人分不清「该改判」与「核对跑不动」。输出：{output}");
        Assert.Contains("到期日已过", output, StringComparison.Ordinal);
        Assert.Contains("docs/NOTES.md G-D5 行", output, StringComparison.Ordinal);

        // 判的是**结论那一行**，不是整段输出：整段里还有一句「同一格必须同时留着『已过期』与『降级』」，
        // 拿整段做 Contains 会被那一句满足——那正是本仓记过的 Presence 型假绿（同一字符串里被另一处满足）。
        // 第二百二十五轮磁盘上实测过：把结论行里的「已过期」改成「已到期」，整段断言照样绿。
        var conclusion = output.ReplaceLineEndings("\n").Split('\n')
            .FirstOrDefault(l => l.Contains("到期日已过：", StringComparison.Ordinal));
        Assert.True(conclusion is not null, $"已过那一支没有打出「到期日已过：」这一行结论。输出：{output}");

        foreach (var word in words)
        {
            Assert.True(conclusion!.Contains(word, StringComparison.Ordinal),
                $"已过那一支的结论行里没有「{word}」——兄弟门 Gd5DueDate_IsNotStaleOrFuture ③ 在到期日之后读的就是这个词，"
                + $"脚本与它逐字对齐才不会出现「预登记看着齐、那天红在格子」。结论行：{conclusion}");
        }

        // 脚本自己得说清这两个词是从哪来的，否则下一个人会以为是随手写的措辞。
        Assert.Contains("Gd5DueDate_IsNotStaleOrFuture", ScriptText, StringComparison.Ordinal);
    }

    /// <summary>
    /// 核对本身跑不动时退出 2（与 <c>collect-real-machine.sh</c> 的 0 / 1 / 2 分工同一族）：
    /// 读不到 NOTES、找不到 G-D5 那一行、行里没有日期、<c>TODAY</c> 不是日期——四种都点名，不静默放行。
    /// <para>
    /// 这一条挡的是最坏的一种：核对跑不动却退出 0，那天的人会以为「还没到，继续」。
    /// </para>
    /// </summary>
    [Fact]
    public void UnreadableInputs_ExitTwoInsteadOfPretendingEverythingIsFine()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeNotes("2026-11-20");

        // ① NOTES 指到不存在的文件。
        var (exitA, outputA) = RunDueDate(("REPO", tree.Root), ("NOTES", Path.Combine(tree.Root, "no-such-NOTES.md")), ("TODAY", "2026-11-15"));
        Assert.True(exitA == 2, $"NOTES 读不到时退出码是 {exitA}，不是 2：核对跑不动却像「还没到」。输出：{outputA}");
        Assert.Contains("核对跑不动", outputA, StringComparison.Ordinal);

        // ② 文件在，但没有 G-D5 那一行。
        File.WriteAllText(tree.Notes, "| [ ] | G-D4 别的条目 | 到期日 2026-11-20 |\n");
        var (exitB, outputB) = RunDueDate(tree.Env("2026-11-15"));
        Assert.True(exitB == 2, $"找不到 G-D5 那一行时退出码是 {exitB}，不是 2。输出：{outputB}");
        Assert.Contains("G-D5 那一行", outputB, StringComparison.Ordinal);

        // ③ 行在，但「到期日」后面没有日期。
        tree.Write("（未记）");
        var (exitC, outputC) = RunDueDate(tree.Env("2026-11-15"));
        Assert.True(exitC == 2, $"G-D5 行里没有日期时退出码是 {exitC}，不是 2。输出：{outputC}");
        Assert.Contains("没有「到期日", outputC, StringComparison.Ordinal);

        // ④ TODAY 不是 YYYY-MM-DD。
        tree.Write("2026-11-20");
        var (exitD, outputD) = RunDueDate(tree.Env("明天"));
        Assert.True(exitD == 2, $"TODAY 不是日期时退出码是 {exitD}，不是 2。输出：{outputD}");
        Assert.Contains("不是 YYYY-MM-DD", outputD, StringComparison.Ordinal);
    }

    /// <summary>
    /// 「已过」那一支<b>不带参数</b>时要点名那条能把清单调到屏幕上的命令（第二百三十九轮，简报 P0-1）。
    /// <para>
    /// 补的是哪一个洞：那一天最先跑的一定是不带参数的那一发（<c>defense-day.sh</c> 第 ① 步就是它）。
    /// 只说「应改判『已过期』」而不把<b>下一步命令</b>递到人手上，那天就得自己想起来还有清单这回事。
    /// 这一发同时判它<b>只指过去、不抄过来</b>：清单的表头不许出现在不带参数的输出里。
    /// </para>
    /// </summary>
    [Fact]
    public void PastDue_PointsAtTheWritebackPlanCommand()
    {
        if (LocateBash() is null) return;

        using var tree = new FakeNotes("2026-11-20");
        var (exit, output) = RunDueDate(tree.Env("2026-11-21"));

        Assert.True(exit == 1, $"已过那一支退出码是 {exit}，不是 1：{output}");
        Assert.Contains("--writeback-plan", output, StringComparison.Ordinal);
        Assert.DoesNotContain("| 步 | 改哪一份 |", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--writeback-plan</c> 四种跑法各跑活一次（第二百三十九轮，简报 P0-1）。
    /// <para>
    /// <b>判什么</b>：① 真仓 + 已过那一天 → 退 `1` 且把 <c>docs/NOTES.md</c> 那一段<b>原样</b>打出来
    /// （表头在、第 1 步点到的两处落点都在）；② 未到那一天带同样的参数也打得出来——那一页是参考卡，
    /// 不是只在过期那天才存在，而退出码仍由日期决定（`0`）；③ <b>假树</b>（NOTES 里没有那一段）→
    /// 打「清单读不到」并<b>一张表都不打</b>：这一发同时证明脚本里没有抄第二份，抄了这里就会漏出表头；
    /// ④ 参数打错 → 退 `2` 并点名，不许跑出三个分支之一（那会被读成「这一支认这个参数」）。
    /// </para>
    /// <para><b>它不判什么</b>：不判清单内容对不对——那是
    /// <see cref="AcceptanceChecklistTests.Gd5_PastDueWritebackPlan_IsWalkableAndNamesRealDestinations"/> 的事。</para>
    /// </summary>
    [Fact]
    public void WritebackPlan_PrintsThatSectionVerbatimAndKeepsTheDateExitCode()
    {
        if (LocateBash() is null) return;

        // ① 真仓真跑（TODAY 注入到到期日之后；只读一个文件、比两个日期，不跑任何界面）。
        var (exitPast, outPast) = RunDueDateWithArgs(["--writeback-plan"], ("TODAY", "2026-11-21"));
        Assert.True(exitPast == 1, $"带参数不改变退出码这一条破了：已过那天退出 {exitPast}，应为 1。输出：{outPast}");
        Assert.Contains("到期日已过", outPast, StringComparison.Ordinal);
        Assert.Contains("| 步 | 改哪一份 |", outPast, StringComparison.Ordinal);
        Assert.Contains("到期日判定（细则）", outPast, StringComparison.Ordinal);
        Assert.Contains("README.md", outPast, StringComparison.Ordinal);

        // ② 未到那一天：清单是参考卡，随时可查；退出码仍走「未到」那一支。
        var (exitBefore, outBefore) = RunDueDateWithArgs(["--writeback-plan"], ("TODAY", "2026-11-15"));
        Assert.True(exitBefore == 0, $"还没到改判那天却退 {exitBefore}：退出码判的是日期关系，不是这一页给没给。输出：{outBefore}");
        Assert.Contains("按现有路径继续", outBefore, StringComparison.Ordinal);
        Assert.Contains("| 步 | 改哪一份 |", outBefore, StringComparison.Ordinal);

        // ③ 假树：NOTES 里没有那一段 → 点名读不到，且一张表都不打（脚本里没有第二份，这一步就是它的证明）。
        using var tree = new FakeNotes("2026-11-20");
        var (exitFake, outFake) = RunDueDateWithArgs(["--writeback-plan"], tree.Env("2026-11-21"));
        Assert.True(exitFake == 1, $"读不到清单不改变日期判定：假树上退出码是 {exitFake}，应为 1。输出：{outFake}");
        Assert.Contains("清单读不到", outFake, StringComparison.Ordinal);
        Assert.DoesNotContain("| 步 | 改哪一份 |", outFake, StringComparison.Ordinal);

        // ④ 参数打错：退 2 并点名，不跑分支。
        var (exitArg, outArg) = RunDueDateWithArgs(["--write-plan"]);
        Assert.True(exitArg == 2, $"认不出的参数退出码是 {exitArg}，不是 2：那一发会被读成「这一支认这个参数」。输出：{outArg}");
        Assert.Contains("认不出参数", outArg, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--writeback-plan</c> 在<b>真仓 NOTES</b> 上实跑一次（第二百四十轮，简报 P0-2）：
    /// 清单<b>起于那一段的标题、止于下一个二级 / 三级标题之前</b>。
    /// <para>
    /// <b>补的是哪一个洞</b>：上面 <see cref="WritebackPlan_PrintsThatSectionVerbatimAndKeepsTheDateExitCode"/>
    /// 走的是 <see cref="FakeNotes"/>——假树里那一段<b>后面没有任何别的节</b>，所以「切在下一节之前」
    /// 这一条从来没有在真仓形状上跑过。真仓那一段紧跟着的是另一整节（带自己的标题与正文），
    /// 一旦 <c>awk</c> 那两个 <c>exit</c> 条件被改坏，屏幕上就会连着打出下一节，而当天照着念的人
    /// 会把「退场那天要删哪几行」读成「过期那天要改的其中一步」——**多打出来的那一节比少打更贵**，
    /// 因为它是完整可读的、看不出出了界。
    /// </para>
    /// <para>
    /// <b>边界全部从 NOTES 现读</b>：下一个标题（是哪一节、叫什么名）由本条当场从文件里取，
    /// <b>不写死</b> <c>G-D6</c>——那一节改名或前面插进新的一节，这一条跟着走，而不是先红在措辞上。
    /// </para>
    /// <para><b>它不判什么</b>：不判清单内容对不对（那是
    /// <see cref="AcceptanceChecklistTests.Gd5_PastDueWritebackPlan_IsWalkableAndNamesRealDestinations"/> 的事）、
    /// 不判那一天该不该改判（<c>TODAY</c> 由这一发注入，为的是让它在任何一天都可跑）。</para>
    /// </summary>
    [Fact]
    public void WritebackPlan_OnTheRealRepoNotes_CutsAtTheNextSectionHeading()
    {
        if (LocateBash() is null) return;

        const string Title = "### 到期日已过 · 改判与回写清单";
        var lines = Repo.Source("docs", "NOTES.md").ReplaceLineEndings("\n").Split('\n');
        // 与脚本的 awk 同一个取法：`index($0, head) == 1` 命中的是**整行**，命中后 `next` 从下一行才开始打。
        // 所以这里也必须整行跳过标题——按字符偏移切会把标题行尾部的「（可照走，…）」当成正文第一句。
        var head = Array.FindIndex(lines, l => l.StartsWith(Title, StringComparison.Ordinal));
        Assert.True(head >= 0,
            $"docs/NOTES.md 里找不到以「{Title}」开头的那一行：脚本按标题现读，标题不在了这一发就没有靶子。");

        var body = lines[(head + 1)..];
        var stop = Array.FindIndex(body, l => l.StartsWith("## ", StringComparison.Ordinal)
                                           || l.StartsWith("### ", StringComparison.Ordinal));
        Assert.True(stop > 0,
            "「到期日已过 · 改判与回写清单」那一段之后找不到下一个二级 / 三级标题："
            + "没有边界就没法判「切在下一节之前」，这一条会退化成只看第一段。");

        var nextHeading = body[stop].Trim();
        var firstBody = body.First(l => l.Trim() != "").Trim();
        var lastBody = body[..stop].Reverse().First(l => l.Trim() != "").Trim();
        var nextBody = body[(stop + 1)..].FirstOrDefault(l => l.Trim() != "")?.Trim();
        Assert.True(nextBody is null || !nextBody.StartsWith('#'),
            $"取法退化：{nextHeading} 之后紧跟的还是一行标题，下一节正文没取到。");

        // 真仓真跑：只读两份文件、比两个日期，不跑任何界面；TODAY 注入到到期日之后走「已过」那一支。
        var (exit, output) = RunDueDateWithArgs(["--writeback-plan"], ("TODAY", "2026-11-21"));
        Assert.True(exit == 1, $"真仓 NOTES 上过那一天退出码是 {exit}，不是 1：{output}");

        Assert.Contains("到期日已过 · 改判与回写清单", output, StringComparison.Ordinal);
        Assert.Contains("| 步 | 改哪一份 |", output, StringComparison.Ordinal);
        Assert.Contains(firstBody, output, StringComparison.Ordinal);
        Assert.Contains(lastBody, output, StringComparison.Ordinal);

        // 收口：下一节的标题行与它下面那一句正文都不许出现在屏幕上（少打一段是缺页，多打一段是假页）。
        Assert.DoesNotContain(nextHeading, output, StringComparison.Ordinal);
        if (nextBody is not null) Assert.DoesNotContain(nextBody, output, StringComparison.Ordinal);

        // 屏幕上不许出现任何 markdown 标题行——那一段的正文只有引用块与表格，标题一律是「越界」的形状。
        var headings = output.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.StartsWith('#')).ToList();
        Assert.True(headings.Count == 0,
            $"输出里出现了标题行（{string.Join("、", headings)}）：清单应当切在下一节之前，越界了。");
    }

    /// <summary>
    /// 兄弟门 <c>Gd5DueDate_IsNotStaleOrFuture</c> ③ 在到期日过去之后要求 G-D5 那一格同时含有的词，
    /// <b>从它的源码里现读</b>（<c>row.Verdict.Contains("…")</c> 的字面量）。
    /// <para>
    /// 取法：定位那个方法，切出 <c>// ③</c> 到 <c>// ④</c> 之间那一段，抓
    /// <c>row.Verdict.Contains("…")</c> 里的字面量。兄弟门若改成别的写法（比如换成正则），
    /// 这里读不到词 → 上面那条断言先红在「只读到 0 个词」上，而不是安静地放过。
    /// </para>
    /// </summary>
    private static string[] SiblingGateExpiryWords()
    {
        var text = Repo.Source("ApifoxHelper.Tests", "AcceptanceChecklistTests.cs");
        var start = text.IndexOf("public void Gd5DueDate_IsNotStaleOrFuture()", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "ApifoxHelper.Tests/AcceptanceChecklistTests.cs 里找不到 Gd5DueDate_IsNotStaleOrFuture："
            + "兄弟门改名了，这一条的对齐对象就没了。");
        var third = text.IndexOf("// ③", start, StringComparison.Ordinal);
        var fourth = text.IndexOf("// ④", start, StringComparison.Ordinal);
        Assert.True(third > start && fourth > third,
            "Gd5DueDate_IsNotStaleOrFuture 里找不到 `// ③` / `// ④` 两个分段标记："
            + "取不到「到期日过去之后读哪两个词」那一段。");

        return Regex.Matches(text[third..fourth], @"row\.Verdict\.Contains\(""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
