using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 完整错误对话框「复制全文」的口径与接线（第一百九十七轮）。
/// <para>
/// <b>为什么要有这一门</b>：这一屏此前只有「框选复制」一条路——说明行写着「可以选中上面的文字复制」，
/// 而错误原因动辄两三百字符、带文件全路径，用户要做的是把<b>整段</b>贴进工单，那要「点进框 → Ctrl+A →
/// Ctrl+C」三步。补一颗「复制全文」按钮之后，判据必须跟着落下来，否则下一轮把它删掉、把说明行改回
/// 「可以选中复制」，界面照样能跑、报告照样 PASS，而「一次点击交出整段」就再没人量过。
/// </para>
/// <para>
/// 判四件事：① 按钮文字只在口径文件里出现一次（界面不就地拼写）；② 对话框真的把按钮加进按钮行、
/// 真的经 <c>Ui.CopyToClipboard</c> 写剪贴板（全仓唯一写入点）；③ 结果标签取 <see cref="NoticeCopy"/>
/// 那一份，不另写一套；④ 三个标签同字数——按钮是 <c>AutoSize</c> 的，字数一变旁边那颗「关闭」就跳位。
/// </para>
/// <para>
/// <b>「只在口径文件里出现一次」判的是代码，不是注释</b>（第一百九十七轮收口时踩到）：第一版直接扫全文，
/// 于是 <c>NoticeCopy.cs</c> 的类注释里那句「完整错误对话框的『复制全文』取的就是这两句」被判成违规——
/// 注释里点名一颗按钮是<b>该有的</b>（读者要能顺着注释找到实物），就地拼写指的是<b>代码里再写一遍字面量</b>。
/// 故这一条扫的是剥掉注释后的代码（<see cref="NativeSource.Code"/>）。
/// </para>
/// <para>
/// <b>不判什么</b>：真的点一下、剪贴板里读回来的是不是那段文字，要 WinForms 真跑——本环境没有
/// Windows 图形子系统，这一支随 G-D5 真机复核（与「复制路径」那一族同一挂账口径）。
/// </para>
/// </summary>
public class StatusTextCopyTests
{
    private static string Dialog => Repo.Source("Desktop", "Native", "StatusTextDialog.cs");

    [Fact]
    public void CopyFullTextButton_IsAVerbPhraseAndSaysWhatItHandsOver()
    {
        Assert.Equal("复制全文", StatusLineText.CopyFullTextButton);
        // 说「交出什么」，不说机制（不写「复制到剪贴板」这类实现词）。
        Assert.DoesNotContain("剪贴板", StatusLineText.CopyFullTextButton);
    }

    [Fact]
    public void CopyFullTextButton_KeepsTheSameCharacterCountAsTheResultLabels()
    {
        // 三个标签同字数：按钮 AutoSize，文字一变宽度就变，右对齐行里「关闭」会跟着左右跳
        // （与 NoticeCopy 的 4/4/4 同一取舍）。
        Assert.Equal(StatusLineText.CopyFullTextButton.Length, NoticeCopy.CopiedLabel.Length);
        Assert.Equal(StatusLineText.CopyFullTextButton.Length, NoticeCopy.FailedLabel.Length);
    }

    [Fact]
    public void Dialog_AddsTheButtonAndWritesTheClipboardThroughTheSinglePoint()
    {
        var dialog = Dialog;
        // 按钮文字取口径文件那一份，不就地拼写。
        Assert.Contains("Ui.Ghost(StatusLineText.CopyFullTextButton)", dialog);
        // 写剪贴板只经 Ui.CopyToClipboard（整个原生界面唯一的写入点）。
        Assert.Contains("Ui.CopyToClipboard(_text.Text)", dialog);
        // 结果标签取 NoticeCopy 那一份，不另写一套。
        Assert.Contains("NoticeCopy.CopiedLabel", dialog);
        Assert.Contains("NoticeCopy.FailedLabel", dialog);
        // 按钮进的是按钮行（与「关闭」同一行），不是另起一行。
        Assert.Contains("buttonRow.Controls.Add(copyButton)", dialog);
    }

    [Fact]
    public void Note_PointsAtTheButtonThatActuallyExists()
    {
        // 说明行指着的动作必须在界面上真有落点：点名「复制全文」，同时保留「选中其中一段」那条路。
        Assert.Contains(StatusLineText.CopyFullTextButton, StatusLineText.FullTextNote);
        Assert.Contains("选中", StatusLineText.FullTextNote);
        Assert.Contains("重试", StatusLineText.FullTextNote);
    }

    [Fact]
    public void ButtonText_IsWrittenInOnePlaceOnly()
    {
        // 字面只在口径文件里出现一次：界面不就地拼写（拼一遍就有一处先漂）。
        // 判代码不判注释——注释里点名按钮是读者顺着找实物的路，不是第二份字面量。
        foreach (var source in Repo.NativeSources().Where(s => s.Name != "StatusLineText.cs"))
        {
            Assert.DoesNotContain(StatusLineText.CopyFullTextButton, source.Code);
        }
    }

    [Fact]
    public void SharedSource_IsCompiledIntoTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\StatusLineText.cs", csproj);
        Assert.Contains(@"..\Desktop\Native\NoticeCopy.cs", csproj);
    }
}
