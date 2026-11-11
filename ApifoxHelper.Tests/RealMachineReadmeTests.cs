using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 真机到手第一天的「读表路径」桌面推演（第一百八十一轮，控制仓简报 P0-2）。
/// <para>
/// <b>要解决的问题</b>：<c>docs/evidence/real-machine/README.md</c> 已经写清「每个目录里该有哪几个文件、
/// 每个文件里该有哪几个数字、缺哪个数就算没跑」，但这份表<b>从没在真实流程上走过一遍</b>——
/// 真机当天第一次读它的人（很可能就是答辩人本人）要现场理解。机器可能只借一天，
/// <b>当天卡住 = G-D5 继续挂空</b>，而卡住的原因往往不是没写，是写得「读不出来」：
/// 「截图若干」建不出目录、「观感良好」判不出过没过、「记几个数」填不出数字。
/// </para>
/// <para>
/// <b>这一轮补的是哪一层</b>：<see cref="RealMachineEvidenceTests"/> 判「登记与产出物对得上」（两边同名、
/// 量测项逐项点到、勾了必须有文件）；本文件判的是<b>另一件事</b>——<b>一个没读过源码的人能不能照着这张表
/// 建目录、放文件、填数字、判过没过</b>。逐行判三条：
/// ① 产出物是<b>具体文件名模式</b>（带 <c>.png</c> / <c>.json</c> / <c>.txt</c> 这类后缀，不是「截图若干」）；
/// ② 「必须有的数字」里至少有一个<b>具体数值示例</b>（如「客户区宽 1400px」）——只写「记宽高」，
/// 现场量到的数该写成什么形状仍要人猜；
/// ③ 「什么算过」是<b>可判定的</b>（不含「观感良好」这类主观词）。
/// </para>
/// <para>
/// <b>它不判什么</b>：<b>不判真机跑没跑</b>（本机没有 Windows 图形子系统，跑没跑只有人知道），
/// 也不判表里的判据<b>对不对</b>（那是 <see cref="RealMachineEvidenceTests"/> 与动作单的事）。
/// 它只保证「这张表读得出来」——<b>读得出来不等于过了</b>，G-D5 在验收表上仍是 <c>[ ]</c>。
/// </para>
/// </summary>
public class RealMachineReadmeTests
{
    private const string ReadmeRelative = "docs/evidence/real-machine/README.md";

    private static string ReadmePath => Path.Combine(Repo.Root, "docs", "evidence", "real-machine", "README.md");

    /// <summary>产出物清单的一行：动作 / 归档目录 / 产出物 / 必须有的数字 / 什么算过（量测项列本文件不判）。</summary>
    private readonly record struct Row(int Action, string Dir, string Files, string Numbers, string Pass);

    private static List<Row> Rows()
    {
        Assert.True(File.Exists(ReadmePath),
            $"缺 {ReadmeRelative}：真机到位当天第一份要读的文件不在，这张表读不出来。");
        var rows = new List<Row>();
        foreach (var line in File.ReadAllText(ReadmePath).Split('\n'))
        {
            if (!line.TrimStart().StartsWith("|", StringComparison.Ordinal)) continue;
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count == 0 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            Assert.True(cells.Count == 6,
                $"README 产出物清单有一行不是 6 列（实际 {cells.Count} 列）：{cells[0]}。列数错的行读表的人会读串格。");
            rows.Add(new Row(int.Parse(cells[0]), cells[1], cells[2], cells[3], cells[4]));
        }

        Assert.Equal(Enumerable.Range(1, 7), rows.Select(r => r.Action).OrderBy(n => n));
        return rows;
    }

    /// <summary>产出物里认得出的文件后缀（封闭清单：真机当天要放进目录的就是这几类文件）。</summary>
    private static readonly string[] ArtifactExtensions = [".png", ".json", ".jsonl", ".txt", ".md", ".csv"];

    /// <summary>
    /// 「产出物」列里点到的文件名（反引号里的那一串），只留带后缀的那些。
    /// </summary>
    private static List<string> NamedFiles(string cell) =>
        Regex.Matches(cell, "`([^`]+)`")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(name => ArtifactExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>
    /// ① 每行的产出物都是具体文件名模式（第一百八十一轮，简报 P0-2）。
    /// <para>
    /// <b>为什么这条要机械判</b>：真机当天第一件事是<b>建目录、放文件</b>。表里写「截图若干」，
    /// 现场就得自己决定截几张、叫什么名字——而归档目录是按文件名与文件数判的
    /// （<see cref="RealMachineEvidenceTests.CheckedAction_RequiresItsArchiveDirectoryToHoldFiles"/>），
    /// 现场自起的名字既不会被判据看见，也不会被下一个人找到。
    /// </para>
    /// <para>
    /// 判三件事：① 每行至少点到 <b>1 个</b>带后缀的文件名（第 7 行只有一份报告，一份就够）；
    /// ② 七行合计至少 <b>20 个</b>（当前 23 个）——地板挡住「把文件名整列删成一句话」那种改法；
    /// ③ 不许出现「若干 / 之类 / 等等」这类模糊量词——那正是「现场自己编」的入口。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRow_NamesConcreteArtifactFiles()
    {
        var total = 0;
        foreach (var row in Rows())
        {
            var files = NamedFiles(row.Files);
            total += files.Count;
            Assert.True(files.Count >= 1,
                $"README 第 {row.Action} 行的「产出物」一个带后缀的文件名都没点到"
                + $"（认得出的后缀：{string.Join(" / ", ArtifactExtensions)}）：真机当天建不出目录、放不对文件。");

            foreach (var vague in new[] { "若干", "之类", "等等", "一些", "几张" })
            {
                Assert.False(row.Files.Contains(vague, StringComparison.Ordinal),
                    $"README 第 {row.Action} 行的「产出物」里有模糊量词「{vague}」：现场得自己决定截几张、叫什么名字，"
                    + "而归档是按文件名与文件数判的——写成具体文件名模式。");
            }
        }

        Assert.True(total >= 20,
            $"README 产出物清单合计只点到 {total} 个带后缀的文件名（地板 20）：清单被删残了，"
            + "真机当天会少建目录、少放文件。");
    }

    /// <summary>
    /// ② 每行的「必须有的数字」里至少有一个具体数值示例（第一百八十一轮，简报 P0-2）。
    /// <para>
    /// <b>为什么这条要机械判</b>：表里写「记客户区宽高、文字宽 / 可用宽」，读表的人知道<b>要量什么</b>，
    /// 但不知道<b>写出来该是什么形状</b>——「1400×860」还是「宽 1400px、高 860px」？「988 / 1243 / 1576px」
    /// 还是「988px、1243px、1576px」？真机当天现编一种写法，下一个人（或答辩现场的自己）就得重新猜一遍。
    /// 一个具体示例把这件事定死。
    /// </para>
    /// <para>
    /// 判据是「有一个带单位的数或一个小数」：<c>1400px</c>、<c>2 条</c>、<c>7 段</c>、<c>2.0</c> 都算。
    /// <b>示例不是读数</b>——它只示范写法，真机当天照这个形状填机器上量到的数（wine 的基线数在表里另有标注）。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRow_GivesAtLeastOneConcreteNumberExample()
    {
        foreach (var row in Rows())
        {
            var hasExample = Regex.IsMatch(row.Numbers, @"\d+(?:\.\d+)?\s*(?:px|×|条|个|张|%|档|段|行|屏|次)")
                || Regex.IsMatch(row.Numbers, @"\d+\.\d+");
            Assert.True(hasExample,
                $"README 第 {row.Action} 行的「必须有的数字」里没有一个具体数值示例（如「客户区宽 1400px」「计划内 2 条」）："
                + "读表的人知道要量什么，却不知道写出来该是什么形状，真机当天只能现编一种写法。");
        }
    }

    /// <summary>
    /// ③ 每行的「什么算过」是可判定的（第一百八十一轮，简报 P0-2）。
    /// <para>
    /// <b>为什么这条要机械判</b>：这一列是<b>唯一决定「勾不勾」的那一列</b>。写成「观感良好」「看着舒服」，
    /// 现场就只能凭感觉判——而 G-D5 的勾选框一旦凭感觉勾上，整条真机证据链就作废了
    /// （同一纪律见 <see cref="WordingHonestyTests"/>）。
    /// </para>
    /// <para>
    /// 判据是禁词表：出现「观感 / 美观 / 好看 / 舒适 / 顺眼 / 感觉 / 差不多 / 大致 / 基本正常 / 良好 / 漂亮 / 舒服 / 大体 / 肉眼」
    /// 任一即红。禁词表是封闭的，加词要同时改这里——<b>宁可判得粗，也不让「凭感觉」混进判据列</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRow_PassCriterionIsDecidable()
    {
        string[] subjective =
        [
            "观感", "美观", "好看", "舒适", "顺眼", "感觉", "差不多", "大致", "基本正常", "良好", "漂亮", "舒服", "大体", "肉眼",
        ];

        foreach (var row in Rows())
        {
            Assert.True(row.Pass.Length >= 8,
                $"README 第 {row.Action} 行的「什么算过」是空的：这一列决定勾不勾，空着等于没有判据。");
            foreach (var word in subjective)
            {
                Assert.False(row.Pass.Contains(word, StringComparison.Ordinal),
                    $"README 第 {row.Action} 行的「什么算过」里有主观词「{word}」：这一列决定勾不勾，"
                    + "凭感觉判出来的勾会把整条真机证据链作废——改写成可判定的条件（量什么、等于什么、差多少）。");
            }
        }
    }

    /// <summary>
    /// 每行的归档目录都是<b>全路径</b>（第一百八十一轮，简报 P0-2）。
    /// <para>
    /// 真机当天第一件事是建目录。表里只写「journey/」，读表的人不知道它相对谁——
    /// 建到仓库根、建到桌面、建到 <c>docs/</c> 下，三种都有人干过。全路径把这件事定死。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRow_NamesAFullArchivePath()
    {
        foreach (var row in Rows())
        {
            Assert.Contains("docs/evidence/real-machine/", row.Dir, StringComparison.Ordinal);
        }
    }
}
