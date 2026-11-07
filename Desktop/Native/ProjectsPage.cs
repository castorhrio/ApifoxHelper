using System.Drawing;
using System.Windows.Forms;
using ApifoxHelper.Infrastructure;
using ApifoxHelper.Profiles;
using ApifoxHelper.Sync;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 项目配置页：列出配置，新建/编辑核心字段（名称、仓库、API 项目、回写范围、Swagger 来源、Apifox 目标），
/// 「连接与写入探测」经既有 CompatService 检查 CLI/登录/项目读取并按需运行写入兼容探测（F02，写入门禁的唯一登记入口）。
/// 保存走 ProfileStore（含路径与取值校验）；本切片不编辑模块、模板与模型服务等高级设置，编辑时保持原值。
/// </summary>
internal sealed class ProjectsPage : UserControl, INativePage
{
    private readonly ProfileStore _profiles;
    private readonly CompatService _compat;
    private readonly FileStore _store;
    private readonly JobRunner _jobs;
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false, Dock = DockStyle.Fill, BackColor = UiTokens.Panel };
    private readonly Panel _empty;
    private readonly Label _status = Ui.StatusLine();
    private readonly Button _editButton;
    private readonly Button _probeButton;

    public ProjectsPage(ProfileStore profiles, CompatService compat, FileStore store, JobRunner jobs)
    {
        _profiles = profiles;
        _compat = compat;
        _store = store;
        _jobs = jobs;

        var newButton = Ui.Primary("新建配置");
        newButton.Click += (_, _) => ShowDialog(null);
        _editButton = Ui.Ghost("编辑");
        _editButton.Click += (_, _) => ShowDialog(Selected());
        _probeButton = Ui.Ghost("连接与写入探测");
        _probeButton.Click += (_, _) => ShowCapabilities();
        var refreshButton = Ui.Ghost("刷新");
        refreshButton.Click += (_, _) => LoadProfiles();
        _list.SelectedIndexChanged += (_, _) =>
        {
            _editButton.Enabled = _list.SelectedIndices.Count > 0;
            _probeButton.Enabled = _list.SelectedIndices.Count > 0;
        };
        _list.DoubleClick += (_, _) => ShowDialog(Selected());

        _list.Columns.Add("名称", 180);
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
        var title = Ui.Section("还没有项目配置");
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

    private void LoadProfiles()
    {
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
            _empty.Visible = profiles.Count == 0;
            _list.Visible = profiles.Count > 0;
            _editButton.Enabled = false;
            _probeButton.Enabled = false;
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
        using var dialog = new ProfileDialog(_profiles, editing);
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
        {
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
/// 配置编辑对话框：核心字段 + 内联校验（ProfileStore.Save 抛出的 UserException 显示在状态行，不弹窗）。
/// </summary>
internal sealed class ProfileDialog : Form
{
    private static readonly string[] SwaggerChoices = ["无（暂不核对 Apifox 结构）", "文件（OpenAPI 3.0 JSON）", "URL（http/https 的 JSON 地址）"];

    private readonly ProfileStore _profiles;
    private readonly ProjectProfile? _editing;

    private readonly TextBox _name = new();
    private readonly TextBox _workspace = new();
    private readonly ComboBox _apiProject = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _allowed = new();
    private readonly ComboBox _swaggerType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _swaggerLocation = new();
    private readonly TextBox _projectId = new();
    private readonly TextBox _branch = new();
    private readonly Label _status = Ui.StatusLine();
    private readonly Button _findButton;

    public ProfileDialog(ProfileStore profiles, ProjectProfile? editing)
    {
        _profiles = profiles;
        _editing = editing;

        Text = editing is null ? "新建配置" : "编辑配置：" + editing.Name;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(680, 430);
        BackColor = UiTokens.Panel;
        Font = UiTokens.Body.Value;

        _findButton = Ui.Ghost("查找 API 项目");
        _findButton.Click += async (_, _) => await FindProjectsAsync();
        var browseButton = Ui.Ghost("浏览…");
        browseButton.Click += (_, _) => BrowseWorkspace();

        _swaggerType.Items.AddRange(SwaggerChoices);
        _swaggerType.SelectedIndexChanged += (_, _) => _swaggerLocation.Enabled = _swaggerType.SelectedIndex > 0;

        _name.Width = 420;
        _apiProject.Width = 420;
        _allowed.Width = 420;
        _swaggerType.Width = 320;
        _swaggerLocation.Width = 420;
        _projectId.Width = 220;
        _branch.Width = 220;

        var saveButton = Ui.Primary("保存");
        saveButton.Click += (_, _) => Save();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(UiTokens.Xl, UiTokens.Lg, UiTokens.Xl, 0), AutoSize = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(grid, "名称", _name);
        AddRow(grid, "仓库根目录", Row(_workspace, 300, browseButton, _findButton));
        AddRow(grid, "API 项目", _apiProject);
        AddRow(grid, "允许回写范围", _allowed);
        AddRow(grid, "", Ui.Caption("相对路径前缀，用「;」分隔；留空表示只允许导出补丁。"));
        AddRow(grid, "Swagger 来源", _swaggerType);
        AddRow(grid, "Swagger 位置", _swaggerLocation);
        AddRow(grid, "Apifox 项目 ID", _projectId);
        AddRow(grid, "分支", _branch);
        AddRow(grid, "", Ui.Caption("模块范围、模板与模型服务等设置不在本切片编辑：编辑时保持原值，新建时使用默认值。"));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，符合 Windows「主行动在右」惯例。
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _status.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_status);

        Controls.Add(grid);
        Controls.Add(buttons);
        Controls.Add(statusHost);

        _swaggerType.SelectedIndex = 0;
        LoadValues();
    }

    private void LoadValues()
    {
        if (_editing is null) return;
        _name.Text = _editing.Name;
        _workspace.Text = _editing.WorkspaceRoot;
        _apiProject.Text = _editing.ApiProject;
        _allowed.Text = string.Join("; ", _editing.AllowedSourcePaths);
        _swaggerType.SelectedIndex = _editing.Swagger.Type switch
        {
            "file" => 1,
            "url" => 2,
            _ => 0,
        };
        _swaggerLocation.Text = _editing.Swagger.Location ?? "";
        _projectId.Text = _editing.Apifox.ProjectId ?? "";
        _branch.Text = _editing.Apifox.BranchName ?? "";
    }

    private static Control Row(Control first, int width, Control second, Control third)
    {
        first.Width = width;
        var panel = new Panel { AutoSize = true, Margin = new Padding(0) };
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.Add(first);
        flow.Controls.Add(second);
        flow.Controls.Add(third);
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
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("查找失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
        finally
        {
            _findButton.Enabled = true;
        }
    }

    private void Save()
    {
        var profile = _editing ?? new ProjectProfile();
        profile.Name = _name.Text;
        profile.WorkspaceRoot = _workspace.Text;
        profile.ApiProject = _apiProject.Text;
        profile.AllowedSourcePaths = _allowed.Text.Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        profile.Swagger = new SwaggerSource
        {
            Type = _swaggerType.SelectedIndex switch { 1 => "file", 2 => "url", _ => "none" },
            Location = _swaggerLocation.Text.Trim().Length == 0 ? null : _swaggerLocation.Text.Trim(),
        };
        profile.Apifox.ProjectId = _projectId.Text.Trim().Length == 0 ? null : _projectId.Text.Trim();
        profile.Apifox.BranchName = _branch.Text.Trim().Length == 0 ? null : _branch.Text.Trim();
        try
        {
            _profiles.Save(profile, isNew: _editing is null);
            DialogResult = DialogResult.OK;
        }
        catch (UserException ex)
        {
            SetStatus(ex.Message, UiTokens.BadText);
        }
        catch (Exception ex)
        {
            SetStatus("保存失败：" + Redactor.Redact(ex.Message), UiTokens.BadText);
        }
    }

    private void SetStatus(string message, Color color)
    {
        _status.Text = message;
        _status.ForeColor = color;
    }
}
