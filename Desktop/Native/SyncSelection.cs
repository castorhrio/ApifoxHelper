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
