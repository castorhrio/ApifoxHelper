using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Contracts;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 补充说明（F10）：按检查结果逐接口编辑业务说明补充——接口标题、接口说明、query/path 参数、
/// 请求体与 200 响应各一个说明位，可勾选「保留现状，本次不补充」。编辑位与保存切片经
/// SupplementEntries 的冻结页面同一口径；校验与落盘经 SupplementService 唯一口径（与
/// PUT /api/tasks/{id}/supplement 路由同一服务），内容变化时递增任务版本、使旧检查与旧计划过期，
/// 内容未变化时不写文件、可重复保存。保存只替换本接口本次编辑位的条目，其他接口与未编辑位
/// （含示例）原样保留。补充在重新检查后才会进入合并结果与同步计划，对话框不代替重新检查。
/// 离线任务同样可用（补充是本机任务数据，不访问 Apifox）；示例不在本对话框编辑（与冻结页面一致）。
/// 状态表：尚未检查=空态指引；检查结果或已保存补充读取失败=内联错误（补充读取失败时停用保存，
/// 避免用空补充覆盖既有内容）；没有接口=如实说明；正常=接口清单 + 编辑位 + 保存；
/// 筛到零行=关键词框留着并说明下一步，计数行仍是整份统计（见 <see cref="PopulateList"/>）。
/// 打开时把键盘交给接口清单（第九十五轮）：打开即选中第一个接口，键盘留在清单上，方向键与 Ctrl+C 才落在那一行。
/// 清单支持点击列头排序（第一百零七轮，口径见 <see cref="SupplementSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始，排序只改呈现顺序——选中仍按接口身份恢复，未保存的输入不因排序而丢。
/// </summary>
internal sealed class SupplementsDialog : Form
{
    private sealed class EditorBox(SupplementEntries.Editor editor)
    {
        public SupplementEntries.Editor Editor { get; } = editor;
        public TextBox Text { get; } = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
        public CheckBox Keep { get; } = new() { Text = "保留现状，本次不补充", AutoSize = true };
    }

    private readonly TaskStore _tasks;
    private readonly CheckTask _task;
    private readonly SupplementService _supplementSaves;

    private CheckResult? _result;
    private SupplementStore? _supplement;
    private List<EditorBox> _editors = [];
    private bool _dirty;
    private bool _saving;
    /// <summary>清单重建期间为真：清空与回填会触发选中事件，那一次不是用户切换接口。</summary>
    private bool _rebuilding;

    // 列排序状态（ListSort 口径，第一百零七轮）：未排序时保持检查结果自身的接口顺序。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    private readonly string[] _titles;

    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = Ui.List();
    private readonly TextBox _keyword = Ui.Input();
    private readonly Panel _editorsHost = new() { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, UiTokens.Sm) };
    private readonly Panel _editorsScroll = Ui.ScrollHost();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };
    private readonly Button _saveButton;
    private readonly Button _discardButton;
    private readonly Label _status = Ui.StatusLine();

    public SupplementsDialog(TaskStore tasks, CheckTask task, SupplementService supplementSaves)
    {
        _tasks = tasks;
        _task = task;
        _supplementSaves = supplementSaves;

        Text = "补充说明 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("接口", 140);
        _list.Columns.Add("名称", 120);
        _list.Columns.Add("补充", 60);
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.SelectedIndexChanged += (_, _) =>
        {
            if (_rebuilding) return;
            ReloadEditors();
        };
        _list.Dock = DockStyle.Fill;
        _keyword.PlaceholderText = ListFilter.EndpointSearchPlaceholder;
        _keyword.TextChanged += (_, _) => PopulateList();

        _saveButton = Ui.Primary("保存本接口补充");
        _saveButton.Click += (_, _) => SaveCurrent();
        _discardButton = Ui.Ghost("放弃修改");
        _discardButton.Click += (_, _) => DiscardEdits();

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 5, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _discardButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_saveButton, 0, 0);
        actionRow.Controls.Add(_discardButton, 1, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 2, 0);
        actionRow.Controls.Add(_counts, 3, 0);
        actionRow.Controls.Add(closeButton, 4, 0);

        var statusGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Xs) };
        statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusGrid.Controls.Add(_status, 0, 0);

        var sidePanel = new Panel { Dock = DockStyle.Left, Width = 330, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Sm, UiTokens.Sm) };
        var sideCaption = Ui.Caption("检查结果中的接口", muted: true);
        sideCaption.Dock = DockStyle.Top;
        sideCaption.TextAlign = ContentAlignment.MiddleLeft;
        sideCaption.Padding = new Padding(0, 0, 0, UiTokens.Xs);
        // 筛选行形状取工厂（§6「列表筛选」）：这一屏没有筛选下拉，走只有关键词框的那一支；
        // 计数在底部动作行（「共 N 个接口 · 已保存补充 M 处」），故 counts 传 null。
        var filterRow = Ui.FilterRow(_keyword, counts: null);
        sidePanel.Controls.Add(_list);
        sidePanel.Controls.Add(filterRow);
        sidePanel.Controls.Add(sideCaption);

        // 编辑区 = 固定说明行 + 可滚动的编辑位列表；说明行交代生效条件与未保存输入的去向，不随选择变化。
        var editorsCaption = Ui.Caption("补充只作用于当前接口；保存后需重新检查才会生效。切换接口前先保存，未保存的输入不会保留。");
        editorsCaption.Dock = DockStyle.Top;
        editorsCaption.TextAlign = ContentAlignment.MiddleLeft;
        editorsCaption.Padding = new Padding(0, 0, 0, UiTokens.Sm);
        // 停靠方向由工厂一处决定（Dock = Fill），这里不再补设。
        _editorsHost.Controls.Add(_editorsScroll);
        _editorsHost.Controls.Add(editorsCaption);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次状态行、接口侧栏，编辑区最后填满剩余空间。
        Controls.Add(_editorsHost);
        Controls.Add(_hint);
        Controls.Add(sidePanel);
        Controls.Add(statusGrid);
        Controls.Add(actionRow);
        _hint.Visible = false;
        _discardButton.Enabled = false;
        UpdateSaveState();
        SetStatus("选择接口编辑说明补充；保存后重新检查，补充才会进入合并结果与同步计划。", UiTokens.Muted);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _result = _tasks.Read<CheckResult>(_task.Id, TaskFiles.Diff);
        }
        catch (Exception ex)
        {
            ShowHint("读取检查结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后在任务页重新检查再试。", UiTokens.BadText);
            return;
        }
        if (_result is null)
        {
            ShowHint("尚未检查。先在任务页点「开始检查」：补充说明的编辑位（标题、参数、请求体、响应）来自检查结果。", UiTokens.Muted);
            return;
        }
        try
        {
            _supplement = _tasks.Read<SupplementStore>(_task.Id, TaskFiles.Supplement) ?? new SupplementStore();
        }
        catch (Exception ex)
        {
            _supplement = null;
            // 读取不到已保存补充时不提供保存动作，避免用空补充覆盖其他接口的既有内容。
            ShowHint("读取已保存补充失败：" + Redactor.Redact(ex.Message) + "。为避免覆盖已有补充，保存已停用；请关闭后重试。", UiTokens.BadText);
            return;
        }
        if (_result.Endpoints.Count == 0)
        {
            ShowHint("检查结果中没有接口，没有可补充的位置。", UiTokens.Muted);
            return;
        }
        // 有数据可筛：关键词框可用（口径见 ListFilter.KeywordEnabled）。
        _keyword.Enabled = ListFilter.KeywordEnabled(true);
        _hint.Visible = false;
        PopulateList();
    }

    /// <summary>
    /// 打开时把键盘交给接口清单（第九十五轮）：这一屏打开即选中第一个接口、编辑位已按它填好，
    /// 而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——方向键动的是别的控件、
    /// Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 另十一张清单在打开时或这一页出现时都把键盘交给清单（第八十八至九十三轮），这一张没有，
    /// 属同壳不同行为。第八十八轮挂账的理由（「编辑位是这一屏的输入」）在打开这一刻不成立：
    /// 这一屏自己的状态行写着「选择接口编辑说明补充」，第一步就是选接口，而选接口的地方就是这张清单；
    /// 编辑位是选中之后的事，打开时还没有任何输入会被抢走。
    /// 接在 OnShown（窗体首次显示之后）而不是 OnLoad：那时窗体还没显示，Focus 的落点取决于框架
    /// 在激活时怎么选初始焦点。打开时那一屏是提示或空态（尚未检查、读取失败、没有接口）就没有选中行，
    /// 判定答「不交」，那一步的下一步在提示与按钮上。只交键盘——不选中、不滚动、不重建清单、不写数据，
    /// 本对话框不自己写 .Focus()、不自己判「选中行与焦点行是不是同一行」（判定只在 Ui.cs 一处）。
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
        _editorsHost.Visible = false;
        _list.Visible = false;
        // 整份数据不可得：关键词框一起停（§7「禁用 = 控件禁用 + 一句话说明缺什么」——
        // 缺什么与下一步由上面那句话给出，框里敲字筛不到任何东西）。口径见 ListFilter.KeywordEnabled。
        _keyword.Enabled = ListFilter.KeywordEnabled(false);
        _saveButton.Enabled = false;
        _discardButton.Enabled = false;
        _counts.Text = "";
    }

    /// <summary>
    /// 接口清单重建（打开时与关键词框输入时同一入口）：筛选只改可见行——匹配字段取
    /// <see cref="SupplementEntries.KeywordFields"/>（那一行在屏幕上被读作的两格：接口身份与名称），
    /// 计数列是数字、不进关键词。
    /// 选中按接口身份恢复：同一个接口仍在筛选结果里时编辑位原样留着——**未保存的输入不因敲了一个字就丢**
    /// （与第七十二轮「勾选不因筛选而丢」同一取舍：静默丢输入比筛不到难发现得多）；被筛掉时选中落到
    /// 新筛选第一条，编辑位按既有的「切换接口丢弃未保存输入」口径重建，并在状态行如实说明这一次丢弃。
    /// 筛到零行不是「检查结果里没有接口」：说清这一屏为什么空、下一步就在那个框上（这一屏没有筛选下拉），
    /// 关键词框留着（§7「禁用不得移除下一步」），计数行仍是整份统计。
    /// </summary>
    private void PopulateList()
    {
        var previousKey = Current()?.Key;
        var wasDirty = _dirty;
        // 筛选先于排序（ListFilter → ListSort，与另十一处同一顺序）：筛掉的行不参与排序，
        // 排序键取 SupplementSort 单一口径（与单元格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(_result!.Endpoints, _keyword.Text, SupplementEntries.KeywordFields),
            _sortColumn, _sortDescending,
            (check, column) => SupplementSort.Key(
                SupplementEntries.EndpointCell(check),
                SupplementEntries.NameCell(check),
                SupplementEntries.EndpointEntryCount(_supplement!, check.Key),
                column));
        _rebuilding = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var check in visible)
        {
            var item = new ListViewItem(SupplementEntries.EndpointCell(check)) { Tag = check, UseItemStyleForSubItems = false };
            item.SubItems[0].Font = UiTokens.Mono.Value;
            item.SubItems.Add(SupplementEntries.NameCell(check));
            item.SubItems.Add(SupplementEntries.EntryCountCell(_supplement!, check.Key));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_result.Endpoints.Count, visible.Count))
        {
            _rebuilding = false;
            ShowFilteredEmpty();
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        _editorsHost.Visible = true;
        var target = previousKey is null
            ? null
            : _list.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as EndpointCheck)?.Key == previousKey);
        Ui.SelectItem(target ?? _list.Items[0]);
        _rebuilding = false;
        if (target is not null)
        {
            // 同一个接口仍在筛选结果里：编辑位与未保存的输入原样留着，只把计数行跟上。
            UpdateCounts();
            return;
        }
        ReloadEditors();
        if (wasDirty) SetStatus("已切换到另一个接口，上一个接口的未保存输入已丢弃。", UiTokens.WarnText);
    }

    /// <summary>
    /// 筛到零行：这一屏的空属于「有条目、只是当前关键词一条都不命中」，与「检查结果里没有接口」
    /// 是两种空（下一步不同，第五十九轮同一口径）。关键词框留着（§7「禁用不得移除下一步」），
    /// 计数行仍是整份统计（「共 N 个接口 · 已保存补充 M 处」讲的是整份检查结果，不因关键词不命中而失效）；
    /// 编辑位收走、保存与放弃一起禁用——没有选中就没有可保存的那一个接口。
    /// </summary>
    private void ShowFilteredEmpty()
    {
        _hint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.EndpointNoun);
        _hint.ForeColor = UiTokens.Muted;
        _hint.Visible = true;
        _editorsHost.Visible = false;
        _dirty = false;
        _saveButton.Enabled = false;
        _discardButton.Enabled = false;
        UpdateCounts();
    }

    /// <summary>
    /// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给关键词框并选中已有关键词、
    /// 框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部接口）。
    /// 与另七张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
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

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百零七轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="PopulateList"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改检查结果与补充内容，选中仍按接口身份恢复
    /// （<see cref="PopulateList"/> 重建前记下、重建后找回，编辑位与未保存的输入随选中事件跟上）。
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

    private void RefreshListCounts()
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is EndpointCheck check) item.SubItems[2].Text = SupplementEntries.EntryCountCell(_supplement!, check.Key);
        }
    }

    private EndpointCheck? Current() => Ui.SelectedItem(_list)?.Tag as EndpointCheck;

    /// <summary>按选中接口重建编辑位：初始值与勾选来自已保存条目，输入框提示显示现状文字（与冻结页面同一取值）。</summary>
    private void ReloadEditors()
    {
        _dirty = false;
        _editorsScroll.Controls.Clear();
        _editors = [];
        _discardButton.Enabled = false;
        UpdateSaveState();
        if (Current() is not { } check || _supplement is null)
        {
            // 清空选中后保存随放弃一起禁用，并说明缺什么（保存可用性随选中变化，不残留上一次的状态）。
            SetStatus("选择接口后可编辑与保存说明补充。", UiTokens.Muted);
            UpdateCounts();
            return;
        }
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Bg, Padding = new Padding(0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var editor in SupplementEntries.EditorsFor(check))
        {
            var box = new EditorBox(editor);
            var existing = SupplementEntries.ExistingEntry(_supplement, check.Key, editor);
            box.Text.Text = existing?.Value ?? "";
            box.Text.PlaceholderText = editor.CurrentText.Length > 0 ? "当前：" + editor.CurrentText : "";
            box.Keep.Checked = existing?.KeepAsIs ?? false;
            box.Text.TextChanged += (_, _) => MarkDirty();
            box.Keep.CheckedChanged += (_, _) => MarkDirty();
            _editors.Add(box);

            AddRow(grid, Ui.Caption(editor.Label, muted: false));
            var field = Ui.Field(box.Text);
            // 原宿主（Hairline 面板）自带 Dock=Fill：FieldBox 保持同一占位（Percent 列 + 48px 行）。
            field.Dock = DockStyle.Fill;
            var row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            field.Margin = new Padding(0, 0, 0, UiTokens.Xs);
            grid.Controls.Add(field, 0, row);
            AddRow(grid, box.Keep);
        }
        _editorsScroll.Controls.Add(grid);
        UpdateCounts();
    }

    private static void AddRow(TableLayoutPanel grid, Control control)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(control.Margin.Left, control.Margin.Top, 0, UiTokens.Xs);
        grid.Controls.Add(control, 0, row);
    }

    /// <summary>输入与已保存内容不同才算未保存修改（与冻结页面一致：原样重复保存不产生变化）。</summary>
    private bool EditorsChanged() =>
        Current() is { } check
        && _editors.Any(box =>
        {
            var existing = SupplementEntries.ExistingEntry(_supplement!, check.Key, box.Editor);
            var text = box.Text.Text.Trim();
            if (box.Keep.Checked) return existing is not { KeepAsIs: true };
            return text != (existing is { KeepAsIs: false, Value: not null } ? existing.Value : "");
        });

    private void MarkDirty()
    {
        _dirty = EditorsChanged();
        _discardButton.Enabled = _dirty && !_saving;
        UpdateSaveState();
        UpdateCounts();
    }

    /// <summary>保存主按钮的门控单一口径：选中接口、已保存补充可读、不在保存中三者齐备才可用。
    /// 此前保存中禁用后没有任何恢复路径，一次保存（成功或被拒）后按钮残留禁用、无法继续保存其他接口。</summary>
    private void UpdateSaveState() =>
        _saveButton.Enabled = Current() is not null && _supplement is not null && !_saving;

    private void DiscardEdits()
    {
        ReloadEditors();
        SetStatus("已还原为本接口已保存的内容。", UiTokens.Muted);
    }

    /// <summary>计数行按全部接口统计，等宽数字不跳宽；有未保存修改时在行尾如实说明。</summary>
    private void UpdateCounts()
    {
        var total = _result?.Endpoints.Sum(e => SupplementEntries.EndpointEntryCount(_supplement ?? new SupplementStore(), e.Key)) ?? 0;
        _counts.Text = $"共 {_result?.Endpoints.Count ?? 0} 个接口 · 已保存补充 {total} 处"
            + (_dirty ? " · 本接口有未保存修改" : "");
    }

    /// <summary>保存本接口：切片经共享 SupplementEntries 构造，校验与落盘经共享 SupplementService；其他接口与未编辑位原样保留。</summary>
    private async void SaveCurrent()
    {
        if (Current() is not { } check || _supplement is null || _saving) return;
        _saving = true;
        _saveButton.Enabled = false;
        _discardButton.Enabled = false;
        var values = _editors.Select(box => new SupplementEntries.EditorValue(box.Editor, box.Text.Text, box.Keep.Checked)).ToList();
        var body = SupplementEntries.MergeEndpoint(_supplement, check.Key, values);
        int before;
        SupplementStore saved;
        try
        {
            before = _tasks.Get(_task.Id).Revision;
            saved = await _supplementSaves.SaveAsync(_task.Id, body);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.WarnText);
            _saving = false;
            MarkDirty();
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("保存补充失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            _saving = false;
            MarkDirty();
            return;
        }
        if (IsDisposed) return;
        _supplement = saved;
        _saving = false;
        ReloadEditors();
        RefreshListCounts();
        var changed = _tasks.Get(_task.Id).Revision > before;
        SetStatus(SupplementEntries.SavedText(changed), UiTokens.OkText);
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
