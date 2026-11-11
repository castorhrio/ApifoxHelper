using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 定向构建输出（只读）：命令、开始时间、退出码、耗时与输出尾部，数据来自任务目录 build.json（BuildService 落盘）。
/// 构建在任务页发起并经用户确认，本对话框无写入动作；超时与非零退出码如实显示，不显示成完成。
/// 状态表：尚未构建=空态指引；读取失败=内联错误与下一步；正常=概要行（与任务页构建状态行同一口径）+ 输出尾部。
/// <para>
/// <b>命令行可框选、可一键交出</b>（第二百轮）：说明行指着「完整输出可在本机终端重新执行查看」，
/// 而命令行此前是 <c>Label</c>——不可选中、不可复制，那一步在界面上没有落点。现命令行是
/// <see cref="Ui.MonoBox"/>（只读文本框，可框选、Ctrl+A、Ctrl+C），按钮行里多一颗「复制命令」
/// （口径在 <see cref="BuildCommandCopy"/>：交出去的是命令本身，不带「命令：」标签；没有命令时不给按钮）。
/// 同一屏的输出尾部本来就是可框选的只读框，命令行跟上之后这一屏不再有两种行为。
/// </para>
/// </summary>
internal sealed class BuildLogDialog : DpiAwareForm
{
    private readonly TaskStore _tasks;
    private readonly string _taskId;
    private readonly Label _summary = Ui.StatusText();
    private readonly TextBox _command;
    private readonly Button _copyButton;
    private readonly TextBox _output = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    /// <summary>记录里的命令原文（<c>null</c> = 没有可交出去的命令）；复制按钮交出去的就是它。</summary>
    private string? _commandText;

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

        // 折行宽与量宽取同一处：内容网格的实际可用宽度（算式在 Ui.ContentWidth）。
        // 构造这一刻 head 还没有客户区（宽 0），先按窗体客户区宽种一次，布局完成后再按实际可用宽度重算
        // （与 NoticeDialog 同一做法：量宽偏大则长命令被裁掉半行，偏小则这一屏多出一块空白）。
        head.Width = ClientSize.Width;
        var wrapWidth = Ui.ContentWidth(head);

        _command = Ui.MonoBox(BuildCommandCopy.Line(null), wrapWidth);
        _command.Margin = new Padding(0, 0, 0, UiTokens.Xs);

        head.Controls.Add(_summary);
        head.Controls.Add(_command);
        head.Controls.Add(Ui.Caption(BuildCommandCopy.Note));

        // 布局完成后按 grid 的实际可用宽度重算一次：量宽与控件实际拿到的宽度必须是同一个数。
        // 宽度没变就不动——在 Layout 里改尺寸会再触发一次 Layout，不设这道闸会自己绕圈。
        var applied = wrapWidth;
        void ApplyWrapWidth()
        {
            var width = Ui.ContentWidth(head);
            if (width == applied) return;
            applied = width;
            // 宽度变了高度必须跟着重量：只改宽不重量，长命令会被裁掉半行。
            _command.Width = width;
            _command.Height = Ui.MonoBoxHeight(_command.Text, width);
        }

        head.Layout += (_, _) => ApplyWrapWidth();

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

        // 复制命令：说明行指着「粘到本机终端即可重新执行」，没有这颗按钮那一步只能照着屏幕敲
        // （命令行虽已可框选，但「整行一键交出」仍要一颗按钮）。记录里没有命令时隐藏——
        // 一颗点了什么也不会发生的按钮不该出现（判定在 BuildCommandCopy.Target）。
        _copyButton = Ui.Ghost(BuildCommandCopy.Label);
        _copyButton.Visible = false;
        _copyButton.Click += (_, _) =>
        {
            if (BuildCommandCopy.Target(_commandText) is not string target) return;
            _copyButton.Text = Ui.CopyToClipboard(target) ? NoticeCopy.CopiedLabel : NoticeCopy.FailedLabel;
        };
        buttonRow.Controls.Add(_copyButton);

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
        _commandText = record["command"]?.ToString();
        _command.Text = BuildCommandCopy.Line(_commandText);
        // 宽度没变时 Layout 不会触发，高度得在这里按当前宽度重量一次（命令长短不一）。
        _command.Height = Ui.MonoBoxHeight(_command.Text, Math.Max(_command.Width, 1));
        _copyButton.Visible = BuildCommandCopy.Target(_commandText) is not null;
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
        _commandText = null;
        _command.Text = "";
        _copyButton.Visible = false;
    }
}
