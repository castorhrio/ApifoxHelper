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
/// 检查任务页：任务列表 + 选中任务的阶段概览 + 源码扫描（JobRunner 后台作业，进度与停止）
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
    // 空态说明那一句由 FirstRunPlan 一处给（第四百零五轮）：首程（一份配置都没有）与「有配置、缺任务」
    // 两态说的是两句不同的话，而两句都点名「此刻屏上真的那一颗按钮」——本页不就地拼这两句。
    private readonly Label _emptyTitle = Ui.Title(EmptyTitle);
    private readonly Label _emptyHint = Ui.Body(FirstRunPlan.Hint(FirstRunPlan.EmptyAction.NewTask), muted: true);
    // 「新建任务」文字单一落点：工具栏实心主按钮与空态按钮同名同源，不出现第二处字面。
    // 第四百零五轮起这个字面收在 FirstRunPlan（两页共用一份词汇），本页只留一个别名，不再写第二份字面。
    private const string NewTaskLabel = FirstRunPlan.NewTaskLabel;
    // 空态的唯一行动按钮（DESIGN.md §7）：没有任务时指向「新建任务」，筛到零行时指向「清空筛选」——
    // 同一时刻只显示其中一个，实心主按钮始终只有工具栏那一个。
    // 第四百零五轮起它的文字随「有没有配置」切换（首程那一态写「新建配置」，见 FirstRunPlan）：
    // 出厂这一句是「有配置」那一态的字，真正的取值在 RefreshEmptyState 里按同一份判定换。
    private readonly Button _emptyCreate = Ui.Ghost(NewTaskLabel);
    // 空态那颗按钮「此刻指向哪一步」（第四百零五轮）：出厂是「有配置」那一态，真正的取值在
    // RefreshEmptyState 里按 FirstRunPlan.EmptyActionFor 一处判、与按钮文字同一轮推导；
    // 点击只读这一笔，不在点击里再判一次「有没有配置」（判两次就会与屏上那颗字对不上）。
    private FirstRunPlan.EmptyAction _emptyAction = FirstRunPlan.EmptyAction.NewTask;
    private readonly Button _emptyClear = Ui.Ghost(ListFilter.ClearActionLabel);
    // 读取失败态的唯一行动按钮（LoadFailureState 口径）：动作回到 LoadTasks 同一个入口，不复制读取逻辑。
    private readonly Button _emptyRetry = Ui.Ghost(LoadFailureState.RetryLabel);
    // 读取失败门控：失败态优先于「筛到零行」与「还没有任务」——这一屏连「有没有任务」都还不知道。
    private bool _loadFailed;
    private readonly Label _status = Ui.StatusLine();
    // 状态行宿主（第一百六十四轮）：状态行 + 右侧「查看完整错误」入口。失败原因来自
    // Exception.Message，动辄两三百字符，AutoEllipsis 只解决「有下文」，读全与复制走这颗入口。
    private readonly StatusLineHost _statusHost;
    // 详情区固定设计宽（FixedPanel = Panel2）：窗口缩放时详情不变、列表吸收剩余宽度，
    // 与「标识列吸收剩余可视宽度」（ListColumns）同一方向；缺省 None 会让详情区随窗口按比例放大、偏离设计宽。
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, BackColor = UiTokens.Line, FixedPanel = FixedPanel.Panel2 };

    // 下一步引导横幅（口径在 NextStep）：标题按语义色调着色，动作按钮映射到本页既有入口。
    private readonly FlowLayoutPanel _nextHead = new() { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
    private readonly Label _nextTitle = Ui.Body();
    private readonly Label _nextDetail = Ui.Caption();
    // 四行动作按钮（_nextActions / _scanActions / _generatedActions / _remoteActions）按栏宽折行。
    // 机制是量出来的（RunDetailReach，wine 96 档）：这一族出厂时行面宽已经等于详情栏那一格（370px），
    // 不折行时行内却仍按自己算出的单行总宽排（最宽那一行 740px）——夹完不重排，越出行面的按钮被裁掉，
    // 拖横向滚动条也到不了（宿主的滚动区只认网格边界）。折行让行内按行面宽重排，右边界回到栏内。
    private readonly FlowLayoutPanel _nextActions = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
    // 横幅那两颗按钮出厂一次、之后只换字与显隐（第三百六十八轮）：此前每次刷新都 Controls.Clear() 再新建，
    // 于是「Tab 到那颗按钮、按空格」之后紧接着的那一次 RefreshDetail（StartScan 自己就调一次）把按钮从父面
    // 摘掉，键盘焦点跟着跳到别处——同一页另四行动作按钮（_stepper / _scanActions / _generatedActions /
    // _remoteActions）都是出厂一次、刷新只改 Enabled/Visible，这一行是唯一的例外。
    // 动作也各记一笔：按钮自己不带「点下去做什么」，换字时把这一次的动作一起换掉，点击只读当前那一笔。
    private readonly Button _nextPrimary = Ui.Ghost("");
    private readonly Button _nextSecondary = Ui.Ghost("");
    private string? _nextPrimaryAction;
    private string? _nextSecondaryAction;

    // 三阶段进度条（口径在 StageSteps）：三格 ghost 按钮 + 一句「点一下会发生什么」，
    // 点一下滚到对应分区（冻结页面 stepper 点一下切到该阶段页签的同一手势）。
    // 按可用宽换行是量出来的，不是猜的：wine 96 档截图逐像素扫到第一格白面 228px 宽、第二格从 272px
    // 起、到 402px 就被网格右边界截断（「未核」只剩半个字），第三格整格在可视区之外，详情区因此挂出
    // 一条横向滚动条。网格那一格 402px 是 RunDetailBreak 每一档都念的数。不换行时这一行读不全，
    // 而它恰恰是「三个阶段各到哪一步」的唯一凭据。
    private readonly FlowLayoutPanel _stepper = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
    private readonly Button[] _stepButtons;
    private readonly Label _stepperHint = Ui.Caption(StageSteps.JumpHint);
    // 折行的那三笔记录（口径与理由见 WrapStepCells）：签名挡住「在布局回调里改尺寸又触发布局」；
    // 单行那一版要多宽多高要趁那一格还没折行时记下——折过行之后它自己报回来的高是折行那一版的高。
    private readonly string[] _stepCellsApplied = new string[StageSteps.Keys.Length];
    private readonly Size[] _stepCellLineSizes = new Size[StageSteps.Keys.Length];
    private readonly int[] _stepCellLineFontHeights = new int[StageSteps.Keys.Length];
    // 三个分区的标题控件：进度条点击的滚动落点（文字取 StageSteps 常量，本页不就地拼）。
    private readonly Label _sourceSection = SectionCaption(StageSteps.SourceLabel);
    private readonly Label _generatedSection = SectionCaption(StageSteps.GeneratedLabel);
    private readonly Label _remoteSection = SectionCaption(StageSteps.RemoteSectionLabel);

    // 详情区（固定控件，按任务刷新文本，不整面板重建）。
    private readonly Label _detailHint;
    private readonly TableLayoutPanel _detail;
    // 详情有九个分区、高于可视高度：放进可滚动宿主，底部动作（同步/报告）可达（DESIGN.md §6 长内容区）。
    private readonly Panel _detailScroll = Ui.ScrollHost();
    // 详情区当前这一截滚动位置属于哪一个任务（第三百七十轮）：换任务时详情回到顶部，
    // 刷新同一个任务时保持原位——动作做完要看得见结果，那个位置不能被抹掉。
    private string? _detailTaskId;
    private readonly Label _detailName = Ui.Section();
    private readonly Label _detailMeta = Ui.Caption();
    private readonly Label _detailRange = Ui.Caption();
    // 「打开配置」这一跳（第二百六十三轮）：任务详情顶部那行元信息写着「配置 X」，而 X 此前只是一串只读文字——
    // 想改那个配置（换 Swagger 来源、改允许回写范围、补模块前缀）只能自己切到项目设置页、再在清单里按名字找回来。
    // 按钮文字、有没有落点、找不到时说什么三件事的唯一落点在 ProfileJump；本页只负责把它摆出来并发出请求。
    private readonly Button _openProfileButton = Ui.Ghost(ProfileJump.ButtonLabel);
    private readonly Label _sourceStatus = Ui.StatusText();
    private readonly Label _sourceNumbers = Ui.Mono();
    private readonly FlowLayoutPanel _scanActions = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
    private readonly FlowLayoutPanel _generatedActions = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
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
    private readonly Button _stopButton;
    private readonly Label _generatedStatus = Ui.StatusText();
    private readonly Label _generatedNumbers = Ui.Mono();
    private readonly Button _contractButton;
    private readonly Button _buildButton;
    private readonly Button _buildLogButton;
    private readonly Label _buildStatus = Ui.StatusText();
    private readonly Label _contractStatus = Ui.StatusText();
    private readonly Label _remoteStatus = Ui.StatusText();
    private readonly Label _remoteNumbers = Ui.Mono();
    private readonly FlowLayoutPanel _remoteActions = new() { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
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
    /// <summary>
    /// 这一页欠着一次结果没报的作业 Id（第三百七十八轮记法、第三百八十六轮改准）：作业结束之后
    /// <c>JobRunner.Running</c> 就答 null 了，要读回它的终态与失败原因只能按 Id 取（<c>JobRunner.Get</c>）。
    /// <para>
    /// <b>记在两处，而不是只记在「看到它在跑」那一次</b>：<see cref="StartTaskJob"/>（这一页自己起作业时）与
    /// <see cref="ShowJobState"/>（界面看到它在跑时）。第三百八十五轮的 wine 冒烟量到只记后一处的后果：
    /// 一次几十毫秒内就失败的作业，<c>RefreshDetail</c> 那一次已经看不到它在跑了，两个都没立起来，
    /// 于是那一句永远不会说出口——用户按了「开始扫描」，屏上对这次失败一句话都没有。
    /// 起作业时记下不依赖任何轮询，那一支就补上了。
    /// </para>
    /// <para>用掉即清（<see cref="ShowJobOutcome"/>），一次作业只报一次结果；报之前先核它属于当前选中的那一个任务
    /// （<see cref="_lastJobTaskId"/>），不对着另一个任务念上一个任务的旧结果。</para>
    /// </summary>
    private string? _lastJobId;
    /// <summary><see cref="_lastJobId"/> 那一次作业所属的任务 Id（<c>JobInfo.Scope</c> 就是这个 Id）。</summary>
    private string? _lastJobTaskId;
    /// <summary>
    /// 已经为哪一个作业请求过停止（第三百八十八轮）：按<b>作业</b> Id 记，不按任务 Id 记——
    /// 停止请求针对的是那一次执行，切到别的任务再切回来时作业还在跑，那句话仍该说。
    /// 用掉即清（<see cref="ShowJobState"/> 的「没有作业在跑」那一支）。
    /// </summary>
    private string? _stopRequestedJobId;
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
        // 这一跳要换页，而页面之间不互相引用（两页都由壳建、由壳切）：本页只发请求，接线在壳那一处。
        _openProfileButton.Click += (_, _) => RequestOpenProfile();
        // 状态行宿主先建：工具栏把它当一格控件加进去（标签与入口按钮的显隐由它自己管）。
        _statusHost = new StatusLineHost(_status);

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

        // 横幅两颗按钮在这里出厂一次（与上面三格按钮同一形状）：之后 RefreshNextBanner 只换字与显隐，
        // 不把控件从父面摘掉——摘掉一次，键盘焦点就丢一次。
        _nextPrimary.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _nextPrimary.Click += (_, _) => RunNextAction(_nextPrimaryAction);
        _nextSecondary.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _nextSecondary.Click += (_, _) => RunNextAction(_nextSecondaryAction);
        _nextActions.Controls.Add(_nextPrimary);
        _nextActions.Controls.Add(_nextSecondary);
        // 这一行的标记（第三百六十八轮）：冒烟段 RunBannerFocus 按它认出「横幅那一行」而不抄用户可见文案——
        // 同一屏上「开始扫描」在扫描区那一行也叫这个名字，按文案认会认错行。
        _nextActions.Tag = Ui.BannerRowMarker;

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
        _stopButton = Ui.DangerGhost("停止");
        _stopButton.Click += (_, _) => RequestStop();
        _scanActions.Controls.Add(_gapButton);
        _scanActions.Controls.Add(_candidatesButton);
        _scanActions.Controls.Add(_ruleButton);
        _scanActions.Controls.Add(_modelButton);
        _scanActions.Controls.Add(_exportMaterialButton);
        _scanActions.Controls.Add(_applyButton);
        _scanActions.Controls.Add(_scanButton);
        _scanActions.Controls.Add(_stopButton);
        _scanActions.Controls.Add(_progress);
        _stopButton.Visible = false;
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
        // 第四百零五轮起它按「此刻指向哪一步」分发（首程那一态指向「新建配置」，见 FirstRunPlan）：
        // 分发只读 RefreshEmptyState 记下的那一笔，不在点击里再判一次「有没有配置」。
        _emptyCreate.Click += (_, _) => RunEmptyCreate();
        _emptyClear.Click += (_, _) => ClearListFilter();
        _emptyRetry.Click += (_, _) => LoadTasks();
        var emptyActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTokens.Bg };
        emptyActions.Controls.Add(_emptyCreate);
        emptyActions.Controls.Add(_emptyClear);
        emptyActions.Controls.Add(_emptyRetry);
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
        toolbar.Controls.Add(buttons, 0, 0);
        // 填满工具栏剩余宽度（第一百六十五轮）：不设 Dock 时 Panel 停在缺省 200px，
        // 状态行这一格在 96/120 两档恒 194px（wine 实测），长失败原因永远放不下、入口永远挂着。
        // 列样式本来就是「按钮列 AutoSize + 状态行列 Percent 100」，缺的只是这一句。
        _statusHost.Dock = DockStyle.Fill;
        toolbar.Controls.Add(_statusHost, 1, 0);

        _detail = BuildDetail();
        _detailHint = Ui.Caption("选择左侧任务查看阶段进度。");
        _detailHint.Dock = DockStyle.Fill;
        _detailHint.TextAlign = ContentAlignment.MiddleCenter;
        // Ui.Caption 出厂是 AutoSize 标签：AutoSize 盖掉 Dock 给的尺寸，这一句就缩成文字本身那么大、
        // 停在栏的左上角（wine 截图量到在栏顶），而 MiddleCenter 什么也没居中。关掉 AutoSize，
        // Dock=Fill 才真的把这格铺满，居中才有对象。
        _detailHint.AutoSize = false;

        // 清单区形状（发丝边 + 叠在前面的空态）取 Ui.ListHost 一份：两页同一份，z 序只在那里定。
        _split.Panel1.Controls.Add(Ui.ListHost(_list, _empty));
        // 停靠按集合倒序生效：滚动宿主最后加入、在提示标签之下填满剩余空间。
        _detailScroll.Controls.Add(_detail);
        // 栏宽变了要重问一次：进度条里放不下整句的那一格自己折行（WrapStepCells）。
        _detail.Layout += (_, _) => WrapStepCells();
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
        AddDetailRow(grid, _openProfileButton);
        AddDetailRow(grid, _sourceSection);
        AddDetailRow(grid, _sourceStatus);
        AddDetailRow(grid, _sourceNumbers);
        AddDetailRow(grid, _scanActions);
        AddDetailRow(grid, Ui.Caption("什么时候可用？扫描完成后；回写另需已接受的候选，模型生成另需配置模型服务。"));
        AddDetailRow(grid, _generatedSection);
        AddDetailRow(grid, _generatedStatus);
        AddDetailRow(grid, _generatedNumbers);
        AddDetailRow(grid, _generatedActions);
        AddDetailRow(grid, _buildStatus);
        AddDetailRow(grid, Ui.Caption("结果需先接受候选：XML 与 Swagger 重新生成后才会更新，核对本身不构建项目。"));
        AddDetailRow(grid, SectionCaption("响应契约"));
        AddDetailRow(grid, _contractStatus);
        AddDetailRow(grid, _contractButton);
        AddDetailRow(grid, Ui.Caption("契约在扫描时静态追踪生成，确认或拒绝后需重新检查才生效。"));
        AddDetailRow(grid, _remoteSection);
        AddDetailRow(grid, _remoteStatus);
        AddDetailRow(grid, _remoteNumbers);
        AddDetailRow(grid, _remoteActions);
        AddDetailRow(grid, Ui.Caption("只读，不写入远端。离线任务需先导入远端 JSON 才能检查，且不能同步；源码或 Swagger 更新后用「重新加载 Swagger 并检查」重新冻结。"));
        AddDetailRow(grid, SectionCaption("报告与交接"));
        AddDetailRow(grid, _reportStatus);
        AddDetailRow(grid, _reportButton);
        AddDetailRow(grid, Ui.Caption("导出即可用：内容基于当前任务数据并已脱敏，发给前端或测试即可。"));
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

    /// <summary>
    /// 「打开配置」这一跳的请求（第二百六十三轮）：参数是任务绑定的配置 Id 与名字。
    /// <para>
    /// 本页<b>不</b>自己切页、也不引用另一页——两页都由壳建、由壳切（<c>NativeShellForm</c>），
    /// 这里只把「用户点了这一跳」这件事交出去，接线在壳那一处。名字随请求一起带过去，
    /// 是因为「跳过去找不到那一行」时要说清是哪一个配置（<see cref="ProfileJump.MissingStatus"/>）。
    /// </para>
    /// </summary>
    public event Action<string, string>? OpenProfileRequested;

    /// <summary>
    /// 「首程：去建一份配置」这一跳的请求（第四百零五轮）。
    /// <para>
    /// <b>补的是哪个缺口</b>：一份配置都没有时，任务页空态上唯一那颗按钮写着「新建任务」，点下去被一句
    /// 「请先在『项目设置』新建配置」拒回来——<b>屏上没有一步能到「项目设置」</b>。侧栏那颗导航按钮在，
    /// 但空态那句话与那颗按钮都没有把用户送过去，用户得自己想到「去点左边那一栏」。
    /// 这与第四十二轮「禁用不得移除下一步」、第五十轮「提示所指引的恢复路径不可达」是同一族。
    /// </para>
    /// <para>
    /// <b>本页不自己切页、也不引用另一页</b>：与 <see cref="OpenProfileRequested"/> 同一纪律——
    /// 两页都由壳建、由壳切，这里只把「用户点了这一跳」这件事交出去，接线在 <c>NativeShellForm</c> 那一处。
    /// </para>
    /// </summary>
    public event Action? NewProfileRequested;

    /// <summary>
    /// 「新建任务」对话框里「项目配置」下拉默认选中的那一份（第四百零八轮，具名 pass：harden）。
    /// <para>
    /// <b>补的是哪个缺口</b>：这个下拉此前一律选中清单第一项，而清单是按<b>配置名称</b>排的
    /// （<c>ProfileStore.List</c>），不是按创建时间。首程那条路上用户刚建完配置、回到本页点「新建任务」，
    /// 只要清单里还有一份名字排在它前面的配置，对话框默认选中的就是别人——照着默认值点「创建并扫描」，
    /// 任务就绑到了另一份配置上（扫另一个仓库、写另一个 Apifox 项目）。判定收在
    /// <see cref="NewTaskProfileChoice"/> 一处，本页只负责把「刚保存的那一份」这一笔带过去。
    /// </para>
    /// <para>
    /// <b>谁写这一笔</b>：壳（<c>NativeShellForm</c>）——项目设置页保存成功之后把那一份的 Id 交过来，
    /// 与「打开配置」「去建配置」两跳同一纪律：两页互不引用，请求由页发出、落点由壳接。
    /// 本页不自己读另一页的选中行，也不缓存配置清单。
    /// </para>
    /// <para>
    /// <b>认不出时不变差</b>：<c>null</c>（这一次启动还没保存过配置）或指向一份已被删掉的配置时，
    /// 下拉回退到第一项——就是本页此前的行为（见 <see cref="NewTaskProfileChoice.Index"/>）。
    /// </para>
    /// </summary>
    public string? PreferredProfileId { get; set; }

    /// <summary>
    /// 点「打开配置」：没有选中任务、或这个任务没有绑定配置（配置 Id 为空）时不发请求——
    /// 认不出该跳到哪一份配置时不跳，也不拿清单第一行顶替（判定在 <see cref="ProfileJump.CanJump"/>）。
    /// </summary>
    private void RequestOpenProfile()
    {
        if (_selectedTask is not { } task) return;
        if (!ProfileJump.CanJump(task.ProfileSnapshot.Id)) return;
        OpenProfileRequested?.Invoke(task.ProfileSnapshot.Id, task.ProfileSnapshot.Name);
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

    private void SetStatus(string? message, Color? color = null) =>
        _statusHost.SetText(message, color ?? UiTokens.BadText);

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
                // 逐子项样式在加入之前关掉：计数格的等宽字体是加进去那一刻交出去的（Ui.CountCell）。
                item.UseItemStyleForSubItems = false;
                item.SubItems.Add(task.ProfileSnapshot.Name);
                var next = NextStep.Compute(task);
                var nextCell = item.SubItems.Add(next.Title);
                Ui.CountCell(item, task.Stages.Source.Endpoints > 0 ? task.Stages.Source.Endpoints.ToString() : "—");
                Ui.CountCell(item, task.Stages.Source.Candidates > 0 ? task.Stages.Source.Candidates.ToString() : "—");
                var remote = item.SubItems.Add(TaskDisplay.RemoteStatusText(task.Stages.Remote.Status));
                nextCell.ForeColor = ToneColor(next.Tone);
                remote.ForeColor = ToneColor(TaskDisplay.RemoteTone(task.Stages.Remote.Status));
                item.SubItems.Add(task.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            RefreshSortHeaders();
            FitColumns();
            // 读成功即退出失败态：否则重试成功后仍停在失败态（清单有行、屏上却还说读不出来）。
            _loadFailed = false;
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
            // UserException 的文案本身就是给用户看的那一句，不再脱敏；仍进失败态（清单不留旧行）。
            ShowLoadFailure(ex.Message);
        }
        catch (Exception ex)
        {
            ShowLoadFailure(LoadFailureState.FailureStatus(LoadFailureState.Tasks.Title, Redactor.Redact(ex.Message)));
        }
    }

    /// <summary>
    /// 读取失败态（<see cref="LoadFailureState"/> 口径，与配置页同一形状）：清单区不留上一次的行——
    /// 这一屏刚刚证明「磁盘上的任务读不出来」，继续显示旧行等于把不可信的内容当成现状；
    /// 改为给一句可操作文案与唯一行动按钮「重试」（动作回到 <see cref="LoadTasks"/> 同一个入口）。
    /// 状态行仍如实给出脱敏后的原因：清单区讲下一步、状态行讲原因，两条通道各说一件事。
    /// </summary>
    private void ShowLoadFailure(string status)
    {
        _loadFailed = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.EndUpdate();
        // 清单空了，详情区没有可作用的对象：与「没有选中任务」同一呈现（提示标签 + 收起详情）。
        _selectedTask = null;
        _selectedId = null;
        RefreshDetail();
        _empty.Visible = true;
        _list.Visible = false;
        RefreshEmptyState();
        SetStatus(status);
    }

    /// <summary>
    /// 空态三态（DESIGN.md §7「空态不留白」「空态=说明现在有什么可做 + 唯一行动按钮」，与配置页同一口径）：
    /// 没有任务时给首次使用的下一步与「新建任务」，筛到零行时说明是筛选结果并给「清空筛选」，
    /// 读取失败时给「重试」——不把筛选结果说成没有任务，也不给筛到零行的用户一个「新建」当作下一步。
    /// 失败态优先：这一屏连「有没有任务」都还不知道，说成「筛到零行」或「还没有任务」都是把读不出来说成读到了空。
    /// </summary>
    private void RefreshEmptyState()
    {
        var filtered = ListFilter.IsActive(_filter.Text);
        // 首程那一态（第四百零五轮）：一份配置都没有时，空态那颗按钮指向「新建配置」而不是「新建任务」——
        // 否则第一次打开的人照着屏上唯一可点的东西按下去，被一句「请先在『项目设置』新建配置」拒回来，
        // 而屏上没有一步能到那一页。判定与文字都在 FirstRunPlan 一处，本页只把「此刻有几份配置」读给它。
        _emptyAction = FirstRunPlan.EmptyActionFor(ProfileCount());
        _emptyTitle.Text = _loadFailed ? LoadFailureState.Tasks.Title : filtered ? ListFilter.TaskNoMatchTitle : EmptyTitle;
        _emptyHint.Text = _loadFailed
            ? LoadFailureState.Tasks.Hint
            : filtered ? ListFilter.TaskNoMatchHint : FirstRunPlan.Hint(_emptyAction);
        _emptyCreate.Text = FirstRunPlan.Label(_emptyAction);
        _emptyCreate.Visible = !_loadFailed && !filtered;
        _emptyClear.Visible = !_loadFailed && filtered;
        _emptyRetry.Visible = LoadFailureState.RetryVisible(_loadFailed);
    }

    /// <summary>
    /// 此刻磁盘上有几份配置（<see cref="FirstRunPlan.EmptyActionFor"/> 的入参）；读不出来时答 <c>null</c>。
    /// <para>
    /// <b>为什么读不出来答 <c>null</c> 而不是 0</b>：这一屏连「有没有配置」都还不知道，说成「还没有配置」
    /// 是把「读不出来」说成「读到了空」（与 <see cref="ProfileLoadState"/> 那一族同一取舍）。
    /// <c>null</c> 在 <see cref="FirstRunPlan.EmptyActionFor"/> 里按「有配置」算——那时点下去走
    /// <see cref="ShowNewTaskDialog"/>，它自己会把读取失败的原因写进状态行，那是如实说、不是猜。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判「该不该把失败原因写进状态行」（那是 <see cref="ShowNewTaskDialog"/> 的事），
    /// 也不缓存——配置清单随时可能被另一页改掉，缓存一次就会让空态那颗按钮指着上一步的答案。
    /// </para>
    /// </summary>
    private int? ProfileCount()
    {
        try
        {
            return _profiles.List().Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 收窄到「读配置清单真会失败的那几种」：磁盘读不到、权限不足、某一份 JSON 坏了。
            // 这三件之外的不吞——那是别的缺陷，吞了等于把它藏起来（错误通道纪律：catch 至少做一件事）。
            // 答 null（不是 0）：这一屏连「有没有配置」都还不知道，说成「还没有配置」是把读不出来说成读到了空。
            // 原因由 <see cref="ShowNewTaskDialog"/> 自己写进状态行（那一处会念脱敏后的原因），不是猜。
            return null;
        }
    }

    /// <summary>
    /// 空态那颗按钮点下去做什么（第四百零五轮）：按 <see cref="RefreshEmptyState"/> 记下的那一笔分发——
    /// 首程那一态把请求交给壳（切到项目设置页并打开「新建配置」），其余走本页既有的
    /// <see cref="ShowNewTaskDialog"/> 同一个入口，不复制创建逻辑。
    /// <para>
    /// <b>本页不自己切页、也不引用另一页</b>：与「打开配置」那一跳（第二百六十三轮）同一纪律——
    /// 两页都由壳建、由壳切，这里只把「用户点了这一跳」这件事交出去，接线在 <c>NativeShellForm</c> 那一处。
    /// </para>
    /// </summary>
    private void RunEmptyCreate()
    {
        if (_emptyAction == FirstRunPlan.EmptyAction.NewProfile)
        {
            NewProfileRequested?.Invoke();
            return;
        }
        ShowNewTaskDialog();
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
    /// 点击列头排序（DESIGN.md §6「列表列排序」）：同一列再点反向，第三次点同一列回到数据源自身顺序
    /// （三态：升序 → 降序 → 未排序），换一列从升序开始；
    /// 排序状态留在本页并按列标题记下（<see cref="_sortMemory"/>；回到未排序时那一格一并抹掉，
    /// 否则用户撤掉的排序下次启动会自己回来），刷新、作业结束与切页回来后的
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
        // 这里不抹欠着的那一次结果：LoadTasks 重建列表时 Items.Clear 也会走到这一句（选中先被抹成 null），
        // 抹掉就把 OnTick 刚要报的那一句一起抹了。「换任务不念上一个任务的旧结果」由 ShowJobOutcome
        // 按欠账所属的任务判，不靠这一处。
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        var task = _selectedTask;
        if (task is null)
        {
            _detailScroll.Visible = false;
            _detailHint.Visible = true;
            // 没有选中任务时详情区的动作入口一律停用：否则它们留着上一次选中时的可用状态——
            // 读取失败后详情区收起、屏上写着「读取任务失败」，而「查看报告与导出」等入口仍是可点的样子
            // （状态与屏上说的不一致，一旦详情区因任何原因重新显示，就出现对着空详情可点的入口）。
            RefreshDetailActions(null);
            return;
        }
        _detailScroll.Visible = true;
        _detailHint.Visible = false;

        // 换任务时详情回到顶部（第三百七十轮）。补的是哪个缺口：详情区是固定控件、按任务刷新文字，
        // 换任务并不重建它——滚动位置因此会留着上一条任务的那一截。于是新任务的头（名称、配置、范围、
        // 下一步横幅、三阶段进度条）整段在屏外，用户看到的是新任务中段某一节，却不知道自己看的是哪一个任务；
        // 「选中即定位」在清单那一侧早已立过（DESIGN.md §6「列表选中定位」，第五十七轮），
        // 详情这一侧此前没有对上的规矩。冻结页面在同一件事上也是回到顶部（Web/app.js 里换一条注释缺口
        // 详情时 detailHost.scrollTop = 0），属同壳同行为，不是原生另立一套。
        // 刷新同一个任务不归位（_detailTaskId 不变）：动作做完要看得见结果，位置不能被抹掉。
        if (_detailTaskId != task.Id)
        {
            _detailScroll.AutoScrollPosition = Point.Empty;
            _detailTaskId = task.Id;
        }

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
        // 文字换了而栏宽没换时签名不会重算，这一步由这里主动问一次。
        WrapStepCells();
    }

    /// <summary>
    /// 点阶段跳到对应分区：只把详情滚动到那一区的标题，不改选中、不改任务数据、不触发任何动作。
    /// 认不出的键不动作（不猜一个分区）。
    /// <para>
    /// <b>键盘跟着落到那一区</b>（第三百七十二轮）：点完那颗阶段按钮之后焦点就在那颗按钮上，
    /// 而那颗按钮在进度条那一行——它此刻已经被滚出屏外。用户接着按 Tab 想走到那一区的动作按钮，
    /// WinForms 会把新焦点控件滚进视野，于是详情被拉回顶部，刚跳到的分区又不见了
    /// （wine 96 档实测：跳到源码注释区之后按一次 Tab，详情由 −779px 回到 0px，屏上又只剩进度条那一行）。
    /// 所以跳完把键盘交给那一区的第一颗可点按钮：Tab 从那里往下走，不再往回走。
    /// 那一区一颗可点的按钮都没有时（例如未扫描时的 Apifox 区）焦点留在原处——那时那一区本来也没有可做的事。
    /// </para>
    /// <para>
    /// <b>本方法只认键</b>（第三百七十三轮）：三个阶段各认自己那一区与那一行动作按钮，认完交给
    /// <see cref="MoveDetailTo"/>——「交焦点 + 换落点」的顺序与算式只在那一处，本方法不自己滚、也不自己交焦点。
    /// </para>
    /// </summary>
    private void JumpToStage(string key)
    {
        var (section, actions) = key switch
        {
            NextStep.StageSource => (_sourceSection, _scanActions),
            NextStep.StageGenerated => (_generatedSection, _generatedActions),
            NextStep.StageRemote => (_remoteSection, _remoteActions),
            _ => ((Label?)null, (FlowLayoutPanel?)null),
        };
        MoveDetailTo(section, actions);
    }

    /// <summary>
    /// 详情区「交焦点 + 换落点」的<b>唯一落点</b>（第三百七十三轮，具名 harden 切片）。
    /// <para>
    /// <b>为什么收成一处</b>：这两笔此前散在 <see cref="JumpToStage"/>（交焦点 + 定落点）与
    /// <see cref="FocusFirstAction"/>（挑哪一颗）里，而详情区这一族已经连做三轮
    /// （横幅按钮出厂一次、换任务回到顶部、跳阶段交键盘），每一轮各钉一处、各写一遍「先交焦点再滚」。
    /// 下一轮再往详情区加一个入口时，第四处会自己再写一遍，而顺序写反了在 Linux 上没人看得见——
    /// 所以顺序与落点算式都收到这里：判定在 <see cref="DetailFocusPlan"/>（纯逻辑、Linux 真执行），
    /// 触达控件只在本方法。
    /// </para>
    /// <para>
    /// <b>先交焦点、再定落点</b>：反过来的话交焦点那一下（WinForms 把焦点控件滚进视野）会把刚定好的落点顶掉。
    /// 认不出的分区（<paramref name="section"/> 或 <paramref name="actions"/> 为 null）不动作——
    /// 不猜一个分区，也不把焦点塞给别处。
    /// </para>
    /// </summary>
    private void MoveDetailTo(Control? section, FlowLayoutPanel? actions)
    {
        if (section is null || actions is null) return;
        // 先交焦点、再定落点：反过来的话交焦点那一下（WinForms 把焦点控件滚进视野）会把刚定好的落点顶掉。
        FocusFirstAction(actions);
        _detailScroll.AutoScrollPosition = new Point(0, DetailFocusPlan.Landing(section.Top, UiTokens.Lg));
    }

    /// <summary>
    /// 把键盘交给一行动作按钮里的第一颗可点按钮（第三百七十二轮；第三百七十三轮起「挑哪一颗」的判定
    /// 收到 <see cref="DetailFocusPlan.FirstSelectable"/>，本方法只负责触达）。
    /// <para>
    /// 一颗都不可点时不动键盘：焦点落在一个禁用控件上等于没落（<c>Focus()</c> 直接返回 false），
    /// 而把焦点硬塞给别处会让用户下一步按 Tab 时从别的地方起手——那比留在原处更难解释。
    /// </para>
    /// </summary>
    private static void FocusFirstAction(Control row)
    {
        var controls = row.Controls.Cast<Control>().ToList();
        var index = DetailFocusPlan.FirstSelectable(controls.Select(control => control.CanSelect).ToList());
        if (index < 0) return;
        controls[index].Focus();
    }

    /// <summary>
    /// 栏比某一格的整句文字窄时，那一格自己按栏宽折行（<c>docs/DESIGN.md</c> §8「任务页按钮行的横向可达性」）。
    /// <para>
    /// <b>为什么不是给一个上界就完事</b>：≈200% 档真 DPI 上下文里两版都量过（模拟器取帧，不是推）。
    /// autosize 开着只给 <c>MaximumSize</c>：那一格宽收到栏宽 353px 而高仍是单行的 55px，
    /// 「未扫描（当前）」那一段被静默截掉——比伸出行面还糟，屏幕上连「还有下文」都不剩。
    /// 关掉 autosize、高按折行后要多高给：同一格画成两行，阶段名与状态都在。
    /// </para>
    /// <para>
    /// <b>高怎么来的</b>：行数 × 字号行高 + 一笔上下留白。行数由 <c>TextRenderer</c> 按
    /// 「栏宽减掉这一格自己的左右留白」量出来；留白 = 单行那一版的高 − 记下它时的字号行高，
    /// 两个数都取当前档自己记下那一笔，不是一个跨档通用的常数：入库那一跑量到 96 档 37−18=19px、
    /// 120 档 39−18=21px、192 档 41−22=19px，真 DPI 那一臂的 192 档另有 55−35=20px
    /// （第三百六十四轮念出这三档，此前此处写的是「不随缩放走」）。单行那一版只在还开着 autosize
    /// 时量得到：折过行之后把 autosize 开回来，宽回到 367px 而高停在折行那一版的 90px 不缩回去（模拟器实测），
    /// 所以这一笔要趁出厂那一版记下，之后按字号行高是否变了再重量。
    /// </para>
    /// <para>
    /// <b>只动放不下整句的那一格</b>：放得下一行的格子仍是出厂那一版（96 / 120 两档三格都放得下，
    /// 这一处一行不改）；栏被拖宽回来到那一格的整句放得下时，那一格也交回出厂那一版。
    /// </para>
    /// </summary>
    private void WrapStepCells()
    {
        var column = Ui.ContentWidth(_detail);
        if (column <= 1) return; // 网格还没布局：这一趟量不到栏宽，别按 1px 折行

        for (var i = 0; i < _stepButtons.Length; i++)
        {
            var cell = _stepButtons[i];
            var applied = cell.Text + "|" + column;
            // 布局回调里改尺寸会再触发一次布局：文字与栏宽都没变时不动它，这一趟到此为止。
            if (_stepCellsApplied[i] == applied) continue;

            if (cell.AutoSize)
            {
                _stepCellLineSizes[i] = cell.Size;
                _stepCellLineFontHeights[i] = cell.Font.Height;
            }

            var line = _stepCellLineSizes[i];
            if (line.Width == 0) continue; // 没记到过单行那一版：宁可保持原样，也不猜一笔留白

            if (line.Width <= column)
            {
                _stepCellsApplied[i] = applied;
                if (cell.AutoSize) continue;
                // 栏又宽回来了：先按记下那一版把尺寸交回去，再让框架自己接管这一格。
                cell.AutoSize = false;
                cell.Size = line;
                cell.AutoSize = true;
                continue;
            }

            var oneLineText = TextRenderer.MeasureText(cell.Text, cell.Font);
            var wrappedText = TextRenderer.MeasureText(
                cell.Text, cell.Font, new Size(column - cell.Padding.Horizontal, 0), TextFormatFlags.WordBreak);
            var lines = Math.Max(1, (int)Math.Ceiling(wrappedText.Height / (double)oneLineText.Height));
            _stepCellsApplied[i] = applied;
            cell.AutoSize = false;
            cell.Size = new Size(column, lines * cell.Font.Height + line.Height - _stepCellLineFontHeights[i]);
        }
    }

    /// <summary>
    /// 下一步引导横幅：标题按语义色调着色，动作按钮映射到本页既有入口（只做引导，不改变状态）。
    /// <para>
    /// <b>只换字与显隐，不重建控件</b>（第三百六十八轮）：这两颗按钮在构造时出厂一次，本方法只改
    /// <c>Text</c> / <c>Visible</c> 与「这一次点下去做什么」。此前这里是 <c>Controls.Clear()</c> 再新建两颗，
    /// 而 <see cref="RefreshDetail"/> 在每一次动作之后都会走到这里（<see cref="StartScan"/> 自己就调一次）——
    /// 键盘用户 Tab 到横幅那颗按钮、按空格之后，按钮当场被从父面摘掉，焦点跳到别处；下一次刷新再摘一次。
    /// 同一页另四行动作按钮都是出厂一次、刷新只改 <c>Enabled</c> / <c>Visible</c>，这一行是唯一的例外。
    /// </para>
    /// <para>
    /// 没有那一半（<c>PrimaryLabel</c> / <c>SecondaryLabel</c> 为 null）时按钮<b>藏起来</b>而不是摘掉：
    /// 藏起来的控件不参与 Tab 序、也不占位，与摘掉在屏上一样，但焦点不会因此丢。
    /// </para>
    /// <para>
    /// <b>被藏起来的那一颗正拿着键盘时，把键盘交给还在屏上的那一颗</b>（第三百七十六轮）：上一段那句
    /// 「焦点不会因此丢」只对<b>没被聚焦</b>的那一颗成立。用户 Tab 到次动作那颗、按空格之后，那一次动作
    /// 会改任务状态，紧接着的这一次刷新把次动作藏起来——而此刻键盘正落在它身上，框架把焦点挪到哪一颗
    /// 不由这一页决定（实测落在父面那一行上），于是「按完一次就得重新 Tab 回来」。
    /// 判定在 <see cref="BannerFocusPlan.Landing"/>（纯逻辑、Linux 真执行），本方法只负责触达。
    /// </para>
    /// <para>
    /// <b>顺序是判据</b>：<c>Visible = false</c> 一落下去框架当场就把焦点挪走了，之后再读 <c>Focused</c>
    /// 读到的是「已经不在它身上」，那一判永远答「不用交」——所以焦点必须在换显隐<b>之前</b>记下来。
    /// </para>
    /// </summary>
    private void RefreshNextBanner(NextStep.Step next)
    {
        // 先记下焦点在哪一颗：换显隐之后就读不到了（见上面「顺序是判据」那一段）。
        var primaryHadFocus = _nextPrimary.Focused;
        var secondaryHadFocus = _nextSecondary.Focused;
        _nextTitle.Text = next.Title;
        _nextTitle.ForeColor = ToneColor(next.Tone);
        _nextDetail.Text = next.Detail;
        _nextPrimaryAction = next.PrimaryAction;
        _nextPrimary.Text = next.PrimaryLabel ?? "";
        _nextPrimary.Visible = next.PrimaryLabel is not null;
        _nextSecondaryAction = next.SecondaryAction;
        _nextSecondary.Text = next.SecondaryLabel ?? "";
        _nextSecondary.Visible = next.SecondaryLabel is not null;
        // 原本聚焦的那一颗被藏起来了：把键盘交给还在屏上的那一颗（两颗都不在屏上时不猜）。
        var landing = BannerFocusPlan.Landing(primaryHadFocus, secondaryHadFocus, _nextPrimary.Visible, _nextSecondary.Visible);
        if (landing == BannerFocusPlan.Primary) _nextPrimary.Focus();
        else if (landing == BannerFocusPlan.Secondary) _nextSecondary.Focus();
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

    /// <summary>扫描区按作业状态切换：按钮（空闲）或进度 + 停止（执行中）。控件位置固定，不跳位。</summary>
    /// <summary>
    /// 详情区动作入口的可用状态（单一落点）：<see cref="ShowJobState"/> 与「没有选中任务」分支共用同一份判定，
    /// 不出现第二处门控。没有选中任务时全部停用——屏上写着「没有选中任务 / 读取任务失败」，
    /// 入口却还是上一次选中时的可用状态，等于把不可作用的东西说成可作用。
    /// </summary>
    private void RefreshDetailActions(JobInfo? job)
    {
        // 缺口清单、候选审阅、规则提取、注释回写、传播核对、响应契约与远端检查都基于扫描落盘产物：扫描完成后可用，
        // 作业执行中与未扫描时禁用（禁用态说明见下方固定说明行）；后台作业的进度与停止共用同一套固定占位控件。
        var scanReady = _selectedTask is { Stages.Source.ScannedAt: not null } && job is null;
        // 引导横幅的动作与各区按钮同一纪律：作业执行中一律禁用（进度在上方状态行）。
        foreach (var bannerButton in _nextActions.Controls.OfType<Button>())
        {
            bannerButton.Enabled = job is null;
        }
        // 「打开配置」与作业无关：它只换页、不写数据，扫描前扫描中都能用；没有绑定配置（配置 Id 为空）时没有落点。
        _openProfileButton.Enabled = _selectedTask is not null && ProfileJump.CanJump(_selectedTask.ProfileSnapshot.Id);
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
    }

    /// <summary>
    /// 这一页起一次作业的唯一落点（第三百八十六轮）：起完当场记下「这一次的结果欠着没报」。
    /// <para>
    /// <b>补的是哪个缺口</b>：那一记此前只落在 <see cref="ShowJobState"/>「界面看到它在跑」那一次。
    /// 一次几十毫秒内就失败的作业，<c>StartScan</c> 里紧接着那一次 <c>RefreshDetail</c> 已经看不到它在跑了
    /// （<c>JobRunner.Running</c> 当场答 null），于是 Id 没记下、触发也没立起来，<see cref="OnTick"/>
    /// 后半支永远走不到——用户按了「开始扫描」，屏上对这一次失败一句话都没有。
    /// 第三百八十五轮的 wine 冒烟在 120 与 192 两档量到的正是这一处（逐字红话留在
    /// <c>ApifoxHelper.Smoke/Program.cs</c> 的 <c>RunJobOutcomeStatus</c> 注释里）。
    /// <c>Start</c> 当场返回那一个 <see cref="JobInfo"/>，记下它不依赖任何轮询，那一支就此补上。
    /// </para>
    /// </summary>
    private void StartTaskJob(string taskId, string kind, Func<JobContext, Task<object?>> work)
    {
        RememberOutcome(_jobs.Start(taskId, kind, work));
    }

    /// <summary>记下「这一次作业的结果还欠着」（唯一落点，与 <see cref="ShowJobOutcome"/> 的用掉即清成对）。</summary>
    private void RememberOutcome(JobInfo job)
    {
        _lastJobId = job.Id;
        _lastJobTaskId = job.Scope;
    }

    private void ShowJobState(JobInfo? job)
    {
        // 先记下焦点在哪一颗：换显隐与换可用性之后就读不到了（与引导横幅同一纪律，算式在 JobFocusPlan）。
        // 横幅那一行也记（第三百九十三轮）：作业在跑时它那两颗一律停用（RefreshDetailActions），
        // 而停用的那一颗接不住键盘——记下是哪一颗，换完可用性之后才判得出它还在不在。
        var scanHadFocus = _scanButton.Focused;
        var stopHadFocus = _stopButton.Focused;
        var bannerHadFocus = _nextPrimary.Focused ? _nextPrimary : _nextSecondary.Focused ? _nextSecondary : null;
        RefreshDetailActions(job);
        if (job is not null)
        {
            // 记下 Id：作业一结束 Running 就答 null，终态与失败原因只能按 Id 读回（见 ShowJobOutcome）。
            // 从别的屏起起来的作业（同步预览、写入探测）也经这一处落到这一页的欠账上。
            RememberOutcome(job);
            _scanButton.Visible = false;
            _progress.Visible = true;
            _stopButton.Visible = true;
            _progress.Text = job.State == "running"
                ? JobOutcome.StoppingText(job.Kind, _stopRequestedJobId == job.Id)
                    ?? $"正在{job.Kind}… {job.Progress:P0} {job.ProgressText}"
                : job is { State: "failed", Error: not null }
                    ? $"{job.Kind}失败：{job.Error} 正在刷新。"
                    : $"{job.Kind}结束（{TaskDisplay.JobStateText(job.State)}），正在刷新。";
            _progress.ForeColor = job.State == "failed" ? UiTokens.BadText : UiTokens.Text;
            _stopButton.Enabled = job.State == "running";
        }
        else
        {
            // 没有作业在跑了：停止请求已经用完（那一行接下来要说的是这一次的结果，见 ShowJobOutcome）。
            _stopRequestedJobId = null;
            _scanButton.Visible = true;
            _progress.Visible = false;
            _stopButton.Visible = false;
        }

        // 原本聚焦的那一颗被藏起来了：把键盘交给还在屏上的那一颗（两颗都不在屏上时不猜）。
        // 这一行只有这一处换显隐：作业起来时「开始扫描」藏、「停止」显，作业结束时反过来。
        var landing = JobFocusPlan.Landing(scanHadFocus, stopHadFocus, _scanButton.Visible, _stopButton.Visible);
        // 横幅那一行原本拿着键盘的那一颗被停用了（作业在跑时横幅那两颗一律停用）：这一行没有可交的落点，
        // 交给作业区那颗「停止」——它是这一屏此刻唯一还能按的下一步（第三百九十三轮）。
        if (landing == JobFocusPlan.None
            && JobFocusPlan.BannerHandsToStop(
                bannerHadFocus is not null,
                bannerHadFocus is null || (bannerHadFocus.Visible && bannerHadFocus.Enabled),
                _stopButton.Visible && _stopButton.Enabled))
        {
            landing = JobFocusPlan.Stop;
        }
        if (landing == JobFocusPlan.Scan) _scanButton.Focus();
        else if (landing == JobFocusPlan.Stop) _stopButton.Focus();
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

        // 触发只看「还欠着一次结果没报」，不看「界面有没有看到它在跑」——后者正是第三百八十五轮量到的那处缺口：
        // 一个到终态比界面第一次看到它还早的作业，那个 flag 从来立不起来，那一句就永远说不出口。
        if (_lastJobId is null) return;

        // 作业刚结束：重读任务与列表，展示落盘的阶段结果。
        LoadTasks();
        // 这一次的结果说在状态行上（第三百七十八轮）：进度行随即收起，失败原因不能跟着一起消失。
        ShowJobOutcome();
    }

    /// <summary>
    /// 作业结束之后，把这一次的结果说在状态行上（第三百七十八轮，具名 pass：clarify）。
    /// <para>
    /// <b>补的是哪个缺口</b>：进度标签是「正在做什么」的通道，作业一结束就被 <see cref="ShowJobState"/>
    /// 藏起来——失败原因因此在屏上只活了不到一个轮询周期（500ms），随后连同一整行一起消失；
    /// 成功与取消更是从来没有一句话。用户按了「开始扫描」，回来只看到进度行没了。
    /// </para>
    /// <para>
    /// <b>为什么读回终态</b>：<c>JobRunner.Running</c> 在作业结束后答 null，终态与失败原因只能按 Id 取
    /// （<c>JobRunner.Get</c>，作业记录在 <c>Prune</c> 之前一直留着）。Id 由 <see cref="StartTaskJob"/>
    /// （这一页自己起作业时）与 <see cref="ShowJobState"/>（界面看到它在跑时）两处记下，本方法用掉即清——
    /// 一次作业只报一次，不重复念同一句。
    /// </para>
    /// <para>
    /// <b>换过任务不念旧结果</b>：欠账带着它属于哪一个任务（<see cref="_lastJobTaskId"/>），报之前先核它
    /// 就是当前选中的那一个——状态行是「当前这个任务刚才那件事怎么样了」的通道，对着另一个任务念上一个
    /// 任务的旧结果会把人说错。这一判此前由 <c>OnSelectedChanged</c> 抹「看到过它在跑」那个 flag 承担，
    /// 挪到这里是因为第三百八十六轮起欠账在起作业时就在手上、换任务那一处不能再抹它
    /// （<c>LoadTasks</c> 重建列表时也会走那一处，抹掉就把刚要报的那一句一起抹了）。
    /// </para>
    /// <para>
    /// <b>不覆盖读取失败</b>：<see cref="LoadTasks"/> 刚判过「磁盘上的任务读不出来」时，状态行已经在说那件事的
    /// 原因——那一句比这一次作业的结果更该留在屏上（屏上此刻连任务清单都没有）。同理，作业还在跑
    /// （<c>FinishedText</c> 答 null）时什么都不说。
    /// </para>
    /// </summary>
    private void ShowJobOutcome()
    {
        if (_lastJobId is not { } id) return;
        _lastJobId = null;
        var owedByTask = _lastJobTaskId;
        _lastJobTaskId = null;
        // 这一笔欠账属于上一个选中的任务：不对着现在这个念，也不留着以后念（留着的数已经旧了）。
        if (owedByTask != _selectedTask?.Id) return;
        if (_jobs.Get(id) is not { } job) return;
        if (JobOutcome.FinishedText(job.Kind, job.State, job.Error) is not { } text) return;
        if (_loadFailed) return;
        SetStatus(text, JobOutcome.Tone(job.State) == "bad" ? UiTokens.BadText : UiTokens.Muted);
    }

    private void StartScan()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        try
        {
            StartTaskJob(taskId, "源码扫描", async job => await _scan.ScanAsync(taskId, job));
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

    private void RequestStop()
    {
        if (_selectedTask is null) return;
        var scope = _selectedTask.Id;
        // 只有真的按下一个在跑的作业才记下这一笔：没有作业在跑时 Cancel 答 false，
        // 那句「正在停止…」就成了没影的话（这一行说的必须是屏上那一次作业）。
        if (!_jobs.Cancel(scope) || _jobs.Running(scope) is not { } job) return;
        _stopRequestedJobId = job.Id;
        // 当场重画一次进度行：否则那句话要等下一个轮询周期（500ms）才出来，
        // 而这一轮补的正是「那一击之后屏上得有回应」。重画走的仍是那唯一一处 ShowJobState。
        ShowJobState(job);
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
            StartTaskJob(taskId, "规则提取候选", async job => await _drafts.GenerateRuleAsync(taskId, [], job.Token));
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
            StartTaskJob(taskId, "模型生成草稿", async job => await _drafts.GenerateWithModelAsync(taskId, [], true, job));
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
            StartTaskJob(taskId, "传播核对", async job => await _propagation.CheckAsync(taskId, job));
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
            StartTaskJob(taskId, "定向构建", async job => await _builds.RunAsync(taskId, job));
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
            StartTaskJob(taskId, "契约检查", async job =>
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
            StartTaskJob(taskId, "导入离线远端", async job =>
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
                // 那一句的单一落点在 FirstRunPlan（第四百零五轮）：界面与冒烟段都读它，不各写一份字面。
                SetStatus(FirstRunPlan.NoProfileStatus, UiTokens.WarnText);
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus("读取配置失败：" + Redactor.Redact(ex.Message));
            return;
        }
        // 默认选中哪一份由 NewTaskProfileChoice 一处判（第四百零八轮）：刚保存的那一份优先，
        // 认不出（没保存过 / 已被删掉）时回退到清单第一项——本页此前的行为。
        using var dialog = new NewTaskDialog(_profiles, PreferredProfileId);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var task = _tasks.Create(dialog.Request);
            if (dialog.StartScan) StartTaskJob(task.Id, "源码扫描", async job => await _scan.ScanAsync(task.Id, job));
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
internal sealed class NewTaskDialog : DpiAwareForm
{
    private readonly ProfileStore _profiles;
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _name = Ui.Input();
    private readonly ListView _modules = Ui.List(checkBoxes: true);
    private readonly TextBox _prefixes = Ui.Input();
    private readonly TextBox _keys = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly TextBox _note = Ui.Input();
    private readonly CheckBox _offline = new() { Text = "离线模式（本次任务不访问 Apifox，仅源码阶段）", AutoSize = true };
    // 「模块」那一行下面的说明句（第四百一十二轮立、第四百一十三轮补第三句）：文字随「选没选中配置」
    // 与「这份配置有几个模块」换，判定与三句字面都在 NewTaskModuleHint 一处——本对话框只把这两件事读给它，
    // 不就地拼这三句。
    // 出厂那一句仍由 Ui.HintRow 那一行（裸语句）写下，这里只是把那一格取回来（见 ModuleHintLabel）。
    private readonly Label? _moduleHint;
    // 两颗创建按钮（第四百一十五轮）：一份配置都没选中时它们该停用——点下去只会得到一句
    // 「请选择项目配置」，而屏上根本没有可选的配置。判定收在 NewTaskPrimaryAction 一处，
    // 这里只把「下拉里选没选中一份配置」读给它（见 LoadModules）。
    private readonly Button _scanButton;
    private readonly Button _createButton;
    private readonly Label _status = Ui.StatusLine();
    // 状态行宿主：与外层页面同一接线口径——放不下时右侧给「查看完整错误」入口。
    private readonly StatusLineHost _statusHost;

    /// <summary>对话框确认后的创建请求；StartScan 表示创建后立即开始扫描。</summary>
    public CreateTaskRequest Request { get; private set; } = new();

    public bool StartScan { get; private set; }

    private sealed record ProfileItem(string Text, ProjectProfile Profile)
    {
        public override string ToString() => Text;
    }

    /// <param name="preferredProfileId">
    /// 「项目配置」下拉默认选中的那一份（第四百零八轮）：由 <see cref="NewTaskProfileChoice.Index"/> 判，
    /// 认不出时回退到清单第一项。传 <c>null</c> 就是本对话框此前的行为。
    /// </param>
    public NewTaskDialog(ProfileStore profiles, string? preferredProfileId = null)
    {
        _profiles = profiles;
        Text = "新建任务";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(560, 560));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var scanButton = Ui.Primary("创建并扫描");
        scanButton.Click += (_, _) => Confirm(startScan: true);
        var createButton = Ui.Ghost("仅创建");
        createButton.Click += (_, _) => Confirm(startScan: false);
        // 两颗创建按钮留成字段（第四百一十五轮）：LoadModules 要按「选没选中配置」启停它们。
        _scanButton = scanButton;
        _createButton = createButton;
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = scanButton;
        CancelButton = cancelButton;

        // 指定接口为多行输入：内层文本框定高，Ui.Field 的尺寸双向同步会带着描边面板一起变高。
        _keys.Height = 62;
        // 列宽留在 AddRow 的字段下限（380）之内：多出来的宽度是整行勾选的可点面，不逼出横向滚动条。
        _modules.Columns.Add("模块", 360);

        // 网格十二行（模块清单、指定接口、版本备注与提示）高于可视高度：放入可滚动宿主，离线模式行可达（DESIGN.md §6 长内容区）。
        var grid = Ui.FieldGrid(ProfileGridColumns.NarrowLabelWidth);
        AddRow(grid, "项目配置", _profile);
        AddRow(grid, "任务名称", Ui.Field(_name));
        Ui.HintRow(grid, "留空则用「配置名 月-日 时:分」。", HintRowForm.Field);
        var moduleHost = Ui.Hairline(_modules);
        moduleHost.Height = 96;
        AddRow(grid, "模块", moduleHost);
        // 这一句随选中那一份配置的模块数换（第四百一十二轮）：出厂先给「有模块」那一句（就是此前那一句，
        // 逐字未改）；真正的取值在 LoadModules 里按 NewTaskModuleHint.For 一处判。
        Ui.HintRow(grid, "勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。", HintRowForm.Field);
        AddRow(grid, "路径前缀", Ui.Field(_prefixes));
        Ui.HintRow(grid, "补充接口路径前缀，用「,」「;」分隔；可与模块并用。", HintRowForm.Field);
        AddRow(grid, "指定接口", Ui.Field(_keys));
        Ui.HintRow(grid, TaskCreation.KeysHint, HintRowForm.Field);
        AddRow(grid, "版本备注", Ui.Field(_note));
        _note.PlaceholderText = TaskCreation.NotePlaceholder;
        Ui.HintRow(grid, TaskCreation.NoteFallbackHint, HintRowForm.Field);
        AddRow(grid, "", _offline);
        // 把「模块」那一行的说明句取回来（第四百一十二轮）：它由上面那一句 Ui.HintRow 出厂，
        // 换配置时要重写它的文字。取法按工厂留的标记 + 出厂那一句的字面（同一屏另有四行说明，
        // 标记相同、文字不同），取不到就答 null——那时不重写，屏上留着出厂那一句（不变差）。
        _moduleHint = ModuleHintLabel(grid);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（创建并扫描）在最右。
        buttons.Controls.Add(scanButton);
        buttons.Controls.Add(createButton);
        buttons.Controls.Add(cancelButton);
        // Tab 次序左 → 右（第三百一十五轮）：RightToLeft 流向里先加的在最右，隐式 Tab 次序因此是右 → 左。
        Ui.TabOrderLeftToRight(buttons);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusHost);

        var gridScroll = Ui.ScrollHost();
        gridScroll.BackColor = UiTokens.Panel;
        gridScroll.Controls.Add(grid);
        Controls.Add(gridScroll);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        // 下拉一换就重填模块清单（说明句跟着换，见 LoadModules）。订阅放在这里而不是构造开头：
        // 说明行那一格在它之前出厂，订阅早于赋值的话，第一次选中事件会撞上一个还没赋值的 _moduleHint。
        _profile.SelectedIndexChanged += (_, _) => LoadModules();

        var available = _profiles.List();
        foreach (var profile in available)
        {
            _profile.Items.Add(new ProfileItem(profile.Name, profile));
        }
        // 默认选中哪一份不在这里判（第四百零八轮）：清单是按名称排的，选中第一项等于「按名字排第一的那一份」，
        // 而用户刚保存的那一份未必排第一——判定收在 NewTaskProfileChoice 一处，这里只落它的答案。
        var index = NewTaskProfileChoice.Index(available.Select(p => p.Id).ToList(), preferredProfileId);
        if (index != NewTaskProfileChoice.None) _profile.SelectedIndex = index;
        // 一份配置都没有时，上面那一行不会触发 SelectedIndexChanged，说明句就停在出厂那一句上——
        // 而屏上是一个空框加一句「勾选要检查的模块」（第四百一十三轮）。这里补一次推导：
        // 清单本来就是空的，重填是空操作，真正要的是让说明句换成「先选一份配置」那一句。
        else LoadModules();
    }

    /// <summary>
    /// 取回「模块」那一行的说明句标签（第四百一十二轮）：按工厂留的标记 <see cref="Ui.HintRowMarker"/>
    /// 与出厂那一句的字面认——同一屏另有四行说明，标记相同、文字不同。取不到答 <c>null</c>，
    /// 调用方据此不重写（屏上留着出厂那一句，不变差），不抛、也不猜另一格。
    /// </summary>
    private static Label? ModuleHintLabel(TableLayoutPanel grid) =>
        grid.Controls.OfType<Label>().FirstOrDefault(label =>
            Equals(label.Tag, Ui.HintRowMarker) && label.Text == NewTaskModuleHint.WithModules);

    /// 按选中那一份配置重填模块清单，并把「模块」那一行的说明句换成与它相符的那一句（第四百一十二轮）。
    /// <para>
    /// <b>两件事在同一处发生</b>：说明句说的是「这个清单里有什么」，清单换了它就得跟着换——
    /// 只在出厂时定一次的话，换到另一份配置之后它说的还是上一份的情况（配置里没有模块时，
    /// 它仍在教人「勾选要检查的模块」，而框里一个可勾的都没有）。判定与两句字面都在
    /// <see cref="NewTaskModuleHint"/> 一处，本方法只把「此刻清单里有几项」读给它。
    /// </para>
    /// <para>
    /// <b>「一份配置都没选中」与「这份配置没有模块」是两件事</b>（第四百一十三轮）：前者屏上根本没有
    /// 「这份配置」，说成「这份配置没有模块」是把「没有对象」说成「对象是空的」（取舍见
    /// <see cref="NewTaskModuleHint.For"/>）。所以判定收两个入参——选没选中、这份配置里有几个模块。
    /// </para>
    /// </summary>
    private void LoadModules()
    {
        _modules.BeginUpdate();
        _modules.Items.Clear();
        var selected = _profile.SelectedItem as ProfileItem;
        if (selected is not null)
        {
            foreach (var module in selected.Profile.Modules)
            {
                _modules.Items.Add(new ListViewItem(TaskCreation.ModuleRowText(module)) { Tag = module });
            }
        }
        _modules.EndUpdate();
        if (_moduleHint is not null)
        {
            // 两个入参各答一件事（第四百一十三轮）：选没选中配置、这份配置里有几个模块。
            // 此前收成一个可空整数，于是「一份配置都没有」与「这份配置没有模块」只能靠传 null / 0 分开，
            // 而那一格被读成了「有模块」——屏上是一个空框加一句「勾选要检查的模块」。
            _moduleHint.Text = NewTaskModuleHint.For(selected is not null, _modules.Items.Count);
        }
        // 两颗创建按钮跟着同一件事启停（第四百一十五轮）：一份配置都没选中时它们该停用——
        // 点下去只会得到一句「请选择项目配置」，而屏上根本没有可选的配置。
        // 判定收在 NewTaskPrimaryAction 一处，这里只把「选没选中」读给它。
        var canCreate = NewTaskPrimaryAction.CanCreate(selected is not null);
        _scanButton.Enabled = canCreate;
        _createButton.Enabled = canCreate;
    }

    private void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        // 没有标签的行不放占位控件：放了就把这一行撑到那个控件的高度（离线模式那一行此前
        // 被默认尺寸的 Panel（200×100）撑到 100px 高）。提示行另有工厂 Ui.HintRow。
        if (label.Length > 0)
        {
            var caption = Ui.Body(label);
            caption.Margin = new Padding(0, UiTokens.Md, UiTokens.Sm, UiTokens.Xs);
            grid.Controls.Add(caption, 0, row);
        }
        field.Margin = new Padding(0, UiTokens.Sm, 0, UiTokens.Xs);
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

    private void SetStatus(string message) => _statusHost.SetText(message, UiTokens.BadText);
}
