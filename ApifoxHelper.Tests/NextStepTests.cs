using ApifoxHelper.Desktop.Native;
using ApifoxHelper.Tasks;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「下一步」引导口径（NextStep 单一落点，与冻结页面 nextStep 同一规则同一文案）：
/// 远端紧急态最优先；未检查时按源码 → 生成文档 → Apifox 顺序引导；已检查后源码侧回写失败、
/// 未应用补丁、未核对生成文档仍然优先；检查过期（含 CheckedRevision 落后于 Revision）按重新检查处理。
/// 断言对真实 CheckTask 类型成立。
/// </summary>
public class NextStepTests
{
    private static CheckTask Task(Action<CheckTask>? setup = null)
    {
        var task = new CheckTask { Revision = 3 };
        setup?.Invoke(task);
        return task;
    }

    private static CheckTask Online(Action<CheckTask>? setup = null) =>
        Task(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "none";
            setup?.Invoke(t);
        });

    // ---------- 引导顺序：远端紧急态最优先 ----------

    [Fact]
    public void Executing_ComesFirst_RegardlessOfOtherStages()
    {
        var step = NextStep.Compute(Task(t =>
        {
            t.Stages.Source.Status = "notStarted";
            t.Stages.Remote.Status = "executing";
        }));
        Assert.Equal("同步执行中", step.Title);
        Assert.Equal("正在逐个写入并回读核对；取消会在当前动作完成后停止。", step.Detail);
        Assert.Equal("查看同步", step.PrimaryLabel);
        Assert.Equal("openSync", step.PrimaryAction);
    }

    [Fact]
    public void Interrupted_ToneBad_MessagePassthrough()
    {
        var step = NextStep.Compute(Task(t => t.Stages.Remote.Status = "interrupted"));
        Assert.Equal("上次同步被中断", step.Title);
        Assert.Equal("请重新核对远端实际状态。", step.Detail);
        Assert.Equal("bad", step.Tone);
        Assert.Equal("去重新核对", step.PrimaryLabel);
        // 服务给出的中断说明原样带出，不替换成通用文案。
        Assert.Equal("部分完成：模型已写入，接口写入失败。", NextStep.Compute(Task(t =>
        {
            t.Stages.Remote.Status = "interrupted";
            t.Stages.Remote.Message = "部分完成：模型已写入，接口写入失败。";
        })).Detail);
    }

    [Fact]
    public void AwaitingConfirm_PointsToSyncPreview()
    {
        var step = NextStep.Compute(Task(t => t.Stages.Remote.Status = "awaitingConfirm"));
        Assert.Equal("确认同步到 Apifox", step.Title);
        Assert.Equal("查看同步预览", step.PrimaryLabel);
        Assert.Equal("openSync", step.PrimaryAction);
    }

    // ---------- 未检查：按源码 → 生成文档 → Apifox 顺序 ----------

    [Fact]
    public void NotStarted_ScanFirst_WithSkipSecondary()
    {
        var step = NextStep.Compute(Online());
        Assert.Equal("扫描源码注释", step.Title);
        Assert.Equal("只读取源码：定位接口、参数与模型，检查注释缺口并收集代码证据；不需要 Swagger，不执行 build/restore。", step.Detail);
        Assert.Equal("开始扫描", step.PrimaryLabel);
        Assert.Equal("startScan", step.PrimaryAction);
        Assert.Equal("跳过，检查 Apifox", step.SecondaryLabel);
        Assert.Equal("startCheck", step.SecondaryAction);
    }

    [Fact]
    public void SourceFailed_ToneBad_DefaultAndCustomMessage()
    {
        var step = NextStep.Compute(Online(t => t.Stages.Source.Status = "failed"));
        Assert.Equal("注释回写失败", step.Title);
        Assert.Equal("查看回写结果，处理后重新生成注释计划。", step.Detail);
        Assert.Equal("bad", step.Tone);
        Assert.Equal("查看回写结果", step.PrimaryLabel);
        Assert.Equal("openApply", step.PrimaryAction);
        Assert.Equal("文件已被外部修改。",
            NextStep.Compute(Online(t =>
            {
                t.Stages.Source.Status = "failed";
                t.Stages.Source.Message = "文件已被外部修改。";
            })).Detail);
    }

    [Fact]
    public void PlanReady_BeforeCheck_PointsToPatchWithSkip()
    {
        var step = NextStep.Compute(Online(t => t.Stages.Source.Status = "planReady"));
        Assert.Equal("应用注释补丁", step.Title);
        Assert.Contains("自行应用后请重新扫描", step.Detail);
        Assert.Equal("查看补丁", step.PrimaryLabel);
        Assert.Equal("openApply", step.PrimaryAction);
        Assert.Equal("startCheck", step.SecondaryAction);
    }

    [Fact]
    public void GeneratedPendingOrPartial_BeforeCheck_PointsToPropagation()
    {
        foreach (var status in new[] { "pending", "partial" })
        {
            var step = NextStep.Compute(Online(t =>
            {
                t.Stages.Source.Status = "scanned";
                t.Stages.Generated.Status = status;
            }));
            Assert.Equal("核对生成文档", step.Title);
            Assert.Equal("去核对", step.PrimaryLabel);
            Assert.Equal("openPropagation", step.PrimaryAction);
            Assert.Equal("startCheck", step.SecondaryAction);
        }
        Assert.Equal("重新生成 XML 文档与 Swagger 后，核对注释是否传播到位。",
            NextStep.Compute(Online(t =>
            {
                t.Stages.Source.Status = "scanned";
                t.Stages.Generated.Status = "pending";
            })).Detail);
    }

    [Fact]
    public void ReviewingWithAccepted_MakeCommentPlan_First()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "reviewing";
            t.Stages.Source.Accepted = 4;
            t.Stages.Source.Candidates = 9;
        }));
        Assert.Equal("生成注释计划", step.Title);
        Assert.Equal("已接受 4 条候选；生成计划后先预览补丁，再决定回写或导出。", step.Detail);
        Assert.Equal("openApply", step.PrimaryAction);
        Assert.Equal("继续审阅", step.SecondaryLabel);
        Assert.Equal("openCandidates", step.SecondaryAction);
    }

    [Fact]
    public void ReviewingWithoutAccepted_ReviewDrafts_WithStalePrefix()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "reviewing";
            t.Stages.Source.Candidates = 7;
        }));
        Assert.Equal("审阅注释草稿", step.Title);
        Assert.Equal("共 7 条候选：查看代码证据后接受，接受的候选才会进入补丁。", step.Detail);
        Assert.Equal("去审阅", step.PrimaryLabel);
        Assert.Equal("openCandidates", step.PrimaryAction);
        Assert.Equal("startCheck", step.SecondaryAction);
        // 源码阶段的说明（如候选过期提示）作为前缀如实带出。
        Assert.StartsWith("2 条候选已过期 ", NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "reviewing";
            t.Stages.Source.Candidates = 7;
            t.Stages.Source.Message = "2 条候选已过期";
        })).Detail);
    }

    [Fact]
    public void GapsRemaining_FillComments()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 5;
            t.Stages.Source.ValidSlots = 2;
        }));
        Assert.Equal("补全注释", step.Title);
        Assert.Equal("3 个位置缺少有效说明：用规则提取、模型生成或手工填写得到候选。", step.Detail);
        Assert.Equal("去生成草稿", step.PrimaryLabel);
        Assert.Equal("startRuleExtraction", step.PrimaryAction);
    }

    [Fact]
    public void OfflineNotChecked_ImportRemoteJson()
    {
        var step = NextStep.Compute(Task(t =>
        {
            t.OfflineMode = true;
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
        }));
        Assert.Equal("导入「远端」OpenAPI 并检查", step.Title);
        Assert.Equal("去导入", step.PrimaryLabel);
        Assert.Equal("importOfflineRemote", step.PrimaryAction);
    }

    [Fact]
    public void ReadyToCheck_LabelFollowsSwaggerFreeze()
    {
        // 未配置 Swagger：开始检查。
        var plain = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
        }));
        Assert.Equal("检查 Apifox 差异", plain.Title);
        Assert.Equal("开始检查", plain.PrimaryLabel);
        Assert.Equal("startCheck", plain.PrimaryAction);
        // 配置了 Swagger 且还没冻结：下一次检查先加载并冻结。
        var frozen = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
            t.ProfileSnapshot.Swagger.Type = "url";
        }));
        Assert.Equal("加载 Swagger 并检查", frozen.PrimaryLabel);
        // 已冻结过（或离线任务不适用）：开始检查。
        Assert.Equal("开始检查", NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Source.Slots = 1;
            t.Stages.Source.ValidSlots = 1;
            t.ProfileSnapshot.Swagger.Type = "url";
            t.Stages.Remote.SourceHash = "abc";
        })).PrimaryLabel);
    }

    // ---------- 已检查：源码侧优先，然后按远端状态 ----------

    [Fact]
    public void AfterCheck_PlanReady_UsesPostCheckWording()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "planReady";
            t.Stages.Remote.Status = "previewable";
        }));
        Assert.Equal("应用注释补丁", step.Title);
        Assert.Contains("回写后重新生成 Swagger，再重新检查 Apifox", step.Detail);
        Assert.Equal("Apifox 差异", step.SecondaryLabel);
        Assert.Equal("openRemoteResults", step.SecondaryAction);
    }

    [Fact]
    public void AfterCheck_GeneratedPending_PointsToPropagationThenReloadSwagger()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Generated.Status = "pending";
            t.Stages.Remote.Status = "previewable";
        }));
        Assert.Equal("核对生成文档", step.Title);
        Assert.Contains("源码注释已回写。", step.Detail);
        // 未配置 Swagger：没有加载入口，说明文字不指名一个界面上不存在的按钮。
        Assert.Contains("再在「Apifox 差异与同步」区重新检查。", step.Detail);
        Assert.DoesNotContain("加载 Swagger 并检查", step.Detail);
        Assert.Equal("openPropagation", step.PrimaryAction);
    }

    [Fact]
    public void SwaggerEntryLabel_NamesTheEntryThatActuallyExists()
    {
        // 未配置 Swagger：界面上没有加载入口，说明文字不指名按钮。
        Assert.Null(NextStep.SwaggerEntryLabel(Online()));
        Assert.Null(NextStep.SwaggerEntryLabel(Online(t => t.ProfileSnapshot.Swagger.Type = "none")));
        // 配置了 Swagger 且未冻结：任务页按钮是「加载 Swagger 并检查」。
        Assert.Equal("加载 Swagger 并检查", NextStep.SwaggerEntryLabel(Online(t => t.ProfileSnapshot.Swagger.Type = "file")));
        // 已冻结：加载入口不再出现，任务页按钮是「重新加载 Swagger 并检查」。
        Assert.Equal("重新加载 Swagger 并检查", NextStep.SwaggerEntryLabel(Online(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "file";
            t.Stages.Remote.SourceHash = "abc";
        })));
        // 离线任务：结构来源是导入的 JSON，两个入口都不适用。
        Assert.Null(NextStep.SwaggerEntryLabel(Online(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "file";
            t.Stages.Remote.SourceHash = "abc";
            t.OfflineMode = true;
        })));
        // 文案常量与任务页按钮同一落点（不复制字面）。
        Assert.Equal(NextStep.LoadSwaggerLabel, NextStep.SwaggerEntryLabel(Online(t => t.ProfileSnapshot.Swagger.Type = "url")));
    }

    [Fact]
    public void AfterCheck_GeneratedPending_NamesReloadEntryWhenFrozen()
    {
        // 已冻结（检查开始过）时，说明文字指名的是「重新加载 Swagger 并检查」——冻结后加载入口已不存在。
        var step = NextStep.Compute(Online(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "file";
            t.Stages.Remote.SourceHash = "abc";
            t.Stages.Generated.Status = "pending";
            t.Stages.Remote.Status = "previewable";
        }));
        Assert.Equal("核对生成文档", step.Title);
        Assert.Contains("用「重新加载 Swagger 并检查」重新检查", step.Detail);
        Assert.DoesNotContain("用「加载 Swagger 并检查」", step.Detail);
    }

    [Fact]
    public void Expired_NamesReloadEntryWhenFrozen_AndOmitsHintWhenNoSwagger()
    {
        // 已冻结：过期提示指名真实存在的重新冻结入口。
        var frozen = NextStep.Compute(Online(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "file";
            t.Stages.Remote.SourceHash = "abc";
            t.Stages.Remote.Status = "expired";
        }));
        Assert.Equal("重新检查", frozen.Title);
        Assert.Contains("请用「重新加载 Swagger 并检查」", frozen.Detail);
        // 未配置 Swagger：没有可重新冻结的副本，提示不指名入口（不指向不存在的按钮）。
        var plain = NextStep.Compute(Online(t => t.Stages.Remote.Status = "expired"));
        Assert.Equal("检查或生成计划之后任务内容有变化，原结果已失效。", plain.Detail);
        Assert.DoesNotContain("Swagger", plain.Detail);
    }

    [Fact]
    public void Expired_CheckAgain_ToneWarn()
    {
        var step = NextStep.Compute(Online(t => t.Stages.Remote.Status = "expired"));
        Assert.Equal("重新检查", step.Title);
        Assert.Equal("warn", step.Tone);
        Assert.Equal("重新检查", step.PrimaryLabel);
        Assert.Equal("startCheck", step.PrimaryAction);
    }

    [Fact]
    public void CheckedRevisionBehindRevision_CountsAsStale()
    {
        // 检查后又接受了注释 / 改了决策 / 补了说明（Revision 前进），pending 与 previewable 都按过期引导。
        foreach (var status in new[] { "pending", "previewable" })
        {
            var step = NextStep.Compute(Online(t =>
            {
                t.Stages.Remote.Status = status;
                t.Stages.Remote.CheckedRevision = 2;
            }));
            Assert.Equal("重新检查", step.Title);
        }
        // 版本一致时不按过期：pending → 处理待确认项。
        var fresh = NextStep.Compute(Online(t =>
        {
            t.Stages.Remote.Status = "pending";
            t.Stages.Remote.CheckedRevision = 3;
        }));
        Assert.Equal("处理待确认项", fresh.Title);
    }

    [Fact]
    public void PendingDecisions_ShowsCounts_ToneWarn()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Remote.Status = "pending";
            t.Stages.Remote.Blocked = 1;
            t.Stages.Remote.Conflicts = 2;
            t.Stages.Remote.Added = 3;
            t.Stages.Remote.StructureChanged = 4;
            t.Stages.Remote.DocChanged = 5;
        }));
        Assert.Equal("处理待确认项", step.Title);
        Assert.Equal("阻断 1 · 说明冲突 2 · 可同步变化 12：高风险变化逐项确认后保存并重新检查；其余接口可以直接勾选同步。", step.Detail);
        Assert.Equal("warn", step.Tone);
        Assert.Equal("去处理", step.PrimaryLabel);
        Assert.Equal("openDecisions", step.PrimaryAction);
    }

    [Fact]
    public void PreviewableWithChanges_ChooseEndpoints()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Remote.Status = "previewable";
            t.Stages.Remote.StructureChanged = 2;
            t.Stages.Remote.DocChanged = 1;
        }));
        Assert.Equal("选择要同步的接口", step.Title);
        Assert.Equal("新增 0 · 结构变化 2 · 说明变化 1：勾选后生成同步预览。", step.Detail);
        Assert.Equal("去选择", step.PrimaryLabel);
        Assert.Equal("openSync", step.PrimaryAction);
        // 没有变化时不引导选择：目标与远端一致，去看报告。
        var noChange = NextStep.Compute(Online(t => t.Stages.Remote.Status = "previewable"));
        Assert.Equal("Apifox 与目标一致", noChange.Title);
        Assert.Equal("openReports", noChange.PrimaryAction);
    }

    [Fact]
    public void SyncPartialOrFailed_HonestTone()
    {
        foreach (var status in new[] { "partial", "failed" })
        {
            var step = NextStep.Compute(Online(t => t.Stages.Remote.Status = status));
            Assert.Equal("同步未全部完成", step.Title);
            Assert.Equal("bad", step.Tone);
            Assert.Equal("查看结果", step.PrimaryLabel);
            Assert.Equal("openSync", step.PrimaryAction);
        }
    }

    [Fact]
    public void OfflineChecked_ReportFirst_SyncUnavailable()
    {
        var step = NextStep.Compute(Task(t =>
        {
            t.OfflineMode = true;
            t.Stages.Source.Status = "scanned";
            t.Stages.Remote.Status = "offline";
        }));
        Assert.Equal("离线检查完成", step.Title);
        Assert.Equal("离线比较不能同步；可以查看差异或下载报告。", step.Detail);
        Assert.Equal("查看报告", step.PrimaryLabel);
        Assert.Equal("openReports", step.PrimaryAction);
        Assert.Equal("openRemoteResults", step.SecondaryAction);
    }

    [Fact]
    public void Verified_ToneOk_MessagePassthrough()
    {
        var step = NextStep.Compute(Online(t => t.Stages.Remote.Status = "verified"));
        Assert.Equal("同步完成", step.Title);
        Assert.Equal("已写入并回读核对通过。", step.Detail);
        Assert.Equal("ok", step.Tone);
        Assert.Equal("查看报告", step.PrimaryLabel);
        Assert.Equal("openReports", step.PrimaryAction);
        Assert.Equal("重新检查", step.SecondaryLabel);
        Assert.Equal("startCheck", step.SecondaryAction);
    }

    [Fact]
    public void NoChangesOrUnknownStatus_TargetConsistent_ToneOk()
    {
        var step = NextStep.Compute(Online(t =>
        {
            t.Stages.Source.Status = "scanned";
            t.Stages.Remote.Status = "snapshotReady";
        }));
        Assert.Equal("Apifox 与目标一致", step.Title);
        Assert.Equal("范围内接口没有需要同步的变化；可以下载报告或前端交接说明。", step.Detail);
        Assert.Equal("ok", step.Tone);
        Assert.Equal("查看报告", step.PrimaryLabel);
        Assert.Equal("Apifox 差异", step.SecondaryLabel);
    }

    // ---------- Swagger 冻结判定（按钮文案与检查前置共用同一口径） ----------

    [Fact]
    public void NeedsSwaggerLoad_ConfiguredAndNotFrozen()
    {
        Assert.False(NextStep.NeedsSwaggerLoad(Task(t => t.ProfileSnapshot.Swagger.Type = "none")));
        Assert.False(NextStep.NeedsSwaggerLoad(Task(t => t.ProfileSnapshot.Swagger.Type = "")));
        Assert.False(NextStep.NeedsSwaggerLoad(Task(t =>
        {
            t.ProfileSnapshot.Swagger.Type = "file";
            t.Stages.Remote.SourceHash = "abc";
        })));
        Assert.False(NextStep.NeedsSwaggerLoad(Task(t =>
        {
            t.OfflineMode = true;
            t.ProfileSnapshot.Swagger.Type = "file";
        })));
        Assert.True(NextStep.NeedsSwaggerLoad(Task(t => t.ProfileSnapshot.Swagger.Type = "file")));
    }

    // ── 接线机械检查：口径存在但界面没调用等于没有 ───────────────────

    [Fact]
    public void TasksPage_WiresBannerListColumnAndGating()
    {
        var page = Repo.Source("Desktop", "Native", "TasksPage.cs");
        // 横幅与列表列都由共享口径驱动；动作经映射调既有入口；作业执行中禁用。
        Assert.Contains("NextStep.Compute(task)", page);
        Assert.Contains("RefreshNextBanner(", page);
        Assert.Contains("RunNextAction(action)", page);
        Assert.Contains("bannerButton.Enabled = job is null", page);
        // 任务列表的「下一步」列在「配置」之后；按钮文案与检查前置共用 NextStep.NeedsSwaggerLoad 单一口径。
        Assert.Matches(@"Columns\.Add\(""配置"", 140\);[\s\S]*?Columns\.Add\(""下一步"", 150\);", page);
        Assert.Contains("NextStep.NeedsSwaggerLoad(task)", page);
        Assert.DoesNotContain("private static bool NeedsSwaggerLoad", page);
    }
}
