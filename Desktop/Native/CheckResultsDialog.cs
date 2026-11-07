using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Sync;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// Apifox 差异检查结果（只读）：逐接口状态、结构差异、说明合并结果、数据模型与检查问题。
/// 数据来自任务目录 diff.json（检查落盘产物）；检查在任务页发起，本对话框无写入动作。
/// 差异确认（高风险变化、说明冲突、模型绑定）在任务页「处理差异确认」中进行，同步预览与执行在任务页「同步预览」中进行；本对话框只读。
/// 状态表：尚未检查=空态指引；读取失败=内联错误与下一步；检查之后任务有变化=过期提示（结果仍如实展示）；
/// 正常=概要行 + 筛选 + 接口列表 + 选中接口详情（结构差异/说明合并/备注/质量提示/阻断）+ 模型清单 + 问题清单。
/// </summary>
internal sealed class CheckResultsDialog : Form
{
    /// <summary>筛选下拉的取值顺序（与选项文案一一对应）。</summary>
    private static readonly string[] FilterValues = ["", "new", "structure", "docs", "unchanged", "blocked", "sourceOnly"];

    private readonly TaskStore _tasks;
    private readonly string _taskId;
    private readonly CheckTask _task;
    private CheckResult? _result;

    private readonly Label _computedAt = Ui.StatusText();
    private readonly Label _counts = Ui.Mono();
    private readonly Label _stale = new()
    {
        AutoSize = true,
        ForeColor = UiTokens.WarnText,
        Font = UiTokens.Caption.Value,
        Margin = new Padding(0),
        Visible = false,
    };
    private readonly ComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly ListView _list = Ui.List();
    private readonly ListView _modelList = Ui.List();
    private readonly TextBox _detail = Ui.Preview(multiline: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly TextBox _problems = Ui.Preview(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly Panel _problemsHost;
    private readonly Panel _modelsHost;
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public CheckResultsDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _taskId = task.Id;
        _task = task;

        Text = "Apifox 差异检查结果 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1000, 720);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("接口", 250);
        _list.Columns.Add("名称", 200);
        _list.Columns.Add("状态", 96);
        _list.Columns.Add("阻断", 56);
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

        _modelList.Columns.Add("模型", 220);
        _modelList.Columns.Add("映射", 100);
        _modelList.Columns.Add("状态", 88);
        _modelList.Columns.Add("用于接口", 88);
        _modelList.Columns.Add("范围外影响", 100);

        _filter.Items.AddRange(["全部状态", "新增", "结构变化", "说明变化", "无变化", "阻断", "仅源码"]);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => PopulateList();

        _computedAt.TextAlign = ContentAlignment.MiddleLeft;
        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;
        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headRow.Controls.Add(_computedAt, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);
        headRow.Controls.Add(_stale, 0, 1);
        headRow.SetColumnSpan(_stale, 2);

        var filterCaption = Ui.Body("筛选");
        filterCaption.Margin = new Padding(0, 0, UiTokens.Sm, 0);
        var filterRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, BackColor = UiTokens.Panel, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        filterRow.Controls.Add(filterCaption);
        filterRow.Controls.Add(_filter);

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var problemsCaption = Ui.Caption("检查问题与提示（读取失败、不支持的结构、远端独有的范围内接口都如实列出）");
        problemsCaption.Dock = DockStyle.Top;
        var problemsInner = Ui.Hairline(_problems);
        problemsInner.Dock = DockStyle.Fill;
        problemsInner.Height = 96;
        _problemsHost = new Panel { Dock = DockStyle.Bottom, Height = 124, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        _problemsHost.Controls.Add(problemsInner);
        _problemsHost.Controls.Add(problemsCaption);

        var detailCaption = Ui.Caption("选中接口的结构差异、说明合并、备注、质量提示与阻断");
        detailCaption.Dock = DockStyle.Top;
        var detailInner = Ui.Hairline(_detail);
        detailInner.Dock = DockStyle.Fill;
        detailInner.Height = 140;
        var detailHost = new Panel { Dock = DockStyle.Bottom, Height = 168, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        detailHost.Controls.Add(detailInner);
        detailHost.Controls.Add(detailCaption);

        var modelsCaption = Ui.Caption("数据模型（映射与状态；绑定确认在任务页「处理差异确认」中完成）");
        modelsCaption.Dock = DockStyle.Top;
        var modelsInner = Ui.Hairline(_modelList);
        modelsInner.Dock = DockStyle.Fill;
        modelsInner.Height = 120;
        _modelsHost = new Panel { Dock = DockStyle.Bottom, Height = 148, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        _modelsHost.Controls.Add(modelsInner);
        _modelsHost.Controls.Add(modelsCaption);

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：标题行、筛选行最先停靠（顶部），其次按钮行、模型清单、条目详情、问题清单（自下而上），接口列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(_problemsHost);
        Controls.Add(detailHost);
        Controls.Add(_modelsHost);
        Controls.Add(buttonRow);
        Controls.Add(filterRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        CheckResult? result;
        try
        {
            result = _tasks.Read<CheckResult>(_taskId, TaskFiles.Diff);
        }
        catch (Exception ex)
        {
            _filter.Enabled = false;
            ShowHint("读取检查结果失败：" + Redactor.Redact(ex.Message) + "。请关闭后在任务页重新检查再试。", UiTokens.BadText);
            return;
        }
        if (result is null)
        {
            _filter.Enabled = false;
            ShowHint("尚未检查。先在任务页点「开始检查」：读取分支概览与范围内接口、模型详情，与目标比较后在这里展示差异。", UiTokens.Muted);
            return;
        }
        _result = result;
        _hint.Visible = false;

        _computedAt.Text = "检查时间 " + result.ComputedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm")
            + " · 结构来源：" + TaskDisplay.StructureSourceText(result.StructureSource)
            + (result.SnapshotAt is { } snapshot
                ? " · 快照 " + snapshot.LocalDateTime.ToString("MM-dd HH:mm") + (result.SnapshotComplete ? "" : "（不完整）")
                : "");
        var endpoints = result.Endpoints;
        var counts = new List<string>
        {
            "接口 " + endpoints.Count,
            "新增 " + endpoints.Count(c => c.Status == "new"),
            "结构变化 " + endpoints.Count(c => c.Status == "structure"),
            "说明变化 " + endpoints.Count(c => c.Status == "docs"),
            "无变化 " + endpoints.Count(c => c.Status == "unchanged"),
            "阻断 " + endpoints.Count(c => c.Status == "blocked"),
        };
        var sourceOnly = endpoints.Count(c => c.Status == "sourceOnly");
        if (sourceOnly > 0) counts.Add("仅源码 " + sourceOnly);
        counts.Add("模型 " + result.Models.Count);
        _counts.Text = string.Join(" · ", counts);
        if (_task.Revision > result.TaskRevision)
        {
            _stale.Text = "检查之后任务内容有变化（注释、契约或补充），重新检查后结果才会更新。";
            _stale.Visible = true;
        }

        var problemLines = result.Problems.ToList();
        problemLines.AddRange(result.UnmatchedRemote.Select(u => "远端有、源中没有（范围内）：" + u));
        if (problemLines.Count > 0)
        {
            _problems.Lines = [.. problemLines];
        }
        else
        {
            _problemsHost.Visible = false;
        }

        PopulateList();

        _modelList.BeginUpdate();
        foreach (var model in result.Models)
        {
            var row = new ListViewItem(model.Name) { Tag = model, UseItemStyleForSubItems = false };
            row.Font = UiTokens.Mono.Value;
            AddState(row, TaskDisplay.MappingStateText(model.MappingState), TaskDisplay.MappingStateTone(model.MappingState));
            AddState(row, TaskDisplay.ModelStatusText(model.Status), TaskDisplay.ModelStatusTone(model.Status));
            AddCount(row, model.UsedBy.Count);
            var impact = AddCount(row, model.ImpactOutOfScope.Count);
            if (model.ImpactOutOfScope.Count > 0) impact.ForeColor = UiTokens.WarnText;
            _modelList.Items.Add(row);
        }
        _modelList.EndUpdate();
        if (result.Models.Count == 0)
        {
            _modelsHost.Visible = false;
        }
    }

    private void PopulateList()
    {
        if (_result is null) return;
        var selectedKey = _list.SelectedItems.Count > 0 ? (_list.SelectedItems[0].Tag as EndpointCheck)?.Key : null;
        var status = FilterValues[Math.Max(0, _filter.SelectedIndex)];
        _list.BeginUpdate();
        _list.Items.Clear();
        var endpoints = _result.Endpoints
            .Where(c => status.Length == 0 || c.Status == status)
            .ToList();
        foreach (var check in endpoints)
        {
            var row = new ListViewItem(check.Method + " " + check.Path) { Tag = check, UseItemStyleForSubItems = false };
            row.Font = UiTokens.Mono.Value;
            row.SubItems.Add(check.RemoteName ?? "—").Font = UiTokens.Body.Value;
            AddState(row, TaskDisplay.CheckStatusText(check.Status), TaskDisplay.CheckStatusTone(check.Status));
            AddCount(row, check.Blockers.Count);
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        _filter.Enabled = _result.Endpoints.Count > 0;
        if (endpoints.Count == 0)
        {
            ShowHint(_result.Endpoints.Count == 0
                ? "范围内没有接口。请确认任务的模块与路径前缀范围，或先完成源码扫描。"
                : "这个状态下没有接口。换一个筛选再试。",
                UiTokens.Muted);
            return;
        }
        _hint.Visible = false;
        _list.Visible = true;
        var target = selectedKey is null ? null : _list.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as EndpointCheck)?.Key == selectedKey);
        (target ?? _list.Items[0]).Selected = true;
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _detail.Text = "";
    }

    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not EndpointCheck check)
        {
            _detail.Text = "";
            return;
        }
        var lines = new List<string>();
        if (check.RemoteId is { } remoteId)
        {
            lines.Add("远端：ID " + remoteId
                + (check.RemoteName is { } name ? " · 名称 " + name : "")
                + (check.RemoteFolder is { } folder ? " · 目录 " + folder : ""));
        }
        if (check.DuplicateRemoteIds.Count > 0)
        {
            lines.Add("远端重复身份：ID " + string.Join("、", check.DuplicateRemoteIds) + "，不能任选其一覆盖。");
        }

        var items = _result!.Items.Where(i => i.ResourceKey == check.Key && i.Category is "structure" or "identity").ToList();
        lines.Add("结构差异：");
        var structural = items.Where(i => i.Change != "info").ToList();
        if (structural.Count == 0)
        {
            lines.Add("  没有结构差异。");
        }
        foreach (var item in structural)
        {
            lines.Add("  " + item.Location + " · " + TaskDisplay.DiffChangeText(item.Change)
                + (string.IsNullOrWhiteSpace(item.Reason) ? "" : " — " + item.Reason)
                + (item.Risk is "normal" or "" ? "" : "（" + TaskDisplay.DiffRiskText(item.Risk) + "）"));
        }
        var infos = items.Where(i => i.Change == "info").ToList();
        if (infos.Count > 0)
        {
            lines.Add("  保留项：" + string.Join("；", infos.Select(i => i.Location + (string.IsNullOrWhiteSpace(i.Reason) ? "" : "（" + i.Reason + "）"))));
        }

        lines.Add("说明合并：");
        if (check.DocChanges.Count == 0)
        {
            lines.Add("  没有说明变化。");
        }
        foreach (var change in check.DocChanges)
        {
            lines.Add("  " + change.Location + "［" + TaskDisplay.DocOriginText(change.Origin) + "］远端「" + (change.RemoteValue ?? "—") + "」目标「" + (change.TargetValue ?? "—") + "」→ " + TaskDisplay.DocDecisionText(change.Decision));
        }

        lines.Add(check.Notes.Count > 0 ? "备注：" + string.Join("；", check.Notes) : "备注：（无）");
        var quality = _result.Quality.Where(q => q.ResourceKey == check.Key && q.Severity != "blocking").ToList();
        lines.Add(quality.Count > 0 ? "质量提示：" : "质量提示：（无）");
        lines.AddRange(quality.Select(q => "  " + q.Location + "：" + q.Message));
        foreach (var blocker in check.Blockers)
        {
            lines.Add("阻断：" + blocker);
        }

        _detail.Lines = [.. lines];
        _detail.SelectionStart = 0;
        _detail.SelectionLength = 0;
    }

    private static void AddState(ListViewItem row, string text, string tone)
    {
        var sub = row.SubItems.Add(text);
        sub.Font = UiTokens.Body.Value;
        sub.ForeColor = tone switch
        {
            "ok" => UiTokens.OkText,
            "warn" => UiTokens.WarnText,
            "bad" => UiTokens.BadText,
            "muted" => UiTokens.Muted,
            "accent" => UiTokens.Accent,
            _ => UiTokens.Text,
        };
    }

    /// <summary>数值列：等宽数字（D5），无值显示「—」。</summary>
    private static ListViewItem.ListViewSubItem AddCount(ListViewItem row, int count)
    {
        var sub = row.SubItems.Add(count > 0 ? count.ToString() : "—");
        sub.Font = UiTokens.Mono.Value;
        return sub;
    }
}
