using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 过渡壳 REST 错误处理的 catch 顺序与文案归属（第一百九十七轮，简报 P1-3）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>Api/ErrorHandling.cs</c> 的 catch 顺序是
/// <c>UserException</c> → <c>BadHttpRequestException</c> → <c>JsonException</c> → <c>Exception</c>，
/// 当前是对的（更具体的在前），但<b>没有任何断言钉住这个顺序</b>。C# 的 catch 是「第一个匹配的赢」，
/// 于是往前面插一个更宽的类型（比如把 <c>Exception</c> 提到最前、或加一条 <c>catch (SystemException)</c>）
/// 会让后面那几条<b>永远不执行</b>——编译照样过、界面照样跑，而「请求 JSON 坏了」从此回的是
/// 「内部错误，请稍后重试」，用户拿到的下一步从「检查请求内容」变成「等一会儿再试」。
/// 这类改动在 diff 里只多一行，读的人很难看出它把后面三条全废了。
/// </para>
/// <para>
/// <b>判什么</b>：① 四条 catch 的类型顺序逐字等于声明的那一份；② 最宽的那条（<c>Exception</c>）在最后，
/// 且全文件没有无类型的裸 <c>catch</c>（裸 catch 等价于 <c>catch (Exception)</c>，插在哪条前面就吃掉哪条）；
/// ③ 每条 catch 回给界面的文案来自 <c>ErrorCopy</c> 的对应成员（唯一落点），
/// 只有 <c>UserException</c> 回 <c>ex.Message</c>——那是产品自己抛的、给用户看的话，不是框架消息。
/// </para>
/// <para>
/// <b>不判什么</b>：真的发一个坏 JSON 请求看回什么。那要起 Kestrel 与真实 HTTP 客户端，
/// 本仓的 <c>MockApifoxCliTests</c> 走的是进程边界不是 HTTP 边界；这一支的端到端随真机/真实服务端复核。
/// 本门判的是「顺序与文案归属没被静默改掉」，不是「运行时行为对」。
/// </para>
/// </summary>
public class ErrorHandlingOrderTests
{
    private static string Source => Repo.Source("Api", "ErrorHandling.cs");

    /// <summary>剥掉注释后的代码（去注释口径只有 <see cref="CodeScan"/> 一份）。</summary>
    private static string Code => CodeScan.Text(Source);

    /// <summary>按出现顺序取出的 catch 子句：类型名 + 该子句的正文（到下一个 catch 之前）。</summary>
    private static List<(string Type, string Body)> Catches()
    {
        var code = Code;
        var matches = Regex.Matches(code, @"catch\s*\(\s*([\w.]+)\s+\w+\s*\)").Cast<Match>().ToList();
        var result = new List<(string, string)>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index + matches[i].Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : code.Length;
            result.Add((matches[i].Groups[1].Value, code[start..end]));
        }

        return result;
    }

    [Fact]
    public void CatchOrder_IsMostSpecificFirstAndTheBroadestIsLast()
    {
        var caught = Catches().Select(c => c.Type).ToList();

        // 顺序逐字钉住：往前面插一个更宽的类型，后面那几条就永远不执行（编译不报错）。
        Assert.Equal(
            ["UserException", "BadHttpRequestException", "JsonException", "Exception"],
            caught);

        // 最宽的那条必须在最后：它前面任何一条都轮不到它，它前面任何一条被它挡住就再没人执行。
        Assert.Equal("Exception", caught[^1]);
    }

    [Fact]
    public void NoBareCatch_ThatWouldSwallowTheTypedOnes()
    {
        // 裸 catch 等价于 catch (Exception)：插在哪条前面就吃掉哪条，且 diff 里只多一行。
        Assert.DoesNotMatch(@"catch\s*\{", Code);
        Assert.DoesNotMatch(@"catch\s*\(\s*\)", Code);
    }

    [Fact]
    public void EveryCatch_AnswersWithTheCopyThatBelongsToIt()
    {
        var byType = Catches().ToDictionary(c => c.Type, c => c.Body);

        // 产品自己抛的、给用户看的话：原样回（不是框架消息，不需要脱敏）。
        Assert.Contains("ex.Message", byType["UserException"]);

        // 框架消息是英文内部描述、绑定失败时还带请求体片段：只回固定文案。
        Assert.Contains("ErrorCopy.BadRequestMessage", byType["BadHttpRequestException"]);

        // JSON 解析失败只回结构信息（第几行第几个字节），不回用户原文片段。
        Assert.Contains("ErrorCopy.InvalidJson(ex)", byType["JsonException"]);

        // 未知异常只回固定文案，详情进日志（异常消息可能带内部路径、类型名或凭据片段）。
        Assert.Contains("ErrorCopy.InternalMessage", byType["Exception"]);
    }

    [Fact]
    public void BroadCatch_LogsTheExceptionInsteadOfShowingIt()
    {
        // 「只回固定文案」的另一半：详情必须进日志，否则排障时什么都没有。
        var broad = Catches().Single(c => c.Type == "Exception").Body;
        Assert.Contains("logger.LogError(ex", broad);
        // 且不许把异常消息拼进回给界面的那一句（拼进去就等于把内部路径/类型名/凭据片段显示在错误行里）。
        Assert.DoesNotContain("ex.Message", broad);
    }
}
