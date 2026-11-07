namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 主页列表列宽的口径（docs/DESIGN.md §6「列表行」）：冻结页面的表格是流式布局、任何窗口宽度都完整可见；
/// 原生列表固定像素列宽合计超过可视宽度时列尾被横向滚动条藏住（默认窗口宽度下任务列表的「更新时间」不可见），
/// 加宽窗口时列尾又留大片空白。规则：标识列（首列）吸收剩余可视宽度——列合计始终对齐可视宽度，
/// 不出横向滚动条，多出的宽度给最长会截断的标识文字（Windows 列表惯例，如资源管理器的名称列）；
/// 可视宽度放不下其余固定列合计时，标识列回退到自身设计宽度、横向滚动条如实出现。
/// 这里只做计算，「首列吸收」的约定在参数顺序里；接线在两个主页（Resize 与重建后调用），应用与快照的
/// WinForms 侧不进本文件——本文件不依赖 WinForms，与测试工程共享编译。
/// </summary>
public static class ListColumns
{
    /// <summary>
    /// 标识列（首列）应取的宽度：可视宽度减去其余列合计，下限为标识列自身的设计宽度。
    /// columnWidths 为全部列的当前宽快照（含首列，首列的值只作占位不参与合计）。
    /// </summary>
    public static int FillWidth(int clientWidth, int fillMinimum, IReadOnlyList<int> columnWidths)
    {
        var fixedSum = 0;
        for (var i = 1; i < columnWidths.Count; i++) fixedSum += columnWidths[i];
        return Math.Max(fillMinimum, clientWidth - fixedSum);
    }
}
