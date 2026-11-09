using ApifoxHelper.Comments;
using ApifoxHelper.Source;

namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 两个审阅队列（注释候选、响应契约）的行文本：清单里的单元格与关键词框匹配的字段都从这里取。
/// 拼一次：同一段文字在两处各拼一次，必然有一处先漂（键与名字各改一次，两屏就说出两个名字）。
/// 与 <see cref="SlotText"/> 同一纪律——筛选字段与单元格同源，界面不就地再拼一套。
/// 审阅状态不在关键词里：那一列的取值由筛选下拉管（待审阅/已接受/已搁置/已驳回/证据已变化；
/// 待确认/已确认/已拒绝/已阻断/已失效），不必再敲一遍。
/// 不依赖 WinForms，以共享源码编入测试工程在 Linux 真执行。
/// </summary>
internal static class QueueText
{
    /// <summary>
    /// 候选行的「位置（说明点）」：注释位置还在时用其展示名，不在时如实显示符号编号。
    /// </summary>
    public static string CandidateTitle(CommentCandidate candidate, DocSlot? slot) =>
        slot is not null ? SlotText.Name(slot) : candidate.SymbolId;

    /// <summary>候选行的「标注」列：标注种类；参数另附参数名。</summary>
    public static string CandidateTag(CommentCandidate candidate) =>
        TaskDisplay.SlotTagText(candidate.Tag) + (candidate.ParameterName is { } name ? " " + name : "");

    /// <summary>
    /// 关键词匹配候选行的哪几格：位置、标注、依据与来源——这一行在屏幕上被读作的那几格，不另造一套字段。
    /// </summary>
    public static string?[] CandidateFields(CommentCandidate candidate, DocSlot? slot) =>
        [CandidateTitle(candidate, slot), CandidateTag(candidate),
         TaskDisplay.CandidateBasisText(candidate.Basis), TaskDisplay.CandidateSourceText(candidate.Source)];

    /// <summary>
    /// 候选清单的计数行（第一百三十七轮收进共享口径）：按<b>全部</b>候选统计，不随筛选与排序变化——
    /// 它讲的是这一份候选队列，「候选 128 · 待审阅 0」正是清完队列的凭据（与 <see cref="ListFilter.IsFilteredEmpty"/>
    /// 同一取舍）。未知审阅状态计入「待审阅」而不是丢掉：服务新增状态值时，计数行不能悄悄少算一条。
    /// <para>
    /// 收进这里而不是留在对话框里：冒烟器（<c>ApifoxHelper.Smoke</c>）要在真实控件上断言
    /// 「计数行与候选队列对得上」，若两边各拼一份，断言比的是第二份文字、不是屏幕上那一份。
    /// 不依赖 WinForms，以共享源码编入测试工程在 Linux 断言。
    /// </para>
    /// </summary>
    public static string CandidateCountsLine(IReadOnlyList<CommentCandidate> candidates)
    {
        if (candidates.Count == 0) return "没有候选";
        int pending = 0, accepted = 0, deferred = 0, rejected = 0, stale = 0;
        foreach (var candidate in candidates)
        {
            switch (candidate.ReviewState)
            {
                case "accepted": accepted++; break;
                case "deferred": deferred++; break;
                case "rejected": rejected++; break;
                case "stale": stale++; break;
                default: pending++; break;
            }
        }
        return $"候选 {candidates.Count} · 待审阅 {pending} · 已接受 {accepted} · 已搁置 {deferred} · 已驳回 {rejected} · 证据已变化 {stale}";
    }

    /// <summary>契约行的「封装」列：没有封装类型时如实显示「（直接返回）」。</summary>
    public static string ContractWrapper(string wrapperType) =>
        wrapperType.Length > 0 ? wrapperType : "（直接返回）";

    /// <summary>契约行的「data 类型」列：类型未确定时如实显示「未确定」。</summary>
    public static string ContractDataType(string? dataType) => dataType ?? "未确定";

    /// <summary>
    /// 关键词匹配契约行的哪几格：接口、封装与 data 类型——这一行在屏幕上被读作的那几格，不另造一套字段。
    /// </summary>
    public static string?[] ContractFields(ResponseContract contract) =>
        [contract.EndpointKey, ContractWrapper(contract.WrapperType), ContractDataType(contract.DataType)];
}
