using System.Text.Json;

namespace ApifoxHelper.Infrastructure;

/// <summary>
/// JSON 解析失败的结构化口径（第一百九十六轮，简报 P0-2）。
/// <para>
/// <b>为什么要有这个文件</b>：同一形状的两条错误路径此前各说各的——
/// <c>Api/ErrorHandling.cs</c> 的 <c>catch (JsonException ex)</c> 走 <c>ErrorCopy.InvalidJson(ex)</c>
/// （第一百五十四轮收成「只回结构信息」，第一百五十七轮把「列」改成「字节」），
/// 而 <c>Apifox/ApifoxCli.cs</c> 的 <c>Parse</c> 里同一个 <c>catch (JsonException ex)</c> 仍在拼
/// <c>"JSON 不完整：" + ex.Message</c>。后者是<b>真实缺陷</b>，不是文档问题：
/// <c>ex.Message</c> 会带用户原文片段，而 CLI 的输出可能来自远端；这条 <c>ParseError</c> 经
/// <c>CliResult.Describe()</c> 拼成「输出无法可靠解析：…」后原样进原生界面的状态行与错误行。
/// </para>
/// <para>
/// <b>为什么落在 <c>Infrastructure</c> 而不是继续留在 <c>Api/ErrorCopy.cs</c></b>：<c>Api/</c> 是过渡壳的
/// HTTP 层，它引用 <c>Apifox/</c>（<c>Api/ApifoxRoutes.cs</c>）；若 <c>Apifox/ApifoxCli.cs</c> 反过来
/// 引用 <c>Api/ErrorCopy</c>，两个目录就互为依赖——分层倒置，且下一次「把 CLI 层单独编出去」时会直接编不过。
/// 本文件只依赖 BCL（<c>System.Text.Json</c>），因此 <c>Api/ErrorCopy</c> 与 <c>Apifox/ApifoxCli</c>
/// 都能引用它，而两者之间不必互相认识。<b>文案逐字未变</b>：<c>ErrorCopy.InvalidJson</c> 现在只是转发，
/// 既有断言（<c>ErrorCopyTests</c> 那五条）仍按原句判。
/// </para>
/// <para>
/// 常量名以 <c>Message</c> 收尾，与 <c>ErrorCopy</c> 同族，因此自动落进 <c>ServiceCopyTests</c>
/// 的「文案常量」扫描面（中文标点、空话禁词同一口径）——<c>Infrastructure</c> 已在
/// <c>Repo.ServiceDirectories</c> 的封闭清单里。
/// </para>
/// </summary>
public static class JsonErrorCopy
{
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
    /// <para>
    /// <b>两条路径共用这一份（第一百九十六轮）</b>：HTTP 请求体解析失败与 CLI 输出解析失败是同一个形状，
    /// 用户读到的必须是同一句口径。CLI 那一侧更常见的是「输出被截断」，但截断与格式错在
    /// <c>JsonException</c> 这一层分不开（解析器只报位置），因此不另造一句「不完整」——
    /// 两句话并存正是这一轮要收掉的东西。
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
}
