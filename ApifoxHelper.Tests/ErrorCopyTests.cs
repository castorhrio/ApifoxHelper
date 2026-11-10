using ApifoxHelper.Api;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 错误响应文案的口径与 500 / 400 分支的收敛（第一百四十三轮；第一百五十四轮把 400 两条分支收成同一口径）。
/// <para>
/// 为什么测这个：<c>Api/ErrorHandling.cs</c> 的 500 分支此前把 <c>ex.Message</c> 原样回给前端，
/// 而 <c>error</c> 字段会被界面原样显示在错误行里——未知异常的消息可能带内部路径、类型名或
/// 凭据片段，方案 §4.1 的「错误脱敏」只处理已知形状，挡不住任意异常消息。
/// 400 两条分支同族：<c>BadHttpRequestException.Message</c> 是框架生成的英文内部描述（绑定失败时
/// 还带请求体片段），<c>JsonException.Message</c> 会带用户原文片段——脱敏挡不住任意消息体，
/// 所以两条分支都不再回消息体：请求格式无效只回固定文案，JSON 格式无效只回结构信息（第几行第几列）。
/// </para>
/// <para>
/// 断言分两层：文案本身（<see cref="ErrorCopy"/> 与测试工程共享编译，真执行）与接线
/// （<c>ErrorHandling.cs</c> 的源码切片——它依赖 ASP.NET Core，Linux 侧不能共享编译）。
/// 两层都要有：只测文案，接线改回 <c>ex.Message</c> 时全绿；只测接线，文案本身可以带半角标点。
/// </para>
/// </summary>
public class ErrorCopyTests
{
    // ── 文案本身 ─────────────────────────────────────────────────────

    /// <summary>
    /// 固定文案不得含任何「详情占位」：没有异常消息、没有类型名、没有路径。
    /// 这条是 500 分支收敛的目的本身——文案里一旦留了拼接位，接线迟早会把它填上。
    /// </summary>
    [Fact]
    public void InternalMessage_CarriesNoDetails()
    {
        var message = ErrorCopy.InternalMessage;
        Assert.False(string.IsNullOrWhiteSpace(message));
        // 不含拼接位（「：」后接详情是输入类错误的形状，固定文案不该有）。
        Assert.DoesNotContain("：", message, StringComparison.Ordinal);
        Assert.DoesNotContain("{", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", message, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.", message, StringComparison.Ordinal);
        // 说清「怎么办」：重试 + 详情去哪了。
        Assert.Contains("重试", message, StringComparison.Ordinal);
        Assert.Contains("日志", message, StringComparison.Ordinal);
    }

    /// <summary>固定文案同样受中文排版条款约束（错误路径上的那一句也是用户要读的）。</summary>
    [Fact]
    public void InternalMessage_FollowsTheTypographyCaliber() => AssertTypography(ErrorCopy.InternalMessage);

    /// <summary>400 两条分支的固定文案同受排版条款约束（第一百五十四轮）。</summary>
    [Fact]
    public void BadRequestMessage_FollowsTheTypographyCaliber() => AssertTypography(ErrorCopy.BadRequestMessage);

    /// <summary>JSON 分支的兜底文案同受排版条款约束（第一百五十四轮）。</summary>
    [Fact]
    public void InvalidJsonMessage_FollowsTheTypographyCaliber() => AssertTypography(ErrorCopy.InvalidJsonMessage);

    /// <summary>错误路径上的每一句都按同一份口径判（不各写一份）。</summary>
    private static void AssertTypography(string message)
    {
        Assert.False(string.IsNullOrWhiteSpace(message));
        foreach (var mark in CopyRules.HalfWidthMarks)
        {
            Assert.True(CopyRules.IndexOfHalfWidthMark(message, mark) < 0,
                $"「{message}」里的半角「{mark}」紧挨中文。");
        }
        Assert.True(CopyRules.IndexOfStrayColon(message) < 0, $"「{message}」里有半角冒号。");
        Assert.False(message.Contains('“') || message.Contains('”'), $"「{message}」里出现弯引号。");
        Assert.DoesNotContain("**", message, StringComparison.Ordinal);
        foreach (var word in CopyRules.BannedWords.Concat(CopyRules.ProgressNarration))
        {
            Assert.DoesNotContain(word, message, StringComparison.Ordinal);
        }
    }

    // ── 400 两条分支：只回结构信息，不回消息体（第一百五十四轮）──────────────

    /// <summary>
    /// 请求格式无效只回固定文案：框架消息是英文内部描述，绑定失败时还带请求体片段，
    /// 用户照着它改不了任何东西（第一百五十四轮）。
    /// </summary>
    [Fact]
    public void BadRequest_ReturnsFixedCopyOnly()
    {
        Assert.Equal("请求格式无效，请检查请求内容后重试。", ErrorCopy.BadRequestMessage);
        // 固定文案里没有拼接位：一旦留了「：」后接详情的位置，接线迟早会把它填上。
        Assert.DoesNotContain("：", ErrorCopy.BadRequestMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("{", ErrorCopy.BadRequestMessage, StringComparison.Ordinal);
        // 说清「怎么办」。
        Assert.Contains("重试", ErrorCopy.BadRequestMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 请求格式无效这条分支在结构上就没有「详情」入口：<c>ErrorCopy</c> 里不再有接受 detail 的
    /// <c>BadRequest</c> 方法，接线也就无从把框架消息拼进去（第一百五十四轮）。
    /// </summary>
    [Fact]
    public void BadRequest_HasNoDetailParameterToLeakThrough()
    {
        var copy = Repo.Source("Api", "ErrorCopy.cs");
        Assert.DoesNotContain("BadRequest(string", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidJson(string", copy, StringComparison.Ordinal);
        // 两条 400 分支的文案常量都在（单一落点）。
        Assert.Contains("BadRequestMessage", copy, StringComparison.Ordinal);
        Assert.Contains("InvalidJsonMessage", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// JSON 格式无效只回结构信息（第几行第几列），**不回消息体**（第一百五十四轮）。
    /// <para>
    /// 这条是「敏感片段不进错误行」的真执行断言：往 <c>JsonException.Message</c> 里注入一段假令牌
    /// 与用户原文，输出里必须只剩行列号。变异注入（把 <c>InvalidJson</c> 改回拼 <c>ex.Message</c>）
    /// 时这条会红。
    /// </para>
    /// </summary>
    [Fact]
    public void InvalidJson_ReportsStructureOnly_NotTheMessageBody()
    {
        var ex = new JsonException(
            "The JSON value could not be converted. 原文片段：{\"api_key\":\"sk-abcdefghijklmnopqrstuvwxyz\"}",
            path: "$.api_key",
            lineNumber: 2,
            bytePositionInLine: 11,
            innerException: null);

        var message = ErrorCopy.InvalidJson(ex);

        // 结构信息在，且是 1 基（用户数行从 1 开始）。
        Assert.Equal("JSON 格式无效（第 3 行，第 12 列）。", message);
        // 消息体不在：假令牌、字段名、英文原文一律不出现。
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", message, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("原文片段", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析器真抛的那一次同样只回结构信息（不是只有手工构造的异常才这样）：
    /// 用 <c>JsonDocument.Parse</c> 造一个真 <c>JsonException</c>，输出里只有行列号。
    /// </summary>
    [Fact]
    public void InvalidJson_FromTheRealParser_AlsoReportsStructureOnly()
    {
        JsonException? caught = null;
        try
        {
            using var _ = JsonDocument.Parse("{\n  \"token\": \"APS-abcdefghijklmnopqrst\",\n  \"n\": 1,,\n}");
        }
        catch (JsonException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        var message = ErrorCopy.InvalidJson(caught!);
        Assert.StartsWith("JSON 格式无效（第 ", message, StringComparison.Ordinal);
        Assert.EndsWith(" 列）。", message, StringComparison.Ordinal);
        Assert.DoesNotContain("APS-abcdefghijklmnopqrst", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 没有位置信息时回固定文案，不猜位置（异常不是解析器抛的、或解析器没填位置）。
    /// </summary>
    [Fact]
    public void InvalidJson_WithoutPosition_FallsBackToFixedCopy()
    {
        var ex = new JsonException("The JSON value could not be converted to System.Int32.");
        Assert.Equal(ErrorCopy.InvalidJsonMessage, ErrorCopy.InvalidJson(ex));
        Assert.Equal("JSON 格式无效，请检查请求内容后重试。", ErrorCopy.InvalidJsonMessage);
        Assert.DoesNotContain("：", ErrorCopy.InvalidJsonMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// 接线：两条 400 分支都从单一落点取文案，且**都不再读 <c>ex.Message</c>**。
    /// 只测文案不够——把接线改回拼 <c>ex.Message</c>，上面几条用例照样绿（它们测的是文案本身）。
    /// 详情不能丢：两条分支都要进日志（排查靠它）。
    /// </summary>
    [Fact]
    public void ErrorHandling_400BranchesTakeCopyFromTheSingleSource()
    {
        var badRequest = CatchBlock("catch (BadHttpRequestException ex)");
        Assert.Contains("ErrorCopy.BadRequestMessage", badRequest, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", badRequest, StringComparison.Ordinal);
        Assert.Contains("LogWarning(ex", badRequest, StringComparison.Ordinal);
        Assert.Contains("context.Request.Path", badRequest, StringComparison.Ordinal);

        var invalidJson = CatchBlock("catch (JsonException ex)");
        Assert.Contains("ErrorCopy.InvalidJson(ex)", invalidJson, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", invalidJson, StringComparison.Ordinal);
        Assert.Contains("LogWarning(ex", invalidJson, StringComparison.Ordinal);
        Assert.Contains("context.Request.Path", invalidJson, StringComparison.Ordinal);
    }

    /// <summary>共享编译：脱敏口径必须进测试工程的编译清单，否则上面的断言测的是另一份代码。</summary>
    [Fact]
    public void Redactor_IsSharedCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Infrastructure\Redactor.cs", csproj);
        // 脱敏口径不依赖 ASP.NET Core 与 WinForms（否则 Linux 侧共享编译不成立）。
        var redactor = Repo.Source("Infrastructure", "Redactor.cs");
        Assert.DoesNotContain("Microsoft.AspNetCore", redactor, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms", redactor, StringComparison.Ordinal);
    }

    // ── 接线（源码切片：ErrorHandling 依赖 ASP.NET Core，不能共享编译）──────

    /// <summary>
    /// 500 分支：只回固定文案，详情进日志。断言的是 <c>catch (Exception ex)</c> 那一段的**方法体**，
    /// 不是整个文件——400 分支里出现 <c>ex.Message</c> 是合法的（那是用户填错的原因）。
    /// </summary>
    [Fact]
    public void UnknownException_ReturnsFixedCopyAndLogsTheDetail()
    {
        var block = CatchBlock("catch (Exception ex)");
        Assert.Contains("ErrorCopy.InternalMessage", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", block, StringComparison.Ordinal);
        Assert.DoesNotContain("Redactor.Redact", block, StringComparison.Ordinal);
        // 详情不能丢：日志里要有异常对象与请求路径（排查靠它）。
        Assert.Contains("LogError(ex", block, StringComparison.Ordinal);
        Assert.Contains("context.Request.Path", block, StringComparison.Ordinal);
    }

    /// <summary>文案只有一份：<c>ErrorHandling.cs</c> 里不得再出现就地拼的错误文案字面量。</summary>
    [Fact]
    public void ErrorHandling_TakesCopyFromTheSingleSource()
    {
        var source = Repo.Source("Api", "ErrorHandling.cs");
        Assert.DoesNotContain("内部错误", source, StringComparison.Ordinal);
        Assert.DoesNotContain("请求格式无效，", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON 格式无效", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCopy.BadRequestMessage", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCopy.InvalidJson(ex)", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCopy.InternalMessage", source, StringComparison.Ordinal);
    }

    /// <summary>共享编译：文案文件必须进测试工程的编译清单，否则上面的断言测的是另一份代码。</summary>
    [Fact]
    public void ErrorCopy_IsSharedCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Api\ErrorCopy.cs", csproj);
        // 口径文件不依赖 ASP.NET Core（否则 Linux 侧共享编译不成立）。
        var copy = Repo.Source("Api", "ErrorCopy.cs");
        Assert.DoesNotContain("Microsoft.AspNetCore", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", copy, StringComparison.Ordinal);
    }

    /// <summary>
    /// 取一个 <c>catch (…)</c> 分支的方法体（按花括号深度界定，不靠缩进猜）。
    /// 找不到就判红——提取器失效时断言会静默通过，那比没有断言更糟。
    /// </summary>
    private static string CatchBlock(string header)
    {
        var source = Repo.Source("Api", "ErrorHandling.cs");
        var start = source.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"ErrorHandling.cs 里找不到「{header}」分支。");
        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"「{header}」后面没有方法体。");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0) return source[open..(i + 1)];
            }
        }
        Assert.Fail($"「{header}」的方法体没有闭合。");
        return string.Empty;
    }
}
