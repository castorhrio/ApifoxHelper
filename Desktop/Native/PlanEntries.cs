using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览写入清单的口径（DESIGN.md §6「列表筛选」），不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自任务目录 plan.json（SyncPlan.Actions：动作、资源、远端 ID、改动、CLI 校验、状态），只读展示；
/// 输入取原始值（与 ApplyPlanEntries、EndpointTrace、PropagationEntries 同法，不绑模型类型）。
/// 三件事收在这里：改动列文字（清单第五格与关键词匹配共用一份）、关键词匹配的字段、计划计数行文字。
/// 这一屏的清单是整份冻结计划的逐动作写入清单（真实项目几十上百个接口与模型），
/// 要找到「上次那个动作」「失败的那几条」只能逐行扫；另十张长清单在第六十五至七十九轮都有关键词框，
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 这一屏没有筛选下拉（动作状态是逐动作结果、不是可选状态），故走只有关键词框的那一支。
/// </summary>
public static class PlanEntries
{
    /// <summary>
    /// 改动列文字：逐条改动（「；」分隔）；没有改动时退回写入字段清单；两者都没有是「—」。
    /// 清单单元格与关键词匹配取同一份，不各写一套（两处各写一套必然漂移：敲「写入字段」筛不到那一行）。
    /// </summary>
    public static string ChangesText(IReadOnlyList<string> changes, IReadOnlyList<string> managedFields)
    {
        if (changes.Count > 0) return string.Join("；", changes);
        return managedFields.Count > 0 ? "写入字段：" + string.Join(", ", managedFields) : "—";
    }

    /// <summary>
    /// CLI 校验列文字：有校验结果时是结果原文，还没有校验结果时是「—」。
    /// 单元格与排序键取同一份原始值（<see cref="SyncPlanSort.ActionKey"/> 的校验列参数就是这里的入参），
    /// 不各写一套——「—」是占位不是值，排序时按「还没有校验结果」处理（交 null，升序排在最前）。
    /// </summary>
    public static string ValidationText(string validation) =>
        string.IsNullOrWhiteSpace(validation) ? "—" : validation;

    /// <summary>
    /// 关键词匹配的字段：那一行在屏幕上被读作的那四格——动作、资源、改动、状态。
    /// 动作序号（第几步）与远端 ID 是数字、不进关键词（与另几张清单的计数列同一取舍）；
    /// CLI 校验那一列几乎全是「通过」，不进；写入内容在下方预览框而不在这一行，不进。
    /// </summary>
    public static string?[] KeywordFields(string kindText, string resourceKey, string changesText, string stateText) =>
        [kindText, resourceKey, changesText, stateText];

    /// <summary>
    /// 计划计数行：接口数、模型数与排除数。按整份计划统计、不随关键词变化——
    /// 「排除 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来。
    /// </summary>
    public static string CountsLine(int endpoints, int models, int excluded) =>
        "接口 " + endpoints.ToString(CultureInfo.InvariantCulture)
        + " · 模型 " + models.ToString(CultureInfo.InvariantCulture)
        + " · 排除 " + excluded.ToString(CultureInfo.InvariantCulture);
}
