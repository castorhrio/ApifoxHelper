using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 生成文档核对条目的口径（DESIGN.md §6「列表筛选」），不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自任务目录 propagation.json（PropagationResult.Entries：位置、标注、三环状态），只读展示；
/// 输入取原始值（与 EndpointTrace 同法，不绑模型类型）。
/// 三件事收在这里：标注列文字（清单第二格与关键词匹配共用一份）、关键词匹配的字段、表头计数行文字。
/// 这一屏存在的理由正是「注释有没有真的写进源码、出现在 XML、被 Swagger 读到」——三环状态文字因此
/// 进关键词（这一屏没有筛选下拉，与接口追踪明细同一支），要找到「XML 缺失」的那几条只能逐行扫。
/// </summary>
public static class PropagationEntries
{
    /// <summary>
    /// 标注列文字：参数位置带参数名（「参数 id」），其余取 <see cref="TaskDisplay.SlotTagText"/>
    /// （说明/返回/未知原值）。清单单元格与关键词匹配取同一份，不各写一套。
    /// </summary>
    public static string TagText(string tag, string? parameterName) =>
        tag == "param" && parameterName is { } name ? "参数 " + name : TaskDisplay.SlotTagText(tag);

    /// <summary>
    /// 关键词匹配的字段：那一行在屏幕上被读作的那五格——位置、标注、源码、XML、Swagger。
    /// 三环状态文字进关键词（这一屏没有筛选下拉可管它们，不进关键词就等于筛不到）；
    /// 说明文字、Swagger 对应位置与备注在详情区而不在这一行，不进。
    /// </summary>
    public static string?[] KeywordFields(string display, string tagText, string sourceText, string xmlText, string swaggerText) =>
        [display, tagText, sourceText, xmlText, swaggerText];

    /// <summary>
    /// 表头计数行：位置总数与三环各自「已到位」的条数（源码已写入、XML 存在、Swagger 已读取）。
    /// 按整份核对数据统计、不随关键词变化——「XML 0」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来。
    /// </summary>
    public static string CountsLine(int entries, int sourceWritten, int xmlPresent, int swaggerPresent) =>
        "位置 " + entries.ToString(CultureInfo.InvariantCulture)
        + " · 源码 " + sourceWritten.ToString(CultureInfo.InvariantCulture)
        + " · XML " + xmlPresent.ToString(CultureInfo.InvariantCulture)
        + " · Swagger " + swaggerPresent.ToString(CultureInfo.InvariantCulture);
}
