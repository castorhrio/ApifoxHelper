using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Apifox;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「模块与模板」对话框：编辑项目配置的模块范围与新接口模板（目录、状态、服务、标签、鉴权、
/// 安全方案、公共 Header 参数——方案 3.1 首次配置旅程的受限模板范围）。
/// 「从既有接口提取」经既有 ApifoxCli 读取接口详情，映射经 EndpointTemplateReader 与冻结页面同一口径；
/// 字段解析与文案经 ProfileAdvanced 单一口径，全部通过后整体应用到配置对象，随外层「保存」经
/// ProfileStore 规整与落盘；本对话框自身不落盘。
/// </summary>
internal sealed class ProfileAdvancedDialog : Form
{
    private readonly ApifoxCli _cli;
    private readonly ProjectProfile _profile;

    private readonly TextBox _modules = new() { Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _folderId = new();
    private readonly ComboBox _status = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _serverId = new();
    private readonly TextBox _tags = new();
    private readonly TextBox _auth = MultilineJson();
    private readonly TextBox _security = MultilineJson();
    private readonly TextBox _headers = MultilineJson();
    private readonly TextBox _templateEndpoint = new();
    private readonly Button _extractButton;
    private readonly Label _statusLine = Ui.StatusLine();

    public ProfileAdvancedDialog(ApifoxCli cli, ProjectProfile profile)
    {
        _cli = cli;
        _profile = profile;

        Text = "模块与模板";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 660);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _extractButton = Ui.Ghost("从接口提取");
        _extractButton.Click += async (_, _) => await ExtractAsync();

        var applyButton = Ui.Primary("确定");
        applyButton.Click += (_, _) => ApplyFields();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = applyButton;
        CancelButton = cancelButton;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(grid, "模块范围");
        AddHint(grid, "每行：模块名=路径前缀1,路径前缀2，例如 财务=/api/finance/；新建任务时可按模块选择范围。");
        _modules.Height = 72;
        AddRow(grid, "模块", _modules);

        AddSection(grid, "新接口模板");
        AddHint(grid, "新建接口创建时的目录与公共设置；只随创建写入这些字段，不克隆脚本、用例或 Mock。");
        AddRow(grid, "目录 ID", _folderId);
        AddHint(grid, "留空表示未指定目录；未指定目录时新接口无法创建。");
        AddRow(grid, "状态", _status);
        AddRow(grid, "服务 ID", _serverId);
        AddRow(grid, "标签", _tags);
        AddHint(grid, "用「,」分隔。");
        AddRow(grid, "鉴权 auth", _auth);
        AddRow(grid, "安全方案", _security);
        AddRow(grid, "公共 Header", _headers);
        AddHint(grid, "JSON 对象或数组；留空表示不设置。");
        AddRow(grid, "从既有接口提取", Row(_templateEndpoint, 120, _extractButton));
        AddHint(grid, "填写接口 ID 后提取其目录、状态、服务、鉴权、安全方案、公共 Header 与标签填入上方字段；请检查后再保存。");

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（确定）在最右。
        buttons.Controls.Add(applyButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _statusLine.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _statusLine.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusLine);

        Controls.Add(grid);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        LoadValues();
    }

    private static TextBox MultilineJson() => new() { Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = UiTokens.Mono.Value };

    private static Label SectionCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = UiTokens.CaptionBold.Value,
        ForeColor = UiTokens.Muted,
        Margin = new Padding(0, UiTokens.Lg, 0, UiTokens.Xs),
    };

    /// <summary>小节标题行，跨两列。</summary>
    private void AddSection(TableLayoutPanel grid, string text)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = SectionCaption(text);
        grid.Controls.Add(caption, 0, row);
        grid.SetColumnSpan(caption, 2);
    }

    private void LoadValues()
    {
        _modules.Text = ProfileAdvanced.FormatModules(_profile.Modules);
        _folderId.Text = _profile.NewEndpoint.FolderId?.ToString() ?? "";
        _status.Items.Clear();
        foreach (var (_, label) in ProfileAdvanced.EndpointStatuses)
        {
            _status.Items.Add(label);
        }
        var index = ProfileAdvanced.StatusIndex(_profile.NewEndpoint.Status);
        if (index < 0 && _profile.NewEndpoint.Status.Length > 0)
        {
            // 未知状态值按原值显示一项，不静默改值。
            _status.Items.Add(ProfileAdvanced.StatusText(_profile.NewEndpoint.Status));
            index = _status.Items.Count - 1;
        }
        if (index < 0) index = 0;
        _status.SelectedIndex = index;
        _serverId.Text = _profile.NewEndpoint.ServerId ?? "";
        _tags.Text = ProfileAdvanced.FormatTags(_profile.NewEndpoint.Tags);
        _auth.Text = ProfileAdvanced.FormatJson(_profile.NewEndpoint.Auth);
        _security.Text = ProfileAdvanced.FormatJson(_profile.NewEndpoint.SecurityScheme);
        if (_profile.NewEndpoint.HeaderParameters.Count > 0)
        {
            _headers.Text = ProfileAdvanced.FormatJson(new JsonArray(_profile.NewEndpoint.HeaderParameters.Select(h => (JsonNode)h.DeepClone()).ToArray()));
        }
    }

    /// <summary>全部字段解析通过后整体应用到配置对象；任一项不合法就停在状态行，不写一半。</summary>
    private void ApplyFields()
    {
        try
        {
            var fields = ProfileAdvanced.ParseFields(_folderId.Text, SelectedStatusValue(), _serverId.Text, _tags.Text, _auth.Text, _security.Text, _headers.Text);
            ProfileAdvanced.Apply(_profile, _modules.Text, fields);
            DialogResult = DialogResult.OK;
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
    }

    /// <summary>下拉框当前项对应的状态值；未知原值项（加载或提取时追加的末项）返回原值，不静默改值。</summary>
    private string SelectedStatusValue()
    {
        var index = _status.SelectedIndex;
        if (index >= 0 && index < ProfileAdvanced.EndpointStatuses.Length) return ProfileAdvanced.EndpointStatuses[index].Value;
        return _profile.NewEndpoint.Status;
    }

    /// <summary>从既有接口提取模板：经既有 ApifoxCli 读取接口详情，映射与冻结页面同一口径。</summary>
    private async Task ExtractAsync()
    {
        var endpointIdText = _templateEndpoint.Text.Trim();
        if (ProfileAdvanced.ExtractError(endpointIdText) is { } idError)
        {
            SetStatus(idError, UiTokens.BadText);
            return;
        }
        if (_profile.Apifox.ProjectId is null)
        {
            SetStatus("请先选择 Apifox 项目。", UiTokens.BadText);
            return;
        }
        _extractButton.Enabled = false;
        SetStatus("正在读取接口…", UiTokens.Muted);
        try
        {
            var endpointId = long.Parse(endpointIdText, CultureInfo.InvariantCulture);
            var result = await _cli.GetEndpointAsync(_profile.Apifox, _profile.Apifox.ProjectId, _profile.Apifox.BranchName, endpointId, CancellationToken.None);
            if (!result.Ok || result.Data is not JsonObject data)
            {
                SetStatus("读取接口失败：" + result.Describe(), UiTokens.BadText);
                return;
            }
            FillFromTemplate(EndpointTemplateReader.Read(data));
            SetStatus("已填入模板字段，请检查后保存。", UiTokens.OkText);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("读取接口失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            _extractButton.Enabled = true;
        }
    }

    /// <summary>提取结果回填输入框（取值与空值处理经 ProfileAdvanced 单一口径）。</summary>
    private void FillFromTemplate(NewEndpointTemplate template)
    {
        var fields = ProfileAdvanced.FromTemplate(template);
        _folderId.Text = fields.FolderId;
        var index = ProfileAdvanced.StatusIndex(fields.Status);
        if (index < 0 && fields.Status.Length > 0)
        {
            _status.Items.Add(ProfileAdvanced.StatusText(fields.Status));
            index = _status.Items.Count - 1;
        }
        if (index >= 0) _status.SelectedIndex = index;
        _serverId.Text = fields.ServerId;
        _tags.Text = fields.Tags;
        _auth.Text = fields.Auth;
        _security.Text = fields.Security;
        _headers.Text = fields.Headers;
    }

    private static Control Row(Control first, int width, Control second)
    {
        first.Width = width;
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.Add(first);
        flow.Controls.Add(second);
        return flow;
    }

    private void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = Ui.Body(label);
        caption.Margin = new Padding(0, UiTokens.Sm, UiTokens.Sm, UiTokens.Xs);
        grid.Controls.Add(caption, 0, row);
        field.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);
        field.Width = Math.Max(field.Width, 480);
        grid.Controls.Add(field, 1, row);
    }

    private void AddHint(TableLayoutPanel grid, string text)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Panel { Margin = new Padding(0) }, 0, row);
        var hint = Ui.Caption(text);
        hint.Margin = new Padding(0, 0, 0, UiTokens.Xs);
        grid.Controls.Add(hint, 1, row);
    }

    private void SetStatus(string message, Color color)
    {
        _statusLine.Text = message;
        _statusLine.ForeColor = color;
    }
}
