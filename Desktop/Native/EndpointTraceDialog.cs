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
/// 正常=接口列表 + 选中接口的追踪明细与证据入口；筛到零行=关键词框留着并说明下一步，
/// 计数行仍是整份追踪数据的数字（见 <see cref="PopulateList"/>）。
/// 清单支持点击列头排序（第九十八轮，口径见 <see cref="EndpointTraceSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始、表头带方向标记；排序状态留在本次打开期间，关闭即忘。
/// </summary>
internal sealed class EndpointTraceDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly CheckTask _task;

    private readonly ListView _list = Ui.List();
    private readonly TextBox _keyword = Ui.Input();
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

    // 列排序状态（ListSort 口径，第九十八轮）：未排序时保持 evidence.json 里 Coverage 的自身顺序。
    // 排序状态留在本次打开期间（敲关键词、重新读取都保持），关闭即忘——
    // 跨启动记忆只有两个常驻页面（第五十五轮那两个页键），这一屏每次打开看的是这一次的扫描结果，
    // 不为它另开一个记忆文件。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _titles;

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
        DpiScale.Apply(this, new Size(920, 640));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("接口", 280);
        _list.Columns.Add("读取方法", 90);
        _list.Columns.Add("未解析调用", 96);
        _list.Columns.Add("外部调用", 88);
        _list.Columns.Add("预算", 64);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _keyword.PlaceholderText = ListFilter.TraceSearchPlaceholder;
        _keyword.TextChanged += (_, _) => PopulateList();

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

        // 筛选行形状取工厂（§6「列表筛选」）：这一屏没有筛选下拉，走只有关键词框的那一支；
        // 计数在表头行（与「调用链追踪按方法预算收手」的说明同排），故 counts 传 null。
        var filterRow = Ui.FilterRow(_keyword, counts: null);

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

        // 停靠按集合倒序生效：标题行、筛选行最先停靠（顶部），其次按钮行（最底部）、详情区，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailGrid);
        Controls.Add(actionRow);
        Controls.Add(filterRow);
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
        // 有数据可筛：关键词框可用（口径见 ListFilter.KeywordEnabled）。
        _keyword.Enabled = ListFilter.KeywordEnabled(true);
        _hint.Visible = false;
        // 计数行按整份追踪数据统计，不随关键词变化（「超限 N」正是要找的那批的凭据）。
        _counts.Text = $"接口 {_coverages.Count} · 超限 {_coverages.Count(c => c.BudgetExceeded)} · 未解析调用 {_coverages.Sum(c => c.Unresolved.Count)}";
        PopulateList();
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「只读清单打开时的键盘归属」，第八十八轮）：
    /// 这一屏打开即选中第一个接口（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
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
    /// 关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行——计数行按整份追踪数据统计、
    /// 不随关键词变化（「超限 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来）。
    /// 筛到零行不是「没有追踪数据」：说清这一屏为什么空，下一步就在那个框上（这一屏没有筛选下拉，
    /// 故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」），详情区与证据入口经既有的
    /// 无选中路径 <see cref="ShowDetail"/> 收口，不在这里另写一套收控件的赋值。
    /// 选中按接口身份恢复（筛一下再筛回来仍停在原来那个接口），恢复不到时落在新筛选第一条
    /// （与两个主页面「重建后按 Id 恢复选中」、第五十六轮「切换筛选落在新筛选第一条」同一口径）。
    /// </summary>
    private void PopulateList()
    {
        var selectedKey = (Ui.SelectedItem(_list)?.Tag as EndpointCoverage)?.EndpointKey;
        // 筛选先于排序（ListFilter → ListSort，与两个主页面、检查结果同一顺序）：筛掉的行不参与排序，
        // 排序状态与关键词各自留在本对话框；排序键取 EndpointTraceSort 单一口径（与单元格同源）。
        var visible = ListSort.Order(
            ListFilter.Apply(_coverages, _keyword.Text, c => EndpointTrace.KeywordFields(c.EndpointKey, c.BudgetExceeded)),
            _sortColumn, _sortDescending, EndpointTraceSort.Key);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var coverage in visible)
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
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_coverages.Count, visible.Count))
        {
            _hint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun);
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            ShowDetail();
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        var target = selectedKey is null
            ? null
            : _list.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as EndpointCoverage)?.EndpointKey == selectedKey);
        Ui.SelectItem(target ?? _list.Items[0]);
    }

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第九十八轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="PopulateList"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改扫描结果、选中仍按接口身份恢复（重建前记下、重建后找回）。
    /// </summary>
    private void SortBy(int column)
    {
        (_sortColumn, _sortDescending) = ListSort.Toggle(_sortColumn, _sortDescending, column);
        PopulateList();
    }

    /// <summary>表头方向标记：排序列追加方向标记，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSortHeaders()
    {
        for (var i = 0; i < _list.Columns.Count && i < _titles.Length; i++)
        {
            _list.Columns[i].Text = ListSort.Header(_titles[i], i, _sortColumn, _sortDescending);
        }
    }

    /// <summary>
    /// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给关键词框并选中已有关键词、
    /// 框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部接口）。
    /// 与另六张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
    /// <see cref="Ui.TryEscapeFilter"/>）：本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
    /// 对话框的 Enter=确认、Esc=取消在焦点不在框上时逐字不变（AcceptButton 与 CancelButton 仍是「关闭」）。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (Ui.TryFocusFilter(keyData, _keyword)) return true;
        if (Ui.TryEnterFilter(keyData, _keyword, _list)) return true;
        if (Ui.TryEscapeFilter(keyData, _keyword)) return true;
        return base.ProcessDialogKey(keyData);
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
        // 整份数据不可得：关键词框一起停（§7「禁用 = 控件禁用 + 一句话说明缺什么」——
        // 缺什么与下一步由上面那句话给出，框里敲字筛不到任何东西）。口径见 ListFilter.KeywordEnabled。
        _keyword.Enabled = ListFilter.KeywordEnabled(false);
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
