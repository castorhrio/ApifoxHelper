using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// <c>docs/CRITIQUE.md</c>（逐屏观感 critique）的门（第二百零五轮，简报 P0-1）。
/// <para>
/// <b>补的是哪个缺口</b>：冲第一口径下「观感 critique 缺失」是仅剩两条实质缺口之一，而第一百五十五轮那次
/// critique 写在 <c>docs/NOTES.md</c> 的一节里、按四组屏合并成表（「注释缺口 / 候选审阅 / 契约 / … 无」），
/// 逐屏读不到结论。一份能照着干活的 critique 必须逐站给「现状 / 像生成物或模板脸的地方 / 这一屏最该改的一处」，
/// 并且第三条点名一个真实存在的文件与控件——否则下一轮照着它改的时候找不到东西，它就退化成又一段感想。
/// </para>
/// <para>
/// <b>判什么</b>：① 十站齐、编号连续、每站三条都在；② 每一站都点到至少一个<b>仓库里真的存在</b>的
/// <c>.cs</c> 文件，全文出现的每一个 <c>Xxx.cs</c> 也都resolve（编一个不存在的文件名即红）；
/// ③ 每一站的「像生成物 / 模板脸」那条要么直接答「没有」，要么点名到真实文件（不许写「有点模板感」这种
/// 无法核对的话）；④ 「最该改的一处」只许点具名 pass 名册里的一个（或明写「判不动」并交代那一步等的是
/// 真机读数），且每站必须交代这一处的去向（本轮已落地 / 本轮未动 / 下一轮 / 留下一轮 / 判不动）；
/// ⑤ 全文无空话禁词；⑥ 证据口径那一行必须写明模拟器不是真机；⑦ 声称「本轮已落地」的 pass 必须
/// 在源码里真有其符号（不许 critique 写完不干活）。
/// <b>它判不了措辞</b>：第二百零五轮两次实测——「略生硬」后面接一串真实文件名、以及「本轮已落地」同条里
/// 别处提到过的文件名，两种变异都仍然全绿。近似取法的措辞门会被下一次改措辞绕过，所以这里不留这种断言，
/// 只钉「落点必须存在」「去向必须交代」这两件机械判得到的事。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判结论对不对、不判观感好坏——那是人的判断，机械判不了也不假装判得了。
/// 它只保证这份文件里每一个指头点下去都点得到实物，让它与「优化 UI」这类无法执行的批评区别开。
/// </para>
/// </summary>
public class CritiqueDocTests
{
    private const string DocPath = "docs/CRITIQUE.md";

    /// <summary>具名 pass 名册（docs/方案.md §6.2 #14）；「不动」是一个合法结论，但必须交代读数在哪一格等。</summary>
    private static readonly string[] PassRoster = ["quieter", "distill", "typeset", "layout", "clarify", "harden", "polish"];

    private static readonly Lazy<Dictionary<string, string>> ExistingCsFiles = new(() =>
        Directory.EnumerateFiles(Repo.Root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/"))
            .GroupBy(f => Path.GetFileName(f), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));

    private static string Doc => Repo.Source(DocPath);

    /// <summary>每一站：编号、屏名、正文。</summary>
    private static List<(int Number, string Title, string Body)> Stations()
    {
        var lines = Doc.ReplaceLineEndings("\n").Split('\n');
        var starts = Enumerable.Range(0, lines.Length)
            .Select(i => (Index: i, Match: Regex.Match(lines[i], @"^## 第 (\d+) 站 · (.+)$") ))
            .Where(x => x.Match.Success)
            .ToList();
        var stations = new List<(int, string, string)>();
        for (var k = 0; k < starts.Count; k++)
        {
            var end = k + 1 < starts.Count ? starts[k + 1].Index : lines.Length;
            var body = string.Join('\n', lines[starts[k].Index..end]);
            stations.Add((int.Parse(starts[k].Match.Groups[1].Value), starts[k].Match.Groups[2].Value.Trim(), body));
        }

        return stations;
    }

    /// <summary>① 十站齐、编号从 1 连续、每站三条都在（现状 / 像生成物 / 最该改的一处）。</summary>
    [Fact]
    public void Critique_HasTenStationsEachWithTheThreeJudgments()
    {
        var stations = Stations();
        Assert.Equal(10, stations.Count);
        foreach (var (_, title, _) in stations)
        {
            Assert.False(title.Length == 0, "有一站没有屏名。");
        }

        Assert.Equal(Enumerable.Range(1, 10), stations.Select(s => s.Number));
        foreach (var (number, _, body) in stations)
        {
            foreach (var label in new[] { "**现状**：", "**像生成物 / 模板脸**：", "**最该改的一处**：" })
            {
                Assert.Contains(label, body, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// ② 每一站都点到真实存在的 <c>.cs</c>，且全文出现的每一个 <c>Xxx.cs</c> 都能在仓库里找到——
    /// critique 的价值全在「指头点下去点得到实物」，编一个文件名或写一个已改名的文件即红。
    /// </summary>
    [Fact]
    public void EveryStation_CitesARealCsFile_AndNoCitationDangles()
    {
        foreach (var (number, _, body) in Stations())
        {
            var names = CitedCsFiles(body);
            Assert.True(names.Count > 0, $"第 {number} 站没有点到任何 <c>.cs</c> 文件——这一站的结论没法照着找东西。");
            Assert.True(names.Any(Exists),
                $"第 {number} 站点到的 .cs 没有一个在仓库里（{string.Join("、", names)}）——critique 必须点名真实文件。");
        }

        foreach (var name in CitedCsFiles(Doc))
        {
            Assert.True(Exists(name), $"{DocPath} 引用了 {name}，仓库里没有这个文件（改名或删除后这一行要跟着改，别指着不存在的源码）。");
        }
    }

    /// <summary>
    /// ③ 「像生成物 / 模板脸」这一条：要么直接答「没有」，要么点名到真实文件。
    /// 「有点模板感」「略生硬」这种话既不能核对也不能执行，正是这份文件要区别于感想文的地方。
    /// </summary>
    [Fact]
    public void EveryTemplateFaceVerdict_IsEitherNoneOrNamesAFile()
    {
        foreach (var (number, _, body) in Stations())
        {
            var line = LabeledLine(body, "**像生成物 / 模板脸**：");
            Assert.False(line.Length == 0, $"第 {number} 站的「像生成物 / 模板脸」那条是空的。");
            if (line.StartsWith("没有", StringComparison.Ordinal))
            {
                Assert.True(line.Length > 8, $"第 {number} 站答「没有」却没说凭什么没有（只写两个字等于没判）。");
                continue;
            }

            Assert.True(CitedCsFiles(line).Any(Exists),
                $"第 {number} 站说有模板脸，但那一整条里没点到真实存在的 .cs（判到却没有落点，下一轮照着改时找不到东西）：{Trim(line)}");
            Assert.True(line.Length > 20, $"第 {number} 站的「有」这一条太短，说不出像在哪：{Trim(line)}");
            // 诚实记下这条门的边界：它判不了措辞。「略生硬」后面接上一串真实文件名，机械上分辨不出来
            // （第二百零五轮实测：按第一句取法的变异仍然全绿——句子太长、落点就在同一句里）。
            // 钉措辞的门最后只会被措辞绕过，所以这里只钉「落点必须存在」这一件能判的事。
        }
    }

    /// <summary>
    /// ④ 「最该改的一处」只许点具名 pass 名册里的一个（或明写「不动」并交代那一步等的是真机读数），
    /// 并且每一站都必须交代这一处的去向——本轮已落地 / 本轮未动 / 留下一轮（下一轮） / 判不动，五者之一。
    /// 批评完了不说做没做，下一轮就得把十站重读一遍才知道账在哪，那正是这份文件与感想文的区别。
    /// </summary>
    [Fact]
    public void EveryShouldFixNamesANamedPassOrSaysItStays()
    {
        var verdicts = new[] { "本轮已落地", "本轮未动", "下一轮", "留下一轮", "判「不动」" };
        foreach (var (number, _, body) in Stations())
        {
            var line = LabeledLine(body, "**最该改的一处**：");
            Assert.False(line.Length == 0, $"第 {number} 站的「最该改的一处」那条是空的。");
            var named = PassRoster.FirstOrDefault(p => line.Contains(p, StringComparison.Ordinal));
            if (named is null)
            {
                Assert.Contains("判「不动」", line, StringComparison.Ordinal);
                Assert.Contains("G-D5", line, StringComparison.Ordinal);
            }
            else
            {
                Assert.True(line.Length > 30,
                    $"第 {number} 站点名了 {named} 却没说这一处具体改什么（一个 pass 名不等于一条判断）：{Trim(line)}");
            }

            Assert.True(verdicts.Any(line.Contains),
                $"第 {number} 站说了「该改」却没说这一处的去向（{string.Join("、", verdicts)} 之一）——账要留在文件里，不能只留一句话。");
            // 「声称本轮已落地的那一条还得点名文件」这一子在第二百零五轮实测挡不住：同一条里别处提到过的
            // 文件名就会让它绿（变异删掉落点文件后仍全绿）。能硬判的那一半放在下面的
            // ClaimedLanding_ExistsInSource——它直接去源码里核那个符号在不在，不靠措辞。
        }

        // 名册本身要与方案里的名册一致：这里抄一份就得同步改，所以核一次出处。
        var plan = Repo.Source("docs", "方案.md").Replace("`", string.Empty);
        foreach (var pass in PassRoster)
        {
            Assert.True(Regex.IsMatch(plan, @"(^|[^\w])" + pass + @"([^\w]|$)"),
                $"docs/方案.md §6.2 #14 的名册里没有 {pass}——本文件的 pass 名册与出处对不上，改名要两处一起改。");
        }
    }

    /// <summary>⑤ 全文无空话禁词（与界面文案同一套 <see cref="CopyRules.BannedWords"/>，不另写一份清单）。</summary>
    [Fact]
    public void Critique_UsesNoBannedSloganWords()
    {
        var text = Doc;
        foreach (var word in CopyRules.BannedWords)
        {
            Assert.False(text.Contains(word, StringComparison.Ordinal),
                $"{DocPath} 里有空话禁词「{word}」——这份文件批评别人像生成物，自己先不能用这种词。");
        }
    }

    /// <summary>⑥ 证据口径在位：引用的读数只有 wine 报告与源码字面量两种来源，且写明模拟器不是真机。</summary>
    [Fact]
    public void Critique_StatesTheEvidenceCaliberAndKeepsTheRealMachineGap()
    {
        var text = Doc;
        Assert.Contains("不是 Windows 真机结果", text, StringComparison.Ordinal);
        Assert.Contains("demo.sh", text, StringComparison.Ordinal);
        Assert.Contains("真机 0 台", text, StringComparison.Ordinal);
        Assert.Contains("**没有第三条路**", text, StringComparison.Ordinal);
        Assert.Contains("docs/NOTES.md", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⑦ 声称本轮落地的 pass 必须真在源码里：critique 与提交之间不许断裂。
    /// 第二百零五轮落地的是 typeset 那一处（<c>Ui.CountCell</c>），第二百零六轮落地的是 distill 那一处
    /// （<c>Ui.HintRow</c>）——**按轮累加，不清零**：后一轮不许把前一轮声称过的东西覆盖掉，
    /// 所以两处都还在这份文件里、两个符号也都还得在源码里。
    /// 而「本文没动的」那一节也得真列出没动的东西（不许写完判断就把账抹平）。
    /// </summary>
    [Fact]
    public void ClaimedLanding_ExistsInSource_AndTheUnfinishedListIsNotEmpty()
    {
        var landed = Regex.Match(Doc, @"## 本轮落地的 pass");
        Assert.True(landed.Success, $"{DocPath} 缺「## 本轮落地的 pass」一节：判断与交付之间的关系要写在同一份文件里。");
        Assert.Contains("Ui.CountCell", Doc, StringComparison.Ordinal);
        Assert.Contains("CountCellTests", Doc, StringComparison.Ordinal);
        Assert.True(Repo.Source("Desktop", "Native", "Ui.cs").Contains("ListViewSubItem CountCell(", StringComparison.Ordinal),
            "critique 说本轮落地了 Ui.CountCell，而 Ui.cs 里没有这个符号——文档不许跑在代码前面。");
        Assert.True(File.Exists(Path.Combine(Repo.Root, "ApifoxHelper.Tests", "CountCellTests.cs")),
            "critique 点名的门文件 ApifoxHelper.Tests/CountCellTests.cs 不在测试工程里。");

        // 第二百零六轮那一处（distill，第 1 站的第三条）。
        Assert.Contains("Ui.HintRow", Doc, StringComparison.Ordinal);
        Assert.Contains("HintRowTests", Doc, StringComparison.Ordinal);
        Assert.True(Repo.Source("Desktop", "Native", "Ui.cs").Contains("Label HintRow(TableLayoutPanel grid, string text)", StringComparison.Ordinal),
            "critique 说第二百零六轮落地了 Ui.HintRow，而 Ui.cs 里没有这个符号——文档不许跑在代码前面。");
        Assert.True(File.Exists(Path.Combine(Repo.Root, "ApifoxHelper.Tests", "HintRowTests.cs")),
            "critique 点名的门文件 ApifoxHelper.Tests/HintRowTests.cs 不在测试工程里。");

        var pending = Regex.Match(Doc, @"## 本文没动的[^\n]*\n([\s\S]+)$");
        Assert.True(pending.Success, $"{DocPath} 缺「本文没动的」那一节：九处没动的判断要留在文件里，不能只留做掉的那一处。");
        Assert.True(Regex.Matches(pending.Groups[1].Value, @"(?m)^\d+\. ").Count >= 5,
            "「本文没动的」少于五条——十站里动手的只是少数站、少数处，剩下的账必须逐条排好，否则下一轮又得把这十站重读一遍。");
    }

    private static List<string> CitedCsFiles(string text) =>
        Regex.Matches(text, @"[A-Za-z0-9_\-.:/\\]+\.cs").Cast<Match>()
            .Select(m => Path.GetFileName(m.Value.Replace('\\', '/')))
            .Distinct().ToList();

    private static bool Exists(string name) => ExistingCsFiles.Value.ContainsKey(name);

    /// <summary>取某一条标签那一行的内容（跨到下一空行为止，判断可能写两三句）。</summary>
    private static string LabeledLine(string body, string label)
    {
        var at = body.IndexOf(label, StringComparison.Ordinal);
        Assert.True(at >= 0, $"正文里没有「{label}」这一条。");
        var start = at + label.Length;
        var end = body.IndexOf("\n\n", start, StringComparison.Ordinal);
        return body[start..(end < 0 ? body.Length : end)].Replace("\n", " ").Trim();
    }

    private static string Trim(string line) => line.Length <= 48 ? line : line[..48] + "…";
}
