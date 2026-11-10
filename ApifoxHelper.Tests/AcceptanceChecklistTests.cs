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

    private static List<Row> Rows()
    {
        var notes = Repo.Source("docs", "NOTES.md");
        var lines = notes.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == Heading);
        Assert.True(start >= 0, $"docs/NOTES.md 顶部缺「{Heading}」一节。");
        // 必须在最顶部：第一处 "## " 标题就得是它，否则「口径表」会被埋到几百行日志之后，读的人找不到。
        var firstHeading = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(start, firstHeading);
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var body = lines[start..(end < 0 ? lines.Length : end)];

        var rows = new List<Row>();
        foreach (var line in body)
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count != 5) continue;
            if (cells[0] != "[x]" && cells[0] != "[ ]") continue;   // 表头行与分隔行跳过
            rows.Add(new Row(cells[0], cells[1], cells[2], cells[3], cells[4]));
        }
        return rows;
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
}
