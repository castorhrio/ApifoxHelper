using ApifoxHelper.Profiles;

namespace ApifoxHelper.Apifox;

public sealed class CliResult
{
    public int ExitCode { get; init; }
    public bool TimedOut { get; init; }
    public bool Success { get; init; }
    public JsonNode? Data { get; init; }
    public JsonNode? Meta { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ParseError { get; init; }
    public TimeSpan Elapsed { get; init; }

    /// <summary>平台明确回答“资源不存在”。其他失败（网络、权限、解析）都不能当作不存在。</summary>
    public bool IsNotFound => !Success && ErrorCode == "404000";

    /// <summary>输出可靠解析且命令成功。</summary>
    public bool Ok => Success && ParseError is null && !TimedOut && ExitCode == 0;

    public string Describe()
    {
        if (TimedOut) return "命令超时";
        if (ParseError is not null) return "输出无法可靠解析：" + ParseError;
        if (!Success) return $"{ErrorCode ?? "错误"}：{ErrorMessage ?? "未知错误"}（退出码 {ExitCode}{ExitMeaning(ExitCode)}）";
        return "成功";
    }

    private static string ExitMeaning(int code) => code switch
    {
        1 => "，操作/API 失败",
        2 => "，认证失败",
        3 => "，输入或校验失败",
        4 => "，Schema/发现失败",
        5 => "，CLI 内部错误",
        _ => "",
    };
}

/// <summary>
/// Apifox CLI 调用边界：明确传递项目与分支；node + cli.js + 参数列表启动，不经 shell；
/// 只接受“前导说明行 + 单个完整 JSON 对象”的输出，其余情况一律视为无法可靠解析。
/// 读取可有限重试，写入绝不自动重发。
/// </summary>
public sealed partial class ApifoxCli
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(5);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[ -/]*[@-~]")]
    private static partial Regex AnsiPattern();

    public sealed record Entry(string Node, string Script);

    public static Entry Resolve(ApifoxTarget target)
    {
        var node = target.NodePath;
        if (string.IsNullOrWhiteSpace(node))
        {
            node = FindOnPath("node.exe") ?? FindOnPath("node");
        }
        if (string.IsNullOrWhiteSpace(node) || !File.Exists(node)) throw new UserException("未找到 node 可执行文件；请安装 Node.js，或在项目设置中填写 node 路径。");
        var script = target.CliScriptPath;
        if (string.IsNullOrWhiteSpace(script))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            script = Path.Combine(appData, "npm", "node_modules", "apifox-cli", "bin", "cli.js");
        }
        if (!File.Exists(script)) throw new UserException("未找到 apifox-cli（cli.js）；请先执行 npm install -g apifox-cli，或在项目设置中填写 cli.js 路径。");
        return new Entry(node, script);
    }

    private static string? FindOnPath(string fileName)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }
        return null;
    }

    public async Task<CliResult> RunAsync(ApifoxTarget target, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var entry = Resolve(target);
        var args = new List<string> { entry.Script };
        args.AddRange(arguments);
        var result = await ProcessRunner.RunAsync(entry.Node, args, Path.GetTempPath(), timeout, ct);
        if (result.TimedOut)
        {
            return new CliResult { ExitCode = -1, TimedOut = true, Elapsed = result.Elapsed, ParseError = "命令超时" };
        }
        return Parse(result);
    }

    public static CliResult Parse(ProcessResult process)
    {
        var text = AnsiPattern().Replace(process.StdOut ?? "", "");
        var start = -1;
        var lines = text.Split('\n');
        var offset = 0;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith('{'))
            {
                start = offset + (line.Length - line.TrimStart().Length);
                break;
            }
            offset += line.Length + 1;
        }
        if (start < 0)
        {
            var stderr = Redactor.Redact(TextUtil.Truncate(AnsiPattern().Replace(process.StdErr ?? "", "").Trim(), 300));
            return new CliResult { ExitCode = process.ExitCode, Elapsed = process.Elapsed, ParseError = "没有 JSON 输出" + (stderr.Length > 0 ? "：" + stderr : "") };
        }

        var bytes = Encoding.UTF8.GetBytes(text[start..]);
        JsonNode? root;
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Disallow });
            root = JsonNode.Parse(ref reader);
            var rest = Encoding.UTF8.GetString(bytes, (int)reader.BytesConsumed, bytes.Length - (int)reader.BytesConsumed);
            if (rest.Trim().Length > 0)
            {
                return new CliResult { ExitCode = process.ExitCode, Elapsed = process.Elapsed, ParseError = "JSON 之后还有其他输出：" + TextUtil.Truncate(Redactor.Redact(rest.Trim()), 120) };
            }
        }
        catch (JsonException ex)
        {
            return new CliResult { ExitCode = process.ExitCode, Elapsed = process.Elapsed, ParseError = "JSON 不完整：" + ex.Message };
        }

        if (root is not JsonObject obj || obj["success"] is not JsonValue successValue || !successValue.TryGetValue<bool>(out var success))
        {
            return new CliResult { ExitCode = process.ExitCode, Elapsed = process.Elapsed, ParseError = "输出缺少 success 字段" };
        }
        if (success && process.ExitCode != 0)
        {
            return new CliResult { ExitCode = process.ExitCode, Elapsed = process.Elapsed, ParseError = "success=true 但退出码非 0" };
        }
        var error = obj["error"] as JsonObject;
        return new CliResult
        {
            ExitCode = process.ExitCode,
            Elapsed = process.Elapsed,
            Success = success,
            Data = obj["data"]?.DeepClone(),
            Meta = obj["meta"]?.DeepClone(),
            ErrorCode = error?["code"]?.ToString(),
            ErrorMessage = Redactor.Redact(error?["message"]?.ToString()),
        };
    }

    /// <summary>只读命令：超时、无法解析或非“不存在”的失败最多重试 3 次（递增等待）。</summary>
    public async Task<CliResult> ReadAsync(ApifoxTarget target, IReadOnlyList<string> arguments, CancellationToken ct, TimeSpan? timeout = null)
    {
        CliResult last = new() { ParseError = "未执行" };
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            last = await RunAsync(target, arguments, timeout ?? ReadTimeout, ct);
            if (last.Ok || last.IsNotFound || last.ExitCode is 2 or 3) return last;
            await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
        }
        return last;
    }

    public Task<CliResult> WriteAsync(ApifoxTarget target, IReadOnlyList<string> arguments, CancellationToken ct) =>
        RunAsync(target, arguments, WriteTimeout, ct);

    public static List<string> Scope(string projectId, string? branch)
    {
        var args = new List<string> { "--project", projectId };
        if (!string.IsNullOrWhiteSpace(branch))
        {
            args.Add("--branch");
            args.Add(branch);
        }
        return args;
    }

    public async Task<string> VersionAsync(ApifoxTarget target, CancellationToken ct)
    {
        var entry = Resolve(target);
        var result = await ProcessRunner.RunAsync(entry.Node, [entry.Script, "--version"], Path.GetTempPath(), TimeSpan.FromSeconds(30), ct);
        var version = AnsiPattern().Replace(result.StdOut, "").Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        if (result.ExitCode != 0 || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+")) throw new UserException("无法读取 Apifox CLI 版本。");
        return version;
    }

    /// <summary>登录状态：auth status 退出码 0 且输出包含已登录账号表格。不读取 CLI 保存的令牌。</summary>
    public async Task<(bool LoggedIn, string Message)> AuthStatusAsync(ApifoxTarget target, CancellationToken ct)
    {
        var entry = Resolve(target);
        var result = await ProcessRunner.RunAsync(entry.Node, [entry.Script, "auth", "status"], Path.GetTempPath(), TimeSpan.FromSeconds(30), ct);
        var text = AnsiPattern().Replace(result.StdOut + "\n" + result.StdErr, "").Trim();
        var loggedIn = result.ExitCode == 0 && text.Contains('*') && !text.Contains("未登录", StringComparison.Ordinal);
        var account = text.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith('*'))?.Trim().TrimStart('*').Trim();
        return (loggedIn, loggedIn ? "已登录：" + Redactor.Redact(TextUtil.NormalizeSpace(account)) : "未登录或登录已失效，请在终端执行 apifox auth login。");
    }

    public Task<CliResult> ListProjectsAsync(ApifoxTarget target, CancellationToken ct) => ReadAsync(target, ["project", "list"], ct);

    public Task<CliResult> ListBranchesAsync(ApifoxTarget target, string projectId, CancellationToken ct) =>
        ReadAsync(target, ["branch", "list", "--project", projectId, "--type", "all"], ct);

    public Task<CliResult> ListFoldersAsync(ApifoxTarget target, string projectId, string? branch, CancellationToken ct) =>
        ReadAsync(target, ["folder", "list", .. Scope(projectId, branch), "--type", "endpoint"], ct);

    public Task<CliResult> EndpointPageAsync(ApifoxTarget target, string projectId, string? branch, int page, int pageSize, CancellationToken ct) =>
        ReadAsync(target, ["endpoint", "list", .. Scope(projectId, branch), "--page", page.ToString(CultureInfo.InvariantCulture), "--page-size", pageSize.ToString(CultureInfo.InvariantCulture)], ct);

    public Task<CliResult> FindEndpointsByPathAsync(ApifoxTarget target, string projectId, string? branch, string path, CancellationToken ct) =>
        ReadAsync(target, ["endpoint", "list", .. Scope(projectId, branch), "--path-contains", path], ct);

    public Task<CliResult> GetEndpointAsync(ApifoxTarget target, string projectId, string? branch, long id, CancellationToken ct) =>
        ReadAsync(target, ["endpoint", "get", id.ToString(CultureInfo.InvariantCulture), .. Scope(projectId, branch)], ct);

    public Task<CliResult> GetSchemaAsync(ApifoxTarget target, string projectId, string? branch, long id, CancellationToken ct) =>
        ReadAsync(target, ["schema", "get", id.ToString(CultureInfo.InvariantCulture), .. Scope(projectId, branch)], ct);

    public Task<CliResult> ListSchemasAsync(ApifoxTarget target, string projectId, string? branch, string? name, CancellationToken ct)
    {
        var args = new List<string> { "schema", "list" };
        args.AddRange(Scope(projectId, branch));
        if (!string.IsNullOrWhiteSpace(name))
        {
            args.Add("--name");
            args.Add(name);
        }
        return ReadAsync(target, args, ct);
    }

    public Task<CliResult> CliSchemaAsync(ApifoxTarget target, string key, CancellationToken ct) => ReadAsync(target, ["cli-schema", "get", key], ct);

    public async Task<(bool Valid, string Message)> ValidateAsync(ApifoxTarget target, string key, string file, CancellationToken ct)
    {
        var result = await RunAsync(target, ["cli-schema", "validate", key, "--file", file], ReadTimeout, ct);
        if (result.Ok) return (true, "通过");
        return (false, result.Describe());
    }

    public Task<CliResult> ExportAsync(ApifoxTarget target, string projectId, string? branch, string output, CancellationToken ct) =>
        ReadAsync(target, ["export", .. Scope(projectId, branch), "--format", "apifox", "--no-include-api-cases", "--output", output], ct, ExportTimeout);

    public Task<CliResult> CreateEndpointAsync(ApifoxTarget target, string projectId, string? branch, string file, CancellationToken ct) =>
        WriteAsync(target, ["endpoint", "create", .. Scope(projectId, branch), "--file", file], ct);

    public Task<CliResult> UpdateEndpointAsync(ApifoxTarget target, string projectId, string? branch, long id, string file, CancellationToken ct) =>
        WriteAsync(target, ["endpoint", "update", id.ToString(CultureInfo.InvariantCulture), .. Scope(projectId, branch), "--file", file], ct);

    public Task<CliResult> CreateSchemaAsync(ApifoxTarget target, string projectId, string? branch, string file, CancellationToken ct) =>
        WriteAsync(target, ["schema", "create", .. Scope(projectId, branch), "--file", file], ct);

    public Task<CliResult> UpdateSchemaAsync(ApifoxTarget target, string projectId, string? branch, long id, string file, CancellationToken ct) =>
        WriteAsync(target, ["schema", "update", id.ToString(CultureInfo.InvariantCulture), .. Scope(projectId, branch), "--file", file], ct);
}
