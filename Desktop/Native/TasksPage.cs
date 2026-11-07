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
/// + 注释候选（规则提取后台作业、逐条审阅接受/搁置/驳回）+ 注释回写 + 生成文档核对（后台作业、只读结果视图）
/// + 响应契约（扫描时追踪落盘，逐条确认/拒绝/重置）
/// + 定向构建（用户确认后经既有 BuildService 在工作区执行 dotnet build --no-restore 刷新 XML 文档，
/// 结果落盘 build.json；「查看构建输出」只读展示命令、退出码、耗时与输出尾部）
/// + Apifox 差异检查（只读后台作业，需要时先加载冻结 Swagger；离线任务先导入远端 JSON；只读结果视图）
/// + 差异确认（高风险变化逐项确认、说明冲突选择、模型绑定与共享模型更新确认，经共享 DecisionService 保存并递增任务版本）
/// + 报告与交接（Markdown 预览 + 原生另存为导出，渲染与字节口径走共享 ReportExportService）
/// + 同步预览与执行（勾选接口生成冻结计划并查看写入清单、排除原因与保留设置，经既有 SyncPlanService；
/// 确认后经既有 SyncExecutor 执行同步——计划哈希绑定、逐动作写入并回读核对、失败即停、可在动作之间停止；
/// 「重新核对（只读）」复用 VerifyAsync）。
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
    private readonly ReportExportService _exports;
    private readonly SyncPlanService _syncPlans;
    private readonly SyncExecutor _syncExecutor;

    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, BackColor = UiTokens.Panel };
    private readonly Panel _empty;
    private readonly Label _status = Ui.StatusLine();
    private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, BackColor = UiTokens.Line };

    // 详情区（固定控件，按任务刷新文本，不整面板重建）。
    private readonly Label _detailHint;
    private readonly TableLayoutPanel _detail;
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
    private readonly Button _decisionsButton;
    private readonly Button _remoteViewButton;
    private readonly Button _syncButton;
    private readonly Button _reportButton;
    private readonly Label _reportStatus = Ui.StatusText();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private CheckTask? _selectedTask;
    private bool _runningShown;
    private bool _splitAdjusted;

    public TasksPage(TaskStore tasks, SourceScanService scan, JobRunner jobs, ProfileStore profiles, DraftService drafts, CommentPlanService plans, PropagationService propagation, BuildService builds, ContractReviewService reviews, CheckService checks, DecisionService decisionSaves, ReportExportService exports, SyncPlanService syncPlans, SyncExecutor syncExecutor)
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
        _exports = exports;
        _syncPlans = syncPlans;
        _syncExecutor = syncExecutor;

        var newButton = Ui.Primary("新建任务");
        newButton.Click += (_, _) => ShowNewTaskDialog();
        var refreshButton = Ui.Ghost("刷新");
        refreshButton.Click += (_, _) => LoadTasks();
        _list.SelectedIndexChanged += (_, _) => OnSelectedChanged();
        _list.Columns.Add("任务", 200);
        _list.Columns.Add("配置", 140);
        _list.Columns.Add("接口", 56);
        _list.Columns.Add("注释候选", 76);
        _list.Columns.Add("Apifox", 110);
        _list.Columns.Add("更新时间", 140);

        _gapButton = Ui.Ghost("查看注释缺口");
        _gapButton.Click += (_, _) => ShowScanResults();
        _candidatesButton = Ui.Ghost("查看注释候选");
        _candidatesButton.Click += (_, _) => ShowCandidates();
        _ruleButton = Ui.Ghost("规则提取候选");
        _ruleButton.Click += (_, _) => StartRuleExtraction();
        _applyButton = Ui.Ghost("注释回写");
        _applyButton.Click += (_, _) => ShowApply();
        _scanButton = Ui.Ghost("开始扫描");
        _scanButton.Click += (_, _) => StartScan();
        _cancelButton = Ui.DangerGhost("取消");
        _cancelButton.Click += (_, _) => CancelScan();
        _scanActions.Controls.Add(_gapButton);
        _scanActions.Controls.Add(_candidatesButton);
        _scanActions.Controls.Add(_ruleButton);
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
        _decisionsButton = Ui.Ghost("处理差异确认");
        _decisionsButton.Click += (_, _) => ShowDecisions();
        _remoteViewButton = Ui.Ghost("查看检查结果");
        _remoteViewButton.Click += (_, _) => ShowCheckResults();
        _syncButton = Ui.Ghost("同步预览");
        _syncButton.Click += (_, _) => ShowSyncPreview();
        _remoteActions.Controls.Add(_importRemoteButton);
        _remoteActions.Controls.Add(_remoteCheckButton);
        _remoteActions.Controls.Add(_decisionsButton);
        _remoteActions.Controls.Add(_remoteViewButton);
        _remoteActions.Controls.Add(_syncButton);

        _reportButton = Ui.Ghost("查看报告与导出");
        _reportButton.Click += (_, _) => ShowReports();

        _empty = BuildEmpty();

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Bg, ColumnCount = 2, Margin = new Padding(0) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 0, UiTokens.Md, 0) };
        buttons.Controls.Add(newButton);
        buttons.Controls.Add(refreshButton);
        _status.Dock = DockStyle.Fill;
        toolbar.Controls.Add(buttons, 0, 0);
        toolbar.Controls.Add(_status, 1, 0);

        _detail = BuildDetail();
        _detailHint = Ui.Caption("选择左侧任务查看阶段进度。");
        _detailHint.Dock = DockStyle.Fill;
        _detailHint.TextAlign = ContentAlignment.MiddleCenter;

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_empty);
        _split.Panel1.Controls.Add(listHost);
        _split.Panel2.Controls.Add(_detailHint);
        _split.Panel2.Controls.Add(_detail);
        _split.Panel2.BackColor = UiTokens.Bg;
        // Panel2MinSize 与 SplitterDistance 依赖真实宽度，等首次显示页面再设置（构造期控件还没有尺寸）。
        _split.SplitterWidth = 1;

        Controls.Add(_split);
        Controls.Add(toolbar);

        _timer.Tick += (_, _) => OnTick();
    }

    private Panel BuildEmpty()
    {
        var title = Ui.Section("还没有检查任务");
        var hint = Ui.Body("先在「项目设置」新建配置，然后点「新建任务」开始源码扫描。", muted: true);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(hint, 0, 2);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        panel.Controls.Add(layout);
        panel.Visible = false;
        return panel;
    }

    private TableLayoutPanel BuildDetail()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddDetailRow(grid, _detailName);
        AddDetailRow(grid, _detailMeta);
        AddDetailRow(grid, _detailRange);
        AddDetailRow(grid, SectionCaption("源码注释"));
        AddDetailRow(grid, _sourceStatus);
        AddDetailRow(grid, _sourceNumbers);
        AddDetailRow(grid, _scanActions);
        AddDetailRow(grid, Ui.Caption("「查看注释缺口」「规则提取候选」「查看注释候选」与「注释回写」在扫描完成后可用；「注释回写」需要有已接受且未回写的候选。模型生成、材料导入与手工填写将在后续切片接入。"));
        AddDetailRow(grid, SectionCaption("生成文档"));
        AddDetailRow(grid, _generatedStatus);
        AddDetailRow(grid, _generatedNumbers);
        AddDetailRow(grid, _generatedActions);
        AddDetailRow(grid, _buildStatus);
        AddDetailRow(grid, Ui.Caption("「核对传播」需要有已接受的注释候选；核对不构建项目，XML 与 Swagger 重新生成后才会更新。工具默认不构建、不 restore、不启动服务：需要刷新 XML 文档时，「执行定向构建」确认后在任务的工作区执行 dotnet build --no-restore，结果与输出尾部在「查看构建输出」查看。"));
        AddDetailRow(grid, SectionCaption("响应契约"));
        AddDetailRow(grid, _contractStatus);
        AddDetailRow(grid, _contractButton);
        AddDetailRow(grid, Ui.Caption("契约在扫描时从返回表达式与调用链静态追踪生成；「查看响应契约」在扫描完成后可用。确认后的契约在后续 Apifox 检查中替换该接口的 200 响应体；确认或拒绝后需重新检查才会生效。"));
        AddDetailRow(grid, SectionCaption("Apifox 差异与同步"));
        AddDetailRow(grid, _remoteStatus);
        AddDetailRow(grid, _remoteNumbers);
        AddDetailRow(grid, _remoteActions);
        AddDetailRow(grid, Ui.Caption("检查为只读：读取分支概览与范围内接口、模型详情，与目标比较，不写入远端；在源码扫描完成后可用，离线任务先导入作为「远端」的 OpenAPI JSON。「处理差异确认」在检查完成后可用：高风险变化逐项确认、说明冲突选择与模型绑定，保存后需重新检查才会生效。「同步预览」在检查完成后可用：勾选接口生成冻结计划，查看逐动作写入内容、排除原因与保留设置；「确认同步」在计划待确认且未过期时可用，逐项写入并立即回读核对，失败即停，执行结束后可「重新核对（只读）」；离线任务不能同步。"));
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
        if (!_splitAdjusted && _split.Width > 800)
        {
            try
            {
                _split.Panel2MinSize = 360;
                _split.SplitterDistance = _split.Width - 420;
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
        var selectedId = _selectedTask?.Id;
        try
        {
            var tasks = _tasks.List();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var task in tasks)
            {
                var item = new ListViewItem(task.Name) { Tag = task };
                item.SubItems.Add(task.ProfileSnapshot.Name);
                item.SubItems.Add(task.Stages.Source.Endpoints > 0 ? task.Stages.Source.Endpoints.ToString() : "—");
                item.SubItems.Add(task.Stages.Source.Candidates > 0 ? task.Stages.Source.Candidates.ToString() : "—");
                var remote = item.SubItems.Add(TaskDisplay.RemoteStatusText(task.Stages.Remote.Status));
                item.UseItemStyleForSubItems = false;
                remote.ForeColor = ToneColor(TaskDisplay.RemoteTone(task.Stages.Remote.Status));
                item.SubItems.Add(task.UpdatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            _empty.Visible = tasks.Count == 0;
            _list.Visible = tasks.Count > 0;
            SetStatus(null);
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

    private void SelectTask(string taskId)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is CheckTask task && task.Id == taskId)
            {
                item.Selected = true;
                return;
            }
        }
        _selectedTask = null;
        RefreshDetail();
    }

    private void OnSelectedChanged()
    {
        _selectedTask = _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as CheckTask : null;
        _runningShown = false;
        RefreshDetail();
    }

    private void RefreshDetail()
    {
        var task = _selectedTask;
        if (task is null)
        {
            _detail.Visible = false;
            _detailHint.Visible = true;
            return;
        }
        _detail.Visible = true;
        _detailHint.Visible = false;

        _detailName.Text = task.Name;
        _detailMeta.Text = $"配置 {task.ProfileSnapshot.Name} · Apifox {(task.ProfileSnapshot.Apifox.ProjectId ?? "未绑定")} · {(task.OfflineMode ? "离线模式" : "在线")}";
        _detailRange.Text = task.PathPrefixes.Count > 0
            ? "范围：" + string.Join("、", task.ModuleNames) + " " + string.Join(" ", task.PathPrefixes)
            : task.ModuleNames.Count > 0 ? "范围：模块 " + string.Join("、", task.ModuleNames) : "范围：API 项目全部接口";

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
        _remoteCheckButton.Text = NeedsSwaggerLoad(task) ? "加载 Swagger 并检查"
            : File.Exists(_tasks.FilePath(task.Id, TaskFiles.Diff)) ? "重新检查" : "开始检查";

        RefreshContractSection();
        RefreshBuildSection();
        RefreshReportSection();

        var job = _jobs.Running(task.Id);
        ShowJobState(job);
    }

    /// <summary>扫描区按作业状态切换：按钮（空闲）或进度 + 取消（执行中）。控件位置固定，不跳位。</summary>
    private void ShowJobState(JobInfo? job)
    {
        // 缺口清单、候选审阅、规则提取、注释回写、传播核对、响应契约与远端检查都基于扫描落盘产物：扫描完成后可用，
        // 作业执行中与未扫描时禁用（禁用态说明见下方固定说明行）；后台作业的进度与取消共用同一套固定占位控件。
        var scanReady = _selectedTask is { Stages.Source.ScannedAt: not null } && job is null;
        _gapButton.Enabled = scanReady;
        _candidatesButton.Enabled = scanReady;
        _ruleButton.Enabled = scanReady;
        _applyButton.Enabled = scanReady && (_selectedTask?.Stages.Source.Accepted ?? 0) > 0;
        _checkButton.Enabled = scanReady && (_selectedTask?.Stages.Source.Accepted ?? 0) > 0;
        _viewButton.Enabled = scanReady && _selectedTask is { Stages.Generated.CheckedAt: not null };
        // 定向构建只需要配置快照里的 API 项目路径（与冻结页面一致，不要求先扫描）；构建记录查看需要已有 build.json。
        _buildButton.Enabled = BuildStatus.CanStart(_selectedTask is not null) && job is null;
        _buildLogButton.Enabled = _selectedTask is not null && job is null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.BuildLog));
        _contractButton.Enabled = scanReady;
        _importRemoteButton.Enabled = scanReady && (_selectedTask?.OfflineMode ?? false);
        _remoteCheckButton.Enabled = scanReady;
        _decisionsButton.Enabled = scanReady && _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        _remoteViewButton.Enabled = scanReady && _selectedTask is not null && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
        // 同步预览需要检查结果；离线任务不能同步（对话框内也如实指引，双重保险）。
        _syncButton.Enabled = scanReady && _selectedTask is { OfflineMode: false } && File.Exists(_tasks.FilePath(_selectedTask.Id, TaskFiles.Diff));
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

    /// <summary>打开注释缺口清单（只读）：数据来自扫描落盘的 symbols.json，无写入动作。</summary>
    private void ShowScanResults()
    {
        if (_selectedTask is null) return;
        using var dialog = new ScanResultsDialog(_tasks, _selectedTask);
        dialog.ShowDialog(FindForm());
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

    /// <summary>打开响应契约审阅（确认/拒绝/重置写任务数据并递增任务版本，不写源码、不访问 Apifox）：关闭后刷新状态行。</summary>
    private void ShowContracts()
    {
        if (_selectedTask is null) return;
        using var dialog = new ContractsDialog(_tasks, _selectedTask, _reviews);
        dialog.ShowDialog(FindForm());
        RefreshContractSection();
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

    /// <summary>配置了 Swagger、还没冻结过且不是离线任务：检查前先加载并冻结 Swagger（与冻结页面同一规则，避免不知情地退化为只按远端现状比较）。</summary>
    private static bool NeedsSwaggerLoad(CheckTask task) =>
        !task.OfflineMode
        && task.ProfileSnapshot.Swagger.Type is not (null or "" or "none")
        && task.Stages.Remote.SourceHash is null;

    /// <summary>Apifox 差异检查（只读后台作业）：需要时先加载并冻结 Swagger，再读取远端与目标比较，结果落盘 diff.json；不写入远端。</summary>
    private void StartRemoteCheck()
    {
        if (_selectedTask is null) return;
        var taskId = _selectedTask.Id;
        var loadSwagger = NeedsSwaggerLoad(_selectedTask);
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
/// 新建任务对话框：选配置、可选模块与路径前缀；「创建并扫描」立即开始源码扫描作业。
/// </summary>
internal sealed class NewTaskDialog : Form
{
    private readonly ProfileStore _profiles;
    private readonly ComboBox _profile = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _name = new();
    private readonly CheckedListBox _modules = new() { CheckOnClick = true, Height = 96, BorderStyle = BorderStyle.FixedSingle };
    private readonly TextBox _prefixes = new();
    private readonly CheckBox _offline = new() { Text = "离线模式（本次任务不访问 Apifox，仅源码阶段）", AutoSize = true };
    private readonly Label _status = Ui.StatusLine();
    private List<ModuleScope> _currentModules = [];

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
        ClientSize = new Size(560, 420);
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

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "项目配置", _profile);
        AddRow(grid, "任务名称", _name);
        AddRow(grid, "", Ui.Caption("留空则用「配置名 月-日 时:分」。"));
        AddRow(grid, "模块", _modules);
        AddRow(grid, "", Ui.Caption("勾选要检查的模块；都不勾选则检查该 API 项目的全部接口。"));
        AddRow(grid, "路径前缀", _prefixes);
        AddRow(grid, "", Ui.Caption("补充接口路径前缀，用「,」「;」分隔；可与模块并用。"));
        AddRow(grid, "", _offline);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（创建并扫描）在最右。
        buttons.Controls.Add(scanButton);
        buttons.Controls.Add(createButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        Controls.Add(grid);
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
        _modules.Items.Clear();
        _currentModules = [];
        if (_profile.SelectedItem is not ProfileItem item) return;
        _currentModules = item.Profile.Modules;
        foreach (var module in item.Profile.Modules)
        {
            _modules.Items.Add(module.Name + "（" + string.Join("、", module.PathPrefixes) + "）");
        }
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
        var moduleNames = _modules.CheckedIndices.Cast<int>().Select(i => _currentModules[i].Name).ToList();
        var prefixes = _prefixes.Text.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Request = new CreateTaskRequest
        {
            ProfileId = item.Profile.Id,
            Name = _name.Text.Trim(),
            ModuleNames = moduleNames,
            PathPrefixes = prefixes,
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
