using ApifoxHelper.Apifox;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Sync;

/// <summary>
/// 能力探测与写入兼容登记。未登记通过写入探测的 CLI 版本只允许只读检查；
/// 探测在用户指定的测试接口上做“原样写回、临时改说明再恢复、整段嵌套字段原样写回”三类往返，
/// 逐项回读核对除易变字段外的全部内容。
/// </summary>
public sealed class CompatService(FileStore store, ApifoxCli cli)
{
    private string StorePath => Path.Combine(store.CompatDir, "write-compat.json");

    public WriteCompatStore Load() => FileStore.ReadJson<WriteCompatStore>(StorePath) ?? new WriteCompatStore();

    public WriteCompatRecord? Verified(string cliVersion) =>
        Load().Records.Where(r => r.Passed && r.CliVersion == cliVersion).OrderByDescending(r => r.At).FirstOrDefault();

    public async Task<CapabilityReport> CapabilitiesAsync(ProjectProfile profile, CancellationToken ct)
    {
        var report = new CapabilityReport();
        try
        {
            var entry = ApifoxCli.Resolve(profile.Apifox);
            report.Cli.Node = entry.Node;
            report.Cli.Script = entry.Script;
            report.Cli.Version = await cli.VersionAsync(profile.Apifox, ct);
            report.Cli.Found = true;
        }
        catch (UserException ex)
        {
            report.Cli.Error = ex.Message;
        }

        if (report.Cli.Found)
        {
            var (loggedIn, message) = await cli.AuthStatusAsync(profile.Apifox, ct);
            report.Login = new CapabilityLogin { LoggedIn = loggedIn, Message = message };
            if (loggedIn && !string.IsNullOrWhiteSpace(profile.Apifox.ProjectId))
            {
                var page = await cli.EndpointPageAsync(profile.Apifox, profile.Apifox.ProjectId!, profile.Apifox.BranchName, 1, 1, ct);
                report.Project = new CapabilityProject
                {
                    Readable = page.Ok,
                    Total = page.Meta?["total"]?.GetValue<int>(),
                    Message = page.Ok ? "可读取" : page.Describe(),
                };
            }
        }

        var verified = report.Cli.Version is null ? null : Verified(report.Cli.Version);
        report.WriteCompat = new CapabilityWriteCompat
        {
            Verified = verified is not null,
            Record = verified,
            Message = verified is not null
                ? $"CLI {report.Cli.Version} 已于 {verified.At:yyyy-MM-dd HH:mm} 通过写入探测"
                : "当前 CLI 版本尚未通过写入探测：只允许只读检查。",
        };
        return report;
    }

    public async Task<WriteCompatRecord> ProbeAsync(ProjectProfile profile, WriteProbeRequest request, string workDir, CancellationToken ct)
    {
        if (!request.Confirm) throw new UserException("写入探测会修改指定的测试接口（结束时恢复），请确认后再执行。");
        if (string.IsNullOrWhiteSpace(request.ProjectId) || !request.ProjectId.All(char.IsDigit)) throw new UserException("请填写测试项目 ID。");
        if (request.EndpointId <= 0) throw new UserException("请填写测试接口 ID。");
        var target = profile.Apifox;
        var version = await cli.VersionAsync(target, ct);
        var record = new WriteCompatRecord { CliVersion = version, ProjectId = request.ProjectId, Branch = request.Branch, EndpointId = request.EndpointId, At = DateTimeOffset.Now };
        Directory.CreateDirectory(workDir);

        void Check(string name, bool passed, string detail) => record.Checks.Add(new ProbeCheck { Name = name, Passed = passed, Detail = detail });

        var first = await cli.GetEndpointAsync(target, request.ProjectId, request.Branch, request.EndpointId, ct);
        if (!first.Ok || first.Data is not JsonObject before)
        {
            Check("读取测试接口", false, first.Describe());
            return Save(record);
        }
        Check("读取测试接口", true, $"{before["method"]} {before["path"]}");
        FileStore.WriteJson(Path.Combine(workDir, "probe-before.json"), before);

        // 分支隔离：测试接口应出现在指定分支的列表中。
        var found = false;
        for (var page = 1; page <= 20 && !found; page++)
        {
            var list = await cli.EndpointPageAsync(target, request.ProjectId, request.Branch, page, 500, ct);
            if (!list.Ok)
            {
                Check("分支列表读取", false, list.Describe());
                return Save(record);
            }
            found = (list.Data as JsonArray ?? new JsonArray()).OfType<JsonObject>().Any(e => RemoteSnapshotService.ReadLong(e["id"]) == request.EndpointId);
            if (list.Meta?["nextPage"] is null || list.Meta["nextPage"]!.GetValueKind() == JsonValueKind.Null) break;
        }
        Check("分支隔离", found, found ? "测试接口出现在指定分支的列表中" : "指定分支的接口列表中找不到测试接口");
        if (!found) return Save(record);

        async Task<bool> RoundTrip(string name, JsonObject payload, JsonObject expectedBefore, Func<JsonObject, bool>? extra = null)
        {
            var file = Path.Combine(workDir, $"probe-{record.Checks.Count}.json");
            FileStore.WriteJson(file, payload);
            var (valid, message) = await cli.ValidateAsync(target, "endpoint-update", file, ct);
            if (!valid)
            {
                Check(name, false, "CLI Schema 校验失败：" + message);
                return false;
            }
            var write = await cli.UpdateEndpointAsync(target, request.ProjectId, request.Branch, request.EndpointId, file, ct);
            if (!write.Ok)
            {
                Check(name, false, "写入失败：" + write.Describe());
                return false;
            }
            var after = await cli.GetEndpointAsync(target, request.ProjectId, request.Branch, request.EndpointId, ct);
            if (!after.Ok || after.Data is not JsonObject data)
            {
                Check(name, false, "回读失败：" + after.Describe());
                return false;
            }
            var managed = payload.Select(p => p.Key).ToList();
            var protectedSame = NativeUtil.ProtectedHash(data, managed) == NativeUtil.ProtectedHash(expectedBefore, managed);
            var managedSame = SyncExecutor.Contains(data, payload);
            var ok = protectedSame && managedSame && (extra?.Invoke(data) ?? true);
            Check(name, ok, ok ? "写入后回读一致，其余字段未变化" : !protectedSame ? "未写入的字段发生了变化" : "写入的内容没有原样保留");
            return ok;
        }

        var description = before["description"]?.ToString() ?? "";
        var ok = await RoundTrip("原样写回说明", new JsonObject { ["description"] = description }, before);
        if (ok)
        {
            var marked = description + "\n<!-- apifox-helper probe " + DateTimeOffset.Now.ToUnixTimeSeconds() + " -->";
            ok = await RoundTrip("临时修改说明", new JsonObject { ["description"] = marked }, before, d => d["description"]?.ToString() == marked);
            // 无论上一步结果如何都恢复原说明。
            var restored = await RoundTrip("恢复说明", new JsonObject { ["description"] = description }, before);
            ok = ok && restored;
        }
        if (ok)
        {
            var nested = new JsonObject
            {
                ["parameters"] = before["parameters"]?.DeepClone(),
                ["requestBody"] = before["requestBody"]?.DeepClone(),
                ["responses"] = before["responses"]?.DeepClone(),
            };
            ok = await RoundTrip("整段写回参数/请求体/响应", nested, before);
        }

        // 导出概览与 get 的引用格式一致，影响分析才可信。
        var exportFile = Path.Combine(Path.GetTempPath(), "apifox-helper-probe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var export = await cli.ExportAsync(target, request.ProjectId, request.Branch, exportFile, ct);
            var exportRefs = new HashSet<long>();
            var exported = false;
            if (export.Ok && File.Exists(exportFile))
            {
                var root = JsonNode.Parse(await File.ReadAllTextAsync(exportFile, Encoding.UTF8, ct)) as JsonObject;
                var snapshot = new RemoteSnapshot();
                if (root?["apiCollection"] is JsonArray apis)
                {
                    RemoteSnapshotService.WalkApis(apis, null, null, snapshot);
                    var entry = snapshot.Endpoints.FirstOrDefault(e => e.Id == request.EndpointId);
                    if (entry is not null)
                    {
                        exported = true;
                        exportRefs = entry.ModelRefs.ToHashSet();
                    }
                }
            }
            var getRefs = RemoteSnapshotService.Refs(new JsonArray(before["parameters"]?.DeepClone(), before["requestBody"]?.DeepClone(), before["responses"]?.DeepClone()));
            var refsMatch = exported && exportRefs.SetEquals(getRefs);
            Check("导出概览与详情的引用一致", refsMatch, exported ? $"导出 {exportRefs.Count} 个引用，详情 {getRefs.Count} 个引用" : "导出中找不到测试接口：" + export.Describe());
            ok = ok && refsMatch;
        }
        finally
        {
            if (File.Exists(exportFile)) File.Delete(exportFile);
        }

        record.Passed = ok && record.Checks.All(c => c.Passed);
        return Save(record);
    }

    private WriteCompatRecord Save(WriteCompatRecord record)
    {
        var all = Load();
        all.Records.Add(record);
        FileStore.WriteJson(StorePath, all);
        return record;
    }
}
