using ApifoxHelper.Profiles;

namespace ApifoxHelper.Contracts;

public sealed class OpenApiInfo
{
    public string Kind { get; set; } = "";
    public string Location { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Version { get; set; } = "";
    public bool Supported { get; set; }
    public int Operations { get; set; }
    public DateTimeOffset LoadedAt { get; set; }
    public List<string> Problems { get; set; } = new();
}

/// <summary>
/// 读取框架生成的 OpenAPI JSON（本机文件或本机工具直接访问的 URL）。只支持 OpenAPI 3.0.x 用于同步；
/// Swagger UI 页面、YAML、Swagger 2.0、OpenAPI 3.1 都明确报错或只做标识，不静默降级。
/// </summary>
public sealed class OpenApiSource
{
    private const int MaxBytes = 50 * 1024 * 1024;
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<(string Text, JsonObject Document, OpenApiInfo Info)> LoadAsync(SwaggerSource source, string workspaceRoot, CancellationToken ct)
    {
        var info = new OpenApiInfo { Kind = source.Type, Location = source.Location ?? "", LoadedAt = DateTimeOffset.Now };
        string text;
        switch (source.Type)
        {
            case "file":
                var path = Path.IsPathFullyQualified(source.Location!) ? source.Location! : Path.Combine(workspaceRoot, source.Location!);
                if (!File.Exists(path)) throw new UserException("Swagger 文件不存在：" + source.Location);
                if (new FileInfo(path).Length > MaxBytes) throw new UserException("Swagger 文件超过 50MB。");
                text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct);
                break;
            case "url":
                if (!Uri.TryCreate(source.Location, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new UserException("Swagger 地址必须是 http/https。");
                }
                using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    if ((int)response.StatusCode is >= 300 and < 400) throw new UserException("Swagger 地址发生跳转，请填写最终的 JSON 地址。");
                    if (!response.IsSuccessStatusCode) throw new UserException($"读取 Swagger 失败：HTTP {(int)response.StatusCode}");
                    if (response.Content.Headers.ContentLength > MaxBytes) throw new UserException("Swagger 内容超过 50MB。");
                    text = await response.Content.ReadAsStringAsync(ct);
                }
                break;
            default:
                throw new UserException("配置中没有 Swagger 来源。");
        }
        return (text, Parse(text, info), info);
    }

    public static JsonObject Parse(string text, OpenApiInfo info)
    {
        var trimmed = text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        if (trimmed.StartsWith('<'))
        {
            throw new UserException("读取到的是 HTML 页面（可能是 Swagger UI 地址），请改用 swagger.json 的 JSON 地址，例如 /swagger/v1/swagger.json。");
        }
        if (!trimmed.StartsWith('{')) throw new UserException("Swagger 内容不是 JSON（第一版不支持 YAML）。");
        JsonObject document;
        try
        {
            document = JsonNode.Parse(trimmed) as JsonObject ?? throw new UserException("Swagger 内容不是 JSON 对象。");
        }
        catch (JsonException ex)
        {
            throw new UserException("Swagger JSON 无法解析：" + ex.Message);
        }
        info.Hash = Canonical.Sha256Hex(trimmed);
        var openapi = document["openapi"]?.ToString();
        var swagger = document["swagger"]?.ToString();
        info.Version = openapi ?? (swagger is null ? "未知" : "swagger " + swagger);
        if (openapi is not null && openapi.StartsWith("3.0.", StringComparison.Ordinal))
        {
            info.Supported = true;
        }
        else
        {
            info.Supported = false;
            info.Problems.Add(swagger is not null ? "Swagger 2.0 第一版不支持同步。" : openapi?.StartsWith("3.1", StringComparison.Ordinal) == true ? "OpenAPI 3.1 第一版不支持同步。" : "无法识别的 OpenAPI 版本。");
        }
        var problems = new List<string>();
        var operations = OpenApiReader.Read(document, problems);
        info.Operations = operations.Count;
        info.Problems.AddRange(problems);
        if (document["paths"] is not JsonObject) info.Problems.Add("文档中没有 paths。");
        return document;
    }
}
