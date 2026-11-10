using ApifoxHelper.Api;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 错误响应文案的口径与 500 分支的收敛（第一百四十三轮，具名 pass：harden）。
/// <para>
/// 为什么测这个：<c>Api/ErrorHandling.cs</c> 的 500 分支此前把 <c>ex.Message</c> 原样回给前端，
/// 而 <c>error</c> 字段会被界面原样显示在错误行里——未知异常的消息可能带内部路径、类型名或
/// 凭据片段，方案 §4.1 的「错误脱敏」只处理已知形状，挡不住任意异常消息。
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
    public void InternalMessage_FollowsTheTypographyCaliber()
    {
        var message = ErrorCopy.InternalMessage;
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

    /// <summary>
    /// 输入类错误仍回具体原因：那是用户自己填错、需要照着改的那一句。
    /// 收敛 500 不等于把所有错误都变成「出错了」——那会让用户无从下手。
    /// </summary>
    [Fact]
    public void InputErrors_KeepTheReason()
    {
        Assert.Equal("请求格式无效：缺少字段 name", ErrorCopy.BadRequest("缺少字段 name"));
        Assert.Equal("JSON 格式无效：第 3 行", ErrorCopy.InvalidJson("第 3 行"));
        // 前缀用全角冒号（中文句子里的标点）。
        Assert.StartsWith("请求格式无效：", ErrorCopy.BadRequest("x"), StringComparison.Ordinal);
        Assert.StartsWith("JSON 格式无效：", ErrorCopy.InvalidJson("x"), StringComparison.Ordinal);
    }

    // ── 400 分支的脱敏有效性（第一百四十六轮）────────────────────────────

    /// <summary>
    /// 请求格式无效这条分支：用户填错的那一句要回，但**凭据形状必须先被吃掉**。
    /// <para>
    /// 此前只断言了「会回具体原因」，没有一条断言证明脱敏对令牌形状仍然生效——
    /// 而这条分支的详情来自 <c>BadHttpRequestException.Message</c>，绑定失败的消息里
    /// 可能带着请求体片段（用户把令牌贴进了 JSON 字段）。断言的是**组合**：
    /// <c>ErrorCopy.BadRequest(Redactor.Redact(detail))</c>，与 <c>ErrorHandling</c> 的接线同序。
    /// </para>
    /// </summary>
    [Fact]
    public void BadRequest_RedactsTokenShapes()
    {
        var detail = "缺少字段 name；请求头 Authorization: Bearer abcdefgh12345678；"
            + "body 里 api_key=sk-abcdefghijklmnopqrstuvwxyz；"
            + "连接串 Server=db.example;Database=app;User Id=sa;Password=Hunter2!";
        var message = ErrorCopy.BadRequest(Redactor.Redact(detail));

        // 原因还在（用户照着改的那一句不能被脱敏一起吃掉）。
        Assert.Contains("缺少字段 name", message, StringComparison.Ordinal);
        // 令牌形状一律不原样出现。
        Assert.DoesNotContain("abcdefgh12345678", message, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Hunter2!", message, StringComparison.Ordinal);
        // 脱敏标记如实出现（不是把整句删掉）。
        Assert.Contains("已脱敏", message, StringComparison.Ordinal);
    }

    /// <summary>JSON 格式无效这条分支：同上，原因要回、令牌形状要被吃掉。</summary>
    [Fact]
    public void InvalidJson_RedactsTokenShapes()
    {
        var detail = "第 3 行第 12 列；token=APS-abcdefghijklmnopqrst；Bearer zyxwvuts98765432";
        var message = ErrorCopy.InvalidJson(Redactor.Redact(detail));

        Assert.Contains("第 3 行第 12 列", message, StringComparison.Ordinal);
        Assert.DoesNotContain("APS-abcdefghijklmnopqrst", message, StringComparison.Ordinal);
        Assert.DoesNotContain("zyxwvuts98765432", message, StringComparison.Ordinal);
        Assert.Contains("已脱敏", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 路径**不**脱敏，这是有意的口径而不是漏网：400 分支的详情是用户自己填错的那一句，
    /// 路径往往正是他要改的那一处（「找不到 C:\\repo\\appsettings.json」删掉路径就无从下手）。
    /// 凭据是秘密、路径不是——本用例把这条取舍钉住，免得下一轮有人「顺手把路径也遮了」
    /// 把可操作的错误变成一句无从下手的空话。
    /// </summary>
    [Fact]
    public void InputErrors_KeepTheUsersOwnPath()
    {
        const string detail = @"找不到文件 C:\repo\src\appsettings.json";
        var message = ErrorCopy.BadRequest(Redactor.Redact(detail));
        Assert.Contains(@"C:\repo\src\appsettings.json", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 接线：两条 400 分支都必须先脱敏再拼文案。只测文案不够——把 <c>Redactor.Redact</c> 从
    /// 接线里删掉，上面两条用例照样绿（它们测的是组合，不是接线）。
    /// </summary>
    [Fact]
    public void ErrorHandling_RedactsBoth400Branches()
    {
        var badRequest = CatchBlock("catch (BadHttpRequestException ex)");
        Assert.Contains("ErrorCopy.BadRequest(Redactor.Redact(ex.Message))", badRequest, StringComparison.Ordinal);

        var invalidJson = CatchBlock("catch (JsonException ex)");
        Assert.Contains("ErrorCopy.InvalidJson(Redactor.Redact(ex.Message))", invalidJson, StringComparison.Ordinal);
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
        Assert.DoesNotContain("请求格式无效：\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON 格式无效：\"", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCopy.BadRequest(", source, StringComparison.Ordinal);
        Assert.Contains("ErrorCopy.InvalidJson(", source, StringComparison.Ordinal);
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
