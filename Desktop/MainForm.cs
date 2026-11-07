using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ApifoxHelper.Api;
using ApifoxHelper.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ApifoxHelper.Desktop;

/// <summary>
/// 桌面主窗口：内嵌 WebView2 显示本地页面。页面只能访问本机服务地址，外部链接交给系统浏览器；
/// 下载改为“另存为”；关闭时请求正在执行的作业在当前步骤结束后停止。
/// </summary>
internal sealed class MainForm : Form
{
    private const string Title = "Apifox 助手";
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private static readonly TimeSpan JobGrace = TimeSpan.FromSeconds(15);
    private static readonly Color PageBackground = Color.FromArgb(245, 247, 251);

    private readonly HelperHost _host;
    private readonly WindowPlacement _placement;
    private readonly WebView2 _web;
    private readonly Panel _notice;
    private readonly Label _noticeText;
    private readonly Button _browserButton;
    private readonly Button _runtimeButton;
    private string? _lastSaveDirectory;
    private bool _webReady;
    private bool _shutdownStarted;
    private bool _shutdownDone;
    private int _folderDialogOpen;

    public MainForm(HelperHost host)
    {
        _host = host;
        Text = Title;
        Icon = LoadIcon();
        MinimumSize = new Size(1024, 640);
        Size = new Size(1400, 900);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = PageBackground;

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = PageBackground };
        _noticeText = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Anchor = AnchorStyles.None,
            Font = new Font("Microsoft YaHei UI", 11F),
            ForeColor = Color.FromArgb(55, 65, 81),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0, 0, 0, 16),
        };
        _browserButton = new Button { Text = "在浏览器中打开", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        _browserButton.Click += (_, _) => OpenInBrowser();
        _runtimeButton = new Button { Text = "下载 WebView2 运行时", AutoSize = true, Padding = new Padding(12, 4, 12, 4) };
        _runtimeButton.Click += (_, _) => OpenExternal(RuntimeDownloadUrl);
        _notice = BuildNotice();

        var menu = BuildMenu();
        // 停靠按集合倒序处理：菜单最后加入、最先停靠；提示层在页面之上，隐藏后露出页面。
        Controls.Add(_notice);
        Controls.Add(_web);
        Controls.Add(menu);
        MainMenuStrip = menu;

        _placement = new WindowPlacement(Path.Combine(host.Store.Root, "desktop-window.json"));
        _placement.Restore(this);
        host.Desktop.PickFolder = PickFolderAsync;
        ShowNotice("正在启动…", browser: false, runtime: false);
    }

    /// <summary>
    /// 页面通过本地接口请求选择目录（已校验会话令牌）。对话框在界面线程以本窗口为所有者模态显示，同一时间只允许一个。
    /// </summary>
    private Task<string?> PickFolderAsync(string? initial)
    {
        if (IsShuttingDown || !IsHandleCreated) return Task.FromResult<string?>(null);
        if (Interlocked.Exchange(ref _folderDialogOpen, 1) == 1) throw new UserException("选择目录的窗口已经打开。", 409);
        var result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(() =>
            {
                try
                {
                    if (IsShuttingDown)
                    {
                        result.TrySetResult(null);
                        return;
                    }
                    using var dialog = new FolderBrowserDialog
                    {
                        Description = "选择代码仓库根目录",
                        UseDescriptionForTitle = true,
                        ShowNewFolderButton = false,
                    };
                    if (initial is not null) dialog.InitialDirectory = initial;
                    result.TrySetResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null);
                }
                catch (Exception ex)
                {
                    result.TrySetException(ex);
                }
                finally
                {
                    Volatile.Write(ref _folderDialogOpen, 0);
                }
            });
        }
        catch (InvalidOperationException)
        {
            // 窗口句柄已销毁（正在退出）。
            Volatile.Write(ref _folderDialogOpen, 0);
            return Task.FromResult<string?>(null);
        }
        return result.Task;
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
            ShowNotice("本地服务启动失败：" + Redactor.Redact(ex.Message) + "\n\n详细信息见“文件 → 打开日志目录”。", browser: false, runtime: false);
            return;
        }
        if (IsShuttingDown) return;
        await InitializeWebViewAsync();
    }

    /// <summary>窗口已开始退出：异步初始化的后续步骤不能再操作界面。</summary>
    private bool IsShuttingDown => _shutdownStarted || _shutdownDone || IsDisposed || Disposing;

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // 内核缓存放在资料目录，程序放在只读位置（如 Program Files）时也能运行。
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_host.Store.Root, "webview2"));
            await _web.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowNotice("未检测到 Microsoft Edge WebView2 运行时，无法在窗口内显示页面。\n安装运行时后重新打开本工具；也可以先在浏览器中使用（本地服务已启动）。", browser: true, runtime: true);
            return;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or IOException)
        {
            // 初始化期间关闭窗口会让内核以 E_ABORT 中止，这属于正常退出。
            if (IsShuttingDown) return;
            _host.Logger.LogError(ex, "WebView2 初始化失败");
            ShowNotice("内置页面初始化失败：" + Redactor.Redact(ex.Message) + "\n可以先在浏览器中使用（本地服务已启动）。", browser: true, runtime: true);
            return;
        }
        if (IsShuttingDown) return;

        var core = _web.CoreWebView2;
        var settings = core.Settings;
        settings.AreDevToolsEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = false;
        // 页面里有模型服务密钥输入框，不能让内核保存密码或自动填充。
        TrySupported(() => settings.IsPasswordAutosaveEnabled = false);
        TrySupported(() => settings.IsGeneralAutofillEnabled = false);
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.ProcessFailed += OnProcessFailed;
        TrySupported(() => core.DownloadStarting += OnDownloadStarting);
        _webReady = true;
        core.Navigate(_host.Url);
    }

    private static void TrySupported(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is NotImplementedException or InvalidCastException)
        {
            // 旧版 WebView2 运行时不支持该能力，使用内核默认行为。
        }
    }

    private bool IsLocal(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttp
        && u.Port == _host.Port
        && (u.Host == "127.0.0.1" || u.Host == "localhost");

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (IsLocal(e.Uri)) return;
        e.Cancel = true;
        OpenExternal(e.Uri);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _notice.Visible = false;
            return;
        }
        // 被拦截的外部导航也会以“已取消”结束，不属于加载失败。
        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
        ShowNotice($"页面加载失败（{e.WebErrorStatus}）。", browser: true, runtime: false);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (IsLocal(e.Uri)) OpenInBrowser();
        else OpenExternal(e.Uri);
    }

    private void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        // 延后到事件返回后再弹出对话框，避免在内核回调中重入。
        var deferral = e.GetDeferral();
        BeginInvoke(() =>
        {
            using (deferral)
            {
                var suggested = Path.GetFileName(e.ResultFilePath);
                using var dialog = new SaveFileDialog
                {
                    Title = "保存文件",
                    FileName = suggested,
                    Filter = FileFilter(Path.GetExtension(suggested)),
                    InitialDirectory = _lastSaveDirectory ?? Path.GetDirectoryName(e.ResultFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    OverwritePrompt = true,
                    AddExtension = true,
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    e.Cancel = true;
                    return;
                }
                _lastSaveDirectory = Path.GetDirectoryName(dialog.FileName);
                e.ResultFilePath = dialog.FileName;
                e.Handled = true;
                var operation = e.DownloadOperation;
                operation.StateChanged += (_, _) =>
                {
                    if (operation.State != CoreWebView2DownloadState.Interrupted) return;
                    var reason = operation.InterruptReason;
                    BeginInvoke(() => MessageBox.Show(this, $"文件保存失败：{reason}", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning));
                };
            }
        });
    }

    private static string FileFilter(string extension) => extension.ToLowerInvariant() switch
    {
        ".md" => "Markdown 文件 (*.md)|*.md|所有文件 (*.*)|*.*",
        ".html" or ".htm" => "HTML 文件 (*.html)|*.html|所有文件 (*.*)|*.*",
        ".json" => "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
        ".patch" or ".diff" => "补丁文件 (*.patch)|*.patch|所有文件 (*.*)|*.*",
        _ => "所有文件 (*.*)|*.*",
    };

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _host.Logger.LogWarning("WebView2 进程异常：{Kind}", e.ProcessFailedKind);
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                _webReady = false;
                BeginInvoke(() => ShowNotice("内置页面引擎已退出。本地服务仍在运行，可以在浏览器中继续使用，或重新打开本工具。", browser: true, runtime: false));
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                // 页面状态都保存在本地服务中，重新加载不会丢失任务资料。
                BeginInvoke(() => _web.Reload());
                break;
        }
    }

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (_shutdownDone) return;
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            // 系统关机时不能阻止退出；未完成的同步会在下次启动时标记为“已中断”，等待重新核对。
            _placement.Save(this);
            return;
        }
        e.Cancel = true;
        if (_shutdownStarted) return;

        // 用户关闭窗口时，有作业在执行需要确认；其他程序（如任务管理器“结束任务”）请求关闭时不弹窗，只缩短等待时间。
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
                $"以下操作仍在执行：{kinds}。\n\n退出时会请求停止，最多等待 {JobGrace.TotalSeconds:0} 秒让当前步骤结束。同步如果没有全部完成，下次打开后请在“同步”页点击“重新核对”。\n\n确定退出吗？",
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

    private MenuStrip BuildMenu()
    {
        static ToolStripMenuItem Item(string text, string? shortcutHint, Action onClick)
        {
            var item = new ToolStripMenuItem(text, null, (_, _) => onClick());
            // 快捷键由页面内核处理，这里只做提示，避免与内核重复响应。
            if (shortcutHint is not null) item.ShortcutKeyDisplayString = shortcutHint;
            return item;
        }

        var file = new ToolStripMenuItem("文件(&F)");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("在浏览器中打开(&B)", null, OpenInBrowser),
            Item("打开资料目录(&D)", null, () => OpenFolder(_host.Store.Root)),
            Item("打开日志目录(&L)", null, () => OpenFolder(_host.LogDirectory)),
            new ToolStripSeparator(),
            Item("退出(&X)", null, Close),
        });
        var view = new ToolStripMenuItem("视图(&V)");
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("刷新(&R)", "F5", () => { if (_webReady) _web.Reload(); }),
            Item("放大(&I)", "Ctrl+=", () => Zoom(0.1)),
            Item("缩小(&O)", "Ctrl+-", () => Zoom(-0.1)),
            Item("实际大小(&A)", "Ctrl+0", () => { if (_webReady) _web.ZoomFactor = 1; }),
            new ToolStripSeparator(),
            Item("开发者工具(&T)", null, OpenDevTools),
        });
        var help = new ToolStripMenuItem("帮助(&H)");
        help.DropDownItems.Add(Item("关于 Apifox 助手(&A)", null, ShowAbout));

        var menu = new MenuStrip { Dock = DockStyle.Top };
        menu.Items.AddRange(new ToolStripItem[] { file, view, help });
        return menu;
    }

    private Panel BuildNotice()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.None, WrapContents = false, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(_browserButton);
        buttons.Controls.Add(_runtimeButton);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(_noticeText, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = PageBackground };
        panel.Controls.Add(layout);
        return panel;
    }

    private void ShowNotice(string text, bool browser, bool runtime)
    {
        if (_shutdownStarted || IsDisposed || Disposing) return;
        _noticeText.Text = text;
        _browserButton.Visible = browser;
        _runtimeButton.Visible = runtime;
        _notice.Visible = true;
    }

    private void Zoom(double delta)
    {
        if (_webReady) _web.ZoomFactor = Math.Clamp(Math.Round(_web.ZoomFactor + delta, 2), 0.5, 2.5);
    }

    private void OpenDevTools()
    {
        if (!_webReady) return;
        _web.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _web.CoreWebView2.OpenDevToolsWindow();
    }

    private void OpenInBrowser()
    {
        if (_host.Port == 0)
        {
            MessageBox.Show(this, "本地服务尚未启动。", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Browser.TryOpen(_host.Url, out var error))
        {
            MessageBox.Show(this, $"无法打开浏览器：{error}\n\n请手动访问：{_host.Url}", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;
        if (!Browser.TryOpen(uri.AbsoluteUri, out var error))
        {
            MessageBox.Show(this, $"无法打开链接：{error}\n\n{uri.AbsoluteUri}", Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
        string runtime;
        try
        {
            runtime = CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "未安装";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            runtime = "未安装";
        }
        MessageBox.Show(this,
            $"Apifox 助手 {TaskStore.ToolVersion}\n\n本地服务：{(_host.Port == 0 ? "未启动" : _host.Url)}\n资料目录：{_host.Store.Root}\nWebView2 运行时：{runtime}\n.NET：{RuntimeInformation.FrameworkDescription}",
            "关于 Apifox 助手", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static Icon? LoadIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Assets.app.ico");
        return stream is null ? null : new Icon(stream);
    }
}

/// <summary>记住窗口位置与大小；显示器布局变化导致窗口不可见时不恢复位置。</summary>
internal sealed class WindowPlacement(string path)
{
    private sealed record Saved(int X, int Y, int Width, int Height, bool Maximized);

    public void Restore(Form form)
    {
        Saved? saved;
        try
        {
            saved = FileStore.ReadJson<Saved>(path);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return;
        }
        if (saved is null || saved.Width < form.MinimumSize.Width / 2 || saved.Height < form.MinimumSize.Height / 2) return;
        var bounds = new Rectangle(saved.X, saved.Y, saved.Width, saved.Height);
        var visible = Screen.AllScreens.Any(s =>
        {
            var overlap = Rectangle.Intersect(s.WorkingArea, bounds);
            return overlap.Width >= 200 && overlap.Height >= 150;
        });
        if (!visible) return;
        form.StartPosition = FormStartPosition.Manual;
        form.Bounds = bounds;
        if (saved.Maximized) form.WindowState = FormWindowState.Maximized;
    }

    public void Save(Form form)
    {
        var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        try
        {
            FileStore.WriteJson(path, new Saved(bounds.X, bounds.Y, bounds.Width, bounds.Height, form.WindowState == FormWindowState.Maximized));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 窗口位置只是便利设置，保存失败不影响退出。
        }
    }
}
