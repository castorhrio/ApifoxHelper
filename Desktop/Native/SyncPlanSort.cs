namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览两张清单的列排序键（DESIGN.md §6「列表列排序」，第一百零六轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）
/// 与注释回写的文件清单（第一百零五轮）点击列头即按该列排序（同一列再点反向、换一列从升序开始、表头带方向标记），
/// 而同步预览预览页的两张清单——写入清单（序号、动作、资源、远端 ID、改动、CLI 校验、状态七列）与
/// 未纳入清单（资源、未纳入原因两列）——点击列头没有任何反应：这一页是确认同步之前的最后一屏，
/// 写入清单按执行顺序排（模型在前、接口在后），真实项目几十上百个动作，
/// 「把写入失败的那几条集中看」「按资源排」「把新建与更新分开」只能逐行扫；
/// 未纳入清单往往更长（计划只纳入需要写入的接口与模型，其余全部落在这里），
/// 「按原因聚在一起看」同样只能逐行扫。同一个动作在别处有、在这一处没有，属同壳不同行为。
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class SyncPlanSort
{
    /// <summary>
    /// 写入清单的排序键（与建列顺序一一对应：序号、动作、资源、远端 ID、改动、CLI 校验、状态）。
    /// 七个参数就是这一行在屏幕上被读作的那七格——调用方传单元格的同一份取值，不另算一套。
    /// 序号列取执行顺序里的第几步（与首格同一份，按数值排；升序即回到执行顺序，排序之后仍能按序号核对执行日志）；
    /// 动作列取 <see cref="TaskDisplay.PlanActionKindText"/>、资源列取资源键原文、改动列取
    /// <see cref="PlanEntries.ChangesText"/>、状态列取 <see cref="TaskDisplay.PlanActionStateText"/>（都与那一格同一份）；
    /// 远端 ID 列取 ID 本身并按数值排（新建动作还没有远端 ID，交 null 视为最小，升序排在最前——
    /// 那一格的「—」是占位不是值）；CLI 校验列取校验结果原文，还没有校验结果时交 null（同上，
    /// 那一格的「—」是占位不是值，不按那个字的码位排）。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? ActionKey(
        int step,
        string kindText,
        string resourceKey,
        long? remoteId,
        string changesText,
        string? validation,
        string stateText,
        int column) => column switch
    {
        0 => step,
        1 => kindText,
        2 => resourceKey,
        3 => remoteId,
        4 => changesText,
        5 => string.IsNullOrWhiteSpace(validation) ? null : validation,
        6 => stateText,
        _ => null,
    };

    /// <summary>
    /// 未纳入清单的排序键（与建列顺序一一对应：资源、未纳入原因）。
    /// 两格都取原文，与 <see cref="ExclusionEntries.KeywordFields"/> 匹配的那两格同源；
    /// 按原因排即把同一类原因（无需修改、远端已被修改、引用的模型存在阻断项…）聚在一起。
    /// 列号落在建列之外交 null，与 <see cref="ActionKey"/> 同一处理。
    /// </summary>
    public static IComparable? ExclusionKey(string resourceKey, string reason, int column) => column switch
    {
        0 => resourceKey,
        1 => reason,
        _ => null,
    };
}
