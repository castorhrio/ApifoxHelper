using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 代码证据（只读）：展示一个注释位置关联的代码证据——分类、摘要、文件与行号、代码片段。
/// 数据来自任务目录的 evidence.json（扫描产物），按缺口条目的证据编号过滤；无任何写入动作。
/// 审阅注释候选前先看这里的代码证据：没有证据的业务含义（单位、权限、默认值）不编造。
/// 状态表：读取失败=内联错误与下一步；该位置无证据=如实说明；正常=计数行 + 证据列表 + 片段详情
/// （打开即选中第一条证据，片段区随之显示它的代码——这一屏存在的理由就是看代码）。
/// 清单三格文字、计数行、片段区说明行与片段文字取 <see cref="EvidenceEntries"/> 一份（与测试工程单一口径）。
/// </summary>
internal sealed class EvidenceDialog : Form
{
    /// <summary>
    /// 标识列（DESIGN.md §6「列表行」的 Ctrl+C 条款）：首列是「分类」（路由、校验、守卫、查询过滤…），
    /// 许多行同值，复制它认不出是哪一条证据；这一行在屏幕上被读作的名字是「证据摘要」，故声明第 2 列。
    /// 「文件:行」是位置而不是名字（与差异确认「接口/模型」分类列、同步预览写入清单的动作序号同一取舍：
    /// 抄出去的是这一行在屏幕之外还能被指代的东西）。
    /// </summary>
    private const int EvidenceSummaryColumn = 1;

    private readonly TaskStore _tasks;
    private readonly string _taskId;
    private readonly string _title;
    private readonly string _location;
    private readonly IReadOnlyCollection<string> _evidenceIds;

    private readonly Label _locationLabel = Ui.Caption();
    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = Ui.List(identityColumn: EvidenceSummaryColumn);
    private readonly TextBox _snippet = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public EvidenceDialog(TaskStore tasks, CheckTask task, DocSlot slot)
        : this(tasks, task, SlotText.Name(slot), SlotText.Location(slot), slot.EvidenceIds)
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
        DpiScale.Apply(this, new Size(960, 640));
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

        // 片段区补说明行（与检查结果、核对传播、同步预览的底部区同一形状）：此前是一块没有标题的框，
        // 打开时又是空的，看不出这一格是什么、也看不出该点一行。
        var snippetCaption = Ui.Caption(EvidenceEntries.SnippetCaption);
        snippetCaption.Dock = DockStyle.Top;
        var snippetInner = Ui.Hairline(_snippet);
        snippetInner.Dock = DockStyle.Fill;
        snippetInner.Height = 180;
        var snippetHost = new Panel { Dock = DockStyle.Bottom, Height = 208, BackColor = UiTokens.Bg, Padding = new Padding(0, UiTokens.Sm, 0, 0) };
        snippetHost.Controls.Add(snippetInner);
        snippetHost.Controls.Add(snippetCaption);

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次片段区、标题行，列表最后填满剩余空间。
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
        _counts.Text = EvidenceEntries.CountsLine(items.Count);
        foreach (var item in items)
        {
            // 三格文字取 EvidenceEntries 一份（分类映射、摘要、文件:行），片段区与计数行同源——
            // 这一屏的清单与片段讲的是同一条证据，各拼一遍必然有一处先漂。
            var cells = EvidenceEntries.Row(item.Kind, item.Summary, item.File, item.StartLine, item.EndLine);
            var row = new ListViewItem(cells[0]) { Tag = item };
            row.SubItems.Add(cells[1]);
            var location = row.SubItems.Add(cells[2]);
            location.Font = UiTokens.Mono.Value;
            _list.Items.Add(row);
        }
        // 打开即选中第一条证据（与检查结果、核对传播、接口追踪明细三处只读清单同一口径）：
        // 此前打开是一张清单加一块空片段区，要看代码得先点一行——而这一屏存在的理由就是看代码，
        // 空着的那一格既不说自己是什么、也不说该点一行。选中经 Ui.SelectItem 唯一入口
        // （Selected + Focused + 滚入可见区），片段随选中事件显示，与鼠标点同一行同一条路。
        // 空清单在上面已经返回（没有可索引的第一行），这里不猜。
        Ui.SelectItem(_list.Items[0]);
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「只读清单打开时的键盘归属」，第八十八轮）：
    /// 这一屏打开即选中第一条证据（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
    /// 方向键动的是别的控件、Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 交键盘只在 <see cref="Ui.KeepKeyboardOnQueue"/> 一处（判定在 <see cref="ListFocus"/>：选中行与焦点行是同一行才交），
    /// 本对话框不自己写 .Focus()、不选中、不滚动、不重建清单。
    /// 放在 OnShown 而不是 OnLoad：OnLoad 时窗体尚未显示，Focus() 的落点取决于框架在激活时怎么选初始焦点；
    /// OnShown 是窗体首次显示之后，是设置初始焦点的常规时机（真机落点随 G-D5 复核）。
    /// 打开时那一屏是提示或空态（没有选中行）就不动键盘——那一步的下一步在提示与按钮上，不在清单上。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.KeepKeyboardOnQueue(_list);
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
        if (Ui.SelectedItem(_list)?.Tag is not CodeEvidence item)
        {
            _snippet.Text = "";
            return;
        }
        _snippet.Text = EvidenceEntries.SnippetText(item.Snippet);
        _snippet.SelectionStart = 0;
        _snippet.SelectionLength = 0;
    }
}
