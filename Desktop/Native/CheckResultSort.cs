using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 检查结果两张清单的列排序键（DESIGN.md §6「列表列排序」，第九十七轮）：
/// 两个主页面的列表自第四十七轮起点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带 ▲/▼），
/// 而检查结果对话框的两张清单（接口清单四列、模型清单五列）点击列头没有任何反应——
/// 这一屏是只读工作台，真实项目几十上百个接口与模型，「把阻断的那几条集中看」「按范围外影响排」
/// 只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：接口列取「方法 + 空格 + 路径」（与首格同一表达式）、名称列取远端名称
/// （缺值交 null，<see cref="ListSort"/> 视为最小，与单元格的「—」同一取舍）、状态列取
/// <see cref="TaskDisplay"/> 的中文映射（与状态格同一份）、阻断与两个计数列取数值。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class CheckResultSort
{
    /// <summary>
    /// 接口清单的排序键（与建列顺序一一对应：接口、名称、状态、阻断）。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? EndpointKey(EndpointCheck check, int column) => column switch
    {
        0 => check.Method + " " + check.Path,
        1 => check.RemoteName,
        2 => TaskDisplay.CheckStatusText(check.Status),
        3 => check.Blockers.Count,
        _ => null,
    };

    /// <summary>
    /// 模型清单的排序键（与建列顺序一一对应：模型、映射、状态、用于接口、范围外影响）。
    /// 映射与状态取 <see cref="TaskDisplay"/> 的中文映射（与那两格同一份），两个计数列取数值。
    /// </summary>
    public static IComparable? ModelKey(ModelCheck model, int column) => column switch
    {
        0 => model.Name,
        1 => TaskDisplay.MappingStateText(model.MappingState),
        2 => TaskDisplay.ModelStatusText(model.Status),
        3 => model.UsedBy.Count,
        4 => model.ImpactOutOfScope.Count,
        _ => null,
    };
}
