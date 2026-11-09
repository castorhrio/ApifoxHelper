using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 任务详情的三阶段进度条（DESIGN.md §6，第一百一十六轮）：
/// 冻结页面的任务详情顶部有 stepper——三个阶段各带状态徽标、已完成的换成 ✓、当前阶段标出来、
/// 点一下跳到该阶段；原生任务详情此前只有「下一步」横幅，三个阶段各自的状态散在五个分区里，
/// 而详情是可滚动的长内容，「三个阶段各到哪一步」要滚着找，属同壳不同行为。
/// 呈现口径收进 <see cref="StageSteps"/> 单一口径（不依赖 WinForms、与测试工程共享编译，真值表在 Linux 真执行）；
/// 「当前阶段」取 <see cref="NextStep.Step.Stage"/>（与横幅同一份判定，不复制引导规则）；
/// 界面接线由行首正则与源码切片机械检查钉住（被注释掉也算删除）。
/// </summary>
public class StageStepsTests
{
    private static CheckTask Task(Action<CheckTask>? setup = null)
    {
        var task = new CheckTask { Revision = 3 };
        setup?.Invoke(task);
        return task;
    }

    // ---------- 文案与顺序（真执行） ----------

    [Fact]
    public void Labels_AreTheSharedConstants()
    {
        // 三格标签与任务详情三个分区的标题同一份；Apifox 那一格与冻结页面 stepper 逐字一致。
        Assert.Equal("源码注释", StageSteps.SourceLabel);
        Assert.Equal("生成文档", StageSteps.GeneratedLabel);
        Assert.Equal("Apifox", StageSteps.RemoteLabel);
        // 点下去要落到「Apifox 差异与同步」区：两处文字不同，映射只在本文件一处。
        Assert.Equal("Apifox 差异与同步", StageSteps.RemoteSectionLabel);
        // 完成记号与当前后缀与冻结页面同一口径；颜色不作唯一信号，当前这一步由文字说出来。
        Assert.Equal("✓", StageSteps.DoneMark);
        Assert.Equal("（当前）", StageSteps.CurrentSuffix);
        Assert.Equal("点击阶段跳到对应分区。", StageSteps.JumpHint);
    }

    [Fact]
    public void Keys_AreTheThreeStagesInOrder()
    {
        // 阶段顺序与冻结页面 stepper 的三格同序，键取 NextStep 的同一套取值（不另立词汇）。
        Assert.Equal([NextStep.StageSource, NextStep.StageGenerated, NextStep.StageRemote], StageSteps.Keys);
        Assert.Equal(["source", "generated", "remote"], StageSteps.Keys);
    }

    // ---------- 三格进度（真执行） ----------

    [Fact]
    public void Build_GivesThreeStepsInOrder()
    {
        var steps = StageSteps.Build(Task(), NextStep.StageSource);
        Assert.Equal(3, steps.Count);
        Assert.Equal(StageSteps.Keys, steps.Select(step => step.Key));
        Assert.Equal([StageSteps.SourceLabel, StageSteps.GeneratedLabel, StageSteps.RemoteLabel], steps.Select(step => step.Label));
    }

    [Fact]
    public void SourceDone_FollowsTheFrozenStepperRule()
    {
        // 已回写即完成；部分回写与已扫描要求没有缺口（第一百一十八轮收敛为 StageSteps.SourceStageDone 一份，
        // 与「下一步」横幅同一判定——冻结页面把「部分回写」直接算完成，本壳要求它同时没有缺口）。
        Assert.True(StageSteps.Build(Task(t => t.Stages.Source.Status = "applied"), null)[0].Done);
        Assert.True(StageSteps.Build(Task(t => t.Stages.Source.Status = "partiallyApplied"), null)[0].Done);
        // 部分回写但仍有缺口：不算完成（同一屏上横幅也在说「补全注释」）。
        Assert.False(StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "partiallyApplied";
            t.Stages.Source.Slots = 4;
            t.Stages.Source.ValidSlots = 1;
        }), null)[0].Done);
        Assert.True(StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 4;
            t.Stages.Source.ValidSlots = 4;
        }), null)[0].Done);
        // 已扫描但有缺口不算完成；审阅中、计划就绪、未扫描都不算。
        Assert.False(StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 4;
            t.Stages.Source.ValidSlots = 3;
        }), null)[0].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Source.Status = "reviewing"), null)[0].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Source.Status = "planReady"), null)[0].Done);
        Assert.False(StageSteps.Build(Task(), null)[0].Done);
    }

    [Fact]
    public void GeneratedAndRemoteDone_OnlyWhenVerified()
    {
        // 生成文档与 Apifox 两格只在「已核对」时完成（与冻结页面 stepper 同一判定）。
        Assert.True(StageSteps.Build(Task(t => t.Stages.Generated.Status = "verified"), null)[1].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Generated.Status = "partial"), null)[1].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Generated.Status = "pending"), null)[1].Done);
        Assert.True(StageSteps.Build(Task(t => t.Stages.Remote.Status = "verified"), null)[2].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Remote.Status = "previewable"), null)[2].Done);
        Assert.False(StageSteps.Build(Task(t => t.Stages.Remote.Status = "partial"), null)[2].Done);
    }

    [Fact]
    public void StatusText_ComesFromTaskDisplay()
    {
        // 状态文字取 TaskDisplay 的三份中文映射（与三个分区的状态行同一份，不另写一套）。
        var steps = StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "reviewing";
            t.Stages.Generated.Status = "partial";
            t.Stages.Remote.Status = "expired";
        }), null);
        Assert.Equal(TaskDisplay.SourceStatusText("reviewing"), steps[0].StatusText);
        Assert.Equal(TaskDisplay.GeneratedStatusText("partial"), steps[1].StatusText);
        Assert.Equal(TaskDisplay.RemoteStatusText("expired"), steps[2].StatusText);
        Assert.Equal("审阅中", steps[0].StatusText);
        Assert.Equal("部分核对", steps[1].StatusText);
        Assert.Equal("计划已过期", steps[2].StatusText);
    }

    [Fact]
    public void Current_MarksExactlyTheNextStepsStage()
    {
        // 当前那一格由 next.Stage 决定：三格各标一次，且同一时刻只有一格是当前。
        foreach (var stage in StageSteps.Keys)
        {
            var steps = StageSteps.Build(Task(), stage);
            Assert.Single(steps, step => step.Current);
            Assert.Equal(stage, steps.Single(step => step.Current).Key);
        }
    }

    [Fact]
    public void Current_ReportOrUnknownMarksNone()
    {
        // 报告阶段与未知值都不落在三格之内（冻结页面 stepper 也只有三格），此时没有一格是当前。
        foreach (var stage in new[] { NextStep.StageReport, "report", "", null, "unknown" })
        {
            Assert.DoesNotContain(StageSteps.Build(Task(), stage), step => step.Current);
        }
    }

    // ---------- 序号与按钮文字（真执行） ----------

    [Fact]
    public void NumberText_ShowsCheckWhenDone()
    {
        // 已完成的阶段把序号换成 ✓，其余显示第几（1 起）——与冻结页面 step-num 同一口径。
        var steps = StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "applied";
            t.Stages.Generated.Status = "verified";
        }), null);
        Assert.Equal(StageSteps.DoneMark, StageSteps.NumberText(steps[0], 0));
        Assert.Equal(StageSteps.DoneMark, StageSteps.NumberText(steps[1], 1));
        Assert.Equal("3", StageSteps.NumberText(steps[2], 2));
    }

    [Fact]
    public void ButtonText_ComposesNumberLabelStatusAndCurrentSuffix()
    {
        var steps = StageSteps.Build(Task(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 2;
            t.Stages.Source.ValidSlots = 2;
            t.Stages.Generated.Status = "verified";
        }), NextStep.StageRemote);
        // 完成格：✓ + 标签 + 状态；当前格再后缀「（当前）」。
        Assert.Equal("✓ 源码注释 · 已扫描", StageSteps.ButtonText(steps[0], 0));
        Assert.Equal("✓ 生成文档 · 已核对", StageSteps.ButtonText(steps[1], 1));
        Assert.Equal("3 Apifox · 未检查（当前）", StageSteps.ButtonText(steps[2], 2));
    }

    // ---------- 「当前」与「下一步」同一份判定（真执行） ----------

    [Fact]
    public void NextStep_StageMatchesTheFrozenPageForEveryBranch()
    {
        // 每个分支的 Stage 与冻结页面 nextStep 的第一个参数同一取值（本文件不复制引导规则，
        // 只钉住「下一步落在哪个阶段」这一件事）。
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "executing"));
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "interrupted"));
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "awaitingConfirm"));
        // 未检查：源码 → 生成文档 → Apifox 的顺序。
        Assert.Equal(NextStep.StageSource, Stage(_ => { }));
        Assert.Equal(NextStep.StageSource, Stage(t => t.Stages.Source.Status = "failed"));
        Assert.Equal(NextStep.StageSource, Stage(t => t.Stages.Source.Status = "planReady"));
        // 生成文档待核对要排在「源码还没开始」之后：源码已回写才轮到这一格。
        Assert.Equal(NextStep.StageGenerated, Stage(t =>
        {
            t.Stages.Source.Status = "applied";
            t.Stages.Generated.Status = "pending";
        }));
        Assert.Equal(NextStep.StageSource, Stage(t =>
        {
            t.Stages.Source.Status = "reviewing";
            t.Stages.Source.Accepted = 2;
        }));
        Assert.Equal(NextStep.StageSource, Stage(t => t.Stages.Source.Status = "reviewing"));
        Assert.Equal(NextStep.StageSource, Stage(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 3;
            t.Stages.Source.ValidSlots = 1;
        }));
        // 离线任务也要先走完源码阶段：源码已扫描且没有缺口才轮到「导入远端」。
        Assert.Equal(NextStep.StageRemote, Stage(t =>
        {
            t.OfflineMode = true;
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
        }));
        Assert.Equal(NextStep.StageRemote, Stage(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
        }));
        // 已开始检查后：源码侧的回写失败、未应用补丁、未核对生成文档仍然优先。
        Assert.Equal(NextStep.StageSource, Stage(t =>
        {
            t.Stages.Remote.Status = "snapshotReady";
            t.Stages.Source.Status = "failed";
        }));
        Assert.Equal(NextStep.StageSource, Stage(t =>
        {
            t.Stages.Remote.Status = "snapshotReady";
            t.Stages.Source.Status = "planReady";
        }));
        Assert.Equal(NextStep.StageGenerated, Stage(t =>
        {
            t.Stages.Remote.Status = "snapshotReady";
            t.Stages.Generated.Status = "pending";
        }));
        // Apifox 阶段。
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "expired"));
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "pending"));
        Assert.Equal(NextStep.StageRemote, Stage(t =>
        {
            t.Stages.Remote.Status = "previewable";
            t.Stages.Remote.Added = 1;
        }));
        Assert.Equal(NextStep.StageRemote, Stage(t => t.Stages.Remote.Status = "partial"));
        // 报告阶段：不落在三格之内。
        Assert.Equal(NextStep.StageReport, Stage(t => t.Stages.Remote.Status = "offline"));
        Assert.Equal(NextStep.StageReport, Stage(t => t.Stages.Remote.Status = "verified"));
        Assert.Equal(NextStep.StageReport, Stage(t => t.Stages.Remote.Status = "snapshotReady"));

        static string Stage(Action<CheckTask> setup) => NextStep.Compute(Task(setup)).Stage;
    }

    [Fact]
    public void Stepper_ShowsTheCurrentStageOfTheNextStep()
    {
        // 端到端：任务停在「生成文档待核对」时，进度条当前格就是生成文档那一格。
        var task = Task(t =>
        {
            t.Stages.Source.Status = "applied";
            t.Stages.Generated.Status = "pending";
        });
        var next = NextStep.Compute(task);
        var steps = StageSteps.Build(task, next.Stage);
        Assert.Equal(NextStep.StageGenerated, steps.Single(step => step.Current).Key);
        Assert.True(steps[0].Done);
        Assert.False(steps[1].Done);
    }

    // ---------- 界面接线（机械检查） ----------

    [Fact]
    public void TasksPage_ShowsTheStepperAboveTheNextBanner()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var detail = Slice(page, "private TableLayoutPanel BuildDetail()", "private static Label SectionCaption(");
        // 进度条是详情的第一行，横幅紧随其后（冻结页面 stepper 在任务头部、横幅之下同一顺序）。
        var stepper = detail.IndexOf("AddDetailRow(grid, _stepper);", StringComparison.Ordinal);
        var banner = detail.IndexOf("AddDetailRow(grid, _nextHead);", StringComparison.Ordinal);
        Assert.True(stepper >= 0 && banner > stepper, "进度条要排在「下一步」横幅之前。");
    }

    [Fact]
    public void TasksPage_BuildsTheThreeButtonsFromTheSharedKeys()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 三格按钮按共享顺序出厂、经工厂创建、点击只交给 JumpToStage。
        Assert.Matches(@"(?m)^\s*_stepButtons = new Button\[StageSteps\.Keys\.Length\];$", page);
        Assert.Matches(@"(?m)^\s*var key = StageSteps\.Keys\[i\];$", page);
        Assert.Matches(@"(?m)^\s*var stepButton = Ui\.Ghost\(""""\);$", page);
        Assert.Matches(@"(?m)^\s*stepButton\.Click \+= \(_, _\) => JumpToStage\(key\);$", page);
        // 文字取共享口径，界面不就地拼序号/标签/状态/当前后缀。
        Assert.Matches(@"(?m)^\s*_stepButtons\[i\]\.Text = StageSteps\.ButtonText\(steps\[i\], i\);$", page);
        Assert.Matches(@"(?m)^\s*var steps = StageSteps\.Build\(task, next\.Stage\);$", page);
    }

    [Fact]
    public void TasksPage_RefreshesTheStepperFromTheSameNextStep()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var detail = Slice(page, "private void RefreshDetail()", "/// <summary>\n    /// 三阶段进度条");
        // 下一步只算一次：横幅与进度条共用同一份判定（不各算一次，避免两处漂移）。
        Assert.Single(Regex.Matches(detail, @"NextStep\.Compute\(task\)").Cast<Match>());
        Assert.Matches(@"(?m)^\s*var next = NextStep\.Compute\(task\);$", detail);
        Assert.Matches(@"(?m)^\s*RefreshNextBanner\(next\);$", detail);
        Assert.Matches(@"(?m)^\s*RefreshStepper\(task, next\);$", detail);
        // 进度条刷新只在 RefreshStepper 一处。
        Assert.Single(Regex.Matches(page, @"StageSteps\.Build\(").Cast<Match>());
    }

    [Fact]
    public void JumpToStage_OnlyScrollsTheDetail()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var jump = Slice(page, "private void JumpToStage(string key)", "/// <summary>下一步引导横幅");
        // 只把详情滚到那一区的标题：不改选中、不重建、不写数据、不触发任何动作。
        Assert.Matches(@"(?m)^\s*_detailScroll\.AutoScrollPosition = new Point\(0, Math\.Max\(0, section\.Top - UiTokens\.Lg\)\);$", jump);
        Assert.DoesNotContain("Ui.SelectItem", jump, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshDetail", jump, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", jump, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", jump, StringComparison.Ordinal);
        Assert.DoesNotContain("Start", jump, StringComparison.Ordinal);
        // 认不出的键不动作（不猜一个分区）。
        Assert.Matches(@"(?m)^\s*if \(section is null\) return;$", jump);
    }

    [Fact]
    public void TasksPage_DoesNotSpellTheStageTextItself()
    {
        // 标签、完成记号、当前后缀与分区标题只在共享口径一处：界面不就地拼这些字面。
        var code = CodeLines(Repo.Source("Desktop", "Native", "TasksPage.cs"));
        Assert.DoesNotContain("（当前）", code, StringComparison.Ordinal);
        Assert.DoesNotContain("✓", code, StringComparison.Ordinal);
        Assert.DoesNotContain("源码注释", code, StringComparison.Ordinal);
        Assert.DoesNotContain("生成文档", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Apifox 差异与同步", code, StringComparison.Ordinal);
        // 三个分区标题取共享常量（文字不在这里写第二遍）。
        Assert.Contains("SectionCaption(StageSteps.SourceLabel)", code, StringComparison.Ordinal);
        Assert.Contains("SectionCaption(StageSteps.GeneratedLabel)", code, StringComparison.Ordinal);
        Assert.Contains("SectionCaption(StageSteps.RemoteSectionLabel)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void StepperState_IsNotPersisted()
    {
        // 进度条是这一屏的呈现：不落盘、不写任务与配置数据（跨启动记忆只有两个常驻页面）。
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        var stepper = Slice(page, "private void RefreshStepper(", "/// <summary>下一步引导横幅");
        Assert.DoesNotContain("ListSortMemory", stepper, StringComparison.Ordinal);
        Assert.DoesNotContain("FileStore", stepper, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteJson", stepper, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedFileIsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\StageSteps.cs", csproj, StringComparison.Ordinal);
    }

    // ---------- 设计基线回写 ----------

    [Fact]
    public void DesignDoc_RecordsTheStageStepper()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("任务详情的三阶段进度条", section);
        Assert.Contains("`StageSteps`", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("任务详情的三阶段进度条（§6）", ledger);
        Assert.Contains("StageSteps.ButtonText", ledger);
    }

    // ---------- 辅助 ----------

    /// <summary>只算代码行：注释里引用「界面不就地写『（当前）』」是在说明理由，不算第二套文案。</summary>
    private static string CodeLines(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Slice(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {start}。");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to > from, $"源码缺少 {end}。");
        return source[from..to];
    }
}
