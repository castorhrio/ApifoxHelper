using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Reports;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 报告与交接：预览当前任务的 Markdown 报告（只读），并经原生「另存为」导出
/// Markdown/HTML/JSON 报告与前端交接说明。渲染与字节口径走共享的 ReportExportService
/// （与 Web 下载同一份内容、同一份任务目录副本），本层不加报告逻辑。报告全部脱敏。
/// 状态表：预览可读=正文区 + 四个导出入口；任务数据读取失败=内联提示与下一步；
/// 导出结果（含失败原因）在底部状态行如实显示，不弹窗。
/// </summary>
internal sealed class ReportsDialog : Form
{
    private readonly ReportExportService _exports;
    private readonly string _taskId;

    private readonly TextBox _preview = new()
    {
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        BackColor = UiTokens.Panel,
        Font = UiTokens.Mono.Value,
    };
    private readonly Button _mdButton;
    private readonly Button _htmlButton;
    private readonly Button _jsonButton;
    private readonly Button _handoffButton;
    private readonly Label _status = Ui.StatusLine();
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
    };

    public ReportsDialog(ReportExportService exports, CheckTask task)
    {
        _exports = exports;
        _taskId = task.Id;

        Text = "报告与交接 — " + task.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 660);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _mdButton = Ui.Ghost("导出 Markdown");
        _mdButton.Click += (_, _) => ExportReport("md");
        _htmlButton = Ui.Ghost("导出 HTML");
        _htmlButton.Click += (_, _) => ExportReport("html");
        _jsonButton = Ui.Ghost("导出 JSON");
        _jsonButton.Click += (_, _) => ExportReport("json");
        _handoffButton = Ui.Primary("前端交接说明");
        _handoffButton.Click += (_, _) => ExportReport("handoff");
        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        var headRow = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm) };
        headRow.Controls.Add(Ui.Caption("预览为 Markdown 源文，与「导出 Markdown」内容一致；HTML/JSON 与前端交接说明由同一份报告生成。"));

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 6, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _mdButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _htmlButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _jsonButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        _handoffButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        closeButton.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_handoffButton, 0, 0);
        actionRow.Controls.Add(_mdButton, 1, 0);
        actionRow.Controls.Add(_htmlButton, 2, 0);
        actionRow.Controls.Add(_jsonButton, 3, 0);
        actionRow.Controls.Add(new Panel { Margin = new Padding(0) }, 4, 0);
        actionRow.Controls.Add(closeButton, 5, 0);

        var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, UiTokens.Sm) };
        previewHost.Controls.Add(Ui.Hairline(_preview));
        previewHost.Controls.Add(_hint);
        _hint.Visible = false;

        // 停靠按集合倒序生效：标题行最先停靠（顶部），其次按钮行（最底部）、状态行，预览区填满剩余空间。
        Controls.Add(previewHost);
        Controls.Add(statusHost);
        Controls.Add(actionRow);
        Controls.Add(headRow);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _preview.Text = _exports.Preview(_taskId).Markdown;
            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
        }
        catch (UserException ex)
        {
            ShowHint(ex.Message + " 处理后可关闭本窗口重新打开。", UiTokens.WarnText);
        }
        catch (Exception ex)
        {
            ShowHint("报告生成失败：" + Redactor.Redact(ex.Message) + "。处理后可关闭本窗口重新打开。", UiTokens.BadText);
        }
    }

    /// <summary>导出：原生「另存为」选择位置后写入，内容与 Web 下载逐字节一致（UTF-8 无 BOM）。</summary>
    private void ExportReport(string format)
    {
        var (fileName, _) = ReportExportService.Descriptor(format);
        var (title, filter) = format switch
        {
            "html" => ("导出 HTML 报告", "网页（*.html）|*.html|全部文件（*.*）|*.*"),
            "json" => ("导出 JSON 报告", "JSON（*.json）|*.json|全部文件（*.*）|*.*"),
            "handoff" => ("导出前端交接说明", "Markdown（*.md）|*.md|全部文件（*.*）|*.*"),
            _ => ("导出 Markdown 报告", "Markdown（*.md）|*.md|全部文件（*.*）|*.*"),
        };
        using var dialog = new SaveFileDialog { Title = title, FileName = fileName, Filter = filter };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var file = _exports.Render(_taskId, format);
            File.WriteAllBytes(dialog.FileName, file.Bytes);
            SetStatus("已导出到 " + dialog.FileName, UiTokens.OkText);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.WarnText);
        }
        catch (Exception ex)
        {
            SetStatus("导出报告失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    private void ShowHint(string message, Color color)
    {
        _hint.Text = message;
        _hint.ForeColor = color;
        _hint.Visible = true;
        _preview.Visible = false;
        _mdButton.Enabled = false;
        _htmlButton.Enabled = false;
        _jsonButton.Enabled = false;
        _handoffButton.Enabled = false;
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
