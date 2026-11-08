using ApifoxHelper.Source;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 接口追踪明细清单的列排序键（DESIGN.md §6「列表列排序」，第九十八轮）：
/// 检查结果对话框的两张清单自第九十七轮起点击列头即按该列排序（同一列再点反向、换一列从升序开始、
/// 表头带方向标记），而接口追踪明细的接口清单（五列）点击列头没有任何反应——
/// 这一屏与检查结果是同一只壳（只读工作台：关键词框 + 清单 + 详情区 + 证据入口），
/// 真实项目几十上百个接口，「把预算超限的那几条集中看」「按未解析调用排」只能逐行扫；
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：接口列取接口身份（与首格同一表达式）、三个计数列取数值
/// （单元格的「—」是零的占位，排序按 0 参与，不按「—」这个字的码位排）、
/// 预算列取「是否超限」（单元格的「超限」/「—」正是这一位，降序即把超限的排到最前）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class EndpointTraceSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：接口、读取方法、未解析调用、外部调用、预算）。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(EndpointCoverage coverage, int column) => column switch
    {
        0 => coverage.EndpointKey,
        1 => coverage.MethodsRead.Count,
        2 => coverage.Unresolved.Count,
        3 => coverage.ExternalCalls.Count,
        4 => coverage.BudgetExceeded ? 1 : 0,
        _ => null,
    };
}
