namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 读取失败态的通用口径（第一百五十二轮，具名 pass：harden）。
/// <para>
/// <b>为什么要有这个文件</b>：第一百五十轮给配置页补了「读取失败 → 唯一行动按钮「重试」」，
/// 但那只落在配置页一处。同一族缺陷在另外三屏仍然成立：
/// 任务页 <c>LoadTasks</c> 的 catch 只往状态行写一句红字（清单区保持上一次的样子，首次进入是整块空白）；
/// 报告对话框与差异确认对话框的读取失败只给一句「请关闭本窗口重新打开」「请关闭后重试」——
/// 把「再读一次」这件事交给用户手动重开窗口，屏上没有任何可点的入口。
/// 这与 DESIGN.md §7「禁用不得移除下一步」是同一条纪律：失败态也是空态的一种，
/// 它同样欠用户一句「现在能做什么」和一个能点的入口。
/// </para>
/// <para>
/// 本文件只放<b>通用机制</b>（按钮文字、三态门控、状态行拼装、指名入口的指引）与<b>各屏文案</b>
/// （<see cref="Tasks"/> / <see cref="Reports"/> / <see cref="Journal"/> / <see cref="Decisions"/> /
/// <see cref="Candidates"/> / <see cref="Supplements"/> / <see cref="ApplyPlan"/>），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言；配置页的文案仍在 <see cref="ProfileLoadState"/>，
/// 它把通用机制委托到这里——「重试」这个字面全仓只有一处。
/// <b>这份枚举与那份封闭清单是一件事的两头</b>（第三百零三轮补的一句）：
/// <c>ApifoxHelper.Tests/LoadFailureRetryCoverageTests.cs</c> 的发现口径是「引用本文件的界面源码即算有失败态」，
/// 并把发现到的屏钉成封闭清单。所以新增一屏失败态要同时做两处——在这里补文案、去那里补名单；
/// 只改一处时那一头会当场点名它，不会出现「文案在、接线没人判」。
/// </para>
/// </summary>
public static class LoadFailureState
{
    /// <summary>
    /// 失败态的唯一行动按钮文字：配置页、任务页，以及报告 / 差异确认 / 任务日志 / 注释候选 / 补充说明
    /// 五处对话框同一颗、同一字面。唯一的例外是注释回写对话框——那一屏不新增这颗按钮，
    /// 失败提示指名它自己那颗「重新生成计划」（形状见 <see cref="RetryHint"/> 与 <see cref="ApplyPlan"/>）。
    /// </summary>
    public const string RetryLabel = "重试";

    /// <summary>
    /// 失败态是否显示「重试」按钮：只在读取失败时显示。
    /// 三种状态（没有数据 / 筛到零行 / 读取失败）同一时刻只显示一个行动按钮——
    /// 失败时给「新建」等于把用户往另一条路上引，而这一屏真正缺的是「再读一次」。
    /// </summary>
    public static bool RetryVisible(bool failed) => failed;

    /// <summary>状态行：失败原因（已脱敏）跟在标题后面，形状与「读取任务失败：…」一致。</summary>
    public static string FailureStatus(string title, string detail) => title + "：" + detail;

    /// <summary>
    /// 原因末尾的句读（全角与半角都算）。拼说明之前先去掉，否则会出现「.。」「。。」这种双标点。
    /// <para>
    /// 为什么需要它：原因来自 <c>Exception.Message</c>，形状不由我们决定——.NET 的
    /// <c>IOException</c> 是英文句子、以半角句点收尾（「… because it is being used by another process.」），
    /// 而中文侧的原因以「。」收尾。第一百五十八轮 wine 实测的任务日志失败提示就是
    /// 「… another process.。任务目录可能被占用…」——两个句号挨在一起，是用户能看见的排版缺陷。
    /// </para>
    /// </summary>
    private static readonly char[] SentenceEnds = ['。', '.', '；', ';', '，', ',', '！', '!', '？', '?', ' '];

    /// <summary>
    /// 对话框里的失败文案：标题 + 原因 + 一句可操作说明（说明里指名那颗「重试」按钮）。
    /// 原因末尾的句读先去掉再补一个「。」，故不出现「.。」「。。」「。；」这种双标点；
    /// 原因整段为空时不留一个孤零零的「：。」。
    /// </summary>
    public static string FailureMessage(string title, string detail, string hint)
    {
        var reason = detail.TrimEnd(SentenceEnds);
        return (reason.Length == 0 ? title : FailureStatus(title, reason)) + "。" + hint;
    }

    /// <summary>
    /// 对话框里「读取项目列表」「读取分支」这类失败时补的一句：指名那颗按钮本身就是重试入口。
    /// 这类对话框不新增第二个「重试」按钮——同一屏两颗做同一件事的按钮违反「唯一行动按钮」，
    /// 而那颗按钮在失败后已经回到可用（<c>finally</c> 里按门控复位），点它就是重试。
    /// </summary>
    public static string RetryHint(string entryLabel) => "可点「" + entryLabel + "」重试。";

    /// <summary>
    /// 对话框失败态的状态行：前缀 + 失败描述 + 重试指引。
    /// 描述末尾的句读（含英文异常消息的半角句点）先去掉再接「；」，否则会出现「…。；」「….；」这种双标点。
    /// </summary>
    public static string PickerFailureStatus(string prefix, string detail, string entryLabel) =>
        prefix + detail.TrimEnd(SentenceEnds) + "；" + RetryHint(entryLabel);

    /// <summary>任务目录读不出来时的共同说明：原因猜测 + 指名屏上那颗「重试」。</summary>
    public const string DirectoryHint = "任务目录可能被占用或权限不足；点「重试」再读一次。";

    /// <summary>任务页（<c>TasksPage</c>）的失败态文案：与配置页同一形状，只换「配置」为「任务」。</summary>
    public static class Tasks
    {
        public const string Title = "读取任务失败";
        public const string Hint = DirectoryHint;
    }

    /// <summary>报告对话框（<c>ReportsDialog</c>）的失败态文案：这一屏的动作是「生成」而不是「读」。</summary>
    public static class Reports
    {
        public const string Title = "报告生成失败";
        public const string Hint = "任务目录可能被占用或权限不足；点「重试」再生成一次。";
    }

    /// <summary>
    /// 任务日志对话框（<c>JournalDialog</c>）的失败态文案（第一百五十七轮）。
    /// 这一屏此前写的是「请关闭后重新执行一次再试」——把「再读一次」交给用户手动重开窗口，
    /// 与报告对话框、差异确认对话框第一百五十二轮修掉的是同一族缺陷。
    /// </summary>
    public static class Journal
    {
        public const string Title = "读取任务日志失败";
        public const string Hint = DirectoryHint;
    }

    /// <summary>
    /// 差异确认对话框（<c>DecisionsDialog</c>）的两种失败态：检查结果读不出来、已保存决策读不出来。
    /// 后者仍保留「保存已停用」这句——读不到既有决策时保存会用空决策覆盖用户的选择，
    /// 这条约束不因为多了「重试」而放松。
    /// </summary>
    public static class Decisions
    {
        public const string Title = "读取检查结果失败";
        public const string Hint = DirectoryHint;
        public const string SavedTitle = "读取已保存决策失败";
        public const string SavedHint = "为避免覆盖已有决策，保存已停用；点「重试」再读一次。";
    }

    /// <summary>
    /// 注释候选对话框（<c>CandidatesDialog</c>）的两种失败态：候选读不出来、扫描结果读不出来
    /// （第三百零三轮）。这一屏此前写的是「请关闭后重试；仍失败时重新扫描再打开」——屏上没有一颗
    /// 按钮做「再读一次」这件事，那句话把用户支去重开窗口，与报告、差异确认（第一百五十二轮）、
    /// 任务日志（第一百五十七轮）修掉的是同一族缺陷。
    /// <para>
    /// 两种失败的下一步不同，各写一句：<see cref="DraftsHint"/> 只可能是任务目录读不出来（候选就在里面），
    /// 重试就是再读一次；<see cref="SlotsHint"/> 还多一条出路——扫描结果本身缺失时重试读不到新东西，
    /// 那一步在检查任务页的「开始扫描」上，所以那句把两处都指名。
    /// </para>
    /// </summary>
    public static class Candidates
    {
        public const string DraftsTitle = "读取注释候选失败";
        public const string DraftsHint = DirectoryHint;
        public const string SlotsTitle = "读取扫描结果失败";
        public const string SlotsHint = "点「重试」再读一次；扫描结果仍读不到时回「" + PageNames.Tasks + "」页重新扫描。";
    }

    /// <summary>
    /// 补充说明对话框（<c>SupplementsDialog</c>）的两种失败态（第三百零三轮）：检查结果读不出来、
    /// 已保存补充读不出来。前者与 <see cref="Decisions"/> 读的是同一份 diff.json，两处此前一个给
    /// 「点「重试」再读一次」、一个给「请关闭后在「检查任务」页重新检查再试」——同一件事两种说法，
    /// 其中一种屏上没有可点的入口。后者与 <see cref="Decisions.SavedHint"/> 同形：保存停用的约束不放松。
    /// </summary>
    public static class Supplements
    {
        public const string Title = "读取检查结果失败";
        public const string Hint = DirectoryHint;
        public const string SavedTitle = "读取已保存补充失败";
        public const string SavedHint = "为避免覆盖已有补充，保存已停用；点「重试」再读一次。";
    }

    /// <summary>
    /// 注释回写对话框（<c>ApplyPlanDialog</c>）的失败态：生成计划是这一屏的第一次读取，它失败时
    /// 这一屏同样只剩一句「请关闭后重试」（第三百零三轮）。
    /// <para>
    /// <b>这一屏不新增第二颗「重试」按钮</b>：屏上那颗「重新生成计划」点下去调的就是首次读取的那个
    /// 入口，与 <see cref="RetryHint"/> 说的那一支同一形状（同一屏两颗做同一件事的按钮违反「唯一行动按钮」）。
    /// 失败提示因此指名那颗按钮，而那颗按钮在失败态必须回到可用——按钮名由调用方交进来，
    /// 本文件不复制第二份字面。
    /// </para>
    /// </summary>
    public static class ApplyPlan
    {
        public const string Title = "生成注释回写预览失败";

        /// <summary>重试指引 + 仍失败时的那一条出路（在「检查任务」页的「开始扫描」上）。</summary>
        public static string Hint(string entryLabel) =>
            RetryHint(entryLabel) + "仍失败时回「" + PageNames.Tasks + "」页重新扫描。";
    }
}
