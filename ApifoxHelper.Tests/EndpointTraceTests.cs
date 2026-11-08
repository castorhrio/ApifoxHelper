using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 接口追踪明细切片（F03 收尾，具名 pass：clarify）：evidence.json 的逐接口 Coverage
/// （调用链读取的方法、未解析调用、外部调用、单接口方法预算是否超限）此前在任何界面都不可见——
/// 预算超限是逐接口的分析降级，用户看到「类型未确定/证据缺失」却无法解释原因。
/// 口径经共享的 EndpointTrace 单一落点；入口与对话框接线由行首正则机械检查钉住
/// （被注释掉也算删除）；真机观感随 G-D5 复核。
/// </summary>
public class EndpointTraceTests
{
    [Fact]
    public void CountCell_ShowsDashForZeroAndNumberOtherwise()
    {
        Assert.Equal("—", EndpointTrace.CountText(0));
        Assert.Equal("3", EndpointTrace.CountText(3));
    }

    [Fact]
    public void BudgetCell_WarnsOnlyWhenExceeded()
    {
        // 颜色 + 文字双通道：超限行有「超限」文字与警示色调；正常显示「—」。
        Assert.Equal(("超限", "warn"), EndpointTrace.BudgetCell(exceeded: true));
        Assert.Equal(("—", "muted"), EndpointTrace.BudgetCell(exceeded: false));
    }

    [Fact]
    public void DetailLines_RendersAllSections()
    {
        var lines = EndpointTrace.DetailLines(
            "GET /api/orders",
            ["GetOrders", "QueryBuilder.Build"],
            [new EndpointTrace.UnresolvedLine("mapper.Map(src)", "重载无法唯一确定", ["Map<T>(T)", "Map(object)"])],
            ["AutoMapper.Mapper.Map"],
            budgetExceeded: true,
            evidenceCount: 7);
        Assert.Equal("接口：GET /api/orders", lines[0]);
        // 预算超限说明必须出现且给出调整入口（解释「为什么追踪不完整」与下一步）。
        Assert.Contains(lines, l => l.Contains("单接口分析预算用尽") && l.Contains("「方法预算」"));
        Assert.Contains(lines, l => l.StartsWith("读取的方法（2）：") && l.Contains("GetOrders、QueryBuilder.Build"));
        Assert.Contains(lines, l => l.StartsWith("未解析的调用（1）："));
        Assert.Contains(lines, l => l == "· mapper.Map(src)：重载无法唯一确定（候选：Map<T>(T) / Map(object)）");
        Assert.Contains(lines, l => l.StartsWith("外部调用（外部类型，不可回写）：") && l.Contains("AutoMapper.Mapper.Map"));
        Assert.Contains(lines, l => l.StartsWith("引用证据：7 条"));
    }

    [Fact]
    public void DetailLines_OmitsEmptySectionsAndExplainsNoMethods()
    {
        var lines = EndpointTrace.DetailLines("POST /api/ping", [], [], [], budgetExceeded: false, evidenceCount: 0);
        Assert.DoesNotContain(lines, l => l.Contains("预算用尽"));
        Assert.DoesNotContain(lines, l => l.Contains("未解析的调用"));
        Assert.DoesNotContain(lines, l => l.Contains("外部调用"));
        Assert.Contains(lines, l => l.StartsWith("没有读取到方法体"));
        Assert.Contains(lines, l => l.StartsWith("引用证据：无"));
    }

    [Fact]
    public void ScanResultsDialog_WiresTraceEntry()
    {
        var source = Repo.Source("Desktop", "Native", "ScanResultsDialog.cs");
        // 入口经工厂出厂（无就地发明控件样式），扫描结果可读后显示、空态隐藏。
        Assert.Matches(@"(?m)^\s*_traceButton = Ui\.Ghost\(""接口追踪明细""\);", source);
        Assert.Matches(@"(?m)^\s*_traceButton\.Click \+= \(_, _\) => ShowTrace\(\);", source);
        Assert.Matches(@"(?m)^\s*_traceButton\.Visible = true;", source);
        Assert.Matches(@"(?m)^\s*_traceButton\.Visible = false;", source);
        Assert.Matches(@"(?m)^\s*private void ShowTrace\(\)", source);
        Assert.Matches(@"(?m)^\s*using var dialog = new EndpointTraceDialog\(_tasks, _task\);", source);
    }

    [Fact]
    public void TraceDialog_UsesFactoriesAndRowScopedEvidenceGate()
    {
        var source = Repo.Source("Desktop", "Native", "EndpointTraceDialog.cs");
        // 列表与只读框只能经工厂出厂（G-D2/D8 接线纪律，与全界面同一口径）。
        Assert.Matches(@"(?m)^\s*private readonly ListView _list = Ui\.List\(\);", source);
        Assert.Matches(@"(?m)^\s*private readonly TextBox _detail = Ui\.Preview\(", source);
        Assert.Matches(@"(?m)^\s*_evidenceButton = Ui\.Ghost\(""查看代码证据""\);", source);
        // 空选中：详情区不再留白——有行可选却没选中时「缺什么」由这一屏的详情区说（§7「禁用 = 控件禁用 + 一句话说明缺什么」），
        // 一行都没有可选时留空，那一屏由提示出口说话；证据按钮仍随选中重新推导，不残留上一行的状态。
        var branch = Regex.Match(source, @"private void ShowDetail\(\)\s*\{(?<body>[^}]*)\}");
        Assert.True(branch.Success, "接口追踪明细缺少 ShowDetail 的无选中分支。");
        Assert.Matches(
            @"(?m)^\s*_detail\.Text = ListSelection\.ShouldExplainMissingSelection\(_list\.Items\.Count\)\r?\n" +
            @"\s*\? ListSelection\.MissingSelectionHint\(ListFilter\.EndpointNoun\)\r?\n" +
            @"\s*: """";\r?\n\s*_evidenceButton\.Enabled = false;\r?\n\s*return;$",
            branch.Groups["body"].Value);
        Assert.Matches(@"(?m)^\s*_evidenceButton\.Enabled = coverage\.EvidenceIds\.Count > 0;", source);
        Assert.DoesNotMatch(@"(?m)^\s*_evidenceButton\.Enabled = true;", source);
    }

    [Fact]
    public void TraceCalibration_IsSharedWithTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\EndpointTrace.cs", csproj);
    }

    [Fact]
    public void DesignDoc_DeclaresTraceDetailEntry()
    {
        // 实现差距回写：降级明细的呈现口径落在设计基线 §8，删除条款即失去单一来源。
        Assert.Contains("接口追踪明细", Repo.DesignDocText, StringComparison.Ordinal);
    }
}
