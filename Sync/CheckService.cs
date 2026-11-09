using ApifoxHelper.Apifox;
using ApifoxHelper.Comments;
using ApifoxHelper.Contracts;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Sync;

/// <summary>
/// 契约检查：冻结的 Swagger + 已确认响应契约 + 说明条目/人工补充 → 目标；Apifox 当前内容 → 远端；
/// 输出结构差异、说明合并结果、模型影响与质量问题。只读，不写远端。
/// </summary>
public sealed class CheckService(TaskStore tasks, FileStore store, ApifoxCli cli, RemoteSnapshotService snapshots, OpenApiSource openApi)
{
    public const string RemoteDetailsDir = "remote-check";

    public async Task<object> LoadSwaggerAsync(string taskId, CancellationToken ct)
    {
        var task = tasks.Get(taskId);
        var (text, _, info) = await openApi.LoadAsync(task.ProfileSnapshot.Swagger, task.ProfileSnapshot.WorkspaceRoot, ct);
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            FileStore.WriteText(tasks.FilePath(taskId, TaskFiles.SourceOpenApi), text);
            tasks.Write(taskId, "source.openapi.info.json", info);
            var current = tasks.Get(taskId);
            current.Stages.Remote.SourceHash = info.Hash;
            current.Stages.Remote.SourceKind = info.Kind;
            if (current.Stages.Remote.Status is "previewable" or "awaitingConfirm") current.Stages.Remote.Status = "expired";
            current.Revision++;
            tasks.Save(current);
        }
        return info;
    }

    public async Task<object> ImportOfflineRemoteAsync(string taskId, string text, CancellationToken ct)
    {
        var info = new OpenApiInfo { Kind = "offline", LoadedAt = DateTimeOffset.Now };
        OpenApiSource.Parse(text, info);
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            FileStore.WriteText(tasks.FilePath(taskId, "offline.remote.openapi.json"), text);
            var current = tasks.Get(taskId);
            current.OfflineMode = true;
            current.Revision++;
            tasks.Save(current);
        }
        return info;
    }

    public async Task<object> RunAsync(string taskId, JobContext job)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        var result = new CheckResult { ComputedAt = DateTimeOffset.Now, TaskRevision = task.Revision, Offline = task.OfflineMode };

        // 目标来源。
        JsonObject? swagger = null;
        var swaggerPath = tasks.FilePath(taskId, TaskFiles.SourceOpenApi);
        if (File.Exists(swaggerPath))
        {
            var info = new OpenApiInfo();
            swagger = OpenApiSource.Parse(await File.ReadAllTextAsync(swaggerPath, Encoding.UTF8, job.Token), info);
            result.SwaggerHash = info.Hash;
            if (!info.Supported) result.Problems.AddRange(info.Problems.Select(p => "Swagger：" + p));
        }
        result.StructureSource = task.OfflineMode ? "offline" : swagger is not null ? "swagger" : "remote";
        var scan = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols);
        var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts);
        var contracts = (tasks.Read<List<ResponseContract>>(taskId, TaskFiles.ResponseContract) ?? new List<ResponseContract>())
            .Where(c => c.ReviewState == "confirmed").ToDictionary(c => c.EndpointKey, StringComparer.Ordinal);
        var supplements = tasks.Read<SupplementStore>(taskId, TaskFiles.Supplement) ?? new SupplementStore();
        var decisions = tasks.Read<DecisionStore>(taskId, TaskFiles.Decisions) ?? new DecisionStore();
        var builder = new TargetBuilder(DocEntries.Build(scan, drafts), supplements, decisions);
        var swaggerOps = swagger is null ? new Dictionary<string, DocOperation>() : OpenApiReader.Read(swagger, result.Problems);
        var swaggerComponents = swagger?["components"]?["schemas"] as JsonObject ?? new JsonObject();

        // 远端。
        RemoteSnapshot? snapshot = null;
        var remoteOps = new Dictionary<string, (DocOperation Op, long Id, JsonObject Data)>(StringComparer.Ordinal);
        var models = new ConcurrentDictionary<long, JsonObject>();
        var mappings = LoadMappings(profile);
        if (task.OfflineMode)
        {
            var offlinePath = tasks.FilePath(taskId, "offline.remote.openapi.json");
            if (!File.Exists(offlinePath)) throw new UserException("离线检查需要先导入作为「远端」的 OpenAPI JSON。");
            var offline = OpenApiSource.Parse(await File.ReadAllTextAsync(offlinePath, Encoding.UTF8, job.Token), new OpenApiInfo());
            foreach (var (key, op) in OpenApiReader.Read(offline, result.Problems)) remoteOps[key] = (op, 0, new JsonObject());
            result.Problems.Add("离线检查：只比较结构，无法证明 Apifox 原生设置、重复接口与线上状态，同步不可用。");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profile.Apifox.ProjectId)) throw new UserException("配置中没有 Apifox 项目 ID。");
            var version = await cli.VersionAsync(profile.Apifox, job.Token);
            snapshot = await snapshots.TakeAsync(profile.Apifox, version, job);
            tasks.Write(taskId, TaskFiles.RemoteSnapshot, snapshot);
            result.SnapshotAt = snapshot.TakenAt;
            result.SnapshotComplete = snapshot.Complete;
            result.Problems.AddRange(snapshot.Problems);
            await tasks.UpdateAsync(taskId, t => t.CliVersion = version, job.Token);
        }

        // 范围内接口身份。
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var e in scan?.Endpoints ?? new List<EndpointInfo>()) keys.Add(e.Key);
        foreach (var op in swaggerOps.Values.Where(o => TaskStore.InScope(task, o.Key, o.Path))) keys.Add(op.Key);
        if (snapshot is not null)
        {
            result.UnmatchedRemote = snapshot.Endpoints
                .Where(e => TaskStore.InScope(task, e.Key, e.Path) && !keys.Contains(e.Key))
                .Select(e => $"{e.Key}（{e.Name}，ID {e.Id}）")
                .ToList();
        }

        // 逐个读取范围内远端接口详情（权威内容），并发受配置限制。
        if (snapshot is not null)
        {
            job.Report(0.75, "读取范围内接口详情");
            var detailsDir = tasks.FilePath(taskId, RemoteDetailsDir);
            Directory.CreateDirectory(detailsDir);
            var unique = keys
                .Select(k => (Key: k, Matches: snapshot.Endpoints.Where(e => e.Key == k).ToList()))
                .Where(x => x.Matches.Count == 1)
                .ToList();
            using var gate = new SemaphoreSlim(profile.Apifox.ReadConcurrency);
            var fetched = new ConcurrentDictionary<string, (long Id, JsonObject Data)>(StringComparer.Ordinal);
            var failures = new ConcurrentBag<string>();
            await Task.WhenAll(unique.Select(async x =>
            {
                await gate.WaitAsync(job.Token);
                try
                {
                    var id = x.Matches[0].Id;
                    var response = await cli.GetEndpointAsync(profile.Apifox, profile.Apifox.ProjectId!, profile.Apifox.BranchName, id, job.Token);
                    if (response.Ok && response.Data is JsonObject data)
                    {
                        fetched[x.Key] = (id, data);
                        FileStore.WriteJson(Path.Combine(detailsDir, id + ".json"), data);
                    }
                    else
                    {
                        failures.Add($"{x.Key}：{response.Describe()}");
                    }
                }
                finally
                {
                    gate.Release();
                }
            }));
            result.Problems.AddRange(failures.Select(f => "读取接口详情失败（不按不存在处理）：" + f));

            // 读取这些接口直接或间接引用的模型。
            job.Report(0.85, "读取引用的数据模型");
            var pending = new HashSet<long>(fetched.Values.SelectMany(v => RemoteSnapshotService.Refs(new JsonArray(v.Data["parameters"]?.DeepClone(), v.Data["requestBody"]?.DeepClone(), v.Data["responses"]?.DeepClone()))));
            foreach (var mapped in mappings.Models.Values) pending.Add(mapped.RemoteId);
            // 用户在本任务中确认绑定的模型也要读取，才能比较结构与说明。
            foreach (var binding in decisions.ModelBindings.Values)
            {
                if (long.TryParse(binding, NumberStyles.Integer, CultureInfo.InvariantCulture, out var boundId)) pending.Add(boundId);
            }
            var visited = new HashSet<long>();
            while (pending.Count > 0)
            {
                var batch = pending.Where(visited.Add).ToList();
                pending.Clear();
                await Task.WhenAll(batch.Select(async id =>
                {
                    await gate.WaitAsync(job.Token);
                    try
                    {
                        var response = await cli.GetSchemaAsync(profile.Apifox, profile.Apifox.ProjectId!, profile.Apifox.BranchName, id, job.Token);
                        if (response.Ok && response.Data is JsonObject data) models[id] = data;
                        else if (!response.IsNotFound) failures.Add($"模型 {id}：{response.Describe()}");
                    }
                    finally
                    {
                        gate.Release();
                    }
                }));
                foreach (var model in batch.Where(models.ContainsKey).Select(id => models[id]))
                {
                    foreach (var r in RemoteSnapshotService.Refs(model["jsonSchema"])) if (!visited.Contains(r)) pending.Add(r);
                }
            }
            var reverseMapping = mappings.Models.ToDictionary(m => m.Value.RemoteId, m => m.Key);
            var resolver = ApifoxReader.Resolver(id => models.TryGetValue(id, out var m)
                ? (reverseMapping.GetValueOrDefault(id) ?? m["name"]?.ToString() ?? "#" + id, m["jsonSchema"])
                : null);
            foreach (var (key, (id, data)) in fetched)
            {
                remoteOps[key] = (ApifoxReader.Read(data, resolver), id, data);
            }
        }

        // 组件：Swagger 组件 + 已确认响应契约中的组件。
        var components = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (name, node) in swaggerComponents)
        {
            if (node is JsonObject component) components[name] = component;
        }
        foreach (var contract in contracts.Values)
        {
            foreach (var (name, component) in contract.Components)
            {
                if (components.TryGetValue(name, out var existing) && !Canonical.Equal(SchemaNormalizer.StructureOnly(SchemaNormalizer.Normalize(existing, _ => null)), SchemaNormalizer.StructureOnly(SchemaNormalizer.Normalize(component, _ => null))))
                {
                    result.Problems.Add($"响应契约模型 {name} 与 Swagger 中同名模型结构不一致，以 Swagger 为准；请确认 Swagger 是否已重新生成。");
                    continue;
                }
                components.TryAdd(name, component);
            }
        }
        var finalComponents = components.ToDictionary(c => c.Key, c => builder.BuildComponent(c.Key, c.Value), StringComparer.Ordinal);
        RefResolver componentResolver = reference => NativeUtil.ComponentName(reference) is { } n ? (n, finalComponents.GetValueOrDefault(n)) : null;

        var engine = new DiffEngine();
        foreach (var key in keys)
        {
            job.Token.ThrowIfCancellationRequested();
            var parts = key.Split(' ', 2);
            var check = new EndpointCheck { Key = key, Method = parts[0], Path = parts[1] };
            check.InSource = scan?.Endpoints.Any(e => e.Key == key) == true;
            check.InSwagger = swaggerOps.ContainsKey(key);
            if (snapshot is not null)
            {
                var matches = snapshot.Endpoints.Where(e => e.Key == key).ToList();
                check.InRemote = matches.Count > 0;
                if (matches.Count > 1)
                {
                    check.DuplicateRemoteIds = matches.Select(m => m.Id).ToList();
                    check.Blockers.Add($"Apifox 中有 {matches.Count} 个同身份接口（ID {string.Join(", ", check.DuplicateRemoteIds)}），不能任选其一覆盖。");
                    engine.SetResource(key, "remote");
                    engine.Add("operation", "identity", "info", "duplicate", JsonDefaults.ToNode(check.DuplicateRemoteIds), null, "blocking", "远端重复身份");
                }
                else if (matches.Count == 1)
                {
                    check.RemoteId = matches[0].Id;
                    check.RemoteName = matches[0].Name;
                    check.RemoteFolder = matches[0].FolderName;
                }
            }
            if (remoteOps.TryGetValue(key, out var remote))
            {
                check.Remote = remote.Op;
                check.InRemote = true;
                if (remote.Data.Count > 0) check.RemoteDetailHash = NativeUtil.Hash(remote.Data);
            }
            else if (check.RemoteId is not null && !task.OfflineMode)
            {
                check.Blockers.Add("远端接口详情读取失败，无法比较。");
            }

            if (check.InSwagger)
            {
                var swaggerOperation = SwaggerOperation(swagger!, check.Path, check.Method);
                contracts.TryGetValue(key, out var contract);
                check.UsesResponseContract = contract is not null;
                var (raw, changes, notes) = builder.BuildRaw(key, swaggerOperation, contract, check.Remote);
                check.RawTarget = raw;
                check.DocChanges = changes;
                check.Notes.AddRange(notes);
                check.Target = NormalizeOperation(key, raw, componentResolver);
                check.ComponentRefs = ComponentClosure(raw, finalComponents).ToList();
            }
            else if (check.Remote is not null && result.StructureSource == "remote")
            {
                var (target, changes) = builder.BuildDocsOnly(key, check.Remote);
                check.Target = target;
                check.DocChanges = changes;
                check.Notes.Add("未提供 Swagger：结构保持远端现状，只合并说明；结构修改需要重新生成 Swagger 后再检查。");
            }
            else if (!check.InSwagger && check.InSource && !check.InRemote)
            {
                check.Status = "sourceOnly";
                check.Notes.Add("源码中有该接口，但 Swagger 和 Apifox 中都没有；请重新生成 Swagger 后再检查。");
                result.Endpoints.Add(check);
                continue;
            }

            if (check.Target is not null)
            {
                foreach (var note in check.Target.Unsupported) check.Blockers.Add("目标包含不支持的结构：" + note);
                if (check.Remote is not null)
                {
                    foreach (var note in check.Remote.Unsupported) check.Blockers.Add("远端包含不支持的结构：" + note);
                }
                engine.CompareOperation(check.Remote, check.Target, check.UsesResponseContract ? "swagger+responseContract" : result.StructureSource);
                if (check.Remote is null && profile.NewEndpoint.FolderId is null)
                {
                    check.Blockers.Add("新接口需要在项目设置中配置目标目录与模板后才能创建。");
                }
            }
            result.Endpoints.Add(check);
        }

        foreach (var check in result.Endpoints)
        {
            var items = engine.Items.Where(i => i.ResourceKey == check.Key).ToList();
            var structural = items.Where(i => i.Category is "structure" or "identity" && i.Change != "info").ToList();
            var docs = check.DocChanges.Where(c => c.Decision is "fill" or "useTarget" or "conflict" or "supplement").ToList();
            if (check.Status == "sourceOnly") continue;
            check.Status = check.Blockers.Count > 0 ? "blocked"
                : check.Remote is null && check.Target is not null ? "new"
                : structural.Count > 0 ? "structure"
                : docs.Count > 0 ? "docs"
                : "unchanged";
        }
        result.Items = engine.Items;

        // 模型。
        if (!task.OfflineMode)
        {
            CheckModels(result, finalComponents, components, models, mappings, decisions, snapshot, builder);
        }
        Quality(result, profile, contracts.Keys.ToHashSet(StringComparer.Ordinal), tasks.Read<List<ResponseContract>>(taskId, TaskFiles.ResponseContract));

        using (await KeyedLocks.AcquireAsync("task:" + taskId, job.Token))
        {
            tasks.Write(taskId, TaskFiles.Diff, result);
            var current = tasks.Get(taskId);
            var remoteStage = current.Stages.Remote;
            remoteStage.SnapshotAt = result.SnapshotAt;
            remoteStage.SnapshotComplete = result.SnapshotComplete;
            remoteStage.Added = result.Endpoints.Count(e => e.Status == "new");
            remoteStage.StructureChanged = result.Endpoints.Count(e => e.Status == "structure");
            remoteStage.DocChanged = result.Endpoints.Count(e => e.Status == "docs");
            remoteStage.Unchanged = result.Endpoints.Count(e => e.Status == "unchanged");
            remoteStage.Blocked = result.Endpoints.Count(e => e.Status == "blocked");
            remoteStage.Conflicts = result.Endpoints.Sum(e => e.DocChanges.Count(c => c.Decision == "conflict"));
            remoteStage.Status = task.OfflineMode ? "offline" : remoteStage.Blocked > 0 || remoteStage.Conflicts > 0 || result.Items.Any(i => i.NeedsDecision) ? "pending" : "previewable";
            remoteStage.Message = task.OfflineMode ? "离线检查：同步不可用。" : null;
            remoteStage.CheckedRevision = result.TaskRevision;
            tasks.Save(current);
        }
        return new
        {
            endpoints = result.Endpoints.Count,
            changed = result.Endpoints.Count(e => e.Status is "new" or "structure" or "docs"),
            blocked = result.Endpoints.Count(e => e.Status == "blocked"),
            models = result.Models.Count,
            problems = result.Problems.Count,
            result.StructureSource,
        };
    }

    private static JsonObject SwaggerOperation(JsonObject swagger, string path, string method)
    {
        var pathItem = (JsonObject)swagger["paths"]![path]!;
        var operation = (JsonObject)pathItem[method.ToLowerInvariant()]!.DeepClone();
        // 合并路径级参数并展开 components 中的参数/请求体/响应引用，结构引用保持不变。
        var parameters = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var source in new[] { pathItem["parameters"] as JsonArray, operation["parameters"] as JsonArray })
        {
            foreach (var p in (source ?? new JsonArray()).Select(n => ResolveComponent(swagger, n, "parameters")).OfType<JsonObject>())
            {
                parameters[p["in"] + ":" + p["name"]] = (JsonObject)p.DeepClone();
            }
        }
        operation["parameters"] = new JsonArray(parameters.Values.Select(p => (JsonNode?)p).ToArray());
        if (ResolveComponent(swagger, operation["requestBody"], "requestBodies") is JsonObject body) operation["requestBody"] = body.DeepClone();
        if (operation["responses"] is JsonObject responses)
        {
            foreach (var (code, node) in responses.ToList())
            {
                if (ResolveComponent(swagger, node, "responses") is JsonObject response) responses[code] = response.DeepClone();
            }
        }
        return operation;
    }

    private static JsonNode? ResolveComponent(JsonObject swagger, JsonNode? node, string section)
    {
        if (node is JsonObject obj && obj["$ref"]?.ToString() is { } reference && reference.StartsWith($"#/components/{section}/", StringComparison.Ordinal))
        {
            return swagger["components"]?[section]?[reference[($"#/components/{section}/").Length..]];
        }
        return node;
    }

    public static DocOperation NormalizeOperation(string key, JsonObject raw, RefResolver resolver)
    {
        var parts = key.Split(' ', 2);
        var document = new JsonObject
        {
            ["openapi"] = "3.0.1",
            ["paths"] = new JsonObject { [parts[1]] = new JsonObject { [parts[0].ToLowerInvariant()] = raw.DeepClone() } },
        };
        // 用传入的组件解析器替代文档内组件：把引用解析交给调用方。
        var op = OpenApiReader.Read(document, new List<string>()).Values.First();
        op.Sources = ["swagger"];
        foreach (var parameter in op.Parameters.Values)
        {
            var source = (raw["parameters"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().First(p => p["in"]?.ToString() == parameter.In && p["name"]?.ToString() == parameter.Name);
            parameter.Schema = SchemaNormalizer.Normalize(source["schema"], resolver);
        }
        if (op.RequestBody is not null && raw["requestBody"]?["content"] is JsonObject bodyContent)
        {
            op.RequestBody.Schema = SchemaNormalizer.Normalize(OpenApiReader.PickMedia(bodyContent).Media?["schema"], resolver);
        }
        foreach (var response in op.Responses.Values)
        {
            if (raw["responses"]?[response.Code]?["content"] is JsonObject content)
            {
                response.Schema = SchemaNormalizer.Normalize(OpenApiReader.PickMedia(content).Media?["schema"], resolver);
            }
        }
        op.Unsupported.Clear();
        OpenApiReader.CollectUnsupported(op);
        return op;
    }

    public static HashSet<string> ComponentClosure(JsonNode? root, Dictionary<string, JsonObject> components)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<JsonNode?>([root]);
        while (queue.Count > 0)
        {
            switch (queue.Dequeue())
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "$ref" && value?.ToString() is { } reference && NativeUtil.ComponentName(reference) is { } name)
                        {
                            if (result.Add(name) && components.TryGetValue(name, out var component)) queue.Enqueue(component);
                        }
                        else
                        {
                            queue.Enqueue(value);
                        }
                    }
                    break;
                case JsonArray arr:
                    foreach (var item in arr) queue.Enqueue(item);
                    break;
            }
        }
        return result;
    }

    private ModelMappingStore LoadMappings(ProjectProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Apifox.ProjectId)) return new ModelMappingStore();
        var path = MappingPath(store, profile.Apifox.ProjectId!, profile.Apifox.BranchName);
        return FileStore.ReadJson<ModelMappingStore>(path) ?? new ModelMappingStore { ProjectId = profile.Apifox.ProjectId!, Branch = profile.Apifox.BranchName };
    }

    public static string MappingPath(FileStore store, string projectId, string? branch) =>
        Path.Combine(store.MappingsDir, FileStore.TargetKey(projectId, branch) + ".json");

    private static void CheckModels(CheckResult result, Dictionary<string, JsonObject> finalComponents, Dictionary<string, JsonObject> rawComponents, ConcurrentDictionary<long, JsonObject> remoteModels,
        ModelMappingStore mappings, DecisionStore decisions, RemoteSnapshot? snapshot, TargetBuilder builder)
    {
        var used = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var usedInRequest = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in result.Endpoints.Where(e => e.RawTarget is not null))
        {
            foreach (var name in check.ComponentRefs)
            {
                if (!used.TryGetValue(name, out var list)) used[name] = list = new List<string>();
                list.Add(check.Key);
            }
            foreach (var name in ComponentClosure(new JsonArray(check.RawTarget!["requestBody"]?.DeepClone(), check.RawTarget["parameters"]?.DeepClone()), finalComponents))
            {
                usedInRequest.Add(name);
            }
        }
        var inScopeRemoteIds = result.Endpoints.Where(e => e.RemoteId is not null).Select(e => e.RemoteId!.Value).ToHashSet();
        var reverseMapping = mappings.Models.ToDictionary(m => m.Value.RemoteId, m => m.Key);
        string? RemoteRefName(string reference)
        {
            var m = Regex.Match(reference, @"^#/definitions/(\d+)$");
            if (!m.Success) return null;
            var id = long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return reverseMapping.GetValueOrDefault(id) ?? (remoteModels.TryGetValue(id, out var model) ? model["name"]?.ToString() : "#" + id);
        }

        foreach (var (name, users) in used.OrderBy(u => u.Key, StringComparer.Ordinal))
        {
            if (!finalComponents.TryGetValue(name, out var component))
            {
                result.Models.Add(new ModelCheck { Name = name, Status = "blocked", UsedBy = users, Blockers = ["Swagger 中缺少该模型定义（悬空引用）。"] });
                continue;
            }
            var check = new ModelCheck { Name = name, UsedBy = users.Distinct().ToList(), UsedInRequest = usedInRequest.Contains(name), Source = component };
            check.DependsOn = ComponentClosure(component, new Dictionary<string, JsonObject>()).Where(n => n != name).ToList();
            check.Target = SchemaNormalizer.NormalizeShallow(component, reference => NativeUtil.ComponentName(reference));

            if (mappings.Models.TryGetValue(name, out var mapping))
            {
                check.RemoteId = mapping.RemoteId;
                check.MappingState = "mapped";
            }
            else if (decisions.ModelBindings.TryGetValue(name, out var binding))
            {
                if (binding == "new") check.MappingState = "new";
                else if (long.TryParse(binding, out var boundId))
                {
                    check.RemoteId = boundId;
                    check.MappingState = "candidate";
                }
            }
            else if (snapshot is not null)
            {
                var candidates = snapshot.Models.Where(m => m.Name == name).Select(m => m.Id).ToList();
                check.Candidates = candidates;
                check.MappingState = candidates.Count switch { 0 => "new", 1 => "candidate", _ => "ambiguous" };
                if (candidates.Count == 1) check.Blockers.Add($"远端存在同名模型（ID {candidates[0]}），首次绑定需要确认。");
                if (candidates.Count > 1) check.Blockers.Add($"远端有 {candidates.Count} 个同名模型，需人工指定绑定。");
            }

            if (check.RemoteId is long remoteId)
            {
                if (remoteModels.TryGetValue(remoteId, out var remote))
                {
                    check.RemoteDetailHash = NativeUtil.Hash(remote);
                    check.Remote = SchemaNormalizer.NormalizeShallow(remote["jsonSchema"], RemoteRefName);
                    var engine = new DiffEngine();
                    engine.SetResource("model:" + name, "swagger");
                    engine.CompareSchema(check.Remote, check.Target, "model:" + name, check.UsedInRequest);
                    result.Items.AddRange(engine.Items);
                    ModelDocs(check, builder);
                    var changed = engine.Items.Any(i => i.Change != "info") || check.DocChanges.Any(c => c.Decision is "fill" or "useTarget" or "supplement");
                    check.Status = changed ? "changed" : "unchanged";
                    if (changed && snapshot is not null)
                    {
                        check.ImpactComplete = snapshot.Complete;
                        foreach (var endpointId in snapshot.EndpointsReferencing(remoteId).Where(id => !inScopeRemoteIds.Contains(id)))
                        {
                            var endpoint = snapshot.Endpoints.First(e => e.Id == endpointId);
                            check.ImpactOutOfScope.Add(new ImpactEntry { Id = endpointId, Key = endpoint.Key, Name = endpoint.Name });
                        }
                        if (!snapshot.Complete) check.Blockers.Add("分支概览不完整，无法确认共享模型的全部影响范围，模型更新被阻断。");
                    }
                }
                else
                {
                    check.Status = "unknown";
                    check.Blockers.Add($"无法读取远端模型 {remoteId}，不能按不存在处理。");
                }
            }
            else
            {
                check.Status = "new";
            }
            if (check.Blockers.Count > 0 && check.Status != "unchanged") check.Status = "blocked";
            result.Models.Add(check);
        }

        // 新建模型之间的相互递归第一版不支持（需要预建空壳）。
        var newModels = result.Models.Where(m => m.Status == "new").ToDictionary(m => m.Name, StringComparer.Ordinal);
        foreach (var model in newModels.Values)
        {
            if (HasCycle(model.Name, newModels, new HashSet<string>(StringComparer.Ordinal)))
            {
                model.Status = "blocked";
                model.Blockers.Add("新建模型之间存在相互递归引用，第一版不自动创建，请人工准备模型并建立映射。");
            }
        }
    }

    private static bool HasCycle(string start, Dictionary<string, ModelCheck> models, HashSet<string> path)
    {
        if (!path.Add(start)) return true;
        foreach (var dependency in models.TryGetValue(start, out var model) ? model.DependsOn : new List<string>())
        {
            if (models.ContainsKey(dependency) && HasCycle(dependency, models, path)) return true;
        }
        path.Remove(start);
        return false;
    }

    private static void ModelDocs(ModelCheck check, TargetBuilder builder)
    {
        void Compare(string location, string? remoteValue, string? targetValue, string? property)
        {
            if (string.IsNullOrWhiteSpace(targetValue) || TextUtil.NormalizeSpace(targetValue) == TextUtil.NormalizeSpace(remoteValue)) return;
            var change = new DocChange { Id = "c" + Ids.ShortHash("model:" + check.Name + "|" + location, 12), Location = location, RemoteValue = remoteValue, TargetValue = targetValue };
            if (string.IsNullOrWhiteSpace(remoteValue))
            {
                change.Decision = "fill";
                change.FinalValue = targetValue;
            }
            else if (builder.IsConfirmedComponentDoc(check.Name, property))
            {
                change.Decision = "conflict";
                change.FinalValue = remoteValue;
            }
            else
            {
                change.Decision = "keepRemote";
                change.FinalValue = remoteValue;
            }
            change.Origin = builder.IsConfirmedComponentDoc(check.Name, property) ? "comment" : "swagger";
            check.DocChanges.Add(change);
        }

        Compare("description", check.Remote?["description"]?.ToString(), check.Target?["description"]?.ToString(), null);
        var remoteProps = check.Remote?["properties"] as JsonObject ?? new JsonObject();
        foreach (var (name, node) in check.Target?["properties"] as JsonObject ?? new JsonObject())
        {
            Compare("properties." + name, remoteProps[name]?["description"]?.ToString(), node?["description"]?.ToString(), name);
        }
    }

    private static void Quality(CheckResult result, ProjectProfile profile, HashSet<string> confirmedContracts, List<ResponseContract>? allContracts)
    {
        foreach (var check in result.Endpoints.Where(e => e.Target is not null))
        {
            var target = check.Target!;
            if (string.IsNullOrWhiteSpace(target.Summary) && string.IsNullOrWhiteSpace(check.RemoteName))
            {
                result.Quality.Add(new QualityIssue { ResourceKey = check.Key, Location = "operation", Kind = "missingSummary", Message = "接口缺少标题/用途说明" });
            }
            if (profile.Quality.RequireParameterDescription)
            {
                foreach (var parameter in target.Parameters.Values.Where(p => p.In is "query" or "path" && string.IsNullOrWhiteSpace(p.Description)))
                {
                    result.Quality.Add(new QualityIssue { ResourceKey = check.Key, Location = $"{parameter.In}.{parameter.Name}", Kind = "missingParameterDescription", Message = $"参数 {parameter.Name} 缺少说明" });
                }
            }
            if (target.Responses.TryGetValue("200", out var ok) && ok.Schema?["properties"] is JsonObject properties
                && properties[profile.Quality.GenericDataProperty] is JsonObject data
                && data["properties"] is null && data["items"] is null && data["additionalProperties"] is not JsonObject && data["x-recursion"] is null
                && (data["type"] is null || data["type"]!.ToJsonString().Contains("object")))
            {
                var contract = allContracts?.FirstOrDefault(c => c.EndpointKey == check.Key);
                var hint = contract is null ? "源码中未追踪到封装返回" : contract.ReviewState == "confirmed" ? "" : $"已追踪到候选类型 {contract.DataType ?? "（未确定）"}，可在“响应契约”中确认";
                result.Quality.Add(new QualityIssue
                {
                    ResourceKey = check.Key,
                    Location = "responses.200." + profile.Quality.GenericDataProperty,
                    Kind = "genericData",
                    Message = "通用响应缺少具体 data 类型" + (hint.Length > 0 ? "：" + hint : ""),
                });
            }
            if (profile.Quality.RequirePropertyDescription && ok?.Schema is not null)
            {
                var missing = new List<string>();
                MissingDescriptions(ok.Schema, "responses.200", missing, 0);
                foreach (var location in missing.Take(30))
                {
                    result.Quality.Add(new QualityIssue { ResourceKey = check.Key, Location = location, Kind = "missingPropertyDescription", Message = "响应字段缺少说明" });
                }
            }
            foreach (var blocker in check.Blockers)
            {
                result.Quality.Add(new QualityIssue { ResourceKey = check.Key, Location = "operation", Kind = "blocking", Message = blocker, Severity = "blocking" });
            }
        }
    }

    private static void MissingDescriptions(JsonObject schema, string path, List<string> result, int depth)
    {
        if (depth > 6 || schema["x-recursion"] is not null) return;
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var (name, node) in properties)
            {
                if (node is not JsonObject property) continue;
                if (string.IsNullOrWhiteSpace(property["description"]?.ToString())) result.Add(path + "." + name);
                MissingDescriptions(property, path + "." + name, result, depth + 1);
            }
        }
        if (schema["items"] is JsonObject items) MissingDescriptions(items, path + "[]", result, depth + 1);
    }
}
