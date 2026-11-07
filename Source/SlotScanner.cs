using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Source;

/// <summary>
/// 为选中接口及其实际关联的类型生成注释位置清单，并判断每个位置的注释状态与是否可安全回写。
/// 空标签、占位文字不算完成；未解析的 inheritdoc 不冒充完整说明。
/// </summary>
public sealed class SlotScanner(LoadedWorkspace workspace, ProjectProfile profile)
{
    private const int MaxTypeDepth = 8;
    private readonly Dictionary<string, DocSlot> _slots = new(StringComparer.Ordinal);
    private readonly HashSet<string> _unresolved = new(StringComparer.Ordinal);
    private readonly HashSet<string> _external = new(StringComparer.Ordinal);

    public IReadOnlyCollection<DocSlot> Slots => _slots.Values;

    public IReadOnlyCollection<string> UnresolvedSymbols => _unresolved;

    public IReadOnlyCollection<string> ExternalTypes => _external;

    public void AddEndpoint(DiscoveredEndpoint endpoint, IEnumerable<ITypeSymbol> responseTypes)
    {
        var key = endpoint.Info.Key;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        AddMethodSlots(endpoint, key);

        foreach (var parameter in endpoint.Method.Parameters)
        {
            var binding = endpoint.Info.Parameters.First(p => p.Name == parameter.Name);
            if (binding.Source is "services" or "special") continue;
            if (parameter.Type.TypeKind == TypeKind.Error) _unresolved.Add($"{endpoint.Info.Signature} 参数 {parameter.Name} 的类型 {parameter.Type.Name}");
            AddTypeTree(parameter.Type, "request", key, 0, visited);
        }

        var inner = SymbolUtil.UnwrapTask(endpoint.Method.ReturnType, out _);
        if (inner is INamedTypeSymbol named && named.OriginalDefinition.ToDisplayString() == "Microsoft.AspNetCore.Mvc.ActionResult<TValue>")
        {
            inner = named.TypeArguments[0];
        }
        if (inner is not null) AddTypeTree(inner, "response", key, 0, visited);
        foreach (var type in responseTypes) AddTypeTree(type, "response", key, 0, visited);
    }

    public bool IsWrapper(ITypeSymbol type) => ResponseTracer.FindWrapperRule(profile, type) is not null;

    private void AddMethodSlots(DiscoveredEndpoint endpoint, string endpointKey)
    {
        var method = endpoint.Method;
        var methodId = SymbolUtil.DocId(method);
        var doc = DocComments.Read(endpoint.Syntax);
        var (file, line) = SymbolUtil.Location(method, workspace.WorkspaceRoot);
        var (editable, reason) = Editability(method, endpoint.Syntax, doc);
        var inherited = method.IsPartialDefinition || method.PartialImplementationPart is not null || method.PartialDefinitionPart is not null
            ? null
            : ResolveInheritDoc(method, doc);

        void Add(string tag, string? parameterName, string display, string? jsonName, string? typeDisplay)
        {
            var slot = NewSlot(methodId, "method", tag, parameterName, display, file, line, "endpoint", endpointKey);
            ApplyStatus(slot, doc, inherited, tag, parameterName);
            slot.Editable = editable;
            slot.NotEditableReason = reason;
            slot.JsonName = jsonName;
            slot.TypeDisplay = typeDisplay;
        }

        Add("summary", null, endpoint.Info.Signature, null, null);
        foreach (var parameter in method.Parameters)
        {
            var binding = endpoint.Info.Parameters.First(p => p.Name == parameter.Name);
            if (binding.Source is "services" or "special") continue;
            Add("param", parameter.Name, $"{endpoint.Info.Action}({parameter.Name})", binding.ExternalName, binding.Type);
        }
        if (SymbolUtil.UnwrapTask(method.ReturnType, out var isVoid) is not null && !isVoid)
        {
            Add("returns", null, endpoint.Info.Signature, null, SymbolUtil.TypeName(method.ReturnType));
        }

        // 方法签名中已不存在的 param 标签（参数改名后遗留）。
        var names = method.Parameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var element in doc.Elements.Where(e => e.Name == "param" && e.NameAttribute is not null && !names.Contains(e.NameAttribute)))
        {
            var slot = NewSlot(methodId, "method", "param", element.NameAttribute, $"{endpoint.Info.Action}({element.NameAttribute})", file, line, "endpoint", endpointKey);
            slot.Status = "stale";
            slot.ExistingText = element.Text;
            slot.Editable = editable && !element.Complex;
            slot.NotEditableReason = slot.Editable ? null : reason ?? "注释格式复杂，需人工处理";
            slot.Issues.Add("方法签名中没有该参数，标签可能是参数改名后遗留的失效说明。");
        }
    }

    private void AddTypeTree(ITypeSymbol type, string role, string endpointKey, int depth, HashSet<string> visited)
    {
        if (depth > MaxTypeDepth) return;
        type = SymbolUtil.UnwrapNullable(type);
        if (type.TypeKind == TypeKind.Error)
        {
            _unresolved.Add("类型 " + type.Name);
            return;
        }
        if (SymbolUtil.TryGetElementType(type, out var element))
        {
            AddTypeTree(element, role, endpointKey, depth + 1, visited);
            return;
        }
        if (SymbolUtil.TryGetDictionaryValue(type, out var value))
        {
            AddTypeTree(value, role, endpointKey, depth + 1, visited);
            return;
        }
        if (type is not INamedTypeSymbol named) return;
        if (named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments) AddTypeTree(argument, role, endpointKey, depth + 1, visited);
        }
        if (named.TypeKind == TypeKind.Enum)
        {
            AddEnum(named, endpointKey);
            return;
        }
        if (!SymbolUtil.IsDocumentableComplex(named)) return;
        if (!SymbolUtil.IsInSource(named))
        {
            _external.Add(named.ToDisplayString());
            return;
        }

        // 每个接口对每个类型只展开一次，既避免递归模型无限展开，又能把接口记入共享类型的使用范围。
        if (!visited.Add(SymbolUtil.DocId(named) + "|" + role)) return;
        var properties = SymbolUtil.SerializableProperties(named);
        AddTypeSlots(named.OriginalDefinition, properties, role, endpointKey);
        foreach (var property in properties)
        {
            AddTypeTree(property.Type, role, endpointKey, depth + 1, visited);
        }
    }

    private void AddTypeSlots(INamedTypeSymbol type, List<IPropertySymbol> properties, string role, string endpointKey)
    {
        var typeId = SymbolUtil.DocId(type);
        var declarations = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<BaseTypeDeclarationSyntax>().ToList();
        if (declarations.Count == 0) return;
        var (file, line) = SymbolUtil.Location(type, workspace.WorkspaceRoot);

        var docs = declarations.Select(DocComments.Read).ToList();
        var primary = declarations.Count == 1 ? declarations[0] : null;
        var primaryDoc = primary is null ? docs.FirstOrDefault(d => d.HasComment) ?? docs[0] : docs[0];
        var typeSlotId = DocSlot.MakeId(typeId, "summary", null);
        if (!_slots.ContainsKey(typeSlotId))
        {
            var typeSlot = NewSlot(typeId, "type", "summary", null, SymbolUtil.Short(type), file, line, role, endpointKey);
            typeSlot.OwnerTypeId = typeId;
            typeSlot.ComponentName = type.IsGenericType ? null : SchemaBuilder.SchemaId(type);
            ApplyStatus(typeSlot, primaryDoc, null, "summary", null);
            var summary = docs.Select(d => d.TextOf("summary")).FirstOrDefault(t => t is not null);
            if (summary is not null)
            {
                typeSlot.Status = "valid";
                typeSlot.ExistingText = summary;
            }
            (typeSlot.Editable, typeSlot.NotEditableReason) = primary is null
                ? (false, "partial 类型有多个声明，需人工指定注释位置")
                : Editability(type, primary, primaryDoc);
        }
        else
        {
            MergeUsage(_slots[typeSlotId], role, endpointKey);
        }

        foreach (var property in properties)
        {
            var declaring = property.ContainingType.OriginalDefinition;
            var declaringSyntax = declaring.DeclaringSyntaxReferences.Length == 1 ? declaring.DeclaringSyntaxReferences[0].GetSyntax() as BaseTypeDeclarationSyntax : null;
            var declaringDoc = declaringSyntax is null ? new DocInfo() : DocComments.Read(declaringSyntax);
            AddPropertySlot(declaring, (IPropertySymbol)property.OriginalDefinition, role, endpointKey, declaringSyntax, declaringDoc);
        }
    }

    private void AddPropertySlot(INamedTypeSymbol owner, IPropertySymbol property, string role, string endpointKey, BaseTypeDeclarationSyntax? ownerDeclaration, DocInfo ownerDoc)
    {
        var typeId = SymbolUtil.DocId(owner);
        var syntax = property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var isRecordParameter = syntax is ParameterSyntax && ownerDeclaration is RecordDeclarationSyntax;
        var existingId = isRecordParameter ? DocSlot.MakeId(typeId, "param", property.Name) : DocSlot.MakeId(SymbolUtil.DocId(property), "summary", null);
        if (_slots.TryGetValue(existingId, out var known))
        {
            MergeUsage(known, role, endpointKey);
            return;
        }
        var (file, line) = SymbolUtil.Location(property, workspace.WorkspaceRoot);
        var jsonName = SymbolUtil.JsonName(property, profile.Serialization);
        DocSlot slot;
        if (isRecordParameter && ownerDeclaration is not null)
        {
            // 位置记录的属性说明写在记录类型的 <param name="X"> 上。
            slot = NewSlot(typeId, "recordParameter", "param", property.Name, $"{owner.Name}.{property.Name}", file, line, role, endpointKey);
            ApplyStatus(slot, ownerDoc, null, "param", property.Name);
            var (editable, reason) = Editability(owner, ownerDeclaration, ownerDoc);
            slot.Editable = editable;
            slot.NotEditableReason = reason;
        }
        else if (syntax is PropertyDeclarationSyntax propertyDeclaration)
        {
            var doc = DocComments.Read(propertyDeclaration);
            slot = NewSlot(SymbolUtil.DocId(property), "property", "summary", null, $"{owner.Name}.{property.Name}", file, line, role, endpointKey);
            ApplyStatus(slot, doc, property.OverriddenProperty is { } overridden ? ReadText(overridden) : null, "summary", null);
            var (editable, reason) = Editability(property, propertyDeclaration, doc);
            slot.Editable = editable;
            slot.NotEditableReason = reason;
        }
        else
        {
            return;
        }
        slot.OwnerTypeId = typeId;
        slot.ComponentName = owner.IsGenericType ? null : SchemaBuilder.SchemaId(owner);
        slot.JsonName = jsonName;
        slot.TypeDisplay = SymbolUtil.TypeName(property.Type);
        if (slot.ExistingText is { } text)
        {
            var required = SymbolUtil.IsRequired(property);
            var nullable = property.NullableAnnotation == NullableAnnotation.Annotated || SymbolUtil.IsNullableValue(property.Type);
            if (text.Contains("必填", StringComparison.Ordinal) && !text.Contains("非必填", StringComparison.Ordinal) && !required && nullable)
            {
                slot.Issues.Add("注释写“必填”，但属性可空且没有 [Required]/required，可能与当前校验不一致（需人工确认）。");
            }
            if ((text.Contains("可选", StringComparison.Ordinal) || text.Contains("选填", StringComparison.Ordinal) || text.Contains("非必填", StringComparison.Ordinal)) && required)
            {
                slot.Issues.Add("注释写“可选”，但属性标记为必填，可能与当前校验不一致（需人工确认）。");
            }
        }
    }

    private void AddEnum(INamedTypeSymbol type, string endpointKey)
    {
        if (!SymbolUtil.IsInSource(type))
        {
            _external.Add(type.ToDisplayString());
            return;
        }
        var typeId = SymbolUtil.DocId(type);
        var slotId = DocSlot.MakeId(typeId, "summary", null);
        if (_slots.TryGetValue(slotId, out var existing))
        {
            MergeUsage(existing, "enum", endpointKey);
            foreach (var slot in _slots.Values.Where(s => s.OwnerTypeId == typeId)) MergeUsage(slot, "enum", endpointKey);
            return;
        }
        if (type.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not EnumDeclarationSyntax declaration) return;
        var (file, line) = SymbolUtil.Location(type, workspace.WorkspaceRoot);
        var doc = DocComments.Read(declaration);
        var typeSlot = NewSlot(typeId, "enum", "summary", null, type.Name, file, line, "enum", endpointKey);
        typeSlot.OwnerTypeId = typeId;
        typeSlot.ComponentName = SchemaBuilder.SchemaId(type);
        ApplyStatus(typeSlot, doc, null, "summary", null);
        (typeSlot.Editable, typeSlot.NotEditableReason) = Editability(type, declaration, doc);

        foreach (var member in type.GetMembers().OfType<IFieldSymbol>().Where(f => f.HasConstantValue))
        {
            if (member.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not EnumMemberDeclarationSyntax memberSyntax) continue;
            var memberDoc = DocComments.Read(memberSyntax);
            var (mFile, mLine) = SymbolUtil.Location(member, workspace.WorkspaceRoot);
            var slot = NewSlot(SymbolUtil.DocId(member), "enumMember", "summary", null, $"{type.Name}.{member.Name} = {member.ConstantValue}", mFile, mLine, "enum", endpointKey);
            slot.OwnerTypeId = typeId;
            ApplyStatus(slot, memberDoc, null, "summary", null);
            (slot.Editable, slot.NotEditableReason) = Editability(member, memberSyntax, memberDoc);
        }
    }

    private static void MergeUsage(DocSlot slot, string role, string endpointKey)
    {
        if (!slot.EndpointKeys.Contains(endpointKey)) slot.EndpointKeys.Add(endpointKey);
        if (!slot.Role.Split(',').Contains(role)) slot.Role = slot.Role + "," + role;
    }

    private DocSlot NewSlot(string symbolId, string kind, string tag, string? parameterName, string display, string file, int line, string role, string endpointKey)
    {
        var id = DocSlot.MakeId(symbolId, tag, parameterName);
        if (_slots.TryGetValue(id, out var existing))
        {
            MergeUsage(existing, role, endpointKey);
            return existing;
        }
        var slot = new DocSlot
        {
            Id = id,
            SymbolId = symbolId,
            SymbolKind = kind,
            Display = display,
            Tag = tag,
            ParameterName = parameterName,
            File = file,
            Line = line,
            Role = role,
            EndpointKeys = [endpointKey],
        };
        _slots[id] = slot;
        return slot;
    }

    private static void ApplyStatus(DocSlot slot, DocInfo doc, string? inheritedText, string tag, string? parameterName)
    {
        if (!doc.HasComment)
        {
            slot.Status = "missing";
            return;
        }
        if (!doc.XmlValid)
        {
            slot.Status = "invalidXml";
            slot.Issues.Add("文档注释不是合法 XML：" + doc.XmlError);
            return;
        }
        var element = doc.Find(tag, parameterName);
        if (element is not null)
        {
            slot.ExistingText = element.Text;
            slot.Status = element.IsEmpty ? "empty" : "valid";
            return;
        }
        if (doc.HasInheritDoc)
        {
            if (inheritedText is not null)
            {
                slot.Status = "inheritDoc";
                slot.ExistingText = inheritedText;
                slot.Issues.Add("说明来自 <inheritdoc/>；C# 生成的 XML 保留 inheritdoc 原样，Swagger 是否展开需在传播核对中确认。");
            }
            else
            {
                slot.Status = "inheritDocUnresolved";
                slot.Issues.Add("<inheritdoc/> 无法在源码中找到可继承的说明。");
            }
            return;
        }
        slot.Status = "missing";
    }

    /// <summary>
    /// 方法 inheritdoc：依次查找被重写成员、显式/隐式实现的接口成员（仅源码中）。
    /// </summary>
    private static string? ResolveInheritDoc(IMethodSymbol method, DocInfo doc)
    {
        if (!doc.HasInheritDoc) return null;
        var candidates = new List<IMethodSymbol>();
        if (method.OverriddenMethod is not null) candidates.Add(method.OverriddenMethod);
        candidates.AddRange(method.ExplicitInterfaceImplementations);
        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method)) candidates.Add(member);
            }
        }
        foreach (var candidate in candidates)
        {
            var text = ReadText(candidate);
            if (text is not null) return text;
        }
        return null;
    }

    private static string? ReadText(ISymbol symbol)
    {
        if (symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is not { } syntax) return null;
        return DocComments.Read(syntax).TextOf("summary");
    }

    private (bool Editable, string? Reason) Editability(ISymbol symbol, SyntaxNode declaration, DocInfo doc)
    {
        if (symbol.DeclaringSyntaxReferences.Length > 1) return (false, "符号有多个声明（partial），需人工指定注释位置");
        if (symbol is IMethodSymbol { PartialDefinitionPart: not null } or IMethodSymbol { PartialImplementationPart: not null } or IMethodSymbol { IsPartialDefinition: true })
        {
            return (false, "partial 方法，需人工确定注释位置");
        }
        var file = workspace.FileOf(declaration.SyntaxTree);
        if (file is null || file.IsSynthetic) return (false, "生成代码或未知文件");
        var rel = file.RelativePath;
        if (rel.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "生成代码不自动修改");
        }
        if (IsAutoGenerated(declaration.SyntaxTree)) return (false, "文件标记为 auto-generated");
        if (!file.IsUtf8) return (false, "文件不是 UTF-8 编码，不自动修改");
        if (!ProfileStore.IsWriteAllowed(profile, rel)) return (false, "不在允许回写范围内（可导出补丁）");
        if (doc.MultiLineStyle) return (false, "/** */ 风格注释不自动修改");
        if (doc.MultipleBlocks) return (false, "存在多段文档注释，需人工整理");
        if (!doc.XmlValid) return (false, "现有注释不是合法 XML，需人工修正");
        return (true, null);
    }

    private static bool IsAutoGenerated(SyntaxTree tree)
    {
        var root = tree.GetRoot();
        var leading = root.GetLeadingTrivia().ToFullString();
        return leading.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
    }

    public CoverageSummary Summarize()
    {
        var counted = _slots.Values.Where(s => s.Status != "stale").ToList();
        var summary = new CoverageSummary
        {
            Total = counted.Count,
            Valid = counted.Count(s => s.Status == "valid"),
            InheritDoc = counted.Count(s => s.Status == "inheritDoc"),
            UnresolvedSymbols = _unresolved.Count,
        };
        foreach (var group in _slots.Values.GroupBy(s => s.Status)) summary.ByStatus[group.Key] = group.Count();
        foreach (var group in counted.GroupBy(s => s.Role.Split(',')[0])) summary.ByRole[group.Key] = group.Count();
        return summary;
    }
}
