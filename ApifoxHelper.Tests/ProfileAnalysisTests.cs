using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「序列化与分析」（项目配置的 JSON 命名与响应封装、代码分析、质量规则与 Apifox CLI 编辑）的口径断言
/// （ProfileAnalysis 单一落点，冻结页面与原生对话框同一取值与文案）：
/// JSON 命名两档与冻结页面同取值同顺序、未知回退；封装行/编译常量/XML 文档路径的解析与冻结页面
/// collect() 同一结果（缺数据属性用 Data、分号逗号分隔、每行「项目名=路径」）；
/// 数字留空用默认值、非数字明确拒绝（冻结页面会静默变成默认值，原生不静默丢值）；
/// XML 文档路径缺「=」的行明确拒绝（冻结页面会静默丢弃，原生不静默丢值）；Apply 写全部字段；
/// 导出配置的文件名、字节与内容类型经 ProfileExport 与 REST 下载同一口径。
/// </summary>
public class ProfileAnalysisTests
{
    // ── JSON 命名两档 ─────────────────────────────────────────────────────

    [Fact]
    public void NamingPolicies_TwoValuesInFrozenPageOrder()
    {
        Assert.Equal(
            [("camelCase", "camelCase（ASP.NET Core 默认）"), ("none", "保持 C# 属性名")],
            ProfileAnalysis.NamingPolicies);
    }

    [Theory]
    [InlineData("camelCase", 0)]
    [InlineData("none", 1)]
    [InlineData("Pascal", -1)]
    [InlineData("", -1)]
    public void NamingIndex_KnownValuesAndUnknownMinusOne(string namingPolicy, int expected) =>
        Assert.Equal(expected, ProfileAnalysis.NamingIndex(namingPolicy));

    // ── 响应封装 ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseWrappers_ParsesTypeNameAndDataMember()
    {
        var wrappers = ProfileAnalysis.ParseWrappers("PNOSActionResult=Data\n ApiResult = Result ");
        Assert.Equal(2, wrappers.Count);
        Assert.Equal("PNOSActionResult", wrappers[0].TypeName);
        Assert.Equal("Data", wrappers[0].DataMember);
        Assert.Equal("ApiResult", wrappers[1].TypeName);
        Assert.Equal("Result", wrappers[1].DataMember);
    }

    [Fact]
    public void ParseWrappers_MissingOrEmptyDataMemberFallsBackToData()
    {
        // 与冻结页面「(d || 'Data').trim()」一致：没有等号或等号后为空串都用 Data。
        var wrappers = ProfileAnalysis.ParseWrappers("Wrapper\nEmpty=\n");
        Assert.Equal(2, wrappers.Count);
        Assert.Equal(("Wrapper", "Data"), (wrappers[0].TypeName, wrappers[0].DataMember));
        Assert.Equal(("Empty", "Data"), (wrappers[1].TypeName, wrappers[1].DataMember));
    }

    [Fact]
    public void ParseWrappers_ThirdSegmentDroppedLikeFrozenPage()
    {
        // 冻结页面解构只取前两段：「a=b=c」的数据属性是 b，第三段丢弃。
        var wrappers = ProfileAnalysis.ParseWrappers("a=b=c");
        Assert.Equal(("a", "b"), (wrappers[0].TypeName, wrappers[0].DataMember));
    }

    [Fact]
    public void ParseWrappers_BlankLinesSkippedAndEmptyTypeNameKeptForStore()
    {
        // 空类型名的行由 ProfileStore 规整过滤（Save 时），解析层与冻结页面一样保留。
        var wrappers = ProfileAnalysis.ParseWrappers("\n  \nA=Data");
        Assert.Single(wrappers);
        Assert.Equal("A", wrappers[0].TypeName);
    }

    [Fact]
    public void FormatWrappers_RoundTripsThroughParse()
    {
        var wrappers = new List<ResponseWrapperRule>
        {
            new() { TypeName = "PNOSActionResult", DataMember = "Data" },
            new() { TypeName = "ApiResult", DataMember = "Result" },
        };
        var parsed = ProfileAnalysis.ParseWrappers(ProfileAnalysis.FormatWrappers(wrappers));
        Assert.Equal(wrappers.Select(w => (w.TypeName, w.DataMember)), parsed.Select(w => (w.TypeName, w.DataMember)));
    }

    // ── 编译常量 ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseSymbols_SplitsOnSemicolonAndComma()
    {
        var symbols = ProfileAnalysis.ParseSymbols("DEBUG; TRACE,NET8_0");
        Assert.Equal(["DEBUG", "TRACE", "NET8_0"], symbols);
    }

    [Fact]
    public void ParseSymbols_AllBlankIsNull()
    {
        Assert.Null(ProfileAnalysis.ParseSymbols(""));
        Assert.Null(ProfileAnalysis.ParseSymbols(" ; , "));
    }

    [Fact]
    public void FormatSymbols_NullRendersEmptyAndRoundTrips()
    {
        Assert.Equal("", ProfileAnalysis.FormatSymbols(null));
        Assert.Equal(["A", "B"], ProfileAnalysis.ParseSymbols(ProfileAnalysis.FormatSymbols(["A", "B"])));
    }

    // ── XML 文档路径 ──────────────────────────────────────────────────────

    [Fact]
    public void ParseXmlDocPaths_ParsesKeyValueLinesWithTrim()
    {
        var paths = ProfileAnalysis.ParseXmlDocPaths("Api=bin/Debug/net8.0/Api.xml\n Core = src/Core/Core.xml ");
        Assert.Equal(2, paths.Count);
        Assert.Equal("bin/Debug/net8.0/Api.xml", paths["Api"]);
        Assert.Equal("src/Core/Core.xml", paths["Core"]);
    }

    [Fact]
    public void ParseXmlDocPaths_ValueMayContainEquals()
    {
        var paths = ProfileAnalysis.ParseXmlDocPaths("Api=bin/a=b/x.xml");
        Assert.Equal("bin/a=b/x.xml", paths["Api"]);
    }

    [Fact]
    public void ParseXmlDocPaths_LaterDuplicateKeyWins()
    {
        var paths = ProfileAnalysis.ParseXmlDocPaths("Api=first.xml\nApi=second.xml");
        Assert.Equal("second.xml", paths["Api"]);
    }

    [Theory]
    [InlineData("没有等号的行")]
    [InlineData("=缺项目名.xml")]
    public void ParseXmlDocPaths_LineWithoutKeyOrSeparatorRejected(string line)
    {
        // 冻结页面会静默丢弃这样的行；原生明确拒绝，让用户知道值没有被采纳。
        var ex = Assert.Throws<UserException>(() => ProfileAnalysis.ParseXmlDocPaths("Api=ok.xml\n" + line));
        Assert.Equal("XML 文档路径每行应为「项目名=路径」。", ex.Message);
        Assert.Equal(ProfileAnalysis.FieldXmlDocPaths, ex.Field);
    }

    [Fact]
    public void FormatXmlDocPaths_RoundTripsThroughParse()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Api"] = "a.xml", ["Core"] = "c.xml" };
        var parsed = ProfileAnalysis.ParseXmlDocPaths(ProfileAnalysis.FormatXmlDocPaths(paths));
        Assert.Equal(paths.ToList(), parsed.ToList());
    }

    // ── 代码分析字段 ──────────────────────────────────────────────────────

    [Fact]
    public void ParseAnalysis_TrimsConfigurationAndParsesNumbers()
    {
        var fields = ProfileAnalysis.ParseAnalysis(" Release ", "DEBUG", "4", "80", "");
        Assert.Equal("Release", fields.Configuration);
        Assert.Equal(["DEBUG"], fields.PreprocessorSymbols);
        Assert.Equal(4, fields.MaxTraceDepth);
        Assert.Equal(80, fields.MaxMethodsPerEndpoint);
        Assert.Empty(fields.XmlDocPaths);
    }

    [Fact]
    public void ParseAnalysis_BlankFieldsUseFrozenPageDefaults()
    {
        // 与冻结页面一致：配置名空取 Debug、深度空取 3、预算空取 40、常量空为 null（按目标框架推导）。
        var fields = ProfileAnalysis.ParseAnalysis("", "", "", "", "");
        Assert.Equal("Debug", fields.Configuration);
        Assert.Null(fields.PreprocessorSymbols);
        Assert.Equal(3, fields.MaxTraceDepth);
        Assert.Equal(40, fields.MaxMethodsPerEndpoint);
    }

    [Theory]
    [InlineData("abc", "40", "调用深度应为数字。", "maxTraceDepth")]
    [InlineData("3", "1x", "方法预算应为数字。", "maxMethodsPerEndpoint")]
    public void ParseAnalysis_NonNumericRejectedNotSilentlyDefaulted(string depth, string budget, string message, string field)
    {
        // 冻结页面会把非法数字静默变成默认值；原生明确拒绝，让用户知道值没有被采纳。
        var ex = Assert.Throws<UserException>(() => ProfileAnalysis.ParseAnalysis("", "", depth, budget, ""));
        Assert.Equal(message, ex.Message);
        // 字段标识定位失败输入框（校验失败描边，DESIGN.md §6）：标识即配置 JSON 属性名。
        Assert.Equal(field, ex.Field);
    }

    [Fact]
    public void FieldIds_AreConfigJsonPropertyNames()
    {
        // 对话框按 UserException.Field 找输入框；标识取配置 JSON 的 camelCase 属性名（单一来源）。
        Assert.Equal("maxTraceDepth", ProfileAnalysis.FieldMaxTraceDepth);
        Assert.Equal("maxMethodsPerEndpoint", ProfileAnalysis.FieldMaxMethodsPerEndpoint);
        Assert.Equal("xmlDocPaths", ProfileAnalysis.FieldXmlDocPaths);
        Assert.Equal("readConcurrency", ProfileAnalysis.FieldReadConcurrency);
    }

    // ── Apifox CLI 字段 ───────────────────────────────────────────────────

    [Fact]
    public void ParseCli_TrimsPathsAndParsesConcurrency()
    {
        var fields = ProfileAnalysis.ParseCli(" C:\\node\\node.exe ", " C:\\npm\\cli.js ", "6");
        Assert.Equal("C:\\node\\node.exe", fields.NodePath);
        Assert.Equal("C:\\npm\\cli.js", fields.CliScriptPath);
        Assert.Equal(6, fields.ReadConcurrency);
    }

    [Fact]
    public void ParseCli_BlankPathsAreNullAndConcurrencyDefaultsToFour()
    {
        var fields = ProfileAnalysis.ParseCli("", "", "");
        Assert.Null(fields.NodePath);
        Assert.Null(fields.CliScriptPath);
        Assert.Equal(4, fields.ReadConcurrency);
    }

    [Fact]
    public void ParseCli_NonNumericConcurrencyRejected()
    {
        var ex = Assert.Throws<UserException>(() => ProfileAnalysis.ParseCli("", "", "8x"));
        Assert.Equal("读取并发应为数字。", ex.Message);
        Assert.Equal(ProfileAnalysis.FieldReadConcurrency, ex.Field);
    }

    // ── 应用到配置对象 ────────────────────────────────────────────────────

    private static ProfileAnalysis.AnalysisFields AnalysisFields() => new("Release", ["DEBUG"], 4, 80, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Api"] = "a.xml" });

    [Fact]
    public void Apply_WritesAllFields()
    {
        var profile = new ProjectProfile();
        var wrappers = new List<ResponseWrapperRule> { new() { TypeName = "W", DataMember = "D" } };
        ProfileAnalysis.Apply(
            profile, "none", enumAsString: true, nonNullableReferenceTypes: true, wrappers,
            AnalysisFields(), suppressAsyncSuffix: false, " result ", requireParameterDescription: false, requirePropertyDescription: false,
            new ProfileAnalysis.CliFields("node", "cli.js", 6));
        Assert.Equal("none", profile.Serialization.NamingPolicy);
        Assert.True(profile.Serialization.EnumAsString);
        Assert.True(profile.Serialization.NonNullableReferenceTypes);
        Assert.Equal("W", Assert.Single(profile.ResponseWrappers).TypeName);
        Assert.Equal("Release", profile.Analysis.Configuration);
        Assert.Equal(["DEBUG"], profile.Analysis.PreprocessorSymbols);
        Assert.Equal(4, profile.Analysis.MaxTraceDepth);
        Assert.Equal(80, profile.Analysis.MaxMethodsPerEndpoint);
        Assert.Equal("a.xml", profile.Analysis.XmlDocPaths["Api"]);
        Assert.False(profile.Analysis.SuppressAsyncSuffixInActionNames);
        Assert.Equal("result", profile.Quality.GenericDataProperty);
        Assert.False(profile.Quality.RequireParameterDescription);
        Assert.False(profile.Quality.RequirePropertyDescription);
        Assert.Equal("node", profile.Apifox.NodePath);
        Assert.Equal("cli.js", profile.Apifox.CliScriptPath);
        Assert.Equal(6, profile.Apifox.ReadConcurrency);
    }

    [Fact]
    public void Apply_BlankDataPropertyUsesFrozenPageDefault()
    {
        // 与冻结页面「f.dataProp.value || 'data'」一致：留空取 data。
        var profile = new ProjectProfile();
        ProfileAnalysis.Apply(
            profile, "camelCase", enumAsString: false, nonNullableReferenceTypes: false, [],
            AnalysisFields(), suppressAsyncSuffix: true, "  ", requireParameterDescription: true, requirePropertyDescription: true,
            new ProfileAnalysis.CliFields(null, null, 4));
        Assert.Equal("data", profile.Quality.GenericDataProperty);
        Assert.Null(profile.Apifox.NodePath);
        Assert.Null(profile.Apifox.CliScriptPath);
    }

    // ── 配置导出（ProfileExport 与 REST 下载同一口径） ────────────────────

    [Fact]
    public void Export_FileNameAndContentType()
    {
        var profile = new ProjectProfile { Id = "pabc123", Name = "示例" };
        Assert.Equal("apifox-helper-profile-pabc123.json", ProfileExport.FileName(profile));
        Assert.Equal("application/json", ProfileExport.ContentType);
    }

    [Fact]
    public void Export_BytesAreUtf8WithoutBomAndRoundTrip()
    {
        var profile = new ProjectProfile { Id = "p1", Name = "示例配置" };
        profile.ResponseWrappers.Add(new ResponseWrapperRule { TypeName = "PNOSActionResult", DataMember = "Data" });
        var bytes = ProfileExport.Render(profile);
        Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "导出字节不应带 BOM。");
        // 中文原样保存（JsonDefaults 口径），可直接再解析为同一配置。
        var json = Encoding.UTF8.GetString(bytes);
        Assert.Contains("示例配置", json);
        var parsed = JsonSerializer.Deserialize<ProjectProfile>(json, JsonDefaults.Options);
        Assert.NotNull(parsed);
        Assert.Equal(profile.Name, parsed!.Name);
        Assert.Equal(profile.ResponseWrappers.Single().TypeName, parsed.ResponseWrappers.Single().TypeName);
    }

    // ── 文案禁词 ──────────────────────────────────────────────────────────

    [Fact]
    public void UserVisibleTexts_NoForbiddenWords()
    {
        var texts = ProfileAnalysis.NamingPolicies.Select(n => n.Label)
            .Append("XML 文档路径每行应为「项目名=路径」。")
            .Append("调用深度应为数字。")
            .Append("方法预算应为数字。")
            .Append("读取并发应为数字。")
            .ToList();
        foreach (var text in texts)
        {
            Assert.False(Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }
}
