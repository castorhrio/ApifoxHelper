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

        Console.WriteLine("== 字段宽度实测 ==");
        MeasureFieldWidths(services, profile);

        Console.WriteLine("== 字段宽度实测（DPI 缩放 125% / 150% / 200%）==");
        MeasureFieldWidthsAtScale(services, profile);

        Console.WriteLine("== 端到端主链路（真实控件交互）==");
        RunEndToEnd(services, dataRoot);

        Console.WriteLine("== 端到端回写链路（扫描 → 候选 → 计划 → 回写落盘）==");
        RunWriteBackEndToEnd(services, dataRoot);

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

    /// <summary>按按钮文字找到按钮并真的点一下（<c>PerformClick</c> 走 WinForms 的点击链路，不是直接调事件处理器）。</summary>
    private static void Click(Control root, string text)
    {
        var button = FindAll<Button>(root).FirstOrDefault(b => string.Equals(b.Text, text, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"对话框里找不到「{text}」按钮。");
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
