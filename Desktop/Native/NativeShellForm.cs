using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ApifoxHelper.Api;
using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Reports;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 原生桌面壳（F16）：原生菜单 + 左侧导航 + 页面区。
/// 复用进程内服务（HelperHost），不嵌网页；"在浏览器中打开"不在本壳的菜单中。
/// 主工作流（配置 → 任务 → 审阅 → 检查 → 计划 → 同步 → 报告）全程在原生窗口完成；本壳是默认入口，过渡壳只按 --web 显式回退（方案 2.4）。
/// </summary>
internal sealed class NativeShellForm : Form
{
    private const string Title = "Apifox 助手";
    private static readonly TimeSpan JobGrace = TimeSpan.FromSeconds(15);
    private const int NavWidth = 200;

    private readonly HelperHost _host;
    private readonly WindowPlacement _placement;
    private readonly LastPage _lastPage;
    private readonly ListSortMemory _sortMemory;
    private readonly Panel _navPanel = new() { Dock = DockStyle.Left, Width = NavWidth, BackColor = UiTokens.Nav, Padding = new Padding(UiTokens.Sm) };
    private readonly Panel _marker = new() { Width = 3, BackColor = UiTokens.Accent, Visible = false };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, BackColor = UiTokens.Bg, Padding = new Padding(UiTokens.Lg) };
    private readonly Label _contentStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = UiTokens.Muted,
        Font = UiTokens.Body.Value,
        Text = "正在启动…",
    };

    private readonly Button _navProjects;
    private readonly Button _navTasks;
    private ProjectsPage? _projectsPage;
    private TasksPage? _tasksPage;
    private bool _shutdownStarted;
    private bool _shutdownDone;

    public NativeShellForm(HelperHost host)
    {
        _host = host;
        Text = Title;
        Icon = LoadIcon();
        MinimumSize = new Size(1024, 640);
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTokens.Bg;
        Font = UiTokens.Body.Value;

        _navProjects = NavButton(PageNames.Projects);
        _navProjects.Click += (_, _) => ShowProjects();
        _navTasks = NavButton(PageNames.Tasks);
        _navTasks.Click += (_, _) => ShowTasks();

        _navPanel.Controls.Add(_marker);
        var version = Ui.Caption("v" + TaskStore.ToolVersion);
        version.Dock = DockStyle.Bottom;
        version.Padding = new Padding(UiTokens.Md, UiTokens.Sm, 0, 0);
        _navPanel.Controls.Add(version);
        var navButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, UiTokens.Xl, 0, 0),
        };
        navButtons.Controls.Add(_navProjects);
        navButtons.Controls.Add(_navTasks);
        _navPanel.Controls.Add(navButtons);
        navButtons.BringToFront();

        _content.Controls.Add(_contentStatus);

        var menu = BuildMenu();
        // 停靠按集合倒序生效：菜单最后加入、最先停靠。
        Controls.Add(_content);
        Controls.Add(_navPanel);
        Controls.Add(menu);
        MainMenuStrip = menu;

        // 长作业完成提醒：用户在其他窗口时闪烁任务栏唤回（口径见 AttentionCue）。
        _host.Jobs.JobCompleted += OnJobCompleted;

        _placement = new WindowPlacement(Path.Combine(host.Store.Root, "desktop-window-native.json"));
        _placement.Restore(this);
        _lastPage = new LastPage(Path.Combine(host.Store.Root, "desktop-page-native.json"));
        // 两页列表的排序状态跨启动记忆：与页面键、窗口位置同目录同族，只记「按哪一列、朝哪个方向」。
        _sortMemory = new ListSortMemory(Path.Combine(host.Store.Root, "desktop-list-sort-native.json"));
    }

    private Button NavButton(string text)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(NavWidth - 2 * UiTokens.Sm, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = UiTokens.Nav,
            ForeColor = UiTokens.NavText,
            Font = UiTokens.Body.Value,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(UiTokens.Md, UiTokens.Sm, UiTokens.Md, UiTokens.Sm),
            Margin = new Padding(0, 0, 0, UiTokens.Xs),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Enabled = false,
        };
        button.FlatAppearance.BorderSize = 0;
        // hover 只改底色（选中态同色），无位移/缩放。
        button.FlatAppearance.MouseOverBackColor = UiTokens.NavSelected;
        button.FlatAppearance.MouseDownBackColor = UiTokens.NavSelected;
        return button;
    }

    /// <summary>导航选中态：选中项 NavSelected 底 + 面板色文字，其余 Nav 底。</summary>
    private void SelectNav(Button selected)
    {
        foreach (var nav in new[] { _navProjects, _navTasks })
        {
            var isSelected = ReferenceEquals(nav, selected);
            nav.BackColor = isSelected ? UiTokens.NavSelected : UiTokens.Nav;
            nav.ForeColor = isSelected ? UiTokens.Panel : UiTokens.NavText;
        }
    }

    private MenuStrip BuildMenu()
    {
        static ToolStripMenuItem Item(string text, Action onClick) => new(text, null, (_, _) => onClick());
        // 手势与动作的配对取自共享口径（ShellShortcuts），界面代码不就地写快捷键。
        static ToolStripMenuItem BoundItem(string text, ShellShortcuts.Entry entry, Action onClick)
        {
            var item = Item(text, onClick);
            item.ShortcutKeys = (Keys)new KeysConverter().ConvertFromString(entry.Gesture)!;
            return item;
        }

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("打开资料目录(&D)", () => OpenFolder(_host.Store.Root)),
            Item("打开日志目录(&L)", () => OpenFolder(_host.LogDirectory)),
            new ToolStripSeparator(),
            Item("退出(&X)", Close),
        });
        var view = new ToolStripMenuItem("视图(&V)");
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            BoundItem(PageNames.Projects + "(&P)", ShellShortcuts.Projects, ShowProjects),
            BoundItem(PageNames.Tasks + "(&T)", ShellShortcuts.Tasks, ShowTasks),
            new ToolStripSeparator(),
            BoundItem("刷新当前页(&R)", ShellShortcuts.Refresh, RefreshVisiblePage),
            BoundItem("筛选当前列表(&F)", ShellShortcuts.Find, FocusListFilterOnVisiblePage),
        });
        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(BoundItem("关于 Apifox 助手(&A)", ShellShortcuts.About, ShowAbout));

        var menu = new MenuStrip { Dock = DockStyle.Top, Font = UiTokens.Body.Value };
        menu.Items.AddRange(new ToolStripItem[] { file, view, help });
        return menu;
    }

    private void ShowProjects() => ShowPage(_projectsPage, _navProjects);

    private void ShowTasks() => ShowPage(_tasksPage, _navTasks);

    /// <summary>刷新当前可见页的数据（与该页「刷新」按钮同一口径）；页面尚未创建时不动作。</summary>
    private void RefreshVisiblePage()
    {
        if (_shutdownStarted || IsDisposed || Disposing) return;
        if (_projectsPage is { Visible: true }) _projectsPage.RefreshData();
        if (_tasksPage is { Visible: true }) _tasksPage.RefreshData();
    }

    /// <summary>Ctrl+F：把焦点交给当前可见页列表的筛选框；页面尚未创建时不动作（与「刷新当前页」同一分发纪律）。</summary>
    private void FocusListFilterOnVisiblePage()
    {
        if (_shutdownStarted || IsDisposed || Disposing) return;
        if (_projectsPage is { Visible: true }) _projectsPage.FocusListFilter();
        if (_tasksPage is { Visible: true }) _tasksPage.FocusListFilter();
    }

    /// <summary>
    /// Esc 退出筛选（DESIGN.md §6「列表筛选」那条全键盘路径的退出一步）：Ctrl+F 聚焦框 → 输入 → Enter 到结果 →
    /// 方向键逐行看，到这一步想回到全部，人不该再拿鼠标去点框或点「清空筛选」——此前 Esc 只挂在筛选框
    /// 自己的按键处理上，Enter 把选中交给列表之后焦点已在别处，按它没有反应。
    /// 这里只在「Esc 且当前可见页的筛选框里真留着关键词」时接住这一次按键（两个条件都在
    /// <see cref="ListFilter.EscClears"/> 判定，两页与壳都不另写一套），清空仍走各页既有唯一路径；
    /// 没有关键词时不接、不吞键，其余按键一律交回基类——菜单快捷键与对话框的 Esc=取消都不经过这里
    /// （模态对话框是另一个窗体，按键只在自己的控件链上冒泡）。
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && ClearFilterOnVisiblePage()) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>问两页要不要接住这一次 Esc：要不要接由页自己答（可见性在它身上、关键词在它框里），壳只分发。</summary>
    private bool ClearFilterOnVisiblePage()
    {
        if (_shutdownStarted || IsDisposed || Disposing) return false;
        if (_projectsPage is not null && _projectsPage.TryClearListFilter()) return true;
        if (_tasksPage is not null && _tasksPage.TryClearListFilter()) return true;
        return false;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            await _host.StartAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex, "本地服务启动失败");
            if (IsDisposed || Disposing) return;
            _contentStatus.Text = "本地服务启动失败：" + Redactor.Redact(ex.Message) + "\n详细信息见「文件 → 打开日志目录」。";
            _contentStatus.ForeColor = UiTokens.BadText;
            return;
        }
        if (IsDisposed || Disposing) return;

        _projectsPage = new ProjectsPage(
            _host.Services.GetRequiredService<ProfileStore>(),
            _host.Services.GetRequiredService<Sync.CompatService>(),
            _host.Services.GetRequiredService<Apifox.ApifoxCli>(),
            _host.Store,
            _host.Jobs,
            _host.Services.GetRequiredService<CredentialStore>(),
            _sortMemory);
        _tasksPage = new TasksPage(
            _host.Services.GetRequiredService<TaskStore>(),
            _host.Services.GetRequiredService<SourceScanService>(),
            _host.Jobs,
            _host.Services.GetRequiredService<ProfileStore>(),
            _host.Services.GetRequiredService<DraftService>(),
            _host.Services.GetRequiredService<CommentPlanService>(),
            _host.Services.GetRequiredService<PropagationService>(),
            _host.Services.GetRequiredService<Comments.BuildService>(),
            _host.Services.GetRequiredService<ContractReviewService>(),
            _host.Services.GetRequiredService<Sync.CheckService>(),
            _host.Services.GetRequiredService<Sync.DecisionService>(),
            _host.Services.GetRequiredService<Sync.SupplementService>(),
            _host.Services.GetRequiredService<Reports.ReportExportService>(),
            _host.Services.GetRequiredService<Sync.SyncPlanService>(),
            _host.Services.GetRequiredService<Sync.SyncExecutor>(),
            _sortMemory);

        // 任务详情「打开配置」这一跳的接线（第二百六十三轮）：两页互不引用，由壳把请求接到另一页的落点上。
        // 先切页（切页会重读配置清单），再按 Id 选中那一行；找不到时那一页自己把原因写在状态行上。
        _tasksPage.OpenProfileRequested += (profileId, profileName) =>
        {
            ShowProjects();
            _projectsPage.RevealProfile(profileId, profileName);
        };

        _content.Controls.Clear();
        _projectsPage.Dock = DockStyle.Fill;
        _tasksPage.Dock = DockStyle.Fill;
        _content.Controls.Add(_tasksPage);
        _content.Controls.Add(_projectsPage);
        _tasksPage.Visible = false;

        _navProjects.Enabled = true;
        _navTasks.Enabled = true;
        // 回到上次使用的页面；无记录或值不认识时落在项目设置页（首次启动的缺省）。
        if (_lastPage.Read() == LastPageKeys.Tasks) ShowPage(_tasksPage, _navTasks);
        else ShowPage(_projectsPage, _navProjects);
    }

    private void ShowPage(Control? page, Button nav)
    {
        if (page is null || _shutdownStarted || IsDisposed || Disposing) return;
        foreach (Control control in _content.Controls)
        {
            if (control is INativePage other && !ReferenceEquals(control, page)) other.OnPageHidden();
            control.Visible = ReferenceEquals(control, page);
        }
        SelectNav(nav);
        PositionMarker(nav);
        if (page is INativePage shown)
        {
            shown.OnPageShown();
            // 这一页出现时把键盘交给它的列表（第九十六轮）：切页后键盘此前落在导航按钮上
            // （点导航按钮时焦点就在那个按钮上），而这一页的列表已经有一行被标出来、详情区已经显示那一行——
            // 方向键动的是别的控件、复制手势也落不到那一行。只交键盘，判定与触达都在页与 Ui 的共享入口。
            shown.KeepKeyboardOnQueue();
        }
        // 记住本次使用的页面，下次启动回到这里；保存失败不影响切换。
        _lastPage.Save(ReferenceEquals(nav, _navTasks) ? LastPageKeys.Tasks : LastPageKeys.Projects);
    }

    /// <summary>
    /// 启动时那一屏的键盘归属（第九十六轮）：<see cref="ShowPage"/> 在 OnLoad 里跑，而 OnLoad 是 async void——
    /// 本地服务启动若真的异步完成，页面是在窗体显示之后才建起来的（ShowPage 那一处即生效）；
    /// 若同步完成，页面在窗体显示之前就建好，那时控件还不可见、Focus 不生效（与第八十八轮「接在 OnShown
    /// 而不是 OnLoad」同一取舍），故在窗体首次显示之后再交一次。两处都经同一处入口与同一份判定，重复调用幂等。
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        KeepKeyboardOnVisiblePage();
    }

    /// <summary>把键盘交给当前可见页的列表（与「刷新当前页」「Ctrl+F」同一分发纪律：页面未创建不动作）。</summary>
    private void KeepKeyboardOnVisiblePage()
    {
        if (_shutdownStarted || IsDisposed || Disposing) return;
        if (_projectsPage is { Visible: true }) _projectsPage.KeepKeyboardOnQueue();
        if (_tasksPage is { Visible: true }) _tasksPage.KeepKeyboardOnQueue();
    }

    /// <summary>导航选中项左侧的 3px 强调色标记。</summary>
    private void PositionMarker(Button nav)
    {
        var top = _navPanel.PointToClient(nav.PointToScreen(Point.Empty)).Y;
        _marker.Location = new Point(0, Math.Max(0, top));
        _marker.Height = nav.Height;
        _marker.Visible = true;
        _marker.BringToFront();
    }

    public void ActivateFromOtherInstance()
    {
        try
        {
            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() =>
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            });
        }
        catch (InvalidOperationException)
        {
            // 窗口正在关闭。
        }
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        StopFlash();
    }

    private void OnJobCompleted(JobInfo job)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            BeginInvoke(() =>
            {
                if (_shutdownStarted || IsDisposed || Disposing) return;
                if (!AttentionCue.ShouldFlash(AppHoldsFocus(), job.State, _shutdownStarted)) return;
                FlashForAttention();
            });
        }
        catch (InvalidOperationException)
        {
            // 窗口正在关闭。
        }
    }

    /// <summary>应用内任一打开的窗口（含对话框）持有焦点即视为用户在场。</summary>
    private static bool AppHoldsFocus() => Application.OpenForms.OfType<Form>().Any(f => f.ContainsFocus);

    private void FlashForAttention()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var info = new FLASHWINFO
        {
            Size = (uint)Marshal.SizeOf<FLASHWINFO>(),
            Handle = Handle,
            Flags = FlashwAll | FlashwTimerNoFg,
        };
        FlashWindowEx(ref info);
    }

    private void StopFlash()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var info = new FLASHWINFO
        {
            Size = (uint)Marshal.SizeOf<FLASHWINFO>(),
            Handle = Handle,
            Flags = FlashwStop,
        };
        FlashWindowEx(ref info);
    }

    private const uint FlashwStop = 0;
    private const uint FlashwAll = 3;
    // 持续闪烁直到窗口来到前台，由系统自行停止。
    private const uint FlashwTimerNoFg = 12;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint Size;
        public IntPtr Handle;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_shutdownDone) return;
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            // 系统关机时不能阻止退出；未结束的同步会在下次启动时标为「已中断」，等待重新核对。
            _placement.Save(this);
            return;
        }
        e.Cancel = true;
        if (_shutdownStarted) return;

        var grace = JobGrace;
        var running = _host.Jobs.RunningJobs();
        if (e.CloseReason != CloseReason.UserClosing)
        {
            grace = TimeSpan.FromSeconds(5);
        }
        else if (running.Count > 0)
        {
            var kinds = string.Join("、", running.Select(j => j.Kind).Distinct());
            var answer = MessageBox.Show(this,
                ConfirmEntries.ExitConfirmText(kinds, (int)JobGrace.TotalSeconds),
                Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }

        _shutdownStarted = true;
        _placement.Save(this);
        Text = Title + " — 正在安全退出…";
        UseWaitCursor = true;
        Enabled = false;
        try
        {
            await _host.StopAsync(grace);
            await _host.DisposeAsync();
        }
        catch (Exception ex)
        {
            _host.Logger.LogError(ex, "退出时停止本地服务失败");
        }
        _shutdownDone = true;
        _host.Jobs.JobCompleted -= OnJobCompleted;
        Close();
    }

    /// <summary>
    /// 打开目录失败：经令牌化提示对话框说明原因与路径（DESIGN.md §1.1「禁止以框架默认观感直接交付」）。
    /// 此前用系统 MessageBox（只有一个「确定」），是全原生界面仅有的两处框架默认观感之一；
    /// 确认类动作仍用原生确认对话框（§6 例外条款），不经过这里。文字取 NoticeText 一份。
    /// </summary>
    private void OpenFolder(string path)
    {
        if (Browser.TryOpen(path, out var error)) return;
        using var dialog = new NoticeDialog(
            NoticeText.FolderErrorHeading,
            error ?? NoticeText.FolderErrorHeading,
            mono: path,
            note: NoticeText.FolderErrorNote,
            copyText: path);
        dialog.ShowDialog(this);
    }

    /// <summary>
    /// 关于：版本、资料目录与运行时，经令牌化提示对话框呈现（与其余信息视图同一套令牌与形状）。
    /// 不再显示本地服务地址——本地 HTTP 服务是过渡壳的实现能力（方案 §2.4），不是用户主产品，
    /// 把地址摆在「关于」里等于把用户往浏览器引（方案 1.4「不把用户推回浏览器」）。
    /// </summary>
    private void ShowAbout()
    {
        using var dialog = new NoticeDialog(
            NoticeText.AboutHeading,
            NoticeText.AboutBody(TaskStore.ToolVersion, RuntimeInformation.FrameworkDescription),
            mono: NoticeText.AboutDataDirLine(_host.Store.Root),
            note: NoticeText.AboutNote,
            copyText: _host.Store.Root);
        dialog.ShowDialog(this);
    }

    private static Icon? LoadIcon()
    {
        using var stream = typeof(NativeShellForm).Assembly.GetManifestResourceStream("Assets.app.ico");
        return stream is null ? null : new Icon(stream);
    }
}

/// <summary>原生壳页面协议：显示/隐藏时刷新数据与轮询。</summary>
internal interface INativePage
{
    /// <summary>页面变为可见时调用：重新加载数据。</summary>
    void OnPageShown();

    /// <summary>页面被隐藏时调用：停止轮询等后台展示工作。</summary>
    void OnPageHidden();

    /// <summary>重读本页数据（壳的「刷新当前页」F5 与本页「刷新」按钮共用同一口径）。</summary>
    void RefreshData();

    /// <summary>把键盘焦点交给本页列表的筛选框并选中已有关键词（壳的 Ctrl+F 分发到当前可见页）。</summary>
    void FocusListFilter();

    /// <summary>
    /// 把键盘交给本页列表（壳在「这一页出现」时调用：切页与启动，第九十六轮）。
    /// 判定仍是 <see cref="ListFocus"/> 那一份（选中行与焦点行是同一行才交），
    /// 触达界面只在 Ui 那一处入口；本页不自己写 <c>.Focus()</c>、
    /// 不自己判「选中行与焦点行是不是同一行」、不顺手选中或滚动（那些各有自己的唯一入口）。
    /// </summary>
    void KeepKeyboardOnQueue();

    /// <summary>
    /// 本页是不是这一次 Esc 的落点：是（本页可见且筛选框里留着关键词）就清空并把键盘留在框里，返回 true
    /// 表示按键已被接住；否返回 false，壳据此不再吞键（判定在 ListFilter.EscClears，壳与页都不另写一套）。
    /// </summary>
    bool TryClearListFilter();
}
