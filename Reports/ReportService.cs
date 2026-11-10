using ApifoxHelper.Comments;
using ApifoxHelper.Source;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Reports;

/// <summary>
/// 分阶段报告：源码注释完整性、XML/Swagger 传播、Apifox 一致性与完整性、业务运行验证（第一版不执行）。
/// 共享报告全部脱敏，不包含令牌、原生快照或原始命令输出。
/// </summary>
public sealed class ReportService(TaskStore tasks)
{
    public JsonObject Build(string taskId)
    {
        var task = tasks.Get(taskId);
        var scan = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols);
        var manifest = tasks.Read<SourceManifest>(taskId, TaskFiles.Manifest);
        var evidence = tasks.Read<EvidenceStore>(taskId, TaskFiles.Evidence);
        var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();
        var commentPlan = tasks.Read<CommentPlan>(taskId, TaskFiles.CommentPlan);
        var propagation = tasks.Read<PropagationResult>(taskId, TaskFiles.Propagation);
        var diff = tasks.Read<CheckResult>(taskId, TaskFiles.Diff);
        var plan = tasks.Read<SyncPlan>(taskId, TaskFiles.Plan);
        var decisions = tasks.Read<DecisionStore>(taskId, TaskFiles.Decisions) ?? new DecisionStore();
        var profile = task.ProfileSnapshot;

        var unfinished = new JsonArray();
        void Todo(string text) => unfinished.Add(Redactor.Redact(text));
        if (scan is not null)
        {
            foreach (var slot in scan.Slots.Where(s => s.Status is "missing" or "empty" or "inheritDocUnresolved" or "stale" or "invalidXml"))
            {
                var hasAccepted = drafts.Candidates.Any(c => c.SlotId == slot.Id && c.ReviewState == "accepted");
                if (!hasAccepted) Todo($"注释未补全：{slot.Display} {slot.Tag}{(slot.ParameterName is null ? "" : " " + slot.ParameterName)}（{slot.Status}）");
            }
        }
        foreach (var candidate in drafts.Candidates.Where(c => c.ReviewState == "stale")) Todo($"草稿依据已变化需重新审阅：{candidate.SymbolId} {candidate.Tag}");
        if (diff is not null)
        {
            foreach (var item in diff.Items.Where(i => i.NeedsDecision && decisions.Items.GetValueOrDefault(i.Id) != "accepted")) Todo($"未确认的高风险变化：{item.ResourceKey} {item.Location}（{item.Reason}）");
            foreach (var endpoint in diff.Endpoints)
            {
                foreach (var conflict in endpoint.DocChanges.Where(c => c.Decision == "conflict")) Todo($"说明冲突未选择：{endpoint.Key} {conflict.Location}");
                foreach (var blocker in endpoint.Blockers) Todo($"阻断：{endpoint.Key} {blocker}");
            }
            foreach (var issue in diff.Quality.Where(q => q.Kind == "genericData")) Todo($"{issue.ResourceKey}：{issue.Message}");
        }
        if (plan is not null)
        {
            foreach (var excluded in plan.Excluded) Todo($"未纳入同步：{excluded.ResourceKey}（{excluded.Reason}）");
            foreach (var action in plan.Actions.Where(a => a.State != "verified")) Todo($"同步动作未完成：{action.ResourceKey} {action.Kind} → {action.State} {action.Message}");
        }

        var report = new JsonObject
        {
            ["task"] = new JsonObject
            {
                ["id"] = task.Id,
                ["name"] = task.Name,
                ["createdAt"] = task.CreatedAt,
                ["toolVersion"] = task.ToolVersion,
                ["cliVersion"] = task.CliVersion,
                ["sourceVersionNote"] = task.SourceVersionNote ?? "未绑定代码版本",
                ["scope"] = JsonDefaults.ToNode(task.PathPrefixes.Concat(task.EndpointKeys)),
            },
            ["target"] = new JsonObject
            {
                ["projectName"] = profile.Apifox.ProjectName,
                ["projectId"] = profile.Apifox.ProjectId,
                ["branch"] = profile.Apifox.BranchName ?? "main（主分支）",
            },
            ["source"] = new JsonObject
            {
                ["workspace"] = profile.WorkspaceRoot,
                ["apiProject"] = profile.ApiProject,
                ["analysisLevel"] = manifest?.AnalysisLevel,
                ["manifestHash"] = manifest?.ManifestHash,
                ["swagger"] = profile.Swagger.Type == "none" ? "未配置" : $"{profile.Swagger.Type}: {profile.Swagger.Location}",
                ["swaggerHash"] = diff?.SwaggerHash,
            },
            ["stages"] = new JsonObject
            {
                ["sourceComments"] = new JsonObject
                {
                    ["status"] = task.Stages.Source.Status,
                    ["coverage"] = JsonDefaults.ToNode(scan?.Coverage),
                    ["unresolvedSymbols"] = scan?.UnresolvedSymbols.Count ?? 0,
                    ["unresolvedCalls"] = evidence?.Coverage.Sum(c => c.Unresolved.Count) ?? 0,
                    ["drafts"] = new JsonObject
                    {
                        ["total"] = drafts.Candidates.Count,
                        ["byState"] = JsonDefaults.ToNode(drafts.Candidates.GroupBy(c => c.ReviewState).ToDictionary(g => g.Key, g => g.Count())),
                        ["byBasis"] = JsonDefaults.ToNode(drafts.Candidates.GroupBy(c => c.Basis).ToDictionary(g => g.Key, g => g.Count())),
                        ["bySource"] = JsonDefaults.ToNode(drafts.Candidates.GroupBy(c => c.Source).ToDictionary(g => g.Key, g => g.Count())),
                        ["applied"] = drafts.Candidates.Count(c => c.AppliedAt is not null),
                    },
                    ["patchExported"] = task.Stages.Source.PatchExported,
                    ["commentPlan"] = commentPlan is null ? null : new JsonObject
                    {
                        ["status"] = commentPlan.Status,
                        ["files"] = JsonDefaults.ToNode(commentPlan.ApplyResults.Select(r => new { r.RelativePath, r.State, r.Message })),
                        ["skipped"] = commentPlan.Skipped.Count,
                    },
                },
                ["generatedDocs"] = new JsonObject
                {
                    ["status"] = task.Stages.Generated.Status,
                    ["message"] = task.Stages.Generated.Message,
                    ["entries"] = propagation?.Entries.Count ?? 0,
                    ["inSource"] = propagation?.Entries.Count(e => e.SourceState == "written") ?? 0,
                    ["inXml"] = propagation?.Entries.Count(e => e.XmlState == "present") ?? 0,
                    ["inSwagger"] = propagation?.Entries.Count(e => e.SwaggerState == "present") ?? 0,
                },
                ["apifox"] = new JsonObject
                {
                    ["status"] = task.Stages.Remote.Status,
                    ["structureSource"] = diff?.StructureSource,
                    ["snapshotComplete"] = diff?.SnapshotComplete,
                    ["endpoints"] = JsonDefaults.ToNode(diff?.Endpoints.GroupBy(e => e.Status).ToDictionary(g => g.Key, g => g.Count())),
                    ["models"] = JsonDefaults.ToNode(diff?.Models.GroupBy(m => m.Status).ToDictionary(g => g.Key, g => g.Count())),
                    ["qualityIssues"] = diff?.Quality.Count ?? 0,
                    ["plan"] = plan is null ? null : new JsonObject
                    {
                        ["status"] = plan.Status,
                        ["actions"] = JsonDefaults.ToNode(plan.Actions.Select(a => new { a.ResourceKey, a.Kind, a.RemoteId, a.CreatedId, a.State, a.Message, a.Changes })),
                        ["outOfScopeImpact"] = JsonDefaults.ToNode(plan.OutOfScopeImpact),
                        ["preservedSettings"] = JsonDefaults.ToNode(plan.PreservedSettings),
                    },
                },
                ["businessRuntime"] = new JsonObject
                {
                    ["status"] = "notExecuted",
                    ["message"] = "第一版只核对文档，未调用业务接口、未读写业务数据库。",
                },
            },
            ["changes"] = JsonDefaults.ToNode(diff?.Endpoints.Where(e => e.Status is "new" or "structure" or "docs").Select(e => new
            {
                e.Key,
                e.Status,
                e.RemoteId,
                incompatible = diff.Items.Where(i => i.ResourceKey == e.Key && i.Risk is "incompatible" or "manual").Select(i => $"{i.Location}：{i.Reason}").ToList(),
                structure = diff.Items.Where(i => i.ResourceKey == e.Key && i.Category == "structure" && i.Change != "info").Select(i => $"{i.Location} {i.Field} {i.Change}").Take(50).ToList(),
                docs = e.DocChanges.Where(c => c.Decision is "fill" or "useTarget" or "supplement").Select(c => $"{c.Location}：{c.FinalValue}").ToList(),
            })),
            ["unfinished"] = unfinished,
            // 四句固定说明的单一落点在 ReportRender.Limits（排版检查直接量同一份文字）。
            ["limits"] = new JsonArray(ReportRender.Limits.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
        };
        return (JsonObject)Redactor.RedactNode(report)!;
    }

    /// <summary>报告 Markdown 源文：渲染在 <see cref="ReportRender.Markdown"/>（无依赖单一落点，测试工程共享编译）。</summary>
    public string Markdown(JsonObject report) => ReportRender.Markdown(report);

    /// <summary>前端交接说明：渲染在 <see cref="ReportRender.Handoff"/>。</summary>
    public string Handoff(JsonObject report) => ReportRender.Handoff(report);

    /// <summary>Markdown → HTML 正文：渲染在 <see cref="ReportRender.Html"/>。</summary>
    public string Html(string markdown) => ReportRender.Html(markdown);
}
