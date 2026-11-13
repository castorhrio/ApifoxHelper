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
    /// 类注释里念的那两个数，必须就是算式在 <see cref="ScanWarningsLayout.MainRows"/> 处的取值
    /// （第二百五十三轮，简报 P1-1，具名 pass：clarify + harden）。
    /// <para>
    /// <b>补的是哪个缺口</b>：<c>ScanWarningsLayout.cs</c> 的类注释与 <c>ListHostChrome</c> 注释此前念
    /// 「宿主 365px / 清单本体 355px」，而全仓其余六处（本文件类注释、<c>DialogHeight_IsTheFormulaAtTheMeasuredRowCount</c>
    /// 那个 461 锚点、<c>MeasuredScreens</c>、<c>docs/NOTES.md</c> 第二百五十二轮记录、<c>ListHostHeight(16) = 346</c>）
    /// 按 346 / 336 念。只读核对结论：<b>是笔误</b>——365 / 355 是「改算式之前那一屏（窗体高 480）」的读数，
    /// 被写进了「这一处现在怎么算」的说明里，不是另一档（120 / 192）的读数。
    /// </para>
    /// <para>
    /// <b>这一条判什么</b>：把 <c>ScanWarningsLayout.cs</c> 的注释行取出来，逐处判「注释里念的数 == 算式取值」——
    /// 类注释那一句（宿主 / 清单本体）、<c>TextBoxPadding</c> 那一句（框高 / 可视高）、
    /// <c>ListHostChrome</c> 那一句（宿主 / 清单）、<c>ListHostMargin</c> 那一句（网格第 2 行 / 宿主自身高）。
    /// <b>不改任何常量与算式</b>：这一条只判注释文字，算式那一族在
    /// <see cref="ListHostHeight_IsRowsTimesLineHeightPlusTheTwoChromes"/> 与
    /// <see cref="DialogHeight_IsTheFormulaAtTheMeasuredRowCount"/> 里。
    /// </para>
    /// <para>
    /// <b>它不判什么</b>：不判注释的散文好不好读；不判「改之前」那一对读数（365 / 355）该不该留——
    /// 留着当来历是有价值的，只判它<b>必须带「改算式之前」这个前缀</b>，否则下一轮读到的人会以为那就是当前取值。
    /// </para>
    /// </summary>
    [Fact]
    public void TheClassComment_ReadsTheSameNumbersAsTheFormula()
    {
        var comments = CommentLines(NativeFile("ScanWarningsLayout.cs"));

        // 五处读数句必须都在：删掉一句等于把「注释与算式对账」这件事删掉，门就再也管不住它。
        Assert.Equal(1, Count(comments, @"警告清单宿主 <b>\d+px</b>"));
        Assert.Equal(1, Count(comments, @"清单本体 <b>\d+px</b>"));
        Assert.Equal(1, Count(comments, @"框高 \d+px 与可视高 \d+px"));
        Assert.Equal(1, Count(comments, @"宿主 \d+px 对清单 \d+px"));
        Assert.Equal(1, Count(comments, @"网格第 2 行 \d+px 对宿主自身高 \d+px"));

        Assert.Empty(CommentMismatches(comments));
    }

    /// <summary>
    /// 上面那段判据<b>自己</b>被抓得住：把第二百五十二轮之前那一句的原样喂进去，必须报出两处。
    /// <para>
    /// <b>为什么这一条必须存在</b>：本仓的教训是「判据自己没被跑过」比「没有判据」更贵
    /// （第二百零九轮那两处假变异、第二百四十三轮 M4 那一发假绿）。这一条同时钉住「来历句豁免」的边界：
    /// 带「改算式之前」前缀的斜杠写法放过，不带前缀的当场红。
    /// </para>
    /// </summary>
    [Fact]
    public void TheCommentGate_ReportsTheStaleReadingItMustCatch()
    {
        // ① 改之前那一句的原样：把「改之前」的读数写进了「现在怎么算」的说明。
        var stale = "/// 警告清单宿主 <b>365px</b>，清单本体 <b>355px</b>——扣掉宿主镀铬 10px 之后";
        var problems = CommentMismatches(stale);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.Contains("346", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("336", StringComparison.Ordinal));

        // ② 来历句（带「改算式之前」前缀的斜杠写法）不算错——它是来历，不是当前取值。
        Assert.Empty(CommentMismatches(
            "/// 改算式之前那一屏（窗体高 480）量到的是宿主 365px / 清单本体 355px，"));

        // ③ 不带前缀的斜杠写法当场红：来历句与「现在怎么算」必须分得开。
        var unlabeled = CommentMismatches("/// 量到的是宿主 365px / 清单本体 355px，");
        Assert.Single(unlabeled);
        Assert.Contains("改算式之前", unlabeled[0], StringComparison.Ordinal);

        // ④ 合规写法：注释念的就是算式取值，一处都不报。
        Assert.Empty(CommentMismatches(
            "/// 警告清单宿主 <b>346px</b>，清单本体 <b>336px</b>——扣掉宿主镀铬 10px 之后"));
    }

    /// <summary>
    /// 注释里念的数与算式取值逐处对账，返回「哪几处分家」的说明（空 = 全对上）。
    /// <para>
    /// 写成纯文本进、问题出的函数，是为了让上面那条自测能拿<b>注入过的</b>夹具喂它——
    /// 与 <c>LayoutMeasurementGateTests.Problems</c> / <c>WiringAssertionShapeTests.Problems</c> 同一形状。
    /// </para>
    /// </summary>
    private static List<string> CommentMismatches(string comments)
    {
        var host = ScanWarningsLayout.ListHostHeight(ScanWarningsLayout.MainRows);
        var text = ScanWarningsLayout.TextHeight(ScanWarningsLayout.MainRows);
        var gridRow = host + ScanWarningsLayout.ListHostMargin;
        var problems = new List<string>();

        foreach (Match match in Regex.Matches(comments, @"警告清单宿主 <b>(?<n>\d+)px</b>"))
        {
            Judge("类注释「警告清单宿主 Npx」", match.Groups["n"], host, problems);
        }

        foreach (Match match in Regex.Matches(comments, @"清单本体 <b>(?<n>\d+)px</b>"))
        {
            Judge("类注释「清单本体 Npx」", match.Groups["n"], text, problems);
        }

        foreach (Match match in Regex.Matches(comments, @"框高 (?<n>\d+)px 与可视高 (?<m>\d+)px"))
        {
            Judge("TextBoxPadding 注释「框高 Npx」", match.Groups["n"], text, problems);
            Judge("TextBoxPadding 注释「可视高 Npx」", match.Groups["m"], text, problems);
        }

        foreach (Match match in Regex.Matches(comments, @"宿主 (?<n>\d+)px 对清单 (?<m>\d+)px"))
        {
            Judge("ListHostChrome 注释「宿主 Npx」", match.Groups["n"], host, problems);
            Judge("ListHostChrome 注释「清单 Npx」", match.Groups["m"], text, problems);
        }

        foreach (Match match in Regex.Matches(comments, @"网格第 2 行 (?<n>\d+)px 对宿主自身高 (?<m>\d+)px"))
        {
            Judge("ListHostMargin 注释「网格第 2 行 Npx」", match.Groups["n"], gridRow, problems);
            Judge("ListHostMargin 注释「宿主自身高 Npx」", match.Groups["m"], host, problems);
        }

        // 来历句：斜杠写法只许出现在「改算式之前」那一句里。
        foreach (Match match in Regex.Matches(comments, @"宿主 (?<n>\d+)px / 清单本体 (?<m>\d+)px"))
        {
            var line = LineOf(comments, match.Index);
            if (line.Contains("改算式之前", StringComparison.Ordinal)) continue;
            problems.Add(
                $"「{line.Trim()}」把改之前的读数（宿主 {match.Groups["n"].Value}px / 清单本体 {match.Groups["m"].Value}px）"
                + "写成了「现在怎么算」——来历句必须带「改算式之前」这个前缀，"
                + $"否则下一轮读到的人会以为当前取值就是 {match.Groups["n"].Value} / {match.Groups["m"].Value}。");
        }

        return problems;
    }

    /// <summary>一处读数句的对账：注释念的数与算式取值不等就记一笔。</summary>
    private static void Judge(string what, Group group, int expected, List<string> problems)
    {
        var got = int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture);
        if (got == expected) return;
        problems.Add(
            $"{what} 念的是 {got}px，算式在 {ScanWarningsLayout.MainRows} 行处给的是 {expected}px——"
            + "注释与算式分家，下一轮读到的人会按注释去改常量。");
    }

    /// <summary>一个字符位置所在的那一行（用来判「来历句有没有前缀」）。</summary>
    private static string LineOf(string text, int index)
    {
        var start = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        var end = text.IndexOf('\n', index);
        return end < 0 ? text[start..] : text[start..end];
    }

    /// <summary>
    /// 一份源码里的注释行，拼成一段文本——这一族门只判注释文字。
    /// <b>取注释的口径在 <see cref="CodeScan.CommentLines"/> 一处</b>，这里不另写一份过滤。
    /// </summary>
    private static string CommentLines(string source) =>
        string.Join('\n', CodeScan.CommentLines(source));

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
