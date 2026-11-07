using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「模块与模板」（项目配置的模块范围与新接口模板编辑）的口径断言（ProfileAdvanced / EndpointTemplateReader
/// 单一落点，冻结页面与原生对话框同一格式与文案）：
/// 模块行「名=前缀1,前缀2」每行一条；状态四档与未知回退；JSON 字段「X 不是合法 JSON」与非对象/非数组明确拒绝；
/// 提取入口「请输入接口 ID」拒绝；提取映射只取模板允许的字段（不克隆脚本/用例/Mock）。
/// </summary>
public class ProfileAdvancedTests
{
    // ── 接口状态档 ────────────────────────────────────────────────────────

    [Fact]
    public void EndpointStatuses_FourValuesInFrozenPageOrder()
    {
        Assert.Equal(
            [("developing", "开发中"), ("designing", "设计中"), ("testing", "测试中"), ("released", "已发布")],
            ProfileAdvanced.EndpointStatuses);
    }

    [Theory]
    [InlineData("developing", "开发中")]
    [InlineData("designing", "设计中")]
    [InlineData("testing", "测试中")]
    [InlineData("released", "已发布")]
    [InlineData("custom-x", "custom-x")]
    public void StatusText_KnownValuesMapAndUnknownFallsBackToRaw(string status, string expected) =>
        Assert.Equal(expected, ProfileAdvanced.StatusText(status));

    [Theory]
    [InlineData("developing", 0)]
    [InlineData("designing", 1)]
    [InlineData("testing", 2)]
    [InlineData("released", 3)]
    [InlineData("custom-x", -1)]
    [InlineData("", -1)]
    public void StatusIndex_KnownValuesAndUnknownMinusOne(string status, int expected) =>
        Assert.Equal(expected, ProfileAdvanced.StatusIndex(status));

    // ── 模块行 ────────────────────────────────────────────────────────────

    [Fact]
    public void Modules_RoundTripThroughLines()
    {
        var modules = new List<ModuleScope>
        {
            new() { Name = "财务", PathPrefixes = ["/api/finance/"] },
            new() { Name = "计费", PathPrefixes = ["/api/bill/", "/api/invoice/"] },
            new() { Name = "空前缀", PathPrefixes = [] },
        };
        var parsed = ProfileAdvanced.ParseModules(ProfileAdvanced.FormatModules(modules));
        Assert.Equal(modules.Count, parsed.Count);
        Assert.Equal("财务", parsed[0].Name);
        Assert.Equal(["/api/finance/"], parsed[0].PathPrefixes);
        Assert.Equal("计费", parsed[1].Name);
        Assert.Equal(["/api/bill/", "/api/invoice/"], parsed[1].PathPrefixes);
        Assert.Equal("空前缀", parsed[2].Name);
        Assert.Empty(parsed[2].PathPrefixes);
    }

    [Fact]
    public void ParseModules_FrozenPageParity()
    {
        // 与冻结页面一致：等号前是名称、第二段按逗号拆分；第三段及以后与冻结页面一样被丢弃。
        var parsed = ProfileAdvanced.ParseModules("财务=/api/finance/, /api/bill\n\n  计费 = p1 , p2  \n无等号行\n多等号=a=b=c");
        Assert.Equal(4, parsed.Count);
        Assert.Equal("财务", parsed[0].Name);
        Assert.Equal(["/api/finance/", "/api/bill"], parsed[0].PathPrefixes);
        Assert.Equal("计费", parsed[1].Name);
        Assert.Equal(["p1", "p2"], parsed[1].PathPrefixes);
        Assert.Equal("无等号行", parsed[2].Name);
        Assert.Empty(parsed[2].PathPrefixes);
        Assert.Equal("多等号", parsed[3].Name);
        Assert.Equal(["a"], parsed[3].PathPrefixes);
    }

    [Fact]
    public void ParseModules_EmptyTextMeansNoModules() =>
        Assert.Empty(ProfileAdvanced.ParseModules(""));

    // ── 标签 ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tags_SplitTrimAndFilter()
    {
        // 逗号分隔、去空白、过滤空项；与冻结页面一样不去重。
        Assert.Equal(["财务", "开票"], ProfileAdvanced.ParseTags(" 财务 ,, 开票 "));
        Assert.Equal("财务,开票", ProfileAdvanced.FormatTags(["财务", "开票"]));
        Assert.Equal("", ProfileAdvanced.FormatTags([]));
    }

    // ── 模板 JSON 字段 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseObject_BlankMeansNotSet(string text) => Assert.Null(ProfileAdvanced.ParseObject(text, "鉴权 auth"));

    [Fact]
    public void ParseObject_ExplicitNullMeansNotSet() => Assert.Null(ProfileAdvanced.ParseObject("null", "鉴权 auth"));

    [Fact]
    public void ParseObject_ParsesObjectAndRejectsOtherKinds()
    {
        var parsed = ProfileAdvanced.ParseObject("""{"type":"apiKey","in":"header"}""", "鉴权 auth");
        Assert.NotNull(parsed);
        Assert.Equal("apiKey", parsed["type"]?.GetValue<string>());

        var arrayEx = Assert.Throws<UserException>(() => ProfileAdvanced.ParseObject("""[1]""", "鉴权 auth"));
        Assert.Equal("鉴权 auth 必须是 JSON 对象。", arrayEx.Message);
        Assert.Equal("鉴权 auth 必须是 JSON 对象。", Assert.Throws<UserException>(() => ProfileAdvanced.ParseObject("5", "鉴权 auth")).Message);
    }

    [Fact]
    public void ParseObject_InvalidJsonUsesFrozenPageWording()
    {
        Assert.Equal("鉴权 auth 不是合法 JSON", Assert.Throws<UserException>(() => ProfileAdvanced.ParseObject("{type:", "鉴权 auth")).Message);
        Assert.Equal("安全方案 不是合法 JSON", Assert.Throws<UserException>(() => ProfileAdvanced.ParseObject("x", "安全方案")).Message);
    }

    [Fact]
    public void ParseArray_BlankMeansEmptyAndObjectsAreKept()
    {
        Assert.Empty(ProfileAdvanced.ParseArray("", "公共 Header 参数"));
        var parsed = ProfileAdvanced.ParseArray("""[{"in":"header","name":"X-Request-Id"}]""", "公共 Header 参数");
        Assert.Single(parsed);
        Assert.Equal("X-Request-Id", parsed[0]["name"]?.GetValue<string>());
    }

    [Fact]
    public void ParseArray_RejectsNonArrayAndNonObjectElements()
    {
        Assert.Equal("公共 Header 参数 必须是 JSON 数组。", Assert.Throws<UserException>(() => ProfileAdvanced.ParseArray("""{"in":"header"}""", "公共 Header 参数")).Message);
        Assert.Equal("公共 Header 参数 的元素必须是 JSON 对象。", Assert.Throws<UserException>(() => ProfileAdvanced.ParseArray("[1]", "公共 Header 参数")).Message);
        Assert.Equal("公共 Header 参数 不是合法 JSON", Assert.Throws<UserException>(() => ProfileAdvanced.ParseArray("[x", "公共 Header 参数")).Message);
    }

    // ── 字段整体解析与应用 ────────────────────────────────────────────────

    [Theory]
    [InlineData("", null)]
    [InlineData("42", 42L)]
    [InlineData(" 42 ", 42L)]
    public void ParseFields_FolderIdOptionalNumber(string text, long? expected)
    {
        var fields = ProfileAdvanced.ParseFields(text, "developing", "", "", "", "", "");
        Assert.Equal(expected, fields.FolderId);
        Assert.Null(fields.ServerId);
        Assert.Empty(fields.Tags);
        Assert.Null(fields.Auth);
        Assert.Null(fields.SecurityScheme);
        Assert.Empty(fields.HeaderParameters);
    }

    [Fact]
    public void ParseFields_FolderIdMustBeNumber() =>
        Assert.Equal("目录 ID 应为数字。", Assert.Throws<UserException>(() => ProfileAdvanced.ParseFields("abc", "developing", "", "", "", "", "")).Message);

    [Fact]
    public void ParseFields_TrimsServerIdAndParsesRemaining()
    {
        var fields = ProfileAdvanced.ParseFields("7", "testing", " srv-1 ", "a, b", """{"type":"apiKey"}""", "", """[{"in":"header"}]""");
        Assert.Equal("srv-1", fields.ServerId);
        Assert.Equal(["a", "b"], fields.Tags);
        Assert.Equal("apiKey", fields.Auth?["type"]?.GetValue<string>());
        Assert.Single(fields.HeaderParameters);
    }

    [Fact]
    public void ParseFields_InvalidPartRejectsWholeAndLaterPartsAreNotApplied()
    {
        // 目录 ID 与鉴权都不合法时报第一条；整体拒绝由对话框保证（Apply 不被调用）。
        var ex = Assert.Throws<UserException>(() => ProfileAdvanced.ParseFields("abc", "developing", "", "", "{bad", "", ""));
        Assert.Equal("目录 ID 应为数字。", ex.Message);
    }

    [Fact]
    public void Apply_WritesModulesAndTemplateFields()
    {
        var profile = new ProjectProfile();
        var fields = ProfileAdvanced.ParseFields("9", "released", "srv", "t1,t2", """{"type":"apiKey"}""", """{"type":"http"}""", """[{"in":"header","name":"X"}]""");
        ProfileAdvanced.Apply(profile, "财务=/api/finance/\n计费", fields);
        Assert.Equal(2, profile.Modules.Count);
        Assert.Equal("计费", profile.Modules[1].Name);
        Assert.Equal(9, profile.NewEndpoint.FolderId);
        Assert.Equal("released", profile.NewEndpoint.Status);
        Assert.Equal("srv", profile.NewEndpoint.ServerId);
        Assert.Equal(["t1", "t2"], profile.NewEndpoint.Tags);
        Assert.Equal("apiKey", profile.NewEndpoint.Auth?["type"]?.GetValue<string>());
        Assert.Equal("http", profile.NewEndpoint.SecurityScheme?["type"]?.GetValue<string>());
        Assert.Equal("X", profile.NewEndpoint.HeaderParameters[0]["name"]?.GetValue<string>());
    }

    // ── 提取入口 ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("abc", false)]
    [InlineData("12a", false)]
    [InlineData("42", true)]
    [InlineData("0", true)]
    public void ExtractError_NumericEndpointIdRequired(string text, bool ok) =>
        Assert.Equal(ok, ProfileAdvanced.ExtractError(text) is null);

    [Fact]
    public void ExtractError_UsesFrozenPageWording() =>
        Assert.Equal("请输入接口 ID", ProfileAdvanced.ExtractError("abc"));

    // ── 提取结果回填 ──────────────────────────────────────────────────────

    [Fact]
    public void FromTemplate_FillsFieldTextsLikeFrozenPage()
    {
        var template = new NewEndpointTemplate
        {
            FolderId = 42,
            Status = "released",
            ServerId = "srv-1",
            Auth = new JsonObject { ["type"] = "apiKey" },
            SecurityScheme = new JsonObject { ["type"] = "http" },
            HeaderParameters = [new JsonObject { ["in"] = "header", ["name"] = "X" }],
            Tags = ["财务", "开票"],
        };
        var fields = ProfileAdvanced.FromTemplate(template);
        Assert.Equal("42", fields.FolderId);
        Assert.Equal("released", fields.Status);
        Assert.Equal("srv-1", fields.ServerId);
        Assert.Equal("财务,开票", fields.Tags);
        Assert.Contains("\"type\": \"apiKey\"", fields.Auth);
        Assert.Contains("\"type\": \"http\"", fields.Security);
        Assert.Contains("\"name\": \"X\"", fields.Headers);
    }

    [Fact]
    public void FromTemplate_EmptyTemplateMeansBlankFieldsAndDefaultStatus()
    {
        var fields = ProfileAdvanced.FromTemplate(new NewEndpointTemplate());
        Assert.Equal("", fields.FolderId);
        Assert.Equal("developing", fields.Status);
        Assert.Equal("", fields.ServerId);
        Assert.Equal("", fields.Tags);
        Assert.Equal("", fields.Auth);
        Assert.Equal("", fields.Security);
        Assert.Equal("", fields.Headers);
    }

    [Fact]
    public void FormatJson_IndentedWithLiteralChineseAndReparsable()
    {
        var node = new JsonObject { ["说明"] = "开票接口", ["嵌套"] = new JsonObject { ["a"] = 1 } };
        var text = ProfileAdvanced.FormatJson(node);
        Assert.Contains("\n  \"说明\": \"开票接口\"", text);
        Assert.DoesNotContain("\\u", text);
        Assert.True(Canonical.Equal(node, JsonNode.Parse(text)));
    }

    // ── 提取映射（EndpointTemplateReader，与 REST 路由同一份） ─────────────

    [Fact]
    public void TemplateReader_ReadsAllowedFieldsOnly()
    {
        var data = JsonNode.Parse("""
        {
          "id": 42,
          "method": "POST",
          "path": "/api/bill",
          "folderId": 7,
          "status": "testing",
          "serverId": "srv-9",
          "auth": { "type": "apiKey" },
          "securityScheme": { "type": "http" },
          "parameters": { "header": [ { "in": "header", "name": "X-Request-Id" }, "not-an-object" ] },
          "tags": [ "财务", 5, null ],
          "scripts": [ { "code": "should-not-leak" } ],
          "cases": [ { "name": "should-not-leak" } ]
        }
        """)!.AsObject();
        var template = EndpointTemplateReader.Read(data);
        Assert.Equal(7, template.FolderId);
        Assert.Equal("testing", template.Status);
        Assert.Equal("srv-9", template.ServerId);
        Assert.Equal("apiKey", template.Auth?["type"]?.GetValue<string>());
        Assert.Equal("http", template.SecurityScheme?["type"]?.GetValue<string>());
        var header = Assert.Single(template.HeaderParameters);
        Assert.Equal("X-Request-Id", header["name"]?.GetValue<string>());
        // 标签只过滤空值；数字标签与冻结页面一样保留为字符串（行为保持提取，不另加过滤）。
        Assert.Equal(["财务", "5"], template.Tags);
    }

    [Fact]
    public void TemplateReader_MissingFieldsFallBackToDefaults()
    {
        var template = EndpointTemplateReader.Read([]);
        Assert.Null(template.FolderId);
        Assert.Equal("developing", template.Status);
        Assert.Null(template.ServerId);
        Assert.Null(template.Auth);
        Assert.Null(template.SecurityScheme);
        Assert.Empty(template.HeaderParameters);
        Assert.Empty(template.Tags);
    }

    [Fact]
    public void TemplateReader_FolderIdAcceptsNumericString()
    {
        var template = EndpointTemplateReader.Read(new JsonObject { ["folderId"] = "7" });
        Assert.Equal(7, template.FolderId);
    }

    [Fact]
    public void TemplateReader_AuthIsDeepCloneNotSameInstance()
    {
        var auth = new JsonObject { ["type"] = "apiKey" };
        var data = new JsonObject { ["auth"] = auth };
        var template = EndpointTemplateReader.Read(data);
        Assert.NotSame(auth, template.Auth);
        auth["type"] = "changed";
        Assert.Equal("apiKey", template.Auth?["type"]?.GetValue<string>());
    }
}
