namespace ApifoxHelper.Contracts;

public sealed class DiffItem
{
    public string Id { get; set; } = "";
    public string ResourceKey { get; set; } = "";
    public string Location { get; set; } = "";

    /// <summary>structure / documentation / identity / quality</summary>
    public string Category { get; set; } = "";

    /// <summary>added / removed / changed / info</summary>
    public string Change { get; set; } = "";
    public string Field { get; set; } = "";
    public JsonNode? Before { get; set; }
    public JsonNode? After { get; set; }
    public string Source { get; set; } = "";

    /// <summary>normal / incompatible / manual / blocking</summary>
    public string Risk { get; set; } = "normal";
    public string Reason { get; set; } = "";
    public bool Blocking { get; set; }

    /// <summary>需要逐项确认（潜在不兼容、需人工判断、说明冲突）。</summary>
    public bool NeedsDecision { get; set; }
}

/// <summary>
/// 结构比对：before 为 Apifox 当前内容，after 为本次目标。风险判断区分请求与响应方向，
/// 例如请求枚举减少更严格、响应枚举新增可能影响前端穷举处理。
/// </summary>
public sealed class DiffEngine
{
    private readonly List<DiffItem> _items = new();
    private string _resource = "";
    private string _source = "";

    public List<DiffItem> Items => _items;

    public void CompareOperation(DocOperation? remote, DocOperation target, string source)
    {
        _resource = target.Key;
        _source = source;
        if (remote is null)
        {
            Add("operation", "identity", "added", "operation", null, JsonValue.Create(target.Key), "normal", "Apifox 中不存在该接口，将新建");
            return;
        }

        foreach (var (key, parameter) in target.Parameters)
        {
            if (!remote.Parameters.TryGetValue(key, out var existing))
            {
                Add(Loc(parameter), "structure", "added", "parameter", null, Describe(parameter),
                    parameter.Required ? "incompatible" : "normal", parameter.Required ? "新增必填请求参数" : "新增可选请求参数");
                continue;
            }
            if (existing.Required != parameter.Required)
            {
                Add(Loc(parameter), "structure", "changed", "required", existing.Required, parameter.Required,
                    parameter.Required ? "incompatible" : "normal", parameter.Required ? "参数改为必填" : "参数改为可选");
            }
            CompareSchema(existing.Schema, parameter.Schema, Loc(parameter), request: true);
        }
        foreach (var (key, parameter) in remote.Parameters)
        {
            if (target.Parameters.ContainsKey(key)) continue;
            if (parameter.In is "header" or "cookie")
            {
                // Swagger 通常不声明鉴权头、公共头；远端独有的头/Cookie 参数按受保护设置保留。
                Add(Loc(parameter), "structure", "info", "parameter", Describe(parameter), null, "normal", "远端独有的头/Cookie 参数，将保留");
                continue;
            }
            Add(Loc(parameter), "structure", "removed", "parameter", Describe(parameter), null, "incompatible", "源中已没有该请求参数");
        }

        CompareBody(remote.RequestBody, target.RequestBody);

        foreach (var (code, response) in target.Responses)
        {
            if (!remote.Responses.TryGetValue(code, out var existing))
            {
                Add("responses." + code, "structure", "added", "response", null, JsonValue.Create(code), "normal", "新增响应码");
                continue;
            }
            if (!SameMedia(existing.MediaType, response.MediaType) && response.Schema is not null && existing.Schema is not null)
            {
                Add("responses." + code, "structure", "changed", "mediaType", existing.MediaType, response.MediaType, "incompatible", "响应媒体类型变化");
            }
            if (response.Schema is not null && existing.Schema is not null)
            {
                CompareSchema(existing.Schema, response.Schema, "responses." + code, request: false);
            }
            else if (response.Schema is not null && existing.Schema is null)
            {
                Add("responses." + code, "structure", "added", "schema", null, SchemaNormalizer.StructureOnly(response.Schema), "normal", "补充响应结构");
            }
        }
        foreach (var code in remote.Responses.Keys.Where(c => !target.Responses.ContainsKey(c)))
        {
            Add("responses." + code, "structure", "info", "response", JsonValue.Create(code), null, "normal", "远端独有的响应定义，将保留");
        }
    }

    private void CompareBody(DocBody? remote, DocBody? target)
    {
        if (remote is null && target is null) return;
        if (remote is null)
        {
            Add("requestBody", "structure", "added", "requestBody", null, JsonValue.Create(target!.MediaType), target.Required ? "incompatible" : "normal", target.Required ? "新增必填请求体" : "新增请求体");
            return;
        }
        if (target is null)
        {
            Add("requestBody", "structure", "removed", "requestBody", JsonValue.Create(remote.MediaType), null, "incompatible", "源中已没有请求体");
            return;
        }
        if (!SameMedia(remote.MediaType, target.MediaType))
        {
            Add("requestBody", "structure", "changed", "mediaType", remote.MediaType, target.MediaType, "incompatible", "请求体媒体类型变化");
        }
        if (remote.Required != target.Required)
        {
            Add("requestBody", "structure", "changed", "required", remote.Required, target.Required, target.Required ? "incompatible" : "normal", target.Required ? "请求体改为必填" : "请求体改为可选");
        }
        CompareSchema(remote.Schema, target.Schema, "requestBody", request: true);
    }

    private static bool SameMedia(string? a, string? b)
    {
        static string N(string? m) => (m ?? "").Split(';')[0].Trim().ToLowerInvariant() switch
        {
            "json" or "text/json" or "application/*+json" => "application/json",
            var x => x,
        };
        return N(a) == N(b);
    }

    public void CompareSchema(JsonObject before, JsonObject after, string location, bool request)
    {
        if (before["x-opaque-ref"] is not null || after["x-opaque-ref"] is not null)
        {
            var b = before["x-opaque-ref"]?.ToString();
            var a = after["x-opaque-ref"]?.ToString();
            if (b != a)
            {
                Add(location, "structure", "changed", "ref", b is null ? SchemaNormalizer.StructureOnly(before) : b, a is null ? SchemaNormalizer.StructureOnly(after) : a, "incompatible", "引用的模型不同");
            }
            return;
        }
        if (before["x-recursion"] is not null || after["x-recursion"] is not null)
        {
            if (before["x-ref"]?.ToString() != after["x-ref"]?.ToString())
            {
                Add(location, "structure", "changed", "recursiveRef", before["x-ref"]?.DeepClone(), after["x-ref"]?.DeepClone(), "manual", "递归引用的模型不同");
            }
            return;
        }

        var beforeTypes = Types(before);
        var afterTypes = Types(after);
        if (beforeTypes != afterTypes)
        {
            // “任意类型 → 具体类型”对响应是补充说明（例如通用封装 data 补上具体结构），对请求则是收紧。
            var (risk, reason) = (beforeTypes.Length == 0, afterTypes.Length == 0) switch
            {
                (true, false) => request ? ("incompatible", "请求由任意类型改为具体类型（更严格）") : ("normal", "响应补充具体类型"),
                (false, true) => request ? ("normal", "请求放宽为任意类型") : ("manual", "响应结构变为任意类型（丢失结构信息）"),
                _ => ("incompatible", "类型变化"),
            };
            Add(location, "structure", "changed", "type", beforeTypes.Length == 0 ? "任意" : beforeTypes, afterTypes.Length == 0 ? "任意" : afterTypes, risk, reason);
            return;
        }

        var beforeNullable = before["nullable"] is not null;
        var afterNullable = after["nullable"] is not null;
        if (beforeNullable != afterNullable)
        {
            var risky = request ? !afterNullable : afterNullable;
            Add(location, "structure", "changed", "nullable", beforeNullable, afterNullable, risky ? "incompatible" : "normal",
                request ? (afterNullable ? "请求允许 null" : "请求不再允许 null") : (afterNullable ? "响应可能返回 null" : "响应不再返回 null"));
        }

        var beforeFormat = before["format"]?.ToString();
        var afterFormat = after["format"]?.ToString();
        if (beforeFormat != afterFormat)
        {
            var changed = beforeFormat is not null && afterFormat is not null;
            Add(location, "structure", "changed", "format", beforeFormat, afterFormat, changed ? "incompatible" : "normal", changed ? "格式变化" : "格式声明增减");
        }

        CompareEnum(before, after, location, request);
        CompareConstraints(before, after, location, request);

        if (!Canonical.Equal(before["default"], after["default"]) && (before.ContainsKey("default") || after.ContainsKey("default")))
        {
            Add(location, "structure", "changed", "default", before["default"]?.DeepClone(), after["default"]?.DeepClone(), request ? "incompatible" : "normal", "默认值变化");
        }

        foreach (var key in new[] { "allOf", "oneOf", "anyOf" })
        {
            if (!Canonical.Equal(SchemaNormalizer.StructureOnly(before[key]), SchemaNormalizer.StructureOnly(after[key])))
            {
                Add(location, "structure", "changed", key, SchemaNormalizer.StructureOnly(before[key]), SchemaNormalizer.StructureOnly(after[key]), "manual", "组合结构变化，第一版不做等价推理，需人工判断");
            }
        }

        var beforeProps = before["properties"] as JsonObject ?? new JsonObject();
        var afterProps = after["properties"] as JsonObject ?? new JsonObject();
        var beforeRequired = Names(before["required"]);
        var afterRequired = Names(after["required"]);
        foreach (var (name, value) in afterProps)
        {
            var path = location + "." + name;
            if (!beforeProps.TryGetPropertyValue(name, out var old) || old is not JsonObject oldSchema)
            {
                var required = afterRequired.Contains(name);
                Add(path, "structure", "added", "property", null, SchemaNormalizer.StructureOnly(value),
                    request && required ? "incompatible" : "normal", request ? (required ? "请求新增必填字段" : "请求新增可选字段") : "响应新增字段");
                continue;
            }
            if (beforeRequired.Contains(name) != afterRequired.Contains(name))
            {
                var nowRequired = afterRequired.Contains(name);
                Add(path, "structure", "changed", "required", !nowRequired, nowRequired, request == nowRequired ? "incompatible" : "normal",
                    request ? (nowRequired ? "请求字段改为必填" : "请求字段改为可选") : (nowRequired ? "响应字段改为必有" : "响应字段可能缺失"));
            }
            CompareSchema(oldSchema, (JsonObject)value!, path, request);
        }
        foreach (var (name, value) in beforeProps)
        {
            if (afterProps.ContainsKey(name)) continue;
            Add(location + "." + name, "structure", "removed", "property", SchemaNormalizer.StructureOnly(value), null, "incompatible", request ? "请求删除字段" : "响应删除字段");
        }

        if (before["items"] is JsonObject beforeItems && after["items"] is JsonObject afterItems)
        {
            CompareSchema(beforeItems, afterItems, location + "[]", request);
        }
        else if (before["items"] is not null || after["items"] is not null)
        {
            Add(location + "[]", "structure", "changed", "items", SchemaNormalizer.StructureOnly(before["items"]), SchemaNormalizer.StructureOnly(after["items"]), "incompatible", "数组元素结构变化");
        }

        var beforeAdditional = before["additionalProperties"];
        var afterAdditional = after["additionalProperties"];
        if (beforeAdditional is JsonObject ba && afterAdditional is JsonObject aa)
        {
            CompareSchema(ba, aa, location + "{}", request);
        }
        else if (!Canonical.Equal(SchemaNormalizer.StructureOnly(beforeAdditional), SchemaNormalizer.StructureOnly(afterAdditional)))
        {
            // additionalProperties:false 与缺省的差别只影响校验严格程度，按普通变化展示。
            var onlyFlag = beforeAdditional is null or JsonValue && afterAdditional is null or JsonValue;
            Add(location, "structure", "changed", "additionalProperties", SchemaNormalizer.StructureOnly(beforeAdditional), SchemaNormalizer.StructureOnly(afterAdditional),
                onlyFlag ? "normal" : "incompatible", onlyFlag ? "额外字段约束声明不同" : "字典值结构变化");
        }
    }

    private void CompareEnum(JsonObject before, JsonObject after, string location, bool request)
    {
        var beforeEnum = before["enum"] as JsonArray;
        var afterEnum = after["enum"] as JsonArray;
        if (beforeEnum is null && afterEnum is null) return;
        if (beforeEnum is null || afterEnum is null)
        {
            Add(location, "structure", "changed", "enum", beforeEnum?.DeepClone(), afterEnum?.DeepClone(), afterEnum is null ? (request ? "normal" : "manual") : (request ? "incompatible" : "normal"),
                afterEnum is null ? "取消枚举限制" : "新增枚举限制");
            return;
        }
        // 按值与 JSON 类型比较：数字 1 与字符串 "1" 不相同。
        var beforeSet = beforeEnum.Select(v => Canonical.Json(v)).ToHashSet(StringComparer.Ordinal);
        var afterSet = afterEnum.Select(v => Canonical.Json(v)).ToHashSet(StringComparer.Ordinal);
        var added = afterSet.Except(beforeSet).ToList();
        var removed = beforeSet.Except(afterSet).ToList();
        if (added.Count > 0)
        {
            Add(location, "structure", "added", "enum", null, new JsonArray(added.Select(a => JsonNode.Parse(a)).ToArray()),
                request ? "normal" : "manual", request ? "请求允许的枚举值增加" : "响应枚举新增值，可能影响前端穷举处理");
        }
        if (removed.Count > 0)
        {
            Add(location, "structure", "removed", "enum", new JsonArray(removed.Select(r => JsonNode.Parse(r)).ToArray()), null,
                request ? "incompatible" : "normal", request ? "请求允许的枚举值减少（更严格）" : "响应枚举值减少");
        }
    }

    private void CompareConstraints(JsonObject before, JsonObject after, string location, bool request)
    {
        // 收紧：下限升高、上限降低、新增约束、正则变化。
        (string Key, bool HigherIsTighter)[] bounds =
        [
            ("minimum", true), ("maximum", false), ("minLength", true), ("maxLength", false),
            ("minItems", true), ("maxItems", false), ("minProperties", true), ("maxProperties", false),
        ];
        foreach (var (key, higherIsTighter) in bounds)
        {
            var b = Number(before[key]);
            var a = Number(after[key]);
            if (b == a) continue;
            bool tighter = b is null ? true : a is null ? false : higherIsTighter ? a > b : a < b;
            Add(location, "structure", "changed", key, before[key]?.DeepClone(), after[key]?.DeepClone(),
                request && tighter ? "incompatible" : "normal", tighter ? "约束收紧" : "约束放宽");
        }
        foreach (var key in new[] { "exclusiveMinimum", "exclusiveMaximum", "uniqueItems", "multipleOf", "readOnly", "writeOnly" })
        {
            if (!Canonical.Equal(before[key], after[key]))
            {
                Add(location, "structure", "changed", key, before[key]?.DeepClone(), after[key]?.DeepClone(), request ? "incompatible" : "normal", "约束变化");
            }
        }
        var beforePattern = before["pattern"]?.ToString();
        var afterPattern = after["pattern"]?.ToString();
        if (beforePattern != afterPattern)
        {
            Add(location, "structure", "changed", "pattern", beforePattern, afterPattern, request ? "incompatible" : "manual", "正则约束变化");
        }
    }

    private static decimal? Number(JsonNode? node) =>
        node is JsonValue v && decimal.TryParse(v.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;

    private static string Types(JsonObject schema) =>
        schema["type"] is JsonArray types ? string.Join("|", types.Select(t => t?.ToString())) : "";

    private static HashSet<string> Names(JsonNode? node) =>
        (node as JsonArray)?.Select(n => n?.ToString()).OfType<string>().ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);

    private static string Loc(DocParameter p) => p.In + "." + p.Name;

    private static JsonNode Describe(DocParameter p) => new JsonObject
    {
        ["in"] = p.In,
        ["name"] = p.Name,
        ["required"] = p.Required,
        ["schema"] = SchemaNormalizer.StructureOnly(p.Schema),
    };

    public DiffItem Add(string location, string category, string change, string field, JsonNode? before, JsonNode? after, string risk, string reason)
    {
        var item = new DiffItem
        {
            Id = "d" + Ids.ShortHash(_resource + "|" + location + "|" + field + "|" + change + "|" + Canonical.Json(before) + "|" + Canonical.Json(after), 12),
            ResourceKey = _resource,
            Location = location,
            Category = category,
            Change = change,
            Field = field,
            Before = before,
            After = after,
            Source = _source,
            Risk = risk,
            Reason = reason,
            Blocking = risk == "blocking",
            NeedsDecision = risk is "incompatible" or "manual" && change != "info",
        };
        _items.Add(item);
        return item;
    }

    public void SetResource(string resource, string source)
    {
        _resource = resource;
        _source = source;
    }
}
