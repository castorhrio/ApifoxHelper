namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 「哪些屏真的显示过」的唯一登记处（第二百四十六轮，简报 P0-1，具名 pass：harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十三轮量到的那件事——冒烟器每一段都只「建句柄 + <c>CreateControl</c>
/// + <c>PerformLayout</c>」，<b>从不显示窗体</b>，而一张没显示过的清单 <c>ClientSize</c> 的高就是 0。
/// 于是「按预算估」冒充了「量过」，而<b>判据、读数、挂账三者在仓里没有一处对账</b>：
/// 哪一屏真的 <c>Show()</c> 过、哪一屏只是被 <c>new</c> 了一次、量过的是哪一轮，全靠人读
/// <c>ApifoxHelper.Smoke/Program.cs</c> 那七千行去认。下一轮再加一屏，同样的事会再发生一次。
/// </para>
/// <para>
/// <b>这一份是什么</b>：纯数据（不依赖 WinForms，与测试工程共享编译）。一行 = 一个冒烟段
/// （<see cref="Section"/>）或一张从未显示过的屏，写清四件事：这一段显示的是哪几屏
/// （<see cref="Classes"/>）、<b>是不是真的显示过</b>（<see cref="Measured"/>）、
/// <b>量过的是哪一轮</b>（<see cref="Round"/>）、<b>出处</b>（<see cref="Note"/>）。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判「量到的数对不对」——「显示过」与「量对了」是两件事，后者只有人读得出来。
/// 也不判「这一屏该不该显示」：登记的是<b>现状</b>，不是目标。
/// </para>
/// <para>
/// <b>为什么 <see cref="Round"/> 与 <see cref="Note"/> 要一起写</b>：只写「已量」而不写哪一轮，
/// 下一个人无法判断这条登记是否过期（本仓已有一次「台账滞后十二轮」的先例，见
/// <c>docs/DESIGN.md</c> §8「数字等宽」那一行）。门在 <c>ApifoxHelper.Tests/MeasuredScreensTests.cs</c>：
/// ① 冒烟器里凡出现 <c>.Show();</c> 的段，必须在登记里标「已量」且轮次非空；
/// ② 标「未量」的屏，冒烟器里不得出现它的 <c>Show()</c>；
/// ③ 每一行的出处必须真的在它引的那份文档里（轮次逐字可查）；
/// ④ 冒烟器构造清单里的每个对话框 / 页面类都必须被某一行认领。
/// </para>
/// </summary>
internal static class MeasuredScreens
{
    /// <summary>
    /// 一行登记。
    /// </summary>
    /// <param name="Screen">屏名（人读的那一个，与冒烟段标题里的叫法一致）。</param>
    /// <param name="Section">
    /// 冒烟器里真的把它显示出来的那一段（<c>RunXxx</c>）。未量的屏写它<b>被构造</b>的那一段，
    /// 或 <see cref="NoSection"/>（连构造都没有）。
    /// </param>
    /// <param name="Classes">这一段显示（或这一行挂账）的对话框 / 页面类名。</param>
    /// <param name="Measured">是否真的 <c>Show()</c> 过并量过。</param>
    /// <param name="Round">量过的那一轮；未量的写挂账登记的那一轮。</param>
    /// <param name="Note">出处：哪一份文档的哪一处说了这件事（轮次必须逐字出现在那份文档里）。</param>
    internal sealed record Row(
        string Screen,
        string Section,
        string[] Classes,
        bool Measured,
        string Round,
        string Note);

    /// <summary>连构造都没有的屏用这个占位（不是段名，门会把它当「没有段」处理）。</summary>
    internal const string NoSection = "—";

    /// <summary>
    /// 当前登记。<b>顺序按冒烟器 <c>Main</c> 里的注册顺序</b>，未量的五屏排在最后——
    /// 读的人从上往下就是冒烟器跑一遍的顺序。
    /// </summary>
    internal static IReadOnlyList<Row> All { get; } =
    [
        // ── 已量：冒烟器里真的 Show() 过 ──
        new("完整旅程（配置 → 任务 → 扫描 → 审阅 → 回写 → 核对 → 报告）", "RunEndToEnd",
            ["NewTaskDialog", "ProfileDialog"], true, "第一百三十九轮",
            "docs/NOTES.md 第一百三十九轮：八段按 README「使用流程」1–9 步走完，每段自己造配置与任务"),
        new("注释回写", "RunWriteBackEndToEnd",
            ["ApplyPlanDialog"], true, "第一百三十七轮",
            "docs/NOTES.md 第一百三十七轮：注释回写对话框的计划生成、确认后回写落盘与逐字节核对"),
        new("注释缺口", "RunScanResultsInteraction",
            ["ScanResultsDialog"], true, "第一百三十六轮",
            "docs/NOTES.md 第一百三十六轮：注释缺口对话框从「只构造一次」推到「真的打开、真的点、真的敲」"),
        new("标识格字体（响应契约首列 / 项目列表 ID 列）", "RunIdentityCellFont",
            ["ContractsDialog", "ProjectsPage"], true, "第二百二十八轮",
            "docs/DESIGN.md 第二百二十八轮「数字等宽（§3 / D5）」那一行：十三处就地设一次收进 Ui.IdentityCell，wine 三档另有本段四段"),
        new("注释候选", "RunCandidatesInteraction",
            ["CandidatesDialog"], true, "第一百三十九轮",
            "docs/NOTES.md 第一百三十九轮：候选对话框六段全部走真实控件（Show 触发 OnLoad、PerformClick、真敲关键词）"),
        new("补充说明", "RunSupplementsInteraction",
            ["SupplementsDialog"], true, "第一百六十二轮",
            "docs/NOTES.md 第一百六十二轮：补充说明对话框六段（编辑位派生、保存递增版本、原样重复保存不递增、保留现状）"),
        new("差异确认", "RunDecisionsInteraction",
            ["DecisionsDialog"], true, "第一百六十二轮",
            "docs/NOTES.md 第一百六十二轮：差异确认对话框六段（确认纳入 / 撤销、说明冲突选边、筛到零行筛选器仍可用）"),
        new("响应契约", "RunContractsInteraction",
            ["ContractsDialog"], true, "第一百八十二轮",
            "docs/NOTES.md 第一百八十二轮：五段走真实控件，点「确认」后契约落盘且任务版本 +1"),
        new("新建 / 编辑配置、模块与模板、序列化与分析、生成文档核对、新建任务", "RunProfileFormLayout",
            ["ProfileDialog", "ProfileAdvancedDialog", "ProfileAnalysisDialog", "GenerationDialog", "NewTaskDialog"], true, "第一百六十二轮",
            "docs/NOTES.md 第一百六十二轮：配置表单行高上限、无横向滚动条、保存 / 取消在可视区、必填连续"),
        new("项目设置页读取失败态", "RunProfileLoadFailure",
            ["ProjectsPage"], true, "第一百五十一轮",
            "docs/NOTES.md 第一百五十一轮：配置列表读取失败 → 重试 → 恢复，五段全部真实 WinForms 控件"),
        new("项目设置页：从任务详情跳过来时清单没读出来，重试读成功后落回那一行", "RunProfileJumpRetry",
            ["ProjectsPage"], true, "第二百六十六轮",
            "docs/NOTES.md 第二百六十六轮：弄坏磁盘 → 从任务详情跳过来 → 重试仍失败 → 修好再重试 → 选中那一行且状态行是 LandedStatus，四段全部真实控件"),
        new("项目设置页：从任务详情跳过来时筛选框里的词还在不在（在筛选结果里 → 词还在；被筛掉 → 清空后落到那一行）", "RunProfileJumpFilterKeep",
            ["ProjectsPage"], true, "第二百六十八轮",
            "docs/NOTES.md 第二百六十八轮：筛选框敲一个命中目标行的词 → 跳过来 → 词逐字还在、清单仍 1 行、选中那一行、状态行是 LandedStatus；敲一个把那一行筛掉的词 → 跳过来 → 筛选被清空、清单回到全部、选中那一行，两段全部真实控件"),
        new("检查任务页读取失败态", "RunTaskLoadFailure",
            ["TasksPage"], true, "第一百五十二轮",
            "docs/NOTES.md 第一百五十二轮：任务页「读取失败 → 重试 → 恢复」真实控件交互"),
        new("报告与交接读取失败态", "RunReportsLoadFailure",
            ["ReportsDialog"], true, "第一百五十三轮",
            "docs/NOTES.md 第一百五十三轮：报告与交接的读取失败态与重试后恢复"),
        new("任务日志读取失败态", "RunJournalLoadFailure",
            ["JournalDialog"], true, "第一百五十七轮",
            "docs/NOTES.md 第一百五十七轮：任务日志「失败 → 重试 → 恢复」"),
        new("差异确认读取失败态", "RunDecisionsLoadFailure",
            ["DecisionsDialog"], true, "第一百五十三轮",
            "docs/NOTES.md 第一百五十三轮：差异确认的读取失败态与重试后恢复"),
        new("检查任务页详情区（超长任务名 / 路径 / 错误消息）", "RunDetailBreak",
            ["TasksPage"], true, "第一百五十九轮",
            "docs/NOTES.md 第一百五十九轮：120 字任务名、两段各 60 字路径、334 字错误消息落进真实产品数据后逐行量详情区"),
        new("状态行完整错误入口（页面侧）", "RunStatusFullText",
            ["ProjectsPage", "TasksPage"], true, "第一百六十五轮",
            "docs/NOTES.md 第一百六十五轮：短句放得下不给入口、真实失败长句放不下才给、点开正文与状态行逐字相同"),
        new("状态行完整错误入口（对话框侧）", "RunDialogStatusFullText",
            ["ProfileDialog", "GenerationDialog", "SupplementsDialog"], true, "第一百七十六轮",
            "docs/NOTES.md 第一百七十六轮：三处代表屏各四段，真的 Show() 之后判入口显隐（不显示窗体就判可见性＝假门）"),
        new("完整错误正文（点入口打开的那一屏）", "RunDialogStatusFullText",
            ["StatusTextDialog"], true, "第一百七十六轮",
            "docs/NOTES.md 第一百七十六轮：点入口后从 Application.OpenForms 取到这一屏、读标题与正文、再点「关闭」关掉"),
        new("状态行完整错误入口（15 处对话框逐屏）", "RunDialogStatusSweep",
            [], true, "第一百七十六轮",
            "docs/NOTES.md 第一百七十六轮：把三处代表屏扩到全部 15 处有状态行的对话框，每屏三段"),
        new("提示对话框（关于 / 打开目录失败）的复制路径", "RunNoticeCopyClick",
            ["NoticeDialog"], true, "第一百八十五轮",
            "docs/NOTES.md 第一百八十五轮：真点那颗按钮 → 按钮说「复制成功」→ 把剪贴板读回来逐字比对 → 真点「关闭」"),
        new("定向构建输出（复制命令）", "RunBuildCommandCopyClick",
            ["BuildLogDialog"], true, "第二百零三轮",
            "docs/NOTES.md 第二百零三轮：定向构建输出那一屏的「复制命令」真点一遍"),
        new("清单列宽七屏 + 同步预览四块（含「出滚动条那一刻」）", "RunListColumnFit",
            ["ScanResultsDialog", "CandidatesDialog", "PropagationDialog", "EvidenceDialog", "DecisionsDialog", "SyncPreviewDialog"], true, "第二百四十三轮",
            "docs/NOTES.md 第二百四十三轮：七屏各一条「真的显示出来并装进行之后」，读 ClientSize 变化与控件自己的滚动条样式位"),
        new("完整旅程（一条任务走到底）", "RunFullJourney",
            ["ProfileDialog", "NewTaskDialog", "CandidatesDialog", "ApplyPlanDialog"], true, "第一百三十九轮",
            "docs/NOTES.md 第一百三十九轮：八段按 README「使用流程」1–9 步里不触碰 Apifox 写入的那几站走完"),
        new("连接与写入探测", "RunCapabilitiesShown",
            ["CapabilitiesDialog"], true, "第二百四十七轮",
            "docs/NOTES.md 第二百四十七轮：真的 Show() 出来之后量到结果区只剩 82px、清单本体 39px（装得下 1 行），窗体高改按「结果区要给几行」算"),

        // ── 第二百四十八轮起逐屏收口：这一节此前是「未量：冒烟器只 new 一次 + 建句柄，从不显示」，
        //    三屏（契约检查结果 / 接口追踪明细 / 手工填写说明）与最后一屏（扫描警告）已逐轮真的 Show() 出来。
        //    第二百五十二轮起**未量屏为 0**——这一节留着，是因为「哪几屏曾经未量」本身是台账的一部分；
        //    下一轮若新增一屏只构造不显示，按同一形状在末尾补一行 Measured=false 即可，门会当场点名。
        new("契约检查结果", "RunCheckResultsShown", ["CheckResultsDialog"], true, "第二百四十八轮",
            "docs/NOTES.md 第二百四十八轮：真的 Show() 出来之后量到接口清单只剩 140px（扣掉表头装得下 5 行），窗体高改按「接口清单要给几行」算"),
        new("接口追踪明细", "RunEndpointTraceShown", ["EndpointTraceDialog"], true, "第二百五十轮",
            "docs/NOTES.md 第二百五十轮：真的 Show() 出来之后量到接口清单宿主 286px、清单本体 276px（扣掉表头装得下 12 行），窗体高改按「接口清单要给几行」算"),
        new("手工填写说明", "RunManualDraftShown", ["ManualDraftDialog"], true, "第二百五十一轮",
            "docs/NOTES.md 第二百五十一轮：真的 Show() 出来之后量到输入区宿主 213px、输入框 211px（装得下 10 行），窗体高改按「输入区要给几行」算"),
        new("扫描警告", "RunScanWarningsShown", ["ScanWarningsDialog"], true, "第二百五十二轮",
            "docs/NOTES.md 第二百五十二轮：真的 Show() 出来之后量到警告清单宿主 346px、清单本体 336px（装得下 16 行），窗体高改按「警告清单要给几行」算"),
    ];
}
