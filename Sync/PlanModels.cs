using ApifoxHelper.Contracts;

namespace ApifoxHelper.Sync;

public sealed class SyncPlan
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string Hash { get; set; } = "";
    public int TaskRevision { get; set; }

    /// <summary>awaitingConfirm / executing / verified / partial / failed / expired / interrupted / canceled</summary>
    public string Status { get; set; } = "awaitingConfirm";
    public string ProjectId { get; set; } = "";
    public string? ProjectName { get; set; }
    public string? Branch { get; set; }
    public string CliVersion { get; set; } = "";
    public string? SwaggerHash { get; set; }
    public string SupplementHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public List<PlanAction> Actions { get; set; } = new();
    public List<PlanExclusion> Excluded { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<ImpactEntry> OutOfScopeImpact { get; set; } = new();
    public List<string> PreservedSettings { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class PlanAction
{
    public string ActionId { get; set; } = "";
    public string ResourceKey { get; set; } = "";

    /// <summary>createModel / updateModel / createEndpoint / updateEndpoint</summary>
    public string Kind { get; set; } = "";
    public long? RemoteId { get; set; }
    public string? ModelName { get; set; }
    public string BeforeHash { get; set; } = "";
    public string ProtectedHash { get; set; } = "";
    public string DesiredHash { get; set; } = "";
    public string PayloadFile { get; set; } = "";
    public List<string> ManagedFields { get; set; } = new();
    public List<string> DependsOn { get; set; } = new();

    /// <summary>写入内容直接引用的源模型名。</summary>
    public List<string> ModelRefs { get; set; } = new();
    public string ValidationKey { get; set; } = "";
    public string Validation { get; set; } = "";
    public List<string> Changes { get; set; } = new();
    public List<string> Notes { get; set; } = new();

    /// <summary>pending / written / verified / writeFailed / unknown / verifyFailed / conflict / notRun</summary>
    public string State { get; set; } = "pending";
    public string? Message { get; set; }
    public long? CreatedId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}

public sealed class PlanExclusion
{
    public string ResourceKey { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class CreatePlanRequest
{
    public List<string> EndpointKeys { get; set; } = new();
}

public sealed class ApplyPlanRequest
{
    public string PlanHash { get; set; } = "";
}

public sealed class Baseline
{
    public string ResourceKey { get; set; } = "";
    public string VerifiedSourceHash { get; set; } = "";
    public string VerifiedRemoteHash { get; set; } = "";
    public string VerifiedTaskId { get; set; } = "";
    public DateTimeOffset VerifiedAt { get; set; }
}

public sealed class WriteCompatRecord
{
    public string CliVersion { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string? Branch { get; set; }
    public long EndpointId { get; set; }
    public bool Passed { get; set; }
    public DateTimeOffset At { get; set; }
    public List<ProbeCheck> Checks { get; set; } = new();
}

public sealed class ProbeCheck
{
    public string Name { get; set; } = "";
    public bool Passed { get; set; }
    public string Detail { get; set; } = "";
}

public sealed class WriteCompatStore
{
    public List<WriteCompatRecord> Records { get; set; } = new();
}

public sealed class WriteProbeRequest
{
    public string ProjectId { get; set; } = "";
    public string? Branch { get; set; }
    public long EndpointId { get; set; }

    /// <summary>必须为 true：探测会对指定的测试接口做两次内容不变的写入与一次说明临时修改并恢复。</summary>
    public bool Confirm { get; set; }
}
