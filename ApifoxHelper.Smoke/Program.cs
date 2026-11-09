using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Apifox;
using ApifoxHelper.Comments;
using ApifoxHelper.Contracts;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using ApifoxHelper.Reports;
using ApifoxHelper.Source;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Smoke;

/// <summary>
/// 原生对话框冒烟构造器（第一百二十九轮）。
/// <para>
/// 存在理由：第一百二十八轮在 wine 下发现「新建配置」对话框一打开就崩——<c>Ui.FieldGridPanel</c>
/// 构造函数里 <c>Padding</c> 先于 <c>ColumnStyles</c> 落地，<c>set_Padding</c> 同步触发
/// <c>ApplyColumnWidths()</c> 时列样式还是空集合，索引 <c>[0]</c> 抛
/// <c>ArgumentOutOfRangeException</c>。这类「编译绿、构造期崩」的缺陷，Linux 上的
/// <c>dotnet build</c> 与源码切片断言都拦不住（断言只能钉住已知的那一处顺序），
/// 唯一可靠的拦截方式是**真的把每个对话框构造出来**。
/// </para>
/// <para>
/// 本程序因此把 <c>Desktop/Native</c> 下每个 <c>*Dialog</c>（含 <c>ProjectsPage</c> /
/// <c>TasksPage</c> / <c>ScanResultsDialog</c> 文件里的三个对话框）与字段网格、滚动宿主、
/// 清单宿主逐个 <c>new</c> 出来并建句柄（<c>Handle</c> + <c>CreateControl()</c> + <c>PerformLayout()</c>），
/// 任一构造抛异常即记为失败并以非零退出码结束。
/// </para>
/// <para>
/// 运行方式（Windows 真机或 wine，需 Windows 桌面运行时）：
/// <c>dotnet run --project ApifoxHelper.Smoke</c>，或发布后直接运行
/// <c>ApifoxHelper.Smoke.exe</c>。退出码 0 = 全部构造成功，1 = 有失败，2 = 环境初始化失败。
/// 输出逐行 <c>PASS/FAIL 名称</c>，失败行带异常类型、消息与首帧堆栈。
/// </para>
/// </summary>
internal static class Program
{
    private static int _failed;
    private static int _passed;

    /// <summary>
    /// 报告落点（<c>--report=&lt;路径&gt;</c>，缺省为临时目录下的 <c>ah-smoke-report.txt</c>）。
    /// 存在的理由：wine 的控制台代码页会把中文打成问号，逐行 PASS/FAIL 读不出是哪一处；
    /// 报告文件按 UTF-8 写，是「真机/模拟器跑过一遍」的可核对证据。
    /// </summary>
    private static string _reportPath = Path.Combine(Path.GetTempPath(), "ah-smoke-report.txt");

    private static readonly List<string> _lines = [];

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetDefaultFont(new Font("Microsoft YaHei UI", 9F));

        // 控制台按 UTF-8 输出：wine 的默认代码页会把中文打成问号，失败行读不出是哪一处。
        try
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
        catch (IOException)
        {
            // 没有控制台（双击运行）时忽略：结果仍可从退出码读。
        }

        foreach (var arg in args)
        {
            if (arg.StartsWith("--report=", StringComparison.Ordinal) && arg.Length > "--report=".Length)
            {
                _reportPath = arg["--report=".Length..];
            }
        }

        var dataRoot = Path.Combine(Path.GetTempPath(), "ah-smoke-" + Guid.NewGuid().ToString("N")[..8]);
        IServiceProvider services;
        ProjectProfile profile;
        CheckTask task;
        try
        {
            services = BuildServices(dataRoot);
            var profiles = services.GetRequiredService<ProfileStore>();
            profile = profiles.Save(new ProjectProfile
            {
                Name = "冒烟配置",
                WorkspaceRoot = dataRoot,
                ApiProject = "Probe.Api/Probe.Api.csproj",
                AllowedSourcePaths = ["Probe.Api/"],
            }, isNew: true);
            task = services.GetRequiredService<TaskStore>().Create(new CreateTaskRequest
            {
                ProfileId = profile.Id,
                Name = "冒烟任务",
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL 环境初始化：" + ex);
            _lines.Add("FAIL 环境初始化：" + ex);
            WriteReport();
            return 2;
        }

        var slot = new DocSlot
        {
            Id = "slot-smoke",
            SymbolId = "M:Probe.Api.Controllers.ProbeController.Get",
            SymbolKind = "method",
            Display = "GET /api/probe",
            Tag = "summary",
            Status = "missing",
            File = "Probe.Api/Controllers/ProbeController.cs",
            Line = 12,
            Role = "endpoint",
            Editable = true,
        };

        var title = "== 原生对话框冒烟构造（" + Environment.OSVersion.VersionString + "）==";
        Console.WriteLine(title);
        _lines.Add(title);
        foreach (var (name, build) in Cases(services, profile, task, slot))
        {
            Run(name, build);
        }

        Console.WriteLine("== 控件工厂 ==");
        foreach (var (name, build) in ControlCases())
        {
            Run(name, build);
        }

        Console.WriteLine($"== 结果：{_passed} 通过 / {_failed} 失败 ==");
        WriteReport();
        return _failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// 冒烟清单：<c>Desktop/Native</c> 下每个对话框一条。新增对话框必须在这里登记——
    /// <c>ApifoxHelper.Tests/NativeDialogSmokeTests.cs</c> 会核对「磁盘上的 *Dialog 类」与
    /// 「本清单里的 new 调用」一一对应，漏登记即测试变红。
    /// </summary>
    private static IEnumerable<(string Name, Func<Control> Build)> Cases(
        IServiceProvider services, ProjectProfile profile, CheckTask task, DocSlot slot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var credentials = services.GetRequiredService<CredentialStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var store = services.GetRequiredService<FileStore>();
        var jobs = services.GetRequiredService<JobRunner>();
        var cli = services.GetRequiredService<ApifoxCli>();

        yield return ("ProfileDialog（新建配置）", () => new ProfileDialog(profiles, cli, credentials, editing: null));
        yield return ("ProfileDialog（编辑配置）", () => new ProfileDialog(profiles, cli, credentials, editing: profile));
        yield return ("NewTaskDialog（新建任务）", () => new NewTaskDialog(profiles));
        yield return ("GenerationDialog（生成方式）", () => new GenerationDialog(credentials, profiles, profile, profileExists: true));
        yield return ("ProfileAdvancedDialog（模块与模板）", () => new ProfileAdvancedDialog(cli, profile));
        yield return ("ProfileAnalysisDialog（序列化与分析）", () => new ProfileAnalysisDialog(profile));
        yield return ("CapabilitiesDialog（写入能力探测）", () => new CapabilitiesDialog(services.GetRequiredService<CompatService>(), store, jobs, profile));
        yield return ("ScanResultsDialog（注释缺口）", () => new ScanResultsDialog(tasks, services.GetRequiredService<DraftService>(), task));
        yield return ("ManualDraftDialog（手工填写说明）", () => new ManualDraftDialog(task, slot));
        yield return ("ScanWarningsDialog（扫描警告）", () => new ScanWarningsDialog(task, ["示例警告：引用不完整。"]));
        yield return ("CandidatesDialog（注释候选）", () => new CandidatesDialog(services.GetRequiredService<DraftService>(), tasks, task));
        yield return ("EvidenceDialog（代码证据）", () => new EvidenceDialog(tasks, task, slot));
        yield return ("ApplyPlanDialog（注释回写）", () => new ApplyPlanDialog(services.GetRequiredService<CommentPlanService>(), tasks, task));
        yield return ("PropagationDialog（生成文档核对）", () => new PropagationDialog(tasks, task));
        yield return ("BuildLogDialog（定向构建输出）", () => new BuildLogDialog(tasks, task));
        yield return ("ContractsDialog（响应契约）", () => new ContractsDialog(tasks, task, services.GetRequiredService<ContractReviewService>()));
        yield return ("CheckResultsDialog（差异检查结果）", () => new CheckResultsDialog(tasks, task));
        yield return ("DecisionsDialog（处理差异确认）", () => new DecisionsDialog(tasks, task, services.GetRequiredService<DecisionService>()));
        yield return ("EndpointTraceDialog（接口追踪明细）", () => new EndpointTraceDialog(tasks, task));
        yield return ("SupplementsDialog（补充说明）", () => new SupplementsDialog(tasks, task, services.GetRequiredService<SupplementService>()));
        yield return ("SyncPreviewDialog（同步预览）", () => new SyncPreviewDialog(
            tasks,
            services.GetRequiredService<SyncPlanService>(),
            services.GetRequiredService<SyncExecutor>(),
            jobs,
            task));
        yield return ("ReportsDialog（报告与交接）", () => new ReportsDialog(services.GetRequiredService<ReportExportService>(), task));
        yield return ("JournalDialog（任务日志）", () => new JournalDialog(tasks, task));
        yield return ("NoticeDialog（提示）", () => new NoticeDialog("关于 Apifox 助手", "冒烟构造用的提示正文。", "等宽区示例", "附注示例"));
    }

    /// <summary>控件工厂：字段网格、滚动宿主、清单宿主——三处都是「构造期就会跑布局」的形状。</summary>
    private static IEnumerable<(string Name, Func<Control> Build)> ControlCases()
    {
        yield return ("Ui.FieldGrid（两列字段网格）", () =>
        {
            var grid = Ui.FieldGrid(ProfileGridColumns.LabelWidth);
            grid.Width = 663; // 第一百二十二轮 wine 实测的网格宽度
            grid.PerformLayout();
            Console.WriteLine($"     cols=[{grid.ColumnStyles[0].Width}, {grid.ColumnStyles[1].Width}]（期望 [120, 495]）");
            return grid;
        });
        yield return ("Ui.ScrollHost（长内容区宿主）", () => Ui.ScrollHost());
        yield return ("Ui.ListHost（清单宿主 + 空态）", () => Ui.ListHost(Ui.List(), new Panel()));
    }

    /// <summary>把逐行结果按 UTF-8 落到报告文件；写不进去只提示，不改退出码（退出码才是判定）。</summary>
    private static void WriteReport()
    {
        try
        {
            _lines.Add($"== 结果：{_passed} 通过 / {_failed} 失败 ==");
            File.WriteAllLines(_reportPath, _lines, new System.Text.UTF8Encoding(false));
            Console.WriteLine("报告：" + _reportPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine("报告写入失败：" + ex.Message);
        }
    }

    private static void Run(string name, Func<Control> build)
    {
        try
        {
            using var control = build();
            // 建句柄 + 强制一次布局：构造期事件顺序缺陷（第一百二十八轮那一类）在这里暴露。
            _ = control.Handle;
            control.CreateControl();
            control.PerformLayout();
            _passed++;
            Console.WriteLine("PASS " + name);
            _lines.Add("PASS " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            var frame = (ex.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
            Console.WriteLine($"FAIL {name}：{ex.GetType().Name}：{ex.Message}");
            Console.WriteLine("     " + frame);
            _lines.Add($"FAIL {name}：{ex.GetType().Name}：{ex.Message}");
            _lines.Add("     " + frame);
        }
    }

    /// <summary>
    /// 与 <c>Api/HelperHost</c> 同构的最小服务容器：同一份 <c>ServiceRegistration</c>，
    /// 只是不起 Kestrel、不注册本机能力桥——冒烟只需要服务层可解析。
    /// </summary>
    private static IServiceProvider BuildServices(string dataRoot)
    {
        // ProfileStore.Validate 要求 API 项目文件真实存在：造一个最小工作区，
        // 否则冒烟在环境初始化阶段就退出，一个对话框都构造不到。
        var probeDir = Path.Combine(dataRoot, "Probe.Api");
        Directory.CreateDirectory(probeDir);
        File.WriteAllText(
            Path.Combine(probeDir, "Probe.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");

        var store = new FileStore(dataRoot);
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new FileLoggerProvider(Path.Combine(store.Root, "logs")));
        });
        services.AddSingleton(store);
        services.AddSingleton<JobRunner>();
        Api.ServiceRegistration.AddHelperServices(services);
        return services.BuildServiceProvider();
    }
}
