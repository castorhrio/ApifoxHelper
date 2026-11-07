using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「模块与模板」对话框（项目配置的模块范围与新接口模板编辑）的共享口径：
/// 模块行与标签的解析/回填格式、接口状态档、模板 JSON 字段校验与提取结果回填。
/// 与冻结页面同一格式与文案（模块「名=前缀1,前缀2」每行一条；JSON 字段「X 不是合法 JSON」）；
/// 保存时的规整与校验仍由 ProfileStore 单一口径执行，本文件不复制。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class ProfileAdvanced
{
    /// <summary>新接口状态四档（与冻结页面同一取值与顺序）。</summary>
    public static readonly (string Value, string Label)[] EndpointStatuses =
    [
        ("developing", "开发中"),
        ("designing", "设计中"),
        ("testing", "测试中"),
        ("released", "已发布"),
    ];

    /// <summary>状态中文显示；未知取值原样返回（与冻结页面同一回退）。</summary>
    public static string StatusText(string status)
    {
        foreach (var (value, label) in EndpointStatuses)
        {
            if (value == status) return label;
        }
        return status;
    }

    /// <summary>状态下拉框选中项；未知取值返回 -1（对话框按原值追加一项显示，不静默改值）。</summary>
    public static int StatusIndex(string status)
    {
        for (var i = 0; i < EndpointStatuses.Length; i++)
        {
            if (EndpointStatuses[i].Value == status) return i;
        }
        return -1;
    }

    /// <summary>模块清单 → 每行「模块名=路径前缀1,路径前缀2」（与冻结页面同一格式）。</summary>
    public static string FormatModules(IEnumerable<ModuleScope> modules) =>
        string.Join("\n", modules.Select(m => m.Name + "=" + string.Join(",", m.PathPrefixes)));

    /// <summary>
    /// 解析模块行。与冻结页面一致：等号前是模块名、第二段按逗号拆分前缀，空行跳过；
    /// 没有等号的行是只有名称的模块。空名与前缀斜杠规整交给 ProfileStore。
    /// </summary>
    public static List<ModuleScope> ParseModules(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Select(l =>
            {
                var parts = l.Split('=');
                var prefixes = parts.Length > 1
                    ? parts[1].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList()
                    : [];
                return new ModuleScope { Name = parts[0].Trim(), PathPrefixes = prefixes };
            })
            .ToList();

    /// <summary>标签 →「标签1,标签2」。</summary>
    public static string FormatTags(IEnumerable<string> tags) => string.Join(",", tags);

    /// <summary>解析标签：逗号分隔、去空白、过滤空项（与冻结页面一致）。</summary>
    public static List<string> ParseTags(string text) =>
        text.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    /// <summary>
    /// JSON 对象字段：留空与「null」都是不设置；非法 JSON →「X 不是合法 JSON」（与冻结页面同一文案）；
    /// 合法但不是对象时明确拒绝（REST 路由的绑定同样拒绝，这里提前内联给出）。
    /// </summary>
    public static JsonObject? ParseObject(string text, string label, string field)
    {
        var node = ParseNode(text, label, field);
        if (node is null) return null;
        return node as JsonObject ?? throw new UserException(label + " 必须是 JSON 对象。", field: field);
    }

    /// <summary>JSON 数组字段：留空即空清单；非法、非数组与数组中的非对象元素都明确拒绝（REST 绑定同样拒绝）。</summary>
    public static List<JsonObject> ParseArray(string text, string label, string field)
    {
        var node = ParseNode(text, label, field);
        if (node is null) return [];
        if (node is not JsonArray array) throw new UserException(label + " 必须是 JSON 数组。", field: field);
        var items = new List<JsonObject>();
        foreach (var item in array)
        {
            items.Add(item as JsonObject ?? throw new UserException(label + " 的元素必须是 JSON 对象。", field: field));
        }
        return items;
    }

    private static JsonNode? ParseNode(string text, string label, string field)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return null;
        try
        {
            return JsonNode.Parse(trimmed);
        }
        catch (JsonException)
        {
            throw new UserException(label + " 不是合法 JSON", field: field);
        }
    }

    /// <summary>JSON 字段回填文本：2 空格缩进、中文原样（与冻结页面的提取回填同一可读口径）。</summary>
    public static string FormatJson(JsonNode? node) => node is null ? "" : JsonSerializer.Serialize(node, JsonDefaults.Options);

    /// <summary>字段标识（配置 JSON 的 camelCase 属性名）：解析失败时随 UserException.Field 定位描红。</summary>
    public const string FieldFolderId = "folderId";
    public const string FieldAuth = "auth";
    public const string FieldSecurityScheme = "securityScheme";
    public const string FieldHeaderParameters = "headerParameters";

    /// <summary>解析后的模板字段；FolderId 的数字校验在这里统一给出。</summary>
    public sealed record TemplateFields(
        long? FolderId,
        string Status,
        string? ServerId,
        List<string> Tags,
        JsonObject? Auth,
        JsonObject? SecurityScheme,
        List<JsonObject> HeaderParameters);

    /// <summary>解析模板字段；任何一项不合法即整体拒绝（不写一半），文案见各解析方法。</summary>
    public static TemplateFields ParseFields(string folderIdText, string status, string serverIdText, string tagsText, string authText, string securityText, string headersText)
    {
        var folder = folderIdText.Trim();
        if (folder.Length > 0 && !long.TryParse(folder, out _)) throw new UserException("目录 ID 应为数字。", field: FieldFolderId);
        var server = serverIdText.Trim();
        return new TemplateFields(
            folder.Length == 0 ? null : long.Parse(folder),
            status,
            server.Length == 0 ? null : server,
            ParseTags(tagsText),
            ParseObject(authText, "鉴权 auth", FieldAuth),
            ParseObject(securityText, "安全方案", FieldSecurityScheme),
            ParseArray(headersText, "公共 Header 参数", FieldHeaderParameters));
    }

    /// <summary>把模块与模板字段应用到配置对象；真正的规整与落盘仍由 ProfileStore.Save 单一口径执行。</summary>
    public static void Apply(ProjectProfile profile, string modulesText, TemplateFields fields)
    {
        profile.Modules = ParseModules(modulesText);
        profile.NewEndpoint.FolderId = fields.FolderId;
        profile.NewEndpoint.Status = fields.Status;
        profile.NewEndpoint.ServerId = fields.ServerId;
        profile.NewEndpoint.Tags = fields.Tags;
        profile.NewEndpoint.Auth = fields.Auth;
        profile.NewEndpoint.SecurityScheme = fields.SecurityScheme;
        profile.NewEndpoint.HeaderParameters = fields.HeaderParameters;
    }

    /// <summary>「从既有接口提取」的接口 ID 校验；返回 null 表示可以提取（与冻结页面同一拒绝文案）。</summary>
    public static string? ExtractError(string endpointIdText) =>
        endpointIdText.Trim().Length == 0 || !endpointIdText.Trim().All(char.IsDigit) ? "请输入接口 ID" : null;

    /// <summary>提取结果回填到输入框的文本（与冻结页面同一取值与空值处理）。</summary>
    public sealed record ExtractedFields(string FolderId, string Status, string ServerId, string Tags, string Auth, string Security, string Headers);

    public static ExtractedFields FromTemplate(NewEndpointTemplate template)
    {
        var headers = template.HeaderParameters.Count == 0
            ? null
            : new JsonArray(template.HeaderParameters.Select(h => (JsonNode)h.DeepClone()).ToArray());
        return new ExtractedFields(
            template.FolderId?.ToString() ?? "",
            string.IsNullOrEmpty(template.Status) ? "developing" : template.Status,
            template.ServerId ?? "",
            FormatTags(template.Tags),
            FormatJson(template.Auth),
            FormatJson(template.SecurityScheme),
            FormatJson(headers));
    }
}
