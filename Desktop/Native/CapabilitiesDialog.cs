using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 连接与写入探测（F02）：打开时经既有 CompatService.CapabilitiesAsync 检查 CLI、登录状态、项目读取
/// 与写入兼容（按 CLI 版本登记的探测记录），结果只读展示；
/// 「运行写入探测」经原生确认（默认按钮在「取消」）后走共享 JobRunner 调既有 ProbeAsync——
/// 往返写入与逐项回读核对全部在服务层单一口径，本层只做输入、确认、进度与结果展示。
/// 作用域与冻结页面 write-probe 路由同一键（profile:{id}），同一配置同时只允许一个探测作业。
/// </summary>
internal sealed class CapabilitiesDialog : DpiAwareForm
{
    private readonly CompatService _compat;
    private readonly FileStore _store;
    private readonly JobRunner _jobs;
    private readonly ProjectProfile _profile;
    private readonly string _scope;

    private readonly Label _status = Ui.StatusLine();
    // 状态行宿主：与两个主页面同一接线口径——放不下时右侧给「查看完整错误」入口。
    // 对话框的状态行宽度随对话框固定，长错误同样会被截断，此前只有悬停提示。
    private readonly StatusLineHost _statusHost;
    private readonly Button _recheckButton;
    private readonly Label _cliBadge = Ui.StatusText("检查中…");
    private readonly Label _cliDetail = Ui.Caption();
    private readonly Label _loginBadge = Ui.StatusText("—");
    private readonly Label _loginDetail = Ui.Caption();
    private readonly Label _projectBadge = Ui.StatusText("—");
    private readonly Label _projectDetail = Ui.Caption();
    private readonly Label _compatBadge = Ui.StatusText("—");
    private readonly Label _compatDetail = Ui.Caption();
    private readonly TextBox _projectId = Ui.Input();
    private readonly TextBox _branch = Ui.Input();
    private readonly TextBox _endpointId = Ui.Input();
    private readonly CheckBox _confirm = new() { Text = "我确认这是可修改的测试接口", AutoSize = true };
    private readonly Button _runButton;
    private readonly Button _stopButton;
    private readonly Label _progress = Ui.StatusText();
    private readonly Label _headline = Ui.StatusText();
    private readonly Label _resultHint;
    private readonly ListView _checks = Ui.List();

    private bool _checking;
    private JobInfo? _probeJob;
    private bool _probeShown;
    /// <summary>
    /// 已经为哪一次探测请求过停止（第三百八十八轮）：取消是协作式的，从点下「停止」到真的停住那一段，
    /// 进度行要说 <see cref="JobOutcome.StoppingText"/> 那一句，而不是继续念「正在写入兼容探测…」。
    /// 写它一处（<c>RequestStop</c>）、清它一处（<c>ShowProbeRunning(null)</c>）。
    /// </summary>
    private string? _stopRequestedJobId;
    /// <summary>
    /// 探测这一作业的<b>种类</b>唯一落点：起作业时传给 <c>JobRunner</c> 的是它，进度行那句「正在…」「正在停止…」
    /// 里出现的也是它。两处必须是同一个字面——那一行报的作业名说的就是屏上这颗钮正在停的那一次。
    /// </summary>
    private const string ProbeKind = "写入兼容探测";

    public CapabilitiesDialog(CompatService compat, FileStore store, JobRunner jobs, ProjectProfile profile)
    {
        _compat = compat;
        _store = store;
        _jobs = jobs;
        _profile = profile;
        _scope = CompatEntries.JobScope(profile.Id);

        Text = "连接与写入探测 — " + profile.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // 窗体高按「结果区要给几行」算（CapabilitiesLayout）：620 时结果区只剩 82px、清单本体 39px，
        // 而一次探测登记 7 条检查——跑完要滚动才看得全（第二百四十七轮 wine 96 档实测）。
        DpiScale.Apply(this, new Size(780, CapabilitiesLayout.DialogHeight));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _recheckButton = Ui.Ghost("重新检查");
        _recheckButton.Click += async (_, _) => await CheckAsync();

        _runButton = Ui.Primary("运行写入探测");
        _runButton.Click += (_, _) => RunProbe();
        _stopButton = Ui.DangerGhost("停止");
        _stopButton.Click += (_, _) => RequestStop();
        _stopButton.Visible = false;
        _progress.Visible = false;
        _progress.Margin = new Padding(UiTokens.Md, 0, 0, 0);
        _projectId.TextChanged += (_, _) => RefreshRunState();
        _endpointId.TextChanged += (_, _) => RefreshRunState();
        _confirm.CheckedChanged += (_, _) => RefreshRunState();

        _checks.Columns.Add("检查项", 200);
        _checks.Columns.Add("结果", 70);
        _checks.Columns.Add("详情", 440);

        var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 1, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var capRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 0, 0, UiTokens.Xs) };
        var capTitle = Ui.Section("连接检查");
        capTitle.Margin = new Padding(0, 0, UiTokens.Md, 0);
        capRow.Controls.Add(capTitle);
        capRow.Controls.Add(_recheckButton);
        head.Controls.Add(capRow);
        head.Controls.Add(FormRow("Apifox CLI", ValuesRow(_cliBadge, _cliDetail)));
        head.Controls.Add(FormRow("登录状态", ValuesRow(_loginBadge, _loginDetail)));
        head.Controls.Add(FormRow("项目读取", ValuesRow(_projectBadge, _projectDetail)));
        head.Controls.Add(FormRow("写入兼容", ValuesRow(_compatBadge, _compatDetail)));

        var probe = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 1, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        probe.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        probe.Controls.Add(SectionCaption("写入兼容探测"));
        probe.Controls.Add(Ui.Caption("探测会对指定接口执行三类往返写入并逐项回读核对；请只使用专用测试项目或测试分支中的接口。"));
        probe.Controls.Add(FormRow("测试项目 ID", Ui.Field(_projectId)));
        probe.Controls.Add(FormRow("测试分支名", Ui.Field(_branch)));
        probe.Controls.Add(FormRow("测试接口 ID", Ui.Field(_endpointId)));
        probe.Controls.Add(FormRow("", _confirm));
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, UiTokens.Sm, 0, 0) };
        actions.Controls.Add(_runButton);
        actions.Controls.Add(_stopButton);
        actions.Controls.Add(_progress);
        probe.Controls.Add(actions);
        probe.Controls.Add(Ui.Caption("探测通过按 CLI 版本登记，CLI 升级后需重新探测；未通过时只允许只读检查，同步计划无法生成。"));

        var resultHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, UiTokens.Lg) };
        _headline.Dock = DockStyle.Top;
        _headline.Visible = false;
        _resultHint = Ui.Caption("尚未运行写入探测。填写上方测试接口并勾选确认后运行；逐项检查结果在这里显示。");
        _resultHint.Dock = DockStyle.Top;
        resultHost.Controls.Add(Ui.Hairline(_checks));
        resultHost.Controls.Add(_headline);
        resultHost.Controls.Add(_resultHint);
        _checks.Visible = false;

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0) };
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusHost);

        // 停靠按集合倒序生效：状态行最先停靠（最底部），结果区填满剩余空间，探测区与连接检查依次在最上。
        Controls.Add(resultHost);
        Controls.Add(probe);
        Controls.Add(head);
        Controls.Add(statusHost);

        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => OnTick();
        timer.Start();
        _timer = timer;
    }

    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>Esc 关闭本对话框（其余对话框由 CancelButton 承担；与标题栏 × 同语义——探测在服务层继续执行，结果按 CLI 版本登记，重开可查）。</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _projectId.Text = _profile.Apifox.ProjectId ?? "";
        _branch.Text = _profile.Apifox.BranchName ?? "";
        RefreshRunState();
        _ = CheckAsync();
        OnTick();
    }

    /// <summary>连接检查（只读）：CLI 版本、登录、项目读取与写入兼容登记一次取得。</summary>
    private async Task CheckAsync()
    {
        if (_checking) return;
        _checking = true;
        _recheckButton.Enabled = false;
        _statusHost.SetText("正在检查 CLI、登录状态与项目读取…", UiTokens.Muted);
        try
        {
            var report = await _compat.CapabilitiesAsync(_profile, CancellationToken.None);
            if (IsDisposed) return;
            ShowReport(report);
            SetStatus(null);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus("连接检查失败：" + ex.Message);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("连接检查失败：" + Redactor.Redact(ex.Message));
        }
        finally
        {
            _checking = false;
            if (!IsDisposed) _recheckButton.Enabled = true;
        }
    }

    private void ShowReport(CapabilityReport report)
    {
        if (report.Cli.Found)
        {
            SetBadge(_cliBadge, _cliDetail, CompatEntries.CliBadge(found: true), (report.Cli.Version ?? "版本未知") + "（" + report.Cli.Node + " " + report.Cli.Script + "）");
        }
        else
        {
            SetBadge(_cliBadge, _cliDetail, CompatEntries.CliBadge(found: false), report.Cli.Error ?? "未找到 Apifox CLI");
        }

        if (report.Login is { } login)
        {
            SetBadge(_loginBadge, _loginDetail, CompatEntries.LoginBadge(login.LoggedIn), login.Message);
        }
        else
        {
            SetBadge(_loginBadge, _loginDetail, ("—", "muted"), "CLI 不可用，未检查登录状态");
        }

        if (report.Project is { } project)
        {
            SetBadge(_projectBadge, _projectDetail, CompatEntries.ProjectBadge(project.Readable),
                project.Readable ? "接口总数 " + project.Total : project.Message);
        }
        else
        {
            SetBadge(_projectBadge, _projectDetail, ("—", "muted"), "未设置项目或未登录");
        }

        SetBadge(_compatBadge, _compatDetail, CompatEntries.WriteCompatBadge(report.WriteCompat.Verified), report.WriteCompat.Message);
    }

    /// <summary>输入校验内联展示 + 主按钮灰掉，不弹窗；探测执行中输入整体禁用。</summary>
    private void RefreshRunState()
    {
        var running = _probeShown;
        _projectId.Enabled = _branch.Enabled = _endpointId.Enabled = _confirm.Enabled = !running;
        _runButton.Enabled = !running && CompatEntries.CanRun(_projectId.Text.Trim(), _endpointId.Text.Trim(), _confirm.Checked);
    }

    /// <summary>运行写入探测：原生确认（默认按钮在「取消」）后经共享 JobRunner 调既有 ProbeAsync。</summary>
    private void RunProbe()
    {
        var projectId = _projectId.Text.Trim();
        var endpointIdText = _endpointId.Text.Trim();
        if (!CompatEntries.CanRun(projectId, endpointIdText, _confirm.Checked)) return;
        var choice = MessageBox.Show(this,
            CompatEntries.ProbeConfirmText(projectId, _branch.Text.Trim(), endpointIdText),
            "确认运行写入探测",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.OK) return;
        var request = new WriteProbeRequest
        {
            ProjectId = projectId,
            Branch = _branch.Text.Trim().Length == 0 ? null : _branch.Text.Trim(),
            EndpointId = long.TryParse(endpointIdText, out var id) ? id : 0,
            Confirm = true,
        };
        var workDir = Path.Combine(_store.CompatDir, "probe-" + DateTimeOffset.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture));
        try
        {
            _jobs.Start(_scope, ProbeKind, async job => await _compat.ProbeAsync(_profile, request, workDir, job.Token));
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            SetStatus("无法开始探测：" + Redactor.Redact(ex.Message));
            return;
        }
        _headline.Visible = false;
        _resultHint.Text = "尚未运行写入探测。填写上方测试接口并勾选确认后运行；逐项检查结果在这里显示。";
        _resultHint.ForeColor = UiTokens.Muted;
        _checks.Visible = false;
        _checks.Items.Clear();
        ShowProbeRunning(_jobs.Running(_scope));
        OnTick();
    }

    /// <summary>
    /// 探测执行中：输入禁用、运行钮换成停止（位置固定不跳位），状态行显示进行中说明。
    /// <para>
    /// <b>键盘跟着落到还在屏上的那一颗</b>（第三百九十四轮）：这一行与引导横幅、任务页作业区是同一形状
    /// ——一颗被藏起来、另一颗显出来，而被藏起来的那颗此刻正拿着键盘。焦点落在被藏起来的那颗上时，
    /// 框架当场把焦点挪走（挪到父面那一行上），于是「按完『运行写入探测』，键盘不在任何能按的东西上」：
    /// 想停掉这一次探测得先 Tab 一轮找回那颗「停止」。判定在 <see cref="ProbeFocusPlan"/>（算式在
    /// <see cref="FocusHandover"/>，三处共用一份），触达只在本方法一处。
    /// </para>
    /// <para>
    /// <b>顺序是判据</b>：焦点必须在换显隐<b>之前</b>记下来（<c>Visible = false</c> 一落下去框架当场就把焦点挪走了，
    /// 之后再读 <c>Focused</c> 永远答「不用交」）；交键盘排在 <see cref="RefreshRunState"/> 之后
    /// （可用性先落定，交给一颗停用的钮是空操作）。
    /// </para>
    /// </summary>
    private void ShowProbeRunning(JobInfo? job)
    {
        // 先记下焦点在哪一颗：换显隐与换可用性之后就读不到了（与引导横幅、任务页作业区同一纪律）。
        var runHadFocus = _runButton.Focused;
        var stopHadFocus = _stopButton.Focused;
        var running = job is not null;
        _runButton.Visible = !running;
        _stopButton.Visible = running;
        _progress.Visible = running;
        // 这一处每个轮询周期都走（OnTick 里），所以停止的请求记在字段里、由这一处读——
        // 只在点击那一刻改文字，半秒后就被这一行抹回去了。
        // 先问「有没有一个作业在跑」再比 Id：两个 null 相等不是「请求过停止」，而是「什么都没在跑」。
        var stoppingRequested = job is not null && _stopRequestedJobId == job.Id;
        _progress.Text = JobOutcome.StoppingText(job?.Kind ?? ProbeKind, stoppingRequested)
            ?? $"正在{ProbeKind}…（逐项写入并回读核对，需要一些时间）";
        _progress.ForeColor = UiTokens.Text;
        if (running) _statusHost.SetText("", _status.ForeColor);
        if (!running) _stopRequestedJobId = null;
        RefreshRunState();

        // 原本聚焦的那一颗被藏起来了：把键盘交给还在屏上的那一颗（两颗都不在屏上时不猜）。
        // 这一行只有这一处换显隐：探测起来时「运行写入探测」藏、「停止」显，探测结束时反过来。
        var landing = ProbeFocusPlan.Landing(runHadFocus, stopHadFocus, _runButton.Visible, _stopButton.Visible);
        if (landing == ProbeFocusPlan.Run) _runButton.Focus();
        else if (landing == ProbeFocusPlan.Stop) _stopButton.Focus();
    }

    /// <summary>
    /// 点下「停止」：请求由 <c>JobRunner.Cancel</c> 收下，进度行当场就得换一句话。
    /// <para>
    /// <b>补的是哪个缺口</b>：探测会在测试接口上写三段内容再逐项回读，中途停下要先恢复说明，
    /// 这一段本来就要时间。此前点下去之后那一行照旧念「正在写入兼容探测…」，一个字都不变，
    /// 用户看不出那一击有没有被收到——而这一屏的说明行写的正是「需要一些时间」。
    /// </para>
    /// <para>没有探测在跑时 <c>Cancel</c> 答 false（那颗钮本就只在跑着时可见），什么都不记。</para>
    /// </summary>
    private void RequestStop()
    {
        if (_jobs.Running(_scope) is not { } job || !_jobs.Cancel(_scope)) return;
        _stopRequestedJobId = job.Id;
        // 走的是轮询每周期都走的那一处：文字只在那一处拼，重画与定时重画不会是两套说法。
        ShowProbeRunning(job);
    }

    private void OnTick()
    {
        var job = _jobs.Running(_scope);
        if (job is not null)
        {
            _probeJob = job;
            _probeShown = true;
            ShowProbeRunning(job);
            return;
        }
        if (!_probeShown) return;
        _probeShown = false;
        ShowProbeRunning(null);
        FinishProbe(_probeJob);
        _probeJob = null;
    }

    private void FinishProbe(JobInfo? job)
    {
        if (job is null) return;
        if (job.State == "canceled")
        {
            ShowResultHint("已停止写入兼容探测。测试接口的说明已在停止前恢复；可重新运行或关闭本窗口。", UiTokens.WarnText);
            return;
        }
        if (job.State == "failed")
        {
            ShowResultHint("写入兼容探测失败：" + (job.Error ?? "原因未知") + " 写入保持关闭。", UiTokens.BadText);
            return;
        }
        if (job.Result is not WriteCompatRecord record)
        {
            ShowResultHint("写入兼容探测没有返回结果记录，写入保持关闭。", UiTokens.BadText);
            return;
        }
        var (text, tone) = CompatEntries.ResultHeadline(record.Passed, record.CliVersion);
        _headline.Text = text;
        _headline.ForeColor = ToneColor(tone);
        _headline.Visible = true;
        _resultHint.Visible = false;
        _checks.BeginUpdate();
        _checks.Items.Clear();
        foreach (var check in record.Checks)
        {
            var (stateText, stateTone) = CompatEntries.CheckBadge(check.Passed);
            var item = new ListViewItem(check.Name);
            var state = item.SubItems.Add(stateText);
            item.SubItems.Add(check.Detail);
            item.UseItemStyleForSubItems = false;
            state.ForeColor = ToneColor(stateTone);
            _checks.Items.Add(item);
        }
        _checks.EndUpdate();
        _checks.Visible = true;
        // 探测结果已按 CLI 版本登记：重查连接状态，写入兼容行立即反映新记录。
        _ = CheckAsync();
    }

    private void ShowResultHint(string message, Color color)
    {
        _resultHint.Text = message;
        _resultHint.ForeColor = color;
        _resultHint.Visible = true;
    }

    private static void SetBadge(Label badge, Label detail, (string Text, string Tone) badgeValue, string detailText)
    {
        badge.Text = badgeValue.Text;
        badge.ForeColor = ToneColor(badgeValue.Tone);
        detail.Text = detailText;
    }

    private static Label SectionCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTokens.CaptionBold.Value,
        ForeColor = UiTokens.Muted,
        Margin = new Padding(0, 0, 0, UiTokens.Xs),
    };

    /// <summary>标签 + 值的两列表单行：标签固定宽，值按内容宽。</summary>
    private static Control FormRow(string label, Control value)
    {
        var caption = Ui.Body(label);
        caption.Margin = new Padding(0, UiTokens.Sm, UiTokens.Md, UiTokens.Xs);
        value.Margin = new Padding(0, UiTokens.Sm, 0, 0);
        var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(caption, 0, 0);
        row.Controls.Add(value, 1, 0);
        return row;
    }

    private static Control ValuesRow(Label badge, Label detail)
    {
        var values = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        values.Controls.Add(badge);
        values.Controls.Add(detail);
        return values;
    }

    private void SetStatus(string? message)
    {
        _statusHost.SetText(message ?? "", message is null ? UiTokens.Text : UiTokens.BadText);
    }

    /// <summary>语义色调 → 设计令牌颜色（docs/DESIGN.md §2：语义色只承担状态且同时有文字标签）。</summary>
    private static Color ToneColor(string tone) => tone switch
    {
        "ok" => UiTokens.OkText,
        "warn" => UiTokens.WarnText,
        "bad" => UiTokens.BadText,
        "muted" => UiTokens.Muted,
        _ => UiTokens.Text,
    };
}
