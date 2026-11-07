using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 状态文案完备断言（设计验收 D3/D9/D11 的机械面）：
/// 数据模型注释里声明的每个状态/枚举值（TaskModels.cs、SourceModels.cs），TaskDisplay 必须有中文映射与语义色调；
/// 未知值按约定原样回退（界面如实显示原始值，不冒充已知状态）；所有输出文案不含空话禁词。
/// TaskDisplay 以共享源码编入本工程，断言直接作用于真实实现。
/// </summary>
public class TaskDisplayTests
{
    private static readonly string[] Tones = ["ok", "warn", "bad", "accent", "muted", "normal"];

    public static IEnumerable<object[]> SourceStatuses() => Statuses("Tasks/TaskModels.cs", "notStarted / scanned");
    public static IEnumerable<object[]> GeneratedStatuses() => Statuses("Tasks/TaskModels.cs", "notChecked / pending / verified");
    public static IEnumerable<object[]> RemoteStatuses() => Statuses("Tasks/TaskModels.cs", "notChecked / snapshotReady");
    public static IEnumerable<object[]> SlotStatuses() => Statuses("Source/SourceModels.cs", "valid / missing / empty");
    public static IEnumerable<object[]> SlotTags() => Statuses("Source/SourceModels.cs", "summary / param / returns");
    public static IEnumerable<object[]> SlotRoles() => Statuses("Source/SourceModels.cs", "endpoint / request / response");
    public static IEnumerable<object[]> SymbolKinds() => Statuses("Source/SourceModels.cs", "method / type / property");

    [Theory]
    [MemberData(nameof(SourceStatuses))]
    public void SourceStatus_HasChineseText(string status)
    {
        Assert.NotEqual(status, TaskDisplay.SourceStatusText(status));
        Assert.Contains(TaskDisplay.SourceTone(status), Tones);
    }

    [Theory]
    [MemberData(nameof(GeneratedStatuses))]
    public void GeneratedStatus_HasChineseText(string status)
    {
        Assert.NotEqual(status, TaskDisplay.GeneratedStatusText(status));
        Assert.Contains(TaskDisplay.GeneratedTone(status), Tones);
    }

    [Theory]
    [MemberData(nameof(RemoteStatuses))]
    public void RemoteStatus_HasChineseText(string status)
    {
        Assert.NotEqual(status, TaskDisplay.RemoteStatusText(status));
        Assert.Contains(TaskDisplay.RemoteTone(status), Tones);
    }

    [Theory]
    [MemberData(nameof(SlotStatuses))]
    public void SlotStatus_HasChineseTextAndTone(string status)
    {
        Assert.NotEqual(status, TaskDisplay.SlotStatusText(status));
        Assert.Contains(TaskDisplay.SlotTone(status), Tones);
    }

    [Theory]
    [MemberData(nameof(SlotTags))]
    public void SlotTag_HasChineseText(string tag) => Assert.NotEqual(tag, TaskDisplay.SlotTagText(tag));

    [Theory]
    [MemberData(nameof(SlotRoles))]
    public void SlotRole_HasChineseText(string role) => Assert.NotEqual(role, TaskDisplay.SlotRoleText(role));

    [Theory]
    [MemberData(nameof(SymbolKinds))]
    public void SymbolKind_HasChineseText(string kind) => Assert.NotEqual(kind, TaskDisplay.SymbolKindText(kind));

    [Fact]
    public void UnknownStatus_FallsBackToRawValue()
    {
        // 回退契约：未知状态原样显示，界面不编造结果。
        Assert.Equal("mystery", TaskDisplay.SourceStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.RemoteStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.SlotStatusText("mystery"));
        Assert.Equal("running", TaskDisplay.JobStateText("running"));
        Assert.Equal("normal", TaskDisplay.SlotTone("mystery"));
    }

    [Fact]
    public void JobEndStates_HaveChineseText()
    {
        Assert.Equal("成功", TaskDisplay.JobStateText("succeeded"));
        Assert.Equal("失败", TaskDisplay.JobStateText("failed"));
        Assert.Equal("已取消", TaskDisplay.JobStateText("canceled"));
    }

    /// <summary>D11：映射输出的用户可见文案不含空话禁词。</summary>
    [Fact]
    public void DisplayTexts_AvoidBannedPhrases()
    {
        var outputs =
            Statuses("Tasks/TaskModels.cs", "notStarted / scanned").Select(o => TaskDisplay.SourceStatusText((string)o[0]))
            .Concat(Statuses("Tasks/TaskModels.cs", "notChecked / pending / verified").Select(o => TaskDisplay.GeneratedStatusText((string)o[0])))
            .Concat(Statuses("Tasks/TaskModels.cs", "notChecked / snapshotReady").Select(o => TaskDisplay.RemoteStatusText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "valid / missing / empty").Select(o => TaskDisplay.SlotStatusText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "summary / param / returns").Select(o => TaskDisplay.SlotTagText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "endpoint / request / response").Select(o => TaskDisplay.SlotRoleText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "method / type / property").Select(o => TaskDisplay.SymbolKindText((string)o[0])))
            .Concat(["成功", "失败", "已取消"]);
        foreach (var text in outputs)
        {
            Assert.False(Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }

    /// <summary>从数据模型的注释行提取状态/枚举列表（"/// a / b / c" → ["a","b","c"]）。</summary>
    private static IEnumerable<object[]> Statuses(string relative, string marker)
    {
        var line = Repo.Source(relative.Split('/'))
            .Split('\n')
            .Single(l => l.Contains(marker, StringComparison.Ordinal));
        var tokens = line[(line.IndexOf("///", StringComparison.Ordinal) + 3)..]
            .Split('/')
            .Select(t => t.Trim())
            .Where(t => Regex.IsMatch(t, "^[a-z][A-Za-z]+$"))
            .ToList();
        Assert.True(tokens.Count > 0, $"在 {relative} 未从「{marker}」注释解析出状态列表。");
        return tokens.Select(t => new object[] { t });
    }
}
