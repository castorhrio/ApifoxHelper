using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 第二百一十一轮那张「六格表」的机器门（第二百三十三轮，简报 P0-2）。
/// <para>
/// <b>要防的是什么</b>：<c>.winecheck/README.md</c>「Windows 侧那支（<c>demo.ps1</c>）」第 4 条里那张
/// 六格表（<c>demo.sh</c> × <c>demo.ps1</c> × 96/120/192，每格两个数）是第二百一十一轮隔离实验的结论——
/// 「改前缀 + 重启前缀之后两支读数相同」。它此前<b>只登记在文档里，没有任何东西钉住</b>：
/// 谁改了 <c>demo.ps1</c> 的换档段（<c>reg add … LogPixels</c> 那一段）或两支的取段口径，
/// 那张表不会红，而它读起来比谁都扎实。这正是本仓反复收掉的那一族——<b>登记与实物漂移</b>。
/// </para>
/// <para>
/// <b>判什么（按六格表的形状）</b>：
/// ① 两支脚本的换档段逐字同名——同一个注册表键/值名/类型、同一个「改档与跑冒烟落在同一个活显示」的写法、
/// 同一个「单档跑」的循环、同一个跑完改回 96 的收尾；
/// ② 两支脚本的取段口径逐字对齐——段名正则 <c>[RunXxx]</c>、段标题判定 <c>== </c>、
/// <c>PASS </c>/<c>FAIL</c> 前缀、读数行 <c>section=… tier=… passed=… failed=…</c>；
/// ③ 那张表本身：六行、三档 × 两支、同档两支的三个数逐字相同（表自己的结论）、档位集合与脚本缺省档位一致。
/// </para>
/// <para>
/// <b>判不到什么（如实记）</b>：不判读数是不是真跑出来的（那是 <c>reading.txt</c> 与报告原文的事，
/// 边界写在 <see cref="ReadingConsistencyTests"/> 的类注释里）；不判 <c>wineserver -k</c> 那一步——
/// 它<b>不在任何一支脚本里</b>，是第二百一十一轮当场用 shell 命令做的手工步骤（README 原文写着
/// 「跑法是当场用 shell 命令跑的，脚本没进仓」）。所以本门反过来钉住这件事：
/// 两支脚本都<b>不得</b>出现 <c>wineserver -k</c>，否则 README 那句「脚本没进仓」当场变成假话，
/// 而六格表的上下文也就换了来源。
/// </para>
/// </summary>
public class DemoTierSwitchTests
{
    private static string Sh => Repo.Source(".winecheck", "demo.sh");

    private static string Ps1 => Repo.Source(".winecheck", "demo.ps1");

    private static string Readme => Repo.Source(".winecheck", "README.md");

    /// <summary>「重启前缀 + 单档跑」那一节的正文（自测要拿它注入破坏，取法与 <see cref="RunSheet"/> 同一份）。</summary>
    private static string Sheet => RunSheet();

    /// <summary>六格表的三档（<c>LogPixels</c> 值），也是两支脚本的缺省档位。</summary>
    private static readonly string[] Tiers = ["96", "120", "192"];

    // ── ① 换档段：两支逐字同名 ────────────────────────────────────────

    /// <summary>
    /// 换档段的两支写法对照表：<b>同一件事在两支脚本里各长什么样</b>。
    /// <para>
    /// 写成字面量而不是「抓一段出来比」：抓出来的东西会跟着源码漂，而这条门要防的恰恰是漂。
    /// 每一条都带一句「为什么它必须在」——改脚本的人看到红消息就知道自己动的是六格表的哪一格。
    /// </para>
    /// </summary>
    private static readonly (string What, string Sh, string Ps1)[] TierSwitchPieces =
    [
        ("注册表键（缩放档写在这里）", "HKCU\\Control Panel\\Desktop", "HKCU\\Control Panel\\Desktop"),
        ("值名", "/v LogPixels", "/v LogPixels"),
        ("值类型", "/t REG_DWORD", "/t REG_DWORD"),
        ("档位取自循环变量（单档跑，不是一次跑三档）", "/d \"$TIER\"", "/d $tier"),
        ("强制覆盖已有值", "-f", "-f"),
        ("冒烟器报告落点（同一档的报告路径）", "\"--report=$REPORT\"", "\"--report=$report\""),
        ("跑完改回 100%（别把下一个人留在 200% 档）",
            "/v LogPixels /t REG_DWORD /d 96 -f", "/v LogPixels /t REG_DWORD /d 96 -f"),
    ];

    /// <summary>缺省档位（六格表的三列）在两支脚本里必须是同一串。</summary>
    private static readonly (string What, string Sh, string Ps1)[] TierListPieces =
    [
        ("缺省档位", "TIERS=${TIERS:-96 120 192}", "$Tiers = '96 120 192'"),
        ("单档循环", "for TIER in $TIERS; do", "foreach ($tier in @($Tiers -split ' ' | Where-Object { $_ }))"),
    ];

    /// <summary>
    /// 换档段逐字同名（六格表的「同一份产物、同一个前缀、同一台 wine」那一半）。
    /// 返回空列表 = 两支对得上；非空 = 逐条点名哪一件在哪一支里没了。
    /// </summary>
    private static List<string> TierSwitchBreaks(string sh, string ps1)
    {
        var breaks = new List<string>();
        foreach (var (what, shPiece, ps1Piece) in TierSwitchPieces.Concat(TierListPieces))
        {
            if (!sh.Contains(shPiece, StringComparison.Ordinal))
            {
                breaks.Add($"demo.sh 里找不到「{what}」（应为 `{shPiece}`）——六格表那一格量到的上下文变了。");
            }
            if (!ps1.Contains(ps1Piece, StringComparison.Ordinal))
            {
                breaks.Add($"demo.ps1 里找不到「{what}」（应为 `{ps1Piece}`）——六格表那一格量到的上下文变了。");
            }
        }

        // 两支的缺省档位必须是同一串三个数（六格表的三列）。
        var shTiers = Regex.Match(sh, @"TIERS=\$\{TIERS:-([0-9 ]+)\}");
        var psTiers = Regex.Match(ps1, @"\$Tiers = '([0-9 ]+)'");
        if (!shTiers.Success) breaks.Add("demo.sh 的缺省档位取不出来（`TIERS=${TIERS:-…}` 那一行）。");
        if (!psTiers.Success) breaks.Add("demo.ps1 的缺省档位取不出来（`$Tiers = '…'` 那一行）。");
        if (shTiers.Success && psTiers.Success)
        {
            var a = shTiers.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var b = psTiers.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (!a.SequenceEqual(b, StringComparer.Ordinal))
            {
                breaks.Add($"两支的缺省档位不是同一串：demo.sh「{string.Join(' ', a)}」、demo.ps1「{string.Join(' ', b)}」。");
            }
            if (!a.SequenceEqual(Tiers, StringComparer.Ordinal))
            {
                breaks.Add($"缺省档位「{string.Join(' ', a)}」与六格表的三档（{string.Join('/', Tiers)}）不一致。");
            }
        }

        return breaks;
    }

    [Fact]
    public void TierSwitch_IsTheSameInBothScripts()
    {
        var breaks = TierSwitchBreaks(Sh, Ps1);
        Assert.True(breaks.Count == 0,
            "换档段两支对不上（六格表「改前缀 + 重启前缀之后两支读数相同」的前提没了）：\n  "
            + string.Join("\n  ", breaks));
    }

    // ── ② 取段口径：两支逐字对齐 ──────────────────────────────────────

    /// <summary>
    /// 取段口径的对照表：段名、段标题判定、<c>PASS</c>/<c>FAIL</c> 前缀、读数行格式。
    /// <para>
    /// 这里钉的是<b>两支取同一份报告的同一种取法</b>：段名只认 <c>[RunXxx]</c>、段标题只认 <c>== </c> 开头、
    /// 计数只认行首 <c>PASS </c> 与 <c>FAIL</c>（注意：段内计数是 <c>FAIL</c> 不带空格、总计是 <c>FAIL </c>
    /// 带空格——这个不对称是两支<b>共有</b>的既有口径，本轮只钉住它，不改它）。
    /// 任一支改了取法，两支的读数就不再是同一件事，六格表也就不能互相对照。
    /// </para>
    /// </summary>
    private static readonly (string What, string Sh, string Ps1)[] SectionStatsPieces =
    [
        ("段名正则（只认 [RunXxx]）", "\\[Run[A-Za-z0-9_]+\\]", "\\[Run[A-Za-z0-9_]+\\]"),
        ("段标题判定（== 开头）", "/^== /", "-like '== *'"),
        ("段内通过行前缀", "/^PASS /", "StartsWith('PASS ')"),
        ("段内失败行前缀（不带空格）", "/^FAIL/", "StartsWith('FAIL')"),
        ("读数行格式", "section=%s tier=%s passed=%d failed=%d", "section=$id tier=$tier passed=$pass failed=$fail"),
        ("总计通过行前缀", "grep -c '^PASS '", "StartsWith('PASS ')"),
        ("总计失败行前缀（带空格）", "grep -c '^FAIL '", "StartsWith('FAIL ')"),
    ];

    private static List<string> SectionStatsBreaks(string sh, string ps1)
    {
        var breaks = new List<string>();
        foreach (var (what, shPiece, ps1Piece) in SectionStatsPieces)
        {
            if (!sh.Contains(shPiece, StringComparison.Ordinal))
            {
                breaks.Add($"demo.sh 的取段口径里找不到「{what}」（应为 `{shPiece}`）。");
            }
            if (!ps1.Contains(ps1Piece, StringComparison.Ordinal))
            {
                breaks.Add($"demo.ps1 的取段口径里找不到「{what}」（应为 `{ps1Piece}`）。");
            }
        }

        // 两支都必须有「按段拆报告」的那一个函数（名字不同、职责同一）。
        if (!sh.Contains("section_stats()", StringComparison.Ordinal))
        {
            breaks.Add("demo.sh 里没有 section_stats()——按段拆报告的那一处没了。");
        }
        if (!ps1.Contains("function Get-SectionStats(", StringComparison.Ordinal))
        {
            breaks.Add("demo.ps1 里没有 Get-SectionStats——按段拆报告的那一处没了。");
        }

        return breaks;
    }

    [Fact]
    public void SectionStats_ExtractTheSameWayInBothScripts()
    {
        var breaks = SectionStatsBreaks(Sh, Ps1);
        Assert.True(breaks.Count == 0,
            "两支的取段口径对不上（六格表每格那两个数就不再是同一件事）：\n  " + string.Join("\n  ", breaks));
    }

    // ── ③ 六格表本身 ─────────────────────────────────────────────────

    /// <summary>六格表的一行：脚本、档位、控件自报、进程自报、通过数、失败数。</summary>
    private sealed record Cell(string Script, string Tier, string ControlDpi, string ProcessDpi, int Passed, int Failed);

    private static readonly Regex CellRow = new(
        @"^\s*\|\s*`demo\.(?<script>sh|ps1)`\s*\|\s*(?<tier>\d+)\s*\|\s*(?<control>\d+)\s*\|\s*(?<process>\d+)\s*\|\s*(?<passed>\d+) 通过 / (?<failed>\d+) 失败\s*\|",
        RegexOptions.Compiled);

    private static List<Cell> Cells(string readme) =>
        readme.Split('\n')
            .Select(line => CellRow.Match(line))
            .Where(m => m.Success)
            .Select(m => new Cell(
                "demo." + m.Groups["script"].Value,
                m.Groups["tier"].Value,
                m.Groups["control"].Value,
                m.Groups["process"].Value,
                int.Parse(m.Groups["passed"].Value),
                int.Parse(m.Groups["failed"].Value)))
            .ToList();

    /// <summary>
    /// 六格表在 README 里、六行齐、三档 × 两支，且<b>同档两支的三个数逐字相同</b>——
    /// 表自己的结论（「改前缀 + 重启前缀之后两支读数相同」）由表本身判，不靠人念。
    /// </summary>
    private static List<string> SixCellBreaks(string readme)
    {
        var breaks = new List<string>();
        var cells = Cells(readme);
        if (cells.Count != 6)
        {
            breaks.Add($"README 里的六格表读到 {cells.Count} 行（应为 6 行：三档 × 两支）。");
            return breaks;
        }

        foreach (var tier in Tiers)
        {
            var sh = cells.SingleOrDefault(c => c.Tier == tier && c.Script == "demo.sh");
            var ps1 = cells.SingleOrDefault(c => c.Tier == tier && c.Script == "demo.ps1");
            if (sh is null) { breaks.Add($"六格表缺 demo.sh 的 {tier} 档那一行。"); continue; }
            if (ps1 is null) { breaks.Add($"六格表缺 demo.ps1 的 {tier} 档那一行。"); continue; }
            if (sh.ControlDpi != ps1.ControlDpi || sh.ProcessDpi != ps1.ProcessDpi)
            {
                breaks.Add($"{tier} 档两支的 DPI 两个数不同：demo.sh {sh.ControlDpi}/{sh.ProcessDpi}、"
                    + $"demo.ps1 {ps1.ControlDpi}/{ps1.ProcessDpi}——表自己的结论（两支读数相同）不成立了。");
            }
            if (sh.Passed != ps1.Passed || sh.Failed != ps1.Failed)
            {
                breaks.Add($"{tier} 档两支的读数不同：demo.sh {sh.Passed} 通过 / {sh.Failed} 失败、"
                    + $"demo.ps1 {ps1.Passed} 通过 / {ps1.Failed} 失败——表自己的结论不成立了。");
            }
        }

        var tiers = cells.Select(c => c.Tier).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (!tiers.SequenceEqual(Tiers.OrderBy(t => t, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            breaks.Add($"六格表的档位集合是「{string.Join('/', tiers)}」，与脚本缺省档位（{string.Join('/', Tiers)}）不一致。");
        }

        return breaks;
    }

    [Fact]
    public void SixCellTable_IsRegisteredAndSaysWhatItClaims()
    {
        var breaks = SixCellBreaks(Readme);
        Assert.True(breaks.Count == 0,
            "六格表与它自己的结论对不上：\n  " + string.Join("\n  ", breaks));
    }

    /// <summary>
    /// 六格表的「重启前缀」那一步是<b>手工步骤</b>，不在任何一支脚本里——README 原文这么写着，
    /// 两支脚本也确实没有它。这条门把这件事钉成事实：谁哪天把 <c>wineserver -k</c> 写进脚本，
    /// README 那句「跑法是当场用 shell 命令跑的，脚本没进仓」就变成假话，六格表的上下文也换了来源。
    /// </summary>
    [Fact]
    public void RestartPrefixStep_StaysAManualStepOutsideBothScripts()
    {
        Assert.Contains("wineserver -k", Readme, StringComparison.Ordinal);
        Assert.Contains("wineserver -w", Readme, StringComparison.Ordinal);
        Assert.Contains("脚本没进仓", Readme, StringComparison.Ordinal);

        foreach (var (name, text) in new[] { ("demo.sh", Sh), ("demo.ps1", Ps1) })
        {
            Assert.False(text.Contains("wineserver -k", StringComparison.Ordinal),
                $"{name} 里出现了 `wineserver -k`：README 写着重启前缀是当场手工做的、脚本没进仓。"
                + "要么把这一步从脚本里拿掉，要么把 README 那段（含六格表的上下文说明）改成与实物一致。");
        }
    }

    /// <summary>
    /// 这张表不能只登记在文档里：README 里放表的那一节必须点名本门，<c>docs/NOTES.md</c> 引用它时也要点名
    /// （第二百三十四轮按简报 P1-2 补上后一半）。README 是操作面，NOTES 是<b>答辩口径的第一读面</b>——
    /// 那边写着「实测：两支读数相同」而这边不说是谁钉住的，读到 NOTES 的人无从判断这句话会不会漂。
    /// </summary>
    [Fact]
    public void SixCellTable_NamesTheGateThatPinsIt()
    {
        Assert.Contains("DemoTierSwitchTests", Readme, StringComparison.Ordinal);

        var notes = Repo.Source("docs", "NOTES.md");
        Assert.True(notes.Contains("六格表", StringComparison.Ordinal),
            "docs/NOTES.md 里再没有引用「六格表」了：那一句要么被删要么改了名，README 那一节也就没了对外读面。");
        Assert.Contains("DemoTierSwitchTests", notes, StringComparison.Ordinal);
    }

    // ── ④ 重启前缀 + 单档跑的步骤单 ───────────────────────────────────

    /// <summary>步骤单那一节的标题（按标题取，不拍平整份 README——README 里别处也出现 <c>wineserver</c>）。</summary>
    private const string RunSheetHeading = "## 重启前缀 + 单档跑";

    /// <summary>步骤单的步数（第二百三十四轮实测跑通的那一条路，一步不多一步不少）。</summary>
    private const int RunSheetSteps = 8;

    /// <summary>「命令」那一格里必须认得出的命令起手式——不在名单里的跨度算正文，不算命令。</summary>
    private static readonly string[] CommandWords =
        ["export ", "xvfb-run", "wine ", "wineserver", "pwsh", "bash ", "grep ", "dotnet ", "TIERS=", "READING="];

    /// <summary>步骤单正文（从标题到下一个二级标题）。</summary>
    private static string RunSheet()
    {
        var start = Readme.IndexOf(RunSheetHeading, StringComparison.Ordinal);
        Assert.True(start >= 0,
            $".winecheck/README.md 里找不到「{RunSheetHeading}」那一节：第二百一十一轮那场手工实验又回到「当场 shell 跑的」，"
            + "下一轮想复现真 DPI 上下文只能重新猜一遍命令。");
        var rest = Readme[(start + RunSheetHeading.Length)..];
        var end = rest.IndexOf("\n## ", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>步骤单表格的一行：序号、做什么、命令、产物、判据。</summary>
    private sealed record RunStep(int No, string What, string Command, string Output, string Verdict);

    /// <summary>取出步骤单表格的每一行（列数不是 5 的行也报出来，不许静默跳过）。</summary>
    private static List<RunStep> RunSteps(string sheet, List<string> breaks)
    {
        var steps = new List<RunStep>();
        foreach (var line in sheet.Split('\n'))
        {
            if (!line.TrimStart().StartsWith("| ", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (!int.TryParse(cells[0], out var no))
            {
                if (cells[0] is "---" or "#") continue;
                if (cells.Count == 5) breaks.Add($"步骤单有一行第一格不是序号：{cells[1]}");
                continue;
            }
            if (cells.Count != 5)
            {
                breaks.Add($"步骤单第 {no} 步不是 5 列（实际 {cells.Count} 列，应为 序号 / 做什么 / 命令 / 产物 / 判据）");
                continue;
            }
            steps.Add(new RunStep(no, cells[1], cells[2], cells[3], cells[4]));
        }

        return steps;
    }

    /// <summary>反引号跨度（命令、路径、报告行名都写在这一层里）。</summary>
    private static List<string> Spans(string cell) =>
        Regex.Matches(cell, @"`([^`]+)`")
            .Cast<Match>().Select(m => m.Groups[1].Value).ToList();

    /// <summary>步骤单的破损清单（空 = 每一步都点得名、拿得到产物）。</summary>
    private static List<string> RunSheetBreaks(string sheet)
    {
        var breaks = new List<string>();
        var steps = RunSteps(sheet, breaks);
        if (steps.Count != RunSheetSteps)
        {
            breaks.Add($"步骤单读到 {steps.Count} 步（应为 {RunSheetSteps} 步）：少一步就复现不出那个上下文，多一步要在这里登记原因。");
        }

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (step.No != i + 1)
            {
                breaks.Add($"步骤单第 {i + 1} 行标的是「{step.No}」：序号不连续，照着念的人会跳步或重跑。");
            }

            var commands = Spans(step.Command);
            if (commands.Count == 0)
            {
                breaks.Add($"步骤单第 {step.No} 步「{step.What}」的命令那一格没有反引号跨度：那是一句描述，不是一条能照抄的命令。");
            }
            else if (!commands.Any(c => CommandWords.Any(w => c.Contains(w, StringComparison.Ordinal))))
            {
                breaks.Add($"步骤单第 {step.No} 步的命令跨度里没有认得出的起手式（{string.Join('、', CommandWords)}）：{commands[0]}");
            }

            // 产物要么点名一样东西（一个跨度：文件、变量、注册表值、报告行），要么明写「无」并说清等的是什么。
            var outputs = Spans(step.Output);
            if (outputs.Count == 0 && !step.Output.StartsWith("无（", StringComparison.Ordinal))
            {
                breaks.Add($"步骤单第 {step.No} 步「{step.What}」的产物那一格没点名的东西：跑完拿不出一个可看的东西，这一步就无法自证跑过。");
            }

            var verdicts = Spans(step.Verdict);
            if (step.Verdict.Length < 12 || verdicts.Count == 0)
            {
                breaks.Add($"步骤单第 {step.No} 步的判据那一格落不到一个读数（要么太短，要么没引用看得见的东西）：「跑成功了」不算判据。");
            }
        }

        // 命令里点到的仓内文件必须在盘上——步骤单漂了最先漂的就是路径。
        foreach (var path in Regex.Matches(sheet, @"(?<![\w/.-])((?:\.winecheck|docs|ApifoxHelper\.Smoke)/[\w.\-]+)")
                     .Cast<Match>().Select(m => m.Groups[1].Value).Distinct())
        {
            if (!File.Exists(Path.Combine(Repo.Root, path)))
            {
                breaks.Add($"步骤单第 …步的命令里点了「{path}」，仓里没有这个文件：照着抄会跑在一条不存在的路上。");
            }
        }

        // 四件横跨全文的事：两条旋钮、收尾改回 100%、以及这一节自己的口径。
        foreach (var needle in new[]
                 {
                     "TIERS=", "READING=/tmp", "LogPixels", "wineserver -k", "wineserver -w",
                     "控件自报 DPI", "不是真机",
                 })
        {
            if (!sheet.Contains(needle, StringComparison.Ordinal))
            {
                breaks.Add($"步骤单里再没有「{needle}」：这一条是复现那一个上下文的必要条件，写了命令少了它仍然跑不出同一件事。");
            }
        }

        // 收尾那一步必须把档改回来：同一份前缀是共享的开发环境，别把下一个人留在 200% 档。
        var last = steps.Count == 0 ? string.Empty : steps[^1].Verdict + steps[^1].Command;
        if (!last.Contains("96", StringComparison.Ordinal))
        {
            breaks.Add("步骤单最后一步没把缩放改回 96：下一跑（或下一个人）会留在改过的那一档上量别的事。");
        }

        return breaks;
    }

    /// <summary>
    /// 「重启前缀 + 单档跑」这条路必须在<b>文档</b>里逐行写明，而写明的标准是「每一步都点得出命令与产物」。
    /// <para>
    /// <b>为什么这一条值得立门</b>：行高预算那一格只有在「控件自报 DPI 跟着档位走」的上下文里才量得出东西，
    /// 而那一个上下文<b>不在任何一支脚本里</b>（上一条门不许它进脚本）。第二百一十一轮是当场手工跑的，
    /// 跑法只活在 <c>.winecheck/README.md</c> 的散文里——散文里少写一句「<c>wineserver -k</c> 退出码 1 也算过」
    /// 或「<c>READING</c> 要指到 /tmp」，下一轮照着跑出来的就是一跑读不出数的东西，
    /// 而那次跑废掉的时间没人记账。所以判据按「每一步都能照抄、跑完都有产物」写。
    /// </para>
    /// <para>
    /// <b>它不判</b>：不判照着跑出来的读数是多少（那是报告与 <c>reading.txt</c> 的事），
    /// 也不判这一节说的上下文是不是真机（这一节自己写着不是）。
    /// </para>
    /// </summary>
    [Fact]
    public void RestartPrefixRunSheet_EveryStepNamesACommandAndAnOutput()
    {
        var breaks = RunSheetBreaks(RunSheet());
        Assert.True(breaks.Count == 0, "重启前缀 + 单档跑的步骤单不完整：\n  " + string.Join("\n  ", breaks));
    }

    // ── ⑤ 修复后的三档 overbudget 读数（第二百三十五轮，简报 P0-1 的证据那一半）──

    /// <summary>那一行的锚点（是一段话，不是一节，所以不按 <c>##</c> 取）。</summary>
    private const string PostFixHeading = "**修复后的三档 overbudget 读数";

    /// <summary>修复后读数的三格：档位、配置表单超预算行数、提示行超预算条数。</summary>
    private sealed record PostFixReading(int Tier, int Form, int Hint);

    /// <summary>取修复后那一段的正文（从锚点到下一个空行；段内不含空行）。</summary>
    private static string PostFixBlock(string readme)
    {
        var at = readme.IndexOf(PostFixHeading, StringComparison.Ordinal);
        if (at < 0) return string.Empty;
        var rest = readme[at..];
        var end = rest.IndexOf("\n\n", StringComparison.Ordinal);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>把修复后那一段整段摘掉的那份 README——只给注入自测用（「改完没人把数带回来」就是这一形状）。</summary>
    private static string ReadmeWithoutPostFix()
    {
        var block = PostFixBlock(Readme);
        Assert.True(block.Length > 0, "README 里本来就没有那一段，注入自测失去对象——先看第 ⑤ 节的锚点还在不在。");
        return Readme.Replace(block, string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// 修复后那一行的全部破坏形状（<paramref name="readme"/> 可以是注入过的那份）。
    /// <para>
    /// <b>为什么 96 与 120 是硬判 0，而 192 判的是「等于那三行分区标题的名字数」</b>：甲组那一族（标签列不随字号
    /// 放大）在这两档是<b>全部</b>超预算行，改了就该一个不剩；200% 档剩下的三行是第二百三十四轮登记的<b>另一个</b>
    /// 根因（分区标题用 Section 档，而预算只按正文档算）。把 192 也写成 0 就是拿别的修法没做的事实替这一条修法背书；
    /// 写成「≤ 3」则变成谁都能把第四行塞进来的免检通道。判成「行数 == 段内点名的那几行」之后，
    /// 真多出第四行时门会红，逼人先回报告看清是哪一行，而不是顺手把数改大。
    /// </para>
    /// </summary>
    private static List<string> PostFixOverbudgetBreaks(string readme)
    {
        var breaks = new List<string>();
        var block = PostFixBlock(readme);
        if (block.Length == 0)
        {
            breaks.Add($"README 里找不到「{PostFixHeading}」这一段：标签列改成随字号放大之后的三档超预算行数没有读面了，"
                + "下一轮无从判断这件事是被改回去还是根本没人复跑。");
            return breaks;
        }

        var readings = Regex.Matches(block, @"overbudget=tier=(\d+) form=(\d+) hint=(\d+)")
            .Cast<Match>()
            .Select(m => new PostFixReading(
                int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)))
            .ToList();
        var tiers = readings.Select(r => r.Tier.ToString()).ToList();
        if (readings.Count != Tiers.Length || !tiers.SequenceEqual(Tiers, StringComparer.Ordinal))
        {
            breaks.Add($"这一段点的档位是「{(tiers.Count == 0 ? "一个都没有" : string.Join('/', tiers))}」，"
                + $"应为 {string.Join('/', Tiers)} 三档各一格（缺一档就是那一档没人照着八步复跑过）："
                + "行高预算的账按档位记，少一档就少一个上下文。");
        }

        // 甲组归零的两档：硬判 0。
        foreach (var tier in new[] { 96, 120 })
        {
            var run = readings.FirstOrDefault(r => r.Tier == tier);
            if (run is not null && run.Form != 0)
            {
                breaks.Add($"{tier} 档 form={run.Form}（应为 0）：标签列随字号放大这条修法没起作用，"
                    + "先回 Desktop/Native/ProfileGridColumns.cs 的 ScaledLabelWidth 与 Ui.cs 的接线看是不是被改回固定像素。");
            }
        }

        // 200% 档：剩下的每一行都必须是段内点名的那一行分区标题。
        var sectionTitles = new[] { "必填", "可选设置（可保存后再补）", "高级设置（可选）" };
        var run200 = readings.FirstOrDefault(r => r.Tier == 192);
        var titled = sectionTitles.Count(title => block.Contains(title, StringComparison.Ordinal));
        if (run200 is not null && run200.Form != titled)
        {
            breaks.Add($"192 档 form={run200.Form}，而这一段点名的分区标题只有 {titled} 行"
                + $"（{string.Join("、", sectionTitles)}）："
                + "多出来的行不在这一段说的那个根因里——先回 /tmp/ah-demo-192/report-192.txt 看是哪一行超了，"
                + "再决定把它登记成第四个决定项，别把这里的数改大了事。");
        }
        foreach (var needle in new[] { "乙组", "决定项", "预算那条算式只按正文档算" })
        {
            if (!block.Contains(needle, StringComparison.Ordinal))
            {
                breaks.Add($"这一段再没有「{needle}」：192 档剩下的那几行为什么可以不是 0，靠的是这一段把它说清"
                    + "（另一个根因、已登记为决定项），话没说清就成了替没做的事背书。");
            }
        }

        // hint 那一列的代价：必须带着改前改后那两个可用宽，不能只说「有代价」。
        if (!block.Contains("如实记一笔代价", StringComparison.Ordinal))
        {
            breaks.Add("这一段没有「如实记一笔代价」那句：标签列宽是从同一笔可视宽度里出的，"
                + "200% 档提示行的超预算条数因此比修复前多——写了修法不写代价，读的人会以为两档都干净了。");
        }
        foreach (var width in new[] { "1086", "966" })
        {
            if (!block.Contains(width, StringComparison.Ordinal))
            {
                breaks.Add($"这一段念的代价里没有可用宽 {width}px 那个数：代价必须是可以回去量的两个数，不是一句形容词。");
            }
        }

        // 出处：这一段是重启前缀那一跑（/tmp），不是入库读数那一跑。
        if (!block.Contains("/tmp/ah-demo-", StringComparison.Ordinal) || !block.Contains("入库读数", StringComparison.Ordinal))
        {
            breaks.Add("这一段没说清自己的出处是 /tmp 那三份报告（重启前缀那一跑）：**两个上下文各念各的数**，"
                + "把它们折成一个说法是第二百三十四轮写下来要避免的动作。");
        }

        if (!block.Contains("PostFixOverbudgetReading_KeepsTheLabelColumnTiersAtZero", StringComparison.Ordinal))
        {
            breaks.Add("这一段没点名钉住本段的门：README 的操作面与 NOTES 的答辩面同一族纪律——"
                + "一句话悬空，下一轮改的人不知道自己动的是谁背了书的东西。");
        }

        return breaks;
    }

    /// <summary>
    /// 第二百三十五轮把标签列改成随字号放大之后，真 DPI 上下文那三档的超预算行数<b>必须有一段说清，且说清的就是实物</b>。
    /// <para>
    /// <b>补的是哪个缺口</b>：简报 P0-1 要「125% / 200% 档超预算行数归零」，而六格表只记<b>修复前</b>那一跑
    /// （125% 破 4 行、200% 破 10 行 + 提示行 2 条）。修完之后没有一行带着数回来，
    /// 这一格就只能读成「大概好了」——第二百三十四轮刚把「步骤单要能照着复跑」立成门，同一族的另一半就是
    /// <b>复跑出来的数要有落点</b>。判据见 <see cref="PostFixOverbudgetBreaks"/>。
    /// </para>
    /// </summary>
    [Fact]
    public void PostFixOverbudgetReading_KeepsTheLabelColumnTiersAtZero()
    {
        var breaks = PostFixOverbudgetBreaks(Readme);
        Assert.True(breaks.Count == 0, "修复后的三档 overbudget 读数与实物对不上：\n  " + string.Join("\n  ", breaks));
    }

    // ── 判据自测：注入破坏必须红 ──────────────────────────────────────

    /// <summary>
    /// 这一族的老毛病：判据写错时门看起来是绿的。四组判据各注入破坏（换档段、取段口径、六格表、步骤单），
    /// 都必须报出来；最后再判一次<b>真实文件是干净的</b>——否则前面那些「非空」只是因为它一直在报。
    /// </summary>
    [Fact]
    public void Detectors_ReportTheBreaksTheyMustCatch()
    {
        // ① 换档段：把 demo.ps1 的档位写成常量（不再单档跑）。
        var ps1Broken = Ps1.Replace("/d $tier", "/d 192", StringComparison.Ordinal);
        Assert.NotEqual(Ps1, ps1Broken);
        Assert.NotEmpty(TierSwitchBreaks(Sh, ps1Broken));

        // ① 换档段：把 demo.sh 的收尾（改回 96）删掉。
        var shBroken = Sh.Replace("/v LogPixels /t REG_DWORD /d 96 -f", "/v LogPixels /t REG_DWORD /d 120 -f", StringComparison.Ordinal);
        Assert.NotEqual(Sh, shBroken);
        Assert.NotEmpty(TierSwitchBreaks(shBroken, Ps1));

        // ② 取段口径：把 demo.ps1 的段内失败前缀改成带空格（与总计那一处混同）。
        var ps1StatsBroken = Ps1.Replace("StartsWith('FAIL')", "StartsWith('FAIL ')", StringComparison.Ordinal);
        Assert.NotEqual(Ps1, ps1StatsBroken);
        Assert.NotEmpty(SectionStatsBreaks(Sh, ps1StatsBroken));

        // ② 取段口径：把 demo.sh 的段名正则放宽（不再只认 [RunXxx]）。
        var shStatsBroken = Sh.Replace("\\[Run[A-Za-z0-9_]+\\]", "\\[Run[A-Za-z0-9_]*\\]", StringComparison.Ordinal);
        Assert.NotEqual(Sh, shStatsBroken);
        Assert.NotEmpty(SectionStatsBreaks(shStatsBroken, Ps1));

        // ③ 六格表：把 demo.ps1 的 120 档读数改成与 demo.sh 不同。
        var readmeBroken = Readme.Replace(
            "| `demo.ps1` | 120 | 120 | 120 | 217 通过 / 1 失败 |",
            "| `demo.ps1` | 120 | 120 | 120 | 218 通过 / 0 失败 |",
            StringComparison.Ordinal);
        Assert.NotEqual(Readme, readmeBroken);
        Assert.NotEmpty(SixCellBreaks(readmeBroken));

        // ③ 六格表：删掉一行（六格变五格）。
        var readmeShort = Readme.Replace(
            "   | `demo.sh` | 192 | 192 | 192 | 216 通过 / 2 失败 |\n", string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(Readme, readmeShort);
        Assert.NotEmpty(SixCellBreaks(readmeShort));

        // ④ 步骤单：少一步（第 8 步那一条整行删掉——收尾改回 96 就没人做了）。
        var sheetShort = Regex.Replace(Sheet, @"(?m)^\|\s*8\s*\|.*\n", string.Empty);
        Assert.NotEqual(Sheet, sheetShort);
        Assert.Contains(RunSheetBreaks(sheetShort), b => b.Contains("应为 8 步", StringComparison.Ordinal));

        // ④ 步骤单：产物格掏空——那一步跑完拿不出任何东西，等于没做，却照样是一行像样的步骤。
        var sheetNoOutput = Sheet.Replace("| 无（等的是一个退出状态） |", "|  |", StringComparison.Ordinal);
        Assert.NotEqual(Sheet, sheetNoOutput);
        Assert.Contains(RunSheetBreaks(sheetNoOutput), b => b.Contains("产物", StringComparison.Ordinal));

        // ④ 步骤单：命令格退成一句描述（反引号跨度没了——「重启前缀」四个字不是能照抄的命令）。
        var sheetNoCommand = Sheet.Replace(
            "`wineserver -k`（本机它不在 PATH，取 `/usr/lib/x86_64-linux-gnu/wine/wineserver -k`，第二百三十四轮实测）",
            "重启前缀", StringComparison.Ordinal);
        Assert.NotEqual(Sheet, sheetNoCommand);
        Assert.Contains(RunSheetBreaks(sheetNoCommand), b => b.Contains("命令那一格", StringComparison.Ordinal));

        // ④ 步骤单：路径漂了（把脚本改成一个仓里不存在的名字，照着抄会跑在一条不存在的路上）。
        var sheetBadPath = Sheet.Replace("bash .winecheck/demo.sh", "bash .winecheck/demo-v2.sh", StringComparison.Ordinal);
        Assert.NotEqual(Sheet, sheetBadPath);
        Assert.Contains(RunSheetBreaks(sheetBadPath), b => b.Contains("demo-v2.sh", StringComparison.Ordinal));

        // ⑤ 修复后读数：整段删掉（改完没人把数带回来）。
        var postFixRemoved = ReadmeWithoutPostFix();
        Assert.Contains(PostFixOverbudgetBreaks(postFixRemoved), b => b.Contains("没有读面", StringComparison.Ordinal));

        // ⑤ 修复后读数：120 档 form 写回 4（标签列那条修法没兑现，或被人改回固定像素）。
        var postFix125 = Readme.Replace("overbudget=tier=120 form=0 hint=0", "overbudget=tier=120 form=4 hint=0",
            StringComparison.Ordinal);
        Assert.NotEqual(Readme, postFix125);
        Assert.Contains(PostFixOverbudgetBreaks(postFix125), b => b.Contains("120 档 form=4", StringComparison.Ordinal));

        // ⑤ 修复后读数：192 档冒出第四行而段内仍只点名三行分区标题（新根因想混进旧账）。
        var postFix192 = Readme.Replace("overbudget=tier=192 form=3 hint=4", "overbudget=tier=192 form=4 hint=4",
            StringComparison.Ordinal);
        Assert.NotEqual(Readme, postFix192);
        Assert.Contains(PostFixOverbudgetBreaks(postFix192), b => b.Contains("192 档 form=4", StringComparison.Ordinal));

        // ⑤ 修复后读数：把代价那一句删掉（只报好消息，200% 档提示行多折的两行就没人知道了）。
        var postFixNoCost = Readme.Replace("如实记一笔代价", "另有一处", StringComparison.Ordinal);
        Assert.NotEqual(Readme, postFixNoCost);
        Assert.Contains(PostFixOverbudgetBreaks(postFixNoCost), b => b.Contains("如实记一笔代价", StringComparison.Ordinal));

        // ⑤ 修复后读数：门名被改掉（这一段就不再是「有门钉住的」那一段）。
        var postFixNoGate = Readme.Replace("PostFixOverbudgetReading_KeepsTheLabelColumnTiersAtZero",
            "PostFixOverbudgetReading_Renamed", StringComparison.Ordinal);
        Assert.NotEqual(Readme, postFixNoGate);
        Assert.Contains(PostFixOverbudgetBreaks(postFixNoGate), b => b.Contains("没点名钉住本段的门", StringComparison.Ordinal));

        // 判据本身在真实文件上必须是绿的（否则上面那些「非空」只是因为它一直在报）。
        Assert.Empty(TierSwitchBreaks(Sh, Ps1));
        Assert.Empty(SectionStatsBreaks(Sh, Ps1));
        Assert.Empty(SixCellBreaks(Readme));
        Assert.Empty(RunSheetBreaks(Sheet));
        Assert.Empty(PostFixOverbudgetBreaks(Readme));
    }
}
