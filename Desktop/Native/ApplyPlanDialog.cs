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
/// 回写中=状态行 + 按钮禁用；部分完成/失败/过期=结果列与状态行如实呈现（过期可重新生成计划）。
/// </summary>
internal sealed class ApplyPlanDialog : Form
{
    private readonly TaskStore _tasks;
    private readonly CommentPlanService _plans;
    private readonly CheckTask _task;
    private readonly string _taskId;

    private readonly Label _planStatus = Ui.StatusText();
    private readonly Label _counts = Ui.Mono();
    private readonly ListView _list = Ui.List();
    private readonly ListView _skippedList = Ui.List();
    private readonly Panel _skippedHost;
    private readonly TextBox _diff = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Button _applyButton;
    private readonly Button _patchButton;
    private readonly Button _regenButton;
    private readonly Label _status = Ui.StatusLine();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    private CommentPlan? _plan;
    private bool _busy;

    public ApplyPlanDialog(CommentPlanService plans, TaskStore tasks, CheckTask task)
    {
        _plans = plans;
        _tasks = tasks;
        _task = task;
        _taskId = task.Id;

        Text = "注释回写 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 680);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("源码文件", 400);
        _list.Columns.Add("回写范围", 170);
        _list.Columns.Add("注释处数", 90);
        _list.Columns.Add("回写结果", 110);
        _list.SelectedIndexChanged += (_, _) => ShowDiff();

        _skippedList.Columns.Add("跳过的位置", 300);
        _skippedList.Columns.Add("原因", 560);
        _skippedHost = new Panel { Dock = DockStyle.Bottom, Height = 150, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, 0) };
        var skippedCaption = Ui.Caption("跳过的位置（原因如下；处理后重新生成计划）");
        skippedCaption.Dock = DockStyle.Top;
        var skippedInner = Ui.Hairline(_skippedList);
        skippedInner.Dock = DockStyle.Fill;
        skippedInner.Height = 110;
        _skippedHost.Controls.Add(skippedInner);
        _skippedHost.Controls.Add(skippedCaption);
        _skippedHost.Visible = false;

        _applyButton = Ui.Primary("确认回写");
        _applyButton.Enabled = false;
        _applyButton.Click += (_, _) => RunApply();
        _patchButton = Ui.Ghost("导出补丁");
        _patchButton.Enabled = false;
        _patchButton.Click += (_, _) => ExportPatch();
        _regenButton = Ui.Ghost("重新生成计划");
        _regenButton.Enabled = false;
        _regenButton.Click += (_, _) => LoadPlan();
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

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var diffHost = Ui.Hairline(_diff);
        diffHost.Dock = DockStyle.Bottom;
        diffHost.Height = 210;

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

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
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 3, 0);
        actionRow.Controls.Add(closeButton, 4, 0);

        // 停靠按集合倒序生效：标题行最先停靠（顶部），其次按钮行（最底部）、状态行、差异框、跳过列表，文件列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(_skippedHost);
        Controls.Add(diffHost);
        Controls.Add(statusHost);
        Controls.Add(actionRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        LoadPlan();
    }

    /// <summary>生成（或重新生成）注释计划：读工作区、核对文件与证据指纹，产物为 comment-plan.json 与 comments.patch。</summary>
    private async void LoadPlan()
    {
        if (_busy) return;
        SetBusy(true);
        _hint.Visible = false;
        _status.Text = "";
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

    /// <summary>按计划刷新列表、计数与按钮可用性；回写后再次调用会补上逐文件结果列。</summary>
    private void ShowPlan(CommentPlan plan)
    {
        _plan = plan;
        _hint.Visible = false;
        _list.Visible = true;
        _planStatus.Text = "计划状态：" + TaskDisplay.PlanStatusText(plan.Status) + (plan.Status == "expired" && plan.Files.Count == 0 ? "" : "（预览后源码再变化则计划过期）");
        _planStatus.ForeColor = ToneColor(TaskDisplay.PlanTone(plan.Status));
        _counts.Text = $"文件 {plan.Files.Count} · 注释 {plan.Files.Sum(f => f.Edits.Count)} · 跳过 {plan.Skipped.Count}";

        var results = plan.ApplyResults
            .GroupBy(r => r.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var file in plan.Files)
        {
            var item = new ListViewItem(file.RelativePath) { Tag = file };
            item.UseItemStyleForSubItems = false;
            var scope = item.SubItems.Add(file.WriteAllowed ? "允许回写" : "只导出补丁");
            if (!file.WriteAllowed) scope.ForeColor = UiTokens.Muted;
            item.SubItems.Add(file.Edits.Count.ToString());
            if (results.GetValueOrDefault(file.RelativePath) is { } result)
            {
                var state = item.SubItems.Add(TaskDisplay.FileApplyStateText(result.State));
                state.ForeColor = ToneColor(TaskDisplay.FileApplyTone(result.State));
                if (result.Message is { } message) item.ToolTipText = message;
            }
            else
            {
                item.SubItems.Add("");
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (_list.Items.Count > 0) _list.Items[0].Selected = true;

        _skippedList.BeginUpdate();
        _skippedList.Items.Clear();
        foreach (var skip in plan.Skipped)
        {
            var item = new ListViewItem(skip.Display);
            item.SubItems.Add(skip.Reason);
            _skippedList.Items.Add(item);
        }
        _skippedList.EndUpdate();
        _skippedHost.Visible = plan.Skipped.Count > 0;

        if (plan.Files.Count == 0)
        {
            // 计划没有可写入的文件：列表区如实说明，跳过原因保持在下方可见。
            _hint.Text = plan.Message ?? "没有可生成的注释修改，请查看跳过原因。";
            _hint.ForeColor = UiTokens.Muted;
            _hint.Visible = true;
            _list.Visible = false;
        }

        var writable = plan.Files.Count(f => f.WriteAllowed);
        _applyButton.Enabled = plan.Status == "ready" && writable > 0;
        _patchButton.Enabled = plan.Files.Count > 0;
        _regenButton.Enabled = plan.Status is "expired" or "failed" or "partial";
    }

    private void ShowDiff()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not PlannedFile file)
        {
            _diff.Text = "";
            return;
        }
        _diff.Text = string.IsNullOrWhiteSpace(file.Diff) ? "（该文件没有差异内容）" : file.Diff;
        _diff.SelectionStart = 0;
        _diff.SelectionLength = 0;
    }

    /// <summary>确认回写：覆盖式写入源码，按设计基线用原生确认对话框（默认按钮在「取消」，防回车误触）。</summary>
    private async void RunApply()
    {
        var plan = _plan;
        if (plan is null || _busy || !_applyButton.Enabled) return;
        var writable = plan.Files.Where(f => f.WriteAllowed).ToList();
        var edits = writable.Sum(f => f.Edits.Count);
        var choice = MessageBox.Show(this,
            $"将把注释改动写入 {writable.Count} 个源码文件，共 {edits} 处注释修改。\n只修改 /// 注释行，不改执行代码；文件在确认后被改动会拒绝写入，失败即停并如实记录。\n\n确认回写？",
            "确认注释回写",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.OK) return;
        SetBusy(true);
        _status.Text = "正在回写注释…";
        _status.ForeColor = UiTokens.Text;
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
        _status.Text = message;
        _status.ForeColor = color;
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
