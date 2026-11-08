namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 差异确认清单的列排序键（DESIGN.md §6「列表列排序」，第九十九轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）与接口追踪明细（第九十八轮）
/// 点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而差异确认的条目清单（类型、资源、位置、状态四列）点击列头没有任何反应——
/// 这一屏与检查结果是同一只壳（关键词框 + 分类筛选 + 清单 + 详情区 + 动作行），
/// 条目按「接口 × 位置」展开，真实项目几十上百条，「把待处理的集中看」「按资源排」只能逐行扫；
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：类型列取 <c>KindText</c> 的中文映射（与首格同一份）、资源列取资源原文、
/// 位置列取位置原文、状态列取状态格文字（与那一格同一份，含「待确认」「已确认」「使用目标」等）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class DecisionSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：类型、资源、位置、状态）。
    /// 四个参数就是这一行在屏幕上被读作的那四格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string kindText, string resource, string location, string stateText, int column) => column switch
    {
        0 => kindText,
        1 => resource,
        2 => location,
        3 => stateText,
        _ => null,
    };
}
