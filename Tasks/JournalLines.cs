namespace ApifoxHelper.Tasks;

/// <summary>
/// 任务日志（<c>journal.jsonl</c>）的行形状口径（方案 §8.6「审计记录」：执行意图与逐项结果追加写入任务日志）。
/// 六个事件各一行、字段名互不冲突，写入点只有 <c>CommentPlanService</c>（注释回写）与
/// <c>SyncExecutor</c>（同步执行）两处，读法只有 <c>Desktop/Native/JournalEntries</c> 一处。
///
/// 收成一处的理由不是整洁：写入意图那一行此前把「事件名」与「动作类型」都写成了 kind——
/// 匿名类型的成员 <c>kind = "intent"</c> 与 <c>action.Kind</c> 在 camelCase 序列化下撞成同一个 JSON 名，
/// <c>JsonSerializer</c> 在写第一行时就抛 <c>InvalidOperationException</c>（属性名冲突），
/// 而它抛在 <c>SyncExecutor</c> 的写入动作里、只被 <c>UserException</c> 接住——同步执行的第一个写入动作
/// 就会以未预期异常结束，计划停在 executing。这条路径从未对真实 Apifox 跑过（F13「已实现（待验证）」），
/// 所以缺陷一直没被触发过；行形状散在六个匿名对象里，也没有任何一处能把它断言住。
/// 现在每个事件一个工厂方法：字段名在编译期就定死，形状由测试工程逐行序列化断言（含「不得有两个字段撞名」）。
///
/// 只依赖 BCL，以共享源码编入测试工程在 Linux 断言。
/// </summary>
public static class JournalLines
{
    // 事件名（kind 的取值）单一落点：写入点与读取点的中文映射都从这里认，界面不就地拼字面。
    public const string CommentApplyIntent = "commentApplyIntent";
    public const string CommentApplyResult = "commentApplyResult";
    public const string PlanStart = "planStart";
    public const string Intent = "intent";
    public const string ActionResult = "actionResult";
    public const string PlanEnd = "planEnd";

    /// <summary>注释回写：即将写入某个源码文件（意图先落盘，进程中断后仍能看出做到哪一步）。</summary>
    public static object CommentApplyIntentLine(DateTimeOffset at, string planId, string file, string beforeHash, string afterHash) =>
        new { at, kind = CommentApplyIntent, planId, file, beforeHash, afterHash };

    /// <summary>注释回写：某个源码文件的逐文件结果（applied / skipped / conflict / failed）。</summary>
    public static object CommentApplyResultLine(DateTimeOffset at, string planId, string file, string state, string? message) =>
        new { at, kind = CommentApplyResult, planId, file, state, message };

    /// <summary>同步执行：计划开始（计划指纹一并记下，事后可核对执行的是哪一份计划）。</summary>
    public static object PlanStartLine(DateTimeOffset at, string planId, string hash) =>
        new { at, kind = PlanStart, planId, hash };

    /// <summary>
    /// 同步执行：即将写入某个远端资源。动作类型写 <c>actionKind</c> 而不是 <c>kind</c>——
    /// 事件名已经占着 kind，两个字段撞成同一个 JSON 名会让整行序列化失败（见类注释）。
    /// </summary>
    public static object IntentLine(DateTimeOffset at, string planId, string actionId, string actionKind, string resourceKey, long? remoteId, string payloadHash) =>
        new { at, kind = Intent, planId, actionId, actionKind, resourceKey, remoteId, payloadHash };

    /// <summary>同步执行：某个动作的逐项结果（含回读核对后的状态与新建资源 ID）。</summary>
    public static object ActionResultLine(DateTimeOffset at, string planId, string actionId, string resourceKey, string state, string? message, long? createdId) =>
        new { at, kind = ActionResult, planId, actionId, resourceKey, state, message, createdId };

    /// <summary>同步执行：计划结束（verified / partial / failed / canceled）。</summary>
    public static object PlanEndLine(DateTimeOffset at, string planId, string status) =>
        new { at, kind = PlanEnd, planId, status };
}
