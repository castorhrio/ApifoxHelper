using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 状态行「查看完整错误」入口的口径（<see cref="StatusLineText"/>，共享源码单一口径）。
/// <para>
/// 这一族断言钉三件事：① 入口只在文字真的放不下时出现（放得下还给入口＝给用户添一步空动作）；
/// ② 判定单调（入口出现会让状态行变窄，变窄只会让更多文字放不下——不单调就会抖动）；
/// ③ 接线只有一处（两个主页面都经 <c>StatusLineHost</c> 写状态行，字面只在口径文件里出现一次）。
/// </para>
/// </summary>
public class StatusLineTextTests
{
    [Theory]
    [InlineData(100, 200, false)] // 放得下：不给入口
    [InlineData(200, 200, false)] // 正好放下：同样不给（多一个像素都没超）
    [InlineData(201, 200, true)]  // 超出一个像素：给
    [InlineData(1000, 200, true)]
    [InlineData(0, 200, false)]   // 没有文字：不给
    public void ShouldOfferFullText_OnlyWhenTextDoesNotFit(int preferred, int available, bool expected) =>
        Assert.Equal(expected, StatusLineText.ShouldOfferFullText(preferred, available));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ShouldOfferFullText_NoWidthMeansNoEntry(int available)
    {
        // 可用宽度为 0 或负数时「放不下」不是文字太长，是这一行还没有宽度（控件未布局、被折叠）：
        // 给入口等于把布局时序当成内容问题。宁可少给一次，不猜。
        Assert.False(StatusLineText.ShouldOfferFullText(1000, available));
    }

    [Fact]
    public void ShouldOfferFullText_IsMonotonic()
    {
        // 单调的方向：状态行越窄越该给入口。固定一句文字，可用宽度从小到大扫一遍，
        // 「放得下」一旦出现就必须一直成立——反过来（宽处不给、窄处又给、再宽又不给）才会出现
        // 「出现 → 变窄 → 消失 → 变宽 → 出现」的抖动，界面层也就不得不预留固定占位。
        // 从 1 起扫：宽度 0 是「这一行还没有宽度」的布局时序例外（上一条 Theory 单独判），
        // 不是内容判定，把它算进单调性会把「最窄的一档不给入口」误读成不单调。
        foreach (var preferred in new[] { 1, 50, 200, 1000 })
        {
            var fits = false;
            for (var available = 1; available <= 2000; available++)
            {
                var offer = StatusLineText.ShouldOfferFullText(preferred, available);
                if (!offer) fits = true;
                else Assert.False(fits, $"preferred={preferred} 在 available={available} 处又从「放得下」退回「给入口」：判定不单调。");
            }
        }
    }

    [Fact]
    public void FullTextButton_IsAVerbPhraseAndNamesWhatItShows()
    {
        Assert.Equal("查看完整错误", StatusLineText.FullTextButton);
        Assert.Equal("完整错误信息", StatusLineText.FullTextHeading);
    }

    [Fact]
    public void FullTextNote_GivesTheNextStepWithoutInventingOne()
    {
        // 说明行讲清这块框能做什么（选中复制）与关掉之后回到哪一步（原处的「重试」），
        // 不写「请联系管理员」这类没有落点的话。
        Assert.Contains("复制", StatusLineText.FullTextNote);
        Assert.Contains("重试", StatusLineText.FullTextNote);
        Assert.DoesNotContain("联系", StatusLineText.FullTextNote);
    }

    [Fact]
    public void Copy_IsCleanOfBannedWordsAndHalfWidthPunctuation()
    {
        foreach (var text in new[] { StatusLineText.FullTextButton, StatusLineText.FullTextHeading, StatusLineText.FullTextNote })
        {
            foreach (var banned in CopyRules.BannedWords) Assert.DoesNotContain(banned, text);
            foreach (var mark in CopyRules.HalfWidthMarks)
            {
                Assert.True(CopyRules.IndexOfHalfWidthMark(text, mark) < 0, $"「{text}」里出现紧挨中文的半角标点「{mark}」。");
            }
        }
    }

    // ── 接线机械检查 ──────────────────────────────────────────────────

    [Fact]
    public void BothPages_WriteStatusThroughTheHost()
    {
        // 判定范围是页面类那一段，不是整份文件：两个页面文件末尾还各嵌着一个对话框
        // （ProfileDialog / NewTaskDialog），它们从第一百六十六轮起同样接了宿主，
        // 由 EveryStatusLine_OnDisk_IsWrittenThroughTheHost 按类逐段判——本断言只管页面类这一段的接线形状。
        foreach (var (file, className) in new[]
                 {
                     ("TasksPage.cs", "TasksPage"),
                     ("ProjectsPage.cs", "ProjectsPage"),
                 })
        {
            var text = PageClassText(file, className);
            Assert.Contains("private readonly StatusLineHost _statusHost;", text);
            Assert.Contains("_statusHost = new StatusLineHost(_status);", text);
            Assert.Contains("toolbar.Controls.Add(_statusHost, 1, 0);", text);
            // 页面不得再直接写标签：入口的显隐与文字必须同一次更新（否则会出现「文字换了、按钮还留着」）。
            Assert.DoesNotContain("_status.Text =", text);
            Assert.DoesNotContain("_status.ForeColor =", text);
            Assert.Contains("_statusHost.SetText(", text);
        }
    }

    /// <summary>
    /// 没接「查看完整错误」入口的状态行（封闭清单，逐条登记理由）。
    /// <para>
    /// 第一百六十五轮之前这里等价于「磁盘上除两个主页面以外的 15 处」；第一百六十六轮全部接入，
    /// 清单收空。清单的作用不是列已做的，而是让「没做的那一处」必须留下名字与理由——
    /// 新增一处 <c>Ui.StatusLine()</c> 而不接宿主，它会自动出现在判定里并判红；
    /// 反过来，登记了却已经不存在的条目同样判红（清单不许留死条目）。
    /// </para>
    /// </summary>
    private static readonly (string File, string Class, string Why)[] StatusLinesWithoutHost = [];

    /// <summary>
    /// 磁盘上所有状态行调用点要么经 <see cref="StatusLineHost"/>、要么在挂账清单里登记理由。
    /// <para>
    /// 按<b>顶格类声明</b>切段判定，不按文件：一个文件里可以既有页面又有对话框（<c>TasksPage.cs</c>
    /// 与 <c>ProjectsPage.cs</c> 各嵌着一个对话框，<c>ScanResultsDialog.cs</c> 嵌着「手工填写说明」），
    /// 拿整份文件判「有宿主就算过」会把没接的那个类算成已做。
    /// </para>
    /// <para>
    /// 地板 17 处是防「扫描本身空转」：判定条件写错、切段切空、正则不匹配时，covered 会掉到 0，
    /// 而「没接的都在清单里」在清单为空时恒真——两条加起来仍然全绿。第一百六十二轮的「空门」同族。
    /// </para>
    /// </summary>
    [Fact]
    public void EveryStatusLine_OnDisk_IsWrittenThroughTheHost()
    {
        var wired = 0;
        var unwired = new List<string>();
        foreach (var source in Repo.NativeSources())
        {
            // 注释行先去掉：宿主类自己的文档注释里引用「Ui.StatusLine()」是在交代理由，不是第二处调用点。
            foreach (var (name, body) in ClassSegments(CodeOnly(source.Text)))
            {
                if (!body.Contains("Ui.StatusLine()", StringComparison.Ordinal)) continue;
                var hostsIt = body.Contains("new StatusLineHost(", StringComparison.Ordinal)
                    && body.Contains("SetText(", StringComparison.Ordinal);
                if (hostsIt) wired++;
                else unwired.Add(source.Name + "." + name);
            }
        }
        Assert.True(wired >= 17, $"经宿主写状态行的只有 {wired} 处（地板 17，第一百六十六轮接入 15 处对话框后共 17 处）：扫描或切段本身失效了。");
        // 左：挂账清单登记的（应为空）；右：磁盘上确实没接宿主的。两侧对不上只有两种情况——
        // 新出现一处没接宿主的（右多），或清单里的条目已经接上了/那处状态行没了（左多，死条目）。
        var ledger = string.Join("、", StatusLinesWithoutHost.Select(e => e.File + "." + e.Class).Order(StringComparer.Ordinal));
        var unwiredIds = string.Join("、", unwired.Order(StringComparer.Ordinal));
        Assert.Equal(ledger, unwiredIds);
    }

    /// <summary>
    /// 原生界面不得再有直接写状态行标签的代码行：文字与入口显隐必须同一次更新。
    /// 接了宿主却还留着一条旁路写标签，就是「文字换了、按钮还留着」的半截状态。
    /// </summary>
    [Fact]
    public void NoSource_WritesTheStatusLabelDirectly()
    {
        var offenders = new List<string>();
        foreach (var source in Repo.NativeSources().Where(s => s.Name != "StatusLineHost.cs"))
        {
            var lines = CodeOnly(source.Text).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (Regex.IsMatch(lines[i], @"^\s*_status(Line)?\.(Text|ForeColor)\s*[!=]"))
                {
                    offenders.Add($"{source.Name}:{i + 1}");
                }
            }
        }
        Assert.True(offenders.Count == 0, $"这些行直接写状态行标签而不是走宿主 SetText：{string.Join("、", offenders)}。");
    }

    [Fact]
    public void Host_KeepsTheStatusLineHeight()
    {
        // 固定占位（DESIGN.md §6「状态行」）：宿主高度跟状态行走，不跟 Panel 缺省尺寸走。
        // 少了这一句，工具栏那一行会被撑到 100px（wine 实测），状态行从一行文字变成一条空带。
        var host = Repo.Source("Desktop", "Native", "StatusLineHost.cs");
        Assert.Contains("Height = label.Height;", host);
        Assert.Contains("AutoSize = false;", host);
    }

    [Fact]
    public void ButtonText_IsWrittenInOnePlaceOnly()
    {
        // 字面只在口径文件里出现一次：界面不就地拼写（拼一遍就有一处先漂）。
        // 只判代码行——注释里引用这颗按钮叫什么是在交代理由，不是第二份实现
        // （与 FontStackTests、LoadFailureStateTests 的 CodeOnly 同一口径）。
        foreach (var source in Repo.NativeSources().Where(s => s.Name != "StatusLineText.cs"))
        {
            var code = CodeOnly(source.Text);
            Assert.DoesNotContain("查看完整错误", code);
            Assert.DoesNotContain("完整错误信息", code);
        }
    }

    [Fact]
    public void Dialog_TakesItsCopyFromTheSharedSource()
    {
        var dialog = Repo.Source("Desktop", "Native", "StatusTextDialog.cs");
        Assert.Contains("StatusLineText.FullTextHeading", dialog);
        Assert.Contains("StatusLineText.FullTextNote", dialog);
        // 只读、可选中复制：错误原因里带文件全路径，折行会把路径断在任意位置。
        Assert.Contains("Ui.Preview(multiline: true", dialog);
        Assert.Contains("ScrollBars.Vertical", dialog);
        // 对话框不读磁盘、不重试、不写任何东西：再读一次仍由原处那颗「重试」负责。
        Assert.DoesNotContain("File.", dialog);
        Assert.DoesNotContain("Retry", dialog);
    }

    [Fact]
    public void SharedSource_IsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\StatusLineText.cs", csproj);
    }

    [Fact]
    public void DesignDoc_StatesTheStatusLineClause()
    {
        Assert.Contains("状态行", Repo.DesignSection(6));
        Assert.Contains("固定占位", Repo.DesignSection(6));
    }

    /// <summary>
    /// 按顶格类型声明把一份源码切段：每段是「类型名 + 从本声明起到下一个顶格声明止的正文」。
    /// 顶格 = 命名空间下的类型，缩进的成员声明不会被当成边界（与 <see cref="PageClassText"/> 同一取向），
    /// 这样「一个文件里既有页面又有对话框」「一个文件里有两个对话框」都能各自判到自己的那一段。
    /// </summary>
    private static IEnumerable<(string Name, string Body)> ClassSegments(string code)
    {
        // 只认顶格声明（行首不许缩进）：与 PageClassText 同一取向，嵌套类型不会被当成边界。
        var starts = Regex.Matches(code, @"^(?:internal|public|private)\w*\s+(?:\w+\s+)*class\s+(\w+)", RegexOptions.Multiline);
        for (var i = 0; i < starts.Count; i++)
        {
            var next = i + 1 < starts.Count ? starts[i + 1].Index : code.Length;
            yield return (starts[i].Groups[1].Value, code[starts[i].Index..next]);
        }
    }

    /// <summary>
    /// 页面类那一段源码：从它的顶格类声明起，到同文件里下一个顶格类声明止
    /// （顶格 = 命名空间下的类型，文件里的嵌套类型不会被当成边界），再去掉注释行。
    /// </summary>
    private static string PageClassText(string file, string className)
    {
        var source = Repo.Source("Desktop", "Native", file);
        var from = source.IndexOf("internal sealed class " + className + " ", StringComparison.Ordinal);
        Assert.True(from >= 0, $"{file} 里找不到 {className} 的类声明。");
        var next = source.IndexOf("\ninternal sealed class ", from + 1, StringComparison.Ordinal);
        return CodeOnly(next < 0 ? source[from..] : source[from..next]);
    }

    /// <summary>只留代码行（注释口径全仓一份，含 <c>//</c> 与 <c>/* … */</c>）：注释里引用写法是说明理由，不是第二份实现。</summary>
    private static string CodeOnly(string source) => CodeScan.Text(source);
}
