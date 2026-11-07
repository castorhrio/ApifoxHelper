namespace ApifoxHelper.Contracts;

/// <summary>
/// 规范化后的接口文档：OpenAPI 与 Apifox 原生格式都转换成这一结构再比较，
/// 引用先解析为逻辑模型名并内联（递归处用标记截断），避免直接比较引用字符串产生假差异。
/// </summary>
public sealed class DocOperation
{
    public string Key { get; set; } = "";
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = new();
    public SortedDictionary<string, DocParameter> Parameters { get; set; } = new(StringComparer.Ordinal);
    public DocBody? RequestBody { get; set; }
    public SortedDictionary<string, DocResponse> Responses { get; set; } = new(StringComparer.Ordinal);
    public List<string> Unsupported { get; set; } = new();

    /// <summary>结构来源：swagger / remote / responseContract 等，供页面展示。</summary>
    public List<string> Sources { get; set; } = new();
}

public sealed class DocParameter
{
    public string In { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Required { get; set; }
    public JsonObject Schema { get; set; } = new();
    public string? Description { get; set; }
    public JsonNode? Example { get; set; }
    public string Key => In + ":" + Name;
}

public sealed class DocBody
{
    public bool Required { get; set; }
    public string MediaType { get; set; } = "";
    public JsonObject Schema { get; set; } = new();
    public string? Description { get; set; }
}

public sealed class DocResponse
{
    public string Code { get; set; } = "";
    public string? Description { get; set; }
    public string? MediaType { get; set; }
    public JsonObject? Schema { get; set; }
}

public delegate (string Name, JsonNode? Schema)? RefResolver(string reference);

public static class SchemaNormalizer
{
    private static readonly string[] StringKeys = ["format", "pattern"];
    private static readonly string[] NumberKeys = ["minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "multipleOf", "minProperties", "maxProperties"];
    private static readonly string[] UnsupportedKeys = ["not", "discriminator", "patternProperties", "if", "then", "else", "dependentRequired", "dependentSchemas", "unevaluatedProperties", "prefixItems", "contains", "propertyNames"];

    public static JsonObject Normalize(JsonNode? schema, RefResolver resolve) => Norm(schema, resolve, new List<string>(), 0);

    /// <summary>
    /// 浅规范化：引用不展开，只保留逻辑模型名。用于判断某个模型自身是否变化（嵌套模型的变化归属嵌套模型）。
    /// </summary>
    public static JsonObject NormalizeShallow(JsonNode? schema, Func<string, string?> refName)
    {
        RefResolver resolver = reference => refName(reference) is { } name ? (name, (JsonNode?)new JsonObject { ["x-opaque"] = true }) : null;
        return Norm(schema, resolver, new List<string>(), 0);
    }

    private static JsonObject Norm(JsonNode? node, RefResolver resolve, List<string> stack, int depth)
    {
        var result = new JsonObject();
        if (node is JsonValue boolean && boolean.TryGetValue<bool>(out var b))
        {
            if (!b) result["x-unsupported"] = new JsonArray("false 模式");
            return result;
        }
        if (node is not JsonObject s) return result;
        if (depth > 40)
        {
            result["x-unsupported"] = new JsonArray("结构嵌套过深");
            return result;
        }

        if (s["$ref"] is JsonValue refValue && refValue.TryGetValue<string>(out var reference))
        {
            var resolved = resolve(reference);
            if (resolved is null)
            {
                result["x-unsupported"] = new JsonArray("不支持的引用 " + reference);
                return result;
            }
            var (name, target) = resolved.Value;
            if (target is JsonObject opaque && opaque.ContainsKey("x-opaque"))
            {
                result["x-opaque-ref"] = name;
                CopyDocs(s, result);
                return result;
            }
            if (stack.Contains(name, StringComparer.Ordinal))
            {
                result["x-ref"] = name;
                result["x-recursion"] = true;
                CopyDocs(s, result);
                return result;
            }
            if (target is null)
            {
                result["x-ref"] = name;
                result["x-dangling"] = true;
                return result;
            }
            stack.Add(name);
            var inner = Norm(target, resolve, stack, depth + 1);
            stack.RemoveAt(stack.Count - 1);
            inner["x-ref"] = name;
            if (Text(s["description"]) is { } siblingDescription) inner["description"] = siblingDescription;
            return inner;
        }

        var types = new SortedSet<string>(StringComparer.Ordinal);
        var nullable = false;
        switch (s["type"])
        {
            case JsonValue typeValue when typeValue.TryGetValue<string>(out var t):
                if (t == "null") nullable = true;
                else types.Add(t);
                break;
            case JsonArray typeArray:
                foreach (var t in typeArray.Select(x => x?.ToString()).OfType<string>())
                {
                    if (t == "null") nullable = true;
                    else types.Add(t);
                }
                break;
        }
        if (s["nullable"] is JsonValue nullableValue && nullableValue.TryGetValue<bool>(out var nb) && nb) nullable = true;
        if (types.Count == 0)
        {
            if (s["properties"] is not null || s["additionalProperties"] is JsonObject) types.Add("object");
            else if (s["items"] is not null) types.Add("array");
        }
        if (types.Count > 0) result["type"] = new JsonArray(types.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
        if (nullable) result["nullable"] = true;
        foreach (var key in StringKeys)
        {
            if (Text(s[key]) is { } value) result[key] = value;
        }
        foreach (var key in NumberKeys)
        {
            if (s[key] is JsonValue number) result[key] = number.DeepClone();
        }
        foreach (var (key, bound) in new[] { ("exclusiveMinimum", "minimum"), ("exclusiveMaximum", "maximum") })
        {
            switch (s[key])
            {
                case JsonValue flag when flag.TryGetValue<bool>(out var exclusive):
                    if (exclusive) result[key] = true;
                    break;
                case JsonValue number:
                    // JSON Schema 2019+ 的数值写法与 OpenAPI 3.0 的布尔写法统一。
                    result[bound] = number.DeepClone();
                    result[key] = true;
                    break;
            }
        }
        foreach (var key in new[] { "uniqueItems", "readOnly", "writeOnly" })
        {
            if (s[key] is JsonValue flag && flag.TryGetValue<bool>(out var on) && on) result[key] = true;
        }
        if (s["enum"] is JsonArray enumValues) result["enum"] = enumValues.DeepClone();
        else if (s.ContainsKey("const")) result["enum"] = new JsonArray(s["const"]?.DeepClone());
        if (s.ContainsKey("default")) result["default"] = s["default"]?.DeepClone();
        if (s["required"] is JsonArray required)
        {
            var names = required.Select(r => r?.ToString()).OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal);
            result["required"] = new JsonArray(names.Select(n => (JsonNode?)JsonValue.Create(n)).ToArray());
        }
        if (s["properties"] is JsonObject properties)
        {
            var normalized = new JsonObject();
            foreach (var (name, value) in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                normalized[name] = Norm(value, resolve, stack, depth + 1);
            }
            result["properties"] = normalized;
        }
        switch (s["items"])
        {
            case JsonArray:
                AddUnsupported(result, "元组形式的 items");
                break;
            case JsonNode items:
                result["items"] = Norm(items, resolve, stack, depth + 1);
                break;
        }
        switch (s["additionalProperties"])
        {
            case JsonValue flag when flag.TryGetValue<bool>(out var allowed):
                result["additionalProperties"] = allowed;
                break;
            case JsonObject additional:
                result["additionalProperties"] = Norm(additional, resolve, stack, depth + 1);
                break;
        }
        foreach (var key in new[] { "allOf", "oneOf", "anyOf" })
        {
            if (s[key] is JsonArray members)
            {
                result[key] = new JsonArray(members.Select(m => (JsonNode?)Norm(m, resolve, stack, depth + 1)).ToArray());
            }
        }
        foreach (var key in UnsupportedKeys)
        {
            if (s.ContainsKey(key)) AddUnsupported(result, key);
        }
        CopyDocs(s, result);
        MergeAllOf(result);
        return result;
    }

    private static void CopyDocs(JsonObject source, JsonObject target)
    {
        if (Text(source["description"]) is { } description) target["description"] = description;
        if (Text(source["title"]) is { } title) target["title"] = title;
        if (source.ContainsKey("example")) target["example"] = source["example"]?.DeepClone();
        else if (source["examples"] is JsonArray examples && examples.Count > 0) target["example"] = examples[0]?.DeepClone();
        if (source["deprecated"] is JsonValue deprecated && deprecated.TryGetValue<bool>(out var d) && d) target["deprecated"] = true;
    }

    public static string? Text(JsonNode? node)
    {
        if (node is not JsonValue value || !value.TryGetValue<string>(out var text)) return null;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AddUnsupported(JsonObject node, string message)
    {
        if (node["x-unsupported"] is not JsonArray list)
        {
            list = new JsonArray();
            node["x-unsupported"] = list;
        }
        list.Add(message);
    }

    /// <summary>
    /// 成员都是对象结构的 allOf 合并为单个对象（Apifox 的展示方式也是合并字段）；
    /// 成员属性冲突或含 oneOf/anyOf 时保留组合，交给人工判断。
    /// </summary>
    private static void MergeAllOf(JsonObject node)
    {
        if (node["allOf"] is not JsonArray members || node.ContainsKey("oneOf") || node.ContainsKey("anyOf")) return;
        var objects = members.OfType<JsonObject>().ToList();
        if (objects.Count != members.Count) return;

        // 单成员 allOf（Swashbuckle 用来给引用加说明或可空）等价于成员本身；
        // 引用模型的可空性在 Apifox 中无法单独表达，两侧都不参与比较。
        if (objects.Count == 1 && !node.ContainsKey("properties") && !node.ContainsKey("type"))
        {
            var member = objects[0];
            var outerDocs = new JsonObject();
            CopyDocs(node, outerDocs);
            node.Clear();
            foreach (var (key, value) in member) node[key] = value?.DeepClone();
            if (member.ContainsKey("x-ref")) node.Remove("nullable");
            foreach (var (key, value) in outerDocs) node[key] = value?.DeepClone();
            return;
        }
        foreach (var member in objects)
        {
            var type = member["type"] as JsonArray;
            var objectLike = type is null ? member.ContainsKey("properties") || member.ContainsKey("x-ref") || member.Count == 0 || member.All(p => p.Key.StartsWith("x-", StringComparison.Ordinal) || p.Key is "description" or "title") : type.Count == 1 && type[0]?.ToString() == "object";
            if (!objectLike || member.ContainsKey("allOf") || member.ContainsKey("oneOf") || member.ContainsKey("anyOf") || member.ContainsKey("x-recursion")) return;
        }
        var properties = new SortedDictionary<string, JsonNode?>(StringComparer.Ordinal);
        var required = new SortedSet<string>(StringComparer.Ordinal);
        var composed = new JsonArray();
        string? description = null;
        foreach (var member in objects)
        {
            if (member["properties"] is JsonObject memberProperties)
            {
                foreach (var (name, value) in memberProperties)
                {
                    if (properties.TryGetValue(name, out var existing) && Canonical.Json(StructureOnly(existing)) != Canonical.Json(StructureOnly(value)))
                    {
                        AddUnsupported(node, $"allOf 成员的属性 {name} 定义冲突，需人工判断");
                        return;
                    }
                    properties[name] = value?.DeepClone();
                }
            }
            if (member["required"] is JsonArray memberRequired)
            {
                foreach (var r in memberRequired.Select(x => x?.ToString()).OfType<string>()) required.Add(r);
            }
            if (member["x-ref"] is JsonNode refName) composed.Add(refName.ToString());
            description ??= Text(member["description"]);
            if (member["x-unsupported"] is JsonArray notes)
            {
                foreach (var note in notes) AddUnsupported(node, note?.ToString() ?? "");
            }
        }
        node.Remove("allOf");
        node["type"] = new JsonArray("object");
        var merged = new JsonObject();
        foreach (var (name, value) in properties) merged[name] = value;
        node["properties"] = merged;
        if (required.Count > 0) node["required"] = new JsonArray(required.Select(r => (JsonNode?)JsonValue.Create(r)).ToArray());
        if (composed.Count > 0) node["x-composed"] = composed;
        if (!node.ContainsKey("description") && description is not null) node["description"] = description;
    }

    /// <summary>去掉说明类字段与 x-* 标记后的结构，用于结构比较。</summary>
    public static JsonNode? StructureOnly(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var copy = new JsonObject();
                foreach (var (key, value) in obj)
                {
                    if (key is "description" or "title" or "example" or "deprecated" || key.StartsWith("x-", StringComparison.Ordinal) && key is not ("x-recursion" or "x-unsupported" or "x-opaque-ref")) continue;
                    copy[key] = StructureOnly(value);
                }
                if (obj["x-recursion"] is not null) copy["x-ref"] = obj["x-ref"]?.DeepClone();
                return copy;
            case JsonArray arr:
                return new JsonArray(arr.Select(StructureOnly).ToArray());
            default:
                return node?.DeepClone();
        }
    }

    public static bool HasUnsupported(JsonNode? node, List<string> notes, string path = "")
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["x-unsupported"] is JsonArray list) notes.AddRange(list.Select(x => (path.Length == 0 ? "" : path + "：") + x));
                if (obj["x-dangling"] is not null) notes.Add((path.Length == 0 ? "" : path + "：") + "悬空引用 " + obj["x-ref"]);
                foreach (var (key, value) in obj)
                {
                    if (key is "properties" && value is JsonObject props)
                    {
                        foreach (var (name, child) in props) HasUnsupported(child, notes, path.Length == 0 ? name : path + "." + name);
                    }
                    else if (key is "items") HasUnsupported(value, notes, path + "[]");
                    else if (key is "additionalProperties" or "allOf" or "oneOf" or "anyOf") HasUnsupported(value, notes, path);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr) HasUnsupported(item, notes, path);
                break;
        }
        return notes.Count > 0;
    }
}

public static class OpenApiReader
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    public static Dictionary<string, DocOperation> Read(JsonObject document, List<string> problems)
    {
        var schemas = document["components"]?["schemas"] as JsonObject;
        RefResolver resolver = reference =>
        {
            const string prefix = "#/components/schemas/";
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var name = Unescape(reference[prefix.Length..]);
            return (name, schemas?[name]);
        };
        var result = new Dictionary<string, DocOperation>(StringComparer.Ordinal);
        if (document["paths"] is not JsonObject paths) return result;
        foreach (var (path, pathNode) in paths)
        {
            if (pathNode is not JsonObject pathItem) continue;
            var pathParameters = ResolveList(pathItem["parameters"] as JsonArray, document, "#/components/parameters/");
            foreach (var method in Methods)
            {
                if (pathItem[method] is not JsonObject operation) continue;
                var op = new DocOperation
                {
                    Method = method.ToUpperInvariant(),
                    Path = path,
                    Key = method.ToUpperInvariant() + " " + path,
                    Summary = SchemaNormalizer.Text(operation["summary"]),
                    Description = SchemaNormalizer.Text(operation["description"]),
                    Tags = (operation["tags"] as JsonArray)?.Select(t => t?.ToString()).OfType<string>().ToList() ?? new List<string>(),
                    Sources = ["swagger"],
                };
                var parameters = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                foreach (var p in pathParameters.Concat(ResolveList(operation["parameters"] as JsonArray, document, "#/components/parameters/")))
                {
                    parameters[(p["in"]?.ToString() ?? "") + ":" + (p["name"]?.ToString() ?? "")] = p;
                }
                foreach (var p in parameters.Values)
                {
                    var parameter = new DocParameter
                    {
                        In = p["in"]?.ToString() ?? "",
                        Name = p["name"]?.ToString() ?? "",
                        Required = p["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req,
                        Schema = SchemaNormalizer.Normalize(p["schema"], resolver),
                        Description = SchemaNormalizer.Text(p["description"]),
                        Example = p["example"]?.DeepClone(),
                    };
                    if (p["content"] is not null) op.Unsupported.Add($"参数 {parameter.Name} 使用 content 描述，第一版不支持");
                    op.Parameters[parameter.Key] = parameter;
                }
                if (Resolve(operation["requestBody"], document, "#/components/requestBodies/") is JsonObject body)
                {
                    var (mediaType, media) = PickMedia(body["content"] as JsonObject);
                    op.RequestBody = new DocBody
                    {
                        Required = body["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req,
                        MediaType = mediaType ?? "",
                        Schema = SchemaNormalizer.Normalize(media?["schema"], resolver),
                        Description = SchemaNormalizer.Text(body["description"]),
                    };
                }
                if (operation["responses"] is JsonObject responses)
                {
                    foreach (var (code, responseNode) in responses)
                    {
                        if (Resolve(responseNode, document, "#/components/responses/") is not JsonObject response) continue;
                        var (mediaType, media) = PickMedia(response["content"] as JsonObject);
                        op.Responses[code] = new DocResponse
                        {
                            Code = code,
                            Description = SchemaNormalizer.Text(response["description"]),
                            MediaType = mediaType,
                            Schema = media is null ? null : SchemaNormalizer.Normalize(media["schema"], resolver),
                        };
                    }
                }
                CollectUnsupported(op);
                result[op.Key] = op;
            }
        }
        return result;
    }

    public static void CollectUnsupported(DocOperation op)
    {
        var notes = new List<string>();
        foreach (var p in op.Parameters.Values) SchemaNormalizer.HasUnsupported(p.Schema, notes, p.Key);
        if (op.RequestBody is not null) SchemaNormalizer.HasUnsupported(op.RequestBody.Schema, notes, "requestBody");
        foreach (var r in op.Responses.Values) SchemaNormalizer.HasUnsupported(r.Schema, notes, "responses." + r.Code);
        op.Unsupported.AddRange(notes.Distinct());
    }

    private static List<JsonObject> ResolveList(JsonArray? list, JsonObject document, string prefix) =>
        list?.Select(item => Resolve(item, document, prefix)).OfType<JsonObject>().ToList() ?? new List<JsonObject>();

    private static JsonNode? Resolve(JsonNode? node, JsonObject document, string prefix)
    {
        if (node is JsonObject obj && obj["$ref"] is JsonValue r && r.TryGetValue<string>(out var reference))
        {
            if (!reference.StartsWith(prefix, StringComparison.Ordinal)) return null;
            var name = Unescape(reference[prefix.Length..]);
            var section = prefix.Split('/')[2];
            return document["components"]?[section]?[name];
        }
        return node;
    }

    public static (string? MediaType, JsonObject? Media) PickMedia(JsonObject? content)
    {
        if (content is null || content.Count == 0) return (null, null);
        string[] preferred = ["application/json", "application/*+json", "text/json"];
        foreach (var p in preferred)
        {
            if (content[p] is JsonObject media) return (p, media);
        }
        var json = content.FirstOrDefault(c => c.Key.Contains("json", StringComparison.OrdinalIgnoreCase));
        if (json.Value is JsonObject jsonMedia) return (json.Key, jsonMedia);
        var first = content.First();
        return (first.Key, first.Value as JsonObject);
    }

    private static string Unescape(string pointer) => Uri.UnescapeDataString(pointer).Replace("~1", "/").Replace("~0", "~");
}

/// <summary>Apifox 接口详情（endpoint get 的 data）→ 规范化文档。</summary>
public static partial class ApifoxReader
{
    [GeneratedRegex(@"^#/definitions/(\d+)$")]
    private static partial Regex DefinitionRef();

    public static RefResolver Resolver(Func<long, (string Name, JsonNode? Schema)?> lookup) => reference =>
    {
        var m = DefinitionRef().Match(reference);
        if (!m.Success) return null;
        var id = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return lookup(id) ?? ("#" + id, null);
    };

    public static DocOperation Read(JsonObject data, RefResolver resolver)
    {
        var method = (data["method"]?.ToString() ?? "").ToUpperInvariant();
        var path = data["path"]?.ToString() ?? "";
        var op = new DocOperation
        {
            Method = method,
            Path = path,
            Key = method + " " + path,
            Summary = SchemaNormalizer.Text(data["name"]),
            Description = SchemaNormalizer.Text(data["description"]),
            Tags = (data["tags"] as JsonArray)?.Select(t => t?.ToString()).OfType<string>().ToList() ?? new List<string>(),
            Sources = ["remote"],
        };
        if (data["parameters"] is JsonObject parameters)
        {
            foreach (var location in new[] { "path", "query", "header", "cookie" })
            {
                foreach (var item in (parameters[location] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    var parameter = ReadParameter(item, location, resolver);
                    op.Parameters[parameter.Key] = parameter;
                }
            }
        }
        if (data["requestBody"] is JsonObject body)
        {
            var type = body["type"]?.ToString() ?? "none";
            if (type != "none")
            {
                var docBody = new DocBody
                {
                    MediaType = type,
                    Required = body["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req,
                    Description = SchemaNormalizer.Text(body["description"]),
                };
                if (type is "multipart/form-data" or "application/x-www-form-urlencoded")
                {
                    // 表单字段转换成对象结构，便于与 OpenAPI 的表单 schema 比较。
                    var properties = new JsonObject();
                    var required = new JsonArray();
                    foreach (var field in (body["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    {
                        var p = ReadParameter(field, "form", resolver);
                        var schema = (JsonObject)p.Schema.DeepClone();
                        if (p.Description is not null) schema["description"] = p.Description;
                        properties[p.Name] = schema;
                        if (p.Required) required.Add(p.Name);
                    }
                    var raw = new JsonObject { ["type"] = "object", ["properties"] = properties };
                    if (required.Count > 0) raw["required"] = required;
                    docBody.Schema = SchemaNormalizer.Normalize(raw, resolver);
                }
                else
                {
                    docBody.Schema = SchemaNormalizer.Normalize(body["jsonSchema"], resolver);
                }
                op.RequestBody = docBody;
            }
        }
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var response in (data["responses"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var code = response["code"]?.ToString() ?? "";
            seen[code] = seen.GetValueOrDefault(code) + 1;
            if (seen[code] > 1)
            {
                op.Unsupported.Add($"响应码 {code} 在 Apifox 中有多条定义，需人工处理");
                continue;
            }
            var contentType = response["contentType"]?.ToString();
            op.Responses[code] = new DocResponse
            {
                Code = code,
                Description = SchemaNormalizer.Text(response["description"]) ?? SchemaNormalizer.Text(response["name"]),
                MediaType = response["mediaType"]?.ToString() ?? (contentType == "json" ? "application/json" : contentType),
                Schema = contentType is "json" or "xml" or null ? SchemaNormalizer.Normalize(response["jsonSchema"], resolver) : null,
            };
        }
        OpenApiReader.CollectUnsupported(op);
        return op;
    }

    private static DocParameter ReadParameter(JsonObject item, string location, RefResolver resolver)
    {
        JsonNode? rawSchema = item["schema"] as JsonObject;
        var type = item["type"]?.ToString();
        if (rawSchema is null || ((JsonObject)rawSchema).Count == 0)
        {
            rawSchema = type switch
            {
                "file" => new JsonObject { ["type"] = "string", ["format"] = "binary" },
                null or "" or "text" => new JsonObject { ["type"] = "string" },
                _ => new JsonObject { ["type"] = type },
            };
        }
        return new DocParameter
        {
            In = location,
            Name = item["name"]?.ToString() ?? "",
            Required = item["required"] is JsonValue r && r.TryGetValue<bool>(out var req) && req,
            Schema = SchemaNormalizer.Normalize(rawSchema, resolver),
            Description = SchemaNormalizer.Text(item["description"]),
            Example = item["example"]?.DeepClone(),
        };
    }
}
