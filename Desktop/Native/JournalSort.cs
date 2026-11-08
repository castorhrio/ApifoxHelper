namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务日志记录清单的列排序键（DESIGN.md §6「列表列排序」，第一百零四轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）与生成文档核对的条目清单（第一百零三轮）点击列头即按该列排序
/// （同一列再点反向、换一列从升序开始、表头带方向标记），而任务日志的记录清单（时间、事件、摘要三列）
/// 点击列头没有任何反应——这一屏是第八十八轮那五张只读长清单里唯一还没接上的一张
/// （检查结果、接口追踪明细、生成文档核对已接，代码证据按「条目少」明确不接），
/// 一份几十个动作的计划就是上百行，「把写入失败的那几条集中看」「按事件排」「把同一个接口的几步放一起」
/// 只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：时间列取 <see cref="JournalEntries.TimeKey"/>（时间格就是它格式化出来的，
/// 按时间戳排而不是按显示文字排）、事件列取 <see cref="JournalEntries.EventText"/>（与第二格同一份）、
/// 摘要列取 <see cref="JournalEntries.SummaryCell"/>（与第三格同一份，含「结果未知」标记）。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class JournalSort
{
    /// <summary>
    /// 排序键（与建列顺序一一对应：时间、事件、摘要）。
    /// 三个参数就是这一行在屏幕上被读作的那三格——调用方传单元格的同一份取值，不另算一套。
    /// 时间读不出时交 null（与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理），
    /// 不拿占位字「—」的码位参与比较；列号落在建列之外同样交 null——不索引不存在的列、
    /// 也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(DateTimeOffset? at, string eventText, string summaryText, int column) => column switch
    {
        0 => at,
        1 => eventText,
        2 => summaryText,
        _ => null,
    };
}
