using System.Net;
using System.Net.Http.Headers;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Comments;

/// <summary>
/// OpenAI 兼容的对话补全接口客户端。只发送用户确认过范围的材料；密钥只放在请求头，不写日志。
/// 模型只返回候选文字与证据编号，不执行命令、不改文件、不调用 Apifox。
/// </summary>
public sealed class ModelClient
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    public async Task<string> CompleteAsync(GenerationSettings settings, string apiKey, string system, string user, CancellationToken ct)
    {
        var url = settings.BaseUrl!.TrimEnd('/') + "/chat/completions";
        var withFormat = await SendAsync(url, settings, apiKey, system, user, useJsonFormat: true, ct);
        if (withFormat.Status == HttpStatusCode.BadRequest && withFormat.Body.Contains("response_format", StringComparison.OrdinalIgnoreCase))
        {
            // 部分兼容服务不支持 response_format，去掉后重试一次。
            withFormat = await SendAsync(url, settings, apiKey, system, user, useJsonFormat: false, ct);
        }
        if ((int)withFormat.Status < 200 || (int)withFormat.Status >= 300)
        {
            throw new UserException($"模型服务返回 {(int)withFormat.Status}：{Redactor.Redact(TextUtil.Truncate(withFormat.Body, 300))}");
        }
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(withFormat.Body);
        }
        catch (JsonException)
        {
            throw new UserException("模型服务返回的不是 JSON。");
        }
        var content = node?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content)) throw new UserException("模型服务没有返回内容。");
        return content;
    }

    private static async Task<(HttpStatusCode Status, string Body)> SendAsync(string url, GenerationSettings settings, string apiKey, string system, string user, bool useJsonFormat, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["temperature"] = 0.1,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user }),
        };
        if (useJsonFormat) body["response_format"] = new JsonObject { ["type"] = "json_object" };
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(JsonDefaults.Compact), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        try
        {
            using var response = await Http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            return (response.StatusCode, text);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UserException($"模型服务在 {settings.TimeoutSeconds} 秒内没有响应。");
        }
        catch (HttpRequestException ex)
        {
            throw new UserException("无法连接模型服务：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>从模型输出中解析候选 JSON；容忍 ```json 包裹，其余格式一律视为失败。</summary>
    public static ExternalResult ParseResult(string content)
    {
        var text = content.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine > 0 && lastFence > firstNewLine) text = text[(firstNewLine + 1)..lastFence].Trim();
        }
        try
        {
            return JsonSerializer.Deserialize<ExternalResult>(text, JsonDefaults.Compact) ?? new ExternalResult();
        }
        catch (JsonException ex)
        {
            throw new UserException("候选结果不是约定的 JSON 格式：" + ex.Message);
        }
    }
}
