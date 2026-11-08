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
/// 两页各有一个关键词框、各接同一份键盘路径（Ctrl+F 到关键词框 → 输入 → Enter 到结果第一行 →
/// 方向键逐行看 → Esc 退出筛选）：勾选页筛接口（第七十二、七十三轮），预览页筛写入清单（第八十轮）。
/// 两个框各管自己那一页——一次按键只有一个含义，预览页的 Esc 不会去清勾选页的关键词，反之亦然。
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
    // 关键词框（DESIGN.md §6「列表筛选」）：这一页是整份检查结果的接口清单，真实项目几十上百个
    // （方案 §4.2 记录千级接口分支），要找到「上次那个接口」只能逐行扫；占位文字取共享常量。
    private readonly TextBox _keyword = Ui.Input();
    // 计数格：整份检查结果的聚合数字（接口总数与可同步数），不随关键词变化——
    // 与五张长清单「计数行按全部条目统计」同一口径（第五十九轮）。
    private readonly Label _visibleCount = Ui.Mono();
    // 勾选状态的唯一来源（按接口键记）：筛选要重建清单，重建就把行上的勾选丢了，
    // 勾了 12 个、筛一下再筛回来只剩 3 个，点「生成同步计划」写出去的就是 3 个。
    private readonly SyncSelect _select = new();
    // 重建清单期间不把行上的勾选回写进 SyncSelect（回写是幂等的，但每行都刷一次计数是 O(n²)）。
    private bool _rebuilding;
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
    // 写入清单的标识列：首列是动作序号（第几步），认不出这一行写的是哪个接口或模型；
    // 名称格在「资源」列——Ctrl+C 交出去的是它。
    private const int ActionResourceColumn = 2;

    private readonly ListView _actionList = Ui.List(identityColumn: ActionResourceColumn);
    // 写入清单的关键词框（第八十轮）：这一屏是整份冻结计划的逐动作写入清单，真实项目几十上百个动作，
    // 确认同步之前要逐条看过，要找到「失败的那几条」只能逐行扫；占位文字取共享常量。
    private readonly TextBox _actionKeyword = Ui.Input();
    // 筛到零行时说话的那一格（与另十张长清单同一形状：清单与提示互斥地填满同一块）。
    private readonly Label _actionHint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };
    private readonly TextBox _payload = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly ListView _excludedList = Ui.List();
    private readonly Panel _excludedHost;
    private readonly TextBox _notes = Ui.Preview(multiline: true, scrollBars: ScrollBars.Vertical);
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
        _keyword.PlaceholderText = ListFilter.SyncSelectSearchPlaceholder;
        _keyword.TextChanged += (_, _) => ApplyFilter();
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
        _selectList.ItemChecked += (_, e) =>
        {
            // 勾选状态的唯一写入口：界面只在 ItemChecked 里调它，不自己维护第二份。
            if (_rebuilding) return;
            if (e.Item.Tag is EndpointCheck check) _select.Set(check.Key, e.Item.Checked);
            RefreshSelectionCount();
        };

        _selectAllButton = Ui.Ghost("全选可同步");
        _selectAllButton.Enabled = false;
        _selectAllButton.Click += (_, _) => SelectAllSyncable();
        _clearButton = Ui.Ghost("清空");
        _clearButton.Enabled = false;
        _clearButton.Click += (_, _) => ClearSelection();
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
        _actionKeyword.PlaceholderText = ListFilter.PlanActionSearchPlaceholder;
        _actionKeyword.TextChanged += (_, _) => PopulateActions();

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
        // 筛选行取工厂（第六十八轮立的形状）：这一页只有关键词框、没有筛选下拉，故走 counts 可缺省的那一支，
        // 前两列留空、关键词框仍在第 4 列、末列吸收剩余宽度——不为它另起一种排布。
        // 底色随本页（Bg）：工厂给的是对话框的 Panel 底，这一页是页面底。
        var filterRow = Ui.FilterRow(_keyword, _visibleCount);
        filterRow.BackColor = UiTokens.Bg;
        filterRow.Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm);
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_selectList));
        listHost.Controls.Add(_selectHint);
        _selectHint.Visible = false;
        // 停靠按集合倒序生效：说明行最先停靠（顶部），筛选行其次，清单填满剩余空间。
        page.Controls.Add(listHost);
        page.Controls.Add(filterRow);
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
        actionHost.Controls.Add(_actionHint);
        _actionHint.Visible = false;
        actionHost.Controls.Add(actionCaption);
        // 筛选行取工厂（第六十八轮立的形状）：这一屏只有关键词框、没有筛选下拉，计数在表头行，故 counts 传 null。
        // 底色随本页（Bg）：工厂给的是对话框的 Panel 底，这一页是页面底。
        var actionFilterRow = Ui.FilterRow(_actionKeyword, counts: null);
        actionFilterRow.BackColor = UiTokens.Bg;
        actionFilterRow.Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm);

        var payloadInner = Ui.Hairline(_payload);
        payloadInner.Dock = DockStyle.Fill;
        payloadInner.Height = 160;
        var payloadHost = BuildBottomHost(payloadInner, "选中动作的写入内容（敏感值已脱敏；与执行时写入的 JSON 一致）", 190);
        var notesInner = Ui.Hairline(_notes);
        notesInner.Dock = DockStyle.Fill;
        notesInner.Height = 100;
        var notesHost = BuildBottomHost(notesInner, "共享模型对范围外接口的影响、计划提示与保留设置", 130);

        // 停靠按集合倒序生效：标题区最先停靠（顶部）、筛选行紧随其后，写入内容、说明区、排除清单自下而上，
        // 写入清单填满剩余空间。
        page.Controls.Add(actionHost);
        page.Controls.Add(_excludedHost);
        page.Controls.Add(notesHost);
        page.Controls.Add(payloadHost);
        page.Controls.Add(actionFilterRow);
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
        ApplyFilter();
        _viewPlanButton.Enabled = File.Exists(_tasks.FilePath(_taskId, TaskFiles.Plan));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// 两页各自的三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给这一页的关键词框并选中
    /// 已有关键词、框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部行）。
    /// 与另十张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
    /// <see cref="Ui.TryEscapeFilter"/>）：本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
    /// 这一屏与那十张有一处不同——**两页各有一个关键词框**（勾选页筛接口、预览页筛写入清单，第八十轮）：
    /// 门控按当前可见页走，一次按键只有一个含义——预览页的 Esc 不会去清勾选页的关键词（反之亦然），
    /// 两页都不在时（构造期）一概不接，Ctrl+F 不吞键、Esc 仍是取消对话框。
    /// 勾选状态按接口键记在 <see cref="SyncSelect"/>，Esc 清空关键词触发的重建从它恢复，勾选不因退出筛选而丢。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_page1.Visible)
        {
            if (Ui.TryFocusFilter(keyData, _keyword)) return true;
            if (Ui.TryEnterFilter(keyData, _keyword, _selectList)) return true;
            if (Ui.TryEscapeFilter(keyData, _keyword)) return true;
        }
        else if (_page2.Visible)
        {
            if (Ui.TryFocusFilter(keyData, _actionKeyword)) return true;
            if (Ui.TryEnterFilter(keyData, _actionKeyword, _actionList)) return true;
            if (Ui.TryEscapeFilter(keyData, _actionKeyword)) return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    private void DisableSelect(string message, Color color)
    {
        _selectHint.Text = message;
        _selectHint.ForeColor = color;
        _selectHint.Visible = true;
        _selectList.Visible = false;
        // 整份数据不可得时两个筛选入口一起停：只停一个等于留一个按不动的入口。
        _keyword.Enabled = ListFilter.KeywordEnabled(false);
        _selectAllButton.Enabled = false;
        _clearButton.Enabled = false;
        _generateButton.Enabled = false;
    }

    /// <summary>
    /// 关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行、不改勾选——勾选状态按接口键记在
    /// <see cref="SyncSelect"/>（单一来源），清单重建时从它恢复。筛到零行不是「没有接口可同步」：
    /// 说清这一屏为什么空，下一步就在那个框上（这一页没有筛选下拉，故不指「换一个筛选」），
    /// 关键词框与两个批量按钮都留着（§7「禁用不得移除下一步」）。
    /// </summary>
    private void ApplyFilter()
    {
        if (_diff is null) return;
        _select.Query = _keyword.Text;
        PopulateSelectList();
        if (ListFilter.IsFilteredEmpty(_diff.Endpoints.Count, _selectList.Items.Count))
        {
            _selectHint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun);
            _selectHint.ForeColor = UiTokens.Muted;
            _selectHint.Visible = true;
            _selectList.Visible = false;
            return;
        }
        _selectHint.Visible = false;
        _selectList.Visible = true;
    }

    /// <summary>
    /// 按当前关键词重建清单：可见行取 <see cref="SyncSelect.Visible"/>（与五张长清单同一份匹配规则），
    /// 每行的勾选从 <see cref="SyncSelect"/> 恢复——重建不丢勾选，这是本页筛选的前提。
    /// 计数格按整份检查结果统计（不随关键词变化），按钮上的数字是可见行里的可同步数（与按钮实际勾的行同一批）。
    /// </summary>
    private void PopulateSelectList()
    {
        var diff = _diff!;
        _rebuilding = true;
        _selectList.BeginUpdate();
        try
        {
            _selectList.Items.Clear();
            foreach (var check in _select.Visible(diff))
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
                row.Checked = _select.IsChecked(check.Key);
                _selectList.Items.Add(row);
            }
        }
        finally
        {
            _selectList.EndUpdate();
            _rebuilding = false;
        }
        _selectAllButton.Text = "全选可同步（" + _select.VisibleSyncableCount(diff, _decisions, _task.OfflineMode) + "）";
        _selectAllButton.Enabled = true;
        _clearButton.Enabled = true;
        _visibleCount.Text = "接口 " + diff.Endpoints.Count + " · 可同步 "
            + SyncSelection.SyncableKeys(_task.OfflineMode, diff, _decisions).Count;
        RefreshSelectionCount();
    }

    /// <summary>把勾选状态从 <see cref="SyncSelect"/> 回写到可见行（行只是它的呈现，不另存一份）。</summary>
    private void ApplyChecks()
    {
        _rebuilding = true;
        try
        {
            foreach (ListViewItem item in _selectList.Items)
            {
                if (item.Tag is EndpointCheck check) item.Checked = _select.IsChecked(check.Key);
            }
        }
        finally
        {
            _rebuilding = false;
        }
        RefreshSelectionCount();
    }

    /// <summary>「清空」：按全部清（按钮说的是清空，不是清空可见——留着看不见的勾选，点完清空计数还显示已选择）。</summary>
    private void ClearSelection()
    {
        _select.Clear();
        ApplyChecks();
    }

    /// <summary>「全选可同步」：只作用于可见行（屏幕上只有 3 行时点「全选」却勾上 40 个，用户看不见自己勾了什么）。</summary>
    private void SelectAllSyncable()
    {
        if (_diff is null) return;
        _select.SelectAllSyncable(_diff, _decisions, _task.OfflineMode);
        ApplyChecks();
    }

    private void RefreshSelectionCount()
    {
        var count = _select.CheckedCount;
        _selectedCount.Text = count > 0 ? "已选择 " + count + " 个接口" : "尚未勾选接口";
        if (!_busy) _generateButton.Enabled = count > 0;
    }

    private void Generate()
    {
        if (_diff is null || _busy) return;
        // 交出去的是 SyncSelect 里那一批（含被关键词筛掉的行）：筛选只改可见行，不改勾选。
        var keys = _select.CheckedKeys(_diff);
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
        _keyword.Enabled = ListFilter.KeywordEnabled(_diff is not null, busy);
        _actionKeyword.Enabled = ListFilter.KeywordEnabled(_plan is { Actions.Count: > 0 }, busy);
        _viewPlanButton.Enabled = !busy && File.Exists(_tasks.FilePath(_taskId, TaskFiles.Plan));
        _backButton.Enabled = !busy;
        _generateButton.Enabled = !busy && _select.CheckedCount > 0;
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
        // 计数行按整份计划统计、不随关键词变化（「排除 N」正是要找的那批的凭据）。
        _planCounts.Text = PlanEntries.CountsLine(endpoints, plan.Actions.Count - endpoints, plan.Excluded.Count);
        _planMeta.Text = "目标：" + (plan.ProjectName ?? "") + " 项目 " + plan.ProjectId + " / 分支 " + (plan.Branch ?? "main")
            + " · CLI " + plan.CliVersion + " · 计划指纹 " + (plan.Hash.Length > 12 ? plan.Hash[..12] : plan.Hash)
            + " · 生成于 " + plan.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        _stale.Text = "生成计划后任务内容有变化，这份计划不能再执行。请重新检查后重新生成同步预览。";
        _stale.Visible = outdated;
        _planMessage.Visible = plan.Message is not null;
        if (plan.Message is { } message) _planMessage.Text = message;

        // 写入清单按关键词呈现（第八十轮）：整份数据没有动作时框一起停（没有可筛的东西，下一步不在这框上）。
        _actionKeyword.Enabled = ListFilter.KeywordEnabled(plan.Actions.Count > 0);
        PopulateActions();

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

    /// <summary>
    /// 写入清单的关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行——计数行按整份计划统计、
    /// 不随关键词变化（「排除 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来）。
    /// 序号列仍是执行顺序里的第几步（不是可见行的第几行）：筛掉几条之后「第 7 步」还是第 7 步，
    /// 否则按序号核对执行日志就对不上了。
    /// 筛到零行不是「计划没有动作」：说清这一屏为什么空，下一步就在那个框上（这一屏没有筛选下拉，
    /// 故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」），写入内容预览经既有的无选中路径
    /// <see cref="ShowPayload"/> 收口，不在这里另写一套收控件的赋值。
    /// 选中按动作标识（<c>ActionId</c>）恢复：筛一下再筛回来仍停在原来那个动作，执行或核对后按磁盘计划重读
    /// 也仍停在原来那个动作（计划被重读成新对象，按对象引用恢复会落空）；恢复不到时落在新筛选第一条
    /// （与另十张长清单同一口径，仍经 <see cref="Ui.SelectItem"/> 唯一入口）。
    /// </summary>
    private void PopulateActions()
    {
        var plan = _plan;
        if (plan is null) return;
        var selectedId = (Ui.SelectedItem(_actionList)?.Tag as PlanAction)?.ActionId;
        var visible = ListFilter.Apply(
            plan.Actions.Select((action, index) => (action, index)),
            _actionKeyword.Text,
            row => PlanEntries.KeywordFields(
                TaskDisplay.PlanActionKindText(row.action.Kind),
                row.action.ResourceKey,
                PlanEntries.ChangesText(row.action.Changes, row.action.ManagedFields),
                TaskDisplay.PlanActionStateText(row.action.State)));
        _actionList.BeginUpdate();
        try
        {
            _actionList.Items.Clear();
            foreach (var (action, index) in visible)
            {
                var row = new ListViewItem((index + 1).ToString()) { Tag = action, UseItemStyleForSubItems = false };
                var kind = row.SubItems.Add(TaskDisplay.PlanActionKindText(action.Kind));
                kind.Font = UiTokens.Body.Value;
                var resource = row.SubItems.Add(action.ResourceKey);
                resource.Font = UiTokens.Mono.Value;
                var remoteId = row.SubItems.Add(action.RemoteId?.ToString() ?? "—");
                remoteId.Font = UiTokens.Mono.Value;
                var changes = row.SubItems.Add(PlanEntries.ChangesText(action.Changes, action.ManagedFields));
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
        }
        finally
        {
            _actionList.EndUpdate();
        }
        if (ListFilter.IsFilteredEmpty(plan.Actions.Count, visible.Count))
        {
            _actionHint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.ActionNoun);
            _actionHint.ForeColor = UiTokens.Muted;
            _actionHint.Visible = true;
            _actionList.Visible = false;
            ShowPayload();
            return;
        }
        _actionHint.Visible = false;
        _actionList.Visible = true;
        if (_actionList.Items.Count == 0)
        {
            // 整份计划没有动作：这一屏由表头与排除清单说话，不在这里另造一句话（也不索引不存在的第一行）。
            ShowPayload();
            return;
        }
        var target = selectedId is null
            ? null
            : _actionList.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as PlanAction)?.ActionId == selectedId);
        Ui.SelectItem(target ?? _actionList.Items[0]);
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
        if (Ui.SelectedItem(_actionList)?.Tag is not PlanAction action)
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
