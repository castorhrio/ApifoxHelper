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
/// （<see cref="Tasks"/> / <see cref="Reports"/> / <see cref="Decisions"/>），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言；配置页的文案仍在 <see cref="ProfileLoadState"/>，
/// 它把通用机制委托到这里——「重试」这个字面全仓只有一处。
/// </para>
/// </summary>
public static class LoadFailureState
{
    /// <summary>失败态的唯一行动按钮文字：配置页、任务页、报告对话框、差异确认对话框同一颗、同一字面。</summary>
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
}
