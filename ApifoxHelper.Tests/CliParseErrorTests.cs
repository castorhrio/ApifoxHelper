using ApifoxHelper.Apifox;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// CLI 输出解析失败那条错误路径的口径（第一百九十六轮，简报 P0-2）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>Api/ErrorCopy.cs</c> 的 <c>InvalidJson</c> 第一百五十四轮就收成了
/// 「只回结构信息」、第一百五十七轮把「列」改成「字节」，并有五条真执行断言钉住；
/// 而 <c>Apifox/ApifoxCli.cs</c> 的 <c>Parse</c> 里<b>同一个形状</b>的 <c>catch (JsonException ex)</c>
/// 仍在拼 <c>"JSON 不完整：" + ex.Message</c>——<b>另一条错误路径，此前一条断言都没有</b>。
/// 这条路径比 HTTP 那条更该收：CLI 的输出可能来自远端，<c>ex.Message</c> 会带用户原文片段，
/// 而 <c>ParseError</c> 经 <c>CliResult.Describe()</c> 拼成「输出无法可靠解析：…」后
/// 原样进原生界面的状态行与错误行（<c>Sync/SyncExecutor.cs</c> 也按它判失败）。
/// </para>
/// <para>
/// <b>为什么口径本体落在 <c>Infrastructure/JsonErrorCopy.cs</c></b>：<c>Api/</c> 已经引用 <c>Apifox/</c>
/// （<c>Api/ApifoxRoutes.cs</c>），让 <c>Apifox/</c> 反过来引用 <c>Api/ErrorCopy</c> 就是分层倒置。
/// 本文件同时钉住「两边指向同一份」与「没有倒置」两件事——只钉前者的话，下一次「顺手把
/// <c>using ApifoxHelper.Api;</c> 加回去」照样绿。
/// </para>
/// </summary>
public class CliParseErrorTests
{
    /// <summary>造一次真实的解析失败：输出被截断（CLI 被中断、管道断开时最常见的那一支）。</summary>
    private static ProcessResult TruncatedOutput() => new(
        ExitCode: 0,
        StdOut: "{\"success\":true,\"data\":{\"api_key\":\"sk-abcdefghijklmnopqrstuvwxyz\",\"name\":\"订单\"",
        StdErr: "",
        TimedOut: false,
        Elapsed: TimeSpan.Zero);

    /// <summary>
    /// 解析失败只回结构信息，<b>不回 <c>ex.Message</c></b>。
    /// <para>
    /// 变异注入（把 <c>ParseError</c> 改回拼 <c>ex.Message</c>）时这条会红：<c>ex.Message</c> 里带着
    /// 出错位置附近的那段原文，假令牌与字段名会一起进错误行。
    /// </para>
    /// </summary>
    [Fact]
    public void Parse_OnTruncatedJson_ReportsStructureOnly_NotTheMessageBody()
    {
        var result = ApifoxCli.Parse(TruncatedOutput());

        Assert.NotNull(result.ParseError);
        // 结构信息在：与 HTTP 那条路径同一句口径（第几行、第几个字节）。
        Assert.StartsWith("JSON 格式无效（第 ", result.ParseError, StringComparison.Ordinal);
        Assert.EndsWith(" 个字节处）。", result.ParseError, StringComparison.Ordinal);
        // 消息体不在：假令牌、字段名、英文原文、异常类型名一律不出现。
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", result.ParseError, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", result.ParseError, StringComparison.Ordinal);
        Assert.DoesNotContain("订单", result.ParseError, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonException", result.ParseError, StringComparison.Ordinal);
        Assert.DoesNotContain("could not", result.ParseError, StringComparison.OrdinalIgnoreCase);
        // 旧口径那一句也不许回来：两句话并存正是这一轮要收掉的东西。
        Assert.DoesNotContain("JSON 不完整", result.ParseError, StringComparison.Ordinal);
    }

    /// <summary>
    /// 用户读到的是<b>一句</b>：<c>Describe()</c> 拼出来的整句里同样没有原文片段
    /// （状态行与错误行读的是这一句，不是 <c>ParseError</c> 本身）。
    /// </summary>
    [Fact]
    public void Describe_OnTruncatedJson_LeaksNothingFromTheOutput()
    {
        var described = ApifoxCli.Parse(TruncatedOutput()).Describe();

        Assert.StartsWith("输出无法可靠解析：", described, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", described, StringComparison.Ordinal);
        Assert.DoesNotContain("api_key", described, StringComparison.Ordinal);
    }

    /// <summary>
    /// 接线：<c>ApifoxCli.Parse</c> 的 <c>catch (JsonException ex)</c> 分支从共享口径取文案，
    /// 且<b>不再读 <c>ex.Message</c></b>。只测文案不够——把接线改回拼 <c>ex.Message</c>，
    /// 上面两条照样绿（它们测的是文案本身）。
    /// </summary>
    [Fact]
    public void Parse_TakesTheJsonCopyFromTheSharedSource()
    {
        var block = CatchBlock("Apifox/ApifoxCli.cs", "catch (JsonException ex)");
        Assert.Contains("JsonErrorCopy.InvalidJson(ex)", block, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", block, StringComparison.Ordinal);
        Assert.DoesNotContain("JSON 不完整", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// 口径只有一份：<c>Api/ErrorCopy</c> 与 <c>Apifox/ApifoxCli</c> 都指向
    /// <c>Infrastructure/JsonErrorCopy</c>，两边都不再各写一句实现。
    /// </summary>
    [Fact]
    public void BothPaths_PointAtTheSameNeutralCopy()
    {
        var api = Repo.Source("Api", "ErrorCopy.cs");
        Assert.Contains("JsonErrorCopy.InvalidJson(ex)", api, StringComparison.Ordinal);
        Assert.Contains("JsonErrorCopy.InvalidJsonMessage", api, StringComparison.Ordinal);
        // HTTP 层不再自己拼那一句（转发而不是复制一份实现）。
        Assert.DoesNotContain("个字节处", api, StringComparison.Ordinal);

        var cli = Repo.Source("Apifox", "ApifoxCli.cs");
        Assert.Contains("JsonErrorCopy.InvalidJson(ex)", cli, StringComparison.Ordinal);
        Assert.DoesNotContain("个字节处", cli, StringComparison.Ordinal);
    }

    /// <summary>
    /// 没有分层倒置：<c>Apifox/</c> 不引用 <c>Api/</c>（<c>Api/</c> 已经引用 <c>Apifox/</c>，
    /// 反向引用会让两个目录互为依赖），口径本体也不引用 ASP.NET Core 与 WinForms
    /// （否则 Linux 侧共享编译不成立）。
    /// </summary>
    [Fact]
    public void TheNeutralCopy_KeepsTheLayeringIntact()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Repo.Root, "Apifox"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("using ApifoxHelper.Api;", text, StringComparison.Ordinal);
            Assert.DoesNotContain("ApifoxHelper.Api.", text, StringComparison.Ordinal);
        }

        var copy = Repo.Source("Infrastructure", "JsonErrorCopy.cs");
        Assert.Contains("using System.Text.Json;", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.AspNetCore", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Forms", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("ApifoxHelper.Api", copy, StringComparison.Ordinal);
    }

    /// <summary>共享编译：口径文件必须进测试工程的编译清单，否则上面的断言测的是另一份代码。</summary>
    [Fact]
    public void TheNeutralCopy_IsSharedCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Infrastructure\JsonErrorCopy.cs", csproj);
    }

    /// <summary>
    /// 去掉行注释：分支里交代理由的注释会引用 <c>ex.Message</c> 这个名字（「不回 ex.Message」），
    /// 那是解释而不是第二份实现。判「有没有读它」只看代码行（与 <c>StatusLineTextTests.CodeOnly</c> 同一口径）。
    /// </summary>
    private static string CodeOnly(string body) =>
        string.Join('\n', body.Split('\n').Select(l =>
        {
            var at = l.IndexOf("//", StringComparison.Ordinal);
            return at < 0 ? l : l[..at];
        }));

    /// <summary>取一个 <c>catch (…)</c> 分支的方法体（按花括号深度界定，不靠缩进猜）。</summary>
    private static string CatchBlock(string relativePath, string header)
    {
        var source = Repo.Source(relativePath.Split('/'));
        var start = source.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{relativePath} 里找不到「{header}」分支。");
        var open = source.IndexOf('{', start);
        Assert.True(open > start, $"「{header}」后面没有方法体。");

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return CodeOnly(source[open..(i + 1)]);
        }

        Assert.Fail($"「{header}」的方法体没有闭合。");
        return "";
    }
}
