using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 扫描总览与降级警告的口径，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自扫描落盘的 symbols.json（ScanOutput 的 Coverage、UnresolvedSymbols、ExternalTypes）
/// 与 source-manifest.json（SourceManifest 的 AnalysisLevel、ScannedAt、Warnings），只读展示；
/// 冻结页面在源码页显示同一份数据。降级只有让用户看见才算「明确降级」：
/// 未解析符号不按同名猜实现，外部类型上的注释位置不可回写，这些限制必须出现在界面上。
/// </summary>
public static class ScanOverview
{
    /// <summary>
    /// 注释覆盖率行：比例（一位小数，与冻结页面同一算法：比例先四舍五入到 4 位再换算百分数）、
    /// 有效位置数与总位置数、分析等级与扫描时间——等级与时间来自清单文件，缺失时如实省略该段。
    /// 空标签与占位文字不计为完成（与冻结页面同一口径）。
    /// </summary>
    public static string CoverageText(int valid, int inheritDoc, int total, string? analysisLevel, DateTimeOffset? scannedAt)
    {
        var ratio = total == 0 ? 0 : Math.Round((double)(valid + inheritDoc) / total, 4);
        var percent = (Math.Round(ratio * 1000) / 10).ToString("0.#", CultureInfo.InvariantCulture);
        var text = $"注释覆盖率 {percent}% · 有效 {valid + inheritDoc} / {total} 个应对外说明的位置（空标签与占位文字不计为完成）";
        if (!string.IsNullOrWhiteSpace(analysisLevel)) text += "· 分析等级 " + analysisLevel;
        if (scannedAt is { } time) text += " · 扫描于 " + time.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>降级清单条目：警告原样保留，未解析符号加「未解析：」前缀、外部类型加「外部类型（不可回写）：」前缀（与冻结页面同一前缀）。三类清单都可为 null（旧任务缺字段时按无处理）。</summary>
    public static List<string> DegradationItems(IReadOnlyCollection<string>? warnings, IReadOnlyCollection<string>? unresolved, IReadOnlyCollection<string>? externalTypes)
    {
        var items = new List<string>();
        if (warnings is { } ws) items.AddRange(ws);
        if (unresolved is { } us) items.AddRange(us.Select(w => "未解析：" + w));
        if (externalTypes is { } es) items.AddRange(es.Select(w => "外部类型（不可回写）：" + w));
        return items;
    }

    /// <summary>
    /// 入口标题：条目总数括注。冻结页面标题只计警告与未解析符号、外部类型不计入标题，
    /// 只有外部类型时会显示「分析警告与降级（0）」；原生按可见条目计数，不显示自相矛盾的 0。
    /// </summary>
    public static string Headline(int itemCount) => "分析警告与降级（" + itemCount + "）";
}
