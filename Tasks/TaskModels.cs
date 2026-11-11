using ApifoxHelper.Profiles;

namespace ApifoxHelper.Tasks;

/// <summary>
/// 检查任务。源码、生成文档、远端三个阶段分别记录状态，任何一个阶段完成都不能代表全链路完成。
/// </summary>
public sealed class CheckTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProfileId { get; set; } = "";
    public ProjectProfile ProfileSnapshot { get; set; } = new();
    public List<string> ModuleNames { get; set; } = new();

    /// <summary>本次范围内的路径前缀（模块映射展开 + 手工补充），按忽略大小写的前缀匹配。</summary>
    public List<string> PathPrefixes { get; set; } = new();

    /// <summary>手工指定的接口（"GET /api/x"），与路径前缀取并集；两者都为空表示 API 项目全部接口。</summary>
    public List<string> EndpointKeys { get; set; } = new();

    public string? SourceVersionNote { get; set; }
    public bool OfflineMode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ToolVersion { get; set; } = "";
    public string? CliVersion { get; set; }
    public StageResults Stages { get; set; } = new();

    /// <summary>每次影响同步计划的修改都会递增；计划记录创建时的版本，不一致即过期。</summary>
    public int Revision { get; set; }
}

public sealed class StageResults
{
    public SourceStage Source { get; set; } = new();
    public GeneratedStage Generated { get; set; } = new();
    public RemoteStage Remote { get; set; } = new();
}

public sealed class SourceStage
{
    /// <summary>notStarted / scanned / reviewing / planReady / applied / partiallyApplied / failed</summary>
    public string Status { get; set; } = "notStarted";
    public DateTimeOffset? ScannedAt { get; set; }
    public string? AnalysisLevel { get; set; }
    public string? ManifestHash { get; set; }
    public int Endpoints { get; set; }
    public int Slots { get; set; }
    public int ValidSlots { get; set; }
    public int UnresolvedSymbols { get; set; }
    public int Candidates { get; set; }
    public int Accepted { get; set; }
    public int AppliedEdits { get; set; }
    public bool PatchExported { get; set; }
    public string? Message { get; set; }
}

public sealed class GeneratedStage
{
    /// <summary>notChecked / pending / verified / partial / unavailable</summary>
    public string Status { get; set; } = "notChecked";
    public DateTimeOffset? CheckedAt { get; set; }
    public int Entries { get; set; }
    public int InSource { get; set; }
    public int InXml { get; set; }
    public int InSwagger { get; set; }
    public string? Message { get; set; }
}

public sealed class RemoteStage
{
    /// <summary>notChecked / snapshotReady / pending / previewable / awaitingConfirm / executing / verified / partial / failed / expired / interrupted / offline</summary>
    public string Status { get; set; } = "notChecked";
    public DateTimeOffset? SnapshotAt { get; set; }
    public bool SnapshotComplete { get; set; }
    public string? SnapshotMessage { get; set; }
    public string? SourceHash { get; set; }
    public string? SourceKind { get; set; }
    public int Added { get; set; }
    public int StructureChanged { get; set; }
    public int DocChanged { get; set; }
    public int Conflicts { get; set; }
    public int Blocked { get; set; }
    public int Unchanged { get; set; }
    public int ActionsVerified { get; set; }
    public int ActionsFailed { get; set; }
    public int ActionsNotRun { get; set; }
    public string? Message { get; set; }

    /// <summary>最近一次检查所基于的任务版本；与当前 Revision 不同说明检查之后内容有变化，需要重新检查才能生成计划。</summary>
    public int? CheckedRevision { get; set; }
}

public sealed class CreateTaskRequest
{
    private List<string> _moduleNames = new();
    private List<string> _pathPrefixes = new();
    private List<string> _endpointKeys = new();

    public string ProfileId { get; set; } = "";
    public string? Name { get; set; }

    /// <summary>
    /// 三个范围维度（模块 / 路径前缀 / 指定接口）都写成 null 容忍的 setter，同 <c>GenerateRequest.SlotIds</c>（第 212 轮同一修法）：
    /// 这一份是 <c>POST /api/tasks</c> 的请求体本身，显式带 <c>"moduleNames": null</c> 时 System.Text.Json
    /// 会用 null 覆盖属性初始值，<c>TaskStore.Create</c> 头三句清单操作就抛未处理异常（500）。
    /// <b>这里的空 = 这一维不圈范围</b>：路径前缀与指定接口两者皆空即全项目（<c>TaskStore.InScope</c>），
    /// 页面那两行留空送的就是空清单，所以归一目标与「未提供」同义，不是待拒的畸形输入。
    /// 未知模块名仍按原句「配置中没有模块「…」。」拒绝。
    /// </summary>
    public List<string> ModuleNames
    {
        get => _moduleNames;
        set => _moduleNames = value ?? new();
    }

    public List<string> PathPrefixes
    {
        get => _pathPrefixes;
        set => _pathPrefixes = value ?? new();
    }

    public List<string> EndpointKeys
    {
        get => _endpointKeys;
        set => _endpointKeys = value ?? new();
    }

    public string? SourceVersionNote { get; set; }
    public bool OfflineMode { get; set; }
}
