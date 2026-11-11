using ApifoxHelper.Contracts;

namespace ApifoxHelper.Sync;

public sealed class CheckResult
{
    public DateTimeOffset ComputedAt { get; set; }
    public int TaskRevision { get; set; }
    public string? SwaggerHash { get; set; }
    public DateTimeOffset? SnapshotAt { get; set; }
    public bool SnapshotComplete { get; set; }
    public bool Offline { get; set; }

    /// <summary>swagger / remote / offline</summary>
    /// <summary>swagger：结构以 Swagger 为准；remote：未提供 Swagger，结构保持远端现状只补说明；offline：离线比较。</summary>
    public string StructureSource { get; set; } = "";
    public List<EndpointCheck> Endpoints { get; set; } = new();
    public List<ModelCheck> Models { get; set; } = new();
    public List<DiffItem> Items { get; set; } = new();
    public List<QualityIssue> Quality { get; set; } = new();
    public List<string> Problems { get; set; } = new();
    public List<string> UnmatchedRemote { get; set; } = new();
}

public sealed class EndpointCheck
{
    public string Key { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public bool InSource { get; set; }
    public bool InSwagger { get; set; }
    public bool InRemote { get; set; }
    public long? RemoteId { get; set; }
    public string? RemoteName { get; set; }
    public string? RemoteFolder { get; set; }
    public List<long> DuplicateRemoteIds { get; set; } = new();

    /// <summary>new / structure / docs / unchanged / blocked / sourceOnly</summary>
    public string Status { get; set; } = "";
    public List<string> Blockers { get; set; } = new();
    public List<string> Notes { get; set; } = new();
    public DocOperation? Remote { get; set; }
    public DocOperation? Target { get; set; }

    /// <summary>叠加说明后的 OpenAPI 操作对象（引用保持 #/components/schemas/X），用于构造写入内容。</summary>
    public JsonObject? RawTarget { get; set; }
    public List<string> ComponentRefs { get; set; } = new();
    public List<DocChange> DocChanges { get; set; } = new();
    public bool UsesResponseContract { get; set; }
    public string RemoteDetailHash { get; set; } = "";
}

/// <summary>
/// 说明字段的合并结果：远端为空时补充；远端已有且与“已确认的源码注释/人工补充”不同时形成冲突待选择；
/// 仅来自 Swagger 的不同说明默认保留远端。
/// </summary>
public sealed class DocChange
{
    public string Id { get; set; } = "";
    public string Location { get; set; } = "";
    public string Field { get; set; } = "description";
    public string? RemoteValue { get; set; }
    public string? TargetValue { get; set; }
    public string? FinalValue { get; set; }

    /// <summary>swagger / comment / sourceDoc / supplement</summary>
    public string Origin { get; set; } = "";

    /// <summary>fill / keepRemote / useTarget / conflict / supplement / suggestion</summary>
    public string Decision { get; set; } = "";
}

public sealed class ModelCheck
{
    public string Name { get; set; } = "";
    public long? RemoteId { get; set; }

    /// <summary>mapped / candidate / new / ambiguous</summary>
    public string MappingState { get; set; } = "";
    public List<long> Candidates { get; set; } = new();

    /// <summary>unchanged / changed / new / blocked / unknown</summary>
    public string Status { get; set; } = "";
    public List<string> UsedBy { get; set; } = new();
    public bool UsedInRequest { get; set; }
    public List<ImpactEntry> ImpactOutOfScope { get; set; } = new();
    public bool ImpactComplete { get; set; }
    public JsonObject? Source { get; set; }
    public JsonObject? Target { get; set; }
    public JsonObject? Remote { get; set; }
    public List<string> DependsOn { get; set; } = new();
    public List<string> Blockers { get; set; } = new();
    public List<DocChange> DocChanges { get; set; } = new();
    public string RemoteDetailHash { get; set; } = "";
}

public sealed class ImpactEntry
{
    public long Id { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class QualityIssue
{
    public string ResourceKey { get; set; } = "";
    public string Location { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Message { get; set; } = "";

    /// <summary>suggestion / blocking</summary>
    public string Severity { get; set; } = "suggestion";
}

public sealed class SupplementEntry
{
    public string ResourceKey { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>name / description / example</summary>
    public string Field { get; set; } = "description";
    public string? Value { get; set; }

    /// <summary>用户明确选择“保留现状，本次不补充”。</summary>
    public bool KeepAsIs { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class SupplementStore
{
    private List<SupplementEntry> _entries = new();

    /// <summary>
    /// 已保存的补充条目。这一份是 <c>PUT /api/tasks/{id}/supplement</c> 的请求体本身，
    /// <b>空清单是在用的形状</b>：原生对话框把一个接口所有编辑位都勾成「保留现状」时提交的就是空条目集，
    /// <c>SupplementService.Content</c> 数得出零个才算「内容没有变化、不写文件」。
    /// 写成 null 容忍的 setter：显式带 <c>"entries": null</c> 时 System.Text.Json 会用 null 覆盖属性初始值，
    /// 服务头一句 <c>foreach (var entry in body.Entries)</c> 就抛未处理异常（500），而不是按「没有条目」走。
    /// </summary>
    public List<SupplementEntry> Entries
    {
        get => _entries;
        set => _entries = value ?? new();
    }
}

public sealed class DecisionStore
{
    private Dictionary<string, string> _items = new(StringComparer.Ordinal);
    private Dictionary<string, string> _docChoices = new(StringComparer.Ordinal);
    private Dictionary<string, string> _modelBindings = new(StringComparer.Ordinal);

    /// <summary>accepted / excluded</summary>
    /// <summary>
    /// 高风险差异逐项确认的决策值：diffId → 决策值。未确认（含已排除）的接口不纳入结构同步。
    /// 三个字典都写成 null 容忍的 setter，同 <see cref="SupplementStore.Entries"/>：这一份是
    /// <c>PUT /api/tasks/{id}/decisions</c> 的请求体本身，<b>空字典是在用的形状</b>（清空全部决策），
    /// 而显式 null 会让 <c>DecisionService</c> 头三句 <c>.Values</c> 抛未处理异常——该走到那三句
    /// 逐值校验（400）的形状变成 500。<b>比较器保持有序逐字比对，不随归一改动。</b>
    /// </summary>
    public Dictionary<string, string> Items
    {
        get => _items;
        set => _items = value ?? new(StringComparer.Ordinal);
    }

    /// <summary>说明冲突的选择：DocChange.Id → keepRemote / useTarget。</summary>
    public Dictionary<string, string> DocChoices
    {
        get => _docChoices;
        set => _docChoices = value ?? new(StringComparer.Ordinal);
    }

    /// <summary>ID 字符串 / new</summary>
    /// <summary>模型绑定取值：源模型名 → 远端模型 ID（字符串）或 "new"。</summary>
    public Dictionary<string, string> ModelBindings
    {
        get => _modelBindings;
        set => _modelBindings = value ?? new(StringComparer.Ordinal);
    }
}

public sealed class ModelMappingStore
{
    public string ProjectId { get; set; } = "";
    public string? Branch { get; set; }
    public Dictionary<string, MappingEntry> Models { get; set; } = new(StringComparer.Ordinal);
}

public sealed class MappingEntry
{
    public long RemoteId { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public string? ConfirmedByTask { get; set; }
}
