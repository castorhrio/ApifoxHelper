using ApifoxHelper.Comments;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 注释候选的「改文字」与「撤销接受」口径（DESIGN.md §6「候选文字编辑」）：
/// 冻结页面的候选卡片有可编辑的说明文字框、「保存修改」与「撤销接受」，原生审阅对话框此前只能
/// 接受 / 搁置 / 驳回，候选文字只读——而候选文字正是要写进源码的 /// 注释，生成或导入的文字有错字、
/// 或需要按团队口径改写时，原生界面只能驳回重来（驳回后重新生成未必得到同一段文字）。
/// 更要紧的是「接受」在原生界面是单向的：已接受的候选只能改成搁置或驳回，退不回待审阅。
/// 这里只做判定与请求构造，写入仍经 DraftService.UpdateAsync 单一口径——占位文字、超长、注释标记、
/// 待确认问题需先回答、覆盖已有注释需勾选、同一位置只保留一条已接受候选等校验全在服务层，本层不复制。
/// 不依赖 WinForms，与测试工程共享编译。
/// </summary>
public static class CandidateEdit
{
    /// <summary>
    /// 候选文字可编辑：只有 "set"（写入或补全说明）的候选有可改的说明文字；
    /// "removeStale"（删除失效标签）的文字是那条删除动作的说明，与冻结页面同一口径（那一格禁用）。
    /// </summary>
    public static bool CanEditText(CommentCandidate candidate) => candidate.Action == "set";

    /// <summary>
    /// 「保存修改」可用：文字可编辑，且注释位置仍在本次扫描结果中——服务按位置校验，
    /// 位置已不在扫描结果里时保存必然被拒（与三个审阅动作同一门控）。
    /// </summary>
    public static bool CanSaveText(CommentCandidate candidate, bool slotExists) => CanEditText(candidate) && slotExists;

    /// <summary>
    /// 「撤销接受」可用：已接受，且注释位置仍在本次扫描结果中（退回待审阅，与冻结页面同一手势）。
    /// 其余状态没有可撤销的接受，按钮如实禁用而不是点了没反应。
    /// </summary>
    public static bool CanUndoAccept(CommentCandidate candidate, bool slotExists) =>
        candidate.ReviewState == "accepted" && slotExists;

    /// <summary>
    /// 接受请求：与冻结页面同一份——"set" 的候选连文字一起提交（用户可能刚改过那一格），
    /// "removeStale" 不提交文字（服务只对 "set" 应用文字）。
    /// <paramref name="answers"/> 与 <paramref name="overwriteExisting"/> 为 null 表示「本次没有改动」，
    /// 服务按「不传即不改」处理（与既有审阅路径同一口径，不把没动过的回答写成空串）。
    /// </summary>
    public static CandidateUpdateRequest AcceptRequest(
        CommentCandidate candidate,
        string text,
        string? answers,
        bool? overwriteExisting) => new()
    {
        ReviewState = "accepted",
        Text = CanEditText(candidate) ? text : null,
        Answers = answers,
        OverwriteExisting = overwriteExisting,
    };

    /// <summary>
    /// 保存修改请求：只提交文字、回答与覆盖勾选，不动审阅状态（与冻结页面同一份）——
    /// 改文字不该顺手把一条待审阅的候选变成已接受。
    /// </summary>
    public static CandidateUpdateRequest SaveTextRequest(string text, string? answers, bool? overwriteExisting) => new()
    {
        Text = text,
        Answers = answers,
        OverwriteExisting = overwriteExisting,
    };

    /// <summary>
    /// 撤销接受请求：退回待审阅。同一位置的其他已接受候选由服务一并退回（服务层单一口径），本层不复制。
    /// </summary>
    public static CandidateUpdateRequest UndoAcceptRequest() => new() { ReviewState = "pending" };

    /// <summary>候选文字那一行的说明：讲清这一格是什么、改完怎么生效。</summary>
    public const string TextCaption = "候选文字（写入源码的 /// 注释内容；改完点「保存修改」）";

    /// <summary>不可编辑时的说明：删除失效标签的候选没有可改的说明文字。</summary>
    public const string TextCaptionReadOnly = "候选文字（删除失效标签的候选没有可改的说明文字）";

    /// <summary>没有选中候选时的说明：这一格随提示一起收掉，不指着一条看不见的候选说「改完点保存」。</summary>
    public const string TextCaptionNoSelection = "候选文字（选中一条候选后可编辑）";

    /// <summary>按候选取那一行的说明（界面不就地写两种说法）。</summary>
    public static string TextCaptionFor(CommentCandidate candidate) =>
        CanEditText(candidate) ? TextCaption : TextCaptionReadOnly;

    /// <summary>保存修改后的状态行。</summary>
    public const string SaveDoneText = "已保存修改。";

    /// <summary>撤销接受后的状态行。</summary>
    public const string UndoDoneText = "已撤销接受，该候选回到待审阅。";
}
