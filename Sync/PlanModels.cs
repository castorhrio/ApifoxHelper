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
    private List<string> _endpointKeys = new();

    /// <summary>
    /// 要纳入计划的接口身份。<b>这里的空与 <c>GenerateRequest.SlotIds</c> 的空不是同一个意思</b>：
    /// 那一处空清单 = 全部缺口，这一处空清单 = 一个都没选，由 <c>SyncPlanService.CreateAsync</c> 那句
    /// 「请至少选择一个接口。」定义。写成 null 容忍的 setter：请求体显式带 <c>"endpointKeys": null</c> 时
    /// System.Text.Json 会用 null 覆盖属性初始值，路由那一句 <c>request.EndpointKeys.ToList()</c> 就抛
    /// 未处理异常（500 分支那句「内部错误」），而不是走到上面那句本该给它的 400。
    /// </summary>
    public List<string> EndpointKeys
    {
        get => _endpointKeys;
        set => _endpointKeys = value ?? new();
    }
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

/// <summary>
/// 连接检查结果（CompatService.CapabilitiesAsync）：CLI、登录、项目读取与写入兼容四项。
/// 原生「连接与写入探测」对话框进程内读取同一形状；Web /api/capabilities 路由序列化的也是这份类型，
/// JSON 键名与路由行为保持不变。
/// </summary>
public sealed class CapabilityReport
{
    public CapabilityCli Cli { get; set; } = new();
    public CapabilityLogin? Login { get; set; }
    public CapabilityProject? Project { get; set; }
    public CapabilityWriteCompat WriteCompat { get; set; } = new();
}

public sealed class CapabilityCli
{
    public bool Found { get; set; }
    public string? Node { get; set; }
    public string? Script { get; set; }
    public string? Version { get; set; }
    public string? Error { get; set; }
}

public sealed class CapabilityLogin
{
    public bool LoggedIn { get; set; }
    public string Message { get; set; } = "";
}

public sealed class CapabilityProject
{
    public bool Readable { get; set; }
    public int? Total { get; set; }
    public string Message { get; set; } = "";
}

public sealed class CapabilityWriteCompat
{
    public bool Verified { get; set; }
    public WriteCompatRecord? Record { get; set; }
    public string Message { get; set; } = "";
}
