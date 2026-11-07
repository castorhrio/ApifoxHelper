using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApifoxHelper.Api;

public static class ApiRoutes
{
    public static IResult Json(object? value) => Results.Json(value, JsonDefaults.Compact);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/session", (FileStore store, DesktopBridge desktop) => Json(new
        {
            toolVersion = TaskStore.ToolVersion,
            dataRoot = store.Root,
            canPickFolder = desktop.PickFolder is not null,
        }));

        // 新建配置时的本机辅助：选择目录只在桌面窗口可用；查找 .csproj 只读目录与项目文件开头。
        app.MapPost("/api/local/pick-folder", async (PickFolderRequest request, DesktopBridge desktop) =>
        {
            var pick = desktop.PickFolder ?? throw new UserException("只有桌面窗口支持选择目录，请直接填写路径。", 404);
            var initial = request.Initial?.Trim();
            if (initial is null || !Path.IsPathFullyQualified(initial) || !Directory.Exists(initial)) initial = null;
            return Json(new { path = await pick(initial) });
        });
        app.MapGet("/api/local/projects", (string root) => Json(ProjectFinder.Find(root)));

        // 项目配置
        app.MapGet("/api/profiles", (ProfileStore profiles, CredentialStore credentials) =>
            Json(profiles.List().Select(p => new { profile = p, hasModelKey = credentials.HasModelKey(p.Id) })));
        app.MapGet("/api/profiles/{id}", (string id, ProfileStore profiles, CredentialStore credentials) =>
            Json(new { profile = profiles.Get(id), hasModelKey = credentials.HasModelKey(id) }));
        app.MapPost("/api/profiles", (ProjectProfile profile, ProfileStore profiles) => Json(profiles.Save(profile, isNew: true)));
        app.MapPut("/api/profiles/{id}", (string id, ProjectProfile profile, ProfileStore profiles) =>
        {
            profile.Id = Ids.Validate(id);
            return Json(profiles.Save(profile, isNew: false));
        });
        app.MapPut("/api/profiles/{id}/model-key", (string id, ModelKeyRequest request, ProfileStore profiles, CredentialStore credentials) =>
        {
            profiles.Get(id);
            credentials.SetModelKey(id, request.Key);
            return Json(new { hasModelKey = credentials.HasModelKey(id) });
        });
        app.MapGet("/api/profiles/{id}/export", (string id, ProfileStore profiles) =>
        {
            var profile = profiles.Get(id);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, JsonDefaults.Options);
            return Results.File(bytes, "application/json", $"apifox-helper-profile-{profile.Id}.json");
        });

        // 任务
        app.MapGet("/api/tasks", (TaskStore tasks, JobRunner jobs) =>
            Json(tasks.List().Select(t => new { task = Summary(t), runningJob = jobs.Running(t.Id) })));
        app.MapPost("/api/tasks", (CreateTaskRequest request, TaskStore tasks) => Json(tasks.Create(request)));
        app.MapGet("/api/tasks/{id}", (string id, TaskStore tasks, JobRunner jobs) =>
            Json(new { task = tasks.Get(id), runningJob = jobs.Running(id) }));
        app.MapPost("/api/tasks/{id}/cancel", (string id, JobRunner jobs) => Json(new { canceled = jobs.Cancel(id) }));

        app.MapPost("/api/tasks/{id}/source-scan", (string id, TaskStore tasks, SourceScanService scan, JobRunner jobs) =>
        {
            tasks.Get(id);
            return Json(jobs.Start(id, "源码扫描", async job => await scan.ScanAsync(id, job)));
        });
        app.MapGet("/api/tasks/{id}/symbols", (string id, TaskStore tasks) =>
            Json(new
            {
                scan = tasks.Read<ScanOutput>(id, TaskFiles.Symbols),
                manifest = tasks.Read<SourceManifest>(id, TaskFiles.Manifest),
            }));
        app.MapGet("/api/tasks/{id}/evidence", (string id, string? ids, TaskStore tasks) =>
        {
            var store = tasks.Read<EvidenceStore>(id, TaskFiles.Evidence) ?? new EvidenceStore();
            if (string.IsNullOrWhiteSpace(ids)) return Json(store);
            var wanted = ids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
            return Json(new EvidenceStore { Items = store.Items.Where(e => wanted.Contains(e.Id)).ToList(), Coverage = new() });
        });
        app.MapGet("/api/tasks/{id}/coverage/{endpointKey}", (string id, string endpointKey, TaskStore tasks) =>
        {
            var store = tasks.Read<EvidenceStore>(id, TaskFiles.Evidence) ?? new EvidenceStore();
            return Json(store.Coverage.FirstOrDefault(c => c.EndpointKey == Uri.UnescapeDataString(endpointKey)));
        });
        app.MapGet("/api/tasks/{id}/response-contracts", (string id, TaskStore tasks) =>
            Json(tasks.Read<List<ResponseContract>>(id, TaskFiles.ResponseContract) ?? new List<ResponseContract>()));

        app.MapGet("/api/jobs/{id}", (string id, JobRunner jobs) => jobs.Get(id) is { } job ? Json(job) : Results.NotFound());

        MapComments(app);
        ApifoxRoutes.Map(app);
    }

    private static void MapComments(WebApplication app)
    {
        app.MapGet("/api/tasks/{id}/comment-drafts", (string id, DraftService drafts) => Json(drafts.Load(id)));
        app.MapPost("/api/tasks/{id}/comment-drafts", async (string id, GenerateRequest request, DraftService drafts, JobRunner jobs, HttpContext http) =>
        {
            switch (request.Mode)
            {
                case "rule":
                    return Json(await drafts.GenerateRuleAsync(id, request.SlotIds, http.RequestAborted));
                case "model":
                    var slotIds = request.SlotIds.ToList();
                    var confirm = request.ConfirmSend;
                    return Json(jobs.Start(id, "模型生成草稿", async job => await drafts.GenerateWithModelAsync(id, slotIds, confirm, job)));
                default:
                    throw new UserException("生成方式无效。");
            }
        });
        app.MapPost("/api/tasks/{id}/comment-drafts/material", (string id, MaterialRequest request, DraftService drafts, TaskStore tasks) =>
        {
            var material = drafts.BuildMaterial(id, request.SlotIds, out var included);
            if (included.Count == 0) throw new UserException("没有需要生成草稿的注释位置。");
            FileStore.WriteText(tasks.FilePath(id, TaskFiles.Material), material.ToJsonString(JsonDefaults.Options));
            return Json(new
            {
                slots = included.Count,
                files = ((JsonArray)material["endpoints"]!).SelectMany(e => ((JsonArray)e!["evidence"]!).Select(x => x!["file"]!.GetValue<string>())).Distinct().ToList(),
                material,
            });
        });
        app.MapPost("/api/tasks/{id}/comment-drafts/import", async (string id, HttpRequest request, DraftService drafts) =>
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(request.HttpContext.RequestAborted);
            return Json(await drafts.ImportResultAsync(id, body, request.HttpContext.RequestAborted));
        });
        app.MapPost("/api/tasks/{id}/comment-drafts/manual", async (string id, ManualCandidateRequest request, DraftService drafts, HttpContext http) =>
            Json(await drafts.AddManualAsync(id, request, http.RequestAborted)));
        app.MapPut("/api/tasks/{id}/comment-drafts/{draftId}", async (string id, string draftId, CandidateUpdateRequest request, DraftService drafts, HttpContext http) =>
            Json(await drafts.UpdateAsync(id, draftId, request, http.RequestAborted)));

        app.MapPost("/api/tasks/{id}/comment-plan", async (string id, CommentPlanService plans, HttpContext http) =>
            Json(await plans.CreateAsync(id, http.RequestAborted)));
        app.MapGet("/api/tasks/{id}/comment-plan", (string id, TaskStore tasks) => Json(tasks.Read<CommentPlan>(id, TaskFiles.CommentPlan)));
        app.MapGet("/api/tasks/{id}/comment-plan/patch", async (string id, CommentPlanService plans, HttpContext http) =>
        {
            var plan = await plans.ExportPatchAsync(id, http.RequestAborted);
            var bytes = new UTF8Encoding(false).GetBytes(plans.CombinedPatch(plan));
            return Results.File(bytes, "text/x-diff", "comments.patch");
        });
        app.MapPost("/api/tasks/{id}/comment-plan/apply", async (string id, ApplyCommentPlanRequest request, CommentPlanService plans, HttpContext http) =>
            Json(await plans.ApplyAsync(id, request.PlanHash, http.RequestAborted)));
    }

    public static object Summary(CheckTask task) => new
    {
        task.Id,
        task.Name,
        task.ProfileId,
        profileName = task.ProfileSnapshot.Name,
        apifoxProjectId = task.ProfileSnapshot.Apifox.ProjectId,
        apifoxProjectName = task.ProfileSnapshot.Apifox.ProjectName,
        branch = task.ProfileSnapshot.Apifox.BranchName ?? "main",
        task.ModuleNames,
        task.PathPrefixes,
        task.SourceVersionNote,
        task.OfflineMode,
        task.CreatedAt,
        task.UpdatedAt,
        task.Revision,
        task.Stages,
    };
}

public sealed class ModelKeyRequest
{
    public string? Key { get; set; }
}

public sealed class MaterialRequest
{
    public List<string> SlotIds { get; set; } = new();
}

public sealed class PickFolderRequest
{
    public string? Initial { get; set; }
}
