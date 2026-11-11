using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 提示对话框「复制路径」断言（DESIGN.md §6「提示对话框」复制条款、§8 本轮条款）。
/// 提示对话框的等宽行装的是路径——「关于」里的资料目录、打开目录失败时那句「手动打开上面的路径」——
/// 而那一行是 <c>Label</c>：不可选中、不可复制，说明行指着的下一步在界面上没有落点。
/// 列表那一侧的文字出口在第五十二轮由 Ctrl+C 补上、状态行那一侧由「查看完整错误」补上，
/// 提示对话框这一侧是同一族里剩下的最后一处。
/// 口径单一落点在 <see cref="NoticeCopy"/>（不依赖 WinForms，与测试工程共享编译），
/// 剪贴板写入收在 <c>Ui.CopyToClipboard</c> 一处（整个原生界面唯一的写入点）。
/// 按钮的实际观感、Tab 可达与复制后粘贴出来的内容随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class NoticeCopyTests
{
    // ---------- 口径真值表 ----------

    [Theory]
    [InlineData(@"C:\Users\dev\AppData\Roaming\ApifoxHelper")]
    [InlineData("/home/dev/.local/share/ApifoxHelper")]
    [InlineData(@"D:\资料\Apifox 助手")]
    public void Target_ReturnsThePathVerbatim(string path)
    {
        // 粘出去的就是屏幕上那一行：不裁剪、不改写、不补引号。
        Assert.Equal(path, NoticeCopy.Target(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Target_IsNull_WhenThereIsNothingToCopy(string? copyText)
    {
        // 没有可交出去的文字就不给按钮：一颗点了什么也不会发生的按钮不该出现
        // （与 StatusLineText「放得下就不给查看完整错误」同一判据）。
        Assert.Null(NoticeCopy.Target(copyText));
    }

    [Fact]
    public void Labels_AreAllFourCharacters_SoTheButtonDoesNotShift()
    {
        // 按钮是 AutoSize 的，文字一变宽度就变，右对齐行里「关闭」会跟着左右跳。
        // 三个标签取同一字数，宽度因此不变（§6「停止/危险：位置尺寸与常态一致，防按钮跳位」同一取舍）。
        Assert.Equal(4, NoticeCopy.Label.Length);
        Assert.Equal(4, NoticeCopy.CopiedLabel.Length);
        Assert.Equal(4, NoticeCopy.FailedLabel.Length);
        Assert.Equal(3, new HashSet<string> { NoticeCopy.Label, NoticeCopy.CopiedLabel, NoticeCopy.FailedLabel }.Count);
    }

    // ---------- 接线机械检查：口径存在但没装到对话框上等于没有 ----------

    [Fact]
    public void NoticeDialog_ShowsTheCopyButtonOnlyWhenThereIsSomethingToCopy()
    {
        var source = Repo.Source("Desktop", "Native", "NoticeDialog.cs");
        // 按钮在「有可复制文字」的分支里创建并加入按钮行——无条件加一颗按钮等于给每一屏都添一步空动作。
        Assert.Matches(@"(?m)^\s*if \(NoticeCopy\.Target\(copyText\) is string target\)", source);
        Assert.Matches(@"(?m)^\s*var copyButton = Ui\.Ghost\(NoticeCopy\.Label\);", source);
        Assert.Matches(@"(?m)^\s*buttonRow\.Controls\.Add\(copyButton\);", source);
        // 文字不就地拼写：三个标签都取口径常量。
        Assert.DoesNotContain("\"复制路径\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"复制成功\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"复制失败\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticeDialog_ReportsWhatActuallyHappened()
    {
        var source = Repo.Source("Desktop", "Native", "NoticeDialog.cs");
        // 成败由 Ui.CopyToClipboard 的返回值决定：写进去了才说「复制成功」，没写进去说「复制失败」。
        Assert.Matches(
            @"(?m)^\s*copyButton\.Text = Ui\.CopyToClipboard\(target\) \? NoticeCopy\.CopiedLabel : NoticeCopy\.FailedLabel;",
            source);
        // 对话框自己不碰剪贴板、不自己 try/catch 一遍（写入点唯一）。
        Assert.DoesNotContain("Clipboard.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ExternalException)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BothNoticeCallSites_PassThePathTheyShow()
    {
        var source = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 「关于」：等宽行是「资料目录：<路径>」，复制的是路径本身而不是带标签的整行。
        Assert.Matches(@"(?m)^\s*mono: NoticeText\.AboutDataDirLine\(_host\.Store\.Root\),", source);
        Assert.Matches(@"(?m)^\s*copyText: _host\.Store\.Root\);", source);
        // 打开目录失败：说明行指着「手动打开上面的路径」，复制的是同一个 path。
        Assert.Matches(@"(?m)^\s*mono: path,", source);
        Assert.Matches(@"(?m)^\s*copyText: path\);", source);
    }

    [Fact]
    public void Ui_CopyToClipboard_IsTheSingleWritePoint()
    {
        var ui = Repo.Source("Desktop", "Native", "Ui.cs");
        var from = ui.IndexOf("public static bool CopyToClipboard(string text)", StringComparison.Ordinal);
        Assert.True(from >= 0, "源码缺少 Ui.CopyToClipboard。");
        var to = ui.IndexOf("public static ListView List(", StringComparison.Ordinal);
        Assert.True(to > from, "Ui.CopyToClipboard 之后应紧跟列表工厂。");
        var slice = ui[from..to];
        // 写入与异常处理都在这一处：写进去了返回 true，被占用返回 false（不重试、不弹窗）。
        Assert.Matches(@"(?m)^\s*Clipboard\.SetText\(text\);", slice);
        Assert.Matches(@"(?m)^\s*return true;", slice);
        Assert.Matches(@"(?m)^\s*catch \(ExternalException\)", slice);
        Assert.Matches(@"(?m)^\s*return false;", slice);
        // 整个原生界面只有这一行写剪贴板（列表工厂与提示对话框都经这里）。
        Assert.Equal(1, Regex.Matches(ui, @"(?m)^[ \t]*Clipboard\.").Count);
    }

    [Fact]
    public void SharedCompile_IncludesNoticeCopy()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\NoticeCopy.cs", csproj);
    }

    [Fact]
    public void DesignDoc_RecordsNoticeCopy()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("NoticeCopy", section);
        Assert.Contains("复制路径", section);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("提示对话框复制路径", ledger);
        Assert.Contains("NoticeCopy", ledger);
    }
}
