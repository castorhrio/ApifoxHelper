using ApifoxHelper.Comments;
using ApifoxHelper.Contracts;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「解析失败」那两条路径交出 <c>UserException.Message</c> 时的口径（第三百四十六轮，控制仓简报 P0-2 选 A）。
/// <para>
/// <b>补的是哪个口</b>：<c>Comments/ModelClient.cs</c> 的 <c>ParseResult</c> 与
/// <c>Contracts/OpenApiSource.cs</c> 的 <c>Parse</c>，两处 <c>catch (JsonException ex)</c> 此前都拼
/// <c>"…：" + ex.Message</c>。第一百五十四轮给 HTTP 那一头定下「JSON 解析失败只回<b>结构信息</b>、
/// 不回消息体」，第一百九十六轮把同一条口径接到 CLI 那一头（<c>CliParseErrorTests</c>）——
/// 这两处是同一族里<b>最后没接上的两条</b>，而且此前一条断言都没有：<c>OpenApiSource</c>
/// 在本轮之前没有任何一份测试引用过它。
/// </para>
/// <para>
/// <b>为什么这两处比 HTTP 那一头更要紧</b>：界面这一头读的是 <c>UserException.Message</c>，而且<b>原样念</b>——
/// 原生界面 38 处 <c>catch (UserException ex)</c> 走的都是 <c>SetStatus(ex.Message)</c>（本轮普查现读的数，
/// 登记在 <c>docs/NOTES.md</c>「原生界面错误文案的两条通道」那一节）。那 38 处之所以敢不脱敏，
/// 靠的是这条通道是<b>授权文案</b>：<c>ServiceCopyTests</c> 扫 <c>new UserException("…")</c> 的字面量。
/// 可那条扫描面判的是<b>写下来的那一句</b>，判不到运行时拼进去的那一段：<c>ex.Message</c> 是解析器生成的
/// 英文句子，后面还跟着 <c>Path: $.candidates[0].text</c> 这一类<b>从被解析的那份内容里取出来的</b>片段。
/// 于是「授权通道」这个前提在抛出这一头被绕过了一次，用户读到的是英文框架描述 + 原文片段。
/// </para>
/// <para>
/// <b>这一族因此收成三条</b>：① 下面几处真执行断言——喂进真实的坏 JSON，读回来的那一句里既没有框架的
/// 英文句子、也没有原文片段，只有「哪一份输入」与「第几行第几个字节」；②
/// <see cref="NoUserExceptionCall_EchoesAnExceptionMessageIntoUserCopy"/>——封闭扫描面上任何一处
/// <c>new UserException(…)</c> 都不许再拼一个未经包装的异常消息；③
/// <see cref="PositionClause_IsWrittenInOnePlaceOnly"/>——「第几行第几个字节」那一句在全部产品源码里
/// 只许有一份。抓不抓得住由 <see cref="ThrowEchoDetector_ReportsTheBreaksItMustCatch"/> 拿注入当场验。
/// </para>
/// </summary>
public class JsonInputErrorTests
{
    /// <summary>
    /// 坏 JSON 里埋的那一串记号。它出现在被解析的内容里，所以一旦 <c>ex.Message</c> 被回显，
    /// 它就会跟着进错误行——判「它不在」判的是这条通道，不是解析器的脾气。
    /// </summary>
    private const string Marker = "APS-SUPERSECRET1234567890";

    /// <summary>模型返回的候选：约定里 <c>candidates[].text</c> 是字符串，这里给成一个对象。</summary>
    private const string WrongTypeCandidate =
        "{\n" +
        "  \"format\": \"apifox-candidates\",\n" +
        "  \"candidates\": [\n" +
        "    { \"text\": { \"" + Marker + "\": 1 } }\n" +
        "  ]\n" +
        "}";

    /// <summary>Swagger 文档：属性名的位置上出现非法记号，解析器在那里报错并把那一个字符写进消息。</summary>
    private const string BrokenSwaggerDocument =
        "{\n" +
        "  \"openapi\": \"3.0.1\",\n" +
        "  " + Marker + ": {}\n" +
        "}";

    /// <summary>取 <c>UserException.Message</c>：界面读的就是这一句，断言必须落在它上面。</summary>
    private static string MessageOf(Action action)
    {
        var ex = Assert.Throws<UserException>(action);
        return ex.Message;
    }

    /// <summary>
    /// 模型那一条：<b>点名是哪一份输入</b>、给结构信息，框架的英文句子与原文片段一律不出现。
    /// <para>
    /// 变异注入（把这一处改回拼 <c>ex.Message</c>）时这一条会红，红的是三件事同时不成立：句子变成
    /// 「候选结果不是约定的 JSON 格式：The JSON value could not be converted…」，里面既带着
    /// <c>Path: $.candidates[0].text</c>（从模型返回的那份内容里取出来的），又没有一句能照着改的话。
    /// </para>
    /// </summary>
    [Fact]
    public void ModelClient_OnWrongTypeCandidate_NamesTheInputAndKeepsTheFrameworkSentenceOut()
    {
        var message = MessageOf(() => ModelClient.ParseResult(WrongTypeCandidate));

        Assert.StartsWith("候选结果不是约定的 JSON 格式（第 ", message, StringComparison.Ordinal);
        Assert.EndsWith(" 个字节处）。", message, StringComparison.Ordinal);
        // 框架的英文描述不是用户能照着改的那一句，也不该出现在中文状态行里。
        Assert.DoesNotContain("could not be converted", message, StringComparison.Ordinal);
        Assert.DoesNotContain("is invalid", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected", message, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("BytePositionInLine:", message, StringComparison.Ordinal);
        // 从被解析的内容里取出来的东西：路径与记号本身都不许跟着进错误行。
        Assert.DoesNotContain("Path:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("$.candidates", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, message, StringComparison.Ordinal);
    }

    /// <summary>Swagger 那一条：同一形状，点名的换成「Swagger JSON 无法解析」。</summary>
    [Fact]
    public void OpenApiSource_OnBrokenSwaggerDocument_NamesTheInputAndKeepsTheRawTokenOut()
    {
        var message = MessageOf(() => OpenApiSource.Parse(BrokenSwaggerDocument, new OpenApiInfo()));

        Assert.StartsWith("Swagger JSON 无法解析（第 ", message, StringComparison.Ordinal);
        Assert.EndsWith(" 个字节处）。", message, StringComparison.Ordinal);
        // 解析器在那一处报的是「'A' is an invalid start of a property name. Expected a '"'」：
        // 引号里那一个字符取自被解析的那份文件，English 那半句也不是用户能照着改的话。
        Assert.DoesNotContain("is an invalid start", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Expected a", message, StringComparison.Ordinal);
        Assert.DoesNotContain("LineNumber:", message, StringComparison.Ordinal);
        Assert.DoesNotContain("BytePositionInLine:", message, StringComparison.Ordinal);
        Assert.DoesNotContain(Marker, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// 位置那半句在<b>全部产品源码里只许有一份</b>。
    /// <para>
    /// 这一条挡的是「两处各自拼一句『第 N 行第 M 个字节』」——那是第一百九十六轮要收掉的「两句话并存」
    /// 换了个出口：口径改了而漏改其中一处，两条错误路径就从此给出两个不同的数，而它们各自都还合规。
    /// 取法与 <see cref="NoUserExceptionCall_EchoesAnExceptionMessageIntoUserCopy"/> 共用同一份
    /// <see cref="ScannedCode"/>（剥掉注释的代码），所以注释里引用这一句不会把它数成第二份。
    /// </para>
    /// </summary>
    [Fact]
    public void PositionClause_IsWrittenInOnePlaceOnly() =>
        Assert.Equal(Carriers("行，第 ", ScannedCode()), ["Infrastructure/JsonErrorCopy.cs"]);

    /// <summary>
    /// 共用口径的两个分支都真被执行到：<b>有位置</b>时接结构信息、<b>没有位置</b>时交出调用方那一句本身。
    /// 取不到位置时不回通用的「JSON 格式无效，请检查请求内容后重试」——那一句没点名是哪一份输入，
    /// 而调用方那一句已经点了名，换过去反而丢信息（这是与 HTTP 那一头唯一的分别，写在方法注释里）。
    /// </summary>
    [Fact]
    public void NamedOverload_GivesPositionWhenTheParserGivesOne_AndItsOwnSentenceOtherwise()
    {
        var positioned = JsonErrorCopy.InvalidJson("候选结果不是约定的 JSON 格式", Caught(() => JsonNode.Parse("{ bad }")));
        Assert.Equal("候选结果不是约定的 JSON 格式（第 1 行，第 3 个字节处）。", positioned);

        // 不是解析器抛的（没有 LineNumber / BytePositionInLine）：不猜位置，交出那一句本身。
        Assert.Equal(
            "候选结果不是约定的 JSON 格式。",
            JsonErrorCopy.InvalidJson("候选结果不是约定的 JSON 格式", new JsonException()));

        // 单参那一支的两句都没被本轮的重构改动（值逐字未变，ErrorCopyTests 那五条仍按原句判）。
        Assert.Equal("JSON 格式无效（第 1 行，第 3 个字节处）。", JsonErrorCopy.InvalidJson(Caught(() => JsonNode.Parse("{ bad }"))));
        Assert.Equal(JsonErrorCopy.InvalidJsonMessage, JsonErrorCopy.InvalidJson(new JsonException()));
    }

    private static JsonException Caught(Func<object?> parse)
    {
        var ex = Record.Exception(() => { parse(); });
        // 真实抛出的是派生的 JsonReaderException（System.Text.Json 的实现细节），这一条要的是
        // 「它是 JsonException」——产品那两处 catch 接的也正是这个基类。
        Assert.IsAssignableFrom<JsonException>(ex);
        return (JsonException)ex!;
    }

    /// <summary>
    /// 本轮改掉的两处，<b>接的确实是那一份共用口径</b>，不是各自另拼一句「第 N 行第 M 个字节」：
    /// 实参里带着各自点名的那一份输入。
    /// </summary>
    [Fact]
    public void TheTwoFixedPaths_CallTheSharedSource_NamingTheirOwnInput()
    {
        foreach (var (relative, named) in new[]
                 {
                     ("Comments/ModelClient.cs", "候选结果不是约定的 JSON 格式"),
                     ("Contracts/OpenApiSource.cs", "Swagger JSON 无法解析"),
                 })
        {
            var parts = relative.Split('/');
            var code = CodeScan.Text(Repo.Source(parts[0], parts[1]));
            Assert.Contains($"JsonErrorCopy.InvalidJson(\"{named}\", ex)", code, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// 封闭扫描面上<b>任何一处</b> <c>new UserException(…)</c> 都不许把一个异常消息未经包装地拼进去。
    /// <para>
    /// <b>判的是被承诺的实物</b>：承诺是「<c>UserException.Message</c> 是授权文案，所以界面那一头 38 处
    /// 可以不脱敏」，这一条判的是抛出这一头有没有真的守住它。扫描面是 <c>Repo.ServiceSources()</c> 那十个目录
    /// 加上 <c>Desktop/</c> 自己那几处抛点（本轮现读：界面那九处只拼标签与固定文案，没有一处拼异常消息）。
    /// 按那五步判（<c>docs/NOTES.md</c> 顶部「假门防护这一族」那一行末尾）：断言指向的是<b>产品源码里的调用</b>，
    /// 不在被它判的那个对象里 → <b>对账门</b>，不属那一族。
    /// </para>
    /// <para>
    /// <b>允许的两份包装，清单封闭</b>：<c>Redactor.Redact(…)</c>（已知形状的脱敏）与
    /// <c>JsonErrorCopy.InvalidJson(…)</c> / <c>ErrorCopy.InvalidJson(…)</c>（只给结构、压根不回消息）。
    /// 新增第三种包装要同时改这一份清单并写清它凭什么算包装——清单放宽一寸，这一条就哑一寸。
    /// </para>
    /// <para>
    /// <b>取法的边界，如实写在这里</b>：只读<b>代码</b>（<c>CodeScan.Text</c>，剥掉注释），与第三百四十五轮
    /// 那一族同一口径——注释里写一句 <c>new UserException(ex.Message)</c> 不该把红写成绿。
    /// 代价是反向的：一句<b>写在字符串字面量里</b>的 <c>x.Message</c> 会被当成真引用报出来。
    /// 这个方向是宁可红（本轮四发注入里第 ⑤ 发判的就是这一条不会漏掉真泄漏），
    /// 而 <c>$"…{ex.Message}"</c> 那一支插值写法照样被抓住，因为取法不剥字面量。
    /// 逐行取实参的前提是「<c>new UserException(</c> 那一行含完整实参」，由
    /// <c>ServiceCopyTests.UserExceptionCalls_AreSingleLine</c> 钉着，本条不抄第二份判法。
    /// </para>
    /// </summary>
    [Fact]
    public void NoUserExceptionCall_EchoesAnExceptionMessageIntoUserCopy()
    {
        Assert.Empty(ThrowEchoProblems(ScannedCode()));
    }

    /// <summary>扫描面：服务层那十个目录 + 界面那几处抛点（相对路径进红话，两个同名文件不互相冒充）。</summary>
    private static IEnumerable<(string Relative, string Code)> ScannedCode()
    {
        foreach (var source in Repo.ServiceSources()) yield return (source.Relative, CodeScan.Text(source.Text));
        foreach (var source in Repo.NativeSources()) yield return ("Desktop/Native/" + source.Name, CodeScan.Text(source.Text));
        foreach (var file in Directory.GetFiles(Path.Combine(Repo.Root, "Desktop"), "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(Repo.Root, file).Replace('\\', '/');
            if (relative.Contains("/Native/", StringComparison.Ordinal)) continue;
            yield return (relative, CodeScan.Text(File.ReadAllText(file)));
        }
    }

    /// <summary>允许的两份包装：一份脱敏、一份只给结构。清单封闭，放宽一寸就哑一寸。</summary>
    private static readonly string[] AllowedWrappers =
        ["Redactor.Redact(", "JsonErrorCopy.InvalidJson(", "ErrorCopy.InvalidJson("];

    /// <summary>
    /// 判据本体（上面那条与下面的注入实测共用同一份，不各写一遍）。
    /// <para>
    /// <b>盯的是 catch 绑住的那个变量</b>，不是「任何取 <c>.Message</c> 的表达式」。第一版按后者取，
    /// 当场把 <c>Comments/CommentPlanService.cs</c> 那两处 <c>plan.Message</c> 与
    /// <c>Sync/SyncExecutor.cs</c> 那处 <c>action.Message</c> 一起报了——那三处取的是<b>计划模型上的字段</b>
    /// （值是仓里写好的那一句，落盘之后读回来），不是异常消息，报它们是噪声。反过来按名字绑定的写法，
    /// 改名也跑不掉：<c>catch (Exception err)</c> 里那个 <c>err</c> 一样进这份名单。
    /// </para>
    /// </summary>
    private static List<string> ThrowEchoProblems(IEnumerable<(string Relative, string Code)> scanned)
    {
        var problems = new List<string>();
        foreach (var (relative, code) in scanned)
        {
            var caught = CatchBoundNames(code);
            if (caught.Count == 0) continue;
            foreach (var line in code.Split('\n'))
            {
                var at = line.IndexOf("new UserException(", StringComparison.Ordinal);
                if (at < 0) continue;
                var arguments = StripAllowedWrappers(line[at..]);
                var leak = Regex.Match(arguments, @"\b(" + string.Join('|', caught) + @")\.Message\b");
                if (!leak.Success) continue;
                problems.Add($"{relative}：new UserException(…) 里拼了未经包装的「{leak.Value}」——"
                    + "这一句会原样进原生状态行（那 38 处 catch (UserException ex) 不脱敏，靠的就是这条通道是授权文案）。"
                    + $"要留详情就包一层：{string.Join(" 或 ", AllowedWrappers)}");
            }
        }

        return problems;
    }

    /// <summary>这一份源码里<b>被 catch 绑住</b>的变量名（<c>catch (JsonException ex)</c> 取 ex，改名一样取到）。</summary>
    private static List<string> CatchBoundNames(string code) =>
        Regex.Matches(code, @"catch\s*\(\s*[A-Za-z_][\w.<>]*\s+(\w+)\s*\)")
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 把允许的包装连实参一起摘掉，反复到摘不动为止——<c>Redactor.Redact(TextUtil.Truncate(x))</c>
    /// 这种嵌套也要摘得干净，摘完剩下的才是「未经包装」那一部分。
    /// </summary>
    private static string StripAllowedWrappers(string text)
    {
        while (true)
        {
            var next = RemoveOneWrapper(text);
            if (next == text) return text;
            text = next;
        }
    }

    private static string RemoveOneWrapper(string text)
    {
        foreach (var wrapper in AllowedWrappers)
        {
            var start = text.IndexOf(wrapper, StringComparison.Ordinal);
            if (start < 0) continue;
            var open = start + wrapper.Length - 1;
            var end = MatchingParen(text, open);
            if (end < 0) continue;
            return text[..start] + text[(end + 1)..];
        }

        return text;
    }

    /// <summary>从左括号位起找到配对的右括号位；配不上返回 -1（宁可判不出来也不猜）。</summary>
    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = open; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped) { escaped = false; continue; }
            if (ch == '\\') { escaped = true; continue; }
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '(') depth++;
            else if (ch == ')')
            {
                depth--;
                if (depth == 0) return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 判据自己被抓得住：每一发注入都必须报出来，合规的写法一律不报。七发都是现读到的形状，不是编的。
    /// <para>
    /// 每一发交进去的是<b>完整的两行</b>（<c>catch (…) ex</c> 那一行 + <c>throw</c> 那一行）——判据认的是
    /// 「这一份源码里被 catch 绑住的那个名字」，只喂 <c>throw</c> 那一行的话，名单是空的，
    /// 于是「没报」既可能是判据对、也可能是取法根本没看见。形状与源码一致，这一条才判得到东西。
    /// </para>
    /// </summary>
    [Fact]
    public void ThrowEchoDetector_ReportsTheBreaksItMustCatch()
    {
        // ① 本轮改掉的那两处，逐字取自改之前的源码文本：报两条。
        var before = new[]
        {
            ("Comments/ModelClient.cs", @"        catch (JsonException ex)
        {
            throw new UserException(""候选结果不是约定的 JSON 格式："" + ex.Message);"),
            ("Contracts/OpenApiSource.cs", @"        catch (JsonException ex)
        {
            throw new UserException(""Swagger JSON 无法解析："" + ex.Message);"),
        };
        Assert.Equal(2, ThrowEchoProblems(before).Count);

        // ② 三种合法形状：包了脱敏、包了结构口径、拼的是 CliResult.Describe()（不是异常消息）——都不报。
        var wrapped = new[]
        {
            ("Comments/ModelClient.cs", @"        catch (HttpRequestException ex)
        {
            throw new UserException(""无法连接模型服务："" + Redactor.Redact(ex.Message));"),
            ("Comments/ModelClient.cs", @"        catch (JsonException ex)
        {
            throw new UserException(JsonErrorCopy.InvalidJson(""候选结果不是约定的 JSON 格式"", ex));"),
            ("Api/ApifoxRoutes.cs", @"        if (!result.Ok) throw new UserException(ErrorCopy.ProjectsReadFailedMessage + result.Describe());"),
        };
        Assert.Empty(ThrowEchoProblems(wrapped));

        // ③ 一句话里包了一半：外层脱敏包住了 ex.Message，后面那一个没有——报一条并点名那一个。
        var half = ThrowEchoProblems(new[]
        {
            ("Sync/SyncExecutor.cs", @"        catch (Exception ex)
        {
            throw new UserException(""写入失败："" + Redactor.Redact(ex.Message) + ""；"" + inner.Message);
        }
        catch (InvalidOperationException inner) { }"),
        });
        Assert.Single(half);
        Assert.Contains("inner.Message", half[0], StringComparison.Ordinal);

        // ④ 插值写法里的泄漏：取法不剥字面量，所以这一条抓得到（剥了就会漏掉它）。
        var interpolated = ThrowEchoProblems(new[]
        {
            ("Sync/SyncExecutor.cs", @"        catch (JsonException ex)
        {
            throw new UserException($""写入失败：{ex.Message}"");"),
        });
        Assert.Single(interpolated);

        // ⑤ 注释里写一句同样的话：判的是代码，不报（第三百四十五轮那一族收的同一个形状）。
        Assert.Empty(ThrowEchoProblems(new[]
        {
            ("Comments/ModelClient.cs", @"    // 此前这里拼的是 new UserException(""…："" + ex.Message)，本轮收进结构口径。"),
        }));

        // ⑥ 取的是计划模型上的字段，不是异常消息：本轮第一版误报过的三处形状，现在一条都不报。
        Assert.Empty(ThrowEchoProblems(new[]
        {
            ("Comments/CommentPlanService.cs", @"        catch (IOException ex) { File.AppendAllText(log, ex.ToString()); }
        plan.Message = ""生成计划后草稿或扫描结果有变化，请重新生成计划。"";
        throw new UserException(plan.Message, 409);"),
            ("Sync/SyncExecutor.cs", @"        catch (IOException ex) { File.AppendAllText(log, ex.ToString()); }
        throw new UserException(action.Message + ""；回读未找到唯一的目标资源，已停止。"");"),
        }));

        // ⑦ 改名跑不掉：catch 里那个变量不叫 ex，那一处照样报。
        var renamed = ThrowEchoProblems(new[]
        {
            ("Sync/SyncExecutor.cs", @"        catch (JsonException err)
        {
            throw new UserException(""输出无法可靠解析："" + err.Message),"),
        });
        Assert.Single(renamed);
        Assert.Contains("err.Message", renamed[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// 「产品源码里谁写了这一串字」的取法本体，<see cref="PositionClause_IsWrittenInOnePlaceOnly"/> 与
    /// <see cref="PositionClauseDetector_ReportsASecondCopy"/> 共用一份：判据改了一处、注入实测没跟着改，就是白测。
    /// </summary>
    private static List<string> Carriers(string fragment, IEnumerable<(string Relative, string Code)> scanned) =>
        scanned.Where(s => s.Code.Contains(fragment, StringComparison.Ordinal))
            .Select(s => s.Relative)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 这一条取法自己被抓得住：注入「第二份抄写」时必须数出两个文件。
    /// <para>
    /// 挡的是取法被写坏成「永远只回那一个文件名」——那种坏法会让上面那条判据变成一句自证的话（删掉
    /// <c>JsonErrorCopy</c> 里那一处、在界面上另拼一句，两条都还绿）。注入的这一份与真抄写同形状：
    /// 代码行、同样的七个字、在 <c>Infrastructure/JsonErrorCopy.cs</c> 之外。
    /// </para>
    /// </summary>
    [Fact]
    public void PositionClauseDetector_ReportsASecondCopy()
    {
        var secondCopy = ("Desktop/Native/ScanResultsDialog.cs", @"SetStatus(""第 1 行，第 2 个字节处"");");
        var carriers = Carriers("行，第 ", ScannedCode().Append(secondCopy));

        Assert.Equal(2, carriers.Count);
        Assert.Contains("Infrastructure/JsonErrorCopy.cs", carriers, StringComparer.Ordinal);
        Assert.Contains("Desktop/Native/ScanResultsDialog.cs", carriers, StringComparer.Ordinal);

        // 「注释里的引用不算第二份」不在这里重判：取法就是 ScannedCode 交出来的代码文本，
        // 它剥注释的那一步已由 ThrowEchoDetector_ReportsTheBreaksItMustCatch 的第 ⑤ 发当场验过。
    }
}
