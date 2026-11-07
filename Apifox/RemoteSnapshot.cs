using ApifoxHelper.Profiles;

namespace ApifoxHelper.Apifox;

public sealed class RemoteEndpointRef
{
    public long Id { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public long? FolderId { get; set; }
    public string? FolderName { get; set; }
    public List<long> ModelRefs { get; set; } = new();
    public string Key => Method + " " + Path;
}

public sealed class RemoteModelRef
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public List<long> ModelRefs { get; set; } = new();
}

/// <summary>
/// 整分支概览：一次 export 得到全部接口与模型的引用关系，用于重复身份、悬空引用与共享模型影响分析。
/// 不保存脚本、鉴权等原生配置；比对与写入使用逐资源 get 的权威内容。
/// </summary>
public sealed class RemoteSnapshot
{
    public DateTimeOffset TakenAt { get; set; }
    public string ProjectId { get; set; } = "";
    public string? Branch { get; set; }
    public string? CliVersion { get; set; }
    public bool Complete { get; set; }
    public List<string> Problems { get; set; } = new();
    public int ListTotal { get; set; }
    public List<RemoteEndpointRef> Endpoints { get; set; } = new();
    public List<RemoteModelRef> Models { get; set; } = new();
    public List<RemoteFolder> Folders { get; set; } = new();

    public IEnumerable<IGrouping<string, RemoteEndpointRef>> Duplicates() =>
        Endpoints.GroupBy(e => e.Key, StringComparer.Ordinal).Where(g => g.Count() > 1);

    /// <summary>直接或间接引用某模型的全部接口（沿模型引用图反向遍历，已访问集合防止递归模型死循环）。</summary>
    public HashSet<long> EndpointsReferencing(long modelId)
    {
        var reverse = new Dictionary<long, List<long>>();
        foreach (var model in Models)
        {
            foreach (var target in model.ModelRefs)
            {
                if (!reverse.TryGetValue(target, out var list)) reverse[target] = list = new List<long>();
                list.Add(model.Id);
            }
        }
        var affectedModels = new HashSet<long> { modelId };
        var queue = new Queue<long>([modelId]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!reverse.TryGetValue(current, out var referrers)) continue;
            foreach (var referrer in referrers)
            {
                if (affectedModels.Add(referrer)) queue.Enqueue(referrer);
            }
        }
        return Endpoints.Where(e => e.ModelRefs.Any(affectedModels.Contains)).Select(e => e.Id).ToHashSet();
    }

    public HashSet<long> DanglingRefs()
    {
        var ids = Models.Select(m => m.Id).ToHashSet();
        return Endpoints.SelectMany(e => e.ModelRefs).Concat(Models.SelectMany(m => m.ModelRefs)).Where(id => !ids.Contains(id)).ToHashSet();
    }
}

public sealed class RemoteFolder
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long? ParentId { get; set; }
}

public sealed partial class RemoteSnapshotService(ApifoxCli cli)
{
    [GeneratedRegex(@"^#/definitions/(\d+)$")]
    private static partial Regex DefinitionRef();

    public async Task<RemoteSnapshot> TakeAsync(ApifoxTarget target, string cliVersion, JobContext job)
    {
        var projectId = target.ProjectId ?? throw new UserException("配置中没有 Apifox 项目 ID。");
        var snapshot = new RemoteSnapshot { TakenAt = DateTimeOffset.Now, ProjectId = projectId, Branch = target.BranchName, CliVersion = cliVersion };
        job.Report(0.1, "读取接口总数");
        var count = await cli.EndpointPageAsync(target, projectId, target.BranchName, 1, 1, job.Token);
        if (!count.Ok) throw new UserException("读取接口列表失败：" + count.Describe());
        snapshot.ListTotal = count.Meta?["total"]?.GetValue<int>() ?? -1;

        job.Report(0.3, "导出整分支概览（只读）");
        var file = Path.Combine(Path.GetTempPath(), "apifox-helper-export-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var export = await cli.ExportAsync(target, projectId, target.BranchName, file, job.Token);
            if (!export.Ok || !File.Exists(file)) throw new UserException("导出分支概览失败：" + export.Describe());
            job.Report(0.7, "解析接口与模型引用");
            var root = JsonNode.Parse(await File.ReadAllTextAsync(file, Encoding.UTF8, job.Token)) as JsonObject
                ?? throw new UserException("导出文件不是 JSON 对象。");
            if (root["apiCollection"] is not JsonArray apis || root["schemaCollection"] is not JsonArray schemas)
            {
                throw new UserException("导出文件缺少 apiCollection/schemaCollection，当前 CLI 版本的导出格式未经验证。");
            }
            WalkApis(apis, null, null, snapshot);
            WalkSchemas(schemas, snapshot);
        }
        finally
        {
            // 导出文件含完整原生配置，解析后立即删除，只保留引用概览。
            if (File.Exists(file)) File.Delete(file);
        }

        snapshot.Complete = true;
        if (snapshot.ListTotal >= 0 && snapshot.ListTotal != snapshot.Endpoints.Count)
        {
            snapshot.Complete = false;
            snapshot.Problems.Add($"接口列表总数 {snapshot.ListTotal} 与导出接口数 {snapshot.Endpoints.Count} 不一致，影响范围可能不完整。");
        }
        var dangling = snapshot.DanglingRefs();
        if (dangling.Count > 0) snapshot.Problems.Add($"存在 {dangling.Count} 个悬空模型引用：" + string.Join(", ", dangling.Take(10)));
        return snapshot;
    }

    internal static void WalkApis(JsonArray items, long? folderId, string? folderName, RemoteSnapshot snapshot)
    {
        foreach (var item in items.OfType<JsonObject>())
        {
            if (item["api"] is JsonObject api)
            {
                var id = ReadLong(api["id"]);
                if (id is null) continue;
                snapshot.Endpoints.Add(new RemoteEndpointRef
                {
                    Id = id.Value,
                    Method = (api["method"]?.ToString() ?? "").ToUpperInvariant(),
                    Path = api["path"]?.ToString() ?? "",
                    Name = api["name"]?.ToString() ?? item["name"]?.ToString() ?? "",
                    FolderId = folderId,
                    FolderName = folderName,
                    ModelRefs = Refs(new JsonArray(api["parameters"]?.DeepClone(), api["requestBody"]?.DeepClone(), api["responses"]?.DeepClone())).ToList(),
                });
                continue;
            }
            if (item["items"] is JsonArray children)
            {
                var id = ReadLong(item["id"]);
                var name = item["name"]?.ToString() ?? "";
                if (id is not null) snapshot.Folders.Add(new RemoteFolder { Id = id.Value, Name = name, ParentId = folderId });
                WalkApis(children, id ?? folderId, name, snapshot);
            }
        }
    }

    private static void WalkSchemas(JsonArray items, RemoteSnapshot snapshot)
    {
        foreach (var item in items.OfType<JsonObject>())
        {
            if (item["schema"] is JsonObject schema)
            {
                var idText = item["id"]?.ToString() ?? "";
                var match = DefinitionRef().Match(idText);
                var id = match.Success ? long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : ReadLong(item["id"]);
                if (id is null) continue;
                snapshot.Models.Add(new RemoteModelRef { Id = id.Value, Name = item["name"]?.ToString() ?? "", ModelRefs = Refs(schema["jsonSchema"]).ToList() });
                continue;
            }
            if (item["items"] is JsonArray children) WalkSchemas(children, snapshot);
        }
    }

    public static HashSet<long> Refs(JsonNode? node)
    {
        var result = new HashSet<long>();
        Collect(node, result);
        return result;

        static void Collect(JsonNode? n, HashSet<long> acc)
        {
            switch (n)
            {
                case JsonObject obj:
                    foreach (var (key, value) in obj)
                    {
                        if (key == "$ref" && value is JsonValue v && v.TryGetValue<string>(out var reference))
                        {
                            var m = DefinitionRef().Match(reference);
                            if (m.Success) acc.Add(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            Collect(value, acc);
                        }
                    }
                    break;
                case JsonArray arr:
                    foreach (var element in arr) Collect(element, acc);
                    break;
            }
        }
    }

    public static long? ReadLong(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<long>(out var l)) return l;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<string>(out var s) && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var e)) return e;
        return null;
    }
}
