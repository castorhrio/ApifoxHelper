using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Sync;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览勾选口径（冻结页面与原生对话框同一规则，SyncSelection 单一落点）：
/// 可勾选 = 新增/结构变化/说明变化且非离线；「全选可同步」再排除有未确认高风险项的接口；
/// 待确认计数只认「已确认」以外的决策值（缺失、已排除都算未确认）。断言对真实 CheckResult/DecisionStore 类型成立。
/// </summary>
public class SyncSelectionTests
{
    private static CheckResult Diff() => new()
    {
        Endpoints =
        [
            new EndpointCheck { Key = "GET /api/new", Status = "new" },
            new EndpointCheck { Key = "PUT /api/structure", Status = "structure" },
            new EndpointCheck { Key = "POST /api/docs", Status = "docs" },
            new EndpointCheck { Key = "GET /api/same", Status = "unchanged" },
            new EndpointCheck { Key = "GET /api/blocked", Status = "blocked" },
            new EndpointCheck { Key = "GET /api/only-source", Status = "sourceOnly" },
        ],
        Items =
        [
            new DiffItem { Id = "d1", ResourceKey = "PUT /api/structure", NeedsDecision = true },
            new DiffItem { Id = "d2", ResourceKey = "GET /api/blocked", NeedsDecision = true },
            new DiffItem { Id = "d3", ResourceKey = "POST /api/docs", NeedsDecision = true },
        ],
    };

    [Fact]
    public void Selectable_OnlySyncableStatusesAndOnline()
    {
        // 可勾选 = 新增/结构变化/说明变化；无变化、阻断、仅源码不能同步。
        Assert.True(SyncSelection.Selectable(offlineMode: false, "new"));
        Assert.True(SyncSelection.Selectable(offlineMode: false, "structure"));
        Assert.True(SyncSelection.Selectable(offlineMode: false, "docs"));
        Assert.False(SyncSelection.Selectable(offlineMode: false, "unchanged"));
        Assert.False(SyncSelection.Selectable(offlineMode: false, "blocked"));
        Assert.False(SyncSelection.Selectable(offlineMode: false, "sourceOnly"));
        Assert.False(SyncSelection.Selectable(offlineMode: false, "mystery"));
        // 离线检查不能同步：任何状态都不可勾选。
        foreach (var status in new[] { "new", "structure", "docs", "unchanged", "blocked", "sourceOnly" })
        {
            Assert.False(SyncSelection.Selectable(offlineMode: true, status));
        }
        Assert.Empty(SyncSelection.SyncableStatuses.Except(["new", "structure", "docs"]));
    }

    [Fact]
    public void PendingCount_OnlyUnconfirmedNeedsDecisionItems()
    {
        var diff = Diff();
        var decisions = new DecisionStore { Items = { ["d1"] = "accepted", ["d2"] = "excluded" } };
        // 已确认的不算待确认；已排除与未决策的都算。
        Assert.Equal(0, SyncSelection.PendingCount(diff, decisions, "PUT /api/structure"));
        Assert.Equal(1, SyncSelection.PendingCount(diff, decisions, "GET /api/blocked"));
        Assert.Equal(1, SyncSelection.PendingCount(diff, decisions, "POST /api/docs"));
        Assert.Equal(0, SyncSelection.PendingCount(diff, decisions, "GET /api/new"));
    }

    [Fact]
    public void SyncableNow_ExcludesPendingAndNonSyncable()
    {
        var diff = Diff();
        // 结构变化接口有未确认高风险项：可手动勾选，但不进「全选可同步」。
        Assert.False(SyncSelection.SyncableNow(offlineMode: false, diff, new DecisionStore(), "PUT /api/structure"));
        var accepted = new DecisionStore { Items = { ["d1"] = "accepted", ["d3"] = "accepted" } };
        Assert.True(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "PUT /api/structure"));
        Assert.True(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "POST /api/docs"));
        Assert.True(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "GET /api/new"));
        Assert.False(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "GET /api/same"));
        Assert.False(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "GET /api/blocked"));
        Assert.False(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "GET /api/only-source"));
        Assert.False(SyncSelection.SyncableNow(offlineMode: false, diff, accepted, "DELETE /api/missing"));
    }

    [Fact]
    public void SyncableKeys_SelectsAllConfirmedSyncableEndpoints()
    {
        var diff = Diff();
        var accepted = new DecisionStore { Items = { ["d1"] = "accepted", ["d3"] = "accepted" } };
        Assert.Equal(["GET /api/new", "PUT /api/structure", "POST /api/docs"],
            SyncSelection.SyncableKeys(offlineMode: false, diff, accepted));
        // 未确认时只有新增接口可全选；离线任务一个都不可同步。
        Assert.Equal(["GET /api/new"], SyncSelection.SyncableKeys(offlineMode: false, diff, new DecisionStore()));
        Assert.Empty(SyncSelection.SyncableKeys(offlineMode: true, diff, accepted));
    }
}
