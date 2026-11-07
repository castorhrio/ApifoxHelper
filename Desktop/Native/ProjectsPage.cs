using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Apifox;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 项目配置页：列出配置，新建/编辑核心字段（名称、仓库、API 项目、回写范围、Swagger 来源、Apifox 目标、版本备注），
/// 「模块与模板」编辑模块范围与新接口模板（含从既有接口提取），「序列化与分析」编辑 JSON 命名、响应封装、
/// 代码分析、质量规则与 Apifox CLI（响应封装是响应契约追踪的数据来源），「模型服务」编辑注释草稿生成方式与
/// 模型服务密钥（DPAPI 加密存储在 CredentialStore），「连接与写入探测」经既有 CompatService
/// 检查 CLI/登录/项目读取并按需运行写入兼容探测（F02，写入门禁的唯一登记入口）；
/// 「导出配置（不含凭据）」经 ProfileExport 与 REST 下载同一字节口径另存为。
/// 保存走 ProfileStore（含路径与取值校验）。
/// </summary>
internal sealed class ProjectsPage : UserControl, INativePage
{
    private readonly ProfileStore _profiles;
    private readonly CompatService _compat;
    private readonly ApifoxCli _cli;
    private readonly FileStore _store;
    private readonly JobRunner _jobs;
    private readonly CredentialStore _credentials;
    private readonly ListView _list = Ui.List();
    private readonly Panel _empty;
    private readonly Label _status = Ui.StatusLine();
    private readonly Button _editButton;
    private readonly Button _probeButton;
    // 选中保留：列表在切页、刷新与保存后都会重建，按配置 Id 恢复选中与键盘焦点（与任务页同一口径）。
    private string? _selectedId;
    private const int ProfileNameColumnWidth = 180;

    public ProjectsPage(ProfileStore profiles, CompatService compat, ApifoxCli cli, FileStore store, JobRunner jobs, CredentialStore credentials)
    {
        _profiles = profiles;
        _compat = compat;
        _cli = cli;
        _store = store;
        _jobs = jobs;
        _credentials = credentials;

        var newButton = Ui.Primary("新建配置");
        newButton.Click += (_, _) => ShowDialog(null);
        _editButton = Ui.Ghost("编辑");
        _editButton.Click += (_, _) => ShowDialog(Selected());
        _probeButton = Ui.Ghost("连接与写入探测");
        _probeButton.Click += (_, _) => ShowCapabilities();
        var refreshButton = Ui.Ghost("刷新");
        refreshButton.Click += (_, _) => RefreshData();
        _list.SelectedIndexChanged += (_, _) =>
        {
            _selectedId = Selected()?.Id;
            _editButton.Enabled = _list.SelectedIndices.Count > 0;
            _probeButton.Enabled = _list.SelectedIndices.Count > 0;
        };
        // 双击只在命中行时打开该行的编辑（Ui.ActivateItem 唯一入口）；双击空白区不动作——
        // ListView.DoubleClick 不区分命中行，双击空白区（首击清空选中）曾借空选中打开「新建配置」对话框。
        Ui.ActivateItem(_list, item => ShowDialog(item.Tag as ProjectProfile));
        // 标识列（首列）吸收剩余可视宽度：窗口或列表尺寸变化即重算（ListColumns 口径）。
        _list.Resize += (_, _) => FitColumns();

        _list.Columns.Add("名称", ProfileNameColumnWidth);
        _list.Columns.Add("API 项目", 260);
        _list.Columns.Add("Apifox 项目", 110);
        _list.Columns.Add("分支", 100);
        _list.Columns.Add("Swagger", 90);
        _list.Columns.Add("更新时间", 140);

        // 空态的按钮是独立实例：同一控件不能同时挂在工具栏与空态面板上。
        var emptyButton = Ui.Primary("新建配置");
        emptyButton.Click += (_, _) => ShowDialog(null);
        _empty = BuildEmpty(emptyButton);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Bg, ColumnCount = 2, Margin = new Padding(0) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 0, UiTokens.Md, 0) };
        buttons.Controls.Add(newButton);
        buttons.Controls.Add(_editButton);
        buttons.Controls.Add(_probeButton);
        buttons.Controls.Add(refreshButton);
        _status.Dock = DockStyle.Fill;
        toolbar.Controls.Add(buttons, 0, 0);
        toolbar.Controls.Add(_status, 1, 0);

        Controls.Add(Ui.Hairline(_list));
        Controls.Add(_empty);
        Controls.Add(toolbar);
    }

    private Panel BuildEmpty(Control newButton)
    {
        var title = Ui.Title("还没有项目配置");
        var hint = Ui.Body("先绑定代码仓库与 Apifox 项目，然后才能创建检查任务。", muted: true);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(title, 0, 1);
        layout.Controls.Add(hint, 0, 2);
        layout.Controls.Add(newButton, 0, 3);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        panel.Controls.Add(layout);
        panel.Visible = false;
        return panel;
    }

    private ProjectProfile? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as ProjectProfile : null;

    public void OnPageShown()
    {
        // 首次显示与每次切回都重读，反映资料目录的最新状态。
        LoadProfiles();
    }

    public void OnPageHidden()
    {
    }

    public void RefreshData() => LoadProfiles();

    private void LoadProfiles()
    {
        // 先取局部：重建清空列表会触发 SelectedIndexChanged 把字段抹成 null。
        var selectedId = _selectedId;
        try
        {
            var profiles = _profiles.List();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var profile in profiles)
            {
                var item = new ListViewItem(profile.Name) { Tag = profile };
                item.SubItems.Add(profile.ApiProject);
                item.SubItems.Add(profile.Apifox.ProjectId ?? "未绑定");
                item.SubItems.Add(profile.Apifox.BranchName ?? "主分支");
                item.SubItems.Add(SwaggerText(profile.Swagger.Type));
                item.SubItems.Add(profile.UpdatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"));
                _list.Items.Add(item);
            }
            _list.EndUpdate();
            FitColumns();
            // 重建后先禁用编辑/探测，恢复选中时由选中事件重新启用；原选中项已不存在则保持禁用。
            _editButton.Enabled = false;
            _probeButton.Enabled = false;
            RestoreSelection(selectedId);
            _empty.Visible = profiles.Count == 0;
            _list.Visible = profiles.Count > 0;
            SetStatus(null);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message);
        }
        catch (Exception ex)
        {
            SetStatus("读取配置失败：" + Redactor.Redact(ex.Message));
        }
    }

    /// <summary>
    /// 标识列（首列「名称」）吸收剩余可视宽度（ListColumns 口径）：列合计对齐可视宽度，
    /// 最小窗口宽度下「更新时间」等列尾列不再被横向滚动条藏住；窗口放不下固定列合计时
    /// 首列回退设计宽度、横向滚动条如实出现。Resize 与重建后都会调用。
    /// </summary>
    private void FitColumns()
    {
        var widths = _list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        _list.Columns[0].Width = ListColumns.FillWidth(_list.ClientSize.Width, ProfileNameColumnWidth, widths);
    }

    /// <summary>重建后恢复选中与键盘焦点（经 Ui.SelectItem 唯一入口）；原选中项已不存在（如被删除）时不动作。</summary>
    private void RestoreSelection(string? selectedId)
    {
        if (selectedId is null) return;
        foreach (ListViewItem item in _list.Items)
        {
            if (item.Tag is ProjectProfile profile && profile.Id == selectedId)
            {
                Ui.SelectItem(item);
                return;
            }
        }
    }

    private static string SwaggerText(string type) => type switch
    {
        "file" => "文件",
        "url" => "URL",
        _ => "未配置",
    };

    private void SetStatus(string? message)
    {
        _status.Text = message ?? "";
    }

    private void ShowDialog(ProjectProfile? editing)
    {
        using var dialog = new ProfileDialog(_profiles, _cli, _credentials, editing);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
            // 刚保存的配置（新建或编辑）在重建后选中，编辑/探测入口保持可用。
            if (dialog.Saved is { } saved) _selectedId = saved.Id;
            LoadProfiles();
        }
    }

    /// <summary>打开连接与写入探测（只读连接检查 + 可选的写入兼容探测，写入边界在 CompatService 单一口径）。</summary>
    private void ShowCapabilities()
    {
        if (Selected() is not { } profile) return;
        using var dialog = new CapabilitiesDialog(_compat, _store, _jobs, profile);
        dialog.ShowDialog(FindForm());
    }
}

/// <summary>
/// 配置编辑对话框：核心字段 + 内联校验（ProfileStore.Save 抛出的 UserException 显示在状态行，
/// 并按 UserException.Field 描红对应输入框，不弹窗）。
/// 「模块与模板」入口打开高级编辑（模块范围与新接口模板），「模型服务」入口打开生成方式与密钥编辑，
/// 改动应用到同一配置对象，随「保存」一起落盘。
/// </summary>
internal sealed class ProfileDialog : Form
{
    private static readonly string[] SwaggerChoices = ["无（暂不核对 Apifox 结构）", "文件（OpenAPI 3.0 JSON）", "URL（http/https 的 JSON 地址）"];

    private readonly ProfileStore _profiles;
    private readonly ApifoxCli _cli;
    private readonly CredentialStore _credentials;
    private readonly ProjectProfile? _editing;
    private readonly ProjectProfile _profile;

    /// <summary>字段标识 → 描边面板：保存校验失败按 UserException.Field 定位描红（DESIGN.md §6 校验失败描边）。</summary>
    private readonly Dictionary<string, Ui.FieldBox> _fields = [];

    private readonly TextBox _name = Ui.Input();
    private readonly TextBox _workspace = Ui.Input();
    private readonly TextBox _sourceVersion = Ui.Input();
    private readonly ComboBox _apiProject = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _allowed = Ui.Input();
    private readonly ComboBox _swaggerType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _swaggerLocation = Ui.Input();
    private readonly ComboBox _projectId = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly ComboBox _branch = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly Label _status = Ui.StatusLine();

    /// <summary>保存成功后的配置（保存失败保持 null；调用方据此在列表中定位刚保存的行）。</summary>
    public ProjectProfile? Saved { get; private set; }
    private readonly Button _findButton;
    private readonly Button _loadProjectsButton;
    private readonly Button _loadBranchesButton;
    private List<ApifoxPicker.ProjectRow> _projectRows = [];
    private List<ApifoxPicker.BranchRow> _branchRows = [];

    public ProfileDialog(ProfileStore profiles, ApifoxCli cli, CredentialStore credentials, ProjectProfile? editing)
    {
        _profiles = profiles;
        _cli = cli;
        _credentials = credentials;
        _editing = editing;
        _profile = editing ?? new ProjectProfile();

        Text = editing is null ? "新建配置" : "编辑配置：" + editing.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 720);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _findButton = Ui.Ghost("查找 API 项目");
        _findButton.Click += async (_, _) => await FindProjectsAsync();
        var browseButton = Ui.Ghost("浏览…");
        browseButton.Click += (_, _) => BrowseWorkspace();
        var advancedButton = Ui.Ghost("模块与模板…");
        advancedButton.Click += (_, _) => ShowAdvanced();
        var analysisButton = Ui.Ghost("序列化与分析…");
        analysisButton.Click += (_, _) => ShowAnalysis();
        var generationButton = Ui.Ghost("模型服务…");
        generationButton.Click += (_, _) => ShowGeneration();
        var exportButton = Ui.Ghost("导出配置（不含凭据）");
        exportButton.Enabled = _editing is not null;
        exportButton.Click += (_, _) => ExportProfile();
        _loadProjectsButton = Ui.Ghost("读取项目列表");
        _loadProjectsButton.Click += async (_, _) => await LoadApifoxProjectsAsync();
        _loadBranchesButton = Ui.Ghost("读取分支");
        _loadBranchesButton.Click += async (_, _) => await LoadApifoxBranchesAsync();

        // 从下拉列表选中一行后回填输入框与配置对象；手动输入不受影响（与冻结页面同一行为）。
        _projectId.SelectionChangeCommitted += (_, _) =>
        {
            if (_projectId.SelectedIndex is not >= 0 || _projectId.SelectedIndex >= _projectRows.Count) return;
            var row = _projectRows[_projectId.SelectedIndex];
            _projectId.Text = row.Id;
            _profile.Apifox.ProjectName = row.Name;
        };
        _branch.SelectionChangeCommitted += (_, _) =>
        {
            if (_branch.SelectedIndex is not >= 0 || _branch.SelectedIndex >= _branchRows.Count) return;
            var row = _branchRows[_branch.SelectedIndex];
            _branch.Text = ApifoxPicker.BranchValue(row);
            _profile.Apifox.BranchId = row.IsMain ? null : row.Id;
        };

        _swaggerType.Items.AddRange(SwaggerChoices);
        _swaggerType.SelectedIndexChanged += (_, _) => _swaggerLocation.Enabled = _swaggerType.SelectedIndex > 0;

        _name.Width = 420;
        _apiProject.Width = 420;
        _allowed.Width = 420;
        _swaggerType.Width = 320;
        _swaggerLocation.Width = 420;

        var saveButton = Ui.Primary("保存");
        saveButton.Click += (_, _) => Save();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        // 网格十几个字段加提示行高于可视高度：放入可滚动宿主，底部子配置入口与导出可达（DESIGN.md §6 长内容区）。
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "名称", Field(_name, ProfileStore.FieldName));
        AddRow(grid, "仓库根目录", Row(300, Field(_workspace, ProfileStore.FieldWorkspaceRoot), browseButton, _findButton));
        AddRow(grid, "API 项目", _apiProject);
        AddRow(grid, "允许回写范围", Field(_allowed, ProfileStore.FieldAllowedSourcePaths));
        AddRow(grid, "", Ui.Caption("相对路径前缀，用「;」分隔；留空表示只允许导出补丁。"));
        AddRow(grid, "版本备注", Ui.Field(_sourceVersion));
        AddRow(grid, "", Ui.Caption("提交号、构建号或版本描述；为空时报告显示「未绑定代码版本」。"));
        AddRow(grid, "Swagger 来源", _swaggerType);
        AddRow(grid, "Swagger 位置", Field(_swaggerLocation, ProfileStore.FieldSwaggerLocation));
        AddRow(grid, "Apifox 项目 ID", Row(220, _projectId, _loadProjectsButton));
        AddRow(grid, "分支", Row(220, _branch, _loadBranchesButton));
        AddRow(grid, "", Ui.Caption("与业务系统中的「项目」无关；读取列表需要先保存配置；分支留空表示主分支。"));
        AddRow(grid, "", Ui.Caption("模块范围与接口模板在「模块与模板」中编辑，序列化与分析在「序列化与分析」中编辑，模型服务在「模型服务」中配置；密钥在配置保存后设置。"));
        AddRow(grid, "", advancedButton);
        AddRow(grid, "", analysisButton);
        AddRow(grid, "", generationButton);
        AddRow(grid, "", exportButton);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，符合 Windows「主行动在右」惯例。
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        var gridScroll = Ui.ScrollHost();
        gridScroll.BackColor = UiTokens.Panel;
        gridScroll.Controls.Add(grid);
        Controls.Add(gridScroll);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        _swaggerType.SelectedIndex = 0;
        LoadValues();
    }

    private void LoadValues()
    {
        if (_editing is null) return;
        _name.Text = _profile.Name;
        _workspace.Text = _profile.WorkspaceRoot;
        _sourceVersion.Text = _profile.SourceVersionNote ?? "";
        _apiProject.Text = _profile.ApiProject;
        _allowed.Text = string.Join("; ", _profile.AllowedSourcePaths);
        _swaggerType.SelectedIndex = _profile.Swagger.Type switch
        {
            "file" => 1,
            "url" => 2,
            _ => 0,
        };
        _swaggerLocation.Text = _profile.Swagger.Location ?? "";
        _projectId.Text = _profile.Apifox.ProjectId ?? "";
        _branch.Text = _profile.Apifox.BranchName ?? "";
    }

    /// <summary>打开「模块与模板」：先把当前输入的核心字段收集到配置对象，提取才能用刚填写的 Apifox 目标。</summary>
    private void ShowAdvanced()
    {
        CollectCoreFields();
        using var dialog = new ProfileAdvancedDialog(_cli, _profile);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("模块与模板已更新，随「保存」写入。", UiTokens.OkText);
    }

    /// <summary>打开「模型服务」：生成方式与密钥的改动应用到同一配置对象，密钥单独即时生效。</summary>
    private void ShowGeneration()
    {
        CollectCoreFields();
        using var dialog = new GenerationDialog(_credentials, _profiles, _profile, _editing is not null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("模型服务设置已更新，随「保存」写入。", UiTokens.OkText);
    }

    /// <summary>打开「序列化与分析」：改动应用到同一配置对象，随外层「保存」经 ProfileStore 校验落盘。</summary>
    private void ShowAnalysis()
    {
        CollectCoreFields();
        using var dialog = new ProfileAnalysisDialog(_profile);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        SetStatus("序列化与分析设置已更新，随「保存」写入。", UiTokens.OkText);
    }

    /// <summary>导出配置（不含凭据）：导出已保存的配置，文件名与字节经 ProfileExport 与 REST 下载同一口径。</summary>
    private void ExportProfile()
    {
        try
        {
            var saved = _profiles.Get(_profile.Id);
            using var dialog = new SaveFileDialog
            {
                Title = "导出配置（不含凭据）",
                Filter = "JSON 文件|*.json",
                FileName = ProfileExport.FileName(saved),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllBytes(dialog.FileName, ProfileExport.Render(saved));
            SetStatus("配置已导出，模型服务密钥不在其中。", UiTokens.OkText);
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("导出失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    /// <summary>把核心输入字段的当前值收集到配置对象（保存与打开高级编辑共用）。</summary>
    private void CollectCoreFields()
    {
        _profile.Name = _name.Text;
        _profile.WorkspaceRoot = _workspace.Text;
        _profile.SourceVersionNote = _sourceVersion.Text.Trim().Length == 0 ? null : _sourceVersion.Text.Trim();
        _profile.ApiProject = _apiProject.Text;
        _profile.AllowedSourcePaths = _allowed.Text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _profile.Swagger = new SwaggerSource
        {
            Type = _swaggerType.SelectedIndex switch { 1 => "file", 2 => "url", _ => "none" },
            Location = _swaggerLocation.Text.Trim().Length == 0 ? null : _swaggerLocation.Text.Trim(),
        };
        _profile.Apifox.ProjectId = _projectId.Text.Trim().Length == 0 ? null : _projectId.Text.Trim();
        _profile.Apifox.BranchName = _branch.Text.Trim().Length == 0 ? null : _branch.Text.Trim();
    }

    private static Control Row(int width, params Control[] controls)
    {
        controls[0].Width = width;
        var panel = new Panel { AutoSize = true, Margin = new Padding(0) };
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        foreach (var control in controls) flow.Controls.Add(control);
        panel.Controls.Add(flow);
        return panel;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (label.Length > 0)
        {
            var caption = Ui.Body(label);
            caption.Margin = new Padding(0, UiTokens.Md, UiTokens.Sm, UiTokens.Xs);
            caption.TextAlign = ContentAlignment.MiddleLeft;
            grid.Controls.Add(caption, 0, row);
        }
        else
        {
            grid.Controls.Add(new Panel { Margin = new Padding(0) }, 0, row);
        }
        field.Margin = new Padding(0, UiTokens.Sm, 0, UiTokens.Xs);
        grid.Controls.Add(field, 1, row);
    }

    private void BrowseWorkspace()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择代码仓库根目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (_workspace.Text.Trim().Length > 0 && Directory.Exists(_workspace.Text.Trim())) dialog.InitialDirectory = _workspace.Text.Trim();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _workspace.Text = dialog.SelectedPath;
        }
    }

    private async Task FindProjectsAsync()
    {
        var root = _workspace.Text.Trim();
        _findButton.Enabled = false;
        SetStatus("正在查找…", UiTokens.Muted);
        try
        {
            // ProjectFinder 返回匿名对象；经 JSON 变形读取，避免反射或 dynamic。
            var node = await Task.Run(() => JsonSerializer.SerializeToNode(ProjectFinder.Find(root), JsonDefaults.Compact));
            if (IsDisposed) return;
            _apiProject.Items.Clear();
            var truncated = node?["truncated"]?.GetValue<bool>() ?? false;
            foreach (var project in node?["projects"] as JsonArray ?? [])
            {
                var path = project?["path"]?.GetValue<string>();
                if (path is not null) _apiProject.Items.Add(path);
            }
            if (_apiProject.Items.Count == 0)
            {
                SetStatus("没有找到 .csproj。请确认仓库根目录，或直接填写 API 项目路径。", UiTokens.WarnText);
            }
            else
            {
                if (_apiProject.Items.Count == 1) _apiProject.SelectedIndex = 0;
                SetStatus(truncated
                    ? $"找到 {_apiProject.Items.Count} 个项目后停止（目录太多）。可直接填写未列出的路径。"
                    : $"找到 {_apiProject.Items.Count} 个项目，Web 项目排在前面。", UiTokens.OkText);
            }
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus("查找失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            if (!IsDisposed) _findButton.Enabled = true;
        }
    }

    /// <summary>
    /// 读取 Apifox 项目列表（只读）：按已保存配置的 CLI 设置调用（与冻结页面路由同一语义），
    /// 行解析、列表项文字与状态行文案经 ApifoxPicker 单一口径；选择后回填项目 ID 与项目名。
    /// </summary>
    private async Task LoadApifoxProjectsAsync()
    {
        if (!ApifoxPicker.CanLoad(_editing is not null))
        {
            SetStatus(ApifoxPicker.SavedRequired, UiTokens.BadText);
            return;
        }
        _loadProjectsButton.Enabled = false;
        SetStatus("正在读取项目列表…", UiTokens.Muted);
        try
        {
            var saved = _profiles.Get(_profile.Id);
            var result = await _cli.ListProjectsAsync(saved.Apifox, CancellationToken.None);
            if (IsDisposed) return;
            if (!result.Ok)
            {
                SetStatus(ApifoxPicker.ProjectsFailedPrefix + result.Describe(), UiTokens.BadText);
                return;
            }
            _projectRows = ApifoxPicker.ProjectRows(result.Data as JsonArray);
            FillCombo(_projectId, _projectRows.Select(ApifoxPicker.ProjectText));
            SetStatus(ApifoxPicker.ProjectsLoaded(_projectRows.Count), UiTokens.OkText);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus(ApifoxPicker.ProjectsFailedPrefix + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            if (!IsDisposed) _loadProjectsButton.Enabled = true;
        }
    }

    /// <summary>
    /// 读取 Apifox 分支列表（只读）：项目 ID 取当前输入（与冻结页面 loadBranches 同一取值），
    /// CLI 设置按已保存配置；已归档分支不进列表，选主分支则分支名留空。
    /// </summary>
    private async Task LoadApifoxBranchesAsync()
    {
        var gate = ApifoxPicker.BranchGate(_editing is not null, _projectId.Text);
        if (gate is not null)
        {
            SetStatus(gate, UiTokens.BadText);
            return;
        }
        _loadBranchesButton.Enabled = false;
        SetStatus("正在读取分支…", UiTokens.Muted);
        try
        {
            var saved = _profiles.Get(_profile.Id);
            var result = await _cli.ListBranchesAsync(saved.Apifox, _projectId.Text.Trim(), CancellationToken.None);
            if (IsDisposed) return;
            if (!result.Ok)
            {
                SetStatus(ApifoxPicker.BranchesFailedPrefix + result.Describe(), UiTokens.BadText);
                return;
            }
            _branchRows = ApifoxPicker.VisibleBranches(ApifoxPicker.BranchRows(result.Data as JsonArray));
            FillCombo(_branch, _branchRows.Select(ApifoxPicker.BranchText));
            SetStatus(ApifoxPicker.BranchesLoaded(_branchRows.Count), UiTokens.OkText);
        }
        catch (UserException ex)
        {
            if (IsDisposed) return;
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            SetStatus(ApifoxPicker.BranchesFailedPrefix + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            if (!IsDisposed) _loadBranchesButton.Enabled = true;
        }
    }

    private static void FillCombo(ComboBox combo, IEnumerable<string> items)
    {
        combo.Items.Clear();
        foreach (var item in items) combo.Items.Add(item);
    }

    private void Save()
    {
        CollectCoreFields();
        try
        {
            _profiles.Save(_profile, isNew: _editing is null);
            Saved = _profile;
            DialogResult = DialogResult.OK;
        }
        catch (UserException ex)
        {
            MarkInvalidField(ex);
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("保存失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    /// <summary>包 1px 描边并登记字段标识；保存校验失败时按 UserException.Field 描红对应输入框。</summary>
    private Ui.FieldBox Field(TextBox input, string fieldId)
    {
        var field = Ui.Field(input);
        _fields[fieldId] = field;
        return field;
    }

    /// <summary>保存校验失败描红对应字段（形状通道）；文字仍在状态行（双通道，DESIGN.md §2/§6）。
    /// API 项目与 Apifox 目标是 ComboBox，不在 §6 输入框范围（设计基线 §8 挂账），失败只显示状态行文字。</summary>
    private void MarkInvalidField(UserException ex)
    {
        if (ex.Field is { } field && _fields.TryGetValue(field, out var box)) box.SetInvalid(true);
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
