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
internal sealed class ReportsDialog : DpiAwareForm
{
    private readonly ReportExportService _exports;
    private readonly string _taskId;

    private readonly TextBox _preview = Ui.Preview(multiline: true, mono: true, wordWrap: false, scrollBars: ScrollBars.Both);
    private readonly Button _mdButton;
    private readonly Button _htmlButton;
    private readonly Button _jsonButton;
    private readonly Button _handoffButton;
    // 读取失败态的唯一行动按钮（LoadFailureState 口径）：动作回到 LoadPreview 同一个入口。
    // 放在底部动作行而不是提示区：这一屏的动作本来就在底部一行，失败时四个导出入口一起停用，
    // 行里只剩「重试」与「关闭」两颗可点，用户不必去猜「重开窗口」。
    private readonly Button _retry = Ui.Ghost(LoadFailureState.RetryLabel);
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
        DpiScale.Apply(this, new Size(920, 660));
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
        _retry.Click += (_, _) => LoadPreview();
        _retry.Visible = false;
        var closeButton = Ui.Ghost("关闭");
        closeButton.Click += (_, _) => Close();
        AcceptButton = closeButton;
        CancelButton = closeButton;

        var headRow = new Panel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, UiTokens.Md, UiTokens.Lg, UiTokens.Sm) };
        headRow.Controls.Add(Ui.Caption("预览为 Markdown 源文，与「导出 Markdown」内容一致；HTML/JSON 与前端交接说明由同一份报告生成。"));

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Lg, 0, UiTokens.Lg, 0) };
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusHost);

        var actionRow = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, BackColor = UiTokens.Panel, ColumnCount = 7, Padding = new Padding(UiTokens.Lg, UiTokens.Xs, UiTokens.Lg, UiTokens.Lg), Margin = new Padding(0) };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        // 第 4 列是 Percent 100 的「把按钮推到右边」那一列：空着就够，不往里放占位控件——
        // 默认尺寸的 Panel 是 200×100，而本行 AutoSize，那一格的高度会变成整行的高度（第二百二十二轮实测 120px 对按钮 40px）。
        _retry.Margin = new Padding(UiTokens.Sm, 0, 0, 0);
        actionRow.Controls.Add(_retry, 5, 0);
        actionRow.Controls.Add(closeButton, 6, 0);

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
        LoadPreview();
    }

    /// <summary>
    /// 生成预览（打开时与失败后点「重试」走同一个入口，不复制渲染逻辑）。
    /// 失败态按 <see cref="LoadFailureState"/> 口径：一句可操作文案（指名「重试」）+ 唯一行动按钮，
    /// 四个导出入口一起停用——预览都没生成出来，导出的只会是同一份失败。
    /// 此前这里写的是「处理后可关闭本窗口重新打开」：把「再读一次」交给用户手动重开窗口，
    /// 屏上没有任何可点的入口，与 DESIGN.md §7「禁用不得移除下一步」相违。
    /// </summary>
    private void LoadPreview()
    {
        try
        {
            _preview.Text = _exports.Preview(_taskId).Markdown;
            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
            // 读成功即退出失败态：否则重试成功后仍停在失败态（预览有内容、屏上却还说生成失败）。
            _preview.Visible = true;
            _hint.Visible = false;
            _retry.Visible = false;
            _mdButton.Enabled = true;
            _htmlButton.Enabled = true;
            _jsonButton.Enabled = true;
            _handoffButton.Enabled = true;
        }
        catch (UserException ex)
        {
            // UserException 的文案本身就是给用户看的那一句，不再脱敏；仍进失败态。
            ShowHint(LoadFailureState.FailureMessage(LoadFailureState.Reports.Title, ex.Message, LoadFailureState.Reports.Hint), UiTokens.WarnText);
        }
        catch (Exception ex)
        {
            ShowHint(LoadFailureState.FailureMessage(LoadFailureState.Reports.Title, Redactor.Redact(ex.Message), LoadFailureState.Reports.Hint), UiTokens.BadText);
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
        // 失败态的唯一行动按钮：文案里指名的那颗按钮必须真的在屏上（第四十二轮纪律）。
        _retry.Visible = true;
        _mdButton.Enabled = false;
        _htmlButton.Enabled = false;
        _jsonButton.Enabled = false;
        _handoffButton.Enabled = false;
    }

    private void SetStatus(string message, Color color) => _statusHost.SetText(message, color);
}
