using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 真机证据归档的机械门（第一百七十八轮，控制仓简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：G-D5 与 SHARED-3/4/7/8 是验收表上唯一剩下的硬缺口，缺的只有同一件事——机器与专用测试项目。
/// 第一百六十八轮把「第一天跑什么」写成了 <c>docs/NOTES.md</c> 的动作单，但那张单子仍是<b>人读的登记</b>：
/// 真机到位那天还得靠人现场判断「四站走完了吗」「三档截图齐了吗」「混合 DPI 量了哪三个数」。
/// 而第一百七十四轮已经证明「发布能跑 ≠ 双击能用」——现场判断正是最容易把「跑了一半」读成「跑完了」的那一步。
/// </para>
/// <para>
/// <b>这一轮补的是哪一层</b>：动作单说「跑什么」，<c>docs/evidence/real-machine/README.md</c> 说「每个目录里该有
/// 哪几个文件、每个文件里该有哪几个数字、缺哪个数就算没跑」。本文件把这两层钉在一起，判三件事：
/// ① 归档子目录两边同名（动作单点到的目录，README 必须逐个点到；README 点到的目录，动作单里必须有）；
/// ② 动作单 7 行「量什么」的每一项，都被 README 对应行的「量测项」列点到（改一处漏一处即红）；
/// ③ 勾成 <c>[x]</c> 的那一条，其归档目录里必须真有文件（防「只跑一半就说 G-D5 过」）。
/// </para>
/// <para>
/// <b>第一百七十九轮补的两个盲区</b>（简报 P0-2）：① <c>&lt;dpi&gt;</c> 占位符的展开改成按封闭清单
/// <see cref="DpiTiers"/> 做，并单独判「两格都逐档点了名」——原来从同一格里抓数字，两边同时删掉一档是绿的；
/// ② 量测项列的分隔符从只认顿号改成顿号 / 斜杠 / 分号都认——只认顿号时，把那一列改写成斜杠分隔，
/// 整格会被当成一项，门就变成了摆设。
/// </para>
/// <para>
/// <b>它不判什么（这一条比判什么更重要）</b>：<b>不判真机有没有跑</b>。本机没有 Windows 图形子系统，
/// 跑没跑只有人知道。门只保证「不许半勾、不许把没归档的说成归档了」——<b>有门不等于过了</b>，
/// G-D5 在验收表上仍是 <c>[ ]</c>（同一纪律见 <see cref="WordingHonestyTests"/> 与
/// <see cref="AcceptanceChecklistTests"/>）。
/// </para>
/// </summary>
public class RealMachineEvidenceTests
{
    private const string ActionListHeading = "## G-D5 真机复核清单（可勾选）";

    private const string ReadmeRelative = "docs/evidence/real-machine/README.md";

    /// <summary>归档根：真机证据的落点。目录里除这份 README 之外什么都没有，就是「未达成」。</summary>
    private static string EvidenceRoot => Path.Combine(Repo.Root, "docs", "evidence", "real-machine");

    private static string ReadmePath => Path.Combine(Repo.Root, "docs", "evidence", "real-machine", "README.md");

    /// <summary>
    /// 归档子目录的地板清单（封闭的）：动作单 7 条各自点到的那几个目录。
    /// <para>
    /// 写成字面量而不是从文档里推，是为了挡住「两边同时删掉一个目录」——那种改法在「两边同名」这条判据下是绿的，
    /// 而少一个目录就是少一条动作的落点。加一条动作要同时改这里、动作单与 README，三处都改才算过。
    /// </para>
    /// </summary>
    private static readonly string[] ExpectedDirectories =
        ["journey", "100", "125", "200", "mixed-dpi", "scale-change", "fonts", "apifox-write", "smoke"];

    /// <summary>动作单的一行：勾选 / 动作 / 量什么 / 归档到哪里 / 判定标准。</summary>
    private readonly record struct ActionRow(string Check, string Action, string Measure, string Archive, string Standard)
    {
        /// <summary>动作序号（「1. 单文件发布…」里的 1）。</summary>
        public int Number => int.Parse(Regex.Match(Action, @"^(\d+)\.").Groups[1].Value);
    }

    /// <summary>README 产出物清单的一行：动作 / 归档目录 / 产出物 / 必须有的数字 / 什么算过 / 量测项。</summary>
    private readonly record struct ArtifactRow(int Action, string DirCell, string Files, string Numbers, string Pass, string Keys);

    /// <summary>动作单那一节的正文行（标题行到下一个 <c>## </c> 之前）。</summary>
    private static string[] ActionListLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == ActionListHeading);
        Assert.True(start >= 0, $"docs/NOTES.md 缺「{ActionListHeading}」一节（真机到位后第一天的动作单）。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<ActionRow> ActionRows()
    {
        var rows = new List<ActionRow>();
        foreach (var line in ActionListLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells[0] != "[x]" && cells[0] != "[ ]") continue;
            Assert.True(cells.Count == 5,
                $"动作单有一行是勾选框却不是 5 列（实际 {cells.Count} 列）：{cells[1]}。格式错的行不许被解析器静默跳过。");
            rows.Add(new ActionRow(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }

        return rows;
    }

    private static string ReadmeText()
    {
        Assert.True(File.Exists(ReadmePath),
            $"缺 {ReadmeRelative}：真机到位当天第一份要读的文件不在，动作单的「产出物清单」就没有落点。");
        return File.ReadAllText(ReadmePath);
    }

    private static List<ArtifactRow> ArtifactRows()
    {
        var rows = new List<ArtifactRow>();
        foreach (var line in ReadmeText().Split('\n'))
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count == 0 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            Assert.True(cells.Count == 6,
                $"README 产出物清单有一行不是 6 列（实际 {cells.Count} 列，应为 动作 / 归档目录 / 产出物 / 必须有的数字 / 什么算过 / 量测项）：{cells[0]}。");
            rows.Add(new ArtifactRow(int.Parse(cells[0]), cells[1], cells[2], cells[3], cells[4], cells[5]));
        }

        return rows;
    }

    /// <summary>
    /// 三档 DPI 的归档目录名（封闭的，第一百七十九轮，简报 P0-2）。
    /// <para>
    /// 动作单第 2 条与 README 第 2 行都必须逐档点到这三个名字。写成字面量而不是从文档里抓数字，
    /// 是因为「抓」这条路有一个盲区：把 <c>125/</c> 从两格里同时删掉，抓出来的集合就只剩两档，
    /// 而「两边同名」那条判据照样绿——<b>三档缺一档，真机当天就少截一套</b>，门却一声不响。
    /// </para>
    /// </summary>
    private static readonly string[] DpiTiers = ["100", "125", "200"];

    /// <summary>
    /// 一个单元格里点到的归档子目录名。
    /// <para>
    /// 动作单第 2 条写的是 <c>&lt;dpi&gt;/</c> 加「按档位分目录（<c>100/</c>、<c>125/</c>、<c>200/</c>）」，
    /// 所以占位符要展开——不展开的话「三档」在目录这一层就只剩一个假名字。
    /// <b>展开按 <see cref="DpiTiers"/> 这份封闭清单做，不从同一格里再抓一遍数字</b>：
    /// 抓的话「把 <c>125/</c> 删掉」会让 125 这一档从目录集合里静默消失，而两边同名照样绿。
    /// 文档里到底写没写这三档，由 <see cref="DpiPlaceholder_ExpandsToAllThreeTiersAndBothCellsNameThem"/> 单独判。
    /// </para>
    /// </summary>
    private static List<string> DirectoriesIn(string cell)
    {
        var names = new List<string>();
        foreach (Match m in Regex.Matches(cell, @"docs/evidence/real-machine/([^/`\s]+)/"))
        {
            var name = m.Groups[1].Value;
            if (name == "<dpi>")
            {
                names.AddRange(DpiTiers);
                continue;
            }

            names.Add(name);
        }

        return names;
    }

    /// <summary>
    /// README 在、且把「空目录 = 未达成」与 7 行产出物清单写全了。
    /// <para>
    /// 判据是结构不是措辞：7 行、行号 1–7 各一次、每行的「产出物 / 必须有的数字 / 什么算过」都不许空着。
    /// 空着的那一格正是真机当天要现场编的东西——现场编出来的判据，等于没有判据。
    /// </para>
    /// </summary>
    [Fact]
    public void Readme_ExistsAndSaysAnEmptyDirectoryMeansUnmet()
    {
        var text = ReadmeText();

        // ① 空目录 = 未达成：这一句是整份文件的判据底座，删掉它，下面那张表就只是「建议放这里」。
        Assert.Contains("空目录 = 未达成", text, StringComparison.Ordinal);

        // ② 7 行，行号 1–7 各一次（与动作单 7 条一一对应）。
        var rows = ArtifactRows();
        Assert.Equal(Enumerable.Range(1, 7), rows.Select(r => r.Action).OrderBy(n => n));

        // ③ 每行的产出物 / 数字 / 判据都不许空着。
        foreach (var row in rows)
        {
            Assert.True(row.Files.Length >= 8, $"README 第 {row.Action} 行没写产出物（文件名模式）：真机当天不知道往目录里放什么。");
            Assert.True(row.Numbers.Length >= 8, $"README 第 {row.Action} 行没写「每个文件里必须有的数字」：只有截图没有数字，等于没量。");
            Assert.True(row.Pass.Length >= 8, $"README 第 {row.Action} 行没写「什么算过」：现场只能凭感觉判。");
            Assert.True(DirectoriesIn(row.DirCell).Count > 0, $"README 第 {row.Action} 行没点到归档目录。");
        }

        // ④ 归档根必须写全路径：只写「journey/」的话，真机当天没人知道它相对谁。
        Assert.Contains("docs/evidence/real-machine/", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// 归档子目录两边同名：动作单点到的，README 必须逐个点到；README 点到的，动作单里必须有。
    /// <para>
    /// 这一条挡的是「只跑一半就说 G-D5 过」的前半截——动作单第 3 条要 <c>mixed-dpi/</c>，README 却只写了三档目录，
    /// 那天跑完三档截图就会以为齐了。反向也挡：README 自己发明一个动作单里没有的目录，等于多出一条没人判的动作。
    /// </para>
    /// </summary>
    [Fact]
    public void Readme_CoversEveryArchiveDirectoryTheActionListNames()
    {
        var fromActionList = ActionRows().SelectMany(r => DirectoriesIn(r.Archive)).ToHashSet(StringComparer.Ordinal);
        var fromReadme = ArtifactRows().SelectMany(r => DirectoriesIn(r.DirCell)).ToHashSet(StringComparer.Ordinal);

        // ① 地板清单：两边同时删掉一个目录时，「两边同名」是绿的，所以地板要单独钉。
        foreach (var expected in ExpectedDirectories)
        {
            Assert.True(fromActionList.Contains(expected),
                $"动作单里已经没有归档目录 {expected}/ 了——少一个目录就是少一条动作的落点（地板清单在 {nameof(ExpectedDirectories)}）。");
            Assert.True(fromReadme.Contains(expected),
                $"README 的产出物清单没点到 {expected}/：动作单要归档到这里，README 不说，真机当天就不会建这个目录。");
        }

        // ② 两边同名（多出来的那一侧即红）。
        var onlyInActionList = fromActionList.Except(fromReadme, StringComparer.Ordinal).ToList();
        Assert.True(onlyInActionList.Count == 0,
            "这些归档目录动作单点了、README 没点：" + string.Join(" ／ ", onlyInActionList.Select(d => d + "/")));
        var onlyInReadme = fromReadme.Except(fromActionList, StringComparer.Ordinal).ToList();
        Assert.True(onlyInReadme.Count == 0,
            "这些归档目录 README 点了、动作单里没有：" + string.Join(" ／ ", onlyInReadme.Select(d => d + "/")));
    }

    /// <summary>
    /// 三档 DPI 的归档目录：占位符展开后三档都在，且两格都逐档点了名（第一百七十九轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个盲区</b>：第一百七十八轮的 <see cref="DirectoriesIn"/> 遇到 <c>&lt;dpi&gt;</c> 时，
    /// 是从<b>同一格里再抓一遍</b> <c>`100/`</c> 这类记号来展开的。于是「把 <c>125/</c> 从动作单与 README
    /// 两格里同时删掉」这种改法在「两边同名」那条判据下是绿的——两边都少一档，集合仍然相等。
    /// 而三档缺一档，真机当天就少截一套，G-D5 的「三档都无裁切」也就无从判起。
    /// </para>
    /// <para>
    /// <b>判两件事</b>：① 展开按封闭清单 <see cref="DpiTiers"/> 做，三档各至少出现一次（不靠抓数字）；
    /// ② 动作单第 2 条与 README 第 2 行<b>都逐档写了</b> <c>`100/`</c>、<c>`125/`</c>、<c>`200/`</c>——
    /// 占位符展开是代码的事，文档里写不写才是人读得到的事。删掉任意一档即红。
    /// </para>
    /// </summary>
    [Fact]
    public void DpiPlaceholder_ExpandsToAllThreeTiersAndBothCellsNameThem()
    {
        var actionRow = ActionRows().Single(r => r.Number == 2);
        var readmeRow = ArtifactRows().Single(r => r.Action == 2);

        // ① 动作单第 2 条用的是占位符（README 第 2 行按档位写全名，不用占位符）。
        Assert.Contains("<dpi>", actionRow.Archive, StringComparison.Ordinal);

        // ② 展开后三档各至少一次：占位符不许退化成「有几个数字算几个」。
        var expanded = DirectoriesIn(actionRow.Archive);
        foreach (var tier in DpiTiers)
        {
            Assert.True(expanded.Contains(tier),
                $"动作单第 2 条的 <dpi>/ 展开后没有 {tier}/ 这一档（展开清单在 {nameof(DpiTiers)}）：三档缺一档，真机当天就少截一套。");
        }

        // ③ 两格都必须逐档点名。两种写法都认：动作单写「按档位分目录（`100/`、`125/`、`200/`）」，
        //    README 写全路径「`docs/evidence/real-machine/100/`」——判的是「这一档有没有被点到」，不是写法。
        foreach (var (label, cell) in new[]
                 {
                     ("动作单第 2 条", actionRow.Archive),
                     ("README 第 2 行", readmeRow.DirCell),
                 })
        {
            var named = NamedDpiTiers(cell);
            foreach (var tier in DpiTiers)
            {
                Assert.True(named.Contains(tier),
                    $"{label}没有逐档点到 {tier}/：三档 DPI 是 G-D5 的判据本体，少写一档，真机当天就少截一套。");
            }

            // ④ 反向：不许出现清单外的档位（例如 150/）——多一档就是多一套没人判的截图。
            var extra = named.Where(n => !DpiTiers.Contains(n, StringComparer.Ordinal)).ToList();
            Assert.True(extra.Count == 0,
                $"{label}点了清单外的档位：" + string.Join(" ／ ", extra.Select(n => n + "/"))
                + "。三档是封闭的（" + string.Join(" / ", DpiTiers) + "），多一档就是多一套没人判的截图。");
        }
    }

    /// <summary>
    /// 一个单元格里逐档点到的 DPI 档位名。两种写法都认：`` `100/` ``（动作单的「按档位分目录」）
    /// 与 `` `docs/evidence/real-machine/100/` ``（README 的全路径）。
    /// </summary>
    private static List<string> NamedDpiTiers(string cell) =>
        Regex.Matches(cell, @"(?:docs/evidence/real-machine/|`)(\d+)/")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 动作单每行「量什么」的每一项，都被 README 对应行的「量测项」列点到。
    /// <para>
    /// 这是「登记与产出物对得上」的核心：动作单说「量客户区宽高、文字宽 / 可用宽、横向滚动条」，
    /// README 就必须逐个点出这几个数——<b>改一处漏一处即红</b>。只判「两边都有字」是不够的：
    /// 那种判据下把「量什么」整格删空、或把 README 的量测项换成一句「按动作单量」，两边照样绿。
    /// </para>
    /// </summary>
    [Fact]
    public void Readme_MeasureKeys_AreAllNamedByTheActionListRow()
    {
        var actions = ActionRows().ToDictionary(r => r.Number);
        var rows = ArtifactRows();
        var total = 0;

        foreach (var row in rows)
        {
            Assert.True(actions.ContainsKey(row.Action), $"README 第 {row.Action} 行在动作单里没有对应条目。");
            // 分隔符按「顿号 / 斜杠 / 分号」三种都认（第一百七十九轮，简报 P0-2）：
            // 只认顿号的话，把量测项列改写成「客户区宽高 / 文字宽 / 可用宽」这种更常见的写法，
            // 整格会被当成**一项**，而「这一项在动作单里有没有」照样能对上——门就变成了摆设。
            var keys = row.Keys.Split(['、', '/', '；', ';'])
                .Select(k => k.Trim().Trim('`', '*', ' '))
                .Where(k => k.Length > 0)
                .ToList();
            Assert.True(keys.Count >= 2,
                $"README 第 {row.Action} 行的「量测项」只点了 {keys.Count} 项：一项的量测清单等于没有清单。");
            total += keys.Count;

            var measure = actions[row.Action].Measure;
            var missing = keys.Where(k => !measure.Contains(k, StringComparison.Ordinal)).ToList();
            Assert.True(missing.Count == 0,
                $"README 第 {row.Action} 行点了这些量测项，动作单该行「量什么」里却没有："
                + string.Join(" ／ ", missing) + "。两边改一处漏一处即红——先对齐，再跑真机。");
        }

        // 地板：7 行合计至少 14 项（当前 24 项）。删到只剩每行两项说明清单被删残了。
        Assert.True(total >= 14, $"README 的量测项合计只有 {total} 项（地板 14）：清单被删残了。");
    }

    /// <summary>
    /// 归档根下的子目录：要么一个都不存在，要么每一个都被 README 逐个点到。
    /// <para>
    /// 真机当天最常见的半成品形态是「随手建了个目录、名字和 README 不一样」——那种目录既不会被判据看见，
    /// 也不会被下一个人找到。这条门把「归档了一个 README 没点到的目录」判红。
    /// </para>
    /// </summary>
    [Fact]
    public void ArchiveDirectories_AreEitherAllAbsentOrAllNamedByTheReadme()
    {
        var present = Directory.Exists(EvidenceRoot)
            ? Directory.GetDirectories(EvidenceRoot).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList()
            : [];
        if (present.Count == 0) return; // 真机 0 台：一个子目录都没有，正是当前状态。

        var fromReadme = ArtifactRows().SelectMany(r => DirectoriesIn(r.DirCell)).ToHashSet(StringComparer.Ordinal);
        var orphans = present.Where(d => !fromReadme.Contains(d)).ToList();
        Assert.True(orphans.Count == 0,
            "归档根下有 README 没点到的子目录：" + string.Join(" ／ ", orphans.Select(d => d + "/"))
            + "。要么按 README 的目录名归档，要么先改 README——两套名字等于两套判据。");
    }

    /// <summary>
    /// 勾成 <c>[x]</c> 的那一条，其归档目录里必须真有文件（负向门）。
    /// <para>
    /// 这是「不许半勾」的落点：动作单 7 行现在全是 <c>[ ]</c>，归档根里除这份 README 之外什么都没有。
    /// 谁把任一行改成 <c>[x]</c> 而目录里没有文件，这条就红——<b>wine 的证据不能替真机打勾</b>，
    /// 「我记得跑过」也不能。
    /// </para>
    /// </summary>
    [Fact]
    public void CheckedAction_RequiresItsArchiveDirectoryToHoldFiles()
    {
        foreach (var row in ActionRows().Where(r => r.Check == "[x]"))
        {
            var dirs = DirectoriesIn(row.Archive);
            Assert.True(dirs.Count > 0, $"动作单第 {row.Number} 条勾成达成，却没写归档目录：勾了哪一条，就要能指出东西在哪。");
            foreach (var dir in dirs)
            {
                var full = Path.Combine(EvidenceRoot, dir);
                var files = Directory.Exists(full) ? Directory.GetFiles(full, "*", SearchOption.AllDirectories) : [];
                Assert.True(files.Length > 0,
                    $"动作单第 {row.Number} 条勾成 [x]，但 docs/evidence/real-machine/{dir}/ 里一个文件都没有。"
                    + "空目录 = 未达成：先归档产出物，再勾。");
            }
        }
    }

    /// <summary>
    /// 到期日 2026-11-20 那天「谁在何时决定、回写到哪一行、由哪条断言核回写」三行必须在
    /// （第一百八十轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：上面那几条门判的是「登记与产出物对得上」，但它们与验收表上那个勾选框之间
    /// <b>没有机械联动</b>——真机到位那天，只要往 <c>journey/</c> 丢一张无关截图，
    /// <see cref="CheckedAction_RequiresItsArchiveDirectoryToHoldFiles"/> 就绿，而 G-D5 那一行该不该勾
    /// 仍要人现场判。反过来，到期日（2026-11-20）到了机器还没来，此前只有一句「按降级答辩脚本逐屏答辩」，
    /// <b>那天做什么、做完写哪里，是口头约定</b>。两种读法都错：把门绿读成「已经达成」，或把到期日读成
    /// 「还差什么说不清」。
    /// </para>
    /// <para>
    /// <b>判什么</b>：动作单那一节的 blockquote 里必须同时有 ① 到期日与「降级」两个关键词；
    /// ② 三行结构（<c>①</c> 谁/何时、<c>②</c> 回写哪里、<c>③</c> 哪条断言核回写）各被点到一次；
    /// ③ 回写落点点名两处（<c>docs/NOTES.md</c> 的 G-D5 行与 <c>README.md</c> 的已知限制）——
    /// 只写一处等于另一处会漂；④ 核回写的那条断言被点名，且它真的存在（就是本方法，自指是刻意的：
    /// 改掉那一行而不同时改断言，这条门自己就红）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机跑没跑</b>（本机没有 Windows 图形子系统，跑没跑只有人知道），
    /// 也<b>不判到期日那天到底选了哪一支</b>——那要等 2026-11-20 当天由人判定后回写。
    /// 这条门只保证「到期日动作不是口头约定」：三行被删掉、被改成一句话、或回写落点少一处，即红。
    /// </para>
    /// </summary>
    [Fact]
    public void DueDate_HasADecisionAction()
    {
        var blockquote = string.Join('\n', ActionListLines()
            .Where(l => l.TrimStart().StartsWith(">", StringComparison.Ordinal)));

        // ① 到期日与降级路径两个关键词都在：这一节此前只写了「到期日 2026-11-20、责任人见降级答辩脚本」，
        //    没有「那天做什么」。
        Assert.Contains("2026-11-20", blockquote, StringComparison.Ordinal);
        Assert.Contains("降级", blockquote, StringComparison.Ordinal);

        // ② 三行结构：谁/何时、回写哪里、哪条断言核回写，各被点到一次。
        foreach (var marker in new[] { "①", "②", "③" })
        {
            Assert.True(blockquote.Contains(marker, StringComparison.Ordinal),
                $"动作单那一节的 blockquote 里没有「{marker}」那一行：到期日动作必须是三行（谁/何时、回写哪里、哪条断言核回写），"
                + "写成一句话就又回到「口头约定」。");
        }

        // ③ 回写落点必须点名两处：只写一处，另一处就会漂成第二套说法。
        Assert.True(blockquote.Contains("docs/NOTES.md", StringComparison.Ordinal),
            "到期日动作没点名回写到 docs/NOTES.md 的 G-D5 行：判定结果不落到验收表那一行，读表的人看不到。");
        Assert.True(blockquote.Contains("README.md", StringComparison.Ordinal),
            "到期日动作没点名回写到 README.md 的已知限制：缺口公开声明的锚点在那儿，只改验收表等于两套说法。");

        // ④ 核回写的那条断言必须点名，且它真的存在（自指：改掉那一行而不同时改断言，这条门自己就红）。
        var self = $"{nameof(RealMachineEvidenceTests)}.{nameof(DueDate_HasADecisionAction)}";
        Assert.True(blockquote.Contains(self, StringComparison.Ordinal),
            $"到期日动作没点名「由哪条断言核回写」（应写 {self}）：没有断言核的回写路径，等于没有回写路径。");
    }

    /// <summary>
    /// 动作单那一节点名这道门，并写清它<b>不判真机有没有跑</b>。
    /// <para>
    /// 存在理由：下一轮读到「G-D5 有机械门了」很容易顺手把「有门」读成「过了」。这一句必须写在动作单里
    /// （不是只写在测试注释里），因为读动作单的人不会去读测试文件。
    /// </para>
    /// </summary>
    [Fact]
    public void Notes_NamesThisGateAndSaysWhatItDoesNotJudge()
    {
        var section = string.Join('\n', ActionListLines());
        Assert.Contains("RealMachineEvidenceTests", section, StringComparison.Ordinal);
        Assert.Contains("不判真机有没有跑", section, StringComparison.Ordinal);
        // 真机当天第一份要读的是那份 README，动作单必须指过去（否则它藏在 evidence 目录里没人找得到）。
        Assert.Contains(ReadmeRelative, section, StringComparison.Ordinal);
    }

    /// <summary>「到期日当天执行单」那一页的标题前缀（三级标题，落在 G-D5 动作单那一节之内）。</summary>
    private const string RunSheetHeading = "### 到期日当天执行单";

    /// <summary>执行单那一页的正文行（标题行到下一个二级/三级标题之前）。</summary>
    private static string[] RunSheetLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(RunSheetHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{RunSheetHeading}」这一页：到期日当天要跑什么命令、读哪份文件、"
            + "写回哪一行，就又散回三处（三行 blockquote、降级答辩脚本、归档 README），现场翻文档 = 出错。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>
    /// 执行单的三步：按行首 <c>1. </c> / <c>2. </c> / <c>3. </c> 切块，每块含该步的标题行与下面三行。
    /// </summary>
    private static List<(int Number, string Text)> RunSheetSteps()
    {
        var steps = new List<(int, string)>();
        var buffer = new System.Text.StringBuilder();
        var number = 0;
        foreach (var line in RunSheetLines())
        {
            var m = Regex.Match(line, @"^(\d+)\.\s");
            if (m.Success)
            {
                if (number > 0) steps.Add((number, buffer.ToString()));
                number = int.Parse(m.Groups[1].Value);
                buffer.Clear();
            }

            if (number > 0) buffer.AppendLine(line);
        }

        if (number > 0) steps.Add((number, buffer.ToString()));
        return steps;
    }

    /// <summary>
    /// 一段文字里有没有引到<b>仓内实物</b>：反引号里的一条命令（其脚本/路径在仓里）、一个文件或目录路径、
    /// 或一个真的存在的断言名（<c>XxxTests.Method</c>）。
    /// <para>
    /// 判「引到实物」而不是「有反引号」，是因为「跑一下脚本」「看那份 README」这种写法在反引号里也成立，
    /// 而真机当天要的是<b>能照着敲的那一条</b>。
    /// </para>
    /// </summary>
    private static bool CitesRealArtifact(string text)
    {
        foreach (Match m in Regex.Matches(text, "`([^`]+)`"))
        {
            var span = m.Groups[1].Value.Trim();

            // 断言名：XxxTests.Method —— 方法名必须真的在测试工程里（写错一个字母即红）。
            var assertion = Regex.Match(span, @"^(\w+Tests)\.(\w+)$");
            if (assertion.Success && AssertionExists(assertion.Groups[1].Value, assertion.Groups[2].Value)) return true;

            // 命令或路径：按空白与管道切开，逐段看是不是仓内实物。
            foreach (var token in span.Split([' ', '|', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
            {
                var path = token.Trim();
                if (path.Length == 0 || path.StartsWith('-')) continue;
                if (!path.Contains('/') && !path.EndsWith(".md", StringComparison.Ordinal)
                    && !path.EndsWith(".sh", StringComparison.Ordinal) && !path.EndsWith(".cs", StringComparison.Ordinal))
                {
                    continue;
                }

                var full = Path.Combine(Repo.Root, path.TrimEnd('/'));
                if (File.Exists(full) || Directory.Exists(full)) return true;
            }
        }

        return false;
    }

    /// <summary>测试工程里有没有这个断言方法（按类名找文件、按方法名找声明）。</summary>
    private static bool AssertionExists(string className, string methodName)
    {
        var file = Path.Combine(Repo.Root, "ApifoxHelper.Tests", className + ".cs");
        return File.Exists(file)
            && Regex.IsMatch(File.ReadAllText(file), $@"\b{Regex.Escape(methodName)}\s*\(");
    }

    /// <summary>
    /// 到期日当天「5 分钟走得完」的执行单：三步各被点到一次、每步一行命令 / 一行读什么 / 一行写回哪、
    /// 每步至少引一条仓内实物、第 3 步的两处回写落点与三行动作 ② 逐字一致（第一百八十一轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百八十轮把「谁在何时决定、回写到哪一行、由哪条断言核回写」三行落成了
    /// （<see cref="DueDate_HasADecisionAction"/>），但<b>当天要跑什么命令、读哪份文件、写回哪一行仍散在三处</b>
    /// （本节开头的三行 blockquote、<c>docs/NOTES.md</c> 的「降级答辩脚本」、<c>docs/evidence/real-machine/README.md</c>）。
    /// 到期日当天人的注意力有限，散在三处 = 现场翻文档 = 出错。这一页把三步收成一条线。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 三步编号 1 / 2 / 3 各一次（少一步或多一步即红）；② 每步都有「命令：」「读什么：」
    /// 「写回哪：」三行（缺一行，那一步就只剩一句口号）；③ 每步至少引一条仓内实物——命令的脚本、要读的文件、
    /// 或核回写的断言名，且它<b>真的在仓里</b>（写错路径即红）；④ 第 3 步点名的两处回写落点
    /// （<c>G-D5 行「当前结论」格</c> 与 <c>「已知限制」第 1 条</c>）与三行动作 ② 的措辞<b>逐字一致</b>——
    /// 两处措辞漂开，就是两套说法。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判当天选哪一支</b>（「按降级路径执行」还是「延后至 &lt;日期&gt;」要人判），
    /// <b>不判真机跑没跑</b>，也<b>不判 G-D5 算不算过</b>。它只保证「当天 5 分钟内走得完、写回不漂」。
    /// </para>
    /// </summary>
    [Fact]
    public void DueDateRunSheet_HasThreeStepsEachAnchoredToARealArtifact()
    {
        var steps = RunSheetSteps();

        // ① 三步各一次：编号必须正好是 1 / 2 / 3（多一步是没人判的动作，少一步是断链）。
        Assert.Equal([1, 2, 3], steps.Select(s => s.Number));

        // ② 每步三行 + ③ 每步至少引一条仓内实物。
        foreach (var (number, text) in steps)
        {
            foreach (var label in new[] { "命令：", "读什么：", "写回哪：" })
            {
                Assert.True(text.Contains(label, StringComparison.Ordinal),
                    $"执行单第 {number} 步没有「{label}」那一行：每步要有一行命令、一行读什么、一行写回哪，"
                    + "缺一行那一步就只剩一句口号，当天仍要现场想。");
            }

            Assert.True(CitesRealArtifact(text),
                $"执行单第 {number} 步一条仓内实物都没引（命令的脚本 / 要读的文件 / 核回写的断言名，且它真的在仓里）："
                + "「跑一下脚本」「看那份 README」这种写法当天照着敲不出来。");

            // ③ 的加强版：**那一行命令本身**要指到仓内实物。整步引到不算——「读什么」那行引到文件、
            //    命令那行写错一个路径，当天照着敲就是一条空命令，而整步判据照样绿。
            //    只判「命令：」到第一个全角括号之前的那一段——括号里是解释，解释里提到 `README.md`
            //    不该把一条写错路径的命令救活。
            var commandLine = text.Split('\n').FirstOrDefault(l => l.Contains("命令：", StringComparison.Ordinal));
            var command = commandLine is null
                ? string.Empty
                : commandLine[(commandLine.IndexOf("命令：", StringComparison.Ordinal) + "命令：".Length)..];
            var paren = command.IndexOf('（');
            if (paren >= 0) command = command[..paren];
            Assert.True(CitesRealArtifact(command),
                $"执行单第 {number} 步的「命令：」那一行没指到仓内实物（脚本 / 目录 / 文件，且它真的在仓里）："
                + "当天照着敲的命令必须能敲通，写错一个路径就是一条空命令。");
        }

        // ④ 第 3 步的两处回写落点必须与三行动作 ② 逐字一致。
        var blockquote = string.Join('\n', ActionListLines()
            .Where(l => l.TrimStart().StartsWith(">", StringComparison.Ordinal)));
        var step3 = steps.Single(s => s.Number == 3).Text;
        foreach (var anchor in new[] { "G-D5 行「当前结论」格", "「已知限制」第 1 条" })
        {
            Assert.True(blockquote.Contains(anchor, StringComparison.Ordinal),
                $"三行动作 ② 里没有「{anchor}」这个回写落点：回写路径的措辞在那一行定，执行单照抄它。");
            Assert.True(step3.Contains(anchor, StringComparison.Ordinal),
                $"执行单第 3 步没点名「{anchor}」：两处回写落点少一处，判定结果就只落一半，另一处漂成第二套说法。");
        }

        // ⑤ 到期日与「不判哪一支」的诚实口径都在这一页上（读这一页的人不必再翻回上面）。
        var sheet = string.Join('\n', RunSheetLines());
        Assert.Contains("2026-11-20", sheet, StringComparison.Ordinal);
        Assert.Contains("不判", sheet, StringComparison.Ordinal);
    }

    // ---------- 第一百八十七轮（简报 P0-1）：真机动作单「照抄即可」化 ----------

    /// <summary>「照抄即可」三列那一节的标题前缀（三级标题，落在 G-D5 动作单那一节之内）。</summary>
    private const string RunbookHeading = "### 七条动作的「照抄即可」三列";

    /// <summary>三列小节的一行：动作序号 / 量什么数 / 数写在哪个控件上 / 归档文件名。</summary>
    private readonly record struct RunbookRow(int Action, string Measure, string Slots, string Files);

    /// <summary>三列小节那一节的正文行（标题行到下一个二级/三级标题之前）。</summary>
    private static string[] RunbookLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(RunbookHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{RunbookHeading}」这一节：真机到位当天仍要现场想"
            + "「这个数写在哪个控件上、落进哪个文件名」，而现场想出来的判据等于没有判据。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<RunbookRow> RunbookRows()
    {
        var rows = new List<RunbookRow>();
        foreach (var line in RunbookLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            var number = Regex.Match(cells[0], @"^(\d+)\.");
            if (!number.Success) continue;
            Assert.True(cells.Count == 4,
                $"「照抄即可」三列有一行不是 4 列（实际 {cells.Count} 列，应为 动作 / 量什么数 / 数写在哪个控件上 / 归档文件名）：{cells[0]}。");
            rows.Add(new RunbookRow(int.Parse(number.Groups[1].Value), cells[1], cells[2], cells[3]));
        }

        return rows;
    }

    /// <summary>
    /// 每条动作「数写在哪个控件上」的<b>预置槽位清单</b>（封闭的，第一百八十七轮，简报 P0-1）。
    /// <para>
    /// 写成字面量而不是从文档里抓，是为了挡住「自己发明一个控件名」：真机当天照着念的控件名如果只存在于
    /// 这一份文档里，量的时候眼睛不知道往哪看，而「量到了」这句话仍然写得出来。加一条动作要同时改这里、
    /// 动作单与 README，三处都改才算过。
    /// </para>
    /// </summary>
    private static readonly Dictionary<int, string[]> PresetControlSlots = new()
    {
        [1] = ["系统显示设置", "主窗口边框", "阶段条", "下一步按钮", "注释缺口计数行"],
        [2] = ["系统显示设置", "主窗口边框", "说明行", "状态行", "内容区滚动条", "底部按钮行"],
        [3] = ["系统显示设置", "主窗口边框", "说明行", "副屏可用区"],
        [4] = ["系统显示设置", "主窗口边框", "说明行"],
        [5] = ["界面控件字体", "进程默认字体", "对话框字体", "数字列"],
        [6] = ["计划预览计数行", "任务日志 journal.jsonl", "远端详情快照", "状态行"],
        [7] = ["报告首行", "段标题行", "退出码"],
    };

    /// <summary>一个单元格里反引号点到的名字（去重、保序）。</summary>
    private static List<string> Backticked(string cell) =>
        Regex.Matches(cell, "`([^`]+)`").Select(m => m.Groups[1].Value.Trim()).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// 七条动作的「照抄即可」三列：控件槽位只许点预置清单里的名字、归档文件名必须与 README 逐字一致、
    /// 量什么数把 README 的量测项一项不落地说一遍（第一百八十七轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百七十八轮把「跑什么 / 放哪里 / 什么算过」钉住了，但真机到位当天仍有两处
    /// 得现场想——<b>这个数写在界面的哪个控件上</b>、<b>这一行数落进哪个文件名</b>。前者决定量的时候眼睛往哪看，
    /// 后者决定归档时不要另起一套命名。两处都是「现场想出来的判据」，而现场想出来的判据等于没有判据。
    /// </para>
    /// <para>
    /// <b>判三件事</b>：① 每条动作的控件槽位<b>只许点 <see cref="PresetControlSlots"/> 里的名字</b>，
    /// 且清单里每一个都要被点到（发明一个控件名即红，漏掉一个槽位也即红）；
    /// ② 每条动作点到的归档文件名必须<b>逐字出现在 README 那一行的产出物清单里</b>（两边各写一套名字即红）；
    /// ③ 每条动作的「量什么数」把 README 那一行「量测项」列的每一项都说一遍（改一处漏一处即红）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机有没有跑</b>（本机没有 Windows 图形子系统），也不判这些数在真机上
    /// 到底是多少——它只保证「当天照着念的控件名与文件名，都是仓里已经定好的那一个」。
    /// </para>
    /// </summary>
    [Fact]
    public void ActionRunbook_NamesOnlyPresetControlSlotsAndRealArchiveFiles()
    {
        var rows = RunbookRows();
        Assert.Equal(Enumerable.Range(1, 7), rows.Select(r => r.Action).OrderBy(n => n));

        var artifacts = ArtifactRows().ToDictionary(r => r.Action);
        foreach (var row in rows)
        {
            // ① 控件槽位：只许点预置清单里的名字，且清单里每一个都要点到。
            var slots = Backticked(row.Slots);
            Assert.True(slots.Count >= 2,
                $"第 {row.Action} 条只点了 {slots.Count} 个控件槽位：一个槽位的「数写在哪里」等于没写。");
            var preset = PresetControlSlots[row.Action];
            var invented = slots.Where(s => !preset.Contains(s, StringComparer.Ordinal)).ToList();
            Assert.True(invented.Count == 0,
                $"第 {row.Action} 条点了预置清单外的控件名：" + string.Join(" ／ ", invented)
                + "。真机当天照着念的控件名必须是仓里定好的那一个（清单在 " + nameof(PresetControlSlots) + "）。");
            var missingSlots = preset.Where(p => !slots.Contains(p, StringComparer.Ordinal)).ToList();
            Assert.True(missingSlots.Count == 0,
                $"第 {row.Action} 条漏了这些控件槽位：" + string.Join(" ／ ", missingSlots)
                + "。漏一个槽位，那个数当天就没人知道往哪看。");

            // ② 归档文件名必须与 README 那一行逐字一致。
            var files = Backticked(row.Files);
            Assert.True(files.Count >= 1, $"第 {row.Action} 条没点归档文件名：归档时只能另起一套命名。");
            foreach (var file in files)
            {
                Assert.True(Regex.IsMatch(file, @"\.(png|json|jsonl|txt)$"),
                    $"第 {row.Action} 条点的「{file}」不是产出物文件名（应为 .png / .json / .jsonl / .txt）。");
                Assert.True(artifacts[row.Action].Files.Contains(file, StringComparison.Ordinal),
                    $"第 {row.Action} 条点的归档文件名「{file}」在 README 第 {row.Action} 行的产出物清单里找不到："
                    + "两边各写一套名字，真机当天就会归档到没人判的名字上。");
            }

            // ③ 量什么数把 README 的量测项一项不落地说一遍。
            var keys = artifacts[row.Action].Keys.Split(['、', '/', '；', ';'])
                .Select(k => k.Trim().Trim('`', '*', ' '))
                .Where(k => k.Length > 0)
                .ToList();
            var missingKeys = keys.Where(k => !row.Measure.Contains(k, StringComparison.Ordinal)).ToList();
            Assert.True(missingKeys.Count == 0,
                $"第 {row.Action} 条的「量什么数」漏了 README 点到的量测项：" + string.Join(" ／ ", missingKeys)
                + "。三列是动作单的照抄版，不是另一份清单。");
        }
    }

    // ---------- 第一百八十九轮（简报 P0-2）：DPI 变更「结果三个数」的照抄三列 ----------

    /// <summary>「DPI 变更结果三个数」那一节的标题前缀（三级标题，落在 G-D5 动作单那一节之内）。</summary>
    private const string DpiNumbersHeading = "### DPI 变更「结果三个数」的照抄三列";

    /// <summary>三个数（封闭的，顺序即归档文件里的行序）：因子 / 客户区宽 / 说明行可用宽。</summary>
    private static readonly string[] DpiChangeNumbers = ["因子", "客户区宽", "说明行可用宽"];

    /// <summary>这一节的一行：动作序号 / 数 / 量什么 / 从哪读 / 归档到哪（文件 · 字段）。</summary>
    private readonly record struct DpiNumberRow(int Action, string Number, string Measure, string Read, string Archive);

    /// <summary>这一节那一节的正文行（标题行到下一个二级/三级标题之前）。</summary>
    private static string[] DpiNumberLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(DpiNumbersHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{DpiNumbersHeading}」这一节：第 3、4 条要量的三个数"
            + "（因子 / 客户区宽 / 说明行可用宽）各自从哪读、落进归档文件的哪个字段，又得真机当天现场想。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<DpiNumberRow> DpiNumberRows()
    {
        var rows = new List<DpiNumberRow>();
        foreach (var line in DpiNumberLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count == 0 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            Assert.True(cells.Count == 5,
                $"「DPI 变更结果三个数」有一行不是 5 列（实际 {cells.Count} 列，应为 动作 / 数 / 量什么 / 从哪读 / 归档到哪）：{cells[0]}。");
            rows.Add(new DpiNumberRow(int.Parse(cells[0]), cells[1], cells[2], cells[3], cells[4]));
        }

        return rows;
    }

    /// <summary>
    /// 一个反引号记号是不是仓里真有的符号（<c>类名.成员</c>，成员按方法声明找）。
    /// <para>
    /// 只认 <c>Desktop/Native/</c> 下的类：这一节要点的符号只有 DPI 那一族（<c>DpiScale.ScaleFactor</c>），
    /// 而它们全在那一层。写成「仓里任意同名文件」会让一个拼错的类名被别处的同名文件救活。
    /// </para>
    /// </summary>
    private static bool SymbolExists(string token)
    {
        var parts = token.TrimEnd('(', ')').Split('.');
        if (parts.Length != 2) return false;
        var file = Path.Combine(Repo.Root, "Desktop", "Native", parts[0] + ".cs");
        return File.Exists(file)
            && Regex.IsMatch(File.ReadAllText(file), @"\b" + Regex.Escape(parts[1]) + @"\s*\(");
    }

    /// <summary>
    /// DPI 变更「结果三个数」的照抄三列：第 3、4 条各三个数一行不缺、三列不空、从哪读只点预置槽位或仓内符号、
    /// 归档目录与动作单同名且文件名与上一节该条一致（第一百八十九轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：动作单第 3、4 条要求量「该屏因子 / 客户区宽 / 说明行可用宽」三个数，
    /// 第一百八十七轮的「照抄即可」三列也点了「数写在哪个控件上」与「归档文件名」——但<b>这三个数各自从哪读、
    /// 落进归档文件的哪个字段</b>仍要现场想。真机当天最贵的不是量，是「量到了却不知道往哪写」：
    /// 写进 <c>mixed-dpi-读数.txt</c> 却和 README 的示例格式不一样，下一个人读不出来；或者把「因子」记成
    /// 「缩放百分比」，与仓内算式（该屏 DPI ÷ 96）不是同一口径。
    /// </para>
    /// <para>
    /// <b>判四件事</b>：① 第 3、4 条各自三个数（<see cref="DpiChangeNumbers"/>，顺序也判）一行不缺、
    /// 也不许多出第四个；② 每行三列都不许空着；③ 「从哪读」只许点 <see cref="PresetControlSlots"/> 里该条的
    /// 控件名，或仓里真有的符号（<see cref="SymbolExists"/>）——自己发明一个读数位置即红；
    /// ④ 「归档到哪」的目录必须与动作单该行点到的归档目录<b>同名</b>，文件名必须是上一节该条已点到的那个
    /// （两边各写一套名字即红），且写明字段名（<c>factor=</c> 这种）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机有没有跑</b>（本机没有 Windows 图形子系统），也不判这三个数在真机上
    /// 到底是多少——它只保证「当天照着念的读数位置与归档字段，都是仓里已经定好的那一个」。
    /// </para>
    /// </summary>
    [Fact]
    public void DpiChangeNumbers_HaveThreeColumnsAndArchiveToTheSameDirectory()
    {
        var rows = DpiNumberRows();
        var actions = ActionRows().ToDictionary(r => r.Number);
        var runbook = RunbookRows().ToDictionary(r => r.Action);

        // ① 第 3、4 条各三个数，一行不缺、不许多出第四个（顺序也判：归档文件里的行序就是这三行）。
        Assert.Equal([3, 4], rows.Select(r => r.Action).Distinct().OrderBy(n => n));
        foreach (var action in new[] { 3, 4 })
        {
            var numbers = rows.Where(r => r.Action == action).Select(r => r.Number).ToList();
            Assert.Equal(DpiChangeNumbers, numbers);
        }

        foreach (var row in rows)
        {
            // ② 三列都不许空着：空着的那一格正是真机当天要现场编的东西。
            foreach (var (label, cell) in new[]
                     {
                         ("量什么", row.Measure), ("从哪读", row.Read), ("归档到哪", row.Archive),
                     })
            {
                Assert.True(cell.Length >= 4,
                    $"第 {row.Action} 条「{row.Number}」的「{label}」格太短或空着（{cell}）：真机当天照着念不出来。");
            }

            // ③ 从哪读：只许点预置槽位清单里该条的控件名，或仓里真有的符号。
            var reads = Backticked(row.Read);
            Assert.True(reads.Count >= 1,
                $"第 {row.Action} 条「{row.Number}」的「从哪读」一个反引号记号都没有：读数位置必须是仓里定好的控件名或符号。");
            foreach (var token in reads)
            {
                var preset = PresetControlSlots[row.Action];
                Assert.True(preset.Contains(token, StringComparer.Ordinal) || SymbolExists(token),
                    $"第 {row.Action} 条「{row.Number}」的「从哪读」点了「{token}」——它既不是预置槽位清单里的控件名"
                    + "（" + string.Join(" / ", preset) + "），也不是 Desktop/Native 下真有的符号。"
                    + "真机当天照着念的读数位置必须是仓里定好的那一个。");
            }

            // ④ 归档到哪：目录与动作单同名、文件名与上一节该条一致、字段名写明。
            var dirs = DirectoriesIn(row.Archive);
            var expectedDirs = DirectoriesIn(actions[row.Action].Archive);
            Assert.True(dirs.Count > 0, $"第 {row.Action} 条「{row.Number}」的「归档到哪」没点到归档目录。");
            Assert.True(dirs.OrderBy(d => d, StringComparer.Ordinal).SequenceEqual(expectedDirs.OrderBy(d => d, StringComparer.Ordinal)),
                $"第 {row.Action} 条「{row.Number}」的归档目录（" + string.Join(" ／ ", dirs.Select(d => d + "/"))
                + "）与动作单该行点到的（" + string.Join(" ／ ", expectedDirs.Select(d => d + "/"))
                + "）不同名：两套目录名，真机当天就会归档到没人判的地方。");

            var files = Backticked(row.Archive).Where(f => f.EndsWith(".txt", StringComparison.Ordinal)).ToList();
            Assert.True(files.Count == 1,
                $"第 {row.Action} 条「{row.Number}」的「归档到哪」必须点到一个 .txt 读数文件（实际 {files.Count} 个）："
                + "三个数落在同一个读数文件里，不另起一套命名。");
            // 这一节写全路径（照抄时不用再拼目录），上一节写裸文件名——按文件名比，不按写法比。
            var fileName = Path.GetFileName(files[0]);
            Assert.True(runbook[row.Action].Files.Contains(fileName, StringComparison.Ordinal),
                $"第 {row.Action} 条「{row.Number}」点的归档文件「{fileName}」在上一节该条的归档文件名里找不到："
                + "两边各写一套名字，真机当天就会归档到没人判的名字上。");

            Assert.True(Regex.IsMatch(row.Archive, @"`[a-z][a-z-]*=`"),
                $"第 {row.Action} 条「{row.Number}」的「归档到哪」没写明字段名（形如 `factor=`）："
                + "只写文件名不写字段，三个数挤在一个文件里，下一个人读不出哪个是哪个。");
        }
    }

    /// <summary>「首程四站逐站预期」那一节的标题前缀（二级标题，落在归档 README 里）。</summary>
    private const string JourneyHeading = "## 首程四站逐站预期";

    /// <summary>四站表的一行：站 / 读哪一行 / 看哪个控件 / 点到哪一步 / 预期屏幕主张 / 归档文件名。</summary>
    private readonly record struct JourneyRow(string Station, string Read, string Control, string Step, string Claim, string File);

    private static string[] JourneyLines()
    {
        var lines = ReadmeText().Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(JourneyHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"缺「{JourneyHeading}」一节：产出物清单第 1 行只说「四站各一张截图」，真机当天仍要现场设计"
            + "「每站读哪一行、点哪颗按钮、屏幕上该出现什么」。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    private static List<JourneyRow> JourneyRows()
    {
        var rows = new List<JourneyRow>();
        foreach (var line in JourneyLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells[0] is not ("一" or "二" or "三" or "四")) continue;
            Assert.True(cells.Count == 6,
                $"四站表有一行不是 6 列（实际 {cells.Count} 列，应为 站 / 读哪一行 / 看哪个控件 / 点到哪一步 / 预期屏幕主张 / 归档文件名）：{cells[0]}。");
            rows.Add(new JourneyRow(cells[0], cells[1], cells[2], cells[3], cells[4], cells[5]));
        }

        return rows;
    }

    /// <summary>
    /// 首程四站逐站预期：四站各一行、每格不空、归档文件名与产出物清单第 1 行逐字一致
    /// （第一百八十七轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：产出物清单第 1 行说「四站各一张截图 + `task.json` 与 `symbols.json` 副本」，
    /// 但真机到位当天最容易卡住的不是拍照，是<b>每一站到底读哪一行、点哪颗按钮、屏幕上应该出现什么</b>。
    /// 现场设计这一步，一天就废了——而「预期屏幕主张」这一格空着，走完四站也判不出「走通了没有」。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 四站各一行（一 / 二 / 三 / 四 各一次）；② 每行六格都不许空着；
    /// ③ 每行点到的归档文件名必须逐字出现在产出物清单第 1 行里（两边各写一套名字即红）；
    /// ④ 四站的名字（新建配置 / 新建任务 / 扫描 / 注释缺口）都在这一节里。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机有没有跑</b>，也不判「预期屏幕主张」写得对不对——那是真机当天
    /// 照着核对的事，门只保证「当天有东西可核对」。
    /// </para>
    /// </summary>
    [Fact]
    public void JourneyStations_EachHasItsExpectedClaimAndArchiveFile()
    {
        var rows = JourneyRows();
        Assert.Equal(["一", "二", "三", "四"], rows.Select(r => r.Station));

        var files = ArtifactRows().Single(r => r.Action == 1).Files;
        foreach (var row in rows)
        {
            foreach (var (label, cell) in new[]
                     {
                         ("读哪一行", row.Read), ("看哪个控件", row.Control), ("点到哪一步", row.Step),
                         ("预期屏幕主张", row.Claim), ("归档文件名", row.File),
                     })
            {
                Assert.True(cell.Length >= 6,
                    $"四站表第 {row.Station} 站的「{label}」格太短或空着（{cell}）：真机当天照着念不出来。");
            }

            var file = row.File.Trim('`');
            Assert.True(files.Contains(file, StringComparison.Ordinal),
                $"四站表第 {row.Station} 站点的归档文件名「{file}」在产出物清单第 1 行里找不到："
                + "两边各写一套名字，真机当天就会归档到没人判的名字上。");
        }

        var section = string.Join('\n', JourneyLines());
        foreach (var station in new[] { "新建配置", "新建任务", "扫描", "注释缺口" })
        {
            Assert.Contains(station, section, StringComparison.Ordinal);
        }
    }

    // ---------- 第一百九十三轮（简报 P0-1）：真机当天用到的工具逐个点名 ----------

    /// <summary>「真机当天用到的工具」那一节的标题前缀（三级标题，落在 G-D5 动作单那一节之内）。</summary>
    private const string ToolRosterHeading = "### 真机当天用到的工具";

    /// <summary>这一节那一节的正文行（标题行到下一个二级/三级标题之前）。</summary>
    private static string[] ToolRosterLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(ToolRosterHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{ToolRosterHeading}」这一节：上面两张「照抄即可」表只写了"
            + "「数写在哪个控件上」，而控件名不是工具名——真机当天仍要现场决定「客户区宽拿什么量、截图存成什么格式、"
            + "DPI 从哪个界面读」，现场决定 = 现场发明。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>
    /// 真机当天用到的工具：每个预置槽位都被点到、截图工具与冒烟器被点名、让读的报告行关键词真的在冒烟器源码里、
    /// 且这一节里出现的 .exe 只许是仓内产物（第一百九十三轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：第一百八十七轮把「量什么数 / 数写在哪个控件上 / 归档文件名」补到了照抄粒度，
    /// 但「<b>用什么读</b>」这一列一直是空的——<c>主窗口边框</c>、<c>说明行</c>、<c>系统显示设置</c> 是槽位名，
    /// 不是工具名。真机当天站在那台机器前，仍要现场决定「客户区宽拿什么量、截图存成什么格式、DPI 从哪个界面读」。
    /// 现场决定 = 现场发明，而现场发明出来的读数没人能复核。
    /// </para>
    /// <para>
    /// <b>判四件事</b>：① <see cref="PresetControlSlots"/> 里的<b>每一个</b>槽位名都在这一节里被点到
    /// （漏一个，那个数当天就没人知道拿什么读）；② 截图工具与 <c>Win+Shift+S</c> 被点名；
    /// ③ 这一节让读的报告行关键词（<c>缩放因子</c>、<c>客户区</c>、<c>文字/可用宽度</c>）<b>逐字出现在
    /// <c>ApifoxHelper.Smoke/Program.cs</c> 里</b>——指着一行冒烟器根本不打的字，等于没指；
    /// ④ 这一节里出现的 <c>.exe</c> 只许是仓内产物（<c>ApifoxHelper.Smoke.exe</c> / <c>ApifoxHelper.exe</c>），
    /// 真机上没有的工具不许写进来。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机有没有跑</b>（本机没有 Windows 图形子系统），也不判这些工具在真机上
    /// 打不打得开——它只保证「当天照着念的工具名，都是真机上真有的那一个」。
    /// </para>
    /// </summary>
    [Fact]
    public void ToolRoster_NamesAToolForEveryPresetSlotAndOnlyRealReportLines()
    {
        var section = string.Join('\n', ToolRosterLines());

        // ① 每个预置槽位都被点到：槽位名是「数写在哪里」，工具是「拿什么读」，两列缺一列都到不了照抄粒度。
        var slots = PresetControlSlots.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).ToList();
        var missing = slots.Where(s => !section.Contains(s, StringComparison.Ordinal)).ToList();
        Assert.True(missing.Count == 0,
            "「真机当天用到的工具」这一节漏了这些预置槽位：" + string.Join(" ／ ", missing)
            + "。漏一个槽位，那个数当天就没人知道拿什么读。");

        // ② 截图工具：7 条动作全都要截图，工具名与快捷键都得点名（只写「截图」等于没写）。
        Assert.Contains("截图工具", section, StringComparison.Ordinal);
        Assert.Contains("Win+Shift+S", section, StringComparison.Ordinal);

        // ③ 让读的报告行关键词必须真的在冒烟器源码里：文档指着一行冒烟器不打的字，当天照着搜就是空手。
        var smoke = Repo.Source("ApifoxHelper.Smoke", "Program.cs");
        foreach (var keyword in new[] { "缩放因子", "客户区", "文字/可用宽度" })
        {
            Assert.True(section.Contains(keyword, StringComparison.Ordinal),
                $"「真机当天用到的工具」这一节没点名报告行里的「{keyword}」：三个数（因子 / 客户区宽 / 说明行可用宽）"
                + "当天要照着这一行抄，不点名就不知道搜什么。");
            Assert.True(smoke.Contains(keyword, StringComparison.Ordinal),
                $"这一节让读报告里的「{keyword}」，而 ApifoxHelper.Smoke/Program.cs 里根本没有这几个字："
                + "文档指着一行冒烟器不打的字，当天照着搜就是空手。");
        }

        // ④ 只许点仓内产物：真机上没有的工具写进来，当天照着找就是找不到。
        var exes = Regex.Matches(section, @"[A-Za-z0-9_.]+\.exe")
            .Select(m => m.Value).Distinct(StringComparer.Ordinal).ToList();
        Assert.True(exes.Count > 0,
            "「真机当天用到的工具」这一节一个 .exe 都没点：三个数（因子 / 客户区宽 / 说明行可用宽）"
            + "要读的那份报告是仓内冒烟器打的，工具名必须点名。");
        foreach (var exe in exes)
        {
            Assert.True(exe is "ApifoxHelper.Smoke.exe" or "ApifoxHelper.exe",
                $"这一节点了仓外工具「{exe}」：真机上没有的工具不许写进动作单——当天照着找就是找不到。");
        }

        // ⑤ 表格结构：至少 4 行、4 列、每格不空（空着的那一格正是当天要现场编的东西）。
        var rows = 0;
        foreach (var line in ToolRosterLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells[0] == "用途") continue;
            if (cells.All(c => c.All(ch => ch is '-' or ' '))) continue;
            Assert.True(cells.Count == 4,
                $"「真机当天用到的工具」有一行不是 4 列（实际 {cells.Count} 列，应为 用途 / 工具 / 照抄怎么用 / 读到的数落进哪）：{cells[0]}。");
            foreach (var cell in cells)
            {
                Assert.True(cell.Length >= 2, $"「真机当天用到的工具」有一格空着（{cells[0]}）：空着的那一格正是当天要现场编的东西。");
            }

            rows++;
        }

        Assert.True(rows >= 4, $"「真机当天用到的工具」只有 {rows} 行（地板 4）：截图 / 缩放档 / 三个数 / 归档各要一行。");
    }
}
