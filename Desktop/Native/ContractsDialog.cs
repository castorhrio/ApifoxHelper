using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 响应契约审阅：通用响应封装中 data 的实际类型，逐接口确认 / 拒绝 / 重置。
/// 数据来自任务目录 responseContract.json（扫描时静态追踪落盘）；审阅经 ContractReviewService 的唯一校验口径
/// （阻断契约不能确认；确认/拒绝只写任务数据并递增任务版本），不写源码、不访问 Apifox。
/// 确认后的契约在后续 Apifox 检查中替换该接口的 200 响应体，不修改共享封装。
/// 状态表：读取失败=内联错误与下一步；没有契约=与冻结页面同一口径的说明；正常=计数行 + 筛选 + 列表 + 详情与审阅动作。
/// </summary>
internal sealed class ContractsDialog : Form
{
    private static readonly string[] FilterChoices = ["全部契约", "待确认", "已确认", "已拒绝", "已阻断", "已失效"];

    private readonly TaskStore _tasks;
    private readonly CheckTask _task;
    private readonly ContractReviewService _reviews;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ListView _list = Ui.List();
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly Button _confirmButton;
    private readonly Button _rejectButton;
    private readonly Button _resetButton;
    private readonly Button _evidenceButton;
    private readonly Label _status = Ui.StatusLine();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private List<ResponseContract> _contracts = [];

    public ContractsDialog(TaskStore tasks, CheckTask task, ContractReviewService reviews)
    {
        _tasks = tasks;
        _task = task;
        _reviews = reviews;

        Text = "响应契约 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("接口", 300);
        _list.Columns.Add("封装", 150);
        _list.Columns.Add("data 类型", 170);
        _list.Columns.Add("分支", 56);
        _list.Columns.Add("审阅状态", 90);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

        _confirmButton = Ui.Primary("确认");
        _confirmButton.Click += (_, _) => RunReview("confirmed");
        _rejectButton = Ui.Ghost("拒绝");
        _rejectButton.Click += (_, _) => RunReview("rejected");
        _resetButton = Ui.Ghost("重置");
        _resetButton.Click += (_, _) => RunReview("pending");
        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence();

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _filter.Items.AddRange(FilterChoices);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter();

        var filterRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 3, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        filterRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var filterCaption = Ui.Body("筛选");
        filterCaption.Margin = new Padding(0, 0, UiTokens.Xs, 0);
        filterCaption.TextAlign = ContentAlignment.MiddleLeft;
        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;
        filterRow.Controls.Add(filterCaption, 0, 0);
        filterRow.Controls.Add(_filter, 1, 0);
        filterRow.Controls.Add(_counts, 2, 0);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Sm) };
        detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCaptionRow(detailGrid, "选中契约的分支、未解析点与证据");
        AddBoxRow(detailGrid, Ui.Hairline(_detail), 190);
        AddCaptionRow(detailGrid, _status);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 6, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _rejectButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _resetButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _evidenceButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_confirmButton, 0, 0);
        actionRow.Controls.Add(_rejectButton, 1, 0);
        actionRow.Controls.Add(_resetButton, 2, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 3, 0);
        actionRow.Controls.Add(_evidenceButton, 4, 0);
        actionRow.Controls.Add(closeButton, 5, 0);

        // 停靠按集合倒序生效：筛选行最先停靠（顶部），其次按钮行（最底部）、详情区，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailGrid);
        Controls.Add(actionRow);
        Controls.Add(filterRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!ReloadContracts(out var error))
        {
            ShowHint("读取响应契约失败：" + error + "。请关闭后重新扫描再试。", UiTokens.BadText);
            return;
        }
        if (_contracts.Count == 0)
        {
            // 与冻结页面同一口径：没有需要补充的契约时说明原因与出路，不留白。
            ShowHint("没有需要补充的响应契约（未配置通用响应封装，或接口返回具体类型）。请在项目设置中填写「通用响应封装」，并重新扫描。", UiTokens.Muted);
            return;
        }
        _hint.Visible = false;
        _filter.Enabled = true;
        _list.Visible = true;
        UpdateCounts();
        ApplyFilter();
    }

    /// <summary>从任务目录重新读取契约清单；读取失败时返回 false 并带出脱敏错误。</summary>
    private bool ReloadContracts(out string error)
    {
        try
        {
            _contracts = _tasks.Read<List<ResponseContract>>(_task.Id, TaskFiles.ResponseContract) ?? [];
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            _contracts = [];
            error = Redactor.Redact(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 提示出口。<c>keepAggregate</c> 为真表示「契约还在，只是当前筛选一条都不命中」：筛选器要留着
    /// （提示语「换一个筛选再试」指的就是它），计数行的聚合数字也要留着（「待确认 0」是清完队列的凭据）。
    /// 缺省为假：打开期读取失败与「没有需要补充的响应契约」仍是原来的形状。
    /// </summary>
    private void ShowHint(string message, Color color, bool keepAggregate = false)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _filter.Enabled = keepAggregate;
        SetButtons(null);
        if (!keepAggregate) _counts.Text = "";
        _detail.Text = "";
    }

    /// <summary>计数行按全部契约统计（不随筛选变化），等宽数字不跳宽。</summary>
    private void UpdateCounts()
    {
        int pending = 0, confirmed = 0, rejected = 0, blocked = 0, stale = 0;
        foreach (var contract in _contracts)
        {
            switch (contract.ReviewState)
            {
                case "confirmed": confirmed++; break;
                case "rejected": rejected++; break;
                case "blocked": blocked++; break;
                case "stale": stale++; break;
                default: pending++; break;
            }
        }
        _counts.Text = $"契约 {_contracts.Count} · 待确认 {pending} · 已确认 {confirmed} · 已拒绝 {rejected} · 已阻断 {blocked} · 已失效 {stale}";
    }

    private bool Matches(ResponseContract contract) => _filter.SelectedIndex switch
    {
        1 => contract.ReviewState == "pending",
        2 => contract.ReviewState == "confirmed",
        3 => contract.ReviewState == "rejected",
        4 => contract.ReviewState == "blocked",
        5 => contract.ReviewState == "stale",
        _ => true,
    };

    private void ApplyFilter()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var contract in _contracts)
        {
            if (!Matches(contract)) continue;
            var item = new ListViewItem(contract.EndpointKey) { Tag = contract };
            var wrapper = item.SubItems.Add(contract.WrapperType.Length > 0 ? contract.WrapperType : "（直接返回）");
            wrapper.ForeColor = UiTokens.Muted;
            item.SubItems.Add(contract.DataType ?? "未确定");
            item.SubItems.Add(contract.Branches.Count.ToString());
            var review = item.SubItems.Add(TaskDisplay.ContractStateText(contract.ReviewState));
            item.UseItemStyleForSubItems = false;
            review.ForeColor = ToneColor(TaskDisplay.ContractTone(contract.ReviewState));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (ListFilter.IsFilteredEmpty(_contracts.Count, _list.Items.Count))
        {
            // 筛到零行不是「没有契约」：说清这一屏为什么空、下一步就在筛选上，聚合计数留着（口径见 ListFilter）。
            ShowHint(ListFilter.FilteredEmptyHint(ListFilter.ContractNoun), UiTokens.Muted, keepAggregate: true);
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        // 重建之后再问一次详情：本视图不自动选中任何一行，判在清空之前量到的是上一轮筛选的行数，
        // 「缺什么」那句话会在该说话的时候沉默（第一次进屏时清单还是空的）。
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (Ui.SelectedItem(_list)?.Tag is not ResponseContract contract)
        {
            // 有行可选却没选中：缺什么由详情区说；一行都没有时空的是筛选，那一屏归提示出口（口径见 ListSelection）。
            _detail.Text = ListSelection.ShouldExplainMissingSelection(_list.Items.Count)
                ? ListSelection.MissingSelectionHint(ListFilter.ContractNoun)
                : "";
            SetButtons(null);
            return;
        }
        var lines = new List<string>
        {
            "接口：" + contract.EndpointKey,
            "封装 " + (contract.WrapperType.Length > 0 ? contract.WrapperType : "（直接返回）")
                + " · " + (contract.DataJsonName.Length > 0 ? contract.DataJsonName : "data") + " = "
                + (contract.DataType ?? "未确定") + (contract.DataNullable ? "（异常分支为空）" : ""),
        };
        if (contract.UnresolvedPoints.Count > 0)
        {
            lines.Add("未解析点（阻断确认）：");
            for (var i = 0; i < contract.UnresolvedPoints.Count; i++)
            {
                lines.Add((i + 1) + ". " + contract.UnresolvedPoints[i]);
            }
        }
        if (contract.Branches.Count > 0)
        {
            lines.Add("分支：");
            foreach (var branch in contract.Branches)
            {
                var data = TaskDisplay.ContractDataKindText(branch.DataKind)
                    + (branch.DataKind == "type" && branch.DataType is { } type ? " " + type : branch.Note is { } note ? "（" + note + "）" : "");
                lines.Add("· " + branch.BranchKey + "：HTTP " + (branch.HttpStatus?.ToString() ?? "200（直接返回）")
                    + " · 条件 " + (branch.Condition.Length > 0 ? branch.Condition : "—")
                    + " · 业务状态 " + (branch.BusinessStatus ?? "—")
                    + " · data " + data
                    + " · " + branch.File + ":" + branch.Line);
            }
        }
        if (contract.ReviewState == "stale") lines.Add("源码已变化，之前的审阅结论已失效，请重新对照分支与证据确认。");
        lines.Add("审阅状态：" + TaskDisplay.ContractStateText(contract.ReviewState)
            + (contract.ReviewedAt is { } at ? " · 审阅于 " + at.LocalDateTime.ToString("MM-dd HH:mm") : ""));
        lines.Add("引用证据：" + (contract.EvidenceIds.Count > 0 ? string.Join("、", contract.EvidenceIds) : "无"));
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
        SetButtons(contract);
    }

    /// <summary>审阅动作的可用性与冻结页面一致：阻断不能确认；已确认不能再确认；已拒绝不能再拒绝；非待确认/阻断可重置。</summary>
    private void SetButtons(ResponseContract? contract)
    {
        var state = contract?.ReviewState;
        _confirmButton.Enabled = state is "pending" or "rejected" or "stale";
        _rejectButton.Enabled = state is not ("rejected" or null);
        _resetButton.Enabled = state is "confirmed" or "rejected" or "stale";
        _evidenceButton.Enabled = contract is { EvidenceIds.Count: > 0 };
    }

    private async void RunReview(string reviewState)
    {
        if (Ui.SelectedItem(_list)?.Tag is not ResponseContract contract) return;
        var endpointKey = contract.EndpointKey;
        // 被处理那一行在旧清单上的显示序号：审阅后它可能不再属于当前筛选而消失，续接选中要用（ListAdvance 口径）。
        var handledAt = Ui.SelectedIndex(_list) ?? ListAdvance.None;
        SetButtons(null);
        try
        {
            await _reviews.ReviewAsync(_task.Id, endpointKey, reviewState, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.WarnText);
            SetButtons(contract);
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("保存审阅结果失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            SetButtons(contract);
            return;
        }
        if (IsDisposed) return;
        if (!ReloadContracts(out var error))
        {
            // 写入已成功，重新读取失败时如实说明，不显示成审阅失败。
            SetStatus("审阅结果已保存，但重新读取契约失败：" + error, UiTokens.BadText);
            return;
        }
        UpdateCounts();
        ApplyFilter();
        if (!SelectContract(endpointKey)) Ui.AdvanceAfterRemoval(_list, handledAt);
        // 与候选审阅同一纪律：保存是点按钮完成的，交回键盘之后方向键才接着处理下一条；
        // 被拒与重新读取失败的路径都在上面返回，不抢用户正在改的那一格。
        Ui.KeepKeyboardOnQueue(_list);
        SetStatus(TaskDisplay.ContractStateText(reviewState) + "。剩余待确认 " + _contracts.Count(c => c.ReviewState == "pending") + " 个。确认或拒绝后需重新检查 Apifox 差异才会生效。", UiTokens.OkText);
    }

    /// <summary>重建后回到刚审阅的那一条；返回它是否仍在当前筛选的清单上（不在时由调用方续接下一行）。</summary>
    private bool SelectContract(string endpointKey)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is ResponseContract contract && contract.EndpointKey == endpointKey)
            {
                Ui.SelectItem(item);
                return true;
            }
        }
        return false;
    }

    /// <summary>打开所选契约关联的代码证据（只读）：返回表达式与调用链是 data 类型的依据。</summary>
    private void ShowEvidence()
    {
        if (Ui.SelectedItem(_list)?.Tag is not ResponseContract contract) return;
        using var dialog = new EvidenceDialog(_tasks, _task, contract.EndpointKey, contract.EndpointKey, contract.EvidenceIds);
        dialog.ShowDialog(this);
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }

    private static Color ToneColor(string tone) => tone switch
    {
        "ok" => UiTokens.OkText,
        "warn" => UiTokens.WarnText,
        "bad" => UiTokens.BadText,
        "muted" => UiTokens.Muted,
        _ => UiTokens.Text,
    };

    private static void AddCaptionRow(TableLayoutPanel grid, Control control)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(control.Margin.Left, control.Margin.Top, 0, UiTokens.Xs);
        grid.Controls.Add(control, 0, row);
    }

    private static void AddCaptionRow(TableLayoutPanel grid, string text) =>
        AddCaptionRow(grid, new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTokens.CaptionBold.Value,
            ForeColor = UiTokens.Muted,
            Margin = new Padding(0),
        });

    /// <summary>文本框行用固定行高（行高随 DPI 缩放），内容经发丝边宿主填充单元格。</summary>
    private static void AddBoxRow(TableLayoutPanel grid, Control inner, int height)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        inner.Margin = new Padding(0, 0, 0, UiTokens.Xs);
        grid.Controls.Add(inner, 0, row);
    }
}
