using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 定向构建输出（只读）：命令、开始时间、退出码、耗时与输出尾部，数据来自任务目录 build.json（BuildService 落盘）。
/// 构建在任务页发起并经用户确认，本对话框无写入动作；超时与非零退出码如实显示，不显示成完成。
/// 状态表：尚未构建=空态指引；读取失败=内联错误与下一步；正常=概要行（与任务页构建状态行同一口径）+ 输出尾部。
/// </summary>
internal sealed class BuildLogDialog : DpiAwareForm
{
    private readonly TaskStore _tasks;
    private readonly string _taskId;
    private readonly Label _summary = Ui.StatusText();
    private readonly Label _command = Ui.Mono();
    private readonly TextBox _output = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public BuildLogDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _taskId = task.Id;

        Text = "定向构建输出 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        DpiScale.Apply(this, new Size(860, 560));
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 1, Padding = new Padding(UiTokens.Lg, UiTokens.Lg, UiTokens.Lg, UiTokens.Sm), Margin = new Padding(0) };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.Controls.Add(_summary);
        head.Controls.Add(_command);
        head.Controls.Add(Ui.Caption("输出尾部为最近 60 行，敏感值已脱敏；完整输出可在本机终端重新执行查看。"));

        var outputHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, UiTokens.Sm, UiTokens.Lg, UiTokens.Sm) };
        outputHost.Controls.Add(Ui.Hairline(_output));
        outputHost.Controls.Add(_hint);
        _hint.Visible = false;

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, UiTokens.Lg, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），输出区填满剩余空间，标题行最上。
        Controls.Add(outputHost);
        Controls.Add(buttonRow);
        Controls.Add(head);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            ShowRecord(FileStore.ReadNode(_tasks.FilePath(_taskId, TaskFiles.BuildLog)));
        }
        catch (Exception ex)
        {
            ShowHint("读取构建记录失败：" + Redactor.Redact(ex.Message) + "。可关闭后在任务页重新执行构建。", UiTokens.BadText);
        }
    }

    private void ShowRecord(JsonNode? record)
    {
        if (record is null)
        {
            ShowHint("尚未构建。在任务页「生成文档」区点「执行定向构建」：确认后在任务的工作区执行 dotnet build --no-restore 刷新 XML 文档。", UiTokens.Muted);
            return;
        }
        var (text, tone) = BuildStatus.LastText(record);
        _summary.Text = text;
        _summary.ForeColor = tone switch
        {
            "ok" => UiTokens.OkText,
            "warn" => UiTokens.WarnText,
            "bad" => UiTokens.BadText,
            "muted" => UiTokens.Muted,
            _ => UiTokens.Text,
        };
        _command.Text = "命令：" + (record["command"]?.ToString() ?? "—");
        if (DateTimeOffset.TryParse(record["startedAt"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var started))
        {
            Text += "（" + started.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") + "）";
        }
        var tail = record["outputTail"]?.ToString();
        _output.Text = string.IsNullOrWhiteSpace(tail) ? "（构建没有产生输出）" : tail;
        _output.SelectionStart = 0;
        _output.SelectionLength = 0;
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _output.Visible = false;
        _summary.Text = "";
        _command.Text = "";
    }
}
