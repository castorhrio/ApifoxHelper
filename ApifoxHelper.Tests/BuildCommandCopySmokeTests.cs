using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 定向构建输出「复制命令」那一段真实控件交互的门（第二百零一轮）。
/// <para>
/// <b>为什么补这个文件</b>：<see cref="BuildCommandCopyTests"/> 的类注释从第二百轮起就写着
/// 「机械门在 <c>BuildCommandCopySmokeTests</c>」——<b>那个类当时并不存在</b>，是一处悬空引用。
/// 悬空的代价在第二百零一轮实测到了：冒烟段 <c>RunBuildCommandCopyClick</c> 里「尚未构建」那一段
/// 断言命令行是占位「命令：—」，而对话框的 <c>ShowHint</c> 把命令行清成空串、静态门
/// <c>Dialog_ClearsTheCommandAndHidesTheButtonInTheHintState</c> 又照着实现把空串钉住——
/// <b>静态门与冒烟段互相矛盾，Linux 侧 2639 条全绿，只有 wine 里真构造这一屏才红</b>
/// （216 通过 / 1 失败，失败行：对话框里找不到命令行）。
/// </para>
/// <para>
/// <b>本文件钉什么</b>：① 这一段真的接在冒烟器上（<c>nameof</c> 注册 + 真调用行）；
/// ② 空态与「记录里没有命令」两段真的按口径断言占位（掏空成「构造一下就算过」即红）；
/// ③ 真的点按钮、真的把剪贴板读回来逐字比对、真的点「关闭」；
/// ④ <b>对话框的空态与冒烟段的断言取同一个表达式</b>——两边各写一套正是上一轮那处矛盾的成因。
/// </para>
/// <para>
/// <b>它不跑 wine</b>（跑法是 <c>.winecheck/demo.sh</c>）：它只保证「这一段没被掏空、两边口径同源」，
/// 真跑出来的读数记在 <c>docs/NOTES.md</c>。模拟器读数不是真机读数，G-D5 仍 `[ ]`。
/// </para>
/// </summary>
public class BuildCommandCopySmokeTests
{
    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    private static string Dialog => Repo.Source("Desktop", "Native", "BuildLogDialog.cs");

    /// <summary>冒烟段本体：从方法签名切到下一个 <c>/// &lt;summary&gt;</c> 之前（紧随其后的是辅助方法）。</summary>
    private static string Section()
    {
        var program = SmokeProgram;
        var start = program.IndexOf("private static void RunBuildCommandCopyClick(", StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里没有 RunBuildCommandCopyClick——「复制命令」的真实控件交互段被删了。");
        var end = program.IndexOf("\n    /// <summary>", start, StringComparison.Ordinal);
        Assert.True(end > start, "RunBuildCommandCopyClick 的方法体切不出边界。");
        return program[start..end];
    }

    /// <summary>
    /// 段注册与调用点：标题带 <c>nameof</c>（段改名即编译不过，演示脚本按段名对账），
    /// 且调用点是一行真代码——注释掉的调用在报告里不会出现这一段，演示脚本会判「对不上」。
    /// </summary>
    [Fact]
    public void Smoke_RunsTheBuildCommandCopySection()
    {
        Assert.Contains(
            "Section(\"定向构建输出的「复制命令」（按钮在 → 真点 → 剪贴板逐字核对 → 不跳位 → 真点关闭，真实控件）\", nameof(RunBuildCommandCopyClick))",
            SmokeProgram);
        Assert.True(CodeScan.HasLine(SmokeProgram, "RunBuildCommandCopyClick(services, dataRoot);"),
            "冒烟器没有真的调用 RunBuildCommandCopyClick（注释掉的调用不算接线）。");
    }

    /// <summary>
    /// 空态与「记录里没有命令」两段都按口径断言占位：这两段是「没有命令就不给按钮」的唯一运行时证据，
    /// 掏空成「构造一下就算过」之后，<c>ShowHint</c> 把命令行清成空串这类缺陷就再没人拦得住
    /// （第二百零一轮实测：正是这两段里的第一段红的）。
    /// </summary>
    [Fact]
    public void Section_AssertsThePlaceholderInBothNoCommandStates()
    {
        var section = Section();
        Assert.Contains("RunAction(\"构建输出：尚未构建时不出现「复制命令」按钮（真实控件）\"", section);
        Assert.Contains("RunAction(\"构建输出：记录里没有命令时不出现「复制命令」按钮（真实控件）\"", section);
        // 两段都按口径常量断言，不就地写「命令：—」字面量（另写一份必然漂移）。
        Assert.Equal(2, CountOf(section, "line.Text != BuildCommandCopy.Line(null)"));
        Assert.DoesNotContain("\"命令：—\"", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// 真的点按钮、真的把剪贴板读回来逐字比对、真的点「关闭」——这三件事是「真实控件交互」
    /// 与「构造冒烟」的分界。少一件，这一段就退回成「构造一下就算过」。
    /// </summary>
    [Fact]
    public void Section_ReallyClicksAndReadsTheClipboardBack()
    {
        var section = Section();
        Assert.Contains("copyButton.PerformClick();", section);
        Assert.Contains("Clipboard.GetText()", section);
        Assert.Contains("if (pasted != command)", section);
        Assert.Contains("closeButton.PerformClick();", section);
        // 按钮宽度与旁边「关闭」的位置都要量：四个字换四个字，宽度本该不变（防按钮跳位）。
        Assert.Contains("if (copyButton.Width != widthBefore)", section);
        Assert.Contains("if (closeButton.Left != closeLeftBefore)", section);
    }

    /// <summary>
    /// <b>两边同源</b>：对话框的空态与冒烟段的断言取同一个表达式 <c>BuildCommandCopy.Line(null)</c>。
    /// <para>
    /// 第二百零一轮那处矛盾的成因就是两边各写一套：对话框写 <c>_command.Text = ""</c>、
    /// 静态门照着实现把空串钉住、冒烟段按口径断言占位。这条门要求「空态那一行由口径算出来」，
    /// 并且<b>不许再出现清成空串的写法</b>——把 <c>ShowHint</c> 改回空串即红，不必等 wine。
    /// </para>
    /// </summary>
    [Fact]
    public void HintState_TakesThePlaceholderFromTheSharedCopy()
    {
        var hint = MethodBody(Dialog, "private void ShowHint(");
        Assert.Contains("_command.Text = BuildCommandCopy.Line(null);", hint);
        Assert.DoesNotContain("_command.Text = \"\";", hint);
        // 文字变了高度要跟着重量：宽度没变时 Layout 不会触发（与 ShowRecord 同一处算式）。
        Assert.Contains("_command.Height = Ui.MonoBoxHeight(_command.Text, Math.Max(_command.Width, 1));", hint);
        // 按钮仍然收掉：没有命令就不给按钮（判定在 BuildCommandCopy.Target）。
        Assert.Contains("_copyButton.Visible = false;", hint);
        // 冒烟段断言的也是同一个表达式（两边同源，不各写一套）。
        Assert.Contains("line.Text != BuildCommandCopy.Line(null)", Section());
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
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
