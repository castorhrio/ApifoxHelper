using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApifoxHelper.Comments;

public sealed class CommentPlan
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string Hash { get; set; } = "";

    /// <summary>ready / applying / applied / partial / failed / expired</summary>
    public string Status { get; set; } = "ready";
    public int TaskRevision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public List<PlannedFile> Files { get; set; } = new();
    public List<PlanSkip> Skipped { get; set; } = new();
    public Dictionary<string, string> EvidenceFileHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FileApplyResult> ApplyResults { get; set; } = new();
    public string? Message { get; set; }
}

public sealed class PlannedFile
{
    public string RelativePath { get; set; } = "";
    public string BeforeHash { get; set; } = "";
    public string AfterHash { get; set; } = "";
    public bool HasBom { get; set; }
    public string Eol { get; set; } = "";
    public bool WriteAllowed { get; set; }
    public string? WriteBlockedReason { get; set; }
    public string LanguageVersion { get; set; } = "";
    public List<string> PreprocessorSymbols { get; set; } = new();
    public List<PlannedEdit> Edits { get; set; } = new();
    public List<DeclarationCheck> Checks { get; set; } = new();
    public string Diff { get; set; } = "";
}

/// <summary>应用前后用于复核的声明定位（token 序号在只改注释时保持不变）。</summary>
public sealed class DeclarationCheck
{
    public int TokenIndex { get; set; }
    public List<string> ParameterOrder { get; set; } = new();
    public List<DocEditOperation> Operations { get; set; } = new();
}

public sealed class PlanSkip
{
    public string CandidateId { get; set; } = "";
    public string Display { get; set; } = "";
    public string Reason { get; set; } = "";
}

public sealed class FileApplyResult
{
    public string RelativePath { get; set; } = "";

    /// <summary>applied / skipped / failed / conflict</summary>
    public string State { get; set; } = "";
    public string? Message { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class ApplyCommentPlanRequest
{
    public string PlanHash { get; set; } = "";
}

/// <summary>
/// 注释计划：只含已接受候选对应的文档注释编辑；可导出补丁或在二次核对后回写。
/// 回写前重新核对文件与证据指纹，逐文件原子替换，失败时如实记录部分完成，不自动回滚。
/// </summary>
public sealed class CommentPlanService(TaskStore tasks, ProjectLoader loader)
{
    public async Task<CommentPlan> CreateAsync(string taskId, CancellationToken ct)
    {
        var task = tasks.Get(taskId);
        var profile = task.ProfileSnapshot;
        var scan = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols) ?? throw new UserException("请先完成源码扫描。");
        var manifest = tasks.Read<SourceManifest>(taskId, TaskFiles.Manifest) ?? throw new UserException("缺少扫描清单，请重新扫描。");
        var evidence = tasks.Read<EvidenceStore>(taskId, TaskFiles.Evidence) ?? new EvidenceStore();
        var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();
        var accepted = drafts.Candidates.Where(c => c.ReviewState == "accepted" && c.AppliedAt is null).ToList();
        if (accepted.Count == 0) throw new UserException("没有已接受且未回写的注释候选。");

        var workspace = await Task.Run(() => loader.Load(profile, ct), ct);
        var slots = scan.Slots.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var evidenceById = evidence.Items.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var plan = new CommentPlan { Id = Ids.New("cp"), TaskId = taskId, CreatedAt = DateTimeOffset.Now, TaskRevision = task.Revision };

        // 依据文件：候选所在文件 + 证据所在文件，扫描后变化则要求重新扫描。
        var byDeclaration = new Dictionary<string, (SyntaxNode Node, SourceFileEntry File, List<(CommentCandidate Candidate, DocSlot Slot)> Items, List<string> Parameters)>(StringComparer.Ordinal);
        foreach (var candidate in accepted)
        {
            if (!slots.TryGetValue(candidate.SlotId, out var slot))
            {
                plan.Skipped.Add(new PlanSkip { CandidateId = candidate.Id, Display = candidate.SymbolId, Reason = "注释位置已不存在，请重新扫描" });
                continue;
            }
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { slot.File };
            foreach (var id in slot.EvidenceIds.Concat(candidate.EvidenceIds))
            {
                if (evidenceById.TryGetValue(id, out var item)) files.Add(item.File);
            }
            var changed = files.Where(f => !string.IsNullOrEmpty(f) && CurrentHash(workspace, f) != manifest.FileHashes.GetValueOrDefault(f)).ToList();
            if (changed.Count > 0)
            {
                plan.Skipped.Add(new PlanSkip { CandidateId = candidate.Id, Display = slot.Display, Reason = "扫描后源码或证据文件已变化，请重新扫描并审阅：" + string.Join(", ", changed.Take(3)) });
                continue;
            }
            foreach (var f in files.Where(f => f.Length > 0)) plan.EvidenceFileHashes[f] = manifest.FileHashes.GetValueOrDefault(f) ?? "";

            var (node, file, parameters, error) = LocateDeclaration(workspace, slot);
            if (error is not null || node is null || file is null)
            {
                plan.Skipped.Add(new PlanSkip { CandidateId = candidate.Id, Display = slot.Display, Reason = error ?? "无法定位声明" });
                continue;
            }
            if (!file.IsUtf8)
            {
                plan.Skipped.Add(new PlanSkip { CandidateId = candidate.Id, Display = slot.Display, Reason = "文件不是 UTF-8 编码，不自动修改" });
                continue;
            }
            var key = file.FullPath + "|" + node.SpanStart;
            if (!byDeclaration.TryGetValue(key, out var group))
            {
                group = (node, file, new List<(CommentCandidate, DocSlot)>(), parameters);
                byDeclaration[key] = group;
            }
            group.Items.Add((candidate, slot));
        }

        foreach (var fileGroup in byDeclaration.Values.GroupBy(g => g.File.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            var file = fileGroup.First().File;
            var text = file.Tree.GetText(ct).ToString();
            if (TextFiles.EncodeUtf8(text, file.HasBom) is var roundTrip && Canonical.Sha256Hex(roundTrip) != file.Hash)
            {
                foreach (var item in fileGroup.SelectMany(g => g.Items)) plan.Skipped.Add(new PlanSkip { CandidateId = item.Candidate.Id, Display = item.Slot.Display, Reason = "文件内容无法无损往返编码，不自动修改" });
                continue;
            }
            var lines = LineModel.Parse(text);
            var planned = new PlannedFile
            {
                RelativePath = file.RelativePath,
                BeforeHash = file.Hash,
                HasBom = file.HasBom,
                Eol = lines.DominantEol == "\r\n" ? "CRLF" : "LF",
                WriteAllowed = ProfileStore.IsWriteAllowed(profile, file.RelativePath),
            };
            if (!planned.WriteAllowed) planned.WriteBlockedReason = "不在允许回写范围内，只能导出补丁";
            foreach (var declaration in fileGroup)
            {
                var operations = declaration.Items.Select(i => new DocEditOperation(i.Candidate.Id, i.Slot.Tag, i.Slot.ParameterName, i.Candidate.Text, i.Candidate.OverwriteExisting, i.Candidate.Action)).ToList();
                var (edits, skipped) = DocEditBuilder.Build(lines, declaration.Node, declaration.Parameters, operations, declaration.Items[0].Slot.SymbolId, declaration.Items[0].Slot.Display);
                foreach (var (candidateId, reason) in skipped)
                {
                    plan.Skipped.Add(new PlanSkip { CandidateId = candidateId, Display = declaration.Items.First(i => i.Candidate.Id == candidateId || candidateId.Contains(i.Candidate.Id)).Slot.Display, Reason = reason });
                }
                if (edits.Count == 0) continue;
                planned.Edits.AddRange(edits);
                var included = edits.SelectMany(e => e.CandidateId.Split(',')).ToHashSet(StringComparer.Ordinal);
                planned.Checks.Add(new DeclarationCheck
                {
                    TokenIndex = DocEditBuilder.TokenIndex(declaration.Node),
                    ParameterOrder = declaration.Parameters,
                    Operations = operations.Where(o => included.Contains(o.CandidateId)).ToList(),
                });
            }
            if (planned.Edits.Count == 0) continue;

            var after = DocEditBuilder.Apply(lines, planned.Edits);
            var parseOptions = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)file.Tree.Options;
            var failure = DocEditBuilder.VerifyOnlyDocCommentsChanged(text, after, parseOptions)
                ?? planned.Checks.Select(c => DocEditBuilder.VerifyDeclaration(after, parseOptions, c.TokenIndex, c.ParameterOrder, c.Operations)).FirstOrDefault(e => e is not null);
            if (failure is not null)
            {
                foreach (var edit in planned.Edits) plan.Skipped.Add(new PlanSkip { CandidateId = edit.CandidateId, Display = edit.Display, Reason = failure });
                continue;
            }
            planned.LanguageVersion = parseOptions.LanguageVersion.ToString();
            planned.PreprocessorSymbols = parseOptions.PreprocessorSymbolNames.ToList();
            planned.AfterHash = Canonical.Sha256Hex(TextFiles.EncodeUtf8(after, file.HasBom));
            planned.Diff = UnifiedDiff(file.RelativePath, lines, planned.Edits, file.HasBom);
            plan.Files.Add(planned);
        }

        plan.Hash = Canonical.HashOf(new
        {
            files = plan.Files.Select(f => new { f.RelativePath, f.BeforeHash, f.AfterHash, f.WriteAllowed }),
            evidence = plan.EvidenceFileHashes.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + ":" + p.Value),
        });
        plan.Status = plan.Files.Count == 0 ? "expired" : "ready";
        plan.Message = plan.Files.Count == 0 ? "没有可生成的注释修改，请查看跳过原因。" : null;

        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            var existing = tasks.Read<CommentPlan>(taskId, TaskFiles.CommentPlan);
            if (existing is { Status: "applying" }) throw new UserException("注释计划正在回写，不能重新生成。", 409);
            tasks.Write(taskId, TaskFiles.CommentPlan, plan);
            FileStore.WriteText(tasks.FilePath(taskId, TaskFiles.CommentPatch), CombinedPatch(plan));
            var current = tasks.Get(taskId);
            if (plan.Files.Count > 0) current.Stages.Source.Status = "planReady";
            tasks.Save(current);
        }
        return plan;
    }

    public string CombinedPatch(CommentPlan plan) => string.Concat(plan.Files.Select(f => f.Diff));

    public async Task<CommentPlan> ExportPatchAsync(string taskId, CancellationToken ct)
    {
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            var plan = tasks.Read<CommentPlan>(taskId, TaskFiles.CommentPlan) ?? throw new UserException("还没有注释计划。");
            var task = tasks.Get(taskId);
            task.Stages.Source.PatchExported = true;
            tasks.Save(task);
            return plan;
        }
    }

    public async Task<CommentPlan> ApplyAsync(string taskId, string planHash, CancellationToken ct)
    {
        CommentPlan plan;
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            plan = tasks.Read<CommentPlan>(taskId, TaskFiles.CommentPlan) ?? throw new UserException("还没有注释计划。");
            if (plan.Hash != planHash) throw new UserException("确认的计划与当前计划不一致，请重新预览。", 409);
            if (plan.Status is "applied" or "partial" or "failed") return plan;
            if (plan.Status == "applying") throw new UserException("注释计划正在回写。", 409);
            if (plan.Status != "ready") throw new UserException("注释计划已过期，请重新生成。", 409);
            var task = tasks.Get(taskId);
            if (task.Revision != plan.TaskRevision)
            {
                plan.Status = "expired";
                plan.Message = "生成计划后草稿或扫描结果有变化，请重新生成计划。";
                tasks.Write(taskId, TaskFiles.CommentPlan, plan);
                throw new UserException(plan.Message, 409);
            }

            // 全部文件与证据先核对一遍，任何变化都不写入。
            var workspaceRoot = task.ProfileSnapshot.WorkspaceRoot;
            var changed = new List<string>();
            foreach (var file in plan.Files.Where(f => f.WriteAllowed))
            {
                if (HashOnDisk(workspaceRoot, file.RelativePath) != file.BeforeHash) changed.Add(file.RelativePath);
            }
            foreach (var (path, hash) in plan.EvidenceFileHashes)
            {
                if (HashOnDisk(workspaceRoot, path) != hash) changed.Add(path);
            }
            if (changed.Count > 0)
            {
                plan.Status = "expired";
                plan.Message = "源码在预览后发生变化，请重新扫描并生成计划：" + string.Join(", ", changed.Distinct().Take(5));
                tasks.Write(taskId, TaskFiles.CommentPlan, plan);
                throw new UserException(plan.Message, 409);
            }
            plan.Status = "applying";
            plan.ConfirmedAt = DateTimeOffset.Now;
            tasks.Write(taskId, TaskFiles.CommentPlan, plan);
        }

        var root = tasks.Get(taskId).ProfileSnapshot.WorkspaceRoot;
        var journal = tasks.FilePath(taskId, TaskFiles.Journal);
        var failed = false;
        foreach (var file in plan.Files)
        {
            var result = new FileApplyResult { RelativePath = file.RelativePath, At = DateTimeOffset.Now };
            if (!file.WriteAllowed)
            {
                result.State = "skipped";
                result.Message = file.WriteBlockedReason;
                plan.ApplyResults.Add(result);
                continue;
            }
            if (failed)
            {
                result.State = "skipped";
                result.Message = "前一个文件失败，后续文件未执行";
                plan.ApplyResults.Add(result);
                continue;
            }
            try
            {
                FileStore.AppendJsonLine(journal, new { at = DateTimeOffset.Now, kind = "commentApplyIntent", planId = plan.Id, file = file.RelativePath, file.BeforeHash, file.AfterHash });
                var fullPath = ProfileStore.ResolveInside(root, file.RelativePath) ?? throw new UserException("文件不在工作区内。");
                var current = TextFiles.Read(fullPath);
                if (current.Hash != file.BeforeHash)
                {
                    result.State = "conflict";
                    result.Message = "文件在确认后被修改，未写入";
                    failed = true;
                }
                else
                {
                    var backup = Path.Combine(tasks.FilePath(taskId, TaskFiles.SourceBefore), file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    FileStore.WriteBytesAtomic(backup, current.Bytes);
                    var lines = LineModel.Parse(current.Text);
                    var after = DocEditBuilder.Apply(lines, file.Edits);
                    var bytes = TextFiles.EncodeUtf8(after, file.HasBom);
                    if (Canonical.Sha256Hex(bytes) != file.AfterHash) throw new UserException("重新生成的内容与预览不一致，已停止。");
                    var languageVersion = Enum.TryParse<Microsoft.CodeAnalysis.CSharp.LanguageVersion>(file.LanguageVersion, out var parsedVersion) ? parsedVersion : Microsoft.CodeAnalysis.CSharp.LanguageVersion.Latest;
                    var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(languageVersion, DocumentationMode.Diagnose, SourceCodeKind.Regular, file.PreprocessorSymbols);
                    var verification = DocEditBuilder.VerifyOnlyDocCommentsChanged(current.Text, after, options)
                        ?? file.Checks.Select(c => DocEditBuilder.VerifyDeclaration(after, options, c.TokenIndex, c.ParameterOrder, c.Operations)).FirstOrDefault(e => e is not null);
                    if (verification is not null) throw new UserException(verification);
                    ReplaceFile(fullPath, bytes, file.BeforeHash);
                    var written = TextFiles.HashFile(fullPath);
                    result.State = written == file.AfterHash ? "applied" : "failed";
                    result.Message = written == file.AfterHash ? null : "写入后的内容与预期不一致";
                    failed = result.State != "applied";
                }
            }
            catch (Exception ex) when (ex is UserException or IOException or UnauthorizedAccessException)
            {
                result.State = "failed";
                result.Message = ex.Message;
                failed = true;
            }
            result.At = DateTimeOffset.Now;
            plan.ApplyResults.Add(result);
            FileStore.AppendJsonLine(journal, new { at = DateTimeOffset.Now, kind = "commentApplyResult", planId = plan.Id, file = file.RelativePath, result.State, result.Message });
        }

        using (await KeyedLocks.AcquireAsync("task:" + taskId, CancellationToken.None))
        {
            var appliedFiles = plan.ApplyResults.Where(r => r.State == "applied").Select(r => r.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var writable = plan.Files.Count(f => f.WriteAllowed);
            plan.Status = appliedFiles.Count == 0 ? "failed" : appliedFiles.Count < writable ? "partial" : "applied";
            tasks.Write(taskId, TaskFiles.CommentPlan, plan);

            var appliedCandidates = plan.Files.Where(f => appliedFiles.Contains(f.RelativePath)).SelectMany(f => f.Edits).SelectMany(e => e.CandidateId.Split(',')).ToHashSet(StringComparer.Ordinal);
            var drafts = tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();
            foreach (var candidate in drafts.Candidates.Where(c => appliedCandidates.Contains(c.Id))) candidate.AppliedAt = DateTimeOffset.Now;
            tasks.Write(taskId, TaskFiles.Drafts, drafts);

            var task = tasks.Get(taskId);
            task.Stages.Source.Status = plan.Status == "applied" ? "applied" : plan.Status == "partial" ? "partiallyApplied" : "failed";
            task.Stages.Source.AppliedEdits += plan.Files.Where(f => appliedFiles.Contains(f.RelativePath)).Sum(f => f.Edits.Count);
            if (appliedFiles.Count > 0)
            {
                // 源码变化后：生成文档需要重新核对，之前的远端同步计划失效。
                task.Stages.Generated.Status = "pending";
                task.Stages.Generated.Message = "源码注释已回写，XML/Swagger 尚未重新生成或核对。";
                if (task.Stages.Remote.Status is "previewable" or "awaitingConfirm") task.Stages.Remote.Status = "expired";
                task.Revision++;
            }
            tasks.Save(task);
        }
        return plan;
    }

    /// <summary>写临时文件后 File.Replace 原子替换（保留原文件属性/ACL）；替换前最后一次核对指纹缩小竞态窗口。</summary>
    private static void ReplaceFile(string fullPath, byte[] bytes, string expectedHash)
    {
        var dir = Path.GetDirectoryName(fullPath)!;
        var tmp = Path.Combine(dir, "." + Path.GetFileName(fullPath) + ".apifoxhelper-" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes);
                fs.Flush(flushToDisk: true);
            }
            if (TextFiles.HashFile(fullPath) != expectedHash) throw new UserException("文件在写入前一刻被修改，未写入。");
            File.Replace(tmp, fullPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    private static string? HashOnDisk(string workspaceRoot, string relativePath)
    {
        var full = ProfileStore.ResolveInside(workspaceRoot, relativePath);
        return full is not null && File.Exists(full) ? TextFiles.HashFile(full) : null;
    }

    private static string? CurrentHash(LoadedWorkspace workspace, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(workspace.WorkspaceRoot, relativePath));
        return workspace.FilesByPath.TryGetValue(full, out var entry) ? entry.Hash : (File.Exists(full) ? TextFiles.HashFile(full) : null);
    }

    /// <summary>
    /// 在当前源码中重新定位要编辑的声明（不复用扫描时的语法树），并给出参数顺序用于排序 param 标签。
    /// </summary>
    private static (SyntaxNode? Node, SourceFileEntry? File, List<string> Parameters, string? Error) LocateDeclaration(LoadedWorkspace workspace, DocSlot slot)
    {
        var symbol = workspace.ResolveInRoot(slot.SymbolId);
        if (symbol is null) return (null, null, new List<string>(), "当前源码中找不到该符号，请重新扫描");
        if (symbol.DeclaringSyntaxReferences.Length != 1) return (null, null, new List<string>(), "符号有多个声明（partial），需人工指定位置");
        var syntax = symbol.DeclaringSyntaxReferences[0].GetSyntax();
        var file = workspace.FileOf(syntax.SyntaxTree);
        if (file is null || file.IsSynthetic) return (null, null, new List<string>(), "生成代码或未知文件");
        switch (symbol)
        {
            case IMethodSymbol method when syntax is MethodDeclarationSyntax:
                if (method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null) return (null, null, new List<string>(), "partial 方法，需人工指定位置");
                return (syntax, file, method.Parameters.Select(p => p.Name).ToList(), null);
            case INamedTypeSymbol type when syntax is BaseTypeDeclarationSyntax:
                var parameters = syntax is RecordDeclarationSyntax { ParameterList: not null } record
                    ? record.ParameterList.Parameters.Select(p => p.Identifier.ValueText).ToList()
                    : new List<string>();
                return (syntax, file, parameters, null);
            case IPropertySymbol when syntax is PropertyDeclarationSyntax:
                return (syntax, file, new List<string>(), null);
            case IFieldSymbol when syntax is EnumMemberDeclarationSyntax:
                return (syntax, file, new List<string>(), null);
            default:
                return (null, null, new List<string>(), "不支持在该声明上自动写注释");
        }
    }

    /// <summary>生成 git apply 可用的统一差异；保留每行原换行符，BOM 文件首行带 BOM。</summary>
    public static string UnifiedDiff(string relativePath, LineModel lines, List<PlannedEdit> edits, bool hasBom)
    {
        var sorted = edits.OrderBy(e => e.StartLine).ThenBy(e => e.Rank).ToList();
        var groups = new List<List<PlannedEdit>>();
        foreach (var edit in sorted)
        {
            if (groups.Count > 0)
            {
                var last = groups[^1][^1];
                if (edit.StartLine - (last.StartLine + last.DeleteCount) <= 6)
                {
                    groups[^1].Add(edit);
                    continue;
                }
            }
            groups.Add([edit]);
        }

        var sb = new StringBuilder();
        sb.Append("diff --git a/").Append(relativePath).Append(" b/").Append(relativePath).Append('\n');
        sb.Append("--- a/").Append(relativePath).Append('\n');
        sb.Append("+++ b/").Append(relativePath).Append('\n');
        var lineCount = lines.Lines.Count;
        if (lines.Lines[^1].Length == 0 && lines.Eols[^1].Length == 0) lineCount--;
        var delta = 0;
        foreach (var group in groups)
        {
            var from = Math.Max(0, group[0].StartLine - 3);
            var to = Math.Min(lineCount, group[^1].StartLine + group[^1].DeleteCount + 3);
            var body = new StringBuilder();
            int oldLen = 0, newLen = 0;
            var newEol = lines.DominantEol;
            var pending = group.GroupBy(e => e.StartLine).ToDictionary(g => g.Key, g => g.ToList());
            var i = from;
            while (i < to || pending.ContainsKey(i))
            {
                if (pending.Remove(i, out var here))
                {
                    // 与 DocEditBuilder.Apply 的结果顺序一致：同一行先放插入（按排序），再放被替换内容的新行。
                    var deleting = here.FirstOrDefault(e => e.DeleteCount > 0);
                    var eol = lines.EolNear(Math.Max(0, i - 1));
                    if (eol.Length == 0) eol = newEol;
                    if (deleting is not null)
                    {
                        for (var d = 0; d < deleting.DeleteCount; d++)
                        {
                            AppendLine(body, '-', lines, i + d, hasBom);
                            oldLen++;
                        }
                    }
                    foreach (var insert in here.Where(e => e.DeleteCount == 0).OrderBy(e => e.Rank))
                    {
                        foreach (var added in insert.NewLines)
                        {
                            body.Append('+').Append(added).Append(eol);
                            newLen++;
                        }
                    }
                    if (deleting is not null)
                    {
                        foreach (var added in deleting.NewLines)
                        {
                            body.Append('+').Append(added).Append(eol);
                            newLen++;
                        }
                        i += deleting.DeleteCount;
                        continue;
                    }
                }
                if (i >= to) break;
                AppendLine(body, ' ', lines, i, hasBom);
                oldLen++;
                newLen++;
                i++;
            }
            sb.Append($"@@ -{from + 1},{oldLen} +{from + 1 + delta},{newLen} @@\n");
            sb.Append(body);
            delta += newLen - oldLen;
        }
        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, char prefix, LineModel lines, int index, bool hasBom)
    {
        sb.Append(prefix);
        if (index == 0 && hasBom) sb.Append('\uFEFF');
        sb.Append(lines.Lines[index]);
        var eol = lines.Eols[index];
        if (eol.Length > 0)
        {
            sb.Append(eol);
        }
        else
        {
            sb.Append('\n').Append("\\ No newline at end of file\n");
        }
    }
}
