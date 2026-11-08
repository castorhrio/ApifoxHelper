using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 代码证据清单口径（DESIGN.md §6「列表行」），不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自任务目录 evidence.json（<c>EvidenceStore.Items</c>），按缺口条目的证据编号过滤，只读展示；
/// 输入取原始值（与 EndpointTrace、PropagationEntries、ApplyPlanEntries 同法，不绑模型类型）。
/// 四件事收在这里：清单三格文字（分类 / 证据摘要 / 文件:行）、计数行、片段区说明行与片段文字。
/// 这一屏存在的理由就是看代码：清单是「哪几条证据」，片段区是「那一条的代码」，
/// 两处文字必须同源——各拼一遍必然有一处先漂（与第七十七、七十九轮把标注列与计数行收进一处的理由相同）。
/// </summary>
public static class EvidenceEntries
{
    /// <summary>
    /// 片段区说明行：此前片段区是一块没有标题的框，打开时又是空的，看不出这一格是什么、
    /// 也看不出该点一行（与检查结果、核对传播、同步预览的底部区同一形状）。
    /// </summary>
    public const string SnippetCaption = "选中证据的代码片段（文件与行号见清单）";

    /// <summary>证据没有代码片段时如实说明，不留一块空白（§7「空态不留白」）。</summary>
    public const string EmptySnippetText = "（无代码片段）";

    /// <summary>
    /// 位置格：文件与行区间。这是位置而不是名字（标识列声明在对话框里，取的是「证据摘要」那一格），
    /// 但它是这一行在屏幕之外唯一能被指认的坐标，故与摘要同列展示。
    /// </summary>
    public static string LocationText(string file, int startLine, int endLine) =>
        file + ":" + startLine.ToString(CultureInfo.InvariantCulture)
        + "-" + endLine.ToString(CultureInfo.InvariantCulture);

    /// <summary>计数行：这一屏有几条证据。按整份过滤结果统计，不随选中变化。</summary>
    public static string CountsLine(int count) =>
        "证据 " + count.ToString(CultureInfo.InvariantCulture) + " 条";

    /// <summary>片段文字：空白即「无代码片段」，不把空串当成一段代码显示。</summary>
    public static string SnippetText(string? snippet) =>
        string.IsNullOrWhiteSpace(snippet) ? EmptySnippetText : snippet;

    /// <summary>
    /// 清单一行三格：分类（<see cref="TaskDisplay.EvidenceKindText"/> 与单元格同一份口径）、
    /// 证据摘要（标识列，Ctrl+C 复制到的就是这一格）、文件:行。
    /// 摘要缺失按空串处理——不拿位置或分类顶上（那会把「没有摘要」说成「摘要就是文件名」）。
    /// </summary>
    public static string[] Row(string kind, string? summary, string file, int startLine, int endLine) =>
        [TaskDisplay.EvidenceKindText(kind), summary ?? "", LocationText(file, startLine, endLine)];
}
