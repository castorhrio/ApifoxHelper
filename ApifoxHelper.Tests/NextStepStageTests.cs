using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「源码阶段完成」的交叉断言（第一百一十七轮，纯测试补强，不动产品代码）：
/// <see cref="StageSteps.Build"/> 与 <see cref="NextStep.Compute"/> 各自算
/// <c>Math.Max(0, Slots - ValidSlots)</c> 判源码阶段是否完成，此前没有任何用例把两处钉在一起——
/// 一处改了判定、另一处没跟上，进度条会显示 ✓ 而横幅还在说「补全注释」（或反过来），
/// 而这两处正是同一屏上下相邻的两块。
/// <para>
/// 这里用同一批 <see cref="CheckTask"/> 状态跑真值表：源码阶段没有其他待办时，
/// 「进度条第一格已完成」与「下一步不在源码阶段」必须同真同假。
/// 另有一条用例把本轮实测发现的**已知漂移**钉住（见 <see cref="KnownDivergence_PartiallyAppliedWithRemainingGaps"/>），
/// 它一旦被修好就会变红，提醒把那条用例改成一致断言。
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
    /// 已知漂移（本轮实测发现，如实钉住，不在本轮改产品代码）：
    /// 源码阶段「部分回写」且仍有缺口时，<see cref="StageSteps"/> 按冻结页面 stepper 的规则判第一格完成
    /// （<c>partiallyApplied</c> 即完成），而 <see cref="NextStep"/> 走到 <c>gaps &gt; 0</c> 那一支，
    /// 仍引导「补全注释」并落在源码阶段——同一屏上进度条打 ✓、横幅说还有缺口。
    /// 这条用例钉住现状：修好任一侧都会变红，届时把它改成一致断言。
    /// </summary>
    [Fact]
    public void KnownDivergence_PartiallyAppliedWithRemainingGaps()
    {
        var task = Task(t =>
        {
            t.Stages.Source.Status = "partiallyApplied";
            t.Stages.Source.Slots = 3;
            t.Stages.Source.ValidSlots = 1;
        });
        var next = NextStep.Compute(task);
        Assert.True(StageSteps.Build(task, next.Stage)[0].Done);
        Assert.Equal(NextStep.StageSource, next.Stage);
        Assert.Equal("补全注释", next.Title);
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
        };
        foreach (var setup in states)
        {
            var task = Task(setup);
            var next = NextStep.Compute(task);
            Assert.False(StageSteps.Build(task, next.Stage)[0].Done);
            Assert.Equal(NextStep.StageSource, next.Stage);
        }
    }
}
