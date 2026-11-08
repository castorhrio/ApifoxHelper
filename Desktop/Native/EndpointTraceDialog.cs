using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 接口追踪明细（只读）：逐接口显示扫描时调用链追踪的读取方法、未解析调用、外部调用与
/// 单接口方法预算是否超限——预算超限或调用未解析时，类型追踪与证据为什么不完整在这里可见
/// （与「分析警告与降级」同一透明口径，降级只有让用户看见才算「明确降级」）。
/// 数据来自任务目录 evidence.json 的 Coverage（扫描落盘产物）；证据经 EvidenceDialog 只读对照；
/// 本对话框无写入动作。文字与结构口径经 EndpointTrace 单一口径。
/// 状态表：读取失败=内联错误与下一步；没有追踪数据（旧任务缺 Coverage）=如实说明；
/// 正常=接口列表 + 选中接口的追踪明细与证据入口。
/// </summary>
internal sealed class EndpointTraceDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly CheckTask _task;

    private readonly ListView _list = Ui.List();
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly Button _evidenceButton;
    private readonly Label _counts = Ui.Mono();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private List<EndpointCoverage> _coverages = [];

    public EndpointTraceDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _task = task;

        Text = "接口追踪明细 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 640);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("接口", 280);
        _list.Columns.Add("读取方法", 90);
        _list.Columns.Add("未解析调用", 96);
        _list.Columns.Add("外部调用", 88);
        _list.Columns.Add("预算", 64);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence();
        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;
        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var hintCaption = Ui.Caption("调用链追踪按「方法预算」与「调用深度」收手；未解析的调用与超限的接口如实列出，不是扫描失败。");
        hintCaption.Dock = DockStyle.Fill;
        hintCaption.TextAlign = ContentAlignment.MiddleLeft;
        headRow.Controls.Add(hintCaption, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Sm) };
        detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCaptionRow(detailGrid, "选中接口的追踪明细：读取的方法、未解析的调用、外部调用与预算");
        AddBoxRow(detailGrid, Ui.Hairline(_detail), 190);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 3, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _evidenceButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_evidenceButton, 0, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 1, 0);
        actionRow.Controls.Add(closeButton, 2, 0);

        // 停靠按集合倒序生效：标题行最先停靠（顶部），其次按钮行（最底部）、详情区，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailGrid);
        Controls.Add(actionRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        EvidenceStore? store;
        try
        {
            store = _tasks.Read<EvidenceStore>(_task.Id, TaskFiles.Evidence);
        }
        catch (Exception ex)
        {
            ShowHint("读取追踪明细失败：" + Redactor.Redact(ex.Message) + "。请关闭后重新扫描再试。", UiTokens.BadText);
            return;
        }
        if (store is null || store.Coverage.Count == 0)
        {
            // Coverage 与证据同一次扫描写入；旧任务缺这段数据时如实说明出路，不留白。
            ShowHint("这份任务的扫描结果没有接口追踪数据。请在项目设置确认「方法预算」等分析设置后重新扫描。", UiTokens.Muted);
            return;
        }
        _coverages = store.Coverage;
        _hint.Visible = false;
        _counts.Text = $"接口 {_coverages.Count} · 超限 {_coverages.Count(c => c.BudgetExceeded)} · 未解析调用 {_coverages.Sum(c => c.Unresolved.Count)}";
        _list.BeginUpdate();
        foreach (var coverage in _coverages)
        {
            var row = new ListViewItem(coverage.EndpointKey) { Tag = coverage, UseItemStyleForSubItems = false };
            row.Font = UiTokens.Mono.Value;
            row.SubItems.Add(EndpointTrace.CountText(coverage.MethodsRead.Count)).Font = UiTokens.Body.Value;
            var unresolved = row.SubItems.Add(EndpointTrace.CountText(coverage.Unresolved.Count));
            unresolved.Font = UiTokens.Body.Value;
            if (coverage.Unresolved.Count > 0) unresolved.ForeColor = UiTokens.WarnText;
            row.SubItems.Add(EndpointTrace.CountText(coverage.ExternalCalls.Count)).Font = UiTokens.Body.Value;
            var (budgetText, budgetTone) = EndpointTrace.BudgetCell(coverage.BudgetExceeded);
            var budget = row.SubItems.Add(budgetText);
            budget.Font = UiTokens.Body.Value;
            budget.ForeColor = budgetTone switch
            {
                "warn" => UiTokens.WarnText,
                "muted" => UiTokens.Muted,
                _ => UiTokens.Text,
            };
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        if (_list.Items.Count > 0) Ui.SelectItem(_list.Items[0]);
    }

    private void ShowDetail()
    {
        if (Ui.SelectedItem(_list)?.Tag is not EndpointCoverage coverage)
        {
            // 有行可选却没选中：缺什么由详情区说；一行都没有时空的是整份追踪数据，那一屏归提示出口（口径见 ListSelection）。
            _detail.Text = ListSelection.ShouldExplainMissingSelection(_list.Items.Count)
                ? ListSelection.MissingSelectionHint(ListFilter.EndpointNoun)
                : "";
            _evidenceButton.Enabled = false;
            return;
        }
        var lines = EndpointTrace.DetailLines(
            coverage.EndpointKey,
            coverage.MethodsRead,
            coverage.Unresolved.Select(u => new EndpointTrace.UnresolvedLine(u.Expression, u.Reason, u.Candidates)).ToList(),
            coverage.ExternalCalls,
            coverage.BudgetExceeded,
            coverage.EvidenceIds.Count);
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
        // 行内门控与按钮同值：该行有证据才可打开（清空选中时禁用，不残留上一行的状态）。
        _evidenceButton.Enabled = coverage.EvidenceIds.Count > 0;
    }

    private void ShowEvidence()
    {
        if (Ui.SelectedItem(_list)?.Tag is not EndpointCoverage coverage) return;
        if (coverage.EvidenceIds.Count == 0) return;
        using var dialog = new EvidenceDialog(_tasks, _task, coverage.EndpointKey, coverage.EndpointKey, coverage.EvidenceIds);
        dialog.ShowDialog(this);
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _evidenceButton.Enabled = false;
        _counts.Text = "";
        _detail.Text = "";
    }

    private static void AddCaptionRow(TableLayoutPanel grid, string text)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label
        {
            Text = text,
            AutoSize = true,
            Font = UiTokens.CaptionBold.Value,
            ForeColor = UiTokens.Muted,
            Margin = new Padding(0),
        };
        grid.Controls.Add(caption, 0, row);
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
}
