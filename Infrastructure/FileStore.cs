namespace ApifoxHelper.Infrastructure;

/// <summary>
/// 本地资料目录。所有 JSON 先写临时文件再原子替换，执行日志按行追加。
/// </summary>
public sealed class FileStore
{
    public FileStore(string root)
    {
        Root = Path.GetFullPath(root);
        foreach (var dir in new[] { "profiles", "mappings", "baselines", "tasks", "locks", "compat", "credentials" })
        {
            Directory.CreateDirectory(Path.Combine(Root, dir));
        }
    }

    public string Root { get; }

    public string ProfilesDir => Path.Combine(Root, "profiles");

    public string TasksDir => Path.Combine(Root, "tasks");

    public string MappingsDir => Path.Combine(Root, "mappings");

    public string BaselinesDir => Path.Combine(Root, "baselines");

    public string LocksDir => Path.Combine(Root, "locks");

    public string CompatDir => Path.Combine(Root, "compat");

    public string CredentialsDir => Path.Combine(Root, "credentials");

    public string TaskDir(string taskId) => Path.Combine(TasksDir, Ids.Validate(taskId));

    public string TaskFile(string taskId, string name) => Path.Combine(TaskDir(taskId), name);

    /// <summary>
    /// 远端目标键：项目 + 分支。不同项目/分支的映射、基线和锁完全隔离。
    /// </summary>
    public static string TargetKey(string projectId, string? branch)
    {
        var branchPart = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
        return $"{projectId}_{Ids.ShortHash(branchPart, 12)}";
    }

    public static void WriteJson<T>(string path, T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options);
        WriteBytesAtomic(path, bytes);
    }

    public static void WriteText(string path, string text) => WriteBytesAtomic(path, new UTF8Encoding(false).GetBytes(text));

    public static void WriteBytesAtomic(string path, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "." + Path.GetFileName(path) + ".tmp-" + Guid.NewGuid().ToString("N"));
        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static T? ReadJson<T>(string path)
    {
        if (!File.Exists(path)) return default;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return JsonSerializer.Deserialize<T>(fs, JsonDefaults.Options);
    }

    public static JsonNode? ReadNode(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8));
    }

    public static void AppendJsonLine(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(value, JsonDefaults.Compact) + "\r\n";
        var bytes = Encoding.UTF8.GetBytes(line);
        using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        fs.Write(bytes);
        fs.Flush(flushToDisk: true);
    }

    public static List<JsonNode> ReadJsonLines(string path)
    {
        var result = new List<JsonNode>();
        if (!File.Exists(path)) return result;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var node = JsonNode.Parse(line);
                if (node is not null) result.Add(node);
            }
            catch (JsonException)
            {
                // 进程中断可能留下半行；保留其余记录，由核对流程判断未结束动作。
            }
        }
        return result;
    }
}
