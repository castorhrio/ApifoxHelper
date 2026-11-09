using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Comments;

/// <summary>
/// 注释草稿：规则提取（代码可证实的约束）、模型服务、材料文件交换、手工填写四种入口共用同一候选格式与校验。
/// 候选必须引用本次扫描中存在的证据编号；没有证据的结论只能作为“需业务确认”。
/// </summary>
public sealed partial class DraftService(TaskStore tasks, CredentialStore credentials, ModelClient model)
{
    public const string MaterialFormat = "apifox-helper-material/1";
    public const string ResultFormat = "apifox-helper-candidates/1";

    private const string Instructions =
        "你是 C# 接口文档注释助手。只根据 material 中给出的代码证据，为每个 slot 写中文 XML 文档注释的正文（纯文本，不要 XML 标签、不要代码块）。\n" +
        "规则：\n" +
        "1. 只写证据能支持的结论；每条候选在 evidenceIds 中列出所依据的证据编号（必须来自 evidence 列表）。\n" +
        "2. 不要只把标识符翻译成中文（例如 Name 写成“名称”不合格），要说明用途、取值来源、单位、范围、条件或返回含义。\n" +
        "3. 证据不足以确定的业务含义（权限、单位、默认值、状态、互斥关系等）不要写成事实，放进 openQuestions。\n" +
        "4. 条件成立才生效的规则要写出触发条件，不能写成无条件的必填或限制。\n" +
        "5. 共享 DTO 的属性只写可复用的字段含义；只在某个接口成立的规则写到该接口方法的说明里。\n" +
        "6. 区分 HTTP 状态码与业务封装中的 statusCode；异步任务“已受理”不要写成“已完成”。\n" +
        "7. 输出 JSON：{\"format\":\"" + ResultFormat + "\",\"candidates\":[{\"slotId\":\"...\",\"text\":\"...\",\"evidenceIds\":[\"E...\"],\"openQuestions\":[\"...\"],\"basis\":\"code|needsConfirmation\"}]}。没有把握的 slot 可以不输出。";

    public DraftStore Load(string taskId) => tasks.Read<DraftStore>(taskId, TaskFiles.Drafts) ?? new DraftStore();

    private (ScanOutput Scan, EvidenceStore Evidence) LoadScan(string taskId)
    {
        var scan = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols) ?? throw new UserException("请先完成源码扫描。");
        var evidence = tasks.Read<EvidenceStore>(taskId, TaskFiles.Evidence) ?? new EvidenceStore();
        return (scan, evidence);
    }

    private static bool NeedsDraft(DocSlot slot) => slot.Status is "missing" or "empty" or "inheritDocUnresolved";

    // ---------- 规则提取 ----------

    public async Task<object> GenerateRuleAsync(string taskId, List<string> slotIds, CancellationToken ct)
    {
        var (scan, evidence) = LoadScan(taskId);
        var contracts = tasks.Read<List<ResponseContract>>(taskId, TaskFiles.ResponseContract) ?? new List<ResponseContract>();
        var evidenceById = evidence.Items.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var targets = SelectSlots(scan, slotIds).Where(NeedsDraft).ToList();
        var produced = new List<CommentCandidate>();
        foreach (var slot in targets)
        {
            var linked = slot.EvidenceIds.Select(id => evidenceById.GetValueOrDefault(id)).OfType<CodeEvidence>().ToList();
            // 约束只属于具体参数/字段；方法和类型的用途说明需要语义判断，规则提取不生成。
            var candidate = slot.Tag == "returns"
                ? ReturnsFromContract(slot, contracts, evidenceById)
                : slot.SymbolKind is "property" or "recordParameter" or "enumMember" || (slot.SymbolKind == "method" && slot.Tag == "param")
                    ? ConstraintsFromEvidence(slot, linked)
                    : null;
            if (candidate is not null) produced.Add(candidate);
        }
        var run = new GenerationRun
        {
            Id = Ids.New("g"),
            Mode = "rule",
            StartedAt = DateTimeOffset.Now,
            FinishedAt = DateTimeOffset.Now,
            RequestedSlots = targets.Count,
            Produced = produced.Count,
            Status = produced.Count == 0 ? "noResult" : "succeeded",
        };
        await SaveCandidatesAsync(taskId, produced, run, ct);
        return new { requested = targets.Count, produced = produced.Count, note = "规则提取只生成代码可证实的约束说明（校验特性、默认值、守卫条件、枚举显示名、响应 data 类型），业务用途仍需补充。" };
    }

    private static CommentCandidate? ReturnsFromContract(DocSlot slot, List<ResponseContract> contracts, Dictionary<string, CodeEvidence> evidenceById)
    {
        var contract = contracts.FirstOrDefault(c => slot.EndpointKeys.Contains(c.EndpointKey) && c.DataType is not null && c.ReviewState != "blocked");
        if (contract is null) return null;
        var failures = contract.Branches.Where(b => b.DataKind is "none" or "null" && b.BusinessStatus is not null)
            .Select(b => b.BusinessStatus!.Replace("（属性初始值）", "", StringComparison.Ordinal))
            .Distinct()
            .ToList();
        var text = string.IsNullOrEmpty(contract.WrapperType)
            ? $"响应体为 {contract.DataType}。"
            : $"{contract.WrapperType} 业务响应；成功时 {contract.DataJsonName} 为 {contract.DataType}"
              + (failures.Count > 0 ? $"，异常分支 {contract.DataJsonName} 为空，statusCode 为 {string.Join("/", failures)}。" : "。");
        return new CommentCandidate
        {
            Id = Ids.New("c"),
            SlotId = slot.Id,
            SymbolId = slot.SymbolId,
            Tag = slot.Tag,
            ParameterName = slot.ParameterName,
            Text = text,
            GeneratedText = text,
            EvidenceIds = contract.EvidenceIds.Where(evidenceById.ContainsKey).ToList(),
            OpenQuestions = ["返回数据的业务含义（例如列表范围、排序）需补充。"],
            Basis = "code",
            Source = "rule",
            CreatedAt = DateTimeOffset.Now,
        };
    }

    private static CommentCandidate? ConstraintsFromEvidence(DocSlot slot, List<CodeEvidence> linked)
    {
        var sentences = new List<string>();
        var used = new List<string>();
        foreach (var item in linked)
        {
            var before = sentences.Count;
            switch (item.Kind)
            {
                case "validation":
                    sentences.AddRange(FromValidation(item.Snippet));
                    break;
                case "default":
                    // 初始值只有在请求绑定时才等于“未传时的取值”；响应对象的初始值不代表业务默认值。
                    var isInput = slot.Role.Split(',').Contains("request") || (slot.SymbolKind == "method" && slot.Tag == "param");
                    if (isInput && item.Summary is { } summary && DefaultValuePattern().Match(summary) is { Success: true } m)
                    {
                        var value = m.Groups[1].Value.Trim();
                        sentences.Add(value is "\"\"" or "string.Empty" ? "未传时为空字符串。" : $"未传时为 {value}。");
                    }
                    break;
                case "guard" when item.RelatedSymbolIds.Count == 1:
                    if (FromGuard(item.Snippet) is { } guardSentence) sentences.Add(guardSentence);
                    break;
                case "enumMember" when slot.SymbolKind == "enumMember":
                    if (DisplayPattern().Match(item.Snippet) is { Success: true } display) sentences.Add(display.Groups[1].Value.Trim() + "。");
                    break;
            }
            if (sentences.Count > before) used.Add(item.Id);
        }
        sentences = sentences.Distinct(StringComparer.Ordinal).ToList();
        if (sentences.Count == 0) return null;
        var text = string.Join("", sentences);
        return new CommentCandidate
        {
            Id = Ids.New("c"),
            SlotId = slot.Id,
            SymbolId = slot.SymbolId,
            Tag = slot.Tag,
            ParameterName = slot.ParameterName,
            Text = text,
            GeneratedText = text,
            EvidenceIds = used,
            OpenQuestions = slot.SymbolKind == "enumMember" ? new List<string>() : ["业务用途与取值来源需补充（规则提取只覆盖代码可证实的约束）。"],
            Basis = "code",
            Source = "rule",
            CreatedAt = DateTimeOffset.Now,
            Warnings = slot.SymbolKind == "enumMember" ? new List<string>() : ["该候选只包含约束，接受后说明仍缺少业务用途。"],
        };
    }

    private static IEnumerable<string> FromValidation(string snippet)
    {
        if (RequiredPattern().IsMatch(snippet) || RequiredModifierPattern().IsMatch(snippet)) yield return "必填。";
        var range = RangePattern().Match(snippet);
        if (range.Success)
        {
            var min = range.Groups[1].Value.Trim();
            var max = range.Groups[2].Value.Trim();
            if (max.Contains("MaxValue", StringComparison.Ordinal)) yield return $"不小于 {min}。";
            else if (min.Contains("MinValue", StringComparison.Ordinal)) yield return $"不大于 {max}。";
            else yield return $"取值范围 {min}～{max}。";
        }
        var stringLength = StringLengthPattern().Match(snippet);
        if (stringLength.Success)
        {
            var min = MinimumLengthPattern().Match(snippet);
            yield return min.Success ? $"长度 {min.Groups[1].Value}～{stringLength.Groups[1].Value} 个字符。" : $"长度不超过 {stringLength.Groups[1].Value} 个字符。";
        }
        var maxLength = MaxLengthPattern().Match(snippet);
        if (maxLength.Success) yield return $"最大长度 {maxLength.Groups[1].Value}。";
        var minLength = MinLengthPattern().Match(snippet);
        if (minLength.Success) yield return $"最小长度 {minLength.Groups[1].Value}。";
        var regex = RegexPattern().Match(snippet);
        if (regex.Success) yield return $"需匹配正则 {regex.Groups[1].Value}。";
        if (snippet.Contains("[EmailAddress", StringComparison.Ordinal)) yield return "需为邮箱格式。";
        if (snippet.Contains("[Phone", StringComparison.Ordinal)) yield return "需为电话号码格式。";
        if (snippet.Contains("[Url", StringComparison.Ordinal)) yield return "需为 URL 格式。";
    }

    /// <summary>只解析“单一比较”的守卫条件；条件描述的是被拒绝的情况，这里写成正向要求。</summary>
    private static string? FromGuard(string snippet)
    {
        var condition = GuardConditionPattern().Match(snippet);
        if (!condition.Success) return null;
        var text = condition.Groups[1].Value.Trim();
        if (text.Contains("&&", StringComparison.Ordinal) || text.Contains("||", StringComparison.Ordinal)) return null;
        if (NullOrEmptyPattern().IsMatch(text)) return "不能为空。";
        if (NullCheckPattern().IsMatch(text)) return "不能为空。";
        var comparison = ComparisonPattern().Match(text);
        if (!comparison.Success) return null;
        var op = comparison.Groups[2].Value;
        var value = comparison.Groups[3].Value;
        var length = comparison.Groups[1].Value.EndsWith(".Length", StringComparison.Ordinal) || comparison.Groups[1].Value.EndsWith(".Count", StringComparison.Ordinal);
        var subject = length ? "长度" : "";
        return op switch
        {
            "<=" => $"{subject}必须大于 {value}。",
            "<" => $"{subject}不能小于 {value}。",
            ">" => $"{subject}不能大于 {value}。",
            ">=" => $"{subject}必须小于 {value}。",
            _ => null,
        };
    }

    [GeneratedRegex(@"(?:取默认值|初始值)\s+(.+)$")]
    private static partial Regex DefaultValuePattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?Required(?:Attribute)?\b")]
    private static partial Regex RequiredPattern();

    [GeneratedRegex(@"\brequired\s+[A-Za-z_]")]
    private static partial Regex RequiredModifierPattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?Range\(\s*(?:typeof\([^)]*\)\s*,\s*)?([^,]+),\s*([^,)\]]+)")]
    private static partial Regex RangePattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?StringLength\(\s*(\d+)")]
    private static partial Regex StringLengthPattern();

    [GeneratedRegex(@"MinimumLength\s*=\s*(\d+)")]
    private static partial Regex MinimumLengthPattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?MaxLength\(\s*(\d+)")]
    private static partial Regex MaxLengthPattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?MinLength\(\s*(\d+)")]
    private static partial Regex MinLengthPattern();

    [GeneratedRegex(@"\[(?:[A-Za-z.]*\.)?RegularExpression\(\s*@?""((?:[^""\\]|\\.)*)""")]
    private static partial Regex RegexPattern();

    [GeneratedRegex(@"(?:Description|Display\s*\(\s*Name\s*=)\s*\(?\s*""([^""]+)""")]
    private static partial Regex DisplayPattern();

    [GeneratedRegex(@"^if \((.+)\) (?:return|throw)")]
    private static partial Regex GuardConditionPattern();

    [GeneratedRegex(@"^string\.IsNullOr(?:Empty|WhiteSpace)\(\s*[A-Za-z_][A-Za-z0-9_.]*\s*\)$")]
    private static partial Regex NullOrEmptyPattern();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*\s*(?:==\s*null|is\s+null)$")]
    private static partial Regex NullCheckPattern();

    [GeneratedRegex(@"^([A-Za-z_][A-Za-z0-9_.]*)\s*(<=|<|>=|>)\s*(-?\d+(?:\.\d+)?)[mMdDfFlL]?$")]
    private static partial Regex ComparisonPattern();

    // ---------- 模型服务与材料文件 ----------

    public JsonObject BuildMaterial(string taskId, List<string> slotIds, out List<DocSlot> included)
    {
        var (scan, evidence) = LoadScan(taskId);
        var evidenceById = evidence.Items.ToDictionary(e => e.Id, StringComparer.Ordinal);
        included = SelectSlots(scan, slotIds).Where(s => NeedsDraft(s) || slotIds.Contains(s.Id)).Where(s => s.Status != "stale").ToList();
        var endpoints = new JsonArray();
        foreach (var group in included.GroupBy(s => s.EndpointKeys.FirstOrDefault() ?? ""))
        {
            var endpoint = scan.Endpoints.FirstOrDefault(e => e.Key == group.Key);
            var evidenceIds = group.SelectMany(s => s.EvidenceIds).Distinct(StringComparer.Ordinal).ToList();
            var coverage = evidence.Coverage.FirstOrDefault(c => c.EndpointKey == group.Key);
            var evidenceArray = new JsonArray();
            foreach (var id in evidenceIds)
            {
                if (!evidenceById.TryGetValue(id, out var item) || Redactor.IsExcludedMaterialFile(item.File)) continue;
                evidenceArray.Add(new JsonObject
                {
                    ["id"] = item.Id,
                    ["kind"] = item.Kind,
                    ["file"] = item.File,
                    ["lines"] = $"{item.StartLine}-{item.EndLine}",
                    ["summary"] = item.Summary,
                    ["snippet"] = item.Snippet,
                });
            }
            var slotsArray = new JsonArray();
            foreach (var slot in group)
            {
                slotsArray.Add(new JsonObject
                {
                    ["slotId"] = slot.Id,
                    ["target"] = slot.Display,
                    ["kind"] = slot.SymbolKind,
                    ["tag"] = slot.Tag,
                    ["parameterName"] = slot.ParameterName,
                    ["jsonName"] = slot.JsonName,
                    ["type"] = slot.TypeDisplay,
                    ["existingText"] = slot.ExistingText,
                    ["sharedByEndpoints"] = slot.EndpointKeys.Count,
                    ["evidenceIds"] = new JsonArray(slot.EvidenceIds.Select(i => (JsonNode)JsonValue.Create(i)!).ToArray()),
                });
            }
            endpoints.Add(new JsonObject
            {
                ["endpointKey"] = group.Key,
                ["signature"] = endpoint?.Signature,
                ["route"] = endpoint?.RouteTemplate,
                ["parameters"] = JsonDefaults.ToNode(endpoint?.Parameters),
                ["coverage"] = new JsonObject
                {
                    ["methodsRead"] = JsonDefaults.ToNode(coverage?.MethodsRead.Take(40)),
                    ["unresolved"] = JsonDefaults.ToNode(coverage?.Unresolved.Take(20).Select(u => u.Reason + "：" + u.Expression)),
                },
                ["slots"] = slotsArray,
                ["evidence"] = evidenceArray,
            });
        }
        return new JsonObject
        {
            ["format"] = MaterialFormat,
            ["taskId"] = taskId,
            ["instructions"] = Instructions,
            ["endpoints"] = endpoints,
        };
    }

    /// <summary>材料涉及的源码文件清单（去重、按出现顺序）：确认发送与导出的提示用同一口径。</summary>
    public static List<string> EvidenceFiles(JsonObject material) =>
        ((JsonArray)material["endpoints"]!).SelectMany(e => ((JsonArray)e!["evidence"]!).Select(x => x!["file"]!.GetValue<string>())).Distinct().ToList();

    /// <summary>没有需要生成草稿的注释位置时的拒绝文案（材料导出与模型生成共用）。</summary>
    public const string NoSlotsMessage = "没有需要生成草稿的注释位置。";

    public async Task<object> GenerateWithModelAsync(string taskId, List<string> slotIds, bool confirmSend, JobContext job)
    {
        var task = tasks.Get(taskId);
        var settings = task.ProfileSnapshot.Generation;
        if (settings.Mode != "openai") throw new UserException("配置中没有启用模型服务；可以使用「规则提取」、手工填写，或导出材料交给编码助手处理。");
        if (!confirmSend) throw new UserException("发送前需要确认材料范围。");
        var key = credentials.GetModelKey(task.ProfileId) ?? throw new UserException("尚未保存模型服务密钥。");
        var material = BuildMaterial(taskId, slotIds, out var included);
        if (included.Count == 0) throw new UserException("没有需要生成草稿的注释位置。");

        var run = new GenerationRun { Id = Ids.New("g"), Mode = "model", StartedAt = DateTimeOffset.Now, RequestedSlots = included.Count };
        var (scan, evidence) = LoadScan(taskId);
        var produced = new List<CommentCandidate>();
        var endpoints = (JsonArray)material["endpoints"]!;
        var chunks = new List<JsonObject>();
        foreach (var endpoint in endpoints.OfType<JsonObject>())
        {
            var slots = (JsonArray)endpoint["slots"]!;
            for (var offset = 0; offset < slots.Count; offset += settings.MaxSlotsPerRequest)
            {
                var chunk = (JsonObject)endpoint.DeepClone();
                chunk["slots"] = new JsonArray(slots.Skip(offset).Take(settings.MaxSlotsPerRequest).Select(s => s!.DeepClone()).ToArray());
                chunks.Add(chunk);
            }
        }
        for (var i = 0; i < chunks.Count; i++)
        {
            job.Token.ThrowIfCancellationRequested();
            job.Report((double)i / chunks.Count, $"请求模型生成候选 {i + 1}/{chunks.Count}");
            var user = new JsonObject { ["format"] = MaterialFormat, ["endpoints"] = new JsonArray(chunks[i].DeepClone()) }.ToJsonString(JsonDefaults.Options);
            try
            {
                var content = await model.CompleteAsync(settings, key, Instructions, user, job.Token);
                var result = ModelClient.ParseResult(content);
                var (accepted, rejected) = Validate(result.Candidates, scan, evidence, "model");
                produced.AddRange(accepted);
                run.Rejected += rejected.Count;
                run.Errors.AddRange(rejected.Take(10));
            }
            catch (UserException ex)
            {
                run.Errors.Add($"第 {i + 1} 批：{ex.Message}");
            }
        }
        run.FinishedAt = DateTimeOffset.Now;
        run.Produced = produced.Count;
        run.Status = produced.Count == 0 ? (run.Errors.Count > 0 ? "failed" : "noResult") : run.Errors.Count > 0 ? "partial" : "succeeded";
        await SaveCandidatesAsync(taskId, produced, run, job.Token);
        return new { run.Status, run.RequestedSlots, run.Produced, run.Rejected, errors = run.Errors };
    }

    public async Task<DraftImportResult> ImportResultAsync(string taskId, string json, CancellationToken ct)
    {
        var result = ModelClient.ParseResult(json);
        if (!string.IsNullOrEmpty(result.Format) && result.Format != ResultFormat) throw new UserException($"结果格式应为 {ResultFormat}。");
        var (scan, evidence) = LoadScan(taskId);
        var (accepted, rejected) = Validate(result.Candidates, scan, evidence, "file");
        var run = new GenerationRun
        {
            Id = Ids.New("g"),
            Mode = "file",
            StartedAt = DateTimeOffset.Now,
            FinishedAt = DateTimeOffset.Now,
            RequestedSlots = result.Candidates.Count,
            Produced = accepted.Count,
            Rejected = rejected.Count,
            Errors = rejected.Take(20).ToList(),
            Status = accepted.Count == 0 ? "noResult" : rejected.Count > 0 ? "partial" : "succeeded",
        };
        await SaveCandidatesAsync(taskId, accepted, run, ct);
        return new DraftImportResult(accepted.Count, rejected.Count, run.Errors);
    }

    private static (List<CommentCandidate> Accepted, List<string> Rejected) Validate(List<ExternalCandidate> candidates, ScanOutput scan, EvidenceStore evidence, string source)
    {
        var slots = scan.Slots.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var evidenceIds = evidence.Items.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var accepted = new List<CommentCandidate>();
        var rejected = new List<string>();
        foreach (var external in candidates)
        {
            if (!slots.TryGetValue(external.SlotId ?? "", out var slot))
            {
                rejected.Add($"slotId {external.SlotId} 不存在于本次扫描。");
                continue;
            }
            var error = ValidateText(external.Text);
            if (error is not null)
            {
                rejected.Add($"{slot.Display}：{error}");
                continue;
            }
            var validEvidence = (external.EvidenceIds ?? new List<string>()).Where(evidenceIds.Contains).Distinct(StringComparer.Ordinal).ToList();
            var invalidEvidence = (external.EvidenceIds ?? new List<string>()).Except(validEvidence).ToList();
            var candidate = new CommentCandidate
            {
                Id = Ids.New("c"),
                SlotId = slot.Id,
                SymbolId = slot.SymbolId,
                Tag = slot.Tag,
                ParameterName = slot.ParameterName,
                Text = external.Text.Trim(),
                GeneratedText = external.Text.Trim(),
                EvidenceIds = validEvidence,
                OpenQuestions = (external.OpenQuestions ?? new List<string>()).Where(q => !string.IsNullOrWhiteSpace(q)).Select(q => q.Trim()).Take(10).ToList(),
                Basis = validEvidence.Count > 0 && external.Basis != "needsConfirmation" ? "code" : "needsConfirmation",
                Source = source,
                CreatedAt = DateTimeOffset.Now,
            };
            if (invalidEvidence.Count > 0) candidate.Warnings.Add("引用了本次材料中不存在的证据编号，已忽略：" + string.Join(", ", invalidEvidence));
            if (validEvidence.Count == 0) candidate.Warnings.Add("没有可核对的代码证据，只能作为需业务确认的说明。");
            if (candidate.Text.Length < 4) candidate.Warnings.Add("说明过短，可能只是翻译了名称。");
            accepted.Add(candidate);
        }
        return (accepted, rejected);
    }

    public static string? ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "说明为空";
        var trimmed = text.Trim();
        if (trimmed.Length > 2000) return "说明超过 2000 字";
        if (trimmed.Contains("```", StringComparison.Ordinal)) return "说明中包含代码块，只接受纯文本";
        if (trimmed.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t')) return "说明中包含控制字符";
        if (trimmed.Contains("///", StringComparison.Ordinal) || trimmed.Contains("*/", StringComparison.Ordinal)) return "说明中包含注释标记，只接受纯文本";
        if (DocComments.IsPlaceholder(trimmed)) return "说明是占位文字";
        return null;
    }

    // ---------- 手工与审阅 ----------

    public async Task<CommentCandidate> AddManualAsync(string taskId, ManualCandidateRequest request, CancellationToken ct)
    {
        var (scan, _) = LoadScan(taskId);
        var slot = scan.Slots.FirstOrDefault(s => s.Id == request.SlotId) ?? throw new UserException("注释位置不存在。");
        var action = request.Action == "removeStale" ? "removeStale" : "set";
        if (action == "removeStale" && slot.Status != "stale") throw new UserException("只有失效的 param 标签可以删除。");
        if (action == "set" && ValidateText(request.Text) is { } error) throw new UserException(error);
        var candidate = new CommentCandidate
        {
            Id = Ids.New("c"),
            SlotId = slot.Id,
            SymbolId = slot.SymbolId,
            Tag = slot.Tag,
            ParameterName = slot.ParameterName,
            Text = action == "set" ? request.Text.Trim() : "",
            Basis = "confirmed",
            Source = "manual",
            Action = action,
            CreatedAt = DateTimeOffset.Now,
        };
        await SaveCandidatesAsync(taskId, [candidate], null, ct);
        return candidate;
    }

    public async Task<CommentCandidate> UpdateAsync(string taskId, string candidateId, CandidateUpdateRequest update, CancellationToken ct)
    {
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            var (scan, _) = LoadScan(taskId);
            var store = Load(taskId);
            var candidate = store.Candidates.FirstOrDefault(c => c.Id == candidateId) ?? throw new UserException("候选不存在。", 404);
            var slot = scan.Slots.FirstOrDefault(s => s.Id == candidate.SlotId) ?? throw new UserException("对应的注释位置已不存在，请重新扫描。");
            if (update.Text is not null && candidate.Action == "set")
            {
                if (ValidateText(update.Text) is { } error) throw new UserException(error);
                candidate.Text = update.Text.Trim();
            }
            if (update.Answers is not null) candidate.Answers = update.Answers.Trim();
            if (update.OverwriteExisting is not null) candidate.OverwriteExisting = update.OverwriteExisting.Value;
            if (update.ReviewState is not null)
            {
                if (update.ReviewState is not ("pending" or "accepted" or "deferred" or "rejected")) throw new UserException("审阅状态无效。");
                if (update.ReviewState == "accepted")
                {
                    if (candidate.Action == "set" && ValidateText(candidate.Text) is { } error) throw new UserException(error);
                    if (candidate.Basis == "needsConfirmation" && candidate.OpenQuestions.Count > 0 && string.IsNullOrWhiteSpace(candidate.Answers))
                    {
                        throw new UserException("该候选有待确认的问题，请先填写确认结果再接受。");
                    }
                    if (slot.Status == "valid" && candidate.Action == "set" && !candidate.OverwriteExisting)
                    {
                        throw new UserException("该位置已有有效注释；如需改写，请勾选「覆盖已有注释」。");
                    }
                    foreach (var other in store.Candidates.Where(c => c.SlotId == candidate.SlotId && c.Id != candidate.Id && c.ReviewState == "accepted"))
                    {
                        other.ReviewState = "pending";
                    }
                    // 人工改写过文字或原本需业务确认的候选，接受后记为“人工已确认”；未改动的代码可证实候选保持原依据。
                    if (candidate.Basis == "needsConfirmation" || candidate.Text != (candidate.GeneratedText ?? candidate.Text))
                    {
                        candidate.Basis = "confirmed";
                    }
                    candidate.ReviewedEvidenceHash = SourceScanService.SlotEvidenceHash(slot);
                }
                candidate.ReviewState = update.ReviewState;
                candidate.ReviewedAt = DateTimeOffset.Now;
            }
            tasks.Write(taskId, TaskFiles.Drafts, store);
            await RefreshStageAsync(taskId, store, ct);
            return candidate;
        }
    }

    private async Task SaveCandidatesAsync(string taskId, List<CommentCandidate> produced, GenerationRun? run, CancellationToken ct)
    {
        using (await KeyedLocks.AcquireAsync("task:" + taskId, ct))
        {
            var store = Load(taskId);
            store.Candidates.AddRange(produced);
            if (run is not null) store.Runs.Add(run);
            tasks.Write(taskId, TaskFiles.Drafts, store);
            await RefreshStageAsync(taskId, store, ct);
        }
    }

    private Task RefreshStageAsync(string taskId, DraftStore store, CancellationToken ct)
    {
        var task = tasks.Get(taskId);
        task.Stages.Source.Candidates = store.Candidates.Count;
        task.Stages.Source.Accepted = store.Candidates.Count(c => c.ReviewState == "accepted");
        if (task.Stages.Source.Status == "scanned" && store.Candidates.Count > 0) task.Stages.Source.Status = "reviewing";
        task.Revision++;
        tasks.Save(task);
        return Task.CompletedTask;
    }

    private static IEnumerable<DocSlot> SelectSlots(ScanOutput scan, List<string> slotIds) =>
        slotIds.Count == 0 ? scan.Slots : scan.Slots.Where(s => slotIds.Contains(s.Id));
}
