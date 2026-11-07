namespace ApifoxHelper.Profiles;

/// <summary>
/// 项目配置。源码工作区与 Apifox 目标是两个独立的写入边界；Apifox 项目 ID、分支与业务项目上下文是不同概念。
/// </summary>
public sealed class ProjectProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>仓库根目录（绝对路径）。</summary>
    public string WorkspaceRoot { get; set; } = "";

    /// <summary>API 项目文件，相对于仓库根目录。</summary>
    public string ApiProject { get; set; } = "";

    /// <summary>允许回写注释的相对路径前缀（目录或文件）；为空时只允许导出补丁。</summary>
    public List<string> AllowedSourcePaths { get; set; } = new();

    public AnalysisSettings Analysis { get; set; } = new();
    public GenerationSettings Generation { get; set; } = new();
    public SwaggerSource Swagger { get; set; } = new();
    public string? SourceVersionNote { get; set; }
    public List<ModuleScope> Modules { get; set; } = new();
    public ApifoxTarget Apifox { get; set; } = new();
    public NewEndpointTemplate NewEndpoint { get; set; } = new();
    public List<ResponseWrapperRule> ResponseWrappers { get; set; } = new();
    public SerializationSettings Serialization { get; set; } = new();
    public QualitySettings Quality { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class AnalysisSettings
{
    /// <summary>用于评估 MSBuild 条件和默认编译常量的配置名。</summary>
    public string Configuration { get; set; } = "Debug";

    /// <summary>显式指定编译常量；为 null 时按目标框架与配置推导。</summary>
    public List<string>? PreprocessorSymbols { get; set; }

    public int MaxTraceDepth { get; set; } = 3;
    public int MaxMethodsPerEndpoint { get; set; } = 40;

    /// <summary>项目名 → 生成的 XML 文档路径（相对仓库根或绝对路径），未配置时按默认输出目录推断。</summary>
    public Dictionary<string, string> XmlDocPaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>与 ASP.NET Core MvcOptions.SuppressAsyncSuffixInActionNames 一致，默认 true。</summary>
    public bool SuppressAsyncSuffixInActionNames { get; set; } = true;
}

public sealed class GenerationSettings
{
    /// <summary>none：只扫描和手工补全；openai：OpenAI 兼容的对话接口；file：导出材料交给编码助手处理后导入结果。</summary>
    public string Mode { get; set; } = "none";
    public string? BaseUrl { get; set; }
    public string? Model { get; set; }
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxSlotsPerRequest { get; set; } = 30;
}

public sealed class SwaggerSource
{
    /// <summary>none / file / url。源码阶段可以为空。</summary>
    public string Type { get; set; } = "none";
    public string? Location { get; set; }
}

public sealed class ModuleScope
{
    public string Name { get; set; } = "";
    public List<string> PathPrefixes { get; set; } = new();
}

public sealed class ApifoxTarget
{
    public string? ProjectId { get; set; }
    public string? ProjectName { get; set; }

    /// <summary>分支名；为空表示主分支。CLI 通过 --branch 分支名解析。</summary>
    public string? BranchName { get; set; }
    public string? BranchId { get; set; }

    /// <summary>可选：node 可执行文件路径；为空时从 PATH 查找。</summary>
    public string? NodePath { get; set; }

    /// <summary>可选：apifox-cli 的 cli.js 路径；为空时在全局 npm 目录查找。</summary>
    public string? CliScriptPath { get; set; }

    /// <summary>只读详情读取的并发数。写入始终串行。</summary>
    public int ReadConcurrency { get; set; } = 4;
}

/// <summary>
/// 新接口模板只包含明确列出的字段，不克隆任意接口的脚本、用例、Mock 或历史请求。
/// </summary>
public sealed class NewEndpointTemplate
{
    public long? FolderId { get; set; }
    public string Status { get; set; } = "developing";
    public string? ServerId { get; set; }
    public JsonObject? Auth { get; set; }
    public JsonObject? SecurityScheme { get; set; }
    public List<JsonObject> HeaderParameters { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class ResponseWrapperRule
{
    /// <summary>通用响应封装类型名，可写简单名或完整名。</summary>
    public string TypeName { get; set; } = "";
    public string DataMember { get; set; } = "Data";
}

public sealed class SerializationSettings
{
    /// <summary>camelCase（ASP.NET Core 默认）或 none（保持 C# 属性名）。</summary>
    public string NamingPolicy { get; set; } = "camelCase";

    /// <summary>全局是否以字符串输出枚举（例如注册了 JsonStringEnumConverter）。</summary>
    public bool EnumAsString { get; set; }

    /// <summary>
    /// 与 Swashbuckle 的 SupportNonNullableReferenceTypes 保持一致：false 时所有引用类型字段视为可空（Swashbuckle 默认行为）。
    /// </summary>
    public bool NonNullableReferenceTypes { get; set; }
}

public sealed class QualitySettings
{
    /// <summary>通用响应中承载具体数据的属性名；该属性只有 object 时提示“缺少具体 data 类型”。</summary>
    public string GenericDataProperty { get; set; } = "data";
    public bool RequireParameterDescription { get; set; } = true;
    public bool RequirePropertyDescription { get; set; } = true;
}
