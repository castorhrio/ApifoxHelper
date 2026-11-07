using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「序列化与分析」对话框（项目配置的序列化与响应封装、代码分析、质量规则与 Apifox CLI 编辑）的共享口径：
/// JSON 命名两档（与冻结页面同取值同顺序）、封装行/编译常量/XML 文档路径的解析与回填格式、
/// 数字字段的默认值与拒绝文案、各提示与默认值（与冻结页面 collect() 同一结果）。
/// 数字越界钳制（调用深度 0–6、方法预算 1–200、读取并发 1–8）与 JSON 命名取值校验仍由
/// ProfileStore 单一口径在「保存」时执行，本文件不复制。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ProfileAnalysis
{
    /// <summary>JSON 命名两档（与冻结页面同一取值与顺序）。</summary>
    public static readonly (string Value, string Label)[] NamingPolicies =
    [
        ("camelCase", "camelCase（ASP.NET Core 默认）"),
        ("none", "保持 C# 属性名"),
    ];

    /// <summary>JSON 命名下拉框选中项；未知取值返回 -1（对话框按原值追加一项显示，不静默改值）。</summary>
    public static int NamingIndex(string namingPolicy)
    {
        for (var i = 0; i < NamingPolicies.Length; i++)
        {
            if (NamingPolicies[i].Value == namingPolicy) return i;
        }
        return -1;
    }

    /// <summary>数字字段留空时的默认值（与冻结页面「+值 || 默认」同一结果）。</summary>
    public const string DefaultConfigurationName = "Debug";
    public const int DefaultMaxTraceDepth = 3;
    public const int DefaultMaxMethodsPerEndpoint = 40;
    public const int DefaultReadConcurrency = 4;
    public const string DefaultGenericDataProperty = "data";

    /// <summary>字段标识（配置 JSON 的 camelCase 属性名）：解析失败时随 UserException.Field 定位描红。</summary>
    public const string FieldMaxTraceDepth = "maxTraceDepth";
    public const string FieldMaxMethodsPerEndpoint = "maxMethodsPerEndpoint";
    public const string FieldXmlDocPaths = "xmlDocPaths";
    public const string FieldReadConcurrency = "readConcurrency";

    /// <summary>封装清单 → 每行「类型名=数据属性名」（与冻结页面同一格式）。</summary>
    public static string FormatWrappers(IEnumerable<ResponseWrapperRule> wrappers) =>
        string.Join("\n", wrappers.Select(w => w.TypeName + "=" + w.DataMember));

    /// <summary>
    /// 解析封装行。与冻结页面一致：每行先去空白再拆分，「类型名=数据属性名」，
    /// 没有等号或等号后为空时用 Data；等号后的第二段之后与冻结页面一样丢弃；空行跳过。
    /// 空类型名的行由 ProfileStore 规整过滤。
    /// </summary>
    public static List<ResponseWrapperRule> ParseWrappers(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l =>
            {
                var parts = l.Split('=');
                var dataMember = parts.Length > 1 && parts[1].Length > 0 ? parts[1].Trim() : "Data";
                return new ResponseWrapperRule { TypeName = parts[0].Trim(), DataMember = dataMember };
            })
            .ToList();

    /// <summary>编译常量 →「A;B;C」（与冻结页面同一分隔符）。</summary>
    public static string FormatSymbols(IEnumerable<string>? symbols) => symbols is null ? "" : string.Join(";", symbols);

    /// <summary>解析编译常量：分号或逗号分隔、去空白、过滤空项；全部为空即 null（按目标框架与配置推导）。</summary>
    public static List<string>? ParseSymbols(string text)
    {
        var items = text.Split([';', ',']).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        return items.Count == 0 ? null : items;
    }

    /// <summary>XML 文档路径 → 每行「项目名=路径」。</summary>
    public static string FormatXmlDocPaths(IEnumerable<KeyValuePair<string, string>> paths) =>
        string.Join("\n", paths.Select(p => p.Key + "=" + p.Value));

    /// <summary>解析 XML 文档路径：每行「项目名=路径」，项目名与路径都去空白；缺「=」或项目名为空的行明确拒绝——
    /// 冻结页面会静默丢弃这样的行，原生拒绝让用户知道值没有被采纳。同名（不区分大小写）行后者覆盖前者，
    /// 与冻结页面对象属性重复赋值一致。</summary>
    public static Dictionary<string, string> ParseXmlDocPaths(string text)
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) throw new UserException("XML 文档路径每行应为「项目名=路径」。", field: FieldXmlDocPaths);
            paths[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }
        return paths;
    }

    /// <summary>解析后的代码分析字段；数字留空用默认值，非数字内联拒绝（冻结页面会静默变成默认值，原生不静默丢值）。</summary>
    public sealed record AnalysisFields(string Configuration, List<string>? PreprocessorSymbols, int MaxTraceDepth, int MaxMethodsPerEndpoint, Dictionary<string, string> XmlDocPaths);

    /// <summary>解析代码分析字段；任何一项不合法即整体拒绝（不写一半），文案见各解析方法。</summary>
    public static AnalysisFields ParseAnalysis(string configurationText, string symbolsText, string depthText, string budgetText, string xmlDocPathsText)
    {
        var configuration = configurationText.Trim();
        return new AnalysisFields(
            configuration.Length == 0 ? DefaultConfigurationName : configuration,
            ParseSymbols(symbolsText),
            ParseNumber(depthText, "调用深度", DefaultMaxTraceDepth, FieldMaxTraceDepth),
            ParseNumber(budgetText, "方法预算", DefaultMaxMethodsPerEndpoint, FieldMaxMethodsPerEndpoint),
            ParseXmlDocPaths(xmlDocPathsText));
    }

    /// <summary>解析后的 Apifox CLI 字段；路径去空白、留空即不设置（与冻结页面「|| null」同一结果）。</summary>
    public sealed record CliFields(string? NodePath, string? CliScriptPath, int ReadConcurrency);

    public static CliFields ParseCli(string nodePathText, string cliScriptPathText, string concurrencyText) => new(
        TextOrNull(nodePathText),
        TextOrNull(cliScriptPathText),
        ParseNumber(concurrencyText, "读取并发", DefaultReadConcurrency, FieldReadConcurrency));

    /// <summary>
    /// 把全部字段应用到配置对象；数字越界钳制与 JSON 命名取值校验仍由 ProfileStore 在「保存」时执行。
    /// </summary>
    public static void Apply(
        ProjectProfile profile,
        string namingPolicy,
        bool enumAsString,
        bool nonNullableReferenceTypes,
        List<ResponseWrapperRule> wrappers,
        AnalysisFields analysis,
        bool suppressAsyncSuffix,
        string genericDataProperty,
        bool requireParameterDescription,
        bool requirePropertyDescription,
        CliFields cli)
    {
        profile.Serialization.NamingPolicy = namingPolicy;
        profile.Serialization.EnumAsString = enumAsString;
        profile.Serialization.NonNullableReferenceTypes = nonNullableReferenceTypes;
        profile.ResponseWrappers = wrappers;
        profile.Analysis.Configuration = analysis.Configuration;
        profile.Analysis.PreprocessorSymbols = analysis.PreprocessorSymbols;
        profile.Analysis.MaxTraceDepth = analysis.MaxTraceDepth;
        profile.Analysis.MaxMethodsPerEndpoint = analysis.MaxMethodsPerEndpoint;
        profile.Analysis.XmlDocPaths = analysis.XmlDocPaths;
        profile.Analysis.SuppressAsyncSuffixInActionNames = suppressAsyncSuffix;
        var dataProperty = genericDataProperty.Trim();
        profile.Quality.GenericDataProperty = dataProperty.Length == 0 ? DefaultGenericDataProperty : dataProperty;
        profile.Quality.RequireParameterDescription = requireParameterDescription;
        profile.Quality.RequirePropertyDescription = requirePropertyDescription;
        profile.Apifox.NodePath = cli.NodePath;
        profile.Apifox.CliScriptPath = cli.CliScriptPath;
        profile.Apifox.ReadConcurrency = cli.ReadConcurrency;
    }

    private static int ParseNumber(string text, string label, int fallback, string field) =>
        text.Trim().Length == 0 ? fallback
        : int.TryParse(text.Trim(), out var value) ? value
        : throw new UserException(label + "应为数字。", field: field);

    private static string? TextOrNull(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
