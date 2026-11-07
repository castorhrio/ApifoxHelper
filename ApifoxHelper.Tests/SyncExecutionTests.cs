using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步执行入口的口径（冻结页面与原生对话框同一规则，SyncExecution 单一落点）：
/// 「确认同步」只在计划待确认且任务内容未变化（未过期）时可用；
/// 「重新核对（只读）」在待确认（尚无可核对的动作）与执行中（不能同时核对）之外都可用；
/// 执行或核对结束后的状态行按计划状态如实报告——部分完成不得显示成全部成功。
/// </summary>
public class SyncExecutionTests
{
    [Theory]
    [InlineData("awaitingConfirm", false, true)]
    [InlineData("awaitingConfirm", true, false)]
    [InlineData("executing", false, false)]
    [InlineData("verified", false, false)]
    [InlineData("partial", false, false)]
    [InlineData("failed", false, false)]
    [InlineData("expired", false, false)]
    [InlineData("interrupted", false, false)]
    [InlineData("canceled", false, false)]
    public void CanExecute_OnlyFreshAwaitingConfirmPlan(string status, bool outdated, bool expected) =>
        Assert.Equal(expected, SyncExecution.CanExecute(status, outdated));

    [Theory]
    [InlineData("awaitingConfirm", false)]
    [InlineData("executing", false)]
    [InlineData("verified", true)]
    [InlineData("partial", true)]
    [InlineData("failed", true)]
    [InlineData("expired", true)]
    [InlineData("interrupted", true)]
    [InlineData("canceled", true)]
    public void CanVerify_NotWhileAwaitingConfirmOrExecuting(string status, bool expected) =>
        Assert.Equal(expected, SyncExecution.CanVerify(status));

    [Fact]
    public void ResultText_VerifiedReportsAllActionsChecked()
    {
        var (text, tone) = SyncExecution.ResultText("verified", verified: 3, failed: 0, notRun: 0);
        Assert.Equal("ok", tone);
        Assert.Contains("核对通过", text);
        Assert.Contains("3", text);
    }

    [Fact]
    public void ResultText_PartialReportsUnfinishedCountsAndNeverClaimsFullSuccess()
    {
        var (text, tone) = SyncExecution.ResultText("partial", verified: 1, failed: 1, notRun: 2);
        Assert.Equal("warn", tone);
        Assert.Contains("部分完成", text);
        Assert.Contains("1", text);
        Assert.Contains("3", text); // 未完成 = 写入失败等 1 + 未执行 2
        Assert.DoesNotContain("全部核对", text);
    }

    [Fact]
    public void ResultText_FailedAndStopped()
    {
        var (failedText, failedTone) = SyncExecution.ResultText("failed", verified: 0, failed: 2, notRun: 0);
        Assert.Equal("bad", failedTone);
        Assert.Contains("失败", failedText);
        var (canceledText, canceledTone) = SyncExecution.ResultText("canceled", verified: 0, failed: 0, notRun: 2);
        Assert.Equal("warn", canceledTone);
        Assert.Contains("已停止", canceledText);
    }

    [Fact]
    public void ResultText_InterruptedPointsToVerifyFirst()
    {
        var (text, tone) = SyncExecution.ResultText("interrupted", verified: 0, failed: 0, notRun: 3);
        Assert.Equal("warn", tone);
        Assert.Contains("重新核对", text);
    }

    [Theory]
    [InlineData("expired", "已过期")]
    [InlineData("awaitingConfirm", "待确认")]
    [InlineData("mystery", "mystery")] // 未知值原样出现在文案里，不冒充已知状态
    public void ResultText_UnexpectedStatusFallsBackHonestly(string status, string expectedInText)
    {
        var (text, tone) = SyncExecution.ResultText(status, verified: 0, failed: 0, notRun: 0);
        Assert.Equal("warn", tone);
        Assert.Contains(expectedInText, text);
        Assert.DoesNotContain("全部核对", text);
    }

    [Fact]
    public void ResultTexts_AvoidBannedPhrases()
    {
        foreach (var status in new[] { "verified", "partial", "failed", "canceled", "interrupted", "expired", "awaitingConfirm", "mystery" })
        {
            Assert.False(Regex.IsMatch(SyncExecution.ResultText(status, 1, 1, 1).Text, "赋能|闭环|一站式|无缝|智能"),
                $"状态 {status} 的结果文案出现禁词。");
        }
    }
}
