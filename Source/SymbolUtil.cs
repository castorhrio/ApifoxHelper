using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Source;

public static class SymbolUtil
{
    public static readonly SymbolDisplayFormat ShortFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public static string DocId(ISymbol symbol) =>
        symbol.OriginalDefinition.GetDocumentationCommentId() ?? symbol.ToDisplayString();

    public static string ParameterId(IMethodSymbol method, string parameterName) => DocId(method) + "#" + parameterName;

    public static string Short(ISymbol symbol) => symbol.ToDisplayString(ShortFormat);

    public static string TypeName(ITypeSymbol type) => type.ToDisplayString(ShortFormat);

    public static (string File, int Line) Location(ISymbol symbol, string workspaceRoot)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null) return ("", 0);
        var span = reference.SyntaxTree.GetLineSpan(reference.Span);
        return (Relative(workspaceRoot, reference.SyntaxTree.FilePath), span.StartLinePosition.Line + 1);
    }

    public static string Relative(string workspaceRoot, string path) =>
        string.IsNullOrEmpty(path) ? "" : TextUtil.ToForwardSlash(System.IO.Path.GetRelativePath(workspaceRoot, path));

    public static bool IsInSource(ISymbol symbol) => symbol.DeclaringSyntaxReferences.Length > 0;

    /// <summary>
    /// 按完整名比较特性类型；类型未解析（错误类型）时退回按简单名比较，便于在部分降级时仍能识别常见特性。
    /// </summary>
    public static bool Is(AttributeData attribute, string fullName)
    {
        var cls = attribute.AttributeClass;
        if (cls is null) return false;
        if (cls.TypeKind != TypeKind.Error) return cls.ToDisplayString() == fullName;
        var simple = fullName[(fullName.LastIndexOf('.') + 1)..];
        var name = cls.Name;
        return name == simple || name + "Attribute" == simple;
    }

    public static AttributeData? Find(ISymbol symbol, string fullName) => symbol.GetAttributes().FirstOrDefault(a => Is(a, fullName));

    public static bool Has(ISymbol symbol, string fullName) => Find(symbol, fullName) is not null;

    public static string? StringArg(AttributeData attribute, int index = 0)
    {
        if (attribute.ConstructorArguments.Length <= index) return null;
        var arg = attribute.ConstructorArguments[index];
        return arg.Kind == TypedConstantKind.Primitive ? arg.Value as string : null;
    }

    public static string? NamedString(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == name && pair.Value.Value is string s) return s;
        }
        return null;
    }

    public static object? NamedValue(AttributeData attribute, string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (pair.Key == name) return pair.Value.Value;
        }
        return null;
    }

    public static ITypeSymbol UnwrapNullable(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named ? named.TypeArguments[0] : type;

    public static bool IsNullableValue(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

    /// <summary>Task&lt;T&gt; / ValueTask&lt;T&gt; → T；非泛型 Task → null（表示无返回值）。</summary>
    public static ITypeSymbol? UnwrapTask(ITypeSymbol type, out bool isAsyncVoid)
    {
        isAsyncVoid = false;
        if (type is INamedTypeSymbol named)
        {
            var name = named.OriginalDefinition.ToDisplayString();
            if (name is "System.Threading.Tasks.Task<TResult>" or "System.Threading.Tasks.ValueTask<TResult>") return named.TypeArguments[0];
            if (name is "System.Threading.Tasks.Task" or "System.Threading.Tasks.ValueTask")
            {
                isAsyncVoid = true;
                return null;
            }
        }
        if (type.SpecialType == SpecialType.System_Void)
        {
            isAsyncVoid = true;
            return null;
        }
        return type;
    }

    public static bool IsSimple(ITypeSymbol type)
    {
        type = UnwrapNullable(type);
        if (type.TypeKind == TypeKind.Enum) return true;
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Char:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Decimal:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_String:
            case SpecialType.System_DateTime:
                return true;
        }
        return type.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.TimeSpan" or "System.Uri"
            or "System.DateOnly" or "System.TimeOnly" or "System.Version" or "System.Int128" or "System.UInt128" or "System.Half";
    }

    public static bool IsFormFile(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        if (name is "Microsoft.AspNetCore.Http.IFormFile" or "Microsoft.AspNetCore.Http.IFormFileCollection") return true;
        return TryGetElementType(type, out var element) && element.ToDisplayString() == "Microsoft.AspNetCore.Http.IFormFile";
    }

    public static bool IsSpecialParameter(ITypeSymbol type) =>
        type.ToDisplayString() is "System.Threading.CancellationToken" or "Microsoft.AspNetCore.Http.HttpContext"
            or "Microsoft.AspNetCore.Http.HttpRequest" or "Microsoft.AspNetCore.Http.HttpResponse"
            or "System.Security.Claims.ClaimsPrincipal";

    /// <summary>数组或 IEnumerable&lt;T&gt; 族（不含 string 与字典）。</summary>
    public static bool TryGetElementType(ITypeSymbol type, out ITypeSymbol element)
    {
        element = type;
        if (type is IArrayTypeSymbol array)
        {
            element = array.ElementType;
            return true;
        }
        if (type.SpecialType == SpecialType.System_String || type is not INamedTypeSymbol named) return false;
        if (TryGetDictionaryValue(type, out _)) return false;
        if (named.IsGenericType && named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            element = named.TypeArguments[0];
            return true;
        }
        var enumerable = named.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        if (enumerable is null) return false;
        element = enumerable.TypeArguments[0];
        return true;
    }

    public static bool TryGetDictionaryValue(ITypeSymbol type, out ITypeSymbol value)
    {
        value = type;
        if (type is not INamedTypeSymbol named) return false;
        var candidates = new List<INamedTypeSymbol> { named };
        candidates.AddRange(named.AllInterfaces);
        foreach (var c in candidates)
        {
            var name = c.OriginalDefinition.ToDisplayString();
            if (name is "System.Collections.Generic.IDictionary<TKey, TValue>" or "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>" or "System.Collections.Generic.Dictionary<TKey, TValue>")
            {
                value = c.TypeArguments[1];
                return true;
            }
        }
        return false;
    }

    public static bool IsFrameworkType(ITypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal) || ns.StartsWith("Microsoft.", StringComparison.Ordinal);
    }

    /// <summary>
    /// 需要展开字段说明的复杂类型：源码中的 class/struct/record/interface，不含框架类型与集合。
    /// </summary>
    public static bool IsDocumentableComplex(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct or TypeKind.Interface } named
        && !IsSimple(type)
        && named.SpecialType == SpecialType.None
        && !IsFrameworkType(type)
        && !TryGetElementType(type, out _)
        && !TryGetDictionaryValue(type, out _);

    public static string JsonName(IPropertySymbol property, SerializationSettings settings)
    {
        var stj = Find(property, "System.Text.Json.Serialization.JsonPropertyNameAttribute");
        if (stj is not null && StringArg(stj) is { Length: > 0 } explicitName) return explicitName;
        var newtonsoft = Find(property, "Newtonsoft.Json.JsonPropertyAttribute");
        if (newtonsoft is not null)
        {
            var name = StringArg(newtonsoft) ?? NamedString(newtonsoft, "PropertyName");
            if (!string.IsNullOrEmpty(name)) return name;
        }
        return settings.NamingPolicy == "camelCase" ? JsonNamingPolicy.CamelCase.ConvertName(property.Name) : property.Name;
    }

    /// <summary>
    /// 属性是否不进入对外 JSON：无条件 JsonIgnore（System.Text.Json Condition=Always 或 Newtonsoft）。
    /// </summary>
    public static bool IsJsonIgnored(IPropertySymbol property)
    {
        var stj = Find(property, "System.Text.Json.Serialization.JsonIgnoreAttribute");
        if (stj is not null)
        {
            var condition = NamedValue(stj, "Condition");
            // JsonIgnoreCondition: Never=0, Always=1, WhenWritingDefault=2, WhenWritingNull=3；未指定即 Always。
            if (condition is null || condition is int c && c == 1) return true;
        }
        return Has(property, "Newtonsoft.Json.JsonIgnoreAttribute");
    }

    public static bool HasCustomConverter(ISymbol symbol, out string? converter)
    {
        converter = null;
        var attribute = Find(symbol, "System.Text.Json.Serialization.JsonConverterAttribute") ?? Find(symbol, "Newtonsoft.Json.JsonConverterAttribute");
        if (attribute is null) return false;
        converter = attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is ITypeSymbol t ? t.Name : "未知转换器";
        return true;
    }

    public static bool IsStringEnumConverter(string? converter) =>
        converter is not null && (converter.StartsWith("JsonStringEnumConverter", StringComparison.Ordinal) || converter == "StringEnumConverter");

    /// <summary>对外序列化的公开实例属性（含继承），按声明顺序，基类在前。</summary>
    public static List<IPropertySymbol> SerializableProperties(INamedTypeSymbol type)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var t = type; t is not null && t.SpecialType != SpecialType.System_Object && t.SpecialType != SpecialType.System_ValueType; t = t.BaseType)
        {
            chain.Insert(0, t);
        }
        var result = new List<IPropertySymbol>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in chain)
        {
            foreach (var member in t.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic || member.IsIndexer || member.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;
                if (member.GetMethod is null || member.GetMethod.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;
                if (member.Name == "EqualityContract") continue;
                if (IsJsonIgnored(member)) continue;
                if (seen.Add(member.Name))
                {
                    result.Add(member);
                }
                else
                {
                    // new/override 重新声明时使用派生类型上的定义。
                    var index = result.FindIndex(p => p.Name == member.Name);
                    result[index] = member;
                }
            }
        }
        return result;
    }

    public static bool IsRequired(IPropertySymbol property) =>
        property.IsRequired || Has(property, "System.ComponentModel.DataAnnotations.RequiredAttribute");

    public static string LineText(SyntaxNode node)
    {
        var text = node.ToString();
        return TextUtil.Truncate(Regex.Replace(text, @"\s+", " ").Trim(), 400);
    }

    public static MemberDeclarationSyntax? DeclarationOf(ISymbol symbol)
    {
        var reference = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        return reference?.GetSyntax() as MemberDeclarationSyntax;
    }
}
