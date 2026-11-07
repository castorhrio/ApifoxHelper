using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Comments;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 生成文档核对结果（只读）：已确认注释是否写入源码、出现在实际生成的 XML 文档、被 Swagger 读取到正确位置。
/// 数据来自任务目录 propagation.json（核对落盘产物）；核对在任务页发起，本对话框无写入动作。
/// 旧生成物不算注释已生效：XML 早于源码修改、未启用 XML 生成、Swagger 未刷新都如实显示，不显示成完成。
/// 状态表：尚未核对=空态指引；读取失败=内联错误与下一步；正常=概要计数 + 条目列表（源码/XML/Swagger 三环状态，颜色 + 文字双通道）+ 条目详情 + XML 文件清单。
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
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = UiTokens.Panel };
    private readonly ListView _xmlList = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, BackColor = UiTokens.Panel };
    private readonly TextBox _detail = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Body.Value,
    };
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

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
        _list.SelectedIndexChanged += (_, _) => ShowDetail();

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

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次 XML 清单、条目详情、标题行，条目列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(detailHost);
        Controls.Add(xmlHost);
        Controls.Add(buttonRow);
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
        _counts.Text = $"位置 {result.Entries.Count} · 源码 {result.Entries.Count(en => en.SourceState == "written")} · XML {result.Entries.Count(en => en.XmlState == "present")} · Swagger {result.Entries.Count(en => en.SwaggerState == "present")}";
        if (result.SwaggerMessage is { } message)
        {
            _swaggerMessage.Text = message;
            _swaggerMessage.Visible = true;
        }

        _list.BeginUpdate();
        foreach (var entry in result.Entries)
        {
            var row = new ListViewItem(entry.Display) { Tag = entry };
            row.SubItems.Add(entry.Tag == "param" && entry.ParameterName is { } name ? "参数 " + name : TaskDisplay.SlotTagText(entry.Tag));
            AddState(row, TaskDisplay.PropagationSourceText(entry.SourceState), TaskDisplay.PropagationSourceTone(entry.SourceState));
            AddState(row, TaskDisplay.PropagationXmlText(entry.XmlState), TaskDisplay.PropagationXmlTone(entry.XmlState));
            AddState(row, TaskDisplay.PropagationSwaggerText(entry.SwaggerState), TaskDisplay.PropagationSwaggerTone(entry.SwaggerState));
            _list.Items.Add(row);
        }
        _list.EndUpdate();
        if (_list.Items.Count > 0)
        {
            _list.Items[0].Selected = true;
        }
        else
        {
            _hint.Text = "没有核对条目。";
            _hint.Visible = true;
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
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _counts.Text = "";
        _checkedAt.Text = "";
        _detail.Text = "";
        _swaggerMessage.Visible = false;
    }

    private void ShowDetail()
    {
        if (_list.SelectedItems.Count == 0 || _list.SelectedItems[0].Tag is not PropagationEntry entry)
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
