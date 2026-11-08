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
    /// 时间格：日志里的 <c>at</c> 是带偏移的时间戳，按本机时区显示到秒。
    /// 读不出（缺字段、不是时间、进程中断留下的半行）给占位，不拿当前时间顶上——
    /// 那会把「不知道什么时候」说成「刚刚」。
    /// </summary>
    public static string TimeText(JsonNode? line)
    {
        var raw = Text(line, "at");
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : MissingTimeText;
    }

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

    /// <summary>清单一行三格：时间 / 事件 / 摘要（摘要即标识列，Ctrl+C 复制到的就是这一格）。</summary>
    public static string[] Row(JsonNode? line) => [TimeText(line), EventText(Text(line, "kind")), SummaryText(line)];

    /// <summary>
    /// 只有写入意图、没有结果记录的动作标识（按出现顺序）。
    /// 这是日志存在的理由：意图先落盘，进程在写入途中中断时，远端到底写没写只能靠这一条看出来。
    /// 只认同步执行的写入意图——注释回写写的是本机源码文件，中断与否由逐文件结果与文件哈希核对，
    /// 不在这里猜；认不出标识的意图行（半行、字段缺失）不列进来，不拿空串当动作名。
    /// </summary>
    public static IReadOnlyList<string> UnfinishedActions(IEnumerable<JsonNode?> lines)
    {
        var intents = new List<string>();
        var finished = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var kind = Text(line, "kind");
            var actionId = Text(line, "actionId");
            if (actionId.Length == 0) continue;
            if (kind == JournalLines.Intent) intents.Add(actionId);
            else if (kind == JournalLines.ActionResult) finished.Add(actionId);
        }
        return intents.Where(id => !finished.Contains(id)).ToList();
    }

    /// <summary>
    /// 未结束动作那句话：没有这样的动作时给空串（界面据此不显示这一行，不显示一句「没有」）。
    /// 句子说的是「结果未知」而不是「失败」——没有结果记录不等于没写进去，重发之前要先重新检查。
    /// </summary>
    public static string UnfinishedCaption(IReadOnlyList<string> actionIds) =>
        actionIds.Count == 0
            ? ""
            : "有 " + actionIds.Count.ToString(CultureInfo.InvariantCulture)
              + " 个动作只有写入意图、没有结果记录（进程可能中断），远端结果未知："
              + string.Join("、", actionIds) + "。请重新检查确认远端状态后再决定是否重发。";

    private static string IntentText(JsonNode? line)
    {
        var kindText = TaskDisplay.PlanActionKindText(Text(line, "actionKind"));
        var resource = Text(line, "resourceKey");
        var head = resource.Length == 0 ? kindText : kindText + " " + resource;
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

    /// <summary>读一个字符串字段：缺失、不是字符串、半行都按空串处理（不抛、不猜）。</summary>
    private static string Text(JsonNode? line, string name) =>
        line?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
}
