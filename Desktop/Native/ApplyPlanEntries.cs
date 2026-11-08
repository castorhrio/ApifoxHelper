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
/// 第一百一十二轮把同一个对话框的跳过清单并入同一册（<see cref="SkippedKeywordFields"/>）：
/// 那一张是整份计划里没能生成修改的注释位置，要按原因处理就得先把同一原因的集中看。
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
    /// 跳过清单的关键词匹配字段（DESIGN.md §6「列表筛选」，第一百一十二轮）：那一行在屏幕上被读作的那两格——
    /// 跳过的位置与原因。顺序与清单两列一致（跳过的位置 | 原因），界面不另造一套字段。
    /// 原因进关键词是这一屏的要害：用户问的正是「为什么这个位置没被处理」，而答案就在这一格里
    /// （「文件不是 UTF-8 编码」「扫描后源码或证据文件已变化」「无法定位声明」…）。
    /// 同一个对话框里文件清单自第七十九轮起就有关键词框、跳过清单没有，属同壳不同行为。
    /// </summary>
    public static string?[] SkippedKeywordFields(string display, string reason) => [display, reason];

    /// <summary>
    /// 差异预览区的说明行（DESIGN.md §6「列表行」，第九十四轮）：这一屏的核心是逐文件预览统一 diff，
    /// 而那块只读框此前没有标题——另六处底部/详情预览区都有（检查结果、核对传播、代码证据、同步预览两处、
    /// 本对话框的跳过清单），同一只壳里同一个动作在别处有、在这一处没有，属同壳不同行为。
    /// 说明行讲清三件事：这块框是什么（选中文件的注释差异）、改的是什么（只改 /// 注释行）、
    /// 与回写的关系（与回写时写入的内容一致）——用户不必先点一行再猜。
    /// </summary>
    public const string DiffCaption = "选中文件的注释差异（只改 /// 注释行；与回写时写入的内容一致）";

    /// <summary>
    /// 计数行：文件总数、注释处数与跳过数。按整份计划统计、不随关键词变化——
    /// 「跳过 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来。
    /// </summary>
    public static string CountsLine(int files, int edits, int skipped) =>
        "文件 " + files.ToString(CultureInfo.InvariantCulture)
        + " · 注释 " + edits.ToString(CultureInfo.InvariantCulture)
        + " · 跳过 " + skipped.ToString(CultureInfo.InvariantCulture);
}
