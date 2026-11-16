using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 生成文档核对那一屏的 <b>XML 文件清单</b>的列表 Ctrl+C 复制在冒烟器里<b>真的被按过</b>
/// （第三百五十五轮，接续 <c>RunListCopyGesture</c> 一族）。
/// <para>
/// <b>补的是哪个缺口</b>：同屏那张条目清单第三百五十一轮已收，那一轮的说明里把这张清单<b>如实挂账</b>——
/// 「它首列是「项目」而不是文件路径，按现状登记、不在这一轮替它改声明」。挂账的后果是具体的：
/// 这一屏存在的理由正是「注释有没有真的写进 XML 文档」，而「哪一份 XML 文件」只在第二列；
/// 首列「项目」在同一个项目下每一行都同值，Ctrl+C 抄出去的是认不出是哪一份文件的分类词。
/// 本轮在出厂处把标识列声明成「XML 文档文件」（<c>PropagationDialog.XmlFileColumn</c>），
/// 这一份判的就是「那只手势在这一张清单上真的按过、抄出来的是不是那一格」。
/// </para>
/// <para>
/// <b>这一份判什么</b>：① 冒烟段 <c>RunListCopyGesturePropagationXmlFiles</c> 真的经 <c>PressKey</c> 按了
/// <c>^c</c>（三次：抄一次、换行再抄一次、未选中时再按一次）与 <c>{DOWN}</c>（不是直接调事件处理器、
/// 也不是直接写剪贴板）；② 剪贴板是<b>读回来</b>逐字比对的（<c>ReadClipboard()</c>），不是断言「调过写入口」；
/// ③ 期望值取产品口径——标识列走 <c>PropagationDialog.XmlFileColumn</c>，不另写一份「应该抄第几列」
/// （那个常量指向第几位、那一位的标题是不是「XML 文档文件」，由 <see cref="ListCopyIdentityColumnTests"/> 两侧对账）；
/// ④ 那五件事各有一句能读的断言（按段体里的话认，不数行数）；⑤ 这一段在
/// <see cref="MeasuredScreens.All"/> 里有登记、轮次可查。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判 wine 上按下去的结果对不对（那是冒烟器那一跑的事，读数在
/// <c>.winecheck/reading.txt</c> 与 <c>docs/NOTES.md</c> 第三百五十五轮那一节）；不判中文输入法是否吞掉
/// Ctrl+C、不判真机上剪贴板被别的程序占用时的行为；也不判这张清单该不该打开即选中第一行
/// （现状是不选，落点由用户点出来，本轮不改这一条）——Windows GUI 在本机不可验证，观感随 G-D5 复核。
/// </para>
/// </summary>
public class ListCopyGesturePropagationXmlFilesSmokeTests
{
    private const string Segment = "RunListCopyGesturePropagationXmlFiles";

    private static string Smoke => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>
    /// 那五件事各一句能读的话：点一行之后键盘在清单上 / 抄的是「XML 文档文件」不是首列「项目」 /
    /// 换一行跟着换 / 未选中不动作。
    /// </summary>
    private static readonly string[] RequiredSentences =
    [
        "点第一行之后键盘不在 XML 文件清单上（焦点落在别的控件上）：Ctrl+C 落在别的控件上，",
        "这一屏声明了标识列（PropagationDialog.XmlFileColumn），抄出去的必须是那一格。",
        "抄出去的是项目名，",
        "复制的是固定第一行，不是焦点行。",
        "只复制屏幕上真正标出来的那一行，未选中时不该动作（空复制会把用户先前复制的内容抹掉）。",
    ];

    /// <summary>
    /// 真的按过的键：Ctrl+C 三次（抄一次、换行再抄一次、未选中时再按一次——一次按键只有一个含义），
    /// 方向键下至少一次（换行那一段按目标行数按）。
    /// </summary>
    private static readonly string[] RequiredKeyPresses =
    [
        "PressKey(dialog, list, \"^c\");",
        "PressKey(dialog, list, \"{DOWN}\");",
    ];

    [Fact]
    public void TheSmokeSegment_ReallyPressesCtrlCAndReadsTheClipboardBack()
    {
        var body = Body();

        Assert.Empty(Problems(body));

        // Ctrl+C 三次：抄一次、换行再抄一次、未选中时再按一次。少一次就少一件事的读数。
        var copies = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^c\");")).Count;
        Assert.True(copies >= 3,
            $"段体里只按了 {copies} 次 Ctrl+C：抄一次、换行再抄一次、未选中时再按一次，三次各量一件事。");
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
        Assert.True(row!.Measured, "这一段真的 Show() 过 PropagationDialog，登记里必须标「已量」。");
        Assert.Equal("第三百五十五轮", row.Round);
        Assert.Contains("PropagationDialog", row.Classes);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：四发注入各报一处，合规段体不报。
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

        // ② 把「抄的是标识列」那一句断言删掉：抄成首列「项目」时这一段照样绿。
        var noIdentity = good.Replace(
            "这一屏声明了标识列（PropagationDialog.XmlFileColumn），抄出去的必须是那一格。",
            "（删掉）",
            StringComparison.Ordinal);
        Assert.Contains(Problems(noIdentity), p => p.Contains("XmlFileColumn", StringComparison.Ordinal));

        // ③ 把方向键那一下删掉：「复制跟着焦点走」这一条量不到。
        var noArrow = good.Replace("PressKey(dialog, list, \"{DOWN}\");", "", StringComparison.Ordinal);
        Assert.Contains(Problems(noArrow), p => p.Contains("{DOWN}", StringComparison.Ordinal));

        // ④ 剪贴板不再读回来（改成断言「调过写入口」）：逐字比对这件事没有读数了。
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

        // Ctrl+C 必须真的按过三次（抄一次、换行再抄一次、未选中时再按一次）：少一次就少一件事的读数。
        // 只判「这个字符串在不在」不够——段体里本来就有三处，删掉其中一处照样「在」，
        // 而「未选中时剪贴板不动」那件事就没有读数了（第三百五十一轮这一条自测抓到的正是它）。
        var copies = Regex.Matches(body, Regex.Escape("PressKey(dialog, list, \"^c\");")).Count;
        if (copies < 3)
        {
            problems.Add($"段体里只按了 {copies} 次 `PressKey(dialog, list, \"^c\");`：抄一次、换行再抄一次、"
                         + "未选中时再按一次，三次各量一件事——少一次那件事就没有读数。");
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

        // 期望值取产品口径：标识列走 PropagationDialog.XmlFileColumn，不另写一份「应该抄第几列」。
        if (!body.Contains("PropagationDialog.XmlFileColumn", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `PropagationDialog.XmlFileColumn`：标识列的期望值必须取产品口径，"
                         + "写死一个列号就是第二份口径。");
        }

        return problems;
    }

    /// <summary>
    /// 取 <c>RunListCopyGesturePropagationXmlFiles</c> 的方法体：从签名那一行到下一个方法注释之前。
    /// 与 <c>SelectFilterGestureSmokeTests.Body</c> 同一取法（按大括号配平，不按缩进猜）。
    /// </summary>
    private static string Body()
    {
        const string signature = "private static void RunListCopyGesturePropagationXmlFiles(IServiceProvider services, string dataRoot)";
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
