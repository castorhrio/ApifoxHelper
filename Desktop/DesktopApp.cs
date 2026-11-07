using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ApifoxHelper.Api;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Desktop;

public sealed record LaunchOptions(int Port, string DataRoot, bool Headless)
{
    public const string Usage = "用法：ApifoxHelper.exe [--data <资料目录>] [--port <端口>] [--headless]";

    public static LaunchOptions Parse(string[] args)
    {
        var port = 5179;
        string? data = null;
        var headless = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"参数 {args[i]} 缺少取值。");
            switch (args[i])
            {
                case "--port":
                    if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is < 0 or > 65535)
                    {
                        throw new ArgumentException("端口必须是 0–65535 的整数。");
                    }
                    break;
                case "--data":
                    data = Next();
                    break;
                case "--headless":
                case "--no-browser": // 兼容旧版命令行参数
                    headless = true;
                    break;
                default:
                    throw new ArgumentException("未知参数：" + args[i]);
            }
        }
        data ??= Environment.GetEnvironmentVariable("APIFOXHELPER_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApifoxHelper");
        return new LaunchOptions(port, Path.GetFullPath(data), headless);
    }
}

public static class DesktopApp
{
    private const string Title = "Apifox 助手";
    private const int AttachParentProcess = -1;

    public static int Run(string[] args)
    {
        LaunchOptions options;
        try
        {
            options = LaunchOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            MessageBox.Show(ex.Message + "\n\n" + LaunchOptions.Usage, Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        using var instance = new SingleInstance(options.DataRoot);
        if (!instance.IsFirst)
        {
            // 同一资料目录只允许一个实例，避免两个进程同时改写任务资料；把已运行的窗口带到前台。
            instance.ActivateExisting();
            return 0;
        }

        if (options.Headless) AttachConsole(AttachParentProcess);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F));

        HelperHost host;
        try
        {
            host = new HelperHost(options.DataRoot, options.Port);
        }
        catch (Exception ex)
        {
            Fail("无法初始化本地服务：" + Redactor.Redact(ex.Message), options.Headless);
            return 1;
        }

        Application.ThreadException += (_, e) =>
        {
            host.Logger.LogError(e.Exception, "界面线程未处理异常");
            MessageBox.Show("发生未处理的错误：" + Redactor.Redact(e.Exception.Message) + "\n\n详细信息已写入日志：" + host.LogDirectory,
                Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => host.Logger.LogCritical(e.ExceptionObject as Exception, "未处理异常");

        if (options.Headless)
        {
            Application.Run(new HeadlessContext(host, instance));
            return 0;
        }

        using var form = new MainForm(host);
        instance.OnActivationRequested(form.ActivateFromOtherInstance);
        Application.Run(form);
        return 0;
    }

    private static void Fail(string message, bool headless)
    {
        if (headless) Console.Error.WriteLine(message);
        else MessageBox.Show(message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);
}

/// <summary>
/// 无界面模式：只运行本地服务，从终端启动时输出地址并响应 Ctrl+C；再次启动程序会在浏览器中打开页面。
/// </summary>
internal sealed class HeadlessContext : ApplicationContext
{
    private readonly HelperHost _host;

    public HeadlessContext(HelperHost host, SingleInstance instance)
    {
        _host = host;
        instance.OnActivationRequested(() => Browser.TryOpen(host.Url, out _));
        _ = RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            await _host.StartAsync();
            Console.WriteLine($"Apifox 助手（无界面模式）已启动：{_host.Url}");
            Console.WriteLine($"本地资料目录：{_host.Store.Root}");
            await _host.WaitForShutdownAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex, "无界面模式运行失败");
            Console.Error.WriteLine("运行失败：" + Redactor.Redact(ex.Message));
        }
        finally
        {
            await _host.DisposeAsync();
            ExitThread();
        }
    }
}

/// <summary>
/// 按资料目录区分的单实例锁。第二个实例通过命名事件通知第一个实例激活窗口，然后退出。
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const int AnyProcess = -1;
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activation;
    private RegisteredWaitHandle? _registration;

    public SingleInstance(string dataRoot)
    {
        var name = "ApifoxHelper-" + Ids.ShortHash(Path.GetFullPath(dataRoot).TrimEnd('\\').ToUpperInvariant(), 16);
        _mutex = new Mutex(initiallyOwned: true, @"Local\" + name, out var createdNew);
        IsFirst = createdNew;
        _activation = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + name + "-activate");
    }

    public bool IsFirst { get; }

    public void ActivateExisting()
    {
        // Windows 只允许前台进程切换前台窗口；当前进程是用户刚启动的，先把这个权限让给已运行的实例。
        AllowSetForegroundWindow(AnyProcess);
        _activation.Set();
    }

    public void OnActivationRequested(Action callback)
    {
        _registration?.Unregister(null);
        _registration = ThreadPool.RegisterWaitForSingleObject(_activation, (_, _) => callback(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activation.Dispose();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}

internal static class Browser
{
    /// <summary>只用系统默认程序打开 http/https 地址或本地目录，不执行其他协议。</summary>
    public static bool TryOpen(string target, out string? error)
    {
        error = null;
        try
        {
            if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                return true;
            }
            if (Path.IsPathFullyQualified(target))
            {
                Directory.CreateDirectory(target);
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return true;
            }
            error = "不支持打开该地址。";
            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
