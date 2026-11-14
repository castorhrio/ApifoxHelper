using ApifoxHelper.Contracts;
using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览的接口勾选口径（与冻结页面同一规则）：
/// 可勾选 = 检查状态可同步（新增/结构变化/说明变化）且任务不是离线模式；
/// 「全选可同步」只勾选其中没有待确认项的接口。
/// 待确认 = 该接口存在潜在不兼容/需人工判断的差异，且逐项决策不是「已确认」。
/// 这里只决定勾选的初始可用性；计划阶段的最终排除判断（含模型依赖、远端指纹、门禁）在
/// SyncPlanService 单一口径，本文件不复制写入边界。
/// 本文件不依赖 WinForms：以共享源码方式编入测试工程，在 Linux 上断言。
/// </summary>
public static class SyncSelection
{
    /// <summary>状态可同步的检查结果（与冻结页面同一取值：无变化/阻断/仅源码不能同步）。</summary>
    public static readonly string[] SyncableStatuses = ["new", "structure", "docs"];

    public static bool Selectable(bool offlineMode, string checkStatus) =>
        !offlineMode && SyncableStatuses.Contains(checkStatus);

    /// <summary>
    /// 勾不动的那一行说一句为什么（第二百九十八轮）：可勾的行返回 <c>null</c>，勾不上的行返回那一句话。
    /// <para>
    /// <b>补的是哪个缺口</b>：勾选页的接口清单里，不可同步的行此前<b>一句话都没有</b>——
    /// <c>SyncPreviewDialog</c> 的 <c>ItemCheck</c> 把 <c>e.NewValue</c> 改回 <c>Unchecked</c>，
    /// 勾选框闪一下就回去了，状态行、提示行、计数行一个字都不变。用户看到的与「勾上了又被谁取消」
    /// 分不开，而这一页的说明行只写了<b>整页的规则</b>（「无变化、阻断与仅源码不可同步」）——
    /// 他点到的那一行是哪一种、为什么勾不上，得在现场说出来；离线模式那一种说明行里根本没提。
    /// 第四十轮与第二百八十七、二百九十五轮判的是「<b>禁用</b>的控件看起来按得动」，
    /// 这一处是同一族判据的另一半：<b>看起来按得动、按下去什么也没有</b>。
    /// </para>
    /// <para>
    /// <b>为什么判据与 <see cref="Selectable"/> 同源</b>：能不能勾由 <see cref="Selectable"/> 判，
    /// 为什么勾不上由这里说——两处各写一份「哪些状态可同步」的判据，迟早一处改了另一处没改，
    /// 于是界面上出现「勾得上却说勾不上」或反过来。本方法第一句就是 <see cref="Selectable"/>，
    /// 不另列一份状态表。
    /// </para>
    /// <para>
    /// <b>它不新造理由文案</b>：状态那一段取 <see cref="TaskDisplay.CheckStatusText"/>（与清单「状态」列
    /// 同一份映射），于是状态行上说的那两个字与用户眼睛看到的那一格逐字相同；
    /// 计划阶段「为什么没进计划」的逐条原因仍只在 <c>SyncPlanService</c> 一处，本方法不复制它。
    /// </para>
    /// <para>
    /// <b>它不改什么</b>：不改可同步口径、不改勾选状态的写入（仍只经 <c>ItemChecked</c> 写进
    /// <see cref="SyncSelect"/>）、不改两个批量动作的作用域、不改任何按钮的启停。
    /// </para>
    /// <para>
    /// <b>边界登记：<paramref name="endpointKey"/> 原样写进那一句，不裁剪、不改写（第二百九十九轮，简报 P1-2）</b>。
    /// 这一条<b>只登记、不另加门</b>——本仓纪律是「能找到根因 + 证据才改」，而下面两种边界在真实数据里
    /// 一次都没出现过（`EndpointCheck.Key` 由扫描器按「方法 + 空格 + 路径」拼出来，既不会空也不会长到
    /// 撑破状态行）。登记的是<b>当前行为</b>与<b>为什么它不构成阻断</b>：
    /// </para>
    /// <list type="bullet">
    /// <item><b>空 key</b>：那一句变成「「」勾不上：…」——括号里空着，但「为什么勾不上」那半句照旧完整，
    /// 用户仍读得到结论。空 key 在界面上不可能出现（清单首列就是这一格，空着的那一行用户也点不到），
    /// 所以不为它加一条「空 key 时换一句话」的分支：那会多出一条永远走不到的路，而走不到的路没人验。</item>
    /// <item><b>超长 key</b>：原样写进去、<b>不截断</b>。截断会把「是哪一行」这半句弄坏（截断处正是
    /// 区分两个接口的那一段），而状态行本身有两条兜底：<c>AutoEllipsis</c> 让末尾出现「…」、
    /// 「查看完整错误」入口把全文交出来（<see cref="StatusLineText.ShouldOfferFullText"/> 判「放不下才给」）。
    /// 所以长 key 的代价是「要多点一次入口」，不是「读不到」。</item>
    /// </list>
    /// <para>
    /// 这两条由 <c>RejectMessageTests.RejectMessage_KeyIsWrittenVerbatim_EmptyAndLongKeysIncluded</c>
    /// 钉住<b>当前行为</b>（空 key 仍说清为什么、长 key 不被截断）——它判的是「这一句里有没有那一行」，
    /// 判不到「该不该为这两种边界另写一句话」，后者只有人读得出来。
    /// </para>
    /// </summary>
    /// <param name="endpointKey">那一行的接口身份（清单首列，与 <c>EndpointCheck.Key</c> 同一份）。</param>
    /// <param name="offlineMode">任务是不是离线模式（离线时整份检查结果都不可同步）。</param>
    /// <param name="checkStatus">那一行的检查状态（<c>EndpointCheck.Status</c>）。</param>
    public static string? RejectMessage(string endpointKey, bool offlineMode, string checkStatus)
    {
        if (Selectable(offlineMode, checkStatus)) return null;
        var reason = offlineMode
            ? "离线模式下的整份检查结果都不进计划"
            : "这一行是「" + TaskDisplay.CheckStatusText(checkStatus) + "」，不进计划";
        return "「" + endpointKey + "」勾不上：" + reason + "，勾选不生效。";
    }

    /// <summary>待确认计数：潜在不兼容/需人工判断且还没有逐项确认（决策缺失或已排除都算未确认）。</summary>
    public static int PendingCount(CheckResult diff, DecisionStore decisions, string endpointKey) =>
        diff.Items.Count(i => i.ResourceKey == endpointKey && i.NeedsDecision && decisions.Items.GetValueOrDefault(i.Id) != "accepted");

    public static bool SyncableNow(bool offlineMode, CheckResult diff, DecisionStore decisions, string endpointKey)
    {
        var check = diff.Endpoints.FirstOrDefault(e => e.Key == endpointKey);
        return check is not null
            && Selectable(offlineMode, check.Status)
            && PendingCount(diff, decisions, endpointKey) == 0;
    }

    /// <summary>「全选可同步」勾选的接口键。</summary>
    public static List<string> SyncableKeys(bool offlineMode, CheckResult diff, DecisionStore decisions) =>
        diff.Endpoints.Where(e => SyncableNow(offlineMode, diff, decisions, e.Key)).Select(e => e.Key).ToList();
}
