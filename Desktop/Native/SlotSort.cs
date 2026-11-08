namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释缺口清单的列排序键（DESIGN.md §6「列表列排序」，第一百零二轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）与响应契约的契约清单（第一百零一轮）
/// 点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而注释缺口清单（位置、标注、状态、角色、文件:行五列）点击列头没有任何反应——
/// 这一屏是全应用最长的一张清单（一次扫描的全部注释位置），也是注释这一条路上的第一屏，
/// 「把缺失的集中看」「按角色排」「把同一个文件的放一起」只能逐行扫；
/// 同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：位置列取 <c>SlotText.Name</c>（与首格同一份）、标注列取 <c>SlotText.Tag</c>、
/// 状态列取 <c>TaskDisplay.SlotStatusText</c>（与那一格同一份中文映射）、
/// 角色列取 <c>TaskDisplay.SlotRoleText</c>、文件:行列取 <c>SlotText.Location</c>。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class SlotSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：位置、标注、状态、角色、文件:行）。
    /// 五个参数就是这一行在屏幕上被读作的那五格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string name, string tag, string statusText, string roleText, string location, int column) => column switch
    {
        0 => name,
        1 => tag,
        2 => statusText,
        3 => roleText,
        4 => location,
        _ => null,
    };
}
