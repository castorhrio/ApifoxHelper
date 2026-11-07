namespace ApifoxHelper.Desktop.Native;

/// <summary>
/// 状态与枚举值的中文文案与语义色调（设计验收 D3 状态双通道、D9 状态完备、D11 文案口径的机械面）。
/// 本文件不依赖 WinForms 与 UiTokens：以共享源码方式编入测试工程，状态完备断言因此可在 Linux 上运行。
/// 未知状态原样返回，界面如实显示原始值，不编造"成功"或"干净"。
/// </summary>
public static class TaskDisplay
{
    /// <summary>语义色调：ok / warn / bad / accent / muted / normal。界面层把它映射到 UiTokens 颜色。</summary>
    public static string SourceTone(string status) => status switch
    {
        "applied" => "ok",
        "partiallyApplied" => "warn",
        "failed" => "bad",
        "scanned" or "reviewing" or "planReady" => "accent",
        "notStarted" => "muted",
        _ => "normal",
    };

    public static string GeneratedTone(string status) => status switch
    {
        "verified" => "ok",
        "partial" => "warn",
        "pending" => "accent",
        "notChecked" => "muted",
        "unavailable" => "bad",
        _ => "normal",
    };

    public static string RemoteTone(string status) => status switch
    {
        "verified" => "ok",
        "partial" or "expired" or "interrupted" => "warn",
        "failed" => "bad",
        "executing" or "pending" or "previewable" or "awaitingConfirm" or "snapshotReady" => "accent",
        "notChecked" => "muted",
        _ => "normal",
    };

    /// <summary>注释位置状态（DocSlot.Status）的色调：缺口=warn，失效/无效=bad，有效=ok。</summary>
    public static string SlotTone(string status) => status switch
    {
        "valid" => "ok",
        "missing" or "empty" => "warn",
        "stale" or "invalidXml" or "inheritDocUnresolved" => "bad",
        "inheritDoc" => "muted",
        "unsupported" => "muted",
        _ => "normal",
    };

    public static string SourceStatusText(string status) => status switch
    {
        "notStarted" => "未扫描",
        "scanned" => "已扫描",
        "reviewing" => "审阅中",
        "planReady" => "注释计划就绪",
        "applied" => "已回写",
        "partiallyApplied" => "部分回写",
        "failed" => "扫描失败",
        _ => status,
    };

    public static string GeneratedStatusText(string status) => status switch
    {
        "notChecked" => "未核对",
        "pending" => "待处理",
        "verified" => "已核对",
        "partial" => "部分核对",
        "unavailable" => "不可用",
        _ => status,
    };

    public static string RemoteStatusText(string status) => status switch
    {
        "notChecked" => "未检查",
        "snapshotReady" => "快照就绪",
        "pending" => "待处理",
        "previewable" => "可预览",
        "awaitingConfirm" => "待确认",
        "executing" => "同步执行中",
        "verified" => "已核对",
        "partial" => "部分完成",
        "failed" => "失败",
        "expired" => "计划已过期",
        "interrupted" => "已中断",
        _ => status,
    };

    /// <summary>后台作业结束态（JobRunner）：成功 / 失败 / 已取消；未知值原样显示。</summary>
    public static string JobStateText(string state) => state switch
    {
        "succeeded" => "成功",
        "failed" => "失败",
        "canceled" => "已取消",
        _ => state,
    };

    /// <summary>注释位置状态：缺口清单里逐条显示的状态文字。</summary>
    public static string SlotStatusText(string status) => status switch
    {
        "valid" => "有效",
        "missing" => "缺失",
        "empty" => "空",
        "stale" => "已失效",
        "inheritDoc" => "继承文档",
        "inheritDocUnresolved" => "继承无法解析",
        "invalidXml" => "XML 无效",
        "unsupported" => "不支持",
        _ => status,
    };

    /// <summary>注释标注点（DocSlot.Tag）：summary / param / returns。</summary>
    public static string SlotTagText(string tag) => tag switch
    {
        "summary" => "说明",
        "param" => "参数",
        "returns" => "返回",
        _ => tag,
    };

    /// <summary>注释位置的角色（DocSlot.Role）：说明属于哪一层。</summary>
    public static string SlotRoleText(string role) => role switch
    {
        "endpoint" => "接口",
        "request" => "请求",
        "response" => "响应",
        "enum" => "枚举",
        _ => role,
    };

    /// <summary>注释位置的符号种类（DocSlot.SymbolKind）。</summary>
    public static string SymbolKindText(string kind) => kind switch
    {
        "method" => "方法",
        "type" => "类型",
        "property" => "属性",
        "recordParameter" => "记录参数",
        "enum" => "枚举",
        "enumMember" => "枚举成员",
        _ => kind,
    };

    /// <summary>注释候选审阅状态（CommentCandidate.ReviewState）的色调：已接受=ok，待审阅/证据已变化=warn，搁置/驳回=muted。</summary>
    public static string ReviewTone(string state) => state switch
    {
        "accepted" => "ok",
        "pending" or "stale" => "warn",
        "deferred" or "rejected" => "muted",
        _ => "normal",
    };

    /// <summary>注释候选审阅状态：候选清单与审阅结果里逐条显示的状态文字。</summary>
    public static string ReviewStateText(string state) => state switch
    {
        "pending" => "待审阅",
        "accepted" => "已接受",
        "deferred" => "已搁置",
        "rejected" => "已驳回",
        "stale" => "证据已变化",
        _ => state,
    };

    /// <summary>注释候选的依据（CommentCandidate.Basis）：说明结论建立在什么之上。</summary>
    public static string CandidateBasisText(string basis) => basis switch
    {
        "code" => "代码可证实",
        "needsConfirmation" => "需业务确认",
        "notCovered" => "分析未覆盖",
        "confirmed" => "人工已确认",
        _ => basis,
    };

    /// <summary>注释候选的来源（CommentCandidate.Source）。</summary>
    public static string CandidateSourceText(string source) => source switch
    {
        "rule" => "规则提取",
        "manual" => "手工填写",
        "model" => "模型生成",
        "file" => "导入结果",
        _ => source,
    };

    /// <summary>草稿生成运行的结果（GenerationRun.Status）。</summary>
    public static string RunStatusText(string status) => status switch
    {
        "succeeded" => "生成成功",
        "partial" => "部分生成",
        "failed" => "生成失败",
        "noResult" => "没有生成候选",
        _ => status,
    };

    /// <summary>注释计划状态（CommentPlan.Status）的色调：已回写=ok，部分回写/已过期=warn，回写失败=bad，可回写/回写中=accent。</summary>
    public static string PlanTone(string status) => status switch
    {
        "applied" => "ok",
        "partial" or "expired" => "warn",
        "failed" => "bad",
        "ready" or "applying" => "accent",
        _ => "normal",
    };

    /// <summary>注释计划状态：回写预览与结果里显示的状态文字。</summary>
    public static string PlanStatusText(string status) => status switch
    {
        "ready" => "可回写",
        "applying" => "回写中",
        "applied" => "已回写",
        "partial" => "部分回写",
        "expired" => "已过期",
        "failed" => "回写失败",
        _ => status,
    };

    /// <summary>逐文件回写结果（FileApplyResult.State）的色调：已写入=ok，已跳过=muted，文件已变化=warn，失败=bad。</summary>
    public static string FileApplyTone(string state) => state switch
    {
        "applied" => "ok",
        "skipped" => "muted",
        "conflict" => "warn",
        "failed" => "bad",
        _ => "normal",
    };

    /// <summary>逐文件回写结果：回写后逐文件显示的结果文字。</summary>
    public static string FileApplyStateText(string state) => state switch
    {
        "applied" => "已写入",
        "skipped" => "已跳过",
        "conflict" => "文件已变化",
        "failed" => "失败",
        _ => state,
    };

    /// <summary>代码证据类型（CodeEvidence.Kind）：证据清单里逐条显示的分类标签。</summary>
    public static string EvidenceKindText(string kind) => kind switch
    {
        "route" => "路由",
        "binding" => "参数绑定",
        "validation" => "校验",
        "guard" => "守卫条件",
        "return" => "返回语句",
        "call" => "内部调用",
        "queryFilter" => "查询过滤",
        "mapping" => "字段映射",
        "default" => "默认值",
        "enumMember" => "枚举成员",
        "existingDoc" => "现有注释",
        "docDiagnostic" => "注释诊断",
        "responseData" => "响应数据",
        _ => kind,
    };
}
