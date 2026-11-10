using System.Text.Json;

namespace ApifoxHelper.Api;

/// <summary>
/// 错误响应文案的单一口径（第一百四十三轮；第一百五十四轮把 400 两条分支收成同一口径；
/// 第一百五十七轮把 JSON 分支的列号口径改成「字节」）。
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
/// 现在：请求格式无效只回固定文案；JSON 格式无效只回**结构信息**（第几行第几个字节），不回消息体。
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
    /// JSON 格式无效（<c>JsonException</c>）：只回**结构信息**——第几行、第几个字节，不回消息体。
    /// <para>
    /// 行号与位置是用户能照着改的那一句（「第 3 行第 12 个字节处」直接指到出错的那个字符），
    /// 而 <c>JsonException.Message</c> 会带用户原文片段，不能回显。
    /// <c>LineNumber</c> / <c>BytePositionInLine</c> 是 0 基的，这里换算成 1 基（用户数行从 1 开始）。
    /// 两者缺一（异常不是解析器抛的、没有位置）时回固定文案，不猜位置。
    /// </para>
    /// <para>
    /// <b>为什么说「字节」而不说「列」（第一百五十七轮）</b>：<c>BytePositionInLine</c> 的名字与
    /// .NET 8 的实测行为一致——它是**行内 UTF-8 字节位**，不是字符位。实测（<c>JsonDocument.Parse</c>）：
    /// <c>{"名":"值",,}</c> 报 <c>BytePositionInLine = 15</c>，而同一个出错位置按字符数是第 11 个字符
    /// （「名」「值」各占 3 字节）。此前文案写「第 15 列」，含中文的请求体上这个数字对不上用户数出来的列，
    /// 而这一句恰恰是错误路径上用户唯一能照着改的那一句——口径错就是误导。
    /// 本方法拿不到请求体（<c>JsonException</c> 只带位置、不带原文），无法把字节位换算成字符位，
    /// 因此如实按字节计：纯 ASCII 的请求体上字节位与字符位相等（数字仍然精确），
    /// 含中文时用户按「字节」理解不会数错方向。换算成字符位需要请求体原文，属另一件事，不在本方法里猜。
    /// </para>
    /// </summary>
    public static string InvalidJson(JsonException ex)
    {
        if (ex.LineNumber is not { } line || ex.BytePositionInLine is not { } position)
        {
            return InvalidJsonMessage;
        }

        return $"JSON 格式无效（第 {line + 1} 行，第 {position + 1} 个字节处）。";
    }

    /// <summary>
    /// 前置条件「还没选 Apifox 项目」的唯一落点（第一百七十四轮，简报 P1-1）。
    /// <para>
    /// <b>收的是哪个口</b>：这一句此前<b>三处各自写了一遍</b>——<c>/api/apifox/folders</c> 与
    /// <c>/api/apifox/template-from-endpoint</c> 两条路由各一份，原生「模块与模板」对话框的状态行又一份。
    /// 三处字面相同，所以今天没有可见缺陷；挡的是「改了其中一处」：用户在同一个产品里会因为走的是页面还是
    /// 对话框而读到两种说法，而这类漂移不会报错。
    /// </para>
    /// <para>
    /// <b>值逐字未变</b>：本轮只动落点，不动措辞（<c>ApifoxPickerTests</c> 的真值表仍按原句断言，
    /// 改一个字即红）。常量名以 <c>Message</c> 收尾，与上面三条同族，因此自动落进
    /// <c>ServiceCopyTests</c> 的「文案常量」扫描面（中文标点、空话禁词同一口径）。
    /// </para>
    /// </summary>
    public const string NoApifoxProjectMessage = "请先选择 Apifox 项目。";

    /// <summary>
    /// 前置条件「项目 ID 不是纯数字」的唯一落点（第一百七十四轮，简报 P1-1）。
    /// <para>
    /// 这一句原本有两处：<c>/api/apifox/branches</c> 路由就地写了一遍，
    /// <c>Desktop/Native/ApifoxPicker.cs</c> 的 <c>ProjectIdInvalid</c> 又写了一遍——后者自己的注释就写着
    /// 「与 /api/apifox/branches 路由同文案」，即用注释承认重复、而不是消除它。现在那一处也指到这里。
    /// </para>
    /// </summary>
    public const string InvalidProjectIdMessage = "项目 ID 无效。";
}
