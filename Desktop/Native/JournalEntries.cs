using System.Globalization;
using System.Text.Json.Nodes;
using ApifoxHelper.Tasks;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 任务日志（<c>journal.jsonl</c>）的读法口径（方案 §8.6「审计记录」），不依赖 WinForms，
/// 以共享源码编入测试工程在 Linux 断言。写入形状在 <see cref="JournalLines"/> 一处，
/// 读法只在这里一处——界面不就地认字段名、不就地拼事件名与状态文字。
///
/// 这一屏存在的理由不是「把文件摊开」：日志是执行意图与逐项结果的追加记录，
/// 它回答的是「做到哪一步了」——最后一行就是答案，而只有意图、没有结果的那几个动作
/// 说明进程在写入途中中断过，远端结果未知（<see cref="UnfinishedActions"/>）。
/// 事件名与状态文字一律取既有口径（<see cref="TaskDisplay.PlanActionKindText"/>、
/// <see cref="TaskDisplay.PlanActionStateText"/>、<see cref="TaskDisplay.FileApplyStateText"/>、
/// <see cref="TaskDisplay.SyncPlanStatusText"/>），与写入清单、回写结果讲同一件事时用同一批字。
/// </summary>
public static class JournalEntries
{
    /// <summary>这一屏的说明行：日志是什么、按什么顺序读、最后一行意味着什么。</summary>
    public const string Caption = "任务日志按时间顺序记录注释回写与同步执行的每一步（只读）；最后一行就是做到的那一步。";

    /// <summary>没有日志时如实说明，不留一块空白（§7「空态不留白」）。</summary>
    public const string EmptyText = "这个任务还没有执行记录。注释回写与同步执行会把每一步追加到任务日志，执行过之后这里就能看到。";

    /// <summary>认不出的事件名：不猜它是什么，也不把它当成已知事件显示。</summary>
    public const string UnknownEventText = "未识别事件";

    /// <summary>时间缺失或读不出时的占位（与接口追踪明细的「—」同一取舍：占位不是内容）。</summary>
    public const string MissingTimeText = "—";

    /// <summary>
    /// 只有意图、没有结果的那一行在清单上的标记（摘要格后缀）。
    /// 与 <see cref="UnfinishedCaption"/> 说的是同一件事、同一批字：清单上标出来，
    /// 用户不必拿那句话里的名字回清单里逐行对。
    /// </summary>
    public const string UnfinishedMark = "结果未知";

    /// <summary>
    /// 意图行没写清「写的是哪个资源、什么动作」时的名字（半行、字段缺失）。
    /// 不拿内部动作标识顶上——那串东西用户认不出、也做不了什么（与「不拿空串当动作名」同一取舍）。
    /// </summary>
    public const string UnknownActionText = "未记录资源与动作类型";

    /// <summary>
    /// 只有写入意图、没有结果记录的一个动作：动作标识（只用于与结果行配对，不上屏幕）、
    /// 动作类型与资源（这两个才是用户认得出、能拿去重新检查的东西）。
    /// </summary>
    public sealed record UnfinishedAction(string ActionId, string ActionKind, string ResourceKey);

    /// <summary>事件名（<c>kind</c>）的中文映射。写入点与读取点认的是同一批常量，界面不就地拼字面。</summary>
    public static string EventText(string? kind) => kind switch
    {
        JournalLines.CommentApplyIntent => "注释回写：准备写入",
        JournalLines.CommentApplyResult => "注释回写：结果",
        JournalLines.PlanStart => "同步执行：开始",
        JournalLines.Intent => "同步执行：准备写入",
        JournalLines.ActionResult => "同步执行：结果",
        JournalLines.PlanEnd => "同步执行：结束",
        null or "" => UnknownEventText,
        _ => UnknownEventText + "（" + kind + "）",
    };

    /// <summary>计数行：这一屏有几条记录。按整份日志统计，不随选中变化。</summary>
    public static string CountsLine(int count) =>
        "记录 " + count.ToString(CultureInfo.InvariantCulture) + " 条";

    /// <summary>
    /// 时间格的时间戳（第一百零四轮）：日志里的 <c>at</c> 是带偏移的时间戳，读不出（缺字段、不是时间、
    /// 进程中断留下的半行）交 null——不拿当前时间顶上，那会把「不知道什么时候」说成「刚刚」。
    /// 时间格与时间列的排序键取同一份解析结果（<see cref="TimeText"/> 只是把它格式化），
    /// 两处必然一起漂或一起对：排序按时间戳而不是按显示文字，读不出时间的那几行交 null（视为最小）。
    /// </summary>
    public static DateTimeOffset? TimeKey(JsonNode? line)
    {
        var raw = Text(line, "at");
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null;
    }

    /// <summary>
    /// 时间格：按本机时区显示到秒；读不出时给占位（<see cref="MissingTimeText"/>）。
    /// 取值来自 <see cref="TimeKey"/> 那一份解析，不另解析一遍。
    /// </summary>
    public static string TimeText(JsonNode? line) =>
        TimeKey(line) is { } at
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : MissingTimeText;

    /// <summary>
    /// 摘要格：这一行在屏幕之外还能被指代的东西（标识列声明在这一格）。
    /// 六个事件各说各的那件事——注释回写说文件与逐文件结果，同步执行说动作、资源与逐动作结果，
    /// 计划开始与结束说计划与状态；状态文字取 <see cref="TaskDisplay"/> 同一份，不另造一套。
    /// </summary>
    public static string SummaryText(JsonNode? line)
    {
        var kind = Text(line, "kind");
        var message = Text(line, "message");
        return kind switch
        {
            JournalLines.CommentApplyIntent => Text(line, "file"),
            JournalLines.CommentApplyResult => Join(Text(line, "file"), TaskDisplay.FileApplyStateText(Text(line, "state")), message),
            JournalLines.PlanStart => Join("计划 " + Text(line, "planId"), "指纹 " + ShortHash(Text(line, "hash"))),
            JournalLines.Intent => IntentText(line),
            JournalLines.ActionResult => ActionResultText(line, message),
            JournalLines.PlanEnd => TaskDisplay.SyncPlanStatusText(Text(line, "status")),
            _ => "",
        };
    }

    /// <summary>
    /// 清单一行三格：时间 / 事件 / 摘要（摘要即标识列，Ctrl+C 复制到的就是这一格）。
    /// 传了未结束动作集合时，只有意图、没有结果的那一行在摘要格后缀 <see cref="UnfinishedMark"/>——
    /// 这一屏要回答的是「做到哪一步」，而「这一步没有结果」正是清单上缺的那一行，标在原处比只在
    /// 上面那句话里数个数更好找（那句话仍给出名字与下一步，两处说的是同一件事）。
    /// </summary>
    public static string[] Row(JsonNode? line, IReadOnlySet<string>? unfinishedActionIds = null) =>
        [TimeText(line), EventCell(line), SummaryCell(line, unfinishedActionIds)];

    /// <summary>
    /// 事件格（第二格）：这一行的事件名，取 <see cref="EventText"/> 那一份中文映射。
    /// 清单第二格与事件列的排序键取同一份（第一百零四轮），界面不就地认 <c>kind</c> 字段。
    /// </summary>
    public static string EventCell(JsonNode? line) => EventText(Text(line, "kind"));

    /// <summary>
    /// 摘要格（含「结果未知」标记）：清单第三格、关键词匹配字段与摘要列的排序键取同一份——
    /// 三处各拼一遍必然有一处先漂（第一百零四轮把这一格提出来，排序键不再另算一套）。
    /// </summary>
    public static string SummaryCell(JsonNode? line, IReadOnlySet<string>? unfinishedActionIds = null)
    {
        var summary = SummaryText(line);
        return unfinishedActionIds is not null && IsUnfinishedIntent(line, unfinishedActionIds)
            ? Join(summary, UnfinishedMark)
            : summary;
    }

    /// <summary>
    /// 关键词匹配字段（DESIGN.md §6「列表筛选」）：这一行在屏幕上被读作的那两格——事件名与摘要。
    /// 直接取 <see cref="Row"/> 的后两格，与单元格同源（不另造一套字段，两处必然一起漂或一起对）；
    /// 时间列是时间戳、不进关键词（与两个主页面「时间列不进关键词」同一取舍）。
    /// 摘要里带着「结果未知」标记，故传了未结束动作集合时敲「结果未知」就能把那几条找出来。
    /// </summary>
    public static string[] KeywordFields(JsonNode? line, IReadOnlySet<string>? unfinishedActionIds = null)
    {
        var cells = Row(line, unfinishedActionIds);
        return [cells[1], cells[2]];
    }

    /// <summary>
    /// 只有写入意图、没有结果记录的动作（按出现顺序）。
    /// 这是日志存在的理由：意图先落盘，进程在写入途中中断时，远端到底写没写只能靠这一条看出来。
    /// 只认同步执行的写入意图——注释回写写的是本机源码文件，中断与否由逐文件结果与文件哈希核对，
    /// 不在这里猜；认不出标识的意图行（半行、字段缺失）不列进来，不拿空串当动作名。
    /// </summary>
    public static IReadOnlyList<UnfinishedAction> UnfinishedActions(IEnumerable<JsonNode?> lines)
    {
        var intents = new List<UnfinishedAction>();
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var kind = Text(line, "kind");
            var actionId = Text(line, "actionId");
            if (actionId.Length == 0) continue;
            if (kind == JournalLines.Intent)
            {
                intents.Add(new UnfinishedAction(actionId, Text(line, "actionKind"), Text(line, "resourceKey")));
            }
            else if (kind == JournalLines.ActionResult)
            {
                finished.Add(actionId);
            }
        }
        return intents.Where(action => !finished.Contains(action.ActionId)).ToList();
    }

    /// <summary>
    /// 未结束动作的名字：动作类型 + 资源（与写入清单、日志摘要同一批字，取 <see cref="TaskDisplay"/> 一份）。
    /// 意图行没写清这两样时如实说「未记录资源与动作类型」，不把内部动作标识摆到屏幕上——
    /// 用户拿它做不了任何事，而这一屏要他做的是「重新检查确认远端状态」。
    /// </summary>
    public static string UnfinishedActionText(UnfinishedAction action)
    {
        var name = Name(TaskDisplay.PlanActionKindText(action.ActionKind), action.ResourceKey);
        return name.Length == 0 ? UnknownActionText : name;
    }

    /// <summary>
    /// 未结束动作那句话：没有这样的动作时给空串（界面据此不显示这一行，不显示一句「没有」）。
    /// 句子说的是「结果未知」而不是「失败」——没有结果记录不等于没写进去，重发之前要先重新检查。
    /// 列出来的是动作的名字（类型 + 资源），不是内部动作标识。
    /// </summary>
    public static string UnfinishedCaption(IReadOnlyList<UnfinishedAction> actions) =>
        actions.Count == 0
            ? ""
            : "有 " + actions.Count.ToString(CultureInfo.InvariantCulture)
              + " 个动作只有写入意图、没有结果记录（进程可能中断），远端结果未知："
              + string.Join("、", actions.Select(UnfinishedActionText)) + "。请重新检查确认远端状态后再决定是否重发。";

    /// <summary>这一行是不是「只有意图、没有结果」的那一行（清单标记与那句话认的是同一份集合）。</summary>
    private static bool IsUnfinishedIntent(JsonNode? line, IReadOnlySet<string> unfinishedActionIds) =>
        Text(line, "kind") == JournalLines.Intent && unfinishedActionIds.Contains(Text(line, "actionId"));

    private static string IntentText(JsonNode? line)
    {
        var head = Name(TaskDisplay.PlanActionKindText(Text(line, "actionKind")), Text(line, "resourceKey"));
        // 有远端 ID 是更新、没有是新建：这一格回答「写的是哪个资源、是新建还是改」。
        return line?["remoteId"] is JsonValue remote && remote.TryGetValue<long>(out var remoteId)
            ? head + "（远端 " + remoteId.ToString(CultureInfo.InvariantCulture) + "）"
            : head + "（新建）";
    }

    private static string ActionResultText(JsonNode? line, string message)
    {
        var resource = Text(line, "resourceKey");
        var state = TaskDisplay.PlanActionStateText(Text(line, "state"));
        var created = line?["createdId"] is JsonValue value && value.TryGetValue<long>(out var createdId)
            ? "新建 ID " + createdId.ToString(CultureInfo.InvariantCulture)
            : "";
        return Join(resource, state, created, message);
    }

    /// <summary>指纹只显示前 8 位：这一格用来核对「执行的是哪一份计划」，不是把整串哈希摊在屏幕上。</summary>
    private static string ShortHash(string hash) => hash.Length <= 8 ? hash : hash[..8];

    /// <summary>按「 · 」拼非空片段：空字段不留下一个孤零零的分隔符。</summary>
    private static string Join(params string[] parts) =>
        string.Join(" · ", parts.Where(part => part.Length > 0));

    /// <summary>按空格拼非空片段（「动作类型 + 资源」这一种名字）：缺一段时不留下多余的空格。</summary>
    private static string Name(params string[] parts) =>
        string.Join(" ", parts.Where(part => part.Length > 0));

    /// <summary>读一个字符串字段：缺失、不是字符串、半行都按空串处理（不抛、不猜）。</summary>
    private static string Text(JsonNode? line, string name) =>
        line?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
