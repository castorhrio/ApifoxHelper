using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Api;

/// <summary>
/// 进程内的本地服务（只监听回环地址）。桌面窗口与无界面模式共用同一套服务和页面。
/// </summary>
public sealed class HelperHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private bool _disposed;

    public HelperHost(string dataRoot, int preferredPort)
    {
        Store = new FileStore(dataRoot);
        LogDirectory = Path.Combine(Store.Root, "logs");
        Security = new LocalSecurity();
        Desktop = new DesktopBridge();

        // 首选端口可用时使用固定端口，便于“在浏览器中打开”的地址保持不变；否则交给系统分配，避免检查与绑定之间的竞争。
        var listenPort = preferredPort > 0 && IsPortFree(preferredPort) ? preferredPort : 0;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production,
        });
        builder.WebHost.UseKestrel(o => o.Listen(IPAddress.Loopback, listenPort));
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FileLoggerProvider(LogDirectory));
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Encoder = JsonDefaults.Options.Encoder;
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });
        builder.Services.AddSingleton(Security);
        builder.Services.AddSingleton(Store);
        builder.Services.AddSingleton(Desktop);
        builder.Services.AddSingleton<JobRunner>();
        ServiceRegistration.AddHelperServices(builder.Services);

        _app = builder.Build();
        Jobs = _app.Services.GetRequiredService<JobRunner>();
        Logger = _app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ApifoxHelper");

        // 上次进程中断时仍在执行的同步计划标记为“已中断”，等待用户重新核对，不自动重放。
        // 单实例锁保证此时没有其他进程正在使用同一资料目录。
        _app.Services.GetRequiredService<Sync.SyncExecutor>().RecoverInterrupted();
        _app.Use((context, next) => Security.InvokeAsync(context, () => next(context)));
        ErrorHandling.Use(_app);
        WebAssets.Map(_app, Security);
        ApiRoutes.Map(_app);
    }

    public FileStore Store { get; }

    /// <summary>
    /// 进程内服务容器：原生桌面界面（Desktop/Native）直接解析服务层，不经 HTTP。
    /// </summary>
    public IServiceProvider Services => _app.Services;

    public LocalSecurity Security { get; }

    /// <summary>桌面窗口创建后注册本机能力（如选择目录）；无界面模式保持为空。</summary>
    public DesktopBridge Desktop { get; }

    public JobRunner Jobs { get; }

    public ILogger Logger { get; }

    public string LogDirectory { get; }

    public int Port { get; private set; }

    public string Url => $"http://127.0.0.1:{Port}/";

    public async Task StartAsync()
    {
        await _app.StartAsync();
        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("本地服务没有返回监听地址。");
        Port = new Uri(address).Port;
        Security.Port = Port;
        Logger.LogInformation("本地服务已启动：{Url}，资料目录 {Root}", Url, Store.Root);
    }

    public Task WaitForShutdownAsync() => _app.WaitForShutdownAsync();

    /// <summary>
    /// 先请求正在执行的作业在当前步骤结束后停止（写入中的单个动作不会被打断），再停止服务。
    /// </summary>
    public async Task StopAsync(TimeSpan jobGrace)
    {
        await Jobs.CancelAllAsync(jobGrace);
        await _app.StopAsync();
        Logger.LogInformation("本地服务已停止");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _app.DisposeAsync();
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
