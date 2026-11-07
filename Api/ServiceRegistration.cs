using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace ApifoxHelper.Api;

public static class ServiceRegistration
{
    public static void AddHelperServices(IServiceCollection services)
    {
        services.AddSingleton<ProfileStore>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton<TaskStore>();
        services.AddSingleton<ProjectLoader>();
        services.AddSingleton<SourceScanService>();
        services.AddSingleton<ContractReviewService>();
        services.AddSingleton<Comments.ModelClient>();
        services.AddSingleton<Comments.DraftService>();
        services.AddSingleton<Comments.CommentPlanService>();
        services.AddSingleton<Comments.PropagationService>();
        services.AddSingleton<Comments.BuildService>();
        services.AddSingleton<Apifox.ApifoxCli>();
        services.AddSingleton<Apifox.RemoteSnapshotService>();
        services.AddSingleton<Contracts.OpenApiSource>();
        services.AddSingleton<Sync.CompatService>();
        services.AddSingleton<Sync.CheckService>();
        services.AddSingleton<Sync.SyncPlanService>();
        services.AddSingleton<Sync.SyncExecutor>();
        services.AddSingleton<Reports.ReportService>();
        services.AddSingleton<Reports.ReportExportService>();
    }
}
