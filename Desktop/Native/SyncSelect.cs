using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览勾选页的清单状态（DESIGN.md §6「列表筛选」）：勾选按接口键记，关键词只改可见行。
/// <para>
/// 这一页是「生成计划前选哪些接口」的唯一入口，清单是整份检查结果的接口——真实项目几十上百个
/// （方案 §4.2 记录千级接口分支），要找到「上次那个接口」只能逐行扫。冻结页面的选择发生在差异视图里，
/// 那里一直有关键词框（「关键词（路径、接口名）」）；原生把选择搬到独立一页后没有框，
/// 同一个动作（在长清单里按关键词找一行）在七处有、在这一处没有，属同壳不同行为。
/// </para>
/// <para>
/// 但这一页与那七处有一处不同：**勾选状态挂在行上**。筛选要重建清单，重建就把勾选丢了——
/// 用户勾了 12 个、筛一下再筛回来只剩 3 个，点「生成同步计划」写出去的是 3 个：
/// 静默少同步比勾不上难发现得多（与第五十三轮「宁可复制不到，也不把邻列文字当名称抄出去」同一取舍）。
/// 所以勾选状态从行上搬到本文件（按接口键记，单一来源），清单重建时从它恢复；
/// 筛选只改可见行，不改勾选——这条不变量由用例在 Linux 上真执行钉住。
/// </para>
/// <para>
/// 两个批量动作的作用域不同，各有理由：「全选可同步」只作用于**可见行**（与资源管理器 Ctrl+A 同序——
/// 屏幕上只有 3 行时点「全选」却勾上 40 个，用户看不见自己勾了什么），按钮上的数字因此也是可见行里的可同步数；
/// 可见行里可同步的勾上、其余取消，与改前逐字一致（改前清单里就是全部行）。
/// 「清空」作用于**全部**（按钮说的是清空，不是清空可见——留着看不见的勾选，点完清空计数还显示「已选择 5 个接口」，
/// 那看起来就是坏了）。
/// </para>
/// <para>
/// 匹配字段与清单那三格同源（接口=方法+路径、名称、状态文字），状态文字取 <see cref="TaskDisplay.CheckStatusText"/>
/// 与单元格同一份；「待确认」那一列是数字，不进关键词（与五张长清单「状态列由筛选下拉管、不进关键词」同一口径）。
/// 本文件不依赖 WinForms：以共享源码方式编入测试工程，在 Linux 上断言。
/// </para>
/// </summary>
public sealed class SyncSelect
{
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);

    /// <summary>关键词框里的原文（空即全部行）。</summary>
    public string Query { get; set; } = "";

    /// <summary>这一行现在是不是勾上的（清单重建时按它恢复）。</summary>
    public bool IsChecked(string key) => _checked.Contains(key);

    /// <summary>勾选状态的唯一写入口：界面只在 ItemChecked 里调它，不自己维护第二份。</summary>
    public void Set(string key, bool isChecked)
    {
        if (isChecked) _checked.Add(key);
        else _checked.Remove(key);
    }

    /// <summary>已勾选的接口数（按钮行的聚合数字，不随筛选变化）。</summary>
    public int CheckedCount => _checked.Count;

    /// <summary>
    /// 「清空」此刻有没有可清的东西（第二百八十七轮）：一行都没勾时这颗按钮应当如实禁用。
    /// <para>
    /// <b>补的是哪个缺口</b>：勾选页按钮行上三颗批量按钮里，「生成同步预览」按已勾数量启停
    /// （<see cref="SyncPreviewDialog"/> 的 <c>RefreshSelectionCount</c>），「全选可同步（N）」按数据是否
    /// 可用启停，唯独「清空」只要数据在就常亮——打开这一页（一行都没勾）点它，什么也没发生，
    /// 计数行也不会变一个字。第四十轮对同一形状判过：候选审阅对话框清空选中后「查看代码证据」残留启用、
    /// 点击静默无响应，那一次的处理是「空选中分支如实禁用」，本处照同一条判据。
    /// </para>
    /// <para>
    /// 判据刻意与 <see cref="CheckedCount"/> 同源：能清的东西就是已勾的那一批（「清空」按<b>全部</b>清，
    /// 不是只清可见行——看不见的勾选若留着，点完「清空」计数行还写着「已选择 5 个接口」，
    /// 那看起来就是坏了）。所以「有没有可清的」= 已勾数为零与否，不引入 seconde 份计数。
    /// </para>
    /// </summary>
    public bool CanClear => _checked.Count > 0;

    /// <summary>
    /// 「全选可同步」此刻有没有可勾的东西（第二百九十五轮）：可见行里一个可同步的都没有时，
    /// 这颗按钮应当如实禁用——与 <see cref="CanClear"/> 同一形状。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百八十七轮把「清空」按已勾数启停之后，同一行上三颗批量按钮里
    /// 只剩「全选可同步（N）」还按「数据是否可用」启停——把关键词敲成只命中不可同步的行
    /// （或筛到零行），按钮上写着「全选可同步（0）」却仍是亮的，点下去什么也不会发生、
    /// 计数行也不会变一个字。第四十轮对同一形状判过（「查看代码证据」残留启用、点击静默无响应），
    /// 第二百八十七轮对「清空」判过第二次，本处是同一行上的第三颗。
    /// </para>
    /// <para>
    /// <b>为什么参数是「可见行里的可同步数」而不是「已勾数」</b>：这颗按钮的作用域是<b>可见行</b>
    /// （与资源管理器 Ctrl+A 同序，见类注释），所以「有没有可勾的东西」问的是可见行里还剩几个可同步的，
    /// 不是已经勾了几个——勾了 5 个、筛到只剩不可同步的行时，这颗按钮仍然没有可勾的东西。
    /// 计数由调用方按 <see cref="VisibleSyncableCount"/> 算好交进来（按钮文字里的那个 N 就是它），
    /// 本判定不另算一份，也不读关键词。
    /// </para>
    /// <para>
    /// 它不判「忙不忙」「有没有数据」：那两项与「清空」一样由界面那一处判定一起给
    /// （<c>SyncPreviewDialog.RefreshSelectAllButton</c>），本文件只答「有没有可勾的东西」。
    /// </para>
    /// </summary>
    public static bool CanSelectAll(int visibleSyncableCount) => visibleSyncableCount > 0;

    /// <summary>可见行：关键词只改可见行，不改勾选（空关键词返回数据源顺序的副本）。</summary>
    public List<EndpointCheck> Visible(CheckResult diff) =>
        ListFilter.Apply(diff.Endpoints, Query, SearchFields);

    /// <summary>
    /// 关键词匹配的字段：与清单那三格同源（接口、名称、状态文字）——首格与第三格取
    /// <see cref="SyncSelectSort"/> 那一份（第一百零九轮把三格文字收进排序口径，匹配与显示仍是同一份），
    /// 名称格取远端名称原文（占位文字「—」「Apifox 中不存在」不进关键词：那是显示用的兜底，不是内容）。
    /// </summary>
    public static string[] SearchFields(EndpointCheck check) =>
        [SyncSelectSort.EndpointCell(check), check.RemoteName ?? "", SyncSelectSort.StatusCell(check)];

    /// <summary>可见行里可同步的接口数：「全选可同步（N）」的 N 与按钮实际勾的行必须是同一批。</summary>
    public int VisibleSyncableCount(CheckResult diff, DecisionStore decisions, bool offlineMode) =>
        Visible(diff).Count(c => SyncSelection.SyncableNow(offlineMode, diff, decisions, c.Key));

    /// <summary>
    /// 「全选可同步」：只作用于可见行——可见行里可同步的勾上、其余取消（与改前逐字一致：
    /// 改前清单里就是全部行，故无关键词时行为不变）；看不见的行一个都不动。
    /// </summary>
    public void SelectAllSyncable(CheckResult diff, DecisionStore decisions, bool offlineMode)
    {
        var keys = SyncSelection.SyncableKeys(offlineMode, diff, decisions).ToHashSet(StringComparer.Ordinal);
        foreach (var check in Visible(diff))
        {
            Set(check.Key, keys.Contains(check.Key));
        }
    }

    /// <summary>「清空」：按全部清（按钮说的是清空，不是清空可见）。</summary>
    public void Clear() => _checked.Clear();

    /// <summary>
    /// 已勾选的接口键，按数据源顺序（生成计划交出去的就是这一批）。顺序取清单同一份而不是勾选先后：
    /// 计划里的动作顺序由服务层按模型依赖排，本层不另造一种顺序，也不让「先勾哪个」影响交出去的内容。
    /// 被关键词筛掉的行同样在册——筛选只改可见行，不改勾选。
    /// </summary>
    public List<string> CheckedKeys(CheckResult diff) =>
        diff.Endpoints.Where(e => _checked.Contains(e.Key)).Select(e => e.Key).ToList();
}
