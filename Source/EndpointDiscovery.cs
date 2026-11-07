using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Source;

public sealed class DiscoveredEndpoint
{
    public required EndpointInfo Info { get; init; }
    public required IMethodSymbol Method { get; init; }
    public required INamedTypeSymbol Controller { get; init; }
    public required MethodDeclarationSyntax Syntax { get; init; }
}

/// <summary>
/// 按 ASP.NET Core MVC 的规则发现控制器动作与属性路由；只读语义信息，不运行应用。
/// </summary>
public sealed class EndpointDiscovery(LoadedWorkspace workspace, ProjectProfile profile)
{
    private static readonly Dictionary<string, string> HttpAttributes = new(StringComparer.Ordinal)
    {
        ["Microsoft.AspNetCore.Mvc.HttpGetAttribute"] = "GET",
        ["Microsoft.AspNetCore.Mvc.HttpPostAttribute"] = "POST",
        ["Microsoft.AspNetCore.Mvc.HttpPutAttribute"] = "PUT",
        ["Microsoft.AspNetCore.Mvc.HttpDeleteAttribute"] = "DELETE",
        ["Microsoft.AspNetCore.Mvc.HttpPatchAttribute"] = "PATCH",
        ["Microsoft.AspNetCore.Mvc.HttpHeadAttribute"] = "HEAD",
        ["Microsoft.AspNetCore.Mvc.HttpOptionsAttribute"] = "OPTIONS",
    };

    public List<string> Warnings { get; } = new();

    public List<DiscoveredEndpoint> Discover(CancellationToken ct)
    {
        var result = new List<DiscoveredEndpoint>();
        var assemblyApiController = workspace.Root.Compilation.Assembly.GetAttributes().Any(a => SymbolUtil.Is(a, "Microsoft.AspNetCore.Mvc.ApiControllerAttribute"));
        var ignored = 0;
        foreach (var type in EnumerateRootTypes(workspace.Root.Compilation.Assembly.GlobalNamespace))
        {
            ct.ThrowIfCancellationRequested();
            if (!IsController(type)) continue;
            var isApiController = assemblyApiController || InheritedAttribute(type, "Microsoft.AspNetCore.Mvc.ApiControllerAttribute") is not null;
            var controllerRoutes = ControllerRoutes(type);
            var controllerName = ControllerName(type);
            var area = InheritedAttribute(type, "Microsoft.AspNetCore.Mvc.AreaAttribute") is { } areaAttr ? SymbolUtil.StringArg(areaAttr) : null;
            var controllerIgnored = IsIgnoredByApiExplorer(type);

            foreach (var method in ActionMethods(type))
            {
                if (method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is not MethodDeclarationSyntax syntax) continue;
                if (controllerIgnored || IsIgnoredByApiExplorer(method))
                {
                    ignored++;
                    continue;
                }
                var actionName = ActionName(method);
                foreach (var (template, verbs) in ActionRoutes(method))
                {
                    var templates = Combine(controllerRoutes, template);
                    if (templates.Count == 0)
                    {
                        Warnings.Add($"{type.Name}.{method.Name} 未使用属性路由，无法确定路径，已跳过。");
                        continue;
                    }
                    foreach (var full in templates)
                    {
                        var path = ToOpenApiPath(ReplaceTokens(full, controllerName, actionName, area));
                        foreach (var verb in verbs.Count > 0 ? verbs : ["ANY"])
                        {
                            var info = BuildInfo(type, method, syntax, verb, path, full, actionName, controllerName, isApiController);
                            if (verbs.Count == 0) info.Notes.Add("动作没有声明 HTTP 方法特性，接受任意方法；同步前需人工确认。");
                            result.Add(new DiscoveredEndpoint { Info = info, Method = method, Controller = type, Syntax = syntax });
                        }
                    }
                }
            }
        }
        if (ignored > 0) Warnings.Add($"有 {ignored} 个动作标记了 ApiExplorerSettings(IgnoreApi = true)，Swagger 不生成，已排除。");

        // 同一身份（方法 + 路径）出现多个动作时如实列出，交给后续检查阻断。
        foreach (var group in result.GroupBy(e => e.Info.Key).Where(g => g.Count() > 1))
        {
            foreach (var item in group)
            {
                item.Info.Notes.Add($"源码中有 {group.Count()} 个动作映射到同一接口身份，需人工确认。");
            }
        }
        return result;
    }

    private EndpointInfo BuildInfo(INamedTypeSymbol type, IMethodSymbol method, MethodDeclarationSyntax syntax, string verb, string path, string template, string actionName, string controllerName, bool isApiController)
    {
        var (file, line) = SymbolUtil.Location(method, workspace.WorkspaceRoot);
        var routeParameters = Regex.Matches(path, @"\{([^}]+)\}").Select(m => m.Groups[1].Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var info = new EndpointInfo
        {
            Key = verb + " " + path,
            HttpMethod = verb,
            Path = path,
            RouteTemplate = template,
            Controller = controllerName,
            Action = actionName,
            MethodSymbolId = SymbolUtil.DocId(method),
            Signature = SymbolUtil.Short(method),
            File = file,
            Line = line,
            DeclaredReturnType = SymbolUtil.TypeName(method.ReturnType),
        };

        var inner = SymbolUtil.UnwrapTask(method.ReturnType, out _);
        if (inner is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ActionResult<TValue>")
        {
            inner = named.TypeArguments[0];
        }
        if (inner is not null && !IsUntypedResult(inner))
        {
            info.EffectiveReturnType = SymbolUtil.TypeName(inner);
            info.EffectiveReturnTypeId = SymbolUtil.DocId(inner);
        }

        foreach (var attribute in method.GetAttributes())
        {
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute"))
            {
                var typeArg = attribute.ConstructorArguments.FirstOrDefault(a => a.Kind == TypedConstantKind.Type).Value as ITypeSymbol
                    ?? attribute.AttributeClass?.TypeArguments.FirstOrDefault();
                if (typeArg is not null) info.ProducesTypes.Add(SymbolUtil.TypeName(typeArg));
            }
        }

        foreach (var parameter in method.Parameters)
        {
            info.Parameters.Add(Bind(parameter, routeParameters, isApiController));
        }
        return info;
    }

    private static bool IsUntypedResult(ITypeSymbol type)
    {
        var name = type.ToDisplayString();
        return name is "Microsoft.AspNetCore.Mvc.IActionResult" or "Microsoft.AspNetCore.Mvc.ActionResult" or "Microsoft.AspNetCore.Http.IResult"
            or "Microsoft.AspNetCore.Mvc.ObjectResult" or "Microsoft.AspNetCore.Mvc.JsonResult" or "System.Object";
    }

    private ParameterBinding Bind(IParameterSymbol parameter, HashSet<string> routeParameters, bool isApiController)
    {
        var type = parameter.Type;
        var binding = new ParameterBinding
        {
            Name = parameter.Name,
            ExternalName = parameter.Name,
            Type = SymbolUtil.TypeName(type),
            TypeId = SymbolUtil.DocId(SymbolUtil.UnwrapNullable(type)),
            HasDefault = parameter.HasExplicitDefaultValue,
            DefaultValue = parameter.HasExplicitDefaultValue ? FormatDefault(parameter.ExplicitDefaultValue) : null,
            IsNullable = SymbolUtil.IsNullableValue(type) || type.NullableAnnotation == NullableAnnotation.Annotated,
        };
        binding.IsComplex = !SymbolUtil.IsSimple(type) && !SymbolUtil.IsFormFile(type);

        string? Named(AttributeData a) => SymbolUtil.NamedString(a, "Name");
        foreach (var attribute in parameter.GetAttributes())
        {
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromBodyAttribute")) { binding.Source = "body"; return binding; }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromQueryAttribute")) { binding.Source = "query"; binding.ExternalName = Named(attribute) ?? parameter.Name; return binding; }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromRouteAttribute")) { binding.Source = "path"; binding.ExternalName = Named(attribute) ?? parameter.Name; return binding; }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromHeaderAttribute")) { binding.Source = "header"; binding.ExternalName = Named(attribute) ?? parameter.Name; return binding; }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromFormAttribute")) { binding.Source = "form"; binding.ExternalName = Named(attribute) ?? parameter.Name; return binding; }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.FromServicesAttribute") || SymbolUtil.Is(attribute, "Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"))
            {
                binding.Source = "services";
                return binding;
            }
        }

        binding.Inferred = true;
        if (SymbolUtil.IsSpecialParameter(type))
        {
            binding.Source = "special";
            return binding;
        }
        if (SymbolUtil.IsFormFile(type))
        {
            binding.Source = "form";
            return binding;
        }
        if (routeParameters.Contains(parameter.Name))
        {
            binding.Source = "path";
            return binding;
        }
        if (binding.IsComplex)
        {
            if (type.TypeKind == TypeKind.Interface)
            {
                // .NET 7+ 会把已注册到 DI 的类型推断为服务；接口类型参数大概率是服务注入，无法确认注册情况。
                binding.Source = "services";
                return binding;
            }
            binding.Source = isApiController ? "body" : "query";
            return binding;
        }
        binding.Source = "query";
        return binding;
    }

    private static string? FormatDefault(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static IEnumerable<INamedTypeSymbol> EnumerateRootTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            yield return type;
        }
        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateRootTypes(child)) yield return type;
        }
    }

    private static bool IsController(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic || type.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) return false;
        if (type.IsGenericType && type.TypeArguments.Any(t => t.TypeKind == TypeKind.TypeParameter)) return false;
        if (InheritedAttribute(type, "Microsoft.AspNetCore.Mvc.NonControllerAttribute") is not null) return false;
        if (InheritedAttribute(type, "Microsoft.AspNetCore.Mvc.ControllerAttribute") is not null) return true;
        if (InheritedAttribute(type, "Microsoft.AspNetCore.Mvc.ApiControllerAttribute") is not null) return true;
        for (var b = type.BaseType; b is not null; b = b.BaseType)
        {
            if (b.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ControllerBase") return true;
        }
        return type.Name.EndsWith("Controller", StringComparison.Ordinal);
    }

    private static AttributeData? InheritedAttribute(INamedTypeSymbol type, string fullName)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var attribute = SymbolUtil.Find(t, fullName);
            if (attribute is not null) return attribute;
        }
        return null;
    }

    private static bool IsIgnoredByApiExplorer(ISymbol symbol)
    {
        var attribute = SymbolUtil.Find(symbol, "Microsoft.AspNetCore.Mvc.ApiExplorerSettingsAttribute");
        return attribute is not null && SymbolUtil.NamedValue(attribute, "IgnoreApi") is true;
    }

    private static IEnumerable<IMethodSymbol> ActionMethods(INamedTypeSymbol type)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var t = type; t is not null; t = t.BaseType)
        {
            var name = t.ToDisplayString();
            if (name is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller" || t.SpecialType == SpecialType.System_Object) break;
            foreach (var method in t.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.IsAbstract || method.IsGenericMethod) continue;
                if (method.DeclaredAccessibility != Microsoft.CodeAnalysis.Accessibility.Public) continue;
                if (SymbolUtil.Has(method, "Microsoft.AspNetCore.Mvc.NonActionAttribute")) continue;
                if (method.Name == "Dispose" && method.Parameters.Length == 0) continue;
                // 派生类重写的方法只取最派生的一次。
                var signature = method.Name + "(" + string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString())) + ")";
                if (!seen.Add(signature)) continue;
                yield return method;
            }
        }
    }

    private string ActionName(IMethodSymbol method)
    {
        var attribute = SymbolUtil.Find(method, "Microsoft.AspNetCore.Mvc.ActionNameAttribute");
        if (attribute is not null && SymbolUtil.StringArg(attribute) is { Length: > 0 } explicitName) return explicitName;
        var name = method.Name;
        if (profile.Analysis.SuppressAsyncSuffixInActionNames && name.EndsWith("Async", StringComparison.Ordinal) && name.Length > 5)
        {
            name = name[..^5];
        }
        return name;
    }

    private static string ControllerName(INamedTypeSymbol type)
    {
        var attribute = SymbolUtil.Find(type, "Microsoft.AspNetCore.Mvc.ControllerNameAttribute");
        if (attribute is not null && SymbolUtil.StringArg(attribute) is { Length: > 0 } explicitName) return explicitName;
        return type.Name.EndsWith("Controller", StringComparison.Ordinal) && type.Name.Length > 10 ? type.Name[..^10] : type.Name;
    }

    private static List<string> ControllerRoutes(INamedTypeSymbol type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            var routes = t.GetAttributes()
                .Where(a => SymbolUtil.Is(a, "Microsoft.AspNetCore.Mvc.RouteAttribute"))
                .Select(a => SymbolUtil.StringArg(a) ?? "")
                .ToList();
            if (routes.Count > 0) return routes;
        }
        return new List<string>();
    }

    /// <summary>
    /// 动作的路由与方法：带模板的 HttpX 只对自身方法生效；[Route] 使用无模板 HttpX 声明的方法集合。
    /// </summary>
    private static List<(string? Template, List<string> Verbs)> ActionRoutes(IMethodSymbol method)
    {
        var result = new List<(string?, List<string>)>();
        var templateLessVerbs = new List<string>();
        foreach (var attribute in method.GetAttributes())
        {
            var cls = attribute.AttributeClass?.ToDisplayString() ?? "";
            var verb = HttpAttributes.FirstOrDefault(p => SymbolUtil.Is(attribute, p.Key)).Value;
            if (verb is not null)
            {
                var template = SymbolUtil.StringArg(attribute);
                if (template is null) templateLessVerbs.Add(verb);
                else result.Add((template, [verb]));
                continue;
            }
            if (SymbolUtil.Is(attribute, "Microsoft.AspNetCore.Mvc.AcceptVerbsAttribute"))
            {
                var verbs = new List<string>();
                foreach (var arg in attribute.ConstructorArguments)
                {
                    if (arg.Kind == TypedConstantKind.Array) verbs.AddRange(arg.Values.Select(v => (v.Value as string ?? "").ToUpperInvariant()).Where(v => v.Length > 0));
                    else if (arg.Value is string s) verbs.Add(s.ToUpperInvariant());
                }
                var route = SymbolUtil.NamedString(attribute, "Route");
                if (route is null) templateLessVerbs.AddRange(verbs);
                else result.Add((route, verbs));
            }
        }
        var routeAttributes = method.GetAttributes().Where(a => SymbolUtil.Is(a, "Microsoft.AspNetCore.Mvc.RouteAttribute")).Select(a => SymbolUtil.StringArg(a) ?? "").ToList();
        foreach (var template in routeAttributes)
        {
            result.Add((template, templateLessVerbs.Distinct().ToList()));
        }
        if (result.Count == 0)
        {
            result.Add((null, templateLessVerbs.Distinct().ToList()));
        }
        return result;
    }

    private static List<string> Combine(List<string> controllerRoutes, string? actionTemplate)
    {
        if (actionTemplate is not null && (actionTemplate.StartsWith('/') || actionTemplate.StartsWith("~/", StringComparison.Ordinal)))
        {
            return [actionTemplate.TrimStart('~')];
        }
        if (controllerRoutes.Count == 0)
        {
            return actionTemplate is null ? new List<string>() : [actionTemplate];
        }
        return controllerRoutes
            .Select(c => actionTemplate is null || actionTemplate.Length == 0 ? c : c.TrimEnd('/') + "/" + actionTemplate)
            .ToList();
    }

    private static string ReplaceTokens(string template, string controller, string action, string? area)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            if (c == '[' && i + 1 < template.Length && template[i + 1] == '[')
            {
                sb.Append('[');
                i++;
                continue;
            }
            if (c == ']' && i + 1 < template.Length && template[i + 1] == ']')
            {
                sb.Append(']');
                i++;
                continue;
            }
            if (c == '[')
            {
                var end = template.IndexOf(']', i);
                if (end > i)
                {
                    var token = template[(i + 1)..end].ToLowerInvariant();
                    sb.Append(token switch
                    {
                        "controller" => controller,
                        "action" => action,
                        "area" => area ?? "",
                        _ => template[i..(end + 1)],
                    });
                    i = end;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 路由模板转 OpenAPI 路径：去掉约束、默认值、可选标记与 catch-all 前缀，与 ApiExplorer 的 RelativePath 一致。
    /// </summary>
    public static string ToOpenApiPath(string template)
    {
        var path = template.Trim().TrimStart('~').Trim('/');
        path = Regex.Replace(path, @"\{\*{0,2}([A-Za-z_][A-Za-z0-9_]*)[^}]*\}", "{$1}");
        return "/" + path;
    }
}
