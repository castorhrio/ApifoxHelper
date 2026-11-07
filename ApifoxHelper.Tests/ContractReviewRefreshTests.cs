using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 契约审阅关闭后的整页刷新断言（第四十三轮，DESIGN.md §8 条款）：
/// 响应契约审阅经共享 ContractReviewService 写 responseContract.json 并递增任务版本（旧检查与旧计划过期），
/// 对话框此前关闭后只刷新契约计数行——引导横幅、「任务内容在检查后有变化，需要重新检查」提示与
/// 任务列表「下一步」列全部滞留旧版本。修复口径：关闭后与其他写任务数据的入口（注释回写、差异确认、
/// 补充说明等）同一口径整页重读；接线由行首正则机械检查钉住（被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class ContractReviewRefreshTests
{
    private static string ShowContractsBody()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var start = source.IndexOf("private void ShowContracts()", StringComparison.Ordinal);
        var end = source.IndexOf("/// <summary>报告区状态行", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "TasksPage.cs 缺少 ShowContracts 方法或其后续锚点。");
        return source[start..end];
    }

    [Fact]
    public void ShowContracts_ReloadsTasksAfterDialogCloses()
    {
        var body = ShowContractsBody();
        // 关闭对话框后整页重读：契约计数、引导横幅、远端状态行与列表「下一步」列都从磁盘新版本推导。
        Assert.Matches(@"(?m)^\s*dialog\.ShowDialog\(FindForm\(\)\);\s*\n(?:\s*//[^\n]*\n)*\s*LoadTasks\(\);", body);
    }

    [Fact]
    public void ShowContracts_DoesNotStopAtContractCountsOnly()
    {
        var body = ShowContractsBody();
        // 只刷新契约计数行是本切片修复前的缺陷形态：版本推进后的横幅与过期提示不在该路径上。
        Assert.DoesNotContain("RefreshContractSection()", body);
    }

    [Fact]
    public void ContractReview_RevisionBumpAccompaniesContractWrite()
    {
        var source = Repo.Source("Source", "ContractReviewService.cs");
        // 刷新必要性的来源：审阅写入契约清单的同时递增任务版本，使旧检查与旧计划过期。
        Assert.Matches(
            @"(?m)^\s*tasks\.Write\(taskId, TaskFiles\.ResponseContract, contracts\);\s*\n\s*t\.Revision\+\+;",
            source);
    }

    [Fact]
    public void DetailPane_ShowsStaleCheckNoticeFromRevision()
    {
        var source = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 滞留后果的两条呈现：远端状态行的「有变化，需要重新检查」后缀按版本推导，横幅按同一任务重算。
        Assert.Matches(
            @"(?m)^\s*_remoteStatus\.Text = ""状态："" \+ TaskDisplay\.RemoteStatusText\(remote\.Status\) \+ \(remote\.CheckedRevision is \{ \} rev && rev < task\.Revision \? ""（任务内容在检查后有变化，需要重新检查）"" : """"\);",
            source);
        Assert.Matches(@"(?m)^\s*RefreshNextBanner\(NextStep\.Compute\(task\)\);", source);
    }

    [Fact]
    public void DesignDoc_RecordsTaskDataRefreshRule()
    {
        var section = Repo.DesignSection(8);
        Assert.Contains("写任务数据后的详情刷新", section);
        Assert.Contains("契约审阅", section);
        Assert.Contains("整页重读", section);
    }
}
