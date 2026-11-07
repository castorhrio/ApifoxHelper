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
    public static IEnumerable<object[]> EvidenceKinds() => Statuses("Source/SourceModels.cs", "route / binding / validation");
    public static IEnumerable<object[]> ReviewStates() => Statuses("Comments/DraftModels.cs", "pending / accepted / deferred");
    public static IEnumerable<object[]> CandidateBases() => Statuses("Comments/DraftModels.cs", "code / needsConfirmation");
    public static IEnumerable<object[]> CandidateSources() => Statuses("Comments/DraftModels.cs", "rule / model / file / manual");
    public static IEnumerable<object[]> GenerationRunStatuses() => Statuses("Comments/DraftModels.cs", "succeeded / partial");
    public static IEnumerable<object[]> PlanStatuses() => Statuses("Comments/CommentPlanService.cs", "ready / applying / applied");
    public static IEnumerable<object[]> FileApplyStates() => Statuses("Comments/CommentPlanService.cs", "applied / skipped / failed");
    public static IEnumerable<object[]> PropagationSourceStates() => Statuses("Comments/PropagationService.cs", "written / notWritten");
    public static IEnumerable<object[]> PropagationXmlStates() => Statuses("Comments/PropagationService.cs", "present / different / missing / noXml");
    public static IEnumerable<object[]> PropagationSwaggerStates() => Statuses("Comments/PropagationService.cs", "present / different / missing / notRendered");
    public static IEnumerable<object[]> ContractReviewStates() => Statuses("Source/SourceModels.cs", "pending / confirmed / rejected / blocked");
    public static IEnumerable<object[]> ContractDataKinds() => Statuses("Source/SourceModels.cs", "type / null / none / anonymous");

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

    [Theory]
    [MemberData(nameof(EvidenceKinds))]
    public void EvidenceKind_HasChineseText(string kind) => Assert.NotEqual(kind, TaskDisplay.EvidenceKindText(kind));

    [Theory]
    [MemberData(nameof(ReviewStates))]
    public void ReviewState_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.ReviewStateText(state));
        Assert.Contains(TaskDisplay.ReviewTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(CandidateBases))]
    public void CandidateBasis_HasChineseText(string basis) => Assert.NotEqual(basis, TaskDisplay.CandidateBasisText(basis));

    [Theory]
    [MemberData(nameof(CandidateSources))]
    public void CandidateSource_HasChineseText(string source) => Assert.NotEqual(source, TaskDisplay.CandidateSourceText(source));

    [Theory]
    [MemberData(nameof(GenerationRunStatuses))]
    public void GenerationRunStatus_HasChineseText(string status) => Assert.NotEqual(status, TaskDisplay.RunStatusText(status));

    [Theory]
    [MemberData(nameof(PlanStatuses))]
    public void PlanStatus_HasChineseTextAndTone(string status)
    {
        Assert.NotEqual(status, TaskDisplay.PlanStatusText(status));
        Assert.Contains(TaskDisplay.PlanTone(status), Tones);
    }

    [Theory]
    [MemberData(nameof(FileApplyStates))]
    public void FileApplyState_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.FileApplyStateText(state));
        Assert.Contains(TaskDisplay.FileApplyTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(PropagationSourceStates))]
    public void PropagationSource_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.PropagationSourceText(state));
        Assert.Contains(TaskDisplay.PropagationSourceTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(PropagationXmlStates))]
    public void PropagationXml_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.PropagationXmlText(state));
        Assert.Contains(TaskDisplay.PropagationXmlTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(PropagationSwaggerStates))]
    public void PropagationSwagger_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.PropagationSwaggerText(state));
        Assert.Contains(TaskDisplay.PropagationSwaggerTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(ContractReviewStates))]
    public void ContractReviewState_HasChineseTextAndTone(string state)
    {
        Assert.NotEqual(state, TaskDisplay.ContractStateText(state));
        Assert.Contains(TaskDisplay.ContractTone(state), Tones);
    }

    [Theory]
    [MemberData(nameof(ContractDataKinds))]
    public void ContractDataKind_HasChineseTextAndTone(string kind)
    {
        Assert.NotEqual(kind, TaskDisplay.ContractDataKindText(kind));
        Assert.Contains(TaskDisplay.ContractDataKindTone(kind), Tones);
    }

    [Fact]
    public void UnknownStatus_FallsBackToRawValue()
    {
        // 回退契约：未知状态原样显示，界面不编造结果。
        Assert.Equal("mystery", TaskDisplay.SourceStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.RemoteStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.SlotStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.EvidenceKindText("mystery"));
        Assert.Equal("mystery", TaskDisplay.ReviewStateText("mystery"));
        Assert.Equal("mystery", TaskDisplay.CandidateBasisText("mystery"));
        Assert.Equal("mystery", TaskDisplay.CandidateSourceText("mystery"));
        Assert.Equal("mystery", TaskDisplay.RunStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.PlanStatusText("mystery"));
        Assert.Equal("mystery", TaskDisplay.FileApplyStateText("mystery"));
        Assert.Equal("mystery", TaskDisplay.PropagationSourceText("mystery"));
        Assert.Equal("mystery", TaskDisplay.PropagationXmlText("mystery"));
        Assert.Equal("mystery", TaskDisplay.PropagationSwaggerText("mystery"));
        Assert.Equal("mystery", TaskDisplay.ContractStateText("mystery"));
        Assert.Equal("mystery", TaskDisplay.ContractDataKindText("mystery"));
        Assert.Equal("running", TaskDisplay.JobStateText("running"));
        Assert.Equal("normal", TaskDisplay.SlotTone("mystery"));
        Assert.Equal("normal", TaskDisplay.ReviewTone("mystery"));
        Assert.Equal("normal", TaskDisplay.PlanTone("mystery"));
        Assert.Equal("normal", TaskDisplay.FileApplyTone("mystery"));
        Assert.Equal("normal", TaskDisplay.PropagationSourceTone("mystery"));
        Assert.Equal("normal", TaskDisplay.PropagationXmlTone("mystery"));
        Assert.Equal("normal", TaskDisplay.PropagationSwaggerTone("mystery"));
        Assert.Equal("normal", TaskDisplay.ContractTone("mystery"));
        Assert.Equal("normal", TaskDisplay.ContractDataKindTone("mystery"));
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
            .Concat(Statuses("Source/SourceModels.cs", "route / binding / validation").Select(o => TaskDisplay.EvidenceKindText((string)o[0])))
            .Concat(Statuses("Comments/DraftModels.cs", "pending / accepted / deferred").Select(o => TaskDisplay.ReviewStateText((string)o[0])))
            .Concat(Statuses("Comments/DraftModels.cs", "code / needsConfirmation").Select(o => TaskDisplay.CandidateBasisText((string)o[0])))
            .Concat(Statuses("Comments/DraftModels.cs", "rule / model / file / manual").Select(o => TaskDisplay.CandidateSourceText((string)o[0])))
            .Concat(Statuses("Comments/DraftModels.cs", "succeeded / partial").Select(o => TaskDisplay.RunStatusText((string)o[0])))
            .Concat(Statuses("Comments/CommentPlanService.cs", "ready / applying / applied").Select(o => TaskDisplay.PlanStatusText((string)o[0])))
            .Concat(Statuses("Comments/CommentPlanService.cs", "applied / skipped / failed").Select(o => TaskDisplay.FileApplyStateText((string)o[0])))
            .Concat(Statuses("Comments/PropagationService.cs", "written / notWritten").Select(o => TaskDisplay.PropagationSourceText((string)o[0])))
            .Concat(Statuses("Comments/PropagationService.cs", "present / different / missing / noXml").Select(o => TaskDisplay.PropagationXmlText((string)o[0])))
            .Concat(Statuses("Comments/PropagationService.cs", "present / different / missing / notRendered").Select(o => TaskDisplay.PropagationSwaggerText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "pending / confirmed / rejected / blocked").Select(o => TaskDisplay.ContractStateText((string)o[0])))
            .Concat(Statuses("Source/SourceModels.cs", "type / null / none / anonymous").Select(o => TaskDisplay.ContractDataKindText((string)o[0])))
            .Concat(["成功", "失败", "已取消"]);
        foreach (var text in outputs)
        {
            Assert.False(Regex.IsMatch(text, "赋能|闭环|一站式|无缝|智能"), $"文案出现禁词：{text}");
        }
    }

    /// <summary>
    /// 从数据模型的注释行提取状态/枚举列表（"/// a / b / c" → ["a","b","c"]）。
    /// 两端标记先剥掉：列表第一个值（紧贴 &lt;summary&gt;）与最后一个值（紧贴 &lt;/summary&gt;）同样要被断言到。
    /// </summary>
    private static IEnumerable<object[]> Statuses(string relative, string marker)
    {
        var line = Repo.Source(relative.Split('/'))
            .Split('\n')
            .Single(l => l.Contains(marker, StringComparison.Ordinal));
        var tokens = line[(line.IndexOf("///", StringComparison.Ordinal) + 3)..]
            .Replace("<summary>", "")
            .Replace("</summary>", "")
            .Split('/')
            .Select(t => t.Trim())
            .Where(t => Regex.IsMatch(t, "^[a-z][A-Za-z]+$"))
            .ToList();
        Assert.True(tokens.Count > 0, $"在 {relative} 未从「{marker}」注释解析出状态列表。");
        return tokens.Select(t => new object[] { t });
    }
}
