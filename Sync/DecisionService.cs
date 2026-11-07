using ApifoxHelper.Tasks;

namespace ApifoxHelper.Sync;

/// <summary>
/// 差异决策的单一保存口径：高风险变化逐项确认、说明冲突选择、模型绑定与共享模型更新确认。
/// 决策内容有变化时递增任务版本，使已有的检查结果与同步计划过期；内容未变化时不写文件、不递增（保存可重复点击）。
/// PUT /api/tasks/{id}/decisions 路由与原生「处理差异确认」对话框共用本服务，避免两处校验口径漂移。
/// </summary>
public sealed class DecisionService(TaskStore tasks)
{
    public async Task<DecisionStore> SaveAsync(string taskId, DecisionStore body, CancellationToken ct = default)
    {
        foreach (var value in body.Items.Values) if (value is not ("accepted" or "excluded")) throw new UserException("决策值无效。");
        foreach (var value in body.DocChoices.Values) if (value is not ("keepRemote" or "useTarget")) throw new UserException("说明选择无效。");
        foreach (var value in body.ModelBindings.Values) if (value != "new" && !long.TryParse(value, out _)) throw new UserException("模型绑定无效。");
        await tasks.UpdateAsync(taskId, t =>
        {
            var existing = tasks.Read<DecisionStore>(taskId, TaskFiles.Decisions) ?? new DecisionStore();
            if (Canonical.HashOf(existing) == Canonical.HashOf(body)) return;
            tasks.Write(taskId, TaskFiles.Decisions, body);
            t.Revision++;
            if (t.Stages.Remote.Status is "previewable" or "awaitingConfirm") t.Stages.Remote.Status = "expired";
        }, ct);
        return body;
    }
}
