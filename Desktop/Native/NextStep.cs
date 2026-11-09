using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「下一步」引导的单一口径（方案 3.2：任务按「源码注释 → 生成文档 → Apifox」三个阶段推进，
/// 界面始终给出唯一「下一步」）：根据任务三阶段状态算出当前唯一该做的事。
/// 只做引导，不改变任何状态；写入类操作仍在各自区域与对话框中确认。
/// 与冻结页面（Web/app.js 的 nextStep）同一规则、同一文案；中文引用记号按排版规则用「」。
/// 动作以种类名表述，由界面映射到既有入口（开始扫描、打开对话框、开始检查……），本文件不执行动作。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class NextStep
{
    /// <summary>
    /// 下一步条目。Tone 为语义色调（空 / ok / warn / bad），由界面按设计令牌映射为文字颜色——
    /// 颜色只承担状态且不作唯一信号，标题与说明文字本身承载含义。
    /// 动作种类：startScan / startRuleExtraction / openApply / openPropagation / openCandidates /
    /// importOfflineRemote / startCheck / openDecisions / openSync / openReports / openRemoteResults。
    /// Stage 是这一步落在哪个阶段（<see cref="StageSource"/> / <see cref="StageGenerated"/> /
    /// <see cref="StageRemote"/> / <see cref="StageReport"/>），与冻结页面 nextStep 的第一个参数同一套取值：
    /// 任务详情的三阶段进度条据此标出「当前」那一格（<see cref="StageSteps"/>），不另写一套引导规则。
    /// </summary>
    public sealed record Step(
        string Title,
        string Detail,
        string Tone,
        string? PrimaryLabel = null,
        string? PrimaryAction = null,
        string? SecondaryLabel = null,
        string? SecondaryAction = null,
        string Stage = StageReport);

    /// <summary>源码注释阶段的键（与冻结页面 nextStep 的 stage 同一套取值）。</summary>
    public const string StageSource = "source";

    /// <summary>生成文档阶段的键。</summary>
    public const string StageGenerated = "generated";

    /// <summary>Apifox 阶段的键。</summary>
    public const string StageRemote = "remote";

    /// <summary>报告阶段的键：不落在三阶段进度条的三格之内（与冻结页面 stepper 只有三格同一口径）。</summary>
    public const string StageReport = "report";

    /// <summary>
    /// 配置了 Swagger、还没冻结过且不是离线任务：下一次检查先加载并冻结 Swagger，
    /// 避免不知情地退化为只按远端现状比较（与冻结页面同一规则）。
    /// </summary>
    public static bool NeedsSwaggerLoad(CheckTask task) =>
        !task.OfflineMode
        && task.ProfileSnapshot.Swagger.Type is not (null or "" or "none")
        && task.Stages.Remote.SourceHash is null;

    /// <summary>
    /// 「重新加载 Swagger 并检查」入口的可用条件（与冻结页面同一规则）：配置了 Swagger、已冻结过且
    /// 不是离线任务。检查始终基于冻结副本，检查本身不悄悄换源；注释回写或定向构建后源码与 Swagger
    /// 已更新，需要这个入口重新冻结，否则检查永远比较旧副本（冻结页面工具栏同一指引）。
    /// </summary>
    public static bool CanReloadSwagger(CheckTask task) =>
        !task.OfflineMode
        && task.ProfileSnapshot.Swagger.Type is not (null or "" or "none")
        && task.Stages.Remote.SourceHash is not null;

    /// <summary>「重新加载 Swagger 并检查」入口文案（与冻结页面同一文案）。</summary>
    public const string ReloadSwaggerLabel = "重新加载 Swagger 并检查";

    /// <summary>「加载 Swagger 并检查」入口文案（首次冻结，与冻结页面同一文案）。</summary>
    public const string LoadSwaggerLabel = "加载 Swagger 并检查";

    /// <summary>
    /// 说明文字里指名的检查入口：配置了 Swagger 时按冻结状态给出界面上真实存在的入口名——
    /// 未冻结是「加载 Swagger 并检查」，已冻结是「重新加载 Swagger 并检查」（检查始终基于冻结副本，
    /// 冻结后加载入口不再出现）；未配置 Swagger 时没有加载入口，返回 null，说明文字不指名入口。
    /// </summary>
    public static string? SwaggerEntryLabel(CheckTask task) =>
        NeedsSwaggerLoad(task) ? LoadSwaggerLabel
        : CanReloadSwagger(task) ? ReloadSwaggerLabel
        : null;

    public static Step Compute(CheckTask task)
    {
        var s = task.Stages.Source;
        var g = task.Stages.Generated;
        var r = task.Stages.Remote;
        // 缺口数与「源码阶段是否完成」都取 StageSteps 那一份（第一百一十八轮）：进度条第一格与
        // 本文件的「补全注释」那一支不再各算一遍，同一屏上不会出现「进度条打 ✓、横幅说还有缺口」。
        var gaps = StageSteps.SourceGaps(task);
        var sourceDone = StageSteps.SourceStageDone(task);
        var remoteStarted = r.Status != "notChecked";
        var changed = r.Added + r.StructureChanged + r.DocChanged;
        // 源码阶段的说明（如「有候选已过期」）作为引导文字的前缀如实带出。
        var stale = string.IsNullOrEmpty(s.Message) ? "" : s.Message + " ";
        // 说明文字指名的检查入口按任务状态取真实存在的那个（未配置 Swagger 时不指名）。
        var swaggerEntry = SwaggerEntryLabel(task);

        // 远端执行中、被中断或等待确认最紧迫。
        if (r.Status == "executing")
            return new Step("同步执行中", "正在逐个写入并回读核对；取消会在当前动作完成后停止。", "", "查看同步", "openSync", Stage: StageRemote);
        if (r.Status == "interrupted")
            return new Step("上次同步被中断", r.Message ?? "请重新核对远端实际状态。", "bad", "去重新核对", "openSync", Stage: StageRemote);
        if (r.Status == "awaitingConfirm")
            return new Step("确认同步到 Apifox", "同步预览已生成：核对写入清单与写入内容后确认执行。", "", "查看同步预览", "openSync", Stage: StageRemote);

        // 还没开始检查 Apifox：按源码 → 生成文档 → Apifox 的顺序引导。
        if (!remoteStarted)
        {
            if (s.Status == "notStarted")
                return new Step("扫描源码注释", "只读取源码：定位接口、参数与模型，检查注释缺口并收集代码证据；不需要 Swagger，不执行 build/restore。", "", "开始扫描", "startScan", "跳过，检查 Apifox", "startCheck", StageSource);
            if (s.Status == "failed")
                return new Step("注释回写失败", s.Message ?? "查看回写结果，处理后重新生成注释计划。", "bad", "查看回写结果", "openApply", Stage: StageSource);
            if (s.Status == "planReady")
                return new Step("应用注释补丁", "注释计划已生成：确认后写回源码，或导出补丁自行应用（自行应用后请重新扫描）。", "", "查看补丁", "openApply", "跳过，检查 Apifox", "startCheck", StageSource);
            if (g.Status is "pending" or "partial")
                return new Step("核对生成文档", g.Message ?? "重新生成 XML 文档与 Swagger 后，核对注释是否传播到位。", "", "去核对", "openPropagation", "跳过，检查 Apifox", "startCheck", StageGenerated);
            if (s.Status == "reviewing" && s.Accepted > 0)
                return new Step("生成注释计划", stale + $"已接受 {s.Accepted} 条候选；生成计划后先预览补丁，再决定回写或导出。", "", "生成注释计划", "openApply", "继续审阅", "openCandidates", StageSource);
            if (s.Status == "reviewing")
                return new Step("审阅注释草稿", stale + $"共 {s.Candidates} 条候选：查看代码证据后接受，接受的候选才会进入补丁。", "", "去审阅", "openCandidates", "跳过，检查 Apifox", "startCheck", StageSource);
            // 只有共享判定说源码阶段没完成时才引导「补全注释」——这一支与进度条第一格同源，
            // 已回写（applied）但扫描数据还留着缺口时不再把用户拉回源码阶段。
            if (!sourceDone && gaps > 0)
                return new Step("补全注释", $"{gaps} 个位置缺少有效说明：用规则提取、模型生成或手工填写得到候选。", "", "去生成草稿", "startRuleExtraction", "跳过，检查 Apifox", "startCheck", StageSource);
            if (task.OfflineMode)
                return new Step("导入「远端」OpenAPI 并检查", "离线比较：导入作为「远端」的 OpenAPI JSON 后开始检查（不访问 Apifox，同步不可用）。", "", "去导入", "importOfflineRemote", Stage: StageRemote);
            return new Step("检查 Apifox 差异", "只读：导出分支概览，再逐个读取范围内接口与模型详情，与 Swagger、响应契约和补充说明构成的目标比较。", "", CheckLabel(), "startCheck", Stage: StageRemote);
        }

        // 已开始检查 Apifox 后，源码侧的回写失败、未应用的补丁、回写后未核对的生成文档仍然优先：它们会改变比较的目标。
        if (s.Status == "failed")
            return new Step("注释回写失败", s.Message ?? "查看回写结果，处理后重新生成注释计划。", "bad", "查看回写结果", "openApply", "Apifox 差异", "openRemoteResults", StageSource);
        if (s.Status == "planReady")
            return new Step("应用注释补丁", "注释计划已生成：回写后重新生成 Swagger，再重新检查 Apifox；也可以导出补丁自行应用（之后请重新扫描）。", "", "查看补丁", "openApply", "Apifox 差异", "openRemoteResults", StageSource);
        if (g.Status == "pending")
            return new Step("核对生成文档", (g.Message ?? "源码注释已回写。") + " 重新生成 XML 文档与 Swagger 后核对，再在「Apifox 差异与同步」区" + (swaggerEntry is null ? "重新检查。" : $"用「{swaggerEntry}」重新检查。"), "", "去核对", "openPropagation", "Apifox 差异", "openRemoteResults", StageGenerated);

        // 检查结果所基于的版本落后于任务（检查后又接受了注释、改了决策或补充说明）时，按过期处理。
        var checkStale = r.CheckedRevision is { } revision && revision != task.Revision && r.Status is "pending" or "previewable";
        if (r.Status == "expired" || checkStale)
            return new Step("重新检查", "检查或生成计划之后任务内容有变化，原结果已失效。"
                + (swaggerEntry is null ? "" : $"源码或 Swagger 有更新时，请用「{swaggerEntry}」。"), "warn", "重新检查", "startCheck", Stage: StageRemote);
        if (r.Status == "pending")
            return new Step("处理待确认项", $"阻断 {r.Blocked} · 说明冲突 {r.Conflicts} · 可同步变化 {changed}：高风险变化逐项确认后保存并重新检查；其余接口可以直接勾选同步。", "warn", "去处理", "openDecisions", Stage: StageRemote);
        if (r.Status == "previewable" && changed > 0)
            return new Step("选择要同步的接口", $"新增 {r.Added} · 结构变化 {r.StructureChanged} · 说明变化 {r.DocChanged}：勾选后生成同步预览。", "", "去选择", "openSync", Stage: StageRemote);
        if (r.Status is "partial" or "failed")
            return new Step("同步未全部完成", r.Message ?? "查看失败的动作后重新核对。", "bad", "查看结果", "openSync", Stage: StageRemote);
        if (r.Status == "offline")
            return new Step("离线检查完成", "离线比较不能同步；可以查看差异或下载报告。", "", "查看报告", "openReports", "Apifox 差异", "openRemoteResults", StageReport);
        if (r.Status == "verified")
            return new Step("同步完成", r.Message ?? "已写入并回读核对通过。", "ok", "查看报告", "openReports", "重新检查", "startCheck", StageReport);
        return new Step("Apifox 与目标一致", "范围内接口没有需要同步的变化；可以下载报告或前端交接说明。", "ok", "查看报告", "openReports", "Apifox 差异", "openRemoteResults", StageReport);

        string CheckLabel() => NeedsSwaggerLoad(task) ? LoadSwaggerLabel : "开始检查";
    }
}
