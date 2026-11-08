using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 生成文档核对结果（只读）：已确认注释是否写入源码、出现在实际生成的 XML 文档、被 Swagger 读取到正确位置。
/// 数据来自任务目录 propagation.json（核对落盘产物）；核对在任务页发起，本对话框无写入动作。
/// 旧生成物不算注释已生效：XML 早于源码修改、未启用 XML 生成、Swagger 未刷新都如实显示，不显示成完成。
/// 状态表：尚未核对=空态指引；读取失败=内联错误与下一步；正常=概要计数 + 条目列表（源码/XML/Swagger 三环状态，
/// 颜色 + 文字双通道）+ 条目详情 + XML 文件清单；筛到零行=关键词框留着并说明下一步，计数行仍是整份核对数据的数字
/// （见 <see cref="PopulateList"/>）。
/// 条目清单支持点击列头排序（第一百零三轮，口径见 <see cref="PropagationSort"/> 与 <see cref="ListSort"/>）：
/// 这一屏存在的理由正是「注释有没有真的写进源码、出现在 XML、被 Swagger 读到」，
/// 「把 XML 未收录的那几条集中看」只能逐行扫——排序只改呈现顺序，不改核对结果与筛选口径。
/// </summary>
internal sealed class PropagationDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly string _taskId;

    private readonly Label _checkedAt = Ui.StatusText();
    private readonly Label _counts = Ui.Mono();
    private readonly Label _swaggerMessage = new()
    {
        AutoSize = true,
        ForeColor = UiTokens.WarnText,
        Font = UiTokens.Caption.Value,
        Margin = new Padding(0),
        Visible = false,
    };
    private readonly ListView _list = Ui.List();
    private readonly ListView _xmlList = Ui.List();
    private readonly TextBox _keyword = Ui.Input();
    private readonly TextBox _detail = Ui.Preview(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private List<PropagationEntry> _entries = [];

    // 列排序状态（ListSort 口径，第一百零三轮）：未排序时保持核对结果自身的顺序。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    private readonly string[] _titles;

    public PropagationDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _taskId = task.Id;

        Text = "生成文档核对 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("位置", 330);
        _list.Columns.Add("标注", 110);
        _list.Columns.Add("源码", 100);
        _list.Columns.Add("XML", 110);
        _list.Columns.Add("Swagger", 130);
        _titles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();
        _keyword.PlaceholderText = ListFilter.PropagationSearchPlaceholder;
        _keyword.TextChanged += (_, _) => PopulateList();

        _xmlList.Columns.Add("项目", 150);
        _xmlList.Columns.Add("XML 文档文件", 420);
        _xmlList.Columns.Add("状态", 80);
        _xmlList.Columns.Add("更新时间", 220);

        _checkedAt.TextAlign = ContentAlignment.MiddleLeft;
        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;
        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headRow.Controls.Add(_checkedAt, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);
        headRow.Controls.Add(_swaggerMessage, 0, 1);
        headRow.SetColumnSpan(_swaggerMessage, 2);

        // 筛选行形状取工厂（§6「列表筛选」）：这一屏没有筛选下拉，走只有关键词框的那一支；
        // 计数在表头行（与核对时间同排），故 counts 传 null。
        var filterRow = Ui.FilterRow(_keyword, counts: null);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var detailCaption = Ui.Caption("选中条目的说明文字、Swagger 对应位置与备注");
        detailCaption.Dock = DockStyle.Top;
        var detailInner = Ui.Hairline(_detail);
        detailInner.Dock = DockStyle.Fill;
        detailInner.Height = 100;
        var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 130, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        detailHost.Controls.Add(detailInner);
        detailHost.Controls.Add(detailCaption);

        var xmlCaption = Ui.Caption("XML 文档文件（早于源码修改的可能是旧生成物）");
        xmlCaption.Dock = DockStyle.Top;
        var xmlInner = Ui.Hairline(_xmlList);
        xmlInner.Dock = DockStyle.Fill;
        xmlInner.Height = 110;
        var xmlHost = new Panel { Dock = DockStyle.Bottom, Height = 140, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        xmlHost.Controls.Add(xmlInner);
        xmlHost.Controls.Add(xmlCaption);

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次 XML 清单、条目详情、筛选行、标题行，条目列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailHost);
        Controls.Add(xmlHost);
        Controls.Add(buttonRow);
        Controls.Add(filterRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        PropagationResult? result;
        try
        {
            result = _tasks.Read<PropagationResult>(_taskId, TaskFiles.Propagation);
        }
        catch (Exception ex)
        {
            ShowHint("读取核对结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后在任务页重新核对再试。", UiTokens.BadText);
            return;
        }
        if (result is null)
        {
            ShowHint("尚未核对。先在任务页点「核对传播」：核对检查已确认注释是否写入源码、出现在生成的 XML 文档、并被 Swagger 读取到正确位置。", UiTokens.Muted);
            return;
        }
        _hint.Visible = false;
        _checkedAt.Text = "核对时间 " + result.CheckedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
        _entries = result.Entries;
        // 计数行按整份核对数据统计，不随关键词变化（「XML 0」正是要找的那批的凭据）。
        _counts.Text = PropagationEntries.CountsLine(
            _entries.Count,
            _entries.Count(en => en.SourceState == "written"),
            _entries.Count(en => en.XmlState == "present"),
            _entries.Count(en => en.SwaggerState == "present"));
        if (result.SwaggerMessage is { } message)
        {
            _swaggerMessage.Text = message;
            _swaggerMessage.Visible = true;
        }

        _xmlList.BeginUpdate();
        foreach (var xml in result.XmlFiles)
        {
            var row = new ListViewItem(xml.Project) { Tag = xml };
            var path = row.SubItems.Add(xml.Path ?? "未启用");
            path.Font = UiTokens.Mono.Value;
            var state = row.SubItems.Add(xml.Exists ? "存在" : "不存在");
            state.ForeColor = xml.Exists ? UiTokens.OkText : UiTokens.BadText;
            row.SubItems.Add(xml.LastWrite is { } time
                ? time.ToString("yyyy-MM-dd HH:mm") + (xml.OlderThanSource ? "（早于源码修改）" : "")
                : "—");
            _xmlList.Items.Add(row);
        }
        _xmlList.EndUpdate();

        if (_entries.Count == 0)
        {
            // 整份数据没有条目：这一屏的空不在筛选上，提示出口说话、清单不显示（与接口追踪明细同一形状）。
            _hint.Text = "没有核对条目。";
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            // 整份数据没有条目：关键词框一起停（没有可筛的东西，下一步不在这框上）。口径见 ListFilter.KeywordEnabled。
            _keyword.Enabled = ListFilter.KeywordEnabled(false);
            return;
        }
        // 有数据可筛：关键词框可用（口径见 ListFilter.KeywordEnabled）。
        _keyword.Enabled = ListFilter.KeywordEnabled(true);
        PopulateList();
    }

    /// <summary>
    /// 打开时把键盘交给清单（DESIGN.md §6「列表行」的「只读清单打开时的键盘归属」，第八十八轮）：
    /// 这一屏打开即选中第一个条目（详情区已经显示那一行），而对话框打开时键盘落在框架 tab 序的第一个可聚焦控件上——
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
    /// 关键词框（DESIGN.md §6「列表筛选」）：输入即筛选，只改可见行——计数行按整份核对数据统计、
    /// 不随关键词变化（「XML 0」正是要找的那批的凭据，被一屏的空抹掉等于把结果藏起来）。
    /// 筛到零行不是「没有核对条目」：说清这一屏为什么空，下一步就在那个框上（这一屏没有筛选下拉，
    /// 故不指「换一个筛选」），关键词框留着（§7「禁用不得移除下一步」），详情区经既有的无选中路径
    /// <see cref="ShowDetail"/> 收口，不在这里另写一套收控件的赋值。
    /// 选中按位置恢复（筛一下再筛回来仍停在原来那个条目），恢复不到时落在新筛选第一条
    /// （与两个主页面「重建后按 Id 恢复选中」、第五十六轮「切换筛选落在新筛选第一条」同一口径）。
    /// 筛选先于排序（<see cref="ListFilter"/> → <see cref="ListSort"/>，与另七处同一顺序）：筛掉的行不参与排序，
    /// 排序键取 <see cref="PropagationSort"/> 单一口径（与单元格同源），界面不就地写 switch。
    /// <paramref name="keep"/> 是「排序后重建要把当前这一条交回去」的那一条（排序不该把选中甩回队首）；
    /// 关键词与筛选的重建路径不传它（那一屏的下一步是换关键词或筛选，落点会把详情区从用户正在看的那一条挪走）。
    /// </summary>
    private void PopulateList(PropagationEntry? keep = null)
    {
        var selectedDisplay = (Ui.SelectedItem(_list)?.Tag as PropagationEntry)?.Display;
        var visible = ListSort.Order(
            ListFilter.Apply(_entries, _keyword.Text, entry => PropagationEntries.KeywordFields(
                entry.Display,
                PropagationEntries.TagText(entry.Tag, entry.ParameterName),
                TaskDisplay.PropagationSourceText(entry.SourceState),
                TaskDisplay.PropagationXmlText(entry.XmlState),
                TaskDisplay.PropagationSwaggerText(entry.SwaggerState))),
            _sortColumn, _sortDescending,
            (entry, column) => PropagationSort.Key(
                entry.Display,
                PropagationEntries.TagText(entry.Tag, entry.ParameterName),
                TaskDisplay.PropagationSourceText(entry.SourceState),
                TaskDisplay.PropagationXmlText(entry.XmlState),
                TaskDisplay.PropagationSwaggerText(entry.SwaggerState),
                column));
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in visible)
        {
            var row = new ListViewItem(entry.Display) { Tag = entry };
            row.SubItems.Add(PropagationEntries.TagText(entry.Tag, entry.ParameterName));
            AddState(row, TaskDisplay.PropagationSourceText(entry.SourceState), TaskDisplay.PropagationSourceTone(entry.SourceState));
            AddState(row, TaskDisplay.PropagationXmlText(entry.XmlState), TaskDisplay.PropagationXmlTone(entry.XmlState));
            AddState(row, TaskDisplay.PropagationSwaggerText(entry.SwaggerState), TaskDisplay.PropagationSwaggerTone(entry.SwaggerState));
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        RefreshSortHeaders();
        if (ListFilter.IsFilteredEmpty(_entries.Count, visible.Count))
        {
            _hint.Text = ListFilter.FilteredEmptyKeywordHint(ListFilter.PropagationNoun);
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
            ShowDetail();
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        // 排序后重建把当前这一条交回去（keep），不落到「选中第一行」——排序不该把选中甩回队首；
        // 交回去仍经 Ui.SelectItem 唯一入口（选中事件随即问到详情，详情跟着那一行走）。
        if (keep is not null && SelectEntry(keep.Display)) return;
        var target = selectedDisplay is null
            ? null
            : _list.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as PropagationEntry)?.Display == selectedDisplay);
        Ui.SelectItem(target ?? _list.Items[0]);
    }

    /// <summary>
    /// 三个筛选手势（DESIGN.md §6「列表筛选」与「菜单/快捷键」）：Ctrl+F 把焦点交给关键词框并选中已有关键词、
    /// 框里的裸 Enter 把焦点与选中交给筛选结果第一行、框里的 Esc 退出筛选（清空关键词回到全部条目）。
    /// 与另八张长清单同一份口径、同一处接线（<see cref="Ui.TryFocusFilter"/>、<see cref="Ui.TryEnterFilter"/>、
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
    /// 点击列头（DESIGN.md §6「列表列排序」，第一百零三轮）：同一列再点反向、换一列从升序开始，
    /// 排序状态留在本对话框、重建走同一口径（<see cref="PopulateList"/> 里筛选先于排序）。
    /// 只改呈现顺序——不写任务数据、不改核对结果，选中仍按条目自身恢复（重建前记下、重建后找回）。
    /// </summary>
    private void SortBy(int column)
    {
        (_sortColumn, _sortDescending) = ListSort.Toggle(_sortColumn, _sortDescending, column);
        PopulateList(Current());
    }

    /// <summary>表头方向标记：排序列追加方向标记，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSortHeaders()
    {
        for (var i = 0; i < _list.Columns.Count && i < _titles.Length; i++)
        {
            _list.Columns[i].Text = ListSort.Header(_titles[i], i, _sortColumn, _sortDescending);
        }
    }

    private PropagationEntry? Current() => Ui.SelectedItem(_list)?.Tag as PropagationEntry;

    /// <summary>重建后回到刚看的那一条；返回它是否仍在当前筛选的清单上（不在时由调用方续接既有落点）。</summary>
    private bool SelectEntry(string display)
    {
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is PropagationEntry entry && entry.Display == display)
            {
                Ui.SelectItem(item);
                return true;
            }
        }
        return false;
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
        _counts.Text = "";
        _checkedAt.Text = "";
        _detail.Text = "";
        _swaggerMessage.Visible = false;
    }

    private void ShowDetail()
    {
        if (Ui.SelectedItem(_list)?.Tag is not PropagationEntry entry)
        {
            _detail.Text = "";
            return;
        }
        _detail.Lines =
        [
            "说明文字：" + entry.Text,
            "Swagger 对应位置：" + (entry.SwaggerLocation ?? "—"),
            entry.Notes.Count > 0 ? "备注：" + string.Join("；", entry.Notes) : "备注：（无）",
        ];
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
    }

    private static void AddState(ListViewItem row, string text, string tone)
    {
        var sub = row.SubItems.Add(text);
        sub.ForeColor = tone switch
        {
            "ok" => UiTokens.OkText,
            "warn" => UiTokens.WarnText,
            "bad" => UiTokens.BadText,
            "muted" => UiTokens.Muted,
            _ => UiTokens.Text,
        };
    }
}
