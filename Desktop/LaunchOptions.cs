namespace ApifoxHelper.Desktop;

/// <summary>
/// 命令行启动选项。原生桌面客户端是默认入口（方案 1.4：桌面客户端是必选主交付）；
/// --web 显式回退到冻结的过渡壳（WinForms + WebView2 内嵌冻结页面，方案 2.4：只修阻断性缺陷）；
/// --headless 只运行本地服务（自动化/CI 入口），优先于两个壳。
/// </summary>
public sealed record LaunchOptions(int Port, string DataRoot, bool Headless, bool Web = false)
{
    public const string Usage =
        "用法：ApifoxHelper.exe [--data <资料目录>] [--port <端口>] [--headless] [--web]\n" +
        "默认启动原生桌面客户端；--web 打开冻结的过渡壳（内嵌网页，仅供回退）。";

    public static LaunchOptions Parse(string[] args)
    {
        var port = 5179;
        string? data = null;
        var headless = false;
        var web = false;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"参数 {args[i]} 缺少取值。");
            switch (args[i])
            {
                case "--port":
                    if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is < 0 or > 65535)
                    {
                        throw new ArgumentException("端口必须是 0–65535 的整数。");
                    }
                    break;
                case "--data":
                    data = Next();
                    break;
                case "--headless":
                case "--no-browser": // 兼容旧版命令行参数
                    headless = true;
                    break;
                case "--web":
                    web = true;
                    break;
                case "--native":
                    // 原生桌面客户端已是默认入口，此参数保留为兼容写法，不再改变行为。
                    break;
                default:
                    throw new ArgumentException("未知参数：" + args[i]);
            }
        }
        data ??= Environment.GetEnvironmentVariable("APIFOXHELPER_DATA")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ApifoxHelper");
        return new LaunchOptions(port, Path.GetFullPath(data), headless, web);
    }
}
