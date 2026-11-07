using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using ApifoxHelper.Profiles;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace ApifoxHelper.Source;

public enum AnalysisLevel
{
    /// <summary>引用完整，可做符号与类型分析。</summary>
    Semantic,

    /// <summary>部分引用缺失（例如未还原的包），相关符号会降级。</summary>
    Partial,

    /// <summary>只能做语法扫描。</summary>
    SyntaxOnly,
}

public sealed class SourceFileEntry
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Hash { get; init; }
    public bool HasBom { get; init; }
    public bool IsUtf8 { get; init; }
    public long Length { get; init; }
    public DateTime LastWriteUtc { get; init; }
    public required SyntaxTree Tree { get; init; }
    public bool IsSynthetic { get; init; }
}

public sealed class LoadedProject
{
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public required string Directory { get; init; }
    public required string AssemblyName { get; init; }
    public required string TargetFramework { get; init; }
    public required CSharpParseOptions ParseOptions { get; init; }
    public bool IsWebSdk { get; init; }
    public bool GenerateDocumentationFile { get; init; }
    public string? DocumentationFilePath { get; init; }
    public bool AssetsFileFound { get; set; }
    public int MissingPackageAssets { get; set; }
    public List<SourceFileEntry> Files { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<LoadedProject> References { get; } = new();
    public CSharpCompilation Compilation { get; set; } = null!;
    public MetadataReference ReferenceForDependents { get; set; } = null!;
}

public sealed class LoadedWorkspace
{
    private readonly ConcurrentDictionary<SyntaxTree, SemanticModel> _models = new();
    private ImplementationIndex? _index;

    public required string WorkspaceRoot { get; init; }
    public required LoadedProject Root { get; init; }
    public required List<LoadedProject> Projects { get; init; }
    public required Dictionary<SyntaxTree, LoadedProject> TreeOwner { get; init; }
    public required Dictionary<string, SourceFileEntry> FilesByPath { get; init; }
    public required List<string> Warnings { get; init; }
    public AnalysisLevel Level { get; set; }
    public required string ManifestHash { get; init; }
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.Now;

    public ImplementationIndex Index => _index ??= ImplementationIndex.Build(this);

    public SemanticModel Model(SyntaxTree tree) =>
        _models.GetOrAdd(tree, t => TreeOwner[t].Compilation.GetSemanticModel(t));

    public SourceFileEntry? FileOf(SyntaxTree tree) =>
        FilesByPath.TryGetValue(tree.FilePath, out var entry) ? entry : null;

    /// <summary>
    /// 用文档 ID 在根项目编译中解析符号；跨项目比较符号时统一用这个视角，避免不同编译的同名符号不相等。
    /// </summary>
    public ISymbol? ResolveInRoot(string documentationId) =>
        DocumentationCommentId.GetFirstSymbolForDeclarationId(documentationId, Root.Compilation);

    public IEnumerable<INamedTypeSymbol> SourceTypes()
    {
        var assemblies = new List<IAssemblySymbol> { Root.Compilation.Assembly };
        foreach (var project in Projects)
        {
            if (ReferenceEquals(project, Root)) continue;
            if (Root.Compilation.GetAssemblyOrModuleSymbol(project.ReferenceForDependents) is IAssemblySymbol assembly)
            {
                assemblies.Add(assembly);
            }
        }
        foreach (var assembly in assemblies)
        {
            foreach (var type in EnumerateTypes(assembly.GlobalNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateTypes(INamespaceSymbol ns)
    {
        foreach (var type in ns.GetTypeMembers())
        {
            foreach (var nested in WithNested(type)) yield return nested;
        }
        foreach (var child in ns.GetNamespaceMembers())
        {
            foreach (var type in EnumerateTypes(child)) yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nested in type.GetTypeMembers())
        {
            foreach (var t in WithNested(nested)) yield return t;
        }
    }
}

/// <summary>
/// 接口/抽象成员 → 编译范围内实现类型的索引，每次加载只构建一次。
/// </summary>
public sealed class ImplementationIndex
{
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _implementations = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _derived = new(SymbolEqualityComparer.Default);

    public static ImplementationIndex Build(LoadedWorkspace workspace)
    {
        var index = new ImplementationIndex();
        foreach (var type in workspace.SourceTypes())
        {
            if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct)) continue;
            if (!type.IsAbstract)
            {
                foreach (var iface in type.AllInterfaces)
                {
                    Add(index._implementations, iface.OriginalDefinition, type);
                }
            }
            for (var b = type.BaseType; b is not null && b.SpecialType != SpecialType.System_Object; b = b.BaseType)
            {
                Add(index._derived, b.OriginalDefinition, type);
            }
        }
        return index;
    }

    public IReadOnlyList<INamedTypeSymbol> ImplementationsOf(INamedTypeSymbol iface) =>
        _implementations.TryGetValue(iface.OriginalDefinition, out var list) ? list : Array.Empty<INamedTypeSymbol>();

    public IReadOnlyList<INamedTypeSymbol> DerivedOf(INamedTypeSymbol type) =>
        _derived.TryGetValue(type.OriginalDefinition, out var list) ? list : Array.Empty<INamedTypeSymbol>();

    private static void Add(Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> map, INamedTypeSymbol key, INamedTypeSymbol value)
    {
        if (!map.TryGetValue(key, out var list))
        {
            list = new List<INamedTypeSymbol>();
            map[key] = list;
        }
        if (!list.Contains(value, SymbolEqualityComparer.Default)) list.Add(value);
    }
}

/// <summary>
/// 不运行 MSBuild 的项目加载：读取 csproj 与最近的 Directory.Build.props 的无条件/简单条件属性，
/// 按 SDK 默认规则收集源码，引用 targeting pack 与 obj/project.assets.json 中已还原的包。
/// 不执行 restore、build 或任何自定义目标；无法评估的部分记录为警告并降级。
/// </summary>
public sealed class ProjectLoader
{
    private static readonly ConcurrentDictionary<string, CachedTree> TreeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, CachedReference> ReferenceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] ExcludedDirectories = ["bin", "obj", "node_modules"];

    private sealed record CachedTree(long Length, DateTime LastWriteUtc, string OptionsKey, SyntaxTree Tree, string Hash, bool HasBom, bool IsUtf8);

    private sealed record CachedReference(DateTime LastWriteUtc, PortableExecutableReference Reference);

    public LoadedWorkspace Load(ProjectProfile profile, CancellationToken ct)
    {
        var workspaceRoot = Path.GetFullPath(profile.WorkspaceRoot);
        var projectPath = ProfileStore.ResolveInside(workspaceRoot, profile.ApiProject)
            ?? throw new UserException("API 项目不在代码工作区内。");
        if (!File.Exists(projectPath)) throw new UserException("API 项目文件不存在：" + profile.ApiProject);

        var loaded = new Dictionary<string, LoadedProject>(StringComparer.OrdinalIgnoreCase);
        var order = new List<LoadedProject>();
        var root = LoadProject(projectPath, workspaceRoot, profile.Analysis, loaded, order, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ct);

        var treeOwner = new Dictionary<SyntaxTree, LoadedProject>();
        var files = new Dictionary<string, SourceFileEntry>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        foreach (var project in order)
        {
            foreach (var file in project.Files)
            {
                treeOwner[file.Tree] = project;
                files[file.FullPath] = file;
            }
            warnings.AddRange(project.Warnings.Select(w => $"{project.Name}: {w}"));
        }

        var manifest = string.Join("\n", files.Values.Where(f => !f.IsSynthetic).OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).Select(f => f.RelativePath + ":" + f.Hash));
        var workspace = new LoadedWorkspace
        {
            WorkspaceRoot = workspaceRoot,
            Root = root,
            Projects = order,
            TreeOwner = treeOwner,
            FilesByPath = files,
            Warnings = warnings,
            ManifestHash = Canonical.Sha256Hex(manifest),
        };
        workspace.Level = DetermineLevel(order);
        return workspace;
    }

    private static AnalysisLevel DetermineLevel(List<LoadedProject> projects)
    {
        if (projects.Any(p => p.Compilation.References.Count() <= 1)) return AnalysisLevel.SyntaxOnly;
        if (projects.Any(p => !p.AssetsFileFound || p.MissingPackageAssets > 0)) return AnalysisLevel.Partial;
        return AnalysisLevel.Semantic;
    }

    private LoadedProject LoadProject(
        string projectPath,
        string workspaceRoot,
        AnalysisSettings analysis,
        Dictionary<string, LoadedProject> loaded,
        List<LoadedProject> order,
        HashSet<string> visiting,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        projectPath = Path.GetFullPath(projectPath);
        if (loaded.TryGetValue(projectPath, out var existing)) return existing;
        if (!visiting.Add(projectPath)) throw new UserException("项目引用存在循环：" + Path.GetFileName(projectPath));

        var projectDir = Path.GetDirectoryName(projectPath)!;
        var projectName = Path.GetFileNameWithoutExtension(projectPath);
        var msbuild = new MsBuildState(projectDir, projectName, analysis.Configuration);
        var directoryBuildProps = FindUpwards(projectDir, workspaceRoot, "Directory.Build.props");
        if (directoryBuildProps is not null) msbuild.Read(directoryBuildProps);
        var sdk = msbuild.Read(projectPath);

        var tfm = msbuild.Get("TargetFramework");
        if (string.IsNullOrEmpty(tfm))
        {
            tfm = msbuild.Get("TargetFrameworks").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
            if (tfm.Length > 0) msbuild.Warnings.Add($"多目标框架项目，按第一个目标 {tfm} 分析。");
        }
        if (tfm.Length == 0)
        {
            tfm = "net8.0";
            msbuild.Warnings.Add("未读取到 TargetFramework，按 net8.0 分析。");
        }
        msbuild.Set("TargetFramework", tfm);

        var isWeb = sdk.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase);
        var isWorker = sdk.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase);
        var assemblyName = msbuild.Get("AssemblyName") is { Length: > 0 } an ? an : projectName;
        var symbols = analysis.PreprocessorSymbols?.Where(s => s.Length > 0).ToList() ?? DefaultSymbols(tfm, analysis.Configuration, msbuild.Get("DefineConstants"));
        var languageVersion = ParseLanguageVersion(msbuild.Get("LangVersion"), tfm);
        var parseOptions = new CSharpParseOptions(languageVersion, DocumentationMode.Diagnose, SourceCodeKind.Regular, symbols);
        var optionsKey = languageVersion + "|" + string.Join(";", symbols.OrderBy(s => s, StringComparer.Ordinal));

        var generateDoc = msbuild.Get("GenerateDocumentationFile").Equals("true", StringComparison.OrdinalIgnoreCase);
        var docFileProp = msbuild.Get("DocumentationFile");
        string? docPath = null;
        if (docFileProp.Length > 0)
        {
            docPath = Path.GetFullPath(Path.Combine(projectDir, docFileProp));
            generateDoc = true;
        }
        else if (generateDoc)
        {
            var outputPath = msbuild.Get("OutputPath");
            var outDir = outputPath.Length > 0
                ? Path.Combine(projectDir, outputPath)
                : Path.Combine(projectDir, "bin", analysis.Configuration, tfm);
            docPath = Path.GetFullPath(Path.Combine(outDir, assemblyName + ".xml"));
        }

        var project = new LoadedProject
        {
            Name = projectName,
            ProjectPath = projectPath,
            Directory = projectDir,
            AssemblyName = assemblyName,
            TargetFramework = tfm,
            ParseOptions = parseOptions,
            IsWebSdk = isWeb,
            GenerateDocumentationFile = generateDoc,
            DocumentationFilePath = docPath,
        };
        project.Warnings.AddRange(msbuild.Warnings);

        foreach (var reference in msbuild.Items.Where(i => i.Type == "ProjectReference"))
        {
            var include = reference.Element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            var refPath = Path.GetFullPath(Path.Combine(projectDir, msbuild.Expand(include)));
            if (!File.Exists(refPath))
            {
                project.Warnings.Add("项目引用不存在：" + include);
                continue;
            }
            project.References.Add(LoadProject(refPath, workspaceRoot, analysis, loaded, order, visiting, ct));
        }

        // 源码收集与解析。
        var sourcePaths = CollectSources(projectDir, msbuild, project.Warnings);
        var entries = new ConcurrentBag<SourceFileEntry>();
        Parallel.ForEach(sourcePaths, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, path =>
        {
            entries.Add(ParseFile(path, workspaceRoot, parseOptions, optionsKey));
        });
        project.Files.AddRange(entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase));

        var implicitUsings = msbuild.Get("ImplicitUsings");
        if (implicitUsings.Equals("enable", StringComparison.OrdinalIgnoreCase) || implicitUsings.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            var text = BuildGlobalUsings(isWeb, isWorker, msbuild);
            var syntheticPath = Path.Combine(projectDir, "obj", "__ApifoxHelper.GlobalUsings.g.cs");
            project.Files.Add(new SourceFileEntry
            {
                FullPath = syntheticPath,
                RelativePath = TextUtil.ToForwardSlash(Path.GetRelativePath(workspaceRoot, syntheticPath)),
                Hash = Canonical.Sha256Hex(text),
                IsUtf8 = true,
                Tree = CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, syntheticPath, ct),
                IsSynthetic = true,
            });
        }
        else
        {
            var usings = msbuild.Items.Where(i => i.Type == "Using").ToList();
            if (usings.Count > 0)
            {
                var text = BuildUsingItems(usings, msbuild, new List<string>());
                var syntheticPath = Path.Combine(projectDir, "obj", "__ApifoxHelper.Usings.g.cs");
                project.Files.Add(new SourceFileEntry
                {
                    FullPath = syntheticPath,
                    RelativePath = TextUtil.ToForwardSlash(Path.GetRelativePath(workspaceRoot, syntheticPath)),
                    Hash = Canonical.Sha256Hex(text),
                    IsUtf8 = true,
                    Tree = CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parseOptions, syntheticPath, ct),
                    IsSynthetic = true,
                });
            }
        }

        // 引用：框架参考程序集 + 已还原的包 + 项目引用。
        var references = new List<MetadataReference>();
        var frameworkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.NETCore.App" };
        if (isWeb) frameworkNames.Add("Microsoft.AspNetCore.App");
        foreach (var fr in msbuild.Items.Where(i => i.Type == "FrameworkReference"))
        {
            var include = fr.Element.Attribute("Include")?.Value;
            if (!string.IsNullOrWhiteSpace(include)) frameworkNames.Add(include.Trim());
        }

        var packageAssemblies = ReadAssets(projectDir, msbuild, tfm, project, frameworkNames);
        var candidates = new List<string>();
        foreach (var framework in frameworkNames)
        {
            var dir = FrameworkReferenceDirectory(framework, tfm);
            if (dir is null)
            {
                project.Warnings.Add($"未找到框架参考程序集 {framework}（{tfm}），相关符号无法解析。");
                continue;
            }
            candidates.AddRange(Directory.EnumerateFiles(dir, "*.dll"));
        }
        candidates.AddRange(packageAssemblies);
        foreach (var path in Deduplicate(candidates))
        {
            var reference = CreateReference(path);
            if (reference is not null) references.Add(reference);
        }
        foreach (var dependency in project.References)
        {
            references.Add(dependency.ReferenceForDependents);
        }

        var outputKind = isWeb || isWorker || msbuild.Get("OutputType").Equals("Exe", StringComparison.OrdinalIgnoreCase) || msbuild.Get("OutputType").Equals("WinExe", StringComparison.OrdinalIgnoreCase)
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;
        var nullable = msbuild.Get("Nullable").ToLowerInvariant() switch
        {
            "enable" => NullableContextOptions.Enable,
            "warnings" => NullableContextOptions.Warnings,
            "annotations" => NullableContextOptions.Annotations,
            _ => NullableContextOptions.Disable,
        };
        var options = new CSharpCompilationOptions(
            outputKind,
            nullableContextOptions: nullable,
            allowUnsafe: msbuild.Get("AllowUnsafeBlocks").Equals("true", StringComparison.OrdinalIgnoreCase),
            concurrentBuild: true);
        project.Compilation = CSharpCompilation.Create(assemblyName, project.Files.Select(f => f.Tree), references, options);
        project.ReferenceForDependents = project.Compilation.ToMetadataReference();

        visiting.Remove(projectPath);
        loaded[projectPath] = project;
        order.Add(project);
        return project;
    }

    private static SourceFileEntry ParseFile(string path, string workspaceRoot, CSharpParseOptions options, string optionsKey)
    {
        var info = new FileInfo(path);
        if (TreeCache.TryGetValue(path, out var cached)
            && cached.Length == info.Length
            && cached.LastWriteUtc == info.LastWriteTimeUtc
            && cached.OptionsKey == optionsKey)
        {
            return new SourceFileEntry
            {
                FullPath = path,
                RelativePath = TextUtil.ToForwardSlash(Path.GetRelativePath(workspaceRoot, path)),
                Hash = cached.Hash,
                HasBom = cached.HasBom,
                IsUtf8 = cached.IsUtf8,
                Length = cached.Length,
                LastWriteUtc = cached.LastWriteUtc,
                Tree = cached.Tree,
            };
        }

        var file = TextFiles.Read(path);
        var tree = CSharpSyntaxTree.ParseText(SourceText.From(file.Text, Encoding.UTF8), options, path);
        TreeCache[path] = new CachedTree(info.Length, info.LastWriteTimeUtc, optionsKey, tree, file.Hash, file.HasBom, file.IsUtf8);
        return new SourceFileEntry
        {
            FullPath = path,
            RelativePath = TextUtil.ToForwardSlash(Path.GetRelativePath(workspaceRoot, path)),
            Hash = file.Hash,
            HasBom = file.HasBom,
            IsUtf8 = file.IsUtf8,
            Length = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            Tree = tree,
        };
    }

    private static List<string> CollectSources(string projectDir, MsBuildState msbuild, List<string> warnings)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enableDefault = !msbuild.Get("EnableDefaultCompileItems").Equals("false", StringComparison.OrdinalIgnoreCase)
            && !msbuild.Get("EnableDefaultItems").Equals("false", StringComparison.OrdinalIgnoreCase);
        if (enableDefault)
        {
            foreach (var file in EnumerateCs(projectDir)) result.Add(file);
        }

        foreach (var item in msbuild.Items.Where(i => i.Type == "Compile"))
        {
            var include = item.Element.Attribute("Include")?.Value;
            var remove = item.Element.Attribute("Remove")?.Value;
            if (!string.IsNullOrWhiteSpace(remove))
            {
                foreach (var pattern in SplitPatterns(msbuild.Expand(remove)))
                {
                    var regex = GlobToRegex(pattern);
                    result.RemoveWhere(f => regex.IsMatch(TextUtil.ToForwardSlash(Path.GetRelativePath(projectDir, f))));
                }
            }
            if (!string.IsNullOrWhiteSpace(include))
            {
                foreach (var pattern in SplitPatterns(msbuild.Expand(include)))
                {
                    foreach (var file in ExpandInclude(projectDir, pattern, warnings)) result.Add(file);
                }
            }
        }
        return result.ToList();
    }

    private static IEnumerable<string> EnumerateCs(string dir)
    {
        var stack = new Stack<string>();
        stack.Push(dir);
        var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint };
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*.cs", options)) yield return file;
            foreach (var sub in Directory.EnumerateDirectories(current, "*", options))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith('.') || ExcludedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase) && Path.GetDirectoryName(sub) == dir.TrimEnd(Path.DirectorySeparatorChar))
                {
                    continue;
                }
                if (name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;
                stack.Push(sub);
            }
        }
    }

    private static IEnumerable<string> ExpandInclude(string projectDir, string pattern, List<string> warnings)
    {
        var normalized = pattern.Replace('/', Path.DirectorySeparatorChar);
        var wildcard = normalized.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            var full = Path.GetFullPath(Path.Combine(projectDir, normalized));
            if (File.Exists(full)) return [full];
            warnings.Add("Compile Include 文件不存在：" + pattern);
            return [];
        }
        var baseEnd = normalized.LastIndexOf(Path.DirectorySeparatorChar, wildcard);
        var baseDir = Path.GetFullPath(Path.Combine(projectDir, baseEnd >= 0 ? normalized[..baseEnd] : "."));
        if (!Directory.Exists(baseDir)) return [];
        var rest = baseEnd >= 0 ? normalized[(baseEnd + 1)..] : normalized;
        var regex = GlobToRegex(TextUtil.ToForwardSlash(rest));
        return Directory.EnumerateFiles(baseDir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
            .Where(f => regex.IsMatch(TextUtil.ToForwardSlash(Path.GetRelativePath(baseDir, f))));
    }

    private static IEnumerable<string> SplitPatterns(string value) =>
        value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static Regex GlobToRegex(string glob)
    {
        var g = TextUtil.ToForwardSlash(glob);
        var sb = new StringBuilder("^");
        for (var i = 0; i < g.Length; i++)
        {
            var c = g[i];
            if (c == '*')
            {
                if (i + 1 < g.Length && g[i + 1] == '*')
                {
                    var followedBySlash = i + 2 < g.Length && g[i + 2] == '/';
                    sb.Append(followedBySlash ? "(?:.*/)?" : ".*");
                    i += followedBySlash ? 2 : 1;
                }
                else
                {
                    sb.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildGlobalUsings(bool isWeb, bool isWorker, MsBuildState msbuild)
    {
        var namespaces = new List<string>
        {
            "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Threading", "System.Threading.Tasks",
        };
        if (isWeb)
        {
            namespaces.AddRange([
                "System.Net.Http.Json", "Microsoft.AspNetCore.Builder", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing", "Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection",
                "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging",
            ]);
        }
        else if (isWorker)
        {
            namespaces.AddRange(["Microsoft.Extensions.Configuration", "Microsoft.Extensions.DependencyInjection", "Microsoft.Extensions.Hosting", "Microsoft.Extensions.Logging"]);
        }
        return BuildUsingItems(msbuild.Items.Where(i => i.Type == "Using").ToList(), msbuild, namespaces);
    }

    private static string BuildUsingItems(List<MsBuildItem> items, MsBuildState msbuild, List<string> namespaces)
    {
        var lines = new List<string>();
        var removed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var remove = item.Element.Attribute("Remove")?.Value;
            if (!string.IsNullOrWhiteSpace(remove)) removed.Add(msbuild.Expand(remove).Trim());
        }
        foreach (var ns in namespaces.Where(n => !removed.Contains(n)).Distinct(StringComparer.Ordinal))
        {
            lines.Add($"global using global::{ns};");
        }
        foreach (var item in items)
        {
            var include = item.Element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include)) continue;
            include = msbuild.Expand(include).Trim();
            if (removed.Contains(include) || namespaces.Contains(include)) continue;
            var alias = item.Element.Attribute("Alias")?.Value ?? item.Element.Element(item.Element.Name.Namespace + "Alias")?.Value;
            var isStatic = (item.Element.Attribute("Static")?.Value ?? item.Element.Element(item.Element.Name.Namespace + "Static")?.Value)?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
            if (!string.IsNullOrWhiteSpace(alias)) lines.Add($"global using {alias.Trim()} = global::{include};");
            else if (isStatic) lines.Add($"global using static global::{include};");
            else lines.Add($"global using global::{include};");
        }
        return string.Join("\n", lines) + "\n";
    }

    private static List<string> DefaultSymbols(string tfm, string configuration, string defineConstants)
    {
        var symbols = new List<string> { "TRACE" };
        if (configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase)) symbols.Add("DEBUG");
        if (configuration.Equals("Release", StringComparison.OrdinalIgnoreCase)) symbols.Add("RELEASE");
        var match = Regex.Match(tfm, @"^net(\d+)\.(\d+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var major = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var minor = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            if (major >= 5)
            {
                symbols.AddRange(["NET", "NETCOREAPP", $"NET{major}_{minor}"]);
                for (var v = 5; v <= major; v++) symbols.Add($"NET{v}_0_OR_GREATER");
                foreach (var core in new[] { "1_0", "1_1", "2_0", "2_1", "2_2", "3_0", "3_1" }) symbols.Add($"NETCOREAPP{core}_OR_GREATER");
            }
        }
        symbols.AddRange(defineConstants.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !s.Contains('$')));
        return symbols.Distinct(StringComparer.Ordinal).ToList();
    }

    private static LanguageVersion ParseLanguageVersion(string value, string tfm)
    {
        if (value.Length > 0 && LanguageVersionFacts.TryParse(value, out var parsed)) return parsed;
        if (tfm.StartsWith("net8", StringComparison.OrdinalIgnoreCase)) return LanguageVersion.CSharp12;
        if (tfm.StartsWith("net9", StringComparison.OrdinalIgnoreCase)) return LanguageVersion.CSharp13;
        if (tfm.StartsWith("net7", StringComparison.OrdinalIgnoreCase)) return LanguageVersion.CSharp11;
        if (tfm.StartsWith("net6", StringComparison.OrdinalIgnoreCase)) return LanguageVersion.CSharp10;
        return LanguageVersion.Latest;
    }

    private static List<string> ReadAssets(string projectDir, MsBuildState msbuild, string tfm, LoadedProject project, HashSet<string> frameworkNames)
    {
        var result = new List<string>();
        var objDir = msbuild.Get("BaseIntermediateOutputPath") is { Length: > 0 } custom ? Path.Combine(projectDir, custom) : Path.Combine(projectDir, "obj");
        var assetsPath = Path.Combine(objDir, "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            project.Warnings.Add("未找到 obj/project.assets.json（项目可能未还原），包中的类型无法解析；工具不会自动 restore。");
            return result;
        }
        project.AssetsFileFound = true;
        JsonNode? assets;
        try
        {
            assets = JsonNode.Parse(File.ReadAllText(assetsPath, Encoding.UTF8));
        }
        catch (JsonException ex)
        {
            project.Warnings.Add("project.assets.json 无法解析：" + ex.Message);
            project.AssetsFileFound = false;
            return result;
        }

        var packageFolders = (assets?["packageFolders"] as JsonObject)?.Select(p => p.Key).ToList() ?? new List<string>();
        var targets = assets?["targets"] as JsonObject;
        JsonObject? target = null;
        if (targets is not null)
        {
            target = (targets.FirstOrDefault(t => t.Key.Equals(tfm, StringComparison.OrdinalIgnoreCase)).Value
                ?? targets.FirstOrDefault(t => !t.Key.Contains('/')).Value) as JsonObject;
        }
        if (target is null)
        {
            project.Warnings.Add("project.assets.json 中没有匹配的目标框架。");
            return result;
        }

        var frameworks = assets?["project"]?["frameworks"] as JsonObject;
        var frameworkNode = frameworks?.FirstOrDefault(f => f.Key.Equals(tfm, StringComparison.OrdinalIgnoreCase)).Value ?? frameworks?.FirstOrDefault().Value;
        if (frameworkNode?["frameworkReferences"] is JsonObject frameworkRefs)
        {
            foreach (var fr in frameworkRefs) frameworkNames.Add(fr.Key);
        }

        var libraries = assets?["libraries"] as JsonObject;
        foreach (var (key, value) in target)
        {
            if (value is not JsonObject lib || lib["type"]?.GetValue<string>() != "package") continue;
            if (lib["frameworkReferences"] is JsonArray packageFrameworks)
            {
                foreach (var f in packageFrameworks)
                {
                    if (f?.GetValue<string>() is { Length: > 0 } name) frameworkNames.Add(name);
                }
            }
            if (lib["compile"] is not JsonObject compile) continue;
            var libPath = libraries?[key]?["path"]?.GetValue<string>() ?? key.ToLowerInvariant();
            foreach (var asset in compile.Select(c => c.Key))
            {
                if (asset.EndsWith("/_._", StringComparison.Ordinal) || !asset.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                var found = packageFolders
                    .Select(folder => Path.Combine(folder, libPath, asset.Replace('/', Path.DirectorySeparatorChar)))
                    .FirstOrDefault(File.Exists);
                if (found is null)
                {
                    project.MissingPackageAssets++;
                    continue;
                }
                result.Add(found);
            }
        }
        if (project.MissingPackageAssets > 0)
        {
            project.Warnings.Add($"有 {project.MissingPackageAssets} 个包程序集在本机缓存中缺失，相关类型会降级为未解析。");
        }
        return result;
    }

    private static string? FrameworkReferenceDirectory(string framework, string tfm)
    {
        var root = DotnetRoot();
        if (root is null) return null;
        var match = Regex.Match(tfm, @"^net(\d+)\.(\d+)", RegexOptions.IgnoreCase);
        var prefix = match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}." : "";
        var packDir = Path.Combine(root, "packs", framework + ".Ref");
        if (Directory.Exists(packDir))
        {
            var best = Directory.EnumerateDirectories(packDir)
                .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null))
                .Where(x => x.Version is not null && Path.GetFileName(x.Dir).StartsWith(prefix, StringComparison.Ordinal) && Directory.Exists(Path.Combine(x.Dir, "ref", tfm)))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (best.Dir is not null) return Path.Combine(best.Dir, "ref", tfm);
        }
        // 没有 targeting pack 时退回同主版本的共享运行时（实现程序集，足够做符号分析）。
        var sharedDir = Path.Combine(root, "shared", framework);
        if (Directory.Exists(sharedDir))
        {
            var best = Directory.EnumerateDirectories(sharedDir)
                .Select(d => (Dir: d, Version: Version.TryParse(Path.GetFileName(d).Split('-')[0], out var v) ? v : null))
                .Where(x => x.Version is not null && Path.GetFileName(x.Dir).StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(x => x.Version)
                .FirstOrDefault();
            if (best.Dir is not null) return best.Dir;
        }
        return null;
    }

    /// <summary>
    /// 被分析项目的参考程序集来自本机 .NET SDK 安装目录。工具自包含发布时自身运行时在程序目录内，
    /// 不能从自身运行时位置反推，因此依次查找 DOTNET_ROOT、PATH 中的 dotnet.exe、默认安装目录。
    /// </summary>
    private static string? DotnetRoot()
    {
        static bool Looks(string? dir) => !string.IsNullOrEmpty(dir) && (Directory.Exists(Path.Combine(dir, "packs")) || Directory.Exists(Path.Combine(dir, "shared")));

        var env = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (Looks(env)) return env;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = dir.Trim('"');
                if (File.Exists(Path.Combine(candidate, "dotnet.exe")) && Looks(candidate)) return candidate;
            }
            catch (ArgumentException)
            {
            }
        }
        var programFiles = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
        if (Looks(programFiles)) return programFiles;
        try
        {
            var fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
            if (Looks(fromRuntime)) return fromRuntime;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
        }
        return null;
    }

    /// <summary>
    /// 同名程序集只保留版本较高的一份，接近 SDK 的冲突解析结果。
    /// </summary>
    private static IEnumerable<string> Deduplicate(List<string> paths)
    {
        var chosen = new Dictionary<string, (string Path, Version Version)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!chosen.TryGetValue(name, out var current))
            {
                chosen[name] = (path, new Version(0, 0));
                continue;
            }
            var currentVersion = current.Version == new Version(0, 0) ? AssemblyVersion(current.Path) : current.Version;
            var candidateVersion = AssemblyVersion(path);
            chosen[name] = candidateVersion > currentVersion ? (path, candidateVersion) : (current.Path, currentVersion);
        }
        return chosen.Values.Select(v => v.Path);
    }

    private static Version AssemblyVersion(string path)
    {
        try
        {
            return System.Reflection.AssemblyName.GetAssemblyName(path).Version ?? new Version(0, 0);
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or IOException)
        {
            return new Version(0, 0);
        }
    }

    private static PortableExecutableReference? CreateReference(string path)
    {
        var info = new FileInfo(path);
        if (ReferenceCache.TryGetValue(path, out var cached) && cached.LastWriteUtc == info.LastWriteTimeUtc) return cached.Reference;
        try
        {
            using (var stream = File.OpenRead(path))
            using (var pe = new PEReader(stream))
            {
                if (!pe.HasMetadata) return null;
            }
            var reference = MetadataReference.CreateFromFile(path);
            ReferenceCache[path] = new CachedReference(info.LastWriteTimeUtc, reference);
            return reference;
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? FindUpwards(string startDir, string stopDir, string fileName)
    {
        var stop = Path.GetFullPath(stopDir).TrimEnd(Path.DirectorySeparatorChar);
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate)) return candidate;
            if (dir.FullName.TrimEnd(Path.DirectorySeparatorChar).Equals(stop, StringComparison.OrdinalIgnoreCase)) break;
            dir = dir.Parent;
        }
        return null;
    }
}

public sealed record MsBuildItem(string Type, XElement Element);

/// <summary>
/// 极简 MSBuild 属性求值：支持 $(Prop) 展开与 'a'=='b' / != 及 and/or 组合的条件；其他条件跳过并记录。
/// </summary>
internal sealed class MsBuildState
{
    private readonly Dictionary<string, string> _props = new(StringComparer.OrdinalIgnoreCase);

    public MsBuildState(string projectDir, string projectName, string configuration)
    {
        _props["MSBuildProjectName"] = projectName;
        _props["MSBuildProjectDirectory"] = projectDir;
        _props["Configuration"] = configuration;
        _props["Platform"] = "AnyCPU";
        _props["DefineConstants"] = "";
    }

    public List<MsBuildItem> Items { get; } = new();

    public List<string> Warnings { get; } = new();

    public string Get(string name) => _props.TryGetValue(name, out var v) ? v : "";

    public void Set(string name, string value) => _props[name] = value;

    /// <summary>读取项目文件，返回 Sdk 属性。</summary>
    public string Read(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new UserException("项目文件格式无效：" + path);
        _props["MSBuildThisFileDirectory"] = Path.GetDirectoryName(path) + Path.DirectorySeparatorChar;
        var sdk = root.Attribute("Sdk")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value ?? "";
        foreach (var element in root.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "PropertyGroup":
                    if (!Evaluate(element, path)) continue;
                    foreach (var property in element.Elements())
                    {
                        if (!Evaluate(property, path)) continue;
                        _props[property.Name.LocalName] = Expand(property.Value.Trim());
                    }
                    break;
                case "ItemGroup":
                    if (!Evaluate(element, path)) continue;
                    foreach (var item in element.Elements())
                    {
                        if (!Evaluate(item, path)) continue;
                        Items.Add(new MsBuildItem(item.Name.LocalName, item));
                    }
                    break;
                case "Import":
                    Warnings.Add($"{Path.GetFileName(path)} 含 Import（{element.Attribute("Project")?.Value}），未展开。");
                    break;
                case "Choose":
                    Warnings.Add($"{Path.GetFileName(path)} 含 Choose 条件块，未评估。");
                    break;
            }
        }
        return sdk;
    }

    public string Expand(string value) =>
        Regex.Replace(value, @"\$\(([A-Za-z_][A-Za-z0-9_.]*)\)", m => Get(m.Groups[1].Value));

    private bool Evaluate(XElement element, string path)
    {
        var condition = element.Attribute("Condition")?.Value;
        if (string.IsNullOrWhiteSpace(condition)) return true;
        if (TryEvaluate(condition, out var result)) return result;
        // 只对可能影响分析的分组记录警告（Content/None 等资源项不影响编译）。
        var affectsCompile = element.Name.LocalName is "PropertyGroup" or "Compile" or "ProjectReference" or "Using" or "FrameworkReference" or "DefineConstants" or "Nullable" or "LangVersion"
            || element.Elements().Any(e => e.Name.LocalName is "Compile" or "ProjectReference" or "Using" or "FrameworkReference");
        if (affectsCompile)
        {
            Warnings.Add($"{Path.GetFileName(path)} 中的条件未评估（已跳过）：{condition.Trim()}");
        }
        return false;
    }

    private bool TryEvaluate(string condition, out bool result)
    {
        result = false;
        var hasAnd = Regex.IsMatch(condition, @"\s+and\s+", RegexOptions.IgnoreCase);
        var hasOr = Regex.IsMatch(condition, @"\s+or\s+", RegexOptions.IgnoreCase);
        if (hasAnd && hasOr) return false;
        var parts = Regex.Split(condition, hasOr ? @"\s+or\s+" : @"\s+and\s+", RegexOptions.IgnoreCase);
        var values = new List<bool>();
        foreach (var part in parts)
        {
            var m = Regex.Match(part.Trim().Trim('(', ')').Trim(), @"^'([^']*)'\s*(==|!=)\s*'([^']*)'$");
            if (!m.Success) return false;
            var left = Expand(m.Groups[1].Value);
            var right = Expand(m.Groups[3].Value);
            var equal = string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            values.Add(m.Groups[2].Value == "==" ? equal : !equal);
        }
        result = hasOr ? values.Any(v => v) : values.All(v => v);
        return true;
    }
}
