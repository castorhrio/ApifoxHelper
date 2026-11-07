namespace ApifoxHelper;

public static class Program
{
    /// <summary>
    /// 默认打开桌面窗口；--headless 只启动本地服务（用于自动化或没有 WebView2 的环境）。
    /// </summary>
    [STAThread]
    public static int Main(string[] args) => Desktop.DesktopApp.Run(args);
}
