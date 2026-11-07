using System.Globalization;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 接口追踪明细的口径，不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// 数据来自扫描落盘 evidence.json 的 Coverage（EndpointCoverage：调用链读取的方法、未解析调用、
/// 外部调用与单接口方法预算是否超限），只读展示；输入取原始值（与 ScanOverview 同法，不绑模型类型）。
/// 调用链在方法预算或深度限制处收手属于分析降级：预算超限只有出现在界面上，用户才能解释
/// 「为什么这个接口的类型追踪或证据不完整」，与「明确降级、不按同名猜实现」同一要求。
/// </summary>
public static class EndpointTrace
{
    /// <summary>列表单元格文字：计数为零显示「—」（没有可看的内容），其余为数字（列表行本身等宽）。</summary>
    public static string CountText(int count) => count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "—";

    /// <summary>预算列文字与色调：超限用警示文字色（颜色 + 文字双通道），正常显示「—」。</summary>
    public static (string Text, string Tone) BudgetCell(bool exceeded) =>
        exceeded ? ("超限", "warn") : ("—", "muted");

    /// <summary>
    /// 选中接口的追踪明细：方法清单、逐条未解析调用（表达式 — 原因，重载候选一并如实列出）、
    /// 外部调用（不可回写，与「分析警告与降级」同一前缀口径）、预算超限说明与调整入口、证据数。
    /// 各段为空时如实省略；全部为空时说明该接口没有可追踪的实现细节。
    /// </summary>
    public static List<string> DetailLines(
        string endpointKey,
        IReadOnlyCollection<string> methodsRead,
        IReadOnlyCollection<UnresolvedLine> unresolved,
        IReadOnlyCollection<string> externalCalls,
        bool budgetExceeded,
        int evidenceCount)
    {
        var lines = new List<string> { "接口：" + endpointKey };
        if (budgetExceeded)
        {
            lines.Add("单接口分析预算用尽，调用链追踪提前收手，预算后的调用未读取；可在项目设置「序列化与分析」中调大「方法预算」后重新扫描。");
        }
        lines.Add(methodsRead.Count > 0
            ? "读取的方法（" + methodsRead.Count + "）：" + string.Join("、", methodsRead)
            : "没有读取到方法体（方法体可能为空或不可达）。");
        if (unresolved.Count > 0)
        {
            lines.Add("未解析的调用（" + unresolved.Count + "）：");
            lines.AddRange(unresolved.Select(u =>
            {
                var line = "· " + u.Expression + "：" + u.Reason;
                return u.Candidates.Count > 0 ? line + "（候选：" + string.Join(" / ", u.Candidates) + "）" : line;
            }));
        }
        if (externalCalls.Count > 0)
        {
            lines.Add("外部调用（外部类型，不可回写）：" + string.Join("、", externalCalls));
        }
        lines.Add("引用证据：" + (evidenceCount > 0 ? evidenceCount + " 条（点「查看代码证据」对照）" : "无"));
        return lines;
    }

    /// <summary>未解析调用行：表达式、原因与重载候选（对话框从落盘数据投影为纯文字后传入）。</summary>
    public sealed record UnresolvedLine(string Expression, string Reason, IReadOnlyCollection<string> Candidates);
}
