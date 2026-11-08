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
/// 候选清单支持点击列头排序（第一百轮，口径见 <see cref="CandidateSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始、表头带方向标记；排序只改呈现顺序，选中仍按候选自身恢复。
/// 候选文字可改、「保存修改」与「撤销接受」两个动作（第一百一十一轮，口径见 <see cref="CandidateEdit"/>）：
/// 候选文字正是要写进源码的 /// 注释，生成或导入的文字有错字时在这里改；已接受的候选可退回待审阅。
/// </summary>
internal sealed class CandidatesDialog : Form
{
    private static readonly string[] FilterChoices = ["全部候选", "待审阅", "已接受", "已搁置", "已驳回", "证据已变化"];

    private readonly DraftService _drafts;
    private readonly TaskStore _tasks;
    private readonly CheckTask _task;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly TextBox _keyword = Ui.Input();
    private readonly ListView _list = Ui.List();
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly TextBox _text = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _textCaption = Ui.Caption();
    private readonly TextBox _answers = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _answersCaption = Ui.Caption();
    private readonly CheckBox _overwrite = new() { AutoSize = true, Text = "覆盖该位置已有的有效注释（需要改写时勾选）" };
    private readonly Button _acceptButton;
    private readonly Button _deferButton;
    private readonly Button _rejectButton;
    private readonly Button _saveTextButton;
    private readonly Button _undoButton;
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

    // 列排序状态（ListSort 口径，第一百轮）：未排序时保持候选自身的构造顺序。
    // 排序状态留在本次打开期间（切筛选、敲关键词、保存后重建都保持），关闭即忘——
    // 跨启动记忆只有两个常驻页面（第五十五轮那两个页键），
    // 这一屏每次打开看的是这一次的候选，不为它另开一个记忆文件。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _titles;

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
        ClientSize = new Size(960, 740);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("位置（说明点）", 340);
        _list.Columns.Add("标注", 70);
        _list.Columns.Add("依据", 110);
        _list.Columns.Add("审阅状态", 110);
        _list.Columns.Add("来源", 90);
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence(Ui.SelectedItem(_list)?.Tag as CommentCandidate);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        // 双击=查看证据经共享入口：命中哪行看哪行的证据（行自身 Tag，不借当前选中），双击列头与空白区不动作。
        Ui.ActivateItem(_list, item => ShowEvidence(item.Tag as CommentCandidate));

        _acceptButton = Ui.Primary("接受");
        _acceptButton.Click += (_, _) => RunReview("accepted");
        _deferButton = Ui.Ghost("搁置");
        _deferButton.Click += (_, _) => RunReview("deferred");
        _rejectButton = Ui.Ghost("驳回");
        _rejectButton.Click += (_, _) => RunReview("rejected");
        _saveTextButton = Ui.Ghost("保存修改");
        _saveTextButton.Click += (_, _) => SaveText();
        _undoButton = Ui.Ghost("撤销接受");
        _undoButton.Click += (_, _) => UndoAccept();
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

        _keyword.PlaceholderText = ListFilter.CandidateSearchPlaceholder;
        _keyword.TextChanged += (_, _) => ApplyFilter();

        // 筛选行形状取工厂（§6「列表筛选」）：五张长清单同一份，不各拼一遍。
        var filterRow = Ui.FilterRow(_filter, _keyword, _counts);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Sm) };
        detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCaptionRow(detailGrid, "候选说明");
        AddBoxRow(detailGrid, Ui.Hairline(_detail), 92);
        // 候选文字是可编辑的那一格（DESIGN.md §6「候选文字编辑」）：候选文字正是要写进源码的 /// 注释，
        // 生成或导入的文字有错字时在这里改，改完点「保存修改」——写入仍经 DraftService.UpdateAsync 既有校验。
        AddCaptionRow(detailGrid, _textCaption);
        AddBoxRow(detailGrid, Ui.Field(_text), 56);
        _answersCaption.Text = "待确认问题的回答";
        _answersCaption.Margin = new Padding(0, UiTokens.Sm, 0, 0);
        AddCaptionRow(detailGrid, _answersCaption);
        AddBoxRow(detailGrid, Ui.Field(_answers), 44);
        AddCaptionRow(detailGrid, _overwrite);
        AddCaptionRow(detailGrid, _status);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 9, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _deferButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _rejectButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _saveTextButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _undoButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _importButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_acceptButton, 0, 0);
        actionRow.Controls.Add(_deferButton, 1, 0);
        actionRow.Controls.Add(_rejectButton, 2, 0);
        actionRow.Controls.Add(_saveTextButton, 3, 0);
        actionRow.Controls.Add(_undoButton, 4, 0);
        actionRow.Controls.Add(_importButton, 5, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 6, 0);
        actionRow.Controls.Add(_evidenceButton, 7, 0);
        actionRow.Controls.Add(closeButton, 8, 0);

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
        _keyword.Enabled = true;
        _list.Visible = true;
        UpdateCounts();
        ApplyFilter();
        // 打开即选中第一条（第九十轮）：这一屏是逐条清队列的，打开时详情区与三个审阅动作就该对着第一条，
        // 与差异确认、五张只读长清单同一行为。落点经 Ui.SelectFirstOnOpen 唯一入口（有行才落，空态不索引）；
        // 只落选中、不取键盘——键盘在 OnShown 交给清单。
        Ui.SelectFirstOnOpen(_list);
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「审阅队列打开即选中第一条」，第九十轮）：
    /// 这一屏打开即选中第一条候选（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
    /// 方向键动的是别的控件、Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 交键盘只在 <see cref="Ui.KeepKeyboardOnQueue"/> 一处（判定在 <see cref="ListFocus"/>：选中行与焦点行是同一行才交），
    /// 本对话框不自己写 .Focus()、不选中、不滚动、不重建清单。
    /// 放在 OnShown 而不是 OnLoad：OnLoad 时窗体尚未显示，Focus() 的落点取决于框架在激活时怎么选初始焦点。
    /// 打开时那一屏是提示或空态（读取失败、没有候选、筛到零行）就没有选中行，不动键盘——
    /// 那一步的下一步在提示与按钮上，不在清单上。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.KeepKeyboardOnQueue(_list);
    }

    private void LoadSlots()
    {
        var scan = _tasks.Read<ScanOutput>(_task.Id, TaskFiles.Symbols);
        _slotsById = scan?.Slots.ToDictionary(s => s.Id, StringComparer.Ordinal) ?? [];
    }

    /// <summary>
    /// 提示出口。<c>keepAggregate</c> 为真表示「候选还在，只是当前筛选一条都不命中」：筛选器与关键词框要留着
    /// （提示语「换一个关键词或筛选再试」指的就是它们，见第四十二轮对差异确认的同一纠正），计数行的聚合数字也要留着
    /// （「候选 128 · 待审阅 0」正是清完队列的凭据，与两页「筛选出 0 个任务（共 42 个）」同一如实口径）。
    /// 缺省为假：打开期读取失败与「没有候选」仍是原来的形状。
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
        // 候选文字那一格随提示一起收掉：清单已经藏起来、动作已经禁用，留着上一行候选的文字
        // 等于让用户对着一条看不见的候选改字（改完也没有「保存修改」可按）。
        _text.Text = "";
        _text.Enabled = false;
        _textCaption.Text = CandidateEdit.TextCaptionNoSelection;
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

    private void ApplyFilter(CommentCandidate? keep = null)
    {
        // 关键词先经共享口径（与两个主页面、另三张长清单同一份匹配规则：词间「且」、字段间「或」、忽略大小写），
        // 审阅状态筛选在其后——两个条件都是「且」，顺序不影响结果，但匹配规则只有一份。
        // 筛选先于排序（ListFilter → ListSort，与两个主页面、检查结果、接口追踪明细、差异确认同一顺序）：
        // 筛掉的行不参与排序，排序状态与筛选状态各自留在本对话框；
        // 排序键取 CandidateSort 单一口径（与单元格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(_store.Candidates, _keyword.Text, c => QueueText.CandidateFields(c, _slotsById.GetValueOrDefault(c.SlotId))).Where(Matches).ToList(),
            _sortColumn, _sortDescending,
            (candidate, column) => CandidateSort.Key(
                QueueText.CandidateTitle(candidate, _slotsById.GetValueOrDefault(candidate.SlotId)),
                QueueText.CandidateTag(candidate),
                TaskDisplay.CandidateBasisText(candidate.Basis),
                TaskDisplay.ReviewStateText(candidate.ReviewState),
                TaskDisplay.CandidateSourceText(candidate.Source),
                column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var candidate in visible)
        {
            var slot = _slotsById.GetValueOrDefault(candidate.SlotId);
            var item = new ListViewItem(QueueText.CandidateTitle(candidate, slot)) { Tag = candidate };
            item.SubItems.Add(QueueText.CandidateTag(candidate));
            item.SubItems.Add(TaskDisplay.CandidateBasisText(candidate.Basis));
            var review = item.SubItems.Add(TaskDisplay.ReviewStateText(candidate.ReviewState));
            item.UseItemStyleForSubItems = false;
            review.ForeColor = ToneColor(TaskDisplay.ReviewTone(candidate.ReviewState));
            item.SubItems.Add(TaskDisplay.CandidateSourceText(candidate.Source));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_store.Candidates.Count, _list.Items.Count))
        {
            // 筛到零行不是「没有候选」：说清这一屏为什么空、下一步就在筛选上，聚合计数留着（口径见 ListFilter）。
            ShowHint(ListFilter.FilteredEmptyHint(ListFilter.CandidateNoun, ListFilter.IsActive(_keyword.Text)), UiTokens.Muted, keepAggregate: true);
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        // 排序后重建把当前候选交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首；
        // 交回去仍经 Ui.SelectItem 唯一入口（选中事件随即问到详情，详情与三个审阅动作跟着那一行走）。
        // 关键词与筛选的重建路径不传 keep（那一屏的下一步是换关键词或筛选，落点会把回答框里刚打的字重置掉）。
        if (keep is not null && SelectCandidate(keep.Id)) return;
        // 重建之后再问一次详情：重建路径不自动选中任何一行（打开时的落点在 OnLoad 一处，见 Ui.SelectFirstOnOpen），
        // 判在清空之前量到的是上一轮筛选的行数，「缺什么」那句话会在该说话的时候沉默（第一次进屏时清单还是空的）。
        // 重建路径不落点是有理由的：关键词框每敲一个字都重建一次，落点会把回答框里刚打的字重置掉
        // （ShowDetail 按选中行重填回答框），等于把用户正在改的那一格从他手里拿走（与第五十八轮同一取舍）。
        ShowDetail();
    }

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="ApplyFilter"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改审阅状态，选中仍按候选自身恢复（重建前记下、重建后找回）。
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

    private CommentCandidate? Current() => Ui.SelectedItem(_list)?.Tag as CommentCandidate;

    private void ShowDetail()
    {
        if (Ui.SelectedItem(_list)?.Tag is not CommentCandidate candidate)
        {
            // 有行可选却没选中：缺什么由详情区说；一行都没有时空的是筛选，那一屏归提示出口（口径见 ListSelection）。
            _detail.Text = ListSelection.ShouldExplainMissingSelection(_list.Items.Count)
                ? ListSelection.MissingSelectionHint(ListFilter.CandidateNoun)
                : "";
            _text.Text = "";
            _text.Enabled = false;
            _textCaption.Text = CandidateEdit.TextCaptionNoSelection;
            _answers.Text = "";
            _answersDirty = false;
            _overwrite.Checked = false;
            _evidenceButton.Enabled = false;
            SetButtons(null);
            return;
        }
        var slot = _slotsById.GetValueOrDefault(candidate.SlotId);
        // 候选文字不再重复写进这块只读摘要：它就在下面那一格可编辑的输入框里（改完点「保存修改」），
        // 同一段文字摆在两个相邻的框里，用户不知道该改哪一个。
        var lines = new List<string>
        {
            "符号：" + QueueText.CandidateTitle(candidate, slot) + (slot is null ? "（该注释位置已不在本次扫描结果中，请重新扫描后再审阅）" : ""),
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

        // 候选文字那一格：可编辑的候选（"set"）填当前文字并可改，删除失效标签的候选只读
        // （那一格讲的是删除动作，没有可改的说明文字，与冻结页面同一口径）。
        _text.Text = candidate.Text;
        _text.Enabled = CandidateEdit.CanEditText(candidate);
        _textCaption.Text = CandidateEdit.TextCaptionFor(candidate);

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

    /// <summary>
    /// 审阅动作只在注释位置仍在扫描结果中时可用（服务的审阅校验按位置执行）。
    /// 「保存修改」与「撤销接受」的可用性取 <see cref="CandidateEdit"/> 单一口径：
    /// 前者要文字可编辑（"set"），后者要当前已接受——不可用的按钮如实禁用，不点了没反应。
    /// </summary>
    private void SetButtons(CommentCandidate? candidate)
    {
        var slotExists = candidate is not null && _slotsById.ContainsKey(candidate.SlotId);
        _acceptButton.Enabled = slotExists;
        _deferButton.Enabled = slotExists;
        _rejectButton.Enabled = slotExists;
        _saveTextButton.Enabled = candidate is not null && CandidateEdit.CanSaveText(candidate, slotExists);
        _undoButton.Enabled = candidate is not null && CandidateEdit.CanUndoAccept(candidate, slotExists);
    }

    /// <summary>
    /// 三个审阅动作（接受 / 搁置 / 驳回）：请求体经 <see cref="CandidateEdit"/> 单一口径构造后交给
    /// <see cref="RunCandidateUpdate"/>——写入、重读、重建、续接选中与交回键盘只有那一处，
    /// 五个动作（三个审阅 + 保存修改 + 撤销接受）不各写一套（两套落点必然漂移）。
    /// 接受时连候选文字一起提交（用户可能刚在那一格里改过），回答与覆盖勾选只在本次动过时才带上
    /// （不把没动过的写成空串，与冻结页面同一份）。
    /// </summary>
    private void RunReview(string reviewState)
    {
        if (Ui.SelectedItem(_list)?.Tag is not CommentCandidate candidate) return;
        var update = reviewState == "accepted"
            ? CandidateEdit.AcceptRequest(candidate, _text.Text, _answersDirty ? _answers.Text : null, _overwrite.Enabled ? _overwrite.Checked : null)
            : new CandidateUpdateRequest { ReviewState = reviewState };
        RunCandidateUpdate(update, () => TaskDisplay.ReviewStateText(reviewState)
            + "。剩余待审阅 " + _store.Candidates.Count(c => c.ReviewState == "pending") + " 条。");
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
        // 导入后候选重新可读：两个筛选入口一起开（只开一个等于留一个按不动的入口）。
        _filter.Enabled = true;
        _keyword.Enabled = true;
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

    /// <summary>
    /// 保存修改（DESIGN.md §6「候选文字编辑」）：把这一格改过的候选文字写回任务数据，
    /// 请求体由 <see cref="CandidateEdit.SaveTextRequest"/> 单一口径构造（只提交文字、回答与覆盖勾选，
    /// 不动审阅状态——改文字不该顺手把一条待审阅的候选变成已接受），
    /// 校验仍全在 DraftService.UpdateAsync（占位文字、超长、注释标记、位置已不存在等拒绝原因原样内联）。
    /// </summary>
    private void SaveText()
    {
        if (Ui.SelectedItem(_list)?.Tag is not CommentCandidate candidate) return;
        if (!CandidateEdit.CanSaveText(candidate, _slotsById.ContainsKey(candidate.SlotId))) return;
        RunCandidateUpdate(
            CandidateEdit.SaveTextRequest(_text.Text, _answersDirty ? _answers.Text : null, _overwrite.Enabled ? _overwrite.Checked : null),
            () => CandidateEdit.SaveDoneText);
    }

    /// <summary>
    /// 撤销接受（DESIGN.md §6「候选文字编辑」）：把已接受的候选退回待审阅——此前「接受」在原生界面是单向的，
    /// 已接受的候选只能改成搁置或驳回。请求体由 <see cref="CandidateEdit.UndoAcceptRequest"/> 单一口径构造。
    /// </summary>
    private void UndoAccept()
    {
        if (Ui.SelectedItem(_list)?.Tag is not CommentCandidate candidate) return;
        if (!CandidateEdit.CanUndoAccept(candidate, _slotsById.ContainsKey(candidate.SlotId))) return;
        RunCandidateUpdate(CandidateEdit.UndoAcceptRequest(), () => CandidateEdit.UndoDoneText);
    }

    /// <summary>
    /// 保存修改与撤销接受共用的落点（与三个审阅动作同一口径）：写入 → 按磁盘重读候选 → 重建清单 →
    /// 回到该候选（它已不在当前筛选上时续接下一行，<see cref="ListAdvance"/> 口径）→ 把键盘交回清单。
    /// 两条被拒分支与重新读取失败分支都在交键盘之前返回——那时用户要改的就是手里那一格
    /// （候选文字、待确认问题的回答），把键盘交走等于把下一步从他手里拿走（与第五十八轮同一取舍）。
    /// await 之后触达界面以前先检查 IsDisposed（第四十五轮口径：对话框执行期间「关闭」始终可用）。
    /// </summary>
    private async void RunCandidateUpdate(CandidateUpdateRequest request, Func<string> doneText)
    {
        if (Ui.SelectedItem(_list)?.Tag is not CommentCandidate candidate) return;
        // 被处理那一行在旧清单上的显示序号：保存后它可能不再属于当前筛选而消失，续接选中要用（ListAdvance 口径）。
        var handledAt = Ui.SelectedIndex(_list) ?? ListAdvance.None;
        SetButtons(null);
        try
        {
            await _drafts.UpdateAsync(_task.Id, candidate.Id, request, CancellationToken.None);
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
            SetStatus("保存候选失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
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
            // 写入已成功，重新读取失败时如实说明，不显示成保存失败。
            SetStatus("已保存，但重新读取候选失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            SetButtons(candidate);
            return;
        }
        UpdateCounts();
        ApplyFilter();
        if (!SelectCandidate(candidate.Id)) Ui.AdvanceAfterRemoval(_list, handledAt);
        Ui.KeepKeyboardOnQueue(_list);
        // 完成文案在重读之后才算：审阅动作那句「剩余待审阅 N 条」要的是写入后的计数。
        SetStatus(doneText(), UiTokens.OkText);
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
