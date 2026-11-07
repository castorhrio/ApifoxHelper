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
/// 避免用空补充覆盖既有内容）；没有接口=如实说明；正常=接口清单 + 编辑位 + 保存。
/// </summary>
internal sealed class SupplementsDialog : Form
{
    private sealed class EditorBox(SupplementEntries.Editor editor)
    {
        public SupplementEntries.Editor Editor { get; } = editor;
        public TextBox Text { get; } = new()
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = UiTokens.Panel,
            Font = UiTokens.Body.Value,
        };
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

    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = Ui.List();
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
        _list.SelectedIndexChanged += (_, _) => ReloadEditors();
        _list.Dock = DockStyle.Fill;

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
        sidePanel.Controls.Add(_list);
        sidePanel.Controls.Add(sideCaption);

        // 编辑区 = 固定说明行 + 可滚动的编辑位列表；说明行交代生效条件与未保存输入的去向，不随选择变化。
        var editorsCaption = Ui.Caption("补充只作用于当前接口；保存后需重新检查才会生效。切换接口前先保存，未保存的输入不会保留。");
        editorsCaption.Dock = DockStyle.Top;
        editorsCaption.TextAlign = ContentAlignment.MiddleLeft;
        editorsCaption.Padding = new Padding(0, 0, 0, UiTokens.Sm);
        // 对象初始化器不能跟在方法调用后（CS1003），Dock 在此补设。
        _editorsScroll.Dock = DockStyle.Fill;
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
        _hint.Visible = false;
        FillList();
        UpdateCounts();
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _editorsHost.Visible = false;
        _list.Visible = false;
        _saveButton.Enabled = false;
        _discardButton.Enabled = false;
        _counts.Text = "";
    }

    private void FillList()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var check in _result!.Endpoints)
        {
            var item = new ListViewItem(check.Method + " " + check.Path) { Tag = check, UseItemStyleForSubItems = false };
            item.SubItems[0].Font = UiTokens.Mono.Value;
            item.SubItems.Add(check.RemoteName ?? "");
            item.SubItems.Add(SupplementEntries.EndpointEntryCount(_supplement!, check.Key) + " 处");
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (_list.Items.Count > 0) Ui.SelectItem(_list.Items[0]);
    }

    private void RefreshListCounts()
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is EndpointCheck check) item.SubItems[2].Text = SupplementEntries.EndpointEntryCount(_supplement!, check.Key) + " 处";
        }
    }

    private EndpointCheck? Current() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as EndpointCheck : null;

    /// <summary>按选中接口重建编辑位：初始值与勾选来自已保存条目，输入框提示显示现状文字（与冻结页面同一取值）。</summary>
    private void ReloadEditors()
    {
        _dirty = false;
        _editorsScroll.Controls.Clear();
        _editors = [];
        _discardButton.Enabled = false;
        if (Current() is not { } check || _supplement is null)
        {
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
            var hairline = Ui.Hairline(box.Text);
            var row = grid.RowCount;
            grid.RowCount = row + 1;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            hairline.Margin = new Padding(0, 0, 0, UiTokens.Xs);
            grid.Controls.Add(hairline, 0, row);
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
        UpdateCounts();
    }

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
            SetStatus(ex.Message, UiTokens.WarnText);
            _saving = false;
            MarkDirty();
            return;
        }
        catch (Exception ex)
        {
            SetStatus("保存补充失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            _saving = false;
            MarkDirty();
            return;
        }
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
