using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「模型服务」对话框：编辑项目配置的注释草稿生成方式（仅规则提取/模型服务/材料交换）与
/// 服务地址、模型、超时、每批数量，并保存模型服务密钥。
/// 字段解析经 GenerationEntries 单一口径；越界钳制与 openai 必填校验随外层「保存」由
/// ProfileStore 执行；密钥经既有 CredentialStore（Windows DPAPI，当前用户）保存，不回显、
/// 不导出；密钥只在配置已保存后可设置（与冻结页面同一门控），本对话框对配置对象本身的
/// 落盘不负责——改动随外层「保存」写入。
/// </summary>
internal sealed class GenerationDialog : Form
{
    private readonly CredentialStore _credentials;
    private readonly ProfileStore _profiles;
    private readonly ProjectProfile _profile;
    private readonly bool _profileExists;

    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _baseUrl = new();
    private readonly TextBox _model = new();
    private readonly TextBox _timeout = new();
    private readonly TextBox _batch = new();
    private readonly TextBox _key = new() { UseSystemPasswordChar = true };
    private readonly Button _saveKeyButton;
    private readonly Label _statusLine = Ui.StatusLine();

    public GenerationDialog(CredentialStore credentials, ProfileStore profiles, ProjectProfile profile, bool profileExists)
    {
        _credentials = credentials;
        _profiles = profiles;
        _profile = profile;
        _profileExists = profileExists;

        Text = "模型服务";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 470);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _saveKeyButton = Ui.Ghost("保存密钥");
        _saveKeyButton.Click += (_, _) => SaveKey();
        if (!_profileExists)
        {
            // 与冻结页面同一门控：新建配置未落盘前不能保存密钥（密钥按配置 ID 存储）。
            _saveKeyButton.Enabled = false;
            _statusLine.Text = GenerationEntries.KeyNeedsProfile;
            _statusLine.ForeColor = UiTokens.Muted;
        }

        var applyButton = Ui.Primary("确定");
        applyButton.Click += (_, _) => ApplyFields();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = applyButton;
        CancelButton = cancelButton;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddSection(grid, "注释草稿生成");
        AddRow(grid, "生成方式", _mode);
        AddRow(grid, "服务地址", _baseUrl);
        AddHint(grid, "例如 https://api.openai.com/v1；只发送用户确认的材料。");
        AddRow(grid, "模型", _model);
        AddRow(grid, "超时秒数", _timeout);
        AddRow(grid, "每批数量", _batch);
        AddHint(grid, "每次请求最多包含的注释位置数；数字留空用默认值。");

        AddSection(grid, "服务密钥");
        _key.Width = 280;
        AddRow(grid, "服务密钥", Row(_key, _saveKeyButton));
        AddHint(grid, GenerationEntries.KeyHint + "。");

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

    private void LoadValues()
    {
        _mode.Items.Clear();
        foreach (var (_, label) in GenerationEntries.Modes)
        {
            _mode.Items.Add(label);
        }
        var index = GenerationEntries.ModeIndex(_profile.Generation.Mode);
        if (index < 0 && _profile.Generation.Mode.Length > 0)
        {
            // 未知方式按原值显示一项，不静默改值。
            _mode.Items.Add(_profile.Generation.Mode);
            index = _mode.Items.Count - 1;
        }
        if (index < 0) index = 0;
        _mode.SelectedIndex = index;
        _baseUrl.Text = _profile.Generation.BaseUrl ?? "";
        _model.Text = _profile.Generation.Model ?? "";
        _timeout.Text = _profile.Generation.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        _batch.Text = _profile.Generation.MaxSlotsPerRequest.ToString(CultureInfo.InvariantCulture);
        _key.PlaceholderText = GenerationEntries.KeyPlaceholder(_profileExists && _credentials.HasModelKey(_profile.Id));
    }

    /// <summary>字段解析全部通过后应用到配置对象；不合法停在状态行，不写一半。</summary>
    private void ApplyFields()
    {
        try
        {
            var fields = GenerationEntries.ParseFields(_baseUrl.Text, _model.Text, _timeout.Text, _batch.Text);
            GenerationEntries.Apply(_profile, SelectedModeValue(), fields);
            DialogResult = DialogResult.OK;
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
    }

    /// <summary>下拉框当前项对应的生成方式；未知原值项（追加的末项）返回原值，不静默改值。</summary>
    private string SelectedModeValue()
    {
        var index = _mode.SelectedIndex;
        if (index >= 0 && index < GenerationEntries.Modes.Length) return GenerationEntries.Modes[index].Value;
        return _profile.Generation.Mode;
    }

    /// <summary>保存密钥：先核对配置存在（与冻结页面路由同序），再交既有 CredentialStore 加密存储。</summary>
    private void SaveKey()
    {
        _saveKeyButton.Enabled = false;
        try
        {
            _profiles.Get(_profile.Id);
            _credentials.SetModelKey(_profile.Id, _key.Text.Length == 0 ? null : _key.Text);
            var hasKey = _credentials.HasModelKey(_profile.Id);
            _key.Text = "";
            _key.PlaceholderText = GenerationEntries.KeyPlaceholder(hasKey);
            SetStatus(GenerationEntries.KeySavedMessage(hasKey), UiTokens.OkText);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("保存密钥失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            _saveKeyButton.Enabled = true;
        }
    }

    private static Control Row(Control first, Control second)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.Add(first);
        flow.Controls.Add(second);
        return flow;
    }

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
