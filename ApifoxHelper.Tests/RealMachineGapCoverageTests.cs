using static ApifoxHelper.Tests.RealMachineGapRegistry;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// README「已知限制」点名的屏必须在 <see cref="GapRegistryPath"/> 的登记表里有行——登记完整性对账门
/// （第四百二十四轮，控制仓简报 P0-1）。
/// <para>
/// <b>补的是哪个缺口</b>：README「已知限制」第 1 条点名了多处「随 G-D5 复核」的屏——状态行「查看完整错误」
/// 入口、提示对话框「复制路径」按钮、200% 档提示行、多显示器混合缩放、状态行「已取消」/「正在停止」两句——
/// 这些屏的未验证状态 README 里挂了三十多轮的账，登记表里却一处都没有：「未验证交互点有界」那条门
/// （<c>RealMachineInteractionGapTests</c>）管的是「表里有这一行」的纪律（层名合法、轮次真有、计数现算），
/// <b>管不到「表里应当有哪些行」</b>。
/// </para>
/// <para>
/// <b>判据（两侧对账，两个方向都能红）</b>：① README 点名了却没登记 → 红；② 表里还有行而 README 已不再点名
/// → 红（点名没了行还在，这一行就成了没人认领的债）。<b>为什么是对账门而不是本族</b>：README 与登记表都是
/// 外部实物，名字的定义不在判据里（按 <c>LayoutMeasurementGateTests</c> 那五步的第 ② 步答「不是」）。
/// <b>行质量那一半判据（层名、轮次、计数）与「未验证交互点有界」那条门共用同一份
/// <c>RealMachineGapRegistry.UnverifiedPointProblems</c>，不养第二份</b>——这一条门喂的是同一张表，
/// 红的时候两条门一起红，说的也是同一件事。
/// </para>
/// <para>
/// <b>两类行的承认落点</b>：README 点名的屏（<see cref="KnownLimitScreens"/> 里 <c>ReadmeAnchored</c> 的那几处）
/// 存废跟着 README 走——验完了先摘掉 README 里的标记、再删行，同轮做；账本条目承认的新交互点
/// （词表外，如列表右键菜单的键盘入口）的承认落点在账本：首次登记轮次的条目必须写着「未验证」。
/// </para>
/// <para>
/// <b>第四百二十五轮按控制仓简报 P1-1 搬家</b>：本条门此前写在 <c>RealMachineSupplyGateTests.cs</c> 里，
/// 本轮拆成三个文件，本类只留这一族；<b>判据一条不放宽、断言逐字未动</b>（共用的行质量判据与解析器
/// 经 <c>using static</c> 按原名调用，不养第二份）。
/// </para>
/// </summary>
public class RealMachineGapCoverageTests
{
    /// <summary>
    /// 一张 README「已知限制」点名过的真机未验屏，与它在两侧的对账词。
    /// <para>
    /// <see cref="KnownLimitScreens"/> 在「已知限制」里<b>带真机未验标记的条目</b>（见 <see cref="UnverifiedMarkers"/>）
    /// 里找，登记侧在表的「交互点」格里找。<c>ReadmeAnchored</c> 为 <c>false</c> 的屏
    /// （列表右键菜单的键盘入口）不走 README 这一侧——它的承认落点在账本条目（第四百二十二轮），
    /// 那一侧由 <c>RealMachineGapRegistry.UnverifiedPointProblems</c> 判。
    /// </para>
    /// </summary>
    private sealed record KnownLimitScreen(
        string Name, string[] ReadmeKeys, string[] RegistryKeys, bool ReadmeAnchored = true);

    /// <summary>
    /// README「已知限制」点名过的屏（封闭词表）。README 点名了一张词表外的新屏时，
    /// <b>同轮要把新屏加进这张词表并在登记表里补行</b>——词表管不住词表外的屏，这是对账门过散文的已知边界，
    /// 不是可以静默放过的事。
    /// </summary>
    private static readonly KnownLimitScreen[] KnownLimitScreens =
    [
        new("状态行的「查看完整错误」入口", ["查看完整错误"], ["查看完整错误"]),
        new("提示对话框的「复制路径」按钮", ["复制路径"], ["复制路径"]),
        new("200% 档提示行", ["200% 档提示行"], ["200% 档提示行"]),
        new("多显示器混合缩放与运行中改缩放", ["多显示器混合缩放"], ["多显示器混合缩放"]),
        new("状态行的「已取消」与「正在停止…」两句", ["已取消", "正在停止"], ["已取消", "正在停止"]),
        new("列表右键菜单的键盘入口", [], ["右键菜单"], ReadmeAnchored: false),
    ];

    /// <summary>
    /// 「这一条还在挂真机的账」的标记：已知限制的条目里出现任一个，这一条点名的屏就是仍未验的屏。
    /// <para>
    /// <b>为什么靠标记而不靠屏名</b>：第 7 条（作业结束状态行）也提到「已取消」，但那一屏<b>模拟器量过</b>、
    /// 不欠真机的账——只认「点名」会把量过的屏也抓来要行。标记（随 G-D5 / 真机复核 / 没量过 / 仍未验证 /
    /// 真机仍缺）是「这一条自己承认还没验」的写法，README 里写了这些词的条目才是点名。
    /// </para>
    /// </summary>
    private static readonly string[] UnverifiedMarkers =
        ["随 G-D5", "真机复核", "真机未验证", "没量过", "仍未验证", "真机仍缺"];

    /// <summary>
    /// README「已知限制」点名的屏必须在 <c>docs/real-machine-gaps.md</c> 的登记表里有行（第四百二十四轮，
    /// 控制仓简报 P0-1）。
    /// </summary>
    [Fact]
    public void KnownLimitationScreens_MustBeRegisteredInTheGapTable()
    {
        var problems = GapCoverageProblems(
            Repo.Source("README.md"), Repo.Source("docs", "real-machine-gaps.md"), Repo.Source("docs", "NOTES.md"));
        Assert.True(problems.Count == 0,
            "README「已知限制」点名的屏与登记表对不上：\n  · " + string.Join("\n  · ", problems)
            + "\n改法：README 点名了却没登记的在 " + GapRegistryPath + " 补行（层名按实际情况填、"
            + "首次登记轮次写账本里真承认过的那一轮）；验完了先摘掉 README 里的标记再删行，同轮做；"
            + "README 点名词表外的新屏时，同轮把新屏加进 KnownLimitScreens 并补行——两处一处都不能省。");
    }

    /// <summary>
    /// 七发注入：合规两发必须<b>不报</b>，破坏五发必须<b>当场报</b>。
    /// <para>
    /// 与 <c>UnverifiedPointDetector_ReportsTheBreaksItMustCatch</c> 同一纪律：判据自己没被跑过，
    /// 比没有判据更贵。③④⑤⑥ 五发里凡是层名、轮次那些行质量的破坏，走的是与上一条门共用的那一份
    /// <c>RealMachineGapRegistry.UnverifiedPointProblems</c>——这里各喂一遍，验的是「这一条门真跑的那份组合判据」。
    /// </para>
    /// </summary>
    [Fact]
    public void GapCoverageDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：README 点名两处、表里两行、账本承认——一处都不报。
        Assert.Empty(GapCoverageProblems(
            KnownLimitSection(
                "- **真机供给现状**：三档真机截图仍未验证。",
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读：模拟器量到过入口出现与点开读全，真机弹出位置与观感量不到"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读：模拟器真点过那颗按钮（鼠标路径），按键路由与真机观感量不到")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。"))));

        // ② 合规：现读过一次、读数写清了是模拟器量的、计数从现读那一轮起算——不报。
        Assert.Empty(GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置", "第四百二十四轮", "第四百二十四轮", "0",
                    "wine 模拟器 `RunDialogStatusSweep` 那一支通过 / 0 失败，弹出位置这一层模拟器量不到"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由", "第四百二十四轮", "第四百二十四轮", "0",
                    "wine 模拟器 `RunNoticeCopyClick` 那一支通过 / 0 失败，按键路由这一层模拟器量不到")),
            GapLedger((424, "本轮现读一次（模拟器），真机未验证的两处文字出口读数记进 " + GapRegistryPath + "。"))));

        // ③ README 点名一处但表里没有：点名了却没登记，下一轮分不清这一层是判过了还是没人验过。
        var unregistered = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(unregistered);
        Assert.Contains("查看完整错误", unregistered[0], StringComparison.Ordinal);

        // ④ 表里多一行 README 没点名：点名没了行还在，这一行就成了没人认领的债。
        var unclaimed = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("多显示器混合缩放与运行中改缩放", "观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的屏登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(unclaimed);
        Assert.Contains("多显示器混合缩放", unclaimed[0], StringComparison.Ordinal);

        // ⑤ 层名写含糊话：行质量那一半判据（与上一条门共用）必须报。
        var vague = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "未验证", "第四百二十四轮", NeverReadMarker, "0", "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(vague);
        Assert.Contains("哪一层", vague[0], StringComparison.Ordinal);

        // ⑥ 首次登记轮次在账本里查不到：登记一个不存在的轮次，等于把登记变成自述。
        var phantom = GapCoverageProblems(
            KnownLimitSection(
                "- **两处文字出口：状态行的「查看完整错误」入口与提示对话框的「复制路径」按钮，都只在模拟器量过**"
                + "：剩下的账随 G-D5 真机复核。"),
            GapTable(
                GapRowLine("状态行的「查看完整错误」入口", "弹出位置、观感", "第四百二十轮", NeverReadMarker, "0",
                    "未现读"),
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的两处文字出口登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(phantom);
        Assert.Contains("账本", phantom[0], StringComparison.Ordinal);

        // ⑦ README 缺「已知限制」一节：点名的落点没了，两侧对账无从谈起。
        var noSection = GapCoverageProblems(
            "前文。\n\n## 使用流程\n\n正文。",
            GapTable(
                GapRowLine("提示对话框的「复制路径」按钮", "按键路由、观感", "第四百二十四轮", NeverReadMarker, "0",
                    "未现读")),
            GapLedger((424, "本轮把 README 点名的屏登记进 " + GapRegistryPath + "，真机未验证。")));
        Assert.Single(noSection);
        Assert.Contains("已知限制", noSection[0], StringComparison.Ordinal);
    }

    // ---------- 判据本体（第四百二十四轮，收成一处好让注入各喂一遍） ----------

    /// <summary>
    /// 判据本体：README 全文 + 登记表全文 + 账本全文进，问题清单出（空 = 两侧对得上）。
    /// <para>
    /// 覆盖那一半（README 点名的屏 ↔ 登记表的行）在这里判；行质量那一半（层名、轮次、计数）
    /// 原样走 <c>RealMachineGapRegistry.UnverifiedPointProblems</c>——同一张表不养两份判据。
    /// </para>
    /// </summary>
    private static List<string> GapCoverageProblems(string readme, string registry, string notes)
    {
        var problems = GapCoverageBreaks(readme, registry);
        problems.AddRange(UnverifiedPointProblems(registry, notes));
        return problems;
    }

    /// <summary>覆盖那一半：README「已知限制」点名的屏集合 ↔ 登记表的行集合，两个方向各判一次。</summary>
    private static List<string> GapCoverageBreaks(string readme, string registry)
    {
        var problems = new List<string>();
        var lines = readme.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## 已知限制");
        if (start < 0)
        {
            problems.Add("README.md 里找不到「## 已知限制」一节：真机缺口公开声明的落点没了，"
                         + "「README 点名的屏 ↔ 登记表的行」两侧对账无从谈起。");
            return problems;
        }

        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var markedItems = lines[(start + 1)..(end < 0 ? lines.Length : end)]
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Where(item => UnverifiedMarkers.Any(m => item.Contains(m, StringComparison.Ordinal)))
            .ToList();
        var rows = GapRows(registry);

        foreach (var screen in KnownLimitScreens)
        {
            if (!screen.ReadmeAnchored) continue;

            var named = markedItems.Any(item =>
                screen.ReadmeKeys.Any(k => item.Contains(k, StringComparison.Ordinal)));
            var hasRow = rows.Any(r =>
                screen.RegistryKeys.Any(k => r.Point.Contains(k, StringComparison.Ordinal)));

            if (named && !hasRow)
            {
                problems.Add($"README「已知限制」点名了{screen.Name}（条目里带着真机未验的标记），"
                             + "登记表里没有它的行：点名了却没登记，下一轮读的人分不清这一层是判过了"
                             + "还是从来没人验过——在表里补一行，层名按实际情况填。");
            }

            if (!named && hasRow)
            {
                problems.Add($"登记表里有{screen.Name}那一行，而 README「已知限制」带真机未验标记的条目"
                             + "已不再点名它：点名没了行还在，这一行就成了没人认领的债——"
                             + "验完了就把行删掉（真机证据归档进 docs/evidence/real-machine/ 之后），"
                             + "并在当轮条目里写清删的是哪一行。");
            }
        }

        return problems;
    }

    /// <summary>夹具：造一份「已知限制」一节（已知限制是最后一节，取到文末）。</summary>
    private static string KnownLimitSection(params string[] items) =>
        "前文。\n\n## 已知限制\n\n" + string.Concat(items.Select(i => i + "\n"));
}
