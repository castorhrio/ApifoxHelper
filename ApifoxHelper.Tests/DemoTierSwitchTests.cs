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
    /// 这张表不能只登记在文档里：README 里放表的那一节必须点名本门，
    /// 否则下一个人读到「实测：两支读数相同」时，没有任何东西告诉他这句话是被钉住的。
    /// </summary>
    [Fact]
    public void SixCellTable_NamesTheGateThatPinsIt()
    {
        Assert.Contains("DemoTierSwitchTests", Readme, StringComparison.Ordinal);
    }

    // ── 判据自测：注入破坏必须红 ──────────────────────────────────────

    /// <summary>
    /// 这一族的老毛病：判据写错时门看起来是绿的。三条判据各注入一处破坏，都必须报出来。
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

        // 判据本身在真实文件上必须是绿的（否则上面那些「非空」只是因为它一直在报）。
        Assert.Empty(TierSwitchBreaks(Sh, Ps1));
        Assert.Empty(SectionStatsBreaks(Sh, Ps1));
        Assert.Empty(SixCellBreaks(Readme));
    }
}
