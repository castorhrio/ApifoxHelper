namespace ApifoxHelper.Api;

/// <summary>
/// 错误响应文案的单一口径（第一百四十三轮；第一百五十四轮把 400 两条分支收成同一口径）。
/// <para>
/// 存在理由：<see cref="ErrorHandling"/> 的四个 catch 分支此前各自就地拼字符串，其中 500 分支把
/// <c>ex.Message</c> 原样回给前端——未知异常的消息里可能带内部路径、类型名甚至凭据片段，
/// 而它会被界面原样显示在错误行里（冻结页面与原生界面都读 <c>error</c> 字段）。
/// 方案 §4.1 的边界是「错误脱敏」，而脱敏只处理已知形状（令牌、路径），挡不住任意异常消息。
/// </para>
/// <para>
/// 第一百四十三轮先把 500 分支收成固定文案；第一百五十四轮把 400 两条分支也收成同一口径：
/// <c>BadHttpRequestException.Message</c> 是框架生成的英文内部描述（绑定失败时还会带请求体片段），
/// <c>JsonException.Message</c> 会带用户原文片段（「The JSON value could not be converted…」后面
/// 跟着出错的那一段）。两者都不是用户能照着改的那一句，却都可能把请求内容原样回显到界面上。
/// 现在：请求格式无效只回固定文案；JSON 格式无效只回**结构信息**（第几行第几列），不回消息体。
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

    /// <summary>
    /// 请求格式无效（<c>BadHttpRequestException</c>）：只回固定文案，不回框架消息。
    /// 框架消息是英文内部描述、绑定失败时还会带请求体片段，用户照着它改不了任何东西。
    /// </summary>
    public const string BadRequestMessage = "请求格式无效，请检查请求内容后重试。";

    /// <summary>
    /// JSON 格式无效但没有位置信息时的固定文案（<see cref="InvalidJson"/> 的兜底）。
    /// </summary>
    public const string InvalidJsonMessage = "JSON 格式无效，请检查请求内容后重试。";

    /// <summary>
    /// JSON 格式无效（<c>JsonException</c>）：只回**结构信息**——第几行、第几列，不回消息体。
    /// <para>
    /// 行号与列号是用户能照着改的那一句（「第 3 行第 12 列」直接指到出错的那个字符），
    /// 而 <c>JsonException.Message</c> 会带用户原文片段，不能回显。
    /// <c>LineNumber</c> / <c>BytePositionInLine</c> 是 0 基的，这里换算成 1 基（用户数行从 1 开始）。
    /// 两者缺一（异常不是解析器抛的、没有位置）时回固定文案，不猜位置。
    /// </para>
    /// </summary>
    public static string InvalidJson(JsonException ex)
    {
        if (ex.LineNumber is not { } line || ex.BytePositionInLine is not { } column)
        {
            return InvalidJsonMessage;
        }

        return $"JSON 格式无效（第 {line + 1} 行，第 {column + 1} 列）。";
    }
}
