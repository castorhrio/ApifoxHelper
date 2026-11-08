using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览勾选页接口清单的列排序键与三格文字（DESIGN.md §6「列表列排序」，第一百零九轮）：
/// 两个主页面（第四十七轮）、检查结果的两张清单（第九十七轮）、接口追踪明细（第九十八轮）、
/// 差异确认的条目清单（第九十九轮）、注释候选的候选清单（第一百轮）、响应契约的契约清单（第一百零一轮）、
/// 注释缺口清单（第一百零二轮）、生成文档核对的条目清单（第一百零三轮）、任务日志的记录清单（第一百零四轮）、
/// 注释回写的两张清单（第一百零五、一百零八轮）、同步预览预览页的两张清单（第一百零六轮）与
/// 补充说明的接口清单（第一百零七轮）点击列头即按该列排序，
/// 而同步预览**勾选页**的接口清单（接口、名称、状态、待确认四列）点击列头没有任何反应——
/// 这一页是「生成计划前选哪些接口」的唯一入口，清单是整份检查结果的接口（真实项目几十上百个，
/// 方案 §4.2 记录千级接口分支），「把不可同步的挑出来」「按状态排」「把还有待确认项的集中看」
/// 只能逐行扫；同一个动作在别处有、在这一处没有，属同壳不同行为。
/// <para>
/// 这一页与另十二处有一处不同：**清单是复选清单**（勾选状态挂在行上）。排序要重建清单，
/// 而勾选状态自第七十二轮起按接口键记在 <see cref="SyncSelect"/>（单一来源），重建时从它恢复——
/// 排序因此不丢勾选，与筛选同一份前提（勾了 12 个、排一下只剩 3 个，点「生成同步计划」写出去的就是 3 个）。
/// </para>
/// <para>
/// 排序状态、方向翻转与表头标记仍取 <see cref="ListSort"/> 那一份（本文件只给「这一列按什么排」），
/// 界面不就地写 switch、不就地拼排序键——排序键与单元格取值同源，避免「显示与排序两套口径」。
/// 键的取法与单元格一致：接口列取 <see cref="EndpointCell"/>（与首格同一份拼法，也是关键词匹配的第一格）、
/// 名称列取远端名称原文（与第二格同一份取值；远端没有名称时交 null 视为最小——那一格的「—」与
/// 「Apifox 中不存在」是占位不是值，不按那两个字的码位排，升序排在最前即「远端还没有名字」的那批）、
/// 状态列取 <see cref="StatusCell"/>（与第三格同一份中文映射）、待确认列取待确认计数本身并按数值排
/// （与两个主页面的数量列同一口径，按文字排会把「10」排到「2」前；那一格的「—」是零的占位，按 0 参与）。
/// </para>
/// <para>
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </para>
/// </summary>
public static class SyncSelectSort
{
    /// <summary>接口列的文字（与首格同一份拼法，也是关键词匹配的第一格）。</summary>
    public static string EndpointCell(EndpointCheck check) => check.Method + " " + check.Path;

    /// <summary>
    /// 名称列的文字：远端名称，没有名称时按「远端有没有这个接口」给两种占位——
    /// 与第二格同一份取值，界面不就地拼这两句话。
    /// </summary>
    public static string NameCell(EndpointCheck check) => check.RemoteName ?? (check.InRemote ? "—" : "Apifox 中不存在");

    /// <summary>状态列的文字（与第三格同一份中文映射）。</summary>
    public static string StatusCell(EndpointCheck check) => TaskDisplay.CheckStatusText(check.Status);

    /// <summary>待确认列的文字：零显示「—」（那一格是占位，排序按 0 参与）。</summary>
    public static string PendingCell(int pending) => pending > 0 ? pending.ToString() : "—";

    /// <summary>
    /// 排序键（与建列顺序一一对应：接口、名称、状态、待确认）。
    /// 参数就是这一行在屏幕上被读作的那几格——调用方传单元格的同一份取值，不另算一套。
    /// 列号落在建列之外交 null——与 <see cref="ListSort.Order"/> 的「缺键视为最小」同一处理，
    /// 不索引不存在的列、也不把邻列的值当成这一列。
    /// </summary>
    public static IComparable? Key(EndpointCheck check, int pending, int column) => column switch
    {
        0 => EndpointCell(check),
        1 => check.RemoteName,
        2 => StatusCell(check),
        3 => pending,
        _ => null,
    };
}
