namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 补充说明接口清单的列排序键（DESIGN.md §6「列表列排序」，第一百零七轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）、
/// 注释回写的文件清单（第一百零五轮）与同步预览的两张清单（第一百零六轮）点击列头即按该列排序
/// （同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而补充说明的接口清单（接口、名称、补充三列）点击列头没有任何反应——
/// 这一屏的清单是整份检查结果的接口（真实项目几十上百个），
/// 「把已经补过的那几个集中看」「按名称排」「把还没补的挑出来」只能逐行扫；
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：接口列取 <see cref="SupplementEntries.EndpointCell"/>（与首格同一份拼法）、
/// 名称列取 <see cref="SupplementEntries.NameCell"/>（与第二格同一份，远端没有这个接口时是空串）、
/// 补充列取条目数本身并按数值排（与两个主页面的数量列同一口径，按文字排会把「10 处」排到「2 处」前；
/// 那一格的「N 处」由同一个数派生，见 <see cref="SupplementEntries.EntryCountCell"/>）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class SupplementSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：接口、名称、补充）。
    /// 三个参数就是这一行在屏幕上被读作的那三格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string endpointCell, string nameCell, int entries, int column) => column switch
    {
        0 => endpointCell,
        1 => nameCell,
        2 => entries,
        _ => null,
    };
}
