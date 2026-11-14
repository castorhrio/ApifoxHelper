using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 勾选页三个筛选手势（Ctrl+F / 框里 Enter / 框里 Esc）在冒烟器里<b>真的被按过</b>（第二百九十七轮）。
/// <para>
/// <b>补的是哪个缺口</b>：这三个手势自第七十三轮接上以来，证据只有<b>源码形状</b>——
/// <c>FilterEscapeTests</c> / <c>FilterEnterTests</c> / <c>FilterGesturePlanTests</c> 读的是
/// 「登记表里登记了哪几个框、<c>ProcessDialogKey</c> 里那一行在不在」，而<b>这三个键一次都没被真的按过</b>
/// （全仓冒烟器此前只按过 Ctrl+A、裸空格、Ctrl+空格）。于是「按下去焦点落在哪个框上」
/// 「框里已有关键词会不会被全选」「Esc 清空之后清单回不回全部行」「Esc 会不会顺手把对话框关掉」
/// 「清空关键词触发的重建会不会把勾选丢掉」这五件事，全仓没有一条读数。
/// </para>
/// <para>
/// <b>这一份判什么</b>：① 冒烟段 <c>RunSelectFilterGestures</c> 真的经 <c>PressKey</c> 按了
/// <c>^f</c> / <c>{ENTER}</c> / <c>{ESC}</c>（不是直接调事件处理器、也不是直接赋 <c>Text</c>）；
/// ② 期望值取产品自己的口径（<see cref="SyncSelect.Visible"/> / <see cref="SyncSelect.VisibleSyncableCount"/>），
/// 不另写一份「应该剩几行」；③ 那五件事各有一句能读的断言（按段体里的话认，不数行数）；
/// ④ 这一段在 <see cref="MeasuredScreens.All"/> 里有登记、轮次可查。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判 wine 上按下去的结果对不对（那是冒烟器那一跑的事，读数在
/// <c>.winecheck/reading.txt</c> 与 <c>docs/NOTES.md</c> 第二百九十七轮那一节）；
/// 也不判真机上的按键路由——Windows GUI 在本机不可验证，随 G-D5 复核。
/// </para>
/// </summary>
public class SelectFilterGestureSmokeTests
{
    private const string Segment = "RunSelectFilterGestures";

    private static string Smoke => Repo.Source("ApifoxHelper.Smoke", "Program.cs");

    /// <summary>
    /// 那五件事各一句能读的话：焦点交给框 / 已有关键词整段被选中 / 键入即筛选 / 清空关键词回到全部行且不关窗 /
    /// 重建不丢勾选 / Enter 落在显示序第一行 / 清单上 Esc 仍是取消。
    /// </summary>
    private static readonly string[] RequiredSentences =
    [
        "按一次 Ctrl+F 之后焦点不在关键词框上",
        "「把焦点交给框并选中已有关键词」这一句要求整段被选中",
        "输入即筛选这一条在真实键入下没生效",
        "清空关键词触发的重建必须从 SyncSelect 恢复勾选",
        "框里的 Esc 是「退出筛选」",
        "Enter 的落点是显示序第一行（筛选与排序之后）",
        "焦点不在关键词框上时 Esc 仍是「取消对话框」",
    ];

    /// <summary>三个键各按一次（Esc 两次：框里一次、清单上一次——一次按键只有一个含义）。</summary>
    private static readonly string[] RequiredKeyPresses =
    [
        "PressKey(dialog, null, \"^f\");",
        "PressKey(dialog, null, \"{ENTER}\");",
        "PressKey(dialog, null, \"{ESC}\");",
        "PressKey(dialog, list, \"{ESC}\");",
    ];

    [Fact]
    public void TheSmokeSegment_ReallyPressesTheThreeFilterGestures()
    {
        var body = Body();

        // 期望值取产品口径：这两句在冒烟段与这一条门里逐字相同（同源的证据，见
        // GateSourceConsistencyTests.Pairs 里 RunSelectFilterGestures 那一行）。
        Assert.Contains("new SyncSelect { Query = keywordText }.Visible(diff)", body, StringComparison.Ordinal);
        Assert.Contains("VisibleSyncableCount(diff, new DecisionStore(), false)", body, StringComparison.Ordinal);

        Assert.Empty(Problems(body));
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
        Assert.Equal("第二百九十七轮", row.Round);
        Assert.Contains("SyncPreviewDialog", row.Classes);
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：三发注入各报一处，合规段体不报。
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

        // ① 把 Ctrl+F 那一下删掉：焦点交给框这件事没有读数了。
        var noFind = good.Replace("PressKey(dialog, null, \"^f\");", "", StringComparison.Ordinal);
        Assert.Contains(Problems(noFind), p => p.Contains("^f", StringComparison.Ordinal));

        // ② 把「重建不丢勾选」那一句断言删掉：勾选静默丢光时这一段照样绿。
        var noChecked = good.Replace("清空关键词触发的重建必须从 SyncSelect 恢复勾选", "（删掉）", StringComparison.Ordinal);
        Assert.Contains(Problems(noChecked), p => p.Contains("恢复勾选", StringComparison.Ordinal));

        // ③ 期望值改成写死的数：口径不再取产品自己那一份。
        var hardcoded = good.Replace(
            "new SyncSelect { Query = keywordText }.Visible(diff)",
            "new List<EndpointCheck>()",
            StringComparison.Ordinal);
        Assert.Contains(Problems(hardcoded), p => p.Contains("SyncSelect.Visible", StringComparison.Ordinal));
    }

    // ---------- 判据本体（纯函数：段体进、问题出） ----------

    /// <summary>
    /// 段体进、问题出（空 = 过得去）。写成纯函数是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>MeasuredScreensTests.Problems</c> / <c>RoundLedgerTests.Problems</c> 同一形状。
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

        foreach (var sentence in RequiredSentences)
        {
            if (!body.Contains(sentence, StringComparison.Ordinal))
            {
                problems.Add($"段体里读不到「{sentence}」这一句：那件事没有读数，"
                             + "改坏了它这一段照样绿。");
            }
        }

        // 期望值取产品口径：不另写一份「应该剩几行」。
        if (!body.Contains("new SyncSelect { Query = keywordText }.Visible(diff)", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `new SyncSelect { Query = keywordText }.Visible(diff)`："
                         + "可见行的期望值必须取 SyncSelect.Visible，写死一个数就是第二份口径。");
        }

        if (!body.Contains("VisibleSyncableCount(diff, new DecisionStore(), false)", StringComparison.Ordinal))
        {
            problems.Add("段体里没有 `VisibleSyncableCount(diff, new DecisionStore(), false)`："
                         + "可同步行数的期望值必须取产品口径，写死一个数就是第二份口径。");
        }

        // 键入必须真的敲进去（不是直接赋 Text）：直接赋 Text 量不到「键入即筛选」。
        if (!Regex.IsMatch(body, @"PressKey\(dialog, null, keywordText\);"))
        {
            problems.Add("段体里没有 `PressKey(dialog, null, keywordText);`：关键词是直接赋 Text 的话，"
                         + "「键入即筛选」这一条量不到。");
        }

        return problems;
    }

    /// <summary>
    /// 取 <c>RunSelectFilterGestures</c> 的方法体：从签名那一行到下一个方法注释之前。
    /// 与 <c>SelectAllButtonGateTests.Slice</c> 同一取法（按大括号配平，不按缩进猜）。
    /// </summary>
    private static string Body()
    {
        const string signature = "private static void RunSelectFilterGestures(IServiceProvider services, string dataRoot)";
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
