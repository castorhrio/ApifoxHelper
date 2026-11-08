using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释回写的文件清单口径（DESIGN.md §6「列表筛选」），不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自任务目录 comment-plan.json（CommentPlan.Files：源码文件、回写范围、注释处数、回写结果），只读展示；
/// 输入取原始值（与 EndpointTrace、PropagationEntries 同法，不绑模型类型）。
/// 三件事收在这里：回写范围列文字（清单第二格与关键词匹配共用一份）、关键词匹配的字段、计数行文字。
/// 这一屏的清单是整份计划的源码文件（真实项目一个模块几十个文件），要找到「上次那个文件」只能逐行扫；
/// 另九张长清单在第六十五至七十八轮都有关键词框，同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 这一屏没有筛选下拉（回写结果是逐文件结果、不是可选状态），故走只有关键词框的那一支。
/// </summary>
public static class ApplyPlanEntries
{
    /// <summary>
    /// 回写范围列文字：允许回写 / 只导出补丁。清单单元格与关键词匹配取同一份，不各写一套。
    /// </summary>
    public static string ScopeText(bool writeAllowed) => writeAllowed ? "允许回写" : "只导出补丁";

    /// <summary>
    /// 关键词匹配的字段：那一行在屏幕上被读作的那三格——源码文件、回写范围、回写结果。
    /// 注释处数是数字、不进关键词（与另几张清单的计数列同一取舍）；
    /// 差异内容在下方预览框而不在这一行，不进。
    /// </summary>
    public static string?[] KeywordFields(string relativePath, string scopeText, string resultText) =>
        [relativePath, scopeText, resultText];

    /// <summary>
    /// 计数行：文件总数、注释处数与跳过数。按整份计划统计、不随关键词变化——
    /// 「跳过 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来。
    /// </summary>
    public static string CountsLine(int files, int edits, int skipped) =>
        "文件 " + files.ToString(CultureInfo.InvariantCulture)
        + " · 注释 " + edits.ToString(CultureInfo.InvariantCulture)
        + " · 跳过 " + skipped.ToString(CultureInfo.InvariantCulture);
}
