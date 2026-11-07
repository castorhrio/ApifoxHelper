using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Source;

/// <summary>
/// 追踪通用响应封装中 data 实际装入的类型：沿 return 表达式、封装构造函数/初始化器、工厂方法与局部变量赋值，
/// 用语义模型取表达式的静态类型。每个 return 是一个分支；多个形状、object、动态或匿名类型列为未解析点。
/// </summary>
public sealed class ResponseTracer(LoadedWorkspace workspace, ProjectProfile profile, EvidenceSink sink)
{
    private const int MaxFactoryDepth = 2;

    private static readonly Dictionary<string, int> HelperStatus = new(StringComparer.Ordinal)
    {
        ["Ok"] = 200, ["Created"] = 201, ["CreatedAtAction"] = 201, ["CreatedAtRoute"] = 201, ["Accepted"] = 202,
        ["AcceptedAtAction"] = 202, ["AcceptedAtRoute"] = 202, ["NoContent"] = 204, ["BadRequest"] = 400, ["Unauthorized"] = 401,
        ["Forbid"] = 403, ["NotFound"] = 404, ["Conflict"] = 409, ["UnprocessableEntity"] = 422, ["ValidationProblem"] = 400, ["Problem"] = 500,
    };

    private static readonly Dictionary<string, int> ResultTypeStatus = new(StringComparer.Ordinal)
    {
        ["OkObjectResult"] = 200, ["CreatedResult"] = 201, ["CreatedAtActionResult"] = 201, ["AcceptedResult"] = 202,
        ["BadRequestObjectResult"] = 400, ["UnauthorizedObjectResult"] = 401, ["NotFoundObjectResult"] = 404,
        ["ConflictObjectResult"] = 409, ["UnprocessableEntityObjectResult"] = 422, ["ObjectResult"] = 200, ["JsonResult"] = 200,
    };

    public static ResponseWrapperRule? FindWrapperRule(ProjectProfile profile, ITypeSymbol type)
    {
        var named = SymbolUtil.UnwrapNullable(type);
        var full = named.ToDisplayString();
        return profile.ResponseWrappers.FirstOrDefault(r =>
            string.Equals(r.TypeName, named.Name, StringComparison.Ordinal) || string.Equals(r.TypeName, full, StringComparison.Ordinal)
            || (named is INamedTypeSymbol g && g.IsGenericType && string.Equals(r.TypeName, g.OriginalDefinition.Name, StringComparison.Ordinal)));
    }

    private sealed class BranchData
    {
        public int? HttpStatus { get; set; }
        public string? BusinessStatus { get; set; }
        public ExpressionSyntax? DataExpression { get; set; }
        public SemanticModel? DataModel { get; set; }
        public bool NoData { get; set; }
        public bool Unknown { get; set; }
        public string? Note { get; set; }
        public ITypeSymbol? DirectBodyType { get; set; }
        public ITypeSymbol? WrapperType { get; set; }
    }

    public ResponseContract? Trace(DiscoveredEndpoint endpoint, out List<ITypeSymbol> dataTypes, CancellationToken ct)
    {
        dataTypes = new List<ITypeSymbol>();
        var method = endpoint.Method;
        var declared = SymbolUtil.UnwrapTask(method.ReturnType, out var isVoid);
        if (isVoid || declared is null) return null;
        if (declared is INamedTypeSymbol actionResult && actionResult.OriginalDefinition.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ActionResult<TValue>")
        {
            declared = actionResult.TypeArguments[0];
        }
        var declaredWrapper = FindWrapperRule(profile, declared);
        var untyped = declared.ToDisplayString() is "Microsoft.AspNetCore.Mvc.IActionResult" or "Microsoft.AspNetCore.Mvc.ActionResult"
            or "Microsoft.AspNetCore.Http.IResult" or "Microsoft.AspNetCore.Mvc.ObjectResult" or "System.Object";
        if (declaredWrapper is null && !untyped) return null;

        var model = workspace.Model(endpoint.Syntax.SyntaxTree);
        var returns = Returns(endpoint.Syntax);
        var branches = new List<ResponseBranch>();
        var involvedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { endpoint.Syntax.SyntaxTree.FilePath };
        ITypeSymbol? wrapperType = declaredWrapper is null ? null : declared;
        var methodId = SymbolUtil.DocId(method);
        var index = 0;

        foreach (var (node, expression) in returns)
        {
            ct.ThrowIfCancellationRequested();
            var results = new List<BranchData>();
            Analyze(expression, model, new Dictionary<string, (ExpressionSyntax, SemanticModel)>(), 0, results, involvedFiles, node);
            var evidence = sink.Add("responseData", methodId, node, "响应分支返回 " + SymbolUtil.LineText(expression), [methodId]);
            foreach (var data in results)
            {
                index++;
                wrapperType ??= data.WrapperType;
                var branch = new ResponseBranch
                {
                    BranchKey = $"R{index}",
                    HttpStatus = data.HttpStatus,
                    BusinessStatus = data.BusinessStatus,
                    Condition = ConditionOf(node),
                    File = SymbolUtil.Relative(workspace.WorkspaceRoot, node.SyntaxTree.FilePath),
                    Line = node.SyntaxTree.GetLineSpan(node.Span).StartLinePosition.Line + 1,
                    EvidenceId = evidence.Id,
                    Note = data.Note,
                };
                ClassifyData(data, branch, dataTypes, involvedFiles);
                branches.Add(branch);
            }
        }

        if (wrapperType is null && branches.All(b => b.DataKind is "unknown" or "none" or "null")) return null;

        var rule = wrapperType is null ? null : FindWrapperRule(profile, wrapperType);
        var contract = new ResponseContract
        {
            EndpointKey = endpoint.Info.Key,
            WrapperType = wrapperType is null ? "" : SymbolUtil.TypeName(wrapperType),
            DataMember = rule?.DataMember ?? "",
            Branches = branches,
            EvidenceIds = branches.Select(b => b.EvidenceId).OfType<string>().Distinct().ToList(),
        };
        if (wrapperType is not null && rule is not null && wrapperType is INamedTypeSymbol wrapperNamed)
        {
            var dataProperty = SymbolUtil.SerializableProperties(wrapperNamed).FirstOrDefault(p => p.Name == rule.DataMember);
            contract.DataJsonName = dataProperty is null ? rule.DataMember : SymbolUtil.JsonName(dataProperty, profile.Serialization);
            if (dataProperty is null) contract.UnresolvedPoints.Add($"封装类型 {wrapperNamed.Name} 中没有名为 {rule.DataMember} 的公开属性。");
            involvedFiles.UnionWith(wrapperNamed.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath));
        }

        // 业务成功分支：HTTP 200（或封装直接返回）且 data 有具体类型。
        var success = branches.Where(b => (b.HttpStatus is null or 200) && b.DataKind is "type" or "anonymous").ToList();
        var shapes = success.Select(b => b.DataTypeId ?? b.DataType).Distinct(StringComparer.Ordinal).ToList();
        contract.DataNullable = branches.Any(b => b.DataKind is "null" or "none");
        foreach (var branch in branches.Where(b => b.DataKind == "unknown"))
        {
            contract.UnresolvedPoints.Add($"分支 {branch.BranchKey}（{branch.File}:{branch.Line}）：{branch.Note ?? "无法确定 data 类型"}");
        }
        if (success.Any(b => b.DataKind == "anonymous"))
        {
            contract.UnresolvedPoints.Add("存在匿名类型返回：字段由编译器推断，确认前需人工核对对外字段名。");
        }
        if (shapes.Count == 0)
        {
            contract.UnresolvedPoints.Add("没有找到返回具体 data 类型的成功分支。");
            contract.ReviewState = "blocked";
        }
        else if (shapes.Count > 1)
        {
            contract.UnresolvedPoints.Add("多个成功分支返回不同的 data 类型：" + string.Join("、", success.Select(b => b.DataType).Distinct()));
            contract.ReviewState = "blocked";
        }
        else
        {
            var chosen = success[0];
            contract.DataType = chosen.DataType;
            contract.DataTypeId = chosen.DataTypeId;
            var dataSymbol = dataTypes.FirstOrDefault(t => SymbolUtil.DocId(t) == chosen.DataTypeId || SymbolUtil.TypeName(t) == chosen.DataType);
            if (dataSymbol is not null)
            {
                var builder = new SchemaBuilder(profile);
                contract.ResponseSchema = BuildResponseSchema(builder, wrapperType as INamedTypeSymbol, rule, dataSymbol, contract.DataNullable, contract);
                foreach (var pair in builder.Components) contract.Components[pair.Key] = pair.Value;
                contract.UnresolvedPoints.AddRange(builder.Notes);
            }
        }

        var hashes = involvedFiles
            .Select(f => workspace.FilesByPath.TryGetValue(f, out var entry) ? entry.RelativePath + ":" + entry.Hash : f)
            .OrderBy(x => x, StringComparer.Ordinal);
        contract.SourceFingerprint = Canonical.Sha256Hex(string.Join("\n", hashes));
        return contract;
    }

    private JsonObject BuildResponseSchema(SchemaBuilder builder, INamedTypeSymbol? wrapper, ResponseWrapperRule? rule, ITypeSymbol dataType, bool dataNullable, ResponseContract contract)
    {
        if (wrapper is null || rule is null)
        {
            return builder.Build(dataType);
        }
        var schema = new JsonObject { ["type"] = "object" };
        var properties = new JsonObject();
        var required = new JsonArray();
        foreach (var property in SymbolUtil.SerializableProperties(wrapper))
        {
            var name = SymbolUtil.JsonName(property, profile.Serialization);
            JsonObject propertySchema;
            if (property.Name == rule.DataMember)
            {
                propertySchema = builder.Build(dataType, dataNullable);
            }
            else
            {
                var nullable = SymbolUtil.IsNullableValue(property.Type)
                    || (!property.Type.IsValueType && (!profile.Serialization.NonNullableReferenceTypes || property.NullableAnnotation != NullableAnnotation.NotAnnotated));
                propertySchema = builder.Build(property.Type, nullable && !SymbolUtil.IsNullableValue(property.Type));
            }
            if (DocText(property) is { } description)
            {
                // $ref 旁不能直接加说明，包一层 allOf；已有 allOf（可空引用）时直接附加。
                if (propertySchema.ContainsKey("$ref")) propertySchema = new JsonObject { ["allOf"] = new JsonArray(propertySchema), ["description"] = description };
                else propertySchema["description"] = description;
            }
            properties[name] = propertySchema;
            if (SymbolUtil.IsRequired(property)) required.Add(name);
        }
        schema["properties"] = properties;
        if (required.Count > 0) schema["required"] = required;
        schema["additionalProperties"] = false;
        if (DocText(wrapper) is { } wrapperDescription) schema["description"] = wrapperDescription;
        return schema;
    }

    private static string? DocText(ISymbol symbol)
    {
        var syntax = symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax is null ? null : DocComments.Read(syntax).TextOf("summary");
    }

    private void ClassifyData(BranchData data, ResponseBranch branch, List<ITypeSymbol> dataTypes, HashSet<string> involvedFiles)
    {
        if (data.Unknown)
        {
            branch.DataKind = "unknown";
            return;
        }
        if (data.NoData)
        {
            branch.DataKind = "none";
            return;
        }
        ITypeSymbol? type = data.DirectBodyType;
        if (type is null && data.DataExpression is not null && data.DataModel is not null)
        {
            var expression = Strip(data.DataExpression);
            if (expression.IsKind(SyntaxKind.NullLiteralExpression) || expression.IsKind(SyntaxKind.DefaultLiteralExpression))
            {
                branch.DataKind = "null";
                return;
            }
            type = data.DataModel.GetTypeInfo(expression).Type;
        }
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            branch.DataKind = "unknown";
            branch.Note ??= "表达式类型无法解析（可能有未还原的依赖）";
            return;
        }
        if (type.SpecialType == SpecialType.System_Object || type.TypeKind == TypeKind.Dynamic)
        {
            branch.DataKind = "unknown";
            branch.Note ??= "表达式静态类型为 object/dynamic，无法确定对外结构";
            return;
        }
        if (type is INamedTypeSymbol { IsAnonymousType: true })
        {
            branch.DataKind = "anonymous";
            branch.DataType = SymbolUtil.TypeName(type);
            branch.DataTypeId = "anonymous:" + Ids.ShortHash(type.ToDisplayString(), 12);
            dataTypes.Add(type);
            return;
        }
        branch.DataKind = "type";
        branch.DataType = SymbolUtil.TypeName(type);
        branch.DataTypeId = type.ToDisplayString();
        dataTypes.Add(type);
        foreach (var reference in UnderlyingTypes(type).SelectMany(t => t.DeclaringSyntaxReferences))
        {
            involvedFiles.Add(reference.SyntaxTree.FilePath);
        }
    }

    private static IEnumerable<ITypeSymbol> UnderlyingTypes(ITypeSymbol type)
    {
        yield return type;
        if (SymbolUtil.TryGetElementType(type, out var element)) yield return element;
        if (type is INamedTypeSymbol named)
        {
            foreach (var argument in named.TypeArguments) yield return argument;
        }
    }

    private static List<(SyntaxNode Node, ExpressionSyntax Expression)> Returns(MethodDeclarationSyntax method)
    {
        var result = new List<(SyntaxNode, ExpressionSyntax)>();
        if (method.ExpressionBody is not null)
        {
            result.Add((method.ExpressionBody, method.ExpressionBody.Expression));
            return result;
        }
        if (method.Body is null) return result;
        foreach (var statement in method.Body.DescendantNodes(n => n is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)).OfType<ReturnStatementSyntax>())
        {
            if (statement.Expression is not null) result.Add((statement, statement.Expression));
        }
        return result;
    }

    private static ExpressionSyntax Strip(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax p:
                    expression = p.Expression;
                    continue;
                case CastExpressionSyntax c:
                    expression = c.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } s:
                    expression = s.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private void Analyze(ExpressionSyntax expression, SemanticModel model, Dictionary<string, (ExpressionSyntax Expression, SemanticModel Model)> substitution, int depth, List<BranchData> results, HashSet<string> files, SyntaxNode origin)
    {
        expression = Strip(expression);
        switch (expression)
        {
            case AwaitExpressionSyntax awaitExpression:
                Analyze(awaitExpression.Expression, model, substitution, depth, results, files, origin);
                return;
            case ConditionalExpressionSyntax conditional:
                Analyze(conditional.WhenTrue, model, substitution, depth, results, files, origin);
                Analyze(conditional.WhenFalse, model, substitution, depth, results, files, origin);
                return;
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.CoalesceExpression):
                Analyze(binary.Left, model, substitution, depth, results, files, origin);
                Analyze(binary.Right, model, substitution, depth, results, files, origin);
                return;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms) Analyze(arm.Expression, model, substitution, depth, results, files, origin);
                return;
            case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression):
                results.Add(new BranchData { NoData = true, Note = "返回 null" });
                return;
        }

        var type = model.GetTypeInfo(expression).Type;
        if (type is not null && FindWrapperRule(profile, type) is { } rule)
        {
            AnalyzeWrapper(expression, type, rule, model, substitution, depth, results, files);
            return;
        }

        if (expression is InvocationExpressionSyntax invocation && model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
        {
            var containing = method.ContainingType?.ToDisplayString();
            if (containing is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller" && HelperStatus.TryGetValue(method.Name, out var status))
            {
                AnalyzePayload(invocation.ArgumentList.Arguments.LastOrDefault()?.Expression, status, model, substitution, depth, results, files, origin, method.Parameters.Length == 0);
                return;
            }
            if (method.Name == "StatusCode" && containing is "Microsoft.AspNetCore.Mvc.ControllerBase" or "Microsoft.AspNetCore.Mvc.Controller")
            {
                var code = invocation.ArgumentList.Arguments.Count > 0 ? model.GetConstantValue(invocation.ArgumentList.Arguments[0].Expression) : default;
                AnalyzePayload(invocation.ArgumentList.Arguments.Count > 1 ? invocation.ArgumentList.Arguments[1].Expression : null, code.HasValue && code.Value is int c ? c : null, model, substitution, depth, results, files, origin, invocation.ArgumentList.Arguments.Count <= 1);
                return;
            }
        }

        if (expression is BaseObjectCreationExpressionSyntax creation && type is INamedTypeSymbol created && ResultTypeStatus.TryGetValue(created.Name, out var resultStatus))
        {
            int? statusCode = resultStatus;
            if (creation.Initializer is not null)
            {
                foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                {
                    if (assignment.Left is IdentifierNameSyntax { Identifier.ValueText: "StatusCode" } && model.GetConstantValue(assignment.Right) is { HasValue: true, Value: int s }) statusCode = s;
                }
            }
            AnalyzePayload(creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression, statusCode, model, substitution, depth, results, files, origin, creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0);
            return;
        }

        if (type is not null && type.SpecialType != SpecialType.System_Object && type.TypeKind != TypeKind.Error
            && type.ToDisplayString() is not ("Microsoft.AspNetCore.Mvc.IActionResult" or "Microsoft.AspNetCore.Mvc.ActionResult" or "Microsoft.AspNetCore.Http.IResult"))
        {
            results.Add(new BranchData { DirectBodyType = type, HttpStatus = 200 });
            return;
        }
        results.Add(new BranchData { Unknown = true, Note = "无法识别的返回表达式：" + TextUtil.Truncate(expression.ToString(), 120) });
    }

    private void AnalyzePayload(ExpressionSyntax? payload, int? status, SemanticModel model, Dictionary<string, (ExpressionSyntax, SemanticModel)> substitution, int depth, List<BranchData> results, HashSet<string> files, SyntaxNode origin, bool noPayload)
    {
        if (payload is null || noPayload)
        {
            results.Add(new BranchData { HttpStatus = status, NoData = true, Note = "无响应体" });
            return;
        }
        var before = results.Count;
        Analyze(payload, model, substitution, depth, results, files, origin);
        for (var i = before; i < results.Count; i++)
        {
            results[i].HttpStatus ??= status;
            if (results[i].HttpStatus == 200 && status is not null) results[i].HttpStatus = status;
        }
    }

    private void AnalyzeWrapper(ExpressionSyntax expression, ITypeSymbol wrapperType, ResponseWrapperRule rule, SemanticModel model, Dictionary<string, (ExpressionSyntax Expression, SemanticModel Model)> substitution, int depth, List<BranchData> results, HashSet<string> files)
    {
        switch (expression)
        {
            case BaseObjectCreationExpressionSyntax creation:
            {
                var data = new BranchData { WrapperType = wrapperType };
                ExpressionSyntax? dataExpression = null;
                SemanticModel? dataModel = model;
                var ctor = model.GetSymbolInfo(creation).Symbol as IMethodSymbol;
                if (ctor is not null && creation.ArgumentList is not null)
                {
                    var (dataParameter, statusParameter, statusLiteral) = ConstructorFlow(ctor);
                    foreach (var (argument, i) in creation.ArgumentList.Arguments.Select((a, i) => (a, i)))
                    {
                        var parameter = argument.NameColon is not null
                            ? ctor.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText)
                            : i < ctor.Parameters.Length ? ctor.Parameters[i] : null;
                        if (parameter is null) continue;
                        if (parameter.Name == dataParameter) dataExpression = argument.Expression;
                        if (parameter.Name == statusParameter && model.GetConstantValue(argument.Expression) is { HasValue: true } constant) data.BusinessStatus = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
                    }
                    if (data.BusinessStatus is null && statusLiteral is not null) data.BusinessStatus = statusLiteral;
                    files.UnionWith(ctor.DeclaringSyntaxReferences.Select(r => r.SyntaxTree.FilePath));
                }
                if (creation.Initializer is not null)
                {
                    foreach (var assignment in creation.Initializer.Expressions.OfType<AssignmentExpressionSyntax>())
                    {
                        if (assignment.Left is not IdentifierNameSyntax name) continue;
                        if (name.Identifier.ValueText == rule.DataMember) dataExpression = assignment.Right;
                        if (name.Identifier.ValueText == "StatusCode" && model.GetConstantValue(assignment.Right) is { HasValue: true } constant)
                        {
                            data.BusinessStatus = Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
                        }
                    }
                }
                data.BusinessStatus ??= DefaultBusinessStatus(wrapperType);
                if (dataExpression is null)
                {
                    data.NoData = true;
                    data.Note = "封装未设置 data";
                }
                else
                {
                    (dataExpression, dataModel) = Substitute(dataExpression, model, substitution);
                    data.DataExpression = dataExpression;
                    data.DataModel = dataModel;
                }
                results.Add(data);
                return;
            }
            case InvocationExpressionSyntax invocation when model.GetSymbolInfo(invocation).Symbol is IMethodSymbol factory:
            {
                if (depth >= MaxFactoryDepth || !SymbolUtil.IsInSource(factory) || factory.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not MethodDeclarationSyntax factorySyntax)
                {
                    results.Add(new BranchData { WrapperType = wrapperType, Unknown = true, Note = $"封装由 {factory.Name} 返回，未继续展开（外部方法或超出深度）" });
                    return;
                }
                files.Add(factorySyntax.SyntaxTree.FilePath);
                var factoryModel = workspace.Model(factorySyntax.SyntaxTree);
                var inner = new Dictionary<string, (ExpressionSyntax, SemanticModel)>(StringComparer.Ordinal);
                foreach (var (argument, i) in invocation.ArgumentList.Arguments.Select((a, i) => (a, i)))
                {
                    var parameter = argument.NameColon is not null
                        ? factory.Parameters.FirstOrDefault(p => p.Name == argument.NameColon.Name.Identifier.ValueText)
                        : i < factory.Parameters.Length ? factory.Parameters[i] : null;
                    if (parameter is not null) inner[parameter.Name] = Substitute(argument.Expression, model, substitution);
                }
                foreach (var (node, returned) in Returns(factorySyntax))
                {
                    var before = results.Count;
                    Analyze(returned, factoryModel, inner, depth + 1, results, files, node);
                    for (var i = before; i < results.Count; i++)
                    {
                        results[i].Note = TextUtil.Truncate(((results[i].Note ?? "") + $" 经 {factory.Name} 返回").Trim(), 200);
                    }
                }
                return;
            }
            case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local:
            {
                var body = identifier.FirstAncestorOrSelf<MethodDeclarationSyntax>();
                var declarator = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;
                var produced = new List<BranchData>();
                if (declarator?.Initializer is not null)
                {
                    AnalyzeWrapper(Strip(declarator.Initializer.Value), wrapperType, rule, model, substitution, depth, produced, files);
                }
                if (body is not null)
                {
                    foreach (var assignment in body.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                    {
                        if (assignment.Left is IdentifierNameSyntax target && target.Identifier.ValueText == local.Name)
                        {
                            AnalyzeWrapper(Strip(assignment.Right), wrapperType, rule, model, substitution, depth, produced, files);
                        }
                        else if (assignment.Left is MemberAccessExpressionSyntax member && member.Expression is IdentifierNameSyntax owner && owner.Identifier.ValueText == local.Name
                                 && member.Name.Identifier.ValueText == rule.DataMember)
                        {
                            var (expr, exprModel) = Substitute(assignment.Right, model, substitution);
                            produced.Add(new BranchData { WrapperType = wrapperType, DataExpression = expr, DataModel = exprModel, BusinessStatus = DefaultBusinessStatus(wrapperType), Note = "局部变量赋值 data" });
                        }
                    }
                }
                if (produced.Count == 0)
                {
                    produced.Add(new BranchData { WrapperType = wrapperType, Unknown = true, Note = $"局部变量 {local.Name} 的来源无法确定" });
                }
                // 局部变量先构造封装、再给 data 赋值时，只保留带 data 的结果，避免把中间状态误判为“无 data”分支。
                if (produced.Any(p => p.DataExpression is not null)) produced.RemoveAll(p => p.NoData);
                results.AddRange(produced);
                return;
            }
            case IdentifierNameSyntax parameterIdentifier when substitution.TryGetValue(parameterIdentifier.Identifier.ValueText, out var mapped):
                Analyze(mapped.Expression, mapped.Model, new Dictionary<string, (ExpressionSyntax, SemanticModel)>(), depth, results, files, parameterIdentifier);
                return;
            default:
                results.Add(new BranchData { WrapperType = wrapperType, Unknown = true, Note = "封装来源无法静态确定：" + TextUtil.Truncate(expression.ToString(), 120) });
                return;
        }
    }

    private static (ExpressionSyntax, SemanticModel) Substitute(ExpressionSyntax expression, SemanticModel model, Dictionary<string, (ExpressionSyntax Expression, SemanticModel Model)> substitution)
    {
        var stripped = Strip(expression);
        if (stripped is IdentifierNameSyntax identifier && model.GetSymbolInfo(identifier).Symbol is IParameterSymbol && substitution.TryGetValue(identifier.Identifier.ValueText, out var mapped))
        {
            return mapped;
        }
        return (expression, model);
    }

    /// <summary>
    /// 构造函数体内 Data = 参数 / StatusCode = 参数或字面量 的对应关系（含一层 : this(...) 链）。
    /// </summary>
    private (string? DataParameter, string? StatusParameter, string? StatusLiteral) ConstructorFlow(IMethodSymbol ctor)
    {
        if (ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not ConstructorDeclarationSyntax syntax) return (null, null, null);
        var rule = profile.ResponseWrappers.FirstOrDefault(r => r.TypeName == ctor.ContainingType.Name || r.TypeName == ctor.ContainingType.ToDisplayString());
        var dataMember = rule?.DataMember ?? "Data";
        string? dataParameter = null, statusParameter = null, statusLiteral = null;
        var parameters = ctor.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var assignments = ((SyntaxNode?)syntax.Body ?? syntax.ExpressionBody)?.DescendantNodesAndSelf().OfType<AssignmentExpressionSyntax>() ?? Enumerable.Empty<AssignmentExpressionSyntax>();
        foreach (var assignment in assignments)
        {
            var left = assignment.Left switch
            {
                IdentifierNameSyntax i => i.Identifier.ValueText,
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } m => m.Name.Identifier.ValueText,
                _ => null,
            };
            if (left is null) continue;
            var right = assignment.Right is IdentifierNameSyntax r ? r.Identifier.ValueText : null;
            if (left == dataMember && right is not null && parameters.Contains(right)) dataParameter = right;
            if (left == "StatusCode")
            {
                if (right is not null && parameters.Contains(right)) statusParameter = right;
                else if (assignment.Right is LiteralExpressionSyntax literal) statusLiteral = literal.Token.ValueText;
            }
        }
        return (dataParameter, statusParameter, statusLiteral);
    }

    private static string? DefaultBusinessStatus(ITypeSymbol wrapperType)
    {
        var property = wrapperType.GetMembers("StatusCode").OfType<IPropertySymbol>().FirstOrDefault();
        if (property?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax { Initializer.Value: LiteralExpressionSyntax literal })
        {
            return literal.Token.ValueText + "（属性初始值）";
        }
        return null;
    }

    private static string ConditionOf(SyntaxNode node)
    {
        var parts = new List<string>();
        SyntaxNode? child = node;
        for (var current = node.Parent; current is not null && current is not MemberDeclarationSyntax && parts.Count < 2; current = current.Parent)
        {
            switch (current)
            {
                case CatchClauseSyntax catchClause:
                    parts.Add("异常分支 catch" + (catchClause.Declaration is null ? "" : $" ({catchClause.Declaration.Type})"));
                    break;
                case IfStatementSyntax ifStatement when ReferenceEquals(ifStatement.Statement, child) || ifStatement.Statement.Span.Contains(node.Span):
                    parts.Add($"if ({TextUtil.Truncate(ifStatement.Condition.ToString(), 120)})");
                    break;
                case ElseClauseSyntax elseClause when elseClause.Parent is IfStatementSyntax parentIf:
                    parts.Add($"else（非 {TextUtil.Truncate(parentIf.Condition.ToString(), 100)}）");
                    break;
                case SwitchSectionSyntax section:
                    parts.Add(TextUtil.Truncate(string.Join(" ", section.Labels.Select(l => l.ToString())), 100));
                    break;
            }
            child = current;
        }
        return parts.Count == 0 ? "默认路径" : string.Join(" ← ", parts);
    }
}
