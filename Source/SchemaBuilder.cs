using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;

namespace ApifoxHelper.Source;

/// <summary>
/// 由 C# 类型生成 OpenAPI 3.0 结构，命名与可空规则对齐 Swashbuckle 默认行为，便于与生成文档和既有模型比较。
/// 自定义转换器等无法可靠推断的结构以“任意类型 + 说明”表示，并记录到 Notes。
/// </summary>
public sealed class SchemaBuilder(ProjectProfile profile, Func<string, string?>? descriptionOf = null)
{
    private readonly HashSet<string> _building = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _idOwners = new(StringComparer.Ordinal);

    public Dictionary<string, JsonObject> Components { get; } = new(StringComparer.Ordinal);

    public List<string> Notes { get; } = new();

    public JsonObject Build(ITypeSymbol type, bool nullableContext = false)
    {
        var schema = BuildCore(type, out var isReference);
        if (nullableContext && !isReference) schema["nullable"] = true;
        if (nullableContext && isReference)
        {
            // OpenAPI 3.0 中 $ref 旁不能附加关键字，可空引用用 allOf 包一层（与 Swashbuckle 生成方式一致）。
            return new JsonObject { ["allOf"] = new JsonArray(schema), ["nullable"] = true };
        }
        return schema;
    }

    public static string SchemaId(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return SchemaId(array.ElementType) + "Array";
        if (type is INamedTypeSymbol { IsGenericType: true } named && !SymbolUtil.IsNullableValue(type))
        {
            return string.Concat(named.TypeArguments.Select(SchemaId)) + named.Name;
        }
        if (SymbolUtil.TryGetElementType(type, out var element) && type is INamedTypeSymbol)
        {
            return SchemaId(element) + type.Name;
        }
        return SymbolUtil.UnwrapNullable(type).Name;
    }

    private JsonObject BuildCore(ITypeSymbol type, out bool isReference)
    {
        isReference = false;
        if (SymbolUtil.IsNullableValue(type))
        {
            var inner = BuildCore(SymbolUtil.UnwrapNullable(type), out isReference);
            if (!isReference) inner["nullable"] = true;
            return isReference ? new JsonObject { ["allOf"] = new JsonArray(inner), ["nullable"] = true } : inner;
        }

        if (Primitive(type) is { } primitive) return primitive;

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
        {
            isReference = true;
            return EnumReference(enumType);
        }

        var display = type.ToDisplayString();
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic
            || display is "System.Text.Json.JsonElement" or "System.Text.Json.Nodes.JsonNode" or "System.Text.Json.Nodes.JsonObject" or "Newtonsoft.Json.Linq.JObject" or "Newtonsoft.Json.Linq.JToken")
        {
            return new JsonObject();
        }

        if (SymbolUtil.TryGetDictionaryValue(type, out var valueType))
        {
            return new JsonObject { ["type"] = "object", ["additionalProperties"] = Build(valueType, IsNullableReference(valueType)) };
        }

        if (SymbolUtil.TryGetElementType(type, out var elementType))
        {
            return new JsonObject { ["type"] = "array", ["items"] = Build(elementType, IsNullableReference(elementType)) };
        }

        if (type is INamedTypeSymbol named && named.IsAnonymousType)
        {
            return ObjectSchema(named, anonymous: true);
        }

        if (type is INamedTypeSymbol complex && (complex.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Interface))
        {
            isReference = true;
            return ComponentReference(complex);
        }

        Notes.Add($"类型 {display} 无法生成结构，以任意类型表示。");
        return new JsonObject();
    }

    private static JsonObject? Primitive(ITypeSymbol type)
    {
        static JsonObject S(string t, string? f = null)
        {
            var o = new JsonObject { ["type"] = t };
            if (f is not null) o["format"] = f;
            return o;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean: return S("boolean");
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32: return S("integer", "int32");
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64: return S("integer", "int64");
            case SpecialType.System_Single: return S("number", "float");
            case SpecialType.System_Double:
            case SpecialType.System_Decimal: return S("number", "double");
            case SpecialType.System_String:
            case SpecialType.System_Char: return S("string");
            case SpecialType.System_DateTime: return S("string", "date-time");
        }
        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte }) return S("string", "byte");
        return type.ToDisplayString() switch
        {
            "System.DateTimeOffset" => S("string", "date-time"),
            "System.DateOnly" => S("string", "date"),
            "System.TimeOnly" => S("string", "time"),
            "System.TimeSpan" => S("string", "date-span"),
            "System.Guid" => S("string", "uuid"),
            "System.Uri" => S("string", "uri"),
            "Microsoft.AspNetCore.Http.IFormFile" => S("string", "binary"),
            _ => null,
        };
    }

    private JsonObject EnumReference(INamedTypeSymbol type)
    {
        var id = UniqueId(type);
        if (!Components.ContainsKey(id))
        {
            var asString = profile.Serialization.EnumAsString || (SymbolUtil.HasCustomConverter(type, out var converter) && SymbolUtil.IsStringEnumConverter(converter));
            var values = new JsonArray();
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
            {
                values.Add(asString ? JsonValue.Create(member.Name) : JsonValue.Create(Convert.ToInt64(member.ConstantValue, CultureInfo.InvariantCulture)));
            }
            var schema = asString
                ? new JsonObject { ["type"] = "string", ["enum"] = values }
                : new JsonObject { ["type"] = "integer", ["format"] = "int32", ["enum"] = values };
            if (Description(SymbolUtil.DocId(type), type) is { } description) schema["description"] = description;
            Components[id] = schema;
        }
        return new JsonObject { ["$ref"] = "#/components/schemas/" + id };
    }

    private JsonObject ComponentReference(INamedTypeSymbol type)
    {
        var id = UniqueId(type);
        if (!Components.ContainsKey(id) && _building.Add(id))
        {
            Components[id] = new JsonObject();
            Components[id] = ObjectSchema(type, anonymous: false);
            _building.Remove(id);
        }
        return new JsonObject { ["$ref"] = "#/components/schemas/" + id };
    }

    private JsonObject ObjectSchema(INamedTypeSymbol type, bool anonymous)
    {
        var schema = new JsonObject { ["type"] = "object" };
        var properties = new JsonObject();
        var required = new JsonArray();
        if (SymbolUtil.HasCustomConverter(type, out var typeConverter))
        {
            Notes.Add($"{type.Name} 使用自定义 JSON 转换器 {typeConverter}，结构需人工确认。");
        }
        foreach (var property in SymbolUtil.SerializableProperties(type))
        {
            var name = anonymous ? (profile.Serialization.NamingPolicy == "camelCase" ? JsonNamingPolicy.CamelCase.ConvertName(property.Name) : property.Name) : SymbolUtil.JsonName(property, profile.Serialization);
            JsonObject propertySchema;
            if (SymbolUtil.HasCustomConverter(property, out var converter) && !(property.Type.TypeKind == TypeKind.Enum && SymbolUtil.IsStringEnumConverter(converter)))
            {
                propertySchema = new JsonObject { ["description"] = $"自定义转换器 {converter}，结构需人工确认" };
                Notes.Add($"{type.Name}.{property.Name} 使用自定义 JSON 转换器 {converter}，结构需人工确认。");
            }
            else if (property.Type.TypeKind == TypeKind.Enum && SymbolUtil.HasCustomConverter(property, out var enumConverter) && SymbolUtil.IsStringEnumConverter(enumConverter) && property.Type is INamedTypeSymbol enumType)
            {
                var values = new JsonArray();
                foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue)) values.Add(member.Name);
                propertySchema = new JsonObject { ["type"] = "string", ["enum"] = values };
            }
            else
            {
                propertySchema = Build(property.Type, IsNullableReference(property.Type, property.NullableAnnotation));
            }
            if (!anonymous && Description(SymbolUtil.DocId(property.OriginalDefinition), property) is { } description)
            {
                if (propertySchema.ContainsKey("$ref"))
                {
                    propertySchema = new JsonObject { ["allOf"] = new JsonArray(propertySchema), ["description"] = description };
                }
                else
                {
                    propertySchema["description"] = description;
                }
            }
            if (property.SetMethod is null && !anonymous && !propertySchema.ContainsKey("allOf") && !propertySchema.ContainsKey("$ref"))
            {
                propertySchema["readOnly"] = true;
            }
            properties[name] = propertySchema;
            if (SymbolUtil.IsRequired(property)) required.Add(name);
        }
        schema["properties"] = properties;
        if (required.Count > 0) schema["required"] = required;
        if (!anonymous)
        {
            schema["additionalProperties"] = false;
            if (Description(SymbolUtil.DocId(type.OriginalDefinition), type) is { } typeDescription) schema["description"] = typeDescription;
        }
        return schema;
    }

    private bool IsNullableReference(ITypeSymbol type, NullableAnnotation annotation = NullableAnnotation.None)
    {
        if (type.IsValueType) return false;
        if (!profile.Serialization.NonNullableReferenceTypes) return true;
        var effective = annotation == NullableAnnotation.None ? type.NullableAnnotation : annotation;
        return effective != NullableAnnotation.NotAnnotated;
    }

    private string? Description(string symbolId, ISymbol symbol)
    {
        var overridden = descriptionOf?.Invoke(symbolId);
        if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax is null ? null : DocComments.Read(syntax).TextOf("summary");
    }

    private string UniqueId(INamedTypeSymbol type)
    {
        var id = SchemaId(type);
        var owner = type.ToDisplayString();
        if (_idOwners.TryGetValue(id, out var existing) && existing != owner)
        {
            var alternative = id + "_" + Ids.ShortHash(owner, 4);
            Notes.Add($"模型名 {id} 同时对应 {existing} 与 {owner}，后者改用 {alternative}；Swagger 中的实际名称以生成器配置为准。");
            id = alternative;
        }
        _idOwners[id] = owner;
        return id;
    }
}
