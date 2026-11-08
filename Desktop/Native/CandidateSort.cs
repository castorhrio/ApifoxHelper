namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释候选清单的列排序键（DESIGN.md §6「列表列排序」，第一百轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）与
/// 差异确认的条目清单（第九十九轮）点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而注释候选审阅的候选清单（位置、标注、依据、审阅状态、来源五列）点击列头没有任何反应——
/// 这一屏与差异确认是同一只壳（关键词框 + 分类筛选 + 清单 + 详情区 + 动作行），
/// 候选按「注释位置 × 标注」展开，真实项目几十上百条，「把待审阅的集中看」「按依据排」
/// 「把同一来源的放一起」只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：位置列取 <c>QueueText.CandidateTitle</c>（与首格同一份，含「该位置已不在扫描结果中」时
/// 回退符号 Id 的同一取值）、标注列取 <c>QueueText.CandidateTag</c>、依据列取 <c>TaskDisplay.CandidateBasisText</c>、
/// 审阅状态列取 <c>TaskDisplay.ReviewStateText</c>（与那一格同一份中文映射）、来源列取
/// <c>TaskDisplay.CandidateSourceText</c>。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class CandidateSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：位置、标注、依据、审阅状态、来源）。
    /// 五个参数就是这一行在屏幕上被读作的那五格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(string title, string tag, string basisText, string reviewStateText, string sourceText, int column) => column switch
    {
        0 => title,
        1 => tag,
        2 => basisText,
        3 => reviewStateText,
        4 => sourceText,
        _ => null,
    };
}
