using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 定向构建输出对话框「命令」行的口径与接线断言（第二百轮）。
/// <para>
/// <b>为什么要有这个文件</b>：这一屏的说明行写着「完整输出可在本机终端重新执行查看」——
/// 指着的下一步是「把那条命令拿到终端里再跑一遍」。而命令行此前是 <c>Ui.Mono</c> 那个 <c>Label</c>：
/// 不可选中、不可复制，用户只能照着屏幕一个字一个字敲（命令里带 API 项目的完整路径，动辄几十个字符）。
/// 同一屏的输出尾部早就是可框选的只读框——<b>同一屏里两种行为</b>，按本仓一贯的判据判定为缺口。
/// </para>
/// <para>
/// 口径单一落点在 <see cref="BuildCommandCopy"/>（不依赖 WinForms，与测试工程共享编译），
/// 剪贴板写入收在 <c>Ui.CopyToClipboard</c> 一处（整个原生界面唯一的写入点）。
/// 按钮的实际观感、Tab 可达与用户在自己机器上粘贴时的手感随 G-D5 真机复核（Linux 无 WinForms GUI 运行时）；
/// 「点下去剪贴板里到底是不是那条命令」这一支在 wine 模拟器里真量（<c>RunBuildCommandCopyClick</c>，
/// 机械门在 <see cref="BuildCommandCopySmokeTests"/>）。
/// </para>
/// </summary>
public class BuildCommandCopyTests
{
    // ---------- 口径真值表 ----------

    [Theory]
    [InlineData(@"dotnet build Probe.Api/Probe.Api.csproj --no-restore -nologo -v:minimal")]
    [InlineData(@"dotnet build D:\资料\接口\Probe.Api\Probe.Api.csproj --no-restore -nologo -v:minimal")]
    public void Line_ShowsTheLabelThenTheCommandVerbatim(string command)
    {
        // 屏幕上那一行 = 标签 + 命令原文：不裁剪、不改写（命令要能照着敲）。
        Assert.Equal(BuildCommandCopy.LinePrefix + command, BuildCommandCopy.Line(command));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Line_FallsBackToThePlaceholder_WhenThereIsNoCommand(string? command)
    {
        // 空白既不是信息也不是缺省：用户看不出是「没跑过」还是「这一格坏了」。
        Assert.Equal(BuildCommandCopy.LinePrefix + BuildCommandCopy.Missing, BuildCommandCopy.Line(command));
    }

    [Theory]
    [InlineData(@"dotnet build Probe.Api/Probe.Api.csproj --no-restore -nologo -v:minimal")]
    [InlineData(@"dotnet build D:\资料\接口\Probe.Api\Probe.Api.csproj --no-restore -nologo -v:minimal")]
    public void Target_ReturnsTheCommandVerbatim_WithoutTheLabel(string command)
    {
        // 粘进终端的是标签后面那一段：带上「命令：」粘过去，终端会把它当成命令名。
        Assert.Equal(command, BuildCommandCopy.Target(command));
        Assert.DoesNotContain(BuildCommandCopy.LinePrefix, BuildCommandCopy.Target(command)!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Target_IsNull_WhenThereIsNothingToCopy(string? command)
    {
        // 没有可交出去的命令就不给按钮：一颗点了什么也不会发生的按钮不该出现
        // （与 NoticeCopy.Target 同一判据）。
        Assert.Null(BuildCommandCopy.Target(command));
    }

    [Fact]
    public void Label_IsFourCharacters_SoTheButtonDoesNotShift()
    {
        // 按钮是 AutoSize 的，文字一变宽度就变，右对齐行里「关闭」会跟着左右跳。
        // 「复制命令」与「复制成功」「复制失败」「关闭」同字数，宽度因此不变
        // （§6「停止/危险：位置尺寸与常态一致，防按钮跳位」同一取舍）。
        Assert.Equal(4, BuildCommandCopy.Label.Length);
        Assert.Equal(4, NoticeCopy.CopiedLabel.Length);
        Assert.Equal(4, NoticeCopy.FailedLabel.Length);
    }

    [Fact]
    public void Note_NamesTheButtonItPointsAt()
    {
        // 说明行指着的动作必须在界面上真有落点，且落点要能被用户认出来：
        // 说明行里点名的按钮文字必须就是那颗按钮上的文字（与 NoticeCopy 对「手动打开上面的路径」同一纪律）。
        Assert.Contains("「" + BuildCommandCopy.Label + "」", BuildCommandCopy.Note, StringComparison.Ordinal);
        // 说明行不再指着「照着屏幕敲」那条老路：老句子只说「可在本机终端重新执行查看」，
        // 界面上没有落点；新句子必须点名那颗按钮。
        Assert.DoesNotContain("完整输出可在本机终端重新执行查看", BuildCommandCopy.Note, StringComparison.Ordinal);
    }

    // ---------- 接线机械检查：口径存在但没装到对话框上等于没有 ----------

    [Fact]
    public void Dialog_ShowsTheCommandInASelectableMonoBox_NotALabel()
    {
        var source = Repo.Source("Desktop", "Native", "BuildLogDialog.cs");
        // 命令行经 Ui.MonoBox（只读文本框）构造：Label 一个字都选不中，
        // 而说明行指着「粘到本机终端即可重新执行」——用户要能把命令挑一段贴出去。
        Assert.Matches(@"(?m)^\s*_command = Ui\.MonoBox\(BuildCommandCopy\.Line\(null\), wrapWidth\);", source);
        Assert.Matches(@"(?m)^\s*head\.Controls\.Add\(_command\);", source);
        // 文字不就地拼写：标签与占位都取口径常量。
        Assert.DoesNotContain("\"命令：\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"复制命令\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"复制成功\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"复制失败\"", source, StringComparison.Ordinal);
        // 说明行取共享常量（长度与句数由 CaptionBrevityTests 判）。
        Assert.Matches(@"(?m)^\s*head\.Controls\.Add\(Ui\.Caption\(BuildCommandCopy\.Note\)\);", source);
    }

    [Fact]
    public void Dialog_ShowsTheCopyButtonOnlyWhenThereIsACommand()
    {
        var source = Repo.Source("Desktop", "Native", "BuildLogDialog.cs");
        // 按钮先建出来但默认隐藏，可见性由口径判定——无条件显示等于给每一屏都添一步空动作。
        Assert.Matches(@"(?m)^\s*_copyButton = Ui\.Ghost\(BuildCommandCopy\.Label\);", source);
        Assert.Matches(@"(?m)^\s*_copyButton\.Visible = false;", source);
        Assert.Matches(@"(?m)^\s*_copyButton\.Visible = BuildCommandCopy\.Target\(_commandText\) is not null;", source);
        Assert.Matches(@"(?m)^\s*buttonRow\.Controls\.Add\(_copyButton\);", source);
    }

    [Fact]
    public void Dialog_ReportsWhatActuallyHappened()
    {
        var source = Repo.Source("Desktop", "Native", "BuildLogDialog.cs");
        // 成败由 Ui.CopyToClipboard 的返回值决定：写进去了才说「复制成功」，没写进去说「复制失败」。
        Assert.Matches(
            @"(?m)^\s*_copyButton\.Text = Ui\.CopyToClipboard\(target\) \? NoticeCopy\.CopiedLabel : NoticeCopy\.FailedLabel;",
            source);
        // 交出去的是命令本身（Target），不是屏幕上那一行（Line）。
        Assert.Matches(@"(?m)^\s*if \(BuildCommandCopy\.Target\(_commandText\) is not string target\) return;", source);
        // 对话框自己不碰剪贴板、不自己 try/catch 一遍（写入点唯一）。
        Assert.DoesNotContain("Clipboard.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (ExternalException)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_MeasuresTheMonoLineWithTheGridsActualAvailableWidth()
    {
        var source = Repo.Source("Desktop", "Native", "BuildLogDialog.cs");
        // 折行宽与量宽取同一处算式（Ui.ContentWidth）：量宽偏大则长命令被裁掉半行，偏小则多出一块空白。
        Assert.Matches(@"(?m)^\s*var wrapWidth = Ui\.ContentWidth\(head\);", source);
        Assert.Matches(@"(?m)^\s*var width = Ui\.ContentWidth\(head\);", source);
        // 宽度变了高度必须跟着重量（只改宽不重量 = 长命令被裁掉半行）。
        Assert.Matches(@"(?m)^\s*_command\.Height = Ui\.MonoBoxHeight\(_command\.Text, width\);", source);
        // 记录读进来之后按当前宽度重量一次（宽度没变时 Layout 不会触发）。
        Assert.Matches(@"(?m)^\s*_command\.Height = Ui\.MonoBoxHeight\(_command\.Text, Math\.Max\(_command\.Width, 1\)\);", source);
    }

    [Fact]
    public void Dialog_ResetsTheCommandToThePlaceholderAndHidesTheButtonInTheHintState()
    {
        var source = Repo.Source("Desktop", "Native", "BuildLogDialog.cs");
        // 空态 / 读取失败态：命令回到占位、按钮收掉——留着上一次的命令等于把「没跑过」说成「跑过」。
        // 第二百零一轮修：这里原先钉的是 `_command.Text = ""`（照着实现写的），与冒烟段
        // 「尚未构建时命令行是占位」互相矛盾，Linux 侧全绿而 wine 里真构造这一屏才红。
        // 空值该显示什么由 BuildCommandCopy.Line 一处决定，对话框不另写一套。
        var hint = MethodBody(source, "private void ShowHint(");
        Assert.Contains("_commandText = null;", hint);
        Assert.Contains("_command.Text = BuildCommandCopy.Line(null);", hint);
        Assert.DoesNotContain("_command.Text = \"\";", hint);
        Assert.Contains("_copyButton.Visible = false;", hint);
    }

    /// <summary>方法体（按大括号配平取，与本仓其它门同一套取法）。</summary>
    private static string MethodBody(string source, string signature)
    {
        var from = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(from >= 0, $"源码缺少 {signature}。");
        var open = source.IndexOf('{', from);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        Assert.Fail($"{signature} 的方法体没有闭合。");
        return "";
    }
}
