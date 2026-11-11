using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// `docs/NOTES.md` 顶部「本仓验收口径（可勾选）」一节的机械门（第一百六十六轮，简报 P0-1）。
/// <para>
/// <b>为什么要有这一节</b>：本产品没有对应的 DoD 文件（第一百六十五轮核对 INDEX.md，正文 9 份与豁免名单均无
/// ApifoxHelper）。DoD 缺席时验收口径散在方案 §2.2/§8、README「使用流程」「写入安全」、DESIGN.md 与 NOTES 多处，
/// <b>没有一份能逐项勾选的表</b>——于是任何一轮都可以自行定义「缺口」。这一节把既有口径落成可机械读取的表。
/// </para>
/// <para>
/// <b>这一节本身必须可核对，否则它只是另一份自述</b>：每条「证据出处」的引用都要落到实物——反引号里的
/// <c>路径.cs</c> / <c>路径.md</c> 必须在仓库里存在，<c>类名.断言名</c> 必须在测试工程里真的有这一条断言。
/// 编一个不存在的断言名即红，删掉一行的证据出处即红。勾成 <c>[x]</c> 的行必须至少被一条断言钉住；
/// 没有断言可钉的（端到端从未跑过的红线）只能留 <c>[ ]</c>——<b>「达成」不是写下「达成」就达成的</b>。
/// </para>
/// <para>
/// <b>G-D5 不许被悄悄勾上</b>：真机 0 台这一条每轮重述，门钉在「G-D5 必须是 <c>[ ]</c> 且结论里写未达成」，
/// 而不是钉在某一句措辞上（措辞会漂，见简报对「门钉一种写法」的老毛病）。
/// </para>
/// </summary>
public class AcceptanceChecklistTests
{
    private const string Heading = "## 本仓验收口径（可勾选）";

    /// <summary>本节取出的每一行（一个勾选框一行）。</summary>
    private readonly record struct Row(string Check, string Item, string Verdict, string Evidence, string Gui);

    /// <summary>本节正文的行（从标题行到下一个 <c>## </c> 之前）。</summary>
    private static string[] SectionLines()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var lines = notes.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == Heading);
        Assert.True(start >= 0, $"docs/NOTES.md 顶部缺「{Heading}」一节。");
        // 必须在最顶部：第一处 "## " 标题就得是它，否则「口径表」会被埋到几百行日志之后，读的人找不到。
        var firstHeading = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(start, firstHeading);
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>本节里所有以 <c>|</c> 开头的行切成单元格的结果，含表头行与分隔行——格式检查要看的是全部行，不只是已被接受的那些。</summary>
    private static List<List<string>> CellRows()
    {
        var cells = new List<List<string>>();
        foreach (var line in SectionLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            cells.Add(line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList());
        }
        return cells;
    }

    private static List<Row> Rows() => CellRows()
        .Where(c => c.Count == 5 && (c[0] == "[x]" || c[0] == "[ ]"))
        .Select(c => new Row(c[0], c[1], c[2], c[3], c[4]))
        .ToList();

    /// <summary>
    /// 「本仓没有 DoD 文件」这件事本身要有出处（第一百九十七轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：本节此前只在「为什么要有这一节」那段里顺带提了一句「第一百六十五轮核对过
    /// INDEX.md，正文 9 份与豁免名单都没有 ApifoxHelper」——那是<b>叙述</b>，不是<b>登记</b>：
    /// 没有核对日期、没有写清「既不在正文也不在豁免名单」这个具体结论、也没有一条断言钉住它。
    /// 于是每一轮都可能有人重新去翻一遍调度仓（跨主机、只读、耗时），或者更糟——自己发明一份 DoD。
    /// </para>
    /// <para>
    /// <b>判什么</b>：本节里有一行以「DoD 豁免登记」开头，且同时写着 ① 核对来源（INDEX.md 的路径）、
    /// ② 核对日期（<c>YYYY-MM-DD</c>）、③ 结论（「无 DoD 文件可加载」）。三项缺一即红——
    /// 只写「没有 DoD」而不写从哪核的、哪天核的，下一个人无法判断这条结论是否过期。
    /// </para>
    /// <para>
    /// <b>时效软判（第一百九十九轮，简报 P0-1）</b>：上面三项只判「登记齐不齐」，不判「登记还新不新」——
    /// 一条 2025 年的核对结论与一条昨天的结论在门眼里一模一样。若 INDEX.md 之后新增了 ApifoxHelper 条目
    /// （或把它列进豁免名单），本仓不会有任何红灯，直到有人碰巧重读那一行。所以补一条：核对日期距今
    /// <b>不得超过 90 天</b>，且不得写在未来。过期即红，红的时候要做的事只有一件——重核 INDEX.md，
    /// 把这一行的日期与结论改成这次核到的，门跟着新日期走。这是<b>软判</b>：它不证明结论正确，
    /// 只保证「这条结论是最近核过的」，把「静默过期」变成「到期必须有人看一眼」。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：调度仓里那份 INDEX.md 现在到底写了什么（跨主机、只读、且本仓不该依赖它）。
    /// 本门判的是「本仓把核对结果登记下来了、且是最近核的」，不是「核对结果永远正确」。
    /// </para>
    /// </summary>
    [Fact]
    public void DodExemption_IsRegisteredWithSourceAndDate()
    {
        var line = SectionLines()
            .FirstOrDefault(l => l.Contains("DoD 豁免登记", StringComparison.Ordinal));

        Assert.True(line is not null,
            "docs/NOTES.md「本仓验收口径」一节缺「DoD 豁免登记」一行：本仓没有 DoD 文件这件事必须有出处，"
            + "否则下一轮要么重新去翻调度仓，要么自己发明一份 DoD。");

        // ① 核对来源：从哪一份文件核的。
        Assert.Contains("product-dod/INDEX.md", line!, StringComparison.Ordinal);
        // ② 核对日期：这条结论是哪天核的（过期了要重核）。
        Assert.Matches(@"20\d{2}-\d{2}-\d{2}", line!);
        // ③ 结论：不是「大概没有」，是「无 DoD 文件可加载」。
        Assert.Contains("无 DoD 文件可加载", line!, StringComparison.Ordinal);

        // ④ 时效：核对日期距今不得超过 90 天，也不得写在未来（第一百九十九轮，简报 P0-1）。
        //    取这一行里第一处 YYYY-MM-DD——登记行的写法是「第一百九十七轮 · 2026-11-11 核对」，
        //    日期在前、结论在后，第一处就是核对日期。
        var dateText = Regex.Match(line!, @"20\d{2}-\d{2}-\d{2}").Value;
        Assert.True(DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var checkedOn),
            $"「DoD 豁免登记」里的核对日期「{dateText}」不是合法的 YYYY-MM-DD。");
        var age = (DateTime.UtcNow.Date - checkedOn.Date).Days;
        Assert.True(age >= 0,
            $"「DoD 豁免登记」的核对日期是 {dateText}，比今天（{DateTime.UtcNow:yyyy-MM-dd}）还晚 {(-age)} 天："
            + "核对日期只能写已经发生的那一天，写未来等于这条结论永远不过期。");
        Assert.True(age <= 90,
            $"「DoD 豁免登记」的核对日期是 {dateText}，距今 {age} 天（上限 90 天）："
            + "INDEX.md 可能已经变了（新增 ApifoxHelper 条目、或把它列进豁免名单），而本仓不会有任何红灯。"
            + "请重核 /opt/agent-scheduler/docs/product-dod/INDEX.md，把这一行的日期与结论改成这次核到的。");
    }

    /// <summary>
    /// 登记里的 INDEX 版本日期必须等于仓内记录的最新 INDEX 版本（第二百零二轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百九十九轮给「DoD 豁免登记」加了 90 天时效软判，判的是
    /// 「这条结论是最近核过的」——但它<b>不判核的是哪一版 INDEX</b>。INDEX.md 若在两次核对之间
    /// 新增了 ApifoxHelper 条目（或把它列进豁免名单），本仓只要在 90 天内重核一次、把日期改新，
    /// 结论照旧写「无 DoD 文件可加载」，门全绿——<b>版本漂了，登记没漂</b>。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 登记行里写着 <c>INDEX 版本 YYYY-MM-DD</c>；② 那个版本日期等于本文件里
    /// <c>LatestIndexVersion</c> 这个常量——它是<b>仓内记录的最新 INDEX 版本</b>，INDEX 更新后要跟着改；
    /// ③ 核对日期不早于版本日期（核一个还没发布的版本是自相矛盾）。
    /// </para>
    /// <para>
    /// <b>为什么两处都要动</b>：只改 NOTES 不改常量 → ② 红；只改常量不改 NOTES → ② 红。
    /// 于是「INDEX 更新了」这件事必须同时落到登记与记录上，<b>静默过期变成必须有人看一眼</b>。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：调度仓里那份 INDEX.md 现在到底写了什么（跨主机、只读、本仓不该依赖它）。
    /// 本门判的是「本仓记的版本与登记一致」，不是「版本永远最新」——最新与否只有重核的人知道。
    /// </para>
    /// </summary>
    [Fact]
    public void DodExemption_IndexVersionIsCurrent()
    {
        // 仓内记录的最新 INDEX 版本。INDEX.md 更新后改这里，同时改 docs/NOTES.md 那一行——
        // 两处必须一起动，只动一处即红（这正是本门存在的理由）。
        const string LatestIndexVersion = "2026-10-31";

        var line = SectionLines()
            .FirstOrDefault(l => l.Contains("DoD 豁免登记", StringComparison.Ordinal));
        Assert.True(line is not null,
            "docs/NOTES.md「本仓验收口径」一节缺「DoD 豁免登记」一行：版本时效门没有可判的对象。");

        // ① 登记行里写着 INDEX 版本日期。
        var version = Regex.Match(line!, @"INDEX 版本\s*(?<v>20\d{2}-\d{2}-\d{2})");
        Assert.True(version.Success,
            "「DoD 豁免登记」没写「INDEX 版本 YYYY-MM-DD」：只写核对日期，判不出核的是哪一版 INDEX——"
            + "INDEX 更新后本仓不会有任何红灯。");

        // ② 版本日期 == 仓内记录的最新 INDEX 版本。
        Assert.Equal(LatestIndexVersion, version.Groups["v"].Value);

        // ③ 核对日期不早于版本日期：核一个还没发布的版本是自相矛盾。
        var checkedText = Regex.Match(line!, @"20\d{2}-\d{2}-\d{2}").Value;
        Assert.True(DateTime.TryParseExact(checkedText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var checkedOn),
            $"「DoD 豁免登记」里的核对日期「{checkedText}」不是合法的 YYYY-MM-DD。");
        Assert.True(DateTime.TryParseExact(version.Groups["v"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var versionOn),
            $"「DoD 豁免登记」里的 INDEX 版本日期「{version.Groups["v"].Value}」不是合法的 YYYY-MM-DD。");
        Assert.True(checkedOn >= versionOn,
            $"核对日期 {checkedText} 早于 INDEX 版本日期 {version.Groups["v"].Value}："
            + "核一个还没发布的版本是自相矛盾，两处必有一处写错了。");
    }

    [Fact]
    public void Checklist_HasEveryGateAndEverySharedInvariant()
    {
        var rows = Rows();
        var gates = rows.Count(r => Regex.IsMatch(r.Item, @"^G-D\d"));
        var shared = rows.Count(r => Regex.IsMatch(r.Item, @"^SHARED-\d"));
        // 方案 §2.2 的门槛表有 G-D1…G-D6，§8 的红线有 9 条：少一行就是口径漏了一条，而不是「那条不用列」。
        Assert.True(gates == 6, $"形态门槛列了 {gates} 行，方案 §2.2 的验收门槛表是 G-D1…G-D6 共 6 行。");
        Assert.True(shared == 9, $"SHARED 不变量列了 {shared} 行，方案 §8 的产品红线是 9 条。");
        Assert.True(rows.Count >= 15, $"本节只有 {rows.Count} 行可勾选项（地板 15）。");
    }

    [Fact]
    public void Checklist_EveryItemHasAnEvidenceSourceAndItResolves()
    {
        foreach (var row in Rows())
        {
            var citations = Regex.Matches(row.Evidence, @"`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
            Assert.True(citations.Count > 0, $"「{row.Item}」没有证据出处：这一行只是又一次自述，读的人无法核对。");
            foreach (var citation in citations)
            {
                if (citation.EndsWith(".cs", StringComparison.Ordinal) || citation.EndsWith(".md", StringComparison.Ordinal))
                {
                    Assert.True(File.Exists(Path.Combine(Repo.Root, citation)), $"「{row.Item}」引用的文件不存在：{citation}。");
                    continue;
                }
                var parts = citation.Split('.');
                Assert.True(parts.Length == 2, $"「{row.Item}」的引用「{citation}」既不是仓库里的文件路径，也不是「类名.断言名」。");
                var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                Assert.True(File.Exists(testFile), $"「{row.Item}」引用的测试类不存在：{parts[0]}。");
                var text = File.ReadAllText(testFile);
                Assert.True(Regex.IsMatch(text, @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                    $"「{row.Item}」引用的断言不存在：{parts[0]}.{parts[1]}。");
            }
            // 勾成达成的，必须至少被一条断言钉住——只指到一个源文件不算「达成」，那只能证明代码在。
            if (row.Check == "[x]")
            {
                Assert.True(citations.Any(c => !c.EndsWith(".cs", StringComparison.Ordinal) && !c.EndsWith(".md", StringComparison.Ordinal)),
                    $"「{row.Item}」勾成达成却没有断言引用：达成要有可复跑的断言，没有就留空框。");
            }
        }
    }

    [Fact]
    public void Checklist_RealMachineGate_IsStillUnmetAndSaysSo()
    {
        // 真机断链每轮重述（简报 P0-3）：这一条不是措辞检查——勾上 [x] 就是谎报，留 [ ] 但不写「未达成」是自相矛盾。
        var row = Rows().Single(r => r.Item.StartsWith("G-D5", StringComparison.Ordinal));
        Assert.Equal("[ ]", row.Check);
        Assert.Contains("未达成", row.Verdict);
        Assert.Contains("真机", row.Verdict);
        // GUI 证据一列必须点名模拟器，不许写成实测（WordingHonestyTests 同一纪律，这里钉的是这一节）。
        Assert.True(row.Gui.Contains("wine", StringComparison.OrdinalIgnoreCase),
            $"G-D5 的 GUI 证据一列没点名模拟器：{row.Gui}。");
    }

    [Fact]
    public void Checklist_EveryRowNamesTheGuiEvidenceOrSaysThereIsNone()
    {
        foreach (var row in Rows())
        {
            Assert.True(row.Gui.Length > 0, $"「{row.Item}」的 GUI 证据一列是空的：没量过就写「不经界面」，不许留白。");
            var measured = Regex.IsMatch(row.Gui, @"第[一二三四五六七八九十百千零〇\d]+轮");
            Assert.True(measured || row.Gui.StartsWith("不经界面", StringComparison.Ordinal),
                $"「{row.Item}」的 GUI 证据既没写轮次也没写「不经界面」：{row.Gui}。");
        }
    }

    /// <summary>
    /// G-D3 的「GUI 证据」格必须收敛成单一句式：以「最近一次真跑：」开头（第一百八十轮，简报 P1-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百七十九轮把「第一百七十九轮在当前源码上复跑核对」补进这一格时，
    /// 没有删掉原来那句「第一百七十七轮三档复跑含本段」——于是同一格里并列两句话，
    /// <b>读者要自己判断哪个是权威读数</b>。这与 <c>HtmlWrapTests.RegisteredGap_NamesTheLastRealBrowserRun</c>
    /// 的口径不一致：那一族要求「挂账必须写明最近一次真跑是哪一轮」，写法是单一句式。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 这一格以「最近一次真跑：」开头（权威读数只有一个入口）；
    /// ② 旧读数不许被顺手删掉——第一百七十七轮的 180 通过 / 0 失败 ×3 与 <c>RunDialogStatusSweep</c>
    /// 仍在同一格里（收敛是「重排成一句」，不是「删掉出处」）。<b>不判</b>轮次数字本身对不对
    /// （那是 <c>NativeDialogSmokeTests.Notes_RecordsThatTheThreeTierRerunIncludesTheDialogStatusSweep</c> 的事）。
    /// </para>
    /// </summary>
    [Fact]
    public void Checklist_StatusGateGuiEvidence_NamesTheLastRealRunInOneSentence()
    {
        var row = Rows().Single(r => r.Item.StartsWith("G-D3", StringComparison.Ordinal));

        Assert.True(row.Gui.StartsWith("最近一次真跑：", StringComparison.Ordinal),
            $"G-D3 的 GUI 证据格没有以「最近一次真跑：」开头：{row.Gui}。"
            + "同一格并列两句话时，读者要自己判断哪个是权威读数——收敛成单一句式，旧读数放进「读数出处」。");

        // 收敛不是删掉出处：第一百七十七轮那次的读数与段名必须还在同一格里。
        Assert.Contains("第一百七十七轮", row.Gui, StringComparison.Ordinal);
        Assert.Contains("RunDialogStatusSweep", row.Gui, StringComparison.Ordinal);
    }

    /// <summary>
    /// 格式错的行必须判红，不许被解析器静默跳过（第一百六十七轮，简报 P0-2）。
    /// <para>
    /// <b>存在的理由</b>：本节的其余断言都从 <see cref="Rows"/> 取数，而它按「五列且首格是勾选框」筛行。
    /// 既有那 15 行列数写错时，<c>Checklist_HasEveryGateAndEverySharedInvariant</c> 会数到 5 或 8 而变红；
    /// 漏的是<b>新增行</b>这一支：有人往本表加一条自己的口径（首格是勾选框、少写一列），
    /// 这行既不被计数、也不被查证据出处与 GUI 证据——<b>整表照样绿，而多出来的那一格没有任何断言看过</b>。
    /// 这条负向门把「首格是勾选框但列数不是 5」的行本身判红。
    /// </para>
    /// </summary>
    [Fact]
    public void Checklist_MisshapedRow_IsRedNotSilentlySkipped()
    {
        var misshaped = CellRows()
            .Where(c => (c[0] == "[x]" || c[0] == "[ ]") && c.Count != 5)
            .ToList();
        Assert.True(
            misshaped.Count == 0,
            $"这些行以勾选框开头却不是 5 列（应为 勾选 / 门槛 / 当前结论 / 证据出处 / GUI 证据），"
            + $"它们不会进 Rows()，等于从口径表里悄悄消失：{string.Join(" ／ ", misshaped.Select(c => $"{c.Count} 列「{c[1]}」"))}。");
    }

    // ---------- G-D5 真机复核清单（第一百六十八轮，简报 P0-1） ----------

    private const string RealMachineHeading = "## G-D5 真机复核清单（可勾选）";

    /// <summary>真机动作单的一行：勾选 / 动作 / 量什么 / 归档到哪里 / 判定标准。</summary>
    private readonly record struct ActionRow(string Check, string Action, string Measure, string Archive, string Standard);

    /// <summary>真机证据的归档根（截图落点）。目录里一张 <c>*.png</c> 都没有时，本表的每一条都必须是空框。</summary>
    private static string RealMachineEvidenceDir => Path.Combine(Repo.Root, "docs", "evidence", "real-machine");

    private static bool RealMachineEvidenceArchived() =>
        Directory.Exists(RealMachineEvidenceDir) && Directory.GetFiles(RealMachineEvidenceDir, "*.png", SearchOption.AllDirectories).Length > 0;

    /// <summary>本节全部行（标题行到下一个 <c>## </c> 之前）。本节必须排在验收口径之后——口径表在前，动作单才有「哪几条缺口由它关闭」的落点。</summary>
    private static string[] RealMachineSectionLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == RealMachineHeading);
        Assert.True(start >= 0, $"docs/NOTES.md 缺「{RealMachineHeading}」一节（真机到位后第一天的动作单）。");
        var acceptance = Array.FindIndex(lines, l => l.TrimEnd() == Heading);
        Assert.True(acceptance >= 0 && start > acceptance,
            $"「{RealMachineHeading}」必须排在「{Heading}」之后：那张表说清哪几条没达成，这一节才是关闭它们的路径。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<ActionRow> RealMachineRows()
    {
        var rows = new List<ActionRow>();
        foreach (var line in RealMachineSectionLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells[0] != "[x]" && cells[0] != "[ ]") continue;
            // 与验收表同一族负向门：格式错的勾选行不许被解析器静默跳过，否则那一格没有任何断言看过。
            Assert.True(cells.Count == 5,
                $"真机清单有一行是勾选框却不是 5 列（实际 {cells.Count} 列，应为 勾选 / 动作 / 量什么 / 归档到哪里 / 判定标准）：{cells[1]}");
            rows.Add(new ActionRow(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return rows;
    }

    /// <summary>运行期数据文件名的后缀：它们写在这节里是「要归档/要读的东西」，不是仓内实物，也不许冒充「类名.断言名」。</summary>
    private static readonly string[] DataFileExtensions = ["json", "jsonl", "txt", "log", "png", "html", "csv"];

    /// <summary>反引号里认定为「实物」的三类引用：仓内文件、测试断言、冒烟段名。命中即核对存在。</summary>
    private static int ResolvableCitations(string text, List<string> unresolved)
    {
        var resolved = 0;
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        foreach (var citation in Regex.Matches(text, @"`([^`]+)`").Select(m => m.Groups[1].Value))
        {
            if (citation.EndsWith(".cs", StringComparison.Ordinal) || citation.EndsWith(".md", StringComparison.Ordinal)
                || citation.EndsWith(".sh", StringComparison.Ordinal) || citation.EndsWith(".ps1", StringComparison.Ordinal))
            {
                if (File.Exists(Path.Combine(Repo.Root, citation.Replace('\\', '/')))) resolved++;
                else unresolved.Add(citation + "（文件不存在）");
                continue;
            }

            if (Regex.IsMatch(citation, @"^[A-Za-z0-9_]+\.[A-Za-z0-9_]+$"))
            {
                var parts = citation.Split('.');
                if (DataFileExtensions.Contains(parts[1], StringComparer.OrdinalIgnoreCase)) continue;
                var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                if (!File.Exists(testFile))
                {
                    // 不是测试类，可能是界面层真有的符号（第一百八十九轮：DPI 那一族，如 `DpiScale.ScaleFactor`）。
                    // 动作单第 3、4 条要量「因子」，读数位置就是它——只认测试类的话，这一格只能写成一句没有落点的
                    // 「看 DPI 因子」，而「从哪读」正是真机当天最容易现场编的一格。
                    var nativeFile = Path.Combine(Repo.Root, "Desktop", "Native", parts[0] + ".cs");
                    if (File.Exists(nativeFile)
                        && Regex.IsMatch(File.ReadAllText(nativeFile), @"\b" + Regex.Escape(parts[1]) + @"\s*\("))
                    {
                        resolved++;
                        continue;
                    }

                    unresolved.Add(citation + "（测试类不存在）");
                    continue;
                }

                if (Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]")) resolved++;
                else unresolved.Add(citation + "（断言不存在）");
                continue;
            }

            if (Regex.IsMatch(citation, @"^Run[A-Za-z0-9]+$"))
            {
                if (smoke.Contains($"private static void {citation}(", StringComparison.Ordinal)) resolved++;
                else unresolved.Add(citation + "（冒烟器里没有这一段）");
            }

            // 其余是运行期数据文件名（task.json 之类）与归档目标目录：这一节写的正是「还没归档」的东西，不核它存在。
        }

        return resolved;
    }

    /// <summary>
    /// 真机复核清单存在、排在验收口径之后、每一条都引用实物、且覆盖到所有只能由真机关闭的项
    /// （第一百六十八轮，简报 P0-1）。
    /// <para>
    /// <b>为什么是 P0</b>：G-D5 与 SHARED-3/4/7/8 是这张验收表上唯一剩下的硬缺口，而它们缺的只有同一件事——
    /// 机器与专用测试项目。真机到手那天如果还要现场设计「跑什么、量什么、东西放哪」，这一天就废了。
    /// 这一节把动作单钉成机械可判：<b>编一个不存在的断言名即红，漏掉一条只能由真机关闭的项也即红</b>。
    /// </para>
    /// <para>
    /// <b>它不许被悄悄勾上</b>：归档根里没有截图时，本表每一条都必须是空框——与
    /// <see cref="Checklist_RealMachineGate_IsStillUnmetAndSaysSo"/> 同一口径，钉的是结构不是措辞。
    /// </para>
    /// </summary>
    [Fact]
    public void RealMachineChecklist_ExistsAndCoversEveryGate()
    {
        var rows = RealMachineRows();
        Assert.True(rows.Count >= 6, $"真机清单只有 {rows.Count} 行动作（地板 6：首程四站 / 三档截图 / 混合缩放 / 运行中改缩放 / 字体度量 / 写入端到端）。");
        Assert.True(rows.All(r => r.Check == "[ ]") || RealMachineEvidenceArchived(),
            $"归档根 {RealMachineEvidenceDir} 里一张截图都没有，这一节却有勾上的行——wine 的证据不能替真机打勾。");

        var section = string.Join('\n', RealMachineSectionLines());
        var unresolved = new List<string>();
        ResolvableCitations(section, unresolved);
        Assert.True(unresolved.Count == 0, "这一节引用了不存在的东西：" + string.Join(" ／ ", unresolved));

        foreach (var row in rows)
        {
            var cellUnresolved = new List<string>();
            var cited = ResolvableCitations(string.Join('\n', row.Action, row.Measure, row.Archive, row.Standard), cellUnresolved);
            Assert.True(cellUnresolved.Count == 0, $"第 {row.Action[..Math.Min(18, row.Action.Length)]}… 这一行引用了不存在的东西：" + string.Join(" ／ ", cellUnresolved));
            Assert.True(cited >= 1, $"这一行没有引用任何实物（文件 / 断言名 / 冒烟段名），读的人无从核对：{row.Action[..Math.Min(40, row.Action.Length)]}…");
            Assert.True(row.Measure.Length >= 12 && row.Archive.Length >= 8 && row.Standard.Length >= 12,
                $"这一行的「量什么 / 归档到哪里 / 判定标准」有空格：{row.Action[..Math.Min(40, row.Action.Length)]}…");
        }

        // 覆盖：只能由真机关闭的项，一条都不许从这张动作单里漏掉。
        foreach (var required in new[]
                 {
                     "G-D5", "SHARED-3", "SHARED-4", "SHARED-7", "SHARED-8",
                     "100%", "125%", "200%",
                     "新建配置", "补充说明", "模型服务",
                     "多显示器混合缩放", "运行中改系统缩放", "字体", "部分失败",
                     "docs/evidence/real-machine/",
                 })
        {
            Assert.Contains(required, section, StringComparison.Ordinal);
        }

        // README 的「已知限制」要点名这一节：缺口公开声明的落点在那儿，动作单藏在 1800 行日志里等于没有。
        Assert.Contains("G-D5 真机复核清单（可勾选）", Repo.Source("README.md"), StringComparison.Ordinal);
    }

    // ---------- G-D6 退场清单（第一百八十七轮，简报 P0-3） ----------

    private const string ExitChecklistHeading = "## G-D6 退场清单（可勾选）";

    /// <summary>退场清单的一行：勾选 / 退场项 / 删改哪一处 / 验收标准。</summary>
    private readonly record struct ExitRow(string Check, string Item, string Where, string Standard);

    private static string[] ExitChecklistLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == ExitChecklistHeading);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺「{ExitChecklistHeading}」一节：G-D6 的退场此前只是一句话，"
            + "一句话在任何一轮都能被读成「还剩一件事」，而实际是七件、分散在五个文件里。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<ExitRow> ExitRows()
    {
        var rows = new List<ExitRow>();
        foreach (var line in ExitChecklistLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells[0] != "[x]" && cells[0] != "[ ]") continue;
            Assert.True(cells.Count == 4,
                $"退场清单有一行是勾选框却不是 4 列（实际 {cells.Count} 列，应为 勾选 / 退场项 / 删改哪一处 / 验收标准）：{cells[1]}。"
                + "格式错的行不许被解析器静默跳过。");
            rows.Add(new ExitRow(cells[0], cells[1], cells[2], cells[3]));
        }

        return rows;
    }

    /// <summary>反引号里认定为「仓内实物」的引用：文件 / 目录、测试断言、代码符号。</summary>
    private static readonly string[] SourceExtensions =
        [".cs", ".md", ".csproj", ".html", ".js", ".css", ".json", ".jsonl", ".txt", ".ps1", ".sh"];

    /// <summary>
    /// G-D6 退场清单：七条各一行、每条都点到仓内真实存在的文件 / 断言 / 代码符号
    /// （第一百八十七轮，简报 P0-3）。
    /// <para>
    /// <b>补的是哪个缺口</b>：G-D6 未达成，且按决定排在 G-D5 之后（真机 0 台，到期日 2026-11-20）。
    /// 本轮<b>不执行退场</b>，但退场清单此前只有一句话——「删 `--web` 参数解析、删页面层引用、删过渡壳分支、
    /// 删那条断言、改 README」这种写法在任何一轮都能被读成「还剩一件事」，而实际是七件、分散在五个文件里，
    /// 且其中一件（路由去留）是<b>决定项</b>：删了页面却留着会话令牌注入，等于把凭据接口留在没有调用方的服务上。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 七条各一行（编号 1–7 各一次）；② 每条都点到<b>仓内实物</b>——文件路径必须存在、
    /// <c>类名.断言名</c> 必须在测试工程里真的有这一条、代码符号（如 <c>case "--web":</c>、<c>MainForm</c>）
    /// 必须真的出现在<b>同一行点到的文件</b>里（编一个不存在的类名即红，把一个符号挪到别的文件也即红）；
    /// ③ 决定项（路由去留）被点名；④ 全部仍是 <c>[ ]</c>——退场没执行，勾上就是谎报。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判退场有没有执行</b>，也不判那天选哪一支路由方案（要人答）。它只保证
    /// 「退场那天照着删的每一处，都是仓里真实存在的那一处」——清单里编一个不存在的符号，那天就删不动。
    /// </para>
    /// </summary>
    [Fact]
    public void ExitChecklist_EveryItemPointsToRealSymbols()
    {
        var rows = ExitRows();
        Assert.True(rows.Count >= 6, $"退场清单只有 {rows.Count} 行（地板 6：参数解析 / 壳分支 / 过渡壳窗口 / 路由去留 / 页面层 / 测试断言 / 文档口径）。");

        // ④ 退场没执行：一条都不许勾。
        Assert.True(rows.All(r => r.Check == "[ ]"),
            "退场清单里有勾成 [x] 的行，而 G-D6 在验收表上仍是 [ ]：退场没执行，勾上就是谎报。");

        foreach (var row in rows)
        {
            var citations = Regex.Matches(row.Where, @"`([^`]+)`").Select(m => m.Groups[1].Value.Trim()).ToList();
            Assert.True(citations.Count > 0, $"「{row.Item}」没点到任何仓内实物：退场那天照着删什么？");

            var citedFiles = new List<string>();
            var symbols = 0;
            foreach (var citation in citations)
            {
                // 文件 / 目录：必须真的在仓里。
                // 判「像不像路径」而不是「有没有斜杠」：`app.MapGet("/api/session"` 这类代码符号也带斜杠，
                // 把它当路径核会把一条正确的符号引用判成「文件不存在」——那正是「门红得没道理」的老毛病。
                var looksLikePath = !citation.Contains('(') && !citation.Contains('"')
                    && !citation.Contains('=') && !citation.Contains(';') && !citation.Contains(' ');
                if (looksLikePath
                    && (SourceExtensions.Any(e => citation.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                        || citation.Contains('/', StringComparison.Ordinal)))
                {
                    var full = Path.Combine(Repo.Root, citation.Replace('\\', '/'));
                    Assert.True(File.Exists(full) || Directory.Exists(full),
                        $"「{row.Item}」点到的路径不存在：{citation}。退场清单里编一个不存在的文件，那天就删不动。");
                    if (File.Exists(full)) citedFiles.Add(full);
                    continue;
                }

                // 测试断言：类名.方法名，方法必须真的在测试工程里。
                if (Regex.IsMatch(citation, @"^[A-Za-z0-9_]+\.[A-Za-z0-9_]+$"))
                {
                    var parts = citation.Split('.');
                    var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                    Assert.True(File.Exists(testFile), $"「{row.Item}」引用的测试类不存在：{parts[0]}。");
                    Assert.True(Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                        $"「{row.Item}」引用的断言不存在：{citation}。");
                    citedFiles.Add(testFile);
                    symbols++;
                    continue;
                }

                // 纯中文描述（例如「过渡壳窗口功能」这种节名）：不核，它不是符号。
                if (!citation.Any(char.IsAsciiLetter)) continue;

                // 代码符号：必须真的出现在同一行点到的文件里。
                var haystack = string.Join('\n', citedFiles.Select(File.ReadAllText));
                Assert.True(haystack.Contains(citation, StringComparison.Ordinal),
                    $"「{row.Item}」点到的代码符号「{citation}」在这一行点到的文件里找不到："
                    + "编一个不存在的符号即红，把一个符号挪到别的文件也即红——退场那天照着删的必须是真实存在的那一处。");
                symbols++;
            }

            Assert.True(symbols >= 1,
                $"「{row.Item}」只点了文件、没点到任何断言或代码符号：退场那天要删的是哪一行，读的人仍要自己找。");
        }

        // ③ 决定项必须被点名：路由去留是唯一需要人先答再动手的一条。
        var section = string.Join('\n', ExitChecklistLines());
        Assert.Contains("决定项", section, StringComparison.Ordinal);
        Assert.Contains("路由", section, StringComparison.Ordinal);

        // 验收表那一行必须指过来，且仍是空框（与 G-D5 同一口径：勾选框只由证据关闭）。
        var gate = Rows().Single(r => r.Item.StartsWith("G-D6", StringComparison.Ordinal));
        Assert.Equal("[ ]", gate.Check);
        Assert.Contains("G-D6 退场清单（可勾选）", gate.Verdict, StringComparison.Ordinal);
        Assert.Contains($"{nameof(AcceptanceChecklistTests)}.{nameof(ExitChecklist_EveryItemPointsToRealSymbols)}",
            gate.Evidence, StringComparison.Ordinal);
    }

    // ---------- 第 0 条「机器到手前的四项准备」的执行状态（第一百九十八轮，简报 P0-1） ----------

    /// <summary>「第 0 条」那一段的标记（落在 G-D5 动作单那一节之内，排在七条动作之前）。</summary>
    private const string BeforeArrivalMarker = "第 0 条（机器到手前，本仓可做";

    /// <summary>第 0 条的一项：勾选 / 编号 / 正文（含末尾那句「状态：」）。</summary>
    private readonly record struct BeforeArrivalItem(string Check, string Marker, string Text);

    /// <summary>
    /// 第 0 条的四项（<c>&gt; - [x] ① …</c> 形状的列表行），从标记行往下取到动作单表格表头之前。
    /// <para>
    /// 与 <c>RealMachineEvidenceTests.BeforeArrivalLines</c> 判的是同一段材料，但取的是<b>不同的东西</b>：
    /// 那一条判「四项齐、每项有勾选框」，这一条判「每项末尾那句执行状态」。两处各写一份解析是有意的——
    /// 那一条在 <c>RealMachineEvidenceTests</c> 里是私有的，跨类复用要把它改成 internal 并让两个文件互相依赖；
    /// 而这两条门判的判据不同（形状 vs 状态），改一处不该牵动另一处。
    /// </para>
    /// </summary>
    private static List<BeforeArrivalItem> BeforeArrivalItems()
    {
        var lines = RealMachineSectionLines();
        var start = Array.FindIndex(lines, l => l.Contains(BeforeArrivalMarker, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{BeforeArrivalMarker}…）」这一段：机器到手前本仓能做的最后准备一条都没写。");

        var items = new List<BeforeArrivalItem>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("|", StringComparison.Ordinal)) break; // 动作单表格开始
            if (!Regex.IsMatch(line, @"^>\s*-\s")) continue;
            var m = Regex.Match(line, @"^>\s*-\s*\[(?<box>[ x])\]\s*(?<marker>[①②③④])\s*(?<text>.+)$");
            Assert.True(m.Success,
                $"「第 0 条」有一项不是「> - [ ] ① …」的形状：{line.Trim()}。写成普通段落的那一项当天没人会去勾它。");
            items.Add(new BeforeArrivalItem(
                m.Groups["box"].Value == "x" ? "[x]" : "[ ]",
                m.Groups["marker"].Value,
                m.Groups["text"].Value.Trim()));
        }

        return items;
    }

    /// <summary>反引号里「像仓内路径」的引用：带斜杠或以已知后缀结尾。命令名（<c>dotnet build</c>）不算。</summary>
    private static bool LooksLikeRepoPath(string token) =>
        token.Contains('/', StringComparison.Ordinal)
        || SourceExtensions.Any(e => token.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 第 0 条四项各自带一句执行状态，且「做完的」与「没做完的」分得开（第一百九十八轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百九十五轮把「机器到手前的四项准备」写成了四项可勾选，但<b>四项全是空框</b>——
    /// 于是「机器到手前能做的都做完了吗」这个问题在仓里没有答案：读者看到四个空框，分不清是「还没做」
    /// 还是「做了但没人勾」。到期日 9 天内，这个分不清正是最贵的：机器一到手，人得先花半天判断哪几项其实已经就绪。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 四项齐且编号 ① ② ③ ④ 各一次；② 每项末尾都有一句「状态：」，且那句话至少 12 字
    /// （一句「已做」不算状态，得说清做了什么、什么时候做的）；③ 勾成 <c>[x]</c> 的项，状态里必须点到
    /// <b>仓里真能跑的东西</b>——命令（<c>bash .winecheck/demo.sh</c>）、用例工程（<c>…csproj</c>）、
    /// 文档（<c>…md</c>）或断言名，且这些引用<b>必须 resolve</b>（编一个不存在的路径即红）；
    /// ④ 留 <c>[ ]</c> 的项，状态里必须同时写「未做」与「缺」（缺什么、为什么这一项在机器到手前做不完）；
    /// ⑤ ③ 那一项备好的命令与 <c>docs/evidence/real-machine/README.md</c> 产出物清单的九个目录名<b>逐字一致</b>
    /// （少一个、多一个都即红——两套名字等于两套判据）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判这四项真的做过</b>（本机没有 Windows 图形子系统，做没做只有人知道），
    /// 也<b>不判真机跑没跑</b>（G-D5 仍是 <c>[ ]</c>）。它只保证「做完的与没做完的分得开，且做完的那几项指得出东西」。
    /// </para>
    /// </summary>
    [Fact]
    public void BeforeMachineArrives_EveryItemCarriesItsExecutionStatus()
    {
        var items = BeforeArrivalItems();

        // ① 四项齐、编号各一次。
        Assert.Equal(["①", "②", "③", "④"], items.Select(i => i.Marker));

        foreach (var item in items)
        {
            // ② 每项末尾一句「状态：」，且不是一句口号。
            var at = item.Text.IndexOf("状态：", StringComparison.Ordinal);
            Assert.True(at >= 0,
                $"「第 0 条」{item.Marker} 没写「状态：」：四个空框分不清「还没做」与「做了没人勾」，"
                + "机器到手前最贵的正是这个分不清。");
            var status = item.Text[(at + "状态：".Length)..].Trim();
            Assert.True(status.Length >= 12,
                $"「第 0 条」{item.Marker} 的状态只写了 {status.Length} 字：「已做」两个字不算状态，得说清做了什么、什么时候做的。");

            if (item.Check == "[x]")
            {
                // ③ 做完的项必须指到仓里真能跑的东西，且引用要 resolve。
                var citations = Regex.Matches(status, @"`([^`]+)`").Select(m => m.Groups[1].Value).ToList();
                Assert.True(citations.Count > 0,
                    $"「第 0 条」{item.Marker} 勾成 [x] 却没点到任何仓内实物：做完的项要指得出东西（命令 / 用例工程 / 文档 / 断言名）。");

                var resolved = 0;
                foreach (var citation in citations)
                {
                    // 命令带参数时取最后一个词：`bash .winecheck/demo.sh` 的实物是那个脚本，不是 `bash`。
                    var token = citation.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? citation;
                    if (LooksLikeRepoPath(token))
                    {
                        var full = Path.Combine(Repo.Root, token.Replace('\\', '/'));
                        Assert.True(File.Exists(full) || Directory.Exists(full),
                            $"「第 0 条」{item.Marker} 的状态里点到的路径不存在：{token}。做完的项指一个不存在的路径，等于又一次自述。");
                        resolved++;
                        continue;
                    }

                    if (Regex.IsMatch(token, @"^[A-Za-z0-9_]+\.[A-Za-z0-9_]+$"))
                    {
                        var parts = token.Split('.');
                        var testFile = Path.Combine(Repo.Root, "ApifoxHelper.Tests", parts[0] + ".cs");
                        Assert.True(File.Exists(testFile), $"「第 0 条」{item.Marker} 引用的测试类不存在：{parts[0]}。");
                        Assert.True(Regex.IsMatch(File.ReadAllText(testFile), @"\bvoid\s+" + Regex.Escape(parts[1]) + @"\s*[(:]"),
                            $"「第 0 条」{item.Marker} 引用的断言不存在：{token}。");
                        resolved++;
                    }
                }

                Assert.True(resolved > 0,
                    $"「第 0 条」{item.Marker} 勾成 [x]，但状态里没有一条引用落到仓内实物：{status}");
            }
            else
            {
                // ④ 没做完的项必须写清「未做」与缺在哪一句。
                Assert.Contains("未做", status, StringComparison.Ordinal);
                Assert.Contains("缺", status, StringComparison.Ordinal);
            }
        }

        // ⑤ ③ 备好的那条命令与 README 的九个目录名逐字一致。
        var readme = Repo.Source("docs", "evidence", "real-machine", "README.md");
        var listed = readme.Split('\n')
            .Where(l => l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            .SelectMany(l => Regex.Matches(l, @"docs/evidence/real-machine/([^/`\s]+)/").Select(m => m.Groups[1].Value))
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.True(listed.Count == 9,
            $"docs/evidence/real-machine/README.md 的产出物清单点了 {listed.Count} 个归档目录（应为 9 个）：{string.Join("、", listed)}。");

        // 命令的出处是脚本，不是 README 里手抄的一条 mkdir（第二百零二轮，简报 P0-2）：
        // 目录名只有一份出处，README 只指过来——两处各写一份，改了一处另一处不会红。
        var script = Repo.Source(".winecheck", "prepare-real-machine.sh");
        var mkdir = script.Split('\n').FirstOrDefault(l => l.Contains("mkdir -p docs/evidence/real-machine/", StringComparison.Ordinal));
        Assert.True(mkdir is not null,
            ".winecheck/prepare-real-machine.sh 里没有那条「一条命令建好九个目录」的 mkdir："
            + "第 0 条 ③ 的证据就是这个脚本，命令不在，那一项就只剩一句「已做」。");
        var brace = Regex.Match(mkdir!, @"mkdir -p docs/evidence/real-machine/\{([^}]+)\}");
        Assert.True(brace.Success,
            $"脚本里那条 mkdir 不是「mkdir -p docs/evidence/real-machine/{{…}}」的形状：{mkdir!.Trim()}");
        var made = brace.Groups[1].Value.Split(',').Select(s => s.Trim()).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(listed, made);

        // ⑥ 脚本把 ①②③ 各收成一条可执行的命令，④ 打印动作单（第二百零二轮，简报 P0-2）。
        //    只写「已收成脚本」而脚本里没有那三条命令，等于把四项准备换成一句自述。
        foreach (var marker in new[] { "①", "②", "③", "④" })
        {
            Assert.Contains($"-- {marker} ", script, StringComparison.Ordinal);
        }
        Assert.Contains(".winecheck/demo.sh", script, StringComparison.Ordinal);   // ① 三档冒烟
        Assert.Contains("dotnet build", script, StringComparison.Ordinal);         // ② 构建
        Assert.Contains("dotnet test", script, StringComparison.Ordinal);          // ② 用例
        Assert.Contains("mkdir -p docs/evidence/real-machine/{", script, StringComparison.Ordinal); // ③ 九个目录
        // ④ 的动作单从 NOTES 读，不在脚本里抄一份（抄的会漂）。
        Assert.Contains("\"$NOTES\"", script, StringComparison.Ordinal);
        Assert.Contains("G-D5 真机复核清单", script, StringComparison.Ordinal);
    }

    // ---------- 第二百零四轮（简报 P0-3）：G-D5 到期日的时效门 ----------

    /// <summary>
    /// 「到期日」后面紧跟的那个固定日期（<c>到期日 2026-11-20</c> / <c>到期日 **2026-11-20**</c> /
    /// <c>到期日：**2026-11-20**</c> 三种写法都认）。
    /// </summary>
    private static readonly Regex DueDatePattern = new(@"到期日[^\d\n]{0,8}(\d{4}-\d{2}-\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// 会过期的动态倒计时（<c>距今 9 天</c> 这一类）。写死在文档里的倒计时第二天就是错的读数，
    /// 而它不会让任何代码出错——只能靠机械门挡。
    /// </summary>
    private static readonly Regex StaleCountdown = new(@"距今\s*\d+\s*天", RegexOptions.Compiled);

    /// <summary>
    /// G-D5 到期日必须是未来日期，或明确记为「已过期并降级」；且「距今 N 天」这类动态读数不许写死在文档里
    /// （第二百零四轮，简报 P0-3）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>docs/NOTES.md</c> 多处记「到期日 2026-11-20（第一百八十轮记：距今 9 天）」，
    /// 但<b>没有任何门判这个倒计时</b>——「距今 9 天」是写那一刻的读数，第二天就错了，而它读起来像事实。
    /// 到期日这件事本身也没有门：日期被改成过去、或两处各写一个日期，都不会让任何断言变红。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① G-D5 行里写着固定到期日（<c>YYYY-MM-DD</c>），不是「距今 N 天」这类相对读数；
    /// ② <c>docs/NOTES.md</c> 与 <c>README.md</c> 里所有「到期日 + 日期」的写法<b>只许是同一个日期</b>
    /// （两处各写一个日期就是两套说法）；③ 到期日若已过去，G-D5 行必须同时写着「已过期」与「降级」——
    /// 过期了却仍按「未来日期」讲，就是把没到的机器说成会到；④ 两份文件里都不许出现「距今 N 天」。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（真机 0 台，G-D5 仍是 <c>[ ]</c>），也不判到期日选得对不对——
    /// 它只保证「到期日这件事本身不会悄悄过期，也不会在文档里分叉」。
    /// </para>
    /// </summary>
    [Fact]
    public void Gd5DueDate_IsNotStaleOrFuture()
    {
        var row = Rows().Single(r => r.Item.StartsWith("G-D5", StringComparison.Ordinal));

        // ① 到期日必须是固定日期：相对读数（「距今 N 天」）第二天就错，不能当到期日。
        var match = DueDatePattern.Match(row.Verdict);
        Assert.True(match.Success,
            "G-D5 行里没有「到期日 YYYY-MM-DD」这样的固定日期：到期日写成相对读数（「距今 N 天」）第二天就错，"
            + "而它读起来像事实。");
        var dueText = match.Groups[1].Value;
        var due = DateOnly.ParseExact(dueText, "yyyy-MM-dd");

        // ② 两份文件里所有「到期日 + 日期」只许是同一个日期：两处各写一个日期就是两套说法。
        foreach (var file in new[] { "docs/NOTES.md", "README.md" })
        {
            var text = Repo.Source(file.Replace('/', Path.DirectorySeparatorChar));
            var dates = DueDatePattern.Matches(text)
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.True(dates.Count == 1 && dates[0] == dueText,
                $"{file} 里的到期日不是同一个日期（G-D5 行写 {dueText}，这份文件里还有："
                + string.Join(" ／ ", dates) + "）：两处各写一个日期，读的人不知道哪个是权威。");
        }

        // ③ 未来日期，或明确记为「已过期并降级」。
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (due < today)
        {
            Assert.True(row.Verdict.Contains("已过期", StringComparison.Ordinal)
                        && row.Verdict.Contains("降级", StringComparison.Ordinal),
                $"G-D5 的到期日 {dueText} 已经过去（今天 {today:yyyy-MM-dd}），而这一行既没写「已过期」也没写「降级」："
                + "过期了却仍按「未来日期」讲，就是把没到的机器说成会到。");
        }

        // ④ 动态倒计时不许写死在文档里：它是写那一刻的读数，第二天就错。
        foreach (var file in new[] { "docs/NOTES.md", "README.md" })
        {
            var text = Repo.Source(file.Replace('/', Path.DirectorySeparatorChar));
            var stale = StaleCountdown.Matches(text).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
            Assert.True(stale.Count == 0,
                $"{file} 里写死了动态倒计时：" + string.Join(" ／ ", stale)
                + "。这类读数第二天就错，而它读起来像事实——改成固定到期日（到期日 YYYY-MM-DD），时效由本门判。");
        }
    }

    /// <summary>
    /// G-D5 那一行的「到期日判定」段必须写着**改写成哪一句**与**引用哪三份产物**，且那三份是
    /// <c>demo.sh</c> 真会写出的那三份（第二百一十一轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：到期日那天要写的东西此前只有散文——「届时仍不可得按降级答辩逐屏答辩」
    /// （第一百八十轮）与下一节开头那三行（谁决定、回写哪里、哪条断言核）。这三行把<b>动作</b>定死了，
    /// 没定死<b>结论那一格改成什么话、话后要附哪几份产物</b>。真机多轮不可得，最坏的不是没有机器，
    /// 是到期日当天现场发明一句「基本达成」——所以把那句话与那三份路径提前写进行内，并由本门判它们对得上实物。
    /// </para>
    /// <para>
    /// <b>判四件事</b>：① 段在位，且段里那个日期与本行既记的到期日是<b>同一个</b>（两个日期就是两套说法，
    /// 与 <see cref="Gd5DueDate_IsNotStaleOrFuture"/> ② 同一口径）；② 触发条件写在行内（那条可跑的
    /// <c>collect-real-machine.sh</c> 与它的 0 / 9 读数），不是「到时候看情况」；③ 改写后的结论措辞逐字在行内，
    /// 且这一格<b>当天</b>还要留着「已过期」与「降级」两个词——兄弟门 <see cref="Gd5DueDate_IsNotStaleOrFuture"/> ③
    /// 读的就是这两个字，预登记若绕开它，到期日第二天红的会是那一格而不是这段话（那天眼红=现场返工）；
    /// ④ 三份讲稿路径<b>逐个等于</b> <c>demo.sh</c> 自己算出的那三份：目录取自脚本 <c>OUT</c> 的缺省值、
    /// 档位取自 <c>TIERS</c> 的缺省值、文件名取自脚本里那一句 <c>NARRATION=</c>——脚本改了自己产物叫什么，
    /// 本门红，因为行内那三份已经指着不相干的路径了（同一个「不许抄第二份」的纪律见
    /// <c>WineCheckScriptsTests.CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList</c>）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判当天选「按降级路径执行」还是「延后至某日」（那是下一节 ① 由人决定），
    /// 不判 G-D5 算不算过（勾选框只由真机证据关闭，见 <see cref="Checklist_RealMachineGate_IsStillUnmetAndSaysSo"/>），
    /// 也不判那三份讲稿里念的数字对不对（那是 <c>ReadingConsistencyTests</c> 与冒烟段自己的事）。
    /// </para>
    /// </summary>
    [Fact]
    public void Gd5_ExpiryVerdict_IsRecordedWithTheDegradationRunPath()
    {
        var row = Rows().Single(r => r.Item.StartsWith("G-D5", StringComparison.Ordinal));
        var verdict = row.Verdict;

        // ① 段在位 + 段里的日期与本行既记的到期日同一个。
        var start = verdict.IndexOf("到期日判定（", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "G-D5 行没有「到期日判定（YYYY-MM-DD）」这一段：那段收口仍是散文，到期日当天要现场发明一句话。");
        var section = verdict[start..];
        var sectionDate = DueDatePattern.Match(section).Groups[1].Value;
        var rowDate = DueDatePattern.Match(verdict).Groups[1].Value;
        Assert.True(sectionDate.Length > 0 && sectionDate == rowDate,
            $"到期日判定那一段写的日期（{(sectionDate.Length > 0 ? sectionDate : "没写日期")}）与本行既记的到期日"
            + $"（{rowDate}）不是同一个：两处各写一个日期，那天不知道按哪一个收口。");

        // ② 触发条件是那条可跑的核对，不是「到时候看情况」。
        Assert.Contains("collect-real-machine.sh", section, StringComparison.Ordinal);
        Assert.Contains("0 / 9", section, StringComparison.Ordinal);

        // ③ 改写后的措辞逐字在行内；且当天那一格还得留着兄弟门读的两个词。
        Assert.Contains("未达成（已到期 · 环境供给缺口 · 降级答辩已执行）", section, StringComparison.Ordinal);
        // 两个词必须是被「留着…」那一句点名的（本轮变异实测：只写两个 Assert.Contains 抓不住——把
        // 「留着 **「已过期」** 与 **「降级」**」里的「已过期」删掉，同一句后半段的解释还会再提它一次，
        // 两条 Contains 照样绿。判顺序与距离才判的是「这句话在不在」，而不是「这两个字出现过没有」。
        Assert.True(Regex.IsMatch(section, @"留着.{0,12}已过期.{0,16}降级"),
            "那段没有把「当天必须同时留着『已过期』与『降级』」这句话写出来（要求：两个词在同一个「留着…」子句里、"
            + "按这个顺序）：兄弟门 Gd5DueDate_IsNotStaleOrFuture 在到期日之后读的就是这两个字，"
            + "这句话悬空，那天红的会是格子，而预登记看起来是齐的。");

        Assert.True(verdict.StartsWith("**未达成（环境供给缺口）**", StringComparison.Ordinal),
            "G-D5 那一格的「当前」结论不再是「未达成（环境供给缺口）」：到期日还没到就把预登记的那句当现况写，"
            + "读的人会以为那天已经过完了。");

        // ④ 三份讲稿路径 = demo.sh 自己算出的那三份（OUT / TIERS / 文件名三处全部现读）。
        var script = Repo.Source(".winecheck", "demo.sh");
        var outDir = Regex.Match(script, @"(?m)^OUT=\$\{OUT:-(?<v>[^}]*)\}$").Groups["v"].Value;
        var tiers = Regex.Match(script, @"(?m)^TIERS=\$\{TIERS:-(?<v>[^}]*)\}$").Groups["v"].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var naming = Regex.Match(script, @"NARRATION=""\$OUT/(?<head>[^""$]*)\$TIER(?<tail>\.[a-z]+)""");
        Assert.True(outDir.Length > 0 && tiers.Length > 0 && naming.Success,
            $"从 .winecheck/demo.sh 现读产物路径失败（OUT={outDir}、TIERS={tiers.Length} 档、"
            + $"文件名模板{(naming.Success ? "读到" : "读不到")}）：脚本的取法变了要先改本门，"
            + "否则这里比对的是三个凭空的字符串。");

        var expected = tiers.Select(t => $"{outDir}/{naming.Groups["head"].Value}{t}{naming.Groups["tail"].Value}")
            .ToList();
        var cited = Regex.Matches(section, @"`(?<p>/[^`]*narration-[^`]*)`").Cast<Match>()
            .Select(m => m.Groups["p"].Value).ToList();
        Assert.True(cited.Count > 0,
            "那段说「引用降级答辩那一次的三档产物」，行内却没有一个反引号包住的 narration 路径：只说引用不给路径。");
        foreach (var path in expected)
        {
            Assert.True(cited.Contains(path, StringComparer.Ordinal),
                $"demo.sh 当前会写出 {path}（OUT 与 TIERS 与文件名三处都从脚本现读），行内那三份里却没有它："
                + "到期日那天照着行内去取产物，会取到一份脚本根本没写出的东西。行内现读到的："
                + string.Join("、", cited) + "。");
        }

        Assert.True(cited.Count == expected.Count,
            $"行内点了 {cited.Count} 份讲稿路径，而 demo.sh 的缺省档位只有 {expected.Count} 档（{string.Join("、", expected)}）："
            + "多出来的那几份要么是自己发明的档位，要么是上一场留在别处的产物，那天没有人替它们背书。");
    }

    /// <summary>
    /// 真机 125% / 200% 档「行高超出预算算不算过」——必须是一段<b>写在 G-D5 行内</b>的预登记，
    /// 而不是第二百一十一轮那种「README 里说不能读成过」（第二百一十二轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百一十一轮在现有环境里量到了分岔变量（换档时重不重启 wine 前缀），
    /// 也量到了后果（重启那一跑的 125% 档 4 行 60px 超 50px 预算；慢一档那一跑同一批行 31/37px 不超），
    /// 三处活文档都写清了「<c>reading.txt</c> 那份三档全绿<b>不能</b>读成行高预算在 125%/200% 也过」——
    /// 但<b>那是描述，不是判定</b>。真机到手大概先撞的就是这两档，而那天没有判定路径就等于 G-D5 当场卡住：
    /// 现场要么把超预算念成「观感问题，先勾上」，要么把整条 G-D5 念成「全都不过」，两种都不诚实。
    /// 这一段提前把三句话定死。
    /// </para>
    /// <para>
    /// <b>简报原文在本仓对不上的那一处（顺手钉住，与第二百一十一轮那句「已到期」同族）</b>：简报给的措辞是
    /// 「见归档 <c>dpi-125/</c>、<c>dpi-200/</c>」，而<b>这两个目录不存在</b>——九个归档目录的名字只有一份出处
    /// （<c>docs/evidence/real-machine/README.md</c> 的产出物清单），那两档在那里叫 <c>125/</c> 与 <c>200/</c>。
    /// 照原文写下去，当天照这段话去建 <c>dpi-125/</c>，<c>collect-real-machine.sh</c> 会因「磁盘上多出一个清单
    /// 没点到的目录」直接退 <c>2</c>：核对坏掉，而读起来像「证据有了」。所以本门判④把<b>档位数字</b>与
    /// <b>目录名</b>钉成同一个，且必须是产出物清单第 2 行声明的那几个（现读 <see cref="RealMachineEvidenceTests.ArtifactRows"/>
    /// 与 <see cref="RealMachineEvidenceTests.DirectoriesIn"/>，不在本文件抄第二份清单）。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 那一段在位，且只取到下一段（<c>到期日判定（</c>）之前——写在进行中的段落里不算；
    /// ② 三句后果齐全（动作单第 2 条判不过／G-D4 那一格的 <c>[x]</c> 不动但补真机读数／这一段不构成任何达成、
    /// 也不改预算）；③ 要比的那个数取<b>报告同一行</b>报出的那一个，算式只许指 <c>ProfileFormLayout.MaxRowHeight</c>
    /// 一处，且那一处真在文件里；④ 段里点名的档位与归档目录一一对上、都在磁盘上、都在产出物清单第 2 行里；
    /// ⑤ 那两档的 px 数被标明是<b>模拟器那一跑</b>的读数（不许被当成真机的预算）；⑥ G-D4 那一格有反向锚点
    /// 指回这一段——单向引用等于只有被指着的一方知道有这件事。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判真机到底超没超（那天由报告那两行说），<b>不判预算该是多少</b>（本轮一行未改，
    /// 改它等于替真机做决定），也不判 G-D4 或 G-D5 算不算过（<see cref="Checklist_RealMachineGate_IsStillUnmetAndSaysSo"/>）。
    /// </para>
    /// </summary>
    [Fact]
    public void Gd4_RealMachineOverBudget_IsPreRegistered()
    {
        var verdict = Rows().Single(r => r.Item.StartsWith("G-D5", StringComparison.Ordinal)).Verdict;

        // ① 段在位，并切到下一段之前。
        var start = verdict.IndexOf("行高预算判定（", StringComparison.Ordinal);
        Assert.True(start >= 0,
            "G-D5 行没有「行高预算判定（…）」那一段：真机 125% / 200% 超预算时「算不算过」仍是一句描述，"
            + "那天要么把超预算念成观感问题先勾上，要么把整条 G-D5 念成全不过，两种都不诚实。");
        var next = verdict.IndexOf("到期日判定（", start, StringComparison.Ordinal);
        var section = verdict[start..(next > start ? next : verdict.Length)];

        // ② 三句后果，一句都不能少。每条都念得出「缺的是哪一句、少了它那天会怎么读」。
        foreach (var (needle, consequence) in new (string, string)[]
                 {
                     ("其一，动作单第 2 条判不过", "超预算没有后果，那一屏的截图就会被当成「量过了」，而动作单第 2 条的判定标准本就是「三档都无裁切」"),
                     ("其二，G-D4 那一格的 `[x]` 不动", "要么没人知道往哪一格补话，要么有人把那一格改回空框——那才是把「可源码判定」与「渲染观感」两件事混成一套"),
                     ("其三，这段预登记不构成任何一项达成的说法", "一段提前写好的判定被读成一条已完成的工作"),
                 })
        {
            Assert.True(section.Contains(needle, StringComparison.Ordinal),
                $"段里没有那一句「{needle}」：少了它，{consequence}。");
        }

        // 「不改预算」必须写在段里：缺它，下一轮的默认动作就是把预算改到能过。
        Assert.True(section.Contains("不改预算", StringComparison.Ordinal),
            "段里没有「不改预算」这四个字：少了这一句，超预算当天的最短路径是把预算改到能过，而不是把数记下来。");

        // ③ 判据取同一行报出的数，算式只指一处，且那一处真在文件里（指空即红）。
        //    这里判的是**带反引号的整跨度**：裸子串 Contains("MaxRowHeight") 会被 `MaxRowHeightX` 蒙过——
        //    符号改了名而文档没跟上，正是这一段最该红的那一种。
        // 判的是**整句话**而不是「同一行报出」这四个字：那四个字在本段出现了两次（「其二」那句引的
        // 归档措辞里还有一次），只判子串时把上面那句换成「要比的那个数看那一行」照样绿——
        // 与第二百一十一轮记下的「Presence 型断言在同一个字符串里被另一处满足」同一个坑，本轮自己又踩了一次。
        Assert.True(section.Contains("要比的那个数就取同一行报出来的那一个", StringComparison.Ordinal),
            "段里没有「要比的那个数就取同一行报出来的那一个」这句整话：不写清预算取报告同一行的那个数，"
            + "读的人会拿第二百一十一轮记在 .winecheck/README.md 里的 50px / 65px 当真机的预算，而预算按档位现算。");
        Assert.True(section.Contains("`MaxRowHeight`", StringComparison.Ordinal),
            $"段里没有带反引号的那一个跨度 `MaxRowHeight`（现读到的反引号跨度："
            + string.Join("、", Regex.Matches(section, "`([^`]+)`").Cast<Match>().Select(m => m.Groups[1].Value).Take(8))
            + "）：算式指给一个不存在的符号名，那天比的是一个没人算过的数。");
        var layout = Repo.Source("Desktop", "Native", "ProfileFormLayout.cs");
        Assert.True(layout.Contains("public static int MaxRowHeight(", StringComparison.Ordinal),
            "段里把行高预算指给 ProfileFormLayout.MaxRowHeight，而那个文件里没有这一处算式：算式搬了家，"
            + "这段预登记指的是一行不存在的判据，那天按它比的是空气。");
        Assert.True(Regex.Matches(section, "`MaxRowHeight`").Count == 1,
            "段里点了两次以上 `MaxRowHeight`：这一族要的是「算式只有一处」，正文里复述第二遍就是抄的第二份。");

        // ③之二：那句要**照抄进 G-D4 格**的措辞，自己就得带着「预算取同一行」这个口径与那两个目录名。
        //        它会被整句搬走，所以判它完整，不判它出现——只判段里别处提到，搬走的那一句可以是个空壳。
        var quoted = Regex.Match(section, @"措辞为「(?<q>[^」]+)」").Groups["q"].Value;
        Assert.True(quoted.Length > 0,
            "段里没有「措辞为「…」」那一句：那天要写进 G-D4 格的话没有定好的原句，回写又变成现场编。");
        Assert.True(quoted.Contains("同一行报出的行高预算", StringComparison.Ordinal),
            $"要照抄进 G-D4 格的那一句里没有「同一行报出的行高预算」（现读到：{quoted}）："
            + "搬过去的句子不带口径，读 G-D4 的人手里就只剩一个数，而那个数是按档位现算的。");

        // ④ 档位 ⇄ 归档目录：同一个数字，且是产出物清单第 2 行声明的那几个。
        var tiers = Regex.Matches(section, @"(?<t>\d{3})\s*%\s*档").Cast<Match>()
            .Select(m => m.Groups["t"].Value).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var dirs = Regex.Matches(section, @"`(?<d>\d{2,3})/`").Cast<Match>()
            .Select(m => m.Groups["d"].Value).Distinct(StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal).ToList();
        Assert.True(tiers.Count == 2 && dirs.Count == 2 && tiers.SequenceEqual(dirs, StringComparer.Ordinal),
            $"段里那两个档位（现读到：{(tiers.Count == 0 ? "没写档位" : string.Join("、", tiers))}）与那两个归档目录名"
            + $"（现读到：{(dirs.Count == 0 ? "没写目录" : string.Join("、", dirs))}）不是同一批数字："
            + "简报原文的 dpi-125/ 与 dpi-200/ 在本仓不存在——照它建目录，collect-real-machine.sh 会因为"
            + "「磁盘上多出一个清单没点到的目录」退 2，读起来却像证据到位了。");
        var row2 = RealMachineEvidenceTests.ArtifactRows().Single(r => r.Action == 2);
        var declared = RealMachineEvidenceTests.DirectoriesIn(row2.DirCell);
        var strays = dirs.Where(d => !declared.Contains(d, StringComparer.Ordinal)).ToList();
        Assert.True(strays.Count == 0,
            "段里那两个目录不在产出物清单第 2 行声明的集合里：" + string.Join("、", strays)
            + "（清单现读到：" + string.Join("、", declared) + "）。目录名有第二份出处，就是那天报警时念错的那一个。");
        foreach (var dir in dirs)
        {
            Assert.True(Directory.Exists(Path.Combine(RealMachineEvidenceDir, dir)),
                $"段里指向 docs/evidence/real-machine/{dir}/，磁盘上没有这个目录：当天第一件事又变成建目录。");
            Assert.True(quoted.Contains($"`{dir}/`", StringComparison.Ordinal),
                $"那两个目录里的 {dir}/ 没有出现在要照抄的那一句里（现读到：{quoted}）："
                + "回写时被人手补一个出处，等于那天没归档也能写成归档了。");
        }

        // ⑤ 那两个 px 数被标明出自模拟器那一跑（不标明，读的人会把 wine 的数当成真机的预算）。
        Assert.True(Regex.IsMatch(section, @"模拟器[^。]{0,20}\d{2}px[^。]{0,20}\d{2}px"),
            "段里那两个 px 数没有跟在「模拟器」后面：那是第二百一十一轮在 wine 里量到的两个读数，"
            + "不写出处就会被读成「真机的预算是 50px / 65px」，而预算本来按档位现算。");
        foreach (var forbidden in new[] { "真机实测", "真机已验证", "真机证据已归档" })
        {
            Assert.False(section.Contains(forbidden, StringComparison.Ordinal),
                $"段里出现了「{forbidden}」：这一段是预登记，真机还没跑过，这种措辞会把一条待办读成一条已完成。");
        }

        // ⑥ G-D4 那一格指回这一段（双向：单向引用等于只有被指着的一方知道有这件事）。
        var gd4 = Rows().Single(r => r.Item.StartsWith("G-D4", StringComparison.Ordinal)).Verdict;
        Assert.True(gd4.Contains("行高预算判定", StringComparison.Ordinal),
            "G-D4 那一格没有指回「行高预算判定」那一段：那天读 G-D4 的人不知道自己就是回写落点，"
            + "而这段预登记写的第二句会永远落空。");
        Assert.True(gd4.Contains("仍随 G-D5 真机复核的只剩", StringComparison.Ordinal),
            "G-D4 那一格被改了，段里指的补写位置（「仍随 G-D5 真机复核的只剩…」那一句之后）已不存在："
            + "回写落点悬空，等于那天现场发明一句。");
    }
}
