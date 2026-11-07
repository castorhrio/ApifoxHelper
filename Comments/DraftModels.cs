namespace ApifoxHelper.Comments;

/// <summary>
/// XML 注释候选。依据状态（basis）与审阅状态（reviewState）分开记录：
/// basis：code（代码可证实）/ needsConfirmation（需业务确认）/ notCovered（分析未覆盖）/ confirmed（人工已确认）；
/// reviewState：pending / accepted / deferred / rejected / stale（证据已变化，需重新审阅）。
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
    public string Basis { get; set; } = "needsConfirmation";

    /// <summary>rule / model / file / manual</summary>
    public string Source { get; set; } = "manual";
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
    /// <summary>rule / model</summary>
    public string Mode { get; set; } = "rule";
    public List<string> SlotIds { get; set; } = new();

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
