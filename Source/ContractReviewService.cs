using ApifoxHelper.Tasks;

namespace ApifoxHelper.Source;

/// <summary>
/// 响应契约审阅：确认 / 拒绝 / 恢复待审。校验口径只有这一份，Web API 与原生界面共用：
/// 阻断契约（未解析点或多个形状）不能确认；状态只允许 confirmed / rejected / pending；
/// 审阅写入任务目录 responseContract.json 并递增任务版本（使旧检查与旧计划过期）。
/// </summary>
public sealed class ContractReviewService(TaskStore tasks)
{
    public async Task<ResponseContract> ReviewAsync(string taskId, string endpointKey, string reviewState, CancellationToken ct = default)
    {
        ResponseContract? updated = null;
        await tasks.UpdateAsync(taskId, t =>
        {
            var contracts = tasks.Read<List<ResponseContract>>(taskId, TaskFiles.ResponseContract) ?? new List<ResponseContract>();
            var contract = contracts.FirstOrDefault(c => c.EndpointKey == endpointKey) ?? throw new UserException("响应契约不存在。", 404);
            if (reviewState == "confirmed" && contract.ReviewState == "blocked") throw new UserException("该响应契约存在未解析点或多个形状，不能确认。");
            if (reviewState is not ("confirmed" or "rejected" or "pending")) throw new UserException("审阅状态无效。");
            contract.ReviewState = reviewState;
            contract.ReviewedAt = DateTimeOffset.Now;
            tasks.Write(taskId, TaskFiles.ResponseContract, contracts);
            t.Revision++;
            updated = contract;
        }, ct);
        return updated!;
    }
}
