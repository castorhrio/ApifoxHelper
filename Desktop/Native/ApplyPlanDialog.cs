using System.Drawing;
using System.Text;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释回写：从已接受且未回写的候选生成注释计划，逐文件预览统一 diff，
/// 确认后回写源码（覆盖式写入，原生确认对话框；计划哈希绑定，源码变化即过期），
/// 或把补丁导出为 .patch 文件交由开发处理。计划生成、回写、补丁都走既有 CommentPlanService
/// （只改 /// 注释行、写入前核对文件与证据指纹、失败即停、部分完成如实记录），本层不加写入逻辑。
/// 状态表：生成预览中=状态行 + 主行动禁用；无候选/不可用=空态指引；可回写=计数行 + 文件列表 + 差异；
/// 回写中=状态行 + 按钮禁用；部分完成/失败/过期=结果列与状态行如实呈现（过期可重新生成计划）；
/// 筛到零行=关键词框留着并说明下一步，计数行仍是整份计划的数字（见 <see cref="PopulateList"/>）。
/// 差异预览区带说明行（<see cref="ApplyPlanEntries.DiffCaption"/>，第九十四轮）：这块框是什么、改的是什么、
/// 与回写的关系，不靠用户先点一行再猜。
/// 两张清单都支持点击列头排序（文件清单第一百零五轮、跳过清单第一百零八轮，
/// 口径见 <see cref="ApplyPlanSort"/> 与 <see cref="ListSort"/>）：
/// 同一列再点反向、换一列从升序开始、表头带方向标记，排序只改呈现顺序。
/// 两张清单各有一个关键词框（文件清单第七十九轮、跳过清单第一百一十二轮，口径见 <see cref="ListFilter"/>）：
/// 输入即筛选、只改可见行，计数行仍按整份计划统计；同一页两个框，Enter 与 Esc 看焦点各接一次，
/// Ctrl+F 只交给文件清单那个框（与同步预览两页各一个框同一取舍）。
/// </summary>
internal sealed class ApplyPlanDialog : DpiAwareForm
{
    private readonly TaskStore _tasks;
    private readonly CommentPlanService _plans;
    private readonly CheckTask _task;
    private readonly string _taskId;

    private readonly Label _planStatus = Ui.StatusText();
    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = Ui.List();
    private readonly ListView _skippedList = Ui.List();
    private readonly TextBox _keyword = Ui.Input();
    private readonly TextBox _skippedKeyword = Ui.Input();
    private readonly Panel _skippedHost;
    private readonly Label _skippedHint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };
    private readonly TextBox _diff = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Label _diffCaption;
    private readonly Button _applyButton;
    private readonly Button _patchButton;
    private readonly Button _regenButton;
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

    private CommentPlan? _plan;
    private bool _busy;

    /// <summary>
    /// 冒烟器用的确认替身（第一百三十二轮）：默认就是那个原生确认对话框（默认按钮在「取消」）。
    /// 存在的理由：确认框是模态的，无人值守的冒烟器点不动它；把「确认」收成一个可替换的委托，
    /// 冒烟器就能真的点「确认回写」按钮、走同一条回写链路，而不是绕过对话框直接调服务。
    /// 产品行为不变——默认实现仍是同一个 MessageBox，文案与默认按钮一字未改。
    /// </summary>
    internal Func<CommentPlan, bool> ConfirmApply { get; set; }

    /// <summary>冒烟器用：最近一次「生成计划」的异步任务（<see cref="OnLoad"/> 与「重新生成计划」都会置位）。</summary>
    internal Task? PendingLoad { get; private set; }

    /// <summary>冒烟器用：最近一次「确认回写」的异步任务（按钮点击后由冒烟器等待完成）。</summary>
    internal Task? PendingApply { get; private set; }

    /// <summary>冒烟器用：当前界面上的计划（只读，供断言计划非空与哈希稳定）。</summary>
    internal CommentPlan? Plan => _plan;

    // 列排序状态（ListSort 口径，第一百零五轮）：未排序时保持计划自身的文件顺序。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    private readonly string[] _titles;

    // 跳过清单的列排序状态（ListSort 口径，第一百零八轮）：两张清单各记各的列与方向，
    // 排文件清单不会把跳过清单也排了；未排序时保持计划自身的跳过顺序。
    private int _skippedSortColumn = ListSort.None;
    private bool _skippedSortDescending;
    private readonly string[] _skippedTitles;

    /// <summary>
    /// 本屏两个关键词框的登记表（<see cref="FilterGestures"/>）：主框是文件清单那个（Ctrl+F 的落点），
    /// 跳过清单那个随后登记——Enter 与 Esc 看焦点各接一次，一次按键只有一个含义
    /// （跳过清单的 Esc 不会去清文件清单的关键词，反之亦然）。判定在 <see cref="FilterGesturePlan"/> 一份。
    /// </summary>
    private readonly FilterGestures _gestures;

    public ApplyPlanDialog(CommentPlanService plans, TaskStore tasks, CheckTask task)
    {
        _plans = plans;
        _tasks = tasks;
        _task = task;
        _taskId = task.Id;
        _gestures = new FilterGestures(_keyword, _list).Then(_skippedKeyword, _skippedList);

        Text = "注释回写 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(960, 680));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("源码文件", 400);
        _list.Columns.Add("回写范围", 170);
        _list.Columns.Add("注释处数", 90);
        _list.Columns.Add("回写结果", 110);
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.SelectedIndexChanged += (_, _) => ShowDiff();
        _keyword.PlaceholderText = ListFilter.ApplyPlanSearchPlaceholder;
        _keyword.TextChanged += (_, _) => PopulateList();

        _skippedList.Columns.Add("跳过的位置", 300);
        _skippedList.Columns.Add("原因", 560);
        _skippedTitles = _skippedList.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径，第一百零八轮）：同一列再点反向，换一列从升序开始；只改呈现顺序。
        _skippedList.ColumnClick += (_, e) => SortSkipped(e.Column);
        // 跳过清单的关键词框（DESIGN.md §6「列表筛选」，第一百一十二轮）：同一个对话框里文件清单自第七十九轮起
        // 就有关键词框、跳过清单没有，而这一张是整份计划里没能生成修改的注释位置（真实项目几十条），
        // 要按原因处理就得先把同一原因的集中看——属同壳不同行为。
        _skippedKeyword.PlaceholderText = ListFilter.SkippedSearchPlaceholder;
        _skippedKeyword.TextChanged += (_, _) =>
        {
            if (_plan is { } plan) PopulateSkipped(plan);
        };
        _skippedHost = new Panel { Dock = DockStyle.Bottom, Height = 180, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        var skippedCaption = Ui.Caption("跳过的位置：这些注释位置本次不写回源码，每条为什么写在「原因」列；改完点「重新生成计划」。");
        skippedCaption.Dock = DockStyle.Top;
        var skippedInner = Ui.Hairline(_skippedList);
        skippedInner.Dock = DockStyle.Fill;
        skippedInner.Height = 110;
        // 筛选行取工厂（第六十八轮立的形状）：这一屏只有关键词框、没有筛选下拉，计数在表头行，故 counts 传 null。
        // 底色随本区（Bg）：工厂给的是对话框的 Panel 底，这一块是页面底。
        var skippedFilterRow = Ui.FilterRow(_skippedKeyword, counts: null);
        skippedFilterRow.BackColor = UiTokens.Bg;
        skippedFilterRow.Padding = new Padding(0, 0, 0, UiTokens.Sm);
        _skippedHost.Controls.Add(skippedInner);
        _skippedHost.Controls.Add(_skippedHint);
        _skippedHost.Controls.Add(skippedFilterRow);
        _skippedHost.Controls.Add(skippedCaption);
        _skippedHint.Visible = false;
        _skippedHost.Visible = false;

        // 确认的默认实现：原生确认对话框（默认按钮在「取消」，防回车误触）。冒烟器可替换本委托，
        // 产品路径不变——文案、按钮与默认按钮与接入本委托之前逐字一致。
        // 正文与标题取 ApplyPlanEntries 一份落点（第一百三十三轮）：这段字是「只改注释、不动执行代码」
        // 在界面上的唯一表述，收成纯函数后 Linux 侧可断言，界面不就地拼。
        ConfirmApply = plan =>
        {
            var writable = plan.Files.Where(f => f.WriteAllowed).ToList();
            var edits = writable.Sum(f => f.Edits.Count);
            return MessageBox.Show(this,
                ApplyPlanEntries.ConfirmApplyText(writable.Count, edits),
                ApplyPlanEntries.ConfirmApplyTitle,
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.OK;
        };

        _applyButton = Ui.Primary("确认回写");
        _applyButton.Enabled = false;
        _applyButton.Click += (_, _) => RunApply();
        _patchButton = Ui.Ghost("导出补丁");
        _patchButton.Enabled = false;
        _patchButton.Click += (_, _) => ExportPatch();
        _regenButton = Ui.Ghost("重新生成计划");
        _regenButton.Enabled = false;
        _regenButton.Click += (_, _) => PendingLoad = LoadPlanAsync();
        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;
        _planStatus.TextAlign = ContentAlignment.MiddleLeft;
        _planStatus.Text = "正在生成注释回写预览…";
        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headRow.Controls.Add(_planStatus, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);

        // 筛选行形状取工厂（§6「列表筛选」）：这一屏没有筛选下拉，走只有关键词框的那一支；
        // 计数在表头行（与计划状态同排），故 counts 传 null。
        var filterRow = Ui.FilterRow(_keyword, counts: null);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        // 差异预览区 = 说明行 + 发丝边只读框（与检查结果、核对传播、代码证据、同步预览的底部预览区同一形状）：
        // 这一屏的核心是逐文件预览统一 diff，而这块框此前没有标题，用户得先点一行再猜它是什么。
        // 说明行取 ApplyPlanEntries.DiffCaption 一份（不在这里就地拼字），且不随选中、筛选与提示变化——
        // 它讲的是这块框是什么，不是这一行是什么。
        var diffInner = Ui.Hairline(_diff);
        diffInner.Dock = DockStyle.Fill;
        var diffHost = new Panel { Dock = DockStyle.Bottom, Height = 210, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        _diffCaption = Ui.Caption(ApplyPlanEntries.DiffCaption);
        _diffCaption.Dock = DockStyle.Top;
        diffHost.Controls.Add(diffInner);
        diffHost.Controls.Add(_diffCaption);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0) };
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusHost);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 5, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _patchButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _regenButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_applyButton, 0, 0);
        actionRow.Controls.Add(_patchButton, 1, 0);
        actionRow.Controls.Add(_regenButton, 2, 0);
        // 第 3 列是 Percent 100 的「把按钮推到右边」那一列：空着就够，不往里放占位控件——
        // 默认尺寸的 Panel 是 200×100，而本行 AutoSize，那一格的高度会变成整行的高度（第二百二十二轮实测 120px 对按钮 40px）。
        actionRow.Controls.Add(closeButton, 4, 0);

        // 停靠按集合倒序生效：标题行最先停靠（顶部），其次筛选行、按钮行（最底部）、状态行、差异框、跳过列表，文件列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(_skippedHost);
        Controls.Add(diffHost);
        Controls.Add(statusHost);
        Controls.Add(actionRow);
        Controls.Add(filterRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PendingLoad = LoadPlanAsync();
    }

    /// <summary>生成（或重新生成）注释计划：读工作区、核对文件与证据指纹，产物为 comment-plan.json 与 comments.patch。</summary>
    internal async Task LoadPlanAsync()
    {
        if (_busy) return;
        SetBusy(true);
        _hint.Visible = false;
        _statusHost.SetText("", UiTokens.Text);
        _planStatus.Text = "正在生成注释回写预览…（读取项目并核对文件指纹）";
        _planStatus.ForeColor = UiTokens.Text;
        CommentPlan plan;
        try
        {
            plan = await _plans.CreateAsync(_taskId, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetBusy(false);
            ShowHint(ex.Message, UiTokens.WarnText);
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetBusy(false);
            ShowHint("生成注释回写预览失败：" + Redactor.Redact(ex.Message) + "。请关闭后重试；仍失败时重新扫描。", UiTokens.BadText);
            return;
        }
        if (IsDisposed) return;
        SetBusy(false);
        ShowPlan(plan);
    }

    /// <summary>
    /// 按计划刷新计数行、跳过清单与按钮可用性；文件清单经 <see cref="PopulateList"/> 按关键词呈现
    /// （回写后再次调用会补上逐文件结果列）。
    /// 计划**首次到达**时把键盘交给文件清单（DESIGN.md §6「列表行」，第九十三轮）：这一屏的清单在计划生成
    /// 完成之后才出现，出现时已选中第一条、差异已显示，而键盘此前不在清单上——方向键动的是别的控件、
    /// Ctrl+C 也复制不到那一行，「有一行被标出来」只成立了一半（另十张清单在打开或这一页出现时都交，
    /// 第八十八至九十二轮）。交键盘经 <see cref="Ui.KeepKeyboardOnQueue"/> 同一处入口，判定仍是
    /// `ListFocus` 那一份（选中行与焦点行是同一行才交，不猜第一行）。
    /// **只在首次到达那一次交**（<c>_plan is null</c> 判一次）：重新生成计划、回写后按磁盘计划重读、
    /// 回写被拒后重读都走本方法，那些是重建路径——那一屏的下一步是看结果或换关键词，交键盘会把
    /// 用户正在改的那一格抢走（与第五十八轮「保存被拒的路径不交回」、第九十二轮「重建路径不交」同一取舍）。
    /// 计划没有可写入的文件时清单是空的、也没有选中行，判定答「不交」，那一屏由提示与跳过原因说话。
    /// </summary>
    private void ShowPlan(CommentPlan plan)
    {
        // 这一屏是不是刚出现：计划首次到达那一次才算（重建路径上 _plan 早已有值）。
        var opening = _plan is null;
        _plan = plan;
        _hint.Visible = false;
        _planStatus.Text = "计划状态：" + TaskDisplay.PlanStatusText(plan.Status) + (plan.Status == "expired" && plan.Files.Count == 0 ? "" : "（预览后源码再变化则计划过期）");
        _planStatus.ForeColor = ToneColor(TaskDisplay.PlanTone(plan.Status));
        // 计数行按整份计划统计、不随关键词变化（「跳过 N」正是要找的那批的凭据）。
        _counts.Text = ApplyPlanEntries.CountsLine(plan.Files.Count, plan.Files.Sum(f => f.Edits.Count), plan.Skipped.Count);

        // 跳过清单的关键词框：整份计划没有跳过位置时停用（没有可筛的东西，下一步不在这框上），
        // 有数据可筛时恢复可用。口径见 ListFilter.KeywordEnabled。
        _skippedKeyword.Enabled = ListFilter.KeywordEnabled(plan.Skipped.Count > 0);
        PopulateSkipped(plan);

        if (plan.Files.Count == 0)
        {
            // 计划没有可写入的文件：列表区如实说明，跳过原因保持在下方可见。
            _hint.Text = plan.Message ?? "没有可生成的注释修改，请查看跳过原因。";
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            _diff.Text = "";
            // 整份数据没有文件：关键词框一起停（没有可筛的东西，下一步不在这框上）。口径见 ListFilter.KeywordEnabled。
            _keyword.Enabled = ListFilter.KeywordEnabled(false);
        }
        else
        {
            // 有数据可筛：关键词框可用（口径见 ListFilter.KeywordEnabled）。
            _keyword.Enabled = ListFilter.KeywordEnabled(true);
            PopulateList();
        }

        var writable = plan.Files.Count(f => f.WriteAllowed);
        _applyButton.Enabled = plan.Status == "ready" && writable > 0;
        _patchButton.Enabled = plan.Files.Count > 0;
        _regenButton.Enabled = plan.Status is "expired" or "failed" or "partial";
        // 交键盘排在方法末尾：清单、差异与按钮都已就位，此后没有别的赋值会再动焦点。
        if (opening) Ui.KeepKeyboardOnQueue(_list);
    }

    /// <summary>
    /// 关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行——计数行按整份计划统计、
    /// 不随关键词变化（「跳过 N」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来）。
    /// 筛到零行不是「没有可回写的文件」：说清这一屏为什么空，下一步就在那个框上（这一屏没有筛选下拉，
    /// 故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」），差异预览经既有的无选中路径
    /// <see cref="ShowDiff"/> 收口，不在这里另写一套收控件的赋值。
    /// 选中按源码文件路径恢复（筛一下再筛回来仍停在原来那个文件），恢复不到时落在新筛选第一条
    /// （与两个主页面「重建后按 Id 恢复选中」、第五十六轮「切换筛选落在新筛选第一条」同一口径）。
    /// </summary>
    private void PopulateList()
    {
        var plan = _plan;
        if (plan is null) return;
        var selectedPath = (Ui.SelectedItem(_list)?.Tag as PlannedFile)?.RelativePath;
        var results = plan.ApplyResults
            .GroupBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        // 筛选先于排序（ListFilter → ListSort，与另九处同一顺序）：筛掉的行不参与排序，
        // 排序键取 ApplyPlanSort 单一口径（与单元格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(plan.Files, _keyword.Text, file => ApplyPlanEntries.KeywordFields(
                file.RelativePath,
                ApplyPlanEntries.ScopeText(file.WriteAllowed),
                ResultText(results, file))),
            _sortColumn, _sortDescending,
            (file, column) => ApplyPlanSort.Key(
                file.RelativePath,
                ApplyPlanEntries.ScopeText(file.WriteAllowed),
                file.Edits.Count,
                ResultText(results, file),
                column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var file in visible)
        {
            var item = new ListViewItem(file.RelativePath) { Tag = file };
            item.UseItemStyleForSubItems = false;
            var scope = item.SubItems.Add(ApplyPlanEntries.ScopeText(file.WriteAllowed));
            if (!file.WriteAllowed) scope.ForeColor = UiTokens.Muted;
            item.SubItems.Add(file.Edits.Count.ToString());
            // 回写结果格与排序键取同一份文字（ResultText），不各写一套。
            var state = item.SubItems.Add(ResultText(results, file));
            if (results.GetValueOrDefault(file.RelativePath) is { } result)
            {
                state.ForeColor = ToneColor(TaskDisplay.FileApplyTone(result.State));
                if (result.Message is { } message) item.ToolTipText = message;
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(plan.Files.Count, visible.Count))
        {
            _hint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.ApplyPlanNoun);
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            ShowDiff();
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        var target = selectedPath is null
            ? null
            : _list.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as PlannedFile)?.RelativePath == selectedPath);
        Ui.SelectItem(target ?? _list.Items[0]);
    }

    /// <summary>
    /// 跳过清单（DESIGN.md §6「列表列排序」，第一百零八轮）：整份计划里没能生成修改的注释位置，
    /// 每个被跳过的候选一行（位置 + 原因）。这一屏的说明行写着「跳过的位置：这些注释位置本次不写回源码，每条为什么写在「原因」列；改完点「重新生成计划」。」，
    /// 要按原因处理就得先把同一原因的集中看——排序键取 <see cref="ApplyPlanSort.SkippedKey"/> 单一口径
    /// （与两格同源），界面不就地写 switch。
    /// 跳过清单没有选中语义（没有详情区、没有动作挂在它上面），故排序只改呈现顺序：
    /// 不恢复选中、不碰键盘焦点、不写数据；计数行仍按整份计划统计、不随排序变化
    /// （「跳过 N」正是这一屏的凭据）。重建路径（重新生成计划、回写后按磁盘计划重读）走同一口径，
    /// 排序状态因此留在本次打开期间。
    /// </summary>
    private void PopulateSkipped(CommentPlan plan)
    {
        // 筛选先于排序（ListFilter → ListSort，与文件清单同一顺序）：筛掉的行不参与排序，
        // 匹配字段与排序键都取共享口径（与两格同源），界面不就地写 switch。
        var visible = ListSort.Order(
            ListFilter.Apply(
                plan.Skipped,
                _skippedKeyword.Text,
                skip => ApplyPlanEntries.SkippedKeywordFields(skip.Display, skip.Reason)),
            _skippedSortColumn,
            _skippedSortDescending,
            (skip, column) => ApplyPlanSort.SkippedKey(skip.Display, skip.Reason, column));
        _skippedList.BeginUpdate();
        _skippedList.Items.Clear();
        foreach (var skip in visible)
        {
            var item = new ListViewItem(skip.Display);
            item.SubItems.Add(skip.Reason);
            _skippedList.Items.Add(item);
        }
        _skippedList.EndUpdate();
        RefreshSkippedHeaders();
        _skippedHost.Visible = plan.Skipped.Count > 0;
        if (ListFilter.IsFilteredEmpty(plan.Skipped.Count, visible.Count))
        {
            // 筛到零行不是「计划没有跳过位置」：说清这一屏为什么空，下一步就在那个框上
            // （这一屏没有筛选下拉，故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」）。
            _skippedHint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.SkippedNoun);
            _skippedHint.ForeColor = UiTokens.Muted;
            _skippedHint.Visible = true;
            _skippedList.Visible = false;
            return;
        }
        _skippedHint.Visible = false;
        _skippedList.Visible = true;
    }

    /// <summary>
    /// 跳过清单的点击列头（第一百零八轮）：同一列再点反向、换一列从升序开始，
    /// 方向翻转只在 <see cref="ListSort.Toggle"/> 一处。只改呈现顺序——不选中、不滚动、不写数据。
    /// </summary>
    private void SortSkipped(int column)
    {
        (_skippedSortColumn, _skippedSortDescending) = ListSort.Toggle(_skippedSortColumn, _skippedSortDescending, column);
        if (_plan is { } plan) PopulateSkipped(plan);
    }

    /// <summary>跳过清单的表头方向标记：排序列追加方向标记，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSkippedHeaders()
    {
        for (var i = 0; i < _skippedList.Columns.Count && i < _skippedTitles.Length; i++)
        {
            _skippedList.Columns[i].Text = ListSort.Header(_skippedTitles[i], i, _skippedSortColumn, _skippedSortDescending);
        }
    }

    /// <summary>
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百零五轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="PopulateList"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改计划与回写结果，选中仍按源码文件路径恢复
    /// （<see cref="PopulateList"/> 重建前记下、重建后找回，差异预览随选中事件跟上）。
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
    /// 回写结果列的文字：有逐文件结果时取 <see cref="TaskDisplay.FileApplyStateText"/> 的中文映射，
    /// 还没有结果时是空串（那一格本来就是空的）。单元格与排序键取同一份，不各写一套。
    /// </summary>
    private static string ResultText(Dictionary<string, FileApplyResult> results, PlannedFile file) =>
        results.GetValueOrDefault(file.RelativePath) is { } result ? TaskDisplay.FileApplyStateText(result.State) : "";

    /// <summary>
    /// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给关键词框并选中已有关键词、
    /// 框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部文件）。
    /// 与另九张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
    /// <see cref="Ui.TryEscapeFilter"/>）：本对话框不就地认键、不就地拆修饰键、不自己选中或滚动、不自己清空关键词。
    /// 同一页两个框（文件清单与跳过清单，第一百一十二轮）：Enter 与 Esc 看焦点各接一次——一次按键只有一个含义，
    /// 跳过清单的 Esc 不会去清文件清单的关键词，反之亦然；Ctrl+F 只交给文件清单那个框
    /// （与同步预览两页各一个框同一取舍：查找手势落在这一屏的主清单上）。
    /// 对话框的 Enter=确认、Esc=取消在焦点不在框上时逐字不变（AcceptButton 与 CancelButton 仍是「关闭」）。
    /// </summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_gestures.Dispatch(keyData)) return true;
        return base.ProcessDialogKey(keyData);
    }

    private void ShowDiff()
    {
        if (Ui.SelectedItem(_list)?.Tag is not PlannedFile file)
        {
            _diff.Text = "";
            return;
        }
        _diff.Text = string.IsNullOrWhiteSpace(file.Diff) ? "（该文件没有差异内容）" : file.Diff;
        _diff.SelectionStart = 0;
        _diff.SelectionLength = 0;
    }

    /// <summary>确认回写：覆盖式写入源码，按设计基线用原生确认对话框（默认按钮在「取消」，防回车误触）。</summary>
    private void RunApply() => PendingApply = RunApplyAsync();

    private async Task RunApplyAsync()
    {
        var plan = _plan;
        if (plan is null || _busy || !_applyButton.Enabled) return;
        if (!ConfirmApply(plan)) return;
        SetBusy(true);
        _statusHost.SetText("正在回写注释…", UiTokens.Text);
        CommentPlan applied;
        try
        {
            applied = await _plans.ApplyAsync(_taskId, plan.Hash, CancellationToken.None);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetBusy(false);
            SetStatus(ex.Message, UiTokens.WarnText);
            RefreshPlanFromDisk();
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetBusy(false);
            SetStatus("回写中断：" + Redactor.Redact(ex.Message) + "。已写入与未写入的文件见结果列；请重新生成计划核对。", UiTokens.BadText);
            RefreshPlanFromDisk();
            return;
        }
        if (IsDisposed) return;
        SetBusy(false);
        ShowPlan(applied);
        var written = applied.ApplyResults.Count(r => r.State == "applied");
        var writtenEdits = applied.Files.Where(f => applied.ApplyResults.Any(r => r.RelativePath == f.RelativePath && r.State == "applied")).Sum(f => f.Edits.Count);
        switch (applied.Status)
        {
            case "applied":
                SetStatus($"已回写 {written} 个文件、{writtenEdits} 处注释。源码已变化，请重新扫描后再核对生成文档。", UiTokens.OkText);
                break;
            case "partial":
                SetStatus($"部分回写：已写入 {written} 个文件，其余未写入（原因见结果列）。已写入部分不回滚。", UiTokens.WarnText);
                break;
            default:
                SetStatus("回写未完成，没有文件被写入（原因见结果列）。请处理后重新生成计划。", UiTokens.BadText);
                break;
        }
    }

    /// <summary>回写被拒绝（计划过期等）后按磁盘上的计划刷新界面，状态行保留服务给出的原因与下一步。</summary>
    private void RefreshPlanFromDisk()
    {
        try
        {
            if (_tasks.Read<CommentPlan>(_taskId, TaskFiles.CommentPlan) is { } plan) ShowPlan(plan);
        }
        catch
        {
            // 读取失败时保留当前界面与状态行错误信息，不再叠加二次错误。
        }
    }

    /// <summary>导出补丁：与回写内容逐字节一致（UTF-8 无 BOM），交由开发自行处理；不改动源码。</summary>
    private async void ExportPatch()
    {
        var plan = _plan;
        if (plan is null || _busy || plan.Files.Count == 0) return;
        using var dialog = new SaveFileDialog
        {
            Title = "导出注释补丁",
            FileName = "comments.patch",
            Filter = "补丁文件（*.patch）|*.patch|全部文件（*.*）|*.*",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await _plans.ExportPatchAsync(_taskId, CancellationToken.None);
            if (IsDisposed) return;
            File.WriteAllText(dialog.FileName, _plans.CombinedPatch(plan), new UTF8Encoding(false));
            SetStatus("补丁已导出到 " + dialog.FileName, UiTokens.OkText);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.WarnText);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("导出补丁失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        // 作业运行期间清单正在被重建：关键词框一起停（口径见 ListFilter.KeywordEnabled）。
        _keyword.Enabled = ListFilter.KeywordEnabled(_plan is { Files.Count: > 0 }, busy);
        _skippedKeyword.Enabled = ListFilter.KeywordEnabled(_plan is { Skipped.Count: > 0 }, busy);
        _applyButton.Enabled = !busy && _plan is { Status: "ready" } && _plan.Files.Any(f => f.WriteAllowed);
        _patchButton.Enabled = !busy && _plan is { Files.Count: > 0 };
        _regenButton.Enabled = !busy;
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
        _skippedKeyword.Enabled = ListFilter.KeywordEnabled(false);
        _counts.Text = "";
        _diff.Text = "";
        _skippedHost.Visible = false;
        _applyButton.Enabled = false;
        _patchButton.Enabled = false;
        _regenButton.Enabled = false;
        _planStatus.Text = "";
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
        "accent" => UiTokens.Accent,
        "muted" => UiTokens.Muted,
        _ => UiTokens.Text,
    };
}
