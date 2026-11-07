namespace ApifoxHelper.Apifox;

/// <summary>
/// Apifox 原生格式辅助：指纹计算、受保护字段投影，以及 OpenAPI 3.0 结构到 Apifox JSON Schema 的转换。
/// </summary>
public static partial class NativeUtil
{
    /// <summary>每次写入都会由平台刷新的字段，不参与“受保护内容是否变化”的比较。</summary>
    public static readonly HashSet<string> VolatileKeys = new(StringComparer.Ordinal) { "updatedAt", "editorId", "editorUserId", "updatedBy", "updatedById" };

    [GeneratedRegex(@"^#/components/schemas/(.+)$")]
    private static partial Regex ComponentRef();

    public static string Hash(JsonObject data) => Canonical.Hash(WithoutKeys(data, VolatileKeys));

    public static JsonObject WithoutKeys(JsonObject data, IEnumerable<string> keys)
    {
        var copy = (JsonObject)data.DeepClone();
        foreach (var key in keys) copy.Remove(key);
        return copy;
    }

    /// <summary>除本次写入的顶层字段与易变字段外的全部内容，写入前后必须完全一致。</summary>
    public static string ProtectedHash(JsonObject data, IEnumerable<string> managedKeys) =>
        Canonical.Hash(WithoutKeys(data, VolatileKeys.Concat(managedKeys)));

    public static string? ComponentName(string reference)
    {
        var m = ComponentRef().Match(reference);
        return m.Success ? Uri.UnescapeDataString(m.Groups[1].Value).Replace("~1", "/").Replace("~0", "~") : null;
    }

    /// <summary>
    /// OpenAPI 3.0 schema → Apifox JSON Schema：组件引用换成模型 ID（或待创建模型的占位），nullable 换成 type 数组，
    /// example 换成 examples；对象补 x-apifox-orders 保持字段顺序。无法表达的内容记入 notes。
    /// </summary>
    public static JsonNode? ToApifox(JsonNode? node, Func<string, string?> refFor, List<string> notes)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"] is JsonValue r && r.TryGetValue<string>(out var reference))
                {
                    var name = ComponentName(reference);
                    var target = name is null ? null : refFor(name);
                    if (target is null)
                    {
                        notes.Add("无法转换的引用 " + reference);
                        return new JsonObject();
                    }
                    var refObject = new JsonObject { ["$ref"] = target };
                    return refObject;
                }
                var result = new JsonObject();
                var nullable = obj["nullable"] is JsonValue n && n.TryGetValue<bool>(out var isNullable) && isNullable;
                foreach (var (key, value) in obj)
                {
                    switch (key)
                    {
                        case "nullable":
                            break;
                        case "type":
                            var type = value?.ToString();
                            result["type"] = nullable && type is not null ? new JsonArray(type, "null") : value?.DeepClone();
                            break;
                        case "example":
                            result["examples"] = new JsonArray(value?.DeepClone());
                            break;
                        case "properties" when value is JsonObject properties:
                            var converted = new JsonObject();
                            foreach (var (propertyName, propertySchema) in properties)
                            {
                                converted[propertyName] = ToApifox(propertySchema, refFor, notes);
                            }
                            result["properties"] = converted;
                            result["x-apifox-orders"] = new JsonArray(properties.Select(p => (JsonNode?)JsonValue.Create(p.Key)).ToArray());
                            break;
                        case "items" or "additionalProperties" when value is JsonObject:
                            result[key] = ToApifox(value, refFor, notes);
                            break;
                        case "allOf" or "oneOf" or "anyOf" when value is JsonArray members:
                            result[key] = new JsonArray(members.Select(m => ToApifox(m, refFor, notes)).ToArray());
                            break;
                        default:
                            if (key.StartsWith("x-", StringComparison.Ordinal)) break;
                            result[key] = value?.DeepClone();
                            break;
                    }
                }
                if (nullable && !obj.ContainsKey("type") && obj["allOf"] is not null)
                {
                    notes.Add("引用模型的可空标记在 Apifox 中无法表达，已忽略。");
                }
                return result;
            case JsonArray arr:
                return new JsonArray(arr.Select(x => ToApifox(x, refFor, notes)).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    /// <summary>
    /// 在远端已有 JSON Schema 的基础上套用新结构：保留远端的 x-apifox-* 扩展、examples、mock 等未管理字段，
    /// 字段顺序沿用远端顺序、新字段追加在后；新结构未给出说明时保留远端说明。
    /// </summary>
    public static JsonNode? MergeOnto(JsonNode? existing, JsonNode? desired)
    {
        if (existing is not JsonObject old || desired is not JsonObject target) return desired?.DeepClone();
        if (target.ContainsKey("$ref") || old.ContainsKey("$ref")) return PreserveDescription(old, (JsonObject)target.DeepClone());
        var result = (JsonObject)target.DeepClone();
        foreach (var (key, value) in old)
        {
            if (result.ContainsKey(key) || key is "properties" or "required" or "items" or "additionalProperties" or "allOf" or "oneOf" or "anyOf" or "type"
                or "format" or "enum" or "default" or "minimum" or "maximum" or "minLength" or "maxLength" or "pattern" or "minItems" or "maxItems"
                or "exclusiveMinimum" or "exclusiveMaximum" or "uniqueItems" or "multipleOf" or "x-apifox-orders")
            {
                continue;
            }
            result[key] = value?.DeepClone();
        }
        if (!result.ContainsKey("description") && old["description"] is not null) result["description"] = old["description"]!.DeepClone();
        if (result["properties"] is JsonObject newProps && old["properties"] is JsonObject oldProps)
        {
            foreach (var (name, value) in newProps.ToList())
            {
                if (oldProps[name] is JsonNode oldValue) newProps[name] = MergeOnto(oldValue, value);
            }
            if (old["x-apifox-orders"] is JsonArray oldOrders)
            {
                var order = oldOrders.Select(o => o?.ToString()).OfType<string>().Where(newProps.ContainsKey).ToList();
                order.AddRange(newProps.Select(p => p.Key).Where(k => !order.Contains(k)));
                result["x-apifox-orders"] = new JsonArray(order.Select(o => (JsonNode?)JsonValue.Create(o)).ToArray());
            }
        }
        if (result["items"] is JsonNode items && old["items"] is JsonNode oldItems) result["items"] = MergeOnto(oldItems, items);
        return result;
    }

    private static JsonObject PreserveDescription(JsonObject old, JsonObject target)
    {
        if (!target.ContainsKey("description") && old["description"] is not null && target.ContainsKey("allOf")) target["description"] = old["description"]!.DeepClone();
        return target;
    }
}
