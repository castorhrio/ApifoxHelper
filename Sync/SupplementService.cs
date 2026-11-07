using ApifoxHelper.Tasks;

namespace ApifoxHelper.Sync;

/// <summary>
/// 人工补充说明的单一保存口径（F10）：接口标题/说明、参数与请求体、响应说明的补充与「保留现状」。
/// 示例字段须为合法 JSON、说明超长拒绝；内容有变化时递增任务版本，使已有的检查结果与同步计划过期；
/// 内容未变化时不写文件、不递增（保存可重复点击）。
/// PUT /api/tasks/{id}/supplement 路由与原生「补充说明」对话框共用本服务，避免两处校验口径漂移。
/// </summary>
public sealed class SupplementService(TaskStore tasks)
{
    public async Task<SupplementStore> SaveAsync(string taskId, SupplementStore body, CancellationToken ct = default)
    {
        foreach (var entry in body.Entries)
        {
            if (entry.Field == "example" && entry.Value is not null)
            {
                try
                {
                    JsonNode.Parse(entry.Value);
                }
                catch (JsonException)
                {
                    throw new UserException($"{entry.ResourceKey} {entry.Location} 的示例不是合法 JSON。");
                }
            }
            if (entry.Value is not null && entry.Value.Length > 65535) throw new UserException("说明过长。");
        }
        await tasks.UpdateAsync(taskId, t =>
        {
            // 内容没有变化时不改写，也不让已有的检查结果和同步计划失效，“保存”可以重复点击。
            var existing = tasks.Read<SupplementStore>(taskId, TaskFiles.Supplement) ?? new SupplementStore();
            if (Content(existing) == Content(body))
            {
                body = existing;
                return;
            }
            foreach (var entry in body.Entries) entry.UpdatedAt = DateTimeOffset.Now;
            tasks.Write(taskId, TaskFiles.Supplement, body);
            t.Revision++;
            if (t.Stages.Remote.Status is "previewable" or "awaitingConfirm") t.Stages.Remote.Status = "expired";
        }, ct);
        return body;
    }

    /// <summary>补充说明的实际内容（忽略更新时间与条目顺序），用于判断保存是否真的有修改。</summary>
    private static string Content(SupplementStore store) => Canonical.HashOf(store.Entries
        .Select(e => new { e.ResourceKey, e.Location, e.Field, e.Value, e.KeepAsIs })
        .OrderBy(e => e.ResourceKey, StringComparer.Ordinal).ThenBy(e => e.Location, StringComparer.Ordinal).ThenBy(e => e.Field, StringComparer.Ordinal)
        .ToList());
}
