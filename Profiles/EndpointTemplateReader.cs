namespace ApifoxHelper.Profiles;

/// <summary>
/// 从 Apifox 接口详情 JSON 提取新接口模板：只取允许的字段（目录、状态、服务、鉴权、安全方案、
/// 公共 Header 参数、标签），不克隆脚本、用例、Mock 或历史请求。
/// REST 路由与原生「模块与模板」对话框共用同一份映射；数值解析经 JsonNodeValue 单一口径。
/// </summary>
public static class EndpointTemplateReader
{
    public static NewEndpointTemplate Read(JsonObject data) => new()
    {
        FolderId = JsonNodeValue.ReadLong(data["folderId"]),
        Status = data["status"]?.ToString() ?? "developing",
        ServerId = data["serverId"]?.ToString(),
        Auth = data["auth"]?.DeepClone() as JsonObject,
        SecurityScheme = data["securityScheme"]?.DeepClone() as JsonObject,
        HeaderParameters = (data["parameters"]?["header"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(h => (JsonObject)h.DeepClone()).ToList(),
        Tags = (data["tags"] as JsonArray)?.Select(t => t?.ToString()).OfType<string>().ToList() ?? new List<string>(),
    };
}
