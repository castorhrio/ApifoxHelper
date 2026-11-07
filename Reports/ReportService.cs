using System.Net;
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
            ["limits"] = new JsonArray(
                "分析不运行 MSBuild：条件属性、Import、源生成器未评估，相关缺口见扫描警告。",
                "动态派发、多实现 DI、反射与外部程序集中的逻辑未展开，相关结论需人工确认。",
                "用例脚本等任意文本中的模型依赖未分析，需人工复核。",
                "本地锁不能阻止其他用户同时修改 Apifox；执行前指纹核对后仍存在竞态窗口。"),
        };
        return (JsonObject)Redactor.RedactNode(report)!;
    }

    public string Markdown(JsonObject report)
    {
        var sb = new StringBuilder();
        var task = report["task"]!;
        var target = report["target"]!;
        var stages = report["stages"]!;
        sb.AppendLine($"# {task["name"]} — Apifox 助手报告").AppendLine();
        sb.AppendLine($"- 目标：{target["projectName"] ?? "（未命名项目）"} / 项目 ID {target["projectId"]} / 分支 {target["branch"]}");
        sb.AppendLine($"- 来源：{report["source"]!["apiProject"]}，分析等级 {report["source"]!["analysisLevel"]}，Swagger {report["source"]!["swagger"]}");
        sb.AppendLine($"- 版本备注：{task["sourceVersionNote"]}；工具 {task["toolVersion"]}；CLI {task["cliVersion"] ?? "未使用"}").AppendLine();
        sb.AppendLine("## 分阶段结果").AppendLine();
        sb.AppendLine("| 阶段 | 状态 | 说明 |").AppendLine("| --- | --- | --- |");
        var coverage = stages["sourceComments"]!["coverage"];
        sb.AppendLine($"| 源码注释完整性 | {stages["sourceComments"]!["status"]} | 有效 {coverage?["valid"]}+继承 {coverage?["inheritDoc"]} / {coverage?["total"]}，未解析符号 {stages["sourceComments"]!["unresolvedSymbols"]} |");
        sb.AppendLine($"| XML/Swagger 传播 | {stages["generatedDocs"]!["status"]} | 源码 {stages["generatedDocs"]!["inSource"]}、XML {stages["generatedDocs"]!["inXml"]}、Swagger {stages["generatedDocs"]!["inSwagger"]} / {stages["generatedDocs"]!["entries"]} |");
        sb.AppendLine($"| Apifox 一致性 | {stages["apifox"]!["status"]} | 结构来源 {stages["apifox"]!["structureSource"] ?? "未检查"} |");
        sb.AppendLine($"| 业务运行验证 | 未执行 | {stages["businessRuntime"]!["message"]} |").AppendLine();

        if (stages["apifox"]!["plan"] is JsonObject plan)
        {
            sb.AppendLine("## 实际写入").AppendLine();
            sb.AppendLine($"计划状态：{plan["status"]}").AppendLine();
            foreach (var action in plan["actions"] as JsonArray ?? new JsonArray())
            {
                sb.AppendLine($"- {action!["kind"]} {action["resourceKey"]}（ID {action["remoteId"] ?? action["createdId"]}）：{action["state"]} {action["message"]}");
            }
            if (plan["outOfScopeImpact"] is JsonArray { Count: > 0 } impact)
            {
                sb.AppendLine().AppendLine("### 共享模型对范围外接口的影响").AppendLine();
                foreach (var entry in impact) sb.AppendLine($"- {entry!["key"]}（{entry["name"]}，ID {entry["id"]}）");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## 接口变化").AppendLine();
        foreach (var change in report["changes"] as JsonArray ?? new JsonArray())
        {
            sb.AppendLine($"### {change!["key"]}（{change["status"]}）").AppendLine();
            foreach (var item in change["incompatible"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- ⚠ {item}");
            foreach (var item in change["structure"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- {item}");
            foreach (var item in change["docs"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- 说明：{item}");
            sb.AppendLine();
        }
        sb.AppendLine("## 未完成事项").AppendLine();
        var unfinished = report["unfinished"] as JsonArray ?? new JsonArray();
        if (unfinished.Count == 0) sb.AppendLine("无。");
        foreach (var item in unfinished) sb.AppendLine($"- {item}");
        sb.AppendLine().AppendLine("## 检查覆盖范围与限制").AppendLine();
        foreach (var item in report["limits"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- {item}");
        sb.AppendLine().AppendLine("> 验证边界：只核对文档，未执行业务接口。");
        return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    /// <summary>前端交接说明：只列出前端关心的接口与字段变化。</summary>
    public string Handoff(JsonObject report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 接口变更交接：{report["task"]!["name"]}").AppendLine();
        sb.AppendLine($"目标文档：{report["target"]!["projectName"]} / 分支 {report["target"]!["branch"]}；同步状态：{report["stages"]!["apifox"]!["status"]}").AppendLine();
        foreach (var change in report["changes"] as JsonArray ?? new JsonArray())
        {
            sb.AppendLine($"## {change!["key"]}").AppendLine();
            var incompatible = change["incompatible"] as JsonArray ?? new JsonArray();
            if (incompatible.Count > 0)
            {
                sb.AppendLine("需要前端注意：");
                foreach (var item in incompatible) sb.AppendLine($"- {item}");
            }
            foreach (var item in (change["structure"] as JsonArray ?? new JsonArray()).Take(20)) sb.AppendLine($"- {item}");
            foreach (var item in change["docs"] as JsonArray ?? new JsonArray()) sb.AppendLine($"- 说明：{item}");
            sb.AppendLine();
        }
        sb.AppendLine("> 说明：本交接只代表文档变化，接口行为未经实际调用验证。");
        return sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");
    }

    public string Html(string markdown)
    {
        var body = new StringBuilder();
        foreach (var line in markdown.Split('\n'))
        {
            var text = WebUtility.HtmlEncode(line.TrimEnd('\r'));
            if (line.StartsWith("# ", StringComparison.Ordinal)) body.Append("<h1>").Append(text[2..]).Append("</h1>");
            else if (line.StartsWith("## ", StringComparison.Ordinal)) body.Append("<h2>").Append(text[3..]).Append("</h2>");
            else if (line.StartsWith("### ", StringComparison.Ordinal)) body.Append("<h3>").Append(text[4..]).Append("</h3>");
            else if (line.StartsWith("- ", StringComparison.Ordinal)) body.Append("<li>").Append(text[2..]).Append("</li>");
            else if (line.Trim().Length == 0) body.Append("<br>");
            else body.Append("<p>").Append(text).Append("</p>");
            body.Append('\n');
        }
        return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\">"
            + "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">"
            + "<title>Apifox 助手报告</title><style>body{font-family:system-ui,'Microsoft YaHei',sans-serif;max-width:960px;margin:24px auto;line-height:1.6;color:#1f2937}h1{font-size:22px}h2{font-size:18px;border-bottom:1px solid #e5e7eb}li{margin-left:18px}</style>"
            + "</head><body>" + body + "</body></html>";
    }
}
