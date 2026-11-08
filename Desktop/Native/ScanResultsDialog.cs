using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释缺口清单：展示扫描得到的全部注释位置（DocSlot），按状态筛选与关键词筛选，逐条显示现状、覆盖接口与证据数；
/// 顶部注释覆盖率行与分析等级、扫描时间来自扫描落盘的 symbols.json 与 source-manifest.json；
/// 选中条目可打开其代码证据（EvidenceDialog，只读），也可手工填写说明或删除失效 param 标签（经既有
/// DraftService.AddManualAsync 写任务数据成为候选，不写源码、不访问 Apifox；候选在「查看注释候选」审阅）；
/// 「分析警告与降级」打开只读清单（扫描警告、未解析符号与不可回写的外部类型——降级如实可见）；
/// 「接口追踪明细」打开逐接口的调用链追踪只读视图（读取方法、未解析调用、外部调用与预算超限）。
/// 关键词框与另两张长清单同一形态、同一匹配口径（ListFilter）：这一屏是全应用最长的一张清单
/// （一次扫描的全部注释位置），只有状态下拉时找「上次那个位置」只能逐行扫；
/// 状态表：无扫描产物=引导先扫描；读取失败=内联错误与下一步；正常=覆盖率行 + 计数行 + 列表（空列表即全部有效）；
/// 打开即选中第一条（第九十一轮：详情区与三个选中动作对着第一条，键盘在 OnShown 交给清单）；
/// 筛到零行=列表区一句话与「换一个筛选」这一步，覆盖率行、计数行与两个聚合入口照旧（见 <see cref="ShowFilteredEmpty"/>；
/// 它与「整份数据不可得」是两种空，后者才随提示把那些入口一起收掉）。
/// </summary>
internal sealed class ScanResultsDialog : Form
{
    private static readonly string[] FilterChoices = ["全部位置", "缺失", "空", "已失效", "有效", "其他问题"];

    private readonly TaskStore _tasks;
    private readonly DraftService _drafts;
    private readonly CheckTask _task;

    private readonly Label _counts = Ui.Mono();
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    // 关键词框：注释缺口清单是全应用最长的一张清单（一次扫描的全部注释位置，方案 §4.2 记录千级接口分支），
    // 此前只有状态下拉时找「上次那个位置」只能逐行扫。与另两张长清单同一形态、同一匹配口径（ListFilter）。
    private readonly TextBox _keyword = Ui.Input();
    private readonly Label _coverage = Ui.Caption();
    private readonly Button _warningsButton;
    private readonly Button _traceButton;
    private readonly ListView _list = Ui.List();
    private readonly Button _evidenceButton;
    private readonly Button _manualButton;
    private readonly Button _removeStaleButton;
    private readonly TextBox _detail = Ui.Preview(multiline: true, caption: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _status = Ui.StatusLine();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private List<DocSlot> _slots = [];
    private List<string> _degradations = [];

    public ScanResultsDialog(TaskStore tasks, DraftService drafts, CheckTask task)
    {
        _tasks = tasks;
        _drafts = drafts;
        _task = task;

        Text = "注释缺口 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 640);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("位置（说明点）", 360);
        _list.Columns.Add("标注", 60);
        _list.Columns.Add("状态", 110);
        _list.Columns.Add("角色", 60);
        _list.Columns.Add("文件:行", 320);
        _evidenceButton = Ui.Ghost("查看代码证据");
        _evidenceButton.Enabled = false;
        _evidenceButton.Click += (_, _) => ShowEvidence(Ui.SelectedItem(_list)?.Tag as DocSlot);
        _manualButton = Ui.Ghost("手工填写说明");
        _manualButton.Enabled = false;
        _manualButton.Click += (_, _) => ShowManualFill();
        _removeStaleButton = Ui.Ghost("删除失效标签");
        _removeStaleButton.Enabled = false;
        _removeStaleButton.Click += (_, _) => AddRemoveStale();
        _warningsButton = Ui.Ghost(ScanOverview.Headline(0));
        _warningsButton.Visible = false;
        _warningsButton.Click += (_, _) => ShowDegradations();
        // 接口追踪明细与降级清单同族：同一次扫描落盘的只读透明视图（证据文件里的逐接口调用链追踪）。
        _traceButton = Ui.Ghost("接口追踪明细");
        _traceButton.Visible = false;
        _traceButton.Click += (_, _) => ShowTrace();
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        // 双击=查看证据经共享入口：命中哪行看哪行的证据（行自身 Tag，不借当前选中），双击列头与空白区不动作。
        Ui.ActivateItem(_list, item => ShowEvidence(item.Tag as DocSlot));

        _filter.Items.AddRange(FilterChoices);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => ApplyFilter();

        _keyword.PlaceholderText = ListFilter.SlotSearchPlaceholder;
        _keyword.TextChanged += (_, _) => ApplyFilter();

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        // 筛选行形状取工厂（§6「列表筛选」）：五张长清单同一份，不各拼一遍。
        var filterRow = Ui.FilterRow(_filter, _keyword, _counts);
        // 注释覆盖率行（分析等级与扫描时间在数据缺失时整段省略）；无扫描产物时整行隐藏。
        filterRow.RowCount = 2;
        filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        filterRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _coverage.Margin = new Padding(0, UiTokens.Xs, 0, 0);
        filterRow.Controls.Add(_coverage, 0, 1);
        filterRow.SetColumnSpan(_coverage, 5);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailHost = Ui.Hairline(_detail);
        detailHost.Dock = DockStyle.Bottom;
        detailHost.Height = 96;

        var statusHost = new Panel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Xs, 0, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);
        buttonRow.Controls.Add(_evidenceButton);
        buttonRow.Controls.Add(_manualButton);
        buttonRow.Controls.Add(_removeStaleButton);
        buttonRow.Controls.Add(_warningsButton);
        buttonRow.Controls.Add(_traceButton);
        _removeStaleButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _manualButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _warningsButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _traceButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次状态行、详情框、筛选行，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailHost);
        Controls.Add(statusHost);
        Controls.Add(buttonRow);
        Controls.Add(filterRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ScanOutput? output;
        SourceManifest? manifest;
        try
        {
            output = _tasks.Read<ScanOutput>(_task.Id, TaskFiles.Symbols);
            // 清单与扫描结果由同一次扫描写入：任一读取失败都按扫描结果不可读处理，不显示半份总览。
            manifest = _tasks.Read<SourceManifest>(_task.Id, TaskFiles.Manifest);
        }
        catch (Exception ex)
        {
            ShowHint("读取扫描结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后重新扫描再试。", UiTokens.BadText);
            return;
        }
        if (output is null)
        {
            ShowHint("没有扫描结果。请先在任务里运行源码扫描，再查看注释缺口。", UiTokens.Muted);
            return;
        }
        _slots = output.Slots;
        _hint.Visible = false;
        _filter.Enabled = true;
        _keyword.Enabled = true;
        _list.Visible = true;
        _coverage.Visible = true;
        _coverage.Text = ScanOverview.CoverageText(
            output.Coverage.Valid, output.Coverage.InheritDoc, output.Coverage.Total,
            manifest?.AnalysisLevel, manifest?.ScannedAt);
        _degradations = ScanOverview.DegradationItems(manifest?.Warnings, output.UnresolvedSymbols, output.ExternalTypes);
        _warningsButton.Text = ScanOverview.Headline(_degradations.Count);
        _warningsButton.Visible = _degradations.Count > 0;
        _traceButton.Visible = true;
        UpdateCounts();
        ApplyFilter();
        // 打开即选中第一条（第九十一轮）：这一屏的详情区与三个选中动作（查看代码证据、手工填写说明、删除失效标签）
        // 都挂在选中上，打开时一行都没选中就是一屏没有解释的灰——而另八张清单（五张只读长清单、三个审阅视图）
        // 打开时都落在第一条，同一个动作在别处有、在这一处没有，属同壳不同行为。
        // 落点经 Ui.SelectFirstOnOpen 唯一入口（有行才落，空态不索引不存在的第一行）；
        // 只落选中、不取键盘——键盘在 OnShown 交给清单。
        Ui.SelectFirstOnOpen(_list);
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「注释缺口清单打开即选中第一条」，第九十一轮）：
    /// 这一屏打开即选中第一条注释位置（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
    /// 方向键动的是别的控件、Ctrl+C 也复制不到那一行，「打开就有一行被标出来」只成立了一半。
    /// 交键盘只在 <see cref="Ui.KeepKeyboardOnQueue"/> 一处（判定在 <see cref="ListFocus"/>：选中行与焦点行是同一行才交），
    /// 本对话框不自己写 .Focus()、不选中、不滚动、不重建清单。
    /// 放在 OnShown 而不是 OnLoad：OnLoad 时窗体尚未显示，Focus() 的落点取决于框架在激活时怎么选初始焦点。
    /// 打开时那一屏是提示或空态（读取失败、没有扫描产物、筛到零行）就没有选中行，不动键盘——
    /// 那一步的下一步在提示与按钮上，不在清单上。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Ui.KeepKeyboardOnQueue(_list);
    }

    /// <summary>打开接口追踪明细（只读）：逐接口的调用链读取、未解析调用、外部调用与预算超限，来自 evidence.json。</summary>
    private void ShowTrace()
    {
        using var dialog = new EndpointTraceDialog(_tasks, _task);
        dialog.ShowDialog(this);
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _filter.Enabled = false;
        _keyword.Enabled = false;
        _evidenceButton.Enabled = false;
        _manualButton.Enabled = false;
        _removeStaleButton.Enabled = false;
        _coverage.Visible = false;
        _coverage.Text = "";
        _warningsButton.Visible = false;
        _traceButton.Visible = false;
        _degradations = [];
        _counts.Text = "";
        _detail.Text = "";
    }

    /// <summary>计数行按全部位置统计（不随筛选变化），等宽数字不跳宽。</summary>
    private void UpdateCounts()
    {
        int valid = 0, missing = 0, empty = 0, stale = 0, other = 0;
        foreach (var slot in _slots)
        {
            switch (slot.Status)
            {
                case "valid": valid++; break;
                case "missing": missing++; break;
                case "empty": empty++; break;
                case "stale": stale++; break;
                default: other++; break;
            }
        }
        _counts.Text = _slots.Count == 0
            ? "没有注释位置"
            : $"位置 {_slots.Count} · 有效 {valid} · 缺失 {missing} · 空 {empty} · 失效 {stale} · 其他 {other}";
    }

    private bool Matches(DocSlot slot) => _filter.SelectedIndex switch
    {
        1 => slot.Status == "missing",
        2 => slot.Status == "empty",
        3 => slot.Status == "stale",
        4 => slot.Status == "valid",
        5 => slot.Status is "inheritDoc" or "inheritDocUnresolved" or "invalidXml" or "unsupported",
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

    private void ApplyFilter()
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        // 关键词先经共享口径（与两个主页面、另两张长清单同一份匹配规则：词间「且」、字段间「或」、忽略大小写），
        // 状态筛选在其后——两个条件都是「且」，顺序不影响结果，但匹配规则只有一份。
        var visible = ListFilter.Apply(_slots, _keyword.Text, SlotText.KeywordFields).Where(Matches).ToList();
        foreach (var slot in visible)
        {
            var item = new ListViewItem(SlotText.Name(slot)) { Tag = slot };
            item.SubItems.Add(SlotText.Tag(slot));
            var status = item.SubItems.Add(TaskDisplay.SlotStatusText(slot.Status));
            item.UseItemStyleForSubItems = false;
            status.ForeColor = ToneColor(TaskDisplay.SlotTone(slot.Status));
            item.SubItems.Add(TaskDisplay.SlotRoleText(slot.Role));
            var location = item.SubItems.Add(SlotText.Location(slot));
            location.Font = UiTokens.Mono.Value;
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (ListFilter.IsFilteredEmpty(_slots.Count, _list.Items.Count))
        {
            // 筛到零行不是「没有注释位置」：这一屏给共享口径的那句话，整份数据的聚合入口留着（口径见 ListFilter）。
            ShowFilteredEmpty();
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        // 重建之后再问一次详情：重建路径不落点（落点只在打开这一处，见 OnLoad——关键词框每敲一个字都重建，
        // 落点会把用户正在筛的那一屏选中重置掉），判在清空之前量到的是上一轮筛选的行数，
        // 「缺什么」那句话会在该说话的时候沉默（第一次进屏时清单还是空的）。
        ShowDetail();
    }

    /// <summary>
    /// 筛到零行的自有形状：只把列表区换成那句话。筛选器不动（保持可用——那句话指名的下一步就是它，第四十二轮），
    /// 覆盖率行、计数行的聚合数字与「分析警告与降级」「接口追踪明细」两个入口也一概不动：
    /// 它们讲的是整份扫描，不因当前筛选不命中而失效，收掉它们等于把这一步之后还能看的东西一起删掉
    /// （§7「禁用不得移除下一步」）。与 <see cref="ShowHint"/> 的分别正在这里——那种空是整份数据不可得，
    /// 那时这些数字与入口本身也不可得，随提示一起收掉才是如实。
    /// </summary>
    private void ShowFilteredEmpty()
    {
        // 有关键词框时那句话的下一步有两个真实入口（关键词或筛选），关键词为空时仍只指筛选器
        // （文字不指向界面上按不动的控件——第四十二轮同一条纪律）。
        _hint.Text = ListFilter.FilteredEmptyHint(ListFilter.SlotNoun, ListFilter.IsActive(_keyword.Text));
        _hint.ForeColor = UiTokens.Muted;
        _hint.Visible = true;
        _list.Visible = false;
        // 详情区与三个选中动作经既有的「无选中」路径收掉，不靠控件在清空时自发的事件：
        // 清单刚重建，此刻没有选中行，留着上一行的文字与按钮就是把不存在的那一行当成还能操作。
        ShowDetail();
    }

    private void ShowDetail()
    {
        if (Ui.SelectedItem(_list) is null)
        {
            // 有行可选却没选中：缺什么由详情区说；一行都没有时空的是筛选，那一屏归提示出口（口径见 ListSelection）。
            _detail.Text = ListSelection.ShouldExplainMissingSelection(_list.Items.Count)
                ? ListSelection.MissingSelectionHint(ListFilter.SlotNoun)
                : "";
            _evidenceButton.Enabled = false;
            _manualButton.Enabled = false;
            _removeStaleButton.Enabled = false;
            return;
        }
        if (Ui.SelectedItem(_list)?.Tag is not DocSlot slot) return;
        _evidenceButton.Enabled = slot.EvidenceIds.Count > 0;
        // 手工填写对扫描结果中仍存在的位置可用（服务的候选校验按位置执行）；删除失效标签只对失效 param 可用。
        _manualButton.Enabled = true;
        _removeStaleButton.Enabled = slot.Status == "stale";
        var lines = new List<string>
        {
            "符号：" + slot.Display + "（" + TaskDisplay.SymbolKindText(slot.SymbolKind) + "）",
            "现状：" + (string.IsNullOrWhiteSpace(slot.ExistingText) ? "（无说明）" : slot.ExistingText),
            "覆盖接口：" + (slot.EndpointKeys.Count > 0 ? string.Join("、", slot.EndpointKeys) : "—"),
            "证据：" + slot.EvidenceIds.Count + " 条",
        };
        if (slot.Issues.Count > 0) lines.Add("问题：" + string.Join("；", slot.Issues));
        if (!slot.Editable && slot.NotEditableReason is { } reason) lines.Add("不可回写原因：" + reason);
        _detail.Text = string.Join(Environment.NewLine, lines);
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
    }

    /// <summary>打开注释位置的代码证据（只读）：数据来自扫描落盘的 evidence.json，无写入动作；按钮与双击共用这一份行内门控。</summary>
    private void ShowEvidence(DocSlot? slot)
    {
        if (slot is null || slot.EvidenceIds.Count == 0) return;
        using var dialog = new EvidenceDialog(_tasks, _task, slot);
        dialog.ShowDialog(this);
    }

    /// <summary>打开分析警告与降级清单（只读）：扫描警告、未解析符号与不可回写的外部类型，无写入动作。</summary>
    private void ShowDegradations()
    {
        using var dialog = new ScanWarningsDialog(_task, _degradations);
        dialog.ShowDialog(this);
    }

    /// <summary>手工填写说明：经既有 AddManualAsync 写任务数据成为候选（占位文字等服务校验拒绝原因原样内联），不写源码。</summary>
    private void ShowManualFill()
    {
        if (Ui.SelectedItem(_list)?.Tag is not DocSlot slot) return;
        using var dialog = new ManualDraftDialog(_task, slot);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        RunManualAdd(slot, dialog.Request, _manualButton);
    }

    /// <summary>删除失效 param 标签（作为候选）：只对失效位置可用；实际删除在「注释回写」确认后执行。</summary>
    private void AddRemoveStale()
    {
        if (Ui.SelectedItem(_list)?.Tag is not DocSlot slot) return;
        RunManualAdd(slot, new ManualCandidateRequest { SlotId = slot.Id, Action = "removeStale" }, _removeStaleButton);
    }

    /// <summary>写手工候选：执行期间禁用发起按钮（防重复提交），服务拒绝原因原样内联，不弹窗。</summary>
    private async void RunManualAdd(DocSlot slot, ManualCandidateRequest request, Button trigger)
    {
        trigger.Enabled = false;
        try
        {
            await _drafts.AddManualAsync(_task.Id, request, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (!IsDisposed) SetStatus(slot.Display + "：" + ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) SetStatus("添加候选失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
            return;
        }
        finally
        {
            if (!IsDisposed) trigger.Enabled = true;
        }
        if (!IsDisposed) SetStatus(slot.Display + "：" + DraftEntries.ManualAddedText, UiTokens.OkText);
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
}

/// <summary>
/// 手工填写说明对话框：为所选注释位置输入纯文本说明成为候选。
/// 文字校验与生成/导入共用 DraftService.ValidateText 单一口径，校验不通过内联显示、不关闭对话框。
/// </summary>
internal sealed class ManualDraftDialog : Form
{
    private readonly DocSlot _slot;
    private readonly TextBox _text = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _status = Ui.StatusLine();

    /// <summary>确认后的候选请求；Action 恒为 set，删除失效标签走 ScanResultsDialog 的直通入口。</summary>
    public ManualCandidateRequest Request { get; private set; } = new();

    public ManualDraftDialog(CheckTask task, DocSlot slot)
    {
        _slot = slot;
        Text = "手工填写说明 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(560, 400);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var okButton = Ui.Primary("添加为候选");
        okButton.Click += (_, _) => Confirm();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = okButton;
        CancelButton = cancelButton;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, UiTokens.Sm) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.Controls.Add(PositionCaption(slot), 0, 0);
        var guidance = Ui.Caption("纯文本说明，不用 XML 标签；保存时自动转义 XML 字符。共享类型的说明只写可复用的字段含义。");
        guidance.Margin = new Padding(0, UiTokens.Sm, 0, UiTokens.Xs);
        grid.Controls.Add(guidance, 0, 1);
        var textHost = Ui.Field(_text);
        textHost.Dock = DockStyle.Fill;
        grid.Controls.Add(textHost, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（添加为候选）在最右。
        buttons.Controls.Add(okButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        Controls.Add(grid);
        Controls.Add(statusHost);
        Controls.Add(buttons);
    }

    private static Label PositionCaption(DocSlot slot)
    {
        var caption = Ui.Body(SlotText.Name(slot) + "（" + SlotText.Tag(slot) + "）");
        caption.AutoSize = true;
        return caption;
    }

    private void Confirm()
    {
        // 与生成、导入同一份文字校验口径；拒绝文案原样内联显示。
        if (DraftService.ValidateText(_text.Text) is { } error)
        {
            _status.Text = error + "。";
            _status.ForeColor = UiTokens.BadText;
            return;
        }
        Request = new ManualCandidateRequest { SlotId = _slot.Id, Text = _text.Text, Action = "set" };
        DialogResult = DialogResult.OK;
    }
}

/// <summary>
/// 分析警告与降级清单（只读）：扫描警告、未解析符号与不可回写的外部类型逐行列出；
/// 条目口径在 ScanOverview（与冻结页面同一前缀），数据来自扫描落盘的任务文件，无写入动作。
/// </summary>
internal sealed class ScanWarningsDialog : Form
{
    public ScanWarningsDialog(CheckTask task, IReadOnlyList<string> items)
    {
        Text = ScanOverview.Headline(items.Count) + " — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(720, 480);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        var guidance = Ui.Caption("扫描中对这些位置降级处理：未解析符号不按同名猜实现，外部类型上的注释位置不可回写。");
        guidance.AutoSize = true;
        guidance.Margin = new Padding(0, 0, 0, UiTokens.Sm);

        var text = Ui.Preview(multiline: true, scrollBars: ScrollBars.Vertical);
        text.Dock = DockStyle.Fill;
        text.Text = string.Join(Environment.NewLine, items);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(text));

        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, UiTokens.Sm) };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.Controls.Add(guidance, 0, 0);
        content.Controls.Add(listHost, 0, 1);

        Controls.Add(content);
        Controls.Add(buttonRow);
    }
}
