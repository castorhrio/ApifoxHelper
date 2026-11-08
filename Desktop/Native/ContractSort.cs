namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 响应契约清单的列排序键（DESIGN.md §6「列表列排序」，第一百零一轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）与注释候选的候选清单（第一百轮）点击列头即按该列排序
/// （同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而响应契约审阅的契约清单（接口、封装、data 类型、分支、审阅状态五列）点击列头没有任何反应——
/// 这一屏与差异确认、注释候选是同一只壳（关键词框 + 分类筛选 + 清单 + 详情区 + 动作行），
/// 契约按「接口 × 通用封装」展开，真实项目几十上百条，「把待确认的集中看」「按封装排」
/// 「把分支多的先看」只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：接口列取 <c>EndpointKey</c>（与首格同一份）、封装列取
/// <c>QueueText.ContractWrapper</c>、data 类型列取 <c>QueueText.ContractDataType</c>、
/// 审阅状态列取 <c>TaskDisplay.ContractStateText</c>（与那一格同一份中文映射）；
/// 分支列取分支数本身（与那一格的计数同一份，按数值排——与两个主页面的数量列同一口径，
/// 不按文字排，否则「10」会排在「2」前）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ContractSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：接口、封装、data 类型、分支、审阅状态）。
    /// 五个参数就是这一行在屏幕上被读作的那五格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string endpointKey, string wrapperText, string dataTypeText, int branchCount, string reviewStateText, int column) => column switch
    {
        0 => endpointKey,
        1 => wrapperText,
        2 => dataTypeText,
        3 => branchCount,
        4 => reviewStateText,
        _ => null,
    };
}
