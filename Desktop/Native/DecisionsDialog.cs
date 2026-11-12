using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Contracts;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 处理差异确认（F09 决策部分）：高风险变化逐项确认、说明冲突选择、模型绑定与共享模型更新确认。
/// 条目来自任务目录 diff.json（检查落盘）；已保存决策读自 decisions.json。保存经 DecisionService 的唯一校验口径
/// （与 PUT /api/tasks/{id}/decisions 路由同一服务），内容变化时递增任务版本、使旧检查与旧计划过期。
/// 与冻结页面同一口径：高风险变化只逐项确认接口侧条目（accepted；模型侧的结构差异经共享模型更新确认处理）；
/// 说明冲突选保留远端/使用目标；候选与同名多个模型绑定远端 ID 或新建。不写源码、不访问 Apifox。
/// 状态表：尚未检查=空态指引；读取失败=内联错误与下一步（已保存决策读取失败时停用保存，避免覆盖）；
/// 没有待处理条目=如实说明；筛选下没有条目=提示换筛选且筛选器保持可用（恢复路径本身不禁用）；
/// 正常=计数行 + 筛选 + 列表 + 详情与动作。保存后需重新检查，决策才会生效。
/// 条目清单支持点击列头排序（第九十九轮，口径见 <see cref="DecisionSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始、表头带方向标记；排序状态留在本次打开期间，关闭即忘。
/// </summary>
internal sealed class DecisionsDialog : DpiAwareForm
{
    private enum EntryKind
    {
        Risk,
        Conflict,
        Binding,
        ModelChange,
    }

    private sealed class Entry(EntryKind kind, string resource, string location)
    {
        public EntryKind Kind { get; } = kind;
        public string Resource { get; } = resource;
        public string Location { get; } = location;
        public DiffItem? Item { get; init; }
        public DocChange? Change { get; init; }
        public ModelCheck? Model { get; init; }
        public bool Pending { get; set; }
        public string StateText { get; set; } = "";
        public string StateTone { get; set; } = "normal";
    }

    private static readonly string[] FilterChoices = ["全部", "待处理", "高风险变化", "说明冲突", "模型绑定", "共享模型确认"];

    private readonly TaskStore _tasks;
    private readonly CheckTask _task;
    private readonly DecisionService _decisionSaves;

    private DecisionStore _decisions = new();
    private List<Entry> _entries = [];

    // 列排序状态（ListSort 口径，第九十九轮）：未排序时保持条目自身的构造顺序。
    // 排序状态留在本次打开期间（切筛选、敲关键词、保存后重建都保持），关闭即忘——
    // 跨启动记忆只有两个常驻页面（第五十五轮那两个页键），
    // 这一屏每次打开看的是这一次的检查结果，不为它另开一个记忆文件。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _titles;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    // 关键词框：条目按接口×位置展开，几十上百条里找「上次那个接口」只能逐行扫（冻结页面的差异视图
    // 一直有关键词框）。与两个主页面的筛选框同一形态、同一匹配口径（ListFilter）。
    private readonly TextBox _keyword = Ui.Input();
    // 标识列：首列是「类型」（接口/模型），许多行同值、认不出是哪一条；名称格在「资源」列——Ctrl+C 交出去的是它。
    private const int ResourceColumn = 1;

    private readonly ListView _list = Ui.List(identityColumn: ResourceColumn);
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly Button _confirmButton;
    private readonly Button _useTargetButton;
    private readonly Button _keepRemoteButton;
    private readonly Button _clearButton;
    private readonly ComboBox _bindingCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly Button _bindingSaveButton;
    // 读取失败态的唯一行动按钮（LoadFailureState 口径）：动作回到 LoadAll 同一个入口。
    // 放在底部动作行：失败时其余动作一起停用，行里只剩「重试」与「关闭」两颗可点。
    private readonly Button _retry = Ui.Ghost(LoadFailureState.RetryLabel);
    private readonly Label _status = Ui.StatusLine();
    // 状态行宿主：与两个主页面同一接线口径——放不下时右侧给「查看完整错误」入口。
    // 对话框的状态行宽度随对话框固定，长错误同样会被截断，此前只有悬停提示。
    private readonly StatusLineHost _statusHost;
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public DecisionsDialog(TaskStore tasks, CheckTask task, DecisionService decisionSaves)
    {
        _tasks = tasks;
        _task = task;
        _decisionSaves = decisionSaves;

        Text = "处理差异确认 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(960, 680));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("类型", 100);
        _list.Columns.Add("资源", 260);
        _list.Columns.Add("位置", 180);
        _list.Columns.Add("状态", 110);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);

        _confirmButton = Ui.Primary("确认纳入");
        _confirmButton.Click += (_, _) => ConfirmCurrent();
        _useTargetButton = Ui.Ghost("使用目标");
        _useTargetButton.Click += (_, _) => ChooseConflict("useTarget");
        _keepRemoteButton = Ui.Ghost("保留远端");
        _keepRemoteButton.Click += (_, _) => ChooseConflict("keepRemote");
        _clearButton = Ui.Ghost("撤销确认");
        _clearButton.Click += (_, _) => ClearCurrent();
        _bindingSaveButton = Ui.Ghost("保存绑定");
        _bindingSaveButton.Click += (_, _) => SaveBinding();
        _bindingCombo.SelectedIndexChanged += (_, _) =>
        {
            if (Current() is { Kind: EntryKind.Binding } entry) UpdateBindingSaveEnabled(entry);
        };

        _retry.Click += (_, _) => LoadAll();
        _retry.Visible = false;
        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _filter.Items.AddRange(FilterChoices);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter(Current());

        _keyword.PlaceholderText = ListFilter.DecisionSearchPlaceholder;
        _keyword.TextChanged += (_, _) => ApplyFilter(Current());

        // 筛选行形状取工厂（§6「列表筛选」）：五张长清单同一份，不各拼一遍。
        var filterRow = Ui.FilterRow(_filter, _keyword, _counts);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailGrid = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Sm) };
        detailGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddCaptionRow(detailGrid, "选中条目的差异详情与处理说明");
        AddCaptionRow(detailGrid, "决策保存后任务内容即变化：已有检查结果与同步计划过期，重新检查后决策才会体现在合并结果与计划中。");
        AddBoxRow(detailGrid, Ui.Hairline(_detail), 190);
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        AddCaptionRow(detailGrid, _statusHost);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 9, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        for (var i = 0; i < 6; i++) actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _useTargetButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _keepRemoteButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _clearButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _bindingCombo.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _bindingSaveButton.Margin = new Padding(UiTokens.Xs, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_confirmButton, 0, 0);
        actionRow.Controls.Add(_useTargetButton, 1, 0);
        actionRow.Controls.Add(_keepRemoteButton, 2, 0);
        actionRow.Controls.Add(_clearButton, 3, 0);
        actionRow.Controls.Add(_bindingCombo, 4, 0);
        actionRow.Controls.Add(_bindingSaveButton, 5, 0);
        // 第 6 列是 Percent 100 的「把按钮推到右边」那一列：空着就够，不往里放占位控件——
        // 默认尺寸的 Panel 是 200×100，而本行 AutoSize，那一格的高度会变成整行的高度（第二百二十二轮实测 120px 对按钮 40px）。
        _retry.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_retry, 7, 0);
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
        LoadAll();
    }

    /// <summary>
    /// 读取检查结果与已保存决策（打开时与失败后点「重试」走同一个入口，不复制读取逻辑）。
    /// 两种失败态按 <see cref="LoadFailureState"/> 口径：一句可操作文案（指名「重试」）+ 唯一行动按钮。
    /// 此前这里写的是「请关闭后在任务页重新检查再试」「请关闭后重试」——把「再读一次」交给用户
    /// 手动重开窗口，屏上没有任何可点的入口，与 DESIGN.md §7「禁用不得移除下一步」相违。
    /// 「尚未检查」与「没有需要处理的差异确认」不是失败态：重试解决不了它们，下一步在任务页与筛选器上，
    /// 故这两种状态不显示「重试」（同一时刻只给一个下一步）。
    /// </summary>
    private void LoadAll()
    {
        _retry.Visible = false;
        CheckResult? result;
        try
        {
            result = _tasks.Read<CheckResult>(_task.Id, TaskFiles.Diff);
        }
        catch (Exception ex)
        {
            _filter.Enabled = false;
            _keyword.Enabled = false;
            ShowHint(
                LoadFailureState.FailureMessage(LoadFailureState.Decisions.Title, Redactor.Redact(ex.Message), LoadFailureState.Decisions.Hint),
                UiTokens.BadText,
                canRetry: true);
            return;
        }
        if (result is null)
        {
            _filter.Enabled = false;
            _keyword.Enabled = false;
            ShowHint("尚未检查。先在任务页点「开始检查」：差异确认的条目（高风险变化、说明冲突、模型绑定）来自检查结果。", UiTokens.Muted);
            return;
        }
        if (!ReloadDecisions(out var error))
        {
            // 读取不到已保存决策时不提供保存动作，避免用空决策覆盖既有选择。
            _filter.Enabled = false;
            _keyword.Enabled = false;
            ShowHint(
                LoadFailureState.FailureMessage(LoadFailureState.Decisions.SavedTitle, error, LoadFailureState.Decisions.SavedHint),
                UiTokens.BadText,
                canRetry: true);
            return;
        }
        _entries = BuildEntries(result);
        RefreshStates();
        if (_entries.Count == 0)
        {
            _filter.Enabled = false;
            _keyword.Enabled = false;
            ShowHint("没有需要处理的差异确认：检查结果中没有高风险变化、说明冲突、待绑定的模型与待确认的共享模型更新。", UiTokens.Muted);
            return;
        }
        _hint.Visible = false;
        _filter.Enabled = true;
        _keyword.Enabled = true;
        UpdateCounts();
        ApplyFilter();
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「审阅清单打开时的键盘归属」，第八十九轮）：
    /// 这一屏打开即选中第一条待处理条目（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的
    /// 第一个可聚焦控件上——方向键动的是别的控件、Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 三个审阅视图里只有这一屏打开时自动选中一行（注释候选与响应契约打开时不选中任何一行，没有落点可交，
    /// 故不接此口径），而它正是逐条清队列的那一屏：方向键该从那一行继续。
    /// 交键盘只在 <see cref="Ui.KeepKeyboardOnQueue"/> 一处（判定在 <see cref="ListFocus"/>：选中行与焦点行是同一行才交），
    /// 本对话框不自己写 .Focus()、不选中、不滚动、不重建清单。
    /// 放在 OnShown 而不是 OnLoad：OnLoad 时窗体尚未显示，Focus() 的落点取决于框架在激活时怎么选初始焦点；
    /// OnShown 是窗体首次显示之后，是设置初始焦点的常规时机（真机落点随 G-D5 复核）。
    /// 打开时那一屏是提示或空态（读取失败、尚未检查、没有待处理条目）就没有选中行，不动键盘——
    /// 那一步的下一步在提示与按钮上，不在清单上。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.KeepKeyboardOnQueue(_list);
    }

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

    /// <summary>从任务目录重新读取已保存决策；读取失败时返回 false 并带出脱敏错误。</summary>
    private bool ReloadDecisions(out string error)
    {
        try
        {
            _decisions = _tasks.Read<DecisionStore>(_task.Id, TaskFiles.Decisions) ?? new DecisionStore();
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            _decisions = new DecisionStore();
            error = Redactor.Redact(ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 条目清单与冻结页面同一口径：高风险变化只列接口侧条目；说明冲突含接口与模型；
    /// 模型绑定列候选与同名多个；共享模型更新确认列有变化的已映射模型。
    /// </summary>
    private static List<Entry> BuildEntries(CheckResult result)
    {
        var entries = new List<Entry>();
        foreach (var item in result.Items.Where(i => i.NeedsDecision && !i.ResourceKey.StartsWith("model:", StringComparison.Ordinal)))
        {
            entries.Add(new Entry(EntryKind.Risk, item.ResourceKey, item.Location) { Item = item });
        }
        foreach (var check in result.Endpoints)
        {
            foreach (var change in check.DocChanges.Where(c => c.Decision == "conflict"))
            {
                entries.Add(new Entry(EntryKind.Conflict, check.Key, change.Location) { Change = change });
            }
        }
        foreach (var model in result.Models)
        {
            foreach (var change in model.DocChanges.Where(c => c.Decision == "conflict"))
            {
                entries.Add(new Entry(EntryKind.Conflict, model.Name, change.Location) { Change = change });
            }
            if (model.MappingState is "candidate" or "ambiguous")
            {
                entries.Add(new Entry(EntryKind.Binding, model.Name, TaskDisplay.MappingStateText(model.MappingState)) { Model = model });
            }
            if (model.Status == "changed")
            {
                entries.Add(new Entry(EntryKind.ModelChange, model.Name, "共享模型") { Model = model });
            }
        }
        return entries;
    }

    /// <summary>按已保存决策重算每个条目的状态：待处理判断只看 decisions，不改动检查结果。</summary>
    private void RefreshStates()
    {
        foreach (var entry in _entries)
        {
            var (text, tone, pending) = entry.Kind switch
            {
                EntryKind.Risk => DecisionItemState(_decisions.Items.GetValueOrDefault(entry.Item!.Id)),
                EntryKind.Conflict => DocChoiceState(_decisions.DocChoices.GetValueOrDefault(entry.Change!.Id)),
                EntryKind.Binding => BindingState(_decisions.ModelBindings.GetValueOrDefault(entry.Model!.Name)),
                EntryKind.ModelChange => DecisionItemState(_decisions.Items.GetValueOrDefault("model:" + entry.Model!.Name)),
                _ => ("", "normal", true),
            };
            entry.StateText = text;
            entry.StateTone = tone;
            entry.Pending = pending;
        }
    }

    private static (string Text, string Tone, bool Pending) DecisionItemState(string? value) => value switch
    {
        "accepted" => ("已确认", "ok", false),
        "excluded" => ("已排除", "muted", false),
        _ => ("待确认", "warn", true),
    };

    private static (string Text, string Tone, bool Pending) DocChoiceState(string? value) => value switch
    {
        "useTarget" => ("使用目标", "accent", false),
        "keepRemote" => ("保留远端", "muted", false),
        _ => ("待选择", "warn", true),
    };

    private static (string Text, string Tone, bool Pending) BindingState(string? value) => value switch
    {
        null or "" => ("待绑定", "warn", true),
        _ => (TaskDisplay.ModelBindingText(value), "accent", false),
    };

    private static string KindText(EntryKind kind) => kind switch
    {
        EntryKind.Risk => "高风险变化",
        EntryKind.Conflict => "说明冲突",
        EntryKind.Binding => "模型绑定",
        EntryKind.ModelChange => "共享模型确认",
        _ => "",
    };

    /// <summary>提示态的共享出口：隐藏列表并停用动作，不触碰筛选器——筛选器启停由调用方按状态决定
    /// （打开期无数据时先禁用再提示；运行期筛选为空时必须保持可用，见 ApplyFilter）。
    /// <c>keepAggregate</c> 为真表示「条目还在，只是当前筛选一条都不命中」：计数行的聚合数字留着——
    /// 「共 5 项 · 待处理 0」正是清完队列的凭据，与两页「筛选出 0 个任务（共 42 个）」同一如实口径。</summary>
    private void ShowHint(string message, Color color, bool keepAggregate = false, bool canRetry = false)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        // 失败态的唯一行动按钮：文案里指名的那颗按钮必须真的在屏上（第四十二轮纪律）。
        _retry.Visible = canRetry;
        EnableActions(null);
        if (!keepAggregate) _counts.Text = "";
        _detail.Text = "";
    }

    /// <summary>计数行按全部条目统计（不随筛选变化），等宽数字不跳宽。</summary>
    private void UpdateCounts()
    {
        _counts.Text = $"共 {_entries.Count} 项 · 待处理 {_entries.Count(e => e.Pending)}"
            + $" · 高风险 {_entries.Count(e => e.Kind == EntryKind.Risk)}"
            + $" · 说明冲突 {_entries.Count(e => e.Kind == EntryKind.Conflict)}"
            + $" · 模型绑定 {_entries.Count(e => e.Kind == EntryKind.Binding)}"
            + $" · 共享模型 {_entries.Count(e => e.Kind == EntryKind.ModelChange)}";
    }

    /// <summary>
    /// 一行是否留在清单里：分类筛选与关键词都是「且」。关键词经共享口径（与两个主页面同一份匹配规则），
    /// 匹配的是这一行在屏幕上被读作的那三格——资源、位置、类型，不另造一套字段。
    /// </summary>
    private bool Matches(Entry entry) =>
        MatchesKind(entry) && ListFilter.Matches(_keyword.Text, entry.Resource, entry.Location, KindText(entry.Kind));

    private bool MatchesKind(Entry entry) => _filter.SelectedIndex switch
    {
        1 => entry.Pending,
        2 => entry.Kind == EntryKind.Risk,
        3 => entry.Kind == EntryKind.Conflict,
        4 => entry.Kind == EntryKind.Binding,
        5 => entry.Kind == EntryKind.ModelChange,
        _ => true,
    };

    private void ApplyFilter(Entry? keep = null, int handledIndex = ListAdvance.None)
    {
        // 筛选先于排序（ListFilter → ListSort，与两个主页面、检查结果、接口追踪明细同一顺序）：
        // 筛掉的行不参与排序，排序状态与筛选状态各自留在本对话框；
        // 排序键取 DecisionSort 单一口径（与单元格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            _entries.Where(Matches).ToList(),
            _sortColumn, _sortDescending,
            (entry, column) => DecisionSort.Key(KindText(entry.Kind), entry.Resource, entry.Location, entry.StateText, column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in visible)
        {
            var item = new ListViewItem(KindText(entry.Kind)) { Tag = entry, UseItemStyleForSubItems = false };
            item.SubItems.Add(entry.Resource).Font = UiTokens.Mono.Value;
            item.SubItems.Add(entry.Location);
            var state = item.SubItems.Add(entry.StateText);
            state.ForeColor = ToneColor(entry.StateTone);
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (_list.Items.Count == 0)
        {
            // 筛选下没有条目时，筛选器本身是提示语「换一个筛选再试」所指的恢复路径，必须保持可用——
            // 此前随共享提示出口一并禁用，点开一个空分类或确认完「待处理」最后一条后对话框锁死在本状态，只能关闭重开。
            var filteredEmpty = ListFilter.IsFilteredEmpty(_entries.Count, _list.Items.Count);
            _filter.Enabled = true;
            _keyword.Enabled = true;
            ShowHint(filteredEmpty
                ? ListFilter.FilteredEmptyHint(ListFilter.DecisionNoun, ListFilter.IsActive(_keyword.Text))
                : "没有需要处理的差异确认。",
                UiTokens.Muted,
                keepAggregate: filteredEmpty);
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        if (keep is not null)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (ReferenceEquals(item.Tag, keep))
                {
                    Ui.SelectItem(item);
                    return;
                }
            }
            // 刚处理的那条已不属于当前筛选、从清单上消失：选中续到接替他位置的下一行，
            // 不回跳队首——在「待处理」筛选下逐条清队列时，回跳等于每保存一条都要重新找位置。
            Ui.AdvanceAfterRemoval(_list, handledIndex);
            return;
        }
        Ui.SelectItem(_list.Items[0]);
    }

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第九十九轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="ApplyFilter"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改检查结果与决策，选中仍按条目自身恢复（重建前记下、重建后找回）。
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

    private Entry? Current() => Ui.SelectedItem(_list)?.Tag as Entry;

    private void ShowDetail()
    {
        if (Current() is not { } entry)
        {
            // 有行可选却没选中：缺什么由详情区说；一行都没有时空的是筛选，那一屏归提示出口（口径见 ListSelection）。
            _detail.Text = ListSelection.ShouldExplainMissingSelection(_list.Items.Count)
                ? ListSelection.MissingSelectionHint(ListFilter.DecisionNoun)
                : "";
            EnableActions(null);
            return;
        }
        var lines = new List<string>();
        switch (entry.Kind)
        {
            case EntryKind.Risk:
                var item = entry.Item!;
                lines.Add("高风险变化：" + entry.Resource);
                lines.Add("位置：" + entry.Location);
                lines.Add("变化：" + TaskDisplay.DiffChangeText(item.Change) + " · 风险：" + TaskDisplay.DiffRiskText(item.Risk)
                    + (string.IsNullOrWhiteSpace(item.Reason) ? "" : " · " + item.Reason));
                lines.Add("原值（远端）：" + Short(item.Before));
                lines.Add("新值（目标）：" + Short(item.After));
                lines.Add("状态：" + entry.StateText + "。确认后该变化可随接口进入同步计划；不确认则该接口不会纳入本次同步。");
                break;
            case EntryKind.Conflict:
                var change = entry.Change!;
                lines.Add("说明冲突：" + entry.Resource);
                lines.Add("位置：" + entry.Location + " · 来源：" + TaskDisplay.DocOriginText(change.Origin));
                lines.Add("远端说明：" + (change.RemoteValue ?? "—"));
                lines.Add("目标说明：" + (change.TargetValue ?? "—"));
                lines.Add("状态：" + entry.StateText + "。选择后需重新检查，合并结果才会更新；未选择前该接口不能进入同步计划。");
                break;
            case EntryKind.Binding:
                var model = entry.Model!;
                lines.Add("模型绑定：" + model.Name);
                lines.Add("映射状态：" + TaskDisplay.MappingStateText(model.MappingState)
                    + " · 候选远端模型：" + (model.Candidates.Count > 0 ? string.Join("、", model.Candidates) : "—"));
                lines.Add("用于接口：" + (model.UsedBy.Count > 0 ? string.Join("、", model.UsedBy) : "—"));
                if (model.Blockers.Count > 0) lines.Add("阻断：" + string.Join("；", model.Blockers));
                lines.Add("状态：" + entry.StateText + "。首次绑定同名模型需要确认；同名多个时需指定其中一个 ID，或选择新建模型。");
                break;
            case EntryKind.ModelChange:
                var changed = entry.Model!;
                lines.Add("共享模型更新：" + changed.Name + "（" + TaskDisplay.ModelStatusText(changed.Status) + "）");
                lines.Add("用于接口：" + (changed.UsedBy.Count > 0 ? string.Join("、", changed.UsedBy) : "—"));
                lines.Add("范围外影响：" + changed.ImpactOutOfScope.Count + " 个接口" + (changed.ImpactComplete ? "" : "（分支概览不完整，影响可能未列全）"));
                foreach (var impact in changed.ImpactOutOfScope)
                {
                    lines.Add("  · " + impact.Id + "（" + impact.Key + " " + impact.Name + "）");
                }
                if (changed.Blockers.Count > 0) lines.Add("阻断：" + string.Join("；", changed.Blockers));
                lines.Add("状态：" + entry.StateText + "。共享模型更新会影响范围外接口，确认后才纳入同步计划。");
                break;
        }
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
        EnableActions(entry);
    }

    /// <summary>动作可用性按条目种类与当前决策切换；控件位置固定，不跳位。待处理的条目才能确认；已保存决策的条目才能撤销。</summary>
    private void EnableActions(Entry? entry)
    {
        _confirmButton.Enabled = entry is { Kind: EntryKind.Risk or EntryKind.ModelChange } && entry.Pending;
        _confirmButton.Text = entry?.Kind switch
        {
            EntryKind.Risk => "确认纳入",
            EntryKind.ModelChange => "确认更新",
            _ => "确认",
        };
        _useTargetButton.Enabled = entry is { Kind: EntryKind.Conflict } && _decisions.DocChoices.GetValueOrDefault(entry.Change!.Id) != "useTarget";
        _keepRemoteButton.Enabled = entry is { Kind: EntryKind.Conflict } && _decisions.DocChoices.GetValueOrDefault(entry.Change!.Id) != "keepRemote";
        _clearButton.Enabled = entry is { Kind: EntryKind.Conflict } && _decisions.DocChoices.ContainsKey(entry.Change!.Id)
            || entry is { Kind: EntryKind.Risk or EntryKind.ModelChange } && !entry.Pending;
        _clearButton.Text = entry is { Kind: EntryKind.Conflict } ? "清除选择" : "撤销确认";
        _bindingCombo.Enabled = entry is { Kind: EntryKind.Binding };
        if (entry is { Kind: EntryKind.Binding } binding)
        {
            LoadBindingCombo(binding);
        }
        else
        {
            _bindingCombo.Items.Clear();
            _bindingSaveButton.Enabled = false;
        }
    }

    /// <summary>
    /// 绑定下拉的选项与选中项取共享口径 <see cref="BindingChoices"/>：已保存但不在本次候选里的绑定
    /// 按原值追加一项并注明，不显示成「未确认」（否则同一屏的状态列与下拉对同一件事说两种话，
    /// 且保存按钮会因「未确认 ≠ 已保存值」亮起，一次点击静默删掉用户没打算改的绑定）。
    /// </summary>
    private void LoadBindingCombo(Entry entry)
    {
        _bindingCombo.Items.Clear();
        var options = BindingChoices.Build(entry.Model?.Candidates ?? [], SavedBinding(entry));
        foreach (var choice in options.Choices) _bindingCombo.Items.Add(choice);
        _bindingCombo.SelectedIndex = options.SelectedIndex;
        UpdateBindingSaveEnabled(entry);
    }

    private string? SelectedBindingValue() => _bindingCombo.SelectedItem is BindingChoice choice ? choice.Value : null;

    /// <summary>该模型已保存的绑定值（没有绑定即空串，与 decisions.json 的取值同一口径）。</summary>
    private string SavedBinding(Entry entry) => _decisions.ModelBindings.GetValueOrDefault(entry.Model?.Name ?? "") ?? "";

    private void UpdateBindingSaveEnabled(Entry entry) =>
        _bindingSaveButton.Enabled = BindingChoices.CanSave(SelectedBindingValue(), SavedBinding(entry));

    private void ConfirmCurrent()
    {
        if (Current() is not { } entry) return;
        if (entry.Kind == EntryKind.Risk)
        {
            var id = entry.Item!.Id;
            SaveDecision("高风险变化已确认（" + entry.Resource + " " + entry.Location + "）", body => body.Items[id] = "accepted");
        }
        else if (entry.Kind == EntryKind.ModelChange)
        {
            var key = "model:" + entry.Model!.Name;
            SaveDecision("共享模型更新已确认（" + entry.Model.Name + "）", body => body.Items[key] = "accepted");
        }
    }

    private void ChooseConflict(string choice)
    {
        if (Current() is not { Kind: EntryKind.Conflict } entry) return;
        var id = entry.Change!.Id;
        var label = choice == "useTarget" ? "使用目标" : "保留远端";
        SaveDecision("说明冲突已选择" + label + "（" + entry.Resource + " " + entry.Location + "）", body => body.DocChoices[id] = choice);
    }

    private void ClearCurrent()
    {
        if (Current() is not { } entry) return;
        switch (entry.Kind)
        {
            case EntryKind.Risk:
                var id = entry.Item!.Id;
                SaveDecision("已撤销确认（" + entry.Resource + " " + entry.Location + "）", body => body.Items.Remove(id));
                break;
            case EntryKind.Conflict:
                var changeId = entry.Change!.Id;
                SaveDecision("已清除说明选择（" + entry.Resource + " " + entry.Location + "）", body => body.DocChoices.Remove(changeId));
                break;
            case EntryKind.ModelChange:
                var key = "model:" + entry.Model!.Name;
                SaveDecision("已撤销共享模型更新确认（" + entry.Model.Name + "）", body => body.Items.Remove(key));
                break;
        }
    }

    private void SaveBinding()
    {
        if (Current() is not { Kind: EntryKind.Binding } entry) return;
        var name = entry.Model!.Name;
        var value = SelectedBindingValue();
        SaveDecision("模型绑定已保存（" + name + "）", body =>
        {
            if (string.IsNullOrEmpty(value)) body.ModelBindings.Remove(name);
            else body.ModelBindings[name] = value;
        });
    }

    /// <summary>保存一个决策动作：经共享 DecisionService 校验并写任务数据；内容变化时任务版本递增（旧检查与旧计划过期）。</summary>
    private async void SaveDecision(string description, Action<DecisionStore> mutate)
    {
        if (Current() is not { } entry) return;
        // 被处理那一行在旧清单上的显示序号：保存后它可能不再属于当前筛选而消失，续接选中要用（ListAdvance 口径）。
        var handledAt = Ui.SelectedIndex(_list) ?? ListAdvance.None;
        EnableActions(null);
        var body = CloneStore(_decisions);
        mutate(body);
        int before;
        try
        {
            before = _tasks.Get(_task.Id).Revision;
            await _decisionSaves.SaveAsync(_task.Id, body);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.WarnText);
            EnableActions(entry);
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("保存决策失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            EnableActions(entry);
            return;
        }
        if (IsDisposed) return;
        if (!ReloadDecisions(out var error))
        {
            // 写入已成功，重新读取失败时如实说明，不显示成保存失败。
            SetStatus("决策已保存，但重新读取失败：" + error, UiTokens.BadText);
            return;
        }
        var changed = _tasks.Get(_task.Id).Revision > before;
        RefreshStates();
        UpdateCounts();
        ApplyFilter(entry, handledAt);
        // 与候选、契约两处审阅同一纪律：保存是点按钮完成的，交回键盘之后方向键才接着处理下一条；
        // 被拒与重新读取失败的路径都在上面返回，不抢用户正在改的那一格。
        Ui.KeepKeyboardOnQueue(_list);
        SetStatus(changed
            ? "已保存：" + description + "。决策变化已使检查结果与同步计划过期，重新检查后生效。"
            : "决策内容没有变化，未修改任务。", UiTokens.OkText);
    }

    private static DecisionStore CloneStore(DecisionStore store) => new()
    {
        Items = new Dictionary<string, string>(store.Items, StringComparer.Ordinal),
        DocChoices = new Dictionary<string, string>(store.DocChoices, StringComparer.Ordinal),
        ModelBindings = new Dictionary<string, string>(store.ModelBindings, StringComparer.Ordinal),
    };

    /// <summary>差异值摘要：JSON 截断展示，只用于对照阅读，完整值以检查结果与计划为准。</summary>
    private static string Short(JsonNode? node)
    {
        if (node is null) return "—";
        var text = node.ToJsonString();
        return text.Length <= 160 ? text : text[..160] + "…";
    }

    private void SetStatus(string message, Color color)
    {
        _statusHost.SetText(message, color);
    }

    private static Color ToneColor(string tone) => tone switch
    {
        "ok" => UiTokens.OkText,
        "warn" => UiTokens.WarnText,
        "bad" => UiTokens.BadText,
        "muted" => UiTokens.Muted,
        "accent" => UiTokens.Accent,
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
