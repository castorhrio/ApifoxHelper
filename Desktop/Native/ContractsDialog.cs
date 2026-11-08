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
/// 契约清单支持点击列头排序（第一百零一轮，口径见 <see cref="ContractSort"/> 与 <see cref="ListSort"/>）：
/// 与差异确认、注释候选同一只壳，同一个手势在别处有、在这一处没有属同壳不同行为。
/// </summary>
internal sealed class ContractsDialog : Form
{
    private static readonly string[] FilterChoices = ["全部契约", "待确认", "已确认", "已拒绝", "已阻断", "已失效"];

    private readonly TaskStore _tasks;
    private readonly CheckTask _task;
    private readonly ContractReviewService _reviews;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TextBox _keyword = Ui.Input();
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

    // 列排序状态（ListSort 口径，第一百零一轮）：未排序时保持契约自身的构造顺序。
    // 排序状态留在本次打开期间（切筛选、敲关键词、保存后重建都保持），关闭即忘——
    // 跨启动记忆只有两个常驻页面（第五十五轮那两个页键），
    // 这一屏每次打开看的是这一次的契约，不为它另开一个记忆文件。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _titles;

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
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
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

        _keyword.PlaceholderText = ListFilter.ContractSearchPlaceholder;
        _keyword.TextChanged += (_, _) => ApplyFilter();

        // 筛选行形状取工厂（§6「列表筛选」）：五张长清单同一份，不各拼一遍。
        var filterRow = Ui.FilterRow(_filter, _keyword, _counts);

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
        _keyword.Enabled = true;
        _list.Visible = true;
        UpdateCounts();
        ApplyFilter();
        // 打开即选中第一条（第九十轮）：这一屏是逐条确认的，打开时详情区与三个审阅动作就该对着第一条，
        // 与差异确认、五张只读长清单同一行为。落点经 Ui.SelectFirstOnOpen 唯一入口（有行才落，空态不索引）；
        // 只落选中、不取键盘——键盘在 OnShown 交给清单。
        Ui.SelectFirstOnOpen(_list);
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「审阅队列打开即选中第一条」，第九十轮）：
    /// 这一屏打开即选中第一条契约（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
    /// 方向键动的是别的控件、Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 交键盘只在 <see cref="Ui.KeepKeyboardOnQueue"/> 一处（判定在 <see cref="ListFocus"/>：选中行与焦点行是同一行才交），
    /// 本对话框不自己写 .Focus()、不选中、不滚动、不重建清单。
    /// 放在 OnShown 而不是 OnLoad：OnLoad 时窗体尚未显示，Focus() 的落点取决于框架在激活时怎么选初始焦点。
    /// 打开时那一屏是提示或空态（读取失败、没有契约、筛到零行）就没有选中行，不动键盘——
    /// 那一步的下一步在提示与按钮上，不在清单上。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.KeepKeyboardOnQueue(_list);
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
    /// 提示出口。<c>keepAggregate</c> 为真表示「契约还在，只是当前筛选一条都不命中」：筛选器与关键词框要留着
    /// （提示语「换一个关键词或筛选再试」指的就是它们），计数行的聚合数字也要留着（「待确认 0」是清完队列的凭据）。
    /// 缺省为假：打开期读取失败与「没有需要补充的响应契约」仍是原来的形状。
    /// </summary>
    private void ShowHint(string message, Color color, bool keepAggregate = false)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _filter.Enabled = keepAggregate;
        _keyword.Enabled = keepAggregate;
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

    /// <summary>
    /// 对话框里的两个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给本屏关键词框并选中已有关键词，
    /// 与两个主页面同一手势——模态对话框打开期间壳的菜单不路由，这个手势只能由对话框自己接；手势的字母与修饰键判定
    /// 都取 <see cref="ShellShortcuts.FindKey"/> 与 <see cref="ShellShortcuts.IsFindGesture"/> 单一口径，
    /// 界面不就地写 Keys.F、也不就地拆修饰键（另写一份必然漂移：对话框里按 Ctrl+F 没反应正是这一族）。
    /// 关键词框里的裸 Enter 与两个主页面同一手势：把焦点与选中交给筛选结果第一行（目标行与选中走
    /// <see cref="Ui.FocusFirstResult"/> 一处，第六十九轮收口、本轮接线），键盘随后在清单上逐行看——此前这一步在对话框里
    /// 什么都不做，焦点留在框里、方向键动的是光标，要看某一行只能回鼠标点清单。两条判定都只经 Ui 一处
    /// （<see cref="Ui.TryFocusFilter"/> 与 <see cref="Ui.TryEnterFilter"/>），对话框不就地拆修饰键、不自己选中或滚动。
    /// 对话框的 Enter=确认（表单字段）不受影响：筛选框不是表单字段，本入口只在关键词框取得焦点时接住这一次按键。
    /// 带修饰键的组合不属这两个判定，照旧交给基类。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (Ui.TryFocusFilter(keyData, _keyword)) return true;
        if (Ui.TryEnterFilter(keyData, _keyword, _list)) return true;
        if (Ui.TryEscapeFilter(keyData, _keyword)) return true;
        return base.ProcessDialogKey(keyData);
    }

    private void ApplyFilter(ResponseContract? keep = null)
    {
        // 关键词先经共享口径（与两个主页面、另三张长清单同一份匹配规则：词间「且」、字段间「或」、忽略大小写），
        // 审阅状态筛选在其后——两个条件都是「且」，顺序不影响结果，但匹配规则只有一份。
        // 筛选先于排序（ListFilter → ListSort，与两个主页面、检查结果、接口追踪明细、差异确认、注释候选同一顺序）：
        // 筛掉的行不参与排序，排序状态与筛选状态各自留在本对话框；
        // 排序键取 ContractSort 单一口径（与单元格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(_contracts, _keyword.Text, QueueText.ContractFields).Where(Matches).ToList(),
            _sortColumn, _sortDescending,
            (contract, column) => ContractSort.Key(
                contract.EndpointKey,
                QueueText.ContractWrapper(contract.WrapperType),
                QueueText.ContractDataType(contract.DataType),
                contract.Branches.Count,
                TaskDisplay.ContractStateText(contract.ReviewState),
                column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var contract in visible)
        {
            var item = new ListViewItem(contract.EndpointKey) { Tag = contract };
            var wrapper = item.SubItems.Add(QueueText.ContractWrapper(contract.WrapperType));
            wrapper.ForeColor = UiTokens.Muted;
            item.SubItems.Add(QueueText.ContractDataType(contract.DataType));
            item.SubItems.Add(contract.Branches.Count.ToString());
            var review = item.SubItems.Add(TaskDisplay.ContractStateText(contract.ReviewState));
            item.UseItemStyleForSubItems = false;
            review.ForeColor = ToneColor(TaskDisplay.ContractTone(contract.ReviewState));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_contracts.Count, _list.Items.Count))
        {
            // 筛到零行不是「没有契约」：说清这一屏为什么空、下一步就在筛选上，聚合计数留着（口径见 ListFilter）。
            ShowHint(ListFilter.FilteredEmptyHint(ListFilter.ContractNoun, ListFilter.IsActive(_keyword.Text)), UiTokens.Muted, keepAggregate: true);
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        // 排序后重建把当前契约交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首；
        // 交回去仍经 Ui.SelectItem 唯一入口（选中事件随即问到详情，详情与四个动作跟着那一行走）。
        // 关键词与筛选的重建路径不传 keep（那一屏的下一步是换关键词或筛选，落点会把详情区从用户正在看的那一条挪走）。
        if (keep is not null && SelectContract(keep.EndpointKey)) return;
        // 重建之后再问一次详情：重建路径不自动选中任何一行（打开时的落点在 OnLoad 一处，见 Ui.SelectFirstOnOpen），
        // 判在清空之前量到的是上一轮筛选的行数，「缺什么」那句话会在该说话的时候沉默（第一次进屏时清单还是空的）。
        // 重建路径不落点与注释候选同一理由：关键词框每敲一个字都重建一次，落点会把详情区从用户正在看的那一条
        // 挪到第一条（与第五十八轮「不抢用户正在改的那一格」同一取舍）。
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
            "封装 " + QueueText.ContractWrapper(contract.WrapperType)
                + " · " + (contract.DataJsonName.Length > 0 ? contract.DataJsonName : "data") + " = "
                + QueueText.ContractDataType(contract.DataType) + (contract.DataNullable ? "（异常分支为空）" : ""),
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

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百零一轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="ApplyFilter"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改审阅状态，选中仍按契约自身恢复（重建前记下、重建后找回）。
    /// </summary>
    private void SortBy(int column)
    {
        (_sortColumn, _sortDescending) = ListSort.Toggle(_sortColumn, _sortDescending, column);
        ApplyFilter(Current());
    }

    /// <summary>表头方向标记：排序列追加方向标记，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSortHeaders()
    {
        for (var i = 0; i < _list.Columns.Count && i < _titles.Length; i++)
        {
            _list.Columns[i].Text = ListSort.Header(_titles[i], i, _sortColumn, _sortDescending);
        }
    }

    private ResponseContract? Current() => Ui.SelectedItem(_list)?.Tag as ResponseContract;

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
