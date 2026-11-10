using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 状态行的截断提示（第一百五十九轮，具名 pass：harden）。
/// <para>
/// 存在理由：状态行装的是错误原因，而原因来自 <c>Exception.Message</c>——.NET 的英文句子带文件全路径，
/// 动辄两三百字符，比状态行宽得多。此前 <c>Ui.StatusLine()</c> 没有开 <c>AutoEllipsis</c>，
/// 超出的部分被<b>静默裁掉</b>：用户看到的是半句话，既不知道「还有下文」，也读不到失败在哪。
/// 这个缺陷在 Linux 侧全绿（源码切片看不出「文字比控件宽」），是 wine 下
/// <c>RunDetailBreak</c> 在真实控件上量出来的（240 字符的 IOException 消息在 770px 的状态行上被切在中间）。
/// </para>
/// <para>
/// 断言分两层：口径（<c>Ui.cs</c> 的源码切片——它依赖 WinForms，Linux 侧不能共享编译）与覆盖面
/// （全原生界面的状态行都从这一个工厂出厂，改一处即全屏生效）。真实控件上的量测由冒烟器
/// <c>RunDetailBreak</c> 在 wine/真机下走。
/// </para>
/// </summary>
public class StatusLineEllipsisTests
{
    private static string Ui => Repo.Source("Desktop", "Native", "Ui.cs");

    /// <summary>
    /// 状态行开省略号：文字比控件宽时末尾出现「…」，且 WinForms 的 Label 在文字被裁时
    /// 自动把全文放进悬停提示——「有下文」与「看全文」两件事一起成立。
    /// </summary>
    [Fact]
    public void StatusLine_ShowsEllipsisWhenTheTextIsWiderThanTheControl()
    {
        var body = MethodBody(Ui, "public static Label StatusLine()");
        Assert.Contains("AutoEllipsis = true,", body, StringComparison.Ordinal);
        // 仍是固定占位：不换行、不推动布局（DESIGN §6「状态行」）。
        Assert.Contains("AutoSize = false,", body, StringComparison.Ordinal);
        Assert.Contains("Height = UiTokens.Body.Value.Height + UiTokens.Xs * 2,", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 状态行只有这一个工厂：全原生界面的状态行都从它出厂，改一处即全屏生效
    /// （若某屏自己 <c>new Label</c> 拼一条状态行，这一屏就绕过了省略号口径）。
    /// </summary>
    [Fact]
    public void StatusLines_AllComeFromTheSingleFactory()
    {
        var files = Directory.GetFiles(Path.Combine(Repo.Root, "Desktop", "Native"), "*.cs");
        var count = 0;
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            count += Count(source, "Ui.StatusLine()");
        }
        // 地板：第十七处起是第一百五十九轮之前的既有接线，掉下去说明有人整批删了状态行。
        Assert.True(count >= 15, $"全原生界面只有 {count} 处 Ui.StatusLine()（地板 15）。");
    }

    /// <summary>
    /// 省略号不是「换行」：状态行的高度固定，超长文字不得把它撑高（撑高会推动工具栏与详情区布局）。
    /// 这一条在源码层钉住「AutoSize 保持 false」——AutoSize 一开，AutoEllipsis 就失效、高度随文字走。
    /// </summary>
    [Fact]
    public void StatusLine_StaysAFixedHeightLine()
    {
        var body = MethodBody(Ui, "public static Label StatusLine()");
        Assert.DoesNotContain("AutoSize = true", body, StringComparison.Ordinal);
        // 高度是常量表达式，不随文字变化。
        Assert.Contains("Height = UiTokens.Body.Value.Height", body, StringComparison.Ordinal);
    }

    private static int Count(string text, string fragment)
    {
        var count = 0;
        for (var i = text.IndexOf(fragment, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(fragment, i + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    /// <summary>取一个方法的方法体（按花括号深度界定，不靠缩进猜）；找不到即红。</summary>
    private static string MethodBody(string source, string header)
    {
        var start = source.IndexOf(header, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Ui.cs 里找不到「{header}」。");
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
