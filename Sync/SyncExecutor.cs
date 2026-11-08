using ApifoxHelper.Apifox;
using ApifoxHelper.Contracts;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Sync;

/// <summary>
/// 执行已确认的同步计划：同一项目/分支加本地独占锁；每个动作写入前重读目标并核对指纹、保存前态；
/// 写入后立即回读核对（写入内容原样存在 + 未写入字段不变）。首个失败、结果不确定或核对失败即停止。
/// 写入超时不重发，先按 ID/身份回读判断。没有跨资源事务，部分完成如实记录。
/// </summary>
public sealed class SyncExecutor(TaskStore tasks, FileStore store, ApifoxCli cli, CompatService compat, OpenApiSource openApi)
{
    public async Task<object> ExecuteAsync(string taskId, string planHash, JobContext job)
    {
        SyncPlan plan;
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        using (await KeyedLocks.AcquireAsync("task:" + taskId, job.Token))
        {
            plan = tasks.Read<SyncPlan>(taskId, TaskFiles.Plan) ?? throw new UserException("还没有同步计划。");
            if (plan.Hash != planHash) throw new UserException("确认的计划与当前计划不一致，请重新预览。", 409);
            if (plan.Status is "verified" or "partial" or "failed" or "canceled") return Summary(plan);
            if (plan.Status == "executing") throw new UserException("该计划正在执行。", 409);
            if (plan.Status != "awaitingConfirm") throw new UserException("计划已过期，请重新生成。", 409);
            if (task.Revision != plan.TaskRevision) throw Expire(taskId, plan, "生成计划后任务内容有变化，请重新检查并生成计划。");
        }

        var version = await cli.VersionAsync(profile.Apifox, job.Token);
        if (version != plan.CliVersion) throw Expire(taskId, plan, $"CLI 版本已从 {plan.CliVersion} 变为 {version}，请重新生成计划。");
        if (compat.Verified(version) is null) throw new UserException($"CLI {version} 未通过写入兼容探测。");
        if (plan.SwaggerHash is not null && profile.Swagger.Type != "none")
        {
            // 来源变化后计划过期：重新读取当前 Swagger 并与冻结快照比较。
            var (_, _, info) = await openApi.LoadAsync(profile.Swagger, profile.WorkspaceRoot, job.Token);
            if (info.Hash != plan.SwaggerHash) throw Expire(taskId, plan, "Swagger 来源在检查后已变化，请重新加载并检查。");
        }

        var lockPath = Path.Combine(store.LocksDir, FileStore.TargetKey(plan.ProjectId, plan.Branch) + ".lock");
        FileStream lockStream;
        try
        {
            lockStream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new UserException("该项目/分支正在执行其他同步，请稍后再试。", 409);
        }

        using (lockStream)
        {
            await UpdatePlanAsync(taskId, p =>
            {
                p.Status = "executing";
                p.ConfirmedAt = DateTimeOffset.Now;
            });
            await tasks.UpdateAsync(taskId, t => t.Stages.Remote.Status = "executing", CancellationToken.None);
            var journal = tasks.FilePath(taskId, TaskFiles.Journal);
            FileStore.AppendJsonLine(journal, JournalLines.PlanStartLine(DateTimeOffset.Now, plan.Id, plan.Hash));

            var created = new Dictionary<string, long>(StringComparer.Ordinal);
            var stop = false;
            for (var i = 0; i < plan.Actions.Count; i++)
            {
                var action = plan.Actions[i];
                if (stop || job.Token.IsCancellationRequested)
                {
                    action.State = "notRun";
                    action.Message = stop ? "前序动作未通过，停止后续写入" : "用户请求停止";
                    continue;
                }
                job.Report((double)i / plan.Actions.Count, $"执行 {i + 1}/{plan.Actions.Count}：{action.ResourceKey}");
                action.StartedAt = DateTimeOffset.Now;
                try
                {
                    await ExecuteActionAsync(taskId, plan, action, created, journal);
                }
                catch (UserException ex)
                {
                    action.State = action.State is "pending" ? "writeFailed" : action.State;
                    action.Message = ex.Message;
                }
                action.FinishedAt = DateTimeOffset.Now;
                FileStore.AppendJsonLine(journal, JournalLines.ActionResultLine(DateTimeOffset.Now, plan.Id, action.ActionId, action.ResourceKey, action.State, action.Message, action.CreatedId));
                await UpdatePlanAsync(taskId, p => p.Actions = plan.Actions);
                if (action.State != "verified") stop = true;
            }

            var verified = plan.Actions.Count(a => a.State == "verified");
            var status = job.Token.IsCancellationRequested && verified < plan.Actions.Count ? (verified > 0 ? "partial" : "canceled")
                : verified == plan.Actions.Count ? "verified"
                : verified > 0 ? "partial" : "failed";
            await UpdatePlanAsync(taskId, p =>
            {
                p.Actions = plan.Actions;
                p.Status = status;
                p.FinishedAt = DateTimeOffset.Now;
            });
            FileStore.AppendJsonLine(journal, JournalLines.PlanEndLine(DateTimeOffset.Now, plan.Id, status));
            await tasks.UpdateAsync(taskId, t =>
            {
                t.Stages.Remote.Status = status;
                t.Stages.Remote.ActionsVerified = verified;
                t.Stages.Remote.ActionsFailed = plan.Actions.Count(a => a.State is "writeFailed" or "verifyFailed" or "unknown" or "conflict");
                t.Stages.Remote.ActionsNotRun = plan.Actions.Count(a => a.State == "notRun");
                t.Stages.Remote.Message = status == "verified" ? "已写入并回读核对通过；建议重新检查确认无差异。" : "存在未完成动作，详见同步结果。";
                t.Revision++;
            }, CancellationToken.None);
            plan.Status = status;
        }
        return Summary(plan);
    }

    private async Task ExecuteActionAsync(string taskId, SyncPlan plan, PlanAction action, Dictionary<string, long> created, string journal)
    {
        var task = tasks.Get(taskId);
        var target = task.ProfileSnapshot.Apifox;
        var payloadText = await File.ReadAllTextAsync(action.PayloadFile, Encoding.UTF8);
        foreach (var (name, id) in created)
        {
            payloadText = payloadText.Replace(SyncPlanService.NewModelPrefix + name + "\"", "#/definitions/" + id.ToString(CultureInfo.InvariantCulture) + "\"", StringComparison.Ordinal);
        }
        if (payloadText.Contains(SyncPlanService.NewModelPrefix, StringComparison.Ordinal)) throw new UserException("依赖的新模型尚未创建，不能写入。");
        var payload = JsonNode.Parse(payloadText) as JsonObject ?? throw new UserException("写入文件无效。");
        var resolvedFile = Path.Combine(Path.GetDirectoryName(action.PayloadFile)!, action.ActionId + ".resolved.json");
        FileStore.WriteJson(resolvedFile, payload);
        var (valid, validation) = await cli.ValidateAsync(target, action.ValidationKey, resolvedFile, CancellationToken.None);
        if (!valid) throw new UserException("CLI Schema 校验失败：" + validation);

        var isEndpoint = action.Kind is "createEndpoint" or "updateEndpoint";
        JsonObject? before = null;
        if (action.RemoteId is long remoteId)
        {
            var current = isEndpoint
                ? await cli.GetEndpointAsync(target, plan.ProjectId, plan.Branch, remoteId, CancellationToken.None)
                : await cli.GetSchemaAsync(target, plan.ProjectId, plan.Branch, remoteId, CancellationToken.None);
            if (!current.Ok || current.Data is not JsonObject data) throw new UserException("写入前读取目标失败：" + current.Describe());
            if (NativeUtil.Hash(data) != action.BeforeHash)
            {
                action.State = "conflict";
                throw new UserException("目标在确认后被修改，计划过期，未覆盖新修改。");
            }
            before = data;
            FileStore.WriteJson(Path.Combine(tasks.FilePath(taskId, TaskFiles.RemoteBefore), (isEndpoint ? "endpoint-" : "schema-") + remoteId + ".json"), data);
        }
        else
        {
            // 创建前复查同身份资源，避免重复创建。
            var existing = await FindExistingAsync(target, plan, action, payload);
            if (existing is not null)
            {
                action.State = "conflict";
                throw new UserException($"远端已存在同身份资源（ID {existing}），未重复创建，请重新检查。");
            }
        }

        FileStore.AppendJsonLine(journal, JournalLines.IntentLine(DateTimeOffset.Now, plan.Id, action.ActionId, action.Kind, action.ResourceKey, action.RemoteId, Canonical.Hash(payload)));
        var result = action.Kind switch
        {
            "createModel" => await cli.CreateSchemaAsync(target, plan.ProjectId, plan.Branch, resolvedFile, CancellationToken.None),
            "updateModel" => await cli.UpdateSchemaAsync(target, plan.ProjectId, plan.Branch, action.RemoteId!.Value, resolvedFile, CancellationToken.None),
            "createEndpoint" => await cli.CreateEndpointAsync(target, plan.ProjectId, plan.Branch, resolvedFile, CancellationToken.None),
            "updateEndpoint" => await cli.UpdateEndpointAsync(target, plan.ProjectId, plan.Branch, action.RemoteId!.Value, resolvedFile, CancellationToken.None),
            _ => throw new UserException("未知动作类型"),
        };

        long? resourceId = action.RemoteId;
        if (result.Ok)
        {
            action.State = "written";
            if (resourceId is null) resourceId = RemoteSnapshotService.ReadLong(result.Data?["id"]);
        }
        else if (result.TimedOut || result.ParseError is not null)
        {
            // 结果不确定：不重发，按 ID 或身份回读判断。
            action.State = "unknown";
            action.Message = "写入结果不确定：" + result.Describe();
            if (resourceId is null) resourceId = await FindExistingAsync(target, plan, action, payload);
            if (resourceId is null) throw new UserException(action.Message + "；回读未找到唯一的目标资源，已停止。");
        }
        else
        {
            action.State = "writeFailed";
            throw new UserException("写入失败：" + result.Describe());
        }
        if (resourceId is null)
        {
            resourceId = await FindExistingAsync(target, plan, action, payload);
            if (resourceId is null)
            {
                action.State = "unknown";
                throw new UserException("创建成功但没有返回 ID，且无法按身份唯一找到新资源，已停止。");
            }
        }

        // 回读核对，平台可能有短暂延迟，有限重试。
        string? failure = null;
        JsonObject? after = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(1.5));
            var read = isEndpoint
                ? await cli.GetEndpointAsync(target, plan.ProjectId, plan.Branch, resourceId.Value, CancellationToken.None)
                : await cli.GetSchemaAsync(target, plan.ProjectId, plan.Branch, resourceId.Value, CancellationToken.None);
            if (!read.Ok || read.Data is not JsonObject data)
            {
                failure = "回读失败：" + read.Describe();
                continue;
            }
            after = data;
            if (!Contains(data, payload))
            {
                failure = "回读内容与写入内容不一致";
                continue;
            }
            if (before is not null && NativeUtil.ProtectedHash(data, action.ManagedFields) != action.ProtectedHash)
            {
                failure = "未计划修改的字段发生了变化（受保护设置未能保留）";
                break;
            }
            failure = null;
            break;
        }
        if (failure is not null)
        {
            action.State = action.State == "unknown" ? "unknown" : "verifyFailed";
            action.Message = failure;
            throw new UserException(failure);
        }

        action.State = "verified";
        action.Message = null;
        if (action.RemoteId is null) action.CreatedId = resourceId;
        FileStore.WriteJson(Path.Combine(tasks.FilePath(taskId, TaskFiles.RemoteAfter), (isEndpoint ? "endpoint-" : "schema-") + resourceId + ".json"), after!);
        if (action.Kind == "createModel" && action.ModelName is not null) created[action.ModelName] = resourceId.Value;
        if (action.ModelName is not null) await SaveMappingAsync(plan, action.ModelName, resourceId.Value, taskId);
        await SaveBaselineAsync(plan, action, after!, taskId);
    }

    private async Task<long?> FindExistingAsync(Profiles.ApifoxTarget target, SyncPlan plan, PlanAction action, JsonObject payload)
    {
        if (action.Kind is "createModel" or "updateModel")
        {
            var name = action.ModelName ?? "";
            var list = await cli.ListSchemasAsync(target, plan.ProjectId, plan.Branch, name, CancellationToken.None);
            if (!list.Ok) throw new UserException("复查同名模型失败：" + list.Describe());
            var matches = (list.Data as JsonArray ?? new JsonArray()).OfType<JsonObject>().Where(m => m["name"]?.ToString() == name).ToList();
            return matches.Count == 1 ? RemoteSnapshotService.ReadLong(matches[0]["id"]) : matches.Count > 1 ? throw new UserException("远端出现多个同名模型，已停止。") : null;
        }
        var method = payload["method"]?.ToString() ?? action.ResourceKey.Split(' ')[0].ToLowerInvariant();
        var path = payload["path"]?.ToString() ?? action.ResourceKey.Split(' ', 2)[1];
        var endpoints = await cli.FindEndpointsByPathAsync(target, plan.ProjectId, plan.Branch, path, CancellationToken.None);
        if (!endpoints.Ok) throw new UserException("复查同身份接口失败：" + endpoints.Describe());
        var found = (endpoints.Data as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(e => e["path"]?.ToString() == path && string.Equals(e["method"]?.ToString(), method, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return found.Count == 1 ? RemoteSnapshotService.ReadLong(found[0]["id"]) : found.Count > 1 ? throw new UserException("远端出现多个同身份接口，已停止。") : null;
    }

    /// <summary>
    /// 写入的每个字段都原样出现在回读结果中（平台可补充额外字段，但不能改动或丢弃写入的内容）。
    /// 数字与数字字符串视为相同，以容忍平台对 id/code 的类型规范化。
    /// </summary>
    public static bool Contains(JsonNode? actual, JsonNode? expected)
    {
        switch (expected)
        {
            case null:
                return actual is null || (actual is JsonValue v && v.GetValueKind() == JsonValueKind.Null);
            case JsonObject expectedObject:
                if (actual is not JsonObject actualObject) return false;
                foreach (var (key, value) in expectedObject)
                {
                    if (!actualObject.TryGetPropertyValue(key, out var actualValue))
                    {
                        if (value is JsonValue ev && ev.GetValueKind() == JsonValueKind.String && ev.GetValue<string>().Length == 0) continue;
                        if (value is JsonArray { Count: 0 }) continue;
                        return false;
                    }
                    if (!Contains(actualValue, value)) return false;
                }
                return true;
            case JsonArray expectedArray:
                if (actual is not JsonArray actualArray || actualArray.Count != expectedArray.Count) return false;
                for (var i = 0; i < expectedArray.Count; i++)
                {
                    if (!Contains(actualArray[i], expectedArray[i])) return false;
                }
                return true;
            default:
                var expectedText = Canonical.Json(expected).Trim('"');
                var actualText = Canonical.Json(actual).Trim('"');
                return expectedText == actualText;
        }
    }

    /// <summary>只回读核对，不写入：用于程序中断后的恢复判断与用户主动复核。</summary>
    public async Task<object> VerifyAsync(string taskId, JobContext job)
    {
        var task = tasks.Get(taskId);
        var target = task.ProfileSnapshot.Apifox;
        var plan = tasks.Read<SyncPlan>(taskId, TaskFiles.Plan) ?? throw new UserException("还没有同步计划。");
        if (plan.Status == "executing" && File.Exists(Path.Combine(store.LocksDir, FileStore.TargetKey(plan.ProjectId, plan.Branch) + ".lock")))
        {
            try
            {
                using var probe = new FileStream(Path.Combine(store.LocksDir, FileStore.TargetKey(plan.ProjectId, plan.Branch) + ".lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new UserException("同步仍在执行中，不能同时核对。", 409);
            }
        }
        foreach (var action in plan.Actions.Where(a => a.State is "written" or "unknown" or "verified" or "pending" or "verifyFailed"))
        {
            job.Token.ThrowIfCancellationRequested();
            var payload = JsonNode.Parse(await File.ReadAllTextAsync(File.Exists(Path.ChangeExtension(action.PayloadFile, ".resolved.json")) ? Path.ChangeExtension(action.PayloadFile, ".resolved.json") : action.PayloadFile, Encoding.UTF8)) as JsonObject;
            var id = action.RemoteId ?? action.CreatedId;
            if (id is null)
            {
                id = await FindExistingAsync(target, plan, action, payload ?? new JsonObject());
                if (id is null)
                {
                    if (action.State != "pending") action.Message = "远端找不到该资源";
                    continue;
                }
            }
            var isEndpoint = action.Kind is "createEndpoint" or "updateEndpoint";
            var read = isEndpoint
                ? await cli.GetEndpointAsync(target, plan.ProjectId, plan.Branch, id.Value, job.Token)
                : await cli.GetSchemaAsync(target, plan.ProjectId, plan.Branch, id.Value, job.Token);
            if (!read.Ok || read.Data is not JsonObject data)
            {
                action.Message = "核对读取失败：" + read.Describe();
                continue;
            }
            var reached = payload is not null && Contains(data, payload);
            if (reached && action.State != "verified")
            {
                action.State = "verified";
                action.Message = "重新核对：远端内容已达到目标";
                if (action.RemoteId is null) action.CreatedId = id;
            }
            else if (!reached && action.State == "verified")
            {
                action.State = "verifyFailed";
                action.Message = "重新核对：远端内容已与写入内容不同（可能被他人修改）";
            }
        }
        var verified = plan.Actions.Count(a => a.State == "verified");
        plan.Status = verified == plan.Actions.Count ? "verified" : verified > 0 ? "partial" : plan.Status == "interrupted" ? "interrupted" : "failed";
        await UpdatePlanAsync(taskId, p =>
        {
            p.Actions = plan.Actions;
            p.Status = plan.Status;
        });
        await tasks.UpdateAsync(taskId, t =>
        {
            t.Stages.Remote.Status = plan.Status;
            t.Stages.Remote.ActionsVerified = verified;
        }, job.Token);
        return Summary(plan);
    }

    /// <summary>启动时把上次未结束的执行标记为“已中断”，等待用户重新核对，不自动重放。</summary>
    public void RecoverInterrupted()
    {
        foreach (var task in tasks.List())
        {
            var plan = tasks.Read<SyncPlan>(task.Id, TaskFiles.Plan);
            if (plan is not { Status: "executing" }) continue;
            plan.Status = "interrupted";
            foreach (var action in plan.Actions.Where(a => a.State is "pending" or "written"))
            {
                action.State = action.StartedAt is null ? "notRun" : "unknown";
                action.Message ??= "执行过程中程序中断，需要重新核对";
            }
            tasks.Write(task.Id, TaskFiles.Plan, plan);
            task.Stages.Remote.Status = "interrupted";
            task.Stages.Remote.Message = "上次同步执行中断，请点击“重新核对”确认远端实际状态。";
            tasks.Save(task);
        }
    }

    private async Task SaveMappingAsync(SyncPlan plan, string modelName, long remoteId, string taskId)
    {
        var path = CheckService.MappingPath(store, plan.ProjectId, plan.Branch);
        using (await KeyedLocks.AcquireAsync("mapping:" + path))
        {
            var mappings = FileStore.ReadJson<ModelMappingStore>(path) ?? new ModelMappingStore { ProjectId = plan.ProjectId, Branch = plan.Branch };
            mappings.Models[modelName] = new MappingEntry { RemoteId = remoteId, ConfirmedAt = DateTimeOffset.Now, ConfirmedByTask = taskId };
            FileStore.WriteJson(path, mappings);
        }
    }

    private async Task SaveBaselineAsync(SyncPlan plan, PlanAction action, JsonObject after, string taskId)
    {
        var diff = tasks.Read<CheckResult>(taskId, TaskFiles.Diff);
        var check = diff?.Endpoints.FirstOrDefault(e => e.Key == action.ResourceKey);
        var sourceHash = check?.Target is null ? "" : Canonical.HashOf(new
        {
            parameters = check.Target.Parameters.Values.Select(p => new { p.Key, p.Required, schema = SchemaNormalizer.StructureOnly(p.Schema) }),
            body = SchemaNormalizer.StructureOnly(check.Target.RequestBody?.Schema),
            responses = check.Target.Responses.Values.Select(r => new { r.Code, schema = SchemaNormalizer.StructureOnly(r.Schema) }),
        });
        var baseline = new Baseline
        {
            ResourceKey = action.ResourceKey,
            VerifiedSourceHash = sourceHash,
            VerifiedRemoteHash = NativeUtil.Hash(after),
            VerifiedTaskId = taskId,
            VerifiedAt = DateTimeOffset.Now,
        };
        var dir = Path.Combine(store.BaselinesDir, FileStore.TargetKey(plan.ProjectId, plan.Branch));
        using (await KeyedLocks.AcquireAsync("baseline:" + dir))
        {
            FileStore.WriteJson(Path.Combine(dir, Ids.ShortHash(action.ResourceKey, 16) + ".json"), baseline);
        }
    }

    private async Task UpdatePlanAsync(string taskId, Action<SyncPlan> change)
    {
        using (await KeyedLocks.AcquireAsync("task:" + taskId))
        {
            var current = tasks.Read<SyncPlan>(taskId, TaskFiles.Plan) ?? throw new UserException("计划丢失。");
            change(current);
            tasks.Write(taskId, TaskFiles.Plan, current);
        }
    }

    private UserException Expire(string taskId, SyncPlan plan, string message)
    {
        plan.Status = "expired";
        plan.Message = message;
        tasks.Write(taskId, TaskFiles.Plan, plan);
        return new UserException(message, 409);
    }

    private static object Summary(SyncPlan plan) => new
    {
        plan.Status,
        verified = plan.Actions.Count(a => a.State == "verified"),
        failed = plan.Actions.Count(a => a.State is "writeFailed" or "verifyFailed" or "unknown" or "conflict"),
        notRun = plan.Actions.Count(a => a.State == "notRun"),
        actions = plan.Actions.Select(a => new { a.ActionId, a.ResourceKey, a.Kind, a.State, a.Message, a.CreatedId }),
    };
}
