using ApifoxHelper.Comments;
using ApifoxHelper.Tasks;
using Microsoft.CodeAnalysis;

namespace ApifoxHelper.Source;

/// <summary>
/// 源码扫描：加载工作区 → 发现范围内接口 → 追踪响应 data → 生成注释位置清单 → 收集证据 → 复核已有草稿是否过期。
/// 只读取源码，不写工作区文件。
/// </summary>
public sealed class SourceScanService(TaskStore tasks, ProjectLoader loader)
{
    public async Task<object> ScanAsync(string taskId, JobContext job)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        job.Report(0.05, "加载项目与引用（不执行 restore/build）");
        var workspace = await Task.Run(() => loader.Load(profile, job.Token), job.Token);

        job.Report(0.3, "发现接口与路由");
        var discovery = new EndpointDiscovery(workspace, profile);
        var all = discovery.Discover(job.Token);
        var endpoints = all.Where(e => TaskStore.InScope(task, e.Info.Key, e.Info.Path)).ToList();

        var sink = new EvidenceSink(workspace);
        var tracer = new ResponseTracer(workspace, profile, sink);
        var scanner = new SlotScanner(workspace, profile);
        var collector = new EvidenceCollector(workspace, profile, sink);
        var contracts = new List<ResponseContract>();
        var coverages = new List<EndpointCoverage>();

        for (var i = 0; i < endpoints.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var endpoint = endpoints[i];
            if (i % 5 == 0) job.Report(0.3 + 0.6 * i / Math.Max(1, endpoints.Count), $"分析接口 {i + 1}/{endpoints.Count}：{endpoint.Info.Key}");
            var contract = tracer.Trace(endpoint, out var dataTypes, job.Token);
            if (contract is not null) contracts.Add(contract);
            scanner.AddEndpoint(endpoint, dataTypes);

            var requestTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var parameter in endpoint.Method.Parameters) CollectTypes(parameter.Type, requestTypes, 0);
            var responseTypes = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var returned = SymbolUtil.UnwrapTask(endpoint.Method.ReturnType, out _);
            if (returned is not null) CollectTypes(returned, responseTypes, 0);
            foreach (var type in dataTypes) CollectTypes(type, responseTypes, 0);
            coverages.Add(collector.Collect(endpoint, requestTypes, responseTypes, job.Token));
        }

        job.Report(0.92, "关联证据并复核已有草稿");
        var slots = scanner.Slots.OrderBy(s => s.File, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Line).ToList();
        LinkEvidence(slots, sink.Items, coverages, endpoints);

        var output = new ScanOutput
        {
            Endpoints = endpoints.Select(e => e.Info).OrderBy(e => e.Path, StringComparer.Ordinal).ThenBy(e => e.HttpMethod, StringComparer.Ordinal).ToList(),
            Slots = slots,
            UnresolvedSymbols = scanner.UnresolvedSymbols.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            ExternalTypes = scanner.ExternalTypes.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            Coverage = scanner.Summarize(),
        };
        var manifest = new SourceManifest
        {
            WorkspaceRoot = workspace.WorkspaceRoot,
            ApiProject = profile.ApiProject,
            AnalysisLevel = workspace.Level.ToString(),
            ManifestHash = workspace.ManifestHash,
            ScannedAt = DateTimeOffset.Now,
            Warnings = workspace.Warnings.Concat(discovery.Warnings).Distinct().ToList(),
            FileHashes = workspace.FilesByPath.Values.Where(f => !f.IsSynthetic).ToDictionary(f => f.RelativePath, f => f.Hash, StringComparer.OrdinalIgnoreCase),
            Projects = workspace.Projects.Select(p => new ProjectSummary
            {
                Name = p.Name,
                TargetFramework = p.TargetFramework,
                Files = p.Files.Count(f => !f.IsSynthetic),
                AssetsFound = p.AssetsFileFound,
                MissingPackageAssets = p.MissingPackageAssets,
                GenerateDocumentationFile = p.GenerateDocumentationFile,
                DocumentationFile = p.DocumentationFilePath is null ? null : SymbolUtil.Relative(workspace.WorkspaceRoot, p.DocumentationFilePath),
            }).ToList(),
        };
        foreach (var project in manifest.Projects)
        {
            var loaded = workspace.Projects.First(p => p.Name == project.Name);
            project.DeclarationErrors = loaded.Compilation.GetDeclarationDiagnostics(job.Token)
                .Count(d => d.Severity == DiagnosticSeverity.Error && d.Id is "CS0246" or "CS0234" or "CS0518" or "CS0012");
        }

        var evidence = new EvidenceStore { Items = sink.Items.OrderBy(e => e.File, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.StartLine).ToList(), Coverage = coverages };

        using (await KeyedLocks.AcquireAsync("task:" + taskId, job.Token))
        {
            // 合并已有响应契约的审阅结果：源码指纹不变则保留确认，变化则标为过期。
            var previous = tasks.Read<List<ResponseContract>>(taskId, TaskFiles.ResponseContract) ?? new List<ResponseContract>();
            foreach (var contract in contracts)
            {
                var old = previous.FirstOrDefault(p => p.EndpointKey == contract.EndpointKey);
                if (old is null || contract.ReviewState == "blocked") continue;
                if (old.ReviewState is "confirmed" or "rejected")
                {
                    if (old.SourceFingerprint == contract.SourceFingerprint && old.DataTypeId == contract.DataTypeId)
                    {
                        contract.ReviewState = old.ReviewState;
                        contract.ReviewedAt = old.ReviewedAt;
                    }
                    else
                    {
                        contract.ReviewState = "stale";
                        contract.UnresolvedPoints.Insert(0, "源码已变化，之前的确认已失效，请重新审阅。");
                    }
                }
            }

            var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();
            var slotMap = slots.ToDictionary(s => s.Id, StringComparer.Ordinal);
            var evidenceMap = evidence.Items.ToDictionary(e => e.Id, StringComparer.Ordinal);
            var staled = 0;
            foreach (var candidate in drafts.Candidates)
            {
                if (candidate.ReviewState is "rejected") continue;
                if (!slotMap.TryGetValue(candidate.SlotId, out var slot))
                {
                    if (candidate.ReviewState != "stale") staled++;
                    candidate.ReviewState = "stale";
                    candidate.Warnings.Add("对应的注释位置在本次扫描中已不存在（符号可能已改名或删除）。");
                    continue;
                }
                candidate.EvidenceIds = candidate.EvidenceIds.Where(evidenceMap.ContainsKey).ToList();
                if (candidate.ReviewState == "accepted" && candidate.ReviewedEvidenceHash is not null && candidate.ReviewedEvidenceHash != SlotEvidenceHash(slot))
                {
                    candidate.ReviewState = "stale";
                    candidate.Warnings.Add("审阅后相关代码证据发生变化，需要重新审阅。");
                    staled++;
                }
            }
            tasks.Write(taskId, TaskFiles.Manifest, manifest);
            tasks.Write(taskId, TaskFiles.Symbols, output);
            tasks.Write(taskId, TaskFiles.Evidence, evidence);
            tasks.Write(taskId, TaskFiles.ResponseContract, contracts);
            tasks.Write(taskId, TaskFiles.Drafts, drafts);

            var current = tasks.Get(taskId);
            current.Stages.Source.Status = drafts.Candidates.Count > 0 ? "reviewing" : "scanned";
            current.Stages.Source.ScannedAt = manifest.ScannedAt;
            current.Stages.Source.AnalysisLevel = manifest.AnalysisLevel;
            current.Stages.Source.ManifestHash = manifest.ManifestHash;
            current.Stages.Source.Endpoints = output.Endpoints.Count;
            current.Stages.Source.Slots = output.Coverage.Total;
            current.Stages.Source.ValidSlots = output.Coverage.Valid + output.Coverage.InheritDoc;
            current.Stages.Source.UnresolvedSymbols = output.UnresolvedSymbols.Count;
            current.Stages.Source.Candidates = drafts.Candidates.Count;
            current.Stages.Source.Accepted = drafts.Candidates.Count(c => c.ReviewState == "accepted");
            current.Stages.Source.Message = staled > 0 ? $"{staled} 条已审阅草稿因代码变化需要重新审阅。" : null;
            current.Revision++;
            tasks.Save(current);

            return new
            {
                endpoints = output.Endpoints.Count,
                slots = output.Coverage.Total,
                valid = output.Coverage.Valid,
                inheritDoc = output.Coverage.InheritDoc,
                evidence = evidence.Items.Count,
                contracts = contracts.Count,
                analysisLevel = manifest.AnalysisLevel,
                warnings = manifest.Warnings.Count,
                staleDrafts = staled,
            };
        }
    }

    public static string SlotEvidenceHash(DocSlot slot) =>
        Canonical.Sha256Hex(string.Join("|", slot.EvidenceIds.OrderBy(x => x, StringComparer.Ordinal)));

    private static void CollectTypes(ITypeSymbol type, HashSet<INamedTypeSymbol> set, int depth)
    {
        if (depth > 8) return;
        type = SymbolUtil.UnwrapNullable(type);
        if (SymbolUtil.TryGetElementType(type, out var element))
        {
            CollectTypes(element, set, depth + 1);
            return;
        }
        if (SymbolUtil.TryGetDictionaryValue(type, out var value))
        {
            CollectTypes(value, set, depth + 1);
            return;
        }
        if (type is not INamedTypeSymbol named) return;
        if (named.IsGenericType)
        {
            foreach (var argument in named.TypeArguments) CollectTypes(argument, set, depth + 1);
        }
        if (named.TypeKind == TypeKind.Enum)
        {
            if (SymbolUtil.IsInSource(named)) set.Add(named);
            return;
        }
        if (!SymbolUtil.IsDocumentableComplex(named) || !SymbolUtil.IsInSource(named)) return;
        if (!set.Add(named)) return;
        foreach (var property in SymbolUtil.SerializableProperties(named)) CollectTypes(property.Type, set, depth + 1);
    }

    /// <summary>
    /// 把证据挂到注释位置：参数按“方法#参数名”、属性按属性 ID、方法 summary/returns 取该接口调用链上的主要证据。
    /// </summary>
    private static void LinkEvidence(List<DocSlot> slots, IReadOnlyCollection<CodeEvidence> evidence, List<EndpointCoverage> coverages, List<DiscoveredEndpoint> endpoints)
    {
        var byRelated = new Dictionary<string, List<CodeEvidence>>(StringComparer.Ordinal);
        foreach (var item in evidence)
        {
            foreach (var related in item.RelatedSymbolIds)
            {
                if (!byRelated.TryGetValue(related, out var list)) byRelated[related] = list = new List<CodeEvidence>();
                list.Add(item);
            }
        }
        var byOwner = evidence.GroupBy(e => e.OwnerSymbolId).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var evidenceById = evidence.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var coverageByMethod = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            var coverage = coverages.FirstOrDefault(c => c.EndpointKey == endpoint.Info.Key);
            if (coverage is null) continue;
            if (!coverageByMethod.TryGetValue(endpoint.Info.MethodSymbolId, out var list)) coverageByMethod[endpoint.Info.MethodSymbolId] = list = new List<string>();
            list.AddRange(coverage.EvidenceIds);
        }

        foreach (var slot in slots)
        {
            IEnumerable<CodeEvidence> linked;
            switch (slot.SymbolKind)
            {
                case "method" when slot.Tag == "param" && slot.ParameterName is not null:
                    linked = byRelated.GetValueOrDefault(slot.SymbolId + "#" + slot.ParameterName) ?? new List<CodeEvidence>();
                    break;
                case "method":
                    linked = (coverageByMethod.GetValueOrDefault(slot.SymbolId) ?? new List<string>())
                        .Distinct(StringComparer.Ordinal)
                        .Select(id => evidenceById.GetValueOrDefault(id))
                        .OfType<CodeEvidence>()
                        .Where(e => slot.Tag == "returns" ? e.Kind is "return" or "responseData" or "mapping" : e.Kind is "route" or "call" or "guard" or "queryFilter" or "return" or "responseData" or "mapping")
                        .Take(30);
                    break;
                case "property":
                case "enumMember":
                    linked = byRelated.GetValueOrDefault(slot.SymbolId) ?? new List<CodeEvidence>();
                    break;
                case "recordParameter" when slot.ParameterName is not null && slot.SymbolId.StartsWith("T:", StringComparison.Ordinal):
                    linked = byRelated.GetValueOrDefault("P:" + slot.SymbolId[2..] + "." + slot.ParameterName) ?? new List<CodeEvidence>();
                    break;
                default:
                    linked = (byOwner.GetValueOrDefault(slot.SymbolId) ?? new List<CodeEvidence>())
                        .Concat(evidence.Where(e => e.Kind == "mapping" && e.RelatedSymbolIds.Any(r => r.StartsWith("P:" + slot.SymbolId[2..] + ".", StringComparison.Ordinal))))
                        .Take(30);
                    break;
            }
            slot.EvidenceIds = linked.Select(e => e.Id).Distinct(StringComparer.Ordinal).ToList();
        }
    }
}
