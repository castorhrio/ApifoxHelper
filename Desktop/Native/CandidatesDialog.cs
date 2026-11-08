using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释候选审阅：展示任务目录 comment-drafts.json 中的注释候选，逐条对照候选文字、待确认问题与代码证据，
/// 执行接受 / 搁置 / 驳回；并可导入编码助手结果（DraftService.ImportResultAsync，候选写任务数据）。
/// 审阅经 DraftService.UpdateAsync 走既有校验（占位文字、覆盖确认、待确认问题需先回答），
/// 只写任务数据，不写源码、不访问 Apifox；源码回写在「注释回写」进行。
/// 状态表：读取失败=内联错误与下一步；没有候选=引导各生成入口（仍可导入）；正常=计数行 + 列表 + 详情与审阅动作。
/// </summary>
internal sealed class CandidatesDialog : Form
{
    private static readonly string[] FilterChoices = ["全部候选", "待审阅", "已接受", "已搁置", "已驳回", "证据已变化"];

    private readonly DraftService _drafts;
    private readonly TaskStore _tasks;
    private readonly CheckTask _task;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ListView _list = Ui.List();
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly TextBox _answers = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _answersCaption = Ui.Caption();
    private readonly CheckBox _overwrite = new() { AutoSize = true, Text = "覆盖该位置已有的有效注释（需要改写时勾选）" };
    private readonly Button _acceptButton;
    private readonly Button _deferButton;
    private readonly Button _rejectButton;
    private readonly Button _importButton;
    private readonly Button _evidenceButton;
    private readonly Label _status = Ui.StatusLine();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private DraftStore _store = new();
    private Dictionary<string, DocSlot> _slotsById = new(StringComparer.Ordinal);
    private bool _answersDirty;

    public CandidatesDialog(DraftService drafts, TaskStore tasks, CheckTask task)
    {
        _drafts = drafts;
        _tasks = tasks;
        _task = task;

        Text = "注释候选 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("位置（说明点）", 340);
        _list.Columns.Add("标注", 70);
        _list.Columns.Add("依据", 110);
        _list.Columns.Add("审阅状态", 110);
        _list.Columns.Add("来源", 90);
        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence(_list.SelectedItems[0]?.Tag as CommentCandidate);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        // 双击=查看证据经共享入口：命中哪行看哪行的证据（行自身 Tag，不借当前选中），双击列头与空白区不动作。
        Ui.ActivateItem(_list, item => ShowEvidence(item.Tag as CommentCandidate));

        _acceptButton = Ui.Primary("接受");
        _acceptButton.Click += (_, _) => RunReview("accepted");
        _deferButton = Ui.Ghost("搁置");
        _deferButton.Click += (_, _) => RunReview("deferred");
        _rejectButton = Ui.Ghost("驳回");
        _rejectButton.Click += (_, _) => RunReview("rejected");
        _importButton = Ui.Ghost("导入编码助手结果");
        _importButton.Click += (_, _) => ImportResult();

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _answers.TextChanged += (_, _) => _answersDirty = true;

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
        AddCaptionRow(detailGrid, "候选说明");
        AddBoxRow(detailGrid, Ui.Hairline(_detail), 92);
        _answersCaption.Text = "待确认问题的回答";
        _answersCaption.Margin = new Padding(0, UiTokens.Sm, 0, 0);
        AddCaptionRow(detailGrid, _answersCaption);
        AddBoxRow(detailGrid, Ui.Field(_answers), 44);
        AddCaptionRow(detailGrid, _overwrite);
        AddCaptionRow(detailGrid, _status);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 7, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _deferButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _rejectButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _importButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_acceptButton, 0, 0);
        actionRow.Controls.Add(_deferButton, 1, 0);
        actionRow.Controls.Add(_rejectButton, 2, 0);
        actionRow.Controls.Add(_importButton, 3, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 4, 0);
        actionRow.Controls.Add(_evidenceButton, 5, 0);
        actionRow.Controls.Add(closeButton, 6, 0);

        // 停靠按集合倒序生效：筛选行最先停靠（顶部），其次按钮行（最底部）、详情区，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailGrid);
        Controls.Add(actionRow);
        Controls.Add(filterRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        DraftStore? store;
        try
        {
            store = _drafts.Load(_task.Id);
        }
        catch (Exception ex)
        {
            ShowHint("读取注释候选失败：" + Redactor.Redact(ex.Message) + "。请关闭后重试；仍失败时重新扫描再打开。", UiTokens.BadText);
            return;
        }
        if (store.Candidates.Count == 0)
        {
            var run = store.Runs.Count > 0 ? store.Runs[^1] : null;
            ShowHint(run is null
                ? "还没有注释候选。可以在任务页「规则提取候选」「模型生成候选」「导出材料」生成，或点「导入编码助手结果」导回，也可以在「查看注释缺口」里手工填写。"
                : "规则提取没有生成候选：约束类草稿只覆盖代码可证实的约束（校验、默认值、守卫、枚举显示名）；方法与类型的用途说明可点「导入编码助手结果」导回、在任务页「模型生成候选」，或在「查看注释缺口」里手工填写。",
                UiTokens.Muted);
            return;
        }
        try
        {
            LoadSlots();
        }
        catch (Exception ex)
        {
            ShowHint("读取扫描结果失败：" + Redactor.Redact(ex.Message) + "。请重新扫描后再审阅候选。", UiTokens.BadText);
            return;
        }
        _store = store;
        _hint.Visible = false;
        _filter.Enabled = true;
        _list.Visible = true;
        UpdateCounts();
        ApplyFilter();
    }

    private void LoadSlots()
    {
        var scan = _tasks.Read<ScanOutput>(_task.Id, TaskFiles.Symbols);
        _slotsById = scan?.Slots.ToDictionary(s => s.Id, StringComparer.Ordinal) ?? [];
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _filter.Enabled = false;
        SetButtons(null);
        _counts.Text = "";
        _detail.Text = "";
    }

    /// <summary>计数行按全部候选统计（不随筛选变化），等宽数字不跳宽。</summary>
    private void UpdateCounts()
    {
        int pending = 0, accepted = 0, deferred = 0, rejected = 0, stale = 0;
        foreach (var candidate in _store.Candidates)
        {
            switch (candidate.ReviewState)
            {
                case "accepted": accepted++; break;
                case "deferred": deferred++; break;
                case "rejected": rejected++; break;
                case "stale": stale++; break;
                default: pending++; break;
            }
        }
        _counts.Text = _store.Candidates.Count == 0
            ? "没有候选"
            : $"候选 {_store.Candidates.Count} · 待审阅 {pending} · 已接受 {accepted} · 已搁置 {deferred} · 已驳回 {rejected} · 证据已变化 {stale}";
    }

    private bool Matches(CommentCandidate candidate) => _filter.SelectedIndex switch
    {
        1 => candidate.ReviewState == "pending",
        2 => candidate.ReviewState == "accepted",
        3 => candidate.ReviewState == "deferred",
        4 => candidate.ReviewState == "rejected",
        5 => candidate.ReviewState == "stale",
        _ => true,
    };

    private void ApplyFilter()
    {
        ShowDetail();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var candidate in _store.Candidates)
        {
            if (!Matches(candidate)) continue;
            var item = new ListViewItem(SlotTitle(candidate)) { Tag = candidate };
            item.SubItems.Add(TaskDisplay.SlotTagText(candidate.Tag) + (candidate.ParameterName is { } name ? " " + name : ""));
            item.SubItems.Add(TaskDisplay.CandidateBasisText(candidate.Basis));
            var review = item.SubItems.Add(TaskDisplay.ReviewStateText(candidate.ReviewState));
            item.UseItemStyleForSubItems = false;
            review.ForeColor = ToneColor(TaskDisplay.ReviewTone(candidate.ReviewState));
            item.SubItems.Add(TaskDisplay.CandidateSourceText(candidate.Source));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    /// <summary>列表里的位置标题：注释位置还在时用其展示名，不在时如实显示符号编号。</summary>
    private string SlotTitle(CommentCandidate candidate) =>
        _slotsById.GetValueOrDefault(candidate.SlotId) is { } slot
            ? (string.IsNullOrWhiteSpace(slot.Display) ? slot.SymbolId : slot.Display)
            : candidate.SymbolId;

    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not CommentCandidate candidate)
        {
            _detail.Text = "";
            _answers.Text = "";
            _answersDirty = false;
            _overwrite.Checked = false;
            _evidenceButton.Enabled = false;
            SetButtons(null);
            return;
        }
        var slot = _slotsById.GetValueOrDefault(candidate.SlotId);
        var lines = new List<string>
        {
            "符号：" + SlotTitle(candidate) + (slot is null ? "（该注释位置已不在本次扫描结果中，请重新扫描后再审阅）" : ""),
            "候选：" + candidate.Text,
        };
        if (candidate.Warnings.Count > 0) lines.Add("提示：" + string.Join("；", candidate.Warnings));
        if (candidate.OpenQuestions.Count > 0)
        {
            lines.Add("待确认问题：");
            for (var i = 0; i < candidate.OpenQuestions.Count; i++)
            {
                lines.Add((i + 1) + ". " + candidate.OpenQuestions[i]);
            }
        }
        lines.Add("依据：" + TaskDisplay.CandidateBasisText(candidate.Basis)
            + " · 来源：" + TaskDisplay.CandidateSourceText(candidate.Source)
            + " · 生成于 " + candidate.CreatedAt.LocalDateTime.ToString("MM-dd HH:mm"));
        if (candidate.ReviewState == "stale") lines.Add("该候选审阅后代码发生了变化，结论需要重新对照证据确认。");
        lines.Add("引用证据：" + (candidate.EvidenceIds.Count > 0 ? string.Join("、", candidate.EvidenceIds) : "无（没有证据的结论不作为事实写入）"));
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;

        _answers.Text = candidate.Answers ?? "";
        _answersDirty = false;
        _answers.Enabled = candidate.OpenQuestions.Count > 0;
        _answersCaption.Text = candidate.OpenQuestions.Count > 0
            ? "待确认问题的回答（接受前需逐条确认）"
            : "待确认问题的回答（该候选没有待确认问题）";

        _overwrite.Enabled = slot is { Status: "valid" } && candidate.Action == "set";
        _overwrite.Checked = candidate.OverwriteExisting;
        _evidenceButton.Enabled = slot is { EvidenceIds.Count: > 0 };
        SetButtons(candidate);
    }

    /// <summary>审阅动作只在注释位置仍在扫描结果中时可用（服务的审阅校验按位置执行）。</summary>
    private void SetButtons(CommentCandidate? candidate)
    {
        var enabled = candidate is not null && _slotsById.ContainsKey(candidate.SlotId);
        _acceptButton.Enabled = enabled;
        _deferButton.Enabled = enabled;
        _rejectButton.Enabled = enabled;
    }

    private async void RunReview(string reviewState)
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not CommentCandidate candidate) return;
        // 被处理那一行在旧清单上的显示序号：保存后它可能不再属于当前筛选而消失，续接选中要用（ListAdvance 口径）。
        var handledAt = _list.SelectedIndices[0];
        SetButtons(null);
        try
        {
            var update = new CandidateUpdateRequest { ReviewState = reviewState };
            if (reviewState == "accepted")
            {
                if (_answersDirty) update.Answers = _answers.Text;
                if (_overwrite.Enabled) update.OverwriteExisting = _overwrite.Checked;
            }
            await _drafts.UpdateAsync(_task.Id, candidate.Id, update, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.WarnText);
            SetButtons(candidate);
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("保存审阅结果失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            SetButtons(candidate);
            return;
        }
        if (IsDisposed) return;
        try
        {
            _store = _drafts.Load(_task.Id);
        }
        catch (Exception ex)
        {
            // 写入已成功，重新读取失败时如实说明，不显示成审阅失败。
            SetStatus("审阅结果已保存，但重新读取候选失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            SetButtons(candidate);
            return;
        }
        UpdateCounts();
        ApplyFilter();
        if (!SelectCandidate(candidate.Id)) Ui.AdvanceAfterRemoval(_list, handledAt);
        SetStatus(TaskDisplay.ReviewStateText(reviewState) + "。剩余待审阅 " + _store.Candidates.Count(c => c.ReviewState == "pending") + " 条。", UiTokens.OkText);
    }

    /// <summary>重建后回到刚处理的那一条；返回它是否仍在当前筛选的清单上（不在时由调用方续接下一行）。</summary>
    private bool SelectCandidate(string candidateId)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is CommentCandidate candidate && candidate.Id == candidateId)
            {
                Ui.SelectItem(item);
                return true;
            }
        }
        return false;
    }

    /// <summary>打开注释位置的代码证据（只读），先对照证据再做审阅决定；按钮与双击共用这一份行内门控。</summary>
    private void ShowEvidence(CommentCandidate? candidate)
    {
        if (candidate is null) return;
        if (_slotsById.GetValueOrDefault(candidate.SlotId) is not { EvidenceIds.Count: > 0 } slot) return;
        using var dialog = new EvidenceDialog(_tasks, _task, slot);
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// 导入编码助手结果：选择结果 JSON，经既有 DraftService.ImportResultAsync 校验并写入候选
    /// （slotId 不在本次扫描、占位文字等拒绝原因如实列出）；导入后刷新清单，逐条审阅。
    /// </summary>
    private async void ImportResult()
    {
        using var picker = new OpenFileDialog { Title = "导入编码助手结果", Filter = "结果 JSON|*.json", CheckFileExists = true };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _importButton.Enabled = false;
        string json;
        try
        {
            json = await File.ReadAllTextAsync(picker.FileName, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) SetStatus("读取结果文件失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            if (!IsDisposed) _importButton.Enabled = true;
            return;
        }
        DraftImportResult result;
        try
        {
            result = await _drafts.ImportResultAsync(_task.Id, json, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (!IsDisposed) SetStatus(ex.Message, UiTokens.WarnText);
            if (!IsDisposed) _importButton.Enabled = true;
            return;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) SetStatus("导入结果失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            if (!IsDisposed) _importButton.Enabled = true;
            return;
        }
        if (IsDisposed) return;
        try
        {
            _store = _drafts.Load(_task.Id);
        }
        catch (Exception ex)
        {
            // 导入已成功，重新读取失败时如实说明，不显示成导入失败。
            SetStatus("候选已导入，但重新读取候选失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            _importButton.Enabled = true;
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        _filter.Enabled = true;
        UpdateCounts();
        ApplyFilter();
        _importButton.Enabled = true;
        SetStatus(DraftEntries.ImportDoneText(result.Imported, result.Rejected, result.Errors),
            result.Rejected > 0 ? UiTokens.WarnText : UiTokens.OkText);
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

    /// <summary>文本框行用固定行高（行高随 DPI 缩放），内容经发丝边宿主填充单元格。</summary>
    private static void AddBoxRow(TableLayoutPanel grid, Control inner, int height)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        inner.Margin = new Padding(0, 0, 0, UiTokens.Xs);
        grid.Controls.Add(inner, 0, row);
    }

    private static Label AddCaptionRow(TableLayoutPanel grid, string text)
    {
        var caption = new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTokens.CaptionBold.Value,
            ForeColor = UiTokens.Muted,
            Margin = new Padding(0),
        };
        AddCaptionRow(grid, caption);
        return caption;
    }
}
