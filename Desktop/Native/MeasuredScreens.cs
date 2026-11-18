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
        new("注释缺口读取失败态", "RunScanResultsLoadFailure",
            ["ScanResultsDialog"], true, "第三百零五轮",
            "docs/NOTES.md 第三百零五轮：七段真实控件——正常打开不亮那颗按钮且覆盖率行在 → 扫描结果读不出来换成失败态、那句同时指名按钮与任务页 → 点一次换一种失败、句里的原因跟着换 → 修好后清单、计数与覆盖率行回来且那颗按钮收起 → 只弄坏 source-manifest.json 时按同一句话处理（那份「任一读取失败都按扫描结果不可读处理」第一次在真实控件上量到） → 没有扫描产物时不给那颗按钮"),
        new("差异确认读取失败态", "RunDecisionsLoadFailure",
            ["DecisionsDialog"], true, "第一百五十三轮",
            "docs/NOTES.md 第一百五十三轮：差异确认的读取失败态与重试后恢复"),
        new("注释候选读取失败态", "RunCandidatesLoadFailure",
            ["CandidatesDialog"], true, "第三百零四轮",
            "docs/NOTES.md 第三百零四轮：八段真实控件——正常打开不亮那颗按钮 → 候选读不出来换成失败态 → 点一次换一种失败、句里的原因跟着换 → 修好后清单与计数回来 → 扫描结果读不出来时那句同时指名任务页 → 没有候选时不给那颗按钮。这一段当场抓到一处真实缺陷——恢复成功后清单、计数、筛选器都回来了，那颗按钮却还亮着（成功路径不经 ShowHint，那里是唯一的收起处），已按差异确认同一形状补在读取入口开头"),
        new("补充说明读取失败态", "RunSupplementsLoadFailure",
            ["SupplementsDialog"], true, "第三百零四轮",
            "docs/NOTES.md 第三百零四轮：八段真实控件——正常打开不亮那颗按钮 → 检查结果读不出来换成失败态且保存停用 → 点一次换一种失败、句里的原因跟着换 → 修好后接口清单与编辑位回来 → 已保存补充读不出来时那句说明保存停用而按钮亮着 → 尚未检查时不给那颗按钮"),
        new("注释回写读取失败态", "RunApplyPlanLoadFailure",
            ["ApplyPlanDialog"], true, "第三百零四轮",
            "docs/NOTES.md 第三百零四轮：六段真实控件——正常打开时那颗「重新生成计划」按状态停用、屏上没有第二颗「重试」 → 未扫描时那句自己写下一步且那颗按钮停着 → 候选读不出来时提示指名那颗按钮而它回到可用 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次计划与文件清单回来"),
        new("检查结果读取失败态", "RunCheckResultsLoadFailure",
            ["CheckResultsDialog"], true, "第三百零八轮",
            "docs/NOTES.md 第三百零八轮：七段真实控件——准备一份真实检查结果 → 正常打开不亮那颗按钮而清单与计数行在 → 检查结果读不出来换成失败态、那句指名屏上那颗按钮而不再叫用户把窗口关掉重开 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次清单、计数与两个筛选器一起回来且那颗按钮收起 → 筛到零行不给那颗按钮而筛选器仍可用 → 还没有检查结果时那句自己指名任务页那颗「开始检查」而不给那颗按钮"),
        new("代码证据读取失败态", "RunEvidenceLoadFailure",
            ["EvidenceDialog"], true, "第三百零九轮",
            "docs/NOTES.md 第三百零九轮：七段真实控件——准备一份真实代码证据 → 正常打开不亮那颗按钮而清单、计数行与片段都在 → 证据读不出来换成失败态、那句指名屏上那颗按钮而不再叫用户把窗口关掉重开 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次清单、计数与片段一起回来、那颗按钮收起 → 该位置没有直接关联的证据不给那颗按钮 → 那份产物本来就没有也不给那颗按钮"),
        new("接口追踪明细读取失败态", "RunEndpointTraceLoadFailure",
            ["EndpointTraceDialog"], true, "第三百一十轮",
            "docs/NOTES.md 第三百一十轮：七段真实控件——准备一份真实接口追踪数据 → 正常打开不亮那颗按钮而清单、计数行、明细与关键词框都在 → 那份产物读不出来换成失败态、那句以口径文件「读取代码证据失败」开头（同一份 evidence.json 在两屏收成一句，这一判在真实控件上念出来才算）且指名屏上那颗按钮 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次清单、计数与明细一起回来、关键词框可用且那颗按钮收起 → 筛到零行不给那颗按钮而框留着、计数仍是整份的数字 → 那份产物里没有追踪数据不给那颗按钮"),
        new("同步预览勾选页读取失败态", "RunSyncPreviewLoadFailure",
            ["SyncPreviewDialog"], true, "第三百一十二轮",
            "docs/NOTES.md 第三百一十二轮：七段真实控件——准备一份真实检查结果 → 正常打开不亮那颗按钮而勾选清单与关键词框都在 → 检查结果读不出来换成失败态、那句以口径文件「读取检查结果失败」开头（与差异确认、补充说明、检查结果同一句话，这一判在真实控件上念出来才算）且指名屏上那颗按钮 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次勾选清单与关键词框一起回来、那颗按钮收起 → 筛到零行不给那颗按钮而框留着 → 还没有检查结果时那句自己指名任务页那颗「开始检查」而不给那颗按钮"),
        new("生成文档核对读取失败态", "RunPropagationLoadFailure",
            ["PropagationDialog"], true, "第三百一十三轮",
            "docs/NOTES.md 第三百一十三轮：九段真实控件——准备一份真实核对结果 → 正常打开不亮那颗按钮而条目清单与计数行都在 → 核对结果读不出来换成失败态、那句以口径文件「读取核对结果失败」开头且指名屏上那颗按钮 → 点一次换一种失败、句里的原因跟着换 → 失败态下那颗「重试」在 Tab 链里且紧挨着「关闭」 → 修好后点一次条目清单与计数行一起回来、那颗按钮收起并退出 Tab 链 → 筛到零行不给那颗按钮而关键词框留着 → 整份数据没有条目不给那颗按钮 → 尚未核对时那句自己指名任务页那颗「核对传播」而不给那颗按钮"),
        new("响应契约读取失败态", "RunContractsLoadFailure",
            ["ContractsDialog"], true, "第三百一十四轮",
            "docs/NOTES.md 第三百一十四轮：七段真实控件——准备一份真实契约清单 → 正常打开不亮那颗按钮而清单与计数行都在 → 契约读不出来换成失败态、那句以口径文件「读取响应契约失败」开头且指名屏上那颗按钮 → 点一次换一种失败、句里的原因跟着换 → 修好后点一次清单与计数行一起回来、那颗按钮收起 → 没有需要补充的响应契约不给那颗按钮 → 尚未检查时那句自己指名任务页那颗「开始扫描」而不给那颗按钮"),
        new("检查任务页详情区（超长任务名 / 路径 / 错误消息）", "RunDetailBreak",
            ["TasksPage"], true, "第一百五十九轮",
            "docs/NOTES.md 第一百五十九轮：120 字任务名、两段各 60 字路径、334 字错误消息落进真实产品数据后逐行量详情区"),
        new("检查任务页详情区动作按钮的横向可达性", "RunDetailReach",
            ["TasksPage"], true, "第三百六十三轮",
            "docs/DESIGN.md 第三百六十三轮：逐行逐颗量「按钮相对滚动宿主的右边界」与「有没有越出自己那一行的行面」，"
            + "两臂三档各 409 通过 / 0 失败；第三百六十二轮在同一扇门上量到的 ≈200% 档单独一颗进度格比整栏还宽那一处，"
            + "本轮按栏宽折格收口，门里那条放行例外的判据随之删掉（出处同一格）"),
        new("检查任务页详情区换任务回到顶部", "RunDetailScrollReset",
            ["TasksPage"], true, "第三百七十轮",
            "docs/NOTES.md 第三百七十轮：真的显示任务页、真的把详情滚到底（滚不动即报错点名）、"
            + "真的换一行选中，读「详情是不是回到顶部」；反过来同一段还量「刷新同一个任务不抹掉当前位置」——"
            + "只判前者的话「每次刷新都回顶部」也能判绿，而那一条会把用户做完动作后拉回屏顶；"
            + "第三百七十一轮补上键盘路径那一半：真的按一次 End 在清单里换任务（PressKey），读回焦点行确实换了、详情同样回到顶部"),
        new("检查任务页引导横幅那两颗按钮的同一颗与键盘焦点", "RunBannerFocus",
            ["TasksPage"], true, "第三百八十九轮",
            "docs/NOTES.md 第三百八十九轮：四段——前两段第三百六十八轮（真的把键盘焦点放到横幅那颗按钮上、真的走一次刷新，"
            + "读「同一颗实例还在父面里」「焦点还在它身上」；只有主动作的那一步里次动作那颗藏起来而不是摘掉，"
            + "刷新四次控件数与实例都不变）、第三段第三百七十六轮（次动作那颗正拿着键盘时刷新把它藏起来，键盘交给还在屏上的"
            + "主动作那颗）、第四段第三百八十九轮（那颗正拿着键盘而这一次刷新没把它藏起来时键盘原地不动，既不白交一次也不"
            + "顺手交给别一颗）；认那一行按 Ui.BannerRowMarker，不抄用户可见文案"),
        new("检查任务页三阶段进度条跳转之后的键盘落点", "RunStageJumpFocus",
            ["TasksPage"], true, "第三百七十三轮",
            "docs/NOTES.md 第三百七十三轮：真的显示任务页、真的把焦点放到阶段按钮上、真的 PerformClick 一次，"
            + "读「焦点落在那一区第一颗可点按钮上」「那一区一颗都不可点时焦点不往那一区塞」「跳完按一次 Tab 详情不再被拉回顶部」；"
            + "三段在 wine 96 / 120 / 192 三档各跑过一次（三档各 417 通过 / 0 失败），不是真机结果"),
        new("检查任务页作业结束之后状态行那一句结果", "RunJobOutcomeStatus",
            ["TasksPage"], true, "第三百八十六轮",
            "docs/NOTES.md 第三百八十六轮：真的显示任务页、真的起页面自己的 500ms 轮询、真的点扫描区那颗「开始扫描」，"
            + "读三件事——「进度行收起之后状态行逐字写着『源码扫描完成。』、色调 muted、磁盘上真有扫描时间」、「那一句在之后"
            + "三次轮询里逐字未变」与「一个在界面第一次看到它之前就到达终态的作业（API 项目在配置保存之后被删掉），那一句"
            + "失败原因也在屏上说过、色调 bad」；第三百七十八轮那一片此前是「有门无读数」，第三百八十五轮补上前两支、"
            + "并在第三支量到一处产品缺口（欠账只在「界面看到它在跑」那一次记下，几十毫秒内就失败的那一次永远不说），"
            + "第三百八十六轮修掉那一处并把第三支补回段里，三支在 wine 96 / 120 / 192 三档各跑一次，不是真机结果。"
            + "<b>这一屏量的是作业结束之后那一句；「请求已收下而作业还在跑」那一句与「已取消」那一句到现在仍然没有 wine 读数</b>——"
            + "第三百九十一轮按「作业登记同步写下、摘登记只由线程池那一头清掉」推出一条不用造慢作业的量法"
            + "（两击之间一次消息都不泵），段体真按它写了第四、第五支，三档各跑一次、<b>每一档都红那两发</b>，"
            + "于是被这一跑自己证伪：<c>StartScan</c> 在起作业之后自己就调一次 <c>RefreshDetail</c>，UI 线程在那一次重画里"
            + "花掉的时间比这一页最快那支作业跑得完的时间还长，那一击落在「已终态、还没摘登记」的窗口里，"
            + "而 <c>ShowJobState</c> 给那个窗口的答案是那颗钮可见但点不动（产品在这里没错，无物可停时本就不该点得动）。"
            + "逐字红话留在 ApifoxHelper.Smoke/Program.cs 那一段的注释里；那两句的停留时长与实际字色仍随 G-D5 真机复核"),
        new("检查任务页作业区那两颗钮的键盘落点", "RunJobFocusHandover",
            ["TasksPage"], true, "第三百九十三轮",
            "docs/NOTES.md 第三百九十二轮：真的显示任务页、真的把键盘放到那颗「开始扫描」上、真的起一支在跑的作业"
            + "（作业体空着、只等冒烟器放行——这一页最快的一支作业几十毫秒就跑完，界面来不及看见它在跑），"
            + "读两支——「作业真在跑、这一行换成进度 + 停止时，键盘落在那颗『停止』上」与「作业结束之后，键盘回到那颗"
            + "『开始扫描』上」；第三百九十三轮补第三支（跨行）：真的把键盘放到横幅那颗「开始扫描」上、真的起一支在跑的作业，"
            + "读「横幅那两颗被停用之后，键盘落到作业区那颗『停止』上」；判据与落点在 Desktop/Native/FocusHandover.cs"
            + "（算式只有一份，横幅那两颗与作业区那两颗共用），门在 ApifoxHelper.Tests/JobFocusPlanTests.cs；不是真机结果"),
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
        new("同步预览勾选页的键盘流（真的按一次 Ctrl+A）", "RunSelectKeyboard",
            ["SyncPreviewDialog"], true, "第二百八十六轮",
            "docs/NOTES.md 第二百八十六轮：真的按一次 Ctrl+A（真键经系统键入，裸 Ctrl + A 进清单），可见行里可同步的勾上、不可同步的没勾、筛到只剩几行时藏起来的行一个都不动；第二百八十七轮同一段里另判「清空」按勾选数启停的三态（打开时禁用 → 勾上后可用 → 清空后回到禁用）"),
        new("同步预览勾选页 ↔ 预览页往返（真的点一次「查看已有计划」再点一次「重新选择接口」）", "RunSelectRoundTrip",
            ["SyncPreviewDialog"], true, "第二百九十二轮",
            "docs/NOTES.md 第二百九十二轮：真的点一次「查看已有计划」再点一次「重新选择接口」——勾选（含被关键词筛掉、清单里看不见的那些）一个不丢、计数行与「生成同步预览」跟着回来、键盘回到清单上、空格仍翻得动焦点行"),
        new("同步预览勾选页的筛选手势（真的按一次 Ctrl+F、一次 Enter、一次 Esc）", "RunSelectFilterGestures",
            ["SyncPreviewDialog"], true, "第二百九十七轮",
            "docs/NOTES.md 第二百九十七轮：真的按一次 Ctrl+F（焦点交给关键词框、框里已有关键词整段被选中）、键入即筛选、框里 Esc 清空关键词回到全部行且对话框不关、勾选含被筛掉的一个不丢、框里 Enter 落在筛选结果第一行、清单上 Esc 仍是取消"),
        new("差异确认 / 检查结果两屏的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGesture",
            ["DecisionsDialog", "CheckResultsDialog"], true, "第三百一十七轮",
            "docs/NOTES.md 第三百一十七轮：七段真实控件——两份真实落盘的检查结果读回来数条目 → 差异确认打开即选中第一行且键盘真的在清单上（Ctrl+C 的前提） → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行声明的「资源」列原文且不等于首列「类型」列原文 → 按方向键换一行再按，剪贴板跟着换到那一行 → Ctrl+Shift+C 不吞键、剪贴板不动 → 焦点行未选中时按 Ctrl+C 剪贴板不动 → 检查结果（默认标识列 = 首列「接口」）同样逐字对得上"),
        new("任务日志的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureJournal",
            ["JournalDialog"], true, "第三百一十九轮",
            "docs/NOTES.md 第三百一十九轮：四段真实控件——打开即选中最后一条且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「摘要」列原文且不等于首列「时间」 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("代码证据的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureEvidence",
            ["EvidenceDialog"], true, "第三百一十九轮",
            "docs/NOTES.md 第三百一十九轮：四段真实控件——打开即选中第一条且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「证据摘要」列原文且不等于首列「分类」 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("同步预览写入清单的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureSyncPreview",
            ["SyncPreviewDialog"], true, "第三百二十轮",
            "docs/NOTES.md 第三百二十轮：四段真实控件——点「查看已有计划」进预览页后写入清单打开即选中第一条动作且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「资源」列原文且不等于首列「序号」 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("补充说明的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureSupplements",
            ["SupplementsDialog"], true, "第三百二十一轮",
            "docs/NOTES.md 第三百二十一轮：四段真实控件——打开即选中第一个接口且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「接口」格原文且不等于邻列「名称」 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("接口追踪明细的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureEndpointTrace",
            ["EndpointTraceDialog"], true, "第三百二十一轮",
            "docs/NOTES.md 第三百二十一轮：四段真实控件——打开即选中第一个接口且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「接口」格原文且不等于邻列「读取方法」 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("响应契约的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureContracts",
            ["ContractsDialog"], true, "第三百二十二轮",
            "docs/NOTES.md 第三百二十二轮：四段真实控件——打开即选中第一条契约且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「接口」格原文且不等于邻列「封装」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("注释候选的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureCandidates",
            ["CandidatesDialog"], true, "第三百二十三轮",
            "docs/NOTES.md 第三百二十三轮：四段真实控件——打开即选中第一条候选且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「位置（说明点）」格原文且不等于邻列「标注」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("注释缺口的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureScanResults",
            ["ScanResultsDialog"], true, "第三百二十四轮",
            "docs/NOTES.md 第三百二十四轮：四段真实控件——打开即选中第一条位置且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「位置（说明点）」格原文且不等于邻列「标注」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("注释回写文件清单的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureApplyPlan",
            ["ApplyPlanDialog"], true, "第三百二十五轮",
            "docs/NOTES.md 第三百二十五轮：五段真实控件——扫描两个控制器各一处缺口并各接受一条候选（夹具真的落盘） → 打开即选中第一个文件且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「源码文件」格原文且不等于邻列「回写范围」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("生成文档核对的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGesturePropagation",
            ["PropagationDialog"], true, "第三百五十一轮",
            "docs/NOTES.md 第三百五十一轮：五段真实控件——准备一份真实核对结果并读回来（三条条目、位置互不相同，产品自己的写入口径落盘） → 打开即选中第一条位置且键盘真的在清单上 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「位置」格原文且不等于邻列「标注」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("生成文档核对的 XML 文件清单的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGesturePropagationXmlFiles",
            ["PropagationDialog"], true, "第三百五十五轮",
            "docs/NOTES.md 第三百五十五轮：五段真实控件——准备一份真实核对结果并读回来（三个 XML 文件、项目与路径互不相同，产品自己的写入口径落盘） → 点一行之后键盘真的在清单上（这张清单打开时不自动选中，落点由用户点出来） → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「XML 文档文件」格原文且不等于首列「项目」格原文 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("同步预览勾选页接口清单的列表 Ctrl+C 复制（真的按一次 Ctrl+C，把剪贴板读回来逐字比对）", "RunListCopyGestureSyncSelect",
            ["SyncPreviewDialog"], true, "第三百五十七轮",
            "docs/NOTES.md 第三百五十七轮：五段真实控件——打开即选中第一条接口、键盘真的在清单上，且这一张确实是复选清单 → 真的按一次 Ctrl+C，剪贴板逐字等于焦点行「接口」格原文且不等于邻列「名称」格原文 → 按一次空格把焦点行勾上之后标识格没被改写、再按 Ctrl+C 剪贴板仍是同一个接口格 → 按方向键换一行再按，剪贴板跟着换 → 焦点行未选中时按 Ctrl+C 剪贴板不动"),
        new("对话框主动作的键盘入口（真的按一次 Ctrl+Enter：走掉一条、选中交给下一条、带 Shift 不动作、按钮禁用时不动作）", "RunPrimaryActionGesture",
            ["CandidatesDialog"], true, "第三百五十九轮",
            "docs/NOTES.md 第三百五十九轮：四段真实控件——打开即选中第一条候选、键盘真的在清单上、「接受并下一条」可用且可见 → 真的按一次 Ctrl+Enter（经系统键入，按键先到窗体的 ProcessCmdKey），磁盘上第一条变成 accepted、状态行取 CandidateEdit.ReviewDoneText 单一口径、选中交给下一条 → Ctrl+Shift+Enter 不属本手势，再按一次第二条仍是 pending、选中仍在原处、状态行一个字没变 → 筛到零行让主动作禁用，按 Ctrl+Enter 对话框仍开着、磁盘不变、状态行没被改写"),
        new("任务列表的列头排序（真的点一次列头：自然序重排、方向标记、换列、不写任务数据、选中按 Id 恢复、排序记忆跨启动）", "RunColumnSortGesture",
            ["TasksPage"], true, "第三百二十七轮",
            "docs/NOTES.md 第三百二十七轮：六段真实控件——三份真配置与三个真任务（任务名带序号、配置名与任务名顺序错开） → 点之前清单是数据源自身顺序且表头无方向标记 → 真的点一次「任务」列头（真鼠标：取表头窗口位置 → 按 ColumnHeaderClick 算落点 → 移光标 → 按下松开），行序按自然序重排且表头画出「任务 ▲」 → 再点一次反向且表头换成「任务 ▼」 → 换点「配置」列按配置名升序且原列标记撤掉 → 排序只改呈现顺序：三个 task.json 一字未动、选中的任务按 Id 恢复；第三百二十八轮补两段：排序记忆真的落进 list-sort.json（记的是「配置」这个标题与方向，不是列号）→ 关掉这一页、用同一份记忆重新打开（模拟下次启动），清单直接按上次那一列同一方向排、表头带同一个方向标记，一次都没再点"),
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
