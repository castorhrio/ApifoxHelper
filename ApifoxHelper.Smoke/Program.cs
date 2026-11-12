using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    /// 报告文件按 UTF-8 写，是「wine 模拟器跑过一遍」的可核对证据（**不是 Windows 真机证据**）。
    /// </summary>
    private static string _reportPath = Path.Combine(Path.GetTempPath(), "ah-smoke-report.txt");

    private static readonly List<string> _lines = [];

    /// <summary>
    /// 冒烟清单里登记的对话框类名（第一百五十四轮，控制仓简报 P1-1）。
    /// <para>
    /// 存在理由：此前「磁盘上的 <c>*Dialog</c> 类 ↔ 冒烟登记」的核对靠正则匹配
    /// <c>new XxxDialog(</c>。正则只认这一种写法——有人把某个用例改成 helper 方法包装
    /// （<c>yield return ("…", () =&gt; BuildProfileDialog(...))</c>），正则就静默漏发现，
    /// 那个对话框从此不再被构造，而 Linux 侧测试全绿。
    /// </para>
    /// <para>
    /// 现在这份清单是**数据**：新增对话框忘了登记、删对话框忘了删登记，两条都由
    /// <c>ApifoxHelper.Tests/NativeDialogSmokeTests.cs</c> 拿磁盘类名与它比对判定，不再看写法。
    /// 清单本身在运行时也被核对一次（<see cref="CheckDialogCoverage"/>）：声明了却没真的构造出来，
    /// 冒烟当场判失败——「登记了」与「构造了」是两件事，只有后者能拦住构造期崩溃。
    /// </para>
    /// </summary>
    internal static readonly string[] SmokeDialogNames =
    [
        "ApplyPlanDialog",
        "BuildLogDialog",
        "CandidatesDialog",
        "CapabilitiesDialog",
        "CheckResultsDialog",
        "ContractsDialog",
        "DecisionsDialog",
        "EndpointTraceDialog",
        "EvidenceDialog",
        "GenerationDialog",
        "JournalDialog",
        "ManualDraftDialog",
        "NewTaskDialog",
        "NoticeDialog",
        "ProfileAdvancedDialog",
        "ProfileAnalysisDialog",
        "ProfileDialog",
        "PropagationDialog",
        "ReportsDialog",
        "ScanResultsDialog",
        "ScanWarningsDialog",
        "StatusTextDialog",
        "SupplementsDialog",
        "SyncPreviewDialog",
    ];

    /// <summary>
    /// 冒烟清单里登记的页面类名（第一百五十五轮，控制仓简报 P1-2）。
    /// <para>
    /// 存在理由：清单此前只覆盖 <c>*Dialog</c>，而两个主页面（<c>ProjectsPage</c> / <c>TasksPage</c>）
    /// 同样是构造期崩溃的高发地——它们的构造函数里建了整屏控件树、装了事件、读了排序记忆文件。
    /// 页面此前只在「读取失败态」那两段里被构造，删掉那两段就再没人构造页面，而 Linux 侧全绿。
    /// </para>
    /// <para>
    /// 与对话框清单同一形状：这里是**数据**，磁盘类名与它双向比对由
    /// <c>ApifoxHelper.Tests/NativeDialogSmokeTests.cs</c> 判定；运行时再由
    /// <see cref="CheckPageCoverage"/> 核对「声明了」与「真的构造了」是同一批。
    /// </para>
    /// </summary>
    internal static readonly string[] SmokePageNames =
    [
        "ProjectsPage",
        "TasksPage",
    ];

    /// <summary>本次运行真的构造出来的对话框类名（<see cref="Run"/> 里按控件实际类型记录）。</summary>
    private static readonly HashSet<string> _constructedDialogs = new(StringComparer.Ordinal);

    /// <summary>本次运行真的构造出来的页面类名（<see cref="Run"/> 里按控件实际类型记录）。</summary>
    private static readonly HashSet<string> _constructedPages = new(StringComparer.Ordinal);

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 与产品入口同一份字体口径（DESIGN §3 的字体栈，找到即停）：冒烟器量到的控件尺寸
        // 必须与真机同一套字体，否则「字段宽度实测」量的是另一只壳。
        Application.SetDefaultFont(ApifoxHelper.Desktop.Native.UiTokens.Create(ApifoxHelper.Desktop.Native.UiTokens.CaptionPt));

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

        Console.WriteLine("== 字段宽度实测 ==");
        MeasureFieldWidths(services, profile);

        Console.WriteLine("== 字段宽度实测（DPI 缩放 125% / 150% / 200%）==");
        MeasureFieldWidthsAtScale(services, profile);

        Section("端到端主链路（真实控件交互）", nameof(RunEndToEnd));
        RunEndToEnd(services, dataRoot);

        Section("端到端回写链路（扫描 → 候选 → 计划 → 回写落盘）", nameof(RunWriteBackEndToEnd));
        RunWriteBackEndToEnd(services, dataRoot);

        Section("注释缺口对话框（真实控件交互）", nameof(RunScanResultsInteraction));
        RunScanResultsInteraction(services, dataRoot);

        Section("字体栈运行时命中（真实控件）", nameof(RunFontStackRuntime));
        RunFontStackRuntime();

        Section("注释候选对话框（真实控件交互）", nameof(RunCandidatesInteraction));
        RunCandidatesInteraction(services, dataRoot);

        Section("补充说明对话框（真实控件交互）", nameof(RunSupplementsInteraction));
        RunSupplementsInteraction(services, dataRoot);

        Section("差异确认对话框（真实控件交互）", nameof(RunDecisionsInteraction));
        RunDecisionsInteraction(services, dataRoot);

        Section("响应契约对话框（真实控件交互）", nameof(RunContractsInteraction));
        RunContractsInteraction(services, dataRoot);

        Section("配置表单的真实布局（新建 / 编辑）", nameof(RunProfileFormLayout));
        RunProfileFormLayout(services, profile);

        Section("配置页读取失败态（失败 → 重试 → 恢复，真实控件）", nameof(RunProfileLoadFailure));
        RunProfileLoadFailure(services, dataRoot);

        Section("任务页读取失败态（失败 → 重试 → 恢复，真实控件）", nameof(RunTaskLoadFailure));
        RunTaskLoadFailure(services, dataRoot);

        Section("报告对话框读取失败态（失败 → 重试 → 恢复，真实控件）", nameof(RunReportsLoadFailure));
        RunReportsLoadFailure(services, dataRoot);

        Section("差异确认读取失败态（两种失败 → 重试 → 恢复，真实控件）", nameof(RunDecisionsLoadFailure));
        RunDecisionsLoadFailure(services, dataRoot);

        Section("任务日志读取失败态（失败 → 重试 → 恢复，真实控件）", nameof(RunJournalLoadFailure));
        RunJournalLoadFailure(services, dataRoot);

        Section("极端数据下的详情区与列表（长中文 / 长路径 / 超长错误消息，真实控件）", nameof(RunDetailBreak));
        RunDetailBreak(services, dataRoot);

        Section("状态行的完整错误入口（放得下不给 → 放不下给 → 点开读全 → 恢复后收起，真实控件）", nameof(RunStatusFullText));
        RunStatusFullText(services, dataRoot);

        Section("对话框状态行的完整错误入口（三屏各四段：嵌入页面文件的 / 独立文件的 / 真实保存失败的，真实控件）", nameof(RunDialogStatusFullText));
        RunDialogStatusFullText(services, dataRoot);

        Section("对话框状态行逐屏（15 处对话框各四段：短句不给 → 长句按口径重判 → 点开读全 → 回短句收起，真实控件）", nameof(RunDialogStatusSweep));
        RunDialogStatusSweep(services, profile, task, slot);

        Section("提示对话框的「复制路径」（按钮在 → 真点 → 剪贴板逐字核对 → 不跳位 → 真点关闭，真实控件）", nameof(RunNoticeCopyClick));
        RunNoticeCopyClick(dataRoot);

        Section("定向构建输出的「复制命令」（按钮在 → 真点 → 剪贴板逐字核对 → 不跳位 → 真点关闭，真实控件）", nameof(RunBuildCommandCopyClick));
        RunBuildCommandCopyClick(services, dataRoot);

        Section("说明行放得下（三屏说明行按各自对话框宽度量文字，真实控件）", nameof(RunCaptionFit));
        RunCaptionFit(services, dataRoot);

        Section("写入清单表头放得下（逐列量表头文字与排序方向标记，真实控件）", nameof(RunActionHeaderFit));
        RunActionHeaderFit(services, dataRoot);

        Section("完整旅程单次运行（配置 → 任务 → 扫描 → 规则提取 → 审阅 → 回写 → 核对 → 报告）", nameof(RunFullJourney));
        RunFullJourney(services, dataRoot);

        Console.WriteLine("== 冒烟清单覆盖（声明 ↔ 真实构造）==");
        CheckDialogCoverage();
        CheckPageCoverage();

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

        // 两个主页面（第一百五十五轮）：页面类此前只在「读取失败态」两段里被构造，
        // 清单里没有它们——删掉那两段就再没人构造页面，而 Linux 侧全绿。
        // 这里按产品入口同一份构造参数真的 new 一次（建句柄 + 强制布局由 Run 统一做）。
        yield return ("ProjectsPage（项目设置）", () => new ProjectsPage(
            profiles,
            services.GetRequiredService<CompatService>(),
            cli,
            store,
            jobs,
            credentials,
            new ListSortMemory(Path.Combine(store.Root, "ui", "list-sort.json"))));
        yield return ("TasksPage（检查任务）", () => new TasksPage(
            tasks,
            services.GetRequiredService<SourceScanService>(),
            jobs,
            profiles,
            services.GetRequiredService<DraftService>(),
            services.GetRequiredService<CommentPlanService>(),
            services.GetRequiredService<PropagationService>(),
            services.GetRequiredService<BuildService>(),
            services.GetRequiredService<ContractReviewService>(),
            services.GetRequiredService<CheckService>(),
            services.GetRequiredService<DecisionService>(),
            services.GetRequiredService<SupplementService>(),
            services.GetRequiredService<ReportExportService>(),
            services.GetRequiredService<SyncPlanService>(),
            services.GetRequiredService<SyncExecutor>(),
            new ListSortMemory(Path.Combine(store.Root, "ui", "list-sort.json"))));
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
        // 默认确认委托（第一百三十三轮）：Linux 侧的源码切片只能钉住「构造里装了委托」这一处形状，
        // 「装上的确实是对话框自己的默认实现」只有真构造一次才知道——留空的话产品路径点「确认回写」
        // 会 NRE，而 Linux 侧测试全绿。
        // 运行时核对正文与标题（第一百三十四轮）：MessageBox 是静态方法，默认按钮参数在运行时读不到
        // （如实记录，不冒充），退而求其次核对与产品路径逐字同源的那份正文——对话框的默认委托取的就是
        // 这两个成员，它们在这里对得上，产品路径上弹出的那段话就对得上。
        yield return ("ApplyPlanDialog 默认确认委托（真实控件）", () =>
        {
            var dialog = new ApplyPlanDialog(services.GetRequiredService<CommentPlanService>(), tasks, task);
            var confirm = dialog.ConfirmApply
                ?? throw new InvalidOperationException("构造后 ConfirmApply 为空（产品路径点「确认回写」会 NRE）。");
            var declaring = confirm.Method.DeclaringType;
            if (declaring != typeof(ApplyPlanDialog) && declaring?.DeclaringType != typeof(ApplyPlanDialog))
            {
                throw new InvalidOperationException($"默认确认委托的声明类型是「{declaring?.Name}」，不是 ApplyPlanDialog 自己的默认实现。");
            }
            if (ApplyPlanEntries.ConfirmApplyTitle != "确认注释回写")
            {
                throw new InvalidOperationException($"确认框标题是「{ApplyPlanEntries.ConfirmApplyTitle}」，不是「确认注释回写」。");
            }
            var text = ApplyPlanEntries.ConfirmApplyText(3, 7);
            // 承诺句在这里是**故意重复的字面量**：它是运行时核对的绊线，不是第二份正文——
            // 正文改了而这里没跟上，冒烟就红，逼人回来看这段承诺还在不在。
            if (!text.Contains("只修改 /// 注释行，不改执行代码", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("确认正文里没有「只修改 /// 注释行，不改执行代码」这句承诺。");
            }
            if (!text.Contains("将把注释改动写入 3 个源码文件，共 7 处注释修改。", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"确认正文没有按计划给的数字说话：{text}");
            }
            if (!text.EndsWith("确认回写？", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("确认正文没有以问句收尾（这是要用户回答的问题，不是通知）。");
            }
            return dialog;
        });
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
        // 状态行完整错误对话框（第一百六十四轮）：正文按产品路径同一份拼装（标题 + 失败原因），
        // 这一条只拦「构造期就崩」（第一百二十八轮那一类）；放不下 → 入口 → 点开读全的真实交互在 RunStatusFullText。
        yield return ("StatusTextDialog（读全状态行的失败原因）", () => new StatusTextDialog(
            LoadFailureState.FailureStatus(
                LoadFailureState.Tasks.Title,
                "The process cannot access the file 'task.json' because it is being used by another process.")));
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

    /// <summary>
    /// 字段宽度实测（第一百三十轮）：把「新建配置」对话框真的构造出来、强制布局，然后量
    /// 字段列里每个输入框/下拉框的宽度。
    /// <para>
    /// 存在理由：第一百二十八轮挂账的缺陷是「字段列 495，而四个输入框只有 68px」——这类
    /// 「构造不崩、但摆错了」的缺陷，构造冒烟（只判抛不抛异常）拦不住，源码切片也只能钉住
    /// 已知的那一处。这里量的是**真实 WinForms 布局算出来的宽度**，与 <c>Ui.FieldGridPanel</c>
    /// 的 <c>ApplyFieldWidths</c> 走同一条路径：宽度小于 <c>MinFieldBoxWidth</c> 即判失败。
    /// </para>
    /// </summary>
    private static void MeasureFieldWidths(IServiceProvider services, ProjectProfile profile)
    {
        const string name = "ProfileDialog 字段宽度（真实布局）";
        try
        {
            using var dialog = new ProfileDialog(
                services.GetRequiredService<ProfileStore>(),
                services.GetRequiredService<ApifoxCli>(),
                services.GetRequiredService<CredentialStore>(),
                editing: profile);
            _ = dialog.Handle;
            dialog.CreateControl();
            dialog.PerformLayout();

            var grid = FindGrid(dialog);
            if (grid is null)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + "：对话框里找不到两列字段网格。");
                _lines.Add("FAIL " + name + "：对话框里找不到两列字段网格。");
                return;
            }

            var measured = new List<string>();
            var tooNarrow = new List<string>();
            foreach (Control control in grid.Controls)
            {
                if (grid.GetColumn(control) != 1) continue;
                if (control is not (Ui.FieldBox or ComboBox)) continue;
                var label = RowLabel(grid, grid.GetRow(control));
                measured.Add($"{label}={control.Width}");
                if (control.Width < ProfileGridColumns.MinFieldBoxWidth) tooNarrow.Add($"{label}={control.Width}");
            }

            var line = "     " + string.Join("，", measured);
            Console.WriteLine(line);
            _lines.Add(line);
            if (measured.Count == 0)
            {
                _failed++;
                Console.WriteLine("FAIL " + name + "：字段列里一个字段控件都没量到。");
                _lines.Add("FAIL " + name + "：字段列里一个字段控件都没量到。");
                return;
            }
            if (tooNarrow.Count > 0)
            {
                _failed++;
                var fail = $"FAIL {name}：{string.Join("，", tooNarrow)} 低于下限 {ProfileGridColumns.MinFieldBoxWidth}。";
                Console.WriteLine(fail);
                _lines.Add(fail);
                return;
            }
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
    /// 字段宽度实测（DPI 缩放，第一百三十三轮）：把「新建配置」对话框按 125% / 150% / 200% 缩放后
    /// 再量一遍字段列。
    /// <para>
    /// 存在理由：控制仓简报的 P0-3 要求真机 125%/150%/200% 三档 DPI 实测，本环境没有 Windows 真机
    /// （第一百二十四轮的挂账决策：责任在环境供给）。这里做的是**本环境能做的替代**——用
    /// <c>Control.Scale</c> 模拟三档缩放，量真实 WinForms 布局算出来的列宽与字段宽，并断言
    /// 「列宽之和恰等于网格可用宽度」这条不变量在缩放后仍成立（<c>Ui.FieldGridPanel</c> 在
    /// <c>OnSizeChanged</c> 里按当前宽度重算，缩放会改宽度，正是这条路径）。
    /// </para>
    /// <para>
    /// 它**不能替代真机 DPI 观感复核**：wine 的字体度量与 GDI 绘制不是 Windows 实现，
    /// 文字截断、按钮热区这类观感问题只有真机截图能判（见 docs/NOTES.md 的真机清单）。
    /// </para>
    /// </summary>
    private static void MeasureFieldWidthsAtScale(IServiceProvider services, ProjectProfile profile)
    {
        foreach (var percent in new[] { 125, 150, 200 })
        {
            var name = $"ProfileDialog 字段宽度（DPI {percent}%）";
            try
            {
                using var dialog = new ProfileDialog(
                    services.GetRequiredService<ProfileStore>(),
                    services.GetRequiredService<ApifoxCli>(),
                    services.GetRequiredService<CredentialStore>(),
                    editing: profile);
                _ = dialog.Handle;
                dialog.CreateControl();
                dialog.Scale(new SizeF(percent / 100f, percent / 100f));
                dialog.PerformLayout();

                var grid = FindGrid(dialog)
                    ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
                // 不变量：列宽之和恰等于网格可用宽度（缩放改了宽度，列宽必须跟着重算）。
                var usable = grid.Width - grid.Padding.Horizontal;
                var sum = grid.ColumnStyles.Cast<ColumnStyle>().Sum(style => (int)style.Width);
                if (sum != usable)
                {
                    throw new InvalidOperationException($"列宽之和 {sum} 与网格可用宽度 {usable} 不一致（缩放后列宽没有按当前宽度重算）。");
                }

                var measured = new List<string>();
                var tooNarrow = new List<string>();
                foreach (Control control in grid.Controls)
                {
                    if (grid.GetColumn(control) != 1) continue;
                    if (control is not (Ui.FieldBox or ComboBox)) continue;
                    var label = RowLabel(grid, grid.GetRow(control));
                    measured.Add($"{label}={control.Width}");
                    if (control.Width < ProfileGridColumns.MinFieldBoxWidth) tooNarrow.Add($"{label}={control.Width}");
                }
                if (measured.Count == 0) throw new InvalidOperationException("字段列里一个字段控件都没量到。");
                if (tooNarrow.Count > 0)
                {
                    throw new InvalidOperationException($"{string.Join("，", tooNarrow)} 低于下限 {ProfileGridColumns.MinFieldBoxWidth}。");
                }

                var line = $"     {percent}%：网格 {grid.Width}、可用 {usable}、列宽 [{grid.ColumnStyles[0].Width}, {grid.ColumnStyles[1].Width}]；" + string.Join("，", measured);
                Console.WriteLine(line);
                _lines.Add(line);
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
    }

    /// <summary>
    /// 端到端主链路（第一百三十一轮）：不再只判「构造不抛异常」，而是**真的把字敲进控件、
    /// 真的点按钮**，再断言数据落到了磁盘上。
    /// <para>
    /// 存在理由：第一百二十九轮的冒烟器把「构造期崩」变成了可拦截，但控制仓简报的 P0 缺口是
    /// 「只到每个对话框能构造，未到端到端能走通」——构造成功不等于「填得进、点得动、存得下」。
    /// 本段覆盖主链路的前两段（新建配置 → 保存 → 新建任务 → 落盘），全部走真实 WinForms 控件：
    /// <c>TextBox.Text</c> 赋值、<c>Button.PerformClick()</c>（WinForms 的点击链路，不是直接调事件处理器），
    /// 断言读的是 <c>ProfileStore.Get</c> / <c>TaskStore.Get</c> 从磁盘重读的结果。
    /// </para>
    /// <para>
    /// 不覆盖的部分如实挂账：扫描及之后各段需要 Roslyn 参考程序集与真实 Apifox CLI 登录态，
    /// 本环境（wine）不具备，仍待 Windows 真机（见 docs/NOTES.md 的真机清单）。
    /// </para>
    /// </summary>
    private static void RunEndToEnd(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var cli = services.GetRequiredService<ApifoxCli>();
        var credentials = services.GetRequiredService<CredentialStore>();

        // 第一段：新建配置 → 填四个字段 → 点「保存」→ 配置真的落到 profiles/<id>.json。
        string? profileId = null;
        RunAction("端到端：新建配置填写并保存（真实控件）", () =>
        {
            var before = profiles.List().Count;
            using var dialog = new ProfileDialog(profiles, cli, credentials, editing: null);
            dialog.Show();
            Application.DoEvents();

            var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
            SetField(grid, "名称", "端到端配置");
            SetField(grid, "仓库根目录", dataRoot);
            SetField(grid, "API 项目", "Probe.Api/Probe.Api.csproj");
            SetField(grid, "允许回写范围", "Probe.Api/");
            Click(dialog, "保存");
            Application.DoEvents();

            if (dialog.DialogResult != DialogResult.OK)
            {
                throw new InvalidOperationException("点「保存」后对话框没有以「确定」结束（校验被拒或按钮未触发）。");
            }
            var saved = dialog.Saved ?? throw new InvalidOperationException("保存后 Saved 为空。");
            // 从磁盘重读：ProfileStore.Get 直接读 profiles/<id>.json，不经内存缓存。
            var reread = profiles.Get(saved.Id);
            if (reread.Name != "端到端配置") throw new InvalidOperationException($"落盘名称是「{reread.Name}」。");
            if (reread.WorkspaceRoot != dataRoot) throw new InvalidOperationException($"落盘仓库根目录是「{reread.WorkspaceRoot}」。");
            if (reread.ApiProject != "Probe.Api/Probe.Api.csproj") throw new InvalidOperationException($"落盘 API 项目是「{reread.ApiProject}」。");
            if (profiles.List().Count != before + 1) throw new InvalidOperationException("配置数量没有增加一条。");
            profileId = saved.Id;
            _lines.Add($"     配置已落盘：{saved.Id}，名称「{reread.Name}」，回写范围「{string.Join(";", reread.AllowedSourcePaths)}」");
        });

        // 第二段：新建任务 → 选配置、填名称/指定接口/版本备注 → 点「仅创建」→ 任务真的落到 tasks/<id>/task.json。
        RunAction("端到端：新建任务填写并创建（真实控件）", () =>
        {
            if (profileId is null) throw new InvalidOperationException("上一段没有产出配置，无法继续。");
            using var dialog = new NewTaskDialog(profiles);
            dialog.Show();
            Application.DoEvents();

            var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
            SelectProfile(grid, "端到端配置");
            SetField(grid, "任务名称", "端到端任务");
            SetField(grid, "指定接口", "GET /api/probe\nPOST /api/probe");
            SetField(grid, "版本备注", "v1.2.3");
            Click(dialog, "仅创建");
            Application.DoEvents();

            if (dialog.DialogResult != DialogResult.OK) throw new InvalidOperationException("点「仅创建」后对话框没有以「确定」结束。");
            if (dialog.StartScan) throw new InvalidOperationException("点「仅创建」却要求立即扫描。");
            var request = dialog.Request;
            if (request.ProfileId != profileId) throw new InvalidOperationException("创建请求里的配置不是刚保存的那一个。");
            if (request.Name != "端到端任务") throw new InvalidOperationException($"创建请求里的任务名是「{request.Name}」。");
            if (request.EndpointKeys.Count != 2) throw new InvalidOperationException($"指定接口解析出 {request.EndpointKeys.Count} 行，期望 2 行。");
            if (request.SourceVersionNote != "v1.2.3") throw new InvalidOperationException($"创建请求里的版本备注是「{request.SourceVersionNote}」。");

            // 页面在对话框确认后做的事：把请求交给 TaskStore（与 TasksPage 同一入口）。
            var task = tasks.Create(request);
            var reread = tasks.Get(task.Id);
            if (reread.Name != "端到端任务") throw new InvalidOperationException($"落盘任务名是「{reread.Name}」。");
            if (reread.EndpointKeys.Count != 2) throw new InvalidOperationException($"落盘指定接口是 {reread.EndpointKeys.Count} 行。");
            if (reread.SourceVersionNote != "v1.2.3") throw new InvalidOperationException($"落盘版本备注是「{reread.SourceVersionNote}」。");
            if (reread.ProfileId != profileId) throw new InvalidOperationException("落盘任务的配置不是刚保存的那一个。");
            _lines.Add($"     任务已落盘：{task.Id}，名称「{reread.Name}」，指定接口「{string.Join("、", reread.EndpointKeys)}」");
        });

        // 第三段（反向）：名称留空点「保存」→ 校验拒绝、不落盘、状态行有文案。
        RunAction("端到端：保存校验失败不落盘（真实控件）", () =>
        {
            var before = profiles.List().Count;
            using var dialog = new ProfileDialog(profiles, cli, credentials, editing: null);
            dialog.Show();
            Application.DoEvents();

            var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
            SetField(grid, "仓库根目录", dataRoot);
            SetField(grid, "API 项目", "Probe.Api/Probe.Api.csproj");
            Click(dialog, "保存");
            Application.DoEvents();

            if (dialog.DialogResult == DialogResult.OK) throw new InvalidOperationException("名称留空却保存成功。");
            if (dialog.Saved is not null) throw new InvalidOperationException("保存被拒却留下了 Saved。");
            if (profiles.List().Count != before) throw new InvalidOperationException("保存被拒却多出一条配置。");
            var status = FindAll<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.Contains("请填写配置名称", StringComparison.Ordinal));
            if (status is null) throw new InvalidOperationException("状态行没有出现「请填写配置名称」的拒绝文案。");
            _lines.Add($"     拒绝文案：{status}");
        });
    }

    /// <summary>
    /// 探针工作区（第一百三十二轮）：一个 Web SDK 项目 + 一个缺 <c>///</c> 的控制器。
    /// <para>
    /// 存在理由：扫描要真的发现接口与注释缺口，项目就必须是 Web SDK（<c>EndpointDiscovery</c> 按
    /// ASP.NET Core MVC 的属性路由发现动作），控制器里必须真的少一段文档注释（否则没有可回写的位置）。
    /// 文件按 UTF-8 带 BOM + CRLF 写，回写后要能断言「BOM 与换行符一字未改」。
    /// </para>
    /// </summary>
    private static void CreateProbeWorkspace(string projectDir)
    {
        Directory.CreateDirectory(Path.Combine(projectDir, "Controllers"));
        File.WriteAllText(
            Path.Combine(projectDir, Path.GetFileName(projectDir) + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(projectDir, "Controllers", "ProbeController.cs"),
            ProbeControllerSource,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>探针控制器源码：<c>Get</c> 与 <c>ProbeResponse.Name</c> 都没有 <c>///</c>，扫描会报缺口。</summary>
    private const string ProbeControllerSource =
        "using Microsoft.AspNetCore.Mvc;\r\n" +
        "\r\n" +
        "namespace Probe.Api.Controllers;\r\n" +
        "\r\n" +
        "[ApiController]\r\n" +
        "[Route(\"api/probe\")]\r\n" +
        "public class ProbeController : ControllerBase\r\n" +
        "{\r\n" +
        "    [HttpGet]\r\n" +
        "    public ActionResult<ProbeResponse> Get()\r\n" +
        "    {\r\n" +
        "        return Ok(new ProbeResponse { Name = \"probe\" });\r\n" +
        "    }\r\n" +
        "}\r\n" +
        "\r\n" +
        "public class ProbeResponse\r\n" +
        "{\r\n" +
        "    public string Name { get; set; } = \"\";\r\n" +
        "}\r\n";

    /// <summary>
    /// 端到端回写链路（第一百三十二轮）：从扫描一路走到「注释真的写进源码文件」。
    /// <para>
    /// 存在理由：第一百三十一轮的端到端只覆盖到「新建配置 → 新建任务」，扫描及之后各段仍停在
    /// 「能构造对话框」或「纯服务层单测」。注释回写是产品核心价值动作，也是写入安全不变量最集中的地方
    /// （只改 <c>///</c> 注释行、写入前保存前态、确认后文件被改动即拒绝写入），这一段没跑过，
    /// 「主链路可靠」无从谈起。
    /// </para>
    /// <para>
    /// 六段：① 扫描产出注释缺口；② 手工候选并接受；③ 生成计划且两次哈希一致；
    /// ④ 反向——计划哈希不符时拒绝写入、磁盘不变；⑤ 确认后回写落盘，只改 <c>///</c> 行、BOM/换行不变；
    /// ⑥ 真实对话框驱动——构造 <c>ApplyPlanDialog</c>、等它自己生成计划、点「确认回写」按钮，
    /// 断言源码真的被改（确认框用 <c>ConfirmApply</c> 替身，因为模态框在无人值守下点不动）。
    /// </para>
    /// <para>
    /// 服务层调用一律包在 <c>Task.Run</c> 里同步等待：WinForms 装了同步上下文，直接在界面线程上
    /// <c>GetResult()</c> 会把续体排到没有消息循环的队列上，等成死锁。
    /// </para>
    /// </summary>
    private static void RunWriteBackEndToEnd(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var scan = services.GetRequiredService<SourceScanService>();
        var drafts = services.GetRequiredService<DraftService>();
        var plans = services.GetRequiredService<CommentPlanService>();

        string? taskId = null;
        string? slotId = null;
        string? planHash = null;
        string? before = null;
        var probeSource = Path.Combine(dataRoot, "Probe.Api", "Controllers", "ProbeController.cs");

        // 第一段：扫描。真的加载项目、发现接口、产出注释位置清单。
        RunAction("回写链路：源码扫描产出注释缺口（真实服务）", () =>
        {
            var profile = profiles.Save(new ProjectProfile
            {
                Name = "回写配置",
                WorkspaceRoot = dataRoot,
                ApiProject = "Probe.Api/Probe.Api.csproj",
                AllowedSourcePaths = ["Probe.Api/"],
            }, isNew: true);
            var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "回写任务" });
            taskId = task.Id;
            var job = new JobContext(new JobInfo { Id = "job-smoke-scan", Scope = task.Id, Kind = "scan", StartedAt = DateTimeOffset.Now });
            Task.Run(() => scan.ScanAsync(task.Id, job)).GetAwaiter().GetResult();

            var output = tasks.Read<ScanOutput>(task.Id, TaskFiles.Symbols)
                ?? throw new InvalidOperationException("扫描后没有 symbols.json。");
            if (output.Endpoints.Count == 0) throw new InvalidOperationException("扫描没有发现任何接口。");
            if (output.Slots.Count == 0) throw new InvalidOperationException("扫描没有产出任何注释位置。");
            var missing = output.Slots.Where(s => s.Status == "missing" && s.Editable).ToList();
            if (missing.Count == 0) throw new InvalidOperationException("扫描没有产出可回写的缺失注释位置。");
            var summary = missing.FirstOrDefault(s => s.Tag == "summary") ?? missing[0];
            slotId = summary.Id;
            _lines.Add($"     扫描：{output.Endpoints.Count} 个接口、{output.Slots.Count} 个注释位置，其中缺失且可回写 {missing.Count} 个；选中「{summary.Display}」");
        });

        // 第二段：手工候选 → 接受。走 DraftService 的既有校验（占位文字、待确认问题、覆盖已有注释）。
        RunAction("回写链路：手工候选接受（真实服务）", () =>
        {
            if (taskId is null || slotId is null) throw new InvalidOperationException("上一段没有产出任务或注释位置。");
            var candidate = Task.Run(() => drafts.AddManualAsync(
                taskId,
                new ManualCandidateRequest { SlotId = slotId, Text = "按名称查询探针数据。" },
                CancellationToken.None)).GetAwaiter().GetResult();
            var accepted = Task.Run(() => drafts.UpdateAsync(
                taskId,
                candidate.Id,
                new CandidateUpdateRequest { ReviewState = "accepted" },
                CancellationToken.None)).GetAwaiter().GetResult();
            if (accepted.ReviewState != "accepted") throw new InvalidOperationException($"候选审阅状态是「{accepted.ReviewState}」。");
            _lines.Add($"     候选已接受：{accepted.Id}，文字「{accepted.Text}」");
        });

        // 第三段：生成计划，并断言两次生成的哈希一致（计划哈希是回写的确认凭据）。
        RunAction("回写链路：生成注释计划且哈希稳定（真实服务）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务。");
            var first = Task.Run(() => plans.CreateAsync(taskId, CancellationToken.None)).GetAwaiter().GetResult();
            if (first.Files.Count == 0)
            {
                throw new InvalidOperationException("计划里没有可回写的文件：" + string.Join("；", first.Skipped.Select(s => s.Reason)));
            }
            if (!first.Files.Any(f => f.WriteAllowed)) throw new InvalidOperationException("计划里没有允许回写的文件。");
            if (first.Hash.Length == 0) throw new InvalidOperationException("计划哈希为空。");
            var second = Task.Run(() => plans.CreateAsync(taskId, CancellationToken.None)).GetAwaiter().GetResult();
            if (second.Hash != first.Hash) throw new InvalidOperationException($"两次生成的计划哈希不一致：{first.Hash} / {second.Hash}。");
            planHash = first.Hash;
            before = File.ReadAllText(probeSource);
            _lines.Add($"     计划 {first.Id}：{first.Files.Count} 个文件、{first.Files.Sum(f => f.Edits.Count)} 处注释，哈希 {first.Hash[..12]}…（两次一致）");
        });

        // 第四段（反向）：计划哈希不符 → 拒绝写入、磁盘一字未改。
        RunAction("回写链路：计划哈希不符时拒绝写入（真实服务）", () =>
        {
            if (taskId is null || before is null) throw new InvalidOperationException("上一段没有产出任务或计划。");
            var rejected = false;
            try
            {
                Task.Run(() => plans.ApplyAsync(taskId, "0000000000000000", CancellationToken.None)).GetAwaiter().GetResult();
            }
            catch (UserException ex)
            {
                rejected = true;
                if (ex.StatusCode != 409) throw new InvalidOperationException($"拒绝回写的状态码是 {ex.StatusCode}，期望 409。");
                _lines.Add($"     拒绝文案：{ex.Message}");
            }
            if (!rejected) throw new InvalidOperationException("计划哈希不符却回写成功。");
            if (File.ReadAllText(probeSource) != before) throw new InvalidOperationException("被拒绝的回写改动了源码。");
        });

        // 第五段：确认后回写落盘。只改 /// 行、BOM 与换行符不变、执行代码一字未改。
        RunAction("回写链路：确认后回写落盘且只改 /// 行（真实服务）", () =>
        {
            if (taskId is null || planHash is null || before is null) throw new InvalidOperationException("上一段没有产出任务或计划。");
            var applied = Task.Run(() => plans.ApplyAsync(taskId, planHash, CancellationToken.None)).GetAwaiter().GetResult();
            if (applied.Status != "applied")
            {
                throw new InvalidOperationException($"计划状态是「{applied.Status}」：" + string.Join("；", applied.ApplyResults.Select(r => r.State + " " + r.Message)));
            }
            var after = File.ReadAllText(probeSource);
            if (after == before) throw new InvalidOperationException("回写后源码内容没有变化。");
            AssertOnlyDocLinesChanged(before, after);
            AssertBomAndEolPreserved(probeSource);
            if (!after.Contains("/// 按名称查询探针数据。", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("回写后的源码里找不到候选文字。");
            }
            _lines.Add($"     回写结果：{applied.ApplyResults.Count(r => r.State == "applied")} 个文件已写入，/// 行新增 {DocLineCount(after) - DocLineCount(before)} 行，BOM 与 CRLF 保留");
        });

        // 第六段：真实对话框驱动。构造 ApplyPlanDialog → 等它自己生成计划 → 点「确认回写」→ 断言源码被改。
        RunAction("回写链路：真实对话框点「确认回写」落盘（真实控件）", () =>
        {
            var secondDir = Path.Combine(dataRoot, "Probe2.Api");
            CreateProbeWorkspace(secondDir);
            var profile = profiles.Save(new ProjectProfile
            {
                Name = "回写对话框配置",
                WorkspaceRoot = dataRoot,
                ApiProject = "Probe2.Api/Probe2.Api.csproj",
                AllowedSourcePaths = ["Probe2.Api/"],
            }, isNew: true);
            var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "回写对话框任务" });
            var job = new JobContext(new JobInfo { Id = "job-smoke-scan-2", Scope = task.Id, Kind = "scan", StartedAt = DateTimeOffset.Now });
            Task.Run(() => scan.ScanAsync(task.Id, job)).GetAwaiter().GetResult();

            var output = tasks.Read<ScanOutput>(task.Id, TaskFiles.Symbols)
                ?? throw new InvalidOperationException("扫描后没有 symbols.json。");
            var slot = output.Slots.FirstOrDefault(s => s.Status == "missing" && s.Editable && s.Tag == "summary")
                ?? throw new InvalidOperationException("扫描没有产出可回写的 summary 缺口。");
            var candidate = Task.Run(() => drafts.AddManualAsync(
                task.Id,
                new ManualCandidateRequest { SlotId = slot.Id, Text = "按名称查询第二份探针数据。" },
                CancellationToken.None)).GetAwaiter().GetResult();
            Task.Run(() => drafts.UpdateAsync(
                task.Id,
                candidate.Id,
                new CandidateUpdateRequest { ReviewState = "accepted" },
                CancellationToken.None)).GetAwaiter().GetResult();

            var source = Path.Combine(secondDir, "Controllers", "ProbeController.cs");
            var beforeText = File.ReadAllText(source);

            using var dialog = new ApplyPlanDialog(plans, tasks, tasks.Get(task.Id));
            var confirmed = 0;
            // 确认框是模态的，无人值守点不动：换成替身，但按钮与回写链路一字不改。
            dialog.ConfirmApply = _ =>
            {
                confirmed++;
                return true;
            };
            dialog.Show();
            PumpUntil(dialog.PendingLoad);
            Application.DoEvents();

            var plan = dialog.Plan;
            if (plan is null)
            {
                throw new InvalidOperationException("对话框没有拿到注释计划。界面文案：" + LabelTexts(dialog));
            }
            if (plan.Files.Count == 0) throw new InvalidOperationException("对话框里的计划没有可回写的文件。");

            Click(dialog, "确认回写");
            PumpUntil(dialog.PendingApply);
            Application.DoEvents();

            if (confirmed != 1) throw new InvalidOperationException($"确认委托被调用 {confirmed} 次，期望 1 次。");
            var afterText = File.ReadAllText(source);
            if (afterText == beforeText) throw new InvalidOperationException("点「确认回写」后源码没有变化。");
            AssertOnlyDocLinesChanged(beforeText, afterText);
            AssertBomAndEolPreserved(source);
            var status = FindAll<Label>(dialog).Select(l => l.Text)
                .FirstOrDefault(t => t.Contains("已回写", StringComparison.Ordinal));
            if (status is null) throw new InvalidOperationException("状态行没有出现「已回写」的结果文案。界面文案：" + LabelTexts(dialog));
            _lines.Add($"     对话框状态行：{status}");
        });
    }

    /// <summary>
    /// 注释缺口对话框的真实控件交互（第一百三十六轮）：打开 → 计数行与覆盖率行 → 打开即选中第一条 →
    /// 上一条/下一条 → 关键词筛到零行与清空 → 状态筛选。
    /// <para>
    /// 存在理由：第一百二十九轮起这个对话框只被「构造一次」（<c>Cases</c> 里 <c>new</c> + 建句柄），
    /// 而它是整条审阅链路的入口——计数行、覆盖率行、打开即选中第一条、上一条/下一条、关键词与状态筛选
    /// 都是「构造成功」看不出来的行为；真机清单第 5 项（查看缺口对话框可读）此前只有「构造不抛异常」这一层证据。
    /// </para>
    /// <para>
    /// 全部走真实 WinForms 控件：<c>Show()</c> 触发 <c>OnLoad</c> 读盘、<c>Button.PerformClick()</c> 走点击链路、
    /// 关键词真的敲进 <c>TextBox</c>、状态筛选真的改 <c>ComboBox.SelectedIndex</c>；
    /// 断言读的是控件上的文字与清单行数，不是服务层返回值。
    /// </para>
    /// </summary>
    private static void RunScanResultsInteraction(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var scan = services.GetRequiredService<SourceScanService>();
        var drafts = services.GetRequiredService<DraftService>();

        ScanOutput? output = null;
        SourceManifest? manifest = null;
        CheckTask? task = null;
        ScanResultsDialog? dialog = null;
        try
        {
            // 第一段：造一份真实扫描产物（Web SDK 探针项目 + 缺 /// 的控制器）。
            RunAction("缺口对话框：准备扫描产物（真实服务）", () =>
            {
                var projectDir = Path.Combine(dataRoot, "Probe3.Api");
                CreateProbeWorkspace(projectDir);
                var profile = profiles.Save(new ProjectProfile
                {
                    Name = "缺口对话框配置",
                    WorkspaceRoot = dataRoot,
                    ApiProject = "Probe3.Api/Probe3.Api.csproj",
                    AllowedSourcePaths = ["Probe3.Api/"],
                }, isNew: true);
                task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "缺口对话框任务" });
                var job = new JobContext(new JobInfo { Id = "job-smoke-scan-3", Scope = task.Id, Kind = "scan", StartedAt = DateTimeOffset.Now });
                Task.Run(() => scan.ScanAsync(task.Id, job)).GetAwaiter().GetResult();
                output = tasks.Read<ScanOutput>(task.Id, TaskFiles.Symbols)
                    ?? throw new InvalidOperationException("扫描后没有 symbols.json。");
                manifest = tasks.Read<SourceManifest>(task.Id, TaskFiles.Manifest);
                if (output.Slots.Count < 2)
                {
                    throw new InvalidOperationException($"扫描只产出 {output.Slots.Count} 个注释位置，不足以验证上一条/下一条。");
                }
                _lines.Add($"     扫描产物：{output.Slots.Count} 个注释位置，缺失 {output.Slots.Count(s => s.Status == "missing")} 个");
            });

            // 第二段：打开对话框（Show 触发 OnLoad 读盘），核对计数行、覆盖率行、清单行数与打开时的选中落点。
            RunAction("缺口对话框：打开即选中第一条且计数与覆盖率对得上（真实控件）", () =>
            {
                if (task is null || output is null) throw new InvalidOperationException("上一段没有产出扫描结果。");
                dialog = new ScanResultsDialog(tasks, drafts, task);
                dialog.Show();
                Application.DoEvents();

                var counts = CountsLabel(dialog);
                var expectedCounts = SlotText.CountsLine(output.Slots);
                if (counts.Text != expectedCounts)
                {
                    throw new InvalidOperationException($"计数行是「{counts.Text}」，期望「{expectedCounts}」。");
                }
                var coverage = CoverageLabel(dialog);
                var expectedCoverage = ScanOverview.CoverageText(
                    output.Coverage.Valid, output.Coverage.InheritDoc, output.Coverage.Total,
                    manifest?.AnalysisLevel, manifest?.ScannedAt);
                if (coverage.Text != expectedCoverage)
                {
                    throw new InvalidOperationException($"覆盖率行是「{coverage.Text}」，期望「{expectedCoverage}」。");
                }
                var list = SlotList(dialog);
                if (!list.Visible) throw new InvalidOperationException("清单不可见（对话框停在提示态）。");
                if (list.Items.Count != output.Slots.Count)
                {
                    throw new InvalidOperationException($"清单有 {list.Items.Count} 行，扫描产出 {output.Slots.Count} 个位置。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException(
                        $"打开后选中 {list.SelectedIndices.Count} 行（首行序号 {list.SelectedIndices.Cast<int>().FirstOrDefault()}），期望恰好第一行。");
                }
                var detail = DetailBox(dialog);
                if (!detail.Text.Contains("符号：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("详情区没有显示第一条的符号行：" + detail.Text);
                }
                // 降级入口按可见条目计数（第二十轮口径）：有降级才显示，标题数字与条目数一致。
                var degradations = ScanOverview.DegradationItems(manifest?.Warnings, output.UnresolvedSymbols, output.ExternalTypes);
                var warnings = ButtonOf(dialog, ScanOverview.Headline(degradations.Count));
                if (warnings.Visible != degradations.Count > 0)
                {
                    throw new InvalidOperationException($"降级入口可见性是 {warnings.Visible}，降级条目 {degradations.Count} 条。");
                }
                _lines.Add($"     计数行：{counts.Text}");
                _lines.Add($"     覆盖率行：{coverage.Text}");
            });

            // 第三段：上一条/下一条（真实按钮）：到头不绕回、位置指示与 ListAdvance 口径一致。
            RunAction("缺口对话框：上一条/下一条按钮逐条走（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var list = SlotList(dialog);
                var prev = ButtonOf(dialog, ListAdvance.PrevLabel);
                var next = ButtonOf(dialog, ListAdvance.NextLabel);
                var navText = NavLabel(dialog);
                if (prev.Enabled) throw new InvalidOperationException("停在第一条时「上一条」仍可点。");
                if (!next.Enabled) throw new InvalidOperationException($"清单有 {list.Items.Count} 行，「下一条」却不可点。");
                if (navText.Text != ListAdvance.PositionText(0, list.Items.Count))
                {
                    throw new InvalidOperationException($"位置指示是「{navText.Text}」，期望「{ListAdvance.PositionText(0, list.Items.Count)}」。");
                }
                next.PerformClick();
                Application.DoEvents();
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 1)
                {
                    throw new InvalidOperationException(
                        $"点「下一条」后选中第 {list.SelectedIndices.Cast<int>().FirstOrDefault()} 行，期望第 1 行（0 基）。");
                }
                if (!prev.Enabled) throw new InvalidOperationException("走到第二条后「上一条」仍不可点。");
                if (navText.Text != ListAdvance.PositionText(1, list.Items.Count))
                {
                    throw new InvalidOperationException($"位置指示是「{navText.Text}」，期望「{ListAdvance.PositionText(1, list.Items.Count)}」。");
                }
                prev.PerformClick();
                Application.DoEvents();
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException("点「上一条」后没有回到第一行。");
                }
                // 走到末条：「下一条」按不动（到头不绕回）。
                var steps = 0;
                while (next.Enabled)
                {
                    next.PerformClick();
                    Application.DoEvents();
                    if (++steps > list.Items.Count) throw new InvalidOperationException("「下一条」没有在末条停下（可能绕回了队首）。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != list.Items.Count - 1)
                {
                    throw new InvalidOperationException(
                        $"走到末条后选中第 {list.SelectedIndices.Cast<int>().FirstOrDefault()} 行，期望第 {list.Items.Count - 1} 行。");
                }
                // 回到第一条，后续段从已知状态开始。
                while (prev.Enabled)
                {
                    prev.PerformClick();
                    Application.DoEvents();
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException("连点「上一条」后没有回到第一行。");
                }
                _lines.Add($"     逐条走：{list.Items.Count} 行，末条「下一条」按不动，位置指示「{navText.Text}」");
            });

            // 第四段：关键词筛到零行（提示语与筛选器可用性）与清空恢复。
            RunAction("缺口对话框：关键词筛到零行与清空恢复（真实控件）", () =>
            {
                if (dialog is null || output is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var list = SlotList(dialog);
                var keyword = KeywordBox(dialog);
                var filter = StatusFilter(dialog);
                var total = list.Items.Count;
                keyword.Text = "zzz-没有这个位置";
                Application.DoEvents();
                if (list.Items.Count != 0) throw new InvalidOperationException($"关键词不命中时清单还有 {list.Items.Count} 行。");
                if (!filter.Enabled) throw new InvalidOperationException("筛到零行时状态筛选被禁用（那句话指名的下一步按不动）。");
                if (!keyword.Enabled) throw new InvalidOperationException("筛到零行时关键词框被禁用。");
                var hint = HintLabel(dialog);
                var expectedHint = ListFilter.FilteredEmptyHint(ListFilter.SlotNoun, ListFilter.IsActive(keyword.Text));
                if (!hint.Visible || hint.Text != expectedHint)
                {
                    throw new InvalidOperationException(
                        $"筛到零行的提示是「{(hint.Visible ? hint.Text : "（不可见）")}」，期望「{expectedHint}」。");
                }
                // 计数行讲的是整份扫描，不因筛选变化（与 ListFilter.IsFilteredEmpty 同一取舍）。
                if (CountsLabel(dialog).Text != SlotText.CountsLine(output.Slots))
                {
                    throw new InvalidOperationException("筛到零行后计数行变了：" + CountsLabel(dialog).Text);
                }
                keyword.Text = "";
                Application.DoEvents();
                if (list.Items.Count != total) throw new InvalidOperationException($"清空关键词后清单 {list.Items.Count} 行，期望 {total} 行。");
                if (hint.Visible) throw new InvalidOperationException("清空关键词后提示仍然可见。");
                _lines.Add($"     关键词筛到零行：提示「{expectedHint}」，清空后回到 {total} 行");
            });

            // 第五段：状态筛选（真实下拉）：选「缺失」后行数等于扫描里的缺失数，计数行不随筛选变化。
            RunAction("缺口对话框：状态筛选按缺失计数（真实控件）", () =>
            {
                if (dialog is null || output is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var list = SlotList(dialog);
                var filter = StatusFilter(dialog);
                var missing = output.Slots.Count(s => s.Status == "missing");
                // 下拉项与对话框 FilterChoices 同序：0 全部位置、1 缺失。
                filter.SelectedIndex = 1;
                Application.DoEvents();
                if (list.Items.Count != missing)
                {
                    throw new InvalidOperationException($"选「缺失」后清单 {list.Items.Count} 行，扫描里缺失 {missing} 个。");
                }
                if (CountsLabel(dialog).Text != SlotText.CountsLine(output.Slots))
                {
                    throw new InvalidOperationException("状态筛选后计数行变了：" + CountsLabel(dialog).Text);
                }
                filter.SelectedIndex = 0;
                Application.DoEvents();
                if (list.Items.Count != output.Slots.Count)
                {
                    throw new InvalidOperationException($"回到「全部位置」后清单 {list.Items.Count} 行，期望 {output.Slots.Count} 行。");
                }
                _lines.Add($"     状态筛选：缺失 {missing} 行，回到全部 {output.Slots.Count} 行");
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 字体栈的运行时命中（第一百三十九轮，控制仓简报 P0-1 的降级手段）。
    /// <para>
    /// 存在理由：第一百三十五轮把字体栈的有序回退从「已记录未实现」变成真实现，但证据只到
    /// 「<c>FontStack.Resolve</c> 的判定真值表」与「两处入口都调了它」——**本机实际回退到哪个字体**
    /// 从未在运行时看过一眼。承诺（DESIGN §3「Segoe UI → Microsoft YaHei UI → Microsoft YaHei → SimHei，
    /// 找到即停」）与「这台机器上真的命中了哪一项」之间差的就是这一段。
    /// </para>
    /// <para>
    /// 断言五件事：① 回退结果等于「按栈序找到的第一个已装字体」（或系统无衬线兜底），不是别的名字；
    /// ② 回退结果在本机真的装得上（探测失败的名字不会被当成结果）；③ 界面控件工厂、进程默认字体
    /// （<c>Control.DefaultFont</c>，决定 MessageBox 与菜单）与真实对话框上的控件取的是**同一份实际渲染名**、
    /// 同一档磅值；④ 等宽栈同一份判定；⑤ 真实对话框（<c>NoticeDialog</c>）与它里面的标签也取同一份。
    /// </para>
    /// <para>
    /// **比的是「实际渲染名」（<c>Font.Name</c>），不是「请求的名字」。** 第一百四十轮 wine 实测把这条
    /// 断言打红过一次：请求 <c>Segoe UI</c>、<c>Font.Name</c> 却是 <c>Noto Sans CJK SC</c>——GDI+ 在
    /// 缺这个字体的机器上做字体替换，请求名与渲染名本来就可以不同（Windows 上装了 Segoe UI 时两者相同）。
    /// 产品要保证的是「三处取同一份」，不是「渲染名等于请求名」；把后者写成断言，等于要求运行时不替换字体，
    /// 那不是 DESIGN §3 的承诺。替换事实作为证据行如实打印，不判失败。
    /// </para>
    /// <para>
    /// 它**不能替代真机观感复核**：wine 的字体度量与 GDI 绘制不是 Windows 实现，文字截断、
    /// 字距这类观感问题只有真机截图能判（真机清单第 17 项继续留空）。
    /// </para>
    /// </summary>
    private static void RunFontStackRuntime()
    {
        RunAction("字体栈：回退结果在运行时命中且界面与进程默认字体一致（真实控件）", () =>
        {
            static bool Installed(string name)
            {
                try
                {
                    using var probe = new FontFamily(name);
                    return true;
                }
                catch (ArgumentException)
                {
                    return false;
                }
            }

            var resolved = UiTokens.ResolveFontFamily();
            if (string.IsNullOrWhiteSpace(resolved))
            {
                throw new InvalidOperationException("回退结果是空字体名。");
            }
            // ① 回退结果必须是「按栈序找到的第一个已装字体」，或者是系统无衬线兜底——不是别的名字。
            var expected = FontStack.Resolve(FontStack.Families, Installed, FontFamily.GenericSansSerif.Name);
            if (resolved != expected)
            {
                throw new InvalidOperationException($"回退结果是「{resolved}」，按栈序应为「{expected}」（找到即停）。");
            }
            // ② 回退结果在本机真的装得上：探测失败的名字被当成结果，界面上会静默落到系统默认。
            if (!Installed(resolved))
            {
                throw new InvalidOperationException($"回退结果「{resolved}」在本机并未安装。");
            }
            // ③ 界面控件工厂、进程默认字体、真实对话框取同一份「实际渲染名」。
            //    第一百三十五轮的缺陷正是两处各写一份（一处雅黑、一处系统默认），同一只壳里两种字。
            using var uiFont = UiTokens.Create(UiTokens.BodyPt);
            var rendered = uiFont.Name;
            if (string.IsNullOrWhiteSpace(rendered))
            {
                throw new InvalidOperationException("界面字体工厂产出的字体没有名字。");
            }
            using var label = new Label { Font = UiTokens.Body.Value };
            if (label.Font.Name != rendered)
            {
                throw new InvalidOperationException($"界面控件字体是「{label.Font.Name}」，字体工厂产出的是「{rendered}」。");
            }
            if (Control.DefaultFont.Name != rendered)
            {
                throw new InvalidOperationException(
                    $"进程默认字体是「{Control.DefaultFont.Name}」，界面字体是「{rendered}」（MessageBox 与菜单会与界面不同字）。");
            }
            if (Math.Abs(Control.DefaultFont.Size - UiTokens.CaptionPt) > 0.01f)
            {
                throw new InvalidOperationException(
                    $"进程默认字体是 {Control.DefaultFont.Size}pt，期望 Caption 档 {UiTokens.CaptionPt}pt。");
            }
            // ④ 等宽栈同一份判定（计数、进度、ID、路径、差异文本必须等宽）。
            //    兜底是系统等宽、不是系统无衬线：DESIGN §3 的等宽栈以「→ monospace」收尾，
            //    两条栈兜到同一个名字时，缺等宽字体的机器上计数行与差异文本会渲染成比例字体。
            var monoExpected = FontStack.Resolve(FontStack.MonoFamilies, Installed, FontFamily.GenericMonospace.Name);
            if (UiTokens.Mono.Value.FontFamily.Name != monoExpected)
            {
                throw new InvalidOperationException(
                    $"等宽字体是「{UiTokens.Mono.Value.FontFamily.Name}」，按栈序应为「{monoExpected}」。");
            }
            // 等宽承诺在界面上成立：等宽那一份的实际渲染名与正文那一份不同（同一个名字就是没等宽）。
            var monoRendered = UiTokens.Mono.Value.Name;
            if (monoRendered == rendered)
            {
                throw new InvalidOperationException(
                    $"等宽字体与正文字体的实际渲染名都是「{rendered}」（计数行、路径与差异文本不会等宽）。");
            }
            // ⑤ 真实对话框上的控件也取同一份（不是只有工厂里那个 Label 对）。
            using var dialog = new NoticeDialog("关于 Apifox 助手", "字体栈运行时核对用的提示正文。", "等宽区示例", "附注示例");
            _ = dialog.Handle;
            dialog.CreateControl();
            if (dialog.Font.Name != rendered)
            {
                throw new InvalidOperationException($"对话框字体是「{dialog.Font.Name}」，界面字体是「{rendered}」。");
            }
            var nested = FindAll<Label>(dialog).FirstOrDefault(l => l.Text.Length > 0);
            if (nested is not null && nested.Font.Name != rendered)
            {
                throw new InvalidOperationException($"对话框里的标签字体是「{nested.Font.Name}」，界面字体是「{rendered}」。");
            }
            _lines.Add($"     回退结果：{resolved}（栈序 {string.Join(" → ", FontStack.Families)}）；实际渲染：{rendered}；"
                + $"等宽请求 {UiTokens.Mono.Value.FontFamily.Name} / 渲染 {monoRendered}");
            if (rendered != resolved)
            {
                _lines.Add($"     本机对「{resolved}」做了字体替换，渲染成「{rendered}」——运行时替换，不是产品缺陷；"
                    + "界面控件、进程默认字体与对话框取的是同一份。");
            }
        });
    }

    /// <summary>
    /// 注释候选对话框的真实控件交互（第一百三十九轮）：打开 → 计数行 → 打开即选中第一条 →
    /// 待确认问题未回答时接受被拒 → 填回答后接受并下一条 → 筛到零行时回答框与覆盖勾选一起收掉 → 撤销接受。
    /// <para>
    /// 存在理由：这个对话框是真机清单第 6/7/8 项（规则提取候选 → 查看注释候选 → 接受候选）的落点，
    /// 而它此前只被 <c>Cases</c> 里 <c>new</c> 一次 + 建句柄——计数行、打开即选中第一条、
    /// 「接受并下一条」的落点、待确认问题的拒绝路径、提示态收掉回答框与覆盖勾选（第一百三十八轮的修法）、
    /// 撤销接受，全都可以是坏的而冒烟照样 PASS。
    /// </para>
    /// <para>
    /// 全部走真实 WinForms 控件：<c>Show()</c> 触发 <c>OnLoad</c> 读盘、<c>Button.PerformClick()</c> 走点击链路、
    /// 回答真的敲进 <c>TextBox</c>、关键词真的敲进筛选框；断言读的是控件上的文字与清单行数，
    /// 以及 <c>DraftService.Load</c> 从磁盘重读的审阅状态（不是内存里的那一份）。
    /// 候选由真实服务产出：扫描探针项目后经 <c>DraftService.ImportResultAsync</c> 导入两条
    /// （第一条带待确认问题，第二条没有），不手写 comment-drafts.json。
    /// </para>
    /// </summary>
    private static void RunCandidatesInteraction(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var scan = services.GetRequiredService<SourceScanService>();
        var drafts = services.GetRequiredService<DraftService>();

        CheckTask? task = null;
        string? questionCandidateId = null;
        CandidatesDialog? dialog = null;
        try
        {
            // 第一段：造候选（真实服务）——扫描探针项目，再导入两条编码助手结果。
            RunAction("候选对话框：准备候选（真实服务）", () =>
            {
                var projectDir = Path.Combine(dataRoot, "Probe4.Api");
                CreateProbeWorkspace(projectDir);
                var profile = profiles.Save(new ProjectProfile
                {
                    Name = "候选对话框配置",
                    WorkspaceRoot = dataRoot,
                    ApiProject = "Probe4.Api/Probe4.Api.csproj",
                    AllowedSourcePaths = ["Probe4.Api/"],
                }, isNew: true);
                task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "候选对话框任务" });
                var job = new JobContext(new JobInfo { Id = "job-smoke-scan-4", Scope = task.Id, Kind = "scan", StartedAt = DateTimeOffset.Now });
                Task.Run(() => scan.ScanAsync(task.Id, job)).GetAwaiter().GetResult();

                var output = tasks.Read<ScanOutput>(task.Id, TaskFiles.Symbols)
                    ?? throw new InvalidOperationException("扫描后没有 symbols.json。");
                var slots = output.Slots.Where(s => s.Status == "missing" && s.Editable).Take(2).ToList();
                if (slots.Count < 2)
                {
                    throw new InvalidOperationException($"可回写的缺失位置只有 {slots.Count} 个，不足以验证两条候选。");
                }
                // 导入编码助手结果（真实服务）：第一条带待确认问题（接受前必须回答），第二条没有。
                var json = "{\"format\":\"" + DraftService.ResultFormat + "\",\"candidates\":["
                    + "{\"slotId\":\"" + slots[0].Id + "\",\"text\":\"按名称查询探针数据。\",\"openQuestions\":[\"返回数据的业务含义需补充。\"]},"
                    + "{\"slotId\":\"" + slots[1].Id + "\",\"text\":\"探针名称。\"}]}";
                var result = Task.Run(() => drafts.ImportResultAsync(task.Id, json, CancellationToken.None)).GetAwaiter().GetResult();
                if (result.Imported != 2)
                {
                    throw new InvalidOperationException($"导入 {result.Imported} 条候选，期望 2 条：" + string.Join("；", result.Errors));
                }
                var store = drafts.Load(task.Id);
                if (store.Candidates.Count != 2)
                {
                    throw new InvalidOperationException($"候选队列有 {store.Candidates.Count} 条，期望 2 条。");
                }
                if (store.Candidates[0].OpenQuestions.Count == 0)
                {
                    throw new InvalidOperationException("第一条候选没有待确认问题（「接受被拒」那一段就没有前提）。");
                }
                questionCandidateId = store.Candidates[0].Id;
                _lines.Add($"     候选队列：{store.Candidates.Count} 条，第一条带 {store.Candidates[0].OpenQuestions.Count} 个待确认问题");
            });

            // 第二段：打开对话框（Show 触发 OnLoad 读盘），核对计数行、清单行数、打开时的选中落点与两格输入。
            RunAction("候选对话框：打开即选中第一条且计数对得上（真实控件）", () =>
            {
                if (task is null) throw new InvalidOperationException("上一段没有产出任务。");
                var store = drafts.Load(task.Id);
                dialog = new CandidatesDialog(drafts, tasks, task);
                dialog.Show();
                Application.DoEvents();

                var counts = CandidateCountsLabel(dialog);
                var expectedCounts = QueueText.CandidateCountsLine(store.Candidates);
                if (counts.Text != expectedCounts)
                {
                    throw new InvalidOperationException($"计数行是「{counts.Text}」，期望「{expectedCounts}」。");
                }
                var list = CandidateList(dialog);
                if (!list.Visible) throw new InvalidOperationException("候选清单不可见（对话框停在提示态）。");
                if (list.Items.Count != store.Candidates.Count)
                {
                    throw new InvalidOperationException($"清单有 {list.Items.Count} 行，候选队列 {store.Candidates.Count} 条。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException(
                        $"打开后选中 {list.SelectedIndices.Count} 行（首行序号 {list.SelectedIndices.Cast<int>().FirstOrDefault()}），期望恰好第一行。");
                }
                var detail = DetailBox(dialog);
                if (!detail.Text.Contains("符号：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("详情区没有显示第一条的符号行：" + detail.Text);
                }
                var boxes = EditableBoxes(dialog);
                if (boxes.Length != 2)
                {
                    throw new InvalidOperationException($"可编辑的多行输入框有 {boxes.Length} 个，期望 2 个（候选文字 + 待确认问题的回答）。");
                }
                if (!boxes[0].Enabled || boxes[0].Text != store.Candidates[0].Text)
                {
                    throw new InvalidOperationException($"候选文字那一格是「{boxes[0].Text}」（启用 {boxes[0].Enabled}），期望第一条候选的文字。");
                }
                if (!boxes[1].Enabled)
                {
                    throw new InvalidOperationException("第一条候选有待确认问题，回答框却不可填。");
                }
                var answersCaption = CaptionOf(dialog, "待确认问题的回答");
                if (answersCaption.Text != CandidateEdit.AnswersCaptionFor(store.Candidates[0]))
                {
                    throw new InvalidOperationException(
                        $"回答框说明是「{answersCaption.Text}」，期望「{CandidateEdit.AnswersCaptionFor(store.Candidates[0])}」。");
                }
                if (!ButtonOf(dialog, "接受并下一条").Enabled)
                {
                    throw new InvalidOperationException("打开后「接受并下一条」不可点。");
                }
                _lines.Add($"     计数行：{counts.Text}");
            });

            // 第三段（反向）：待确认问题没回答就点「接受并下一条」→ 服务拒绝、状态行如实、磁盘状态不变。
            RunAction("候选对话框：待确认问题未回答时接受被拒（真实控件）", () =>
            {
                if (dialog is null || task is null || questionCandidateId is null)
                {
                    throw new InvalidOperationException("上一段没有打开对话框。");
                }
                var status = StatusLabel(dialog);
                Click(dialog, "接受并下一条");
                PumpUntilCondition(() => status.Text.Length > 0, "接受被拒的状态行");
                if (!status.Text.Contains("请先填写确认结果再接受", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「该候选有待确认的问题，请先填写确认结果再接受。」。");
                }
                var store = drafts.Load(task.Id);
                var candidate = store.Candidates.First(c => c.Id == questionCandidateId);
                if (candidate.ReviewState != "pending")
                {
                    throw new InvalidOperationException($"被拒后磁盘上的审阅状态是「{candidate.ReviewState}」，期望 pending。");
                }
                var list = CandidateList(dialog);
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException("被拒后选中没有留在原处（被拒不该把用户甩到别的候选上）。");
                }
                _lines.Add($"     拒绝文案：{status.Text}");
            });

            // 第四段：填回答后接受 → 状态行取共享口径、磁盘状态 accepted、选中交给下一条。
            RunAction("候选对话框：填回答后接受并下一条（真实控件）", () =>
            {
                if (dialog is null || task is null || questionCandidateId is null)
                {
                    throw new InvalidOperationException("上一段没有打开对话框。");
                }
                var boxes = EditableBoxes(dialog);
                boxes[1].Text = "返回列表按名称过滤。";
                Application.DoEvents();
                var status = StatusLabel(dialog);
                // 清掉上一段的拒绝文案：这一段要等的是「接受完成」那一句，不是「状态行非空」。
                status.Text = "";
                Click(dialog, "接受并下一条");
                PumpUntilCondition(() => status.Text.Length > 0, "接受完成的状态行");

                var store = drafts.Load(task.Id);
                var candidate = store.Candidates.First(c => c.Id == questionCandidateId);
                if (candidate.ReviewState != "accepted")
                {
                    throw new InvalidOperationException($"磁盘上的审阅状态是「{candidate.ReviewState}」，期望 accepted。");
                }
                if (candidate.Answers != "返回列表按名称过滤。")
                {
                    throw new InvalidOperationException($"磁盘上的回答是「{candidate.Answers}」，期望刚填的那一句。");
                }
                var expectedStatus = CandidateEdit.ReviewDoneText("accepted", store.Candidates.Count(c => c.ReviewState == "pending"));
                if (status.Text != expectedStatus)
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「{expectedStatus}」。");
                }
                var list = CandidateList(dialog);
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 1)
                {
                    throw new InvalidOperationException(
                        $"接受后选中在第 {list.SelectedIndices.Cast<int>().FirstOrDefault()} 行，期望第 1 行（接受并下一条）。");
                }
                _lines.Add($"     状态行：{status.Text}");
            });

            // 第五段：关键词筛到零行 → 提示语取共享口径、筛选器仍可用、计数行不变，
            // 且第一百三十八轮的修法在真实控件上成立：回答框与覆盖勾选随提示一起收掉。
            RunAction("候选对话框：筛到零行时回答框与覆盖勾选一起收掉（真实控件）", () =>
            {
                if (dialog is null || task is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var store = drafts.Load(task.Id);
                var list = CandidateList(dialog);
                var keyword = KeywordBox(dialog);
                var filter = StatusFilter(dialog);
                var total = list.Items.Count;
                keyword.Text = "zzz-没有这个候选";
                Application.DoEvents();
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"关键词不命中时清单还有 {list.Items.Count} 行。");
                }
                if (!filter.Enabled) throw new InvalidOperationException("筛到零行时筛选下拉被禁用（提示语指名的下一步按不动）。");
                if (!keyword.Enabled) throw new InvalidOperationException("筛到零行时关键词框被禁用。");
                var hint = HintLabel(dialog);
                var expectedHint = ListFilter.FilteredEmptyHint(ListFilter.CandidateNoun, ListFilter.IsActive(keyword.Text));
                if (!hint.Visible || hint.Text != expectedHint)
                {
                    throw new InvalidOperationException(
                        $"筛到零行的提示是「{(hint.Visible ? hint.Text : "（不可见）")}」，期望「{expectedHint}」。");
                }
                if (CandidateCountsLabel(dialog).Text != QueueText.CandidateCountsLine(store.Candidates))
                {
                    throw new InvalidOperationException("筛到零行后计数行变了：" + CandidateCountsLabel(dialog).Text);
                }
                var boxes = EditableBoxes(dialog);
                if (boxes[0].Enabled || boxes[0].Text.Length != 0)
                {
                    throw new InvalidOperationException("提示态下候选文字那一格没有收掉。");
                }
                if (boxes[1].Enabled || boxes[1].Text.Length != 0)
                {
                    throw new InvalidOperationException("提示态下回答框没有收掉（清单已藏起来，用户还能对着看不见的候选填回答）。");
                }
                var overwrite = OverwriteBox(dialog);
                if (overwrite.Enabled || overwrite.Checked)
                {
                    throw new InvalidOperationException("提示态下覆盖勾选没有收掉。");
                }
                if (CaptionOf(dialog, "待确认问题的回答").Text != CandidateEdit.AnswersCaptionNoSelection)
                {
                    throw new InvalidOperationException(
                        $"提示态下回答框说明是「{CaptionOf(dialog, "待确认问题的回答").Text}」，期望「{CandidateEdit.AnswersCaptionNoSelection}」。");
                }
                if (CaptionOf(dialog, "候选文字").Text != CandidateEdit.TextCaptionNoSelection)
                {
                    throw new InvalidOperationException(
                        $"提示态下候选文字说明是「{CaptionOf(dialog, "候选文字").Text}」，期望「{CandidateEdit.TextCaptionNoSelection}」。");
                }
                keyword.Text = "";
                Application.DoEvents();
                if (list.Items.Count != total)
                {
                    throw new InvalidOperationException($"清空关键词后清单 {list.Items.Count} 行，期望 {total} 行。");
                }
                _lines.Add($"     筛到零行：提示「{expectedHint}」，回答框与覆盖勾选已收掉，清空后回到 {total} 行");
            });

            // 第六段：撤销接受 → 状态行取共享口径、磁盘状态回到 pending。
            RunAction("候选对话框：撤销接受回到待审阅（真实控件）", () =>
            {
                if (dialog is null || task is null || questionCandidateId is null)
                {
                    throw new InvalidOperationException("上一段没有打开对话框。");
                }
                var list = CandidateList(dialog);
                var index = -1;
                for (var i = 0; i < list.Items.Count; i++)
                {
                    if (list.Items[i].Tag is CommentCandidate candidate && candidate.Id == questionCandidateId) index = i;
                }
                if (index < 0) throw new InvalidOperationException("清单里找不到刚接受的那条候选。");
                list.Items[index].Selected = true;
                Application.DoEvents();
                var undo = ButtonOf(dialog, "撤销接受");
                if (!undo.Enabled) throw new InvalidOperationException("已接受的候选「撤销接受」却不可点。");
                var status = StatusLabel(dialog);
                status.Text = "";
                undo.PerformClick();
                PumpUntilCondition(() => status.Text.Length > 0, "撤销接受的状态行");
                if (status.Text != CandidateEdit.UndoDoneText)
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「{CandidateEdit.UndoDoneText}」。");
                }
                var store = drafts.Load(task.Id);
                if (store.Candidates.First(c => c.Id == questionCandidateId).ReviewState != "pending")
                {
                    throw new InvalidOperationException("撤销接受后磁盘上的审阅状态没有回到 pending。");
                }
                _lines.Add($"     状态行：{status.Text}");
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 补充说明对话框的真实控件交互（第一百四十五轮）：这一屏此前只被「构造一次」（<c>Cases</c> 里
    /// <c>new</c> + 建句柄），而它是 F10 人工补充说明的唯一入口、也是原生界面里少数几个「写任务数据」的
    /// 交互之一——只构造的话，编辑位对不对、保存按钮点不点得动、保存后磁盘上有没有、版本有没有递增、
    /// 重复保存会不会白写一遍，全都可以是坏的而冒烟照样 PASS。
    /// <para>
    /// 六段：① 准备检查结果（真实落盘 diff.json 夹具）→ ② 打开即选中第一个接口且编辑位与计数对得上 →
    /// ③ 敲接口说明并保存（真实控件）→ ④ 原样重复保存不递增（真实控件）→ ⑤ 勾选「保留现状」后保存
    /// （真实控件）→ ⑥ 关键词筛到零行（真实控件）。断言一律读磁盘产物（<c>supplement.json</c> 与
    /// <c>task.json</c> 的版本号）与控件文字，不读服务层返回值。
    /// </para>
    /// <para>
    /// 检查结果用夹具而不是真实检查：真实检查需要 Apifox CLI 登录态与项目权限，本环境（wine）不具备。
    /// 夹具只提供这一屏真正读的字段（接口身份、目标结构），不冒充「检查跑过」——检查本身的证据在
    /// 只读契约检查的既有记录里。
    /// </para>
    /// </summary>
    private static void RunSupplementsInteraction(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var supplementSaves = services.GetRequiredService<SupplementService>();

        CheckTask? task = null;
        EndpointCheck? first = null;
        SupplementsDialog? dialog = null;
        try
        {
            // 第一段：准备检查结果（真实落盘）——第一个接口带参数/请求体/200 响应（五个编辑位），
            // 第二个接口只有标题与说明（两个编辑位）：编辑位数量按目标结构派生，不是固定五格。
            RunAction("补充说明：准备检查结果（真实落盘）", () =>
            {
                var projectDir = Path.Combine(dataRoot, "Probe5.Api");
                CreateProbeWorkspace(projectDir);
                var profile = profiles.Save(new ProjectProfile
                {
                    Name = "补充说明配置",
                    WorkspaceRoot = dataRoot,
                    ApiProject = "Probe5.Api/Probe5.Api.csproj",
                    AllowedSourcePaths = ["Probe5.Api/"],
                }, isNew: true);
                task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "补充说明任务" });
                var fixture = SupplementFixture();
                tasks.Write(task.Id, TaskFiles.Diff, fixture);
                var reread = tasks.Read<CheckResult>(task.Id, TaskFiles.Diff)
                    ?? throw new InvalidOperationException("diff.json 写进任务目录后读不回来。");
                if (reread.Endpoints.Count != 2)
                {
                    throw new InvalidOperationException($"夹具读回 {reread.Endpoints.Count} 个接口，期望 2 个。");
                }
                first = reread.Endpoints[0];
                var editors = SupplementEntries.EditorsFor(first);
                if (editors.Count != 5)
                {
                    throw new InvalidOperationException($"第一个接口的编辑位有 {editors.Count} 个，期望 5 个（标题/说明/参数/请求体/200 响应）。");
                }
                if (SupplementEntries.EditorsFor(reread.Endpoints[1]).Count != 2)
                {
                    throw new InvalidOperationException("第二个接口（只有标题与说明）的编辑位不是 2 个。");
                }
                _lines.Add($"     夹具：2 个接口，编辑位 {editors.Count} / 2；第一个接口的编辑位：{string.Join("、", editors.Select(e => e.Label))}");
            });

            // 第二段：打开对话框（Show 触发 OnLoad 读盘），核对清单行数、打开时的选中落点、编辑位与计数行。
            RunAction("补充说明：打开即选中第一个接口且编辑位对得上（真实控件）", () =>
            {
                if (task is null || first is null) throw new InvalidOperationException("上一段没有产出任务。");
                dialog = new SupplementsDialog(tasks, task, supplementSaves);
                dialog.Show();
                Application.DoEvents();

                var list = SupplementList(dialog);
                if (!list.Visible) throw new InvalidOperationException("接口清单不可见（对话框停在提示态）。");
                if (list.Items.Count != 2)
                {
                    throw new InvalidOperationException($"清单有 {list.Items.Count} 行，检查结果有 2 个接口。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException(
                        $"打开后选中 {list.SelectedIndices.Count} 行（首行序号 {list.SelectedIndices.Cast<int>().FirstOrDefault()}），期望恰好第一行。");
                }
                var boxes = SupplementEditorBoxes(dialog);
                var expected = SupplementEntries.EditorsFor(first);
                if (boxes.Length != expected.Count)
                {
                    throw new InvalidOperationException($"编辑位输入框有 {boxes.Length} 个，期望 {expected.Count} 个。");
                }
                if (SupplementKeepBoxes(dialog).Length != expected.Count)
                {
                    throw new InvalidOperationException("「保留现状」勾选数与编辑位数不一致。");
                }
                if (boxes[0].Text.Length != 0)
                {
                    throw new InvalidOperationException($"尚未保存过补充，接口标题那一格却有内容「{boxes[0].Text}」。");
                }
                if (!boxes[0].PlaceholderText.StartsWith("当前：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"接口标题那一格的现状提示是「{boxes[0].PlaceholderText}」，期望以「当前：」开头。");
                }
                var counts = SupplementCountsLabel(dialog);
                if (!counts.Text.Contains("共 2 个接口", StringComparison.Ordinal) || !counts.Text.Contains("已保存补充 0 处", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"计数行是「{counts.Text}」，期望「共 2 个接口 · 已保存补充 0 处」。");
                }
                if (!ButtonOf(dialog, "保存本接口补充").Enabled)
                {
                    throw new InvalidOperationException("打开后「保存本接口补充」不可点。");
                }
                if (ButtonOf(dialog, "放弃修改").Enabled)
                {
                    throw new InvalidOperationException("还没有任何输入，「放弃修改」却可点。");
                }
                _lines.Add($"     计数行：{counts.Text}；编辑位：{string.Join("、", expected.Select(e => e.Label))}");
            });

            // 第三段：敲接口说明并保存 → 磁盘上出现条目、任务版本递增、状态行取共享口径、清单计数跟上。
            RunAction("补充说明：敲接口说明并保存（真实控件）", () =>
            {
                if (dialog is null || task is null || first is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var before = tasks.Get(task.Id).Revision;
                var boxes = SupplementEditorBoxes(dialog);
                boxes[1].Text = "查询探针数据，按名称过滤。";
                Application.DoEvents();
                if (!ButtonOf(dialog, "放弃修改").Enabled)
                {
                    throw new InvalidOperationException("敲了字之后「放弃修改」仍不可点（未保存修改没有被识别）。");
                }
                var status = StatusLabel(dialog);
                status.Text = "";
                Click(dialog, "保存本接口补充");
                PumpUntilCondition(() => status.Text.Length > 0, "保存补充的状态行");

                var store = tasks.Read<SupplementStore>(task.Id, TaskFiles.Supplement)
                    ?? throw new InvalidOperationException("保存后任务目录里没有 supplement.json。");
                var entry = store.Entries.FirstOrDefault(e => e.ResourceKey == first.Key && e.Location == "operation" && e.Field == "description");
                if (entry is null)
                {
                    throw new InvalidOperationException("磁盘上的补充里没有这个接口的说明条目。");
                }
                if (entry.Value != "查询探针数据，按名称过滤。")
                {
                    throw new InvalidOperationException($"磁盘上的说明是「{entry.Value}」，期望刚敲的那一句。");
                }
                if (entry.KeepAsIs)
                {
                    throw new InvalidOperationException("填了文字的条目被记成了「保留现状」。");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after <= before)
                {
                    throw new InvalidOperationException($"保存后任务版本没有递增（{before} → {after}），旧检查与旧计划不会过期。");
                }
                if (status.Text != SupplementEntries.SavedText(true))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「{SupplementEntries.SavedText(true)}」。");
                }
                var counts = SupplementCountsLabel(dialog);
                if (!counts.Text.Contains("已保存补充 1 处", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"保存后计数行是「{counts.Text}」，期望「已保存补充 1 处」。");
                }
                var list = SupplementList(dialog);
                if (list.Items[0].SubItems[2].Text != "1 处")
                {
                    throw new InvalidOperationException($"清单第三格是「{list.Items[0].SubItems[2].Text}」，期望「1 处」。");
                }
                _lines.Add($"     状态行：{status.Text}；版本 {before} → {after}");
            });

            // 第四段（反向）：原样再点一次保存 → 内容没有变化，不写文件、不递增，状态行如实说明。
            RunAction("补充说明：原样重复保存不递增（真实控件）", () =>
            {
                if (dialog is null || task is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var before = tasks.Get(task.Id).Revision;
                var status = StatusLabel(dialog);
                status.Text = "";
                Click(dialog, "保存本接口补充");
                PumpUntilCondition(() => status.Text.Length > 0, "重复保存的状态行");
                if (status.Text != SupplementEntries.SavedText(false))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「{SupplementEntries.SavedText(false)}」。");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before)
                {
                    throw new InvalidOperationException($"内容没有变化，任务版本却从 {before} 变成 {after}（旧检查被无谓地作废）。");
                }
                _lines.Add($"     状态行：{status.Text}；版本保持 {after}");
            });

            // 第五段：勾选「保留现状，本次不补充」后保存 → 条目记 KeepAsIs 且值为空（与冻结页面同一口径）。
            RunAction("补充说明：勾选保留现状后保存（真实控件）", () =>
            {
                if (dialog is null || task is null || first is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var before = tasks.Get(task.Id).Revision;
                var keeps = SupplementKeepBoxes(dialog);
                keeps[0].Checked = true;
                Application.DoEvents();
                var status = StatusLabel(dialog);
                status.Text = "";
                Click(dialog, "保存本接口补充");
                PumpUntilCondition(() => status.Text.Length > 0, "保留现状保存的状态行");

                var store = tasks.Read<SupplementStore>(task.Id, TaskFiles.Supplement)
                    ?? throw new InvalidOperationException("保存后任务目录里没有 supplement.json。");
                var entry = store.Entries.FirstOrDefault(e => e.ResourceKey == first.Key && e.Location == "operation" && e.Field == "name");
                if (entry is null)
                {
                    throw new InvalidOperationException("勾选保留现状后，磁盘上没有这个接口标题的条目。");
                }
                if (!entry.KeepAsIs || entry.Value is not null)
                {
                    throw new InvalidOperationException($"保留现状的条目记成了 KeepAsIs={entry.KeepAsIs}、值「{entry.Value}」，期望 KeepAsIs=true 且值为空。");
                }
                if (tasks.Get(task.Id).Revision <= before)
                {
                    throw new InvalidOperationException("勾选保留现状后保存，任务版本没有递增。");
                }
                if (status.Text != SupplementEntries.SavedText(true))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望「{SupplementEntries.SavedText(true)}」。");
                }
                _lines.Add($"     保留现状条目：{entry.Location}/{entry.Field}，KeepAsIs={entry.KeepAsIs}，值「{entry.Value ?? "（空）"}」");
            });

            // 第六段：关键词筛到零行 → 提示语取共享口径、关键词框留着、保存禁用、计数行仍是整份统计。
            RunAction("补充说明：关键词筛到零行（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有打开对话框。");
                var list = SupplementList(dialog);
                var keyword = KeywordBox(dialog);
                keyword.Text = "zzz-没有这个接口";
                Application.DoEvents();
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"关键词不命中时清单还有 {list.Items.Count} 行。");
                }
                if (!keyword.Enabled) throw new InvalidOperationException("筛到零行时关键词框被禁用（提示语指名的下一步按不动）。");
                var hint = HintLabel(dialog);
                var expectedHint = ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun);
                if (!hint.Visible || hint.Text != expectedHint)
                {
                    throw new InvalidOperationException($"筛到零行的提示是「{(hint.Visible ? hint.Text : "（不可见）")}」，期望「{expectedHint}」。");
                }
                if (ButtonOf(dialog, "保存本接口补充").Enabled)
                {
                    throw new InvalidOperationException("没有选中接口时「保存本接口补充」仍可点。");
                }
                var counts = SupplementCountsLabel(dialog);
                if (!counts.Text.Contains("共 2 个接口", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"筛到零行后计数行变成「{counts.Text}」，期望仍是整份统计。");
                }
                keyword.Text = "";
                Application.DoEvents();
                if (list.Items.Count != 2)
                {
                    throw new InvalidOperationException($"清空关键词后清单 {list.Items.Count} 行，期望 2 行。");
                }
                _lines.Add($"     筛到零行：提示「{expectedHint}」，清空后回到 {list.Items.Count} 行");
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 补充说明对话框的检查结果夹具：两个接口，第一个带 query 参数、请求体与 200 响应（五个编辑位），
    /// 第二个只有标题与说明（两个编辑位）。只提供这一屏真正读的字段（接口身份与目标结构），
    /// 不冒充「检查跑过」——真实检查需要 Apifox CLI 登录态与项目权限，本环境不具备。
    /// </summary>
    private static CheckResult SupplementFixture()
    {
        var withStructure = new DocOperation
        {
            Key = "GET /api/probe",
            Method = "GET",
            Path = "/api/probe",
            Summary = "探针查询",
            Description = "查询探针数据。",
            Parameters = new SortedDictionary<string, DocParameter>(StringComparer.Ordinal)
            {
                ["query:pageSize"] = new DocParameter { In = "query", Name = "pageSize", Description = "每页条数。" },
            },
            RequestBody = new DocBody { MediaType = "application/json", Description = "请求体说明。" },
            Responses = new SortedDictionary<string, DocResponse>(StringComparer.Ordinal)
            {
                ["200"] = new DocResponse { Code = "200", Description = "探针数据。" },
            },
        };
        var docsOnly = new DocOperation
        {
            Key = "POST /api/probe",
            Method = "POST",
            Path = "/api/probe",
            Summary = "探针写入",
            Description = "写入探针数据。",
        };
        return new CheckResult
        {
            ComputedAt = DateTimeOffset.Now,
            TaskRevision = 1,
            StructureSource = "swagger",
            SnapshotComplete = true,
            Endpoints =
            [
                new EndpointCheck
                {
                    Key = withStructure.Key,
                    Method = "GET",
                    Path = "/api/probe",
                    InSource = true,
                    InSwagger = true,
                    InRemote = true,
                    RemoteId = 11,
                    RemoteName = "探针查询",
                    Status = "docs",
                    Target = withStructure,
                },
                new EndpointCheck
                {
                    Key = docsOnly.Key,
                    Method = "POST",
                    Path = "/api/probe",
                    InSource = true,
                    InSwagger = true,
                    InRemote = false,
                    Status = "new",
                    Target = docsOnly,
                },
            ],
        };
    }

    /// <summary>
    /// 差异确认对话框的真实控件交互（第一百四十七轮）：这一屏此前只被「构造一次」（<c>Cases</c> 里
    /// <c>new</c> + 建句柄），而它是「检查」这条路上**唯一写决策**的一屏——高风险变化逐项确认、
    /// 说明冲突选边、模型绑定保存，决策落 <c>decisions.json</c> 并递增任务版本。
    /// 只构造的话，「确认纳入」点不点得动、状态列会不会从「待确认」变成「已确认」、撤销会不会真把决策移除、
    /// 版本到底涨不涨、筛到零行给不给下一步，全都可以是坏的而冒烟照样 PASS。
    /// <para>
    /// 六段：① 准备检查结果（真实落盘 <c>diff.json</c> 夹具）→ ② 打开即选中第一条且计数行与门控对得上
    /// （真实控件）→ ③ 点「确认纳入」→ 决策落盘 + 版本递增 + 状态列变「已确认」（真实控件）→
    /// ④ 点「撤销确认」→ 决策移除 + 版本再递增（真实控件）→ ⑤ 说明冲突条目上选「使用目标」→
    /// 选择落盘（真实控件）→ ⑥ 关键词筛到零行（真实控件）。
    /// 断言一律读磁盘产物（<c>decisions.json</c> 与 <c>task.json</c> 的版本号）与控件文字，不读服务层返回值。
    /// </para>
    /// <para>
    /// 检查结果用夹具而不是真实检查：真实检查需要 Apifox CLI 登录态与项目权限，本环境（wine）不具备。
    /// 夹具只提供这一屏真正读的字段（差异条目、说明冲突、模型候选与状态），不冒充「检查跑过」——
    /// 检查本身的证据在只读契约检查的既有记录里。
    /// </para>
    /// </summary>
    private static void RunDecisionsInteraction(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var decisionSaves = services.GetRequiredService<DecisionService>();

        CheckTask? task = null;
        DecisionsDialog? dialog = null;
        try
        {
            // 第一段：准备检查结果（真实落盘）——一个高风险条目（逐项确认）、一个说明冲突（选边）、
            // 一个待绑定模型（模型绑定）与一个共享模型更新（确认更新）：四类条目各一条，够把四种动作都走一遍。
            RunAction("差异确认：准备检查结果（真实落盘）", () =>
            {
                var projectDir = Path.Combine(dataRoot, "Probe6.Api");
                CreateProbeWorkspace(projectDir);
                var profile = profiles.Save(new ProjectProfile
                {
                    Name = "差异确认配置",
                    WorkspaceRoot = dataRoot,
                    ApiProject = "Probe6.Api/Probe6.Api.csproj",
                    AllowedSourcePaths = ["Probe6.Api/"],
                }, isNew: true);
                task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "差异确认任务" });
                var fixture = DecisionsFixture();
                tasks.Write(task.Id, TaskFiles.Diff, fixture);
                var reread = tasks.Read<CheckResult>(task.Id, TaskFiles.Diff)
                    ?? throw new InvalidOperationException("diff.json 写进任务目录后读不回来。");
                if (reread.Items.Count != 1 || reread.Endpoints.Count != 1 || reread.Models.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"夹具读回 {reread.Items.Count}/{reread.Endpoints.Count}/{reread.Models.Count}（高风险/接口/模型），期望 1/1/1。");
                }
                _lines.Add("     夹具：1 个高风险条目、1 个说明冲突、1 个待绑定模型、1 个共享模型更新");
            });

            // 第二段：打开对话框（Show 触发 OnLoad 读盘），核对清单行数、打开时的选中落点、计数行与动作门控。
            RunAction("差异确认：打开即选中第一条且计数行与动作门控对得上（真实控件）", () =>
            {
                if (task is null) throw new InvalidOperationException("上一段没有产出任务。");
                dialog = new DecisionsDialog(tasks, task, decisionSaves);
                dialog.Show();
                Application.DoEvents();

                var list = DecisionList(dialog);
                if (!list.Visible) throw new InvalidOperationException("条目清单不可见（对话框停在提示态）。");
                if (list.Items.Count != 4)
                {
                    throw new InvalidOperationException($"清单有 {list.Items.Count} 行，夹具是 4 个条目（高风险/说明冲突/模型绑定/共享模型）。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException(
                        $"打开后选中 {list.SelectedIndices.Count} 行（首行序号 {list.SelectedIndices.Cast<int>().FirstOrDefault()}），期望恰好第一行。");
                }
                var expectedCounts = "共 4 项 · 待处理 4 · 高风险 1 · 说明冲突 1 · 模型绑定 1 · 共享模型 1";
                var counts = DecisionCountsLabel(dialog);
                if (counts.Text != expectedCounts)
                {
                    throw new InvalidOperationException($"计数行是「{counts.Text}」，期望「{expectedCounts}」。");
                }
                // 第一条是高风险变化：「确认纳入」可点，「使用目标」/「保留远端」此刻必须禁用（它们只对说明冲突条目有效）。
                if (!ButtonOf(dialog, "确认纳入").Enabled)
                {
                    throw new InvalidOperationException("第一条是高风险变化，「确认纳入」却是禁用的。");
                }
                if (ButtonOf(dialog, "使用目标").Enabled || ButtonOf(dialog, "保留远端").Enabled)
                {
                    throw new InvalidOperationException("第一条不是说明冲突，「使用目标」/「保留远端」却是可点的。");
                }
                if (ButtonOf(dialog, "撤销确认").Enabled)
                {
                    throw new InvalidOperationException("尚未保存任何决策，「撤销确认」却是可点的。");
                }
            });

            // 第三段：点「确认纳入」（真实控件）→ 决策落盘、任务版本递增、状态列变「已确认」。
            RunAction("差异确认：确认纳入后决策落盘且版本递增（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var before = tasks.Get(task.Id).Revision;
                Click(dialog, "确认纳入");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith("已保存：", StringComparison.Ordinal),
                    "决策保存完成");

                var store = tasks.Read<DecisionStore>(task.Id, TaskFiles.Decisions)
                    ?? throw new InvalidOperationException("decisions.json 没有落盘。");
                if (!store.Items.TryGetValue("diff-risk-1", out var value) || value != "accepted")
                {
                    throw new InvalidOperationException(
                        $"decisions.json 里没有「diff-risk-1=accepted」，实际：{string.Join("、", store.Items.Select(kv => kv.Key + "=" + kv.Value))}");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before + 1)
                {
                    throw new InvalidOperationException($"任务版本从 {before} 变成 {after}，期望 +1（决策变了就该让旧检查与旧计划过期）。");
                }
                var list = DecisionList(dialog);
                var state = list.Items[0].SubItems[3].Text;
                if (state != "已确认")
                {
                    throw new InvalidOperationException($"状态列是「{state}」，期望「已确认」。");
                }
                if (!StatusLabel(dialog).Text.Contains("重新检查后生效", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{StatusLabel(dialog).Text}」，没有说明「重新检查后生效」。");
                }
            });

            // 第四段：点「撤销确认」（真实控件）→ 决策从磁盘移除、版本再递增、状态列回到「待确认」。
            RunAction("差异确认：撤销确认后决策移除且版本再递增（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var before = tasks.Get(task.Id).Revision;
                Click(dialog, "撤销确认");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith("已保存：", StringComparison.Ordinal),
                    "撤销落盘");

                var store = tasks.Read<DecisionStore>(task.Id, TaskFiles.Decisions)
                    ?? throw new InvalidOperationException("decisions.json 没有落盘。");
                if (store.Items.ContainsKey("diff-risk-1"))
                {
                    throw new InvalidOperationException("撤销之后 decisions.json 里仍留着「diff-risk-1」。");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before + 1)
                {
                    throw new InvalidOperationException($"撤销后任务版本从 {before} 变成 {after}，期望 +1。");
                }
                var state = DecisionList(dialog).Items[0].SubItems[3].Text;
                if (state != "待确认")
                {
                    throw new InvalidOperationException($"撤销后状态列是「{state}」，期望回到「待确认」。");
                }
            });

            // 第五段：筛到「说明冲突」（真实控件）→ 第一条即冲突条目，「使用目标」可点；点它（真实控件）→ 选择落盘。
            RunAction("差异确认：说明冲突选「使用目标」后选择落盘（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                DecisionFilter(dialog).SelectedIndex = 3;
                Application.DoEvents();

                var list = DecisionList(dialog);
                if (list.Items.Count != 1)
                {
                    throw new InvalidOperationException($"「说明冲突」筛选下有 {list.Items.Count} 行，夹具里冲突只有 1 条。");
                }
                if (list.Items[0].SubItems[0].Text != "说明冲突")
                {
                    throw new InvalidOperationException($"筛选后第一行的类型是「{list.Items[0].SubItems[0].Text}」，期望「说明冲突」。");
                }
                var useTarget = ButtonOf(dialog, "使用目标");
                if (!useTarget.Enabled)
                {
                    throw new InvalidOperationException("说明冲突条目上「使用目标」是禁用的。");
                }
                var before = tasks.Get(task.Id).Revision;
                Click(dialog, "使用目标");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith("已保存：", StringComparison.Ordinal),
                    "冲突选择落盘");

                var store = tasks.Read<DecisionStore>(task.Id, TaskFiles.Decisions)
                    ?? throw new InvalidOperationException("decisions.json 没有落盘。");
                if (!store.DocChoices.TryGetValue("doc-conflict-1", out var choice) || choice != "useTarget")
                {
                    throw new InvalidOperationException(
                        $"decisions.json 里没有「doc-conflict-1=useTarget」，实际：{string.Join("、", store.DocChoices.Select(kv => kv.Key + "=" + kv.Value))}");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before + 1)
                {
                    throw new InvalidOperationException($"保存冲突选择后任务版本从 {before} 变成 {after}，期望 +1。");
                }
                // 选过之后「保留远端」仍可点（可以改边），而「使用目标」自己应禁用（已是这一边）。
                if (ButtonOf(dialog, "使用目标").Enabled)
                {
                    throw new InvalidOperationException("已经选了「使用目标」，该按钮却仍可点。");
                }
                if (!ButtonOf(dialog, "保留远端").Enabled)
                {
                    throw new InvalidOperationException("「使用目标」已选，「保留远端」却是禁用的（改不了边）。");
                }
            });

            // 第六段：关键词筛到零行（真实控件）→ 清单空、提示给出下一步、筛选器不被一起禁用。
            RunAction("差异确认：关键词筛到零行且筛选器仍可用（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                DecisionFilter(dialog).SelectedIndex = 0;
                DecisionKeyword(dialog).Text = "不存在的资源";
                Application.DoEvents();

                var list = DecisionList(dialog);
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"关键词筛后清单还有 {list.Items.Count} 行。");
                }
                if (list.Visible)
                {
                    throw new InvalidOperationException("清单已空却仍然可见（该让提示接管这一屏）。");
                }
                var expectedHint = ListFilter.FilteredEmptyHint(ListFilter.DecisionNoun, hasKeyword: true);
                var hint = DecisionHint(dialog);
                if (hint.Text != expectedHint)
                {
                    throw new InvalidOperationException($"提示是「{hint.Text}」，期望「{expectedHint}」。");
                }
                // 筛到零行时筛选器本身就是提示语所指的恢复路径，必须保持可用（关掉就锁死了）。
                if (!DecisionFilter(dialog).Enabled || !DecisionKeyword(dialog).Enabled)
                {
                    throw new InvalidOperationException("筛到零行后筛选下拉或关键词框被一起禁用了（提示指的恢复路径不可达）。");
                }
            });
        }
        finally
        {
            dialog?.Close();
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 差异确认对话框的检查结果夹具：一个高风险条目（逐项确认）、一个说明冲突（选边）、
    /// 一个待绑定模型（模型绑定）与一个共享模型更新（确认更新）——四类条目各一条。
    /// 字段只给这一屏真正读的那些，不冒充「检查跑过」。
    /// </summary>
    private static CheckResult DecisionsFixture()
    {
        var risk = new DiffItem
        {
            Id = "diff-risk-1",
            ResourceKey = "GET /api/probe",
            Location = "响应 200 的说明",
            Category = "documentation",
            Change = "changed",
            Field = "description",
            Before = JsonValue.Create("旧说明"),
            After = JsonValue.Create("新说明"),
            Source = "swagger",
            Risk = "incompatible",
            Reason = "响应字段口径变化，需要人工判断。",
            NeedsDecision = true,
        };
        var conflict = new DocChange
        {
            Id = "doc-conflict-1",
            Location = "请求参数说明",
            Field = "description",
            RemoteValue = "远端说明",
            TargetValue = "目标说明",
            Origin = "swagger",
            Decision = "conflict",
        };
        var endpoint = new EndpointCheck
        {
            Key = "GET /api/probe",
            Method = "GET",
            Path = "/api/probe",
            InSource = true,
            InSwagger = true,
            Status = "docs",
            DocChanges = [conflict],
        };
        var model = new ModelCheck
        {
            Name = "ProbeModel",
            MappingState = "candidate",
            Candidates = [9001],
            Status = "changed",
            DocChanges = [],
        };
        return new CheckResult
        {
            ComputedAt = DateTimeOffset.Now,
            TaskRevision = 1,
            StructureSource = "swagger",
            SnapshotAt = DateTimeOffset.Now,
            SnapshotComplete = true,
            Items = [risk],
            Endpoints = [endpoint],
            Models = [model],
        };
    }

    /// <summary>
    /// 响应契约对话框的真实控件交互（第一百八十二轮，简报 P0-2「让降级路径真正可交付」）。
    /// <para>
    /// 这一屏在「降级答辩脚本」逐屏表里此前只有「源码 + 编译证据」——审阅动作（确认 / 拒绝 / 重置）与
    /// 「写任务数据并递增任务版本使旧检查过期」从来没有在真实控件上走过一次，答辩现场只能说
    /// 「代码与用例在，真实端到端未跑」。本轮把它补成五段真实控件交互：夹具落盘 → 打开即选中第一条且
    /// 计数行与三个动作的门控对得上 → 点「确认」后契约落盘且任务版本递增 → 阻断契约的「确认」是禁用的
    /// （不是点了才报错，详情区同时给出未解析点）→ 点「重置」回到待确认。
    /// </para>
    /// <para>
    /// 它证明的是「这一屏的按钮真的接在共享 <c>ContractReviewService</c> 上、点下去磁盘真的变了」，
    /// <b>不是</b>「好看」——观感与 DPI 缩放仍随 G-D5 真机复核；也不碰真实 Apifox（审阅只写任务目录）。
    /// </para>
    /// </summary>
    private static void RunContractsInteraction(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var reviews = services.GetRequiredService<ContractReviewService>();

        CheckTask? task = null;
        ContractsDialog? dialog = null;
        try
        {
            // 第一段：准备响应契约（真实落盘）——待确认 / 已阻断 / 已拒绝各一条，够把三个动作的门控都走一遍。
            RunAction("响应契约：准备契约清单（真实落盘）", () =>
            {
                var projectDir = Path.Combine(dataRoot, "Probe7.Api");
                CreateProbeWorkspace(projectDir);
                var profile = profiles.Save(new ProjectProfile
                {
                    Name = "响应契约配置",
                    WorkspaceRoot = dataRoot,
                    ApiProject = "Probe7.Api/Probe7.Api.csproj",
                    AllowedSourcePaths = ["Probe7.Api/"],
                }, isNew: true);
                task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "响应契约任务" });
                tasks.Write(task.Id, TaskFiles.ResponseContract, ContractsFixture());
                var reread = tasks.Read<List<ResponseContract>>(task.Id, TaskFiles.ResponseContract)
                    ?? throw new InvalidOperationException("responseContract.json 写进任务目录后读不回来。");
                if (reread.Count != 3)
                {
                    throw new InvalidOperationException($"夹具读回 {reread.Count} 条契约，期望 3 条（待确认 / 已阻断 / 已拒绝）。");
                }
                _lines.Add("     夹具：1 条待确认、1 条已阻断（带未解析点）、1 条已拒绝");
            });

            // 第二段：打开对话框（Show 触发 OnLoad 读盘），核对清单行数、打开时的选中落点、计数行与三个动作的门控。
            RunAction("响应契约：打开即选中第一条且计数行与动作门控对得上（真实控件）", () =>
            {
                if (task is null) throw new InvalidOperationException("上一段没有产出任务。");
                dialog = new ContractsDialog(tasks, task, reviews);
                dialog.Show();
                Application.DoEvents();

                var list = ContractList(dialog);
                if (!list.Visible) throw new InvalidOperationException("契约清单不可见（对话框停在提示态）。");
                if (list.Items.Count != 3)
                {
                    throw new InvalidOperationException($"清单有 {list.Items.Count} 行，夹具是 3 条契约（待确认 / 已阻断 / 已拒绝）。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
                {
                    throw new InvalidOperationException(
                        $"打开后选中 {list.SelectedIndices.Count} 行（首行序号 {list.SelectedIndices.Cast<int>().FirstOrDefault()}），期望恰好第一行。");
                }
                var expectedCounts = "契约 3 · 待确认 1 · 已确认 0 · 已拒绝 1 · 已阻断 1 · 已失效 0";
                var counts = ContractCountsLabel(dialog);
                if (counts.Text != expectedCounts)
                {
                    throw new InvalidOperationException($"计数行是「{counts.Text}」，期望「{expectedCounts}」。");
                }
                // 第一条是待确认：确认与拒绝可点；重置只对已确认 / 已拒绝 / 已失效有效，此刻必须禁用。
                if (!ButtonOf(dialog, "确认").Enabled)
                {
                    throw new InvalidOperationException("第一条是待确认契约，「确认」却是禁用的。");
                }
                if (!ButtonOf(dialog, "拒绝").Enabled)
                {
                    throw new InvalidOperationException("第一条是待确认契约，「拒绝」却是禁用的。");
                }
                if (ButtonOf(dialog, "重置").Enabled)
                {
                    throw new InvalidOperationException("第一条还是待确认，「重置」却是可点的（重置只对已确认 / 已拒绝 / 已失效有效）。");
                }
            });

            // 第三段：点「确认」（真实控件）→ 契约落盘为 confirmed、任务版本递增、状态行与计数行如实。
            RunAction("响应契约：确认后契约落盘且任务版本递增（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var before = tasks.Get(task.Id).Revision;
                var status = StatusLabel(dialog);
                status.Text = "";
                Click(dialog, "确认");
                PumpUntilCondition(() => status.Text.StartsWith("已确认。", StringComparison.Ordinal), "契约确认完成");

                var stored = tasks.Read<List<ResponseContract>>(task.Id, TaskFiles.ResponseContract)
                    ?? throw new InvalidOperationException("确认后 responseContract.json 读不回来。");
                var confirmed = stored.FirstOrDefault(c => c.EndpointKey == "GET /api/probe/orders");
                if (confirmed is null || confirmed.ReviewState != "confirmed")
                {
                    throw new InvalidOperationException(
                        $"磁盘上那条契约的审阅状态是「{confirmed?.ReviewState ?? "（没有这一条）"}」，期望 confirmed。");
                }
                if (confirmed.ReviewedAt is null)
                {
                    throw new InvalidOperationException("确认后 ReviewedAt 没有落盘——审阅时间没记下来。");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before + 1)
                {
                    throw new InvalidOperationException($"任务版本从 {before} 变成 {after}，期望 +1（旧检查与旧计划要过期）。");
                }
                if (!status.Text.Contains("剩余待确认 0 个", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，没有如实报出剩余待确认数。");
                }
                if (!status.Text.Contains("重新检查", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，没有说明确认后需重新检查才生效。");
                }
                var counts = ContractCountsLabel(dialog);
                if (!counts.Text.Contains("待确认 0", StringComparison.Ordinal) || !counts.Text.Contains("已确认 1", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"确认后计数行是「{counts.Text}」，期望待确认 0 / 已确认 1。");
                }
                _lines.Add($"     状态行：{status.Text}（任务版本 {before} → {after}）");
            });

            // 第四段：阻断契约不能确认——「确认」是禁用的，详情区同时给出未解析点（不是点了才报错）。
            RunAction("响应契约：阻断契约的「确认」是禁用的且详情区给出未解析点（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var list = ContractList(dialog);
                SelectContractRow(list, "GET /api/probe/blocked");
                Application.DoEvents();

                if (ButtonOf(dialog, "确认").Enabled)
                {
                    throw new InvalidOperationException("阻断契约的「确认」是可点的——未解析点应当阻断确认。");
                }
                if (!ButtonOf(dialog, "拒绝").Enabled)
                {
                    throw new InvalidOperationException("阻断契约的「拒绝」却是禁用的（拒绝对阻断契约仍然有效）。");
                }
                var detail = DetailBox(dialog);
                if (!detail.Text.Contains("未解析点（阻断确认）", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"详情区没有说明为什么不能确认。详情区原文：{detail.Text}");
                }
                var stored = tasks.Read<List<ResponseContract>>(task.Id, TaskFiles.ResponseContract)
                    ?? throw new InvalidOperationException("responseContract.json 读不回来。");
                if (stored.First(c => c.EndpointKey == "GET /api/probe/blocked").ReviewState != "blocked")
                {
                    throw new InvalidOperationException("只是选中了阻断契约，磁盘上的状态却变了。");
                }
            });

            // 第五段：点「重置」（真实控件）→ 已确认的那条回到待确认、版本再递增一次。
            RunAction("响应契约：重置回待确认（真实控件）", () =>
            {
                if (task is null || dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var list = ContractList(dialog);
                SelectContractRow(list, "GET /api/probe/orders");
                Application.DoEvents();
                if (!ButtonOf(dialog, "重置").Enabled)
                {
                    throw new InvalidOperationException("已确认的契约「重置」却不可点。");
                }
                var before = tasks.Get(task.Id).Revision;
                var status = StatusLabel(dialog);
                status.Text = "";
                Click(dialog, "重置");
                PumpUntilCondition(() => status.Text.StartsWith("待确认。", StringComparison.Ordinal), "契约重置完成");

                var stored = tasks.Read<List<ResponseContract>>(task.Id, TaskFiles.ResponseContract)
                    ?? throw new InvalidOperationException("重置后 responseContract.json 读不回来。");
                if (stored.First(c => c.EndpointKey == "GET /api/probe/orders").ReviewState != "pending")
                {
                    throw new InvalidOperationException("重置后磁盘上的审阅状态没有回到 pending。");
                }
                var after = tasks.Get(task.Id).Revision;
                if (after != before + 1)
                {
                    throw new InvalidOperationException($"重置后任务版本从 {before} 变成 {after}，期望 +1。");
                }
                if (!status.Text.Contains("剩余待确认 1 个", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望剩余待确认 1 个。");
                }
                _lines.Add($"     状态行：{status.Text}（任务版本 {before} → {after}）");
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 响应契约夹具：待确认（可确认）、已阻断（带未解析点，确认必须被挡住）、已拒绝（可重置）各一条。
    /// 三条都带分支与证据，详情区才有东西可显示——空壳夹具会让「详情区给出未解析点」那一段变成空断言。
    /// </summary>
    private static List<ResponseContract> ContractsFixture() =>
    [
        new ResponseContract
        {
            EndpointKey = "GET /api/probe/orders",
            WrapperType = "ApiResponse<T>",
            DataMember = "Data",
            DataJsonName = "data",
            DataType = "ProbeResponse",
            DataTypeId = "#/definitions/ProbeResponse",
            ReviewState = "pending",
            Branches =
            [
                new ResponseBranch
                {
                    BranchKey = "200",
                    HttpStatus = 200,
                    Condition = "",
                    DataKind = "type",
                    DataType = "ProbeResponse",
                    File = "Controllers/ProbeController.cs",
                    Line = 12,
                },
            ],
            EvidenceIds = ["ev-orders"],
            SourceFingerprint = "fixture-orders",
        },
        new ResponseContract
        {
            EndpointKey = "GET /api/probe/blocked",
            WrapperType = "ApiResponse<T>",
            DataMember = "Data",
            DataJsonName = "data",
            DataType = null,
            ReviewState = "blocked",
            UnresolvedPoints = ["ProbeController.Get 的返回表达式无法静态确定 data 类型"],
            Branches =
            [
                new ResponseBranch
                {
                    BranchKey = "200",
                    HttpStatus = 200,
                    Condition = "",
                    DataKind = "unknown",
                    Note = "返回表达式未解析",
                    File = "Controllers/ProbeController.cs",
                    Line = 20,
                },
            ],
            EvidenceIds = ["ev-blocked"],
            SourceFingerprint = "fixture-blocked",
        },
        new ResponseContract
        {
            EndpointKey = "GET /api/probe/rejected",
            WrapperType = "ApiResponse<T>",
            DataMember = "Data",
            DataJsonName = "data",
            DataType = "ProbeResponse",
            ReviewState = "rejected",
            Branches =
            [
                new ResponseBranch
                {
                    BranchKey = "200",
                    HttpStatus = 200,
                    Condition = "",
                    DataKind = "type",
                    DataType = "ProbeResponse",
                    File = "Controllers/ProbeController.cs",
                    Line = 28,
                },
            ],
            EvidenceIds = ["ev-rejected"],
            SourceFingerprint = "fixture-rejected",
        },
    ];

    /// <summary>
    /// 配置表单的真实布局（第一百四十八轮）：新建配置对话框在真机截图里是「名称、仓库根目录之后
    /// 一大段空白，四个高级按钮彼此间距极大，右侧还出滚动条」——用户原话「很抽象、没法用」。
    /// 这一屏是产品主链路的第一步（没有配置就没有任务、没有检查），而它此前只有「构造得起来」这一条证据。
    /// <para>
    /// 八段，全部量真实控件：① 逐行量字段网格的行高，超过 <see cref="ProfileFormLayout.MaxRowHeight"/>
    /// 的行即失败（空标签格里的默认尺寸 <c>Panel</c> 会把行撑到 100px）；② 网格不出横向滚动条
    /// （七十余字的长说明把网格撑宽过）；③ 保存/取消在可视区内、不被滚动区盖住；④ 三个必填字段
    /// 连续且都在首屏；⑤ 四个高级入口在同一行、间距不超过 <see cref="ProfileFormLayout.AdvancedEntryGap"/>；
    /// ⑥ 未保存时「读取项目列表」停用并给出说明（不是点了才报错）；⑦ 编辑对话框字段回显一致、
    /// 「读取项目列表」可用；⑧「新建任务 / 模块与模板 / 序列化与分析 / 模型服务」四个对话框逐条量
    /// 提示行的行高（第二百零六轮：那三处私有 <c>AddHint</c> 与新建任务的 <c>AddRow</c> 都放过
    /// 默认尺寸的占位 Panel，① 修掉的根因在这一族里没跟上）。
    /// </para>
    /// <para>
    /// ① 与 ⑧ 另各记一行 <see cref="DpiContext"/>：控件自报 DPI 与进程 DPI <b>两个数分别进报告</b>
    /// （第二百零九轮，简报 P0-1）。行高预算按字体行高算，而字体按哪一档放大正是两支演示脚本
    /// 读数不同的那一处——归因留给下一轮，本轮先把「那一次量到的是哪一个上下文」变成读数自己能回答的事。
    /// </para>
    /// <para>
    /// 它证明的是「这一屏排得下、点得到、说得清」，**不是「好看」**——观感与 DPI 缩放仍随 G-D5 真机复核。
    /// </para>
    /// </summary>
    private static void RunProfileFormLayout(IServiceProvider services, ProjectProfile profile)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var cli = services.GetRequiredService<ApifoxCli>();
        var credentials = services.GetRequiredService<CredentialStore>();
        var limit = ProfileFormLayout.MaxRowHeight(UiTokens.Body.Value.Height);

        ProfileDialog? dialog = null;
        try
        {
            RunAction("配置表单：新建对话框的行高没有死空白（真实布局）", () =>
            {
                dialog = new ProfileDialog(profiles, cli, credentials, editing: null);
                dialog.Show();
                Application.DoEvents();
                var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
                var heights = grid.GetRowHeights();
                _lines.Add("     行高：" + string.Join("，", heights.Select((h, i) => RowLabel(grid, i) + "=" + h + "px")));
                // 第二百零九轮（简报 P0-1）：行高预算来自字体行高，而字体按哪一档放大，两个读数各有各的说法——
                // demo.ps1 那一跑控件自报的 DPI 跟着档位走（120 / 192）、demo.sh 那一跑恒为 96，
                // 于是同一份产物在两跑里给出不同行数。把两个数**分别**记进报告（不合成一个、不互相代名），
                // 下一次两边再差时，读数本身就能回答「那一次量到的是哪一个上下文」。
                // 第二百一十一轮按简报 P0-2 量到分岔的变量：换档时**是否重启 wine 前缀**——重启后两支读数相同，
                // 只有「控件自报与进程相等」那一个上下文会行使行高预算（实测表见 .winecheck/README.md
                // 「Windows 侧那支」第 4 条；预算本身仍不改，那是另一轮的决定）。
                _lines.Add("     DPI 上下文（新建配置对话框）：" + DpiContext(dialog)
                    + $"、正文行高 {UiTokens.Body.Value.Height}px、行高预算 {limit}px");
                var tall = new List<string>();
                for (var row = 0; row < heights.Length; row++)
                {
                    if (heights[row] > limit) tall.Add($"{RowLabel(grid, row)}={heights[row]}px（{TallestInRow(grid, row)}）");
                }
                if (tall.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{tall.Count} 行超过 {limit}px（正文行高 {UiTokens.Body.Value.Height} + 行内留白 {ProfileFormLayout.RowPadding}；"
                        + $"{DpiContext(dialog)}）：{string.Join("，", tall)}");
                }
            });

            RunAction("配置表单：新建对话框不出横向滚动条（真实布局）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
                var host = FindScrollHost(dialog) ?? throw new InvalidOperationException("对话框里找不到可滚动宿主。");
                var preferred = grid.GetPreferredSize(new Size(0, 0)).Width;
                _lines.Add($"     网格自然宽 {preferred}px，可视宽 {host.ClientSize.Width}px");
                if (preferred > host.ClientSize.Width)
                {
                    throw new InvalidOperationException(
                        $"网格自然宽 {preferred}px 超过可视宽 {host.ClientSize.Width}px：右侧会出横向滚动条（说明行太长或控件比列宽）。");
                }
            });

            RunAction("配置表单：保存与取消在可视区内且不被滚动区盖住（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                foreach (var text in new[] { "保存", "取消" })
                {
                    var button = FindButton(dialog, text) ?? throw new InvalidOperationException($"找不到「{text}」按钮。");
                    if (!button.Visible) throw new InvalidOperationException($"「{text}」按钮不可见。");
                    var rect = dialog.RectangleToClient(button.Parent!.RectangleToScreen(button.Bounds));
                    if (rect.Bottom > dialog.ClientSize.Height || rect.Top < 0)
                    {
                        throw new InvalidOperationException(
                            $"「{text}」按钮落在客户区之外（{rect.Top}..{rect.Bottom}，客户区高 {dialog.ClientSize.Height}）。");
                    }
                    _lines.Add($"     「{text}」按钮：y {rect.Top}..{rect.Bottom}，客户区高 {dialog.ClientSize.Height}");
                }
            });

            RunAction("配置表单：三个必填字段连续且都在首屏（真实布局）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
                var host = FindScrollHost(dialog) ?? throw new InvalidOperationException("对话框里找不到可滚动宿主。");
                var rows = ProfileFormLayout.CoreFields.Select(field => RowOfLabel(grid, field)).ToArray();
                if (rows.Any(row => row < 0))
                {
                    throw new InvalidOperationException(
                        "必填字段没有全部出现在网格里：" + string.Join("，", ProfileFormLayout.CoreFields.Select((f, i) => f + "=" + rows[i])));
                }
                for (var i = 1; i < rows.Length; i++)
                {
                    if (rows[i] != rows[i - 1] + 1)
                    {
                        throw new InvalidOperationException(
                            $"必填字段不连续：{ProfileFormLayout.CoreFields[i - 1]} 在第 {rows[i - 1]} 行，{ProfileFormLayout.CoreFields[i]} 在第 {rows[i]} 行。");
                    }
                }
                var heights = grid.GetRowHeights();
                var bottom = 0;
                for (var row = 0; row <= rows[^1] && row < heights.Length; row++) bottom += heights[row];
                _lines.Add($"     必填字段在第 {rows[0]}–{rows[^1]} 行，底部 {bottom}px，可视高 {host.ClientSize.Height}px");
                if (bottom > host.ClientSize.Height)
                {
                    throw new InvalidOperationException(
                        $"必填字段底部 {bottom}px 超过可视高 {host.ClientSize.Height}px：首用要滚动才能填完。");
                }
            });

            RunAction("配置表单：四个高级入口收在同一行（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var buttons = ProfileFormLayout.AdvancedEntries
                    .Select(text => FindButton(dialog, text) ?? throw new InvalidOperationException($"找不到高级入口「{text}」。"))
                    .ToArray();
                var parent = buttons[0].Parent ?? throw new InvalidOperationException("高级入口没有父容器。");
                if (buttons.Any(button => button.Parent != parent))
                {
                    throw new InvalidOperationException("四个高级入口不在同一个容器里（各占一行）。");
                }
                var tops = buttons.Select(button => button.Top).Distinct().ToArray();
                if (tops.Length != 1)
                {
                    throw new InvalidOperationException($"四个高级入口不在同一行：Top 分别是 {string.Join("，", tops)}。");
                }
                var ordered = buttons.OrderBy(button => button.Left).ToArray();
                for (var i = 1; i < ordered.Length; i++)
                {
                    var gap = ordered[i].Left - ordered[i - 1].Right;
                    if (gap > ProfileFormLayout.AdvancedEntryGap)
                    {
                        throw new InvalidOperationException(
                            $"「{ordered[i - 1].Text}」与「{ordered[i].Text}」之间隔了 {gap}px（上限 {ProfileFormLayout.AdvancedEntryGap}px）。");
                    }
                }
                _lines.Add("     高级入口：" + string.Join("，", ordered.Select(button => button.Text + "@" + button.Left)));
            });

            RunAction("配置表单：未保存时「读取项目列表」停用并说明下一步（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                var button = FindButton(dialog, "读取项目列表") ?? throw new InvalidOperationException("找不到「读取项目列表」按钮。");
                if (button.Enabled)
                {
                    throw new InvalidOperationException("新建配置时「读取项目列表」是启用的：点下去只会得到「请先保存配置」，用户得自己猜下一步。");
                }
                var hint = FindLabel(dialog, ProfileFormLayout.ProjectListHintUnsaved)
                    ?? throw new InvalidOperationException($"找不到说明行「{ProfileFormLayout.ProjectListHintUnsaved}」。");
                if (!hint.Visible) throw new InvalidOperationException("说明行不可见。");
                _lines.Add("     说明行：" + hint.Text);
            });

            RunAction("配置表单：编辑对话框字段回显一致且「读取项目列表」可用（真实控件）", () =>
            {
                using var editing = new ProfileDialog(profiles, cli, credentials, editing: profile);
                editing.Show();
                Application.DoEvents();
                var name = FindTextBox(editing, profile.Name)
                    ?? throw new InvalidOperationException($"名称输入框里没有回显「{profile.Name}」。");
                if (FindTextBox(editing, profile.WorkspaceRoot) is null)
                {
                    throw new InvalidOperationException($"仓库根目录输入框里没有回显「{profile.WorkspaceRoot}」。");
                }
                if (FindTextBox(editing, profile.ApiProject) is null)
                {
                    throw new InvalidOperationException($"API 项目输入框里没有回显「{profile.ApiProject}」。");
                }
                var button = FindButton(editing, "读取项目列表") ?? throw new InvalidOperationException("找不到「读取项目列表」按钮。");
                if (!button.Enabled) throw new InvalidOperationException("编辑已保存的配置时「读取项目列表」是停用的。");
                _lines.Add($"     回显：名称「{name.Text}」、仓库「{profile.WorkspaceRoot}」、API 项目「{profile.ApiProject}」");
            });

            RunAction("配置表单：四个子对话框的提示行没有死空白（真实布局）", () =>
            {
                // 第二百零六轮（distill）：提示行收成一处工厂 Ui.HintRow 之前，「模块与模板 / 序列化与分析 /
                // 模型服务」三份私有 AddHint 与新建任务的 AddRow 都在标签列放默认尺寸的 Panel（200×100），
                // 每一行提示都被撑到 100px 高。这一段逐条量那四个对话框的提示行行高——改回去即红。
                var tall = new List<string>();
                foreach (var (screen, open) in new (string, Func<Form>)[]
                {
                    ("新建任务", () => new NewTaskDialog(profiles)),
                    ("模块与模板", () => new ProfileAdvancedDialog(cli, profile)),
                    ("序列化与分析", () => new ProfileAnalysisDialog(profile)),
                    ("模型服务", () => new GenerationDialog(credentials, profiles, profile, profileExists: true)),
                })
                {
                    using var form = open();
                    form.Show();
                    Application.DoEvents();
                    var grid = FindGrid(form) ?? throw new InvalidOperationException($"「{screen}」对话框里找不到两列字段网格。");
                    var heights = grid.GetRowHeights();
                    var rows = new List<string>();
                    for (var row = 0; row < heights.Length; row++)
                    {
                        if (!IsHintRow(grid, row)) continue;
                        rows.Add($"第{row}行={heights[row]}px");
                        if (heights[row] > limit) tall.Add($"「{screen}」第 {row} 行 {heights[row]}px（{TallestInRow(grid, row)}，{DpiContext(form)}）");
                    }
                    if (rows.Count == 0) throw new InvalidOperationException($"「{screen}」对话框里没有一条提示行——工厂调用点被删掉了？");
                    _lines.Add($"     提示行行高（{screen}，阈值 {limit}px，{DpiContext(form)}）：{string.Join("，", rows)}");
                }

                if (tall.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{tall.Count} 条提示行超过 {limit}px（正文行高 {UiTokens.Body.Value.Height} + 行内留白 {ProfileFormLayout.RowPadding}）：{string.Join("，", tall)}");
                }
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// DPI 上下文的两个读数，各挂各的标签（第二百零九轮，简报 P0-1；本方法是这一族的唯一出口）。
    /// <para>
    /// <b>为什么要分成两个念</b>：同一份产物、同一个前缀、同一个屏幕，<c>demo.ps1</c> 那一跑里
    /// <b>控件自报的 DPI 跟着档位走</b>（120 / 192），<c>demo.sh</c> 那一跑里它<b>恒报 96</b>
    /// （第二百零七轮那两个读数照原样留着）。分岔的变量第二百一十一轮量到了：<b>换档时是否重启 wine 前缀</b>——
    /// 预置 <c>LogPixels</c> 再 <c>wineserver -k</c> 重启前缀，两支在同一档位上的读数逐字相同
    /// （表在 <c>.winecheck/README.md</c>「Windows 侧那支」第 4 条）。行高与文字宽都随字体放大，
    /// 所以「跟档位走」的那一支会先把配置表单量到超出行高预算（125% 档 4 行、200% 档 10 行，提示行另有 2 条）；
    /// 同一次实验也量到反过来的那一半——<b>慢一档</b>那一跑里字体仍按进程 DPI 放大，控件自己的行高却不放大，
    /// 于是三档都量不出超预算（<c>.winecheck/reading.txt</c> 那份三档全绿出自这一个上下文）。
    /// 两边差的这件事有一个可数的候选，就是<b>这两个数是否相等</b>——把它们写进同一行报告，
    /// 下一次再差时读数本身就能回答「那一次量到的是哪一个上下文」，不用回头猜当时哪支脚本在跑。
    /// </para>
    /// <para>
    /// <b>两个数各自的口径</b>：<c>Control.DeviceDpi</c> 是<b>控件自报</b>的那一个（框架按窗口所在
    /// 上下文给出的解释，wine 实测它恒报 96，第一百六十二、一百六十三、一百七十五轮各量到一次）；
    /// <c>DpiScale.ScreenDpi()</c> 是<b>进程取到并按它缩放布局</b>的那一个（屏幕 DC 的
    /// <c>LOGPIXELSY</c>，即 <c>DpiScale.ScaleFactor()</c> 的分子，第一百六十三轮把缩放动作交给它）。
    /// 两者不等时谁赢，就是那一跑落在哪个上下文——本方法<b>只念数，不判断、不换算、不取其一</b>：
    /// 这条纪律不随归因推进放宽，判断留在报告与文档里，不放进取法本身。
    /// </para>
    /// </summary>
    private static string DpiContext(Control control) =>
        $"控件自报 DPI {control.DeviceDpi}、进程 DPI {DpiScale.ScreenDpi()}";

    /// <summary>按文字找按钮（递归）。</summary>
    private static Button? FindButton(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Button button && string.Equals(button.Text, text, StringComparison.Ordinal)) return button;
            if (FindButton(child, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>按文字找标签（递归）。</summary>
    private static Label? FindLabel(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label && string.Equals(label.Text, text, StringComparison.Ordinal)) return label;
            if (FindLabel(child, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// 找内容等于给定文字的输入控件（递归）：<see cref="TextBox"/> 与 <see cref="ComboBox"/> 都算。
    /// 「API 项目」是可选可填的下拉框（<c>DropDownStyle.DropDown</c>：既能手填路径，也能从「查找 API 项目」
    /// 的结果里选），回显一致要看的是「这一格显示的是不是保存的那个值」，不是控件类型——
    /// 只认 TextBox 会把一个正确的回显判成失败。
    /// </summary>
    private static Control? FindTextBox(Control root, string text)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TextBox or ComboBox && string.Equals(child.Text, text, StringComparison.Ordinal)) return child;
            if (FindTextBox(child, text) is { } found) return found;
        }
        return null;
    }

    /// <summary>找可滚动宿主（<c>Ui.ScrollHost</c> 出厂的那一个：AutoScroll 且停靠填充）。</summary>
    private static Panel? FindScrollHost(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Panel { AutoScroll: true } panel) return panel;
            if (FindScrollHost(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>
    /// 某一行里最高的那个控件（类型、高、上下留白）：行高超限时把「是谁把这一行撑高的」一并报出来，
    /// 否则只看到一个像素数，还得再跑一轮才知道该改哪一格。
    /// </summary>
    private static string TallestInRow(TableLayoutPanel grid, int row)
    {
        Control? tallest = null;
        foreach (Control control in grid.Controls)
        {
            if (grid.GetRow(control) != row) continue;
            if (tallest is null || control.Height + control.Margin.Vertical > tallest.Height + tallest.Margin.Vertical) tallest = control;
        }
        if (tallest is null) return "空行";
        var inner = tallest.Controls.Count > 0
            ? "，内含 " + string.Join("/", tallest.Controls.Cast<Control>().Select(c => c.GetType().Name + " " + c.Height + "px"))
            : "";
        return $"{tallest.GetType().Name} 高 {tallest.Height}px、上下留白 {tallest.Margin.Top}+{tallest.Margin.Bottom}{inner}";
    }

    /// <summary>
    /// 这一行是不是提示行：<b>标签列没有控件</b>、字段列里是一个 <c>Label</c>——
    /// 即 <see cref="Ui.HintRow"/> 出厂的那个形状（第二百零六轮之前，三份私有 AddHint 与新建任务的
    /// AddRow 会在标签列再放一个占位 Panel，这一判据正好把它们与字段行分开）。
    /// <para>
    /// <b>为什么不按字号认</b>：第一版按「那一格是 Caption 磅值」判，wine 实测 96 档认得出五行、
    /// 120 与 192 档一条都认不出（冒烟器自己报「没有一条提示行」）。<c>Font.SizeInPoints</c> 随档位变
    /// （窗体经 <c>DpiScale.Apply</c> 缩放后不再是 9），而「这一行的控件在哪一列」不变。
    /// 判断要看形状，不能看一个会随缩放漂走的读数。
    /// </para>
    /// </summary>
    private static bool IsHintRow(TableLayoutPanel grid, int row)
    {
        var hint = false;
        foreach (Control control in grid.Controls)
        {
            if (grid.GetRow(control) != row) continue;
            if (grid.GetColumn(control) == 0) return false;
            if (control is Label) hint = true;
        }
        return hint;
    }

    /// <summary>字段名所在的行号（找不到返回 -1）。</summary>
    private static int RowOfLabel(TableLayoutPanel grid, string text)
    {
        foreach (Control control in grid.Controls)
        {
            if (grid.GetColumn(control) != 0) continue;
            if (string.Equals(control.Text, text, StringComparison.Ordinal)) return grid.GetRow(control);
        }
        return -1;
    }

    /// <summary>
    /// 配置页读取失败态的真实控件交互（第一百五十一轮，控制仓简报 P0-2）。
    /// <para>
    /// 存在理由：第一百五十轮把「读取失败」补成配置页的第三种状态（<see cref="ProfileLoadState"/> 单一落点 +
    /// 清单区换成失败态 + 唯一行动按钮「重试」），但证据只到 Linux 侧的源码切片与判定真值表——
    /// <b>真实控件上「失败 → 点重试 → 恢复」这条闭环从未跑过</b>。而这一屏是产品主链路的第一步：
    /// 失败态里清单留不留旧行、按钮点不点得动、点完是不是真的重读、恢复后编辑与探测能不能用，
    /// 全都可以是坏的而 Linux 侧全绿、冒烟报告照样 PASS。
    /// </para>
    /// <para>
    /// 五段，全部走真实 WinForms 控件（<c>Form.Show()</c> 让页面真的可见、<c>Button.PerformClick()</c>
    /// 走点击链路、<c>RefreshData()</c> 是工具栏「刷新」调的那个入口）：
    /// ① 正常读取有行且不显示「重试」→ 弄坏磁盘上的配置目录 → 读取失败：清单不留旧行、标题与说明取
    /// <see cref="ProfileLoadState"/> 口径、三颗空态按钮里只有「重试」可见、编辑与探测禁用、状态行如实给原因；
    /// ② 磁盘仍坏时点「重试」：留在失败态（不能把「点了没反应」当成恢复）；
    /// ③ 修好磁盘后点「重试」：清单恢复、失败态退出、状态行不再说失败；
    /// ④ 恢复后选中一行：编辑与探测重新可用（失败态禁用过的入口真的回来了）；
    /// ⑤ 筛选状态下读取失败：仍说「读取配置失败」，不说成「没有匹配的配置」，行动按钮仍是「重试」。
    /// </para>
    /// <para>
    /// 失败是**真的**：往配置目录写一份坏 JSON，<c>ProfileStore.List</c> 逐份 <c>ReadJson</c> 时抛
    /// <c>JsonException</c>，走的就是产品里「资料目录被占用/权限不足/文件损坏」那一类读取失败的同一个
    /// catch 分支——不伪造异常类型、不替换服务实现。
    /// </para>
    /// </summary>
    private static void RunProfileLoadFailure(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var store = services.GetRequiredService<FileStore>();
        var compat = services.GetRequiredService<CompatService>();
        var cli = services.GetRequiredService<ApifoxCli>();
        var jobs = services.GetRequiredService<JobRunner>();
        var credentials = services.GetRequiredService<CredentialStore>();
        var sortMemory = new ListSortMemory(Path.Combine(dataRoot, "ui", "list-sort.json"));
        // 坏文件落在配置目录里：ProfileStore.List 枚举 *.json 后逐份反序列化，坏 JSON 即抛。
        var broken = Path.Combine(store.ProfilesDir, "broken.json");

        Form? host = null;
        ProjectsPage? page = null;
        try
        {
            RunAction("配置页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", () =>
            {
                host = new Form { Width = 1100, Height = 700, Text = "配置页冒烟" };
                page = new ProjectsPage(profiles, compat, cli, store, jobs, credentials, sortMemory);
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
                host.Show();
                Application.DoEvents();

                // 先正常读一次：清单里有行、失败态不显示——否则「失败态」可能只是初始状态。
                page.RefreshData();
                Application.DoEvents();
                var list = ProfileList(page);
                if (list.Items.Count == 0)
                {
                    throw new InvalidOperationException("正常读取后配置清单是空的：冒烟数据根里没有配置，失败态无从对照。");
                }
                if (ButtonOf(page, ProfileLoadState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("读取正常时「重试」按钮却可见（失败态没有随成功读取退出）。");
                }
                var healthy = list.Items.Count;

                // 弄坏磁盘上的配置目录，再走产品路径的读取入口（工具栏「刷新」调的就是 RefreshData）。
                File.WriteAllText(broken, "这不是 JSON");
                page.RefreshData();
                Application.DoEvents();

                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"读取失败后清单还留着 {list.Items.Count} 行旧数据：这一屏刚证明磁盘上的配置读不出来，旧行等于把不可信的内容当成现状。");
                }
                var (title, hint) = EmptyStateLabels(page);
                if (title.Text != ProfileLoadState.FailureTitle)
                {
                    throw new InvalidOperationException($"失败态标题是「{title.Text}」，期望「{ProfileLoadState.FailureTitle}」。");
                }
                if (hint.Text != ProfileLoadState.FailureHint)
                {
                    throw new InvalidOperationException($"失败态说明是「{hint.Text}」，期望「{ProfileLoadState.FailureHint}」。");
                }
                var visible = EmptyActionButtons(page).Where(button => button.Visible).ToArray();
                if (visible.Length != 1 || visible[0].Text != ProfileLoadState.RetryLabel)
                {
                    throw new InvalidOperationException(
                        "失败态的空态按钮不是「唯一行动按钮」：可见的是 " + string.Join("、", visible.Select(b => "「" + b.Text + "」")));
                }
                if (!visible[0].Enabled)
                {
                    throw new InvalidOperationException("失败态的「重试」按钮不可点：屏上没有任何下一步。");
                }
                if (ButtonOf(page, "编辑").Enabled || ButtonOf(page, "连接与写入探测").Enabled)
                {
                    throw new InvalidOperationException("读取失败后「编辑」或「连接与写入探测」仍可点：清单已空，它们没有可作用的对象。");
                }
                var status = StatusLabel(page);
                if (!status.Text.StartsWith(ProfileLoadState.FailureTitle + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望以「{ProfileLoadState.FailureTitle}：」开头（如实给出原因）。");
                }
                if (status.Text.Length <= ProfileLoadState.FailureTitle.Length + 1)
                {
                    throw new InvalidOperationException($"状态行只有标题没有原因：「{status.Text}」。");
                }
                _lines.Add($"     正常读取 {healthy} 行 → 弄坏配置目录后：标题「{title.Text}」、状态行「{status.Text}」");
            });

            RunAction("配置页：失败态下点「重试」仍失败则留在失败态（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出配置页。");
                // 磁盘仍是坏的：点「重试」等于再读一次，读还是失败——必须留在失败态，
                // 不能因为「点过了」就把失败态收掉（那等于把没读到的说成读到了）。
                Click(page, ProfileLoadState.RetryLabel);
                Application.DoEvents();
                var list = ProfileList(page);
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"磁盘仍坏，重试后清单却有 {list.Items.Count} 行。");
                }
                var (title, _) = EmptyStateLabels(page);
                if (title.Text != ProfileLoadState.FailureTitle)
                {
                    throw new InvalidOperationException($"磁盘仍坏，重试后标题变成「{title.Text}」，期望仍是「{ProfileLoadState.FailureTitle}」。");
                }
                if (!ButtonOf(page, ProfileLoadState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("磁盘仍坏，重试后「重试」按钮却不见了：用户没有第二次机会。");
                }
            });

            RunAction("配置页：修好磁盘后点「重试」恢复清单（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出配置页。");
                File.Delete(broken);
                var expected = profiles.List().Count;
                Click(page, ProfileLoadState.RetryLabel);
                Application.DoEvents();
                var list = ProfileList(page);
                if (list.Items.Count != expected)
                {
                    throw new InvalidOperationException($"修好磁盘后重试，清单有 {list.Items.Count} 行，磁盘上有 {expected} 个配置。");
                }
                if (ButtonOf(page, ProfileLoadState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                var (title, _) = EmptyStateLabels(page);
                if (title.Text == ProfileLoadState.FailureTitle)
                {
                    throw new InvalidOperationException("恢复后空态标题仍是「读取配置失败」。");
                }
                var status = StatusLabel(page);
                if (status.Text.StartsWith(ProfileLoadState.FailureTitle, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"恢复后状态行仍说失败：「{status.Text}」。");
                }
                _lines.Add($"     修好磁盘后重试：清单 {list.Items.Count} 行，状态行「{status.Text}」");
            });

            RunAction("配置页：恢复后选中行启用编辑与探测（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出配置页。");
                var list = ProfileList(page);
                if (list.Items.Count == 0) throw new InvalidOperationException("清单是空的，没有可选的行。");
                list.Items[0].Selected = true;
                list.Items[0].Focused = true;
                Application.DoEvents();
                if (!ButtonOf(page, "编辑").Enabled)
                {
                    throw new InvalidOperationException("恢复后选中一行，「编辑」仍不可点（失败态禁用过的入口没有回来）。");
                }
                if (!ButtonOf(page, "连接与写入探测").Enabled)
                {
                    throw new InvalidOperationException("恢复后选中一行，「连接与写入探测」仍不可点。");
                }
            });

            RunAction("配置页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出配置页。");
                var filter = FindFirst<TextBox>(page) ?? throw new InvalidOperationException("配置页里找不到筛选框。");
                filter.Text = "冒烟";
                Application.DoEvents();
                File.WriteAllText(broken, "这不是 JSON");
                page.RefreshData();
                Application.DoEvents();
                var (title, _) = EmptyStateLabels(page);
                // 失败态优先：这一屏连「有没有配置」都还不知道，说成「没有匹配的配置」是把读不出来说成读到了空。
                if (title.Text != ProfileLoadState.FailureTitle)
                {
                    throw new InvalidOperationException(
                        $"筛选状态下读取失败，空态标题是「{title.Text}」，期望「{ProfileLoadState.FailureTitle}」（不得说成「{ListFilter.ProfileNoMatchTitle}」）。");
                }
                var visible = EmptyActionButtons(page).Where(button => button.Visible).ToArray();
                if (visible.Length != 1 || visible[0].Text != ProfileLoadState.RetryLabel)
                {
                    throw new InvalidOperationException(
                        "筛选状态下读取失败，行动按钮不是「重试」：可见的是 " + string.Join("、", visible.Select(b => "「" + b.Text + "」")));
                }
            });
        }
        finally
        {
            // 坏文件必须清掉：留着它，后面每一段读配置都会失败（冒烟器自己造的坑自己填）。
            try
            {
                if (File.Exists(broken)) File.Delete(broken);
            }
            catch (IOException)
            {
                // 删不掉只影响后续段，不影响本段判定；不掩盖本段已经记下的失败。
            }
            page?.Dispose();
            host?.Dispose();
        }
    }

    /// <summary>配置页的配置清单（页面里唯一的 <c>ListView</c>）。</summary>
    private static ListView ProfileList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("配置页里找不到配置清单。");

    /// <summary>
    /// 空态面板（清单区里叠在清单上的那一块）：从「重试」按钮往上找到那个直接装着空态布局的面板。
    /// 按控件树定位而不是按文字找——空态标题的文字会随状态变化，位置不会。
    /// </summary>
    private static Panel EmptyPanel(Control root)
    {
        var retry = ButtonOf(root, ProfileLoadState.RetryLabel);
        for (Control? node = retry; node is not null; node = node.Parent)
        {
            if (node is Panel panel && panel.Controls.OfType<TableLayoutPanel>().Any()) return panel;
        }
        throw new InvalidOperationException("配置页里找不到空态面板（「重试」按钮不在空态里）。");
    }

    /// <summary>
    /// 空态里的标题与说明两行：按字号区分（标题档大于正文档），不按控件顺序——
    /// 顺序是布局细节，字号是 DESIGN.md §3 的档位承诺。
    /// </summary>
    private static (Label Title, Label Hint) EmptyStateLabels(Control root)
    {
        var labels = FindAll<Label>(EmptyPanel(root)).ToArray();
        if (labels.Length < 2)
        {
            throw new InvalidOperationException($"空态面板里只有 {labels.Length} 个文字标签，期望标题与说明两个。");
        }
        var title = labels.OrderByDescending(label => label.Font.SizeInPoints).First();
        var hint = labels.First(label => !ReferenceEquals(label, title));
        return (title, hint);
    }

    /// <summary>
    /// 空态的行动按钮组（「新建配置」「清空筛选」「重试」三颗同在一个 Flow 里，同一时刻只显示一颗）：
    /// 从「重试」按钮的父容器取，量的是「这一屏给了几个下一步」。
    /// </summary>
    private static Button[] EmptyActionButtons(Control root)
    {
        var retry = ButtonOf(root, ProfileLoadState.RetryLabel);
        var flow = retry.Parent ?? throw new InvalidOperationException("「重试」按钮没有父容器。");
        return flow.Controls.OfType<Button>().ToArray();
    }

    /// <summary>
    /// 任务页读取失败态的真实控件交互（第一百五十三轮，控制仓简报 P1-3）。
    /// <para>
    /// 存在理由：第一百五十二轮把「读取失败 → 唯一行动按钮「重试」」推广到任务页 / 报告 / 差异确认三屏，
    /// 但证据只到 Linux 侧判定真值表与源码切片——清单留不留旧行、详情区收不收、按钮点不点得动、
    /// 点完是否真的重读、恢复后详情动作能否用，全都可以是坏的而 Linux 全绿、冒烟照样 PASS。
    /// 本段与第一百五十一轮的 <see cref="RunProfileLoadFailure"/> 同一形状（失败 → 重试 → 恢复闭环），
    /// 只是换成任务页：任务页是主链路第二步，它的失败态此前从未被真实控件走过。
    /// </para>
    /// </summary>
    private static void RunTaskLoadFailure(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var store = services.GetRequiredService<FileStore>();
        var sortMemory = new ListSortMemory(Path.Combine(dataRoot, "ui", "list-sort.json"));
        // 坏任务目录：TaskStore.List 枚举任务目录后逐份读 task.json，坏 JSON 即抛。
        // 走的是产品里「任务目录被占用/权限不足/文件损坏」那一类读取失败的同一个 catch 分支。
        var brokenDir = Path.Combine(store.TasksDir, "broken-task");
        var brokenFile = Path.Combine(brokenDir, "task.json");

        Form? host = null;
        TasksPage? page = null;
        try
        {
            RunAction("任务页：读取失败时清单区换成失败态并给出唯一行动按钮（真实控件）", () =>
            {
                host = new Form { Width = 1200, Height = 760, Text = "任务页冒烟" };
                page = new TasksPage(
                    tasks,
                    services.GetRequiredService<SourceScanService>(),
                    services.GetRequiredService<JobRunner>(),
                    services.GetRequiredService<ProfileStore>(),
                    services.GetRequiredService<DraftService>(),
                    services.GetRequiredService<CommentPlanService>(),
                    services.GetRequiredService<PropagationService>(),
                    services.GetRequiredService<BuildService>(),
                    services.GetRequiredService<ContractReviewService>(),
                    services.GetRequiredService<CheckService>(),
                    services.GetRequiredService<DecisionService>(),
                    services.GetRequiredService<SupplementService>(),
                    services.GetRequiredService<ReportExportService>(),
                    services.GetRequiredService<SyncPlanService>(),
                    services.GetRequiredService<SyncExecutor>(),
                    sortMemory);
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
                host.Show();
                Application.DoEvents();

                // 先正常读一次：清单里有行、失败态不显示——否则「失败态」可能只是初始状态。
                page.RefreshData();
                Application.DoEvents();
                var list = TaskList(page);
                if (list.Items.Count == 0)
                {
                    throw new InvalidOperationException("正常读取后任务清单是空的：冒烟数据根里没有任务，失败态无从对照。");
                }
                if (ButtonOf(page, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("读取正常时「重试」按钮却可见（失败态没有随成功读取退出）。");
                }
                var healthy = list.Items.Count;

                // 弄坏磁盘上的任务目录，再走产品路径的读取入口（工具栏「刷新」调的就是 RefreshData）。
                Directory.CreateDirectory(brokenDir);
                File.WriteAllText(brokenFile, "这不是 JSON");
                page.RefreshData();
                Application.DoEvents();

                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException(
                        $"读取失败后清单还留着 {list.Items.Count} 行旧数据：这一屏刚证明磁盘上的任务读不出来，旧行等于把不可信的内容当成现状。");
                }
                var (title, hint) = EmptyStateLabels(page);
                if (title.Text != LoadFailureState.Tasks.Title)
                {
                    throw new InvalidOperationException($"失败态标题是「{title.Text}」，期望「{LoadFailureState.Tasks.Title}」。");
                }
                if (hint.Text != LoadFailureState.Tasks.Hint)
                {
                    throw new InvalidOperationException($"失败态说明是「{hint.Text}」，期望「{LoadFailureState.Tasks.Hint}」。");
                }
                var visible = EmptyActionButtons(page).Where(button => button.Visible).ToArray();
                if (visible.Length != 1 || visible[0].Text != LoadFailureState.RetryLabel)
                {
                    throw new InvalidOperationException(
                        "失败态的空态按钮不是「唯一行动按钮」：可见的是 " + string.Join("、", visible.Select(b => "「" + b.Text + "」")));
                }
                if (!visible[0].Enabled)
                {
                    throw new InvalidOperationException("失败态的「重试」按钮不可点：屏上没有任何下一步。");
                }
                // 清单空了，详情区没有可作用的对象：报告入口必须跟着停用（不能对着空详情导出报告）。
                if (ButtonOf(page, "查看报告与导出").Enabled)
                {
                    throw new InvalidOperationException("读取失败后「查看报告与导出」仍可点：清单已空，它没有可作用的任务。");
                }
                var status = StatusLabel(page);
                if (!status.Text.StartsWith(LoadFailureState.Tasks.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行是「{status.Text}」，期望以「{LoadFailureState.Tasks.Title}：」开头（如实给出原因）。");
                }
                if (status.Text.Length <= LoadFailureState.Tasks.Title.Length + 1)
                {
                    throw new InvalidOperationException($"状态行只有标题没有原因：「{status.Text}」。");
                }
                _lines.Add($"     正常读取 {healthy} 行 → 弄坏任务目录后：标题「{title.Text}」、状态行「{status.Text}」");
            });

            RunAction("任务页：失败态下点「重试」仍失败则留在失败态（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                // 磁盘仍是坏的：点「重试」等于再读一次，读还是失败——必须留在失败态，
                // 不能因为「点过了」就把失败态收掉（那等于把没读到的说成读到了）。
                Click(page, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var list = TaskList(page);
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"磁盘仍坏，重试后清单却有 {list.Items.Count} 行。");
                }
                var (title, _) = EmptyStateLabels(page);
                if (title.Text != LoadFailureState.Tasks.Title)
                {
                    throw new InvalidOperationException($"磁盘仍坏，重试后标题变成「{title.Text}」，期望仍是「{LoadFailureState.Tasks.Title}」。");
                }
                if (!ButtonOf(page, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("磁盘仍坏，重试后「重试」按钮却不见了：用户没有第二次机会。");
                }
            });

            RunAction("任务页：修好磁盘后点「重试」恢复清单（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                Directory.Delete(brokenDir, recursive: true);
                var expected = tasks.List().Count;
                Click(page, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var list = TaskList(page);
                if (list.Items.Count != expected)
                {
                    throw new InvalidOperationException($"修好磁盘后重试，清单有 {list.Items.Count} 行，磁盘上有 {expected} 个任务。");
                }
                if (ButtonOf(page, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                var (title, _) = EmptyStateLabels(page);
                if (title.Text == LoadFailureState.Tasks.Title)
                {
                    throw new InvalidOperationException("恢复后空态标题仍是「读取任务失败」。");
                }
                var status = StatusLabel(page);
                if (status.Text.StartsWith(LoadFailureState.Tasks.Title, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"恢复后状态行仍说失败：「{status.Text}」。");
                }
                _lines.Add($"     修好磁盘后重试：清单 {list.Items.Count} 行，状态行「{status.Text}」");
            });

            RunAction("任务页：恢复后选中行启用详情动作（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var list = TaskList(page);
                if (list.Items.Count == 0) throw new InvalidOperationException("清单是空的，没有可选的行。");
                list.Items[0].Selected = true;
                list.Items[0].Focused = true;
                Application.DoEvents();
                if (!ButtonOf(page, "查看报告与导出").Enabled)
                {
                    throw new InvalidOperationException("恢复后选中一行，「查看报告与导出」仍不可点（失败态禁用过的入口没有回来）。");
                }
            });

            RunAction("任务页：筛选状态下读取失败仍说失败不说筛到零行（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var filter = FindFirst<TextBox>(page) ?? throw new InvalidOperationException("任务页里找不到筛选框。");
                filter.Text = "冒烟";
                Application.DoEvents();
                Directory.CreateDirectory(brokenDir);
                File.WriteAllText(brokenFile, "这不是 JSON");
                page.RefreshData();
                Application.DoEvents();
                var (title, _) = EmptyStateLabels(page);
                // 失败态优先：这一屏连「有没有任务」都还不知道，说成「没有匹配的任务」是把读不出来说成读到了空。
                if (title.Text != LoadFailureState.Tasks.Title)
                {
                    throw new InvalidOperationException(
                        $"筛选状态下读取失败，空态标题是「{title.Text}」，期望「{LoadFailureState.Tasks.Title}」（不得说成「{ListFilter.TaskNoMatchTitle}」）。");
                }
                var visible = EmptyActionButtons(page).Where(button => button.Visible).ToArray();
                if (visible.Length != 1 || visible[0].Text != LoadFailureState.RetryLabel)
                {
                    throw new InvalidOperationException(
                        "筛选状态下读取失败，行动按钮不是「重试」：可见的是 " + string.Join("、", visible.Select(b => "「" + b.Text + "」")));
                }
            });
        }
        finally
        {
            // 坏目录必须清掉：留着它，后面每一段读任务都会失败（冒烟器自己造的坑自己填）。
            try
            {
                if (Directory.Exists(brokenDir)) Directory.Delete(brokenDir, recursive: true);
            }
            catch (IOException)
            {
                // 删不掉只影响后续段，不影响本段判定；不掩盖本段已经记下的失败。
            }
            page?.Dispose();
            host?.Dispose();
        }
    }

    /// <summary>任务页的任务清单（页面里唯一的 <c>ListView</c>）。</summary>
    private static ListView TaskList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("任务页里找不到任务清单。");

    /// <summary>
    /// 报告对话框读取失败态的真实控件交互（第一百五十三轮，控制仓简报 P1-3）。
    /// <para>
    /// 存在理由：第一百五十二轮给报告对话框补了「重试」，但「四个导出入口一起停用」「预览区收起」
    /// 「点重试真的重读」这三件事只有源码切片证据。本段用真实控件走一遍：
    /// 正常打开 → 任务目录读不出来 → 点重试仍失败 → 修好后点重试恢复。
    /// 失败是真的：把任务目录整个挪走，报告生成读任务时抛 <c>UserException</c>，
    /// 走的就是产品里「任务不存在/任务目录读不出来」的同一个分支。
    /// </para>
    /// </summary>
    private static void RunReportsLoadFailure(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var exports = services.GetRequiredService<ReportExportService>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "报告失败态冒烟" });
        var taskDir = Path.GetDirectoryName(tasks.FilePath(task.Id, TaskFiles.Manifest))
            ?? throw new InvalidOperationException("任务目录路径取不出来。");
        var parked = taskDir + ".parked";

        ReportsDialog? dialog = null;
        try
        {
            RunAction("报告对话框：正常打开时预览可见且不显示「重试」（真实控件）", () =>
            {
                dialog = new ReportsDialog(exports, task);
                dialog.Show();
                Application.DoEvents();
                var preview = FindFirst<TextBox>(dialog) ?? throw new InvalidOperationException("报告对话框里找不到预览区。");
                if (!preview.Visible)
                {
                    throw new InvalidOperationException("正常打开时预览区不可见（对话框停在提示态）。");
                }
                if (preview.Text.Length == 0)
                {
                    throw new InvalidOperationException("正常打开时预览是空的：报告没有生成出来。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("正常打开时「重试」按钮却可见（失败态没有随成功读取退出）。");
                }
                foreach (var label in new[] { "导出 Markdown", "导出 HTML", "导出 JSON", "前端交接说明" })
                {
                    if (!ButtonOf(dialog, label).Enabled)
                    {
                        throw new InvalidOperationException($"正常打开时「{label}」不可点：预览已生成，导出入口应当可用。");
                    }
                }
                _lines.Add($"     正常打开：预览 {preview.Text.Length} 字，四个导出入口可用");
                dialog.Dispose();
                dialog = null;
            });

            RunAction("报告对话框：任务目录读不出来时换成失败态并停用四个导出（真实控件）", () =>
            {
                // 把任务目录整个挪走：任务文件读不出来，报告生成必然失败（真实失败，不伪造异常）。
                Directory.Move(taskDir, parked);
                dialog = new ReportsDialog(exports, task);
                dialog.Show();
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Visible)
                {
                    throw new InvalidOperationException("任务目录读不出来时提示区却不可见：失败没有任何交代。");
                }
                if (!hint.Text.StartsWith(LoadFailureState.Reports.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"失败提示是「{hint.Text}」，期望以「{LoadFailureState.Reports.Title}：」开头。");
                }
                if (!hint.Text.Contains(LoadFailureState.RetryLabel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"失败提示没有指名「{LoadFailureState.RetryLabel}」：「{hint.Text}」。");
                }
                var preview = FindFirst<TextBox>(dialog) ?? throw new InvalidOperationException("报告对话框里找不到预览区。");
                if (preview.Visible)
                {
                    throw new InvalidOperationException("失败态下预览区仍可见：屏上同时有「失败」和一份预览，用户不知道该信哪个。");
                }
                var retry = ButtonOf(dialog, LoadFailureState.RetryLabel);
                if (!retry.Visible || !retry.Enabled)
                {
                    throw new InvalidOperationException("失败态下「重试」不可见或不可点：屏上没有任何下一步。");
                }
                foreach (var label in new[] { "导出 Markdown", "导出 HTML", "导出 JSON", "前端交接说明" })
                {
                    if (ButtonOf(dialog, label).Enabled)
                    {
                        throw new InvalidOperationException($"失败态下「{label}」仍可点：预览都没生成出来，导出的只会是同一份失败。");
                    }
                }
                _lines.Add($"     任务目录挪走后：提示「{hint.Text}」");
            });

            RunAction("报告对话框：仍读不出来时点「重试」留在失败态（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出报告对话框。");
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Text.StartsWith(LoadFailureState.Reports.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"仍读不出来，重试后提示变成「{hint.Text}」，期望仍是失败态。");
                }
                if (!ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("仍读不出来，重试后「重试」按钮却不见了：用户没有第二次机会。");
                }
            });

            RunAction("报告对话框：修好任务目录后点「重试」恢复预览与导出（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出报告对话框。");
                Directory.Move(parked, taskDir);
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var preview = FindFirst<TextBox>(dialog) ?? throw new InvalidOperationException("报告对话框里找不到预览区。");
                if (!preview.Visible || preview.Text.Length == 0)
                {
                    throw new InvalidOperationException("修好任务目录后重试，预览仍不可见或为空。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                if (HintLabel(dialog).Visible)
                {
                    throw new InvalidOperationException("恢复后提示区仍可见：屏上还留着上一次的失败文案。");
                }
                foreach (var label in new[] { "导出 Markdown", "导出 HTML", "导出 JSON", "前端交接说明" })
                {
                    if (!ButtonOf(dialog, label).Enabled)
                    {
                        throw new InvalidOperationException($"恢复后「{label}」仍不可点：失败态禁用过的入口没有回来。");
                    }
                }
                _lines.Add($"     修好任务目录后重试：预览 {preview.Text.Length} 字，四个导出入口恢复");
            });
        }
        finally
        {
            // 挪走的目录必须挪回来：留着它，后面每一段读这个任务都会失败。
            try
            {
                if (Directory.Exists(parked) && !Directory.Exists(taskDir)) Directory.Move(parked, taskDir);
            }
            catch (IOException)
            {
                // 挪不回来只影响后续段，不影响本段判定；不掩盖本段已经记下的失败。
            }
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 任务日志对话框读取失败态的真实控件交互（第一百五十八轮，控制仓简报 P1-4 的可达部分）。
    /// <para>
    /// 存在理由：第一百五十七轮给任务日志补了「读取失败 → 唯一行动按钮『重试』」，但证据只有
    /// 源码切片与 Linux 侧文案断言——清单留不留旧行、按钮点不点得动、点完是否真的重读、
    /// 恢复后计数行与关键词框是否回来，全都可以是坏的而 Linux 全绿、冒烟照样 PASS。
    /// 本段用真实控件走完整闭环：正常读取 → 打开时键盘归属 → 读不出来 → 重试仍读不出来 → 解锁后重试恢复。
    /// </para>
    /// <para>
    /// 失败是真的，不是伪造：用 <c>FileShare.None</c> 独占打开 <c>journal.jsonl</c>，产品里
    /// <c>FileStore.ReadJsonLines</c> 的 <c>File.ReadLines</c> 会抛 <c>IOException</c>，走的就是
    /// 「文件被占用 / 权限不足」那一类读取失败的<b>同一个 catch 分支</b>——不替换服务实现、不伪造异常类型。
    /// 注意不能靠写坏 JSON 造失败：<c>ReadJsonLines</c> 逐行吞 <c>JsonException</c>（进程中断会留半行），
    /// 写坏一行只会得到「没有记录」的空态，那是另一条分支。
    /// </para>
    /// <para>
    /// 排序状态（简报 P1-4 的另一半）在真实路径上<b>不可达</b>：排序只能在有行时点列头设置，
    /// 而重读只发生在打开与「重试」两处、「重试」只在失败态可见——「先排序、再失败、再重试」这条序列
    /// 在界面上走不出来。故本段不伪造该序列，改由 <c>JournalLoadFailureTests</c> 在源码切片上钉住
    /// 「失败态与成功读取都不重置排序状态、表头标记按建列快照重算不叠加」这条不变量。
    /// </para>
    /// </summary>
    private static void RunJournalLoadFailure(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "任务日志失败态冒烟" });
        var journalPath = tasks.FilePath(task.Id, TaskFiles.Journal);
        // 日志由产品自己的写入点造（JournalLines 单一口径）：注释回写的意图与结果各一行，不手拼 JSON。
        FileStore.AppendJsonLine(journalPath,
            JournalLines.CommentApplyIntentLine(DateTimeOffset.Now, "p1", "Orders/OrdersController.cs", "h1", "h2"));
        FileStore.AppendJsonLine(journalPath,
            JournalLines.CommentApplyResultLine(DateTimeOffset.Now, "p1", "Orders/OrdersController.cs", "applied", null));

        JournalDialog? dialog = null;
        FileStream? hold = null;
        try
        {
            RunAction("任务日志：正常读取有行、不显示「重试」、关键词框可用（真实控件）", () =>
            {
                dialog = new JournalDialog(tasks, task);
                dialog.Show();
                Application.DoEvents();
                var list = JournalList(dialog);
                if (list.Items.Count != 2)
                {
                    throw new InvalidOperationException($"日志里有两行记录，清单却有 {list.Items.Count} 行。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("正常读取时「重试」可见：失败态没有随成功读取退出。");
                }
                if (!KeywordBox(dialog).Enabled)
                {
                    throw new InvalidOperationException("正常读取时关键词框不可用：有记录可筛。");
                }
                _lines.Add($"     正常读取：清单 {list.Items.Count} 行，关键词框可用");
            });

            RunAction("任务日志：打开时键盘落在清单上（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出任务日志对话框。");
                // 第八十八轮纪律：只读清单打开即选中一行，键盘也要在清单上（否则方向键动的是别的控件、
                // Ctrl+C 复制不到那一行）。落点由 OnShown 的 Ui.KeepKeyboardOnQueue 交，本段读真实焦点。
                var list = JournalList(dialog);
                if (!list.Focused)
                {
                    throw new InvalidOperationException(
                        "对话框打开后键盘不在清单上：方向键与 Ctrl+C 都落不到那一行（ActiveControl="
                        + (dialog.ActiveControl?.GetType().Name ?? "null") + "）。");
                }
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != list.Items.Count - 1)
                {
                    throw new InvalidOperationException(
                        "打开后选中的不是最后一条：选中 " + string.Join("、", list.SelectedIndices.Cast<int>())
                        + $"，共 {list.Items.Count} 行。");
                }
                _lines.Add($"     打开时键盘在清单上，选中第 {list.SelectedIndices[0] + 1} 行（共 {list.Items.Count} 行）");
            });

            RunAction("任务日志：日志读不出来时换成失败态并给出「重试」（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出任务日志对话框。");
                dialog.Dispose();
                dialog = null;
                // 独占打开：产品读日志时 File.ReadLines 抛 IOException，走真实失败分支。
                hold = new FileStream(journalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                dialog = new JournalDialog(tasks, task);
                dialog.Show();
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Visible)
                {
                    throw new InvalidOperationException("日志读不出来时提示区却不可见：失败没有任何交代。");
                }
                if (!hint.Text.StartsWith(LoadFailureState.Journal.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"失败提示是「{hint.Text}」，期望以「{LoadFailureState.Journal.Title}：」开头。");
                }
                if (!hint.Text.Contains(LoadFailureState.RetryLabel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"失败提示没有指名「{LoadFailureState.RetryLabel}」：「{hint.Text}」。");
                }
                // 第一百五十八轮：原因来自 Exception.Message，.NET 的 IOException 是英文句子、以半角句点收尾，
                // 拼说明时若无条件补「。」就会出现「.。」——这一句是错误路径上用户唯一能读到的解释，
                // 双标点就在最显眼处。这里判的是真实控件上渲染出来的那段文字，不是口径函数的返回值。
                foreach (var doubled in new[] { ".。", "。。", "。；", "；；" })
                {
                    if (hint.Text.Contains(doubled, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException($"失败提示里出现双标点「{doubled}」：「{hint.Text}」。");
                    }
                }
                var retry = ButtonOf(dialog, LoadFailureState.RetryLabel);
                if (!retry.Visible || !retry.Enabled)
                {
                    throw new InvalidOperationException("失败态下「重试」不可见或不可点：屏上没有任何下一步。");
                }
                var list = JournalList(dialog);
                if (list.Items.Count != 0)
                {
                    throw new InvalidOperationException($"日志读不出来，清单却留着 {list.Items.Count} 行旧记录。");
                }
                if (KeywordBox(dialog).Enabled)
                {
                    throw new InvalidOperationException("失败态下关键词框仍可用：整份日志都读不出来，敲字筛不到任何东西。");
                }
                _lines.Add($"     日志读不出来：提示「{hint.Text}」");
            });

            RunAction("任务日志：仍读不出来时点「重试」留在失败态（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出任务日志对话框。");
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Text.StartsWith(LoadFailureState.Journal.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"仍读不出来，重试后提示变成「{hint.Text}」，期望仍是失败态。");
                }
                if (!ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("仍读不出来，重试后「重试」按钮却不见了：用户没有第二次机会。");
                }
            });

            RunAction("任务日志：解锁后点「重试」恢复清单、计数行与关键词框（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出任务日志对话框。");
                hold?.Dispose();
                hold = null;
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var list = JournalList(dialog);
                if (list.Items.Count != 2)
                {
                    throw new InvalidOperationException($"解锁后重试，清单有 {list.Items.Count} 行，日志里有 2 行。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                if (!KeywordBox(dialog).Enabled)
                {
                    throw new InvalidOperationException("恢复后关键词框仍不可用：有记录可筛。");
                }
                var counts = JournalCountsLabel(dialog);
                if (counts.Text != JournalEntries.CountsLine(2))
                {
                    throw new InvalidOperationException(
                        $"恢复后计数行是「{counts.Text}」，期望「{JournalEntries.CountsLine(2)}」。");
                }
                var hint = HintLabel(dialog);
                if (hint.Visible)
                {
                    throw new InvalidOperationException($"恢复后提示区仍可见：「{hint.Text}」。");
                }
                _lines.Add($"     解锁后重试：清单 {list.Items.Count} 行，计数行「{counts.Text}」");
            });

            RunAction("任务日志：恢复后仍选中最后一条、Enter/Esc 仍是「关闭」（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出任务日志对话框。");
                var list = JournalList(dialog);
                if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != list.Items.Count - 1)
                {
                    throw new InvalidOperationException(
                        "恢复后选中的不是最后一条：选中 " + string.Join("、", list.SelectedIndices.Cast<int>())
                        + $"，共 {list.Items.Count} 行。");
                }
                // 新增一颗「重试」不改对话框的 Enter/Esc 语义（§6「对话框 Enter=确认、Esc=取消」）。
                if (dialog.AcceptButton is not Button accept || accept.Text != "关闭")
                {
                    throw new InvalidOperationException("恢复后 Enter 的落点不是「关闭」：新增按钮改动了对话框语义。");
                }
                if (dialog.CancelButton is not Button cancel || cancel.Text != "关闭")
                {
                    throw new InvalidOperationException("恢复后 Esc 的落点不是「关闭」。");
                }
                _lines.Add($"     恢复后选中第 {list.SelectedIndices[0] + 1} 行，Enter/Esc 仍是「关闭」");
            });
        }
        finally
        {
            hold?.Dispose();
            dialog?.Dispose();
        }
    }

    /// <summary>任务日志对话框里的记录清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView JournalList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("任务日志对话框里找不到记录清单。");

    /// <summary>任务日志对话框的计数行（铺满、右对齐的那一格）。</summary>
    private static Label JournalCountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Dock == DockStyle.Fill && l.TextAlign == ContentAlignment.MiddleRight)
        ?? throw new InvalidOperationException("任务日志对话框里找不到计数行。界面文案：" + LabelTexts(root));

    /// <summary>
    /// 差异确认对话框读取失败态的真实控件交互（第一百五十三轮，控制仓简报 P1-3）。
    /// <para>
    /// 存在理由：这一屏有两种失败态（检查结果读不出来、已保存决策读不出来）与两种「不是失败」的状态
    /// （尚未检查、没有需要处理的条目）。第一百五十二轮给两种失败态都加了「重试」，并刻意让后两种
    /// <b>不显示</b>「重试」（重试解决不了它们，下一步在任务页与筛选器上）。这四条门控此前只有源码切片证据，
    /// 本段用真实控件逐条走：失败 → 重试仍失败 → 修好恢复 → 另一种失败 → 修好恢复 → 非失败态不显示重试。
    /// </para>
    /// </summary>
    private static void RunDecisionsLoadFailure(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var decisionSaves = services.GetRequiredService<DecisionService>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "差异确认失败态冒烟" });
        var diffPath = tasks.FilePath(task.Id, TaskFiles.Diff);
        var decisionsPath = tasks.FilePath(task.Id, TaskFiles.Decisions);

        DecisionsDialog? dialog = null;
        try
        {
            RunAction("差异确认：检查结果读不出来时换成失败态并给出「重试」（真实控件）", () =>
            {
                File.WriteAllText(diffPath, "这不是 JSON");
                dialog = new DecisionsDialog(tasks, task, decisionSaves);
                dialog.Show();
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Visible)
                {
                    throw new InvalidOperationException("检查结果读不出来时提示区却不可见：失败没有任何交代。");
                }
                if (!hint.Text.StartsWith(LoadFailureState.Decisions.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"失败提示是「{hint.Text}」，期望以「{LoadFailureState.Decisions.Title}：」开头。");
                }
                if (!hint.Text.Contains(LoadFailureState.RetryLabel, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"失败提示没有指名「{LoadFailureState.RetryLabel}」：「{hint.Text}」。");
                }
                var retry = ButtonOf(dialog, LoadFailureState.RetryLabel);
                if (!retry.Visible || !retry.Enabled)
                {
                    throw new InvalidOperationException("失败态下「重试」不可见或不可点：屏上没有任何下一步。");
                }
                var filter = FindFirst<ComboBox>(dialog) ?? throw new InvalidOperationException("差异确认里找不到筛选框。");
                if (filter.Enabled)
                {
                    throw new InvalidOperationException("失败态下筛选框仍可用：清单里没有条目可筛。");
                }
                _lines.Add($"     检查结果读不出来：提示「{hint.Text}」");
            });

            RunAction("差异确认：仍读不出来时点「重试」留在失败态（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出差异确认对话框。");
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Text.StartsWith(LoadFailureState.Decisions.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"仍读不出来，重试后提示变成「{hint.Text}」，期望仍是失败态。");
                }
                if (!ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("仍读不出来，重试后「重试」按钮却不见了：用户没有第二次机会。");
                }
            });

            RunAction("差异确认：修好检查结果后点「重试」退出失败态（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出差异确认对话框。");
                // 一份合法的空检查结果：读得出来、没有条目——失败态必须退出，且不能把「没有条目」说成失败。
                File.WriteAllText(diffPath, "{}");
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (hint.Text.StartsWith(LoadFailureState.Decisions.Title, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"修好检查结果后重试，提示仍是失败：「{hint.Text}」。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                _lines.Add($"     修好检查结果后重试：提示「{hint.Text}」");
                dialog.Dispose();
                dialog = null;
            });

            RunAction("差异确认：已保存决策读不出来时指名「重试」且说明保存停用（真实控件）", () =>
            {
                File.WriteAllText(decisionsPath, "这不是 JSON");
                dialog = new DecisionsDialog(tasks, task, decisionSaves);
                dialog.Show();
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Text.StartsWith(LoadFailureState.Decisions.SavedTitle + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"已保存决策读不出来时提示是「{hint.Text}」，期望以「{LoadFailureState.Decisions.SavedTitle}：」开头。");
                }
                if (!hint.Text.Contains(LoadFailureState.Decisions.SavedHint, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"提示没有说明保存已停用：「{hint.Text}」。");
                }
                var retry = ButtonOf(dialog, LoadFailureState.RetryLabel);
                if (!retry.Visible || !retry.Enabled)
                {
                    throw new InvalidOperationException("已保存决策读不出来时「重试」不可见或不可点。");
                }
                _lines.Add($"     已保存决策读不出来：提示「{hint.Text}」");
            });

            RunAction("差异确认：修好已保存决策后点「重试」退出失败态（真实控件）", () =>
            {
                if (dialog is null) throw new InvalidOperationException("上一段没有产出差异确认对话框。");
                File.Delete(decisionsPath);
                Click(dialog, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (hint.Text.StartsWith(LoadFailureState.Decisions.SavedTitle, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"修好已保存决策后重试，提示仍是失败：「{hint.Text}」。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("恢复后「重试」按钮仍可见：失败态没有随成功读取退出。");
                }
                dialog.Dispose();
                dialog = null;
            });

            RunAction("差异确认：尚未检查时不显示「重试」（重试解决不了它，真实控件）", () =>
            {
                // 没有检查结果不是失败：下一步在任务页点「开始检查」，同一时刻只给一个下一步。
                File.Delete(diffPath);
                dialog = new DecisionsDialog(tasks, task, decisionSaves);
                dialog.Show();
                Application.DoEvents();
                var hint = HintLabel(dialog);
                if (!hint.Visible || !hint.Text.StartsWith("尚未检查", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"没有检查结果时提示是「{hint.Text}」，期望以「尚未检查」开头。");
                }
                if (ButtonOf(dialog, LoadFailureState.RetryLabel).Visible)
                {
                    throw new InvalidOperationException("「尚未检查」不是失败态，却显示了「重试」：同一屏给了两个下一步。");
                }
            });
        }
        finally
        {
            dialog?.Dispose();
        }
    }

    /// <summary>
    /// 极端数据下的详情区与列表（第一百五十九轮，控制仓简报 P1-4，具名 pass：harden）。
    /// <para>
    /// 存在理由：主链路的正常数据都是短名字、短路径、短错误消息——而真实使用里三样都会很长：
    /// 任务名是用户随手写的（可以是一整句中文）、路径前缀是仓库里的真实目录（可以嵌套很深）、
    /// 错误消息来自 <c>Exception.Message</c>（.NET 的英文句子 + 文件全路径，动辄两三百字符）。
    /// 详情区的标签是 <c>AutoSize = true</c> 的单行标签，放进 <c>TableLayoutPanel</c> 的百分比列里——
    /// 文字比格子宽时 WinForms 直接裁掉，既没有换行也没有省略号，用户看到的是半句话。
    /// 这类缺陷在 Linux 侧全绿（源码切片看不出「文字比格子宽」），只有真实控件量得出来。
    /// </para>
    /// <para>
    /// 判定口径：详情区里每一行文字，要么完整显示，要么**有可见的截断提示**（换行后变高、或省略号）。
    /// 静默裁掉即判失败——「看不见的那半句」正是用户判断这次检查覆盖了什么、失败在哪的依据。
    /// </para>
    /// </summary>
    private static void RunDetailBreak(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var store = services.GetRequiredService<FileStore>();
        var sortMemory = new ListSortMemory(Path.Combine(dataRoot, "ui", "list-sort.json"));

        // 极端数据：超长中文任务名（120 字）、超长路径前缀（两段各 60 字的中文目录名）。
        var longName = string.Concat(Enumerable.Repeat("超长任务名", 24));
        var longPath = "Probe.Api/" + new string('深', 60) + "/" + new string('层', 60) + "/";
        var profile = profiles.Save(new ProjectProfile
        {
            Name = "极端数据配置",
            WorkspaceRoot = dataRoot,
            ApiProject = "Probe.Api/Probe.Api.csproj",
            AllowedSourcePaths = [longPath],
        }, isNew: true);
        var task = tasks.Create(new CreateTaskRequest
        {
            ProfileId = profile.Id,
            Name = longName,
            PathPrefixes = [longPath],
        });

        Form? host = null;
        TasksPage? page = null;
        try
        {
            RunAction("任务页：超长中文任务名在详情区换行而不是被静默裁掉（真实控件）", () =>
            {
                host = new Form { Width = 1200, Height = 760, Text = "极端数据冒烟" };
                page = new TasksPage(
                    tasks,
                    services.GetRequiredService<SourceScanService>(),
                    services.GetRequiredService<JobRunner>(),
                    profiles,
                    services.GetRequiredService<DraftService>(),
                    services.GetRequiredService<CommentPlanService>(),
                    services.GetRequiredService<PropagationService>(),
                    services.GetRequiredService<BuildService>(),
                    services.GetRequiredService<ContractReviewService>(),
                    services.GetRequiredService<CheckService>(),
                    services.GetRequiredService<DecisionService>(),
                    services.GetRequiredService<SupplementService>(),
                    services.GetRequiredService<ReportExportService>(),
                    services.GetRequiredService<SyncPlanService>(),
                    services.GetRequiredService<SyncExecutor>(),
                    sortMemory);
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
                host.Show();
                Application.DoEvents();
                // 走壳的真实入口：OnPageShown 才按当前宽度布置分割（详情区取设计宽 420px）。
                // 只调 RefreshData 的话分割距离停在 WinForms 默认 50px，详情区只剩一条缝，
                // 量出来的「换行」是窄缝里的假象，不是产品在真机上的样子。
                page.OnPageShown();
                Application.DoEvents();

                var list = TaskList(page);
                var item = list.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as CheckTask)?.Id == task.Id)
                    ?? throw new InvalidOperationException("任务清单里找不到刚建的超长名字任务。");
                item.Selected = true;
                item.Focused = true;
                Application.DoEvents();

                var grid = DetailGrid(page);
                var name = FindAll<Label>(grid).FirstOrDefault(l => l.Text.StartsWith("超长任务名", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("详情区里找不到任务名那一行。");
                if (name.Text != longName)
                {
                    throw new InvalidOperationException($"详情区的任务名被改写了（{name.Text.Length} 字，期望 {longName.Length} 字）：详情区不得截断数据本身。");
                }
                AssertNoSilentClipping(grid, "超长中文任务名");
                _lines.Add($"     任务名 {longName.Length} 字：标签 {name.Width}×{name.Height}（单行高 {name.Font.Height}），详情网格宽 {grid.ClientSize.Width}");
            });

            RunAction("任务页：超长路径的范围行在详情区换行而不是被静默裁掉（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var grid = DetailGrid(page);
                var range = FindAll<Label>(grid).FirstOrDefault(l => l.Text.StartsWith(TaskSummary.RangePrefix, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("详情区里找不到范围行。");
                if (!range.Text.Contains(longPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("范围行里没有完整的路径前缀：详情区把路径截短了，用户看不出这次检查覆盖哪个目录。");
                }
                AssertNoSilentClipping(grid, "超长路径的范围行");
                _lines.Add($"     范围行 {range.Text.Length} 字：标签 {range.Width}×{range.Height}（单行高 {range.Font.Height}）");
            });

            RunAction("任务页：超长错误消息在状态行有省略号而不是被静默裁掉（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var status = StatusLabel(page);
                if (!status.AutoEllipsis)
                {
                    throw new InvalidOperationException("状态行没有开 AutoEllipsis：超长错误消息会被静默裁掉，用户看不到「还有下文」。");
                }
                // 真的把一条超长错误消息放上去（形状与产品路径一致：标题 + 原因），量它是否被裁。
                var longMessage = LoadFailureState.FailureStatus(
                    LoadFailureState.Tasks.Title,
                    "The process cannot access the file '" + new string('x', 240) + ".json' because it is being used by another process.");
                // 直接放进状态行控件：这一段量的是「状态行遇到超长文字怎么呈现」，
                // 不是「哪条路径会产出超长文字」（后者由失败态各段覆盖）。
                // 高度按「放文字前后是否一样」判，不按「是否等于一个字号高」判——状态行是
                // 字号高 + 上下 Xs 内边距的固定占位（Ui.StatusLine），本来就比一个字号高，
                // 拿字号高当尺子会把正常高度判成「被撑高」（第一百五十九轮 wine 实测：34px 被误判）。
                var heightBefore = status.Height;
                status.Text = longMessage;
                Application.DoEvents();
                var preferred = TextRenderer.MeasureText(status.Text, status.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                if (preferred.Width <= status.Width)
                {
                    throw new InvalidOperationException($"超长错误消息（{status.Text.Length} 字）没有超过状态行宽度（{status.Width}px），这一段量不到裁切。");
                }
                if (status.Height != heightBefore)
                {
                    throw new InvalidOperationException(
                        $"状态行被文字撑高了（{heightBefore}px → {status.Height}px）：它是固定占位的一行，撑高会推动工具栏布局。");
                }
                _lines.Add($"     超长错误消息 {status.Text.Length} 字：文字 {preferred.Width}px、状态行 {status.Width}×{status.Height}px、AutoEllipsis={status.AutoEllipsis}");
            });

            RunAction("任务页：超长任务名下列宽按「标识列吸收剩余宽度」口径落地（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var list = TaskList(page);
                // 口径（DESIGN §6「列表行」/ ListColumns）：可视宽度放得下固定列合计时，标识列吸收剩余宽度、
                // 列合计对齐可视宽度；放不下时标识列回退自身设计宽度、横向滚动条如实出现。
                // 这里按这两支独立重述一遍（不调 ListColumns.FillWidth，否则断言与被测代码同源、恒真）。
                var widths = list.Columns.Cast<ColumnHeader>().Select(column => column.Width).ToArray();
                var fixedSum = widths.Skip(1).Sum();
                var sum = widths.Sum();
                var client = list.ClientSize.Width;
                var expected = client >= fixedSum + widths[0] && client - fixedSum > widths[0]
                    ? client
                    : Math.Max(client, fixedSum + widths[0]);
                if (sum != expected)
                {
                    throw new InvalidOperationException(
                        $"超长任务名下列宽合计 {sum} 与口径算出的 {expected} 不一致（可视宽度 {client}、固定列合计 {fixedSum}、标识列 {widths[0]}）："
                        + "标识列没有吸收剩余宽度，列尾被横向滚动条藏住。");
                }
                _lines.Add($"     清单列宽合计 {sum}（可视宽度 {client}、固定列合计 {fixedSum}、标识列 {widths[0]}，{list.Columns.Count} 列）");
            });

            RunAction("任务页：把分隔条拖到最右后标识列吸收剩余宽度、列合计对齐可视宽度（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                // 上一段量的是「放不下」那一支（默认宽度下可视宽度 770 < 固定列合计 672 + 标识列 200）。
                // 这一支要量「放得下」：把分隔条拖到最右（详情区收到最小宽 360），列表拿到剩余宽度。
                // 用拖分隔条而不是把窗口拉宽——窗口宽度受屏幕限制（wine 下 Xvfb 1280 宽，
                // 第一百五十九轮实测 host.Width = 1600 被钳到 1280，列表只多出 92px，量不到这一支）；
                // 拖分隔条是用户真能做的动作，走的也是同一条 Resize → FitColumns 接线。
                var split = FindAll<SplitContainer>(page).FirstOrDefault()
                    ?? throw new InvalidOperationException("任务页里找不到分割面板。");
                var list = TaskList(page);
                var before = list.ClientSize.Width;
                // 先把窗口拉到屏幕工作区宽度（不写死 1280：wine 的 Xvfb 与真机屏幕不同），
                // 再把分隔条拖到最右——两步都是用户真能做的动作。
                if (host is not null)
                {
                    host.Width = Screen.PrimaryScreen?.WorkingArea.Width ?? host.Width;
                    Application.DoEvents();
                }
                split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Width - split.Panel2MinSize - split.SplitterWidth);
                Application.DoEvents();

                var widths = list.Columns.Cast<ColumnHeader>().Select(column => column.Width).ToArray();
                var fixedSum = widths.Skip(1).Sum();
                var sum = widths.Sum();
                var client = list.ClientSize.Width;
                if (client <= before)
                {
                    throw new InvalidOperationException($"拖分隔条后列表没有变宽（{before}px → {client}px），这一段量不到「放得下」那一支。");
                }
                if (client < fixedSum + widths[0])
                {
                    throw new InvalidOperationException(
                        $"拖分隔条后可视宽度 {client} 仍放不下固定列合计 {fixedSum} + 标识列设计宽 {widths[0]}"
                        + $"（屏幕工作区宽 {Screen.PrimaryScreen?.WorkingArea.Width}、分割面板宽 {split.Width}）：这一段量不到「放得下」那一支。");
                }
                if (sum != client)
                {
                    throw new InvalidOperationException(
                        $"拖分隔条后列宽合计 {sum} 与可视宽度 {client} 不一致：标识列没有吸收剩余宽度，列尾留空白或出横向滚动条。");
                }
                if (widths[0] != client - fixedSum)
                {
                    throw new InvalidOperationException(
                        $"拖分隔条后标识列 {widths[0]}px ≠ 可视宽度 {client} − 固定列合计 {fixedSum}：剩余宽度没有全部给到标识列。");
                }
                _lines.Add($"     拖分隔条后：可视宽度 {client}（原 {before}）、标识列 {widths[0]}、列合计 {sum}");
            });
        }
        finally
        {
            page?.Dispose();
            host?.Dispose();
        }
    }

    /// <summary>
    /// 状态行「查看完整错误」入口的真实控件交互（第一百六十二轮挂账、第一百六十四轮接线的补口）。
    /// <para>
    /// 存在理由：第一百五十九轮开的 <c>AutoEllipsis</c> 只解决「末尾有下文」，读全与复制这一半
    /// 此前只有源码切片证据。这一段量的是真实窗口宽度下四件事：短句放得下不给入口（给了就是
    /// 添一步空动作）、真实失败长句放不下要给、点开看到的正文与状态行逐字相同、关掉回到原处。
    /// </para>
    /// <para>
    /// <b>「放不下」怎么造出来</b>：不手写一句超长文字，走产品的真实失败路径（弄坏任务目录 →
    /// <c>RefreshData</c>），原因文字与宽度都由磁盘和窗口决定；判定在冒烟侧按口径独立重述一遍
    /// （<c>可用宽度 &gt; 0 且 首选宽度 &gt; 可用宽度</c>，不调 <c>StatusLineText</c>，
    /// 否则断言与被测代码同源、恒真）。逐档收窄窗口这一步也是用户真能做的动作——它在测
    /// 「宽度变了要重判」这条接线，不是在凑一个放不下的数字。
    /// </para>
    /// </summary>
    private static void RunStatusFullText(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var store = services.GetRequiredService<FileStore>();
        var sortMemory = new ListSortMemory(Path.Combine(dataRoot, "ui", "list-sort.json"));
        var brokenDir = Path.Combine(store.TasksDir, "broken-status");
        var brokenFile = Path.Combine(brokenDir, "task.json");

        Form? host = null;
        TasksPage? page = null;
        var toolbarHeightBefore = 0;
        var baselineWidth = 0;
        var firstStepOffered = false;
        try
        {
            RunAction("状态行：放得下的短句不给「查看完整错误」入口（真实控件）", () =>
            {
                host = new Form { Width = 1200, Height = 760, Text = "状态行完整错误冒烟" };
                page = new TasksPage(
                    tasks,
                    services.GetRequiredService<SourceScanService>(),
                    services.GetRequiredService<JobRunner>(),
                    services.GetRequiredService<ProfileStore>(),
                    services.GetRequiredService<DraftService>(),
                    services.GetRequiredService<CommentPlanService>(),
                    services.GetRequiredService<PropagationService>(),
                    services.GetRequiredService<BuildService>(),
                    services.GetRequiredService<ContractReviewService>(),
                    services.GetRequiredService<CheckService>(),
                    services.GetRequiredService<DecisionService>(),
                    services.GetRequiredService<SupplementService>(),
                    services.GetRequiredService<ReportExportService>(),
                    services.GetRequiredService<SyncPlanService>(),
                    services.GetRequiredService<SyncExecutor>(),
                    sortMemory);
                page.Dock = DockStyle.Fill;
                host.Controls.Add(page);
                host.Show();
                Application.DoEvents();
                page.OnPageShown();
                page.RefreshData();
                Application.DoEvents();

                // 真实短句：筛到行的计数句（空状态行「放得下」是废话，量不到这一支）。
                var filter = FindFirst<TextBox>(page) ?? throw new InvalidOperationException("任务页里找不到筛选框。");
                filter.Text = "冒烟";
                Application.DoEvents();

                // 先把窗口拉到屏幕工作区宽（用户真能做的动作；第一百五十九轮实测写死 1600 会被屏宽钳住）：
                // 「放得下就不给入口」这一支只有在最宽的一档才可能量到。
                host.Width = Screen.PrimaryScreen?.WorkingArea.Width ?? host.Width;
                baselineWidth = host.Width;
                Application.DoEvents();

                var status = StatusLabel(page);
                if (status.Text.Length == 0)
                {
                    throw new InvalidOperationException("筛选后状态行没有文字，这一段量不到短句那一支。");
                }
                var entry = ButtonOf(page, StatusLineText.FullTextButton);
                // 基准行高在换文字之前取：后面几档要判的是「加一颗入口」这单独一件事有没有推动这一行。
                toolbarHeightBefore = ToolbarOf(status).Height;
                var (preferred, available, shouldOffer) = StatusEntryRule(status);
                if (entry.Visible != shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"短句「{status.Text}」（文字 {preferred}px、入口不占位时这一行 {available}px）下入口可见={entry.Visible}，"
                        + $"按口径该「{(shouldOffer ? "给" : "不给")}」：点开看到的是同一句话时不该给入口，读不到全文时不该没有入口。");
                }
                _lines.Add($"     短句「{status.Text}」：文字 {preferred}px、入口不占位时这一行 {available}px、入口可见={entry.Visible}、"
                    + $"工具栏行高 {toolbarHeightBefore}px（窗口 {baselineWidth}px）"
                    + (shouldOffer ? "——这一档连最宽窗口都放不下这一句，「放得下不给入口」那一支本轮在这一档没量到（工具栏按钮列不随缩放让位，见 docs/NOTES.md 挂账）" : ""));
            });

            RunAction("状态行：真实失败长句放不下时给出入口，且同一行宽上按文字内容重判（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                Directory.CreateDirectory(brokenDir);
                File.WriteAllText(brokenFile, "这不是 JSON");
                page.RefreshData();
                Application.DoEvents();

                var status = StatusLabel(page);
                var entry = ButtonOf(page, StatusLineText.FullTextButton);
                if (!status.Text.StartsWith(LoadFailureState.Tasks.Title + "：", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"状态行不是真实失败原因：「{status.Text}」。");
                }
                // 逐档收窄（用户真能做的动作）：第一档取上一段那个最宽窗口——只有同宽才谈得上
                // 「加一颗入口」这一单独一件事的行高对照，换一档宽度比的是另一件事（按钮列会不会换行）。
                var offered = false;
                var heightHeld = false;
                var widths = "";
                var rowWidths = new HashSet<int>();
                var firstStep = true;
                foreach (var width in new[] { baselineWidth, 1200, 1100, 1000, 900, 800, 640 })
                {
                    host!.Width = width;
                    Application.DoEvents();
                    var (preferred, available, shouldOffer) = StatusEntryRule(status);
                    if (entry.Visible != shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"窗口 {host.Width}px 下状态行文字 {preferred}px、入口不占位时这一行 {available}px，按口径该「{(shouldOffer ? "给" : "不给")}」入口，实际可见={entry.Visible}。");
                    }
                    if (shouldOffer)
                    {
                        if (!heightHeld)
                        {
                            if (firstStep)
                            {
                                AssertToolbarHeightHeld(status, toolbarHeightBefore, host.Width);
                            }
                            heightHeld = true;
                        }
                        offered = true;
                    }
                    else if (offered)
                    {
                        throw new InvalidOperationException(
                            $"窗口收到 {host.Width}px（文字 {preferred}px、入口不占位时这一行 {available}px）后入口又收起：判定随宽度非单调，界面会抖。");
                    }
                    widths += $"{host.Width}:{preferred}/{available}{(shouldOffer ? "给" : "不给")} ";
                    rowWidths.Add(available);
                    if (firstStep) firstStepOffered = shouldOffer;
                    firstStep = false;
                }
                if (!offered)
                {
                    throw new InvalidOperationException(
                        $"逐档收窄到 640px 都没量到「放不下」（{widths.TrimEnd()}）：这一族宽度上真实失败原因都放得下，这一段量不到入口该出现的那一支。");
                }
                if (!entry.Enabled || !entry.CanSelect)
                {
                    throw new InvalidOperationException("入口出现了却不可点。");
                }
                // 串=窗口实际宽:文字宽/入口不占位时的行宽→给不给。同一行宽下换文字就要换判定，这是本段判的内容；
                // 「窗口变窄要不要改判」只有当这一行的宽真的随窗口变过时才量得到，没变就如实写没量到。
                var rowWidthNote = rowWidths.Count == 1
                    ? "（这一行的宽七档未随窗口变，「窗口变窄要不要改判」这一档没量到，重判只由文字内容触发）"
                    : "（这一行的宽随窗口变过：" + string.Join("/", rowWidths.Order()) + "）";
                _lines.Add($"     真实失败原因 {status.Text.Length} 字：{widths.TrimEnd()}{rowWidthNote}，"
                    + $"工具栏行高 {ToolbarOf(status).Height}px（基准 {toolbarHeightBefore}px，同宽档对照{(heightHeld ? "已判" : "未判")}）");
            });

            RunAction("状态行：点入口打开的对话框正文与状态行逐字相同，关掉回到原处（真实控件）", () =>
            {
                if (page is null) throw new InvalidOperationException("上一段没有产出任务页。");
                var status = StatusLabel(page);
                var expected = status.Text;
                var openedTitle = "";
                var openedBody = "";
                var closeButtonEnabled = false;
                using var probe = new System.Windows.Forms.Timer { Interval = 30 };
                var dismissed = false;
                // ShowDialog 是模态的：Click 会卡在对话框里，核对与关闭只能排在它泵起来的消息循环内做。
                // 关它走的也是对话框自己那颗「关闭」的 PerformClick，不直接调 Close()（那绕过了产品路径）。
                probe.Tick += (_, _) =>
                {
                    if (dismissed) return;
                    var dialog = Application.OpenForms.OfType<StatusTextDialog>().FirstOrDefault();
                    if (dialog is null) return;
                    openedTitle = dialog.Text;
                    openedBody = dialog.Message;
                    var close = ButtonOf(dialog, "关闭");
                    closeButtonEnabled = close.Enabled;
                    close.PerformClick();
                    dismissed = true;
                };
                probe.Start();
                try
                {
                    Click(page, StatusLineText.FullTextButton);
                }
                finally
                {
                    probe.Stop();
                }
                Application.DoEvents();
                if (!dismissed)
                {
                    throw new InvalidOperationException("点了入口却没等到完整错误对话框（没打开、或打开的不是这一型）。");
                }
                if (openedBody != expected)
                {
                    throw new InvalidOperationException(
                        $"对话框正文与状态行不逐字相同（正文 {openedBody.Length} 字 / 状态行 {expected.Length} 字）：读全这一步给出的是另一句话或半句话。");
                }
                if (openedTitle != StatusLineText.FullTextHeading)
                {
                    throw new InvalidOperationException($"对话框标题是「{openedTitle}」，取的不是口径文件那一份。");
                }
                if (!closeButtonEnabled)
                {
                    throw new InvalidOperationException("对话框里那颗「关闭」不可点。");
                }
                // 关掉回到原处：状态行那句话与入口都保持原样（这一屏不因为读过一次而换内容）。
                var (backPreferred, backAvailable, backShouldOffer) = StatusEntryRule(status);
                var backEntry = ButtonOf(page, StatusLineText.FullTextButton);
                if (status.Text != expected || backEntry.Visible != backShouldOffer)
                {
                    throw new InvalidOperationException(
                        $"关掉对话框后这一屏变了样：状态行文字是否同一句={status.Text == expected}、入口可见={backEntry.Visible}"
                        + $"（文字 {backPreferred}px、入口不占位时这一行 {backAvailable}px、按口径该「{(backShouldOffer ? "给" : "不给")}」）。");
                }
                _lines.Add($"     点开读全：标题「{openedTitle}」、正文 {openedBody.Length} 字与状态行逐字相同");
            });

            RunAction("状态行：修好磁盘恢复短句后按同一口径重判（放得下时入口收起，真实控件）", () =>
            {
                if (page is null || host is null) throw new InvalidOperationException("上一段没有产出任务页。");
                Directory.Delete(brokenDir, recursive: true);
                host.Width = baselineWidth;
                Click(page, LoadFailureState.RetryLabel);
                Application.DoEvents();
                var status = StatusLabel(page);
                var entry = ButtonOf(page, StatusLineText.FullTextButton);
                if (status.Text.StartsWith(LoadFailureState.Tasks.Title, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"修好磁盘后状态行仍说失败：「{status.Text}」。");
                }
                var (preferred, available, shouldOffer) = StatusEntryRule(status);
                if (entry.Visible != shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"恢复后文字 {preferred}px、入口不占位时这一行 {available}px，入口可见={entry.Visible}，按口径该「{(shouldOffer ? "给" : "不给")}」。");
                }
                AssertToolbarHeightHeld(status, toolbarHeightBefore, host.Width);
                _lines.Add($"     恢复后「{status.Text}」：文字 {preferred}px、入口不占位时这一行 {available}px、入口可见={entry.Visible}、"
                    + $"工具栏行高 {ToolbarOf(status).Height}px"
                    + (!shouldOffer && firstStepOffered ? "（同一窗口宽下失败态给过、恢复后不给——入口收得回来）" : ""));
            });
        }
        finally
        {
            // 坏目录必须清掉：留着它，后面每一段读任务都会失败（冒烟器自己造的坑自己填）。
            try
            {
                if (Directory.Exists(brokenDir)) Directory.Delete(brokenDir, recursive: true);
            }
            catch (IOException)
            {
                // 删不掉只影响后续段，不影响本段已经记下的失败。
            }
            page?.Dispose();
            host?.Dispose();
        }
    }

    /// <summary>
    /// 第一百六十六轮接入的 15 处对话框状态行——挑三处代表性屏走同一套四段（真实控件）。
    /// <para>
    /// <b>挑哪三屏、各代表什么</b>：① <c>ProfileDialog</c>——它嵌在 <c>ProjectsPage.cs</c> 里，
    /// 与外层页面同文件不同类（上一轮挂账点名的就是这一类）；② <c>ProfileAdvancedDialog</c>——
    /// 独立文件、宽 700px 的一屏；③ <c>SupplementsDialog</c>——长文案来自真实保存失败（磁盘上的
    /// 任务文件读不出来），不是拼出来的长句。
    /// </para>
    /// <para>
    /// <b>短句与长句都走产品自己的分支</b：同一颗按钮，换个输入状态就是 gate 短句（「请先填写项目 ID」
    /// 「请输入接口 ID」「已还原为本接口已保存的内容。」），填上值就是失败长句（<c>Exception.Message</c>
    /// 经脱敏后再拼上「点『…』再试一次」）。不手写一句超长文字——那样量到的是冒烟器的想象力。
    /// </para>
    /// <para>
    /// <b>放不下那一支量不到时如实写</b>：对话框比页面工具栏宽得多，某些档上真实失败原因确实放得下；
    /// 那一段就记「这一档放得下，四段里的『点开读全』在本屏这一档没量到」，不改判、不伪造。
    /// 三档缩放（96/120/192）各跑一次，合起来看哪一支在哪一档量到。
    /// </para>
    /// </summary>
    private static void RunDialogStatusFullText(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var cli = services.GetRequiredService<ApifoxCli>();
        var credentials = services.GetRequiredService<CredentialStore>();
        var supplementSaves = services.GetRequiredService<SupplementService>();
        var store = services.GetRequiredService<FileStore>();
        // 工作区必须先落盘：ProfileStore.Validate 拒绝不存在的 API 项目，而这一句在 RunAction 之外——
        // 让它抛出来，整个冒烟进程会以未处理异常终止，后面每一段都不会跑（wine 实测踩过）。
        CreateProbeWorkspace(Path.Combine(dataRoot, "Probe10.Api"));
        var profile = profiles.Save(new ProjectProfile
        {
            Name = "状态行入口配置",
            WorkspaceRoot = dataRoot,
            ApiProject = "Probe10.Api/Probe10.Api.csproj",
            AllowedSourcePaths = ["Probe10.Api/"],
        }, isNew: true);

        // ── 通用四段：一屏一次，短句不给 → 长失败给 → 点开读全 → 回短句收起 ──
        void Steps(
            string screen,
            Func<Form> open,
            Action<Form> shortFirst,
            Action<Form> shortAgain,
            Action<Form> toLong,
            Action? cleanup = null)
        {
            Form? dialog = null;
            var shortText = "";
            var longOffered = false;
            try
            {
                RunAction($"对话框状态行：{screen}——短句放得下，不给「查看完整错误」入口（真实控件）", () =>
                {
                    dialog = open();
                    dialog.Show();
                    Application.DoEvents();
                    shortFirst(dialog);
                    Application.DoEvents();
                    var status = StatusLabel(dialog);
                    shortText = status.Text;
                    if (shortText.Length == 0)
                    {
                        throw new InvalidOperationException("短句这一支量不到：状态行是空的（这一步没有落到状态行上）。");
                    }
                    var entry = ButtonOf(dialog, StatusLineText.FullTextButton);
                    var (preferred, available, shouldOffer) = StatusEntryRule(status);
                    if (entry.Visible != shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"短句「{shortText}」（文字 {preferred}px、入口不占位时这一行 {available}px）下入口可见={entry.Visible}，"
                            + $"按口径该「{(shouldOffer ? "给" : "不给")}」：点开看到的是同一句话时不该给入口。");
                    }
                    if (shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"短句「{shortText}」在这一行（{available}px）就判放不下（文字 {preferred}px）：入口会在最不该出现的时候出现。");
                    }
                    _lines.Add($"     短句「{shortText}」：文字 {preferred}px、入口不占位时这一行 {available}px、入口可见=False");
                });

                RunAction($"对话框状态行：{screen}——真实失败长句按同一口径重判（放不下才给入口）", () =>
                {
                    if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                    toLong(dialog);
                    var status = StatusLabel(dialog);
                    if (status.Text == shortText)
                    {
                        throw new InvalidOperationException($"换了输入状态，状态行却还是那一句「{shortText}」：失败没有落到状态行上。");
                    }
                    var entry = ButtonOf(dialog, StatusLineText.FullTextButton);
                    var (preferred, available, shouldOffer) = StatusEntryRule(status);
                    if (entry.Visible != shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"失败原因 {status.Text.Length} 字（文字 {preferred}px、入口不占位时这一行 {available}px）下入口可见={entry.Visible}，"
                            + $"按口径该「{(shouldOffer ? "给" : "不给")}」：读不到全文时不该没有入口。");
                    }
                    longOffered = shouldOffer;
                    _lines.Add($"     真实失败原因 {status.Text.Length} 字：文字 {preferred}px、入口不占位时这一行 {available}px、"
                        + $"入口可见={entry.Visible}"
                        + (shouldOffer ? "" : "——这一档这一屏放得下，四段里的「点开读全」在本档这一屏没量到（不伪造一个放不下的数字）"));
                });

                RunAction($"对话框状态行：{screen}——点入口读全，正文与状态行逐字相同", () =>
                {
                    if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                    if (!longOffered)
                    {
                        _lines.Add("     本档这一屏没有入口可点（上一段已如实记下宽度数字），这一段跳过");
                        return;
                    }
                    ReadFullTextAndCheck(dialog);
                });

                RunAction($"对话框状态行：{screen}——回到短句后按同一口径重判，入口收得回来", () =>
                {
                    if (dialog is null) throw new InvalidOperationException("上一段没有产出对话框。");
                    shortAgain(dialog);
                    Application.DoEvents();
                    var status = StatusLabel(dialog);
                    var entry = ButtonOf(dialog, StatusLineText.FullTextButton);
                    var (preferred, available, shouldOffer) = StatusEntryRule(status);
                    if (entry.Visible != shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"回到短句「{status.Text}」（文字 {preferred}px、入口不占位时这一行 {available}px）后入口可见={entry.Visible}，"
                            + $"按口径该「{(shouldOffer ? "给" : "不给")}」。");
                    }
                    if (longOffered && shouldOffer)
                    {
                        throw new InvalidOperationException(
                            $"同一屏长失败给过入口、回到短句后仍挂着（文字 {preferred}px / 这一行 {available}px）：判定收不回来。");
                    }
                    _lines.Add($"     回到短句「{status.Text}」：文字 {preferred}px、入口不占位时这一行 {available}px、入口可见={entry.Visible}"
                        + (longOffered ? "（同一屏长失败给过、回短句后不给——入口收得回来）" : ""));
                });
            }
            finally
            {
                // 坏磁盘那一段必须还原：留着它，后面每一段读任务都会失败（冒烟器自己造的坑自己填）。
                cleanup?.Invoke();
                dialog?.Dispose();
            }
        }

        var profilesDir = store.ProfilesDir;
        var profileJson = Path.Combine(profilesDir, profile.Id + ".json");
        var profileBackup = File.ReadAllText(profileJson);

        // 屏 1：嵌在页面文件里的 ProfileDialog——短句走入口自己的门控（项目 ID 没填），
        // 长句走真实磁盘失败（配置文件读不出来，_profiles.Get 在产品代码里抛出并落进状态行）。
        // 不走 CLI：本容器里 node 在 PATH 上但没有登录态，CLI 子进程会挂住（wine 实测：60 秒等不到结果）。
        // 门与失败读的都是「Apifox 项目 ID」那一格（与「读取项目列表」同行），不是「分支」那一格：
        // BranchGate 按 _projectId.Text 判，填错一格门永远开着，等到的只是那句门控文案（wine 实测踩过）。
        Steps("新建/编辑配置对话框（嵌在 ProjectsPage.cs 里）",
            () => new ProfileDialog(profiles, cli, credentials, editing: profile),
            dialog =>
            {
                ComboInRowOf(dialog, "读取项目列表").Text = "";
                Click(dialog, "读取分支");
            },
            dialog =>
            {
                ComboInRowOf(dialog, "读取项目列表").Text = "";
                Click(dialog, "读取分支");
            },
            dialog =>
            {
                File.WriteAllText(profileJson, "这不是 JSON");
                ComboInRowOf(dialog, "读取项目列表").Text = "123456";
                Click(dialog, "读取分支");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith(ApifoxPicker.BranchesFailedPrefix, StringComparison.Ordinal),
                    "读取分支的真实失败原因");
            },
            cleanup: () => File.WriteAllText(profileJson, profileBackup));

        // 屏 2：独立文件的补充说明对话框——长句来自真实保存失败（任务文件读不出来）。
        // 短句两支不同：打开时那一句本来就是短开场白；失败之后回到短句走「放弃修改」。
        // 「放弃修改」的门控是「本接口有未保存修改」（MarkDirty 只在编辑位文字与已保存内容不同
        // 时置位）——不先真的改一笔，保存失败后那颗钮仍是禁用的，第四段就没有回到短句的真实出口。
        CheckTask? supplementTask = null;
        var taskJson = "";
        var taskJsonBackup = "";
        Steps("补充说明对话框（独立文件，长文案来自真实保存失败）",
            () =>
            {
                supplementTask = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "状态行入口任务" });
                tasks.Write(supplementTask.Id, TaskFiles.Diff, SupplementFixture());
                taskJson = tasks.FilePath(supplementTask.Id, TaskStore.TaskFile);
                taskJsonBackup = File.ReadAllText(taskJson);
                return new SupplementsDialog(tasks, supplementTask, supplementSaves);
            },
            dialog => { /* 打开时的那句就是这一屏的真实短句，不为了「有动作」多点一下 */ },
            dialog => Click(dialog, "放弃修改"),
            dialog =>
            {
                // 先真的改一笔：编辑位文字经 TextChanged → MarkDirty 置为未保存修改，
                // 「放弃修改」随之可用（第四段回到短句要靠它）。
                var editor = FindAll<TextBox>(dialog).FirstOrDefault(t => t.Multiline)
                    ?? throw new InvalidOperationException("补充说明对话框里没有找到编辑位输入框。");
                editor.Text = editor.Text + "本轮补一句";
                File.WriteAllText(taskJson, "这不是 JSON");
                Click(dialog, "保存本接口补充");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith("保存补充失败：", StringComparison.Ordinal),
                    "保存补充的真实失败原因");
            },
            cleanup: () =>
            {
                // 坏文件必须还原：留着它，后面每一段读任务都会失败。
                if (taskJsonBackup.Length > 0) File.WriteAllText(taskJson, taskJsonBackup);
            });

        // 屏 3：宽度受限的一屏（模型服务 620×470，状态行可用宽度只有约 566px）。
        // 短句走同一颗「确定」的字段校验拒绝，长句走真实磁盘失败（保存密钥先核对配置存在）。
        Steps("模型服务对话框（独立文件，620px 宽 = 状态行最窄的一屏）",
            () => new GenerationDialog(credentials, profiles, profile, profileExists: true),
            dialog =>
            {
                BoxInRowLabeled(dialog, "超时秒数").Text = "abc";
                Click(dialog, "确定");
            },
            dialog =>
            {
                BoxInRowLabeled(dialog, "超时秒数").Text = "abc";
                Click(dialog, "确定");
            },
            dialog =>
            {
                BoxInRowLabeled(dialog, "超时秒数").Text = "";
                File.WriteAllText(profileJson, "这不是 JSON");
                Click(dialog, "保存密钥");
                PumpUntilCondition(
                    () => StatusLabel(dialog).Text.StartsWith("保存密钥失败：", StringComparison.Ordinal),
                    "保存密钥的真实失败原因");
            },
            cleanup: () => File.WriteAllText(profileJson, profileBackup));
    }

    /// <summary>
    /// 15 处对话框状态行的「放得下不给 / 放不下给 / 回短句收起」三段，按冒烟清单逐屏走一遍
    /// （第一百七十六轮，简报 P0-1 的 R2 收口）。
    /// <para>
    /// <b>为什么要有这一段</b>：<see cref="RunDialogStatusFullText"/> 只挑了三处代表屏走完整四段，
    /// 其余 12 处此前只有 <c>StatusLineTextTests.EveryStatusLine_OnDisk_IsWrittenThroughTheHost</c>
    /// 的接线门——那门判的是「状态行经宿主写」，判不出「这一屏的入口显隐与判定一致」。
    /// 真机到位那天这张表要进 G-D5 复核清单，12 处里只要有一处实际放不下而入口没给，
    /// 用户就看不到完整失败原因（这正是「产品完整度」里最朴素的一条）。
    /// </para>
    /// <para>
    /// <b>为什么按清单走而不是再列一份屏单</b>：屏单从 <see cref="Cases"/> 读——新增对话框自动进这一段，
    /// 漏掉不会有第二份名单替它兜底（与 <c>demo.sh</c> 从逐屏表读行同一取舍）。没有状态行宿主的屏
    /// （<c>StatusTextDialog</c> 这类只读视图）如实记「这一屏没有状态行」，不算失败也不算通过。
    /// </para>
    /// <para>
    /// <b>长句用产品自己那一份拼装</b>：<c>LoadFailureState.FailureStatus</c> 是产品路径上真正落进状态行
    /// 的那一句（标题 + 脱敏后的 <c>Exception.Message</c>），不手写一句超长文字——那样量到的是
    /// 冒烟器的想象力。短句取「已保存」这类真实短句，不用空串（空串在判定里走的是另一支）。
    /// </para>
    /// <para>
    /// <b>量不到就如实写</b>：某一档这一屏放得下时，「放不下给」这一支在本档没量到，报告里逐屏写明，
    /// 不伪造一个放不下的数字（与 <see cref="RunDialogStatusFullText"/> 同一口径）。
    /// 状态行宽度为 0 时判失败而不是放过——那时「放不下」不是文字太长，是这一行还没有宽度，
    /// 拿它判会得到一条永远绿的假门（第一百六十一轮踩过同一形状）。
    /// </para>
    /// </summary>
    private static void RunDialogStatusSweep(IServiceProvider services, ProjectProfile profile, CheckTask task, DocSlot slot)
    {
        // 真实失败原因的形状：.NET 的 IOException 带文件全路径，动辄两三百字符。
        var longText = LoadFailureState.FailureStatus(
            LoadFailureState.Tasks.Title,
            "The process cannot access the file "
            + "'C:\\Users\\someone\\AppData\\Local\\ApifoxHelper\\tasks\\20261111-093000-ab12cd\\task.json' "
            + "because it is being used by another process. 关闭占用该文件的程序后点「重试」再读一次。");
        const string shortText = "已保存";

        var swept = 0;
        var offered = 0;
        var readFull = 0;
        var noStatus = new List<string>();
        var seen = new HashSet<Type>();
        foreach (var (name, build) in Cases(services, profile, task, slot))
        {
            using var control = build();
            if (control is not Form dialog) continue;
            // 同一类对话框在清单里可能出现两次（例如「默认确认委托」那一条也 new 了一次）：
            // 逐屏扫的是屏，不是用例，重复的只走一次。
            if (!seen.Add(dialog.GetType())) continue;
            var host = FindAll<StatusLineHost>(dialog).FirstOrDefault();
            if (host is null)
            {
                noStatus.Add(name);
                continue;
            }

            // 必须真的 Show：`Control.Visible` 读的是**有效可见性**，祖先没显示时它恒为 false——
            // 不 Show 就判入口显隐，量到的是「窗体还没显示」而不是「这一行放不下」（本轮实测：
            // 15 屏全部报「入口可见=False」，而按宽度该给入口）。Show 会触发各屏自己的加载，
            // 与 RunDialogStatusFullText 同一取舍：宁可多跑一次加载，也不拿一个恒假的读数当结论。
            dialog.Show();
            Application.DoEvents();

            var screen = name;
            RunAction($"对话框状态行逐屏：{screen}——短句放得下，不给「查看完整错误」入口（真实控件）", () =>
            {
                host.SetText(shortText, UiTokens.Text);
                Application.DoEvents();
                var (preferred, available, shouldOffer) = StatusEntryRule(host.Label);
                if (available <= 0)
                {
                    throw new InvalidOperationException(
                        $"这一屏的状态行没有宽度（{available}px）：量不到就不是「放得下」，别把它读成通过。");
                }
                if (host.FullTextButton.Visible != shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"短句「{shortText}」（文字 {preferred}px、入口不占位时这一行 {available}px）下入口可见={host.FullTextButton.Visible}，"
                        + $"按口径该「{(shouldOffer ? "给" : "不给")}」。");
                }
                if (shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"短句「{shortText}」在这一行（{available}px）就判放不下（文字 {preferred}px）：入口会在最不该出现的时候出现。");
                }
                swept++;
            });

            RunAction($"对话框状态行逐屏：{screen}——真实失败长句按同一口径重判（放不下才给入口）", () =>
            {
                host.SetText(longText, UiTokens.BadText);
                Application.DoEvents();
                var (preferred, available, shouldOffer) = StatusEntryRule(host.Label);
                if (host.FullTextButton.Visible != shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"失败原因 {longText.Length} 字（文字 {preferred}px、入口不占位时这一行 {available}px）下入口可见={host.FullTextButton.Visible}，"
                        + $"按口径该「{(shouldOffer ? "给" : "不给")}」：读不到全文时不该没有入口。");
                }
                if (shouldOffer) offered++;
                _lines.Add($"     {screen}：失败原因 {longText.Length} 字，文字 {preferred}px / 这一行 {available}px，"
                    + $"入口可见={host.FullTextButton.Visible}"
                    + (shouldOffer ? "" : "——这一档这一屏放得下，「放不下给」这一支在本档这一屏没量到（不伪造一个放不下的数字）"));
            });

            // 「点开读全」这一支：入口给出来之后，真的点它、真的在模态窗里核对正文与状态行逐字相同，
            // 再真的点那颗「关闭」（走产品路径，不直接 Close()）。
            // 这一支此前只在三处代表屏量过（第一百六十六轮），其余 12 处只由接线门判住——
            // 而「点开读全」按屏各不相同（同一句长失败，在 506px 的两屏放不下、在 922px 的几屏放得下），
            // 不逐屏点开就不知道哪一屏的用户真的读得到全文（README「已知限制」与 NOTES 都按实写了这个缺口）。
            RunAction($"对话框状态行逐屏：{screen}——点入口读全，正文与状态行逐字相同（真实控件）", () =>
            {
                if (!host.FullTextButton.Visible)
                {
                    // 放得下就没有入口：这一支在本档本屏没量到，如实写明，不伪造一个「点开了」。
                    var (_, rowWidth) = StatusTextWidths(host.Label);
                    _lines.Add($"     {screen}：这一档这一屏放得下（失败原因 {longText.Length} 字 / 这一行 {rowWidth}px），"
                        + "没有入口可点——「点开读全」这一支在本档本屏没量到（不伪造）");
                    return;
                }
                ReadFullTextAndCheck(dialog, host.Label);
                readFull++;
            });

            RunAction($"对话框状态行逐屏：{screen}——回到短句后入口收得回来", () =>
            {
                host.SetText(shortText, UiTokens.Text);
                Application.DoEvents();
                var (preferred, available, shouldOffer) = StatusEntryRule(host.Label);
                if (host.FullTextButton.Visible != shouldOffer)
                {
                    throw new InvalidOperationException(
                        $"回到短句「{shortText}」（文字 {preferred}px、这一行 {available}px）后入口可见={host.FullTextButton.Visible}，"
                        + $"按口径该「{(shouldOffer ? "给" : "不给")}」。");
                }
            });
        }

        _lines.Add($"     逐屏扫过 {swept} 屏有状态行的对话框；「放不下给」这一档量到 {offered} 屏；"
            + $"「点开读全」这一档量到 {readFull} 屏（放得下就没有入口可点，如实记没量到，不伪造）；"
            + $"没有状态行的屏（如实记，不算失败）：{(noStatus.Count == 0 ? "无" : string.Join("、", noStatus))}");
    }

    /// <summary>
    /// 提示对话框的「复制路径」按钮：第一遍真的走一遍（第一百八十五轮）。
    /// <para>
    /// 这一颗按钮是第一百八十三轮加的，当时只判了口径（三个标签同字数、给了才出现）与接线
    /// （按钮经 <c>Ui.CopyToClipboard</c> 写剪贴板），**按钮本身从来没有被点过**：冒烟构造清单里
    /// 那一屏根本没给 <c>copyText</c>，按钮压根没被构造出来；而「复制后粘贴出来的到底是不是屏幕上
    /// 那一行」当场没有 Windows 图形子系统量不了，当时连 Wine 也按约束退役了，这一支只能挂在
    /// G-D5 真机复核上（那一支的口径写在 <c>ApifoxHelper.Tests/NoticeCopyTests.cs</c> 的类注释里）。
    /// </para>
    /// <para>
    /// 现在 Linux 侧的模拟器又可用（wine 10.0 + Xvfb；本轮先单独探过：剪贴板写进去读得回来，
    /// 读回来的与写进去的逐字相同），这一支就可以在 Linux 上真的走一遍：真点那颗按钮、
    /// 真把剪贴板读回来逐字比对、真核对按钮宽度与旁边「关闭」的位置没有因为文字变化而跳位、
    /// 真点那颗「关闭」。**量到的是模拟器，不是真机**——按钮的实际观感、Tab 可达、用户在自己
    /// 机器上粘贴时的手感仍随 G-D5 真机复核。
    /// </para>
    /// <para>
    /// 这一段刻意不给「按钮点了之后剪贴板里是空的也算过」留口子：读回来必须与屏幕上那一行
    /// <b>逐字相同</b>（不多不少、不裁剪、不改写），否则就是「复制成功」这三个字说了假话。
    /// </para>
    /// </summary>
    private static void RunNoticeCopyClick(string dataRoot)
    {
        // 复制出去的就是屏幕上那一行：路径取真实存在的资料目录（冒烟自己建的那一个），不是拼出来的字符串。
        var path = dataRoot;
        var body = NoticeText.AboutBody(TaskStore.ToolVersion, RuntimeInformation.FrameworkDescription);

        // ① 没有可复制的东西时不该出现按钮：一颗点了什么也不会发生的按钮不该出现
        // （与 StatusLineText「放得下就不给查看完整错误」同一判据）。
        RunAction("提示对话框：没给可复制的路径时不出现「复制路径」按钮（真实控件）", () =>
        {
            using var plain = new NoticeDialog(
                NoticeText.AboutHeading,
                body,
                mono: NoticeText.AboutDataDirLine(path),
                note: NoticeText.AboutNote);
            plain.Show();
            Application.DoEvents();
            var stray = FindAll<Button>(plain).Where(b => b.Text == NoticeCopy.Label).ToList();
            if (stray.Count > 0)
            {
                throw new InvalidOperationException(
                    $"没给可复制的路径却出现了 {stray.Count} 颗「复制路径」按钮（点它什么也不会发生）。");
            }
        });

        // 下面六段共用同一屏：点了按钮之后要接着看按钮文字、看剪贴板、看位置有没有跳位，
        // 最后再点「关闭」——中间不换屏，换屏就量不到「同一颗按钮被点过之后」的那几个数。
        using var dialog = new NoticeDialog(
            NoticeText.AboutHeading,
            body,
            mono: NoticeText.AboutDataDirLine(path),
            note: NoticeText.AboutNote,
            copyText: path);
        dialog.Show();
        Application.DoEvents();

        var copyButton = ButtonOf(dialog, NoticeCopy.Label);
        var closeButton = ButtonOf(dialog, "关闭");
        // 宽度与位置先记下来：点了之后文字从「复制路径」变成「复制成功」，四个字换四个字，
        // 按钮宽度本就该不变（§6「停止/危险：位置尺寸与常态一致，防按钮跳位」），变了就是回归。
        var widthBefore = copyButton.Width;
        var closeLeftBefore = closeButton.Left;
        var rowHeightBefore = copyButton.Parent?.Height ?? 0;

        RunAction("提示对话框：给了可复制的路径时「复制路径」按钮在且可点（真实控件）", () =>
        {
            if (!copyButton.Visible) throw new InvalidOperationException("「复制路径」按钮不可见。");
            if (!copyButton.Enabled) throw new InvalidOperationException("「复制路径」按钮是禁用的。");
            if (!copyButton.CanSelect) throw new InvalidOperationException("「复制路径」按钮不可选中（Tab 到不了它）。");
        });

        // 等宽行的「量宽度」与「实际可用宽度」必须是同一个数（第一百九十二轮，简报 P0-2）：
        // 高度是按某个宽度量出来的，那个宽度必须就是控件实际拿到的宽度——量宽偏大，长路径被裁掉半行；
        // 偏小，这一屏多出一块空白。静态门只能判「算式收在一处」，这个等式要 WinForms 真跑才成立。
        RunAction("提示对话框：等宽行的量宽就是它实际拿到的宽度（真实控件）", () =>
        {
            var box = FindAll<TextBox>(dialog)
                .FirstOrDefault(t => string.Equals(t.Text, NoticeText.AboutDataDirLine(path), StringComparison.Ordinal))
                ?? throw new InvalidOperationException("对话框里找不到等宽行（只读文本框）。");
            if (box.Parent is not TableLayoutPanel grid)
            {
                throw new InvalidOperationException("等宽行不在内容网格里：可用宽度无从算起。");
            }
            var available = Ui.ContentWidth(grid);
            if (box.ClientSize.Width != available)
            {
                throw new InvalidOperationException(
                    $"等宽行实际宽 {box.ClientSize.Width}px，内容网格的可用宽 {available}px："
                    + "量宽与可用宽是两笔账，长路径会被裁掉半行或多出一块空白。");
            }
            var expected = Ui.MonoBoxHeight(box.Text, available);
            if (box.Height != expected)
            {
                throw new InvalidOperationException(
                    $"等宽行高 {box.Height}px，按可用宽 {available}px 量出来应是 {expected}px："
                    + "宽度变了高度没跟着重量。");
            }
            var right = grid.ClientSize.Width - grid.Padding.Right;
            if (box.Right > right)
            {
                throw new InvalidOperationException(
                    $"等宽行右边界 {box.Right}px 越出内容网格的可用右边界 {right}px：这一行被裁了。");
            }
            _lines.Add($"     等宽行量宽 {available}px == 实际宽 {box.ClientSize.Width}px，"
                + $"高 {box.Height}px（按同一宽度量出），右边界 {box.Right}px ≤ {right}px。");
        });

        // 等宽行的 Enter 契约（第一百九十二轮，简报 P1-3）：只读行不吃 Enter，Enter 冒泡给窗体的
        // AcceptButton（提示对话框里是「关闭」）。静态门只能判「源码里写了 AcceptsReturn = false」，
        // 判不了「这个框真的不吃 Enter」「窗体的 AcceptButton 真的是那颗关闭」——那要 WinForms 真跑。
        // 这一段量的是控件契约本身；**真的按一下 Enter 键**没有模拟（本环境没有 Windows 图形子系统，
        // 键盘注入在 wine 里不可靠），「按 Enter 真的关掉这一屏」仍随 G-D5 真机复核，不在这里冒充量到。
        RunAction("提示对话框：等宽行的 Enter 契约——只读框不吃 Enter，落点是「关闭」（真实控件）", () =>
        {
            var box = FindAll<TextBox>(dialog)
                .FirstOrDefault(t => string.Equals(t.Text, NoticeText.AboutDataDirLine(path), StringComparison.Ordinal))
                ?? throw new InvalidOperationException("对话框里找不到等宽行（只读文本框）。");
            if (!box.ReadOnly)
            {
                throw new InvalidOperationException("等宽行不是只读的：路径行不该接受输入。");
            }
            if (!box.Multiline)
            {
                throw new InvalidOperationException("等宽行不是多行的：长路径折不了行，会被裁掉半行。");
            }
            if (box.AcceptsReturn)
            {
                throw new InvalidOperationException(
                    "等宽行吃 Enter（AcceptsReturn = true）：只读行没有换行可做，Enter 从此什么都不发生，"
                    + "键盘用户被卡在这一屏里（Enter 本该冒泡给「关闭」）。");
            }
            if (!box.TabStop)
            {
                throw new InvalidOperationException("等宽行 Tab 到不了：键盘用户没法框选路径。");
            }
            if (dialog.AcceptButton is not Button accept)
            {
                throw new InvalidOperationException("提示对话框没有 AcceptButton：Enter 没有落点。");
            }
            if (!ReferenceEquals(accept, closeButton))
            {
                throw new InvalidOperationException(
                    $"提示对话框的 AcceptButton 是「{accept.Text}」，不是「关闭」：Enter 会落到别的动作上。");
            }
            _lines.Add($"     等宽行：ReadOnly={box.ReadOnly}、Multiline={box.Multiline}、"
                + $"AcceptsReturn={box.AcceptsReturn}、TabStop={box.TabStop}；"
                + $"Enter 的落点 AcceptButton =「{accept.Text}」（真的按 Enter 未模拟，随 G-D5 真机复核）。");
        });

        RunAction("提示对话框：真点「复制路径」，按钮文字切到「复制成功」（真实控件）", () =>
        {
            copyButton.PerformClick();
            Application.DoEvents();
            if (copyButton.Text == NoticeCopy.FailedLabel)
            {
                throw new InvalidOperationException(
                    "点了「复制路径」之后按钮说的是「复制失败」：剪贴板写不进去（这一屏如实说了没复制成，"
                    + "但本段量的是成功路径——写不进去不算量到）。");
            }
            if (copyButton.Text != NoticeCopy.CopiedLabel)
            {
                throw new InvalidOperationException(
                    $"点了「复制路径」之后按钮上是「{copyButton.Text}」，不是「{NoticeCopy.CopiedLabel}」。");
            }
        });

        RunAction("提示对话框：剪贴板里读回来的是那一条路径，逐字相同（真实控件）", () =>
        {
            string pasted;
            try
            {
                pasted = Clipboard.GetText();
            }
            catch (ExternalException ex)
            {
                throw new InvalidOperationException("读不回剪贴板（被其他进程占用）：" + ex.Message);
            }
            if (pasted != path)
            {
                throw new InvalidOperationException(
                    $"剪贴板里是「{pasted}」（{pasted.Length} 字），要复制的是「{path}」（{path.Length} 字）"
                    + "——粘出去的不是屏幕上那一行。");
            }
        });

        RunAction("提示对话框：复制成功后按钮宽度与旁边「关闭」的位置都没动（真实控件）", () =>
        {
            if (copyButton.Width != widthBefore)
            {
                throw new InvalidOperationException(
                    $"按钮宽度从 {widthBefore}px 变成 {copyButton.Width}px：三个标签同字数，宽度本该不变（防按钮跳位）。");
            }
            if (closeButton.Left != closeLeftBefore)
            {
                throw new InvalidOperationException(
                    $"「关闭」按钮从左边 {closeLeftBefore}px 挪到 {closeButton.Left}px：复制按钮宽度一变就把它推走了。");
            }
            if ((copyButton.Parent?.Height ?? 0) != rowHeightBefore)
            {
                throw new InvalidOperationException(
                    $"按钮行从 {rowHeightBefore}px 变成 {copyButton.Parent?.Height}px：文字切换不该推动这一行。");
            }
        });

        RunAction("提示对话框：真点「关闭」把这一屏关掉（真实控件）", () =>
        {
            closeButton.PerformClick();
            Application.DoEvents();
            // 模态之外的窗体 Close() 之后会被销毁：判「关掉了」用「销毁了或不可见了」，
            // 只判其中一个会在另一种框架行为下把通过了的一屏判成失败。
            if (!dialog.IsDisposed && dialog.Visible)
            {
                throw new InvalidOperationException("点了「关闭」这一屏还开着（窗体既没销毁也没隐藏）。");
            }
        });

        _lines.Add($"     提示对话框的「复制路径」：点了之后按钮文字切到「{NoticeCopy.CopiedLabel}」，"
            + $"剪贴板里读回来的与屏幕上那一行逐字相同（{path.Length} 字），按钮宽度 {widthBefore}px 未变。");
    }

    /// <summary>
    /// 定向构建输出对话框的「复制命令」（第二百轮）：命令行可框选、按钮一键交出、剪贴板逐字核对。
    /// <para>
    /// <b>为什么这一段必须真跑</b>：这一屏的说明行指着「粘到本机终端即可重新执行查看完整输出」，
    /// 而命令行此前是 <c>Label</c>——不可选中、不可复制，那一步在界面上没有落点。静态门
    /// （<see cref="BuildCommandCopyTests"/>）只能判「口径与接线」，判不了三件事：
    /// ① 布局完成后等宽行的量宽是不是它实际拿到的宽度（长命令会不会被裁掉半行）；
    /// ② 点下去剪贴板里到底是不是那条命令本身（不带「命令：」标签）；
    /// ③ 没有命令时那颗按钮是不是真的不出现。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：按钮在真实字体与真实剪贴板下的观感、Tab 可达的手感随 G-D5 真机复核
    /// （本环境是 wine 模拟器，不是真机）。
    /// </para>
    /// </summary>
    private static void RunBuildCommandCopyClick(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "构建输出复制命令冒烟" });

        // 命令取产品自己会落盘的那一条（BuildService.Arguments 拼出来的形状），不手拼字符串。
        var command = "dotnet " + string.Join(" ", BuildService.Arguments(profile.ApiProject));

        // ① 尚未构建：空态指引在、命令行是占位、不出现「复制命令」按钮。
        RunAction("构建输出：尚未构建时不出现「复制命令」按钮（真实控件）", () =>
        {
            using var dialog = new BuildLogDialog(tasks, task);
            dialog.Show();
            Application.DoEvents();
            var stray = FindAll<Button>(dialog).Where(b => b.Text == BuildCommandCopy.Label && b.Visible).ToList();
            if (stray.Count > 0)
            {
                throw new InvalidOperationException(
                    $"还没有构建记录却出现了 {stray.Count} 颗「{BuildCommandCopy.Label}」按钮（点它什么也不会发生）。");
            }
            var line = CommandBox(dialog);
            if (line.Text != BuildCommandCopy.Line(null))
            {
                throw new InvalidOperationException(
                    $"还没有构建记录，命令行却是「{line.Text}」，不是占位「{BuildCommandCopy.Line(null)}」。");
            }
        });

        // ② 记录里没有 command 字段：同样不给按钮（判定在 BuildCommandCopy.Target，不在界面里就地判）。
        RunAction("构建输出：记录里没有命令时不出现「复制命令」按钮（真实控件）", () =>
        {
            tasks.Write(task.Id, TaskFiles.BuildLog, new
            {
                startedAt = DateTimeOffset.Now,
                exitCode = 0,
                timedOut = false,
                seconds = 1.2,
                outputTail = "Build succeeded.",
            });
            using var dialog = new BuildLogDialog(tasks, task);
            dialog.Show();
            Application.DoEvents();
            var stray = FindAll<Button>(dialog).Where(b => b.Text == BuildCommandCopy.Label && b.Visible).ToList();
            if (stray.Count > 0)
            {
                throw new InvalidOperationException(
                    $"记录里没有命令却出现了 {stray.Count} 颗「{BuildCommandCopy.Label}」按钮。");
            }
            var line = CommandBox(dialog);
            if (line.Text != BuildCommandCopy.Line(null))
            {
                throw new InvalidOperationException($"记录里没有命令，命令行却是「{line.Text}」。");
            }
        });

        // 下面六段共用同一屏：点了按钮之后要接着看按钮文字、看剪贴板、看位置有没有跳位，
        // 最后再点「关闭」——中间不换屏，换屏就量不到「同一颗按钮被点过之后」的那几个数。
        tasks.Write(task.Id, TaskFiles.BuildLog, new
        {
            command,
            startedAt = DateTimeOffset.Now,
            exitCode = 0,
            timedOut = false,
            seconds = 2.4,
            outputTail = "Build succeeded.\n    0 Warning(s)\n    0 Error(s)",
        });

        using var screen = new BuildLogDialog(tasks, task);
        screen.Show();
        Application.DoEvents();

        var copyButton = ButtonOf(screen, BuildCommandCopy.Label);
        var closeButton = ButtonOf(screen, "关闭");
        var widthBefore = copyButton.Width;
        var closeLeftBefore = closeButton.Left;
        var rowHeightBefore = copyButton.Parent?.Height ?? 0;

        RunAction("构建输出：有命令时「复制命令」按钮在且可点（真实控件）", () =>
        {
            if (!copyButton.Visible) throw new InvalidOperationException($"「{BuildCommandCopy.Label}」按钮不可见。");
            if (!copyButton.Enabled) throw new InvalidOperationException($"「{BuildCommandCopy.Label}」按钮是禁用的。");
            if (!copyButton.CanSelect) throw new InvalidOperationException($"「{BuildCommandCopy.Label}」按钮不可选中（Tab 到不了它）。");
        });

        // 等宽行的「量宽度」与「实际可用宽度」必须是同一个数：高度是按某个宽度量出来的，
        // 那个宽度必须就是控件实际拿到的宽度——量宽偏大，长命令被裁掉半行；偏小，这一屏多出一块空白。
        RunAction("构建输出：命令行的量宽就是它实际拿到的宽度（真实控件）", () =>
        {
            var box = CommandBox(screen);
            if (box.Parent is not TableLayoutPanel head)
            {
                throw new InvalidOperationException("命令行不在内容网格里：可用宽度无从算起。");
            }
            var available = Ui.ContentWidth(head);
            if (box.ClientSize.Width != available)
            {
                throw new InvalidOperationException(
                    $"命令行实际宽 {box.ClientSize.Width}px，内容网格的可用宽 {available}px："
                    + "量宽与可用宽是两笔账，长命令会被裁掉半行或多出一块空白。");
            }
            var expected = Ui.MonoBoxHeight(box.Text, available);
            if (box.Height != expected)
            {
                throw new InvalidOperationException(
                    $"命令行高 {box.Height}px，按可用宽 {available}px 量出来应是 {expected}px："
                    + "宽度变了高度没跟着重量。");
            }
            var right = head.ClientSize.Width - head.Padding.Right;
            if (box.Right > right)
            {
                throw new InvalidOperationException(
                    $"命令行右边界 {box.Right}px 越出内容网格的可用右边界 {right}px：这一行被裁了。");
            }
            _lines.Add($"     命令行量宽 {available}px == 实际宽 {box.ClientSize.Width}px，"
                + $"高 {box.Height}px（按同一宽度量出），右边界 {box.Right}px ≤ {right}px。");
        });

        // 命令行可框选：只读文本框，Tab 到得了，Enter 不吃（Enter 冒泡给「关闭」）。
        RunAction("构建输出：命令行是可框选的只读框，Enter 落点是「关闭」（真实控件）", () =>
        {
            var box = CommandBox(screen);
            if (!box.ReadOnly) throw new InvalidOperationException("命令行不是只读的：命令不该在这里被改。");
            if (!box.Multiline) throw new InvalidOperationException("命令行不是多行的：长命令折不了行，会被裁掉半行。");
            if (box.AcceptsReturn)
            {
                throw new InvalidOperationException(
                    "命令行吃 Enter（AcceptsReturn = true）：只读行没有换行可做，Enter 从此什么都不发生。");
            }
            if (!box.TabStop) throw new InvalidOperationException("命令行 Tab 到不了：键盘用户没法框选命令。");
            if (screen.AcceptButton is not Button accept)
            {
                throw new InvalidOperationException("构建输出对话框没有 AcceptButton：Enter 没有落点。");
            }
            if (!ReferenceEquals(accept, closeButton))
            {
                throw new InvalidOperationException(
                    $"构建输出对话框的 AcceptButton 是「{accept.Text}」，不是「关闭」：Enter 会落到别的动作上。");
            }
        });

        RunAction("构建输出：真点「复制命令」，按钮文字切到「复制成功」（真实控件）", () =>
        {
            copyButton.PerformClick();
            Application.DoEvents();
            if (copyButton.Text == NoticeCopy.FailedLabel)
            {
                throw new InvalidOperationException(
                    "点了「复制命令」之后按钮说的是「复制失败」：剪贴板写不进去（这一屏如实说了没复制成，"
                    + "但本段量的是成功路径——写不进去不算量到）。");
            }
            if (copyButton.Text != NoticeCopy.CopiedLabel)
            {
                throw new InvalidOperationException(
                    $"点了「复制命令」之后按钮上是「{copyButton.Text}」，不是「{NoticeCopy.CopiedLabel}」。");
            }
        });

        RunAction("构建输出：剪贴板里读回来的是那条命令本身，不带「命令：」标签（真实控件）", () =>
        {
            string pasted;
            try
            {
                pasted = Clipboard.GetText();
            }
            catch (ExternalException ex)
            {
                throw new InvalidOperationException("读不回剪贴板（被其他进程占用）：" + ex.Message);
            }
            if (pasted != command)
            {
                throw new InvalidOperationException(
                    $"剪贴板里是「{pasted}」（{pasted.Length} 字），要交出去的是「{command}」（{command.Length} 字）"
                    + "——粘进终端的那一段不是屏幕上标签后面那一段。");
            }
            if (pasted.Contains(BuildCommandCopy.LinePrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"剪贴板里带着「{BuildCommandCopy.LinePrefix}」标签：粘进终端会被当成命令名。");
            }
        });

        RunAction("构建输出：复制成功后按钮宽度与旁边「关闭」的位置都没动（真实控件）", () =>
        {
            if (copyButton.Width != widthBefore)
            {
                throw new InvalidOperationException(
                    $"按钮宽度从 {widthBefore}px 变成 {copyButton.Width}px：四个字换四个字，宽度本该不变（防按钮跳位）。");
            }
            if (closeButton.Left != closeLeftBefore)
            {
                throw new InvalidOperationException(
                    $"「关闭」按钮从左边 {closeLeftBefore}px 挪到 {closeButton.Left}px：复制按钮宽度一变就把它推走了。");
            }
            if ((copyButton.Parent?.Height ?? 0) != rowHeightBefore)
            {
                throw new InvalidOperationException(
                    $"按钮行从 {rowHeightBefore}px 变成 {copyButton.Parent?.Height}px：文字切换不该推动这一行。");
            }
        });

        RunAction("构建输出：真点「关闭」把这一屏关掉（真实控件）", () =>
        {
            closeButton.PerformClick();
            Application.DoEvents();
            if (!screen.IsDisposed && screen.Visible)
            {
                throw new InvalidOperationException("点了「关闭」这一屏还开着（窗体既没销毁也没隐藏）。");
            }
        });

        _lines.Add($"     构建输出的「复制命令」：点了之后按钮文字切到「{NoticeCopy.CopiedLabel}」，"
            + $"剪贴板里读回来的与命令原文逐字相同（{command.Length} 字，不带「{BuildCommandCopy.LinePrefix}」标签），"
            + $"按钮宽度 {widthBefore}px 未变。");
    }

    /// <summary>构建输出对话框里的命令行（按口径的标签前缀定位，不按控件顺序猜）。</summary>
    private static TextBox CommandBox(Control root) =>
        FindAll<TextBox>(root).FirstOrDefault(t => t.Text.StartsWith(BuildCommandCopy.LinePrefix, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到命令行（以「" + BuildCommandCopy.LinePrefix + "」开头的只读文本框）。");


    /// <summary>某个控件所在行里的那个下拉框（按同一行里的按钮文字定位那一行）。</summary>
    private static ComboBox ComboInRowOf(Control root, string buttonLabel) =>
        FindFirst<ComboBox>(ButtonOf(root, buttonLabel).Parent
            ?? throw new InvalidOperationException($"找不到「{buttonLabel}」所在行。"))
        ?? throw new InvalidOperationException($"「{buttonLabel}」所在行里没有下拉框。");


    /// <summary>
    /// 按行标签找那一行里的输入框。字段网格（<c>Ui.FieldGrid</c>）是「标签在第 0 列、字段在第 1 列
    /// 的同一行」——标签的 <c>Parent</c> 是整张网格，按父容器找会取到网格里的<b>第一个</b>输入框
    /// （模型服务对话框即「服务地址」），填错格子等于绕过校验（wine 实测：状态行留空）。
    /// 故按单元格定位取同一行的下一列控件；行内包裹型布局（父容器就是那一行）仍按父容器取。
    /// </summary>
    private static TextBox BoxInRowLabeled(Control root, string label)
    {
        var caption = FindAll<Label>(root).FirstOrDefault(l => string.Equals(l.Text, label, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"找不到「{label}」这一行。");
        var cell = caption.Parent is TableLayoutPanel grid
            ? grid.GetControlFromPosition(grid.GetCellPosition(caption).Column + 1, grid.GetCellPosition(caption).Row)
            : caption.Parent;
        return FindFirst<TextBox>(cell
            ?? throw new InvalidOperationException($"「{label}」那一行右边没有控件。"))
            ?? throw new InvalidOperationException($"「{label}」所在行里没有输入框。");
    }
    /// <summary>
    /// 真的点一下「查看完整错误」，在模态窗泵起来的消息循环里核对正文与状态行逐字相同，再点它那颗「关闭」。
    /// 关它不直接调 <c>Close()</c>——那绕过了产品路径。
    /// <para>
    /// <paramref name="statusOverride"/>：逐屏扫里每屏的状态行标签由宿主给出，不一定是「对话框里第一个
    /// 非 AutoSize 且左对齐的标签」——那一条在部分对话框里会取到别的标签（那就成了拿别人的句子比）。
    /// 传进来就只比这一屏自己的状态行。
    /// </para>
    /// </summary>
    private static void ReadFullTextAndCheck(Form dialog, Label? statusOverride = null)
    {
        var status = statusOverride ?? StatusLabel(dialog);
        var expected = status.Text;
        var openedTitle = "";
        var openedBody = "";
        using var probe = new System.Windows.Forms.Timer { Interval = 30 };
        var dismissed = false;
        probe.Tick += (_, _) =>
        {
            if (dismissed) return;
            var shown = Application.OpenForms.OfType<StatusTextDialog>().FirstOrDefault();
            if (shown is null) return;
            openedTitle = shown.Text;
            openedBody = shown.Message;
            ButtonOf(shown, "关闭").PerformClick();
            dismissed = true;
        };
        probe.Start();
        try
        {
            Click(dialog, StatusLineText.FullTextButton);
        }
        finally
        {
            probe.Stop();
        }
        Application.DoEvents();
        if (!dismissed) throw new InvalidOperationException("点了入口却没等到完整错误对话框（没打开、或打开的不是这一型）。");
        if (openedBody != expected)
        {
            throw new InvalidOperationException(
                $"对话框正文与状态行不逐字相同（正文 {openedBody.Length} 字 / 状态行 {expected.Length} 字）：读全这一步给出的是另一句话或半句话。");
        }
        if (openedTitle != StatusLineText.FullTextHeading)
        {
            throw new InvalidOperationException($"对话框标题是「{openedTitle}」，取的不是口径文件那一份。");
        }
        _lines.Add($"     点开读全：标题「{openedTitle}」、正文 {openedBody.Length} 字与状态行逐字相同");
    }

    /// <summary>
    /// 状态行那一句的单行首选宽度，与「入口不占位时这一行有多宽」（宿主客户区宽 − 状态行左右留白）。
    /// <para>
    /// 基准不取标签当前宽度：入口一出现就把标签挤窄，拿挤窄后的宽度自比等于让入口替自己投票，
    /// 短句也会一直被判成放不下（第一百六十四轮 wine 实测到的正是这一支）。这里按几何独立重算，
    /// 不复用界面层的算法——「该不该给入口」这个判定也在调用点按两个数重述一遍，不调口径文件。
    /// </para>
    /// </summary>
    private static (int Preferred, int Available) StatusTextWidths(Label status)
    {
        var host = status.Parent ?? throw new InvalidOperationException("状态行没有挂在宿主里（接线与预期形状不符）。");
        return (TextRenderer.MeasureText(
                status.Text, status.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width,
            host.ClientSize.Width - status.Margin.Horizontal);
    }

    /// <summary>
    /// 按口径在冒烟侧重述一遍的判定：入口不占位时的行宽为正、且文字首选宽度超过它，才该给入口。
    /// 这里不调 <c>StatusLineText.ShouldOfferFullText</c>——同一个函数判自己不会红，判定要独立写一遍。
    /// </summary>
    private static (int Preferred, int Available, bool ShouldOffer) StatusEntryRule(Label status)
    {
        var (preferred, available) = StatusTextWidths(status);
        return (preferred, available, available > 0 && preferred > available);
    }

    /// <summary>状态行那一格所在的工具栏（状态行 → 宿主 → 工具栏）——入口出现与收起都不该让它变高。</summary>
    private static Control ToolbarOf(Control status) =>
        status.Parent?.Parent ?? throw new InvalidOperationException("状态行没有挂在宿主与工具栏里（接线与预期形状不符）。");

    /// <summary>工具栏行高必须与入口出现之前一致：状态行是固定占位的一行，撑高会推动整屏布局。</summary>
    private static void AssertToolbarHeightHeld(Control status, int baseline, int windowWidth)
    {
        var height = ToolbarOf(status).Height;
        if (height != baseline)
        {
            throw new InvalidOperationException(
                $"窗口 {windowWidth}px 下工具栏那一行从 {baseline}px 变成 {height}px（状态行宿主 {status.Parent?.Height}px）："
                + "状态行是固定占位的一行，加一颗入口不该推动布局。");
        }
    }

    /// <summary>任务页详情区的网格（<c>BuildDetail</c> 出厂的单列表格：行数远多于两列字段网格）。</summary>
    private static TableLayoutPanel DetailGrid(Control root) =>
        FindAll<TableLayoutPanel>(root).FirstOrDefault(grid => grid.ColumnCount == 1 && grid.RowCount >= 10)
        ?? throw new InvalidOperationException("任务页里找不到详情区网格。");

    /// <summary>
    /// 详情区里每一行文字要么完整显示、要么有可见的截断提示（换行变高或省略号）。
    /// 静默裁掉即判失败——这是「极端数据下不撑破」的可判定形式。
    /// </summary>
    private static void AssertNoSilentClipping(TableLayoutPanel grid, string what)
    {
        var clipped = new List<string>();
        foreach (var label in FindAll<Label>(grid))
        {
            if (!label.Visible || label.Text.Length == 0) continue;
            var preferred = TextRenderer.MeasureText(
                label.Text, label.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (preferred.Width <= label.Width + 2) continue;
            var wrapped = label.Height > label.Font.Height * 1.5;
            if (wrapped || label.AutoEllipsis || label.Text.EndsWith("…", StringComparison.Ordinal)) continue;
            var head = label.Text.Length <= 12 ? label.Text : label.Text[..12] + "…";
            clipped.Add($"「{head}」（文字 {preferred.Width}px、控件 {label.Width}px、高 {label.Height}px）");
        }
        if (clipped.Count > 0)
        {
            throw new InvalidOperationException(
                $"{what}：详情区里这些文字被静默裁掉（既没换行也没有省略号）：" + string.Join("、", clipped));
        }
    }

    /// <summary>
    /// 说明行「放得下」（第一百六十轮，简报 P2-3 的冒烟侧）。
    /// <para>
    /// 存在理由：Linux 侧的 <c>CaptionBrevityTests</c> 只能判「说明行 ≤ 80 字且 ≤ 2 句」——
    /// 那是「别写成段落」，不是「放得下」。80 字在 12px 字号下约 960px，比报告与交接对话框（920）
    /// 与代码证据对话框（960）的可用宽度都宽：写到上限的说明行会被静默裁掉，而长度门是绿的。
    /// 这里在真实控件上量文字宽度，与那一屏的可用宽度比。
    /// </para>
    /// <para>
    /// 量法：说明行经 <c>Ui.Caption</c> 出厂（<c>AutoSize = true</c>），所以「放不下」的表现是折行
    /// 或裁掉，不是抛异常——用 <c>TextRenderer.MeasureText</c> 按单行量出文字宽度，与**这一行真正被
    /// 排版进去的容器**的可用宽度比（<see cref="CaptionAvailableWidth"/>）。
    /// </para>
    /// <para>
    /// <b>第一百六十一轮修正（wine 实测抓到的空门）</b>：第一百六十轮那版取 <c>label.Width</c> 当可用宽度，
    /// 而 AutoSize 标签的宽度恒等于文字宽度——自己量自己，断言永远成立。wine 实测打出的
    /// <c>343/343px</c>、<c>283/283px</c>、<c>369/369px</c>、<c>511/511px</c> 四行等式就是证据：
    /// 六条说明行里只有两条（Dock=Top 的底部区）真的量到了容器宽度。现在改取父容器客户区减内边距、
    /// 以对话框可用宽度为上界；容器尚未排版（不可见子树）时退回对话框上界，并在证据行里标出是哪一支。
    /// </para>
    /// <para>
    /// <b>第一百六十二轮（简报 P0-2）</b>：证据行补上「这一档到底是多少缩放」。wine 实测发现
    /// <c>Control.DeviceDpi</c> 在 <c>HKCU\Control Panel\Desktop\LogPixels</c> 改成 120 / 192 之后
    /// <b>仍然恒报 96</b>，而说明行字体确实按 120 / 192 放大（文字宽度 487→608→…）——只写 DeviceDpi
    /// 的话，三档 DPI 的证据行会长得一模一样，读的人分不清量到的是哪一档。故证据行同时给出
    /// 「说明行字体高换算出的实际缩放」（<see cref="CaptionScalePercent"/>），并在量测失败时也把
    /// 这一行写进报告（此前抛异常在前、证据行在后，200% 那一档的数字全丢了）。
    /// </para>
    /// </summary>
    /// <summary>
    /// 一条说明行的可用宽度（第一百六十一轮）：取它真正被排版进去的容器的客户区减内边距，
    /// 并以对话框可用宽度（<c>ClientSize.Width − 2×Lg</c>）为上界。
    /// <para>
    /// 为什么不能取 <c>label.Width</c>：<c>Ui.Caption</c> 出厂是 <c>AutoSize = true</c>，未 Dock 的标签
    /// 宽度恒等于文字宽度——拿它当可用宽度就是自己量自己，断言永远成立。第一百六十轮那版正是如此，
    /// wine 实测打出的全是 <c>343/343px</c> 这种等式：门是绿的，但它没量过任何东西。
    /// 容器尚未排版（不可见子树，如计划生成前的同步预览页）时退回对话框可用宽度，并在证据行里
    /// 标出这一支量的是「按对话框宽度排版时放不放得下」。
    /// </para>
    /// </summary>
    /// <summary>
    /// 说明行字体实际按多少缩放渲染（第一百六十二轮，简报 P0-2）。
    /// <para>
    /// 为什么不能只看 <c>Control.DeviceDpi</c>：wine 实测把 <c>LogPixels</c> 改成 120 / 192 之后，
    /// 说明行文字宽度确实按 1.25 / 2 倍放大（487→608→…），而 <c>DeviceDpi</c> 恒报 96——
    /// 只写 DeviceDpi 的三档证据行会长得一模一样，读的人分不清量到的是哪一档。
    /// </para>
    /// <para>
    /// 换算口径：<c>Font.Height</c> 是**行高**（含行距），不是字号像素高——直接拿它除以磅值会把
    /// 行距算成 DPI（wine 实测 9pt 字体在 96 DPI 下行高 15px，直接换算会得出「125%」这种假数）。
    /// 故先按字体自身的行距比还原字号像素高（<c>Height × EmHeight / LineSpacing</c>），
    /// 再由 <c>字号像素高 = 磅值 × dpi / 72</c> 反解 dpi，最后除以 96 得百分比并取整到 5%。
    /// 字体度量取不到时回 0，证据行如实显示「≈0%」而不是编一个数。
    /// </para>
    /// </summary>
    private static int CaptionScalePercent()
    {
        var font = UiTokens.Caption.Value;
        var family = font.FontFamily;
        var em = family.GetEmHeight(font.Style);
        var spacing = family.GetLineSpacing(font.Style);
        if (em <= 0 || spacing <= 0 || font.Height <= 0 || font.SizeInPoints <= 0) return 0;
        var emPixels = font.Height * (double)em / spacing;
        var dpi = emPixels * 72.0 / font.SizeInPoints;
        return (int)(Math.Round(dpi / 96.0 * 20) * 5);
    }

    private static (int Available, string Source) CaptionAvailableWidth(Label label, int dialogUsable)
    {
        var parent = label.Parent;
        if (parent is null || parent.ClientSize.Width <= 0) return (dialogUsable, "（对话框上界）");
        var usable = parent.ClientSize.Width - parent.Padding.Horizontal;
        if (usable <= 0) return (dialogUsable, "（对话框上界）");
        return usable < dialogUsable ? (usable, "（容器）") : (dialogUsable, "（对话框上界）");
    }

    /// <summary>
    /// 写入清单表头放得下（第二百一十七轮，<c>docs/CRITIQUE.md</c> 第 8 站那一处「先量再改」）。
    /// <para>
    /// 存在理由：同步预览写入清单的首列表头此前是 <c>"#"</c>——原生界面所有清单表头里唯一一个纯符号，
    /// 与 DESIGN §1「中文单语」对不上。它一直没改，是因为那一列只有 40px，而排序方向标记会追加在表头文字
    /// 之后（<see cref="ListSort.Header"/>），「序号 ▲」放不放得下在 Linux 上判不了（没有 Windows 图形子系统，
    /// wine 也没量过这一格）。照抄改字有把表头截断成「序…」的风险。
    /// </para>
    /// <para>
    /// 这一段就是那次量测：把写入清单真的构造出来，按它自己的字体逐列量表头文字（含排序方向标记那一支），
    /// 与列宽比。判据是「文字宽 + 表头左右留白 ≤ 列宽」——留白取 <see cref="HeaderPadding"/>，
    /// 是保守估计而不是量到的（表头单元格的内边距由系统绘制，本环境量不到），所以这一条绿只说明
    /// 「按这个留白算放得下」，真机观感仍随 G-D5。
    /// </para>
    /// <para>
    /// <b>判据只在设计档（100%）成立，非设计档只记读数</b>（第二百一十七轮实测）：列宽是逻辑像素、
    /// 不随 DPI 缩放（DESIGN §6「列宽」口径：标识列吸收剩余可视宽度，其余列是设计值），而表头文字
    /// 随 DPI 放大——同一份源码三档实测，列宽合计恒为 984px，而「序号 ▲」的文字宽是 54 / 58 / 70px
    /// （96 / 120 / 192 档）。于是 125% 与 200% 档「文字宽 > 列宽」是列宽口径的必然结果，不是改字引入的；
    /// 那一档的截断正是 DESIGN §8 已挂账的「窄窗口下表头截断后的标记可见性随 G-D5 复核」那一格。
    /// 把非设计档也判成失败，等于让一条「量设计档宽度选得对不对」的判据去替真机做决定，
    /// 而且会把三档读数从「N 通过 / 0 失败 ×3」变成带失败的一跑——那份读数记的是产品回归，不是口径后果。
    /// 故非设计档把量到的数字照原样写进证据行（真机那天有基线可比），不判失败。
    /// </para>
    /// </summary>
    private static void RunActionHeaderFit(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "写入清单表头放得下冒烟" });

        RunAction("写入清单表头：设计档（100%）每一列的表头文字（含排序方向标记）都在列宽之内（真实控件量文字）", () =>
        {
            using var dialog = new SyncPreviewDialog(
                tasks,
                services.GetRequiredService<SyncPlanService>(),
                services.GetRequiredService<SyncExecutor>(),
                services.GetRequiredService<JobRunner>(),
                task);
            _ = dialog.Handle;
            dialog.CreateControl();
            dialog.PerformLayout();
            Application.DoEvents();

            // 写入清单是这一屏唯一一张七列清单（勾选页四列、未纳入清单两列）。
            var list = FindAll<ListView>(dialog).FirstOrDefault(candidate => candidate.Columns.Count == 7)
                ?? throw new InvalidOperationException("同步预览里找不到七列的写入清单：量测口径可能已失效。");

            var measured = new List<string>();
            var tooWide = new List<string>();
            for (var i = 0; i < list.Columns.Count; i++)
            {
                var title = list.Columns[i].Text;
                var width = list.Columns[i].Width;
                // 两个形状都量：未排序时是原标题，点过这一列之后是「原标题 + 空格 + 方向标记」。
                foreach (var (shape, text) in new[]
                         {
                             ("未排序", title),
                             ("排序后", ListSort.Header(title, i, i, descending: false)),
                         })
                {
                    var preferred = TextRenderer.MeasureText(
                        text, list.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    measured.Add($"「{text}」{preferred.Width}/{width}px（{shape}）");
                    if (preferred.Width + HeaderPadding > width)
                    {
                        tooWide.Add($"第 {i + 1} 列「{text}」（{shape}）文字 {preferred.Width}px + 留白 {HeaderPadding}px > 列宽 {width}px");
                    }
                }
            }

            // 判据只在设计档成立：列宽是逻辑像素、不随 DPI 缩放（DESIGN §6），表头文字随 DPI 放大。
            // 非设计档的「文字宽 > 列宽」是列宽口径的必然结果，随 G-D5 复核，本段只记读数（见方法注释）。
            var designTier = !DpiScaleFactor.ShouldScale(DpiScale.ScaleFactor());
            var verdict = designTier
                ? (tooWide.Count == 0 ? "设计档全部放得下" : $"设计档 {tooWide.Count} 条放不下")
                : $"非设计档（≈{CaptionScalePercent()}%）{tooWide.Count} 条超宽——列宽是逻辑像素、不随 DPI 缩放（DESIGN §6），本段不判";

            // 先写证据行再判失败：判失败的那一档也要留下量到的数字（与 RunCaptionFit 同一纪律）。
            _lines.Add(
                $"     写入清单 {list.Columns.Count} 列、{DpiContext(dialog)}、表头字体 {list.Font.Height}px" +
                $"（≈{CaptionScalePercent()}%），列宽合计 {list.Columns.Cast<ColumnHeader>().Sum(column => column.Width)}px、" +
                $"清单可视宽 {list.ClientSize.Width}px，文字/列宽 = {string.Join("，", measured)}；" +
                $"结论：{verdict}");

            if (designTier && tooWide.Count > 0)
            {
                throw new InvalidOperationException("写入清单表头在设计档放不下（会被截断成「序…」）：" + string.Join("、", tooWide));
            }
        });
    }

    /// <summary>
    /// 表头单元格左右留白的保守估计（像素）：系统绘制表头时左右各留一点，本环境量不到那个内边距，
    /// 故按 6px 一侧计。判据因此是「按这个留白算放得下」，不是「真机上一定不截断」——后者随 G-D5。
    /// </summary>
    private const int HeaderPadding = 12;

    private static void RunCaptionFit(IServiceProvider services, string dataRoot)
    {
        var tasks = services.GetRequiredService<TaskStore>();
        var profiles = services.GetRequiredService<ProfileStore>();
        var exports = services.GetRequiredService<ReportExportService>();
        var profile = profiles.List().First();
        var task = tasks.Create(new CreateTaskRequest { ProfileId = profile.Id, Name = "说明行放得下冒烟" });
        var slot = new DocSlot
        {
            Id = "slot-caption-fit",
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

        var screens = new (string Name, Func<Form> Build)[]
        {
            ("同步预览", () => new SyncPreviewDialog(
                tasks,
                services.GetRequiredService<SyncPlanService>(),
                services.GetRequiredService<SyncExecutor>(),
                services.GetRequiredService<JobRunner>(),
                task)),
            ("报告与交接", () => new ReportsDialog(exports, task)),
            ("代码证据", () => new EvidenceDialog(tasks, task, slot)),
        };

        foreach (var (screenName, build) in screens)
        {
            RunAction($"说明行放得下：{screenName}的说明行在可用宽度内一行放得下（真实控件量文字）", () =>
            {
                using var dialog = build();
                _ = dialog.Handle;
                dialog.CreateControl();
                dialog.PerformLayout();
                Application.DoEvents();

                var dialogUsable = dialog.ClientSize.Width - UiTokens.Lg * 2;
                var measured = new List<string>();
                var tooWide = new List<string>();
                foreach (var label in FindAll<Label>(dialog))
                {
                    if (label.Text.Length == 0) continue;
                    // 只量说明行：经 Ui.Caption 出厂的标签共用同一个 Font 实例。
                    if (!ReferenceEquals(label.Font, UiTokens.Caption.Value)) continue;
                    // 可用宽度取「这一行真正被排版进去的容器」，不是标签自己的宽度：
                    // Ui.Caption 出厂是 AutoSize，未 Dock 的标签 Width 恒等于文字宽度——拿它当可用宽度
                    // 就是自己量自己，永远「放得下」（第一百六十轮那版实测全是 343/343px 这种等式，
                    // 门是绿的但它是空的）。Dock=Top 的标签宽度才等于容器可用宽度。
                    var (available, source) = CaptionAvailableWidth(label, dialogUsable);
                    var preferred = TextRenderer.MeasureText(
                        label.Text, label.Font, new Size(10000, 10000), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                    measured.Add($"{preferred.Width}/{available}px{source}");
                    if (preferred.Width > available + 2)
                    {
                        var head = label.Text.Length <= 16 ? label.Text : label.Text[..16] + "…";
                        tooWide.Add($"「{head}」（文字 {preferred.Width}px、可用 {available}px{source}）");
                    }
                }

                // 证据行带缩放档与对话框可用宽度（第一百六十二轮，简报 P0-2）：三档 DPI 各跑一次时，
                // 这一行就是「可用宽度 / 说明行宽度 / 放得下结论」三件事的落点——没有缩放档就分不清
                // 量到的是哪一档，也没法回答「对话框上界随 DPI 缩放后的真实值是多少」。
                // 先写证据行再判失败：量测失败的那一档（wine 实测 200%）此前抛异常在前、证据行在后，
                // 报告里只剩一句「放不下」，量到的数字全丢了。
                var scale = CaptionScalePercent();
                var verdict = tooWide.Count == 0 ? "全部放得下" : $"{tooWide.Count} 条放不下";
                _lines.Add(
                    $"     {screenName}：控件报 DPI {dialog.DeviceDpi}、说明行字体 {UiTokens.Caption.Value.Height}px" +
                    $"（≈{scale}%），屏幕 DPI {DpiScale.ScreenDpi()}、缩放因子 {DpiScale.ScaleFactor()}、客户区 {dialog.ClientSize.Width}×{dialog.ClientSize.Height}、屏幕 {Screen.PrimaryScreen?.Bounds.Width}×{Screen.PrimaryScreen?.Bounds.Height}、最大窗口 {SystemInformation.MaxWindowTrackSize.Width}×{SystemInformation.MaxWindowTrackSize.Height}，" +
                    $"对话框可用宽度 {dialogUsable}px，{measured.Count} 条说明行，" +
                    $"文字/可用宽度 = {string.Join("，", measured)}；结论：{verdict}");

                if (measured.Count == 0)
                {
                    throw new InvalidOperationException($"{screenName}里一条说明行都没量到：量测口径可能已失效。");
                }
                if (tooWide.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"{screenName}：这些说明行放不下（会折行或被静默裁掉）：" + string.Join("、", tooWide));
                }
            });
        }
    }

    /// <summary>
    /// 完整旅程单次运行（第一百四十四轮）：按 README「使用流程」1–9 步里**不触碰 Apifox 写入门禁**的
    /// 只读路径，从「新建配置」一路走到「报告导出」，全程一条任务、一次连续运行。
    /// <para>
    /// 存在理由：第一百三十一至一百三十九轮把主链路拆成了若干段（配置/任务、回写、缺口对话框、候选对话框），
    /// 每段各自造配置、各自造任务——**没有一次运行证明这些段能接在同一条任务上走完**。
    /// 控制仓简报的 P0-1 要的正是「旅程」而不是「控件」：段与段之间的交接（扫描产物 → 规则提取 →
    /// 审阅接受 → 计划 → 回写 → 传播核对 → 报告）此前从未被同一次运行串起来过。
    /// </para>
    /// <para>
    /// 八段：① 新建配置（真实控件）→ ② 新建任务（真实控件）→ ③ 扫描 → ④ 规则提取候选 →
    /// ⑤ 审阅接受（真实控件）→ ⑥ 确认回写（真实控件）→ ⑦ 传播核对（如实记录未生成 XML/Swagger）→
    /// ⑧ 报告四格式导出。第 ⑤⑥ 段走真实 WinForms 控件（<c>Show()</c> 触发读盘、<c>PerformClick()</c> 走点击链路），
    /// 其余段走服务层真实调用；断言一律读磁盘产物与控件文字，不读服务层返回值。
    /// </para>
    /// <para>
    /// 不覆盖的部分如实挂账：Apifox 远端检查与同步需要真实 CLI 登录态与项目权限，本环境（wine）不具备，
    /// 仍待 Windows 真机（见 docs/NOTES.md 的真机清单）。
    /// </para>
    /// </summary>
    private static void RunFullJourney(IServiceProvider services, string dataRoot)
    {
        var profiles = services.GetRequiredService<ProfileStore>();
        var tasks = services.GetRequiredService<TaskStore>();
        var scan = services.GetRequiredService<SourceScanService>();
        var drafts = services.GetRequiredService<DraftService>();
        var plans = services.GetRequiredService<CommentPlanService>();
        var propagation = services.GetRequiredService<PropagationService>();
        var exports = services.GetRequiredService<ReportExportService>();
        var cli = services.GetRequiredService<ApifoxCli>();
        var credentials = services.GetRequiredService<CredentialStore>();

        const string profileName = "完整旅程配置";
        const string taskName = "完整旅程任务";
        var projectDir = Path.Combine(dataRoot, "Journey.Api");
        var sourcePath = Path.Combine(projectDir, "Controllers", "JourneyController.cs");

        string? profileId = null;
        string? taskId = null;
        string? acceptedId = null;
        string? beforeWrite = null;

        // 第一段：新建配置（真实控件）→ 保存 → 配置真的落到 profiles/<id>.json。
        RunAction("完整旅程 1/8：新建配置并保存（真实控件）", () =>
        {
            CreateJourneyWorkspace(projectDir);
            var before = profiles.List().Count;
            using var dialog = new ProfileDialog(profiles, cli, credentials, editing: null);
            dialog.Show();
            Application.DoEvents();

            var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
            SetField(grid, "名称", profileName);
            SetField(grid, "仓库根目录", dataRoot);
            SetField(grid, "API 项目", "Journey.Api/Journey.Api.csproj");
            SetField(grid, "允许回写范围", "Journey.Api/");
            Click(dialog, "保存");
            Application.DoEvents();

            if (dialog.DialogResult != DialogResult.OK) throw new InvalidOperationException("点「保存」后对话框没有以「确定」结束。");
            var saved = dialog.Saved ?? throw new InvalidOperationException("保存后 Saved 为空。");
            var reread = profiles.Get(saved.Id);
            if (reread.Name != profileName) throw new InvalidOperationException($"落盘名称是「{reread.Name}」。");
            if (reread.ApiProject != "Journey.Api/Journey.Api.csproj") throw new InvalidOperationException($"落盘 API 项目是「{reread.ApiProject}」。");
            if (profiles.List().Count != before + 1) throw new InvalidOperationException("配置数量没有增加一条。");
            profileId = saved.Id;
            _lines.Add($"     配置已落盘：{saved.Id}，名称「{reread.Name}」，回写范围「{string.Join(";", reread.AllowedSourcePaths)}」");
        });

        // 第二段：新建任务（真实控件）→ 点「仅创建」→ 任务真的落到 tasks/<id>/task.json。
        RunAction("完整旅程 2/8：新建任务并创建（真实控件）", () =>
        {
            if (profileId is null) throw new InvalidOperationException("上一段没有产出配置，旅程断在这里。");
            using var dialog = new NewTaskDialog(profiles);
            dialog.Show();
            Application.DoEvents();

            var grid = FindGrid(dialog) ?? throw new InvalidOperationException("对话框里找不到两列字段网格。");
            SelectProfile(grid, profileName);
            SetField(grid, "任务名称", taskName);
            Click(dialog, "仅创建");
            Application.DoEvents();

            if (dialog.DialogResult != DialogResult.OK) throw new InvalidOperationException("点「仅创建」后对话框没有以「确定」结束。");
            if (dialog.StartScan) throw new InvalidOperationException("点「仅创建」却要求立即扫描。");
            var request = dialog.Request;
            if (request.ProfileId != profileId) throw new InvalidOperationException("创建请求里的配置不是刚保存的那一个。");
            var task = tasks.Create(request);
            var reread = tasks.Get(task.Id);
            if (reread.Name != taskName) throw new InvalidOperationException($"落盘任务名是「{reread.Name}」。");
            if (reread.ProfileId != profileId) throw new InvalidOperationException("落盘任务的配置不是刚保存的那一个。");
            taskId = task.Id;
            _lines.Add($"     任务已落盘：{task.Id}，名称「{reread.Name}」");
        });

        // 第三段：扫描（真实服务）——真的加载项目、发现接口、产出注释缺口。
        RunAction("完整旅程 3/8：源码扫描产出注释缺口（真实服务）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务，旅程断在这里。");
            var job = new JobContext(new JobInfo { Id = "job-smoke-journey-scan", Scope = taskId, Kind = "scan", StartedAt = DateTimeOffset.Now });
            Task.Run(() => scan.ScanAsync(taskId, job)).GetAwaiter().GetResult();

            var output = tasks.Read<ScanOutput>(taskId, TaskFiles.Symbols)
                ?? throw new InvalidOperationException("扫描后没有 symbols.json。");
            if (output.Endpoints.Count == 0) throw new InvalidOperationException("扫描没有发现任何接口。");
            var missing = output.Slots.Where(s => s.Status == "missing" && s.Editable).ToList();
            if (missing.Count == 0) throw new InvalidOperationException("扫描没有产出可回写的缺失注释位置。");
            _lines.Add($"     扫描：{output.Endpoints.Count} 个接口、{output.Slots.Count} 个注释位置，其中缺失且可回写 {missing.Count} 个");
        });

        // 第四段：规则提取候选（真实服务）——只生成代码可证实的约束，必须带证据编号与待确认问题。
        RunAction("完整旅程 4/8：规则提取候选（真实服务）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务，旅程断在这里。");
            Task.Run(() => drafts.GenerateRuleAsync(taskId, [], CancellationToken.None)).GetAwaiter().GetResult();

            var store = drafts.Load(taskId);
            var produced = store.Candidates.Where(c => c.Source == "rule").ToList();
            if (produced.Count == 0)
            {
                throw new InvalidOperationException("规则提取没有产出任何候选（探针项目里没有可证实的约束证据）。");
            }
            if (produced.Any(c => c.EvidenceIds.Count == 0))
            {
                throw new InvalidOperationException("有规则候选没有引用任何证据编号（规则提取只允许生成代码可证实的内容）。");
            }
            if (produced.Any(c => c.Text.Length == 0)) throw new InvalidOperationException("有规则候选的文字为空。");
            if (produced.All(c => c.OpenQuestions.Count == 0))
            {
                throw new InvalidOperationException("规则候选全都没有待确认问题（审阅那一段就没有「先回答再接受」的前提）。");
            }
            _lines.Add($"     规则提取：{produced.Count} 条候选，第一条「{produced[0].Text}」，证据 {produced[0].EvidenceIds.Count} 条、待确认问题 {produced[0].OpenQuestions.Count} 个");
        });

        // 第五段：审阅接受（真实控件）——打开候选对话框、填回答、点「接受并下一条」，断言磁盘状态。
        RunAction("完整旅程 5/8：审阅接受候选（真实控件）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务，旅程断在这里。");
            var task = tasks.Get(taskId);
            using var dialog = new CandidatesDialog(drafts, tasks, task);
            dialog.Show();
            Application.DoEvents();

            var list = CandidateList(dialog);
            if (list.Items.Count == 0) throw new InvalidOperationException("候选清单是空的（规则提取的候选没有进队列）。");
            if (list.SelectedIndices.Count != 1 || list.SelectedIndices[0] != 0)
            {
                throw new InvalidOperationException("打开后没有选中第一条候选。");
            }
            var boxes = EditableBoxes(dialog);
            if (boxes.Length != 2) throw new InvalidOperationException($"可编辑的多行输入框有 {boxes.Length} 个，期望 2 个。");
            // 规则候选带待确认问题：不填回答就接受会被拒（第一百三十九轮已钉住），旅程里按用户的做法先回答。
            if (boxes[1].Enabled)
            {
                boxes[1].Text = "分页大小，由前端按列表页容量传入。";
                Application.DoEvents();
            }
            var status = StatusLabel(dialog);
            status.Text = "";
            Click(dialog, "接受并下一条");
            PumpUntilCondition(() => status.Text.Length > 0, "接受完成的状态行");

            var store = drafts.Load(taskId);
            var accepted = store.Candidates.Where(c => c.ReviewState == "accepted").ToList();
            if (accepted.Count == 0) throw new InvalidOperationException("点「接受并下一条」后磁盘上没有已接受的候选。状态行：" + status.Text);
            acceptedId = accepted[0].Id;
            if (accepted[0].Text.Length == 0) throw new InvalidOperationException("已接受候选的文字为空。");
            _lines.Add($"     审阅：已接受 {accepted.Count} 条，第一条「{accepted[0].Text}」；状态行「{status.Text}」");
        });

        // 第六段：确认回写（真实控件）——对话框自己生成计划、点「确认回写」，断言源码真的被改且只改 /// 行。
        RunAction("完整旅程 6/8：确认回写源码（真实控件）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务，旅程断在这里。");
            beforeWrite = File.ReadAllText(sourcePath);
            using var dialog = new ApplyPlanDialog(plans, tasks, tasks.Get(taskId));
            var confirmed = 0;
            // 确认框是模态的，无人值守点不动：换成替身，但按钮与回写链路一字不改。
            dialog.ConfirmApply = _ =>
            {
                confirmed++;
                return true;
            };
            dialog.Show();
            PumpUntil(dialog.PendingLoad);
            Application.DoEvents();

            var plan = dialog.Plan ?? throw new InvalidOperationException("对话框没有拿到注释计划。界面文案：" + LabelTexts(dialog));
            if (plan.Files.Count == 0) throw new InvalidOperationException("计划里没有可回写的文件：" + string.Join("；", plan.Skipped.Select(s => s.Reason)));

            Click(dialog, "确认回写");
            PumpUntil(dialog.PendingApply);
            Application.DoEvents();

            if (confirmed != 1) throw new InvalidOperationException($"确认委托被调用 {confirmed} 次，期望 1 次。");
            var after = File.ReadAllText(sourcePath);
            if (after == beforeWrite) throw new InvalidOperationException("点「确认回写」后源码没有变化。");
            AssertOnlyDocLinesChanged(beforeWrite, after);
            AssertBomAndEolPreserved(sourcePath);
            var status = FindAll<Label>(dialog).Select(l => l.Text).FirstOrDefault(t => t.Contains("已回写", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("状态行没有出现「已回写」的结果文案。界面文案：" + LabelTexts(dialog));
            _lines.Add($"     回写：{plan.Files.Count} 个文件、{plan.Files.Sum(f => f.Edits.Count)} 处注释；状态行「{status}」");
        });

        // 第七段：传播核对（真实服务）——未生成 XML、未配置 Swagger 时必须如实记录，不显示成完成。
        RunAction("完整旅程 7/8：传播核对如实记录（真实服务）", () =>
        {
            if (taskId is null || acceptedId is null) throw new InvalidOperationException("上一段没有产出任务或已接受候选，旅程断在这里。");
            var job = new JobContext(new JobInfo { Id = "job-smoke-journey-propagation", Scope = taskId, Kind = "propagation", StartedAt = DateTimeOffset.Now });
            Task.Run(() => propagation.CheckAsync(taskId, job)).GetAwaiter().GetResult();

            var result = tasks.Read<PropagationResult>(taskId, TaskFiles.Propagation)
                ?? throw new InvalidOperationException("核对后没有 propagation.json。");
            var acceptedCount = drafts.Load(taskId).Candidates.Count(c => c.ReviewState == "accepted");
            if (result.Entries.Count != acceptedCount)
            {
                throw new InvalidOperationException($"核对条目 {result.Entries.Count} 条，已接受候选 {acceptedCount} 条。");
            }
            if (result.Entries.Any(e => e.SourceState != "written"))
            {
                throw new InvalidOperationException("有已回写的注释在核对里不是 written：" + string.Join("；", result.Entries.Select(e => e.Display + "=" + e.SourceState)));
            }
            // 探针项目没有 GenerateDocumentationFile、配置里没有 Swagger：两环必须如实标成「未启用 / 不可用」。
            if (result.Entries.Any(e => e.XmlState == "present"))
            {
                throw new InvalidOperationException("没有生成 XML 文档，核对却报告 XML 已收录。");
            }
            if (result.Entries.Any(e => e.XmlState != "disabled"))
            {
                throw new InvalidOperationException("XML 状态不是 disabled：" + string.Join("；", result.Entries.Select(e => e.Display + "=" + e.XmlState)));
            }
            if (result.Entries.Any(e => e.SwaggerState != "unavailable"))
            {
                throw new InvalidOperationException("没有配置 Swagger，核对却给出了 Swagger 状态：" + string.Join("；", result.Entries.Select(e => e.Display + "=" + e.SwaggerState)));
            }
            if (string.IsNullOrWhiteSpace(result.SwaggerMessage)) throw new InvalidOperationException("Swagger 不可用却没有说明原因。");
            var stage = tasks.Get(taskId).Stages.Generated;
            if (stage.Status == "verified") throw new InvalidOperationException("XML 与 Swagger 都没核对到，阶段状态却是 verified。");
            _lines.Add($"     核对：{result.Entries.Count} 条全部 written；XML 全部 disabled、Swagger 全部 unavailable；阶段状态「{stage.Status}」，说明「{stage.Message}」");
        });

        // 第八段：报告四格式导出（真实服务）——文件名/内容类型/字节与任务目录副本，四格式逐份核对。
        RunAction("完整旅程 8/8：报告四格式导出（真实服务）", () =>
        {
            if (taskId is null) throw new InvalidOperationException("上一段没有产出任务，旅程断在这里。");
            var md = exports.Render(taskId, "md");
            if (md.FileName != "report.md" || md.ContentType != "text/markdown")
            {
                throw new InvalidOperationException($"Markdown 报告是「{md.FileName}」/「{md.ContentType}」。");
            }
            var mdText = new System.Text.UTF8Encoding(false).GetString(md.Bytes);
            if (!mdText.Contains(taskName, StringComparison.Ordinal)) throw new InvalidOperationException("Markdown 报告里没有任务名。");
            if (!File.Exists(tasks.FilePath(taskId, TaskFiles.ReportMarkdown))) throw new InvalidOperationException("Markdown 报告没有写进任务目录。");

            var html = exports.Render(taskId, "html");
            var htmlText = new System.Text.UTF8Encoding(false).GetString(html.Bytes);
            if (html.FileName != "report.html" || html.ContentType != "text/html")
            {
                throw new InvalidOperationException($"HTML 报告是「{html.FileName}」/「{html.ContentType}」。");
            }
            if (!htmlText.Contains("<table>", StringComparison.Ordinal)) throw new InvalidOperationException("HTML 报告里没有表格（分阶段结果应当渲染成表格）。");
            if (htmlText.Contains("| 阶段 |", StringComparison.Ordinal)) throw new InvalidOperationException("HTML 报告正文里泄漏了 Markdown 表格记号。");

            var json = exports.Render(taskId, "json");
            var jsonText = new System.Text.UTF8Encoding(false).GetString(json.Bytes);
            if (json.FileName != "report.json" || json.ContentType != "application/json")
            {
                throw new InvalidOperationException($"JSON 报告是「{json.FileName}」/「{json.ContentType}」。");
            }
            var node = System.Text.Json.Nodes.JsonNode.Parse(jsonText) ?? throw new InvalidOperationException("JSON 报告解析为空。");
            if (node["task"]?["name"]?.ToString() != taskName) throw new InvalidOperationException("JSON 报告里的任务名对不上。");

            var handoff = exports.Render(taskId, "handoff");
            var handoffText = new System.Text.UTF8Encoding(false).GetString(handoff.Bytes);
            if (handoff.FileName != "handoff.md" || handoff.ContentType != "text/markdown")
            {
                throw new InvalidOperationException($"交接说明是「{handoff.FileName}」/「{handoff.ContentType}」。");
            }
            if (!handoffText.Contains("接口变更交接", StringComparison.Ordinal)) throw new InvalidOperationException("交接说明里没有标题。");
            if (!File.Exists(tasks.FilePath(taskId, TaskFiles.Handoff))) throw new InvalidOperationException("交接说明没有写进任务目录。");

            var rejected = false;
            try
            {
                exports.Render(taskId, "pdf");
            }
            catch (UserException ex)
            {
                rejected = true;
                if (ex.Message != "报告格式无效。") throw new InvalidOperationException($"未知格式的拒绝文案是「{ex.Message}」。");
            }
            if (!rejected) throw new InvalidOperationException("未知报告格式却导出成功。");
            _lines.Add($"     报告：md {md.Bytes.Length} 字节、html {html.Bytes.Length} 字节、json {json.Bytes.Length} 字节、handoff {handoff.Bytes.Length} 字节；未知格式按「报告格式无效。」拒绝");
        });
    }

    /// <summary>
    /// 旅程探针工作区（第一百四十四轮）：一个 Web SDK 项目 + 一个带**可证实约束**的控制器。
    /// <para>
    /// 与 <see cref="CreateProbeWorkspace"/> 的区别：那个探针只保证「有缺口可回写」，这个探针还要保证
    /// 「规则提取真的能产出候选」——<c>[Range(1, 100)]</c> 与 <c>[Required]</c> 是规则提取认的证据形状
    /// （校验特性），没有它们，旅程第 4 段会以「没有产出任何候选」失败，而那是探针的问题、不是产品的问题。
    /// 文件按 UTF-8 带 BOM + CRLF 写，回写后要能断言「BOM 与换行符一字未改」。
    /// </para>
    /// </summary>
    private static void CreateJourneyWorkspace(string projectDir)
    {
        Directory.CreateDirectory(Path.Combine(projectDir, "Controllers"));
        File.WriteAllText(
            Path.Combine(projectDir, "Journey.Api.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk.Web\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>");
        File.WriteAllText(
            Path.Combine(projectDir, "Controllers", "JourneyController.cs"),
            JourneyControllerSource,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>旅程控制器源码：<c>pageSize</c> 带 <c>[Range(1, 100)]</c>、<c>Name</c> 带 <c>[Required]</c>，两处都没有 <c>///</c>。</summary>
    private const string JourneyControllerSource =
        "using System.ComponentModel.DataAnnotations;\r\n" +
        "using Microsoft.AspNetCore.Mvc;\r\n" +
        "\r\n" +
        "namespace Journey.Api.Controllers;\r\n" +
        "\r\n" +
        "[ApiController]\r\n" +
        "[Route(\"api/journey\")]\r\n" +
        "public class JourneyController : ControllerBase\r\n" +
        "{\r\n" +
        "    [HttpGet]\r\n" +
        "    public ActionResult<JourneyResponse> Get([FromQuery, Range(1, 100)] int pageSize)\r\n" +
        "    {\r\n" +
        "        return Ok(new JourneyResponse { Name = \"journey\" });\r\n" +
        "    }\r\n" +
        "}\r\n" +
        "\r\n" +
        "public class JourneyResponse\r\n" +
        "{\r\n" +
        "    [Required]\r\n" +
        "    public string Name { get; set; } = \"\";\r\n" +
        "}\r\n";

    /// <summary>注释缺口对话框的计数行（「位置 N · 有效 …」或空态那句）。</summary>
    private static Label CountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("位置 ", StringComparison.Ordinal) || l.Text == "没有注释位置")
        ?? throw new InvalidOperationException("对话框里找不到计数行。界面文案：" + LabelTexts(root));

    /// <summary>注释候选对话框的计数行（「候选 N · 待审阅 …」或空态那句）。</summary>
    private static Label CandidateCountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("候选 ", StringComparison.Ordinal) || l.Text == "没有候选")
        ?? throw new InvalidOperationException("对话框里找不到候选计数行。界面文案：" + LabelTexts(root));

    /// <summary>候选清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView CandidateList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("对话框里找不到候选清单。");

    /// <summary>
    /// 可编辑的多行输入框，按控件树先序：候选文字那一格在前、待确认问题的回答在后
    /// （只读的详情预览框是 <c>Ui.Preview</c>，<c>ReadOnly</c> 为真，不在此列）。
    /// </summary>
    private static TextBox[] EditableBoxes(Control root) =>
        FindAll<TextBox>(root).Where(t => t.Multiline && !t.ReadOnly).ToArray();

    /// <summary>补充说明对话框的接口清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView SupplementList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("对话框里找不到接口清单。");

    /// <summary>响应契约对话框的契约清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView ContractList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("对话框里找不到契约清单。");

    /// <summary>响应契约对话框的计数行（「契约 N · 待确认 M · …」）。</summary>
    private static Label ContractCountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("契约 ", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到契约计数行。界面文案：" + LabelTexts(root));

    /// <summary>
    /// 按接口键选中契约清单里的一行（真实控件：选中事件随即问到详情区与三个审阅动作）。
    /// 清单是 <c>MultiSelect = false</c>（<c>Ui.List</c> 出厂口径），选中一行即换掉上一行。
    /// </summary>
    private static void SelectContractRow(ListView list, string endpointKey)
    {
        foreach (ListViewItem item in list.Items)
        {
            if (item.Tag is ResponseContract contract && contract.EndpointKey == endpointKey)
            {
                item.Selected = true;
                return;
            }
        }
        throw new InvalidOperationException($"契约清单里没有「{endpointKey}」这一行。");
    }

    /// <summary>差异确认对话框的条目清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView DecisionList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("对话框里找不到条目清单。");

    /// <summary>差异确认对话框的计数行（「共 N 项 · 待处理 M · …」）。</summary>
    private static Label DecisionCountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("共 ", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到计数行。界面文案：" + LabelTexts(root));

    /// <summary>
    /// 差异确认对话框的分类筛选下拉（选项里有「全部」的那一个——模型绑定下拉的选项是模型名，不会撞上）。
    /// </summary>
    private static ComboBox DecisionFilter(Control root) =>
        FindAll<ComboBox>(root).FirstOrDefault(c => c.Items.Contains("全部"))
        ?? throw new InvalidOperationException("对话框里找不到分类筛选下拉。");

    /// <summary>差异确认对话框的关键词框（单行输入框；详情区那个是只读多行预览框）。</summary>
    private static TextBox DecisionKeyword(Control root) =>
        FindAll<TextBox>(root).FirstOrDefault(t => !t.Multiline)
        ?? throw new InvalidOperationException("对话框里找不到关键词输入框。");

    /// <summary>差异确认对话框的空态提示（可见且以「这个筛选下没有」开头的那一行）。</summary>
    private static Label DecisionHint(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Visible && l.Text.StartsWith("这个筛选下没有", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到空态提示。界面文案：" + LabelTexts(root));

    /// <summary>
    /// 补充说明的编辑位输入框（多行、非只读），按控件树先序——与 <c>SupplementEntries.EditorsFor</c>
    /// 的编辑位顺序一致（标题、说明、参数、请求体、200 响应）。
    /// </summary>
    private static TextBox[] SupplementEditorBoxes(Control root) =>
        FindAll<TextBox>(root).Where(t => t.Multiline && !t.ReadOnly).ToArray();

    /// <summary>编辑位的「保留现状，本次不补充」勾选，与编辑位同序。</summary>
    private static CheckBox[] SupplementKeepBoxes(Control root) =>
        FindAll<CheckBox>(root).ToArray();

    /// <summary>补充说明的计数行（「共 N 个接口 · 已保存补充 M 处」）。</summary>
    private static Label SupplementCountsLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("共 ", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到计数行。界面文案：" + LabelTexts(root));

    /// <summary>覆盖已有注释的勾选（对话框里唯一的 <c>CheckBox</c>）。</summary>
    private static CheckBox OverwriteBox(Control root) =>
        FindFirst<CheckBox>(root) ?? throw new InvalidOperationException("对话框里找不到覆盖勾选。");

    /// <summary>按说明行的开头文字找那一行说明（「候选文字」「待确认问题的回答」）。</summary>
    private static Label CaptionOf(Control root, string prefix) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith(prefix, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"对话框里找不到「{prefix}」那一行说明。界面文案：" + LabelTexts(root));

    /// <summary>状态行（<c>Ui.StatusLine</c>：固定占位、左对齐、非 AutoSize 的那一个标签）。</summary>
    private static Label StatusLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => !l.AutoSize && l.TextAlign == ContentAlignment.MiddleLeft)
        ?? throw new InvalidOperationException("对话框里找不到状态行。界面文案：" + LabelTexts(root));

    /// <summary>
    /// 等一个条件成立，期间泵消息（<c>Application.DoEvents</c>）。
    /// 对话框的异步续体（<c>async void</c> 的审阅动作）排在 WinForms 同步上下文上，
    /// 不泵消息就永远等不到；等不到即判失败，不静默放过。
    /// </summary>
    private static void PumpUntilCondition(Func<bool> condition, string what, int timeoutMs = 60000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("等待「" + what + "」超时。");
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    /// <summary>注释覆盖率行（第二十轮口径，文字取 <c>ScanOverview.CoverageText</c>）。</summary>
    private static Label CoverageLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Text.StartsWith("注释覆盖率", StringComparison.Ordinal))
        ?? throw new InvalidOperationException("对话框里找不到注释覆盖率行。界面文案：" + LabelTexts(root));

    /// <summary>上一条/下一条之间的位置指示（「第 N 条 / 共 M 条」或「不在当前筛选中」）。</summary>
    private static Label NavLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l =>
            l.Text.StartsWith("第 ", StringComparison.Ordinal) || l.Text == ListAdvance.NotInFilterText)
        ?? throw new InvalidOperationException("对话框里找不到位置指示。界面文案：" + LabelTexts(root));

    /// <summary>空态/筛到零行的提示行（铺满列表区、居中）。</summary>
    private static Label HintLabel(Control root) =>
        FindAll<Label>(root).FirstOrDefault(l => l.Dock == DockStyle.Fill && l.TextAlign == ContentAlignment.MiddleCenter)
        ?? throw new InvalidOperationException("对话框里找不到空态提示行。界面文案：" + LabelTexts(root));

    /// <summary>注释位置清单（对话框里唯一的 <c>ListView</c>）。</summary>
    private static ListView SlotList(Control root) =>
        FindFirst<ListView>(root) ?? throw new InvalidOperationException("对话框里找不到注释位置清单。");

    /// <summary>关键词框（单行输入框；多行那个是详情预览）。</summary>
    private static TextBox KeywordBox(Control root) =>
        FindAll<TextBox>(root).FirstOrDefault(t => !t.Multiline)
        ?? throw new InvalidOperationException("对话框里找不到关键词框。");

    /// <summary>详情预览框（多行只读）。</summary>
    private static TextBox DetailBox(Control root) =>
        FindAll<TextBox>(root).FirstOrDefault(t => t.Multiline)
        ?? throw new InvalidOperationException("对话框里找不到详情预览框。");

    /// <summary>状态筛选下拉（对话框里唯一的 <c>ComboBox</c>）。</summary>
    private static ComboBox StatusFilter(Control root) =>
        FindFirst<ComboBox>(root) ?? throw new InvalidOperationException("对话框里找不到状态筛选下拉。");

    /// <summary>
    /// 等一个异步任务完成，期间泵消息（<c>Application.DoEvents</c>）。
    /// 对话框的异步续体排在 WinForms 同步上下文上，不泵消息就永远等不到。
    /// </summary>
    private static void PumpUntil(Task? task, int timeoutMs = 180000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (task is { IsCompleted: false })
        {
            if (Environment.TickCount64 > deadline) throw new TimeoutException("等待异步操作超时。");
            Application.DoEvents();
            Thread.Sleep(10);
        }
        // 任务失败时把异常原样抛出来（RunAction 会记成失败行）。
        task?.GetAwaiter().GetResult();
    }

    /// <summary>控件树里所有非空标签文字（失败时用来交代「界面上到底写了什么」）。</summary>
    private static string LabelTexts(Control root) =>
        string.Join(" / ", FindAll<Label>(root).Select(l => l.Text).Where(t => t.Length > 0));

    /// <summary>源码里 <c>///</c> 注释行的条数。</summary>
    private static int DocLineCount(string text) =>
        text.Replace("\r\n", "\n").Split('\n').Count(line => line.TrimStart().StartsWith("///", StringComparison.Ordinal));

    /// <summary>
    /// 写入安全不变量（SHARED）的机械断言：把两侧的 <c>///</c> 行都去掉后，剩下的内容必须逐行相同——
    /// 即回写只动了文档注释行，执行代码、空行、其他注释一字未改。
    /// </summary>
    private static void AssertOnlyDocLinesChanged(string before, string after)
    {
        static List<string> Code(string text) => text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .ToList();
        var oldLines = Code(before);
        var newLines = Code(after);
        if (!oldLines.SequenceEqual(newLines, StringComparer.Ordinal))
        {
            var index = Enumerable.Range(0, Math.Min(oldLines.Count, newLines.Count))
                .FirstOrDefault(i => !string.Equals(oldLines[i], newLines[i], StringComparison.Ordinal), -1);
            throw new InvalidOperationException($"回写改动了 /// 注释行以外的内容（第 {index + 1} 行起不同）。");
        }
        if (DocLineCount(after) <= DocLineCount(before)) throw new InvalidOperationException("回写没有新增任何 /// 注释行。");
    }

    /// <summary>回写后 UTF-8 BOM 与 CRLF 换行必须原样保留（不因写入而改写编码或换行）。</summary>
    private static void AssertBomAndEolPreserved(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF)
        {
            throw new InvalidOperationException("回写后 UTF-8 BOM 丢失。");
        }
        var text = File.ReadAllText(path);
        var crlf = text.Split("\r\n").Length - 1;
        var lf = text.Count(c => c == '\n');
        if (crlf != lf) throw new InvalidOperationException($"回写后换行符被改写：CRLF {crlf} 处、LF {lf} 处。");
    }

    /// <summary>按标签文字找到字段行第 1 列的控件（与各对话框 <c>AddRow</c> 的摆放约定一致）。</summary>
    private static Control? FieldOf(TableLayoutPanel grid, string label)
    {
        foreach (Control control in grid.Controls)
        {
            if (grid.GetColumn(control) != 0) continue;
            if (!string.Equals(control.Text, label, StringComparison.Ordinal)) continue;
            var row = grid.GetRow(control);
            foreach (Control candidate in grid.Controls)
            {
                if (grid.GetRow(candidate) == row && grid.GetColumn(candidate) == 1) return candidate;
            }
        }
        return null;
    }

    /// <summary>把文字真的敲进字段行里的输入框（或下拉框的可编辑区）。</summary>
    private static void SetField(TableLayoutPanel grid, string label, string text)
    {
        var host = FieldOf(grid, label) ?? throw new InvalidOperationException($"字段网格里找不到「{label}」这一行。");
        // 字段行第 1 列可能是输入框本身（裸 ComboBox）、也可能是包着输入框的描边面板（Ui.FieldBox）。
        Control input = host as TextBox ?? (Control?)host as ComboBox
            ?? FindFirst<TextBox>(host) ?? (Control?)FindFirst<ComboBox>(host)
            ?? throw new InvalidOperationException($"「{label}」这一行里没有输入框或下拉框。");
        input.Text = text;
    }

    /// <summary>在「项目配置」下拉里按配置名选中一项（与用户点选同一结果）。</summary>
    private static void SelectProfile(TableLayoutPanel grid, string profileName)
    {
        var host = FieldOf(grid, "项目配置") ?? throw new InvalidOperationException("字段网格里找不到「项目配置」这一行。");
        var combo = host as ComboBox ?? FindFirst<ComboBox>(host)
            ?? throw new InvalidOperationException("「项目配置」这一行里没有下拉框。");
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (!string.Equals(combo.Items[i]?.ToString(), profileName, StringComparison.Ordinal)) continue;
            combo.SelectedIndex = i;
            return;
        }
        throw new InvalidOperationException($"下拉里没有配置「{profileName}」。");
    }

    /// <summary>按按钮文字找到按钮（不点：读 <c>Enabled</c> 与再点都要用同一个查找口径）。</summary>
    private static Button ButtonOf(Control root, string text) =>
        FindAll<Button>(root).FirstOrDefault(b => string.Equals(b.Text, text, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"对话框里找不到「{text}」按钮。");

    /// <summary>按按钮文字找到按钮并真的点一下（<c>PerformClick</c> 走 WinForms 的点击链路，不是直接调事件处理器）。</summary>
    private static void Click(Control root, string text)
    {
        var button = ButtonOf(root, text);
        if (!button.CanSelect) throw new InvalidOperationException($"「{text}」按钮不可点（禁用或不可见）。");
        button.PerformClick();
    }

    /// <summary>控件树里第一个指定类型的后代。</summary>
    private static T? FindFirst<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) return match;
            if (FindFirst<T>(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>控件树里全部指定类型的后代（按树的先序）。</summary>
    private static IEnumerable<T> FindAll<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match) yield return match;
            foreach (var nested in FindAll<T>(child)) yield return nested;
        }
    }

    /// <summary>递归找两列字段网格（<c>Ui.FieldGrid</c> 出厂的那个 <c>TableLayoutPanel</c>）。</summary>
    private static TableLayoutPanel? FindGrid(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TableLayoutPanel { ColumnCount: 2 } grid && grid.ColumnStyles.Count == 2) return grid;
            if (FindGrid(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>同一行第 0 列的标签文字（量到的宽度要能对上「名称 / 版本备注」这些字段）。</summary>
    private static string RowLabel(TableLayoutPanel grid, int row)
    {
        foreach (Control control in grid.Controls)
        {
            if (grid.GetRow(control) != row || grid.GetColumn(control) != 0) continue;
            if (control.Text.Length > 0) return control.Text;
        }
        return "第" + row + "行";
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

    /// <summary>与 <see cref="Run"/> 同款的结果记账，用于不返回控件的交互用例。</summary>
    private static void RunAction(string name, Action action)
    {
        try
        {
            action();
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
    /// 冒烟清单覆盖的运行时核对（第一百五十四轮，控制仓简报 P1-1）。
    /// <para>
    /// 两个方向都判：清单里声明了却没构造出来（用例被删、被改成 helper 包装而没真的 new），
    /// 以及构造了却没在清单里声明（新增对话框忘了登记）。前者是这一轮要堵的静默漏发现，
    /// 后者会让 Linux 侧的清单比对与运行时事实脱节。
    /// </para>
    /// <para>
    /// 判失败而不是抛异常：报告里要留下具体类名，退出码仍是 1。
    /// </para>
    /// </summary>
    private static void CheckDialogCoverage()
    {
        const string name = "冒烟清单覆盖（声明 ↔ 真实构造）";
        var declared = SmokeDialogNames.ToHashSet(StringComparer.Ordinal);
        var missing = SmokeDialogNames.Where(n => !_constructedDialogs.Contains(n)).ToList();
        var undeclared = _constructedDialogs.Where(n => !declared.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (missing.Count == 0 && undeclared.Count == 0)
        {
            _passed++;
            var line = $"PASS {name}（{declared.Count} 个对话框全部构造）";
            Console.WriteLine(line);
            _lines.Add(line);
            return;
        }

        _failed++;
        var detail = new List<string>();
        if (missing.Count > 0) detail.Add("声明了却没构造：" + string.Join("、", missing));
        if (undeclared.Count > 0) detail.Add("构造了却没声明：" + string.Join("、", undeclared));
        var fail = $"FAIL {name}：{string.Join("；", detail)}";
        Console.WriteLine(fail);
        _lines.Add(fail);
    }

    /// <summary>
    /// 页面类清单覆盖的运行时核对（第一百五十五轮，控制仓简报 P1-2）。
    /// <para>
    /// 与 <see cref="CheckDialogCoverage"/> 同一形状、同一理由：清单是数据，但「登记了」不等于
    /// 「构造了」。两个方向都判——声明了却没构造（用例被删、被改成 helper 包装而没真的 new），
    /// 以及构造了却没声明（新增页面忘了登记）。判失败而不是抛异常：报告里要留下具体类名。
    /// </para>
    /// </summary>
    private static void CheckPageCoverage()
    {
        const string name = "冒烟清单覆盖（页面类：声明 ↔ 真实构造）";
        var declared = SmokePageNames.ToHashSet(StringComparer.Ordinal);
        var missing = SmokePageNames.Where(n => !_constructedPages.Contains(n)).ToList();
        var undeclared = _constructedPages.Where(n => !declared.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();

        if (missing.Count == 0 && undeclared.Count == 0)
        {
            _passed++;
            var line = $"PASS {name}（{declared.Count} 个页面全部构造）";
            Console.WriteLine(line);
            _lines.Add(line);
            return;
        }

        _failed++;
        var detail = new List<string>();
        if (missing.Count > 0) detail.Add("声明了却没构造：" + string.Join("、", missing));
        if (undeclared.Count > 0) detail.Add("构造了却没声明：" + string.Join("、", undeclared));
        var fail = $"FAIL {name}：{string.Join("；", detail)}";
        Console.WriteLine(fail);
        _lines.Add(fail);
    }

    /// <summary>
    /// 段的标题行：中文段名 + 这一段自己的方法名（<c>[RunXxx]</c>）。
    /// <para>
    /// <b>为什么要带上方法名</b>：<c>docs/NOTES.md</c> 的「降级答辩脚本」逐屏表按段的方法名引用证据
    /// （<c>RunCaptionFit</c>、<c>RunProfileLoadFailure</c>…），而报告里原本只有中文段名——两边对不上时，
    /// 演示脚本只能人手抄一份「哪一屏看哪一段」的清单，抄的那一份会漂（改了段名、加了段，抄单不会红）。
    /// 现在标题里带 id，脚本直接从逐屏表读引用、在报告里找那一段；id 由调用点的 <c>nameof</c> 给出，
    /// 段改名即编译不过，不存在第二份名单。
    /// </para>
    /// </summary>
    private static void Section(string title, string id)
    {
        var line = $"== {title}[{id}] ==";
        Console.WriteLine(line);
        _lines.Add(line);
    }

    private static void Run(string name, Func<Control> build)
    {
        try
        {
            using var control = build();
            // 记下真的构造出来的对话框类型：清单声明与真实构造是两件事，
            // 只有后者能拦住构造期崩溃（见 CheckDialogCoverage）。
            if (control is Form form) _constructedDialogs.Add(form.GetType().Name);
            // 页面类同册（第一百五十五轮）：UserControl 不是 Form，两条记录互不冒充。
            if (control is UserControl page) _constructedPages.Add(page.GetType().Name);
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
        // 第一百三十二轮起这个工作区同时是「扫描 → 候选 → 计划 → 回写」的探针：
        // 项目改成 Web SDK 并带一个缺 /// 的控制器，扫描才真的能发现接口与注释缺口。
        var probeDir = Path.Combine(dataRoot, "Probe.Api");
        Directory.CreateDirectory(probeDir);
        CreateProbeWorkspace(probeDir);

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
