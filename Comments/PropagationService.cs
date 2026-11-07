using System.Xml.Linq;
using ApifoxHelper.Contracts;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.CodeAnalysis;

namespace ApifoxHelper.Comments;

public sealed class PropagationEntry
{
    public string SlotId { get; set; } = "";
    public string Display { get; set; } = "";
    public string Tag { get; set; } = "";
    public string? ParameterName { get; set; }
    public string Text { get; set; } = "";

    /// <summary>written / notWritten / missingSymbol</summary>
    public string SourceState { get; set; } = "";

    /// <summary>present / different / missing / noXml / disabled</summary>
    public string XmlState { get; set; } = "";

    /// <summary>present / different / missing / notRendered / unavailable</summary>
    public string SwaggerState { get; set; } = "";
    public string? SwaggerLocation { get; set; }
    public List<string> Notes { get; set; } = new();
}

public sealed class XmlFileInfo
{
    public string Project { get; set; } = "";
    public string? Path { get; set; }
    public bool Exists { get; set; }
    public DateTime? LastWrite { get; set; }
    public bool OlderThanSource { get; set; }
    public bool Enabled { get; set; }
}

public sealed class PropagationResult
{
    public DateTimeOffset CheckedAt { get; set; }
    public List<XmlFileInfo> XmlFiles { get; set; } = new();
    public string? SwaggerMessage { get; set; }
    public List<PropagationEntry> Entries { get; set; } = new();
}

/// <summary>
/// 传播核对：已确认注释是否写进源码、是否出现在实际生成的 XML 文档、是否被 Swagger 读取到正确位置。
/// 旧生成物不算注释已生效；工具默认不构建，构建需用户明确执行。
/// </summary>
public sealed class PropagationService(TaskStore tasks, ProjectLoader loader, OpenApiSource openApi)
{
    public async Task<object> CheckAsync(string taskId, JobContext job)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        var scan = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols) ?? throw new UserException("请先完成源码扫描。");
        var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();
        var accepted = drafts.Candidates.Where(c => c.ReviewState == "accepted" && c.Action == "set").ToList();
        if (accepted.Count == 0) throw new UserException("没有已确认的注释可核对。");
        job.Report(0.1, "重新加载源码");
        var workspace = await Task.Run(() => loader.Load(profile, job.Token), job.Token);
        var result = new PropagationResult { CheckedAt = DateTimeOffset.Now };

        var xmlDocs = new Dictionary<string, (XDocument? Doc, XmlFileInfo Info)>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in workspace.Projects)
        {
            var path = profile.Analysis.XmlDocPaths.TryGetValue(project.Name, out var custom)
                ? (Path.IsPathFullyQualified(custom) ? custom : Path.Combine(workspace.WorkspaceRoot, custom))
                : project.DocumentationFilePath;
            var info = new XmlFileInfo { Project = project.Name, Path = path is null ? null : SymbolUtil.Relative(workspace.WorkspaceRoot, path), Enabled = project.GenerateDocumentationFile || custom is not null };
            XDocument? doc = null;
            if (path is not null && File.Exists(path))
            {
                info.Exists = true;
                info.LastWrite = File.GetLastWriteTime(path);
                var newestSource = project.Files.Where(f => !f.IsSynthetic).Select(f => File.GetLastWriteTime(f.FullPath)).DefaultIfEmpty(DateTime.MinValue).Max();
                info.OlderThanSource = newestSource > info.LastWrite;
                try
                {
                    doc = XDocument.Load(path);
                }
                catch (System.Xml.XmlException ex)
                {
                    info.Exists = false;
                    result.XmlFiles.Add(info);
                    result.SwaggerMessage = "XML 文档无法解析：" + ex.Message;
                    continue;
                }
            }
            xmlDocs[project.Name] = (doc, info);
            result.XmlFiles.Add(info);
        }

        JsonObject? swagger = null;
        if (profile.Swagger.Type != "none")
        {
            try
            {
                job.Report(0.5, "读取当前 Swagger");
                (_, swagger, _) = await openApi.LoadAsync(profile.Swagger, profile.WorkspaceRoot, job.Token);
            }
            catch (UserException ex)
            {
                result.SwaggerMessage = "Swagger 不可用：" + ex.Message;
            }
        }
        else
        {
            result.SwaggerMessage = "配置中没有 Swagger 来源，无法核对生成文档。";
        }

        var slots = scan.Slots.ToDictionary(s => s.Id, StringComparer.Ordinal);
        foreach (var candidate in accepted)
        {
            if (!slots.TryGetValue(candidate.SlotId, out var slot)) continue;
            var entry = new PropagationEntry { SlotId = slot.Id, Display = slot.Display, Tag = slot.Tag, ParameterName = slot.ParameterName, Text = candidate.Text };
            var symbol = workspace.ResolveInRoot(slot.SymbolId);
            var syntax = symbol?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (symbol is null || syntax is null)
            {
                entry.SourceState = "missingSymbol";
            }
            else
            {
                var doc = DocComments.Read(syntax);
                var element = doc.Find(slot.Tag, slot.Tag == "param" ? slot.ParameterName : null);
                entry.SourceState = element is not null && Same(element.Text, candidate.Text) ? "written" : "notWritten";

                var project = workspace.TreeOwner.TryGetValue(syntax.SyntaxTree, out var owner) ? owner : null;
                if (project is null || !xmlDocs.TryGetValue(project.Name, out var xml))
                {
                    entry.XmlState = "noXml";
                }
                else if (!xml.Info.Enabled && xml.Doc is null)
                {
                    entry.XmlState = "disabled";
                    entry.Notes.Add($"{project.Name} 未启用 XML 文档生成（GenerateDocumentationFile）。");
                }
                else if (xml.Doc is null)
                {
                    entry.XmlState = "noXml";
                    entry.Notes.Add("找不到生成的 XML 文档文件，可能尚未构建。");
                }
                else
                {
                    var member = xml.Doc.Root?.Element("members")?.Elements("member").FirstOrDefault(m => (string?)m.Attribute("name") == slot.SymbolId);
                    var tag = member?.Elements(slot.Tag).FirstOrDefault(e => slot.Tag != "param" || (string?)e.Attribute("name") == slot.ParameterName);
                    entry.XmlState = tag is null ? "missing" : Same(tag.Value, candidate.Text) ? "present" : "different";
                    if (xml.Info.OlderThanSource) entry.Notes.Add("XML 文件早于源码最近修改时间，可能是旧生成物。");
                }
            }
            CheckSwagger(entry, slot, scan, swagger);
            result.Entries.Add(entry);
        }

        using (await KeyedLocks.AcquireAsync("task:" + taskId, job.Token))
        {
            tasks.Write(taskId, TaskFiles.Propagation, result);
            var current = tasks.Get(taskId);
            var stage = current.Stages.Generated;
            stage.CheckedAt = result.CheckedAt;
            stage.Entries = result.Entries.Count;
            stage.InSource = result.Entries.Count(e => e.SourceState == "written");
            stage.InXml = result.Entries.Count(e => e.XmlState == "present");
            stage.InSwagger = result.Entries.Count(e => e.SwaggerState == "present");
            var expected = result.Entries.Count(e => e.SwaggerState != "notRendered");
            stage.Status = swagger is null ? "unavailable"
                : stage.InSwagger == expected && stage.InSource == stage.Entries ? "verified"
                : stage.InSwagger > 0 ? "partial" : "pending";
            stage.Message = stage.Status switch
            {
                "verified" => "已确认注释在源码、XML 与 Swagger 中均已出现（不呈现的标签除外）。",
                "unavailable" => result.SwaggerMessage,
                _ => stage.InSource == stage.Entries ? "源码已完成，Swagger 待刷新或部分说明未呈现。" : "部分注释尚未写入源码。",
            };
            tasks.Save(current);
        }
        return new
        {
            entries = result.Entries.Count,
            inSource = result.Entries.Count(e => e.SourceState == "written"),
            inXml = result.Entries.Count(e => e.XmlState == "present"),
            inSwagger = result.Entries.Count(e => e.SwaggerState == "present"),
            result.SwaggerMessage,
        };
    }

    private static void CheckSwagger(PropagationEntry entry, DocSlot slot, ScanOutput scan, JsonObject? swagger)
    {
        if (swagger is null)
        {
            entry.SwaggerState = "unavailable";
            return;
        }
        string? actual = null;
        string? location = null;
        var notRendered = false;
        switch (slot.SymbolKind)
        {
            case "method":
                foreach (var key in slot.EndpointKeys)
                {
                    var parts = key.Split(' ', 2);
                    var operation = swagger["paths"]?[parts[1]]?[parts[0].ToLowerInvariant()] as JsonObject;
                    if (operation is null) continue;
                    var endpoint = scan.Endpoints.FirstOrDefault(e => e.Key == key);
                    switch (slot.Tag)
                    {
                        case "summary":
                            actual = operation["summary"]?.ToString();
                            location = key + " summary";
                            break;
                        case "returns":
                            // Swashbuckle 不读取 <returns>；看 200 响应说明是否包含。
                            actual = operation["responses"]?["200"]?["description"]?.ToString();
                            location = key + " responses.200.description";
                            notRendered = !Same(actual, entry.Text);
                            break;
                        case "param":
                            var binding = endpoint?.Parameters.FirstOrDefault(p => p.Name == slot.ParameterName);
                            if (binding?.Source == "body")
                            {
                                actual = operation["requestBody"]?["description"]?.ToString();
                                location = key + " requestBody.description";
                            }
                            else
                            {
                                var parameter = (operation["parameters"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(p => p["name"]?.ToString() == (binding?.ExternalName ?? slot.ParameterName));
                                actual = parameter?["description"]?.ToString();
                                location = $"{key} {binding?.Source ?? "query"}.{binding?.ExternalName ?? slot.ParameterName}";
                            }
                            break;
                    }
                    if (actual is not null) break;
                }
                break;
            case "type" or "enum" when slot.ComponentName is not null:
                actual = swagger["components"]?["schemas"]?[slot.ComponentName]?["description"]?.ToString();
                location = "components.schemas." + slot.ComponentName;
                break;
            case "property" or "recordParameter" when slot.ComponentName is not null && slot.JsonName is not null:
                actual = swagger["components"]?["schemas"]?[slot.ComponentName]?["properties"]?[slot.JsonName]?["description"]?.ToString();
                location = $"components.schemas.{slot.ComponentName}.properties.{slot.JsonName}";
                break;
            case "enumMember":
                notRendered = true;
                location = "枚举成员说明";
                break;
        }
        entry.SwaggerLocation = location;
        if (notRendered)
        {
            entry.SwaggerState = "notRendered";
            entry.Notes.Add("当前生成器不呈现该标签（以本次读取的 Swagger 为准），需通过文档补充层同步到 Apifox。");
            return;
        }
        entry.SwaggerState = actual is null ? "missing" : Same(actual, entry.Text) ? "present" : "different";
    }

    private static bool Same(string? actual, string expected)
    {
        var a = TextUtil.NormalizeSpace(actual);
        var e = TextUtil.NormalizeSpace(expected);
        return a.Length > 0 && (a == e || a.Contains(e, StringComparison.Ordinal));
    }
}

/// <summary>用户明确确认后才执行的定向构建（不 restore），用于刷新 XML 文档；结果如实记录。</summary>
public sealed class BuildService(TaskStore tasks)
{
    public static List<string> Arguments(string apiProject) => ["build", apiProject, "--no-restore", "-nologo", "-v:minimal"];

    public async Task<object> RunAsync(string taskId, JobContext job)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        var started = DateTimeOffset.Now;
        job.Report(0.1, "dotnet " + string.Join(" ", Arguments(profile.ApiProject)));
        var result = await ProcessRunner.RunAsync("dotnet", Arguments(profile.ApiProject), profile.WorkspaceRoot, TimeSpan.FromMinutes(15), job.Token);
        var output = (result.StdOut + "\n" + result.StdErr).Split('\n');
        var record = new
        {
            command = "dotnet " + string.Join(" ", Arguments(profile.ApiProject)),
            startedAt = started,
            exitCode = result.ExitCode,
            timedOut = result.TimedOut,
            seconds = Math.Round(result.Elapsed.TotalSeconds, 1),
            outputTail = Redactor.Redact(string.Join("\n", output.TakeLast(60))),
        };
        tasks.Write(taskId, TaskFiles.BuildLog, record);
        return record;
    }
}
