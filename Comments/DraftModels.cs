namespace ApifoxHelper.Comments;

/// <summary>
/// XML 注释候选。依据（basis）与审阅状态（reviewState）分开记录，含义：
/// 依据——代码可证实 / 需业务确认 / 分析未覆盖 / 人工已确认；
/// 审阅——待审阅 / 已接受 / 已搁置 / 已驳回 / 证据已变化（重新扫描后证据指纹不一致，需重新审阅）。
/// 取值清单写在对应属性的注释里，测试据此断言中文映射完备（TaskDisplay.CandidateBasisText、ReviewStateText）。
/// </summary>
public sealed class CommentCandidate
{
    public string Id { get; set; } = "";
    public string SlotId { get; set; } = "";
    public string SymbolId { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? ParameterName { get; set; }
    public string Text { get; set; } = "";
    public string? GeneratedText { get; set; }
    public List<string> EvidenceIds { get; set; } = new();
    public List<string> OpenQuestions { get; set; } = new();
    public string? Answers { get; set; }
    /// <summary>code / needsConfirmation / notCovered / confirmed</summary>
    public string Basis { get; set; } = "needsConfirmation";

    /// <summary>rule / model / file / manual</summary>
    public string Source { get; set; } = "manual";
    /// <summary>pending / accepted / deferred / rejected / stale</summary>
    public string ReviewState { get; set; } = "pending";

    /// <summary>"set" 写入/补全；"removeStale" 删除失效的 param 标签。</summary>
    public string Action { get; set; } = "set";

    /// <summary>目标标签已有有效内容时，必须明确勾选覆盖才会改写。</summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>审阅时该注释位置关联的全部证据指纹；重新扫描后不一致即判定过期。</summary>
    public string? ReviewedEvidenceHash { get; set; }
    public List<string> Warnings { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset? AppliedAt { get; set; }
}

public sealed class DraftStore
{
    public List<CommentCandidate> Candidates { get; set; } = new();
    public List<GenerationRun> Runs { get; set; } = new();
}

/// <summary>导入编码助手结果的结果：导入与拒绝数量、拒绝原因（与冻结页面响应同一形状，属性名序列化为 imported/rejected/errors）。</summary>
public sealed record DraftImportResult(int Imported, int Rejected, List<string> Errors);

public sealed class GenerationRun
{
    public string Id { get; set; } = "";
    public string Mode { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public int RequestedSlots { get; set; }
    public int Produced { get; set; }
    public int Rejected { get; set; }
    public List<string> Errors { get; set; } = new();

    /// <summary>succeeded / partial / failed / noResult</summary>
    public string Status { get; set; } = "";
}

public sealed class CandidateUpdateRequest
{
    public string? Text { get; set; }
    public string? ReviewState { get; set; }
    public bool? OverwriteExisting { get; set; }
    public string? Answers { get; set; }
}

public sealed class ManualCandidateRequest
{
    public string SlotId { get; set; } = "";
    public string Text { get; set; } = "";
    public string Action { get; set; } = "set";
}

public sealed class GenerateRequest
{
    private List<string> _slotIds = new();

    /// <summary>rule / model</summary>
    public string Mode { get; set; } = "rule";

    /// <summary>
    /// 要生成草稿的注释位置，空清单 = 全部缺口（<c>DraftService.SelectSlots</c>）。
    /// 写成 null 容忍的 setter：请求体显式带 <c>"slotIds": null</c> 时 System.Text.Json 会用 null
    /// 覆盖属性初始值，下游 <c>slotIds.Count</c> 就抛未处理异常（500），而不是按「未提供」走。
    /// </summary>
    public List<string> SlotIds
    {
        get => _slotIds;
        set => _slotIds = value ?? new();
    }

    /// <summary>模型模式必须显式确认发送材料范围。</summary>
    public bool ConfirmSend { get; set; }
}

/// <summary>
/// 材料文件/模型返回的候选格式（与“导出材料 → 编码助手处理 → 导入结果”共用）。
/// </summary>
public sealed class ExternalCandidate
{
    public string SlotId { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> EvidenceIds { get; set; } = new();
    public List<string> OpenQuestions { get; set; } = new();
    public string? Basis { get; set; }
}

public sealed class ExternalResult
{
    public string Format { get; set; } = "";
    public List<ExternalCandidate> Candidates { get; set; } = new();
}
