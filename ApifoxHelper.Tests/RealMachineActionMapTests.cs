using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「第 N 条动作 → 哪几个归档目录」的映射门（第二百二十四轮，控制仓简报 P0-1）。
/// <para>
/// <b>要解决的问题</b>：动作单 <b>7 条</b>、归档目录 <b>9 个</b>——两个数不等。第 2 条一条动作占三档三个目录
/// （<c>100/</c>、<c>125/</c>、<c>200/</c>），其余六条各一个。这个映射此前只散在动作单「归档到哪里」那一格的
/// 散文里，真机到手第一天照着 7 条打勾，<b>最容易漏的正是第 2 条多出来的那两个目录</b>——而漏了不会有人当场发现，
/// 因为「7 条都勾了」看起来就是齐了。这一轮把映射写成 <c>docs/NOTES.md</c> 的一行一条，本文件把它钉住。
/// </para>
/// <para>
/// <b>判五件事</b>（与 <c>docs/NOTES.md</c> 那一节开头写的五条逐条对应）：
/// ① 表在册——7 行动作 + 1 行合计，合计行写的两个数等于实际解析出的动作数与目录数，每行「个数」列等于该行点到的目录数；
/// ② 每个归档目录都至少被一条动作点到；③ 每条动作至少点到一个归档目录；
/// ④ 点到的目录名在 <c>docs/evidence/real-machine/</c> 下真实存在；
/// ⑤ 点到的目录名能在 README 产出物清单的<b>同一动作行</b>里找到，且反向也成立（README 该行点到的目录，这一节一个不少；
/// 动作单「归档到哪里」那一格同样双向对齐）。
/// </para>
/// <para>
/// <b>它不是第二份清单</b>：目录名与文件名仍只有一份出处——<c>docs/evidence/real-machine/README.md</c> 的产出物清单
/// （<c>collect-real-machine.sh</c> 从那张表现读）。这一节只补「动作编号 → 目录名」这一列，文件名一个都不抄；
/// ⑤ 就是让「抄了却漂了」当场变红的那一条。
/// </para>
/// <para>
/// <b>它不判什么</b>：<b>不判真机有没有跑</b>（本机没有 Windows 图形子系统），也<b>不判目录里有没有东西</b>——
/// 「有没有东西」是 <c>.winecheck/collect-real-machine.sh</c> 与
/// <see cref="RealMachineEvidenceTests.CheckedAction_RequiresItsArchiveDirectoryToHoldFiles"/> 的账。
/// 有门不等于过了：G-D5 在验收表上仍是 <c>[ ]</c>。
/// </para>
/// </summary>
public class RealMachineActionMapTests
{
    /// <summary>映射表那一节的标题（<c>docs/NOTES.md</c>）。</summary>
    private const string MapHeading = "### 动作 → 归档目录（一行一条，照这个数点数，第二百二十四轮）";

    private static string EvidenceRoot => Path.Combine(Repo.Root, "docs", "evidence", "real-machine");

    /// <summary>映射表的一行：动作号（合计行为 0）/ 目录格 / 个数格。</summary>
    private readonly record struct MapRow(int Action, string DirCell, string CountCell, string Raw);

    /// <summary>映射表那一节的正文行（标题行到下一个 <c>### </c> 或 <c>## </c> 之前）。</summary>
    private static string[] MapLines()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == MapHeading);
        Assert.True(start >= 0,
            $"docs/NOTES.md 缺「{MapHeading}」一节：动作单 7 条与归档目录 9 个的映射没有落点，"
            + "真机到手第一天只能靠人脑记「第 2 条对应哪三个目录」。");
        var end = Array.FindIndex(lines, start + 1,
            l => l.StartsWith("### ", StringComparison.Ordinal) || l.StartsWith("## ", StringComparison.Ordinal));
        return lines[start..(end < 0 ? lines.Length : end)];
    }

    /// <summary>映射表的行（动作行 + 合计行）。格式错的行不许被静默跳过。</summary>
    private static List<MapRow> MapRows()
    {
        var rows = new List<MapRow>();
        foreach (var line in MapLines())
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count == 0) continue;
            if (cells[0] == "动作") continue;                       // 表头
            if (Regex.IsMatch(cells[0], @"^-+$")) continue;          // 分隔行
            Assert.True(cells.Count == 3,
                $"映射表有一行不是 3 列（实际 {cells.Count} 列，应为 动作 / 归档目录 / 个数）：{cells[0]}。"
                + "格式错的行不许被解析器静默跳过——跳过的那一行正是当天没人点数的那一条。");

            if (cells[0] == "合计")
            {
                rows.Add(new MapRow(0, cells[1], cells[2], line));
                continue;
            }

            var m = Regex.Match(cells[0], @"^(\d+)\.");
            Assert.True(m.Success,
                $"映射表有一行的动作格既不是「N. …」也不是「合计」：{cells[0]}。"
                + "动作号是这张表唯一的连接键，认不出来就等于这一行没人判。");
            rows.Add(new MapRow(int.Parse(m.Groups[1].Value), cells[1], cells[2], line));
        }

        return rows;
    }

    /// <summary>映射表里一条动作点到的归档目录名（复用 README 那一侧的解析口径，不另写一套正则）。</summary>
    private static List<string> DirsIn(string cell) => RealMachineEvidenceTests.DirectoriesIn(cell);

    /// <summary>
    /// ① 表在册：7 行动作 + 1 行合计，合计行写的两个数等于实际解析出的动作数与目录数，
    /// 每行「个数」列等于该行点到的目录数。
    /// <para>
    /// 这一条挡的是「表被改残了却仍像一张表」：删掉一行动作、把「个数」列改成手写的数、
    /// 或合计行写成「7 条动作 → 8 个目录」，都在这里红。
    /// </para>
    /// </summary>
    [Fact]
    public void MapTable_HasSevenActionRowsAndATotalRowThatAddsUp()
    {
        var rows = MapRows();
        var actions = rows.Where(r => r.Action > 0).ToList();
        var totals = rows.Where(r => r.Action == 0).ToList();

        // ① 7 行动作，编号 1–7 各一次。
        Assert.Equal(Enumerable.Range(1, 7), actions.Select(r => r.Action).OrderBy(n => n));

        // ② 每行「个数」列 == 该行点到的目录数（手写的数与点到的名字对不上即红）。
        foreach (var row in actions)
        {
            var dirs = DirsIn(row.DirCell);
            Assert.True(dirs.Count > 0,
                $"映射表第 {row.Action} 条一个归档目录都没点到：这一条当天归档到哪里，表上没人说得出来。");
            Assert.True(int.TryParse(row.CountCell, out var declared),
                $"映射表第 {row.Action} 条的「个数」列不是数字：{row.CountCell}。");
            Assert.True(declared == dirs.Count,
                $"映射表第 {row.Action} 条写「{declared} 个」，实际点到 {dirs.Count} 个（{string.Join("、", dirs)}）——"
                + "手写的数与点到的名字对不上，当天照哪个数点数都会漏。");
        }

        // ③ 合计行：一行，且两个数都等于实际解析出来的数。
        Assert.True(totals.Count == 1,
            $"映射表应有且只有一行「合计」（实际 {totals.Count} 行）：7 条动作与 9 个目录这两个数不等，"
            + "合计行是当天唯一能一眼看出「还差两个目录」的那一行。");
        var total = totals[0];
        var distinctDirs = actions.SelectMany(r => DirsIn(r.DirCell)).Distinct(StringComparer.Ordinal).ToList();
        Assert.Contains($"{actions.Count} 条动作", total.DirCell, StringComparison.Ordinal);
        Assert.Contains($"{distinctDirs.Count} 个目录", total.DirCell, StringComparison.Ordinal);
        Assert.True(int.TryParse(total.CountCell, out var totalDeclared) && totalDeclared == distinctDirs.Count,
            $"合计行的「个数」列写的是「{total.CountCell}」，实际解析出 {distinctDirs.Count} 个目录。");
    }

    /// <summary>
    /// ② 每个归档目录都至少被一条动作点到。
    /// <para>
    /// 地板清单 <see cref="RealMachineEvidenceTests.ExpectedDirectories"/> 那九个名字逐个判：
    /// 少一个就是「有一个目录当天没人认领」——它空着也不会有人发现，因为 7 条动作看起来都勾了。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryArchiveDirectory_IsClaimedByAtLeastOneAction()
    {
        var claimed = MapRows().Where(r => r.Action > 0)
            .SelectMany(r => DirsIn(r.DirCell))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var expected in RealMachineEvidenceTests.ExpectedDirectories)
        {
            Assert.True(claimed.Contains(expected),
                $"映射表里没有任何一条动作点到 {expected}/：这个目录当天没人认领，空着也不会有人发现。"
                + "第 2 条一条动作占三档三个目录，正是最容易漏的那两个。");
        }

        // 反向：表里不许出现地板清单之外的目录名（多一个就是多一个没人判的落点）。
        var extra = claimed.Where(d => !RealMachineEvidenceTests.ExpectedDirectories.Contains(d, StringComparer.Ordinal)).ToList();
        Assert.True(extra.Count == 0,
            "映射表点了地板清单之外的目录：" + string.Join(" ／ ", extra.Select(d => d + "/"))
            + "。九个目录是封闭的（" + string.Join(" / ", RealMachineEvidenceTests.ExpectedDirectories) + "）。");
    }

    /// <summary>
    /// ③ 每条动作至少点到一个归档目录（与 ② 成对：② 管目录有没有人认领，③ 管动作有没有落点）。
    /// </summary>
    [Fact]
    public void EveryAction_ClaimsAtLeastOneArchiveDirectory()
    {
        foreach (var row in MapRows().Where(r => r.Action > 0))
        {
            Assert.True(DirsIn(row.DirCell).Count > 0,
                $"映射表第 {row.Action} 条一个归档目录都没点到：这一条跑完，产出物没有落点。");
        }
    }

    /// <summary>
    /// ④ 点到的目录名在 <c>docs/evidence/real-machine/</c> 下真实存在。
    /// <para>
    /// 这一条挡的是「表里写了一个磁盘上没有的名字」——当天照表归档会先卡在「目录不存在」，
    /// 而现场没有时间查是表错了还是目录被删了。目录名只有一份出处（README 的产出物清单），
    /// 磁盘上的九个目录由 <see cref="RealMachineEvidenceTests.EveryArchiveDirectory_ExistsOnDisk"/> 判存在，
    /// 这里判的是「表点到的名字」与「磁盘上的名字」是同一批。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryClaimedDirectory_ExistsOnDisk()
    {
        foreach (var dir in MapRows().Where(r => r.Action > 0).SelectMany(r => DirsIn(r.DirCell)).Distinct(StringComparer.Ordinal))
        {
            var full = Path.Combine(EvidenceRoot, dir);
            Assert.True(Directory.Exists(full),
                $"映射表点到 docs/evidence/real-machine/{dir}/，磁盘上没有这个目录——"
                + "当天照表归档会先卡在「目录不存在」。先跑 bash .winecheck/prepare-real-machine.sh 的第 ③ 步建目录，"
                + "或把表里的名字改成磁盘上真有的那一个（目录名只有一份出处：README 的产出物清单）。");
        }
    }

    /// <summary>
    /// ⑤ 映射表与 README 产出物清单、动作单「归档到哪里」那一格，三处逐条双向对齐。
    /// <para>
    /// 这一条是「不许抄第二份清单」的落点：映射表是给人当天点数用的第三处视图，它一旦与 README 或动作单漂开，
    /// 当天照哪一份都会漏。三个方向都判——映射表多一个、README 多一个、动作单多一个，都红。
    /// </para>
    /// </summary>
    [Fact]
    public void MapTable_AgreesWithTheReadmeAndTheActionListRowByRow()
    {
        var map = MapRows().Where(r => r.Action > 0).ToDictionary(r => r.Action, r => DirsIn(r.DirCell));
        var readme = RealMachineEvidenceTests.ArtifactRows().ToDictionary(r => r.Action, r => DirsIn(r.DirCell));
        var actionList = RealMachineEvidenceTests.ActionRows().ToDictionary(r => r.Number, r => DirsIn(r.Archive));

        foreach (var (action, dirs) in map)
        {
            Assert.True(readme.ContainsKey(action), $"映射表第 {action} 条在 README 产出物清单里没有对应行。");
            Assert.True(actionList.ContainsKey(action), $"映射表第 {action} 条在动作单里没有对应条目。");

            var inReadme = readme[action].ToHashSet(StringComparer.Ordinal);
            var inActionList = actionList[action].ToHashSet(StringComparer.Ordinal);
            var mine = dirs.ToHashSet(StringComparer.Ordinal);

            var missingInReadme = mine.Except(inReadme, StringComparer.Ordinal).ToList();
            Assert.True(missingInReadme.Count == 0,
                $"映射表第 {action} 条点到这些目录，README 产出物清单该行却没有："
                + string.Join(" ／ ", missingInReadme.Select(d => d + "/"))
                + "。README 是目录名与文件名的唯一出处，映射表不许自己发明一个。");

            var missingInMap = inReadme.Except(mine, StringComparer.Ordinal).ToList();
            Assert.True(missingInMap.Count == 0,
                $"README 第 {action} 行点到这些目录，映射表第 {action} 条却没点："
                + string.Join(" ／ ", missingInMap.Select(d => d + "/"))
                + "。当天照映射表点数就会漏掉它们——这正是这一节要防的那一步。");

            var missingInActionList = mine.Except(inActionList, StringComparer.Ordinal).ToList();
            Assert.True(missingInActionList.Count == 0,
                $"映射表第 {action} 条点到这些目录，动作单该行「归档到哪里」却没有："
                + string.Join(" ／ ", missingInActionList.Select(d => d + "/")) + "。三处视图改一处漏一处即红。");

            var missingInMapFromActionList = inActionList.Except(mine, StringComparer.Ordinal).ToList();
            Assert.True(missingInMapFromActionList.Count == 0,
                $"动作单第 {action} 条点到这些目录，映射表却没点："
                + string.Join(" ／ ", missingInMapFromActionList.Select(d => d + "/")) + "。");
        }
    }
}
