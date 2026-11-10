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
        // （ProfileDialog / NewTaskDialog），它们的状态行本轮没有接宿主、仍是悬停提示
        // （挂账在 docs/NOTES.md）。拿整份文件判「不得直接写标签」等于把没做的范围算成已做。
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

    /// <summary>只留代码行（去掉 <c>//</c> 与 <c>///</c> 注释行）：注释里引用写法是说明理由，不是第二份实现。</summary>
    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
}
