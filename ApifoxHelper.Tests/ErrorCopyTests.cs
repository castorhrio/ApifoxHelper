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
    /// JSON 格式无效只回结构信息（第几行第几个字节），**不回消息体**（第一百五十四轮）。
    /// <para>
    /// 这条是「敏感片段不进错误行」的真执行断言：往 <c>JsonException.Message</c> 里注入一段假令牌
    /// 与用户原文，输出里必须只剩位置信息。变异注入（把 <c>InvalidJson</c> 改回拼 <c>ex.Message</c>）
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

        // 结构信息在，且是 1 基（用户数行从 1 开始）；位置按 UTF-8 字节计（第一百五十七轮）。
        Assert.Equal("JSON 格式无效（第 3 行，第 12 个字节处）。", message);
        // 消息体不在：假令牌、字段名、英文原文一律不出现。
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", message, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", message, StringComparison.Ordinal);
        Assert.DoesNotContain("could not", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("原文片段", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 解析器真抛的那一次同样只回结构信息（不是只有手工构造的异常才这样）：
    /// 用 <c>JsonDocument.Parse</c> 造一个真 <c>JsonException</c>，输出里只有位置信息。
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
        Assert.EndsWith(" 个字节处）。", message, StringComparison.Ordinal);
        Assert.DoesNotContain("APS-abcdefghijklmnopqrst", message, StringComparison.Ordinal);
        Assert.DoesNotContain("token", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 含中文的请求体上，位置按 **UTF-8 字节**计而不是按字符列计（第一百五十七轮，控制仓简报 P0-1）。
    /// <para>
    /// 存在理由：<c>JsonException.BytePositionInLine</c> 的名字与 .NET 8 的实测行为一致——它是行内
    /// UTF-8 字节位，不是字符位。此前文案写「第 N 列」，含中文的请求体上这个数字对不上用户数出来的列
    /// （「名」「值」各占 3 字节），而这一句恰恰是错误路径上用户唯一能照着改的那一句。
    /// 本用例用真解析器造异常，把「字节位 ≠ 字符位」这件事钉成机械门：
    /// 文案若改回「列」、或有人把字节位当字符位直接输出，这条即红。
    /// </para>
    /// </summary>
    [Fact]
    public void InvalidJson_OnChineseBody_ReportsBytePositionNotCharacterColumn()
    {
        // 第 1 行第 10 个字符（1 基）是第二个逗号；它前面有「名」「值」两个三字节字符。
        const string json = "{\"名\":\"值\",,}";
        JsonException? caught = null;
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        var bytePosition = caught!.BytePositionInLine ?? throw new InvalidOperationException("解析器没有给出字节位。");
        // 同一偏移按字符数是多少：把前 bytePosition 个字节按 UTF-8 解码。
        var bytes = Encoding.UTF8.GetBytes(json);
        var charPosition = Encoding.UTF8.GetCharCount(bytes, 0, (int)bytePosition);
        // 前提成立才谈得上「口径」：含中文时字节位与字符位确实不同（否则这条用例什么也没钉住）。
        Assert.True(bytePosition > charPosition,
            $"字节位 {bytePosition} 不大于字符位 {charPosition}：这条用例的前提（含中文时两者不同）不成立。");

        var message = ErrorCopy.InvalidJson(caught);

        // 报的是字节位（1 基），不是字符列。
        Assert.Equal($"JSON 格式无效（第 1 行，第 {bytePosition + 1} 个字节处）。", message);
        // 不冒充「列」：用户按字符数出来的列与这个数字不同，说成「列」就是误导。
        Assert.DoesNotContain("列", message, StringComparison.Ordinal);
        // 仍然只回结构信息：原文片段不进错误行。
        Assert.DoesNotContain("名", message, StringComparison.Ordinal);
        Assert.DoesNotContain("值", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 纯 ASCII 的请求体上字节位与字符位相等——此时「第 N 个字节处」就是用户数出来的那一列，
    /// 数字仍然精确（第一百五十七轮）。这条与上一条成对：口径改成字节不是「放弃精度」，
    /// 而是「在两种输入上都说得对」。
    /// </summary>
    [Fact]
    public void InvalidJson_OnAsciiBody_BytePositionEqualsTheCharacterColumn()
    {
        const string json = "{\"name\":1,,}";
        JsonException? caught = null;
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        var bytePosition = caught!.BytePositionInLine ?? throw new InvalidOperationException("解析器没有给出字节位。");
        var bytes = Encoding.UTF8.GetBytes(json);
        var charPosition = Encoding.UTF8.GetCharCount(bytes, 0, (int)bytePosition);
        Assert.Equal(charPosition, bytePosition);

        var message = ErrorCopy.InvalidJson(caught);
        Assert.Equal($"JSON 格式无效（第 1 行，第 {charPosition + 1} 个字节处）。", message);
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

    // ── 路由文案的落点（第三百零一轮，简报 P0-1）────────────────────────

    /// <summary>
    /// <c>Api/ApifoxRoutes.cs</c> 里此前<b>就地拼接</b>的六处文案：字面量 → 常量名（封闭清单）。
    /// <para>
    /// <b>为什么这六处要收</b>：<c>ErrorCopy</c> 的既定方向是「错误文案单一口径」，而
    /// <c>ServiceCopyTests</c> 的扫描面只覆盖 <c>new UserException("…")</c> 的字面量与以 <c>Message</c>
    /// 收尾的常量——<b>就地拼接的前缀两头都不占</b>，于是这六句落在扫描面之外：它们会原样进原生界面的
    /// 状态行与内联错误，却不受中文标点、空话禁词、中英文空隙那几条口径约束。收进常量之后，
    /// 它们自动落进 <c>ServiceCopyTests</c> 的扫描面（常量名以 <c>Message</c> 收尾是落进去的条件）。
    /// </para>
    /// <para>
    /// <b>措辞逐字未变</b>：本轮只动落点，不动一个字（<see cref="RouteCopy_KeepsTheExactWording"/> 钉住）。
    /// </para>
    /// </summary>
    private static readonly (string Literal, string Constant)[] RouteCopySites =
    [
        ("读取项目列表失败：", nameof(ErrorCopy.ProjectsReadFailedMessage)),
        ("读取分支失败：", nameof(ErrorCopy.BranchesReadFailedMessage)),
        ("读取目录失败：", nameof(ErrorCopy.FoldersReadFailedMessage)),
        ("读取接口失败：", nameof(ErrorCopy.EndpointReadFailedMessage)),
        ("还没有同步计划。", nameof(ErrorCopy.NoSyncPlanMessage)),
        ("动作不存在。", nameof(ErrorCopy.ActionNotFoundMessage)),
    ];

    /// <summary>六句的措辞逐字未变（真执行：<c>ErrorCopy</c> 与测试工程共享编译）。</summary>
    [Fact]
    public void RouteCopy_KeepsTheExactWording()
    {
        Assert.Equal("读取项目列表失败：", ErrorCopy.ProjectsReadFailedMessage);
        Assert.Equal("读取分支失败：", ErrorCopy.BranchesReadFailedMessage);
        Assert.Equal("读取目录失败：", ErrorCopy.FoldersReadFailedMessage);
        Assert.Equal("读取接口失败：", ErrorCopy.EndpointReadFailedMessage);
        Assert.Equal("还没有同步计划。", ErrorCopy.NoSyncPlanMessage);
        Assert.Equal("动作不存在。", ErrorCopy.ActionNotFoundMessage);
    }

    /// <summary>
    /// 六句都落进 <c>ServiceCopyTests</c> 的扫描面：常量名必须以 <c>Message</c> 收尾
    /// （那条扫描面的正则就是 <c>const string \w*Message =</c>）。改名成别的收尾，这一条即红——
    /// 否则「收进常量」只是换了个地方写，中文排版那几条口径照样管不到它。
    /// </summary>
    [Fact]
    public void RouteCopy_ConstantsFallIntoTheTypographyScan()
    {
        foreach (var (_, constant) in RouteCopySites)
        {
            Assert.EndsWith("Message", constant, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 六处不再就地拼接（按源码形状判）：路由文件里既不许再出现那六个字面量，
    /// 又必须真的引用到那六个常量；四条「读取…失败：」的详情仍由 <c>Describe()</c> 给
    /// （常量 + 变量，不是把详情也塞进常量）。
    /// </summary>
    [Fact]
    public void Routes_TakeTheSixSentencesFromTheSingleSource() =>
        Assert.Empty(RouteCopyProblems(Repo.Source("Api", "ApifoxRoutes.cs")));

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住：三发注入各报一处，合规源码不报。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。第一发是「就地拼接又回来了」，第二发是「常量引用了但字面量还在」，
    /// 第三发是「详情不再由 Describe() 给」。
    /// </para>
    /// </summary>
    [Fact]
    public void RouteCopyDetector_ReportsTheBreaksItMustCatch()
    {
        var good = Repo.Source("Api", "ApifoxRoutes.cs");
        Assert.Empty(RouteCopyProblems(good));

        // ① 就地拼接又回来了：前缀写回字面量。
        var inPlace = good.Replace(
            "ErrorCopy.ProjectsReadFailedMessage + result.Describe()",
            "\"读取项目列表失败：\" + result.Describe()",
            StringComparison.Ordinal);
        Assert.Contains(RouteCopyProblems(inPlace), p => p.Contains("读取项目列表失败：", StringComparison.Ordinal));

        // ② 常量引用了，但字面量还留在别处（两处各写一份，改一处就漂）。
        var duplicated = good + "\n// throw new UserException(\"读取分支失败：\");\n";
        Assert.Contains(RouteCopyProblems(duplicated), p => p.Contains("读取分支失败：", StringComparison.Ordinal));

        // ③ 详情不再由 Describe() 给：常量后面拼了别的东西（用户读到的就不再是 CLI 的失败描述）。
        var noDetail = good.Replace(
            "ErrorCopy.FoldersReadFailedMessage + result.Describe()",
            "ErrorCopy.FoldersReadFailedMessage",
            StringComparison.Ordinal);
        Assert.Contains(RouteCopyProblems(noDetail), p => p.Contains("Describe()", StringComparison.Ordinal));
    }

    /// <summary>判据本体：路由源码进、问题出（空清单 = 六处都从单一落点取）。</summary>
    private static List<string> RouteCopyProblems(string routes)
    {
        var problems = new List<string>();
        foreach (var (literal, constant) in RouteCopySites)
        {
            if (routes.Contains("\"" + literal + "\"", StringComparison.Ordinal))
            {
                problems.Add($"Api/ApifoxRoutes.cs 里仍有就地拼接的字面量「{literal}」：这一句会原样进界面状态行，"
                    + $"却落在 ServiceCopyTests 的扫描面之外。改成 ErrorCopy.{constant}。");
            }
            if (!routes.Contains("ErrorCopy." + constant, StringComparison.Ordinal))
            {
                problems.Add($"Api/ApifoxRoutes.cs 没有引用 ErrorCopy.{constant}：文案的单一落点没有接上。");
            }
        }

        // 四条「读取…失败：」的详情仍由 Describe() 给：常量 + 变量，不是把详情塞进常量。
        var withDetail = Regex.Matches(routes, @"ErrorCopy\.\w+ReadFailedMessage \+ result\.Describe\(\)").Count;
        if (withDetail != 4)
        {
            problems.Add($"四条「读取…失败：」里只有 {withDetail} 条把 Describe() 的失败描述拼在后面（应为 4）："
                + "详情是 CLI 那一侧的口径，不能在这里丢掉或换成别的东西。");
        }

        return problems;
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
    /// 共享编译的前置条件（第一百五十五轮，控制仓简报 P1-1）：<c>ErrorCopy.cs</c> 用到
    /// <c>JsonException</c>，而它此前只靠主工程 <c>GlobalUsings.cs</c> 里的 global using 拿到
    /// <c>System.Text.Json</c>——测试工程共享编译时靠的又是自己 csproj 里的
    /// <c>&lt;Using Include="System.Text.Json" /&gt;</c>。两边各有一份隐式来源，文件本身没有记录
    /// 「我依赖谁」：任何一边被清理，这个文件就从「可被真执行断言」变成「编译不过」或「靠别人的
    /// 全局设置活着」。现文件自带 using，这条断言钉住它，并同时钉住共享编译的另一半前提
    /// （不引用 ASP.NET Core）。
    /// </summary>
    [Fact]
    public void ErrorCopy_ReferencesSystemTextJsonExplicitly()
    {
        var copy = Repo.Source("Api", "ErrorCopy.cs");
        Assert.Contains("using System.Text.Json;", copy, StringComparison.Ordinal);
        // 用到的类型确实来自这个命名空间（否则上面那条 using 是装饰）。
        Assert.Contains("JsonException", copy, StringComparison.Ordinal);
        // 共享编译的另一半前提：不引用 ASP.NET Core。
        Assert.DoesNotContain("Microsoft.AspNetCore", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpContext", copy, StringComparison.Ordinal);
    }

    // ── 原生界面侧的落点（第三百四十五轮，简报 P1-2）──────────────────────

    /// <summary>
    /// 原生界面此前<b>就地抄写</b>这批句子四处：字面量 → 单一落点（封闭清单）。
    /// <para>
    /// <b>补的是哪个口</b>：第三百零一轮那句「六句都收进 <c>ErrorCopy</c>」判的只有
    /// <c>Api/ApifoxRoutes.cs</c> 一份文件，同一批句子在界面那一头另抄了四份——
    /// <c>ApifoxPicker.cs</c> 两个前缀常量、<c>ProfileAdvancedDialog.cs</c> 两行
    /// <c>SetStatus("读取接口失败：" + …)</c>。那一条的「就地拼接」判据是拿路由文件做的，
    /// 界面文件一个字都没进过它的取法，所以四处全漏而全绿：<b>登记写的是「唯一落点」，实物是两份</b>，
    /// 这一处按「登记与实物不一致」收（与第三百四十二、三百四十三轮那一族同一形状）。
    /// </para>
    /// <para>
    /// <b>为什么两份是要紧事</b>：中文标点与禁词那几条在界面这一头由 <c>TypographyTests</c> 判着，
    /// 收进常量并不改变它受不受排版口径管——改的是<b>漂移</b>。有人把 <c>ErrorCopy</c> 里那一句改准
    /// （「读取接口失败」这一句在冻结页面路由、原生状态行与内联错误里本来就该是同一句话），
    /// 界面那一份照旧不动，两条门各自全绿，用户在同一个产品里因为走的是路由还是对话框读到两种说法。
    /// <c>ApifoxPicker.ProjectIdInvalid</c>（第一百七十四轮）已经立了同法的形状：名字留在界面
    /// （界面、冒烟器与真值表都认它），字面只有一处。
    /// </para>
    /// <para>
    /// <b>值逐字未变</b>：四处都改成引用常量，一个字没动
    /// （<see cref="RouteCopy_KeepsTheExactWording"/> 与 <c>ApifoxPickerTests.FailedPrefixes_MatchRoutes</c>
    /// 仍按原句断言，改一个标点即红）。
    /// </para>
    /// </summary>
    private static readonly (string File, string Constant, string Wiring)[] NativeCopySites =
    [
        ("ApifoxPicker.cs", nameof(ErrorCopy.ProjectsReadFailedMessage), "ErrorCopy.ProjectsReadFailedMessage;"),
        ("ApifoxPicker.cs", nameof(ErrorCopy.BranchesReadFailedMessage), "ErrorCopy.BranchesReadFailedMessage;"),
        ("ProfileAdvancedDialog.cs", nameof(ErrorCopy.EndpointReadFailedMessage),
            "ErrorCopy.EndpointReadFailedMessage + result.Describe()"),
        ("ProfileAdvancedDialog.cs", nameof(ErrorCopy.EndpointReadFailedMessage),
            "ErrorCopy.EndpointReadFailedMessage + Redactor.Redact(ex.Message)"),
    ];

    /// <summary>
    /// 判据本体：原生界面源码进、问题出（空清单 = 这批句子在界面侧只有一个落点）。
    /// <para>
    /// 两侧都判 <see cref="NativeSource.Code"/>（剥掉注释后的代码），不判 <c>Text</c>：注释里点名一句
    /// 文案、写一行 <c>ErrorCopy.XxxMessage</c> 形状的例子，是读者顺着注释找到实物的路，不是第二份字面量，
    /// 也不是接线。第三百零二轮那一族收掉的就是这个形状——判代码的断言拿全文判，注释能把红写成绿。
    /// </para>
    /// </summary>
    private static List<string> NativeCopyProblems(IEnumerable<NativeSource> sources)
    {
        var problems = new List<string>();
        var list = sources.ToList();

        // ① 清单之外不许有新的抄写：六句里任何一句出现在界面代码里都是第二份口径。
        foreach (var (literal, constant) in RouteCopySites)
        {
            foreach (var source in list.Where(s =>
                         s.Code.Contains("\"" + literal + "\"", StringComparison.Ordinal)))
            {
                problems.Add($"{source.Name} 就地写了「{literal}」：这一句会原样进状态行与内联错误，"
                    + $"它是 ErrorCopy.{constant} 之外的第二份字面——改那一句时这里不会红，"
                    + "两条门各自全绿，用户读到的却是两种说法。");
            }
        }

        // ② 四处接线各在自己那一个文件里：只删字面量不接常量，用户读到的就是半句话。
        foreach (var (file, constant, wiring) in NativeCopySites)
        {
            var source = list.FirstOrDefault(s => s.Name == file);
            if (source is null)
            {
                problems.Add($"找不到 {file}：清单登记的界面文件不在 Desktop/Native 下，这一处的接线无从判起。");
                continue;
            }

            if (!source.Code.Contains(wiring, StringComparison.Ordinal))
            {
                problems.Add($"{file} 没有把这一句接到 ErrorCopy.{constant}（要找的形状：{wiring}）："
                    + "常量没引用上，界面读到的就不再是那一句。");
            }
        }

        return problems;
    }

    /// <summary>
    /// 原生界面现读：六句一处都不就地抄，四处接线都在单一落点上。
    /// </summary>
    [Fact]
    public void NativeCode_TakesTheseSentencesFromTheSingleSource() =>
        Assert.Empty(NativeCopyProblems(Repo.NativeSources()));

    /// <summary>
    /// 上面那条判据<b>自己</b>被抓得住：四发注入各报一处，合规源码不报。
    /// <para>
    /// 第一发是「字面量抄回界面代码里」，第二发是「接线拆掉、只剩详情」，
    /// 第三发是「注释里写着引用、代码里没引用」（判 <c>Code</c> 才抓得住，判全文会被骗绿——
    /// 第三百零六轮那一族的同一教训），第四发是「清单之外的第五处新抄写」。
    /// </para>
    /// </summary>
    [Fact]
    public void NativeCopyDetector_ReportsTheBreaksItMustCatch()
    {
        var good = Repo.NativeSources();
        Assert.Empty(NativeCopyProblems(good));

        var picker = good.Single(s => s.Name == "ApifoxPicker.cs").Text;
        var dialog = good.Single(s => s.Name == "ProfileAdvancedDialog.cs").Text;

        // ① 字面量抄回界面代码里：既报这一句，也报接线断（两头都要红）。
        var copiedBack = picker.Replace(
            "ErrorCopy.ProjectsReadFailedMessage;",
            "\"读取项目列表失败：\";",
            StringComparison.Ordinal);
        var copied = NativeCopyProblems([new NativeSource("ApifoxPicker.cs", copiedBack)]);
        Assert.Contains(copied, p => p.Contains("就地写了", StringComparison.Ordinal));
        Assert.Contains(copied, p => p.Contains("没有把这一句接到", StringComparison.Ordinal));

        // ② 接线拆掉：详情还在，前缀没了——用户读到的是半句话。
        var halfSentence = dialog.Replace(
            "ErrorCopy.EndpointReadFailedMessage + result.Describe()",
            "result.Describe()",
            StringComparison.Ordinal);
        Assert.Contains(
            NativeCopyProblems([new NativeSource("ProfileAdvancedDialog.cs", halfSentence)]),
            p => p.Contains("没有把这一句接到", StringComparison.Ordinal));

        // ③ 注释里写着引用、代码里没引用：判 Code 才抓得住。
        var commented = dialog
            .Replace("ErrorCopy.EndpointReadFailedMessage + result.Describe()", "result.Describe()", StringComparison.Ordinal)
            .Replace("/// <summary>从既有接口提取模板",
                "/// <summary>ErrorCopy.EndpointReadFailedMessage + result.Describe()（注释里的形状不算接线）",
                StringComparison.Ordinal);
        Assert.Contains(
            NativeCopyProblems([new NativeSource("ProfileAdvancedDialog.cs", commented)]),
            p => p.Contains("没有把这一句接到", StringComparison.Ordinal));

        // ④ 清单之外的第五处新抄写：换一张表、换一句别的话，同样报。
        var newSite = new NativeSource("CheckResultsDialog.cs",
            "SetStatus(\"读取目录失败：\" + result.Describe(), UiTokens.BadText);");
        Assert.Contains(
            NativeCopyProblems([newSite]),
            p => p.Contains("CheckResultsDialog.cs", StringComparison.Ordinal));
    }

    /// <summary>
    /// 界面侧的落点与路由侧的是<b>同一批句子</b>：清单里每个常量名都必须就是路由那六句之一。
    /// <para>
    /// 这一条挡的是「两边各收各的」：下一轮给界面新造一个 <c>ErrorCopy.EndpointFailedMessage</c>
    /// 而路由那句照旧，两条门各自全绿，同一句话却从此有两个口径、两个名字。
    /// </para>
    /// </summary>
    [Fact]
    public void NativeCopySites_PointAtTheSameConstantsTheRoutesUse()
    {
        var routeConstants = RouteCopySites.Select(s => s.Constant).ToHashSet(StringComparer.Ordinal);
        foreach (var (_, constant, _) in NativeCopySites)
        {
            Assert.Contains(constant, routeConstants);
        }
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
