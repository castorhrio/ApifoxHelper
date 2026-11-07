using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 代码证据（只读）：展示一个注释位置关联的代码证据——分类、摘要、文件与行号、代码片段。
/// 数据来自任务目录的 evidence.json（扫描产物），按缺口条目的证据编号过滤；无任何写入动作。
/// 审阅注释候选前先看这里的代码证据：没有证据的业务含义（单位、权限、默认值）不编造。
/// 状态表：读取失败=内联错误与下一步；该位置无证据=如实说明；正常=计数行 + 证据列表 + 片段详情。
/// </summary>
internal sealed class EvidenceDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly string _taskId;
    private readonly string _title;
    private readonly string _location;
    private readonly IReadOnlyCollection<string> _evidenceIds;

    private readonly Label _locationLabel = Ui.Caption();
    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = UiTokens.Panel };
    private readonly TextBox _snippet = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Mono.Value,
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public EvidenceDialog(TaskStore tasks, CheckTask task, DocSlot slot)
        : this(tasks, task, string.IsNullOrWhiteSpace(slot.Display) ? slot.SymbolId : slot.Display, slot.File + ":" + slot.Line, slot.EvidenceIds)
    {
    }

    /// <summary>按证据编号集合打开（响应契约条目等没有注释位置的场景）。</summary>
    public EvidenceDialog(TaskStore tasks, CheckTask task, string title, string location, IReadOnlyCollection<string> evidenceIds)
    {
        _tasks = tasks;
        _taskId = task.Id;
        _title = title;
        _location = location;
        _evidenceIds = evidenceIds;

        Text = "代码证据 — " + title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 640);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("分类", 96);
        _list.Columns.Add("证据摘要", 470);
        _list.Columns.Add("文件:行", 330);
        _list.SelectedIndexChanged += (_, _) => ShowSnippet();

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;

        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _locationLabel.TextAlign = ContentAlignment.MiddleLeft;
        headRow.Controls.Add(_locationLabel, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var snippetHost = Ui.Hairline(_snippet);
        snippetHost.Dock = DockStyle.Bottom;
        snippetHost.Height = 200;

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次片段框、标题行，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(snippetHost);
        Controls.Add(buttonRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _locationLabel.Text = _location;
        _locationLabel.Font = UiTokens.Mono.Value;

        EvidenceStore? store;
        try
        {
            store = _tasks.Read<EvidenceStore>(_taskId, TaskFiles.Evidence);
        }
        catch (Exception ex)
        {
            ShowHint("读取代码证据失败：" + Redactor.Redact(ex.Message) + "。请关闭后重新扫描再试。", UiTokens.BadText);
            return;
        }
        var wanted = _evidenceIds.ToHashSet(StringComparer.Ordinal);
        var items = store?.Items.Where(item => wanted.Contains(item.Id)).ToList() ?? [];
        if (items.Count == 0)
        {
            // 与冻结页面同一口径：没有直接关联的证据时，不推断单位、权限或默认值。
            ShowHint("没有与该位置直接关联的代码证据。没有证据的业务含义（单位、权限、默认值）不写进文档，留待人工确认。", UiTokens.Muted);
            return;
        }
        _hint.Visible = false;
        _counts.Text = "证据 " + items.Count + " 条";
        foreach (var item in items)
        {
            var row = new ListViewItem(TaskDisplay.EvidenceKindText(item.Kind)) { Tag = item };
            row.SubItems.Add(item.Summary ?? "");
            var location = row.SubItems.Add(item.File + ":" + item.StartLine + "-" + item.EndLine);
            location.Font = UiTokens.Mono.Value;
            _list.Items.Add(row);
        }
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _counts.Text = "";
        _snippet.Text = "";
    }

    private void ShowSnippet()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not CodeEvidence item)
        {
            _snippet.Text = "";
            return;
        }
        _snippet.Text = string.IsNullOrWhiteSpace(item.Snippet) ? "（无代码片段）" : item.Snippet;
        _snippet.SelectionStart = 0;
        _snippet.SelectionLength = 0;
    }
}
