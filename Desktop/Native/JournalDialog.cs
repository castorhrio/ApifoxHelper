using System.Drawing;
using System.Text.Json.Nodes;
using System.Windows.Forms;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务日志（只读）：按时间顺序展示任务目录 journal.jsonl 的逐条记录——注释回写与同步执行的
/// 每一步（意图先落盘、结果随后追加），数据由 <c>CommentPlanService</c> 与 <c>SyncExecutor</c> 写入。
/// 本对话框无任何写入动作，也不重发、不回滚：它只回答「做到哪一步了」。
/// 读取失败时给一颗「重试」——那颗按钮只重新读一次磁盘上的日志，不重发任何动作（第一百五十七轮）。
/// 状态表：读取失败=内联错误 + 唯一行动按钮「重试」；没有记录=空态说明；正常=说明行 + 计数行 + 未结束动作提示 + 记录清单
/// （打开即选中最后一条——最后一行就是做到的那一步；只有意图、没有结果的那几行在摘要格标「结果未知」）；
/// 筛到零行=关键词框留着并说明下一步，计数行与未结束动作那句话仍是整份日志的数字与名字
/// （见 <see cref="PopulateList"/>）。
/// 三格文字、计数行、说明行、未结束动作那句话、清单标记与关键词匹配字段取 <see cref="JournalEntries"/> 一份
/// （与测试工程单一口径）。
/// 记录清单支持点击列头排序（第一百零四轮，口径见 <see cref="JournalSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始，排序只改呈现顺序——计数行与未结束动作那句话仍按整份日志统计。
/// </summary>
internal sealed class JournalDialog : Form
{
    /// <summary>
    /// 标识列（DESIGN.md §6「列表行」的 Ctrl+C 条款）：首列是时间（同一秒可能有多条、认不出是哪一步），
    /// 第 2 列是事件名（同一事件重复出现），这一行在屏幕上被读作的名字是「摘要」，故声明第 3 列。
    /// </summary>
    private const int SummaryColumn = 2;

    private readonly TaskStore _tasks;
    private readonly string _taskId;

    /// <summary>
    /// 读取失败态的唯一行动按钮（<see cref="LoadFailureState"/> 口径）：动作回到 <see cref="LoadJournal"/>
    /// 同一个入口，不复制读取逻辑。只在读取失败时出现——「没有记录」与「筛到零行」不是失败，
    /// 重试解决不了它们（同一时刻只给一个下一步）。
    /// </summary>
    private readonly Button _retry = Ui.Ghost(LoadFailureState.RetryLabel);

    private readonly Label _counts = Ui.Mono();
    private readonly Label _unfinished = Ui.Caption();
    private readonly TextBox _keyword = Ui.Input();
    private readonly ListView _list = Ui.List(identityColumn: SummaryColumn);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    /// <summary>整份日志（读一次，筛选只改可见行）：行载荷就是这里的同一个节点，选中按它恢复。</summary>
    private List<JsonNode> _lines = [];

    /// <summary>只有意图、没有结果的动作标识：清单标记与关键词匹配共用同一份集合。</summary>
    private HashSet<string> _unfinishedIds = new(StringComparer.Ordinal);

    // 列排序状态（ListSort 口径，第一百零四轮）：未排序时保持日志自身的顺序（按时间追加）。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;

    /// <summary>建列时的原标题快照：表头方向标记每次按它重算，反复刷新不叠加。</summary>
    private string[] _titles = [];

    public JournalDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _taskId = task.Id;

        Text = "任务日志 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(960, 620));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("时间", 150);
        _list.Columns.Add("事件", 150);
        _list.Columns.Add("摘要", 640);
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _keyword.PlaceholderText = ListFilter.JournalSearchPlaceholder;
        _keyword.TextChanged += (_, _) => PopulateList();

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;

        var caption = Ui.Caption(JournalEntries.Caption);
        caption.Dock = DockStyle.Fill;
        caption.TextAlign = ContentAlignment.MiddleLeft;

        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headRow.Controls.Add(caption, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);

        // 筛选行形状取工厂（§6「列表筛选」）：这一屏没有筛选下拉，走只有关键词框的那一支；
        // 计数在表头行（与说明行同排），故 counts 传 null。
        var filterRow = Ui.FilterRow(_keyword, counts: null);

        // 未结束动作那句话：没有这样的动作时整行不显示（不显示一句「没有」）。
        _unfinished.Dock = DockStyle.Top;
        _unfinished.ForeColor = UiTokens.BadText;
        _unfinished.Visible = false;

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        _retry.Click += (_, _) => LoadJournal();
        _retry.Visible = false;
        _retry.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(_retry);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次未结束提示、筛选行、标题行，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(buttonRow);
        Controls.Add(_unfinished);
        Controls.Add(filterRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadJournal();
    }

    /// <summary>
    /// 读一次磁盘上的日志并铺开界面（打开时与失败后点「重试」走同一个入口，不复制读取逻辑）。
    /// 失败态按 <see cref="LoadFailureState"/> 口径：一句可操作文案（指名「重试」）+ 唯一行动按钮。
    /// 此前这里写的是「请关闭后重新执行一次再试」——把「再读一次」交给用户手动重开窗口，
    /// 屏上没有任何可点的入口，与 DESIGN.md §7「禁用不得移除下一步」相违（第一百五十七轮）。
    /// </summary>
    private void LoadJournal()
    {
        List<JsonNode> lines;
        try
        {
            lines = FileStore.ReadJsonLines(_tasks.FilePath(_taskId, TaskFiles.Journal));
        }
        catch (Exception ex)
        {
            ShowLoadFailure(Redactor.Redact(ex.Message));
            return;
        }
        if (lines.Count == 0)
        {
            // 没有记录不是失败：重试解决不了它，这一屏的下一步是去执行一次（那句话由空态给出）。
            _lines = [];
            _list.Items.Clear();
            ShowHint(JournalEntries.EmptyText, UiTokens.Muted);
            return;
        }

        _lines = lines;
        // 有记录可筛：关键词框可用（口径见 ListFilter.KeywordEnabled）。
        _keyword.Enabled = ListFilter.KeywordEnabled(true);
        _hint.Visible = false;
        // 读成功即退出失败态：否则重试成功后仍停在失败态（清单有行、屏上却还说读取失败）。
        _retry.Visible = false;
        _counts.Text = JournalEntries.CountsLine(lines.Count);
        var unfinished = JournalEntries.UnfinishedActions(lines);
        var unfinishedText = JournalEntries.UnfinishedCaption(unfinished);
        _unfinished.Text = unfinishedText;
        _unfinished.Visible = unfinishedText.Length > 0;
        // 未结束动作的标识只用来在清单上认出那几行（不上屏幕）：那句话给名字与下一步，清单在原处标出来。
        _unfinishedIds = unfinished.Select(action => action.ActionId).ToHashSet(StringComparer.Ordinal);
        PopulateList();
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「只读清单打开时的键盘归属」，第八十八轮）：
    /// 这一屏打开即选中最后一条记录（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
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
    /// 关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行——计数行与未结束动作那句话按整份日志
    /// 统计与列出，不随关键词变化（「记录 N 条」与「有 N 个动作只有写入意图」正是要找那几条的凭据，
    /// 被一屏的空抹掉等于把结果藏起来）。筛到零行不是「没有记录」：说清这一屏为什么空，下一步就在那个框上
    /// （这一屏没有筛选下拉，故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」）。
    /// 选中按行载荷（同一个日志节点）恢复——筛一下再筛回来仍停在原来那一步；恢复不到时落在**新筛选的最后一行**：
    /// 这一屏的答案在末尾（打开即选中最后一条），筛出「写入失败」之后要看的是最近的那一次失败，
    /// 与另十一张长清单「落在新筛选第一条」的差别就在这里（那些屏的答案是第一条）。
    /// </summary>
    private void PopulateList()
    {
        var selectedLine = Ui.SelectedItem(_list)?.Tag as JsonNode;
        // 筛选先于排序（ListFilter → ListSort，与另八处同一顺序）：筛掉的行不参与排序，
        // 排序键取 JournalSort 单一口径（与三格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(_lines, _keyword.Text, line => JournalEntries.KeywordFields(line, _unfinishedIds)),
            _sortColumn, _sortDescending,
            (line, column) => JournalSort.Key(
                JournalEntries.TimeKey(line),
                JournalEntries.EventCell(line),
                JournalEntries.SummaryCell(line, _unfinishedIds),
                column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var line in visible)
        {
            // 三格文字取 JournalEntries 一份（时间、事件名、摘要），与计数行、未结束动作那句话同源——
            // 这一屏的清单与提示讲的是同一份日志，各拼一遍必然有一处先漂。
            var cells = JournalEntries.Row(line, _unfinishedIds);
            var row = new ListViewItem(cells[0]) { Tag = line };
            row.SubItems.Add(cells[1]);
            row.SubItems.Add(cells[2]);
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_lines.Count, visible.Count))
        {
            _hint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.JournalNoun);
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        if (_list.Items.Count == 0) return;
        var target = selectedLine is null
            ? null
            : _list.Items.OfType<ListViewItem>().FirstOrDefault(item => ReferenceEquals(item.Tag, selectedLine));
        // 打开即选中最后一条：这一屏要回答的是「做到哪一步」，最后一行就是答案
        // （与代码证据清单「打开即选中第一条」同一纪律——打开就有一行被标出来，只是这一屏的答案在末尾）。
        // 选中经 Ui.SelectItem 唯一入口（Selected + Focused + 滚入可见区），与鼠标点同一行同一条路。
        Ui.SelectItem(target ?? _list.Items[^1]);
    }

    /// <summary>
    /// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给关键词框并选中已有关键词、
    /// 框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部记录）。
    /// 与另十一张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
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
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百零四轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="PopulateList"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改日志内容，选中仍按行载荷（同一个日志节点）恢复：
    /// 这一屏的重建本来就按节点找回原来那一行，排序后不必另传 keep（与另八处「把当前条目交回去」同一结果）。
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
    /// 读取失败态：清单不留旧行（这一屏刚证明「磁盘上的日志读不出来」，继续显示旧行等于把不可信的内容
    /// 当成现状）、未结束动作那句话一起收掉（它讲的是旧数据），提示区换成失败文案并给出「重试」。
    /// 原因仍如实给出（已脱敏），下一步由文案指名那颗按钮。
    /// </summary>
    private void ShowLoadFailure(string detail)
    {
        _lines = [];
        _list.Items.Clear();
        _unfinishedIds = new HashSet<string>(StringComparer.Ordinal);
        ShowHint(
            LoadFailureState.FailureMessage(LoadFailureState.Journal.Title, detail, LoadFailureState.Journal.Hint),
            UiTokens.BadText,
            canRetry: true);
    }

    /// <summary>
    /// 提示区（空态 / 筛到零行 / 读取失败共用）。<paramref name="canRetry"/> 只在读取失败时为真：
    /// 另外两种状态重试解决不了，显示「重试」等于给一个点了没用的按钮（同一时刻只给一个下一步）。
    /// </summary>
    private void ShowHint(string message, Color color, bool canRetry = false)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _retry.Visible = LoadFailureState.RetryVisible(canRetry);
        _list.Visible = false;
        // 整份数据不可得：关键词框一起停（§7「禁用 = 控件禁用 + 一句话说明缺什么」——
        // 缺什么与下一步由上面那句话给出，框里敲字筛不到任何东西）。口径见 ListFilter.KeywordEnabled。
        _keyword.Enabled = ListFilter.KeywordEnabled(false);
        _counts.Text = "";
        _unfinished.Visible = false;
    }
}
