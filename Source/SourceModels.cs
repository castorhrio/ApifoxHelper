namespace ApifoxHelper.Source;

public sealed class EndpointInfo
{
    /// <summary>接口身份：大写 HTTP 方法 + 精确路径（保留大小写）。</summary>
    public string Key { get; set; } = "";
    public string HttpMethod { get; set; } = "";
    public string Path { get; set; } = "";
    public string RouteTemplate { get; set; } = "";
    public string Controller { get; set; } = "";
    public string Action { get; set; } = "";
    public string MethodSymbolId { get; set; } = "";
    public string Signature { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string DeclaredReturnType { get; set; } = "";
    public string? EffectiveReturnType { get; set; }
    public string? EffectiveReturnTypeId { get; set; }
    public List<string> ProducesTypes { get; set; } = new();
    public List<ParameterBinding> Parameters { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

public sealed class ParameterBinding
{
    public string Name { get; set; } = "";
    public string ExternalName { get; set; } = "";

    /// <summary>query / path / header / body / form / services / special / unknown</summary>
    public string Source { get; set; } = "";
    public bool Inferred { get; set; }
    public string Type { get; set; } = "";
    public string? TypeId { get; set; }
    public bool IsComplex { get; set; }
    public bool HasDefault { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsNullable { get; set; }
}

/// <summary>
/// 一个应当具备说明的注释位置（方法 summary、某个 param、returns、类型或属性 summary、枚举成员）。
/// </summary>
public sealed class DocSlot
{
    public string Id { get; set; } = "";
    public string SymbolId { get; set; } = "";

    /// <summary>method / type / property / recordParameter / enum / enumMember</summary>
    public string SymbolKind { get; set; } = "";
    public string Display { get; set; } = "";

    /// <summary>summary / param / returns</summary>
    public string Tag { get; set; } = "";
    public string? ParameterName { get; set; }

    /// <summary>valid / missing / empty / stale / inheritDoc / inheritDocUnresolved / invalidXml / unsupported</summary>
    public string Status { get; set; } = "";
    public string? ExistingText { get; set; }
    public string File { get; set; } = "";
    public int Line { get; set; }
    public bool Editable { get; set; }
    public string? NotEditableReason { get; set; }

    /// <summary>endpoint / request / response / enum</summary>
    public string Role { get; set; } = "";
    public string? JsonName { get; set; }
    public string? TypeDisplay { get; set; }
    public string? OwnerTypeId { get; set; }

    /// <summary>所属类型在 OpenAPI 中的模型名（按 Swashbuckle 默认命名），用于把说明映射到 Swagger/Apifox 模型字段。</summary>
    public string? ComponentName { get; set; }
    public List<string> EndpointKeys { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public List<string> EvidenceIds { get; set; } = new();

    public static string MakeId(string symbolId, string tag, string? parameterName) =>
        "s" + Ids.ShortHash(symbolId + "|" + tag + "|" + (parameterName ?? ""), 14);
}

public sealed class CodeEvidence
{
    public string Id { get; set; } = "";

    /// <summary>route / binding / validation / guard / return / call / queryFilter / mapping / default / enumMember / existingDoc / docDiagnostic / responseData</summary>
    public string Kind { get; set; } = "";
    public string OwnerSymbolId { get; set; } = "";
    public List<string> RelatedSymbolIds { get; set; } = new();
    public string File { get; set; } = "";
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Snippet { get; set; } = "";
    public string SnippetHash { get; set; } = "";
    public string? Summary { get; set; }
}

public sealed class UnresolvedCall
{
    public string Expression { get; set; } = "";
    public string Reason { get; set; } = "";
    public string File { get; set; } = "";
    public int Line { get; set; }
    public List<string> Candidates { get; set; } = new();
}

public sealed class EndpointCoverage
{
    public string EndpointKey { get; set; } = "";
    public List<string> MethodsRead { get; set; } = new();
    public List<UnresolvedCall> Unresolved { get; set; } = new();
    public List<string> ExternalCalls { get; set; } = new();
    public bool BudgetExceeded { get; set; }
    public List<string> EvidenceIds { get; set; } = new();
}

public sealed class ResponseBranch
{
    public string BranchKey { get; set; } = "";
    public int? HttpStatus { get; set; }
    public string Condition { get; set; } = "";
    public string? BusinessStatus { get; set; }

    /// <summary>type / null / none / anonymous / unknown</summary>
    public string DataKind { get; set; } = "";
    public string? DataType { get; set; }
    public string? DataTypeId { get; set; }
    public bool DataNullable { get; set; }
    public string File { get; set; } = "";
    public int Line { get; set; }
    public string? EvidenceId { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// 逐接口响应补充契约：通用封装中 data 的实际类型。只增强该接口文档，不修改共享封装。
/// </summary>
public sealed class ResponseContract
{
    public string EndpointKey { get; set; } = "";
    public string WrapperType { get; set; } = "";
    public string DataMember { get; set; } = "";
    public string DataJsonName { get; set; } = "";
    public List<ResponseBranch> Branches { get; set; } = new();
    public string? DataType { get; set; }
    public string? DataTypeId { get; set; }
    public bool DataNullable { get; set; }

    /// <summary>pending / confirmed / rejected / blocked / stale</summary>
    public string ReviewState { get; set; } = "pending";
    public List<string> UnresolvedPoints { get; set; } = new();
    public List<string> EvidenceIds { get; set; } = new();
    public string SourceFingerprint { get; set; } = "";

    /// <summary>OpenAPI 3.0 片段：整个 200 响应体（封装 + 具体 data）。</summary>
    public JsonObject? ResponseSchema { get; set; }

    /// <summary>响应体引用的组件模型（按 Swashbuckle 默认命名）。</summary>
    public Dictionary<string, JsonObject> Components { get; set; } = new();
    public DateTimeOffset? ReviewedAt { get; set; }
}

public sealed class SourceManifest
{
    public string WorkspaceRoot { get; set; } = "";
    public string ApiProject { get; set; } = "";
    public string AnalysisLevel { get; set; } = "";
    public string ManifestHash { get; set; } = "";
    public DateTimeOffset ScannedAt { get; set; }
    public List<ProjectSummary> Projects { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, string> FileHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProjectSummary
{
    public string Name { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public int Files { get; set; }
    public bool AssetsFound { get; set; }
    public int MissingPackageAssets { get; set; }
    public bool GenerateDocumentationFile { get; set; }
    public string? DocumentationFile { get; set; }
    public int DeclarationErrors { get; set; }
}

public sealed class ScanOutput
{
    public List<EndpointInfo> Endpoints { get; set; } = new();
    public List<DocSlot> Slots { get; set; } = new();
    public List<string> UnresolvedSymbols { get; set; } = new();
    public List<string> ExternalTypes { get; set; } = new();
    public CoverageSummary Coverage { get; set; } = new();
}

public sealed class CoverageSummary
{
    public int Total { get; set; }
    public int Valid { get; set; }
    public int InheritDoc { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByRole { get; set; } = new();
    public int UnresolvedSymbols { get; set; }
    public double Ratio => Total == 0 ? 0 : Math.Round((double)(Valid + InheritDoc) / Total, 4);
}

public sealed class EvidenceStore
{
    public List<CodeEvidence> Items { get; set; } = new();
    public List<EndpointCoverage> Coverage { get; set; } = new();
}
