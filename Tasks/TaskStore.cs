using ApifoxHelper.Profiles;

namespace ApifoxHelper.Tasks;

public sealed class TaskStore(FileStore store, ProfileStore profiles)
{
    public const string TaskFile = "task.json";

    public static string ToolVersion => typeof(TaskStore).Assembly.GetName().Version?.ToString() ?? "0";

    public CheckTask Create(CreateTaskRequest request)
    {
        var profile = profiles.Get(request.ProfileId);
        var prefixes = new List<string>();
        foreach (var name in request.ModuleNames)
        {
            var module = profile.Modules.FirstOrDefault(m => m.Name == name) ?? throw new UserException($"配置中没有模块「{name}」。");
            prefixes.AddRange(module.PathPrefixes);
        }
        prefixes.AddRange(request.PathPrefixes.Select(p => p.Trim()).Where(p => p.Length > 0).Select(p => p.StartsWith('/') ? p : "/" + p));
        var keys = request.EndpointKeys.Select(NormalizeEndpointKey).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();

        var task = new CheckTask
        {
            Id = Ids.New("t"),
            Name = string.IsNullOrWhiteSpace(request.Name) ? $"{profile.Name} {DateTime.Now:MM-dd HH:mm}" : request.Name.Trim(),
            ProfileId = profile.Id,
            ProfileSnapshot = profile,
            ModuleNames = request.ModuleNames.ToList(),
            PathPrefixes = prefixes.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            EndpointKeys = keys,
            SourceVersionNote = string.IsNullOrWhiteSpace(request.SourceVersionNote) ? profile.SourceVersionNote : request.SourceVersionNote.Trim(),
            OfflineMode = request.OfflineMode,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
            ToolVersion = ToolVersion,
        };
        Directory.CreateDirectory(store.TaskDir(task.Id));
        Save(task);
        return task;
    }

    public CheckTask Get(string id) =>
        FileStore.ReadJson<CheckTask>(store.TaskFile(id, TaskFile)) ?? throw new UserException("任务不存在。", 404);

    public List<CheckTask> List()
    {
        var result = new List<CheckTask>();
        foreach (var dir in Directory.EnumerateDirectories(store.TasksDir))
        {
            var task = FileStore.ReadJson<CheckTask>(Path.Combine(dir, TaskFile));
            if (task is not null) result.Add(task);
        }
        return result.OrderByDescending(t => t.CreatedAt).ToList();
    }

    public void Save(CheckTask task)
    {
        task.UpdatedAt = DateTimeOffset.Now;
        FileStore.WriteJson(store.TaskFile(task.Id, TaskFile), task);
    }

    /// <summary>
    /// 在任务锁内读取—修改—保存，防止并发请求互相覆盖。
    /// </summary>
    public async Task<CheckTask> UpdateAsync(string id, Action<CheckTask> change, CancellationToken ct = default)
    {
        using (await KeyedLocks.AcquireAsync("task:" + id, ct))
        {
            var task = Get(id);
            change(task);
            Save(task);
            return task;
        }
    }

    public string FilePath(string taskId, string name) => store.TaskFile(taskId, name);

    public T? Read<T>(string taskId, string name) => FileStore.ReadJson<T>(store.TaskFile(taskId, name));

    public void Write<T>(string taskId, string name, T value) => FileStore.WriteJson(store.TaskFile(taskId, name), value);

    public static string NormalizeEndpointKey(string key)
    {
        var parts = key.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) return "";
        var path = parts[1].Trim();
        if (!path.StartsWith('/')) path = "/" + path;
        return parts[0].ToUpperInvariant() + " " + path;
    }

    public static bool InScope(CheckTask task, string endpointKey, string path)
    {
        if (task.PathPrefixes.Count == 0 && task.EndpointKeys.Count == 0) return true;
        if (task.EndpointKeys.Contains(endpointKey, StringComparer.Ordinal)) return true;
        // 模块筛选只用于选范围：ASP.NET Core 路由匹配不区分大小写，前缀也按忽略大小写比较；接口身份仍保留原大小写。
        return task.PathPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
