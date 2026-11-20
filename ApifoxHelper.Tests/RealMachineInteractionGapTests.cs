using static ApifoxHelper.Tests.RealMachineGapRegistry;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「真机未验证的交互点必须有界登记」的门（第四百二十三轮，控制仓简报 P0-1）。
/// <para>
/// <b>补的是哪个缺口</b>：第四百二十二轮给列表右键菜单补上了键盘入口（<c>Shift+F10</c> / 菜单键），
/// 那是<b>真机按键路由</b>这一层第一次有新代码；而「真机供给未读」的有界门
/// （<c>RealMachineSupplyGateTests</c>）只判「真机供给未读」的有界性，
/// <b>判不到「真机未验证的功能点」有没有登记</b>——一轮加了新交互、在条目里写一句「真机未验证」就过去了，
/// 下一轮读的人分不清「这一层验过了」还是「从来没人验过」，也没有任何东西会红。
/// 登记的落点是 <see cref="GapRegistryPath"/> 那一页（六格表：交互点 / 未验证的是哪一层 /
/// 首次登记轮次 / 最近一次现读轮次 / 连续未验证轮次 / 现读读数）。
/// </para>
/// <para>
/// <b>判据（三条，全部落在那一页的表上）</b>：① 每一行必须写清未验证的是哪一层（只认
/// <see cref="GapLayers"/> 那三个词）；② 连续未验证轮次数必须<b>能从账本现算出来</b>——
/// 那一格必须等于「账本最新一轮 − 最近一次现读轮次」（从未现读过的从首次登记轮次起算），自己填一个数不算数；
/// ③ 上限 <see cref="MaxConsecutiveUnverified"/> 轮，超过即红，那一轮必须现读一次、把读数写进那一行。
/// 另两条边界：表不许为空（0 行 = 这笔债被静默丢掉）；首次登记轮次必须是账本里真有的那一轮，
/// 且那一轮的条目里写着「未验证」（登记一个从没承认过这件事的轮次，等于把登记变成自述）。
/// </para>
/// <para>
/// <b>为什么这一条不是「假门」</b>：它判的是<b>外部实物</b>——那一页的表是给人读的登记，定义不在判据里
/// （按 <c>LayoutMeasurementGateTests</c> 那五步的第 ② 步，答「不是」），所以它是对账门。
/// 抓不抓得住由 <see cref="UnverifiedPointDetector_ReportsTheBreaksItMustCatch"/> 拿九发注入当场验。
/// </para>
/// <para>
/// <b>第四百二十五轮按控制仓简报 P1-1 搬家</b>：本条门与判据本体（<c>RealMachineGapRegistry.UnverifiedPointProblems</c>）
/// 此前写在 <c>RealMachineSupplyGateTests.cs</c> 里，那一文件三合一近四轮长了 680 行——本轮拆成三个文件，
/// 本类只留这一族；<b>判据一条不放宽、断言逐字未动</b>（判据本体经 <c>using static</c> 按原名调用，
/// 共用解析器不养第二份）。
/// </para>
/// </summary>
public class RealMachineInteractionGapTests
{
    /// <summary>
    /// 每一轮新增的真机未验证交互点都必须在 <c>docs/real-machine-gaps.md</c> 登记，且写清未验证的是哪一层、
    /// 连着几轮没验（第四百二十三轮，控制仓简报 P0-1）。
    /// </summary>
    [Fact]
    public void UnverifiedInteractionPoints_MustBeRegisteredWithLayer()
    {
        var problems = UnverifiedPointProblems(
            Repo.Source("docs", "real-machine-gaps.md"), Repo.Source("docs", "NOTES.md"));
        Assert.True(problems.Count == 0,
            "真机未验证的交互点登记不成立：\n  · " + string.Join("\n  · ", problems)
            + "\n改法：在 " + GapRegistryPath + " 那张表里按行登记——未验证的是哪一层只认 "
            + string.Join(" / ", GapLayers) + " 这三个词；连续未验证轮次数按账本现算（最新一轮 − 最近一次现读轮次，"
            + "从未现读过的从首次登记轮次起算）；到第 " + MaxConsecutiveUnverified + " 轮为止可以只登记，"
            + "第 " + (MaxConsecutiveUnverified + 1) + " 轮必须现读一次并把读数写进那一行。");
    }

    /// <summary>
    /// 九发注入：合规两发必须<b>不报</b>，破坏七发必须<b>当场报</b>。
    /// <para>
    /// 与 <c>RealMachineSupplyGateTests.Detector_ReportsTheBreaksItMustCatch</c> 同一纪律：
    /// 判据自己没被跑过，比没有判据更贵——一条永远为真的门，绿得再好看也不挡任何事。
    /// </para>
    /// </summary>
    [Fact]
    public void UnverifiedPointDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 合规：登记了、层写清了、计数按账本现算对得上、没到上限。
        Assert.Empty(UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1",
                "未现读：模拟器量不到按键路由这一层")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。"))));

        // ② 合规：现读过一次，读数写清了是模拟器还是真机，计数从现读那一轮起算。
        Assert.Empty(UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由、弹出位置", "第四百二十二轮", "第四百二十三轮", "0",
                "wine 模拟器 `RunListContextMenu` 四支通过 / 0 失败；按键路由这一层模拟器量不到")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮现读一次，真机未验证的交互点登记在 " + GapRegistryPath + "。"))));

        // ③ 层写成含糊话：读不出「这一层到底量没量得到」，下一轮照样要重判一次。
        var vague = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "未验证", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(vague);
        Assert.Contains("哪一层", vague[0], StringComparison.Ordinal);

        // ④ 计数自己填一个数：门按账本现算，填错即红。
        var miscounted = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "0", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(miscounted);
        Assert.Contains("现算", miscounted[0], StringComparison.Ordinal);

        // ⑤ 连续未验证到第 4 轮：超过上限，这一轮必须现读一次。
        var overCap = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "4", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。"),
                      (424, "本轮无新增。"), (425, "本轮无新增。"),
                      (426, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(overCap);
        Assert.Contains("上限", overCap[0], StringComparison.Ordinal);
        Assert.Contains("现读", overCap[0], StringComparison.Ordinal);

        // ⑥ 首次登记轮次在账本里查不到：登记一个不存在的轮次，等于把登记变成自述。
        var phantom = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十轮", NeverReadMarker, "3", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(phantom);
        Assert.Contains("账本", phantom[0], StringComparison.Ordinal);

        // ⑦ 首次登记那一轮的条目里没有「未验证」：那一轮从没承认过这件事。
        var unadmitted = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(unadmitted);
        Assert.Contains("未验证", unadmitted[0], StringComparison.Ordinal);

        // ⑧ 表为空：这笔债被静默丢掉。
        var empty = UnverifiedPointProblems(
            GapTable(),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮把真机未验证的交互点登记进 " + GapRegistryPath + "。")));
        Assert.Single(empty);
        Assert.Contains("一行登记都读不出来", empty[0], StringComparison.Ordinal);

        // ⑨ 最新一轮承认了「真机未验证」却没指回这一页：登记表不是唯一落点。
        var unpointed = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", NeverReadMarker, "1", "未现读")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮真机未验证的交互点照旧。")));
        Assert.Single(unpointed);
        Assert.Contains(GapRegistryPath, unpointed[0], StringComparison.Ordinal);

        // ⑩ 现读过却没写清是模拟器还是真机：只写「已复核」不算读数。
        var uncalibrated = UnverifiedPointProblems(
            GapTable(GapRowLine("列表右键菜单的键盘入口", "按键路由", "第四百二十二轮", "第四百二十三轮", "0", "已复核")),
            GapLedger((422, "本轮给列表右键菜单补上键盘入口，真机未验证。"),
                      (423, "本轮现读一次，真机未验证的交互点登记在 " + GapRegistryPath + "。")));
        Assert.Single(uncalibrated);
        Assert.Contains("模拟器", uncalibrated[0], StringComparison.Ordinal);
    }
}
