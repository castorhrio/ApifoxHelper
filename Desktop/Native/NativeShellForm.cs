using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ApifoxHelper.Api;
using ApifoxHelper.Comments;
using ApifoxHelper.Profiles;
using ApifoxHelper.Source;
using ApifoxHelper.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 原生桌面壳（F16 首个纵切片）：原生菜单 + 左侧导航 + 页面区。
/// 复用进程内服务（HelperHost），不嵌网页；"在浏览器中打开"不在本壳的菜单中。
/// 本切片只接入：项目配置、任务与源码扫描；后续阶段在原生界面逐切片接入，未接入处如实显示禁用态。
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
        _navProjects.Click += (_, _) => ShowPage(_projectsPage, _navProjects);
        _navTasks = NavButton("检查任务");
        _navTasks.Click += (_, _) => ShowPage(_tasksPage, _navTasks);

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

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("打开资料目录(&D)", () => OpenFolder(_host.Store.Root)),
            Item("打开日志目录(&L)", () => OpenFolder(_host.LogDirectory)),
            new ToolStripSeparator(),
            Item("退出(&X)", Close),
        });
        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(Item("关于 Apifox 助手(&A)", ShowAbout));

        var menu = new MenuStrip { Dock = DockStyle.Top, Font = UiTokens.Body.Value };
        menu.Items.AddRange(new ToolStripItem[] { file, help });
        return menu;
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

        _projectsPage = new ProjectsPage(_host.Services.GetRequiredService<ProfileStore>());
        _tasksPage = new TasksPage(
            _host.Services.GetRequiredService<TaskStore>(),
            _host.Services.GetRequiredService<SourceScanService>(),
            _host.Jobs,
            _host.Services.GetRequiredService<ProfileStore>(),
            _host.Services.GetRequiredService<DraftService>());

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

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_shutdownDone) return;
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            // 系统关机时不能阻止退出；未结束的同步会在下次启动时标为“已中断”，等待重新核对。
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
}
