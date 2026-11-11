using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 提示对话框「复制路径」那一条交互链的机械门（第一百八十五轮）。
/// <para>
/// <b>为什么要有这一门</b>：第一百八十三轮把「复制路径」按钮做出来时，判的是<b>口径与接线</b>
/// （三个标签同字数、给了可复制的文字才出现、写入点唯一在 <c>Ui.CopyToClipboard</c>），
/// 按钮本身<b>从来没有被点过</b>——冒烟构造清单里那一屏根本没给 <c>copyText</c>，按钮压根没被构造出来；
/// 「复制后粘贴出来的到底是不是屏幕上那一行」当场没有 Windows 图形子系统量不了，
/// 那一支只能挂在 G-D5 真机复核上（口径写在 <see cref="NoticeCopyTests"/> 的类注释里）。
/// 第一百八十五轮 Linux 侧的模拟器又可用，这一支补上了真点、真读回剪贴板的实测。
/// </para>
/// <para>
/// <b>为什么还要一条 Linux 侧的门</b>：wine 侧的读数只在跑过的时候存在，删掉这一段
/// 冒烟照样绿、报告照样 PASS，而「复制出去的是不是屏幕上那一行」就再没人量过了——
/// 与本仓一贯的取舍同一族：<b>实测一次不够，得让这一段删不掉</b>。删段的形态与删断言一样难发现。
/// </para>
/// <para>
/// 判四件事：① 段在冒烟器里且在 <c>Main</c> 里注册（没注册 = 永远不跑）；② 走的是产品的点击链路
/// （<c>PerformClick</c>）而不是直接调事件处理器；③ <b>真的把剪贴板读回来</b>逐字比对——
/// 只判按钮文字的话，「按钮说复制成功但剪贴板里是空的」照样放过；④ 写剪贴板的那一点仍在产品里
/// （冒烟器只许读，不许写），否则就多出第二个写入点。
/// </para>
/// </summary>
public class NoticeCopySmokeTests
{
    private static string SmokeProgram => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

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

    [Fact]
    public void TheCopyButtonIsActuallyClicked_AndTheClipboardIsReadBack()
    {
        var body = MethodBody(SmokeProgram, "private static void RunNoticeCopyClick(");

        // ① 走产品的点击链路：PerformClick 会触发按钮的 Click 事件处理器，
        // 直接调处理器等于绕过「用户按下去」这一步（那正是要量的东西）。
        Assert.Contains("copyButton.PerformClick()", body);
        Assert.Contains("closeButton.PerformClick()", body);

        // ② 真的把剪贴板读回来逐字比对——这是这一段存在的全部理由。
        // 只判按钮文字的话，「按钮说复制成功、剪贴板里是空的」照样放过。
        Assert.Contains("Clipboard.GetText()", body);
        Assert.Contains("pasted != path", body);

        // ③ 跳位也要量：按钮宽度与旁边「关闭」的位置在点之前先记下来，点之后不许变
        // （三个标签同字数是这条纪律的前提，见 NoticeCopyTests）。
        Assert.Contains("var widthBefore = copyButton.Width;", body);
        Assert.Contains("copyButton.Width != widthBefore", body);
        Assert.Contains("closeButton.Left != closeLeftBefore", body);

        // ④ 段数不许掉下去（每一段都是一个独立的判定，少一段就是少量到一件事）。
        // 地板 8 段 = 第一百八十五轮的 6 段 + 第一百九十二轮的「等宽行量宽 == 实际可用宽」1 段
        // + 同轮的「等宽行 Enter 契约」1 段。
        var segments = Regex.Matches(body, @"RunAction\(").Count;
        Assert.True(segments >= 8, $"「复制路径」这一段只有 {segments} 个 RunAction 段（地板 8 段）。");

        // ⑤ 段在 Main 里注册：没注册 = 永远不跑，报告照样 PASS。
        var main = MethodBody(SmokeProgram, "private static int Main(");
        Assert.Contains("RunNoticeCopyClick(dataRoot)", main);
    }

    /// <summary>
    /// 等宽行的「量宽度」与「实际可用宽度」在运行时相等（第一百九十二轮，简报 P0-2）。
    /// <para>
    /// <b>为什么这一条必须落在冒烟里</b>：静态门（<see cref="NoticeCopyTests.NoticeDialog_MeasuresTheMonoLineWithTheGridsActualAvailableWidth"/>）
    /// 只能判「算式收在一处」，判不了<b>布局完成后那个等式成不成立</b>——构造那一刻内容网格还没有客户区，
    /// 宽度是布局给的，只有真跑一次 WinForms 才知道量宽与控件实际拿到的宽度是不是同一个数。
    /// 这一段删掉，报告照样 PASS，而「长路径会不会被裁掉半行」就再没人量过。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMonoLineIsMeasuredWithTheWidthItActuallyGets()
    {
        var body = MethodBody(SmokeProgram, "private static void RunNoticeCopyClick(");

        // ① 段在，且判的是「实际宽 == 可用宽」这个等式（不是只判非零）。
        Assert.Contains("等宽行的量宽就是它实际拿到的宽度", body);
        Assert.Contains("var available = Ui.ContentWidth(grid);", body);
        Assert.Contains("box.ClientSize.Width != available", body);
        // ② 高度必须按同一个宽度重量（只改宽不重量 = 长路径被裁掉半行）。
        Assert.Contains("Ui.MonoBoxHeight(box.Text, available)", body);
        Assert.Contains("box.Height != expected", body);
        // ③ 越界也要判：宽度相等但被网格裁掉，用户看到的仍是半行。
        Assert.Contains("box.Right > right", body);
    }

    /// <summary>
    /// 等宽行的 Enter 契约在运行时成立（第一百九十二轮，简报 P1-3）。
    /// <para>
    /// <b>为什么这一条必须落在冒烟里</b>：静态门（<see cref="NoticeCopyTests.Ui_MonoBox_IsReadOnlySelectableChrome"/>）
    /// 只能判「源码里写了 <c>AcceptsReturn = false</c>」，判不了<b>这个框真的不吃 Enter</b>、
    /// 也判不了<b>窗体的 <c>AcceptButton</c> 真的是那颗「关闭」</b>——后者是 Enter 的落点，
    /// 落点错了（比如被改成「复制路径」），用户按 Enter 会去复制而不是关屏。
    /// 这一段删掉，报告照样 PASS，而「Enter 到底落到哪」就再没人量过。
    /// </para>
    /// <para>
    /// <b>不判什么</b>：真的按一下 Enter 键没有模拟（本环境没有 Windows 图形子系统，键盘注入在 wine 里
    /// 不可靠），「按 Enter 真的关掉这一屏」仍随 G-D5 真机复核——冒烟报告里逐档写明这一句，不冒充量到。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMonoLineDoesNotEatEnter_AndTheAcceptButtonIsTheCloseButton()
    {
        var body = MethodBody(SmokeProgram, "private static void RunNoticeCopyClick(");

        // ① 段在，且判的是「不吃 Enter」这个方向（不是只判控件存在）。
        Assert.Contains("等宽行的 Enter 契约", body);
        Assert.Contains("if (box.AcceptsReturn)", body);
        // ② 只读与多行一起判：只读行没有换行可做，多行才折得了长路径。
        Assert.Contains("if (!box.ReadOnly)", body);
        Assert.Contains("if (!box.Multiline)", body);
        // ③ Enter 的落点必须是那颗「关闭」——落点错了，Enter 会去干别的。
        Assert.Contains("dialog.AcceptButton is not Button accept", body);
        Assert.Contains("!ReferenceEquals(accept, closeButton)", body);
        // ④ 没模拟按键这件事要如实写在报告里，不许让读者以为「按 Enter 量过了」。
        Assert.Contains("真的按 Enter 未模拟", body);
    }

    [Fact]
    public void TheSmokeOnlyReadsTheClipboard_NeverWritesIt()
    {
        // 整个原生界面只有一个剪贴板写入点（Ui.CopyToClipboard，由 NoticeCopyTests 判住）。
        // 冒烟器若自己写剪贴板，就多出第二个写入点——写成功与失败的口径会分叉。
        Assert.DoesNotContain("Clipboard.SetText", SmokeProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.Clear", SmokeProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopyButtonIsOnlyBuiltWhenThereIsSomethingToCopy()
    {
        // 这一段量的是「给了路径才有按钮」：冒烟里必须先量一遍**没给**路径的那一屏，
        // 否则「按钮永远在」这件事没人拦（产品侧那一半由 NoticeCopyTests 判住）。
        var body = MethodBody(SmokeProgram, "private static void RunNoticeCopyClick(");
        Assert.Contains("没给可复制的路径时不出现「复制路径」按钮", body);
        Assert.Contains("copyText: path", body);
    }

    [Fact]
    public void Notes_RecordTheCopyButtonMeasurement()
    {
        // 实测读数要落在文档里，否则「量到了」这句话无处可查。
        var notes = Repo.Source("docs", "NOTES.md");
        Assert.Contains("RunNoticeCopyClick", notes);
    }
}
