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
    // 列表筛选（ListFilter 口径，与任务页同一规则）：输入即筛选，Esc 或空态的「清空筛选」按钮清空；筛选状态留在本页。
    private readonly TextBox _filter = Ui.Input();
    private readonly Panel _empty;
    // 空态文字按「还没有配置」与「筛到零行」两态切换：筛到零行不说成没有配置，并给出清空筛选的下一步。
    private const string EmptyTitle = "还没有项目配置";
    private const string EmptyHint = "先绑定代码仓库与 Apifox 项目，然后才能创建检查任务。";
    private readonly Label _emptyTitle = Ui.Title(EmptyTitle);
    private readonly Label _emptyHint = Ui.Body(EmptyHint, muted: true);
    // 「新建配置」文字单一落点：工具栏实心主按钮与空态按钮同名同源，不出现第二处字面。
    private const string NewProfileLabel = "新建配置";
    // 空态的唯一行动按钮（DESIGN.md §7，与任务页同一规则）：没有配置时指向「新建配置」，
    // 筛到零行时指向「清空筛选」——同一时刻只显示其中一个，实心主按钮始终只有工具栏那一个。
    private readonly Button _emptyCreate = Ui.Ghost(NewProfileLabel);
    private readonly Button _emptyClear = Ui.Ghost(ListFilter.ClearActionLabel);
    // 读取失败态的唯一行动按钮（ProfileLoadState 口径）：失败时清单区不留旧行，给一句可操作文案与「重试」。
    private readonly Button _emptyRetry = Ui.Ghost(ProfileLoadState.RetryLabel);
    // 这一屏当前是不是读取失败态：三种状态（没有配置 / 筛到零行 / 读取失败）由它区分，行动按钮随之切换。
    private bool _loadFailed;
    private readonly Label _status = Ui.StatusLine();
    // 状态行宿主（第一百六十四轮）：状态行 + 右侧「查看完整错误」入口。失败原因来自
    // Exception.Message，动辄两三百字符，AutoEllipsis 只解决「有下文」，读全与复制走这颗入口。
    private readonly StatusLineHost _statusHost;
    private readonly Button _editButton;
    private readonly Button _probeButton;
    // 选中保留：列表在切页、刷新与保存后都会重建，按配置 Id 恢复选中与键盘焦点（与任务页同一口径）。
    private string? _selectedId;
    private const int ProfileNameColumnWidth = 180;
    // 列排序状态（ListSort 口径，与任务页同一规则）：未排序时按配置名称升序（ProfileStore.List 的顺序）。
    private int _sortColumn = ListSort.None;
    private bool _sortDescending;
    // 表头原标题（建列时取一次）：方向标记每次按原标题重算，不叠加、不复制字面。
    private readonly string[] _columnTitles;
    // 排序状态跨启动记忆（ListSortMemory 口径）：记本页那一列的标题与方向，认不出标题即回退未排序。
    private readonly ListSortMemory _sortMemory;

    public ProjectsPage(ProfileStore profiles, CompatService compat, ApifoxCli cli, FileStore store, JobRunner jobs, CredentialStore credentials, ListSortMemory sortMemory)
    {
        _profiles = profiles;
        _compat = compat;
        _cli = cli;
        _store = store;
        _jobs = jobs;
        _credentials = credentials;
        _sortMemory = sortMemory;
        // 状态行宿主先建：工具栏把它当一格控件加进去（标签与入口按钮的显隐由它自己管）。
        _statusHost = new StatusLineHost(_status);

        var newButton = Ui.Primary(NewProfileLabel);
        newButton.Click += (_, _) => ShowDialog(null);
        _editButton = Ui.Ghost("编辑");
        _editButton.Click += (_, _) => ShowDialog(Selected());
        _probeButton = Ui.Ghost("连接与写入探测");
        _probeButton.Click += (_, _) => ShowCapabilities();
        var refreshButton = Ui.Ghost("刷新");
        refreshButton.Click += (_, _) => RefreshData();
        // 筛选框（DESIGN.md §6「列表筛选」）：输入即筛选，Esc 清空回到全部配置，
        // Enter 把焦点与选中交给筛选结果第一行；只改呈现与焦点，不写配置数据。
        _filter.Width = 220;
        _filter.PlaceholderText = ListFilter.ProfilePlaceholder;
        _filter.TextChanged += (_, _) => LoadProfiles();
        _filter.KeyDown += (_, e) =>
        {
            // Ctrl+F 把焦点交给框、Enter 把选中交给行：筛完不必再用手点进列表。
            // Esc 不在这里接——它归壳分发到可见页（TryClearListFilter），焦点在列表或工具栏上时同样算。
            if (e.KeyCode == Keys.Enter && ListKeys.IsEnterGesture(e.Shift, e.Control, e.Alt))
            {
                Ui.FocusFirstResult(_list);
                e.SuppressKeyPress = true;
                return;
            }
        };
        _list.SelectedIndexChanged += (_, _) =>
        {
            _selectedId = Selected()?.Id;
            _editButton.Enabled = Ui.SelectedIndex(_list) is not null;
            _probeButton.Enabled = Ui.SelectedIndex(_list) is not null;
        };
        // 打开该行的编辑只经 Ui.ActivateItem 唯一入口（双击命中行、Enter 焦点行，同一条回调）；
        // 双击空白区不动作——ListView.DoubleClick 不区分命中行，曾借清空后的空选中打开「新建配置」对话框。
        Ui.ActivateItem(_list, item => ShowDialog(item.Tag as ProjectProfile), byEnter: true);
        // 标识列（首列）吸收剩余可视宽度：窗口或列表尺寸变化即重算（ListColumns 口径）。
        _list.Resize += (_, _) => FitColumns();

        _list.Columns.Add("名称", ProfileNameColumnWidth);
        _list.Columns.Add("API 项目", 260);
        _list.Columns.Add("Apifox 项目", 110);
        _list.Columns.Add("分支", 100);
        _list.Columns.Add("Swagger", 90);
        _list.Columns.Add("更新时间", 140);
        _columnTitles = _list.Columns.Cast<ColumnHeader>().Select(column => column.Text).ToArray();
        // 点击列头排序（ListSort 口径）：同一列再点反向，换一列从升序开始；排序只改呈现顺序。
        _list.ColumnClick += (_, e) => SortBy(e.Column);
        // 上次排序的列与方向跨启动生效（ListSortMemory 口径）：在任何一次加载之前落在字段上，
        // 首次显示即带着方向标记呈现；认不出标题（列增删或文案改动）时回退名称升序。
        (_sortColumn, _sortDescending) = _sortMemory.Read(ListSortMemoryKeys.Profiles, _columnTitles);

        // 空态按钮是独立实例：同一控件不能同时挂在工具栏与空态面板上；动作与工具栏同一入口，不复制逻辑。
        _emptyCreate.Click += (_, _) => ShowDialog(null);
        _emptyClear.Click += (_, _) => ClearListFilter();
        // 重试回到同一个读取入口（LoadProfiles），不复制一份读取逻辑：失败后点它等于再读一次。
        _emptyRetry.Click += (_, _) => LoadProfiles();
        var emptyActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTokens.Bg };
        emptyActions.Controls.Add(_emptyCreate);
        emptyActions.Controls.Add(_emptyClear);
        emptyActions.Controls.Add(_emptyRetry);
        _empty = BuildEmpty(emptyActions);

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, BackColor = UiTokens.Bg, ColumnCount = 2, Margin = new Padding(0) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), Padding = new Padding(0, 0, UiTokens.Md, 0) };
        buttons.Controls.Add(newButton);
        buttons.Controls.Add(_editButton);
        buttons.Controls.Add(_probeButton);
        buttons.Controls.Add(refreshButton);
        var filterField = Ui.Field(_filter);
        filterField.Margin = new Padding(UiTokens.Md, 0, 0, 0);
        buttons.Controls.Add(filterField);
        toolbar.Controls.Add(buttons, 0, 0);
        toolbar.Controls.Add(_statusHost, 1, 0);

        // 清单区形状（发丝边 + 叠在前面的空态）取 Ui.ListHost 一份：两页同一份，z 序只在那里定。
        // 此前这里把发丝边面板加在空态面板之后，空态被整块盖住（第一百一十九轮实测）。
        Controls.Add(Ui.ListHost(_list, _empty));
        Controls.Add(toolbar);
    }

    private Panel BuildEmpty(Control actions)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(_emptyTitle, 0, 1);
        layout.Controls.Add(_emptyHint, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = UiTokens.Bg };
        panel.Controls.Add(layout);
        panel.Visible = false;
        return panel;
    }

    private ProjectProfile? Selected() => Ui.SelectedItem(_list)?.Tag as ProjectProfile;

    public void OnPageShown()
    {
        // 首次显示与每次切回都重读，反映资料目录的最新状态。
        LoadProfiles();
    }

    public void OnPageHidden()
    {
    }

    public void RefreshData() => LoadProfiles();

    /// <summary>
    /// 把键盘交给配置列表（壳在「这一页出现」时调用：切页与启动，第九十六轮）。
    /// 这一页的列表在重建后恢复选中（第三十四轮），详情与「编辑」「连接与写入探测」随选中启用——
    /// 而切页后键盘落在导航按钮上，方向键动的是别的控件、Ctrl+C 也复制不到那一行。
    /// 只交键盘：不选中、不滚动、不重建清单、不写数据；判定与触达都在共享入口（<see cref="Ui.KeepKeyboardOnQueue"/>）。
    /// 重建路径（刷新、筛选、保存返回）不交——那一屏的下一步是换关键词或改配置，交键盘会把用户正在改的那一格抢走。
    /// </summary>
    public void KeepKeyboardOnQueue() => Ui.KeepKeyboardOnQueue(_list);

    /// <summary>Ctrl+F：焦点交给筛选框并选中已有关键词（重输即替换，与 Windows 搜索框一致）；只移焦点，不改筛选结果。</summary>
    public void FocusListFilter()
    {
        _filter.Focus();
        _filter.SelectAll();
    }

    /// <summary>
    /// Esc 退出筛选（壳的分发落到当前可见页）：这一屏的筛选框里留着关键词才接住这一次按键，
    /// 要不要接由 ListFilter.EscClears 判定，页内不另写一套；接住就清空并把键盘留在框里（重输即替换）。
    /// </summary>
    public bool TryClearListFilter()
    {
        if (!ListFilter.EscClears(Visible, ListFilter.IsActive(_filter.Text))) return false;
        ClearListFilter();
        return true;
    }

    /// <summary>
    /// 清空筛选的唯一路径：Esc（壳分发到本页，焦点在这屏哪个控件上都算——判定在 ListFilter.EscClears）
    /// 与空态的「清空筛选」按钮都走这里（与任务页同一口径）。
    /// 清空经 TextChanged 触发既有重建，被筛掉的配置按选中 Id 恢复，不写配置数据。
    /// </summary>
    private void ClearListFilter()
    {
        _filter.Text = "";
        _filter.Focus();
    }

    private void LoadProfiles()
    {
        // 先取局部：重建清空列表会触发 SelectedIndexChanged 把字段抹成 null。
        var selectedId = _selectedId;
        try
        {
            var all = _profiles.List();
            // 筛选先于排序（ListFilter → ListSort）：筛掉的行不参与排序，排序状态与筛选状态各自留在本页。
            var profiles = ListSort.Order(ListFilter.Apply(all, _filter.Text, FilterFields), _sortColumn, _sortDescending, SortKey);
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
            RefreshSortHeaders();
            FitColumns();
            // 重建后先禁用编辑/探测，恢复选中时由选中事件重新启用；原选中项已不存在则保持禁用。
            _editButton.Enabled = false;
            _probeButton.Enabled = false;
            RestoreSelection(selectedId);
            // 读成功即退出失败态：清单区回到「有行」或「还没有配置」两态之一。
            _loadFailed = false;
            _empty.Visible = profiles.Count == 0;
            _list.Visible = profiles.Count > 0;
            RefreshEmptyState();
            // 状态行如实给出筛选结果的数量（筛到零行时也是真实数字，不显示成没有配置）。
            SetStatus(ListFilter.IsActive(_filter.Text) ? $"筛选出 {profiles.Count} 个配置（共 {all.Count} 个）" : null, UiTokens.Muted);
        }
        catch (UserException ex)
        {
            ShowLoadFailure(ex.Message);
        }
        catch (Exception ex)
        {
            ShowLoadFailure(ProfileLoadState.FailureStatus(Redactor.Redact(ex.Message)));
        }
    }

    /// <summary>
    /// 读取失败态（<see cref="ProfileLoadState"/> 口径）：清单区不留上一次的行——这一屏刚刚证明
    /// 「磁盘上的配置读不出来」，继续显示旧行等于把不可信的内容当成现状；改为给一句可操作文案与
    /// 唯一行动按钮「重试」（动作回到 <see cref="LoadProfiles"/> 同一个入口）。
    /// 状态行仍如实给出脱敏后的原因：清单区讲下一步、状态行讲原因，两条通道各说一件事。
    /// </summary>
    private void ShowLoadFailure(string status)
    {
        _loadFailed = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        _list.EndUpdate();
        // 清单空了，编辑与探测没有可作用的对象：与重建后「先禁用、恢复选中再启用」同一口径。
        _editButton.Enabled = false;
        _probeButton.Enabled = false;
        _empty.Visible = true;
        _list.Visible = false;
        RefreshEmptyState();
        SetStatus(status);
    }

    /// <summary>
    /// 空态两态（DESIGN.md §7「空态不留白」「空态=说明现在有什么可做 + 唯一行动按钮」，与任务页同一口径）：
    /// 没有配置时给首次使用的下一步与「新建配置」，筛到零行时说明是筛选结果并给「清空筛选」——
    /// 不把筛选结果说成没有配置，也不给筛到零行的用户一个「新建」当作下一步。
    /// </summary>
    private void RefreshEmptyState()
    {
        var filtered = ListFilter.IsActive(_filter.Text);
        // 三态：读取失败 > 筛到零行 > 还没有配置。失败态优先——这一屏连「有没有配置」都还不知道，
        // 说成「筛到零行」或「还没有配置」都是把读不出来说成读到了空。
        _emptyTitle.Text = _loadFailed ? ProfileLoadState.FailureTitle : filtered ? ListFilter.ProfileNoMatchTitle : EmptyTitle;
        _emptyHint.Text = _loadFailed ? ProfileLoadState.FailureHint : filtered ? ListFilter.ProfileNoMatchHint : EmptyHint;
        _emptyCreate.Visible = !_loadFailed && !filtered;
        _emptyClear.Visible = !_loadFailed && filtered;
        _emptyRetry.Visible = ProfileLoadState.RetryVisible(_loadFailed);
    }

    /// <summary>
    /// 筛选字段（与单元格同源）：名称、API 项目、Apifox 项目、分支、Swagger 来源——
    /// 单元格显示什么就能按什么找，占位值（未绑定/主分支）与单元格同一取值。
    /// </summary>
    private static string?[] FilterFields(ProjectProfile profile) =>
    [
        profile.Name,
        profile.ApiProject,
        profile.Apifox.ProjectId ?? "未绑定",
        profile.Apifox.BranchName ?? "主分支",
        SwaggerText(profile.Swagger.Type),
    ];

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

    /// <summary>
    /// 点击列头排序（DESIGN.md §6「列表列排序」）：同一列再点反向，换一列从升序开始；
    /// 排序状态留在本页并按列标题记下（<see cref="_sortMemory"/>），刷新、切页回来与保存后的
    /// 重建都按同一列同一方向呈现，下次启动回来仍是这一列。
    /// 只改呈现顺序：配置数据一行未动，选中按配置 Id 恢复（<see cref="RestoreSelection"/>）。
    /// </summary>
    private void SortBy(int column)
    {
        (_sortColumn, _sortDescending) = ListSort.Toggle(_sortColumn, _sortDescending, column);
        _sortMemory.Save(ListSortMemoryKeys.Profiles, _columnTitles, _sortColumn, _sortDescending);
        LoadProfiles();
    }

    /// <summary>表头方向标记：排序列追加 ▲/▼，其余列回到原标题（标题取自建列时的快照）。</summary>
    private void RefreshSortHeaders()
    {
        for (var i = 0; i < _list.Columns.Count && i < _columnTitles.Length; i++)
        {
            _list.Columns[i].Text = ListSort.Header(_columnTitles[i], i, _sortColumn, _sortDescending);
        }
    }

    /// <summary>
    /// 列排序键（与建列顺序一一对应，取值与单元格文字同源）：名称、API 项目、Apifox 项目、分支与
    /// Swagger 按文字（占位值「未绑定/主分支/未配置」同按文字参与，同值相邻）；更新时间按完整时间戳，
    /// 不只看单元格显示到分钟的文字。文字列按当前区域比较，与资源管理器同序。
    /// </summary>
    private static IComparable? SortKey(ProjectProfile profile, int column) => column switch
    {
        0 => profile.Name,
        1 => profile.ApiProject,
        2 => profile.Apifox.ProjectId ?? "未绑定",
        3 => profile.Apifox.BranchName ?? "主分支",
        4 => SwaggerText(profile.Swagger.Type),
        5 => profile.UpdatedAt,
        _ => null,
    };

    /// <summary>重建后恢复选中与键盘焦点（经 Ui.SelectItem 唯一入口，该行一并滚入可见区）；原选中项已不存在（如被删除）时不动作。</summary>
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

    private void SetStatus(string? message, Color? color = null) =>
        _statusHost.SetText(message, color ?? UiTokens.BadText);

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
/// <para>
/// 表单形状按 <see cref="ProfileFormLayout"/> 分三段：<b>必填</b>（名称、仓库根目录、API 项目，连续成组，
/// 填完就能保存）→ <b>可选设置</b>（回写范围、版本备注、Swagger、Apifox 目标与分支，留空即取缺省）→
/// <b>高级设置</b>（模块与模板、序列化与分析、模型服务、导出配置，四个入口收在同一行）。
/// 保存/取消固定在底栏，滚动时也点得到；「读取项目列表」在未保存时停用并就地说明下一步，
/// 不再让用户点了才知道「要先保存」。
/// </para>
/// <para>
/// 「模块与模板」入口打开高级编辑（模块范围与新接口模板），「模型服务」入口打开生成方式与密钥编辑，
/// 改动应用到同一配置对象，随「保存」一起落盘。
/// </para>
/// </summary>
internal sealed class ProfileDialog : DpiAwareForm
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
    // 状态行宿主：与外层页面同一接线口径——放不下时右侧给「查看完整错误」入口。
    private readonly StatusLineHost _statusHost;

    /// <summary>保存成功后的配置（保存失败保持 null；调用方据此在列表中定位刚保存的行）。</summary>
    public ProjectProfile? Saved { get; private set; }
    private readonly Button _findButton;
    private readonly Button _loadProjectsButton;
    private readonly Button _loadBranchesButton;
    // 两颗读取按钮的文字单一落点：失败态的重试指引指名它们（ProfileLoadState.RetryHint），
    // 按钮文字与指引里的字面同源，改一处不会让指引指向一颗不存在的按钮。
    private const string LoadProjectsLabel = "读取项目列表";
    private const string LoadBranchesLabel = "读取分支";
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
        DpiScale.Apply(this, new Size(680, 720));
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
        _loadProjectsButton = Ui.Ghost(LoadProjectsLabel);
        // 未保存时停用并就地说明下一步（ProfileFormLayout.ProjectListHint）：此前按钮是启用的，
        // 点下去只得到一句「请先保存配置」，用户得自己猜「那我现在能不能保存」。
        _loadProjectsButton.Enabled = ProfileFormLayout.ProjectListEnabled(_editing is not null);
        _loadProjectsButton.Click += async (_, _) => await LoadApifoxProjectsAsync();
        _loadBranchesButton = Ui.Ghost(LoadBranchesLabel);
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

        var saveButton = Ui.Primary("保存");
        saveButton.Click += (_, _) => Save();
        var cancelButton = Ui.Ghost("取消");
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;
        AcceptButton = saveButton;
        CancelButton = cancelButton;

        // 表单形状（第一百四十九轮）：必填三项连续成组、可选设置在后、四个高级入口收在同一行。
        // 行高上限、入口间距与「读取项目列表」两态文案的口径都在 ProfileFormLayout 一处，
        // 冒烟器逐行量真实控件（RunProfileFormLayout）——此前空标签格里放的是默认尺寸的 Panel（200×100），
        // 七行「没有标签」的行把表单撑出 700px 死空白，四个高级入口各占一行、彼此隔着 100px。
        // 两列字段网格经工厂出厂（DESIGN.md §6「两列字段网格」）：列宽随网格当前宽度重算，
        // 不再用百分比列（第一百二十三轮修掉的真机缺陷：右列停在 32px，四个输入框填不了字）。
        var grid = Ui.FieldGrid(ProfileGridColumns.LabelWidth);
        AddSection(grid, ProfileFormLayout.CoreSectionTitle);
        AddRow(grid, "名称", Field(_name, ProfileStore.FieldName));
        AddRow(grid, "仓库根目录", Row(300, Field(_workspace, ProfileStore.FieldWorkspaceRoot), browseButton, _findButton));
        AddRow(grid, "API 项目", _apiProject);
        AddSection(grid, ProfileFormLayout.OptionalSectionTitle);
        AddRow(grid, "允许回写范围", Field(_allowed, ProfileStore.FieldAllowedSourcePaths));
        AddRow(grid, "", Ui.Caption("相对路径前缀，用「;」分隔；留空表示只允许导出补丁。"));
        AddRow(grid, "版本备注", Ui.Field(_sourceVersion));
        AddRow(grid, "", Ui.Caption("提交号、构建号或版本描述；为空时报告显示「未绑定代码版本」。"));
        AddRow(grid, "Swagger 来源", _swaggerType);
        AddRow(grid, "Swagger 位置", Field(_swaggerLocation, ProfileStore.FieldSwaggerLocation));
        AddRow(grid, "Apifox 项目 ID", Row(220, _projectId, _loadProjectsButton));
        AddRow(grid, "", Ui.Caption(ProfileFormLayout.ProjectListHint(_editing is not null)));
        AddRow(grid, "分支", Row(220, _branch, _loadBranchesButton));
        AddRow(grid, "", Ui.Caption("分支留空表示主分支。"));
        AddSection(grid, ProfileFormLayout.AdvancedSectionTitle);
        AddWide(grid, AdvancedEntries(advancedButton, analysisButton, generationButton, exportButton));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(UiTokens.Xl, UiTokens.Sm, UiTokens.Xl, UiTokens.Lg), BackColor = UiTokens.Panel };
        // RightToLeft 流向：先加的在最右，符合 Windows「主行动在右」惯例。
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var statusHost = new Panel { Dock = DockStyle.Bottom, Height = _status.Height + UiTokens.Xs, BackColor = UiTokens.Panel, Padding = new Padding(UiTokens.Xl, 0, UiTokens.Xl, 0) };
        _statusHost = new StatusLineHost(_status);
        _statusHost.Dock = DockStyle.Fill;
        statusHost.Controls.Add(_statusHost);

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

    /// <summary>
    /// 一行里的「字段 + 若干按钮」（仓库根目录的浏览/查找、Apifox 项目 ID 与分支的读取按钮）：
    /// 直接返回流式容器，不再套一层 <see cref="Panel"/>——套的那一层是 <c>AutoSize = true</c> 的
    /// 默认尺寸面板，行高被它撑到 100px（真机截图里「仓库根目录」那一行 112px 的来源）。
    /// 行内控件自带上下留白（<see cref="UiTokens.Xs"/>），容器本身不留白，行高由最高的控件决定。
    /// </summary>
    private static Control Row(int width, params Control[] controls)
    {
        controls[0].Width = width;
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTokens.Panel };
        for (var i = 0; i < controls.Length; i++)
        {
            controls[i].Margin = i == 0
                ? new Padding(0, UiTokens.Xs, 0, UiTokens.Xs)
                : new Padding(UiTokens.Sm, 0, 0, 0);
            flow.Controls.Add(controls[i]);
        }
        return flow;
    }

    /// <summary>
    /// 四个高级入口收在同一行（<see cref="ProfileFormLayout.AdvancedEntries"/>）：此前各占一整行、
    /// 彼此隔着 100px 的空白，读起来像四颗孤立按钮，把首用视线从「填完就能保存」上拽走。
    /// 间距取 <see cref="ProfileFormLayout.AdvancedEntryGap"/>，冒烟器量相邻两个按钮的实际间隙。
    /// </summary>
    private static Control AdvancedEntries(params Button[] entries)
    {
        var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0), BackColor = UiTokens.Panel };
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i].Margin = new Padding(0, 0, i == entries.Length - 1 ? 0 : ProfileFormLayout.AdvancedEntryGap, 0);
            flow.Controls.Add(entries[i]);
        }
        return flow;
    }

    /// <summary>分区标题行（必填 / 可选设置 / 高级设置）：跨两列，行高只由标题自身决定。</summary>
    private static void AddSection(TableLayoutPanel grid, string title)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = Ui.Section(title);
        caption.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);
        caption.Anchor = AnchorStyles.Left;
        grid.Controls.Add(caption, 0, row);
        grid.SetColumnSpan(caption, 2);
    }

    /// <summary>跨两列的一行（高级入口那一行）：四个入口并排后比字段列宽，跨列才排得下、不出横向滚动条。</summary>
    private static void AddWide(TableLayoutPanel grid, Control control)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(0);
        grid.Controls.Add(control, 0, row);
        grid.SetColumnSpan(control, 2);
    }

    /// <summary>
    /// 一行「标签 + 字段」。没有标签的行（说明行）<b>不再放占位控件</b>：此前放的是
    /// <c>new Panel { Margin = 0 }</c>，而 <see cref="Panel"/> 的默认尺寸是 200×100，
    /// 于是每一行「没有标签」的行都被撑到 100px 高——七行就是 700px 的死空白。
    /// 行高上限见 <see cref="ProfileFormLayout.MaxRowHeight"/>。
    /// </summary>
    private static void AddRow(TableLayoutPanel grid, string label, Control field)
    {
        var row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (label.Length > 0)
        {
            var caption = Ui.Body(label);
            caption.Margin = new Padding(0, UiTokens.Xs, UiTokens.Sm, UiTokens.Xs);
            caption.TextAlign = ContentAlignment.MiddleLeft;
            caption.Anchor = AnchorStyles.Left;
            grid.Controls.Add(caption, 0, row);
        }
        // 行容器（<see cref="Row"/> / <see cref="AdvancedEntries"/>）自己排布行内留白，外层不再加：
        // 容器里最高的控件是按钮（37px），再套 4+4 的留白就把行撑到 45px，超过
        // <see cref="ProfileFormLayout.MaxRowHeight"/>——行高由最高的那个控件决定，不叠两层留白。
        if (field is not FlowLayoutPanel) field.Margin = new Padding(0, UiTokens.Xs, 0, UiTokens.Xs);
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
                // 失败态补一句可操作文案：这颗按钮在 finally 里已回到可用，点它就是重试（不新增第二颗按钮）。
                SetStatus(ProfileLoadState.PickerFailureStatus(ApifoxPicker.ProjectsFailedPrefix, result.Describe(), LoadProjectsLabel), UiTokens.BadText);
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
            SetStatus(ProfileLoadState.PickerFailureStatus(ApifoxPicker.ProjectsFailedPrefix, Redactor.Redact(ex.Message), LoadProjectsLabel), UiTokens.BadText);
        }
        finally
        {
            // 读完之后回到门控值，而不是无条件启用：新建配置时这一颗按钮始终停用。
            if (!IsDisposed) _loadProjectsButton.Enabled = ProfileFormLayout.ProjectListEnabled(_editing is not null);
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
                SetStatus(ProfileLoadState.PickerFailureStatus(ApifoxPicker.BranchesFailedPrefix, result.Describe(), LoadBranchesLabel), UiTokens.BadText);
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
            SetStatus(ProfileLoadState.PickerFailureStatus(ApifoxPicker.BranchesFailedPrefix, Redactor.Redact(ex.Message), LoadBranchesLabel), UiTokens.BadText);
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

    private void SetStatus(string message, Color color) => _statusHost.SetText(message, color);
}
