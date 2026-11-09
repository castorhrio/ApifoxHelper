using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「序列化与分析」对话框：编辑项目配置的 JSON 命名与响应封装（响应契约追踪的数据来源）、
/// 代码分析（配置名、编译常量、调用深度、方法预算、XML 文档路径、Async 后缀）、质量规则
/// 与 Apifox CLI（读取并发、node/cli.js 路径）。
/// 字段解析与文案经 ProfileAnalysis 单一口径；数字越界钳制与 JSON 命名取值校验随外层「保存」
/// 由 ProfileStore 执行；本对话框自身不落盘——改动应用到同一配置对象，随外层「保存」写入。
/// </summary>
internal sealed class ProfileAnalysisDialog : Form
{
    private readonly ProjectProfile _profile;

    private readonly ComboBox _naming = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _enumAsString = new() { Text = "", AutoSize = true };
    private readonly CheckBox _nonNullable = new() { Text = "", AutoSize = true };
    private readonly TextBox _wrappers = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly TextBox _configuration = Ui.Input();
    private readonly TextBox _symbols = Ui.Input();
    private readonly TextBox _depth = Ui.Input();
    private readonly TextBox _budget = Ui.Input();
    private readonly TextBox _xmlDocPaths = Ui.Input(multiline: true, scrollBars: ScrollBars.Vertical);
    private readonly CheckBox _suppressAsync = new() { Text = "", AutoSize = true };
    private readonly TextBox _dataProperty = Ui.Input();
    private readonly CheckBox _requireParam = new() { Text = "", AutoSize = true };
    private readonly CheckBox _requireProperty = new() { Text = "", AutoSize = true };
    private readonly TextBox _concurrency = Ui.Input();
    private readonly TextBox _nodePath = Ui.Input();
    private readonly TextBox _cliPath = Ui.Input();
    private readonly Label _statusLine = Ui.StatusLine();
    /// <summary>字段标识 → 描边面板：解析失败按 UserException.Field 定位描红（DESIGN.md §6 校验失败描边）。</summary>
    private readonly Dictionary<string, Ui.FieldBox> _fields = [];

    public ProfileAnalysisDialog(ProjectProfile profile)
    {
        _profile = profile;

        Text = "序列化与分析";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(700, 700);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        var applyButton = Ui.Primary("确定");
        applyButton.Click += (_, _) => ApplyFields();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = applyButton;
        CancelButton = cancelButton;

        // 长内容区（DESIGN.md §6）：网格统一经 Ui.ScrollHost 出厂（AutoScroll 与点击取焦只有一份口径）。
        var grid = Ui.FieldGrid(ProfileGridColumns.LabelWidth);

        AddSection(grid, "序列化与响应封装");
        AddRow(grid, "JSON 命名", _naming);
        AddRow(grid, "枚举输出为字符串", _enumAsString);
        AddRow(grid, "非空引用类型", _nonNullable);
        AddHint(grid, "与 Swashbuckle SupportNonNullableReferenceTypes 一致。");
        _wrappers.Height = 64;
        AddRow(grid, "通用响应封装", Ui.Field(_wrappers));
        AddHint(grid, "每行：类型名=数据属性名，例如 PNOSActionResult=Data；用于追踪 data 的实际类型（响应契约）。");

        AddSection(grid, "代码分析");
        AddRow(grid, "分析配置名", Ui.Field(_configuration));
        AddRow(grid, "编译常量", Ui.Field(_symbols));
        AddHint(grid, "留空按目标框架与配置推导（DEBUG;TRACE;NET8_0…）。");
        AddRow(grid, "调用深度", Field(_depth, ProfileAnalysis.FieldMaxTraceDepth));
        AddRow(grid, "方法预算", Field(_budget, ProfileAnalysis.FieldMaxMethodsPerEndpoint));
        AddHint(grid, "数字留空用默认值。");
        _xmlDocPaths.Height = 56;
        AddRow(grid, "XML 文档路径", Field(_xmlDocPaths, ProfileAnalysis.FieldXmlDocPaths));
        AddHint(grid, "可选：项目名=路径，每行一个；默认按 bin/配置/目标框架 推断。");
        AddRow(grid, "去掉 Async 后缀", _suppressAsync);
        AddHint(grid, "与 MvcOptions.SuppressAsyncSuffixInActionNames 一致（默认开启）。");

        AddSection(grid, "质量规则");
        AddRow(grid, "通用 data 属性名", Ui.Field(_dataProperty));
        AddRow(grid, "参数说明必填", _requireParam);
        AddRow(grid, "字段说明必填", _requireProperty);

        AddSection(grid, "Apifox CLI");
        AddRow(grid, "读取并发", Field(_concurrency, ProfileAnalysis.FieldReadConcurrency));
        AddHint(grid, "只读详情的并发数（1–8）；写入始终串行。");
        AddRow(grid, "node 路径", Ui.Field(_nodePath));
        AddRow(grid, "cli.js 路径", Ui.Field(_cliPath));
        AddHint(grid, "可选；为空时自动查找 PATH 与全局 npm 目录。");

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，主行动（确定）在最右。
        buttons.Controls.Add(applyButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _statusLine.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _statusLine.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusLine);

        var gridScroll = Ui.ScrollHost();
        gridScroll.BackColor = UiTokens.Panel;
        gridScroll.Controls.Add(grid);
        Controls.Add(gridScroll);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        LoadValues();
    }

    private void LoadValues()
    {
        _naming.Items.Clear();
        foreach (var (_, label) in ProfileAnalysis.NamingPolicies)
        {
            _naming.Items.Add(label);
        }
        var index = ProfileAnalysis.NamingIndex(_profile.Serialization.NamingPolicy);
        if (index < 0 && _profile.Serialization.NamingPolicy.Length > 0)
        {
            // 未知命名策略按原值显示一项，不静默改值。
            _naming.Items.Add(_profile.Serialization.NamingPolicy);
            index = _naming.Items.Count - 1;
        }
        if (index < 0) index = 0;
        _naming.SelectedIndex = index;
        _enumAsString.Checked = _profile.Serialization.EnumAsString;
        _nonNullable.Checked = _profile.Serialization.NonNullableReferenceTypes;
        _wrappers.Text = ProfileAnalysis.FormatWrappers(_profile.ResponseWrappers);
        _configuration.Text = _profile.Analysis.Configuration;
        _symbols.Text = ProfileAnalysis.FormatSymbols(_profile.Analysis.PreprocessorSymbols);
        _depth.Text = _profile.Analysis.MaxTraceDepth.ToString(CultureInfo.InvariantCulture);
        _budget.Text = _profile.Analysis.MaxMethodsPerEndpoint.ToString(CultureInfo.InvariantCulture);
        _xmlDocPaths.Text = ProfileAnalysis.FormatXmlDocPaths(_profile.Analysis.XmlDocPaths);
        _suppressAsync.Checked = _profile.Analysis.SuppressAsyncSuffixInActionNames;
        _dataProperty.Text = _profile.Quality.GenericDataProperty;
        _requireParam.Checked = _profile.Quality.RequireParameterDescription;
        _requireProperty.Checked = _profile.Quality.RequirePropertyDescription;
        _concurrency.Text = _profile.Apifox.ReadConcurrency.ToString(CultureInfo.InvariantCulture);
        _nodePath.Text = _profile.Apifox.NodePath ?? "";
        _cliPath.Text = _profile.Apifox.CliScriptPath ?? "";
    }

    /// <summary>全部字段解析通过后整体应用到配置对象；任一项不合法就停在状态行，不写一半。</summary>
    private void ApplyFields()
    {
        try
        {
            var analysis = ProfileAnalysis.ParseAnalysis(_configuration.Text, _symbols.Text, _depth.Text, _budget.Text, _xmlDocPaths.Text);
            var cli = ProfileAnalysis.ParseCli(_nodePath.Text, _cliPath.Text, _concurrency.Text);
            ProfileAnalysis.Apply(
                _profile,
                SelectedNamingValue(),
                _enumAsString.Checked,
                _nonNullable.Checked,
                ProfileAnalysis.ParseWrappers(_wrappers.Text),
                analysis,
                _suppressAsync.Checked,
                _dataProperty.Text,
                _requireParam.Checked,
                _requireProperty.Checked,
                cli);
            DialogResult = DialogResult.OK;
        }
        catch (UserException ex)
        {
            MarkInvalidField(ex);
            SetStatus(ex.Message, UiTokens.BadText);
        }
    }

    /// <summary>包 1px 描边并登记字段标识；解析失败时按 UserException.Field 描红对应输入框。</summary>
    private Ui.FieldBox Field(TextBox input, string fieldId)
    {
        var field = Ui.Field(input);
        _fields[fieldId] = field;
        return field;
    }

    /// <summary>解析失败描红对应字段（形状通道）；文字仍在状态行（双通道，DESIGN.md §2/§6）。</summary>
    private void MarkInvalidField(UserException ex)
    {
        if (ex.Field is { } field && _fields.TryGetValue(field, out var box)) box.SetInvalid(true);
    }

    /// <summary>下拉框当前项对应的命名策略；未知原值项（追加的末项）返回原值，不静默改值。</summary>
    private string SelectedNamingValue()
    {
        var index = _naming.SelectedIndex;
        if (index >= 0 && index < ProfileAnalysis.NamingPolicies.Length) return ProfileAnalysis.NamingPolicies[index].Value;
        return _profile.Serialization.NamingPolicy;
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
