namespace ApifoxHelper.Infrastructure;

/// <summary>
/// 共享报告、错误信息和发送给模型的材料统一经过脱敏。只做保守替换，宁可多遮挡。
/// </summary>
public static partial class Redactor
{
    [GeneratedRegex(@"(?i)\bBearer\s+[A-Za-z0-9\-._~+/]{8,}=*")]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"(?i)(""?(?:authorization|access[_-]?token|refresh[_-]?token|api[_-]?key|apikey|secret|client[_-]?secret|password|passwd|pwd|token|cookie)""?\s*[:=]\s*""?)([^\s"",;&]{4,})")]
    private static partial Regex KeyValuePattern();

    [GeneratedRegex(@"(?i)\b(?:server|data source|host)\s*=[^;""]+;[^""\r\n]*?(?:password|pwd)\s*=[^;""\r\n]+")]
    private static partial Regex ConnectionStringPattern();

    [GeneratedRegex(@"\b(?:sk|pk|rk)-[A-Za-z0-9_\-]{16,}\b")]
    private static partial Regex ProviderKeyPattern();

    [GeneratedRegex(@"\bAPS-[A-Za-z0-9_\-]{16,}\b")]
    private static partial Regex ApifoxTokenPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = ConnectionStringPattern().Replace(text, "<连接串已脱敏>");
        result = BearerPattern().Replace(result, "Bearer <已脱敏>");
        result = ProviderKeyPattern().Replace(result, "<已脱敏>");
        result = ApifoxTokenPattern().Replace(result, "<已脱敏>");
        result = KeyValuePattern().Replace(result, m =>
        {
            var value = m.Groups[2].Value;
            // 模板变量（如 {{access_token}}）不是凭据，保留可读性。
            if (value.StartsWith("{{", StringComparison.Ordinal) || value.StartsWith("<", StringComparison.Ordinal)) return m.Value;
            return m.Groups[1].Value + "<已脱敏>";
        });
        return result;
    }

    public static JsonNode? RedactNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var pair in obj)
                {
                    copy[pair.Key] = IsSensitiveKey(pair.Key) && pair.Value is JsonValue ? JsonValue.Create("<已脱敏>") : RedactNode(pair.Value);
                }
                return copy;
            case JsonArray arr:
                var list = new JsonArray();
                foreach (var item in arr) list.Add(RedactNode(item));
                return list;
            case JsonValue value when value.TryGetValue<string>(out var s):
                return JsonValue.Create(Redact(s));
            default:
                return node.DeepClone();
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var k = key.ToLowerInvariant();
        return k is "token" or "accesstoken" or "access_token" or "apikey" or "api_key" or "password" or "secret" or "cookie" or "authorization"
            || k.EndsWith("password", StringComparison.Ordinal) || k.EndsWith("secret", StringComparison.Ordinal);
    }

    /// <summary>
    /// 发往外部模型前排除的文件：配置、凭据和连接串常见位置。
    /// </summary>
    public static bool IsExcludedMaterialFile(string relativePath)
    {
        var name = Path.GetFileName(relativePath).ToLowerInvariant();
        return name.StartsWith("appsettings", StringComparison.Ordinal)
            || name.EndsWith(".config", StringComparison.Ordinal)
            || name.EndsWith(".pfx", StringComparison.Ordinal)
            || name.EndsWith(".pem", StringComparison.Ordinal)
            || name.Contains("secret", StringComparison.Ordinal)
            || name.Contains("credential", StringComparison.Ordinal)
            || name == ".env";
    }
}
