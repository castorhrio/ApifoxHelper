namespace ApifoxHelper.Profiles;

public sealed class ProfileStore(FileStore store)
{
    /// <summary>
    /// 核心字段的字段标识（配置 JSON 的 camelCase 属性名）：保存校验失败时随 UserException.Field
    /// 定位原生对话框描红的输入框（DESIGN.md §6 校验失败描边）；REST 错误响应只取 Message，不进入网络形状。
    /// </summary>
    public const string FieldName = "name";
    public const string FieldWorkspaceRoot = "workspaceRoot";
    public const string FieldApiProject = "apiProject";
    public const string FieldAllowedSourcePaths = "allowedSourcePaths";
    public const string FieldProjectId = "projectId";
    public const string FieldSwaggerType = "type";
    public const string FieldSwaggerLocation = "location";
    public const string FieldGenerationMode = "mode";
    public const string FieldBaseUrl = "baseUrl";
    public const string FieldModel = "model";
    public const string FieldNamingPolicy = "namingPolicy";

    public List<ProjectProfile> List()
    {
        var result = new List<ProjectProfile>();
        foreach (var file in Directory.EnumerateFiles(store.ProfilesDir, "*.json"))
        {
            var profile = FileStore.ReadJson<ProjectProfile>(file);
            if (profile is not null) result.Add(profile);
        }
        return result.OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
    }

    public ProjectProfile Get(string id)
    {
        var path = PathOf(id);
        return FileStore.ReadJson<ProjectProfile>(path) ?? throw new UserException("项目配置不存在。", 404);
    }

    public ProjectProfile Save(ProjectProfile profile, bool isNew)
    {
        Normalize(profile);
        Validate(profile);
        if (isNew)
        {
            profile.Id = Ids.New("p");
            profile.CreatedAt = DateTimeOffset.Now;
        }
        else
        {
            var existing = Get(profile.Id);
            profile.CreatedAt = existing.CreatedAt;
        }
        profile.UpdatedAt = DateTimeOffset.Now;
        FileStore.WriteJson(PathOf(profile.Id), profile);
        return profile;
    }

    private string PathOf(string id) => Path.Combine(store.ProfilesDir, Ids.Validate(id) + ".json");

    private static void Normalize(ProjectProfile p)
    {
        p.Name = p.Name.Trim();
        p.WorkspaceRoot = p.WorkspaceRoot.Trim();
        p.ApiProject = TextUtil.ToForwardSlash(p.ApiProject.Trim()).TrimStart('/');
        p.AllowedSourcePaths = p.AllowedSourcePaths
            .Select(s => TextUtil.ToForwardSlash(s.Trim()).Trim('/'))
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        p.Modules = p.Modules.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();
        foreach (var module in p.Modules)
        {
            module.Name = module.Name.Trim();
            module.PathPrefixes = module.PathPrefixes.Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => x.StartsWith('/') ? x : "/" + x).ToList();
        }
        p.ResponseWrappers = p.ResponseWrappers.Where(w => !string.IsNullOrWhiteSpace(w.TypeName)).ToList();
        p.Apifox.ProjectId = string.IsNullOrWhiteSpace(p.Apifox.ProjectId) ? null : p.Apifox.ProjectId.Trim();
        p.Apifox.BranchName = string.IsNullOrWhiteSpace(p.Apifox.BranchName) ? null : p.Apifox.BranchName.Trim();
        p.Apifox.ReadConcurrency = Math.Clamp(p.Apifox.ReadConcurrency, 1, 8);
        p.Analysis.MaxTraceDepth = Math.Clamp(p.Analysis.MaxTraceDepth, 0, 6);
        p.Analysis.MaxMethodsPerEndpoint = Math.Clamp(p.Analysis.MaxMethodsPerEndpoint, 1, 200);
        p.Generation.TimeoutSeconds = Math.Clamp(p.Generation.TimeoutSeconds, 10, 600);
        p.Generation.MaxSlotsPerRequest = Math.Clamp(p.Generation.MaxSlotsPerRequest, 1, 100);
    }

    private static void Validate(ProjectProfile p)
    {
        if (p.Name.Length == 0) throw new UserException("请填写配置名称。", field: FieldName);
        if (p.WorkspaceRoot.Length == 0 || !Path.IsPathFullyQualified(p.WorkspaceRoot)) throw new UserException("代码工作区必须是绝对路径。", field: FieldWorkspaceRoot);
        if (!Directory.Exists(p.WorkspaceRoot)) throw new UserException("代码工作区目录不存在。", field: FieldWorkspaceRoot);
        if (!p.ApiProject.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) throw new UserException("API 项目必须是 .csproj 文件。", field: FieldApiProject);
        var projectPath = ResolveInside(p.WorkspaceRoot, p.ApiProject) ?? throw new UserException("API 项目必须位于代码工作区内。", field: FieldApiProject);
        if (!File.Exists(projectPath)) throw new UserException("API 项目文件不存在。", field: FieldApiProject);
        foreach (var allowed in p.AllowedSourcePaths)
        {
            if (ResolveInside(p.WorkspaceRoot, allowed) is null) throw new UserException($"回写范围“{allowed}”不在代码工作区内。", field: FieldAllowedSourcePaths);
        }
        if (p.Apifox.ProjectId is not null && !p.Apifox.ProjectId.All(char.IsDigit)) throw new UserException("Apifox 项目 ID 应为数字。", field: FieldProjectId);
        switch (p.Swagger.Type)
        {
            case "none":
                break;
            case "file":
                if (string.IsNullOrWhiteSpace(p.Swagger.Location)) throw new UserException("请填写 Swagger 文件路径。", field: FieldSwaggerLocation);
                break;
            case "url":
                if (!Uri.TryCreate(p.Swagger.Location, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new UserException("Swagger 地址必须是 http/https 的 JSON 地址。", field: FieldSwaggerLocation);
                }
                break;
            default:
                throw new UserException("Swagger 来源类型无效。", field: FieldSwaggerType);
        }
        if (p.Generation.Mode is not ("none" or "openai" or "file")) throw new UserException("草稿生成方式无效。", field: FieldGenerationMode);
        if (p.Generation.Mode == "openai")
        {
            if (!Uri.TryCreate(p.Generation.BaseUrl, UriKind.Absolute, out var baseUri)) throw new UserException("请填写模型服务地址。", field: FieldBaseUrl);
            var isLoopback = baseUri.IsLoopback;
            if (baseUri.Scheme != Uri.UriSchemeHttps && !(isLoopback && baseUri.Scheme == Uri.UriSchemeHttp))
            {
                throw new UserException("模型服务地址必须使用 https（本机地址除外）。", field: FieldBaseUrl);
            }
            if (string.IsNullOrWhiteSpace(p.Generation.Model)) throw new UserException("请填写模型名称。", field: FieldModel);
        }
        if (p.Serialization.NamingPolicy is not ("camelCase" or "none")) throw new UserException("JSON 命名策略只支持 camelCase 或 none。", field: FieldNamingPolicy);
    }

    /// <summary>
    /// 把相对路径解析到工作区内；越界（含 ..）返回 null。
    /// </summary>
    public static string? ResolveInside(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relative));
        return full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || string.Equals(full + Path.DirectorySeparatorChar, fullRoot, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    /// <summary>
    /// 源文件是否在允许回写范围内（按相对路径前缀，忽略大小写）。
    /// </summary>
    public static bool IsWriteAllowed(ProjectProfile profile, string relativePath)
    {
        var rel = TextUtil.ToForwardSlash(relativePath);
        return profile.AllowedSourcePaths.Any(prefix =>
            rel.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
    }
}
