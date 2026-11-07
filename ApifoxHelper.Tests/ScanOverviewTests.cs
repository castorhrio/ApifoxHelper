using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 扫描总览与降级警告的口径（冻结页面与原生界面读同一份落盘数据，ScanOverview 单一落点）：
/// 覆盖率行按冻结页面同一算法——比例先四舍五入到 4 位再换算百分数，空标签与占位文字不计为完成；
/// 分析等级与扫描时间来自 source-manifest.json，缺失时整段省略（不显示空标签）；
/// 降级清单的未解析符号与外部类型带前缀、与扫描警告区分（未解析不按同名猜实现、外部类型不可回写）；
/// 标题按可见条目计数——冻结页面标题不计外部类型，只有外部类型时会显示「（0）」，原生不显示自相矛盾的 0。
/// </summary>
public class ScanOverviewTests
{
    private static readonly DateTimeOffset ScannedAt = new(2026, 11, 7, 19, 38, 0, TimeSpan.FromHours(8));

    // ── 覆盖率行 ──────────────────────────────────────────────────────────

    [Fact]
    public void CoverageText_RatioCountsValidAndInheritDoc()
    {
        var text = ScanOverview.CoverageText(valid: 3, inheritDoc: 1, total: 8, "Debug", ScannedAt);
        Assert.Equal(
            "注释覆盖率 50% · 有效 4 / 8 个应对外说明的位置（空标签与占位文字不计为完成）· 分析等级 Debug · 扫描于 2026-11-07 19:38",
            text);
    }

    [Theory]
    [InlineData(2, 0, 3, "66.7%")]  // 0.6667 → 66.7
    [InlineData(1, 0, 3, "33.3%")]  // 0.3333 → 33.3
    [InlineData(2, 0, 7, "28.6%")]  // 0.2857 → 28.6
    [InlineData(5, 0, 5, "100%")]   // 整数百分数不带小数位
    [InlineData(0, 0, 0, "0%")]     // 没有注释位置
    public void CoverageText_PercentRoundsAfterFourDecimals(int valid, int inheritDoc, int total, string expected)
    {
        var text = ScanOverview.CoverageText(valid, inheritDoc, total, null, null);
        Assert.StartsWith("注释覆盖率 " + expected + " ·", text);
    }

    [Fact]
    public void CoverageText_OmitsMissingManifestParts()
    {
        // 旧任务可能没有清单文件：等级与时间缺失时如实省略该段，不显示「分析等级 」「扫描于 」空标签。
        var text = ScanOverview.CoverageText(3, 1, 8, null, null);
        Assert.DoesNotContain("分析等级", text);
        Assert.DoesNotContain("扫描于", text);
        Assert.EndsWith("（空标签与占位文字不计为完成）", text);
    }

    [Fact]
    public void CoverageText_BlankLevelCountsAsMissing()
    {
        var text = ScanOverview.CoverageText(3, 1, 8, "  ", ScannedAt);
        Assert.DoesNotContain("分析等级", text);
        Assert.Contains("扫描于 2026-11-07 19:38", text);
    }

    // ── 降级清单 ──────────────────────────────────────────────────────────

    [Fact]
    public void DegradationItems_KeepsWarningsAndPrefixesUnresolvedAndExternal()
    {
        var items = ScanOverview.DegradationItems(
            new[] { "引用不完整：Demo.Api 缺少 project.assets.json" },
            new[] { "Demo.Shared.IHelper", "Demo.Shared.Legacy" },
            new[] { "Newtonsoft.Json.Linq.JObject" });
        Assert.Equal(
        [
            "引用不完整：Demo.Api 缺少 project.assets.json",
            "未解析：Demo.Shared.IHelper",
            "未解析：Demo.Shared.Legacy",
            "外部类型（不可回写）：Newtonsoft.Json.Linq.JObject",
        ], items);
    }

    [Fact]
    public void DegradationItems_NullLists_AreTreatedAsEmpty()
    {
        Assert.Empty(ScanOverview.DegradationItems(null, null, null));
    }

    [Fact]
    public void DegradationItems_UnresolvedAlwaysPrefixed()
    {
        // 前缀是「颜色之外的另一条通道」：用户必须能区分扫描自己报告的问题与没解析到的符号。
        var items = ScanOverview.DegradationItems([], ["Demo.Shared.IHelper"], []);
        Assert.Equal(["未解析：Demo.Shared.IHelper"], items);
    }

    [Fact]
    public void DegradationItems_ExternalAlwaysPrefixed()
    {
        var items = ScanOverview.DegradationItems([], [], ["Newtonsoft.Json.Linq.JObject"]);
        Assert.Equal(["外部类型（不可回写）：Newtonsoft.Json.Linq.JObject"], items);
    }

    // ── 标题 ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "分析警告与降级（1）")]
    [InlineData(12, "分析警告与降级（12）")]
    public void Headline_CountsVisibleItems(int count, string expected) =>
        Assert.Equal(expected, ScanOverview.Headline(count));

    [Fact]
    public void Headline_ExternalOnlyNeverShowsZero()
    {
        // 冻结页面标题只计警告与未解析符号：只有外部类型时显示「分析警告与降级（0）」；
        // 原生按可见条目计数，标题不出现 0（按钮也只在有可见条目时出现）。
        var items = ScanOverview.DegradationItems(null, null, ["Demo.External.ThirdPartyDto"]);
        Assert.DoesNotContain("（0）", ScanOverview.Headline(items.Count));
    }

    // ── 文案禁词 ──────────────────────────────────────────────────────────

    [Fact]
    public void UserVisibleTexts_NoForbiddenWords()
    {
        var texts = new[]
        {
            ScanOverview.CoverageText(3, 1, 8, "Debug", ScannedAt),
            ScanOverview.CoverageText(0, 0, 0, null, null),
            ScanOverview.Headline(2),
        };
        foreach (var text in texts)
            Assert.False(Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
    }
}
