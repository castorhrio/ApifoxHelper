using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 代码证据（只读）：展示一个注释位置关联的代码证据——分类、摘要、文件与行号、代码片段。
/// 数据来自任务目录的 evidence.json（扫描产物），按缺口条目的证据编号过滤；无任何写入动作。
/// 审阅注释候选前先看这里的代码证据：没有证据的业务含义（单位、权限、默认值）不编造。
/// 状态表：读取失败=内联错误 + 屏上那颗「重试」（第三百零九轮接上，该位置无证据那一种不点亮它）；
/// 该位置无证据=如实说明；正常=计数行 + 证据列表 + 片段详情
/// （打开即选中第一条证据，片段区随之显示它的代码——这一屏存在的理由就是看代码）。
/// 清单三格文字、计数行、片段区说明行与片段文字取 <see cref="EvidenceEntries"/> 一份（与测试工程单一口径）。
/// </summary>
internal sealed class EvidenceDialog : DpiAwareForm
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

    /// <summary>
    /// 读取失败态的唯一行动按钮（第三百零九轮）：文字取自 <see cref="LoadFailureState.RetryLabel"/>，
    /// 点下去调的是与「打开」同一个 <see cref="LoadAll"/>，两条路不各读一份。
    /// </summary>
    private readonly Button _retry = Ui.Ghost(LoadFailureState.RetryLabel);

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
        // 「文件:行」与注释缺口那一格是同一族内容，宽度取同一处口径。
        _list.Columns.Add("文件:行", ListColumnWidths.FileLine);
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

        // 读取失败态的唯一行动入口（第三百零九轮）：与「打开」调同一个 LoadAll，两条路不各读一份。
        _retry.Click += (_, _) => LoadAll();
        _retry.Visible = false;

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        // 「重试」落在「关闭」左边（RightToLeft 流向里后加的在左）：这一屏没有第二颗「重试」。
        _retry.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        buttonRow.Controls.Add(_retry);

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
        LoadAll();
    }

    /// <summary>
    /// 读代码证据并呈现。<b>「打开」与那颗「重试」调的都是这一个入口</b>（第三百零九轮）：
    /// 两条路各读一份，失败态与正常态就会各自漂移。
    /// <para>
    /// 这一屏此前读取失败只给一句「…再试」而屏上没有一颗按钮做「再读一次」这件事，与报告 / 差异确认
    /// （第一百五十二轮）、任务日志（第一百五十七轮）、注释候选 / 补充说明 / 注释回写（第三百零三轮）、
    /// 注释缺口（第三百零五轮）、检查结果（第三百零八轮）修掉的是同一族缺陷，
    /// 违反 DESIGN.md §7「禁用不得移除下一步」。
    /// </para>
    /// <para>
    /// <b>那一句文案没有抄第二份</b>：标题与说明都从口径文件的 <see cref="LoadFailureState.Evidence"/> 交出，
    /// 说明那一句本身就是已有共用落点 <see cref="LoadFailureState.DirectoryHint"/>（证据就落在任务目录里）。
    /// <b>「该位置没有直接关联的证据」不是读取失败</b>：那一种的下一步不在这条清单的再读一次上，
    /// 所以那句空态不点亮那颗按钮（同一取舍见注释候选、检查结果两屏）。
    /// </para>
    /// <para>
    /// <b>为什么这里不预先清一次清单</b>：那颗按钮在读取成功那一刻就收起，失败那一支不经清单填充、
    /// 由提示出口把清单收起并清空计数与片段——同一个入口被走第二遍时上一遍必然是失败的、屏上没有行。
    /// 「重读把上一遍的行叠上去」在这一屏今天走不到，不留三行替它保险（第三百零九轮按注入实测撤掉的正是这一处）。
    /// </para>
    /// </summary>
    private void LoadAll()
    {
        // 每读一次先收起那颗按钮：成功路径不经 ShowHint（那里是唯一的收起处），不先收就会把失败态那颗
        // 「重试」留在已经正常的屏上——与注释候选、注释缺口、检查结果同一形状。
        _retry.Visible = false;
        EvidenceStore? store;
        try
        {
            store = _tasks.Read<EvidenceStore>(_taskId, TaskFiles.Evidence);
        }
        catch (Exception ex)
        {
            ShowHint(
                LoadFailureState.FailureMessage(LoadFailureState.Evidence.Title, Redactor.Redact(ex.Message), LoadFailureState.Evidence.Hint),
                UiTokens.BadText,
                canRetry: true);
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
        _list.Visible = true;
        _counts.Text = EvidenceEntries.CountsLine(items.Count);
        foreach (var item in items)
        {
            // 三格文字取 EvidenceEntries 一份（分类映射、摘要、文件:行），片段区与计数行同源——
            // 这一屏的清单与片段讲的是同一条证据，各拼一遍必然有一处先漂。
            var cells = EvidenceEntries.Row(item.Kind, item.Summary, item.File, item.StartLine, item.EndLine);
            var row = new ListViewItem(cells[0]) { Tag = item };
            row.SubItems.Add(cells[1]);
            var location = row.SubItems.Add(cells[2]);
            Ui.IdentityCell(location);
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

    /// <summary>
    /// 提示出口。<c>canRetry</c> 为真只有一种情形：读取失败。缺省为假——「该位置没有直接关联的证据」那一种
    /// 空态的下一步不在「再读一次」上，那颗按钮因此不亮（第三百零九轮，与注释候选、检查结果同一取舍）。
    /// 这一出口对提示态通用，也是<b>唯一的</b>收起处，所以读取入口开头还要自己收一次（成功路径不经这里）。
    /// </summary>
    private void ShowHint(string message, Color color, bool canRetry = false)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _counts.Text = "";
        _snippet.Text = "";
        _retry.Visible = canRetry;
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
