using System.Text.RegularExpressions;

namespace ApifoxHelper.Tests;

/// <summary>
/// 真机未验证交互点登记表（<c>docs/real-machine-gaps.md</c>）的<b>判据本体与解析器</b>——
/// 「未验证交互点有界」与「README 点名屏对账」两条门共用的那一份（第四百二十五轮，控制仓简报 P1-1）。
/// <para>
/// <b>为什么收成一个类</b>：第四百二十三、四百二十四轮把两条门都写进了 <c>RealMachineSupplyGateTests.cs</c>，
/// 第二条门复用第一条门的 <see cref="UnverifiedPointProblems"/>（行质量那一半判据不养第二份）——
/// 文件三合一之后近四轮长了 680 行。本轮按控制仓简报 P1-1 拆成三个文件，共用的一半落在这里；
/// <b>判据一条不放宽</b>，两条门各自的断言逐字未动（<c>using static</c> 让它们继续按原名调用）。
/// 「真机供给未读」的有界门与这里无共用，仍留在 <c>RealMachineSupplyGateTests</c> 自己那份里。
/// </para>
/// <para>
/// <b>第四百二十七轮按控制仓简报 P0-1 加终局词形</b>：第四百二十六轮那一跑给右键菜单行「现读归零」，
/// 但量到的是鼠标路径，那一行登记的层（按键路由）从未被现读——行内自相矛盾。语义修正（最近一次现读
/// 改回「未现读」、计数按账本从首次登记起算）会让连续未验证超过上限 3 轮，而现读在这层永远做不了：
/// 模拟器没有为它写的冒烟段、真机供给已终局（第四百二十六轮起）。故上限那一格认终局词形——
/// 从未现读、且读数格把 <see cref="GapTerminalFacts"/> 两件事实写全的行不再逐轮判红，
/// 与 <c>RealMachineSupplyGateTests</c> 的「已终结」词形同一分寸；放宽的只有「到限必须现读」的重判时机，
/// 行质量三条（层名 / 轮次 / 计数现算）一条未放宽。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判真机有没有到手（那是 <c>collect-real-machine.sh</c> 与
/// <c>docs/evidence/real-machine/</c> 的地盘），不判 G-D5 那一格该不该勾（勾选框只由真机证据关闭），
/// 也不判「这一层该不该现在验」——那是排期，不是判据。
/// </para>
/// </summary>
internal static class RealMachineGapRegistry
{
    /// <summary>
    /// 登记表那一页（相对仓库根）。这一页是「哪些交互点还没在真机上验过」的<b>唯一落点</b>。
    /// </summary>
    internal const string GapRegistryPath = "docs/real-machine-gaps.md";

    /// <summary>
    /// 「未验证的是哪一层」只认这三个词：<c>按键路由</c>（按下去有没有走到那一段代码）、
    /// <c>弹出位置</c>（菜单 / 提示弹在哪）、<c>观感</c>（灰阶、强调色、字号行高在真机上的样子）。
    /// <para>
    /// <b>为什么是封闭的三个词</b>：含糊话（「未验证」「待复核」）读不出「这一层到底量没量得到」，
    /// 下一轮读的人照样要重判一次——那正是这一页要挡的事。
    /// </para>
    /// </summary>
    internal static readonly string[] GapLayers = ["按键路由", "弹出位置", "观感"];

    /// <summary>
    /// 连续未验证的上限。到第 <see cref="MaxConsecutiveUnverified"/> 轮为止可以只登记；
    /// <b>第 4 轮必须现读一次</b>——上限取 3 的理由与「真机供给未读」那条有界门的上限同一分寸：
    /// 三轮之内还看得出「上一轮判过什么」，再往后读的人只能看见一串「未验证」。
    /// </summary>
    internal const int MaxConsecutiveUnverified = 3;

    /// <summary>「从未为这一层跑过读数」的写法。</summary>
    internal const string NeverReadMarker = "未现读";

    /// <summary>
    /// 终局词形必须写全的两件事实（词形封闭，第四百二十七轮，控制仓简报 P0-1；与第四百二十六轮
    /// 供给门的「已终结」词形同一分寸）：这一层的两条现读腿各断一条——模拟器没有为它写的冒烟段
    /// （<c>没有冒烟段</c>）、真机供给已终局（<c>真机供给已终局</c>，第四百二十六轮起）——
    /// 现读在哪一天都不可能，登记行把两件事实写全时「第 4 轮必须现读」的上限不再逐轮重判。
    /// 换了说法先改这里并在当轮条目写明。
    /// </summary>
    internal static readonly string[] GapTerminalFacts = ["没有冒烟段", "真机供给已终局"];

    /// <summary>登记表里的一行（六格）。</summary>
    internal sealed record GapRow(
        string Point, string Layer, string FirstRound, string LastRead, string Streak, string Reading);

    /// <summary>
    /// 判据本体：登记表全文 + 账本全文进，问题清单出（空 = 这一页成立）。
    /// <para>
    /// 收成一处而不是写在「未验证交互点有界」那条门里，是为了让它的注入验能把每一发破坏各喂一遍——
    /// 注入验的就是仓里真跑的那一条。「README 点名屏对账」那条门的<b>行质量那一半</b>也原样走这一份，
    /// 同一张表不养两份判据。
    /// </para>
    /// </summary>
    internal static List<string> UnverifiedPointProblems(string registry, string notes)
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
            //    终局例外（第四百二十七轮起）：从未现读、且读数格里把两件终局事实（GapTerminalFacts）
            //    写全的行——模拟器没有为这一层写的冒烟段、真机供给已终局——现读在哪一天都不可能，
            //    上限不再逐轮重判（缺口照旧挂在表上，供给恢复那天照旧按现读归零）。
            if (derived > MaxConsecutiveUnverified
                && !(neverRead && GapTerminalFacts.All(f => row.Reading.Contains(f, StringComparison.Ordinal))))
            {
                problems.Add($"「{row.Point}」那一行连续未验证已到第 {derived} 轮，超过上限 "
                             + $"{MaxConsecutiveUnverified} 轮：这一轮必须现读一次（模拟器能覆盖的那一层跑模拟器、"
                             + "真机到手就跑真机），把读数写进那一行、计数归零——「一直不验」不许没有上界。"
                             + "若这一层两条腿都断了（模拟器没有为它写的冒烟段、真机供给已终局），"
                             + "按终局词形登记：在读数格里把「" + GapTerminalFacts[0] + "」「"
                             + GapTerminalFacts[1] + "」两件事实写全，上限即不再逐轮重判。");
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
    internal static List<GapRow> GapRows(string registry)
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
    internal static Dictionary<int, string> LedgerEntries(string notes)
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
    internal static int? RoundOf(string text)
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
    internal static string Chinese(int number)
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

    // ---------- 夹具（两条门的注入验共用同一份造表法） ----------

    /// <summary>夹具：造一张登记表（表头 + 分隔线 + 这几行）。</summary>
    internal static string GapTable(params string[] rows) =>
        "| 交互点 | 未验证的是哪一层 | 首次登记轮次 | 最近一次现读轮次 | 连续未验证轮次 | 现读读数 |\n"
        + "| --- | --- | --- | --- | --- | --- |\n"
        + string.Concat(rows.Select(r => r + "\n"));

    /// <summary>夹具：造登记表里的一行。</summary>
    internal static string GapRowLine(
        string point, string layer, string first, string lastRead, string streak, string reading) =>
        $"| {point} | {layer} | {first} | {lastRead} | {streak} | {reading} |";

    /// <summary>夹具：造一份「这几轮都有条目」的账本。</summary>
    internal static string GapLedger(params (int Round, string Body)[] entries) =>
        string.Concat(entries.Select(e => $"## 第{Chinese(e.Round)}轮（2026-11-20）：夹具条目\n\n{e.Body}\n\n"));
}
