using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Source;

/// <summary>
/// 代码证据集合。证据 ID 由类别、所属符号与片段内容计算，行号移动不改变 ID，内容变化则 ID 变化，
/// 据此判断已审阅草稿的依据是否过期。
/// </summary>
public sealed class EvidenceSink(LoadedWorkspace workspace)
{
    private readonly Dictionary<string, CodeEvidence> _items = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CodeEvidence> Items => _items.Values;

    public CodeEvidence Add(string kind, string ownerSymbolId, SyntaxNode node, string? summary, IEnumerable<string> related, string? snippetOverride = null)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        var raw = snippetOverride ?? node.ToString();
        var snippet = TextUtil.Truncate(Redactor.Redact(Dedent(raw.Trim())), 1500);
        var hash = Canonical.Sha256Hex(snippet.Replace("\r\n", "\n"));
        var id = "E" + Ids.ShortHash(kind + "|" + ownerSymbolId + "|" + hash, 10).ToUpperInvariant();
        if (_items.TryGetValue(id, out var existing))
        {
            foreach (var r in related)
            {
                if (!existing.RelatedSymbolIds.Contains(r)) existing.RelatedSymbolIds.Add(r);
            }
            return existing;
        }
        var evidence = new CodeEvidence
        {
            Id = id,
            Kind = kind,
            OwnerSymbolId = ownerSymbolId,
            RelatedSymbolIds = related.Distinct(StringComparer.Ordinal).ToList(),
            File = SymbolUtil.Relative(workspace.WorkspaceRoot, node.SyntaxTree.FilePath),
            StartLine = span.StartLinePosition.Line + 1,
            EndLine = span.EndLinePosition.Line + 1,
            Snippet = snippet,
            SnippetHash = hash,
            Summary = summary is null ? null : TextUtil.Truncate(summary, 300),
        };
        _items[id] = evidence;
        return evidence;
    }

    private static string Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= 1) return text;
        var indent = lines.Skip(1).Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join("\n", lines.Select((l, i) => i == 0 || l.Length < indent ? l.TrimEnd() : l[indent..].TrimEnd()));
    }
}

/// <summary>
/// 从接口方法出发，在预算内沿调用链读取实现，收集校验、守卫条件、查询过滤、字段映射与返回分支等证据。
/// 多实现、外部程序集、超出预算等无法唯一解析的调用如实记录为缺口，不按同名猜测实现。
/// </summary>
public sealed class EvidenceCollector(LoadedWorkspace workspace, ProjectProfile profile, EvidenceSink sink)
{
    private static readonly HashSet<string> FilterMethods = new(StringComparer.Ordinal)
    {
        "Where", "First", "FirstOrDefault", "Single", "SingleOrDefault", "Any", "All", "Count", "LongCount",
        "FirstAsync", "FirstOrDefaultAsync", "SingleAsync", "SingleOrDefaultAsync", "AnyAsync", "AllAsync", "CountAsync", "LongCountAsync",
    };

    private static readonly HashSet<string> ThrowHelpers = new(StringComparer.Ordinal)
    {
        "ThrowIfNull", "ThrowIfNullOrEmpty", "ThrowIfNullOrWhiteSpace", "ThrowIfNegative", "ThrowIfNegativeOrZero", "ThrowIfZero",
        "ThrowIfGreaterThan", "ThrowIfGreaterThanOrEqual", "ThrowIfLessThan", "ThrowIfLessThanOrEqual", "ThrowIfEqual", "ThrowIfNotEqual",
    };

    private Dictionary<string, List<INamedTypeSymbol>>? _validators;

    public EndpointCoverage Collect(DiscoveredEndpoint endpoint, IReadOnlyCollection<INamedTypeSymbol> requestTypes, IReadOnlyCollection<INamedTypeSymbol> responseTypes, CancellationToken ct)
    {
        var coverage = new EndpointCoverage { EndpointKey = endpoint.Info.Key };
        var method = endpoint.Method;
        var methodId = SymbolUtil.DocId(method);
        var ids = new List<string>();

        // 路由、绑定与方法签名。
        var signatureText = string.Join("\n", endpoint.Syntax.AttributeLists.Select(a => a.ToString()))
            + "\n" + endpoint.Syntax.Modifiers + " " + endpoint.Syntax.ReturnType + " " + endpoint.Syntax.Identifier + endpoint.Syntax.TypeParameterList + endpoint.Syntax.ParameterList;
        ids.Add(sink.Add("route", methodId, endpoint.Syntax, $"{endpoint.Info.HttpMethod} {endpoint.Info.Path}", [methodId], signatureText).Id);
        if (endpoint.Controller.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is ClassDeclarationSyntax controllerSyntax && controllerSyntax.AttributeLists.Count > 0)
        {
            var controllerText = string.Join("\n", controllerSyntax.AttributeLists.Select(a => a.ToString())) + "\n" + controllerSyntax.Keyword + " " + controllerSyntax.Identifier;
            ids.Add(sink.Add("route", SymbolUtil.DocId(endpoint.Controller), controllerSyntax, "控制器级路由与鉴权", [methodId], controllerText).Id);
        }

        foreach (var parameter in method.Parameters)
        {
            var parameterId = SymbolUtil.ParameterId(method, parameter.Name);
            if (parameter.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct) is not ParameterSyntax parameterSyntax) continue;
            if (parameterSyntax.AttributeLists.Count > 0)
            {
                ids.Add(sink.Add(HasValidation(parameter) ? "validation" : "binding", methodId, parameterSyntax, $"参数 {parameter.Name} 的特性", [parameterId]).Id);
            }
            if (parameter.HasExplicitDefaultValue)
            {
                ids.Add(sink.Add("default", methodId, parameterSyntax, $"参数 {parameter.Name} 未传时取默认值 {parameterSyntax.Default?.Value}", [parameterId]).Id);
            }
        }

        foreach (var type in requestTypes.Concat(responseTypes).Distinct<INamedTypeSymbol>(SymbolEqualityComparer.Default))
        {
            ids.AddRange(TypeEvidence(type, requestTypes.Contains(type, SymbolEqualityComparer.Default)));
        }

        // 调用链。
        var interesting = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var parameter in method.Parameters)
        {
            interesting[SymbolUtil.ParameterId(method, parameter.Name)] = [SymbolUtil.ParameterId(method, parameter.Name)];
        }
        var responseTypeIds = responseTypes.Select(SymbolUtil.DocId).ToHashSet(StringComparer.Ordinal);
        var queue = new Queue<(IMethodSymbol Method, int Depth, Dictionary<string, HashSet<string>> ParameterMap)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var external = new HashSet<string>(StringComparer.Ordinal);
        queue.Enqueue((method, 0, interesting));
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (current, depth, parameterMap) = queue.Dequeue();
            var currentId = SymbolUtil.DocId(current);
            if (!visited.Add(currentId)) continue;
            if (visited.Count > profile.Analysis.MaxMethodsPerEndpoint)
            {
                coverage.BudgetExceeded = true;
                coverage.Unresolved.Add(new UnresolvedCall { Expression = SymbolUtil.Short(current), Reason = "超出单接口分析预算，未读取" });
                continue;
            }
            var body = BodyOf(current, ct);
            if (body is null) continue;
            coverage.MethodsRead.Add(SymbolUtil.Short(current));
            var model = workspace.Model(body.SyntaxTree);

            ids.AddRange(BodyEvidence(current, currentId, body, model, parameterMap, responseTypeIds, depth == 0));

            foreach (var invocation in body.DescendantNodes().Where(n => n is InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
            {
                var info = model.GetSymbolInfo(invocation, ct);
                var callee = info.Symbol as IMethodSymbol;
                if (callee is null)
                {
                    if (info.CandidateSymbols.Length > 0 && invocation is InvocationExpressionSyntax)
                    {
                        coverage.Unresolved.Add(Unresolved(invocation, "重载无法唯一确定", info.CandidateSymbols.Select(SymbolUtil.Short)));
                    }
                    continue;
                }
                if (callee.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction or MethodKind.Constructor) continue;
                callee = callee.ReducedFrom ?? callee;
                if (!SymbolUtil.IsInSource(callee) && !callee.IsAbstract && callee.ContainingType?.TypeKind != TypeKind.Interface)
                {
                    if (external.Count < 40) external.Add(callee.ContainingType?.Name + "." + callee.Name);
                    continue;
                }
                if (depth >= profile.Analysis.MaxTraceDepth)
                {
                    coverage.Unresolved.Add(Unresolved(invocation, $"超过调用深度 {profile.Analysis.MaxTraceDepth}，未继续读取", []));
                    continue;
                }
                if (depth == 0 && invocation is InvocationExpressionSyntax call)
                {
                    ids.Add(sink.Add("call", currentId, call, "接口方法调用 " + SymbolUtil.Short(callee), Related(call, model, parameterMap)).Id);
                }

                var targets = ResolveTargets(callee, out var reason, out var candidates);
                if (targets.Count == 0)
                {
                    coverage.Unresolved.Add(Unresolved(invocation, reason ?? "未找到实现", candidates));
                    continue;
                }
                if (reason is not null) coverage.Unresolved.Add(Unresolved(invocation, reason, candidates));
                var arguments = invocation switch
                {
                    InvocationExpressionSyntax i => i.ArgumentList.Arguments,
                    _ => default,
                };
                foreach (var target in targets)
                {
                    queue.Enqueue((target, depth + 1, MapArguments(target, arguments, model, parameterMap)));
                }
            }
        }
        coverage.ExternalCalls = external.OrderBy(x => x, StringComparer.Ordinal).ToList();
        coverage.EvidenceIds = ids.Distinct(StringComparer.Ordinal).ToList();
        return coverage;
    }

    private static UnresolvedCall Unresolved(SyntaxNode node, string reason, IEnumerable<string> candidates)
    {
        var span = node.SyntaxTree.GetLineSpan(node.Span);
        return new UnresolvedCall
        {
            Expression = TextUtil.Truncate(Regex.Replace(node.ToString(), @"\s+", " "), 200),
            Reason = reason,
            File = node.SyntaxTree.FilePath.Length > 0 ? Path.GetFileName(node.SyntaxTree.FilePath) : "",
            Line = span.StartLinePosition.Line + 1,
            Candidates = candidates.Take(8).ToList(),
        };
    }

    private SyntaxNode? BodyOf(IMethodSymbol method, CancellationToken ct)
    {
        var target = method.PartialImplementationPart ?? method;
        var syntax = target.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(ct);
        if (syntax is null || !workspace.TreeOwner.ContainsKey(syntax.SyntaxTree)) return null;
        return syntax switch
        {
            MethodDeclarationSyntax m => (SyntaxNode?)m.Body ?? m.ExpressionBody,
            AccessorDeclarationSyntax a => (SyntaxNode?)a.Body ?? a.ExpressionBody,
            _ => null,
        };
    }

    /// <summary>
    /// 调用目标：普通方法即自身；接口/抽象成员只有在编译范围内唯一实现时才继续读取（DI 注册未验证）。
    /// </summary>
    private List<IMethodSymbol> ResolveTargets(IMethodSymbol callee, out string? note, out List<string> candidates)
    {
        note = null;
        candidates = new List<string>();
        var root = workspace.ResolveInRoot(SymbolUtil.DocId(callee)) as IMethodSymbol ?? callee;
        var containing = root.ContainingType;
        if (containing is null) return new List<IMethodSymbol>();
        if (containing.TypeKind == TypeKind.Interface)
        {
            var implementations = new List<IMethodSymbol>();
            foreach (var type in workspace.Index.ImplementationsOf(containing))
            {
                foreach (var iface in type.AllInterfaces.Where(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, containing.OriginalDefinition)))
                {
                    foreach (var member in iface.GetMembers(root.Name).OfType<IMethodSymbol>().Where(m => SymbolEqualityComparer.Default.Equals(m.OriginalDefinition, root.OriginalDefinition)))
                    {
                        if (type.FindImplementationForInterfaceMember(member) is IMethodSymbol impl && !implementations.Contains(impl, SymbolEqualityComparer.Default))
                        {
                            implementations.Add(impl);
                        }
                    }
                }
            }
            candidates = implementations.Select(i => i.ContainingType.Name).ToList();
            if (implementations.Count == 1)
            {
                note = null;
                return implementations;
            }
            note = implementations.Count == 0 ? $"接口 {containing.Name}.{root.Name} 在编译范围内没有实现" : $"接口 {containing.Name}.{root.Name} 有多个实现，无法确定 DI 绑定";
            return new List<IMethodSymbol>();
        }
        if (root.IsAbstract)
        {
            var overrides = workspace.Index.DerivedOf(containing)
                .SelectMany(t => t.GetMembers(root.Name).OfType<IMethodSymbol>())
                .Where(m => m.IsOverride && SymbolEqualityComparer.Default.Equals(m.OverriddenMethod?.OriginalDefinition, root.OriginalDefinition))
                .ToList();
            candidates = overrides.Select(o => o.ContainingType.Name).ToList();
            if (overrides.Count == 1) return overrides;
            note = overrides.Count == 0 ? $"抽象方法 {containing.Name}.{root.Name} 没有找到重写" : $"抽象方法 {containing.Name}.{root.Name} 有多个重写，无法确定运行时类型";
            return new List<IMethodSymbol>();
        }
        if (root.IsVirtual && workspace.Index.DerivedOf(containing).Any(t => t.GetMembers(root.Name).OfType<IMethodSymbol>().Any(m => m.IsOverride)))
        {
            note = $"虚方法 {containing.Name}.{root.Name} 存在重写，只读取了基类实现";
        }
        return SymbolUtil.IsInSource(root) ? [root] : new List<IMethodSymbol>();
    }

    private Dictionary<string, HashSet<string>> MapArguments(IMethodSymbol target, SeparatedSyntaxList<ArgumentSyntax> arguments, SemanticModel model, Dictionary<string, HashSet<string>> callerMap)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (arguments.Count == 0) return map;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            IParameterSymbol? parameter = null;
            if (argument.NameColon is not null)
            {
                parameter = target.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText);
            }
            else if (i < target.Parameters.Length)
            {
                parameter = target.Parameters[i];
            }
            if (parameter is null) continue;
            var origins = Related(argument.Expression, model, callerMap).ToHashSet(StringComparer.Ordinal);
            if (origins.Count > 0) map[SymbolUtil.ParameterId(target, parameter.Name)] = origins;
        }
        return map;
    }

    /// <summary>
    /// 节点中引用的、对文档有意义的符号：当前方法参数（映射回接口参数）和源码中的属性。
    /// </summary>
    private static List<string> Related(SyntaxNode node, SemanticModel model, Dictionary<string, HashSet<string>> parameterMap)
    {
        var result = new List<string>();
        foreach (var identifier in node.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>().Take(60))
        {
            var symbol = model.GetSymbolInfo(identifier).Symbol;
            switch (symbol)
            {
                case IParameterSymbol parameter when parameter.ContainingSymbol is IMethodSymbol owner:
                    var id = SymbolUtil.ParameterId(owner, parameter.Name);
                    if (parameterMap.TryGetValue(id, out var origins)) result.AddRange(origins);
                    break;
                case IPropertySymbol property when SymbolUtil.IsInSource(property):
                    result.Add(SymbolUtil.DocId(property.OriginalDefinition));
                    break;
            }
            if (result.Count >= 20) break;
        }
        return result.Distinct(StringComparer.Ordinal).ToList();
    }

    private IEnumerable<string> BodyEvidence(IMethodSymbol method, string methodId, SyntaxNode body, SemanticModel model, Dictionary<string, HashSet<string>> parameterMap, HashSet<string> responseTypeIds, bool isEndpoint)
    {
        var ids = new List<string>();
        var guards = 0;
        foreach (var ifStatement in body.DescendantNodes().OfType<IfStatementSyntax>())
        {
            if (guards >= 15) break;
            if (!ExitsImmediately(ifStatement.Statement, out var exit)) continue;
            var related = Related(ifStatement.Condition, model, parameterMap);
            if (related.Count == 0 && !isEndpoint) continue;
            var snippet = $"if ({ifStatement.Condition}) {exit}";
            ids.Add(sink.Add("guard", methodId, ifStatement, $"条件 {ifStatement.Condition} 成立时{(exit.StartsWith("throw", StringComparison.Ordinal) ? "抛出异常" : "提前返回")}", related, snippet).Id);
            guards++;
        }

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var name = invocation.Expression switch
            {
                MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
                IdentifierNameSyntax i => i.Identifier.ValueText,
                _ => "",
            };
            if (ThrowHelpers.Contains(name))
            {
                var related = Related(invocation.ArgumentList, model, parameterMap);
                ids.Add(sink.Add("guard", methodId, invocation, $"参数检查 {name}", related).Id);
            }
            else if (FilterMethods.Contains(name) && invocation.ArgumentList.Arguments.Any(a => a.Expression is LambdaExpressionSyntax))
            {
                var related = Related(invocation.ArgumentList, model, parameterMap);
                if (related.Count == 0) continue;
                ids.Add(sink.Add("queryFilter", methodId, invocation, $"查询条件 {name}", related, invocation.ArgumentList.ToString()).Id);
            }
        }

        foreach (var throwExpression in body.DescendantNodes().OfType<ThrowExpressionSyntax>())
        {
            var statement = throwExpression.FirstAncestorOrSelf<StatementSyntax>();
            if (statement is null) continue;
            ids.Add(sink.Add("guard", methodId, statement, "为空时抛出异常", Related(statement, model, parameterMap)).Id);
        }

        var returns = 0;
        foreach (var returnStatement in body.DescendantNodes(n => n is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)).OfType<ReturnStatementSyntax>())
        {
            if (returns >= 10 || returnStatement.Expression is null) break;
            var type = model.GetTypeInfo(returnStatement.Expression).Type;
            ids.Add(sink.Add("return", methodId, returnStatement, "返回 " + (type is null ? "未知类型" : SymbolUtil.TypeName(type)), Related(returnStatement.Expression, model, parameterMap)).Id);
            returns++;
        }

        var mappings = 0;
        foreach (var creation in body.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
        {
            if (mappings >= 15) break;
            if (creation.Initializer is null) continue;
            if (model.GetTypeInfo(creation).Type is not INamedTypeSymbol created || !responseTypeIds.Contains(SymbolUtil.DocId(created))) continue;
            var assigned = new List<string>();
            foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
            {
                if (model.GetSymbolInfo(assignment.Left).Symbol is IPropertySymbol property) assigned.Add(SymbolUtil.DocId(property.OriginalDefinition));
                assigned.AddRange(Related(assignment.Right, model, parameterMap));
            }
            ids.Add(sink.Add("mapping", methodId, creation, $"构造 {created.Name} 并赋值字段", assigned).Id);
            mappings++;
        }
        foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (mappings >= 25) break;
            if (assignment.Parent is InitializerExpressionSyntax) continue;
            if (assignment.Left is not MemberAccessExpressionSyntax) continue;
            if (model.GetSymbolInfo(assignment.Left).Symbol is not IPropertySymbol property) continue;
            if (!responseTypeIds.Contains(SymbolUtil.DocId(property.ContainingType.OriginalDefinition))) continue;
            var statement = (SyntaxNode?)assignment.FirstAncestorOrSelf<ExpressionStatementSyntax>() ?? assignment;
            var related = new List<string> { SymbolUtil.DocId(property.OriginalDefinition) };
            related.AddRange(Related(assignment.Right, model, parameterMap));
            ids.Add(sink.Add("mapping", methodId, statement, $"为 {property.ContainingType.Name}.{property.Name} 赋值", related).Id);
            mappings++;
        }
        return ids;
    }

    private static bool ExitsImmediately(StatementSyntax statement, out string exitText)
    {
        exitText = "";
        switch (statement)
        {
            case ReturnStatementSyntax or ThrowStatementSyntax:
                exitText = Regex.Replace(statement.ToString(), @"\s+", " ");
                return true;
            case BlockSyntax block when block.Statements.Count is > 0 and <= 4 && block.Statements[^1] is ReturnStatementSyntax or ThrowStatementSyntax:
                exitText = Regex.Replace(block.Statements[^1].ToString(), @"\s+", " ");
                return true;
            default:
                return false;
        }
    }

    private static bool HasValidation(ISymbol symbol) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ContainingNamespace?.ToDisplayString() == "System.ComponentModel.DataAnnotations"
            || a.AttributeClass?.Name is "BindRequiredAttribute" or "BindRequired");

    private IEnumerable<string> TypeEvidence(INamedTypeSymbol type, bool isRequest)
    {
        var ids = new List<string>();
        var typeId = SymbolUtil.DocId(type.OriginalDefinition);
        if (type.TypeKind == TypeKind.Enum)
        {
            foreach (var member in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
            {
                if (member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not EnumMemberDeclarationSyntax syntax) continue;
                ids.Add(sink.Add("enumMember", typeId, syntax, $"{type.Name}.{member.Name} = {member.ConstantValue}", [SymbolUtil.DocId(member), typeId]).Id);
            }
            return ids;
        }
        foreach (var property in SymbolUtil.SerializableProperties(type))
        {
            var propertyId = SymbolUtil.DocId(property.OriginalDefinition);
            var syntax = property.OriginalDefinition.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (syntax is null) continue;
            if (HasValidation(property) || property.IsRequired)
            {
                var text = syntax is PropertyDeclarationSyntax p
                    ? string.Join("\n", p.AttributeLists.Select(a => a.ToString())) + "\n" + p.Modifiers + " " + p.Type + " " + p.Identifier
                    : syntax.ToString();
                ids.Add(sink.Add("validation", typeId, syntax, $"{type.Name}.{property.Name} 的校验特性", [propertyId], text).Id);
            }
            if (syntax is PropertyDeclarationSyntax { Initializer: not null } initialized)
            {
                ids.Add(sink.Add("default", typeId, initialized, $"{type.Name}.{property.Name} 初始值 {initialized.Initializer.Value}", [propertyId],
                    $"{initialized.Type} {initialized.Identifier} {initialized.AccessorList} = {initialized.Initializer.Value};").Id);
            }
        }
        if (isRequest)
        {
            foreach (var validator in ValidatorsFor(type))
            {
                foreach (var syntaxRef in validator.DeclaringSyntaxReferences)
                {
                    if (syntaxRef.GetSyntax() is not ClassDeclarationSyntax cls || !workspace.TreeOwner.ContainsKey(cls.SyntaxTree)) continue;
                    var model = workspace.Model(cls.SyntaxTree);
                    foreach (var statement in cls.DescendantNodes().OfType<ExpressionStatementSyntax>().Where(s => s.ToString().Contains("RuleFor", StringComparison.Ordinal)).Take(30))
                    {
                        var related = statement.DescendantNodes().OfType<IdentifierNameSyntax>()
                            .Select(i => model.GetSymbolInfo(i).Symbol)
                            .OfType<IPropertySymbol>()
                            .Where(SymbolUtil.IsInSource)
                            .Select(p => SymbolUtil.DocId(p.OriginalDefinition))
                            .Distinct()
                            .ToList();
                        ids.Add(sink.Add("validation", typeId, statement, $"{validator.Name} 中的校验规则", related).Id);
                    }
                }
            }
        }
        return ids;
    }

    private IEnumerable<INamedTypeSymbol> ValidatorsFor(INamedTypeSymbol type)
    {
        _validators ??= BuildValidatorIndex();
        return _validators.TryGetValue(SymbolUtil.DocId(type.OriginalDefinition), out var list) ? list : Enumerable.Empty<INamedTypeSymbol>();
    }

    private Dictionary<string, List<INamedTypeSymbol>> BuildValidatorIndex()
    {
        var index = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (var type in workspace.SourceTypes())
        {
            for (var b = type.BaseType; b is not null; b = b.BaseType)
            {
                if (b.Name == "AbstractValidator" && b.TypeArguments.Length == 1 && b.TypeArguments[0] is INamedTypeSymbol validated)
                {
                    var key = SymbolUtil.DocId(validated.OriginalDefinition);
                    if (!index.TryGetValue(key, out var list)) index[key] = list = new List<INamedTypeSymbol>();
                    list.Add(type);
                    break;
                }
            }
        }
        return index;
    }
}
