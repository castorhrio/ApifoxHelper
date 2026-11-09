using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务详情的三阶段进度条口径（DESIGN.md §6「任务详情的三阶段进度条」，第一百一十六轮）：
/// 冻结页面的任务详情顶部有 stepper——「源码注释 / 生成文档 / Apifox」三个阶段各带状态徽标，
/// 已完成的阶段把序号换成 ✓、当前阶段标出来，点一下跳到该阶段的页签；原生任务详情此前只有
/// 「下一步」横幅，三个阶段各自的状态散在五个分区里，而详情是可滚动的长内容——
/// 「三个阶段各到哪一步」要滚着找，属同壳不同行为（冻结页面有、原生没有）。
/// <para>
/// 这里只算呈现：阶段顺序、标签、状态文字、完成与当前标记、跳到哪个分区、悬停提示。状态文字取
/// <see cref="TaskDisplay"/> 的三份中文映射（与三个分区的状态行同一份，不另写一套）；「当前阶段」取
/// <see cref="NextStep.Step.Stage"/>（与「下一步」横幅同一份判定，本文件不复制引导规则）；
/// 完成判定见 <see cref="SourceStageDone"/>（源码阶段）与「已核对」（生成文档与 Apifox）。
/// </para>
/// 不依赖 WinForms，与测试工程共享编译（真值表在 Linux 真执行）；界面只负责把三格文字画出来、
/// 把点击映射到对应分区，不就地拼这几处文字。
/// </summary>
public static class StageSteps
{
    /// <summary>源码注释阶段的标签（与任务详情该分区的标题同一份）。</summary>
    public const string SourceLabel = "源码注释";

    /// <summary>生成文档阶段的标签（与任务详情该分区的标题同一份）。</summary>
    public const string GeneratedLabel = "生成文档";

    /// <summary>Apifox 阶段的标签（进度条上那一格，与冻结页面 stepper 同一文案）。</summary>
    public const string RemoteLabel = "Apifox";

    /// <summary>
    /// Apifox 阶段在任务详情里的分区标题：进度条那一格写「Apifox」（与冻结页面同一文案），
    /// 点下去要落到「Apifox 差异与同步」区——两处文字不同，映射只在本文件一处。
    /// </summary>
    public const string RemoteSectionLabel = "Apifox 差异与同步";

    /// <summary>阶段顺序（与冻结页面 stepper 的三格同序）：点击映射与建格都按这一份走。</summary>
    public static readonly string[] Keys = [NextStep.StageSource, NextStep.StageGenerated, NextStep.StageRemote];

    /// <summary>已完成的阶段把序号换成这个记号（与冻结页面 stepper 的 <c>✓</c> 同一记号）。</summary>
    public const string DoneMark = "✓";

    /// <summary>当前阶段的后缀：颜色不作唯一信号，当前这一步由文字说出来。</summary>
    public const string CurrentSuffix = "（当前）";

    /// <summary>进度条右侧那句话：讲清点一下会发生什么（按钮本身不解释自己）。</summary>
    public const string JumpHint = "点击阶段跳到对应分区。";

    /// <summary>
    /// 一个阶段：键、标签、状态文字（取 <see cref="TaskDisplay"/> 的中文映射）、是否已完成、是否当前阶段。
    /// </summary>
    public sealed record Step(string Key, string Label, string StatusText, bool Done, bool Current);

    /// <summary>
    /// 源码阶段的缺口数（位置数减去有效说明数，负数按 0 处理）。「N 个位置缺少有效说明」这句引导文字
    /// 与 <see cref="SourceStageDone"/> 都取这一份——两处不再各算一遍（第一百一十八轮）。
    /// </summary>
    public static int SourceGaps(CheckTask task)
    {
        var source = task.Stages.Source;
        return Math.Max(0, source.Slots - source.ValidSlots);
    }

    /// <summary>
    /// 源码阶段是否完成——**唯一判定**（第一百一十八轮）：<see cref="Build"/> 的第一格与
    /// <see cref="NextStep.Compute"/> 的「补全注释」那一支都调它，两处不再各写一份表达式。
    /// 规则：已回写（<c>applied</c>）即完成；部分回写（<c>partiallyApplied</c>）与已扫描（<c>scanned</c>）
    /// 要求没有缺口；其余状态（未开始 / 失败 / 计划就绪 / 审阅中）都不算完成。
    /// <para>
    /// 与冻结页面 stepper 的差别如实记在这里：冻结页面把「部分回写」直接算完成，本壳要求它同时没有缺口——
    /// 部分回写本身就说明还有位置没写进去，此时进度条打 ✓ 而横幅说「补全注释」是同一屏自相矛盾
    /// （第一百一十七轮的交叉断言实测抓到，第一百一十八轮收敛为这一份判定）。这是**有意的口径差异**，
    /// 冻结页面（`Web/app.js` 的 stepper）行为不改，只在那边加一句注释说明本壳有意收敛（第一百一十九轮定案）。
    /// </para>
    /// <para>
    /// **未知状态一律当未完成**（第一百一十九轮钉死）：状态值是磁盘上的字符串，任务文件可能来自更新的版本、
    /// 手工编辑或损坏，出现本文件不认识的取值（如 <c>"weird"</c>、空串）时这里答「未完成」——这是安全默认：
    /// 进度条不打 ✓、横幅继续引导补全注释，用户不会因为一个读不懂的状态被当成「已经做完」。
    /// 与 <see cref="TaskDisplay"/> 的「原样回退」口径不同（那里把认不出的状态原样显示，因为那一格要如实
    /// 告诉用户磁盘上写的是什么），两处取舍不同、各有各的理由，不互相套用。
    /// </para>
    /// </summary>
    public static bool SourceStageDone(CheckTask task)
    {
        var source = task.Stages.Source;
        // 已回写：源码阶段的目标（把注释写进源码）已经达成，扫描留下的缺口数不再参与判定。
        if (source.Status == "applied") return true;
        // 部分回写与已扫描：只有没有缺口才算完成。
        if (source.Status is "partiallyApplied" or "scanned") return SourceGaps(task) == 0;
        return false;
    }

    /// <summary>
    /// 由任务三阶段状态与「下一步」所在的阶段算出三格进度。
    /// <paramref name="currentStage"/> 取 <see cref="NextStep.Step.Stage"/>：报告阶段（report）与未知值
    /// 都不落在三格之内，此时没有一格是当前——与冻结页面 stepper 同一口径（它也只有三格）。
    /// </summary>
    public static IReadOnlyList<Step> Build(CheckTask task, string? currentStage)
    {
        var source = task.Stages.Source;
        var generated = task.Stages.Generated;
        var remote = task.Stages.Remote;
        // 源码阶段完成与否只问 SourceStageDone（与「下一步」横幅同一份判定，本文件不另写表达式）。
        var sourceDone = SourceStageDone(task);
        return
        [
            new Step(NextStep.StageSource, SourceLabel, TaskDisplay.SourceStatusText(source.Status), sourceDone, currentStage == NextStep.StageSource),
            new Step(NextStep.StageGenerated, GeneratedLabel, TaskDisplay.GeneratedStatusText(generated.Status), generated.Status == "verified", currentStage == NextStep.StageGenerated),
            new Step(NextStep.StageRemote, RemoteLabel, TaskDisplay.RemoteStatusText(remote.Status), remote.Status == "verified", currentStage == NextStep.StageRemote),
        ];
    }

    /// <summary>
    /// 序号格：已完成的阶段显示 ✓，其余显示第几（1 起）——与冻结页面 stepper 的 <c>step-num</c> 同一口径。
    /// </summary>
    public static string NumberText(Step step, int index) => step.Done ? DoneMark : (index + 1).ToString();

    /// <summary>
    /// 按钮文字：「序号 标签 · 状态」，当前阶段再后缀「（当前）」。
    /// 三处文字都来自本文件与 <see cref="TaskDisplay"/>，界面不就地拼。
    /// </summary>
    public static string ButtonText(Step step, int index) =>
        NumberText(step, index) + " " + step.Label + " · " + step.StatusText + (step.Current ? CurrentSuffix : "");

    /// <summary>
    /// 点这一格落到哪个分区：进度条那一格写「Apifox」、分区标题是「Apifox 差异与同步」，
    /// 两处文字不同，映射只在本文件一处（界面不就地写第二份）。
    /// </summary>
    public static string SectionLabel(string key) => key switch
    {
        NextStep.StageSource => SourceLabel,
        NextStep.StageGenerated => GeneratedLabel,
        NextStep.StageRemote => RemoteSectionLabel,
        _ => "",
    };

    /// <summary>
    /// 悬停提示：与冻结页面 stepper 的 <c>title: '打开' + label</c> 同一句式，但落点按本壳的真实分区名说
    /// （Apifox 那一格落到「Apifox 差异与同步」区，提示里就说这个区，不指一个界面上不存在的页签）。
    /// 认不出的键没有提示（不猜一个分区）。
    /// </summary>
    public static string JumpTooltip(Step step) =>
        SectionLabel(step.Key) is { Length: > 0 } section ? "打开" + section : "";
}
