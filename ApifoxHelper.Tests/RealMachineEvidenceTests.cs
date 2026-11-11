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
    internal readonly record struct ArtifactRow(int Action, string DirCell, string Files, string Numbers, string Pass, string Keys);

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

    /// <summary>产出物清单的行（内部两处门共用：本文件的对齐门与 <c>AcceptanceChecklistTests</c> 那条预登记门）。</summary>
    internal static List<ArtifactRow> ArtifactRows()
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
    internal static List<string> DirectoriesIn(string cell)
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
    /// 执行单的三步：按行首 <c>1. </c> / <c>2. </c> / <c>3. </c> 切块，每块含该步的标题行与下面几行。
    /// </summary>
    private static List<(int Number, string Text)> RunSheetSteps() => NumberedSteps(RunSheetLines());

    /// <summary>
    /// 「按行首编号切块」这一把刀（第二百一十二轮起两处共用：<see cref="RunSheetSteps"/> 与
    /// <see cref="ThirtyMinuteDrill_EveryStepNamesACommandAndAnOutputFile"/>）。
    /// <para>
    /// 抽出来不是为了省十二行，是因为两页执行单必须<b>数出同一种「步」</b>：各写一把切刀时，
    /// 「<c>10.</c> 也算一步」「缩进过的编号不算」这类分歧只会在一页上红，另一页静静少判一步。
    /// </para>
    /// </summary>
    private static List<(int Number, string Text)> NumberedSteps(string[] lines)
    {
        var steps = new List<(int, string)>();
        var buffer = new System.Text.StringBuilder();
        var number = 0;
        foreach (var line in lines)
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

    /// <summary>
    /// 两处回写落点：<b>哪个文件</b> ＋ <b>那个文件的哪一行</b>。成对写全才算落点，
    /// 只写文件名（改哪一行？）或只写行名（哪个文件？）当天都要现场猜。
    /// <para>
    /// 文件带反引号是这一族在文档里的既有写法（<see cref="CitesRealArtifact"/> 就按反引号跨度取实物），
    /// 这里照同一写法取，不给同一件事起第二种形状。
    /// </para>
    /// </summary>
    private static readonly (string File, string Row)[] WritebackDestinations =
    [
        ("`docs/NOTES.md`", "G-D5 行「当前结论」格"),
        ("`README.md`", "「已知限制」第 1 条"),
    ];

    /// <summary>把一段文字按句子切开（句号 / 分号 / 换行 / 圈号都是边界）——落点必须在<b>同一句</b>里写全。</summary>
    private static List<string> Clauses(string text) =>
        Regex.Split(text, @"[。；\n①②③④⑤]").Select(c => c.Trim()).Where(c => c.Length > 0).ToList();

    /// <summary>
    /// 落点成对写全没有（<paramref name="where"/> 说清是哪一处文档）。
    /// <para>
    /// <b>判的是「一段 = 一个落点」</b>：两处落点在文档里由「，与」相连，切开之后每一段必须带着
    /// <b>它自己那一个文件 + 它自己那一行</b>，且不许出现另一处落点的任何一个半边。
    /// 为什么不是「两个字符串都出现过」：② 那一行本来就同时点了两个文件与两个行名，Presence 型判据
    /// 在<b>把两个文件互换</b>时照样绿，而互换后那句读起来完全通顺——
    /// 「<c>`README.md`</c> 顶部验收表的 G-D5 行『当前结论』格」。那天照它改，改的是没有 G-D5 行的那份文件，
    /// 验收表一格未动，两处说法却已经分叉。
    /// <b>也不是「离锚点最近的文件」</b>（本门第一版那么写，在<b>正确</b>的那句话上直接红了）：
    /// 「…G-D5 行「当前结论」格，与 <c>`README.md`</c>…」里物理上离那个锚点更近的恰恰是 README。
    /// 距离量的是字序，配对量的是「这一段在说哪一处」——所以按连接词切段，一段一个落点。
    /// </para>
    /// </summary>
    private static void AssertWritebackDestinationsWrittenOut(string where, string text)
    {
        var segments = Clauses(text)
            .SelectMany(c => Regex.Split(c, @"[，、,]\s*与\s*"))
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        foreach (var (file, row) in WritebackDestinations)
        {
            var others = WritebackDestinations.Where(o => o.File != file).ToList();
            var hit = segments.FirstOrDefault(s => s.Contains(file, StringComparison.Ordinal)
                                                  && s.Contains(row, StringComparison.Ordinal)
                                                  && others.All(o => !s.Contains(o.File, StringComparison.Ordinal)
                                                                     && !s.Contains(o.Row, StringComparison.Ordinal)));
            Assert.True(hit is not null,
                $"{where}没有把回写落点「{file} ＋ {row}」单独成段写全（现读到 {segments.Count} 段，"
                + $"最长一段 {segments.OrderByDescending(s => s.Length).FirstOrDefault()?.Length} 字）："
                + "要么文件与行名分在两段、要么两处落点混进同一段（混写时既切不出「一个文件 + 一行」的形状，"
                + "两处文件互换也抓不到）。当天照混写的那句改，两处只会改到一处。");
        }
    }

    /// <summary>
    /// 「到期日当天执行单」那页的<b>最后一段机械门</b>：三步编号齐、两处回写落点各自把「哪个文件 + 哪一行」
    /// 在同一句里写全（三行动作 ② 与第 3 步两处都要），且这一页的到期日与时效门读的是<b>同一个出口</b>
    /// （第二百一十三轮，简报 P1-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：那页有三步、每步三行，兄弟门
    /// <see cref="DueDateRunSheet_HasThreeStepsEachAnchoredToARealArtifact"/> 判的是「编号齐 / 三行齐 /
    /// 引到实物 / 两个行名两处措辞一致」——<b>但它判的两个行名不带文件</b>。回写这件事的完整说法是
    /// 「改 <c>docs/NOTES.md</c> 的 G-D5 行那一格，与 <c>README.md</c> 的已知限制第 1 条」：
    /// 少了文件，那一行在两份文档里都可能出现（本仓确实两份都有「已知限制」）；少了行名，
    /// 只写「改 README.md」会改到首屏那句场景描述上去。而且这两个落点<b>必须逐字一致</b>——
    /// ② 与第 3 步漂开，读的人按哪一处都行，就是两套说法。
    /// </para>
    /// <para>
    /// <b>到期日为什么收成同一个出口</b>：这一页的标题写着到期日，<see cref="DueDate_HasADecisionAction"/>
    /// 与 <see cref="DueDateRunSheet_HasThreeStepsEachAnchoredToARealArtifact"/> 判的都是
    /// <c>Contains("2026-11-20")</c> 那份<b>字面量</b>。改期那天若只改了验收表那一格，
    /// 这几条门会红（字面量找不到）——但红的是「这一页没写 2026-11-20」，念不出真正的问题
    /// 「这一页的日期与那一格不是同一个」。本门直接比两个<b>现读取值</b>，红的时候念得出两个日期各是多少。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判当天选哪一支、不判真机跑没跑、不判 G-D5 算不算过（勾选框只由真机证据关闭，
    /// 同 <see cref="DueDate_HasADecisionAction"/>）。它只保证「当天那三行落点写全了、日期不会各说一套」。
    /// </para>
    /// </summary>
    [Fact]
    public void DueDateRunSheet_WritebackDestinations_NameTheFileAndTheRow()
    {
        // ① 三步编号齐（多一步是没人判的动作，少一步是断链）。
        var steps = RunSheetSteps();
        Assert.Equal([1, 2, 3], steps.Select(s => s.Number));

        // ② 两处落点各自写全「文件 + 那一行」：定落点的 ② 那一行，与照它写的第 3 步，两处都要。
        var blockquote = string.Join('\n', ActionListLines()
            .Where(l => l.TrimStart().StartsWith(">", StringComparison.Ordinal)));
        AssertWritebackDestinationsWrittenOut("三行动作 ②", blockquote);
        AssertWritebackDestinationsWrittenOut("执行单第 3 步", steps.Single(s => s.Number == 3).Text);

        // ③ 这一页的到期日与时效门读的是同一个值——不是只判「两处都出现过某个日期」。
        var recorded = AcceptanceChecklistTests.Gd5DueDateText();
        Assert.True(recorded.Length > 0,
            "验收表 G-D5 那一格里读不到到期日（YYYY-MM-DD）：没有权威值，这一页对齐谁都没有意义。");
        var onSheet = AcceptanceChecklistTests.FirstDueDateIn(string.Join('\n', RunSheetLines()));
        Assert.True(onSheet.Length > 0,
            "「到期日当天执行单」那一页没有写到期日：这一页是那天唯一不用回头翻别处的一页，"
            + "日期不在页首，走的人会按记忆里的日子收口。");
        Assert.True(onSheet == recorded,
            $"执行单那页写的到期日是 {onSheet}，验收表 G-D5 那一格记的是 {recorded}：两个日期就是两套说法，"
            + "那天按哪一份收口都对，于是哪一份都没人负责。改期要同时改这两处（本仓唯一取法是 "
            + $"{nameof(AcceptanceChecklistTests)}.{nameof(AcceptanceChecklistTests.Gd5DueDateText)}）。");

        // ④ 这一页自己点名本门，并写清它不判什么（有门不等于过了，同族纪律）。
        var sheet = string.Join('\n', RunSheetLines());
        var self = $"{nameof(RealMachineEvidenceTests)}.{nameof(DueDateRunSheet_WritebackDestinations_NameTheFileAndTheRow)}";
        Assert.True(sheet.Contains(self, StringComparison.Ordinal),
            $"这一页没点名判它的那条门（{self}）：读这一页的人不知道两处落点的配对是被机械核过的，"
            + "也不知道要改判据该去哪一条断言。");
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

    // ---------- 第一百九十五轮（简报 P0-2）：机器到手前的第 0 条 ----------

    /// <summary>「第 0 条」那一段的标记（落在 G-D5 动作单那一节之内，排在七条动作之前）。</summary>
    private const string BeforeArrivalMarker = "第 0 条（机器到手前，本仓可做";

    /// <summary>
    /// 「第 0 条」那一段的列表行（<c>&gt; - [ ] …</c>），从标记行往下取到动作单表格表头之前。
    /// <para>
    /// 只收 <c>&gt; - </c> 开头的行：写成普通段落的那一项当天没人会去勾它，所以「每项都有可勾选框」
    /// 这条判据必须落在解析器上，而不是靠人读。
    /// </para>
    /// </summary>
    private static List<string> BeforeArrivalLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.Contains(BeforeArrivalMarker, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{BeforeArrivalMarker}…）」这一段：下面七条都是「机器到手后」的动作，"
            + "机器到手前本仓能做的最后准备一条都没写——机器在到期日当天到，现场就得一边建目录一边找命令。");

        var items = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("|", StringComparison.Ordinal)) break; // 动作单表格开始
            if (Regex.IsMatch(line, @"^>\s*-\s")) items.Add(line);
        }

        return items;
    }

    /// <summary>
    /// 机器到手前的「第 0 条」：四项齐、每项都有可勾选框、至少一项点到可复跑命令（第一百九十五轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：动作单七条全是「机器到手后」的动作，<b>没有一条是「机器到手前，本仓能做的最后准备」</b>。
    /// 若机器在到期日当天到，现场手忙脚乱：一边建归档目录、一边找命令、一边翻文档。四项在机器到手前做完，
    /// 当天只剩「照单跑」。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 四项齐且编号 ① ② ③ ④ 各一次（少一项就是少一步准备）；② 每项都带可勾选框
    /// （<c>- [ ]</c> 或 <c>- [x]</c>）——写成普通段落的那一项当天没人会去勾；③ 至少一项点到可复跑命令
    /// <c>.winecheck/demo.sh</c>（机器到手前唯一能自己跑出读数的东西）；④ 每项都写清「做什么」而不是一句口号
    /// （至少 12 字）；⑤ 这一段点名本门并写清它<b>不判这四项做过没有</b>——有门不等于做过。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判这四项做过没有</b>（本机没有 Windows 图形子系统，做没做只有人知道），
    /// 也<b>不判真机跑没跑</b>（G-D5 仍是 <c>[ ]</c>）。它只保证「机器到手前的那一页不是空的」。
    /// </para>
    /// </summary>
    [Fact]
    public void BeforeMachineArrives_HasFourCheckableItemsAndNamesTheDemoCommand()
    {
        var lines = BeforeArrivalLines();

        // ① 四项齐：少一项就是少一步准备。
        Assert.True(lines.Count == 4,
            $"「第 0 条」有 {lines.Count} 项，简报要的是 4 项（复跑三档冒烟 / 复跑构建与用例 / 建好九个归档子目录 / 存好七条动作单）。");

        // ② 每项都带可勾选框，且编号 ① ② ③ ④ 各一次。
        var markers = new List<string>();
        foreach (var line in lines)
        {
            var m = Regex.Match(line, @"^>\s*-\s*\[(?<box>[ x])\]\s*(?<marker>[①②③④])\s*(?<text>.+)$");
            Assert.True(m.Success,
                $"「第 0 条」有一项不是「> - [ ] ① …」的形状：{line.Trim()}。写成普通段落的那一项当天没人会去勾它。");
            markers.Add(m.Groups["marker"].Value);

            var text = m.Groups["text"].Value.Trim();
            Assert.True(text.Length >= 12,
                $"「第 0 条」{m.Groups["marker"].Value} 只写了 {text.Length} 字：一句口号在机器到手前做不了任何事。");
        }

        Assert.Equal(["①", "②", "③", "④"], markers);

        // ③ 至少一项点到可复跑命令：机器到手前唯一能自己跑出读数的东西。
        Assert.True(lines.Any(l => l.Contains(".winecheck/demo.sh", StringComparison.Ordinal)),
            "「第 0 条」没有一项点到 .winecheck/demo.sh：机器到手前唯一能自己跑出三档读数的那条命令，"
            + "不点名就没人知道先跑什么。");

        // ④ 这一段点名本门，并写清它不判「做过没有」——有门不等于做过。
        var block = string.Join('\n', ActionListLines()
            .SkipWhile(l => !l.Contains(BeforeArrivalMarker, StringComparison.Ordinal))
            .TakeWhile(l => !l.TrimStart().StartsWith("|", StringComparison.Ordinal)));
        var self = $"{nameof(RealMachineEvidenceTests)}.{nameof(BeforeMachineArrives_HasFourCheckableItemsAndNamesTheDemoCommand)}";
        Assert.Contains(self, block, StringComparison.Ordinal);
        Assert.Contains("不判这四项做过没有", block, StringComparison.Ordinal);
    }

    // ---------- 第二百零四轮（简报 P0-2）：九个归档目录必须真实存在于磁盘上 ----------

    /// <summary>
    /// 九个归档目录必须真实存在于磁盘上，且各带一个 <c>.gitkeep</c> 占位（第二百零四轮，简报 P0-2）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<see cref="Readme_CoversEveryArchiveDirectoryTheActionListNames"/> 判的是
    /// 「动作单与 README 两边同名」，<see cref="ArchiveDirectories_AreEitherAllAbsentOrAllNamedByTheReadme"/>
    /// 判的是「仓里多出来的目录不许是 README 没点到的」——两条都<b>不判目录在不在</b>：九个目录一个都没有时
    /// 两条都是绿的（前者只看文档，后者在「一个都没有」时直接 return）。于是「真机到手当天
    /// <c>prepare-real-machine.sh</c> 会不会撞『目录不存在』」这件事在仓里没有答案，而机器若在到期日当天到，
    /// 现场第一件事就是建目录。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① <c>docs/evidence/real-machine/</c> 下 <see cref="ExpectedDirectories"/> 的九个目录
    /// 逐个 <see cref="Directory.Exists(string)"/> 为真；② 每个目录里有一个 <c>.gitkeep</c> 占位——
    /// git 不跟踪空目录，没有占位的话 clone 下来目录又没了，「目录在仓里」这件事仍然没有实物。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判目录里有真机证据</b>（真机 0 台，目录里只有 <c>.gitkeep</c>，空目录 = 未达成），
    /// 也<b>不判真机跑没跑</b>。它只保证「机器到手当天不会卡在第一步」。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryArchiveDirectory_ExistsOnDisk()
    {
        Assert.True(Directory.Exists(EvidenceRoot),
            $"缺 docs/evidence/real-machine/ 目录：真机证据的落点不在，动作单七条都没有归档处。");

        foreach (var dir in ExpectedDirectories)
        {
            var full = Path.Combine(EvidenceRoot, dir);
            Assert.True(Directory.Exists(full),
                $"docs/evidence/real-machine/{dir}/ 不存在：真机到手当天 prepare-real-machine.sh 会撞「目录不存在」，"
                + "而机器若在到期日当天到，现场第一件事就是建目录。目录清单在 " + nameof(ExpectedDirectories) + "。");

            // git 不跟踪空目录：没有 .gitkeep 占位，clone 下来目录又没了，「目录在仓里」仍然没有实物。
            Assert.True(File.Exists(Path.Combine(full, ".gitkeep")),
                $"docs/evidence/real-machine/{dir}/ 里没有 .gitkeep 占位：git 不跟踪空目录，"
                + "没有占位的话这个目录提交不进仓，下一个人 clone 下来又得自己建。");
        }
    }

    // ---------- 第二百零八轮（简报 P0-1）：「空目录 = 未达成」第一次有一条可跑的核对 ----------

    /// <summary>一个目录里「除隐藏占位之外」的文件数（与脚本同一口径：以 <c>.</c> 开头的都不算）。</summary>
    private static int RealFileCount(string dir) =>
        Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly).Count(f => !Path.GetFileName(f).StartsWith('.'))
            : 0;

    /// <summary>
    /// 每个归档目录「该放哪些产出物文件名」，从 README 那张表现读（<c>&lt;档位&gt;</c> 按目录名展开）。
    /// 与本文件另外几条门共用 <see cref="ArtifactRows"/> 与 <see cref="DirectoriesIn"/> 同一取法，
    /// 不另写第二份解析器——本仓已经因为「各写一份去注释」犯过四次。
    /// </summary>
    private static Dictionary<string, List<string>> ArtifactsByDirectory()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var row in ArtifactRows())
        {
            foreach (var dir in DirectoriesIn(row.DirCell))
            {
                if (!map.TryGetValue(dir, out var list)) map[dir] = list = [];
                foreach (Match m in Regex.Matches(row.Files, "`([^`]+)`"))
                {
                    var name = m.Groups[1].Value;
                    // 只收带点又不带斜杠的那个（文件名）；格里另有「一行三个数」这类说明性反引号记号。
                    if (name.Contains('.') && !name.Contains('/'))
                    {
                        list.Add(name.Replace("<档位>", dir).Replace("<dpi>", dir));
                    }
                }
            }
        }

        return map;
    }

    /// <summary>找 bash（PATH，再退到三个常规位置）。找不到返回 null，调用方走「这台机器判不到」那一支。</summary>
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
                // 这一台上不合法的目录写法（比如 Windows 路径出现在 Linux 上）跳过就行。
            }
        }

        return null;
    }

    /// <summary>这个文件现在到底读不读得动。<see cref="File.SetUnixFileMode"/> 在 root / CAP_DAC_OVERRIDE 下挡不住读，
    /// 那一支就不能假装跑过——这里当场试一次，读得动就说明这台机器判不到。</summary>
    private static bool TryCanRead(string path)
    {
        try
        {
            using var _ = File.OpenRead(path);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>真的执行一次核对脚本；<paramref name="evidence"/> 为空时用仓里那个归档根。</summary>
    private static (int Exit, string Output) RunCollectScript(string? evidence)
    {
        var psi = new ProcessStartInfo(LocateBash()!, Path.Combine(Repo.Root, ".winecheck", "collect-real-machine.sh"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Repo.Root,
        };
        psi.Environment["LANG"] = "C.UTF-8";
        if (evidence is not null) psi.Environment["EVIDENCE"] = evidence;

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120_000), "collect-real-machine.sh 跑了 120 秒还没退出——它只该读一个目录树。");
        return (process.ExitCode, output + error);
    }

    /// <summary>
    /// 真的执行 <c>.winecheck/collect-real-machine.sh</c>，核它判的就是「有没有东西」这一件事，
    /// 并且三个退出码都<b>跑得活</b>（第二百零八轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：「空目录 = 未达成」这句判据此前只在文档里，靠人自觉；
    /// <see cref="CheckedAction_RequiresItsArchiveDirectoryToHoldFiles"/> 又只在<b>有人把那一行勾成 [x]</b>
    /// 时才去看目录。于是真机当天最可能的状态——「跑了一半，谁也没勾，也没人去翻那九个目录」——
    /// 在仓里没有任何东西能回答。这一条把那条核对真的跑一遍：它输出的正是当天唯一读得到的
    /// 「还差哪几个目录、各该放什么」。
    /// </para>
    /// <para>
    /// <b>判五件事</b>：① <b>占位不算东西</b>——只装 <c>.gitkeep</c> 的目录必须报「还差东西」；
    /// 这一条若是反的，九个目录永远「已到位」，整支核对当场是死的（本仓对每一条判据都跑一遍反向，
    /// 为的就是让「它绿着」不等于「它什么都看不见」）；② 退出码与磁盘实数<b>同源</b>——仓里跑一次，
    /// 期望值由本测试按同一口径自己数一遍文件得出，两边不一致即红；③ 九个归档目录逐个被点名；
    /// ④ 每个「还差东西」都带着 README 为它点到的<b>每一个</b>产出物文件名（<c>&lt;档位&gt;</c> 已按目录名展开）；
    /// ⑤ 临时假树上把 0（都不空）/ 1（还有空的）/ 2（清单没点到的目录、清单点到的目录不在）三个退出码都跑活；
    /// ⑥⑦ 第二百零九轮在同一棵假树上再跑两种「清单自己歪了」：某一行的归档目录格被删（动作数仍凑得够 7、
    /// 只剩 8 个目录）与同一个目录被点两次（九个名字仍齐）——这两种都必须是 2，且各点自己的名。
    /// ⑧⑨⑩ 第二百一十轮（简报 P1-1）把剩下三条 <c>exit 2</c> 分支也在同一棵假树上跑活：少一行动作（认出 6 条）、
    /// 产出物表被改成散文（一行都没解析出来）、清单文件读不动（awk 本身失败）。从此那张表里每一行退出码 2 的读数
    /// 都有这一段做出处，<c>MutationVerificationTests</c> 判的就是「表与脚本分支一一对得上、而这一对又都有出处」。
    /// </para>
    /// <para>
    /// <b>它判到哪一层</b>：这一支不启动任何界面、不碰模拟器，所以这一层在 Linux 上量到就是它的<b>全部行为</b>；
    /// 它不判真机跑没跑，也不判归档内容对不对（截图有没有裁切、那三个数落得对不对，都要人看）。
    /// 本机没有 bash 时这一条判不到——那种情况不许静默绿：不分路径都跑的
    /// <c>WineCheckScriptsTests.CollectRealMachineScript_NamesItsOwnFailureAndKeepsOneSourceOfTheFileList</c>
    /// 要求 <c>.winecheck/README.md</c> 把这段核对登记在案（同 <c>DemoPs1_ParsesWithARealPowerShellParserWhenOneIsHere</c>
    /// 的做法：判不到就说判不到）。
    /// </para>
    /// </summary>
    [Fact]
    public void CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory()
    {
        if (LocateBash() is null) return; // 这台机器上没有 bash：判不到，由上面那条门把登记守住。

        var artifacts = ArtifactsByDirectory();

        // ② 与仓里的磁盘实数同源：期望退出码由本测试自己数出来，不照抄脚本的话。
        var (exit, output) = RunCollectScript(evidence: null);
        var pendingInRepo = ExpectedDirectories.Where(d => RealFileCount(Path.Combine(EvidenceRoot, d)) == 0).ToList();
        var expectedExit = pendingInRepo.Count > 0 ? 1 : 0;
        Assert.True(exit == expectedExit,
            $"仓里跑一次核对，脚本退出码 {exit}、本测试按同一口径数出来应该是 {expectedExit}"
            + $"（还空的目录：{string.Join("、", pendingInRepo)}）。两边各判一套，当天念的就不是同一句话。");

        // ③ 九个归档目录逐个被点名。
        foreach (var dir in ExpectedDirectories)
        {
            Assert.True(output.Contains(dir + "/", StringComparison.Ordinal),
                $"核对输出里没点到 {dir}/：九个目录少点一个，当天就少一个落点被看过。");
        }

        // ①④ 每个「还差东西」的目录都带着 README 为它点到的每一个文件名；占位单独不算东西。
        foreach (var dir in pendingInRepo)
        {
            Assert.True(output.Contains($"[还差东西] {dir}/", StringComparison.Ordinal),
                $"{dir}/ 里只有隐藏占位（本仓当前九个目录都是这样），核对却没报「还差东西」："
                + "把 .gitkeep 算成东西 = 九个目录永远已到位，这条核对当场就是死的。");
            foreach (var file in artifacts[dir])
            {
                Assert.True(output.Contains(file, StringComparison.Ordinal),
                    $"{dir}/ 报了还差东西，却没打出该放的「{file}」：当天只知道缺、不知道缺哪一件。");
            }
        }

        // ⑤ 临时假树：三个退出码都得真的跑得出来。
        var temp = Path.Combine(Path.GetTempPath(), "ah-collect-" + Guid.NewGuid().ToString("N")[..12]);
        try
        {
            Directory.CreateDirectory(temp);
            File.Copy(ReadmePath, Path.Combine(temp, "README.md"));
            foreach (var dir in ExpectedDirectories)
            {
                var full = Path.Combine(temp, dir);
                Directory.CreateDirectory(full);
                File.WriteAllText(Path.Combine(full, ".gitkeep"), string.Empty);
            }

            var (placeholderOnly, _) = RunCollectScript(temp);
            Assert.True(placeholderOnly == 1,
                $"假树里九个目录都只有 .gitkeep，脚本退出码却是 {placeholderOnly}（应为 1）："
                + "占位被算成了东西，当天九个目录永远显示已到位。");

            foreach (var dir in ExpectedDirectories)
            {
                File.WriteAllText(Path.Combine(temp, dir, "01-fake.png"), "假产出物，只为把「不空」这一支跑到");
            }

            var (filled, filledOutput) = RunCollectScript(temp);
            Assert.True(filled == 0,
                $"假树里九个目录都有了真文件，脚本退出码却是 {filled}（应为 0）——「都不空」这一支不可达，"
                + "真机当天跑完七条动作也拿不到一句「可以回去判什么算过了」。");
            Assert.Contains($"有东西的目录 {ExpectedDirectories.Length} / {ExpectedDirectories.Length} 个", filledOutput, StringComparison.Ordinal);

            var orphan = Path.Combine(temp, "150");
            Directory.CreateDirectory(orphan);
            var (orphanExit, orphanOutput) = RunCollectScript(temp);
            Assert.True(orphanExit == 2 && orphanOutput.Contains("清单没点到的目录：150/", StringComparison.Ordinal),
                $"磁盘上多出一个清单没点到的 150/，脚本退出码 {orphanExit}：那种目录既不会被判据看见，"
                + "也不会被下一个人找到——两边各一套名字时必须停下来点名。");
            Directory.Delete(orphan);

            Directory.Delete(Path.Combine(temp, "125"), true);
            var (absentExit, absentOutput) = RunCollectScript(temp);
            Assert.True(absentExit == 2 && absentOutput.Contains("在磁盘上不存在：125/", StringComparison.Ordinal),
                $"清单点到的 125/ 不在磁盘上，脚本退出码 {absentExit}：少一档就是少截一套图，"
                + "而这一句必须把档位点名，否则当天会去翻别的目录。");

            // ⑥ 清单少一格目录（行数与动作号仍凑得够 7）：那条「应为 7 条」的护栏数不到，必须靠目录数地板。
            Directory.CreateDirectory(Path.Combine(temp, "125"));
            var readmeCopy = Path.Combine(temp, "README.md");
            var table = File.ReadAllText(readmeCopy);
            var thinner = table.Replace("、`docs/evidence/real-machine/125/`", string.Empty, StringComparison.Ordinal);
            Assert.True(thinner != table, "假树上的清单删不动那一格目录引用：这一场景要先与产出物清单的形状对齐。");
            File.WriteAllText(readmeCopy, thinner);
            var (fewerDirs, fewerOutput) = RunCollectScript(temp);
            Assert.True(fewerDirs == 2 && fewerOutput.Contains("清单解析出 8 个归档目录", StringComparison.Ordinal),
                $"README 的归档目录那一列少了一格、动作数仍凑得够 7，脚本退出码却是 {fewerDirs}："
                + "它会带着少一个落点的清单退 1，把「核对被改坏了」念成「活儿还没干完」——那天少截的那一套没人说。");

            // ⑦ 同一个目录被点两次（九个名字仍齐，数量地板拦不住）：只有重名那一条看得见。
            File.WriteAllText(readmeCopy, table);
            var rows = table.Split('\n').ToList();
            var first = rows.FindIndex(l => l.StartsWith("| 1 |", StringComparison.Ordinal));
            Assert.True(first >= 0, "假树上的清单里没有动作 1 那一行：这一场景要先与产出物清单的形状对齐。");
            rows.Insert(first + 1, rows[first]);
            File.WriteAllText(readmeCopy, string.Join('\n', rows));
            var (twice, twiceOutput) = RunCollectScript(temp);
            Assert.True(twice == 2 && twiceOutput.Contains("同一个归档目录点到两次", StringComparison.Ordinal)
                        && twiceOutput.Contains("journey", StringComparison.Ordinal),
                $"README 里同一个归档目录被点到两次（九个名字仍齐、总数仍 9），脚本退出码却是 {twice}："
                + "产出物散进两个名字时，当天得自己猜哪个才是落点，而数量地板与动作数都看不见这一种。");

            // ⑧⑨⑩（第二百一十轮，简报 P1-1）：另外三条 exit 2 分支此前只在 .winecheck/README.md 那张表里
            // 由人写一句「跑过了」。表现在多一列逐字对着脚本里的字面量，而下面这三段把那一行变成当场跑出来的读数。
            File.WriteAllText(readmeCopy, table);
            var fewerActions = table.Split('\n').Where(l => !l.StartsWith("| 4 |", StringComparison.Ordinal)).ToList();
            Assert.True(fewerActions.Count < table.Split('\n').Length,
                "假树上的清单删不动动作 4 那一行：这一场景要先与产出物清单的形状对齐。");
            File.WriteAllText(readmeCopy, string.Join('\n', fewerActions));
            var (sixActions, sixOutput) = RunCollectScript(temp);
            Assert.True(sixActions == 2 && sixOutput.Contains("清单解析出 6 条动作（应为 7 条）", StringComparison.Ordinal),
                $"清单少了一行动作（认出 6 条），脚本退出码却是 {sixActions}：那一条动作的产出物当天没人点名要放什么，"
                + "而核对会带着「表被改过」这件事继续往下数目录。");

            File.WriteAllText(readmeCopy, string.Join('\n', table.Split('\n')
                .Where(l => !Regex.IsMatch(l, @"^\| \d+ \|")).ToList()));
            var (prose, proseOutput) = RunCollectScript(temp);
            Assert.True(prose == 2 && proseOutput.Contains("清单里一行都没解析出来", StringComparison.Ordinal),
                $"清单里的产出物表被改成散文（一行都没解析出来），脚本退出码却是 {prose}："
                + "没有表就无从核对，那种情况必须停在「核对本身跑不动」，而不是退 1 让人以为「活儿还没干完」。");

            File.WriteAllText(readmeCopy, table);
            // ⑩ 清单读不动那一条只在 Linux 上造得出来：SetUnixFileMode 在非 Unix 平台会抛，
            // 而 Git Bash 那台 Windows 机器上根本没有这个模式可言——那种机器上这一支判不到，
            // 判不到就说判不到（与 PowerShell 解析器不在时同一做法），不许把「没跑到」写成「跑过了」。
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(readmeCopy, UnixFileMode.None);
                try
                {
                    if (!TryCanRead(readmeCopy))
                    {
                        var (unreadable, unreadableOutput) = RunCollectScript(temp);
                        Assert.True(unreadable == 2 && unreadableOutput.Contains("awk 本身就失败", StringComparison.Ordinal),
                            $"清单文件读不动时脚本退出码却是 {unreadable}：那一句「awk 本身就失败了」是本条唯一念得出来的话，"
                            + "停不住就会拿一份空清单去数目录，把「核对失效」念成「活儿没干完」。");
                    }
                }
                finally
                {
                    File.SetUnixFileMode(readmeCopy, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
            }

            File.WriteAllText(readmeCopy, table);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }

    /// <summary>
    /// 那两条目录集合地板（第二百零九轮，简报 P0-2）只写死<b>数量</b>，不写死<b>名字</b>。
    /// <para>
    /// <b>为什么单独钉这一条</b>：加地板最容易的写法就是把 <see cref="ExpectedDirectories"/> 那九个名字
    /// 抄进脚本、再逐个 <c>case</c> 比一遍。那样当天是绿的，而它把「一份出处」这条纪律换成了两份：
    /// 改产出物清单的人不会想起来改脚本，脚本从此按上一版的名字报警——与第一百六十九轮
    /// 「run1.sh 的悬空引用」同一课，也与 <c>WineCheckScriptsTests</c> 里「脚本不许抄产出物文件名」同一族。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不跑脚本（跑它是上面那条的活），也不判那九个名字对不对——对齐由
    /// <see cref="Readme_CoversEveryArchiveDirectoryTheActionListNames"/> 判。这一条不需要 bash，
    /// 纯文本判定在任何平台上都判得到。<b>只看会真的跑的那些行</b>：注释里举一个目录名做例子不构成
    /// 第二份判据（那一段不执行），把注释也算进去只会逼人删掉说明文字。
    /// </para>
    /// </summary>
    [Fact]
    public void CollectScript_NeedsNoneOfTheNineDirectoryNamesOnItsOwn()
    {
        var code = Repo.Source(".winecheck", "collect-real-machine.sh").Split('\n')
            .Where(line => !line.TrimStart().StartsWith('#'))
            .ToList();
        var copied = ExpectedDirectories
            .Where(d => code.Any(line => line.Contains(d + "/", StringComparison.Ordinal)))
            .ToList();
        Assert.True(copied.Count == 0,
            "collect-real-machine.sh 的代码行里出现了归档目录名字面量：" + string.Join(" ／ ", copied.Select(d => d + "/"))
            + "。地板只该写死数量 9——名字仍只从 docs/evidence/real-machine/README.md 现读，"
            + "抄进脚本就等于两份清单，改那份的人不会想起来改这支，当天它按上一版的名字报警。");
    }

    // ---------- 第二百一十二轮（简报 P0-1）：真机到手 30 分钟执行单 ----------

    /// <summary>「真机到手 30 分钟执行单」那一页的标题前缀（三级标题，落在 G-D5 动作单那一节之内）。</summary>
    private const string DrillHeading = "### 真机到手 30 分钟执行单";

    /// <summary>归档目录的写法（<c>docs/evidence/real-machine/125/</c> 那一形）。</summary>
    private static readonly Regex ArchiveDirPattern = new(@"docs/evidence/real-machine/([^/`\s]+)/", RegexOptions.Compiled);

    /// <summary>某一步引的产出物清单行号（<c>产出物清单第 2 行</c>）。</summary>
    private static readonly Regex RowCitationPattern = new(@"产出物清单第\s*(?<n>\d+)\s*行", RegexOptions.Compiled);

    /// <summary>执行单那一页的正文行（标题行到下一个二级/三级标题之前）。</summary>
    private static string[] DrillLines()
    {
        var lines = ActionListLines();
        var start = Array.FindIndex(lines, l => l.TrimEnd().StartsWith(DrillHeading, StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"docs/NOTES.md 的 G-D5 动作单里缺「{DrillHeading}」这一页：动作单按事排、「照抄即可」两张表按数排，"
            + "机器真到手那天仍排不出一条时间线——借来的机器只给半小时，现场排线花的正是那半小时的头一段。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("## ", StringComparison.Ordinal) || l.StartsWith("### ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>
    /// 「真机当天用到的工具」那张表点名的工具（每行第二格，取到第一个全角括号为止）。
    /// <para>现读而不是在本门里列一份工具清单：执行单要说「只许用名册里那些工具」，
    /// 名册本身改了（换截图工具、换读缩放的入口），执行单不许自己留在旧名字上。</para>
    /// </summary>
    private static List<string> RosterTools() =>
        ToolRosterLines()
            .Where(l => l.TrimStart().StartsWith("|", StringComparison.Ordinal))
            .Select(l => l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList())
            .Where(c => c.Count >= 2 && c[0] != "用途" && !c[0].All(ch => ch == '-' || char.IsWhiteSpace(ch)))
            .Select(c => c[1].Split('（')[0].Trim())
            .Where(t => t.Length > 0)
            .ToList();

    /// <summary>
    /// 「命令：」那一行当天敲不敲得通：要么引到仓里真有的路径，要么是 <c>publish.ps1</c> 自己算出的那条产物路径
    /// （现读 <see cref="PublishScriptTests.ScriptLiterals"/>，不在这里拼第二份），要么是工具名册里点过名的那一个工具。
    /// </summary>
    private static bool CommandIsRunnable(string line, out string cited)
    {
        var published = $@"{PublishScriptTests.ScriptLiterals().Output}\{PublishScriptTests.ScriptLiterals().SelfContainedDir}\{PublishScriptTests.ScriptLiterals().Exe}";
        foreach (Match span in Regex.Matches(line, "`([^`]+)`"))
        {
            foreach (var raw in span.Groups[1].Value.Split([' ', '|', '"', '\''], StringSplitOptions.RemoveEmptyEntries))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;
                if (token.Contains(published, StringComparison.Ordinal))
                {
                    cited = token + "（publish.ps1 现算的产物路径）";
                    return true;
                }

                var full = Path.Combine(Repo.Root, token.Replace('\\', '/').TrimEnd('/'));
                if (File.Exists(full) || Directory.Exists(full))
                {
                    cited = token;
                    return true;
                }
            }
        }

        var tool = RosterTools().FirstOrDefault(t => line.Contains(t, StringComparison.Ordinal));
        if (tool is not null)
        {
            cited = tool + "（工具名册里点过名）";
            return true;
        }

        cited = string.Empty;
        return false;
    }

    /// <summary>
    /// 「真机到手 30 分钟执行单」七步：每步都得有一句<b>敲得通的命令</b>与一个<b>落点</b>，落点指向的归档
    /// 目录必须与它自己引的那一行产出物清单配得上（第二百一十二轮，简报 P0-1）。
    /// <para>
    /// <b>补的是哪个缺口</b>：动作单七条 + 两张「照抄即可」表 + 那份产出物清单，全都<b>按事</b>与<b>按数</b>排，
    /// 没有一份按<b>时间</b>排。借来的机器给半小时，现场排线 = 前一段白丢。这一页把当天收成为
    /// 「进了归档目录的截图与报告 + 一个说得出口的缺什么」排成七步。
    /// </para>
    /// <para>
    /// <b>本轮抓到的第一处「照简报原文写下去会坏」就在落点上</b>：简报给的四个目录名
    /// （<c>prepare/</c>、<c>first-run/</c>、<c>dpi-100/</c>、<c>dpi-200/</c>）<b>仓里一个都不存在</b>——
    /// 九个归档目录的名字只有一份出处（那份 README 的产出物清单），磁盘上多出一个清单没点到的目录，
    /// <c>collect-real-machine.sh</c> 当场退 <c>2</c>（那条门就是 <see cref="CollectScript_TreatsThePlaceholderAsEmptyAndNamesEveryDirectory"/> 的 ⑤）。
    /// 所以本门判④：落点点到的每一个目录，既要<b>在磁盘上</b>，也要<b>确实是它所引那一行声明的那一个</b>。
    /// 把 <c>125</c> 写成 <c>dpi-125</c>、把第 3 步的截图说成进 <c>fonts/</c>、行号写成第 8 行——都红在这里。
    /// </para>
    /// <para>
    /// <b>判什么</b>：① 七步编号 1–7 各一次；② 每步都有「命令：」与「落点：」两行（缺一行那一步当天没产物）；
    /// ③ 每步的「命令：」敲得通（<see cref="CommandIsRunnable"/>：仓内实物 / <c>publish.ps1</c> 现算的产物路径 /
    /// 名册里的工具，三者至少其一）；④ 落点的归档目录与行号互相配得上，且目录在磁盘上；
    /// ⑤ 页首写清<b>排不进的是第 3、4、5、6 条</b>，并写明「30 分钟走完」不等于这一节七条走完、更不等于 G-D5 达成；
    /// ⑥ 第 5 步让读的那两行报告标签，是<b>冒烟器源码真打的字</b>（与 <c>DpiContextConsistencyTests</c> 同一份标签清单）。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：<b>不判真机有没有跑</b>（本机没有 Windows 图形子系统），<b>不判抄回来的那两个数</b>
    /// 怎么算、<b>不判任何一条动作可勾</b>。它只保证「半小时这条线走得通、落点不悬空」。
    /// </para>
    /// </summary>
    [Fact]
    public void ThirtyMinuteDrill_EveryStepNamesACommandAndAnOutputFile()
    {
        var steps = NumberedSteps(DrillLines());

        // ① 七步各一次。
        Assert.Equal(Enumerable.Range(1, 7), steps.Select(s => s.Number).OrderBy(n => n));

        var declared = ArtifactRows().ToDictionary(r => r.Action, r => DirectoriesIn(r.DirCell));

        foreach (var (number, text) in steps)
        {
            var commandLine = text.Split('\n').FirstOrDefault(l => l.Contains("命令：", StringComparison.Ordinal));
            var landingLine = text.Split('\n').FirstOrDefault(l => l.Contains("落点：", StringComparison.Ordinal));
            Assert.True(commandLine is not null,
                $"执行单第 {number} 步没有「命令：」那一行：没有命令的那一步，当天就是一件说不出做了没做的动作。");
            Assert.True(landingLine is not null,
                $"执行单第 {number} 步没有「落点：」那一行：半小时的收成是「东西进了哪个目录」，"
                + "不写落点的步骤跑完仍然是零份证据（collect-real-machine.sh 数不到它，下一轮也找不到它）。");

            // ③ 命令敲得通。
            Assert.True(CommandIsRunnable(commandLine!, out var cited),
                $"执行单第 {number} 步的「命令：」当天敲不通——既没引到仓里真有的路径，也不是 publish.ps1 现算的"
                + "产物路径，还不在「真机当天用到的工具」名册里：" + commandLine!.Trim()
                + "。「跑一下脚本」「照着设置改」这种写法没人替它敲得通。");

            // ④ 落点的目录 ⇄ 它引的那一行产出物清单，两边配得上。
            var dirs = ArchiveDirPattern.Matches(landingLine!).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            if (dirs.Count == 0) continue; // 第 1、6、7 步的落点是产物文件与本文件那一格，不指归档目录

            var rows = RowCitationPattern.Matches(landingLine!).Cast<Match>()
                .Select(m => int.Parse(m.Groups["n"].Value)).Distinct().ToList();
            Assert.True(rows.Count == 1,
                $"执行单第 {number} 步点了归档目录（{string.Join("、", dirs)}），却没有且只有一个「产出物清单第 N 行」：行号就是这一步与那份清单唯一的接线柱，"
                + "散着写目录名等于在本页抄第二份清单。");
            var row = rows[0];
            Assert.True(declared.TryGetValue(row, out var allowed),
                $"执行单第 {number} 步引的「产出物清单第 {row} 行」不存在（清单只有 {string.Join("、", declared.Keys.OrderBy(k => k))} 行）："
                + "行号写错，那天按这一行找产出物会找到另一条动作的落点。");
            foreach (var dir in dirs)
            {
                Assert.True(allowed!.Contains(dir, StringComparer.Ordinal),
                    $"执行单第 {number} 步把东西放进 docs/evidence/real-machine/{dir}/，而产出物清单第 {row} 行声明的是 "
                    + string.Join("、", allowed) + "：两边各写一套目录名，collect-real-machine.sh 会按清单那套报警（磁盘上多出"
                    + $"的 {dir}/ 判退出码 2），而照着本页放文件的人永远等不到它说「都齐了」。");
                Assert.True(Directory.Exists(Path.Combine(EvidenceRoot, dir)),
                    $"执行单第 {number} 步指向 docs/evidence/real-machine/{dir}/，磁盘上却没有这个目录：当天第一件事又变成建目录。");
            }
        }

        // ⑤ 页首：排不进的必须是第 3、4、5、6 条，且不许被读成「G-D5 走完了」。
        var intro = string.Join('\n', DrillLines().TakeWhile(l => !Regex.IsMatch(l, @"^\d+\.\s")));
        var excluded = Regex.Matches(intro, @"不含第\s*(?<n>\d)\s*条").Cast<Match>()
            .Select(m => int.Parse(m.Groups["n"].Value)).Distinct().OrderBy(n => n).ToList();
        Assert.True(excluded.Count == 4 && excluded.SequenceEqual(new[] { 3, 4, 5, 6 }),
            $"这一页没有逐条写明「排不进来的是动作单第 3、4、5、6 条」（现读到：{(excluded.Count == 0 ? "一条都没写" : string.Join("、", excluded))}）："
            + "半小时只收得下首程四站、三档截图、冒烟报告与那条核对；副屏、运行中改档、字体名核对、真实写入各缺一件硬前置。"
            + "不点名，「30 分钟走完」就会被读成「G-D5 走完」。");
        Assert.True(intro.Contains("更不等于 G-D5 达成", StringComparison.Ordinal),
            "页首没有「更不等于 G-D5 达成」那半句：半小时收完前四条动作里的两条，读的人会把这一页当成关闭 G-D5 的路径。");

        // ⑥ 让读的那两行报告标签，冒烟器真打得出来（指一行不打的字 = 当天空手）。
        var drill = string.Join('\n', DrillLines());
        var smoke = Repo.SmokeProgram;
        foreach (var label in DpiContextConsistencyTests.ReportLineLabels)
        {
            Assert.True(drill.Contains(label, StringComparison.Ordinal),
                $"执行单没有点到报告里「{label}」那一行：那一行才是当天分辨「真机像哪一个上下文」的唯一读数，"
                + "漏了它，第 4 步那两档的截图就只剩目测。");
            Assert.True(smoke.Contains(label, StringComparison.Ordinal),
                $"执行单让读「{label}」，而冒烟器源码里没有这几个字：指一行冒烟器根本不打的字，等于没指。");
        }

        // ⑦ 这一页自己点名本门，并写清它不判什么——有门不等于过了。
        var sheet = string.Join('\n', DrillLines());
        var self = $"{nameof(RealMachineEvidenceTests)}.{nameof(ThirtyMinuteDrill_EveryStepNamesACommandAndAnOutputFile)}";
        Assert.True(sheet.Contains(self, StringComparison.Ordinal),
            $"这一页没点名判它的那条门（{self}）：读了这一页的人不知道这七步的引用是被机械核过的、也没法找到那一门改判据。");
        Assert.True(sheet.Contains("不判真机有没有跑", StringComparison.Ordinal),
            "这一页没写「不判真机有没有跑」：七步走得通不等于七步走完了，这一句是这一页与「G-D5 已达成」之间唯一的隔断。");
    }
}
