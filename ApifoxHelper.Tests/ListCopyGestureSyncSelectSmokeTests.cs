using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 同步预览<b>勾选页</b>的接口清单的列表 Ctrl+C 复制在冒烟器里<b>真的被按过</b>
/// （第三百五十七轮，接续 <c>RunListCopyGesture</c> 一族）。
/// <para>
/// <b>补的是哪个缺口</b>：已按过的十二次 Ctrl+C 全在<b>普通清单</b>上（差异确认、检查结果、任务日志、
/// 代码证据、同步预览写入清单、补充说明、接口追踪明细、响应契约、注释候选、注释缺口、注释回写、
/// 生成文档核对的两张清单），<b>复选清单上一次都没按过</b>。这一屏的接口清单是复选清单
/// （<c>Ui.List(true)</c>）：它自己的 <c>KeyDown</c> 上接着三只手势——Ctrl+C（抄标识列）、空格（翻焦点行）、
/// Ctrl+A（全选可见行里可同步的那些），外加 <c>FilterGestures</c> 的 Ctrl+F。四只手势共用同一段接线，
/// 而此前没有任何一段把它们放到同一行上真的按过。
/// </para>
/// <para>
/// <b>这一屏为什么最该按</b>：这张清单的首列逐字就是「GET /路径」（<c>SyncSelectSort.EndpointCell</c>
/// = 方法 + 空格 + 路径），而 <c>ListCopy</c> 开头那段话点名的用途正是「新建任务的『指定接口』要求逐行
/// 手敲同样的文字」——抄出去的每一个字，用户都要逐行粘回去。这一屏是那张清单里唯一「首列即身份」的。
/// </para>
/// <para>
/// <b>这一份判什么</b>：① 冒烟段 <c>RunListCopyGestureSyncSelect</c> 真的经 <c>PressKey</c> 按了
/// <c>^c</c>（三次：抄一次、翻勾选之后再抄一次、未选中时再按一次）、<c>{DOWN}</c>（换行那一段按目标行数按）
/// 与裸空格（翻焦点行的勾选）——不是直接调事件处理器、也不是直接写剪贴板；② 剪贴板是<b>读回来</b>逐字比对的
/// （<c>ReadClipboard()</c>），不是断言「调过写入口」；③ 期望值取产品口径——标识列走表头「接口」那一格
/// （<c>ColumnIndex(list, "接口")</c>，这一屏走默认首列，不另写一份「应该抄第几列」），
/// 「这一行勾不勾得上」走 <c>SyncSelection.Selectable</c>；④ 那五件事各有一句能读的断言
/// （按段体里的话认，不数行数）；⑤ 这一段在 <see cref="MeasuredScreens.All"/> 里有登记、轮次可查。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判 wine 上按下去的结果对不对（那是冒烟器那一跑的事，读数在
/// <c>.winecheck/reading.txt</c> 与 <c>docs/NOTES.md</c> 第三百五十七轮那一节）；不判中文输入法是否吞掉
/// Ctrl+C、不判真机上剪贴板被别的程序占用时的行为；也不判「点勾选框那一格之后焦点行有没有跟着走」
/// （那是 Windows 上的行为，模拟器量不到）——Windows GUI 在本机不可验证，观感随 G-D5 复核。
/// </para>
/// </summary>
public class ListCopyGestureSyncSelectSmokeTests
{
    private const string Segment = "RunListCopyGestureSyncSelect";

    private static string Smoke => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>
    /// 那五件事各一句能读的话：这一张确实是复选清单（前提）/ 抄的是「接口」不是邻列「名称」 /
    /// 翻勾选不改写标识格 / 换一行跟着换 / 未选中不动作。
    /// </summary>
    private static readonly string[] RequiredSentences =
    [
        "勾选页的接口清单不是复选清单：这一段量的是「复选清单上按一次 Ctrl+C」，",
        "这一屏的标识列是首列「接口」，抄出去的必须是那一格。",
        "不是这一行说的是哪个接口（粘进「指定接口」的那一行得是 GET /路径）。",
        "翻勾选与抄标识列是同一段接线上的两件事，互不干扰。",
        "复制的是固定第一行，不是焦点行。",
        "只复制屏幕上真正标出来的那一行，未选中时不该动作（空复制会把用户先前复制的内容抹掉）。",
    ];

    /// <summary>
    /// 真的按过的键：Ctrl+C 四次（抄一次、翻勾选之后再抄一次、换一行再抄一次、未选中时再按一次——一次按键只有一个含义）、
    /// 方向键下至少一次（换行那一段按目标行数按）、裸空格一次（翻焦点行的勾选）。
    /// </summary>
    private static readonly string[] RequiredKeyPresses =
    [
        "PressKey(dialog, list, \"^c\");",
        "PressKey(dialog, list, \"{DOWN}\");",
        "PressKey(dialog, list, \" \");",
    ];

    [Fact]
    public void TheSmokeSegment_ReallyPressesCtrlCAndReadsTheClipboardBack()
    {
        var body = Body();

        Assert.Empty(Problems(body));

        // Ctrl+C 四次：抄一次、翻勾选之后再抄一次、换一行再抄一次、未选中时再按一次。少一次就少一件事的读数。
        var copies = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^c\");")).Count;
        Assert.True(copies >= 4,
            $"段体里只按了 {copies} 次 Ctrl+C：抄一次、翻勾选之后再抄一次、换一行再抄一次、未选中时再按一次，四次各量一件事。");
    }

    /// <summary>
    /// 这一段在「哪些屏真的显示过」那份登记里有认领，且轮次可查——否则下一轮读登记的人
    /// 不知道这一屏是哪一轮量到的（<c>MeasuredScreensTests</c> 判的是同一件事的另一半）。
    /// </summary>
    [Fact]
    public void TheSegment_IsRegisteredAsMeasured()
    {
        var row = MeasuredScreens.All.SingleOrDefault(screen => screen.Section == Segment);
        Assert.NotNull(row);
        Assert.True(row!.Measured, "这一段真的 Show() 过 SyncPreviewDialog，登记里必须标「已量」。");
        Assert.Equal("第三百五十七轮", row.Round);
        Assert.Contains("SyncPreviewDialog", row.Classes);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：五发注入各报一处，合规段体不报。
    /// <para>
    /// 本仓的教训是「判据自己没被跑过」比「没有判据」更贵（第二百零九轮那两处假变异、
    /// 第二百四十三轮 M4 那一发假绿）。这里喂的是<b>注入的夹具</b>，不是判据自己的定义。
    /// </para>
    /// </summary>
    [Fact]
    public void Detector_ReportsTheBreaksItMustCatch()
    {
        var good = Body();
        Assert.Empty(Problems(good));

        // ① 把「未选中不动作」那一段的 Ctrl+C 删掉：剪贴板被抹掉这件事没有读数了。
        var noUnselected = good.Replace(
            "PressKey(dialog, list, \"^c\");\n                var after = ReadClipboard();",
            "var after = ReadClipboard();",
            StringComparison.Ordinal);
        Assert.Contains(Problems(noUnselected), p => p.Contains("^c", StringComparison.Ordinal));

        // ② 把「抄的是标识列」那一句断言删掉：抄成邻列「名称」时这一段照样绿。
        var noIdentity = good.Replace(
            "这一屏的标识列是首列「接口」，抄出去的必须是那一格。",
            "（删掉）",
            StringComparison.Ordinal);
        Assert.Contains(Problems(noIdentity), p => p.Contains("接口", StringComparison.Ordinal));

        // ③ 把方向键那一下删掉：「复制跟着焦点走」这一条量不到。
        var noArrow = good.Replace("PressKey(dialog, list, \"{DOWN}\");", "", StringComparison.Ordinal);
        Assert.Contains(Problems(noArrow), p => p.Contains("{DOWN}", StringComparison.Ordinal));

        // ④ 把空格那一下删掉：「翻勾选不改写标识格」这一条量不到。
        var noSpace = good.Replace("PressKey(dialog, list, \" \");", "", StringComparison.Ordinal);
        Assert.Contains(Problems(noSpace), p => p.Contains("PressKey(dialog, list, \" \")", StringComparison.Ordinal));

        // ⑤ 剪贴板不再读回来（改成断言「调过写入口」）：逐字比对这件事没有读数了。
        var noRead = good.Replace("ReadClipboard()", "path", StringComparison.Ordinal);
        Assert.Contains(Problems(noRead), p => p.Contains("ReadClipboard", StringComparison.Ordinal));
    }

    // ---------- 判据本体（纯函数：段体进、问题出） ----------

    /// <summary>
    /// 段体进、问题出（空 = 过得去）。写成纯函数是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>MeasuredScreensTests.Problems</c> / <c>RoundLogTests.Problems</c> 同一形状。
    /// </summary>
    private static List<string> Problems(string body)
    {
        var problems = new List<string>();

        foreach (var press in RequiredKeyPresses)
        {
            if (!body.Contains(press, StringComparison.Ordinal))
            {
                problems.Add($"段体里没有 `{press}`：这个手势一次都没被真的按过，"
                             + "「按下去落在哪」这件事仍然只有源码形状在管。");
            }
        }

        // Ctrl+C 必须真的按过四次（抄一次、翻勾选之后再抄一次、换一行再抄一次、未选中时再按一次）：少一次就少一件事的读数。
        // 只判「这个字符串在不在」不够——段体里本来就有三处，删掉其中一处照样「在」，
        // 而「未选中时剪贴板不动」那件事就没有读数了（第三百五十一轮这一条自测抓到的正是它）。
        var copies = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^c\");")).Count;
        if (copies < 4)
        {
            problems.Add($"段体里只按了 {copies} 次 `PressKey(dialog, list, \"^c\");`：抄一次、翻勾选之后再抄一次、"
                         + "换一行再抄一次、未选中时再按一次，四次各量一件事——少一次那件事就没有读数。");
        }

        foreach (var sentence in RequiredSentences)
        {
            if (!body.Contains(sentence, StringComparison.Ordinal))
            {
                problems.Add($"段体里读不到「{sentence}」这一句：那件事没有读数，"
                             + "改坏了它这一段照样绿。");
            }
        }

        // 剪贴板必须读回来逐字比对：断言「调过写入口」量不到抄出去的是什么。
        if (!body.Contains("ReadClipboard()", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `ReadClipboard()`：剪贴板必须读回来逐字比对，"
                         + "只断言「调过写入口」量不到抄出去的是哪一格。");
        }

        // 期望值取产品口径：标识列走表头「接口」那一格，不另写一份「应该抄第几列」。
        if (!body.Contains("ColumnIndex(list, \"接口\")", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `ColumnIndex(list, \"接口\")`：标识列的期望值必须取产品口径（表头那一格），"
                         + "写死一个列号就是第二份口径。");
        }

        // 「这一行勾不勾得上」也必须取产品口径：自己判一遍状态就是第二份口径。
        if (!body.Contains("SyncSelection.Selectable", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `SyncSelection.Selectable`：翻勾选那一段的前提必须取产品口径，"
                         + "自己判一遍状态就是第二份口径。");
        }

        return problems;
    }

    /// <summary>
    /// 取 <c>RunListCopyGestureSyncSelect</c> 的方法体：从签名那一行到下一个方法注释之前。
    /// 与 <c>SelectFilterGestureSmokeTests.Body</c> 同一取法（按大括号配平，不按缩进猜）。
    /// </summary>
    private static string Body()
    {
        const string signature = "private static void RunListCopyGestureSyncSelect(IServiceProvider services, string dataRoot)";
        var start = Smoke.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"冒烟器里找不到 `{signature}`：这一段被改名或删掉了，本文件的口径需要重定。");

        var open = Smoke.IndexOf('{', start);
        Assert.True(open > start, "签名后面找不到方法体的左大括号。");

        var depth = 0;
        for (var i = open; i < Smoke.Length; i++)
        {
            if (Smoke[i] == '{') depth++;
            else if (Smoke[i] == '}')
            {
                depth--;
                if (depth == 0) return Smoke[open..(i + 1)];
            }
        }

        throw new InvalidOperationException("方法体没有闭合：本文件的口径需要重定。");
    }
}
