using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「源码阶段完成」的交叉断言（第一百一十七轮建立，第一百一十八轮收敛为单一判定）：
/// <see cref="StageSteps.Build"/> 与 <see cref="NextStep.Compute"/> 曾各自算
/// <c>Math.Max(0, Slots - ValidSlots)</c> 判源码阶段是否完成，一处改了判定、另一处没跟上，
/// 进度条会显示 ✓ 而横幅还在说「补全注释」（或反过来），而这两处正是同一屏上下相邻的两块。
/// <para>
/// 第一百一十八轮把判定收进 <see cref="StageSteps.SourceStageDone"/> 与
/// <see cref="StageSteps.SourceGaps"/> 一份，两处都调它；本文件因此从「钉住漂移」改为「一致断言」：
/// 真值表逐行断言「进度条第一格已完成」与「下一步不在源码阶段」同真同假，
/// 另有一条机械检查钉住「缺口表达式只有一处」。
/// </para>
/// </summary>
public class NextStepStageTests
{
    private static CheckTask Task(Action<CheckTask>? setup = null)
    {
        var task = new CheckTask { Revision = 3 };
        setup?.Invoke(task);
        return task;
    }

    /// <summary>源码阶段已扫描且没有缺口（进度条第一格完成的最小状态）。</summary>
    private static void ScannedClean(CheckTask task)
    {
        task.Stages.Source.Status = "scanned";
        task.Stages.Source.Slots = 3;
        task.Stages.Source.ValidSlots = 3;
    }

    /// <summary>源码阶段已扫描但还有缺口。</summary>
    private static void ScannedWithGaps(CheckTask task)
    {
        task.Stages.Source.Status = "scanned";
        task.Stages.Source.Slots = 3;
        task.Stages.Source.ValidSlots = 1;
    }

    /// <summary>真值表：每一行是一个任务状态，两处判定必须一致。</summary>
    public static TheoryData<string, Action<CheckTask>> States() => new()
    {
        // 源码阶段还没走完：进度条第一格未完成，下一步也在源码阶段。
        { "未开始", _ => { } },
        { "扫描失败", t => t.Stages.Source.Status = "failed" },
        { "计划就绪", t => t.Stages.Source.Status = "planReady" },
        { "审阅中", t => t.Stages.Source.Status = "reviewing" },
        { "审阅中且有已接受候选", t =>
            {
                t.Stages.Source.Status = "reviewing";
                t.Stages.Source.Accepted = 2;
            } },
        { "已扫描但有缺口", ScannedWithGaps },
        // 第一百一十八轮收敛的那一行：部分回写且仍有缺口，两处必须同判未完成。
        { "部分回写且有缺口", t =>
            {
                t.Stages.Source.Status = "partiallyApplied";
                t.Stages.Source.Slots = 3;
                t.Stages.Source.ValidSlots = 1;
            } },
        // 已开始检查 Apifox 后，源码侧的回写失败与未应用补丁仍然优先。
        { "回写失败且已检查", t =>
            {
                t.Stages.Remote.Status = "snapshotReady";
                t.Stages.Source.Status = "failed";
            } },
        { "计划就绪且已检查", t =>
            {
                t.Stages.Remote.Status = "snapshotReady";
                t.Stages.Source.Status = "planReady";
            } },
        // 源码阶段已完成：进度条第一格完成，下一步不在源码阶段。
        { "已扫描且无缺口", ScannedClean },
        { "已回写", t => t.Stages.Source.Status = "applied" },
        { "部分回写且无缺口", t =>
            {
                t.Stages.Source.Status = "partiallyApplied";
                t.Stages.Source.Slots = 3;
                t.Stages.Source.ValidSlots = 3;
            } },
        // 已回写是源码阶段的终态：扫描留下的缺口数不再参与判定，两处仍须一致（都判完成）。
        { "已回写但扫描数据留着缺口", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Source.Slots = 3;
                t.Stages.Source.ValidSlots = 1;
            } },
        { "已回写且生成文档待核对", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Generated.Status = "pending";
            } },
        { "已扫描无缺口且生成文档待核对", t =>
            {
                ScannedClean(t);
                t.Stages.Generated.Status = "pending";
            } },
        { "已回写且远端待确认", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "pending";
            } },
        { "已回写且远端可预览", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "previewable";
                t.Stages.Remote.Added = 1;
            } },
        { "已回写且远端过期", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "expired";
            } },
        { "已回写且远端部分完成", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "partial";
            } },
        { "已回写且远端离线", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "offline";
            } },
        { "已回写且远端已核对", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "verified";
            } },
        { "已回写且远端快照就绪", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "snapshotReady";
            } },
        // 远端执行中的三个状态最紧迫，先于源码阶段引导；源码阶段此时已完成，两处仍一致。
        { "已回写且同步执行中", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "executing";
            } },
        { "已回写且同步被中断", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "interrupted";
            } },
        { "已回写且等待确认同步", t =>
            {
                t.Stages.Source.Status = "applied";
                t.Stages.Remote.Status = "awaitingConfirm";
            } },
    };

    [Theory]
    [MemberData(nameof(States))]
    public void SourceStageDone_AgreesWithTheNextStepStage(string name, Action<CheckTask> setup)
    {
        var task = Task(setup);
        var next = NextStep.Compute(task);
        var stepperDone = StageSteps.Build(task, next.Stage)[0].Done;
        var nextInSource = next.Stage == NextStep.StageSource;
        Assert.True(stepperDone != nextInSource,
            $"{name}：进度条第一格 Done={stepperDone}，下一步 Stage={next.Stage}（「{next.Title}」）——两处判定漂移。");
    }

    /// <summary>
    /// 第一百一十七轮实测抓到的那处漂移，第一百一十八轮已收敛：源码阶段「部分回写」且仍有缺口时，
    /// 进度条第一格与「下一步」横幅必须**同判未完成**（此前进度条按冻结页面规则打 ✓、横幅说「补全注释」）。
    /// 这条用例从「钉住漂移」改为「一致断言」——任一侧退回旧判定都会变红。
    /// </summary>
    [Fact]
    public void PartiallyAppliedWithRemainingGaps_BothSidesSayNotDone()
    {
        var task = Task(t =>
        {
            t.Stages.Source.Status = "partiallyApplied";
            t.Stages.Source.Slots = 3;
            t.Stages.Source.ValidSlots = 1;
        });
        var next = NextStep.Compute(task);
        Assert.False(StageSteps.Build(task, next.Stage)[0].Done);
        Assert.Equal(NextStep.StageSource, next.Stage);
        Assert.Equal("补全注释", next.Title);
        // 引导文字里的缺口数与判定取同一份（2 = 3 - 1）。
        Assert.Equal(2, StageSteps.SourceGaps(task));
        Assert.Contains("2 个位置缺少有效说明", next.Detail);
    }

    /// <summary>
    /// 反向核对：进度条第一格未完成时，下一步必须落在源码阶段（否则横幅会把用户引到别的阶段，
    /// 而源码阶段还有事没做完）。这一条与上面的真值表互为镜像，防止只钉一个方向。
    /// </summary>
    [Fact]
    public void SourceStageNotDone_KeepsTheNextStepInTheSourceStage()
    {
        var states = new Action<CheckTask>[]
        {
            _ => { },
            t => t.Stages.Source.Status = "failed",
            t => t.Stages.Source.Status = "planReady",
            t => t.Stages.Source.Status = "reviewing",
            ScannedWithGaps,
            t =>
            {
                t.Stages.Source.Status = "partiallyApplied";
                t.Stages.Source.Slots = 3;
                t.Stages.Source.ValidSlots = 1;
            },
        };
        foreach (var setup in states)
        {
            var task = Task(setup);
            var next = NextStep.Compute(task);
            Assert.False(StageSteps.Build(task, next.Stage)[0].Done);
            Assert.Equal(NextStep.StageSource, next.Stage);
        }
    }

    // ---------- 单一来源（机械检查） ----------

    /// <summary>
    /// 缺口表达式（<c>Slots - ValidSlots</c>）在 Desktop/Native 下只允许出现在 <c>StageSteps.cs</c> 一处：
    /// 两处各算一遍正是第一百一十七轮那处漂移的成因，收成一份之后由这条用例守住。
    /// </summary>
    [Fact]
    public void GapExpression_LivesOnlyInTheSharedEntry()
    {
        var hits = Repo.NativeSources()
            .SelectMany(source => source.Matches(@"Slots\s*-\s*\w*\.?ValidSlots").Select(m => source.Name))
            .ToList();
        Assert.Equal(["StageSteps.cs"], hits);
    }

    /// <summary>
    /// 两处都调共享判定：进度条第一格与「下一步」横幅不得自己写完成判定
    /// （<c>StageSteps.Build</c> 调 <c>SourceStageDone</c>，<c>NextStep.Compute</c> 调
    /// <c>SourceStageDone</c> 与 <c>SourceGaps</c>，且「补全注释」那一支被共享判定门控）。
    /// </summary>
    [Fact]
    public void BothSidesCallTheSharedEntry()
    {
        var stageSteps = Repo.Source("Desktop", "Native", "StageSteps.cs");
        var nextStep = Repo.Source("Desktop", "Native", "NextStep.cs");

        // 进度条：第一格取共享判定，不就地写状态字面。
        Assert.Matches(@"var sourceDone = SourceStageDone\(task\);", stageSteps);
        Assert.DoesNotMatch(@"source\.Status is ""applied"" or ""partiallyApplied""", stageSteps);

        // 横幅：缺口数与完成判定都取共享口径。
        Assert.Matches(@"var gaps = StageSteps\.SourceGaps\(task\);", nextStep);
        Assert.Matches(@"var sourceDone = StageSteps\.SourceStageDone\(task\);", nextStep);
        Assert.Matches(@"if \(!sourceDone && gaps > 0\)", nextStep);
        // 横幅不再自己算缺口、不再自己写完成判定的状态字面。
        Assert.DoesNotMatch(@"Math\.Max\(0, s\.Slots", nextStep);
        Assert.DoesNotMatch(@"s\.Status is ""applied"" or ""partiallyApplied""", nextStep);
    }
}
