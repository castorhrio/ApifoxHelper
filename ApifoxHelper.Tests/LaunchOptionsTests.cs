using ApifoxHelper.Desktop;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 启动选项口径（LaunchOptions，共享源码单一口径）：
/// 原生桌面客户端是默认入口；--web 显式回退到冻结的过渡壳（内嵌冻结页面）；--headless 只运行本地服务；
/// --native 保留为兼容写法。壳的分支选择与优先级由源码机械检查钉住——选项存在但入口没接线等于没有。
/// </summary>
public class LaunchOptionsTests : IDisposable
{
    private const string DataEnv = "APIFOXHELPER_DATA";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "apifoxhelper-launch-tests");

    public LaunchOptionsTests() => Environment.SetEnvironmentVariable(DataEnv, _dataRoot);

    public void Dispose() => Environment.SetEnvironmentVariable(DataEnv, null);

    private LaunchOptions Parse(params string[] args) => LaunchOptions.Parse(args);

    [Fact]
    public void Defaults_SelectNativeShell()
    {
        var options = Parse();
        Assert.False(options.Web);
        Assert.False(options.Headless);
        Assert.Equal(5179, options.Port);
        Assert.Equal(Path.GetFullPath(_dataRoot), options.DataRoot);
    }

    [Fact]
    public void WebFlag_SelectsTransitionShell()
    {
        Assert.True(Parse("--web").Web);
    }

    [Fact]
    public void NativeFlag_StaysAcceptedAndKeepsNativeDefault()
    {
        Assert.False(Parse("--native").Web);
    }

    [Fact]
    public void Headless_CanCombineWithShellFlag_BothRecorded()
    {
        // 数据口径：两个标记都如实记录；优先级（headless 先于两个壳）由 DesktopApp 接线机械检查钉住。
        var options = Parse("--headless", "--web");
        Assert.True(options.Headless);
        Assert.True(options.Web);
    }

    [Fact]
    public void NoBrowserAlias_StillMeansHeadless()
    {
        Assert.True(Parse("--no-browser").Headless);
    }

    [Fact]
    public void Port_ParsesInRange_AndRejectsOutside()
    {
        Assert.Equal(8080, Parse("--port", "8080").Port);
        Assert.Equal("端口必须是 0–65535 的整数。",
            Assert.Throws<ArgumentException>(() => Parse("--port", "70000")).Message);
        Assert.Equal("端口必须是 0–65535 的整数。",
            Assert.Throws<ArgumentException>(() => Parse("--port", "abc")).Message);
    }

    [Fact]
    public void DataFlag_OverridesEnvironment_AndIsResolvedToFullPath()
    {
        Assert.Equal(Path.GetFullPath("rel/dir"), Parse("--data", "rel/dir").DataRoot);
    }

    [Fact]
    public void Data_FallsBackToDefaultFolder_WhenEnvironmentUnset()
    {
        Environment.SetEnvironmentVariable(DataEnv, null);
        Assert.EndsWith("ApifoxHelper", Parse().DataRoot, StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownArgument_IsRejected()
    {
        Assert.Equal("未知参数：--native-shell",
            Assert.Throws<ArgumentException>(() => Parse("--native-shell")).Message);
    }

    [Fact]
    public void MissingValue_IsRejected()
    {
        Assert.Equal("参数 --data 缺少取值。",
            Assert.Throws<ArgumentException>(() => Parse("--data")).Message);
    }

    [Fact]
    public void Usage_NamesNativeDefault_AndWebFallback()
    {
        Assert.Contains("默认启动原生桌面客户端", LaunchOptions.Usage, StringComparison.Ordinal);
        Assert.Contains("--web", LaunchOptions.Usage, StringComparison.Ordinal);
        Assert.Contains("内嵌网页", LaunchOptions.Usage, StringComparison.Ordinal);
    }

    // ── 接线机械检查 ──────────────────────────────────────────────────

    [Fact]
    public void Entry_SelectsShells_HeadlessThenWebThenNativeDefault()
    {
        var app = Repo.Source("Desktop", "DesktopApp.cs");
        var headlessRun = app.IndexOf("new HeadlessContext(host, instance)", StringComparison.Ordinal);
        var webBranch = app.IndexOf("if (options.Web)", StringComparison.Ordinal);
        var transition = app.IndexOf("new MainForm(host)", StringComparison.Ordinal);
        var native = app.IndexOf("new Native.NativeShellForm(host)", StringComparison.Ordinal);
        Assert.True(headlessRun >= 0, "缺少 headless 入口（本地服务保留为实现能力）。");
        Assert.True(webBranch > headlessRun, "--web 分支必须在 headless 之后。");
        Assert.True(transition > webBranch, "过渡壳（MainForm）必须只在 --web 分支内创建。");
        Assert.True(native > transition, "原生壳必须是无条件的默认路径。");
        // 默认入口不再依赖 Native 标记；该标记已从选项中移除，不得回潜。
        Assert.DoesNotContain("options.Native", app, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*// 默认入口：原生桌面客户端（F16），不嵌网页，直接复用进程内服务。$", app);
    }

    [Fact]
    public void LaunchOptions_KeepsWebAndCompatibilityCases()
    {
        var source = Repo.Source("Desktop", "LaunchOptions.cs");
        Assert.Matches(@"(?m)^\s*public sealed record LaunchOptions\(int Port, string DataRoot, bool Headless, bool Web = false\)", source);
        Assert.Matches(@"(?m)^\s*case ""--web"":", source);
        Assert.Matches(@"(?m)^\s*case ""--native"":", source);
        Assert.Matches(@"(?m)^\s*case ""--no-browser"": // 兼容旧版命令行参数", source);
    }
}
