using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务日志（只读）：按时间顺序展示任务目录 journal.jsonl 的逐条记录——注释回写与同步执行的
/// 每一步（意图先落盘、结果随后追加），数据由 <c>CommentPlanService</c> 与 <c>SyncExecutor</c> 写入。
/// 本对话框无任何写入动作，也不重发、不重试：它只回答「做到哪一步了」。
/// 状态表：读取失败=内联错误与下一步；没有记录=空态说明；正常=说明行 + 计数行 + 未结束动作提示 + 记录清单
/// （打开即选中最后一条——最后一行就是做到的那一步）。
/// 三格文字、计数行、说明行与未结束动作那句话取 <see cref="JournalEntries"/> 一份（与测试工程单一口径）。
/// </summary>
internal sealed class JournalDialog : Form
{
    /// <summary>
    /// 标识列（DESIGN.md §6「列表行」的 Ctrl+C 条款）：首列是时间（同一秒可能有多条、认不出是哪一步），
    /// 第 2 列是事件名（同一事件重复出现），这一行在屏幕上被读作的名字是「摘要」，故声明第 3 列。
    /// </summary>
    private const int SummaryColumn = 2;

    private readonly TaskStore _tasks;
    private readonly string _taskId;

    private readonly Label _counts = Ui.Mono();
    private readonly Label _unfinished = Ui.Caption();
    private readonly ListView _list = Ui.List(identityColumn: SummaryColumn);
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public JournalDialog(TaskStore tasks, CheckTask task)
    {
        _tasks = tasks;
        _taskId = task.Id;

        Text = "任务日志 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(960, 620);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _list.Columns.Add("时间", 150);
        _list.Columns.Add("事件", 150);
        _list.Columns.Add("摘要", 640);

        _counts.AutoSize = false;
        _counts.Dock = DockStyle.Fill;
        _counts.TextAlign = ContentAlignment.MiddleRight;

        var caption = Ui.Caption(JournalEntries.Caption);
        caption.Dock = DockStyle.Fill;
        caption.TextAlign = ContentAlignment.MiddleLeft;

        var headRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 2, Padding = new Padding(0, 0, 0, UiTokens.Sm), Margin = new Padding(0) };
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headRow.Controls.Add(caption, 0, 0);
        headRow.Controls.Add(_counts, 1, 0);

        // 未结束动作那句话：没有这样的动作时整行不显示（不显示一句「没有」）。
        _unfinished.Dock = DockStyle.Top;
        _unfinished.ForeColor = UiTokens.BadText;
        _unfinished.Visible = false;

        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(0, 0, 0, UiTokens.Sm) };
        listHost.Controls.Add(Ui.Hairline(_list));
        listHost.Controls.Add(_hint);
        _hint.Visible = false;

        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;
        var buttonRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, BackColor = UiTokens.Panel, Padding = new Padding(0, UiTokens.Sm, 0, UiTokens.Md) };
        // RightToLeft 流向：先加的在最右（Windows 惯例：确认类按钮在右）。
        buttonRow.Controls.Add(closeButton);

        // 停靠按集合倒序生效：按钮行最先停靠（最底部），其次未结束提示、标题行，列表最后填满剩余空间。
        Controls.Add(listHost);
        Controls.Add(buttonRow);
        Controls.Add(_unfinished);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        List<System.Text.Json.Nodes.JsonNode> lines;
        try
        {
            lines = FileStore.ReadJsonLines(_tasks.FilePath(_taskId, TaskFiles.Journal));
        }
        catch (Exception ex)
        {
            ShowHint("读取任务日志失败：" + Redactor.Redact(ex.Message) + "。请关闭后重新执行一次再试。", UiTokens.BadText);
            return;
        }
        if (lines.Count == 0)
        {
            ShowHint(JournalEntries.EmptyText, UiTokens.Muted);
            return;
        }

        _hint.Visible = false;
        _counts.Text = JournalEntries.CountsLine(lines.Count);
        var unfinished = JournalEntries.UnfinishedActions(lines);
        var unfinishedText = JournalEntries.UnfinishedCaption(unfinished);
        _unfinished.Text = unfinishedText;
        _unfinished.Visible = unfinishedText.Length > 0;

        foreach (var line in lines)
        {
            // 三格文字取 JournalEntries 一份（时间、事件名、摘要），与计数行、未结束动作那句话同源——
            // 这一屏的清单与提示讲的是同一份日志，各拼一遍必然有一处先漂。
            var cells = JournalEntries.Row(line);
            var row = new ListViewItem(cells[0]) { Tag = line };
            row.SubItems.Add(cells[1]);
            row.SubItems.Add(cells[2]);
            _list.Items.Add(row);
        }
        // 打开即选中最后一条：这一屏要回答的是「做到哪一步」，最后一行就是答案
        // （与代码证据清单「打开即选中第一条」同一纪律——打开就有一行被标出来，只是这一屏的答案在末尾）。
        // 选中经 Ui.SelectItem 唯一入口（Selected + Focused + 滚入可见区），与鼠标点同一行同一条路。
        Ui.SelectItem(_list.Items[^1]);
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _list.Visible = false;
        _counts.Text = "";
        _unfinished.Visible = false;
    }
}
