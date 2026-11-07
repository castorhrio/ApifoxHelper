namespace ApifoxHelper.Profiles;

/// <summary>
/// 在代码工作区中查找 .csproj，供新建配置时选择 API 项目。只列目录并读取项目文件开头判断是否为 Web SDK；
/// 跳过构建输出、依赖、隐藏目录与符号链接/联接点，深度和数量有上限，误选磁盘根目录也不会长时间扫描。
/// </summary>
public static class ProjectFinder
{
    private const int MaxDepth = 6;
    private const int MaxDirectories = 5000;
    private const int MaxResults = 200;

    private static readonly HashSet<string> SkippedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", "packages", "dist", "artifacts", "TestResults",
    };

    public static object Find(string? root)
    {
        root = root?.Trim() ?? "";
        if (root.Length == 0 || !Path.IsPathFullyQualified(root) || !Directory.Exists(root))
        {
            throw new UserException("代码工作区必须是已存在的绝对路径。");
        }
        root = Path.GetFullPath(root);

        var found = new List<(string Path, bool Web, int Depth)>();
        var pending = new Queue<(string Dir, int Depth)>();
        pending.Enqueue((root, 0));
        var visited = 0;
        var truncated = false;
        while (pending.Count > 0 && !truncated)
        {
            var (dir, depth) = pending.Dequeue();
            if (++visited > MaxDirectories)
            {
                truncated = true;
                break;
            }
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
                {
                    if (found.Count >= MaxResults)
                    {
                        truncated = true;
                        break;
                    }
                    found.Add((TextUtil.ToForwardSlash(Path.GetRelativePath(root, file)), IsWebProject(file), depth));
                }
                if (depth >= MaxDepth) continue;
                foreach (var sub in new DirectoryInfo(dir).EnumerateDirectories())
                {
                    if (sub.Name.StartsWith('.') || SkippedNames.Contains(sub.Name)) continue;
                    if ((sub.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    pending.Enqueue((sub.FullName, depth + 1));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                // 无权限或扫描期间被删除的目录直接跳过。
            }
        }

        return new
        {
            root,
            truncated,
            projects = found
                .OrderByDescending(p => p.Web)
                .ThenBy(p => p.Depth)
                .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase)
                .Select(p => new { path = p.Path, web = p.Web })
                .ToList(),
        };
    }

    private static bool IsWebProject(string file)
    {
        try
        {
            using var reader = new StreamReader(file, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buffer = new char[4096];
            var read = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, read).Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
