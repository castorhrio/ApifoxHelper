using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「扫描警告」这一屏的竖直预算（第二百五十二轮，简报 P0-1，具名 pass：layout + harden + clarify）。
/// <para>
/// <b>补的是哪个缺口</b>：<see cref="MeasuredScreens"/> 登记表里这一屏从第二百四十三轮起一直挂着「未量」，
/// 到第二百五十一轮它是<b>全仓最后一屏</b>未量的——它只被冒烟器 <c>new</c> 一次 + 建句柄，
/// 而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 本轮真的 <c>Show()</c> 出来，第一次量到：窗体客户区 720×461，两块固定区 说明行 25 + 按钮行 60 = <b>85px</b>，
/// 内容网格上下内边距 24px，清单宿主在网格里的上下外边距 6px，警告清单宿主 <b>346px</b>、
/// 清单本体 <b>336px</b>——扣掉宿主镀铬 10px 之后<b>装得下 16 行</b>中文降级条目。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：窗体高写死 480，与「警告清单要给几行」之间没有任何算式绑住。
/// 本轮改成：清单先说要给几行，像素由同一份行高算，窗体高由「固定两块 + 网格内边距 + 宿主外边距 + 清单」推出来。
/// 算式在 <see cref="ScanWarningsLayout"/> 一处。
/// </para>
/// <para>
/// <b>这一屏的口径与相邻几屏都不同</b>：主内容是<b>只读预览</b>（降级条目），不是输入框
/// （<see cref="ManualDraftLayout"/> 那一屏）、也不是带表头的清单（<see cref="EndpointTraceLayout"/> 那一屏）。
/// 三处的行高在 96 档都量到 21px，但那是<b>三个不同的 21</b>——真机复核时分别量，不许跨屏复用。
/// </para>
/// </summary>
public class ScanWarningsLayoutTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    /// <summary>这一屏的类与窗体高都写在 <c>ScanResultsDialog.cs</c> 里（与 <c>ManualDraftDialog</c> 同一份文件）。</summary>
    private static string Dialog => NativeFile("ScanResultsDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 清单的高度由「行数 × 同一份行高 + 文本框那一圈 + 宿主镀铬」给出，没有一个像素数是单独发明的；
    /// 窗体高与它同源。
    /// </summary>
    [Fact]
    public void ListHostHeight_IsRowsTimesLineHeightPlusTheTwoChromes()
    {
        Assert.Equal(21, ScanWarningsLayout.LineHeight);
        Assert.Equal(16 * 21, ScanWarningsLayout.TextHeight(16));
        Assert.Equal(16 * 21 + 10, ScanWarningsLayout.ListHostHeight(16));
        Assert.Equal(17 * 21 + 10, ScanWarningsLayout.ListHostHeight(17));
        // 文本框那一圈与宿主镀铬是两笔，不许折成一个数：折了之后改镀铬没人知道该动多少。
        Assert.NotEqual(ScanWarningsLayout.TextBoxPadding, ScanWarningsLayout.ListHostChrome);
        Assert.Equal(
            ScanWarningsLayout.FixedPanesHeight + ScanWarningsLayout.GridPaddingHeight
            + ScanWarningsLayout.ListHostMargin + ScanWarningsLayout.ListHostHeight(ScanWarningsLayout.MainRows),
            ScanWarningsLayout.DialogHeight);
    }

    /// <summary>
    /// 窗体高由算式给出，且用的就是产品那一句行数；461 那一格是读数锚点，不是判据。
    /// <para>
    /// <b>为什么判这个</b>：第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测那一屏、
    /// 第二百四十八轮在差异检查结果那一屏、第二百五十轮在接口追踪明细那一屏、第二百五十一轮在手工填写说明
    /// 那一屏上量到过同一课——窗体高写成字面量、与「放不放得下」之间没有算式绑住，改行数或改任一块开销，
    /// 两边不会一起动，而门在原来那一点上照样全绿。
    /// </para>
    /// <para>
    /// <b>461 那一格红了怎么办</b>：它是本轮 wine 96 档量出来的窗体高（85 + 24 + 6 + 16 × 21 + 10）。
    /// 真要改行数或开销，这一格会红，红了必须去重跑量测并回写 <c>docs/NOTES.md</c> 第二百五十二轮那一节，
    /// 不许顺手改成新值。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeight_IsTheFormulaAtTheMeasuredRowCount()
    {
        Assert.Equal(ScanWarningsLayout.DialogHeightFor(ScanWarningsLayout.MainRows), ScanWarningsLayout.DialogHeight);
        Assert.Equal(461, ScanWarningsLayout.DialogHeight);
    }

    /// <summary>
    /// 算式真的跟着行数走：16 / 17 / 15 行三个取值逐个钉死，且一行差恰好是一个行高。
    /// <para>
    /// <b>这一条挡的是「rows 收进参数却没进算式」</b>：把 <c>DialogHeightFor</c> 改回字面量 461，
    /// 上面那条照样绿（那一点上两者同值），这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeightFor_TracksEveryRow()
    {
        Assert.Equal(461, ScanWarningsLayout.DialogHeightFor(16));
        Assert.Equal(482, ScanWarningsLayout.DialogHeightFor(17));
        Assert.Equal(440, ScanWarningsLayout.DialogHeightFor(15));
        Assert.Equal(
            ScanWarningsLayout.LineHeight,
            ScanWarningsLayout.DialogHeightFor(ScanWarningsLayout.MainRows + 1)
            - ScanWarningsLayout.DialogHeightFor(ScanWarningsLayout.MainRows));
    }

    /// <summary>
    /// 接线：窗体高与窗体宽经口径给，720×480 那个字面量不得复活。
    /// <para>
    /// <b>宽与高一起判、判「恰好一处」</b>：简报 P1-3 点的是「宽 720 是常量但没有门判它恰好接线一处」——
    /// 若下一轮有人把 <c>new Size(720, ScanWarningsLayout.DialogHeight)</c> 写成裸 720，
    /// 只判高的那一族门不会红。这里判的是<b>那一整句</b>出现恰好一次，宽与高任一方被拆开都红。
    /// </para>
    /// </summary>
    [Fact]
    public void Dialog_TakesItsSizeFromTheCaliber()
    {
        Assert.Equal(
            1,
            Count(Dialog, @"new Size\(ScanWarningsLayout\.DialogWidth, ScanWarningsLayout\.DialogHeight\)"));
        Assert.DoesNotContain("new Size(720, 480)", Dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("new Size(720, ScanWarningsLayout.DialogHeight)", Dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("new Size(ScanWarningsLayout.DialogWidth, 480)", Dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 固定两块的高度各有名字，<see cref="ScanWarningsLayout.FixedPanesHeight"/> 是它们的和而不是一个独立读数。
    /// <para>
    /// <b>这一条挡的是什么</b>：<c>FixedPanesHeight</c> 若写成字面量 85，而两块的高度散在
    /// <c>ScanResultsDialog.cs</c> 里就地决定，改任一块 → 85 不跟着动 → 清单被悄悄挤掉一行，
    /// 而 <c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c> 照样绿（它只判 16 行那一点的取值）。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedPanesHeight_IsTheSumOfTheTwoNamedPanes()
    {
        // 两块都是 AutoSize，读数是量出来的。
        Assert.Equal(25, ScanWarningsLayout.GuidanceRowHeight);
        Assert.Equal(60, ScanWarningsLayout.ButtonRowHeight);
        // 和是加出来的，不是抄来的：改任一块，这个和跟着动。
        Assert.Equal(
            ScanWarningsLayout.GuidanceRowHeight + ScanWarningsLayout.ButtonRowHeight,
            ScanWarningsLayout.FixedPanesHeight);
        Assert.Equal(85, ScanWarningsLayout.FixedPanesHeight);
        // 网格内边距与宿主外边距都不是一块屏，各自单独记一笔。
        Assert.Equal(24, ScanWarningsLayout.GridPaddingHeight);
        Assert.Equal(6, ScanWarningsLayout.ListHostMargin);
        Assert.Equal(10, ScanWarningsLayout.ListHostChrome);
        Assert.Equal(0, ScanWarningsLayout.TextBoxPadding);
    }

    /// <summary>
    /// 量测那一段不能退回「建句柄不显示」，也不能把行数抄成第二个字面量。
    /// <para>
    /// <b>判的是那一段，不是整份文件</b>：冒烟器另几段本来就写着 <c>dialog.Show();</c>，
    /// 用 <c>Contains</c> 判整份文件会被别处满足（本仓第二百一十、二百一十一轮记过同一形态）。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ShowsTheDialogAndTakesTheRowCountFromTheCaliber()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunScanWarningsShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("dialog.Show();", segment, StringComparison.Ordinal);
        // 布局要真的跑完：Show() 之后补一次 DoEvents 让停靠重排落地（与 RunManualDraftShown 同一路径）。
        Assert.Contains("Application.DoEvents();", segment, StringComparison.Ordinal);
        // 行数取口径那一处，不抄第二份 16。
        Assert.Contains("ScanWarningsLayout.MainRows", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("const int MainRows = 16;", segment, StringComparison.Ordinal);
        // 非设计档不判（窗体被虚拟屏截断，那一刻属于「量不到」而不是「放不下」）。
        Assert.Contains("DpiScaleFactor.ShouldScale", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测段把<b>实测</b>与<b>口径</b>逐笔对账，而不是只判一句恒等式（第二百五十二轮）。
    /// <para>
    /// <b>为什么这一条必须存在</b>：这一段第一版只判「内容网格 + 按钮行 = 客户区高」——
    /// 那是<b>恒等式</b>（内容网格 Fill、按钮行 Bottom，加起来必然等于客户区），
    /// 常量漂了窗体高跟着漂、两边一起动，那一句照样绿。而段标题念的是「固定两块与 ScanWarningsLayout 对账、
    /// 警告清单实测高与算式对账、装得下若干行」三件事——<b>标题念了而代码没判，就是假门</b>
    /// （第二百四十九、二百五十、二百五十一轮连续三轮撞的都是这一形态）。
    /// 所以这里逐笔判：固定两块、行高、宿主镀铬、宿主高、清单本体高、网格内边距、宿主外边距、窗体高、容量。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ReconcilesEveryMeasuredPaneAgainstTheFormula()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunScanWarningsShown(IServiceProvider services, string dataRoot)");

        // 固定两块：实测合计 vs 口径里的和。
        Assert.Contains("ScanWarningsLayout.FixedPanesHeight", segment, StringComparison.Ordinal);
        // 行高：实测 vs 口径。
        Assert.Contains("ScanWarningsLayout.LineHeight", segment, StringComparison.Ordinal);
        // 宿主镀铬：实测 vs 口径。
        Assert.Contains("ScanWarningsLayout.ListHostChrome", segment, StringComparison.Ordinal);
        // 宿主高与清单本体高：实测 vs 算式（这两句不是恒等式——左边是布局引擎排出来的）。
        Assert.Contains("ScanWarningsLayout.ListHostHeight(ScanWarningsLayout.MainRows)", segment, StringComparison.Ordinal);
        Assert.Contains("ScanWarningsLayout.TextHeight(ScanWarningsLayout.MainRows)", segment, StringComparison.Ordinal);
        // 网格内边距与宿主外边距：两笔各自对账，不许折成一个数。
        Assert.Contains("ScanWarningsLayout.GridPaddingHeight", segment, StringComparison.Ordinal);
        Assert.Contains("ScanWarningsLayout.ListHostMargin", segment, StringComparison.Ordinal);
        // 窗体高：实测客户区 vs 口径。
        Assert.Contains("ScanWarningsLayout.DialogHeight", segment, StringComparison.Ordinal);
        // 容量判据仍在（实测容量对判据行数）。
        Assert.Contains("capacity < ScanWarningsLayout.MainRows", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 登记表里这一屏从「未量」改成「已量」并写清轮次——简报 P0-1 要的那一行。
    /// <para>
    /// 通用的对账在 <c>MeasuredScreensTests</c>；这一条只钉「这一屏这一轮真的被认领了」，
    /// 免得下一轮有人把 <c>RunScanWarningsShown</c> 删掉而登记表悄悄退回「未量」还全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuredScreens_RegistersThisScreenAsMeasured()
    {
        var row = MeasuredScreens.All.Single(r => r.Classes.Contains("ScanWarningsDialog", StringComparer.Ordinal));
        Assert.True(row.Measured, "「扫描警告」在登记表里又退回未量了：本轮真的 Show() 过它。");
        Assert.Equal("RunScanWarningsShown", row.Section);
        Assert.Equal("第二百五十二轮", row.Round);
    }

    /// <summary>
    /// 未量屏归零：登记表里不再有 <c>Measured=false</c> 的行（第二百五十二轮）。
    /// <para>
    /// <b>这一条判的是「这一族收口了」这件事本身</b>：从第二百四十三轮起逐屏收口，到本轮最后一屏量完。
    /// 下一轮若新增一屏只构造不显示，这一条会红——那时要么补量测段，要么把这一条连同理由一起改掉，
    /// 不许让它静默变成「反正还有一行未量」。
    /// </para>
    /// </summary>
    [Fact]
    public void NoScreenIsLeftUnmeasured()
    {
        var unmeasured = MeasuredScreens.All.Where(r => !r.Measured).Select(r => r.Screen).ToList();
        Assert.True(
            unmeasured.Count == 0,
            $"登记表里还有未量的屏：{string.Join("、", unmeasured)}——"
            + "这一族从第二百四十三轮起逐屏收口，第二百五十二轮已归零；新增未量屏要同时补量测段。");
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void ScanWarningsLayout_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\ScanWarningsLayout.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }

    /// <summary>取一个方法体：从签名那一行起，到第一个「四空格缩进的收尾大括号」为止（冒烟器里方法都是这个缩进）。</summary>
    private static string MethodSlice(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{signature} 在冒烟器里找不到了——量测段改名或搬家，这一族门就再也管不住它。");
        var body = new List<string>();
        foreach (var line in source[start..].Split('\n'))
        {
            body.Add(line);
            if (line.TrimEnd() == "    }") break;
        }
        return string.Join('\n', body);
    }
}
