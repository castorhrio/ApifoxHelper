namespace ApifoxHelper.Api;

/// <summary>
/// 错误响应文案的单一口径（第一百四十三轮）。
/// <para>
/// 存在理由：<see cref="ErrorHandling"/> 的四个 catch 分支此前各自就地拼字符串，其中 500 分支把
/// <c>ex.Message</c> 原样回给前端——未知异常的消息里可能带内部路径、类型名甚至凭据片段，
/// 而它会被界面原样显示在错误行里（冻结页面与原生界面都读 <c>error</c> 字段）。
/// 方案 §4.1 的边界是「错误脱敏」，而脱敏只处理已知形状（令牌、路径），挡不住任意异常消息。
/// </para>
/// <para>
/// 现在的口径：未知异常只回固定文案，详情进日志（日志里有完整堆栈与请求路径，排查不受影响）；
/// 输入类错误（请求格式、JSON 格式）仍回具体原因——那是用户自己填错、需要照着改的那一句，
/// 且已经过 <see cref="Redactor.Redact"/>。
/// </para>
/// <para>
/// 这个文件只依赖 BCL（不引用 ASP.NET Core），因此与测试工程共享编译，文案本身可被真执行断言
/// （全角标点、空话禁词、不含异常详情）。常量名以 <c>Message</c> 收尾，是为了落进
/// <c>ServiceCopyTests</c> 已有的「文案常量」扫描面——错误路径上的那一句同样受中文排版条款约束。
/// </para>
/// </summary>
public static class ErrorCopy
{
    /// <summary>
    /// 未知异常的固定文案：不透露任何内部细节，只说明「怎么办」。
    /// 详情由 <see cref="ErrorHandling"/> 记入日志（含堆栈与请求路径）。
    /// </summary>
    public const string InternalMessage = "内部错误，请稍后重试；详情已记入日志。";

    /// <summary>请求格式无效（<c>BadHttpRequestException</c>）：回具体原因，用户照着改。</summary>
    public static string BadRequest(string detail) => "请求格式无效：" + detail;

    /// <summary>JSON 格式无效（<c>JsonException</c>）：回具体原因，用户照着改。</summary>
    public static string InvalidJson(string detail) => "JSON 格式无效：" + detail;
}
