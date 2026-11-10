using System.Diagnostics;
using System.Text;
using Xunit;
using System.Text.Json.Nodes;

namespace ApifoxHelper.Tests;

/// <summary>
/// 真浏览器量测的入口（HTML 折行禁则的机械量测所需，第一百七十轮）。
/// <para>
/// <b>为什么要有这个类</b>：<c>ReportRender.HtmlStyle</c> 里的 <c>line-break:strict;word-break:normal</c>
/// 与 <c>max-width:40em</c> 此前只有声明门——字符串在位不等于折行成立。折行是浏览器干的，
/// 所以量它只能请真浏览器。本类只做三件事：找到浏览器、把产物原样交给它、读回一行 JSON 量测结果。
/// 排版判据全部在 <c>ChromeProbe.js</c> 里（那里只读产物的样式与文字，不改它们）。
/// </para>
/// <para>
/// <b>找浏览器的顺序是封闭的</b>：<c>APIFOXHELPER_CHROME</c> → <c>APIFOXHELPER_HOME</c>（或直接 HOME）下的
/// Playwright 缓存 <c>.cache/ms-playwright/chromium-*/chrome-linux*/chrome</c> → PATH 上三个常见名字。
/// 找不到不等于跳过判定：<see cref="Available"/> 为 false 时，门退成「声明在位 + NOTES 挂账在位」，
/// <b>绝不把「本机没跑」算成「排版成立」</b>（这条纪律与 wine 结果不等于真机结果同一份）。
/// </para>
/// <para>
/// 产物一个字都不改：页面自带的 CSP（<c>default-src 'none'</c>）禁止内联脚本，探针因此走 CDP 的
/// <c>Runtime.evaluate</c>（不受页面 CSP 约束），而不是往文件里塞 <c>&lt;script&gt;</c>。
/// </para>
/// </summary>
internal static class ChromeLayout
{
    /// <summary>浏览器可执行文件；null 表示按 <see cref="LookupOrder"/> 找过、没找到。</summary>
    public static readonly string? ChromePath = FindChrome();

    /// <summary>node 可执行文件（探针是 JS，靠 node 驱动 CDP）。</summary>
    public static readonly string? NodePath = FindFirstOnPath(["APIFOXHELPER_NODE", "node", "nodejs"]);

    /// <summary>找浏览器的顺序，写进失败消息与 NOTES，换机器时照着设环境变量就行。</summary>
    public const string LookupOrder =
        "APIFOXHELPER_CHROME → $HOME/.cache/ms-playwright/chromium-*/chrome-linux*/chrome → PATH(chromium|chromium-browser|google-chrome)";

    public static bool Available => ChromePath is not null && NodePath is not null;

    public static string MissingReason =>
        ChromePath is null
            ? $"本机没有可用的 headless Chromium（查找顺序：{LookupOrder}）"
            : NodePath is null
                ? "本机没有 node，跑不了 CDP 探针"
                : string.Empty;

    /// <summary>
    /// 把渲染出来的产物交给真浏览器量一次。<paramref name="windowWidth"/> 是要量的视口宽：
    /// 宽（1280）量的是「列宽由 <c>max-width</c> 决定」，窄（560）量的是「正文真的折行」——
    /// 同一份字节，两档视口，缺哪一档都有一支判定看不见对方。
    /// </summary>
    public static JsonObject Measure(string html, int windowWidth)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ah-htmlwrap");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"report-{windowWidth}.html");
        File.WriteAllText(file, html, new UTF8Encoding(false));

        var probe = Path.Combine(Repo.Root, "ApifoxHelper.Tests", "ChromeProbe.js");
        Assert.True(File.Exists(probe), $"探针不存在：{Path.GetRelativePath(Repo.Root, probe)}。");

        var (exit, stdout, stderr) = Run(NodePath!, [
            probe,
            "--chrome=" + ChromePath,
            "--html=" + file,
            "--start-bad=" + CopyRules.LineStartForbidden,
            "--end-bad=" + CopyRules.LineEndForbidden,
            $"--window-width={windowWidth}",
        ]);

        Assert.True(exit == 0, $"浏览器量测没跑成（退出码 {exit}）。\nstderr：{Tail(stderr)}\n产物：{file}");
        var json = stdout.Trim();
        Assert.True(json.StartsWith('{'), $"浏览器量测返回的不是 JSON：{Tail(json)}");
        return JsonNode.Parse(json)!.AsObject();
    }

    private static (int Exit, string StdOut, string StdErr) Run(string fileName, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("进程起不来：" + fileName);
        var outTask = process.StandardOutput.ReadToEndAsync();
        var errTask = process.StandardError.ReadToEndAsync();
        // 一次量测在受控容器里是秒级；超过 120 秒按「没跑成」处理，不让它把整套测试挂死。
        if (!process.WaitForExit(120_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 已经退了 */ }
            Assert.Fail($"浏览器量测超时（120 秒）：{fileName}");
        }

        return (process.ExitCode, outTask.GetAwaiter().GetResult(), errTask.GetAwaiter().GetResult());
    }

    private static string Tail(string text)
    {
        text = text.Trim();
        return text.Length <= 800 ? text : text[^800..];
    }

    private static string? FindChrome()
    {
        var fromEnv = Environment.GetEnvironmentVariable("APIFOXHELPER_CHROME");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        foreach (var root in new[] { Environment.GetEnvironmentVariable("APIFOXHELPER_HOME"), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            var cache = Path.Combine(root, ".cache", "ms-playwright");
            if (!Directory.Exists(cache)) continue;
            // 版本号会变，所以这里按形状找；多个候选取最新的目录名，取不到就继续。
            var hit = Directory.EnumerateDirectories(cache, "chromium-*")
                .SelectMany(dir => Directory.EnumerateDirectories(dir, "chrome-linux*"))
                .Select(inner => Path.Combine(inner, "chrome"))
                .Where(File.Exists)
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .FirstOrDefault();
            if (hit is not null) return hit;
        }

        return FindFirstOnPath(["chromium", "chromium-browser", "google-chrome"]);
    }

    /// <summary>
    /// 依次把候选当成环境变量名或直接命令找；PATH 查找自己实现，免得依赖 <c>which</c>。
    /// </summary>
    private static string? FindFirstOnPath(IEnumerable<string> candidates)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var candidate in candidates)
        {
            var fromEnv = Environment.GetEnvironmentVariable(candidate);
            if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;
            foreach (var dir in paths)
            {
                var full = Path.Combine(dir, candidate);
                if (File.Exists(full)) return full;
            }
        }
        return null;
    }
}
