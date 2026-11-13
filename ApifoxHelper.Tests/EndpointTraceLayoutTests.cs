using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「接口追踪明细」这一屏的竖直预算（第二百五十轮，简报 P0-1，具名 pass：layout + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十六轮把「哪些屏真的显示过」登记成表并配了门，这一屏
/// （<c>EndpointTraceDialog</c>）在登记里标的是<b>未量</b>——它至今只被冒烟器 <c>new</c> 一次 + 建句柄
/// （按钮行高度那一段），而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 本轮真的 <c>Show()</c> 出来，第一次量到：窗体 920×640 时四块固定区 顶部 43 + 31 + 底部 219 + 60
/// = <b>353px</b>，留给接口清单宿主只有 <b>287px</b>、清单本体 <b>277px</b>（改算式之后窗体高 639，
/// 实跑量到宿主 <b>286px</b>、清单本体 <b>276px</b>）——扣掉表头 24px 之后
/// <b>装得下 12 行</b>。
/// </para>
/// <para>
/// <b>为什么挑这一屏</b>（简报 P0-1 的「先量再挑」）：三屏都量过之后，按「主内容要给几行」排，
/// 这一屏 12 行、手工填写说明 10 行、扫描警告 16 行——<b>没有一屏掉到个位数</b>，所以判据不能照抄
/// 「只剩个位数行」那一句（简报也写了「以实跑为准，不硬套」）。挑它的理由是另外三条：
/// ① 固定区占屏比最高（353 / 640 = 55%）；② 它是三屏里唯一有<b>就地写死的定高</b>（追踪明细那个
/// 190px 的文本框）与<b>裸窗体高</b>（640）的一屏；③ 它的主内容是一张带表头的清单，与
/// <see cref="CheckResultsLayout"/> 同一形状，收口方式能逐条对上。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：窗体高写死 640，与「接口清单要给几行」之间没有任何算式绑住。
/// 本轮改成：接口清单先说要给几行，像素由同一份行高 + 表头算，窗体高由「固定四块 + 接口清单 +
/// 宿主多出来的那一截」推出来。算式在 <see cref="EndpointTraceLayout"/> 一处。
/// </para>
/// </summary>
public class EndpointTraceLayoutTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Dialog => NativeFile("EndpointTraceDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 接口清单的高度由「行数 × 同一份行高 + 表头」给出，没有一个像素数是单独发明的；窗体高与它同源。
    /// </summary>
    [Fact]
    public void MainListHeight_IsRowsTimesRowHeightPlusHeader()
    {
        Assert.Equal(21, EndpointTraceLayout.RowHeight);
        Assert.Equal(12 * 21 + 24, EndpointTraceLayout.MainListHeight(12));
        Assert.Equal(13 * 21 + 24, EndpointTraceLayout.MainListHeight(13));
        // 表头与宿主那一截是两笔，不许折成一个数：折了之后改内边距没人知道该动多少。
        Assert.NotEqual(EndpointTraceLayout.ColumnHeaderHeight, EndpointTraceLayout.ListHostChrome);
        Assert.Equal(
            EndpointTraceLayout.FixedPanesHeight + EndpointTraceLayout.MainListHeight(EndpointTraceLayout.MainRows) + EndpointTraceLayout.ListHostChrome,
            EndpointTraceLayout.DialogHeight);
    }

    /// <summary>
    /// 窗体高由算式给出，且用的就是产品那一句行数；639 那一格是读数锚点，不是判据。
    /// <para>
    /// <b>为什么判这个</b>：第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测那一屏、
    /// 第二百四十八轮在差异检查结果那一屏上量到过同一课——窗体高写成字面量、与「放不放得下」之间
    /// 没有算式绑住，改行数或改任一块开销，两边不会一起动，而门在原来那一点上照样全绿。
    /// </para>
    /// <para>
    /// <b>639 那一格红了怎么办</b>：它是本轮 wine 96 档量出来的窗体高（353 + 12 × 21 + 24 + 10）。
    /// 真要改行数或开销，这一格会红，红了必须去重跑量测并回写 <c>docs/NOTES.md</c> 第二百五十轮那一节，
    /// 不许顺手改成新值——「算式跟着漂、实测没跟着量」正是这一族最贵的那种绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeight_IsTheFormulaAtTheMeasuredRowCount()
    {
        Assert.Equal(EndpointTraceLayout.DialogHeightFor(EndpointTraceLayout.MainRows), EndpointTraceLayout.DialogHeight);
        Assert.Equal(639, EndpointTraceLayout.DialogHeight);
    }

    /// <summary>
    /// 算式真的跟着行数走：12 / 13 / 11 行三个取值逐个钉死，且一行差恰好是一个行高。
    /// <para>
    /// <b>这一条挡的是「rows 收进参数却没进算式」</b>：把 <c>DialogHeightFor</c> 改回字面量 639，
    /// 上面那条照样绿（那一点上两者同值），这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeightFor_TracksEveryRow()
    {
        Assert.Equal(639, EndpointTraceLayout.DialogHeightFor(12));
        Assert.Equal(660, EndpointTraceLayout.DialogHeightFor(13));
        Assert.Equal(618, EndpointTraceLayout.DialogHeightFor(11));
        Assert.Equal(
            EndpointTraceLayout.RowHeight,
            EndpointTraceLayout.DialogHeightFor(EndpointTraceLayout.MainRows + 1)
            - EndpointTraceLayout.DialogHeightFor(EndpointTraceLayout.MainRows));
    }

    /// <summary>
    /// 接线：窗体高经口径给，640 那个字面量不得复活。
    /// 「窗体高写死」正是本轮的根因形状，留一个在文件里就等于留着下一次的挤没。
    /// </summary>
    [Fact]
    public void Dialog_TakesItsHeightFromTheCaliber()
    {
        Assert.Equal(1, Count(Dialog, @"new Size\(EndpointTraceLayout\.DialogWidth, EndpointTraceLayout\.DialogHeight\)"));
        Assert.DoesNotContain("new Size(920, 640)", Dialog, StringComparison.Ordinal);
    }

    /// <summary>
    /// 追踪明细那个文本框的定高只从口径取，190 那个字面量不得复活（第二百五十轮，简报 P0-1 的接线那一半）。
    /// <para>
    /// <b>判的是「有几处」不是「有没有」</b>（第二百四十五轮那一课）：这一屏只有一处定高，
    /// 恰好一处；少一处说明它又写回了字面量，多一处说明有人把同一块设了两遍。
    /// </para>
    /// </summary>
    [Fact]
    public void Dialog_TakesTheDetailBoxHeightFromTheCaliber()
    {
        Assert.Equal(1, Count(Dialog, @"AddBoxRow\(detailGrid, Ui\.Hairline\(_detail\), EndpointTraceLayout\.DetailBoxHeight\)"));
        Assert.DoesNotContain("Ui.Hairline(_detail), 190", Dialog, StringComparison.Ordinal);
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
            "private static void RunEndpointTraceShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("dialog.Show();", segment, StringComparison.Ordinal);
        // 布局要真的跑完：Show() 之后补一次 DoEvents 让停靠重排落地（与 RunCheckResultsShown 同一路径）。
        Assert.Contains("Application.DoEvents();", segment, StringComparison.Ordinal);
        // 行数取口径那一处，不抄第二份 12。
        Assert.Contains("EndpointTraceLayout.MainRows", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("const int MainRows = 12;", segment, StringComparison.Ordinal);
        // 表头那一笔必须从读数里扣掉：只按「清单高 / 行高」算会把表头也算成一行
        // （第二百四十七轮在探测那一屏撞过同一处）。
        Assert.Contains("headerHeight", segment, StringComparison.Ordinal);
        // 非设计档不判（窗体被虚拟屏截断，那一刻属于「量不到」而不是「放不下」）。
        Assert.Contains("DpiScaleFactor.ShouldScale", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测段真的把「固定四块实测」与口径对账（第二百五十轮）。
    /// <para>
    /// <b>为什么判这一段</b>：顶部两行与按钮行是 <c>AutoSize</c>，代码里没有一处能设它们——
    /// 它们的读数只能在这里量。少了这一句，<c>FixedPanesHeight</c> 就又变回一个没人核对的数。
    /// </para>
    /// <para>
    /// <b>顶部两行按内容认名</b>：这一屏的 43 / 31 与差异检查结果那一屏的 31 / 39 正好相反，
    /// 按控件顺序猜会读反（第二百四十九轮在那一屏量到过同一课）。认名要**递归**找关键词框——
    /// 关键词框在 <c>Ui.FilterRow</c> 里经 <c>Ui.Field</c> 包了一层宿主，只看直接子控件会把两行都认成标题行
    /// （本轮第一次跑就是这么打出来的）。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ReconcilesTheFixedPanesAndNamesTheTopRows()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunEndpointTraceShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("EndpointTraceLayout.FixedPanesHeight", segment, StringComparison.Ordinal);
        Assert.Contains("PaneName", segment, StringComparison.Ordinal);
        // 认名必须递归：关键词框不是筛选行的直接子控件。
        Assert.Contains("FindAll<TextBox>(pane)", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("pane.Controls.Cast<Control>().Any(control => control is TextBox)", segment, StringComparison.Ordinal);
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

    /// <summary>
    /// 登记表里这一屏从「未量」改成「已量」并写清轮次——简报 P0-1 要的那一行。
    /// <para>
    /// 通用的对账在 <c>MeasuredScreensTests</c>；这一条只钉「这一屏这一轮真的被认领了」，
    /// 免得下一轮有人把 <c>RunEndpointTraceShown</c> 删掉而登记表悄悄退回「未量」还全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuredScreens_RegistersThisScreenAsMeasured()
    {
        var row = MeasuredScreens.All.Single(r => r.Classes.Contains("EndpointTraceDialog", StringComparer.Ordinal));
        Assert.True(row.Measured, "「接口追踪明细」在登记表里又退回未量了：本轮真的 Show() 过它。");
        Assert.Equal("RunEndpointTraceShown", row.Section);
        Assert.Equal("第二百五十轮", row.Round);
    }

    /// <summary>
    /// 固定四块的高度各有名字，<see cref="EndpointTraceLayout.FixedPanesHeight"/> 是它们的和而不是一个独立读数
    /// （第二百五十轮，简报 P0-1）。
    /// <para>
    /// <b>这一条挡的是什么</b>：<c>FixedPanesHeight</c> 若写成字面量 353，而四块的高度散在
    /// <c>EndpointTraceDialog.cs</c> 里就地写死，改任一块 → 353 不跟着动 → 接口清单被悄悄挤掉一行，
    /// 而 <c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c> 照样绿（它只判 12 行那一点的取值）。
    /// </para>
    /// <para>
    /// <b>四个数的名字按内容认</b>：43 是标题行、31 是筛选行——与差异检查结果那一屏（31 / 39）正好相反，
    /// 两个数不能跨屏抄。这里把两个数各自钉死，改错名字（把 31 与 43 换回来）这一条会红。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedPanesHeight_IsTheSumOfTheFourNamedPanes()
    {
        // 顶部两行（AutoSize，读数是量出来的）：标题行 43、筛选行 31。
        Assert.Equal(43, EndpointTraceLayout.HeadRowHeight);
        Assert.Equal(31, EndpointTraceLayout.FilterRowHeight);
        // 底部两块：追踪明细（定高，由本文件给）+ 按钮行（AutoSize，读数是量出来的）。
        Assert.Equal(219, EndpointTraceLayout.DetailPaneHeight);
        Assert.Equal(60, EndpointTraceLayout.ButtonRowHeight);
        // 追踪明细那一块自己是三笔加出来的：说明行 17 + 文本框 190 + 上下内边距 12。
        Assert.Equal(190, EndpointTraceLayout.DetailBoxHeight);
        Assert.Equal(17, EndpointTraceLayout.DetailCaptionHeight);
        Assert.Equal(12, EndpointTraceLayout.DetailPanePadding);
        Assert.Equal(
            EndpointTraceLayout.DetailBoxHeight + EndpointTraceLayout.DetailCaptionHeight + EndpointTraceLayout.DetailPanePadding,
            EndpointTraceLayout.DetailPaneHeight);
        // 和是加出来的，不是抄来的：改任一块，这个和跟着动。
        Assert.Equal(
            EndpointTraceLayout.HeadRowHeight + EndpointTraceLayout.FilterRowHeight
            + EndpointTraceLayout.DetailPaneHeight + EndpointTraceLayout.ButtonRowHeight,
            EndpointTraceLayout.FixedPanesHeight);
        Assert.Equal(353, EndpointTraceLayout.FixedPanesHeight);
    }

    /// <summary>
    /// 这一屏的固定区占屏比是三张「未量」屏里最高的——挑它的理由要能被算式复核，而不是只写在注释里。
    /// <para>
    /// <b>判的是「主内容还剩多少」</b>：固定四块 353px 对窗体高 639px，接口清单宿主只剩 286px。
    /// 这一条红了说明有人把固定区撑大了（或把行数改了），那时要重新回答「为什么挑这一屏」。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedPanes_TakeMoreThanHalfOfTheDialog()
    {
        Assert.True(
            EndpointTraceLayout.FixedPanesHeight * 2 > EndpointTraceLayout.DialogHeight,
            $"固定四块 {EndpointTraceLayout.FixedPanesHeight}px 已不再超过窗体高 {EndpointTraceLayout.DialogHeight}px 的一半："
            + "「这一屏固定区占屏比最高」这句话需要重新量过。");
        // 主内容（接口清单宿主）仍拿得到判据行数那么多行：宿主高 − 宿主多出来的那一截 − 表头 ≥ 行数 × 行高。
        var listHost = EndpointTraceLayout.DialogHeight - EndpointTraceLayout.FixedPanesHeight;
        Assert.Equal(286, listHost);
        Assert.True(
            listHost - EndpointTraceLayout.ListHostChrome - EndpointTraceLayout.ColumnHeaderHeight
            >= EndpointTraceLayout.MainRows * EndpointTraceLayout.RowHeight,
            "接口清单宿主扣掉表头与内边距之后装不下判据行数——这一屏的主内容被固定区挤没了。");
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void EndpointTraceLayout_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\EndpointTraceLayout.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }
}
