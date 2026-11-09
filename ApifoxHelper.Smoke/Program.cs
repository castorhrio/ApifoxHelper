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

        Console.WriteLine("== 端到端主链路（真实控件交互）==");
        RunEndToEnd(services, dataRoot);

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
