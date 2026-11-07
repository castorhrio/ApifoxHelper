using System.Text.Json.Nodes;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 定向构建入口的口径（冻结页面与原生界面同一规则，BuildStatus 单一落点）：
/// 构建是用户确认后才执行的动作，确认文案如实说明命令与副作用（不 restore、不启动服务）；
/// 上次构建的状态行按退出码与超时如实报告——超时与非零退出码不显示成完成，
/// 记录缺少退出码时不猜测结果。数据形状来自 BuildService 落盘的 build.json。
/// </summary>
public class BuildStatusTests
{
    private static JsonNode? Record(string json) => json is null ? null : JsonNode.Parse(json);
    private const string OkJson = """{"command":"dotnet build demo.csproj --no-restore","exitCode":0,"timedOut":false,"seconds":2.6}""";
    private const string FailJson = """{"command":"dotnet build demo.csproj --no-restore","exitCode":1,"timedOut":false,"seconds":3.1}""";
    private const string TimeoutJson = """{"command":"dotnet build demo.csproj --no-restore","exitCode":-1,"timedOut":true,"seconds":900}""";

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void CanStart_RequiresSelectedTask(bool hasTask, bool expected) =>
        Assert.Equal(expected, BuildStatus.CanStart(hasTask));

    [Fact]
    public void ConfirmText_StatesCommandAndSideEffects()
    {
        var text = BuildStatus.ConfirmText("dotnet build demo.csproj --no-restore");
        Assert.Contains("dotnet build demo.csproj --no-restore", text);
        Assert.Contains("bin/obj", text);
        Assert.Contains("不会还原依赖或启动服务", text);
    }

    [Fact]
    public void LastText_NullRecord_IsEmptyState()
    {
        var (text, tone) = BuildStatus.LastText(null);
        Assert.Equal("尚未构建", text);
        Assert.Equal("muted", tone);
    }

    [Fact]
    public void LastText_ZeroExitCode_ReportsSuccessWithSeconds()
    {
        var (text, tone) = BuildStatus.LastText(Record(OkJson));
        Assert.Equal("ok", tone);
        Assert.Contains("退出码 0", text);
        Assert.Contains("2.6", text);
    }

    [Fact]
    public void LastText_NonZeroExitCode_NeverShowsSuccess()
    {
        var (text, tone) = BuildStatus.LastText(Record(FailJson));
        Assert.Equal("bad", tone);
        Assert.Contains("退出码 1", text);
        Assert.DoesNotContain("退出码 0", text);
        Assert.Contains("查看构建输出", text);
    }

    [Fact]
    public void LastText_Timeout_ReportsIncomplete()
    {
        var (text, tone) = BuildStatus.LastText(Record(TimeoutJson));
        Assert.Equal("bad", tone);
        Assert.Contains("超时", text);
        Assert.DoesNotContain("退出码 0，", text);
        Assert.Contains("查看构建输出", text);
    }

    [Fact]
    public void LastText_MissingExitCode_DoesNotGuessResult()
    {
        var (text, tone) = BuildStatus.LastText(Record("""{"command":"dotnet build demo.csproj"}"""));
        Assert.Equal("bad", tone);
        Assert.Contains("无法判断", text);
    }

    [Theory]
    [InlineData(OkJson)]
    [InlineData(FailJson)]
    [InlineData(TimeoutJson)]
    public void LastTexts_AvoidBannedPhrases(string json)
    {
        var (text, _) = BuildStatus.LastText(Record(json));
        Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"),
            "构建状态文案出现禁词。");
    }
}
