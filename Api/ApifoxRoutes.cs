using ApifoxHelper.Apifox;
using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Reports;
using ApifoxHelper.Source;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace ApifoxHelper.Api;

/// <summary>远端检查、同步、传播核对与报告相关接口。</summary>
public static class ApifoxRoutes
{
    private static IResult Json(object? value) => ApiRoutes.Json(value);

    public static void Map(WebApplication app)
    {
        app.MapGet("/api/capabilities", async (string profileId, ProfileStore profiles, CompatService compat, HttpContext http) =>
            Json(await compat.CapabilitiesAsync(profiles.Get(profileId), http.RequestAborted)));
        app.MapGet("/api/compat", (CompatService compat) => Json(compat.Load()));

        app.MapGet("/api/apifox/projects", async (string profileId, ProfileStore profiles, ApifoxCli cli, HttpContext http) =>
        {
            var result = await cli.ListProjectsAsync(profiles.Get(profileId).Apifox, http.RequestAborted);
            if (!result.Ok) throw new UserException("读取项目列表失败：" + result.Describe());
            return Json((result.Data as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(p => new { id = p["id"]?.ToString(), name = p["name"]?.ToString(), teamName = p["teamName"]?.ToString() ?? p["team"]?["name"]?.ToString() }));
        });
        app.MapGet("/api/apifox/branches", async (string profileId, string projectId, ProfileStore profiles, ApifoxCli cli, HttpContext http) =>
        {
            if (!projectId.All(char.IsDigit)) throw new UserException("项目 ID 无效。");
            var result = await cli.ListBranchesAsync(profiles.Get(profileId).Apifox, projectId, http.RequestAborted);
            if (!result.Ok) throw new UserException("读取分支失败：" + result.Describe());
            return Json((result.Data as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(b => new
            {
                id = b["id"]?.ToString(),
                name = b["name"]?.ToString(),
                isMain = b["isMain"]?.GetValue<bool>() == true,
                isArchived = b["isArchived"]?.GetValue<bool>() == true,
                type = b["type"]?.ToString(),
            }));
        });
        app.MapGet("/api/apifox/folders", async (string profileId, ProfileStore profiles, ApifoxCli cli, HttpContext http) =>
        {
            var profile = profiles.Get(profileId);
            if (profile.Apifox.ProjectId is null) throw new UserException("请先选择 Apifox 项目。");
            var result = await cli.ListFoldersAsync(profile.Apifox, profile.Apifox.ProjectId, profile.Apifox.BranchName, http.RequestAborted);
            if (!result.Ok) throw new UserException("读取目录失败：" + result.Describe());
            return Json(result.Data);
        });

        // 只提取模板允许的字段，不复制脚本、用例、Mock 或历史请求。
        app.MapGet("/api/apifox/template-from-endpoint", async (string profileId, long endpointId, ProfileStore profiles, ApifoxCli cli, HttpContext http) =>
        {
            var profile = profiles.Get(profileId);
            if (profile.Apifox.ProjectId is null) throw new UserException("请先选择 Apifox 项目。");
            var result = await cli.GetEndpointAsync(profile.Apifox, profile.Apifox.ProjectId, profile.Apifox.BranchName, endpointId, http.RequestAborted);
            if (!result.Ok || result.Data is not JsonObject data) throw new UserException("读取接口失败：" + result.Describe());
            return Json(new NewEndpointTemplate
            {
                FolderId = RemoteSnapshotService.ReadLong(data["folderId"]),
                Status = data["status"]?.ToString() ?? "developing",
                ServerId = data["serverId"]?.ToString(),
                Auth = data["auth"]?.DeepClone() as JsonObject,
                SecurityScheme = data["securityScheme"]?.DeepClone() as JsonObject,
                HeaderParameters = (data["parameters"]?["header"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(h => (JsonObject)h.DeepClone()).ToList(),
                Tags = (data["tags"] as JsonArray)?.Select(t => t?.ToString()).OfType<string>().ToList() ?? new List<string>(),
            });
        });

        app.MapPost("/api/profiles/{id}/write-probe", (string id, WriteProbeRequest request, ProfileStore profiles, CompatService compat, FileStore store, JobRunner jobs) =>
        {
            var profile = profiles.Get(id);
            if (!request.Confirm) throw new UserException("请确认已选择专用测试接口后再执行写入探测。");
            var workDir = Path.Combine(store.CompatDir, "probe-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
            return Json(jobs.Start("profile:" + id, "写入兼容探测", async job => await compat.ProbeAsync(profile, request, workDir, job.Token)));
        });

        app.MapPost("/api/tasks/{id}/swagger-load", async (string id, CheckService check, HttpContext http) => Json(await check.LoadSwaggerAsync(id, http.RequestAborted)));
        app.MapPost("/api/tasks/{id}/offline-remote", async (string id, HttpRequest request, CheckService check) =>
        {
            using var reader = new StreamReader(request.Body, Encoding.UTF8);
            return Json(await check.ImportOfflineRemoteAsync(id, await reader.ReadToEndAsync(request.HttpContext.RequestAborted), request.HttpContext.RequestAborted));
        });
        app.MapPost("/api/tasks/{id}/check", (string id, TaskStore tasks, CheckService check, JobRunner jobs) =>
        {
            tasks.Get(id);
            return Json(jobs.Start(id, "契约检查", async job => await check.RunAsync(id, job)));
        });
        app.MapGet("/api/tasks/{id}/diffs", (string id, TaskStore tasks) => Json(new
        {
            diff = tasks.Read<CheckResult>(id, TaskFiles.Diff),
            decisions = tasks.Read<DecisionStore>(id, TaskFiles.Decisions) ?? new DecisionStore(),
            supplement = tasks.Read<SupplementStore>(id, TaskFiles.Supplement) ?? new SupplementStore(),
        }));
        app.MapPut("/api/tasks/{id}/supplement", async (string id, SupplementStore body, SupplementService supplements, HttpContext http) =>
            Json(await supplements.SaveAsync(id, body, http.RequestAborted)));
        app.MapPut("/api/tasks/{id}/decisions", async (string id, DecisionStore body, DecisionService decisions, HttpContext http) =>
            Json(await decisions.SaveAsync(id, body, http.RequestAborted)));
        app.MapPut("/api/tasks/{id}/response-contract", async (string id, ContractReviewRequest request, ContractReviewService reviews, HttpContext http) =>
            Json(await reviews.ReviewAsync(id, request.EndpointKey, request.ReviewState, http.RequestAborted)));

        app.MapPost("/api/tasks/{id}/plan", (string id, CreatePlanRequest request, SyncPlanService plans, JobRunner jobs) =>
        {
            var keys = request.EndpointKeys.ToList();
            return Json(jobs.Start(id, "生成同步计划", async job => await plans.CreateAsync(id, keys, job)));
        });
        app.MapGet("/api/tasks/{id}/plan", (string id, TaskStore tasks) => Json(tasks.Read<SyncPlan>(id, TaskFiles.Plan)));
        app.MapGet("/api/tasks/{id}/plan/payload/{actionId}", (string id, string actionId, TaskStore tasks) =>
        {
            var plan = tasks.Read<SyncPlan>(id, TaskFiles.Plan) ?? throw new UserException("还没有同步计划。");
            var action = plan.Actions.FirstOrDefault(a => a.ActionId == actionId) ?? throw new UserException("动作不存在。", 404);
            return Json(Redactor.RedactNode(FileStore.ReadNode(action.PayloadFile)));
        });
        app.MapPost("/api/tasks/{id}/plan/apply", (string id, ApplyPlanRequest request, SyncExecutor executor, JobRunner jobs) =>
        {
            var hash = request.PlanHash;
            return Json(jobs.Start(id, "执行同步", async job => await executor.ExecuteAsync(id, hash, job)));
        });
        app.MapPost("/api/tasks/{id}/verify", (string id, SyncExecutor executor, JobRunner jobs) =>
            Json(jobs.Start(id, "回读核对", async job => await executor.VerifyAsync(id, job))));

        app.MapPost("/api/tasks/{id}/propagation-check", (string id, PropagationService propagation, JobRunner jobs) =>
            Json(jobs.Start(id, "传播核对", async job => await propagation.CheckAsync(id, job))));
        app.MapGet("/api/tasks/{id}/propagation", (string id, TaskStore tasks) => Json(tasks.Read<PropagationResult>(id, TaskFiles.Propagation)));
        app.MapGet("/api/tasks/{id}/build", (string id, TaskStore tasks) => Json(new
        {
            command = "dotnet " + string.Join(" ", BuildService.Arguments(tasks.Get(id).ProfileSnapshot.ApiProject)),
            last = FileStore.ReadNode(tasks.FilePath(id, TaskFiles.BuildLog)),
        }));
        app.MapPost("/api/tasks/{id}/build", (string id, BuildRequest request, BuildService build, JobRunner jobs) =>
        {
            if (!request.Confirm) throw new UserException("构建会在工作区生成输出文件，请确认后再执行。");
            return Json(jobs.Start(id, "定向构建", async job => await build.RunAsync(id, job)));
        });

        app.MapGet("/api/tasks/{id}/reports/{format}", (string id, string format, ReportExportService exports) =>
        {
            if (format == "preview")
            {
                var preview = exports.Preview(id);
                return Json(new { report = preview.Report, markdown = preview.Markdown });
            }
            var file = exports.Render(id, format);
            return Results.File(file.Bytes, file.ContentType, file.FileName);
        });
    }
}

public sealed class ContractReviewRequest
{
    public string EndpointKey { get; set; } = "";
    public string ReviewState { get; set; } = "";
}

public sealed class BuildRequest
{
    public bool Confirm { get; set; }
}
