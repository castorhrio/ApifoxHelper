using System.Text.RegularExpressions;
using ApifoxHelper.Desktop.Native;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「手工填写说明」这一屏的竖直预算（第二百五十一轮，简报 P1-1，具名 pass：layout + harden）。
/// <para>
/// <b>补的是哪个缺口</b>：第二百四十六轮把「哪些屏真的显示过」登记成表并配了门，这一屏
/// （<c>ManualDraftDialog</c>）在登记里标的是<b>未量</b>——它至今只被冒烟器 <c>new</c> 一次 + 建句柄，
/// 而一张没显示过的窗体 <c>ClientSize</c> 的高是 0，量到的数不能读成「放得下」。
/// 本轮真的 <c>Show()</c> 出来，第一次量到：窗体 560×400，四块固定区 位置行 20 + 说明行 46 +
/// 状态行 33 + 按钮行 64 = <b>163px</b>，内容网格上下内边距 24px，输入区宿主 <b>213px</b>、
/// 输入框 <b>211px</b>——扣掉输入框自己那一圈 1px 之后<b>装得下 10 行</b>中文说明。
/// </para>
/// <para>
/// <b>这一屏的口径与前几屏不同</b>：前几屏的主内容是<b>清单</b>，判据是「装得下几行」；
/// 这一屏的主内容是<b>多行说明输入区</b>（用户在这里手写方法用途、参数含义、响应字段含义），
/// 判据同样是「装得下几行」，但那一行是<b>输入框的行</b>——两个口径不能跨屏抄，
/// 行高也不是同一个数（清单行高 21px 是清单行的，输入框行高 21px 是输入框文字的）。
/// </para>
/// <para>
/// <b>根因是一个形状而不是一个数</b>：窗体高写死 400，与「输入区要给几行」之间没有任何算式绑住。
/// 本轮改成：输入区先说要给几行，像素由同一份行高算，窗体高由「固定四块 + 网格内边距 + 输入区」
/// 推出来。算式在 <see cref="ManualDraftLayout"/> 一处。
/// </para>
/// </summary>
public class ManualDraftLayoutTests
{
    private static string NativeFile(string name) => Repo.Source("Desktop", "Native", name);

    private static string Dialog => NativeFile("ScanResultsDialog.cs");

    private static int Count(string text, string pattern) => Regex.Matches(text, pattern).Count;

    /// <summary>
    /// 输入区的高度由「行数 × 同一份行高 + 输入框那一圈 + 宿主镀铬」给出，
    /// 没有一个像素数是单独发明的；窗体高与它同源。
    /// </summary>
    [Fact]
    public void InputAreaHeight_IsRowsTimesLineHeightPlusTheTwoChromes()
    {
        Assert.Equal(21, ManualDraftLayout.LineHeight);
        Assert.Equal(10 * 21 + 1 + 2, ManualDraftLayout.InputAreaHeight(10));
        Assert.Equal(11 * 21 + 1 + 2, ManualDraftLayout.InputAreaHeight(11));
        // 输入框那一圈与宿主镀铬是两笔，不许折成一个数：折了之后改镀铬没人知道该动多少。
        Assert.NotEqual(ManualDraftLayout.TextBoxPadding, ManualDraftLayout.FieldBoxPadding);
        Assert.Equal(
            ManualDraftLayout.FixedPanesHeight + ManualDraftLayout.GridPaddingHeight
            + ManualDraftLayout.InputAreaHeight(ManualDraftLayout.MainRows),
            ManualDraftLayout.DialogHeight);
    }

    /// <summary>
    /// 窗体高由算式给出，且用的就是产品那一句行数；400 那一格是读数锚点，不是判据。
    /// <para>
    /// <b>为什么判这个</b>：第二百四十四轮在同步预览那一屏、第二百四十七轮在连接与写入探测那一屏、
    /// 第二百四十八轮在差异检查结果那一屏、第二百五十轮在接口追踪明细那一屏上量到过同一课——
    /// 窗体高写成字面量、与「放不放得下」之间没有算式绑住，改行数或改任一块开销，两边不会一起动，
    /// 而门在原来那一点上照样全绿。
    /// </para>
    /// <para>
    /// <b>400 那一格红了怎么办</b>：它是本轮 wine 96 档量出来的窗体高
    /// （163 + 24 + 10 × 21 + 1 + 2）。真要改行数或开销，这一格会红，
    /// 红了必须去重跑量测并回写 <c>docs/NOTES.md</c> 第二百五十一轮那一节，
    /// 不许顺手改成新值——「算式跟着漂、实测没跟着量」正是这一族最贵的那种绿。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeight_IsTheFormulaAtTheMeasuredRowCount()
    {
        Assert.Equal(ManualDraftLayout.DialogHeightFor(ManualDraftLayout.MainRows), ManualDraftLayout.DialogHeight);
        Assert.Equal(400, ManualDraftLayout.DialogHeight);
    }

    /// <summary>
    /// 算式真的跟着行数走：10 / 11 / 9 行三个取值逐个钉死，且一行差恰好是一个行高。
    /// <para>
    /// <b>这一条挡的是「rows 收进参数却没进算式」</b>：把 <c>DialogHeightFor</c> 改回字面量 400，
    /// 上面那条照样绿（那一点上两者同值），这一条当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void DialogHeightFor_TracksEveryRow()
    {
        Assert.Equal(400, ManualDraftLayout.DialogHeightFor(10));
        Assert.Equal(421, ManualDraftLayout.DialogHeightFor(11));
        Assert.Equal(379, ManualDraftLayout.DialogHeightFor(9));
        Assert.Equal(
            ManualDraftLayout.LineHeight,
            ManualDraftLayout.DialogHeightFor(ManualDraftLayout.MainRows + 1)
            - ManualDraftLayout.DialogHeightFor(ManualDraftLayout.MainRows));
    }

    /// <summary>
    /// 接线：窗体高与窗体宽经口径给，560×400 那个字面量不得复活。
    /// 「窗体高写死」正是本轮的根因形状，留一个在文件里就等于留着下一次的挤没。
    /// </summary>
    [Fact]
    public void Dialog_TakesItsHeightFromTheCaliber()
    {
        Assert.Equal(
            1,
            Count(Dialog, @"new Size\(ManualDraftLayout\.DialogWidth, ManualDraftLayout\.DialogHeight\)"));
        Assert.DoesNotContain("new Size(560, 400)", Dialog, StringComparison.Ordinal);
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
            "private static void RunManualDraftShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("dialog.Show();", segment, StringComparison.Ordinal);
        // 布局要真的跑完：Show() 之后补一次 DoEvents 让停靠重排落地（与 RunEndpointTraceShown 同一路径）。
        Assert.Contains("Application.DoEvents();", segment, StringComparison.Ordinal);
        // 行数取口径那一处，不抄第二份 10。
        Assert.Contains("ManualDraftLayout.MainRows", segment, StringComparison.Ordinal);
        Assert.DoesNotContain("const int MainRows = 10;", segment, StringComparison.Ordinal);
        // 行高与两圈镀铬要分别量，不许只报一个「输入区高」。
        Assert.Contains("ManualDraftLayout.LineHeight", segment, StringComparison.Ordinal);
        Assert.Contains("ManualDraftLayout.TextBoxPadding", segment, StringComparison.Ordinal);
        Assert.Contains("ManualDraftLayout.FieldBoxPadding", segment, StringComparison.Ordinal);
        // 非设计档不判（窗体被虚拟屏截断，那一刻属于「量不到」而不是「放不下」）。
        Assert.Contains("DpiScaleFactor.ShouldScale", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 量测段真的把「固定四块实测」与口径对账（第二百五十一轮）。
    /// <para>
    /// <b>为什么判这一段</b>：位置行、说明行、状态行与按钮行都是 <c>AutoSize</c>，
    /// 代码里没有一处能设它们——它们的读数只能在这里量。少了这一句，
    /// <c>FixedPanesHeight</c> 就又变回一个没人核对的数。
    /// </para>
    /// <para>
    /// <b>四块按内容与类型认名</b>：状态行是类型正好是 <c>Panel</c> 的那块（<c>FlowLayoutPanel</c> 本身就是
    /// <c>Panel</c>，所以按钮行要先摘出去），说明行是带「纯文本说明」那句的那一块，
    /// 位置行是另一块。认名之后还要回查行号——名与行号对不上，行高对账就会张冠李戴。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ReconcilesTheFixedPanesAndNamesThem()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunManualDraftShown(IServiceProvider services, string dataRoot)");
        Assert.Contains("ManualDraftLayout.FixedPanesHeight", segment, StringComparison.Ordinal);
        // 认名：状态行按类型、说明行按内容。
        Assert.Contains("GetType() == typeof(Panel)", segment, StringComparison.Ordinal);
        Assert.Contains("纯文本说明", segment, StringComparison.Ordinal);
        // 名与行号要对得上。
        Assert.Contains("content.GetRow(", segment, StringComparison.Ordinal);
    }

    /// <summary>
    /// 输入区的实测高与算式逐字相等——这一屏的主内容必须对得上（第二百五十一轮）。
    /// <para>
    /// <b>为什么这一格必须挂在实跑上</b>：窗体高由「固定四块 + 网格内边距 + 输入区」推出，
    /// 所以「输入区装不装得下十行」在算式里<b>恒为真</b>——它是定义，不是事实。
    /// 事实只在真控件排完版之后才存在：宿主实测高、输入框实测高、行高实测高，都是量出来的。
    /// </para>
    /// </summary>
    [Fact]
    public void TheMeasurement_ReconcilesTheMeasuredInputAreaAgainstTheFormula()
    {
        var segment = MethodSlice(Repo.SmokeProgram,
            "private static void RunManualDraftShown(IServiceProvider services, string dataRoot)");
        // 实测宿主高 vs 算式：两边都要在——只有算式没有实测是恒等式，只有实测没有算式是读数不是判据。
        Assert.Contains("host.Height", segment, StringComparison.Ordinal);
        Assert.Contains("ManualDraftLayout.InputAreaHeight(ManualDraftLayout.MainRows)", segment, StringComparison.Ordinal);
        // 容量判据仍在（实测容量对判据行数）。
        Assert.Contains("capacity < ManualDraftLayout.MainRows", segment, StringComparison.Ordinal);
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
    /// 登记表里这一屏从「未量」改成「已量」并写清轮次——简报 P1-1 要的那一行。
    /// <para>
    /// 通用的对账在 <c>MeasuredScreensTests</c>；这一条只钉「这一屏这一轮真的被认领了」，
    /// 免得下一轮有人把 <c>RunManualDraftShown</c> 删掉而登记表悄悄退回「未量」还全绿。
    /// </para>
    /// </summary>
    [Fact]
    public void MeasuredScreens_RegistersThisScreenAsMeasured()
    {
        var row = MeasuredScreens.All.Single(r => r.Classes.Contains("ManualDraftDialog", StringComparer.Ordinal));
        Assert.True(row.Measured, "「手工填写说明」在登记表里又退回未量了：本轮真的 Show() 过它。");
        Assert.Equal("RunManualDraftShown", row.Section);
        Assert.Equal("第二百五十一轮", row.Round);
    }

    /// <summary>
    /// 固定四块的高度各有名字，<see cref="ManualDraftLayout.FixedPanesHeight"/> 是它们的和而不是一个独立读数
    /// （第二百五十一轮）。
    /// <para>
    /// <b>这一条挡的是什么</b>：<c>FixedPanesHeight</c> 若写成字面量 163，而四块的高度散在
    /// <c>ScanResultsDialog.cs</c> 里就地决定，改任一块 → 163 不跟着动 → 输入区被悄悄挤掉一行，
    /// 而 <c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c> 照样绿（它只判 10 行那一点的取值）。
    /// </para>
    /// </summary>
    [Fact]
    public void FixedPanesHeight_IsTheSumOfTheFourNamedPanes()
    {
        // 四块都是 AutoSize，读数是量出来的。
        Assert.Equal(20, ManualDraftLayout.PositionRowHeight);
        Assert.Equal(46, ManualDraftLayout.GuidanceRowHeight);
        Assert.Equal(33, ManualDraftLayout.StatusRowHeight);
        Assert.Equal(64, ManualDraftLayout.ButtonRowHeight);
        // 和是加出来的，不是抄来的：改任一块，这个和跟着动。
        Assert.Equal(
            ManualDraftLayout.PositionRowHeight + ManualDraftLayout.GuidanceRowHeight
            + ManualDraftLayout.StatusRowHeight + ManualDraftLayout.ButtonRowHeight,
            ManualDraftLayout.FixedPanesHeight);
        Assert.Equal(163, ManualDraftLayout.FixedPanesHeight);
        // 网格内边距不是一块屏，单独记一笔。
        Assert.Equal(24, ManualDraftLayout.GridPaddingHeight);
    }

    /// <summary>
    /// 这一屏的固定区占屏比与前几屏可比：主内容之外的部分是 163 + 24 = 187px，对窗体高 400px。
    /// <para>
    /// <b>为什么这里要说「可比」</b>（第二百五十一轮，简报 P2-1 的澄清那一半）：
    /// <see cref="EndpointTraceLayout"/> 那一屏的「固定区占屏比」数的是<b>主内容之外的全部</b>
    /// （顶部两行 + 底部两块 = 353px 对 639px = 55%）。
    /// 而此前 <c>EndpointTraceLayout.cs</c> 注释里给这一屏写的是「97/400 = 24%」——
    /// 97 只是底部两块（状态行 33 + 按钮行 64），<b>漏掉了顶部两行与网格内边距</b>，
    /// 与另一屏不是一个口径，拿来跨屏比较是错的。
    /// 本轮这一屏量过之后，那一格改成同一口径：187 / 400。
    /// </para>
    /// </summary>
    [Fact]
    public void TheFixedAreaShare_UsesTheSameCaliberAsTheOtherScreens()
    {
        var fixedArea = ManualDraftLayout.FixedPanesHeight + ManualDraftLayout.GridPaddingHeight;
        Assert.Equal(187, fixedArea);
        // 主内容拿得到判据行数那么多行：窗体高扣掉固定区，剩下的正是输入区。
        Assert.Equal(
            ManualDraftLayout.InputAreaHeight(ManualDraftLayout.MainRows),
            ManualDraftLayout.DialogHeight - fixedArea);
        // 固定区不到整屏的一半——与接口追踪明细那一屏（55%）方向相反，这一屏的主内容是宽裕的。
        Assert.True(
            fixedArea * 2 < ManualDraftLayout.DialogHeight,
            $"固定区 {fixedArea}px 已不再少于窗体高 {ManualDraftLayout.DialogHeight}px 的一半："
            + "「这一屏的主内容是宽裕的」这句话需要重新量过。");
    }

    /// <summary>共享编译：这条算式与测试工程是同一份，不是测试里抄的第二份。</summary>
    [Fact]
    public void ManualDraftLayout_IsSharedCompiledIntoTheTests()
    {
        Assert.Contains(
            @"..\Desktop\Native\ManualDraftLayout.cs",
            Repo.Source("ApifoxHelper.Tests", "ApifoxHelper.Tests.csproj"),
            StringComparison.Ordinal);
    }
}
