using ApifoxHelper.Apifox;
using ApifoxHelper.Contracts;
using ApifoxHelper.Profiles;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Sync;

/// <summary>
/// 同步计划：冻结选中接口、必要模型、写入内容与前置指纹。接口是结构同步的最小单元；
/// 未确认的高风险变化、未解决的说明冲突、未确认的模型绑定都会把相关接口排除在计划之外。
/// 计划阶段只读远端（重新 get 核对），不写入。
/// </summary>
public sealed class SyncPlanService(TaskStore tasks, FileStore store, ApifoxCli cli, CompatService compat)
{
    public const string NewModelPrefix = "#/definitions/__new__:";

    public async Task<SyncPlan> CreateAsync(string taskId, List<string> endpointKeys, JobContext job)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        if (task.OfflineMode) throw new UserException("离线检查不能同步。");
        var projectId = profile.Apifox.ProjectId ?? throw new UserException("配置中没有 Apifox 项目 ID。");
        var diff = tasks.Read<CheckResult>(taskId, TaskFiles.Diff) ?? throw new UserException("请先完成契约检查。");
        if (diff.TaskRevision != task.Revision) throw new UserException("检查之后任务内容有变化（说明、决策或源码），请重新检查后再生成计划。", 409);
        if (endpointKeys.Count == 0) throw new UserException("请至少选择一个接口。");

        var version = await cli.VersionAsync(profile.Apifox, job.Token);
        if (compat.Verified(version) is null)
        {
            throw new UserException($"当前 Apifox CLI {version} 尚未通过写入兼容探测，只允许只读检查。请先在“项目设置”中对测试接口运行写入探测。");
        }
        if (!diff.SnapshotComplete) throw new UserException("远端分支概览不完整，不能生成同步计划。");

        var decisions = tasks.Read<DecisionStore>(taskId, TaskFiles.Decisions) ?? new DecisionStore();
        var supplements = tasks.Read<SupplementStore>(taskId, TaskFiles.Supplement) ?? new SupplementStore();
        var mappings = FileStore.ReadJson<ModelMappingStore>(CheckService.MappingPath(store, projectId, profile.Apifox.BranchName))
            ?? new ModelMappingStore { ProjectId = projectId, Branch = profile.Apifox.BranchName };
        var plan = new SyncPlan
        {
            Id = Ids.New("sp"),
            TaskId = taskId,
            TaskRevision = task.Revision,
            ProjectId = projectId,
            ProjectName = profile.Apifox.ProjectName,
            Branch = profile.Apifox.BranchName,
            CliVersion = version,
            SwaggerHash = diff.SwaggerHash,
            SupplementHash = Canonical.HashOf(supplements),
            CreatedAt = DateTimeOffset.Now,
            PreservedSettings = ["目录", "鉴权与安全方案", "服务", "公共参数", "前后置脚本", "Mock", "用例", "已有示例", "远端独有的头/Cookie 参数与响应定义"],
        };
        var payloadDir = tasks.FilePath(taskId, TaskFiles.PlanPayloads);
        if (Directory.Exists(payloadDir)) Directory.Delete(payloadDir, recursive: true);
        Directory.CreateDirectory(payloadDir);

        var modelChecks = diff.Models.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var modelActions = new Dictionary<string, PlanAction>(StringComparer.Ordinal);
        var endpointActions = new List<PlanAction>();
        var neededModels = new HashSet<string>(StringComparer.Ordinal);
        var selected = endpointKeys.Distinct(StringComparer.Ordinal).ToList();

        for (var i = 0; i < selected.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            var key = selected[i];
            job.Report((double)i / Math.Max(1, selected.Count), "生成计划：" + key);
            var check = diff.Endpoints.FirstOrDefault(e => e.Key == key);
            if (check is null)
            {
                plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = "检查结果中没有该接口" });
                continue;
            }
            var reason = ExclusionReason(check, diff, decisions);
            if (reason is not null)
            {
                plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = reason });
                continue;
            }

            var structural = diff.Items.Where(d => d.ResourceKey == key && d.Category == "structure" && d.Change != "info").ToList();
            var docs = check.DocChanges.Where(c => c.Decision is "fill" or "useTarget" or "supplement" && TextUtil.NormalizeSpace(c.FinalValue) != TextUtil.NormalizeSpace(c.RemoteValue)).ToList();
            var refs = new HashSet<string>(StringComparer.Ordinal);
            var notes = new List<string>();
            string? RefFor(string name)
            {
                refs.Add(name);
                return RefForModel(name, mappings, decisions, modelChecks);
            }

            PlanAction action;
            if (check.Remote is null || check.RemoteId is null)
            {
                if (check.RawTarget is null)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = "缺少 Swagger 结构，无法创建新接口" });
                    continue;
                }
                var payload = BuildCreateEndpoint(check, profile.NewEndpoint, RefFor, notes);
                action = NewAction(plan, "createEndpoint", key, null, payload, payloadDir, "endpoint-create");
                action.Changes.Add("新建接口：" + key);
                action.ManagedFields = payload.Select(p => p.Key).ToList();
            }
            else
            {
                // 计划阶段重新读取远端；与检查时不一致说明期间有人修改，必须重新检查。
                var fresh = await cli.GetEndpointAsync(profile.Apifox, projectId, profile.Apifox.BranchName, check.RemoteId.Value, job.Token);
                if (!fresh.Ok || fresh.Data is not JsonObject before)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = "读取远端接口失败：" + fresh.Describe() });
                    continue;
                }
                if (NativeUtil.Hash(before) != check.RemoteDetailHash)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = "检查之后远端接口已被修改，请重新检查" });
                    continue;
                }
                var payload = BuildUpdateEndpoint(check, before, structural, docs, RefFor, notes, out var managed, out var changes);
                if (managed.Count == 0)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = key, Reason = "无需修改（结构差异只来自共享模型，由模型动作处理）" });
                    neededModels.UnionWith(refs);
                    continue;
                }
                action = NewAction(plan, "updateEndpoint", key, check.RemoteId, payload, payloadDir, "endpoint-update");
                action.BeforeHash = NativeUtil.Hash(before);
                action.ProtectedHash = NativeUtil.ProtectedHash(before, managed);
                action.ManagedFields = managed;
                action.Changes.AddRange(changes);
            }
            action.Notes.AddRange(notes.Distinct());
            action.ModelRefs = refs.OrderBy(r => r, StringComparer.Ordinal).ToList();
            neededModels.UnionWith(refs);
            endpointActions.Add(action);
        }

        // 模型：被写入内容引用的新模型需要先创建；内容变化的已映射模型需要更新（确认影响范围后）。
        var changedModels = diff.Models.Where(m => m.Status == "changed" && diff.Endpoints.Any(e => selected.Contains(e.Key) && m.UsedBy.Contains(e.Key))).Select(m => m.Name);
        foreach (var name in Closure(neededModels.Concat(changedModels), modelChecks))
        {
            if (!modelChecks.TryGetValue(name, out var model)) continue;
            var reason = ModelExclusion(model, decisions, mappings);
            if (reason is not null)
            {
                plan.Excluded.Add(new PlanExclusion { ResourceKey = "model:" + name, Reason = reason });
                continue;
            }
            var notes = new List<string>();
            string? RefFor(string n) => RefForModel(n, mappings, decisions, modelChecks);
            var remoteId = RemoteIdOf(name, mappings, decisions);
            if (remoteId is null)
            {
                var payload = new JsonObject
                {
                    ["name"] = name,
                    ["jsonSchema"] = NativeUtil.ToApifox(model.Source, RefFor, notes),
                    ["folderId"] = 0,
                };
                if (model.Source?["description"]?.ToString() is { Length: > 0 } description)
                {
                    payload["description"] = TextUtil.Truncate(description, 490);
                }
                var action = NewAction(plan, "createModel", "model:" + name, null, payload, payloadDir, "schema-create");
                action.ModelName = name;
                action.Changes.Add("新建模型 " + name);
                action.Notes.AddRange(notes);
                modelActions[name] = action;
            }
            else if (model.Status == "changed")
            {
                var fresh = await cli.GetSchemaAsync(profile.Apifox, projectId, profile.Apifox.BranchName, remoteId.Value, job.Token);
                if (!fresh.Ok || fresh.Data is not JsonObject before)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = "model:" + name, Reason = "读取远端模型失败：" + fresh.Describe() });
                    continue;
                }
                if (model.RemoteDetailHash.Length > 0 && NativeUtil.Hash(before) != model.RemoteDetailHash)
                {
                    plan.Excluded.Add(new PlanExclusion { ResourceKey = "model:" + name, Reason = "检查之后远端模型已被修改，请重新检查" });
                    continue;
                }
                var desired = (JsonObject)model.Source!.DeepClone();
                ApplyModelDocDecisions(desired, model, decisions);
                var jsonSchema = NativeUtil.MergeOnto(before["jsonSchema"], NativeUtil.ToApifox(desired, RefFor, notes));
                var payload = new JsonObject { ["jsonSchema"] = jsonSchema };
                var action = NewAction(plan, "updateModel", "model:" + name, remoteId, payload, payloadDir, "schema-update");
                action.ModelName = name;
                action.BeforeHash = NativeUtil.Hash(before);
                action.ManagedFields = ["jsonSchema"];
                action.ProtectedHash = NativeUtil.ProtectedHash(before, action.ManagedFields);
                action.Changes.Add($"更新模型 {name}（被范围外 {model.ImpactOutOfScope.Count} 个接口引用）");
                action.Notes.AddRange(notes);
                plan.OutOfScopeImpact.AddRange(model.ImpactOutOfScope);
                modelActions[name] = action;
            }
        }

        // 依赖：模型按依赖顺序在前，接口在后；新模型的引用在执行时替换为真实 ID。
        foreach (var action in modelActions.Values)
        {
            var model = modelChecks[action.ModelName!];
            action.DependsOn = model.DependsOn.Where(modelActions.ContainsKey).Select(d => modelActions[d].ActionId).ToList();
        }
        var excludedModels = plan.Excluded.Where(x => x.ResourceKey.StartsWith("model:", StringComparison.Ordinal)).Select(x => x.ResourceKey[6..]).ToHashSet(StringComparer.Ordinal);
        foreach (var action in endpointActions)
        {
            // 写入内容引用的模型（含间接引用）有阻断或未纳入计划时，接口也不能写入，避免产生悬空或未核对的引用。
            var blockedDependency = Closure(action.ModelRefs, modelChecks).FirstOrDefault(n => excludedModels.Contains(n) || (modelChecks.TryGetValue(n, out var m) && m.Status is "blocked" or "unknown"));
            if (blockedDependency is not null)
            {
                plan.Excluded.Add(new PlanExclusion { ResourceKey = action.ResourceKey, Reason = $"引用的模型 {blockedDependency} 存在阻断项或未纳入计划" });
                action.State = "excluded";
                continue;
            }
            var payloadText = File.ReadAllText(action.PayloadFile, Encoding.UTF8);
            action.DependsOn = modelActions.Values.Where(m => payloadText.Contains(NewModelPrefix + m.ModelName + "\"", StringComparison.Ordinal) || m.Kind == "updateModel").Select(m => m.ActionId).ToList();
            var unresolved = Regex.Matches(payloadText, Regex.Escape(NewModelPrefix) + "([^\"]+)\"").Select(m => m.Groups[1].Value).Where(n => !modelActions.ContainsKey(n)).Distinct().ToList();
            if (unresolved.Count > 0)
            {
                plan.Excluded.Add(new PlanExclusion { ResourceKey = action.ResourceKey, Reason = "依赖的模型未能纳入计划：" + string.Join(", ", unresolved) });
                action.State = "excluded";
            }
        }
        endpointActions.RemoveAll(a => a.State == "excluded");
        plan.Actions = TopologicalModels(modelActions.Values.ToList()).Concat(endpointActions).ToList();

        // 写入文件先过 CLI Schema 校验；占位引用是字符串，不影响结构校验。
        foreach (var action in plan.Actions)
        {
            var (valid, message) = await cli.ValidateAsync(profile.Apifox, action.ValidationKey, action.PayloadFile, job.Token);
            action.Validation = message;
            if (!valid) plan.Warnings.Add($"{action.ResourceKey} 未通过 CLI Schema 校验：{message}");
        }
        if (plan.Actions.Any(a => !a.Validation.Equals("通过", StringComparison.Ordinal)))
        {
            plan.Status = "expired";
            plan.Message = "有写入文件未通过 CLI Schema 校验，计划不可执行。";
        }
        else if (plan.Actions.Count == 0)
        {
            plan.Status = "expired";
            plan.Message = "没有可执行的动作。";
        }

        plan.Hash = Canonical.HashOf(new
        {
            plan.ProjectId,
            plan.Branch,
            plan.CliVersion,
            plan.SwaggerHash,
            plan.SupplementHash,
            actions = plan.Actions.Select(a => new { a.Kind, a.ResourceKey, a.RemoteId, a.BeforeHash, a.ProtectedHash, a.DesiredHash, a.ManagedFields, a.DependsOn }),
        });

        using (await KeyedLocks.AcquireAsync("task:" + taskId, job.Token))
        {
            var existing = tasks.Read<SyncPlan>(taskId, TaskFiles.Plan);
            if (existing is { Status: "executing" }) throw new UserException("已有同步正在执行。", 409);
            tasks.Write(taskId, TaskFiles.Plan, plan);
            var current = tasks.Get(taskId);
            current.Stages.Remote.Status = plan.Status == "awaitingConfirm" ? "awaitingConfirm" : current.Stages.Remote.Status;
            tasks.Save(current);
        }
        return plan;
    }

    private static string? ExclusionReason(EndpointCheck check, CheckResult diff, DecisionStore decisions)
    {
        if (check.Status == "blocked") return "存在阻断项：" + string.Join("；", check.Blockers);
        if (check.Status == "sourceOnly") return "Swagger 与 Apifox 中都没有该接口";
        if (check.Status == "unchanged") return "无需修改";
        var undecided = diff.Items.Where(d => d.ResourceKey == check.Key && d.NeedsDecision && decisions.Items.GetValueOrDefault(d.Id) != "accepted").ToList();
        if (undecided.Count > 0) return $"有 {undecided.Count} 项潜在不兼容/需人工判断的变化未逐项确认";
        var conflicts = check.DocChanges.Count(c => c.Decision == "conflict");
        if (conflicts > 0) return $"有 {conflicts} 处说明冲突未选择";
        return null;
    }

    private static string? ModelExclusion(ModelCheck model, DecisionStore decisions, ModelMappingStore mappings)
    {
        var binding = decisions.ModelBindings.GetValueOrDefault(model.Name);
        if (model.MappingState == "ambiguous" && binding is null) return "远端有多个同名模型，需要人工指定绑定";
        if (model.MappingState == "candidate" && binding is null && !mappings.Models.ContainsKey(model.Name)) return "远端存在同名模型，首次绑定未确认";
        var blockers = model.Blockers.Where(b => !b.Contains("首次绑定", StringComparison.Ordinal) && !b.Contains("需人工指定绑定", StringComparison.Ordinal)).ToList();
        if (blockers.Count > 0) return string.Join("；", blockers);
        if (model.Status == "changed" && decisions.Items.GetValueOrDefault("model:" + model.Name) != "accepted")
        {
            return $"共享模型更新需要确认（范围外受影响接口 {model.ImpactOutOfScope.Count} 个）";
        }
        if (model.DocChanges.Any(c => c.Decision == "conflict" && !decisions.DocChoices.ContainsKey(c.Id))) return "模型说明存在未选择的冲突";
        return null;
    }

    private static long? RemoteIdOf(string name, ModelMappingStore mappings, DecisionStore decisions)
    {
        if (mappings.Models.TryGetValue(name, out var mapping)) return mapping.RemoteId;
        if (decisions.ModelBindings.TryGetValue(name, out var binding) && long.TryParse(binding, out var id)) return id;
        return null;
    }

    private static string? RefForModel(string name, ModelMappingStore mappings, DecisionStore decisions, Dictionary<string, ModelCheck> models)
    {
        var id = RemoteIdOf(name, mappings, decisions);
        if (id is not null) return "#/definitions/" + id.Value.ToString(CultureInfo.InvariantCulture);
        return models.ContainsKey(name) ? NewModelPrefix + name : null;
    }

    private static IEnumerable<string> Closure(IEnumerable<string> names, Dictionary<string, ModelCheck> models)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(names);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!result.Add(name) || !models.TryGetValue(name, out var model)) continue;
            foreach (var dependency in model.DependsOn) queue.Enqueue(dependency);
        }
        return result;
    }

    private static List<PlanAction> TopologicalModels(List<PlanAction> actions)
    {
        var ordered = new List<PlanAction>();
        var done = new HashSet<string>(StringComparer.Ordinal);
        var remaining = actions.ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(a => a.DependsOn.All(done.Contains)).ToList();
            if (ready.Count == 0)
            {
                // 已有模型之间的递归引用：互不依赖创建顺序，按原顺序执行。
                ready = [remaining[0]];
            }
            foreach (var action in ready)
            {
                ordered.Add(action);
                done.Add(action.ActionId);
                remaining.Remove(action);
            }
        }
        return ordered;
    }

    private static void ApplyModelDocDecisions(JsonObject desired, ModelCheck model, DecisionStore decisions)
    {
        foreach (var change in model.DocChanges)
        {
            var choice = decisions.DocChoices.GetValueOrDefault(change.Id);
            var keepRemote = change.Decision == "keepRemote" || (change.Decision == "conflict" && choice != "useTarget");
            if (!keepRemote) continue;
            if (change.Location == "description") desired.Remove("description");
            else if (change.Location.StartsWith("properties.", StringComparison.Ordinal) && desired["properties"] is JsonObject properties
                     && properties[change.Location["properties.".Length..]] is JsonObject property)
            {
                // 去掉目标说明后，合并时会保留远端说明。
                property.Remove("description");
            }
        }
    }

    private static PlanAction NewAction(SyncPlan plan, string kind, string resource, long? remoteId, JsonObject payload, string payloadDir, string validationKey)
    {
        var action = new PlanAction
        {
            ActionId = $"a{plan.Actions.Count + Directory.GetFiles(payloadDir).Length + 1:D3}",
            Kind = kind,
            ResourceKey = resource,
            RemoteId = remoteId,
            ValidationKey = validationKey,
        };
        action.PayloadFile = Path.Combine(payloadDir, action.ActionId + ".json");
        FileStore.WriteJson(action.PayloadFile, payload);
        action.DesiredHash = Canonical.Hash(payload);
        return action;
    }

    private static JsonObject BuildCreateEndpoint(EndpointCheck check, NewEndpointTemplate template, Func<string, string?> refFor, List<string> notes)
    {
        var raw = check.RawTarget!;
        var payload = new JsonObject
        {
            ["name"] = SchemaNormalizer.Text(raw["summary"]) ?? check.Key,
            ["type"] = "http",
            ["method"] = check.Method.ToLowerInvariant(),
            ["path"] = check.Path,
            ["status"] = template.Status,
            ["description"] = SchemaNormalizer.Text(raw["description"]) ?? "",
            ["tags"] = new JsonArray(template.Tags.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray()),
            ["parameters"] = BuildParameters(raw, null, null, refFor, notes, template.HeaderParameters),
            ["requestBody"] = BuildRequestBody(raw, null, refFor, notes),
            ["responses"] = BuildResponses(raw, null, null, refFor, notes),
        };
        if (template.FolderId is not null) payload["folderId"] = template.FolderId.Value;
        if (!string.IsNullOrWhiteSpace(template.ServerId)) payload["serverId"] = template.ServerId;
        if (template.Auth is not null) payload["auth"] = template.Auth.DeepClone();
        if (template.SecurityScheme is not null) payload["securityScheme"] = template.SecurityScheme.DeepClone();
        return payload;
    }

    /// <summary>
    /// 更新内容只包含需要改动的顶层字段，每个字段基于远端完整内容构造（CLI 对数组和嵌套对象整体替换）：
    /// 未变化的参数/响应原样保留（含 id、示例等原生字段），只改有结构差异或说明变化的项。
    /// </summary>
    private static JsonObject BuildUpdateEndpoint(EndpointCheck check, JsonObject before, List<DiffItem> structural, List<DocChange> docs, Func<string, string?> refFor, List<string> notes,
        out List<string> managed, out List<string> changes)
    {
        managed = new List<string>();
        changes = new List<string>();
        var payload = new JsonObject();
        var docByLocation = docs.GroupBy(d => d.Location + "|" + d.Field).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        string? Final(string location, string field = "description") => docByLocation.TryGetValue(location + "|" + field, out var change) ? change.FinalValue : null;

        if (Final("operation", "name") is { } name)
        {
            payload["name"] = name;
            managed.Add("name");
            changes.Add("标题 → " + name);
        }
        if (Final("operation") is { } description)
        {
            payload["description"] = description;
            managed.Add("description");
            changes.Add("接口说明更新");
        }

        var structuralParams = structural.Where(d => Regex.IsMatch(d.Location, "^(path|query|header|cookie)\\.")).Select(d => string.Join('.', d.Location.Split('.').Take(2))).ToHashSet(StringComparer.Ordinal);
        var docParams = docs.Where(d => Regex.IsMatch(d.Location, "^(path|query|header|cookie)\\.")).Select(d => d.Location).ToHashSet(StringComparer.Ordinal);
        if (structuralParams.Count > 0 || docParams.Count > 0)
        {
            if (check.RawTarget is not null)
            {
                payload["parameters"] = BuildParameters(check.RawTarget, before["parameters"] as JsonObject, structuralParams, refFor, notes, new List<JsonObject>(), keepRemoteOnly: structuralParams.Count == 0);
            }
            else
            {
                var parameters = (JsonObject)(before["parameters"]?.DeepClone() ?? new JsonObject());
                foreach (var location in docParams)
                {
                    var parts = location.Split('.', 2);
                    var item = (parameters[parts[0]] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(p => p["name"]?.ToString() == parts[1]);
                    if (item is not null && Final(location) is { } text) item["description"] = text;
                }
                payload["parameters"] = parameters;
            }
            managed.Add("parameters");
            changes.AddRange(structuralParams.Select(p => "参数结构：" + p));
            changes.AddRange(docParams.Select(p => "参数说明：" + p));
        }

        var bodyStructural = structural.Any(d => d.Location.StartsWith("requestBody", StringComparison.Ordinal));
        var bodyDoc = Final("requestBody");
        if (bodyStructural || bodyDoc is not null)
        {
            JsonObject body;
            if (bodyStructural && check.RawTarget is not null)
            {
                body = BuildRequestBody(check.RawTarget, before["requestBody"] as JsonObject, refFor, notes);
            }
            else
            {
                body = (JsonObject)(before["requestBody"]?.DeepClone() ?? new JsonObject { ["type"] = "none" });
            }
            if (bodyDoc is not null) body["description"] = bodyDoc;
            payload["requestBody"] = body;
            managed.Add("requestBody");
            changes.Add(bodyStructural ? "请求体结构更新" : "请求体说明更新");
        }

        var responseCodes = structural.Where(d => d.Location.StartsWith("responses.", StringComparison.Ordinal)).Select(d => d.Location.Split('.')[1]).ToHashSet(StringComparer.Ordinal);
        var responseDocs = docs.Where(d => d.Location.StartsWith("responses.", StringComparison.Ordinal)).ToList();
        if (responseCodes.Count > 0 || responseDocs.Count > 0)
        {
            JsonArray responses;
            if (check.RawTarget is not null)
            {
                responses = BuildResponses(check.RawTarget, before["responses"] as JsonArray, responseCodes, refFor, notes);
            }
            else
            {
                responses = (JsonArray)(before["responses"]?.DeepClone() ?? new JsonArray());
            }
            foreach (var doc in responseDocs)
            {
                var code = doc.Location.Split('.')[1];
                var item = responses.OfType<JsonObject>().FirstOrDefault(r => r["code"]?.ToString() == code);
                if (item is not null && doc.FinalValue is not null) item["description"] = doc.FinalValue;
            }
            payload["responses"] = responses;
            managed.Add("responses");
            changes.AddRange(responseCodes.Select(c => $"响应 {c} 结构更新"));
            changes.AddRange(responseDocs.Select(d => $"{d.Location} 说明更新"));
        }
        return payload;
    }

    private static JsonObject BuildParameters(JsonObject raw, JsonObject? existing, HashSet<string>? structural, Func<string, string?> refFor, List<string> notes, List<JsonObject> templateHeaders, bool keepRemoteOnly = false)
    {
        var result = new JsonObject();
        foreach (var location in new[] { "path", "query", "header", "cookie" })
        {
            var existingItems = (existing?[location] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            var items = new JsonArray();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in (raw["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(p => p["in"]?.ToString() == location))
            {
                var name = parameter["name"]?.ToString() ?? "";
                used.Add(name);
                var current = existingItems.FirstOrDefault(p => p["name"]?.ToString() == name);
                JsonObject item;
                if (current is not null && structural is not null && !structural.Contains(location + "." + name))
                {
                    item = (JsonObject)current.DeepClone();
                }
                else
                {
                    item = (JsonObject?)current?.DeepClone() ?? new JsonObject { ["id"] = $"{name}#{items.Count}", ["enable"] = true };
                    var schema = NativeUtil.ToApifox(parameter["schema"], refFor, notes) as JsonObject ?? new JsonObject();
                    item["name"] = name;
                    item["required"] = parameter["required"] is JsonValue r && r.TryGetValue<bool>(out var required) && required;
                    item["type"] = ParameterType(schema);
                    item["schema"] = schema;
                    if (parameter["example"] is JsonNode example && item["example"] is null) item["example"] = example.ToString();
                }
                if (SchemaNormalizer.Text(parameter["description"]) is { } description) item["description"] = description;
                else item["description"] ??= "";
                items.Add(item);
            }
            // 远端独有的头/Cookie 参数保留；独有的 path/query 参数只有在接口结构整体确认后才删除。
            foreach (var current in existingItems.Where(p => !used.Contains(p["name"]?.ToString() ?? "")))
            {
                if (location is "header" or "cookie" || keepRemoteOnly) items.Add(current.DeepClone());
            }
            if (existing is null)
            {
                foreach (var header in templateHeaders.Where(_ => location == "header"))
                {
                    var name = header["name"]?.ToString();
                    if (name is null || used.Contains(name)) continue;
                    var copy = (JsonObject)header.DeepClone();
                    copy["id"] ??= $"{name}#{items.Count}";
                    copy["required"] ??= false;
                    items.Add(copy);
                }
            }
            result[location] = items;
        }
        return result;
    }

    private static string ParameterType(JsonObject schema)
    {
        var type = schema["type"] switch
        {
            JsonArray array => array.Select(t => t?.ToString()).FirstOrDefault(t => t != "null"),
            JsonNode node => node.ToString(),
            _ => null,
        };
        return type is "integer" or "number" or "boolean" or "array" ? type : "string";
    }

    private static JsonObject BuildRequestBody(JsonObject raw, JsonObject? existing, Func<string, string?> refFor, List<string> notes)
    {
        var body = (JsonObject?)existing?.DeepClone() ?? new JsonObject { ["parameters"] = new JsonArray(), ["additionalContentTypes"] = new JsonArray(), ["oasExtensions"] = "" };
        if (raw["requestBody"] is not JsonObject requestBody)
        {
            body["type"] = "none";
            body["required"] = false;
            body.Remove("jsonSchema");
            return body;
        }
        var (mediaType, media) = OpenApiReader.PickMedia(requestBody["content"] as JsonObject);
        var type = mediaType switch
        {
            null => "none",
            var m when m.Contains("json", StringComparison.OrdinalIgnoreCase) => "application/json",
            "multipart/form-data" or "application/x-www-form-urlencoded" or "application/xml" or "text/plain" or "application/octet-stream" => mediaType,
            _ => "application/json",
        };
        body["type"] = type;
        body["required"] = requestBody["required"] is JsonValue r && r.TryGetValue<bool>(out var required) && required;
        if (SchemaNormalizer.Text(requestBody["description"]) is { } description) body["description"] = description;
        if (type is "multipart/form-data" or "application/x-www-form-urlencoded")
        {
            var schema = media?["schema"] as JsonObject ?? new JsonObject();
            var requiredNames = (schema["required"] as JsonArray)?.Select(x => x?.ToString()).ToHashSet() ?? new HashSet<string?>();
            var fields = new JsonArray();
            var existingFields = (existing?["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
            foreach (var (name, propertyNode) in schema["properties"] as JsonObject ?? new JsonObject())
            {
                var propertySchema = NativeUtil.ToApifox(propertyNode, refFor, notes) as JsonObject ?? new JsonObject();
                var field = (JsonObject?)existingFields.FirstOrDefault(f => f["name"]?.ToString() == name)?.DeepClone() ?? new JsonObject { ["id"] = $"{name}#{fields.Count}", ["enable"] = true };
                field["name"] = name;
                field["required"] = requiredNames.Contains(name);
                field["type"] = propertySchema["format"]?.ToString() == "binary" ? "file" : ParameterType(propertySchema);
                field["schema"] = propertySchema;
                if (SchemaNormalizer.Text(propertyNode?["description"]) is { } fieldDescription) field["description"] = fieldDescription;
                fields.Add(field);
            }
            body["parameters"] = fields;
            body.Remove("jsonSchema");
        }
        else
        {
            body["jsonSchema"] = NativeUtil.MergeOnto(existing?["jsonSchema"], NativeUtil.ToApifox(media?["schema"], refFor, notes));
        }
        return body;
    }

    private static JsonArray BuildResponses(JsonObject raw, JsonArray? existing, HashSet<string>? codesToRewrite, Func<string, string?> refFor, List<string> notes)
    {
        var result = (JsonArray?)existing?.DeepClone() ?? new JsonArray();
        foreach (var (code, node) in raw["responses"] as JsonObject ?? new JsonObject())
        {
            if (node is not JsonObject response) continue;
            var current = result.OfType<JsonObject>().FirstOrDefault(r => r["code"]?.ToString() == code);
            if (current is not null && codesToRewrite is not null && !codesToRewrite.Contains(code))
            {
                continue;
            }
            var item = current ?? new JsonObject { ["code"] = code, ["name"] = code == "200" ? "成功" : "", ["headers"] = new JsonArray() };
            var (mediaType, media) = OpenApiReader.PickMedia(response["content"] as JsonObject);
            if (media is not null)
            {
                item["contentType"] = "json";
                item["mediaType"] = mediaType is not null && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ? "application/json" : mediaType;
                item["jsonSchema"] = NativeUtil.MergeOnto(current?["jsonSchema"], NativeUtil.ToApifox(media["schema"], refFor, notes));
            }
            else
            {
                item["contentType"] = "noContent";
            }
            if (SchemaNormalizer.Text(response["description"]) is { } description) item["description"] = description;
            if (current is null) result.Add(item);
        }
        return result;
    }
}
