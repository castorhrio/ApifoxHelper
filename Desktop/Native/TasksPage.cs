using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Reports;
using ApifoxHelper.Source;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 检查任务页：任务列表 + 选中任务的阶段概览 + 源码扫描（JobRunner 后台作业，进度与取消）
/// + 注释候选（规则提取/模型生成/材料导出的生成入口、逐条审阅接受/搁置/驳回、注释回写）
/// + 生成文档核对（后台作业、只读结果视图）
/// + 响应契约（扫描时追踪落盘，逐条确认/拒绝/重置）
/// + 定向构建（用户确认后经既有 BuildService 在工作区执行 dotnet build --no-restore 刷新 XML 文档，
/// 结果落盘 build.json；「查看构建输出」只读展示命令、退出码、耗时与输出尾部）
/// + Apifox 差异检查（只读后台作业，需要时先加载冻结 Swagger；离线任务先导入远端 JSON；只读结果视图）
/// + 差异确认（高风险变化逐项确认、说明冲突选择、模型绑定与共享模型更新确认，经共享 DecisionService 保存并递增任务版本）
/// + 报告与交接（Markdown 预览 + 原生另存为导出，渲染与字节口径走共享 ReportExportService）
/// + 同步预览与执行（勾选接口生成冻结计划并查看写入清单、排除原因与保留设置，经既有 SyncPlanService；
/// 确认后经既有 SyncExecutor 执行同步——计划哈希绑定、逐动作写入并回读核对、失败即停、可在动作之间停止；
/// 「重新核对（只读）」复用 VerifyAsync）
/// + 「下一步」引导（方案 3.2「界面始终给出唯一下一步」）：任务列表「下一步」列与详情顶部引导横幅，
/// 口径经共享的 NextStep 与冻结页面同一规则同一文案；横幅动作映射到本页既有入口，只做引导不改变状态。
/// </summary>
internal sealed class TasksPage : UserControl, INativePage
{
    private readonly TaskStore _tasks;
    private readonly SourceScanService _scan;
    private readonly JobRunner _jobs;
    private readonly ProfileStore _profiles;
    private readonly DraftService _drafts;
    private readonly CommentPlanService _plans;
    private readonly PropagationService _propagation;
    private readonly BuildService _builds;
    private readonly ContractReviewService _reviews;
    private readonly CheckService _checks;
    private readonly DecisionService _decisionSaves;
    private readonly SupplementService _supplementSaves;
    private readonly ReportExportService _exports;
    private readonly SyncPlanService _syncPlans;
    private readonly SyncExecutor _syncExecutor;

    private const int TaskNameColumnWidth = 200;
    // 分割面板口径（单一落点）：详情区设计宽 420、可收窄下限 360，列表下限 320。
    // 首次显示的分割距离与可行性门控都用这些常量，不出现第二处数字。
    private const int DetailDesignWidth = 420;
    private const int DetailMinWidth = 360;
    private const int ListMinWidth = 320;

    private readonly ListView _list = Ui.List();
    // 列表筛选（ListFilter 口径）：输入即筛选，Esc 或空态的「清空筛选」按钮清空；筛选状态留在本页，重建后按同一关键词呈现。
    private readonly TextBox _filter = Ui.Input();
    private readonly Panel _empty;
    // 空态文字按「还没有任务」与「筛到零行」两态切换：筛到零行不说成没有任务，并给出清空筛选的下一步。
    private const string EmptyTitle = "还没有检查任务";
    private const string EmptyHint = "先在「项目设置」新建配置，然后点「新建任务」开始源码扫描。";
    private readonly Label _emptyTitle = Ui.Title(EmptyTitle);
    private readonly Label _emptyHint = Ui.Body(EmptyHint, muted: true);
    // 「新建任务」文字单一落点：工具栏实心主按钮与空态按钮同名同源，不出现第二处字面。
    private const string NewTaskLabel = "新建任务";
    // 空态的唯一行动按钮（DESIGN.md §7）：没有任务时指向「新建任务」，筛到零行时指向「清空筛选」——
    // 同一时刻只显示其中一个，实心主按钮始终只有工具栏那一个。
    private readonly Button _emptyCreate = Ui.Ghost(NewTaskLabel);
    private readonly Button _emptyClear = Ui.Ghost(ListFilter.ClearActionLabel);
    private readonly Label _status = Ui.StatusLine();
    // 详情区固定设计宽（FixedPanel = Panel2）：窗口缩放时详情不变、列表吸收剩余宽度，
    // 与「标识列吸收剩余可视宽度」（ListColumns）同一方向；缺省 None 会让详情区随窗口按比例放大、偏离设计宽。
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, BackColor = UiTokens.Line, FixedPanel = FixedPanel.Panel2 };

    // 下一步引导横幅（口径在 NextStep）：标题按语义色调着色，动作按钮映射到本页既有入口。
    private readonly FlowLayoutPanel _nextHead = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Label _nextTitle = Ui.Body();
    private readonly Label _nextDetail = Ui.Caption();
    private readonly FlowLayoutPanel _nextActions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };

    // 三阶段进度条（口径在 StageSteps）：三格 ghost 按钮 + 一句「点一下会发生什么」，
    // 点一下滚到对应分区（冻结页面 stepper 点一下切到该阶段页签的同一手势）。
    private readonly FlowLayoutPanel _stepper = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Button[] _stepButtons;
    private readonly Label _stepperHint = Ui.Caption(StageSteps.JumpHint);
    // 三个分区的标题控件：进度条点击的滚动落点（文字取 StageSteps 常量，本页不就地拼）。
    private readonly Label _sourceSection = SectionCaption(StageSteps.SourceLabel);
    private readonly Label _generatedSection = SectionCaption(StageSteps.GeneratedLabel);
    private readonly Label _remoteSection = SectionCaption(StageSteps.RemoteSectionLabel);

    // 详情区（固定控件，按任务刷新文本，不整面板重建）。
    private readonly Label _detailHint;
    private readonly TableLayoutPanel _detail;
    // 详情有九个分区、高于可视高度：放进可滚动宿主，底部动作（同步/报告）可达（DESIGN.md §6 长内容区）。
    private readonly Panel _detailScroll = Ui.ScrollHost();
    private readonly Label _detailName = Ui.Section();
    private readonly Label _detailMeta = Ui.Caption();
    private readonly Label _detailRange = Ui.Caption();
    private readonly Label _sourceStatus = Ui.StatusText();
    private readonly Label _sourceNumbers = Ui.Mono();
    private readonly FlowLayoutPanel _scanActions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _generatedActions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Button _gapButton;
    private readonly Button _candidatesButton;
    private readonly Button _ruleButton;
    private readonly Button _modelButton;
    private readonly Button _exportMaterialButton;
    private readonly Button _applyButton;
    private readonly Button _scanButton;
    private readonly Button _checkButton;
    private readonly Button _viewButton;
    private readonly Label _progress = Ui.StatusText();
    private readonly Button _cancelButton;
    private readonly Label _generatedStatus = Ui.StatusText();
    private readonly Label _generatedNumbers = Ui.Mono();
    private readonly Button _contractButton;
    private readonly Button _buildButton;
    private readonly Button _buildLogButton;
    private readonly Label _buildStatus = Ui.StatusText();
    private readonly Label _contractStatus = Ui.StatusText();
    private readonly Label _remoteStatus = Ui.StatusText();
    private readonly Label _remoteNumbers = Ui.Mono();
    private readonly FlowLayoutPanel _remoteActions = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Button _importRemoteButton;
    private readonly Button _remoteCheckButton;
    private readonly Button _reloadSwaggerButton;
    private readonly Button _decisionsButton;
    private readonly Button _supplementsButton;
    private readonly Button _remoteViewButton;
    private readonly Button _syncButton;
    private readonly Button _journalButton;
    private readonly Button _reportButton;
    private readonly Label _reportStatus = Ui.StatusText();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private CheckTask? _selectedTask;
    // 选中任务 Id：重建前先取局部（Items.Clear 触发选中事件会抹掉字段），
    // 被筛掉的任务在清空筛选后仍回到选中（与配置页同一口径）。
    private string? _selectedId;
    private bool _runningShown;
    private bool _splitAdjusted;
    // 列排序状态（ListSort 口径）：未排序时按任务创建时间倒序（TaskStore.List 的顺序）。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _columnTitles;
    // 排序状态跨启动记忆（ListSortMemory 口径）：记本页那一列的标题与方向，认不出标题即回退未排序。
    private readonly ListSortMemory _sortMemory;

    public TasksPage(TaskStore tasks, SourceScanService scan, JobRunner jobs, ProfileStore profiles, DraftService drafts, CommentPlanService plans, PropagationService propagation, BuildService builds, ContractReviewService reviews, CheckService checks, DecisionService decisionSaves, SupplementService supplementSaves, ReportExportService exports, SyncPlanService syncPlans, SyncExecutor syncExecutor, ListSortMemory sortMemory)
    {
        _tasks = tasks;
        _scan = scan;
        _jobs = jobs;
        _profiles = profiles;
        _drafts = drafts;
        _plans = plans;
        _propagation = propagation;
        _builds = builds;
        _reviews = reviews;
        _checks = checks;
        _decisionSaves = decisionSaves;
        _supplementSaves = supplementSaves;
        _exports = exports;
        _syncPlans = syncPlans;
        _syncExecutor = syncExecutor;
        _sortMemory = sortMemory;

        var newButton = Ui.Primary(NewTaskLabel);
        newButton.Click += (_, _) => ShowNewTaskDialog();
        var refreshButton = Ui.Ghost("刷新");
        refreshButton.Click += (_, _) => RefreshData();
        // 筛选框（DESIGN.md §6「列表筛选」）：输入即筛选，Esc 清空回到全部任务，
        // Enter 把焦点与选中交给筛选结果第一行；只改呈现与焦点，不写任务数据。
        _filter.Width = 220;
        _filter.PlaceholderText = ListFilter.TaskPlaceholder;
        _filter.TextChanged += (_, _) => LoadTasks();
        _filter.KeyDown += (_, e) =>
        {
            // Ctrl+F 把焦点交给框、Enter 把选中交给行：筛完不必再用手点进列表。
            // Esc 不在这里接——它归壳分发到可见页（TryClearListFilter），焦点在列表或工具栏上时同样算。
            if (e.KeyCode == Keys.Enter && ListKeys.IsEnterGesture(e.Shift, e.Control, e.Alt))
            {
                Ui.FocusFirstResult(_list);
                e.SuppressKeyPress = true;
                return;
            }
        };
        _list.SelectedIndexChanged += (_, _) => OnSelectedChanged();
        // 标识列（首列）吸收剩余可视宽度：窗口或列表尺寸变化即重算（ListColumns 口径）。
        _list.Resize += (_, _) => FitColumns();
        _list.Columns.Add("任务", TaskNameColumnWidth);
        _list.Columns.Add("配置", 140);
        _list.Columns.Add("下一步", 150);
        _list.Columns.Add("接口", 56);
        _list.Columns.Add("注释候选", 76);
        _list.Columns.Add("Apifox", 110);
        _list.Columns.Add("更新时间", 140);
        _columnTitles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        // 上次排序的列与方向跨启动生效（ListSortMemory 口径）：在任何一次加载之前落在字段上，
        // 首次显示即带着方向标记呈现；认不出标题（列增删或文案改动）时回退创建时间倒序。
        (_sortColumn, _sortDescending) = _sortMemory.Read(ListSortMemoryKeys.Tasks, _columnTitles);

        var nextTag = new Label
        {
            Text = "下一步",
            AutoSize = true,
            Font = UiTokens.CaptionBold.Value,
            ForeColor = UiTokens.Muted,
            Margin = new Padding(0, 0, UiTokens.Sm, 0),
        };
        _nextHead.Controls.Add(nextTag);
        _nextHead.Controls.Add(_nextTitle);

        // 三格按钮按 StageSteps.Keys 的顺序出厂：文案在每次刷新时取 StageSteps.ButtonText，
        // 点击只滚到对应分区（不改任何状态、不写数据）。
        _stepButtons = new Button[StageSteps.Keys.Length];
        for (var i = 0; i < StageSteps.Keys.Length; i++)
        {
            var key = StageSteps.Keys[i];
            var stepButton = Ui.Ghost("");
            stepButton.Margin = new Padding(0, 0, UiTokens.Sm, 0);
            stepButton.Click += (_, _) => JumpToStage(key);
            _stepButtons[i] = stepButton;
            _stepper.Controls.Add(stepButton);
        }
        _stepperHint.Margin = new Padding(0, 0, 0, 0);
        _stepper.Controls.Add(_stepperHint);

        _gapButton = Ui.Ghost("查看注释缺口");
        _gapButton.Click += (_, _) => ShowScanResults();
        _candidatesButton = Ui.Ghost("查看注释候选");
        _candidatesButton.Click += (_, _) => ShowCandidates();
        _ruleButton = Ui.Ghost("规则提取候选");
        _ruleButton.Click += (_, _) => StartRuleExtraction();
        _modelButton = Ui.Ghost("模型生成候选");
        _modelButton.Click += (_, _) => StartModelGeneration();
        _exportMaterialButton = Ui.Ghost("导出材料");
        _exportMaterialButton.Click += (_, _) => ExportMaterial();
        _applyButton = Ui.Ghost("注释回写");
        _applyButton.Click += (_, _) => ShowApply();
        _scanButton = Ui.Ghost("开始扫描");
        _scanButton.Click += (_, _) => StartScan();
        _cancelButton = Ui.DangerGhost("取消");
        _cancelButton.Click += (_, _) => CancelScan();
        _scanActions.Controls.Add(_gapButton);
        _scanActions.Controls.Add(_candidatesButton);
        _scanActions.Controls.Add(_ruleButton);
        _scanActions.Controls.Add(_modelButton);
        _scanActions.Controls.Add(_exportMaterialButton);
        _scanActions.Controls.Add(_applyButton);
        _scanActions.Controls.Add(_scanButton);
        _scanActions.Controls.Add(_cancelButton);
        _scanActions.Controls.Add(_progress);
        _cancelButton.Visible = false;
        _progress.Visible = false;
        _progress.Margin = new Padding(UiTokens.Md, 0, 0, 0);

        _checkButton = Ui.Ghost("核对传播");
        _checkButton.Click += (_, _) => StartPropagationCheck();
        _viewButton = Ui.Ghost("查看核对结果");
        _viewButton.Click += (_, _) => ShowPropagation();
        _buildButton = Ui.Ghost("执行定向构建");
        _buildButton.Click += (_, _) => StartBuild();
        _buildLogButton = Ui.Ghost("查看构建输出");
        _buildLogButton.Click += (_, _) => ShowBuildLog();
        _generatedActions.Controls.Add(_checkButton);
        _generatedActions.Controls.Add(_viewButton);
        _generatedActions.Controls.Add(_buildButton);
        _generatedActions.Controls.Add(_buildLogButton);

        _contractButton = Ui.Ghost("查看响应契约");
        _contractButton.Click += (_, _) => ShowContracts();

        _importRemoteButton = Ui.Ghost("导入离线远端 JSON");
        _importRemoteButton.Click += (_, _) => ImportOfflineRemote();
        _remoteCheckButton = Ui.Ghost("开始检查");
        _remoteCheckButton.Click += (_, _) => StartRemoteCheck();
        // 冻结副本的显式刷新入口（NextStep.CanReloadSwagger 单一口径）：冻结过才显示，默认隐藏。
        _reloadSwaggerButton = Ui.Ghost(NextStep.ReloadSwaggerLabel);
        _reloadSwaggerButton.Visible = false;
        _reloadSwaggerButton.Click += (_, _) => StartRemoteCheck(reloadSwagger: true);
        _decisionsButton = Ui.Ghost("处理差异确认");
        _decisionsButton.Click += (_, _) => ShowDecisions();
        _supplementsButton = Ui.Ghost("补充说明");
        _supplementsButton.Click += (_, _) => ShowSupplements();
        _remoteViewButton = Ui.Ghost("查看检查结果");
        _remoteViewButton.Click += (_, _) => ShowCheckResults();
        _syncButton = Ui.Ghost("同步预览");
        _syncButton.Click += (_, _) => ShowSyncPreview();
        // 任务日志（只读）：注释回写与同步执行把每一步追加到 journal.jsonl，这里按时间顺序看「做到哪一步」。
        _journalButton = Ui.Ghost("查看任务日志");
        _journalButton.Click += (_, _) => ShowJournal();
        _remoteActions.Controls.Add(_importRemoteButton);
        _remoteActions.Controls.Add(_remoteCheckButton);
        _remoteActions.Controls.Add(_reloadSwaggerButton);
        _remoteActions.Controls.Add(_decisionsButton);
        _remoteActions.Controls.Add(_supplementsButton);
        _remoteActions.Controls.Add(_remoteViewButton);
        _remoteActions.Controls.Add(_syncButton);
        _remoteActions.Controls.Add(_journalButton);

        _reportButton = Ui.Ghost("查看报告与导出");
        _reportButton.Click += (_, _) => ShowReports();

        // 空态按钮是独立实例：同一控件不能同时挂在工具栏与空态面板上；动作与工具栏同一入口，不复制逻辑。
        _emptyCreate.Click += (_, _) => ShowNewTaskDialog();
        _emptyClear.Click += (_, _) => ClearListFilter();
        var emptyActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTokens.Bg };
        emptyActions.Controls.Add(_emptyCreate);
        emptyActions.Controls.Add(_emptyClear);
        _empty = BuildEmpty(emptyActions);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Bg, ColumnCount = 2, Margin = new Padding(0) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 0, UiTokens.Md, 0) };
        buttons.Controls.Add(newButton);
        buttons.Controls.Add(refreshButton);
        var filterField = Ui.Field(_filter);
        filterField.Margin = new Padding(UiTokens.Md, 0, 0, 0);
        buttons.Controls.Add(filterField);
        _status.Dock = DockStyle.Fill;
        toolbar.Controls.Add(buttons, 0, 0);
        toolbar.Controls.Add(_status, 1, 0);

        _detail = BuildDetail();
        _detailHint = Ui.Caption("选择左侧任务查看阶段进度。");
        _detailHint.Dock = DockStyle.Fill;
        _detailHint.TextAlign = ContentAlignment.MiddleCenter;

        // 清单区形状（发丝边 + 叠在前面的空态）取 Ui.ListHost 一份：两页同一份，z 序只在那里定。
        _split.Panel1.Controls.Add(Ui.ListHost(_list, _empty));
        // 停靠按集合倒序生效：滚动宿主最后加入、在提示标签之下填满剩余空间。
        _detailScroll.Controls.Add(_detail);
        _split.Panel2.Controls.Add(_detailHint);
        _split.Panel2.Controls.Add(_detailScroll);
        _split.Panel2.BackColor = UiTokens.Bg;
        // Panel2MinSize 与 SplitterDistance 依赖真实宽度，等首次显示页面再设置（构造期控件还没有尺寸）。
        _split.SplitterWidth = 1;

        Controls.Add(_split);
        Controls.Add(toolbar);

        _timer.Tick += (_, _) => OnTick();
    }

    private Panel BuildEmpty(Control actions)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(_emptyTitle, 0, 1);
        layout.Controls.Add(_emptyHint, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        panel.Controls.Add(layout);
        panel.Visible = false;
        return panel;
    }

    private TableLayoutPanel BuildDetail()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddDetailRow(grid, _stepper);
        AddDetailRow(grid, _nextHead);
        AddDetailRow(grid, _nextDetail);
        AddDetailRow(grid, _nextActions);
        AddDetailRow(grid, _detailName);
        AddDetailRow(grid, _detailMeta);
        AddDetailRow(grid, _detailRange);
        AddDetailRow(grid, _sourceSection);
        AddDetailRow(grid, _sourceStatus);
        AddDetailRow(grid, _sourceNumbers);
        AddDetailRow(grid, _scanActions);
        AddDetailRow(grid, Ui.Caption("「查看注释缺口」「规则提取候选」「模型生成候选」「导出材料」「查看注释候选」与「注释回写」在扫描完成后可用；「注释回写」需要有已接受且未回写的候选。「模型生成候选」只在配置的草稿生成方式为 OpenAI 兼容模型服务时可用，发送材料前会先确认范围；没有模型服务时可用「导出材料」交给编码助手处理，或在「查看注释缺口」里手工填写，结果都在「查看注释候选」审阅。"));
        AddDetailRow(grid, _generatedSection);
        AddDetailRow(grid, _generatedStatus);
        AddDetailRow(grid, _generatedNumbers);
        AddDetailRow(grid, _generatedActions);
        AddDetailRow(grid, _buildStatus);
        AddDetailRow(grid, Ui.Caption("「核对传播」需要有已接受的注释候选；核对不构建项目，XML 与 Swagger 重新生成后才会更新。工具默认不构建、不 restore、不启动服务：需要刷新 XML 文档时，「执行定向构建」确认后在任务的工作区执行 dotnet build --no-restore，结果与输出尾部在「查看构建输出」查看。"));
        AddDetailRow(grid, SectionCaption("响应契约"));
        AddDetailRow(grid, _contractStatus);
        AddDetailRow(grid, _contractButton);
        AddDetailRow(grid, Ui.Caption("契约在扫描时从返回表达式与调用链静态追踪生成；「查看响应契约」在扫描完成后可用。确认后的契约在后续 Apifox 检查中替换该接口的 200 响应体；确认或拒绝后需重新检查才会生效。"));
        AddDetailRow(grid, _remoteSection);
        AddDetailRow(grid, _remoteStatus);
        AddDetailRow(grid, _remoteNumbers);
        AddDetailRow(grid, _remoteActions);
        AddDetailRow(grid, Ui.Caption("检查为只读：读取分支概览与范围内接口、模型详情，与目标比较，不写入远端；在源码扫描完成后可用，离线任务先导入作为「远端」的 OpenAPI JSON。配置了 Swagger 时首次检查先加载并冻结副本，之后的检查都基于冻结副本；源码或 Swagger 更新后用「重新加载 Swagger 并检查」重新冻结再检查。「处理差异确认」与「补充说明」在检查完成后可用：前者逐项确认高风险变化、选择说明冲突与绑定模型，后者逐接口补充接口标题、说明、参数与请求体、200 响应的说明或选择「保留现状」；两者保存后都需重新检查才会生效。「同步预览」在检查完成后可用：勾选接口生成冻结计划，查看逐动作写入内容、排除原因与保留设置；「确认同步」在计划待确认且未过期时可用，逐项写入并立即回读核对，失败即停，执行结束后可「重新核对（只读）」；离线任务不能同步。"));
        AddDetailRow(grid, SectionCaption("报告与交接"));
        AddDetailRow(grid, _reportStatus);
        AddDetailRow(grid, _reportButton);
        AddDetailRow(grid, Ui.Caption("报告基于当前任务数据生成并脱敏：分阶段结果、接口变化与未完成事项如实列出。导出 Markdown/HTML/JSON 报告与前端交接说明后自行发给前端或测试。"));
        return grid;
    }

    private static Label SectionCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTokens.CaptionBold.Value,
        ForeColor = UiTokens.Muted,
        Margin = new Padding(0, UiTokens.Lg, 0, 0),
    };

    private static void AddDetailRow(TableLayoutPanel grid, Control control)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(control.Margin.Left, control.Margin.Top, 0, UiTokens.Xs);
        grid.Controls.Add(control, 0, row);
    }

    public void OnPageShown()
    {
        // 首次显示时按当前宽度布置分割：详情区取设计宽，列表吸收剩余。门控按可行性条件
        // （详情设计宽 + 列表下限 + 分隔条宽）而不是任意魔数——壳最小窗口（1024）下分割面板约 776px，
        // 旧门槛宽度过大、在此恒假，分割距离停在 WinForms 默认值 50px，任务列表只剩一条缝。
        if (!_splitAdjusted && _split.Width >= DetailDesignWidth + ListMinWidth + _split.SplitterWidth)
        {
            try
            {
                _split.Panel2MinSize = DetailMinWidth;
                _split.SplitterDistance = _split.Width - DetailDesignWidth;
                // 顺序在分割距离之后：此时距离已不小于列表下限，设置最小尺寸不会与当前布局冲突。
                _split.Panel1MinSize = ListMinWidth;
            }
            catch (InvalidOperationException)
            {
                // 宽度仍不足以放下最小分割时保持默认分割位置。
            }
            _splitAdjusted = true;
        }
        LoadTasks();
        _timer.Start();
    }

    public void OnPageHidden()
    {
        _timer.Stop();
    }

    public void RefreshData() => LoadTasks();

    /// <summary>
    /// 把键盘交给任务列表（壳在「这一页出现」时调用：切页与启动，第九十六轮）。
    /// 这一页的列表在重建后按 Id 恢复选中（第三十四轮同一口径），详情区已经显示那一行——
    /// 而切页后键盘落在导航按钮上（点导航按钮时焦点就在那个按钮上），方向键动的是别的控件、
    /// Ctrl+C 也复制不到那一行，「有一行被标出来」只成立了一半。
    /// 只交键盘：不选中、不滚动、不重建清单、不写数据；判定与触达都在共享入口（<see cref="Ui.KeepKeyboardOnQueue"/>）。
    /// 重建路径（刷新、筛选、作业结束）不交——那一屏的下一步是换关键词或看结果，交键盘会把用户正在改的那一格抢走。
    /// </summary>
    public void KeepKeyboardOnQueue() => Ui.KeepKeyboardOnQueue(_list);

    /// <summary>Ctrl+F：焦点交给筛选框并选中已有关键词（重输即替换，与 Windows 搜索框一致）；只移焦点，不改筛选结果。</summary>
    public void FocusListFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    /// <summary>
    /// Esc 退出筛选（壳的分发落到当前可见页）：这一屏的筛选框里留着关键词才接住这一次按键，
    /// 要不要接由 ListFilter.EscClears 判定，页内不另写一套；接住就清空并把键盘留在框里（重输即替换）。
    /// </summary>
    public bool TryClearListFilter()
    {
        if (!ListFilter.EscClears(Visible, ListFilter.IsActive(_filter.Text))) return false;
        ClearListFilter();
        return true;
    }

    /// <summary>
    /// 清空筛选的唯一路径：Esc（壳分发到本页，焦点在这屏哪个控件上都算——判定在 ListFilter.EscClears）
    /// 与空态的「清空筛选」按钮都走这里（与配置页同一口径）。
    /// 清空经 TextChanged 触发既有重建，被筛掉的行按选中 Id 恢复，不写任务数据。
    /// </summary>
    private void ClearListFilter()
    {
        _filter.Text = "";
        _filter.Focus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private void SetStatus(string? message, Color? color = null)
    {
        _status.Text = message ?? "";
        _status.ForeColor = color ?? UiTokens.BadText;
    }

    private void LoadTasks()
    {
        // 先取局部：重建清空列表会触发选中事件把字段抹成 null。
        var selectedId = _selectedId;
        try
        {
            var all = _tasks.List();
            // 筛选先于排序（ListFilter → ListSort）：筛掉的行不参与排序，排序状态与筛选状态各自留在本页。
            var tasks = ListSort.Order(ListFilter.Apply(all, _filter.Text, FilterFields), _sortColumn, _sortDescending, SortKey);
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var task in tasks)
            {
                var item = new ListViewItem(task.Name) { Tag = task };
                item.SubItems.Add(task.ProfileSnapshot.Name);
                var next = NextStep.Compute(task);
                var nextCell = item.SubItems.Add(next.Title);
                item.SubItems.Add(task.Stages.Source.Endpoints > 0 ? task.Stages.Source.Endpoints.ToString() : "—");
                item.SubItems.Add(task.Stages.Source.Candidates > 0 ? task.Stages.Source.Candidates.ToString() : "—");
                var remote = item.SubItems.Add(TaskDisplay.RemoteStatusText(task.Stages.Remote.Status));
                item.UseItemStyleForSubItems = false;
                nextCell.ForeColor = ToneColor(next.Tone);
                remote.ForeColor = ToneColor(TaskDisplay.RemoteTone(task.Stages.Remote.Status));
                item.SubItems.Add(task.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            RefreshSortHeaders();
            FitColumns();
            _empty.Visible = tasks.Count == 0;
            _list.Visible = tasks.Count > 0;
            RefreshEmptyState();
            // 状态行如实给出筛选结果的数量（筛到零行时也是真实数字，不显示成没有任务）。
            SetStatus(ListFilter.IsActive(_filter.Text) ? $"筛选出 {tasks.Count} 个任务（共 {all.Count} 个）" : null, UiTokens.Muted);
            if (selectedId is not null)
            {
                SelectTask(selectedId);
            }
            else
            {
                RefreshDetail();
            }
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("读取任务失败：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>
    /// 空态两态（DESIGN.md §7「空态不留白」「空态=说明现在有什么可做 + 唯一行动按钮」）：
    /// 没有任务时给首次使用的下一步与「新建任务」，筛到零行时说明是筛选结果并给「清空筛选」——
    /// 不把筛选结果说成没有任务，也不给筛到零行的用户一个「新建」当作下一步。
    /// </summary>
    private void RefreshEmptyState()
    {
        var filtered = ListFilter.IsActive(_filter.Text);
        _emptyTitle.Text = filtered ? ListFilter.TaskNoMatchTitle : EmptyTitle;
        _emptyHint.Text = filtered ? ListFilter.TaskNoMatchHint : EmptyHint;
        _emptyCreate.Visible = !filtered;
        _emptyClear.Visible = filtered;
    }

    /// <summary>
    /// 筛选字段（与单元格同源）：任务名、配置名、下一步标题、Apifox 状态文字——
    /// 用户在列表上看到什么就能按什么找，不引入界面上不存在的字段。
    /// </summary>
    private static string?[] FilterFields(CheckTask task) =>
    [
        task.Name,
        task.ProfileSnapshot.Name,
        NextStep.Compute(task).Title,
        TaskDisplay.RemoteStatusText(task.Stages.Remote.Status),
    ];

    /// <summary>
    /// 标识列（首列「任务」）吸收剩余可视宽度（ListColumns 口径）：列合计对齐可视宽度，
    /// 默认窗口宽度下「更新时间」等列尾列不再被横向滚动条藏住；窗口放不下固定列合计时
    /// 首列回退设计宽度、横向滚动条如实出现。Resize 与重建后都会调用。
    /// </summary>
    private void FitColumns()
    {
        var widths = _list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        _list.Columns[0].Width = ListColumns.FillWidth(_list.ClientSize.Width, TaskNameColumnWidth, widths);
    }

    /// <summary>
    /// 点击列头排序（DESIGN.md §6「列表列排序」）：同一列再点反向，换一列从升序开始；
    /// 排序状态留在本页并按列标题记下（<see cref="_sortMemory"/>），刷新、作业结束与切页回来后的
    /// 重建都按同一列同一方向呈现，下次启动回来仍是这一列。
    /// 只改呈现顺序：任务数据一行未动，选中按任务 Id 恢复（<see cref="SelectTask"/>）。
    /// </summary>
    private void SortBy(int column)
    {
        (_sortColumn, _sortDescending) = ListSort.Toggle(_sortColumn, _sortDescending, column);
        _sortMemory.Save(ListSortMemoryKeys.Tasks, _columnTitles, _sortColumn, _sortDescending);
        LoadTasks();
    }

    /// <summary>表头方向标记：排序列追加 ▲/▼，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSortHeaders()
    {
        for (var i = 0; i < _list.Columns.Count && i < _columnTitles.Length; i++)
        {
            _list.Columns[i].Text = ListSort.Header(_columnTitles[i], i, _sortColumn, _sortDescending);
        }
    }

    /// <summary>
    /// 列排序键（与建列顺序一一对应）：名称、配置、下一步按文字；接口与注释候选按数量；
    /// Apifox 按状态文字；更新时间按时间。文字列按当前区域比较，数量与时间列按数值比较。
    /// </summary>
    private static IComparable? SortKey(CheckTask task, int column) => column switch
    {
        0 => task.Name,
        1 => task.ProfileSnapshot.Name,
        2 => NextStep.Compute(task).Title,
        3 => task.Stages.Source.Endpoints,
        4 => task.Stages.Source.Candidates,
        5 => TaskDisplay.RemoteStatusText(task.Stages.Remote.Status),
        6 => task.UpdatedAt,
        _ => null,
    };

    private void SelectTask(string taskId)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is CheckTask task && task.Id == taskId)
            {
                Ui.SelectItem(item);
                return;
            }
        }
        _selectedTask = null;
        RefreshDetail();
    }

    private void OnSelectedChanged()
    {
        _selectedTask = Ui.SelectedItem(_list)?.Tag as CheckTask;
        _selectedId = _selectedTask?.Id;
        _runningShown = false;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        var task = _selectedTask;
        if (task is null)
        {
            _detailScroll.Visible = false;
            _detailHint.Visible = true;
            return;
        }
        _detailScroll.Visible = true;
        _detailHint.Visible = false;

        _detailName.Text = task.Name;
        // 两行元信息的口径在 TaskSummary 一处（范围按模块/路径前缀/指定接口三个维度如实列出，
        // 元信息行补上版本备注）——本页不就地拼这两行文字。
        _detailMeta.Text = TaskSummary.MetaText(task);
        _detailRange.Text = TaskSummary.RangeText(task);

        // 下一步只算一次：横幅与三阶段进度条共用同一份判定（进度条的「当前」取 next.Stage）。
        var next = NextStep.Compute(task);
        RefreshNextBanner(next);
        RefreshStepper(task, next);

        var source = task.Stages.Source;
        _sourceStatus.Text = "状态：" + TaskDisplay.SourceStatusText(source.Status);
        _sourceStatus.ForeColor = ToneColor(TaskDisplay.SourceTone(source.Status));
        _sourceNumbers.Text = source.ScannedAt is null
            ? "尚未扫描"
            : $"接口 {source.Endpoints} · 注释位置 {source.Slots}（有效 {source.ValidSlots}）· 候选 {source.Candidates} · 已接受 {source.Accepted} · 已回写 {source.AppliedEdits}";

        var generated = task.Stages.Generated;
        _generatedStatus.Text = "状态：" + TaskDisplay.GeneratedStatusText(generated.Status);
        _generatedStatus.ForeColor = ToneColor(TaskDisplay.GeneratedTone(generated.Status));
        _generatedNumbers.Text = generated.CheckedAt is not { } checkedAt
            ? "尚未核对"
            : $"核对时间 {checkedAt.LocalDateTime:MM-dd HH:mm} · 位置 {generated.Entries} · 源码 {generated.InSource} · XML {generated.InXml} · Swagger {generated.InSwagger}";

        var remote = task.Stages.Remote;
        _remoteStatus.Text = "状态：" + TaskDisplay.RemoteStatusText(remote.Status) + (remote.CheckedRevision is { } rev && rev < task.Revision ? "（任务内容在检查后有变化，需要重新检查）" : "");
        _remoteStatus.ForeColor = ToneColor(TaskDisplay.RemoteTone(remote.Status));
        _remoteNumbers.Text = remote.SnapshotAt is null
            ? "尚未检查"
            : $"新增 {remote.Added} · 结构变化 {remote.StructureChanged} · 说明变化 {remote.DocChanged} · 冲突 {remote.Conflicts} · 阻断 {remote.Blocked}";

        // 远端区按钮：离线任务先导入作为「远端」的 OpenAPI JSON；配置了 Swagger 且未冻结时，下一次检查先加载并冻结 Swagger。
        _importRemoteButton.Visible = task.OfflineMode;
        _remoteCheckButton.Text = NextStep.NeedsSwaggerLoad(task) ? NextStep.LoadSwaggerLabel
            : File.Exists(_tasks.FilePath(task.Id, TaskFiles.Diff)) ? "重新检查" : "开始检查";
        // 冻结副本刷新入口与「开始检查/重新检查」并列：已冻结且非离线时显示（冻结页面工具栏同一规则）。
        _reloadSwaggerButton.Visible = NextStep.CanReloadSwagger(task);

        RefreshContractSection();
        RefreshBuildSection();
        RefreshReportSection();

        var job = _jobs.Running(task.Id);
        ShowJobState(job);
    }

    /// <summary>
    /// 三阶段进度条：三格文字取 StageSteps 单一口径（序号/✓、标签、状态、当前后缀），
    /// 当前那一格由 next.Stage 决定（与「下一步」横幅同一份判定）。
    /// </summary>
    private void RefreshStepper(CheckTask task, NextStep.Step next)
    {
        var steps = StageSteps.Build(task, next.Stage);
        for (var i = 0; i < _stepButtons.Length && i < steps.Count; i++)
        {
            _stepButtons[i].Text = StageSteps.ButtonText(steps[i], i);
        }
    }

    /// <summary>
    /// 点阶段跳到对应分区：只把详情滚动到那一区的标题，不改选中、不改任务数据、不触发任何动作。
    /// 认不出的键不动作（不猜一个分区）。
    /// </summary>
    private void JumpToStage(string key)
    {
        var section = key switch
        {
            NextStep.StageSource => _sourceSection,
            NextStep.StageGenerated => _generatedSection,
            NextStep.StageRemote => _remoteSection,
            _ => null,
        };
        if (section is null) return;
        _detailScroll.AutoScrollPosition = new Point(0, Math.Max(0, section.Top - UiTokens.Lg));
    }

    /// <summary>下一步引导横幅：标题按语义色调着色，动作按钮映射到本页既有入口（只做引导，不改变状态）。</summary>
    private void RefreshNextBanner(NextStep.Step next)
    {
        _nextTitle.Text = next.Title;
        _nextTitle.ForeColor = ToneColor(next.Tone);
        _nextDetail.Text = next.Detail;
        _nextActions.Controls.Clear();
        if (next.PrimaryLabel is { } primary) _nextActions.Controls.Add(NextActionButton(primary, next.PrimaryAction));
        if (next.SecondaryLabel is { } secondary) _nextActions.Controls.Add(NextActionButton(secondary, next.SecondaryAction));
    }

    private Button NextActionButton(string label, string? action)
    {
        var button = Ui.Ghost(label);
        button.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        button.Click += (_, _) => RunNextAction(action);
        return button;
    }

    /// <summary>引导动作 → 本页既有入口；入口自身的门控与异常处理（状态行内联）保持不变。</summary>
    private void RunNextAction(string? action)
    {
        switch (action)
        {
            case "startScan": StartScan(); break;
            case "startRuleExtraction": StartRuleExtraction(); break;
            case "openApply": ShowApply(); break;
            case "openPropagation": ShowPropagation(); break;
            case "openCandidates": ShowCandidates(); break;
            case "importOfflineRemote": ImportOfflineRemote(); break;
            case "startCheck": StartRemoteCheck(); break;
            case "openDecisions": ShowDecisions(); break;
            case "openSync": ShowSyncPreview(); break;
            case "openReports": ShowReports(); break;
            case "openRemoteResults": ShowCheckResults(); break;
        }
    }

    /// <summary>扫描区按作业状态切换：按钮（空闲）或进度 + 取消（执行中）。控件位置固定，不跳位。</summary>
    private void ShowJobState(JobInfo? job)
    {
        // 缺口清单、候选审阅、规则提取、注释回写、传播核对、响应契约与远端检查都基于扫描落盘产物：扫描完成后可用，
        // 作业执行中与未扫描时禁用（禁用态说明见下方固定说明行）；后台作业的进度与取消共用同一套固定占位控件。
        var scanReady = _selectedTask is { Stages.Source.ScannedAt: not null } && job is null;
        // 引导横幅的动作与各区按钮同一纪律：作业执行中一律禁用（进度在上方状态行）。
        foreach (var bannerButton in _nextActions.Controls.OfType<Button>())
        {
            bannerButton.Enabled = job is null;
        }
        _gapButton.Enabled = scanReady;
        _candidatesButton.Enabled = scanReady;
        _ruleButton.Enabled = scanReady;
        // 模型生成与冻结页面同一规则：配置的草稿生成方式为 OpenAI 兼容模型服务时可用；密钥缺失由服务在执行时拒绝并说明。
        _modelButton.Enabled = scanReady && DraftEntries.CanGenerateModel(_selectedTask?.ProfileSnapshot.Generation.Mode);
        _exportMaterialButton.Enabled = scanReady;
        _applyButton.Enabled = scanReady && (_selectedTask?.Stages.Source.Accepted ?? 0) > 0;
        _checkButton.Enabled = scanReady && (_selectedTask?.Stages.Source.Accepted ?? 0) > 0;
        _viewButton.Enabled = scanReady && _selectedTask is { Stages.Generated.CheckedAt: not null };
        // 定向构建只需要配置快照里的 API 项目路径（与冻结页面一致，不要求先扫描）；构建记录查看需要已有 build.json。
        _buildButton.Enabled = BuildStatus.CanStart(_selectedTask is not null) && job is null;
        _buildLogButton.Enabled = _selectedTask is not null && job is null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.BuildLog));
        _contractButton.Enabled = scanReady;
        _importRemoteButton.Enabled = scanReady && (_selectedTask?.OfflineMode ?? false);
        _remoteCheckButton.Enabled = scanReady;
        _reloadSwaggerButton.Enabled = scanReady;
        _decisionsButton.Enabled = scanReady && _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        // 补充说明与差异确认同门控：编辑位来自检查结果（离线任务同样可用，补充是本机任务数据）。
        _supplementsButton.Enabled = scanReady && _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        _remoteViewButton.Enabled = scanReady && _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        // 同步预览需要检查结果；离线任务不能同步（对话框内也如实指引，双重保险）。
        _syncButton.Enabled = scanReady && _selectedTask is { OfflineMode: false } && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        // 任务日志只要有记录就能看（注释回写与同步执行都会写它，离线任务同样有），与「查看构建输出」同一门控形状。
        _journalButton.Enabled = _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Journal));
        // 报告随时可看（与冻结页面一致，不要求先扫描）；作业执行中任务文件在被改写，禁用避免读到半份结果。
        _reportButton.Enabled = _selectedTask is not null && job is null;
        if (job is not null)
        {
            _runningShown = true;
            _scanButton.Visible = false;
            _progress.Visible = true;
            _cancelButton.Visible = true;
            _progress.Text = job.State == "running"
                ? $"正在{job.Kind}… {job.Progress:P0} {job.ProgressText}"
                : job is { State: "failed", Error: not null }
                    ? $"{job.Kind}失败：{job.Error} 正在刷新。"
                    : $"{job.Kind}结束（{TaskDisplay.JobStateText(job.State)}），正在刷新。";
            _progress.ForeColor = job.State == "failed" ? UiTokens.BadText : UiTokens.Text;
            _cancelButton.Enabled = job.State == "running";
        }
        else
        {
            _runningShown = false;
            _scanButton.Visible = true;
            _progress.Visible = false;
            _cancelButton.Visible = false;
        }
    }

    private void OnTick()
    {
        if (_selectedTask is null) return;
        var job = _jobs.Running(_selectedTask.Id);
        if (job is not null)
        {
            ShowJobState(job);
            return;
        }
        if (_runningShown)
        {
            // 作业刚结束：重读任务与列表，展示落盘的阶段结果。
            LoadTasks();
        }
    }

    private void StartScan()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        try
        {
            _jobs.Start(taskId, "源码扫描", async job => await _scan.ScanAsync(taskId, job));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始扫描：" + Redactor.Redact(ex.Message));
        }
    }

    private void CancelScan()
    {
        if (_selectedTask is null) return;
        _jobs.Cancel(_selectedTask.Id);
    }

    /// <summary>打开注释缺口清单：证据查看只读；手工填写与删除失效标签经既有 AddManualAsync 写任务数据成为候选。</summary>
    private void ShowScanResults()
    {
        if (_selectedTask is null) return;
        using var dialog = new ScanResultsDialog(_tasks, _drafts, _selectedTask);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    /// <summary>规则提取候选（后台作业）：只生成代码可证实的约束说明，写入任务目录 comment-drafts.json。</summary>
    private void StartRuleExtraction()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        try
        {
            _jobs.Start(taskId, "规则提取候选", async job => await _drafts.GenerateRuleAsync(taskId, [], job.Token));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始规则提取：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>打开注释候选审阅（接受/搁置/驳回写任务数据，不写源码）：关闭后刷新任务列表的候选计数。</summary>
    private void ShowCandidates()
    {
        if (_selectedTask is null) return;
        using var dialog = new CandidatesDialog(_drafts, _tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    /// <summary>
    /// 模型生成候选（需确认的外部发送）：先按材料口径统计位置与涉及源码文件，原生确认（默认按钮在「取消」）后
    /// 经共享 JobRunner 调既有 DraftService.GenerateWithModelAsync；候选写入任务数据，不写源码、不访问 Apifox。
    /// </summary>
    private void StartModelGeneration()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        int slots;
        List<string> files;
        try
        {
            var material = _drafts.BuildMaterial(taskId, [], out var included);
            slots = included.Count;
            if (slots == 0)
            {
                SetStatus(DraftService.NoSlotsMessage, UiTokens.WarnText);
                return;
            }
            files = DraftService.EvidenceFiles(material);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法准备生成材料：" + Redactor.Redact(ex.Message));
            return;
        }
        var choice = MessageBox.Show(this,
            DraftEntries.ModelConfirmText(slots, files),
            "确认发送材料",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.OK) return;
        try
        {
            _jobs.Start(taskId, "模型生成草稿", async job => await _drafts.GenerateWithModelAsync(taskId, [], true, job));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始模型生成：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>
    /// 导出材料：按冻结页面同一落点写任务目录副本 material.json，另经原生「另存为」写出同一内容（UTF-8 无 BOM）；
    /// 交给编码助手生成候选后用「导入编码助手结果」导回。只读扫描产物并写材料文件，不写源码、不访问 Apifox。
    /// </summary>
    private void ExportMaterial()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        JsonObject material;
        int slots;
        try
        {
            material = _drafts.BuildMaterial(taskId, [], out var included);
            slots = included.Count;
            if (slots == 0)
            {
                SetStatus(DraftService.NoSlotsMessage, UiTokens.WarnText);
                return;
            }
            FileStore.WriteText(_tasks.FilePath(taskId, TaskFiles.Material), material.ToJsonString(JsonDefaults.Options));
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法准备导出材料：" + Redactor.Redact(ex.Message));
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "导出材料",
            FileName = DraftEntries.MaterialFileName(taskId),
            Filter = "材料文件（*.json）|*.json|全部文件（*.*）|*.*",
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, material.ToJsonString(JsonDefaults.Options), new UTF8Encoding(false));
            SetStatus(DraftEntries.MaterialExportDoneText(slots), UiTokens.OkText);
        }
        catch (Exception ex)
        {
            SetStatus("材料导出失败：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>打开注释回写（生成计划、预览差异、确认后写源码或导出补丁）：关闭后刷新任务列表的回写计数。</summary>
    private void ShowApply()
    {
        if (_selectedTask is null) return;
        using var dialog = new ApplyPlanDialog(_plans, _tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    /// <summary>生成文档核对（后台作业）：重新加载源码，核对已确认注释是否写入源码、进入 XML 与 Swagger，结果落盘 propagation.json。</summary>
    private void StartPropagationCheck()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        try
        {
            _jobs.Start(taskId, "传播核对", async job => await _propagation.CheckAsync(taskId, job));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始核对：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>打开核对结果（只读）：数据来自任务目录 propagation.json，无写入动作。</summary>
    private void ShowPropagation()
    {
        if (_selectedTask is null) return;
        using var dialog = new PropagationDialog(_tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
    }

    /// <summary>执行定向构建（需确认）：确认后经既有 BuildService 在工作区执行 dotnet build --no-restore 刷新 XML 文档，结果落盘 build.json。</summary>
    private void StartBuild()
    {
        if (_selectedTask is null) return;
        var command = "dotnet " + string.Join(" ", BuildService.Arguments(_selectedTask.ProfileSnapshot.ApiProject));
        var choice = MessageBox.Show(this,
            BuildStatus.ConfirmText(command) + "\n\n确认执行？",
            "确认执行定向构建",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.OK) return;
        var taskId = _selectedTask.Id;
        try
        {
            _jobs.Start(taskId, "定向构建", async job => await _builds.RunAsync(taskId, job));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始构建：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>构建区状态行：任务目录 build.json 的上次构建结果（与「查看构建输出」对话框同一口径）。</summary>
    private void RefreshBuildSection()
    {
        if (_selectedTask is null) return;
        try
        {
            var (text, tone) = BuildStatus.LastText(FileStore.ReadNode(_tasks.FilePath(_selectedTask.Id, TaskFiles.BuildLog)));
            _buildStatus.Text = text;
            _buildStatus.ForeColor = ToneColor(tone);
        }
        catch (Exception ex)
        {
            _buildStatus.Text = "上次构建：记录读取失败：" + Redactor.Redact(ex.Message);
            _buildStatus.ForeColor = UiTokens.BadText;
        }
    }

    /// <summary>打开任务日志（只读）：数据来自任务目录 journal.jsonl，无写入动作、不重发任何动作。</summary>
    private void ShowJournal()
    {
        if (_selectedTask is null) return;
        using var dialog = new JournalDialog(_tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
    }

    /// <summary>打开构建输出（只读）：数据来自任务目录 build.json，无写入动作。</summary>
    private void ShowBuildLog()
    {
        if (_selectedTask is null) return;
        using var dialog = new BuildLogDialog(_tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
    }

    /// <summary>响应契约区状态行：契约在扫描时落盘，这里只读统计；确认/拒绝在契约对话框中执行。</summary>
    private void RefreshContractSection()
    {
        List<ResponseContract>? contracts;
        try
        {
            contracts = _selectedTask is null ? null : _tasks.Read<List<ResponseContract>>(_selectedTask.Id, TaskFiles.ResponseContract);
        }
        catch (Exception ex)
        {
            _contractStatus.Text = "状态：契约清单读取失败：" + Redactor.Redact(ex.Message) + "。请重新扫描后再打开。";
            _contractStatus.ForeColor = UiTokens.BadText;
            return;
        }
        if (contracts is null || contracts.Count == 0)
        {
            _contractStatus.Text = "状态：没有需要补充的响应契约（未配置通用响应封装，或接口返回具体类型）";
            _contractStatus.ForeColor = UiTokens.Muted;
            return;
        }
        var pending = contracts.Count(c => c.ReviewState == "pending");
        var blocked = contracts.Count(c => c.ReviewState == "blocked");
        _contractStatus.Text = $"状态：共 {contracts.Count} 个 · 待确认 {pending} · 已确认 {contracts.Count(c => c.ReviewState == "confirmed")} · 已拒绝 {contracts.Count(c => c.ReviewState == "rejected")} · 已阻断 {blocked} · 已失效 {contracts.Count(c => c.ReviewState == "stale")}";
        _contractStatus.ForeColor = blocked > 0 ? UiTokens.BadText : pending > 0 ? UiTokens.WarnText : UiTokens.OkText;
    }

    /// <summary>打开响应契约审阅（确认/拒绝/重置写任务数据并递增任务版本，不写源码、不访问 Apifox）：关闭后整页重读。</summary>
    private void ShowContracts()
    {
        if (_selectedTask is null) return;
        using var dialog = new ContractsDialog(_tasks, _selectedTask, _reviews);
        dialog.ShowDialog(FindForm());
        // 契约审阅递增任务版本（旧检查与旧计划过期）：与注释回写、差异确认等写任务数据的入口同一口径，
        // 关闭后整页重读——引导横幅、「任务内容在检查后有变化」提示与列表「下一步」列不得滞留旧版本。
        LoadTasks();
    }

    /// <summary>报告区状态行：任务目录里已导出的报告副本（report.md / handoff.md）与时间；预览与导出随时可用。</summary>
    private void RefreshReportSection()
    {
        if (_selectedTask is null) return;
        try
        {
            var exported = new[] { TaskFiles.ReportMarkdown, TaskFiles.Handoff }
                .Select(f => new FileInfo(_tasks.FilePath(_selectedTask.Id, f)))
                .Where(f => f.Exists)
                .ToList();
            _reportStatus.Text = exported.Count > 0
                ? "状态：任务目录已有导出副本 " + string.Join("、", exported.Select(f => $"{f.Name}（{f.LastWriteTime:MM-dd HH:mm}）"))
                : "状态：尚未导出报告";
            _reportStatus.ForeColor = exported.Count > 0 ? UiTokens.Text : UiTokens.Muted;
        }
        catch (Exception ex)
        {
            _reportStatus.Text = "状态：报告状态读取失败：" + Redactor.Redact(ex.Message);
            _reportStatus.ForeColor = UiTokens.BadText;
        }
    }

    /// <summary>打开报告与交接（只读预览 + 原生另存为导出，渲染走共享 ReportExportService）：关闭后刷新导出状态行。</summary>
    private void ShowReports()
    {
        if (_selectedTask is null) return;
        using var dialog = new ReportsDialog(_exports, _selectedTask);
        dialog.ShowDialog(FindForm());
        RefreshReportSection();
    }

    /// <summary>
    /// Apifox 差异检查（只读后台作业）：需要时先加载并冻结 Swagger，再读取远端与目标比较，结果落盘 diff.json；
    /// 不写入远端。检查始终基于冻结副本——已冻结后源码或 Swagger 有更新，经「重新加载 Swagger 并检查」
    /// （reloadSwagger）重新冻结再检查，与冻结页面的 recheck(true) 同一语义。
    /// </summary>
    private void StartRemoteCheck(bool reloadSwagger = false)
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        var loadSwagger = reloadSwagger || NextStep.NeedsSwaggerLoad(_selectedTask);
        try
        {
            _jobs.Start(taskId, "契约检查", async job =>
            {
                if (loadSwagger) await _checks.LoadSwaggerAsync(taskId, job.Token);
                return await _checks.RunAsync(taskId, job);
            });
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法开始检查：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>离线检查前置：选择一份 OpenAPI JSON 作为「远端」导入，写入任务目录 offline.remote.openapi.json；只读比较用，不访问 Apifox。</summary>
    private void ImportOfflineRemote()
    {
        if (_selectedTask is null) return;
        using var picker = new OpenFileDialog { Title = "导入离线远端", Filter = "OpenAPI JSON|*.json", CheckFileExists = true };
        if (picker.ShowDialog(FindForm()) != DialogResult.OK) return;
        var taskId = _selectedTask.Id;
        var path = picker.FileName;
        try
        {
            _jobs.Start(taskId, "导入离线远端", async job =>
                await _checks.ImportOfflineRemoteAsync(taskId, await File.ReadAllTextAsync(path, Encoding.UTF8, job.Token), job.Token));
            RefreshDetail();
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("无法导入离线远端：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>打开处理差异确认（高风险变化逐项确认、说明冲突选择、模型绑定，经共享 DecisionService 保存并递增任务版本）：关闭后刷新任务列表。</summary>
    private void ShowDecisions()
    {
        if (_selectedTask is null) return;
        using var dialog = new DecisionsDialog(_tasks, _selectedTask, _decisionSaves);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    /// <summary>打开补充说明（F10：逐接口编辑业务说明补充，经共享 SupplementService 保存并递增任务版本）：关闭后刷新任务列表。</summary>
    private void ShowSupplements()
    {
        if (_selectedTask is null) return;
        using var dialog = new SupplementsDialog(_tasks, _selectedTask, _supplementSaves);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    /// <summary>打开检查结果（只读）：数据来自任务目录 diff.json，无写入动作。</summary>
    private void ShowCheckResults()
    {
        if (_selectedTask is null) return;
        using var dialog = new CheckResultsDialog(_tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
    }

    /// <summary>打开同步预览与执行（勾选生成冻结计划、查看写入清单；确认后经既有 SyncExecutor 执行并回读核对）：关闭后刷新任务列表。</summary>
    private void ShowSyncPreview()
    {
        if (_selectedTask is null) return;
        using var dialog = new SyncPreviewDialog(_tasks, _syncPlans, _syncExecutor, _jobs, _selectedTask);
        dialog.ShowDialog(FindForm());
        LoadTasks();
    }

    private void ShowNewTaskDialog()
    {
        try
        {
            if (_profiles.List().Count == 0)
            {
                SetStatus("还没有项目配置。请先在「项目设置」新建配置，再创建任务。", UiTokens.WarnText);
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus("读取配置失败：" + Redactor.Redact(ex.Message));
            return;
        }
        using var dialog = new NewTaskDialog(_profiles);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var task = _tasks.Create(dialog.Request);
            if (dialog.StartScan) _jobs.Start(task.Id, "源码扫描", async job => await _scan.ScanAsync(task.Id, job));
            LoadTasks();
            SelectTask(task.Id);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("创建任务失败：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>语义色调 → 设计令牌颜色（docs/DESIGN.md §2：语义色只承担状态且同时有文字标签）。</summary>
    private static Color ToneColor(string tone) => tone switch
    {
        "ok" => UiTokens.OkText,
        "warn" => UiTokens.WarnText,
        "bad" => UiTokens.BadText,
        "accent" => UiTokens.Accent,
        "muted" => UiTokens.Muted,
        _ => UiTokens.Text,
    };
}

/// <summary>
/// 新建任务对话框：选配置、可选模块、路径前缀、指定接口与版本备注；「创建并扫描」立即开始源码扫描作业。
/// </summary>
internal sealed class NewTaskDialog : Form
{
    private readonly ProfileStore _profiles;
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _name = Ui.Input();
    private readonly ListView _modules = Ui.List(checkBoxes: true);
    private readonly TextBox _prefixes = Ui.Input();
    private readonly TextBox _keys = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly TextBox _note = Ui.Input();
    private readonly CheckBox _offline = new() { Text = "离线模式（本次任务不访问 Apifox，仅源码阶段）", AutoSize = true };
    private readonly Label _status = Ui.StatusLine();

    /// <summary>对话框确认后的创建请求；StartScan 表示创建后立即开始扫描。</summary>
    public CreateTaskRequest Request { get; private set; } = new();

    public bool StartScan { get; private set; }

    private sealed record ProfileItem(string Text, ProjectProfile Profile)
    {
        public override string ToString() => Text;
    }

    public NewTaskDialog(ProfileStore profiles)
    {
        _profiles = profiles;
        Text = "新建任务";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 560);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var scanButton = Ui.Primary("创建并扫描");
        scanButton.Click += (_, _) => Confirm(startScan: true);
        var createButton = Ui.Ghost("仅创建");
        createButton.Click += (_, _) => Confirm(startScan: false);
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = scanButton;
        CancelButton = cancelButton;

        _profile.SelectedIndexChanged += (_, _) => LoadModules();

        // 指定接口为多行输入：内层文本框定高，Ui.Field 的尺寸双向同步会带着描边面板一起变高。
        _keys.Height = 62;
        // 列宽留在 AddRow 的字段下限（380）之内：多出来的宽度是整行勾选的可点面，不逼出横向滚动条。
        _modules.Columns.Add("模块", 360);

        // 网格十二行（模块清单、指定接口、版本备注与提示）高于可视高度：放入可滚动宿主，离线模式行可达（DESIGN.md §6 长内容区）。
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "项目配置", _profile);
        AddRow(grid, "任务名称", Ui.Field(_name));
        AddRow(grid, "", Ui.Caption("留空则用「配置名 月-日 时:分」。"));
        var moduleHost = Ui.Hairline(_modules);
        moduleHost.Height = 96;
        AddRow(grid, "模块", moduleHost);
        AddRow(grid, "", Ui.Caption("勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。"));
        AddRow(grid, "路径前缀", Ui.Field(_prefixes));
        AddRow(grid, "", Ui.Caption("补充接口路径前缀，用「,」「;」分隔；可与模块并用。"));
        AddRow(grid, "指定接口", Ui.Field(_keys));
        AddRow(grid, "", Ui.Caption(TaskCreation.KeysHint));
        AddRow(grid, "版本备注", Ui.Field(_note));
        _note.PlaceholderText = TaskCreation.NotePlaceholder;
        AddRow(grid, "", Ui.Caption(TaskCreation.NoteFallbackHint));
        AddRow(grid, "", _offline);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（创建并扫描）在最右。
        buttons.Controls.Add(scanButton);
        buttons.Controls.Add(createButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        var gridScroll = Ui.ScrollHost();
        gridScroll.BackColor = UiTokens.Panel;
        gridScroll.Controls.Add(grid);
        Controls.Add(gridScroll);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        foreach (var profile in _profiles.List())
        {
            _profile.Items.Add(new ProfileItem(profile.Name, profile));
        }
        if (_profile.Items.Count > 0) _profile.SelectedIndex = 0;
    }

    private void LoadModules()
    {
        _modules.BeginUpdate();
        _modules.Items.Clear();
        if (_profile.SelectedItem is ProfileItem item)
        {
            foreach (var module in item.Profile.Modules)
            {
                _modules.Items.Add(new ListViewItem(TaskCreation.ModuleRowText(module)) { Tag = module });
            }
        }
        _modules.EndUpdate();
    }

    private void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (label.Length > 0)
        {
            var caption = Ui.Body(label);
            caption.Margin = new Padding(0, UiTokens.Md, UiTokens.Sm, UiTokens.Xs);
            grid.Controls.Add(caption, 0, row);
        }
        else
        {
            grid.Controls.Add(new Panel { Margin = new Padding(0) }, 0, row);
        }
        field.Margin = new Padding(0, UiTokens.Sm, 0, UiTokens.Xs);
        field.Width = Math.Max(field.Width, 380);
        grid.Controls.Add(field, 1, row);
    }

    private void Confirm(bool startScan)
    {
        if (_profile.SelectedItem is not ProfileItem item)
        {
            SetStatus("请选择项目配置。");
            return;
        }
        var moduleNames = _modules.Items.OfType<ListViewItem>()
            .Where(i => i.Checked && i.Tag is ModuleScope)
            .Select(i => ((ModuleScope)i.Tag!).Name)
            .ToList();
        var prefixes = _prefixes.Text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Request = new CreateTaskRequest
        {
            ProfileId = item.Profile.Id,
            Name = _name.Text.Trim(),
            ModuleNames = moduleNames,
            PathPrefixes = prefixes,
            EndpointKeys = TaskCreation.LineValues(_keys.Text),
            SourceVersionNote = TaskCreation.VersionNote(_note.Text),
            OfflineMode = _offline.Checked,
        };
        StartScan = startScan;
        DialogResult = DialogResult.OK;
    }

    private void SetStatus(string message)
    {
        _status.Text = message;
        _status.ForeColor = UiTokens.BadText;
    }
}
