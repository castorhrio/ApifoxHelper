using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 同步预览与执行：勾选范围内接口生成冻结的同步计划，查看逐动作写入清单、CLI 校验结果、
/// 未纳入原因、共享模型范围外影响与保留设置。计划生成经既有 SyncPlanService
/// （写入门禁、检查版本一致、逐接口重读远端核对指纹、cli-schema 校验、模型依赖排序都不变），
/// 执行与回读核对经既有 SyncExecutor（计划哈希绑定、写入前重读核对指纹、写入后立即回读核对、
/// 首个失败即停止、取消只在动作之间生效、部分完成如实记录、不自动回滚），两者都走共享 JobRunner，可停止。
/// 「确认同步」按设计基线用原生确认对话框（默认按钮在「取消」）；「重新核对（只读）」复用 VerifyAsync，不写入。
/// 选中动作显示写入内容（脱敏后，与执行时写入的 JSON 一致）。
/// 状态表：未检查/读取失败/离线=空态指引并禁用全部动作；生成或执行中=状态行进度 + 停止；
/// 生成失败/执行被拒=原因与下一步；计划就绪=概要 + 写入清单 + 排除清单 + 影响与保留设置；
/// 执行或核对结束=逐动作结果 + 按计划状态如实报告（部分完成不显示成全部成功）；
/// 计划生成后任务内容有变化=过期提示（计划不能再执行，仍如实展示）。
/// </summary>
internal sealed class SyncPreviewDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly SyncPlanService _plans;
    private readonly SyncExecutor _executor;
    private readonly JobRunner _jobs;
    private readonly CheckTask _task;
    private readonly string _taskId;

    private CheckResult? _diff;
    private DecisionStore _decisions = new();
    private SyncPlan? _plan;
    private JobInfo? _job;
    private bool _busy;

    // 勾选页（生成计划前的接口选择）。
    private readonly Label _selectHint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };
    private readonly ListView _selectList = Ui.List(true);
    private readonly Label _selectedCount = Ui.StatusText("尚未勾选接口");
    private readonly Button _selectAllButton;
    private readonly Button _clearButton;
    private readonly Button _viewPlanButton;
    private readonly Button _generateButton;
    private readonly Panel _page1;

    // 预览页（计划生成后）。
    private readonly Label _planStatus = Ui.StatusText();
    private readonly Label _planCounts = Ui.Mono();
    private readonly Label _planMeta = Ui.Mono();
    private readonly Label _stale = new()
    {
        AutoSize = true,
        ForeColor = UiTokens.WarnText,
        Font = UiTokens.Caption.Value,
        Margin = new Padding(0),
        Visible = false,
    };
    private readonly Label _planMessage = new()
    {
        AutoSize = true,
        ForeColor = UiTokens.WarnText,
        Font = UiTokens.Body.Value,
        Margin = new Padding(0),
        Visible = false,
    };
    private readonly ListView _actionList = Ui.List();
    private readonly TextBox _payload = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Mono.Value,
    };
    private readonly ListView _excludedList = Ui.List();
    private readonly Panel _excludedHost;
    private readonly TextBox _notes = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Body.Value,
    };
    private readonly Button _applyButton;
    private readonly Button _verifyButton;
    private readonly Panel _page2;

    private readonly Button _backButton;
    private readonly Button _stopButton;
    private readonly Button _closeButton;
    private readonly Label _status = Ui.StatusLine();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public SyncPreviewDialog(TaskStore tasks, SyncPlanService plans, SyncExecutor executor, JobRunner jobs, CheckTask task)
    {
        _tasks = tasks;
        _plans = plans;
        _executor = executor;
        _jobs = jobs;
        _task = task;
        _taskId = task.Id;

        Text = "同步预览 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1020, 740);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _selectList.Columns.Add("接口", 300);
        _selectList.Columns.Add("名称", 200);
        _selectList.Columns.Add("状态", 96);
        _selectList.Columns.Add("待确认", 72);
        _selectList.ItemCheck += (_, e) =>
        {
            // 不可同步的接口（无变化/阻断/仅源码/离线）勾选立即弹回；口径见 SyncSelection。
            if (e.NewValue != CheckState.Unchecked
                && _selectList.Items[e.Index].Tag is EndpointCheck check
                && !SyncSelection.Selectable(_task.OfflineMode, check.Status))
            {
                e.NewValue = CheckState.Unchecked;
            }
        };
        _selectList.ItemChecked += (_, _) => RefreshSelectionCount();

        _selectAllButton = Ui.Ghost("全选可同步");
        _selectAllButton.Enabled = false;
        _selectAllButton.Click += (_, _) => SelectAllSyncable();
        _clearButton = Ui.Ghost("清空");
        _clearButton.Enabled = false;
        _clearButton.Click += (_, _) => SetAllChecked(false);
        _viewPlanButton = Ui.Ghost("查看已有计划");
        _viewPlanButton.Enabled = false;
        _viewPlanButton.Click += (_, _) => ViewExistingPlan();
        _applyButton = Ui.Primary("确认同步");
        _applyButton.Enabled = false;
        _applyButton.Click += (_, _) => ApplyPlan();
        _verifyButton = Ui.Ghost("重新核对（只读）");
        _verifyButton.Enabled = false;
        _verifyButton.Click += (_, _) => VerifyRemote();
        _generateButton = Ui.Primary("生成同步预览");
        _generateButton.Enabled = false;
        _generateButton.Click += (_, _) => Generate();

        _page1 = BuildSelectPage();

        _actionList.Columns.Add("#", 40);
        _actionList.Columns.Add("动作", 92);
        _actionList.Columns.Add("资源", 240);
        _actionList.Columns.Add("远端 ID", 72);
        _actionList.Columns.Add("改动", 280);
        _actionList.Columns.Add("CLI 校验", 110);
        _actionList.Columns.Add("状态", 110);
        _actionList.SelectedIndexChanged += (_, _) => ShowPayload();

        _excludedList.Columns.Add("资源", 240);
        _excludedList.Columns.Add("未纳入原因", 620);
        var excludedInner = Ui.Hairline(_excludedList);
        excludedInner.Dock = DockStyle.Fill;
        excludedInner.Height = 92;
        _excludedHost = BuildBottomHost(excludedInner, "未纳入计划（原因如下；处理后重新检查、重新生成同步预览）", 122);

        _backButton = Ui.Ghost("重新选择接口");
        _backButton.Visible = false;
        _backButton.Click += (_, _) => ShowSelectPage();
        _stopButton = Ui.DangerGhost("停止");
        _stopButton.Visible = false;
        _stopButton.Click += (_, _) => _jobs.Cancel(_taskId);
        _closeButton = Ui.Ghost("关闭");
        _closeButton.Click += (_, _) => Close();
        AcceptButton = _closeButton;
        CancelButton = _closeButton;

        _page2 = BuildPlanPage();

        var buttonRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 8, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, UiTokens.Md), Margin = new Padding(0) };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _selectedCount.Margin = new Padding(0, 0, UiTokens.Md, 0);
        _selectAllButton.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _clearButton.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _viewPlanButton.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _backButton.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        _closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        buttonRow.Controls.Add(_selectedCount, 0, 0);
        buttonRow.Controls.Add(_selectAllButton, 1, 0);
        buttonRow.Controls.Add(_clearButton, 2, 0);
        buttonRow.Controls.Add(_viewPlanButton, 3, 0);
        buttonRow.Controls.Add(_backButton, 4, 0);
        buttonRow.Controls.Add(new Panel { Margin = new Padding(0) }, 5, 0);
        buttonRow.Controls.Add(_generateButton, 6, 0);
        buttonRow.Controls.Add(_closeButton, 7, 0);

        var statusRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0), Margin = new Padding(0) };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _status.Dock = DockStyle.Fill;
        statusRow.Controls.Add(_status, 0, 0);
        statusRow.Controls.Add(_stopButton, 1, 0);

        // 停靠按集合倒序生效：状态行、按钮行先停靠（底部），勾选页与预览页互斥地填满剩余空间。
        Controls.Add(_page1);
        Controls.Add(_page2);
        Controls.Add(buttonRow);
        Controls.Add(statusRow);

        _timer.Tick += (_, _) => OnTick();
    }

    private Panel BuildSelectPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        var caption = Ui.Caption("勾选要纳入同步的接口（无变化、阻断与仅源码不可同步）；生成计划只读远端、不写入。");
        caption.Dock = DockStyle.Top;
        caption.Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm);
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_selectList));
        listHost.Controls.Add(_selectHint);
        _selectHint.Visible = false;
        page.Controls.Add(listHost);
        page.Controls.Add(caption);
        return page;
    }

    private Panel BuildPlanPage()
    {
        var page = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };

        _planStatus.TextAlign = ContentAlignment.MiddleLeft;
        _planCounts.AutoSize = false;
        _planCounts.Dock = DockStyle.Fill;
        _planCounts.TextAlign = ContentAlignment.MiddleRight;
        _planMeta.AutoSize = false;
        _planMeta.Dock = DockStyle.Fill;
        _planMeta.TextAlign = ContentAlignment.MiddleLeft;
        var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        head.Controls.Add(_planStatus, 0, 0);
        head.Controls.Add(_planCounts, 1, 0);
        head.Controls.Add(_planMeta, 0, 1);
        head.SetColumnSpan(_planMeta, 2);
        head.Controls.Add(_stale, 0, 2);
        head.SetColumnSpan(_stale, 2);
        head.Controls.Add(_planMessage, 0, 3);
        head.SetColumnSpan(_planMessage, 2);
        var planActions = new TableLayoutPanel { AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 3, Margin = new Padding(0), Padding = new Padding(0) };
        planActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        planActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        planActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _verifyButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        planActions.Controls.Add(_applyButton, 0, 0);
        planActions.Controls.Add(_verifyButton, 1, 0);
        planActions.Controls.Add(new Panel { Margin = new Padding(0) }, 2, 0);
        head.Controls.Add(planActions, 0, 4);
        head.SetColumnSpan(planActions, 2);
        var executionCaption = Ui.Caption("确认后逐动作写入并立即回读核对；首个失败即停止，已写入部分不回滚，可在动作之间停止。");
        head.Controls.Add(executionCaption, 0, 5);
        head.SetColumnSpan(executionCaption, 2);

        var actionCaption = Ui.Caption("写入清单（按执行顺序：模型在前、接口在后；选中动作在下方显示写入内容）");
        actionCaption.Dock = DockStyle.Top;
        actionCaption.Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0);
        var actionHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        actionHost.Controls.Add(Ui.Hairline(_actionList));
        actionHost.Controls.Add(actionCaption);

        var payloadInner = Ui.Hairline(_payload);
        payloadInner.Dock = DockStyle.Fill;
        payloadInner.Height = 160;
        var payloadHost = BuildBottomHost(payloadInner, "选中动作的写入内容（敏感值已脱敏；与执行时写入的 JSON 一致）", 190);
        var notesInner = Ui.Hairline(_notes);
        notesInner.Dock = DockStyle.Fill;
        notesInner.Height = 100;
        var notesHost = BuildBottomHost(notesInner, "共享模型对范围外接口的影响、计划提示与保留设置", 130);

        // 停靠按集合倒序生效：标题区最先停靠（顶部），写入内容、说明区、排除清单自下而上，写入清单填满剩余空间。
        page.Controls.Add(actionHost);
        page.Controls.Add(_excludedHost);
        page.Controls.Add(notesHost);
        page.Controls.Add(payloadHost);
        page.Controls.Add(head);
        return page;
    }

    /// <summary>底部固定区：上方说明行 + 发丝边内容控件（Hairline 包裹后传入）。</summary>
    private static Panel BuildBottomHost(Control inner, string caption, int height)
    {
        var host = new Panel { Dock = DockStyle.Bottom, Height = height, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        var captionLabel = Ui.Caption(caption);
        captionLabel.Dock = DockStyle.Top;
        host.Controls.Add(inner);
        host.Controls.Add(captionLabel);
        return host;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _diff = _tasks.Read<CheckResult>(_taskId, TaskFiles.Diff);
            _decisions = _tasks.Read<DecisionStore>(_taskId, TaskFiles.Decisions) ?? new DecisionStore();
        }
        catch (Exception ex)
        {
            DisableSelect("读取检查结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后在任务页重新检查再试。", UiTokens.BadText);
            return;
        }
        if (_diff is null || _diff.Endpoints.Count == 0)
        {
            DisableSelect("请先完成契约检查：在任务页点「开始检查」，检查完成后在这里勾选要同步的接口。", UiTokens.Muted);
            return;
        }
        if (_task.OfflineMode)
        {
            DisableSelect("离线检查不能同步。同步预览只对在线任务开放。", UiTokens.Muted);
            return;
        }
        PopulateSelectList();
        _viewPlanButton.Enabled = File.Exists(_tasks.FilePath(_taskId, TaskFiles.Plan));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    private void DisableSelect(string message, Color color)
    {
        _selectHint.Text = message;
        _selectHint.ForeColor = color;
        _selectHint.Visible = true;
        _selectList.Visible = false;
        _selectAllButton.Enabled = false;
        _clearButton.Enabled = false;
        _generateButton.Enabled = false;
    }

    private void PopulateSelectList()
    {
        var diff = _diff!;
        _selectList.BeginUpdate();
        foreach (var check in diff.Endpoints)
        {
            var row = new ListViewItem(check.Method + " " + check.Path) { Tag = check, UseItemStyleForSubItems = false };
            row.Font = UiTokens.Mono.Value;
            var name = row.SubItems.Add(check.RemoteName ?? (check.InRemote ? "—" : "Apifox 中不存在"));
            name.Font = UiTokens.Body.Value;
            if (!SyncSelection.Selectable(_task.OfflineMode, check.Status)) row.ForeColor = UiTokens.Muted;
            var status = row.SubItems.Add(TaskDisplay.CheckStatusText(check.Status));
            status.Font = UiTokens.Body.Value;
            status.ForeColor = ToneColor(TaskDisplay.CheckStatusTone(check.Status));
            var pending = SyncSelection.PendingCount(diff, _decisions, check.Key);
            var pendingCell = row.SubItems.Add(pending > 0 ? pending.ToString() : "—");
            pendingCell.Font = UiTokens.Mono.Value;
            if (pending > 0) pendingCell.ForeColor = UiTokens.WarnText;
            _selectList.Items.Add(row);
        }
        _selectList.EndUpdate();
        _selectAllButton.Text = "全选可同步（" + SyncSelection.SyncableKeys(_task.OfflineMode, diff, _decisions).Count + "）";
        _selectAllButton.Enabled = true;
        _clearButton.Enabled = true;
        RefreshSelectionCount();
    }

    private void SetAllChecked(bool checkAll)
    {
        foreach (ListViewItem item in _selectList.Items)
        {
            item.Checked = checkAll && item.Tag is EndpointCheck check && SyncSelection.Selectable(_task.OfflineMode, check.Status);
        }
    }

    private void SelectAllSyncable()
    {
        if (_diff is null) return;
        var keys = SyncSelection.SyncableKeys(_task.OfflineMode, _diff, _decisions).ToHashSet(StringComparer.Ordinal);
        foreach (ListViewItem item in _selectList.Items)
        {
            item.Checked = item.Tag is EndpointCheck check && keys.Contains(check.Key);
        }
    }

    private int CheckedCount() => _selectList.Items.OfType<ListViewItem>().Count(i => i.Checked);

    private void RefreshSelectionCount()
    {
        var count = CheckedCount();
        _selectedCount.Text = count > 0 ? "已选择 " + count + " 个接口" : "尚未勾选接口";
        if (!_busy) _generateButton.Enabled = count > 0;
    }

    private void Generate()
    {
        if (_diff is null || _busy) return;
        var keys = _selectList.Items.OfType<ListViewItem>()
            .Where(i => i.Checked && i.Tag is EndpointCheck)
            .Select(i => ((EndpointCheck)i.Tag!).Key)
            .ToList();
        if (keys.Count == 0) return;
        try
        {
            _job = _jobs.Start(_taskId, "生成同步计划", async job => await _plans.CreateAsync(_taskId, keys, job));
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法生成同步计划：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            return;
        }
        SetBusy(true);
        _timer.Start();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _selectAllButton.Enabled = !busy && _diff is not null;
        _clearButton.Enabled = !busy && _diff is not null;
        _viewPlanButton.Enabled = !busy && File.Exists(_tasks.FilePath(_taskId, TaskFiles.Plan));
        _backButton.Enabled = !busy;
        _generateButton.Enabled = !busy && CheckedCount() > 0;
        RefreshPlanActionButtons();
        _stopButton.Visible = busy;
    }

    /// <summary>执行与核对的按钮可用性（口径见 SyncExecution，与冻结页面同一规则）。</summary>
    private void RefreshPlanActionButtons()
    {
        if (_plan is null || _busy)
        {
            _applyButton.Enabled = false;
            _verifyButton.Enabled = false;
            return;
        }
        var outdated = _plan.Status == "awaitingConfirm" && _plan.TaskRevision != ReadTaskSnapshot().Revision;
        _applyButton.Enabled = SyncExecution.CanExecute(_plan.Status, outdated);
        _verifyButton.Enabled = SyncExecution.CanVerify(_plan.Status);
    }

    /// <summary>任务当前内容：读取失败时按打开时的快照判断过期（与既有 ShowPlan 同一兜底）。</summary>
    private CheckTask ReadTaskSnapshot()
    {
        try { return _tasks.Get(_taskId); }
        catch { return _task; }
    }

    /// <summary>
    /// 确认同步：不可逆写入，按设计基线先弹原生确认对话框（默认按钮在「取消」），
    /// 之后经既有 SyncExecutor 执行（计划哈希绑定；写入前核对指纹、写入后回读核对、失败即停都在服务层，本层不改写）。
    /// </summary>
    private void ApplyPlan()
    {
        var plan = _plan;
        if (_busy || plan is null) return;
        var outdated = plan.Status == "awaitingConfirm" && plan.TaskRevision != ReadTaskSnapshot().Revision;
        if (!SyncExecution.CanExecute(plan.Status, outdated))
        {
            SetStatus(outdated
                ? "生成计划后任务内容有变化，这份计划不能再执行。请重新检查后重新生成同步预览。"
                : "当前计划状态是「" + TaskDisplay.SyncPlanStatusText(plan.Status) + "」，不能执行。", UiTokens.WarnText);
            return;
        }
        var endpoints = plan.Actions.Count(a => a.Kind.EndsWith("Endpoint", StringComparison.Ordinal));
        var models = plan.Actions.Count - endpoints;
        var impact = plan.OutOfScopeImpact.Count > 0
            ? "\n共享模型更新会影响范围外 " + plan.OutOfScopeImpact.Count + " 个接口的文档表现。"
            : "";
        var choice = MessageBox.Show(this,
            "将写入 Apifox 项目 " + plan.ProjectId + " / 分支 " + (plan.Branch ?? "main") + "：" + endpoints + " 个接口、" + models + " 个模型。" + impact
            + "\n同步期间请避免在 Apifox 中修改相同资源；本地锁不能阻止其他用户的修改。\n\n确认同步？",
            "确认同步",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.OK) return;
        var hash = plan.Hash;
        try
        {
            _job = _jobs.Start(_taskId, "执行同步", async job => await _executor.ExecuteAsync(_taskId, hash, job));
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法执行同步：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            return;
        }
        SetBusy(true);
        _timer.Start();
    }

    /// <summary>重新核对（只读）：复用既有 VerifyAsync，按 ID/身份回读远端核对已写入动作，不写入。</summary>
    private void VerifyRemote()
    {
        if (_busy || _plan is null) return;
        try
        {
            _job = _jobs.Start(_taskId, "回读核对", async job => await _executor.VerifyAsync(_taskId, job));
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法开始回读核对：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            return;
        }
        SetBusy(true);
        _timer.Start();
    }

    private void OnTick()
    {
        if (IsDisposed || Disposing) return;
        if (_job is not { } job) { _timer.Stop(); return; }
        if (job.FinishedAt is null)
        {
            _status.Text = "正在" + job.Kind + "… " + job.Progress.ToString("P0") + " " + job.ProgressText;
            _status.ForeColor = UiTokens.Text;
            return;
        }
        _timer.Stop();
        _job = null;
        SetBusy(false);
        switch (job.State)
        {
            case "succeeded" when job.Kind == "生成同步计划":
                {
                    SyncPlan? plan = null;
                    try { plan = _tasks.Read<SyncPlan>(_taskId, TaskFiles.Plan); }
                    catch { /* 读取失败时按无计划处理，走下方提示。 */ }
                    if (plan is null) SetStatus("同步计划已生成，但读取计划失败。请点「查看已有计划」重试。", UiTokens.BadText);
                    else ShowPlan(plan);
                    break;
                }
            case "succeeded":
                RefreshPlanFromDisk();
                break;
            case "canceled" when job.Kind == "生成同步计划":
                SetStatus("已停止生成同步计划。可重新勾选后再次生成。", UiTokens.WarnText);
                break;
            case "canceled":
                {
                    var plan = RefreshPlanFromDisk();
                    SetStatus(plan is { Status: "awaitingConfirm" }
                        ? "已停止" + job.Kind + "，还没有动作写入。可重新确认同步。"
                        : "已停止" + job.Kind + "。已写入部分不回滚，逐动作结果见写入清单。",
                        UiTokens.WarnText);
                    break;
                }
            case "failed" when job.Kind == "生成同步计划":
                SetStatus("生成同步计划失败：" + (job.Error ?? "原因未知") + "。处理后可重新生成。", UiTokens.BadText);
                break;
            case "failed":
                // 执行被门禁拒绝（计划过期、CLI 未探测、项目/分支锁占用等）：按磁盘计划刷新（可能已被置为过期），状态行保留原因。
                RefreshPlanFromDisk();
                SetStatus(job.Kind + "失败：" + (job.Error ?? "原因未知") + "。", UiTokens.BadText);
                break;
        }
    }

    /// <summary>
    /// 按磁盘上的计划刷新界面并给出结果状态行（执行、核对、取消后共用）；
    /// 读取失败时保留当前视图并如实报告。返回读到的计划，供调用方补充说明。
    /// </summary>
    private SyncPlan? RefreshPlanFromDisk()
    {
        SyncPlan? plan;
        try { plan = _tasks.Read<SyncPlan>(_taskId, TaskFiles.Plan); }
        catch { plan = null; }
        if (plan is null)
        {
            SetStatus("读取同步计划失败。请点「查看已有计划」重试。", UiTokens.BadText);
            return null;
        }
        ShowPlan(plan);
        var verified = plan.Actions.Count(a => a.State == "verified");
        var failed = plan.Actions.Count(a => a.State is "writeFailed" or "verifyFailed" or "unknown" or "conflict");
        var notRun = plan.Actions.Count(a => a.State == "notRun");
        var (text, tone) = SyncExecution.ResultText(plan.Status, verified, failed, notRun);
        SetStatus(text, ToneColor(tone));
        return plan;
    }

    private void ViewExistingPlan()
    {
        if (_busy) return;
        try
        {
            if (_tasks.Read<SyncPlan>(_taskId, TaskFiles.Plan) is not { } plan)
            {
                SetStatus("任务目录里没有同步计划。请勾选接口后点「生成同步预览」。", UiTokens.WarnText);
                return;
            }
            ShowPlan(plan);
        }
        catch (Exception ex)
        {
            SetStatus("读取同步计划失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    /// <summary>展示计划：概要、过期提示、确认同步与重新核对、写入清单、排除清单与影响/保留设置。</summary>
    private void ShowPlan(SyncPlan plan)
    {
        _status.Text = "";
        _plan = plan;
        _page1.Visible = false;
        _page2.Visible = true;
        _backButton.Visible = true;
        _generateButton.Visible = false;

        var outdated = plan.Status == "awaitingConfirm" && plan.TaskRevision != ReadTaskSnapshot().Revision;
        var display = outdated ? "expired" : plan.Status;
        _planStatus.Text = "计划状态：" + TaskDisplay.SyncPlanStatusText(display);
        _planStatus.ForeColor = ToneColor(TaskDisplay.SyncPlanTone(display));
        var endpoints = plan.Actions.Count(a => a.Kind.EndsWith("Endpoint", StringComparison.Ordinal));
        _applyButton.Text = "确认同步 " + endpoints + " 个接口、" + (plan.Actions.Count - endpoints) + " 个模型";
        _planCounts.Text = $"接口 {endpoints} · 模型 {plan.Actions.Count - endpoints} · 排除 {plan.Excluded.Count}";
        _planMeta.Text = "目标：" + (plan.ProjectName ?? "") + " 项目 " + plan.ProjectId + " / 分支 " + (plan.Branch ?? "main")
            + " · CLI " + plan.CliVersion + " · 计划指纹 " + (plan.Hash.Length > 12 ? plan.Hash[..12] : plan.Hash)
            + " · 生成于 " + plan.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        _stale.Text = "生成计划后任务内容有变化，这份计划不能再执行。请重新检查后重新生成同步预览。";
        _stale.Visible = outdated;
        _planMessage.Visible = plan.Message is not null;
        if (plan.Message is { } message) _planMessage.Text = message;

        _actionList.BeginUpdate();
        _actionList.Items.Clear();
        for (var i = 0; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i];
            var row = new ListViewItem((i + 1).ToString()) { Tag = action, UseItemStyleForSubItems = false };
            var kind = row.SubItems.Add(TaskDisplay.PlanActionKindText(action.Kind));
            kind.Font = UiTokens.Body.Value;
            var resource = row.SubItems.Add(action.ResourceKey);
            resource.Font = UiTokens.Mono.Value;
            var remoteId = row.SubItems.Add(action.RemoteId?.ToString() ?? "—");
            remoteId.Font = UiTokens.Mono.Value;
            var changes = row.SubItems.Add(action.Changes.Count > 0
                ? string.Join("；", action.Changes)
                : action.ManagedFields.Count > 0 ? "写入字段：" + string.Join(", ", action.ManagedFields) : "—");
            changes.Font = UiTokens.Body.Value;
            if (action.Notes.Count > 0) row.ToolTipText = string.Join("\n", action.Notes);
            var validation = row.SubItems.Add(string.IsNullOrWhiteSpace(action.Validation) ? "—" : action.Validation);
            validation.Font = UiTokens.Body.Value;
            if (action.Validation is { Length: > 0 } and not "通过") validation.ForeColor = UiTokens.BadText;
            var state = row.SubItems.Add(TaskDisplay.PlanActionStateText(action.State));
            state.Font = UiTokens.Body.Value;
            state.ForeColor = ToneColor(TaskDisplay.PlanActionStateTone(action.State));
            if (action.Message is { } actionMessage)
            {
                row.ToolTipText = action.Notes.Count > 0 ? actionMessage + "\n" + string.Join("\n", action.Notes) : actionMessage;
            }
            _actionList.Items.Add(row);
        }
        _actionList.EndUpdate();
        if (_actionList.Items.Count > 0) _actionList.Items[0].Selected = true;

        _excludedList.BeginUpdate();
        _excludedList.Items.Clear();
        foreach (var exclusion in plan.Excluded)
        {
            var row = new ListViewItem(exclusion.ResourceKey) { UseItemStyleForSubItems = false };
            row.Font = UiTokens.Mono.Value;
            row.SubItems.Add(exclusion.Reason).Font = UiTokens.Body.Value;
            _excludedList.Items.Add(row);
        }
        _excludedList.EndUpdate();
        _excludedHost.Visible = plan.Excluded.Count > 0;

        var notes = new List<string>();
        if (plan.OutOfScopeImpact.Count > 0)
        {
            notes.Add("共享模型对范围外接口的影响（" + plan.OutOfScopeImpact.Count + "）：");
            notes.AddRange(plan.OutOfScopeImpact.Select(x => "  " + x.Key + "（" + x.Name + "，ID " + x.Id + "）"));
        }
        notes.AddRange(plan.Warnings.Select(w => "警告：" + w));
        notes.Add("保留设置：" + string.Join("、", plan.PreservedSettings));
        notes.Add("计划确认执行时逐个动作：重读目标并核对指纹 → 保存前态 → 写入 → 立即回读核对写入内容与受保护字段；首个失败即停止，不自动回滚，不重发超时写入。");
        _notes.Lines = [.. notes];
        _notes.SelectionStart = 0;
        _notes.SelectionLength = 0;
        RefreshPlanActionButtons();
    }

    private void ShowSelectPage()
    {
        _page2.Visible = false;
        _page1.Visible = true;
        _backButton.Visible = false;
        _generateButton.Visible = true;
        _status.Text = "";
        SetBusy(false);
    }

    /// <summary>选中动作的写入内容：与 Web 下载同一脱敏口径（Redactor.RedactNode），缩进 JSON。</summary>
    private void ShowPayload()
    {
        if (_actionList.SelectedItems.Count == 0 || _actionList.SelectedItems[0].Tag is not PlanAction action)
        {
            _payload.Text = "";
            return;
        }
        try
        {
            var node = FileStore.ReadNode(action.PayloadFile);
            _payload.Text = node is null
                ? "（该动作没有写入内容）"
                // 与任务资料同一序列化口径（JsonDefaults.Options：缩进、中文原样），先脱敏再渲染。
                : JsonSerializer.Serialize(Redactor.RedactNode(node), JsonDefaults.Options);
        }
        catch (Exception ex)
        {
            _payload.Text = "（写入内容读取失败：" + Redactor.Redact(ex.Message) + "）";
        }
        _payload.SelectionStart = 0;
        _payload.SelectionLength = 0;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
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
