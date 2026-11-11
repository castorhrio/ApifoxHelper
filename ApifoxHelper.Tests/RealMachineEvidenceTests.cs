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
}
