using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace ApifoxHelper.Infrastructure;

/// <summary>
/// 面向用户的业务错误；消息直接展示在页面上，因此不能包含凭据或原始命令输出。
/// </summary>
public sealed class UserException(string message, int statusCode = 400) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = Create(indented: true);
    public static readonly JsonSerializerOptions Compact = Create(indented: false);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            // 中文原样保存，便于人工查看任务资料。
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static JsonNode? ToNode<T>(T value) => JsonSerializer.SerializeToNode(value, Compact);

    public static T? FromNode<T>(JsonNode? node) => node is null ? default : node.Deserialize<T>(Compact);

    public static JsonNode? Clone(JsonNode? node) => node?.DeepClone();
}

/// <summary>
/// 规范化 JSON：对象键排序、数字统一表示，用于计算内容指纹和比较。
/// 数组保持原顺序；字符串 "1" 与数字 1 保持不同。
/// </summary>
public static class Canonical
{
    public static string Json(JsonNode? node)
    {
        var sb = new StringBuilder();
        Write(node, sb);
        return sb.ToString();
    }

    public static string Hash(JsonNode? node) => Sha256Hex(Json(node));

    public static string HashOf<T>(T value) => Hash(JsonDefaults.ToNode(value));

    public static string Sha256Hex(string text) => Sha256Hex(Encoding.UTF8.GetBytes(text));

    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static bool Equal(JsonNode? a, JsonNode? b) => Json(a) == Json(b);

    private static void Write(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                sb.Append('{');
                var first = true;
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(pair.Key));
                    sb.Append(':');
                    Write(pair.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray arr:
                sb.Append('[');
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Write(arr[i], sb);
                }
                sb.Append(']');
                break;
            case JsonValue value:
                sb.Append(ValueText(value));
                break;
        }
    }

    private static string ValueText(JsonValue value)
    {
        var raw = value.ToJsonString(JsonDefaults.Compact);
        if (raw.Length > 0 && (char.IsDigit(raw[0]) || raw[0] == '-'))
        {
            if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                // 1 与 1.0 在 JSON Schema 中是同一个数，统一去掉多余小数位。
                return d.ToString("0.############################", CultureInfo.InvariantCulture);
            }
        }
        return raw;
    }
}

public static partial class Ids
{
    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$")]
    private static partial Regex SafeId();

    public static string New(string prefix) =>
        $"{prefix}{DateTime.Now:yyyyMMddHHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";

    public static string Validate(string? id)
    {
        if (string.IsNullOrEmpty(id) || !SafeId().IsMatch(id))
        {
            throw new UserException("无效的标识。");
        }
        return id;
    }

    public static string ShortHash(string text, int length = 10) => Canonical.Sha256Hex(text)[..length];
}

/// <summary>
/// 进程内按键串行化，避免同一任务的资料被并发请求交叉改写。
/// </summary>
public static class KeyedLocks
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);

    public static async Task<IDisposable> AcquireAsync(string key, CancellationToken ct = default)
    {
        var semaphore = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(ct);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}

/// <summary>
/// JSON 节点的宽松数值读取：长整数、整数字符串与 JsonElement 数字都能读取；
/// 只依赖 BCL，接口模板提取等纯逻辑链路共享编译。
/// </summary>
public static class JsonNodeValue
{
    public static long? ReadLong(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var e)) return e;
        return null;
    }
}

public static class TextUtil
{
    public static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        return text.Length <= max ? text : text[..max] + "…";
    }

    public static string NormalizeSpace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return Regex.Replace(text.Trim(), "\\s+", " ");
    }

    public static string ToForwardSlash(string path) => path.Replace('\\', '/');
}
