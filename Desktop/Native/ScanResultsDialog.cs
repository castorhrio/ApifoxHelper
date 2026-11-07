using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释缺口清单（只读）：展示扫描得到的全部注释位置（DocSlot），按状态筛选，逐条显示现状、覆盖接口与证据数；
/// 选中条目可打开其代码证据（EvidenceDialog，同样只读）。
/// 数据来自任务目录的 symbols.json（扫描产物），无任何写入动作；审阅与回写在后续切片接入。
/// 状态表：无扫描产物=引导先扫描；读取失败=内联错误与下一步；正常=计数行 + 列表（空列表即全部有效）。
/// </summary>
internal sealed class ScanResultsDialog : Form
{
    private static readonly string[] FilterChoices = ["全部位置", "缺失", "空", "已失效", "有效", "其他问题"];

    private readonly TaskStore _tasks;
    private readonly CheckTask _task;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = UiTokens.Panel };
    private readonly Button _evidenceButton;
    private readonly TextBox _detail = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Caption.Value,
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private List<DocSlot> _slots = [];

    public ScanResultsDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _task = task;

        Text = "注释缺口 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 640);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("位置（说明点）", 360);
        _list.Columns.Add("标注", 60);
        _list.Columns.Add("状态", 110);
        _list.Columns.Add("角色", 60);
        _list.Columns.Add("文件:行", 320);
        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence();
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _list.DoubleClick += (_, _) =>
        {
            if (_evidenceButton.Enabled) ShowEvidence();
        };

        _filter.Items.AddRange(FilterChoices);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter();

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

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

        var detailHost = Ui.Hairline(_detail);
        detailHost.Dock = DockStyle.Bottom;
        detailHost.Height = 96;

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(_evidenceButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次详情框、筛选行，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailHost);
        Controls.Add(buttonRow);
        Controls.Add(filterRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ScanOutput? output;
        try
        {
            output = _tasks.Read<ScanOutput>(_task.Id, TaskFiles.Symbols);
        }
        catch (Exception ex)
        {
            ShowHint("读取扫描结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后重新扫描再试。", UiTokens.BadText);
            return;
        }
        if (output is null)
        {
            ShowHint("没有扫描结果。请先在任务里运行源码扫描，再查看注释缺口。", UiTokens.Muted);
            return;
        }
        _slots = output.Slots;
        _hint.Visible = false;
        _filter.Enabled = true;
        _list.Visible = true;
        UpdateCounts();
        ApplyFilter();
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _filter.Enabled = false;
        _evidenceButton.Enabled = false;
        _counts.Text = "";
        _detail.Text = "";
    }

    /// <summary>计数行按全部位置统计（不随筛选变化），等宽数字不跳宽。</summary>
    private void UpdateCounts()
    {
        int valid = 0, missing = 0, empty = 0, stale = 0, other = 0;
        foreach (var slot in _slots)
        {
            switch (slot.Status)
            {
                case "valid": valid++; break;
                case "missing": missing++; break;
                case "empty": empty++; break;
                case "stale": stale++; break;
                default: other++; break;
            }
        }
        _counts.Text = _slots.Count == 0
            ? "没有注释位置"
            : $"位置 {_slots.Count} · 有效 {valid} · 缺失 {missing} · 空 {empty} · 失效 {stale} · 其他 {other}";
    }

    private bool Matches(DocSlot slot) => _filter.SelectedIndex switch
    {
        1 => slot.Status == "missing",
        2 => slot.Status == "empty",
        3 => slot.Status == "stale",
        4 => slot.Status == "valid",
        5 => slot.Status is "inheritDoc" or "inheritDocUnresolved" or "invalidXml" or "unsupported",
        _ => true,
    };

    private void ApplyFilter()
    {
        ShowDetail();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var slot in _slots)
        {
            if (!Matches(slot)) continue;
            var item = new ListViewItem(string.IsNullOrWhiteSpace(slot.Display) ? slot.SymbolId : slot.Display) { Tag = slot };
            item.SubItems.Add(TaskDisplay.SlotTagText(slot.Tag) + (slot.Tag == "param" && slot.ParameterName is { } name ? " " + name : ""));
            var status = item.SubItems.Add(TaskDisplay.SlotStatusText(slot.Status));
            item.UseItemStyleForSubItems = false;
            status.ForeColor = ToneColor(TaskDisplay.SlotTone(slot.Status));
            item.SubItems.Add(TaskDisplay.SlotRoleText(slot.Role));
            var location = item.SubItems.Add(slot.File + ":" + slot.Line);
            location.Font = UiTokens.Mono.Value;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
    }

    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0)
        {
            _detail.Text = "";
            _evidenceButton.Enabled = false;
            return;
        }
        if (_list.SelectedItems[0].Tag is not DocSlot slot) return;
        _evidenceButton.Enabled = slot.EvidenceIds.Count > 0;
        var lines = new List<string>
        {
            "符号：" + slot.Display + "（" + TaskDisplay.SymbolKindText(slot.SymbolKind) + "）",
            "现状：" + (string.IsNullOrWhiteSpace(slot.ExistingText) ? "（无说明）" : slot.ExistingText),
            "覆盖接口：" + (slot.EndpointKeys.Count > 0 ? string.Join("、", slot.EndpointKeys) : "—"),
            "证据：" + slot.EvidenceIds.Count + " 条",
        };
        if (slot.Issues.Count > 0) lines.Add("问题：" + string.Join("；", slot.Issues));
        if (!slot.Editable && slot.NotEditableReason is { } reason) lines.Add("不可回写原因：" + reason);
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
    }

    /// <summary>打开所选位置的代码证据（只读）：数据来自扫描落盘的 evidence.json，无写入动作。</summary>
    private void ShowEvidence()
    {
        if (_list.SelectedItems[0]?.Tag is not DocSlot slot) return;
        using var dialog = new EvidenceDialog(_tasks, _task, slot);
        dialog.ShowDialog(this);
    }

    private static Color ToneColor(string tone) => tone switch
    {
        "ok" => UiTokens.OkText,
        "warn" => UiTokens.WarnText,
        "bad" => UiTokens.BadText,
        "muted" => UiTokens.Muted,
        _ => UiTokens.Text,
    };
}
