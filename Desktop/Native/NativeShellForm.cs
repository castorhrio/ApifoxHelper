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

        _navProjects = NavButton("项目设置");
        _navProjects.Click += (_, _) => ShowProjects();
        _navTasks = NavButton("检查任务");
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
            BoundItem("项目设置(&P)", ShellShortcuts.Projects, ShowProjects),
            BoundItem("检查任务(&T)", ShellShortcuts.Tasks, ShowTasks),
            new ToolStripSeparator(),
            BoundItem("刷新当前页(&R)", ShellShortcuts.Refresh, RefreshVisiblePage),
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
            _host.Services.GetRequiredService<CredentialStore>());
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
            _host.Services.GetRequiredService<Sync.SyncExecutor>());

        _content.Controls.Clear();
        _projectsPage.Dock = DockStyle.Fill;
        _tasksPage.Dock = DockStyle.Fill;
        _content.Controls.Add(_tasksPage);
        _content.Controls.Add(_projectsPage);
        _tasksPage.Visible = false;

        _navProjects.Enabled = true;
        _navTasks.Enabled = true;
        ShowPage(_projectsPage, _navProjects);
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
        if (page is INativePage shown) shown.OnPageShown();
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
                $"以下操作仍在执行：{kinds}。\n\n退出时会请求停止，最多等待 {JobGrace.TotalSeconds:0} 秒让当前步骤结束。未完成的操作下次打开后请重新核对。\n\n确定退出吗？",
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

    private void OpenFolder(string path)
    {
        if (!Browser.TryOpen(path, out var error))
        {
            MessageBox.Show(this, $"无法打开目录：{error}\n\n{path}", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(this,
            $"Apifox 助手 {TaskStore.ToolVersion}（原生桌面界面·开发中）\n\n本地服务：{_host.Url}\n资料目录：{_host.Store.Root}\n.NET：{RuntimeInformation.FrameworkDescription}",
            "关于 Apifox 助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
}
