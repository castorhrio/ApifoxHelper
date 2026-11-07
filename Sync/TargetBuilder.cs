using ApifoxHelper.Comments;
using ApifoxHelper.Contracts;
using ApifoxHelper.Source;

namespace ApifoxHelper.Sync;

/// <summary>
/// 说明条目：已确认的注释候选（comment）与源码中已有的有效注释（sourceDoc），按“接口身份 + 位置”或“模型 + 字段”定位。
/// 同一份条目既用于源码补丁，也用于构造文档目标，避免三处分别手填导致含义漂移。
/// </summary>
public sealed class DocEntries
{
    public Dictionary<string, (string Text, string Origin)> Endpoint { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (string Text, string Origin)> Component { get; } = new(StringComparer.Ordinal);

    public static DocEntries Build(ScanOutput? scan, DraftStore? drafts)
    {
        var entries = new DocEntries();
        if (scan is null) return entries;
        var accepted = (drafts?.Candidates ?? new List<CommentCandidate>())
            .Where(c => c.ReviewState == "accepted" && c.Action == "set")
            .GroupBy(c => c.SlotId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.ReviewedAt).First().Text, StringComparer.Ordinal);
        var endpoints = scan.Endpoints.ToDictionary(e => e.Key, StringComparer.Ordinal);
        foreach (var slot in scan.Slots)
        {
            string? text;
            string origin;
            if (accepted.TryGetValue(slot.Id, out var confirmed))
            {
                text = confirmed;
                origin = "comment";
            }
            else if (slot.Status is "valid" or "inheritDoc" && !string.IsNullOrWhiteSpace(slot.ExistingText))
            {
                text = slot.ExistingText;
                origin = "sourceDoc";
            }
            else
            {
                continue;
            }
            switch (slot.SymbolKind)
            {
                case "method":
                    foreach (var key in slot.EndpointKeys)
                    {
                        if (!endpoints.TryGetValue(key, out var endpoint)) continue;
                        if (slot.Tag == "summary") entries.Endpoint[key + "|summary"] = (text!, origin);
                        else if (slot.Tag == "returns") entries.Endpoint[key + "|response:200"] = (text!, origin);
                        else if (slot.Tag == "param" && slot.Status != "stale")
                        {
                            var binding = endpoint.Parameters.FirstOrDefault(p => p.Name == slot.ParameterName);
                            if (binding is null) continue;
                            if (binding.Source == "body") entries.Endpoint[key + "|requestBody"] = (text!, origin);
                            else if (binding.Source is "query" or "path" or "header") entries.Endpoint[$"{key}|param:{binding.Source}:{binding.ExternalName}"] = (text!, origin);
                        }
                    }
                    break;
                case "type" or "enum" when slot.ComponentName is not null:
                    entries.Component[slot.ComponentName] = (text!, origin);
                    break;
                case "property" or "recordParameter" when slot.ComponentName is not null && slot.JsonName is not null:
                    entries.Component[slot.ComponentName + "|" + slot.JsonName] = (text!, origin);
                    break;
            }
        }
        return entries;
    }
}

public sealed class TargetBuilder(DocEntries entries, SupplementStore supplements, DecisionStore decisions)
{
    /// <summary>
    /// 构造接口目标：Swagger 操作 → 已确认响应契约替换 200 响应体 → 叠加说明条目与人工补充 → 与远端说明合并。
    /// </summary>
    public (JsonObject Raw, List<DocChange> Changes, List<string> Notes) BuildRaw(string key, JsonObject swaggerOperation, ResponseContract? contract, DocOperation? remote)
    {
        var raw = (JsonObject)swaggerOperation.DeepClone();
        var notes = new List<string>();
        if (contract is not null && contract.ResponseSchema is not null)
        {
            var responses = raw["responses"] as JsonObject ?? new JsonObject();
            raw["responses"] = responses;
            var ok = responses["200"] as JsonObject ?? new JsonObject { ["description"] = "OK" };
            ok["content"] = new JsonObject { ["application/json"] = new JsonObject { ["schema"] = contract.ResponseSchema.DeepClone() } };
            responses["200"] = ok;
            notes.Add("200 响应体使用已确认的响应契约（data 为 " + contract.DataType + "）。");
        }

        // 叠加说明条目（已确认注释优先于源码已有注释，二者都优先于 Swagger 中可能过时的文字）。
        Apply(raw, key + "|summary", v => raw["summary"] = v);
        Apply(raw, key + "|requestBody", v => { if (raw["requestBody"] is JsonObject body) body["description"] = v; });
        Apply(raw, key + "|response:200", v => { if (raw["responses"]?["200"] is JsonObject ok) ok["description"] = v; });
        foreach (var parameter in (raw["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            var entryKey = $"{key}|param:{parameter["in"]}:{parameter["name"]}";
            Apply(raw, entryKey, v => parameter["description"] = v);
        }

        var changes = new List<DocChange>();
        if (remote is not null)
        {
            // 已有接口：标题默认保留远端；其余说明按合并规则处理。
            Merge(changes, key, "operation", "name", remote.Summary, SchemaNormalizer.Text(raw["summary"]), OriginOf(key + "|summary"), v => raw["summary"] = v, titleRule: true);
            Merge(changes, key, "operation", "description", remote.Description, SchemaNormalizer.Text(raw["description"]), "swagger", v => raw["description"] = v);
            foreach (var parameter in (raw["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var location = $"{parameter["in"]}.{parameter["name"]}";
                remote.Parameters.TryGetValue($"{parameter["in"]}:{parameter["name"]}", out var remoteParameter);
                Merge(changes, key, location, "description", remoteParameter?.Description, SchemaNormalizer.Text(parameter["description"]),
                    OriginOf($"{key}|param:{parameter["in"]}:{parameter["name"]}"), v => parameter["description"] = v);
            }
            if (raw["requestBody"] is JsonObject requestBody)
            {
                Merge(changes, key, "requestBody", "description", remote.RequestBody?.Description, SchemaNormalizer.Text(requestBody["description"]), OriginOf(key + "|requestBody"), v => requestBody["description"] = v);
            }
            foreach (var (code, node) in raw["responses"] as JsonObject ?? new JsonObject())
            {
                if (node is not JsonObject response) continue;
                remote.Responses.TryGetValue(code, out var remoteResponse);
                Merge(changes, key, "responses." + code, "description", remoteResponse?.Description, SchemaNormalizer.Text(response["description"]),
                    code == "200" ? OriginOf(key + "|response:200") : "swagger", v => response["description"] = v);
            }
        }
        else
        {
            ApplySupplement(key, "operation", "name", v => raw["summary"] = v);
            ApplySupplement(key, "operation", "description", v => raw["description"] = v);
        }
        return (raw, changes, notes);
    }

    /// <summary>未提供 Swagger 时：结构保持远端现状，只把说明条目与人工补充合并到远端说明上。</summary>
    public (DocOperation Target, List<DocChange> Changes) BuildDocsOnly(string key, DocOperation remote)
    {
        var target = JsonSerializer.Deserialize<DocOperation>(JsonSerializer.Serialize(remote, JsonDefaults.Compact), JsonDefaults.Compact)!;
        target.Sources = ["remote", "说明补充层"];
        var changes = new List<DocChange>();
        Merge(changes, key, "operation", "name", remote.Summary, Entry(key + "|summary"), OriginOf(key + "|summary"), v => target.Summary = v, titleRule: true);
        Merge(changes, key, "operation", "description", remote.Description, null, "swagger", v => target.Description = v);
        foreach (var parameter in target.Parameters.Values)
        {
            Merge(changes, key, $"{parameter.In}.{parameter.Name}", "description", parameter.Description, Entry($"{key}|param:{parameter.In}:{parameter.Name}"),
                OriginOf($"{key}|param:{parameter.In}:{parameter.Name}"), v => parameter.Description = v);
        }
        if (target.RequestBody is not null)
        {
            Merge(changes, key, "requestBody", "description", remote.RequestBody?.Description, Entry(key + "|requestBody"), OriginOf(key + "|requestBody"), v => target.RequestBody.Description = v);
        }
        if (target.Responses.TryGetValue("200", out var ok))
        {
            Merge(changes, key, "responses.200", "description", ok.Description, Entry(key + "|response:200"), OriginOf(key + "|response:200"), v => ok.Description = v);
        }
        return (target, changes);
    }

    /// <summary>模型说明：已确认注释、源码已有注释与人工补充叠加到 OpenAPI 组件上。</summary>
    public JsonObject BuildComponent(string name, JsonObject component)
    {
        var copy = (JsonObject)component.DeepClone();
        if (entries.Component.TryGetValue(name, out var typeEntry)) copy["description"] = typeEntry.Text;
        if (copy["properties"] is JsonObject properties)
        {
            foreach (var (propertyName, node) in properties.ToList())
            {
                if (!entries.Component.TryGetValue(name + "|" + propertyName, out var entry) || node is not JsonObject property) continue;
                SetSchemaDescription(properties, propertyName, property, entry.Text);
            }
        }
        foreach (var supplement in supplements.Entries.Where(s => s.ResourceKey == "model:" + name && s.Field == "description" && !s.KeepAsIs && s.Value is not null))
        {
            if (supplement.Location.Length == 0) copy["description"] = supplement.Value;
            else if (copy["properties"] is JsonObject props && props[supplement.Location] is JsonObject property) SetSchemaDescription(props, supplement.Location, property, supplement.Value!);
        }
        return copy;
    }

    public bool IsConfirmedComponentDoc(string name, string? property) =>
        (entries.Component.TryGetValue(property is null ? name : name + "|" + property, out var entry) && entry.Origin == "comment")
        || supplements.Entries.Any(s => s.ResourceKey == "model:" + name && s.Location == (property ?? "") && !s.KeepAsIs);

    private static void SetSchemaDescription(JsonObject parent, string key, JsonObject schema, string text)
    {
        if (schema.ContainsKey("$ref"))
        {
            parent[key] = new JsonObject { ["allOf"] = new JsonArray(schema.DeepClone()), ["description"] = text };
        }
        else
        {
            schema["description"] = text;
        }
    }

    private void Apply(JsonObject raw, string entryKey, Action<string> set)
    {
        if (entries.Endpoint.TryGetValue(entryKey, out var entry)) set(entry.Text);
    }

    private string? Entry(string entryKey) => entries.Endpoint.TryGetValue(entryKey, out var entry) ? entry.Text : null;

    private string OriginOf(string entryKey) => entries.Endpoint.TryGetValue(entryKey, out var entry) ? entry.Origin : "swagger";

    private void ApplySupplement(string key, string location, string field, Action<string> set)
    {
        var supplement = supplements.Entries.FirstOrDefault(s => s.ResourceKey == key && s.Location == location && s.Field == field);
        if (supplement is { KeepAsIs: false, Value: not null }) set(supplement.Value);
    }

    /// <summary>
    /// 合并规则：人工补充最优先；目标为空保留远端；远端为空则补充；两者不同且目标来自本次确认的注释时形成冲突，
    /// 冲突未选择前保留远端；其余来源（Swagger、源码已有注释）的不同说明默认保留远端。
    /// </summary>
    private void Merge(List<DocChange> changes, string key, string location, string field, string? remoteValue, string? targetValue, string origin, Action<string?> set, bool titleRule = false)
    {
        var id = "c" + Ids.ShortHash(key + "|" + location + "|" + field, 12);
        var change = new DocChange { Id = id, Location = location, Field = field, RemoteValue = remoteValue, TargetValue = targetValue, Origin = origin };
        var supplement = supplements.Entries.FirstOrDefault(s => s.ResourceKey == key && s.Location == location && s.Field == field);
        if (supplement is not null)
        {
            change.Origin = "supplement";
            change.Decision = "supplement";
            change.FinalValue = supplement.KeepAsIs ? remoteValue : supplement.Value;
            change.TargetValue = supplement.KeepAsIs ? targetValue : supplement.Value;
            set(change.FinalValue);
            if (!Same(change.FinalValue, remoteValue) || supplement.KeepAsIs) changes.Add(change);
            return;
        }
        if (string.IsNullOrWhiteSpace(targetValue) || Same(targetValue, remoteValue))
        {
            set(remoteValue);
            return;
        }
        if (string.IsNullOrWhiteSpace(remoteValue))
        {
            change.Decision = "fill";
            change.FinalValue = targetValue;
            set(targetValue);
            changes.Add(change);
            return;
        }
        if (titleRule)
        {
            // 已有接口标题第一版保留；源码 summary 只作为建议展示，可通过人工补充明确改名。
            change.Decision = "suggestion";
            change.FinalValue = remoteValue;
            set(remoteValue);
            changes.Add(change);
            return;
        }
        if (origin == "comment")
        {
            var choice = decisions.DocChoices.GetValueOrDefault(id);
            change.Decision = choice switch { "useTarget" => "useTarget", "keepRemote" => "keepRemote", _ => "conflict" };
            change.FinalValue = choice == "useTarget" ? targetValue : remoteValue;
            set(change.FinalValue);
            changes.Add(change);
            return;
        }
        change.Decision = "keepRemote";
        change.FinalValue = remoteValue;
        set(remoteValue);
        changes.Add(change);
    }

    private static bool Same(string? a, string? b) => TextUtil.NormalizeSpace(a) == TextUtil.NormalizeSpace(b);
}
