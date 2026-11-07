using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 长作业完成提醒的口径断言（AttentionCue，共享源码单一口径）：
/// 只在用户离开（应用无窗口持有焦点）、退出未开始、作业到达需要用户回来的终态
/// （succeeded/failed）时提醒；canceled 是用户自己的动作，结果符合预期，不提醒。
/// 壳窗口的订阅与闪烁接线、JobRunner 的完成事件由源码机械检查钉住——口径存在但没人调用等于没有。
/// </summary>
public class AttentionCueTests
{
    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    public void TerminalStates_NotifyWhenAway(string state) =>
        Assert.True(AttentionCue.ShouldFlash(appFocused: false, state, shutdownStarted: false));

    [Fact]
    public void Canceled_DoesNotNotify() =>
        Assert.False(AttentionCue.ShouldFlash(false, "canceled", false));

    [Fact]
    public void Running_DoesNotNotify() =>
        Assert.False(AttentionCue.ShouldFlash(false, "running", false));

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("canceled")]
    [InlineData(null)]
    public void FocusedApp_DoesNotNotify(string? state) =>
        Assert.False(AttentionCue.ShouldFlash(true, state, false));

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    public void Shutdown_DoesNotNotify(string state) =>
        Assert.False(AttentionCue.ShouldFlash(false, state, true));

    // ── 接线机械检查 ──────────────────────────────────────────────────

    [Fact]
    public void Shell_SubscribesCompletedEvent_AndUsesCue()
    {
        var shell = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        Assert.Contains("JobCompleted += OnJobCompleted", shell);
        Assert.Contains("AttentionCue.ShouldFlash(AppHoldsFocus()", shell);
        Assert.Contains("FlashWindowEx(ref info)", shell);
        Assert.Contains("OnActivated", shell);
        Assert.Contains("StopFlash()", shell);
    }

    [Fact]
    public void JobRunner_RaisesCompletedEvent_AfterTerminalState()
    {
        var runner = Repo.Source("Infrastructure", "JobRunner.cs");
        Assert.Contains("event Action<JobInfo>? JobCompleted", runner);
        Assert.Contains("JobCompleted?.Invoke(job);", runner);
        Assert.Matches(@"FinishedAt = DateTimeOffset\.Now;[\s\S]*JobCompleted\?\.Invoke\(job\);", runner);
    }
}
