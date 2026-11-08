using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「提示对话框：关于与打开目录失败不再用系统 MessageBox」断言（DESIGN.md §1.1「禁止以框架默认观感
/// 直接交付」、§6「对话框」、§8 本轮条款）。
/// 缺口：全原生界面的信息视图都是令牌化窗体，只有「关于」与「打开目录失败」两处用系统 MessageBox
/// （只有一个「确定」、系统默认观感），属同壳不同行为；「关于」还把本地服务地址摆在用户面前——
/// 本地 HTTP 服务是过渡壳的实现能力（方案 §2.4），不是用户主产品，把地址摆出来等于把用户往浏览器引
/// （方案 1.4「不把用户推回浏览器」）。
/// 口径收在 <see cref="NoticeText"/>（不依赖 WinForms、与测试工程共享编译，下列真值表真执行）；
/// 呈现形状收在 <see cref="NoticeDialog"/>（WinForms 控件触达，本环境不可执行，以源码切片钉住）。
/// 确认类动作（执行同步、覆盖式写入、退出）仍用原生确认对话框（§6 例外条款），不属本切片。
/// 真机上的对话框观感随 G-D5 复核（Linux 无 WinForms GUI 运行时）。
/// </summary>
public class NoticeTextTests
{
    // ---------- 文字口径（真执行） ----------

    [Fact]
    public void AboutHeading_IsTheProductName()
    {
        Assert.Equal("Apifox 助手", NoticeText.AboutHeading);
    }

    [Fact]
    public void AboutBody_PutsVersionBeforeRuntime()
    {
        var body = NoticeText.AboutBody("1.2.3", ".NET 8.0.0");
        var lines = body.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("1.2.3", lines[0], StringComparison.Ordinal);
        Assert.Contains(".NET 8.0.0", lines[1], StringComparison.Ordinal);
        // 版本在前：报问题时先看的就是它。
        Assert.StartsWith("版本", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AboutDataDirLine_IsCaptionPlusPath()
    {
        Assert.Equal("资料目录：/tmp/apifox-helper", NoticeText.AboutDataDirLine("/tmp/apifox-helper"));
        Assert.Equal("资料目录", NoticeText.AboutDataDirCaption);
    }

    [Fact]
    public void AboutNote_PointsAtTheLogDirectoryInTheFileMenu()
    {
        // 说明行给出下一步：日志目录在「文件」菜单里（该菜单项真实存在，见 NativeShellForm）。
        Assert.Contains("日志目录", NoticeText.AboutNote, StringComparison.Ordinal);
        Assert.Contains("「文件」菜单", NoticeText.AboutNote, StringComparison.Ordinal);
    }

    [Fact]
    public void AboutText_DoesNotShowTheLocalServiceAddress()
    {
        // 本地 HTTP 服务是过渡壳的实现能力，不是用户主产品（方案 §2.4 / 1.4）。
        var all = NoticeText.AboutHeading + "\n" + NoticeText.AboutBody("1.0", ".NET 8")
            + "\n" + NoticeText.AboutDataDirLine("/data") + "\n" + NoticeText.AboutNote;
        Assert.DoesNotContain("http", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("本地服务", all, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", all, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FolderErrorHeading_SaysTheResultNotTheMechanism()
    {
        Assert.Equal("无法打开目录", NoticeText.FolderErrorHeading);
    }

    [Fact]
    public void FolderErrorNote_GivesTheNextStep()
    {
        // 错误给下一步（DESIGN.md §1.6）：重试入口在「文件」菜单，或手动打开上面的路径。
        Assert.Contains("「文件」菜单", NoticeText.FolderErrorNote, StringComparison.Ordinal);
        Assert.Contains("路径", NoticeText.FolderErrorNote, StringComparison.Ordinal);
    }

    // ---------- 呈现形状（源码切片） ----------

    [Fact]
    public void Dialog_UsesTheSameTokensAndShapeAsTheOtherInfoViews()
    {
        var source = Repo.Source("Desktop", "Native", "NoticeDialog.cs");
        // 窗体形状：固定对话框、面板底、Body 字体（与其余信息视图同一套令牌）。
        Assert.Contains("FormBorderStyle.FixedDialog", source, StringComparison.Ordinal);
        Assert.Contains("BackColor = UiTokens.Panel", source, StringComparison.Ordinal);
        Assert.Contains("Font = UiTokens.Body.Value", source, StringComparison.Ordinal);
        // 四档文字各经工厂：标题 Section、正文 Body、等宽行 Mono、说明行 Caption。
        Assert.Contains("Ui.Section(heading)", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Body(body)", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Mono(mono)", source, StringComparison.Ordinal);
        Assert.Contains("Ui.Caption(note)", source, StringComparison.Ordinal);
        // 长内容区经工厂（§6「长内容区」），不就地开 AutoScroll。
        Assert.Contains("Ui.ScrollHost()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoScroll = true", source, StringComparison.Ordinal);
        // 一个 ghost 按钮，Enter 与 Esc 都是关闭（说一件事、不需要用户做选择）。
        Assert.Contains("Ui.Ghost(\"关闭\")", source, StringComparison.Ordinal);
        Assert.Contains("AcceptButton = closeButton", source, StringComparison.Ordinal);
        Assert.Contains("CancelButton = closeButton", source, StringComparison.Ordinal);
        // 不退回系统弹窗（注释里点名 MessageBox 是在说明它被替掉了，按代码行判）。
        foreach (var line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("///", StringComparison.Ordinal)) continue;
            Assert.DoesNotContain("MessageBox", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Dialog_DoesNotSpellItsOwnText()
    {
        var source = Repo.Source("Desktop", "Native", "NoticeDialog.cs");
        // 文字取 NoticeText 一份：对话框不就地拼「关于」「无法打开目录」这些字。
        Assert.DoesNotContain("关于", source, StringComparison.Ordinal);
        Assert.DoesNotContain("无法打开目录", source, StringComparison.Ordinal);
        Assert.DoesNotContain("资料目录", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_TakesBothNoticesFromTheSharedText()
    {
        var source = Repo.Source("Desktop", "Native", "NativeShellForm.cs");
        // 打开目录失败：经令牌化提示对话框，文字取 NoticeText。
        Assert.Contains("new NoticeDialog(", source, StringComparison.Ordinal);
        Assert.Contains("NoticeText.FolderErrorHeading", source, StringComparison.Ordinal);
        Assert.Contains("NoticeText.FolderErrorNote", source, StringComparison.Ordinal);
        // 关于：同样经提示对话框，且不再显示本地服务地址。
        Assert.Contains("NoticeText.AboutHeading", source, StringComparison.Ordinal);
        Assert.Contains("NoticeText.AboutBody(", source, StringComparison.Ordinal);
        Assert.Contains("NoticeText.AboutDataDirLine(", source, StringComparison.Ordinal);
        Assert.Contains("NoticeText.AboutNote", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_host.Url", source, StringComparison.Ordinal);
        // 旧形状不得回来：这两处不再直接弹系统 MessageBox。
        Assert.DoesNotContain("MessageBoxButtons.OK,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeSources_KeepMessageBoxOnlyForConfirmations()
    {
        // DESIGN.md §6 例外条款：不可逆或破坏性动作保留原生确认对话框。
        // 信息类提示（只有一个「确定」）一律走令牌化窗体——全原生界面不再有框架默认观感。
        foreach (var source in Repo.NativeSources())
        {
            Assert.Empty(source.Matches(@"MessageBoxButtons\.OK\s*,"));
            foreach (Match match in source.Matches(@"MessageBox\.Show\("))
            {
                var rest = source.Text[(match.Index + match.Length)..];
                var next = rest.IndexOf("MessageBox.Show(", StringComparison.Ordinal);
                var tail = next < 0 ? rest : rest[..next];
                Assert.True(
                    tail.Contains("MessageBoxButtons.YesNo", StringComparison.Ordinal)
                    || tail.Contains("MessageBoxButtons.OKCancel", StringComparison.Ordinal),
                    $"{source.Name}：MessageBox.Show 不是确认对话框（缺 YesNo/OKCancel）。"
                    + "信息类提示经 NoticeDialog。");
            }
        }
    }

    [Fact]
    public void NoticeText_DoesNotDependOnWinForms()
    {
        var source = Repo.Source("Desktop", "Native", "NoticeText.cs");
        Assert.DoesNotContain("using System.Windows.Forms", source, StringComparison.Ordinal);
        Assert.DoesNotContain("using System.Drawing", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new Form", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Control", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Label", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoticeText_IsSharedWithTheTestProject()
    {
        var csproj = Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj");
        Assert.Contains(@"..\Desktop\Native\NoticeText.cs", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void DesignDoc_RecordsTheNoticeDialogClause()
    {
        var section = Repo.DesignSection(6);
        Assert.Contains("提示对话框", section, StringComparison.Ordinal);
        Assert.Contains("NoticeDialog", section, StringComparison.Ordinal);
        Assert.Contains("框架默认观感", section, StringComparison.Ordinal);
        var ledger = Repo.DesignSection(8);
        Assert.Contains("| 提示对话框（§6） |", ledger, StringComparison.Ordinal);
        Assert.Contains("第八十七轮", ledger, StringComparison.Ordinal);
    }
}
