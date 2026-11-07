using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 检查任务页：任务列表 + 选中任务的阶段概览 + 源码扫描（JobRunner 后台作业，进度与取消）
/// + 注释候选（规则提取后台作业、逐条审阅接受/搁置/驳回）。
/// 生成文档核对与 Apifox 检查/同步不在本切片：如实显示"本切片未接入"，不提供入口。
/// </summary>
internal sealed class TasksPage : UserControl, INativePage
{
    private readonly TaskStore _tasks;
    private readonly SourceScanService _scan;
    private readonly JobRunner _jobs;
    private readonly ProfileStore _profiles;
    private readonly DraftService _drafts;

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
    private readonly Button _gapButton;
    private readonly Button _candidatesButton;
    private readonly Button _ruleButton;
    private readonly Button _scanButton;
    private readonly Label _progress = Ui.StatusText();
    private readonly Button _cancelButton;
    private readonly Label _generatedStatus = Ui.StatusText();
    private readonly Label _remoteStatus = Ui.StatusText();
    private readonly Label _remoteNumbers = Ui.Mono();

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private CheckTask? _selectedTask;
    private bool _runningShown;
    private bool _splitAdjusted;

    public TasksPage(TaskStore tasks, SourceScanService scan, JobRunner jobs, ProfileStore profiles, DraftService drafts)
    {
        _tasks = tasks;
        _scan = scan;
        _jobs = jobs;
        _profiles = profiles;
        _drafts = drafts;

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
        _scanButton = Ui.Ghost("开始扫描");
        _scanButton.Click += (_, _) => StartScan();
        _cancelButton = Ui.DangerGhost("取消");
        _cancelButton.Click += (_, _) => CancelScan();
        _scanActions.Controls.Add(_gapButton);
        _scanActions.Controls.Add(_candidatesButton);
        _scanActions.Controls.Add(_ruleButton);
        _scanActions.Controls.Add(_scanButton);
        _scanActions.Controls.Add(_cancelButton);
        _scanActions.Controls.Add(_progress);
        _cancelButton.Visible = false;
        _progress.Visible = false;
        _progress.Margin = new Padding(UiTokens.Md, 0, 0, 0);

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
        AddDetailRow(grid, Ui.Caption("「查看注释缺口」「规则提取候选」与「查看注释候选」在扫描完成后可用；模型生成、材料导入、手工填写与源码回写将在后续切片接入。"));
        AddDetailRow(grid, SectionCaption("生成文档"));
        AddDetailRow(grid, _generatedStatus);
        AddDetailRow(grid, Ui.Caption("本切片未接入。"));
        AddDetailRow(grid, SectionCaption("Apifox 差异与同步"));
        AddDetailRow(grid, _remoteStatus);
        AddDetailRow(grid, _remoteNumbers);
        AddDetailRow(grid, Ui.Caption("本切片未接入；检查、同步与报告将在后续切片提供。"));
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

        var remote = task.Stages.Remote;
        _remoteStatus.Text = "状态：" + TaskDisplay.RemoteStatusText(remote.Status) + (remote.CheckedRevision is { } rev && rev < task.Revision ? "（任务内容在检查后有变化，需要重新检查）" : "");
        _remoteStatus.ForeColor = ToneColor(TaskDisplay.RemoteTone(remote.Status));
        _remoteNumbers.Text = remote.SnapshotAt is null
            ? "尚未检查"
            : $"新增 {remote.Added} · 结构变化 {remote.StructureChanged} · 说明变化 {remote.DocChanged} · 冲突 {remote.Conflicts} · 阻断 {remote.Blocked}";

        var job = _jobs.Running(task.Id);
        ShowJobState(job);
    }

    /// <summary>扫描区按作业状态切换：按钮（空闲）或进度 + 取消（执行中）。控件位置固定，不跳位。</summary>
    private void ShowJobState(JobInfo? job)
    {
        // 缺口清单、候选审阅与规则提取都基于扫描落盘产物：扫描完成后可用，作业执行中与未扫描时禁用（禁用态说明见下方固定说明行）。
        var scanReady = _selectedTask is { Stages.Source.ScannedAt: not null } && job is null;
        _gapButton.Enabled = scanReady;
        _candidatesButton.Enabled = scanReady;
        _ruleButton.Enabled = scanReady;
        if (job is not null)
        {
            _runningShown = true;
            _scanButton.Visible = false;
            _progress.Visible = true;
            _cancelButton.Visible = true;
            _progress.Text = job.State == "running"
                ? $"正在{job.Kind}… {job.Progress:P0} {job.ProgressText}"
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
