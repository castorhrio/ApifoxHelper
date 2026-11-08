using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 异步落点守卫断言（第四十五轮，DESIGN.md §8 条款）：模态对话框的异步操作执行期间「关闭」始终可用，
/// await 的续体回到 UI 线程时窗口可能已释放——此后触达控件（状态行、按钮恢复、列表重建）会把未处理
/// 异常抛给 UI 线程。此前只有较新的路径（注释回写对话框、导入结果、手工候选、壳与同步预览的轮询）有
/// IsDisposed 守卫，九个较早方法缺失（导入结果的按钮恢复也不完整）；现统一为：await 之后触达界面以前先检查（两条 catch 与成功续体
/// 各一处），finally 的按钮恢复同样守卫。接线由方法体切片机械检查钉住（守卫行被注释掉时行首是 //，
/// 仍然算删除）；真机行为随 G-D5 复核。
/// </summary>
public class AsyncDisposeGuardTests
{
    /// <summary>方法体切片：从方法签名到下一个成员锚点（锚点缺失即红，防止断言漂移到空切片）。</summary>
    private static string MethodBody(string file, string signature, string endAnchor)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        var end = source.IndexOf(endAnchor, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"{file} 缺少 {signature} 或其后续锚点 {endAnchor}。");
        return source[start..end];
    }

    /// <summary>「await 之后触达界面以前先检查」的统一形态：两条 catch 与成功续体各一处守卫。</summary>
    private static void AssertThreeGuards(string body, string label, string awaitPattern)
    {
        Assert.Matches(awaitPattern, body);
        Assert.True(Regex.Matches(body, @"(?m)^\s*if \(IsDisposed\) return;").Count == 3,
            $"{label} 的 IsDisposed 守卫应恰有三处（两条 catch 与成功续体）。");
    }

    [Fact]
    public void ReviewAndSavePaths_GuardAllContinuations()
    {
        // 候选审阅：两条 catch 与成功续体（重新读取候选之前）各一处守卫。
        var review = MethodBody("CandidatesDialog.cs", "private async void RunReview(string reviewState)", "/// 导入编码助手结果：选择结果 JSON");
        AssertThreeGuards(review, "CandidatesDialog.RunReview", @"await _drafts\.UpdateAsync\(_task\.Id, candidate\.Id, update, CancellationToken\.None\);");
        Assert.Matches(@"(?m)^\s*if \(IsDisposed\) return;\s*\n\s*try\s*\n\s*\{\s*\n\s*_store = _drafts\.Load\(_task\.Id\);", review);

        // 契约审阅：同一形态；成功守卫在按磁盘重读契约之前。
        var contracts = MethodBody("ContractsDialog.cs", "private async void RunReview(string reviewState)", "private bool SelectContract(string endpointKey)");
        AssertThreeGuards(contracts, "ContractsDialog.RunReview", @"await _reviews\.ReviewAsync\(_task\.Id, endpointKey, reviewState, CancellationToken\.None\);");
        Assert.Matches(@"(?m)^\s*if \(IsDisposed\) return;\s*\n\s*if \(!ReloadContracts\(out var error\)\)", contracts);

        // 差异确认保存：成功守卫在按磁盘重读决策之前。
        var decisions = MethodBody("DecisionsDialog.cs", "private async void SaveDecision(string description, Action<DecisionStore> mutate)", "private static DecisionStore CloneStore(DecisionStore store)");
        AssertThreeGuards(decisions, "DecisionsDialog.SaveDecision", @"await _decisionSaves\.SaveAsync\(_task\.Id, body\);");
        Assert.Matches(@"(?m)^\s*if \(IsDisposed\) return;\s*\n\s*if \(!ReloadDecisions\(out var error\)\)", decisions);

        // 补充保存：成功守卫在回读保存结果并重建编辑位之前。
        var supplements = MethodBody("SupplementsDialog.cs", "private async void SaveCurrent()", "private void SetStatus(string message, Color color)");
        AssertThreeGuards(supplements, "SupplementsDialog.SaveCurrent", @"await _supplementSaves\.SaveAsync\(_task\.Id, body\);");
        Assert.Matches(@"(?m)^\s*if \(IsDisposed\) return;\s*\n\s*_supplement = saved;", supplements);
    }

    [Fact]
    public void ImportResult_RestoresAreGuarded()
    {
        // 导入结果：三条 catch 路径的按钮恢复与同方法的状态行输出同一守卫口径（此前恢复是裸的）。
        var body = MethodBody("CandidatesDialog.cs", "private async void ImportResult()", "private void SetStatus(string message, Color color)");
        Assert.Matches(@"await File\.ReadAllTextAsync\(picker\.FileName, Encoding\.UTF8\);", body);
        Assert.Matches(@"await _drafts\.ImportResultAsync\(_task\.Id, json, CancellationToken\.None\);", body);
        Assert.Equal(3, Regex.Matches(body, @"(?m)^\s*if \(!IsDisposed\) _importButton\.Enabled = true;").Count);
    }

    [Fact]
    public void ConnectionCheckAndExtract_GuardAllContinuations()
    {
        // 连接检查：await 之后立即守卫（打开即检查，秒级 CLI 调用最易撞上关闭），finally 恢复同样守卫。
        var check = MethodBody("CapabilitiesDialog.cs", "private async Task CheckAsync()", "private void ShowReport(CapabilityReport report)");
        AssertThreeGuards(check, "CapabilitiesDialog.CheckAsync",
            @"(?m)^\s*var report = await _compat\.CapabilitiesAsync\(_profile, CancellationToken\.None\);\s*\n\s*if \(IsDisposed\) return;");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _recheckButton\.Enabled = true;", check);
        Assert.DoesNotMatch(@"(?m)^\s*_recheckButton\.Enabled = true;", check);

        // 模板提取：await 之后立即守卫，finally 恢复同样守卫。
        var extract = MethodBody("ProfileAdvancedDialog.cs", "private async Task ExtractAsync()", "<summary>提取结果回填输入框");
        AssertThreeGuards(extract, "ProfileAdvancedDialog.ExtractAsync",
            @"(?m)^\s*var result = await _cli\.GetEndpointAsync\([^\n]*\);\s*\n\s*if \(IsDisposed\) return;");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _extractButton\.Enabled = true;", extract);
        Assert.DoesNotMatch(@"(?m)^\s*_extractButton\.Enabled = true;", extract);
    }

    [Fact]
    public void ProfileDialogLoaders_GuardAllContinuations()
    {
        // 查找 API 项目 / 读取项目列表 / 读取分支：await 之后立即守卫，finally 恢复同样守卫。
        var find = MethodBody("ProjectsPage.cs", "private async Task FindProjectsAsync()", "/// 读取 Apifox 项目列表");
        AssertThreeGuards(find, "ProfileDialog.FindProjectsAsync",
            @"(?m)^\s*var node = await Task\.Run\(\(\) => JsonSerializer\.SerializeToNode\(ProjectFinder\.Find\(root\), JsonDefaults\.Compact\)\);\s*\n\s*if \(IsDisposed\) return;");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _findButton\.Enabled = true;", find);
        Assert.DoesNotMatch(@"(?m)^\s*_findButton\.Enabled = true;", find);

        var projects = MethodBody("ProjectsPage.cs", "private async Task LoadApifoxProjectsAsync()", "/// 读取 Apifox 分支列表");
        AssertThreeGuards(projects, "ProfileDialog.LoadApifoxProjectsAsync",
            @"(?m)^\s*var result = await _cli\.ListProjectsAsync\(saved\.Apifox, CancellationToken\.None\);\s*\n\s*if \(IsDisposed\) return;");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _loadProjectsButton\.Enabled = true;", projects);
        Assert.DoesNotMatch(@"(?m)^\s*_loadProjectsButton\.Enabled = true;", projects);

        var branches = MethodBody("ProjectsPage.cs", "private async Task LoadApifoxBranchesAsync()", "private static void FillCombo(ComboBox combo, IEnumerable<string> items)");
        AssertThreeGuards(branches, "ProfileDialog.LoadApifoxBranchesAsync",
            @"(?m)^\s*var result = await _cli\.ListBranchesAsync\(saved\.Apifox, _projectId\.Text\.Trim\(\), CancellationToken\.None\);\s*\n\s*if \(IsDisposed\) return;");
        Assert.Matches(@"(?m)^\s*if \(!IsDisposed\) _loadBranchesButton\.Enabled = true;", branches);
        Assert.DoesNotMatch(@"(?m)^\s*_loadBranchesButton\.Enabled = true;", branches);
    }

    [Fact]
    public void DesignDoc_RecordsAsyncDisposeGuardRule()
    {
        var section = Repo.DesignSection(8);
        Assert.Contains("异步落点守卫", section);
        Assert.Contains("IsDisposed", section);
        Assert.Contains("九个较早方法", section);
    }
}
