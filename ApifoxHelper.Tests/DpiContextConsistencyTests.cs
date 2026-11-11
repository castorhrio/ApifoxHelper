using System.Text.RegularExpressions;
using Xunit;

namespace ApifoxHelper.Tests;

/// <summary>
/// 「控件自报 DPI」与「进程 DPI」必须是两个分开念的数（第二百零九轮，简报 P0-1）。
/// <para>
/// <b>补的是哪个缺口</b>：<c>demo.ps1</c> 与 <c>demo.sh</c> 在 125% / 200% 两档给出不同读数，机制未归因
/// （第二百零七轮如实记录）。两边差的那件事有一个可数的候选：控件自报的 DPI 跟不跟档位走。
/// 若真机落进「控件自报跟着走」那一支，配置表单在 125% 档会量到 4 行、200% 档 10 行超出行高预算。
/// 本轮不归因、不改预算，只把「那一次量到的是哪一个上下文」变成读数自己能回答的事：
/// <c>RunProfileFormLayout</c> 那两段预算判定各记下两个数，取法收在唯一出口 <c>DpiContext</c>。
/// </para>
/// <para>
/// <b>它不判什么</b>：不判两档真机上到底差在哪（那需要真机或更深的 wine 环境调查），
/// 也不判两个数谁对谁错。它只保证下一次两边再差时，报告里已经有可对照的两行。
/// </para>
/// </summary>
public class DpiContextConsistencyTests
{
    private const string Signature =
        "private static void RunProfileFormLayout(IServiceProvider services, ProjectProfile profile)";

    private static string Smoke => Repo.SmokeProgram;

    /// <summary>整段布局方法体，切到下一个顶格 <c>private</c> 声明之前（段体里有 lambda，花括号配对会被字符串骗）。</summary>
    private static string LayoutBody()
    {
        var start = Smoke.IndexOf(Signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "冒烟器里找不到 RunProfileFormLayout 的签名：取法变了要先改这一处，别让门静默绿着。");
        var end = Smoke.IndexOf("\n    private", start + Signature.Length, StringComparison.Ordinal);
        Assert.True(end > start, "RunProfileFormLayout 切不出方法体边界：后面找不到下一个顶格声明。");
        return Smoke[start..end];
    }

    /// <summary>唯一出口的函数体（表达式体只有一行，取到行尾）。</summary>
    private static string HelperBody()
    {
        var match = Regex.Match(Smoke, @"(?m)^\s*private static string DpiContext\(Control control\) =>$");
        Assert.True(match.Success,
            "冒烟器里找不到 DpiContext 这个唯一出口：两个读数必须共用一处取法，否则两边各念一套，下一次差异仍然对不上。");
        return Smoke[(match.Index + match.Length)..].Trim().Split('\n')[0].Trim();
    }

    /// <summary>整段布局方法体的<b>代码行</b>（注释按本仓唯一口径 <see cref="CodeScan"/> 去掉——注释里引用原始 API 名是在说理由，不是在接线）。</summary>
    private static string LayoutCode() => CodeScan.Text(LayoutBody());

    /// <summary>取 <c>RunProfileFormLayout</c> 里某一段 RunAction 的正文（到下一段 RunAction 之前，不出整段边界）。</summary>
    private static string RunActionSlice(string title)
    {
        var body = LayoutCode();
        var start = body.IndexOf("RunAction(\"" + title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"冒烟器 RunProfileFormLayout 里找不到那一段「{title}」。");
        var next = body.IndexOf("RunAction(\"", start + 10, StringComparison.Ordinal);
        return next < 0 ? body[start..] : body[start..next];
    }

    [Fact]
    public void Helper_NamesBothReadingsUnderTheirOwnLabels()
    {
        var body = HelperBody();

        // ① 两个数都在，且各挂各的标签。
        Assert.Matches(@"控件自报 DPI \{control\.DeviceDpi\}", body);
        Assert.Matches(@"进程 DPI \{DpiScale\.ScreenDpi\(\)\}", body);
        var labels = new[] { "控件自报 DPI", "进程 DPI" };
        Assert.Equal(2, labels.Count(body.Contains));

        // ② 反向：不许把两个标签接反（接反等于混用，报告里念出来的就不是那一个数）。
        Assert.DoesNotMatch(@"控件自报 DPI \{DpiScale", body);
        Assert.DoesNotMatch(@"进程 DPI \{control\.DeviceDpi\}", body);

        // ③ 只念数：不判断、不换算、不取其一。两个数合成一个，下一次差异就又读不出来了。
        Assert.True(!Regex.IsMatch(body, @"Math\.|if\b|=="),
            "DpiContext 里出现了比较或换算：这一处只负责把两个数各按各的口径念出来，判断留给下一轮的归因。");
    }

    [Fact]
    public void LayoutSection_TakesBothReadingsThroughThatOneHelperOnly()
    {
        var body = LayoutCode();

        // 取法只有一份：这一族里不许有人绕过出口自己写 DeviceDpi / ScreenDpi。
        Assert.True(!body.Contains("DeviceDpi", StringComparison.Ordinal),
            "RunProfileFormLayout 里出现了未命名的 DeviceDpi：控件自报 DPI 的取法只有一份。");
        Assert.True(!body.Contains("ScreenDpi", StringComparison.Ordinal),
            "RunProfileFormLayout 里出现了未命名的 ScreenDpi：进程 DPI 的取法只有一份。");

        // 两个数分别进报告：① 行高段与 ⑧ 提示行段各一行，不合成一行。
        Assert.Equal(2, Regex.Matches(body, @"(?m)^\s*_lines\.Add\(.*DpiContext\(").Count);
    }

    [Fact]
    public void BothOverBudgetVerdicts_CarryBothReadings()
    {
        // ① 超行高预算那一句失败消息自己带两个数：真机当天先看的就是这一句。
        var rows = RunActionSlice("配置表单：新建对话框的行高没有死空白");
        var verdict = rows[rows.IndexOf("if (tall.Count > 0)", StringComparison.Ordinal)..];
        Assert.Single(Regex.Matches(verdict, @"DpiContext\(").Cast<Match>());
        // 同一段里报告行也念一遍：数在通过时也要留下，不然只有失败那跑才有对照。
        Assert.Equal(2, Regex.Matches(rows, @"DpiContext\(").Count);

        // ⑧ 超提示行预算：每一条超预算都带上自己那个对话框的两个数，而不是只在末尾指一句。
        var hints = RunActionSlice("配置表单：四个子对话框的提示行没有死空白");
        Assert.Matches(@"tall\.Add\(\$"".*DpiContext\(form\)", hints);
        Assert.DoesNotMatch(@"tall\.Add\(\$""「\{screen\}」第 \{row\} 行 \{heights\[row\]\}px（\{TallestInRow\(grid, row\)}）\)", hints);

        // 这一段的失败消息不再重复指一遍数（数已在每条里）。
        Assert.True(!Regex.IsMatch(hints, @"见上面那[四\d]+行"),
            "⑧ 的失败消息还指着上面那几行：数已经带在每条里了，指回去等于留一句过时的指引。");
    }

    [Fact]
    public void LayoutSection_IsRegisteredAndUnchangedInSegmentCount()
    {
        var body = LayoutCode();

        // 8 段判定一条不加：本轮只加读数，加一条判定就等于把三档读数改掉，而那是下一轮归因之后才干的事。
        Assert.Equal(8, Regex.Matches(body, @"(?m)^\s{12,}RunAction\(").Count);
        Assert.Matches(@"(?m)^\s*Section\(""配置表单的真实布局（新建 / 编辑）"", nameof\(RunProfileFormLayout\)\);", Smoke);

        // 段文档注释说得清「两个数分别记」，读演示脚本的人不用回头翻源码。
        var start = Smoke.IndexOf(Signature, StringComparison.Ordinal);
        var docStart = Smoke.LastIndexOf("/// 八段，全部量真实控件", start, StringComparison.Ordinal);
        Assert.True(docStart >= 0, "RunProfileFormLayout 的八段文档注释找不到：段数说明是答辩时念的那一句。");
        var doc = Smoke[docStart..start];
        Assert.Contains("两个数分别进报告", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_RecordsTheDivergenceAndPointsAtTheNewReportLine()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        var section = readme[readme.IndexOf("**125% / 200% 两档的读数不同", StringComparison.Ordinal)..];
        var end = section.IndexOf("\n\n", StringComparison.Ordinal);
        section = section[..(end < 0 ? section.Length : end)];

        Assert.Contains("机制未归因", section, StringComparison.Ordinal);
        Assert.Contains("DPI 上下文", section, StringComparison.Ordinal);
        Assert.True(section.Contains("DpiContext", StringComparison.Ordinal),
            ".winecheck/README.md 那段分歧记录没指出新的报告行叫什么：读到「两边读数不同」的人应该立刻知道下一次去哪一行对照。");
    }

    // ---------- 第二百一十轮（简报 P0-1）：把悬案铺成「真机先看哪两行、不符时记进哪一条」 ----------

    /// <summary>
    /// 那两行报告行在冒烟器源码里的字面量（① 行高段与 ⑧ 提示行段各一行）。
    /// <para>
    /// 写成字面量而不是「在那一节里正则抓一个像报告行的东西」：抓的话，把标签改成一个冒烟器根本不打的字
    /// （<c>DPI 上下文</c> 后面括号里的名字被改掉是最常见的一种），抓出来的仍是「一段文字」，门照绿，
    /// 而真机那天照着搜就是空手。字面量在两边各写一遍、由门判对得上，是这个仓一贯的做法
    /// （同一形状见 <see cref="Signature"/> 与 <c>ToolRoster_…</c> 的关键词清单）。
    /// </para>
    /// </summary>
    private static readonly string[] ReportLineLabels = ["DPI 上下文（新建配置对话框）", "提示行行高（"];

    /// <summary>「真机当天先看这两行」那一段所在的最小单元：列表项 4 本体，取到下一个顶格 <c>**</c> 块之前。</summary>
    private static string DivergenceSection()
    {
        var readme = Repo.Source(".winecheck", "README.md");
        var start = readme.IndexOf("**125% / 200% 两档的读数不同", StringComparison.Ordinal);
        Assert.True(start >= 0, ".winecheck/README.md 找不到「125% / 200% 两档的读数不同」那一条：分歧记录本身没了。");
        var end = readme.IndexOf("\n**", start, StringComparison.Ordinal);
        return readme[start..(end < 0 ? readme.Length : end)];
    }

    /// <summary>反引号跨度（<c>`…`</c>）逐个取出——引用只认「整段就是一个跨度」，不认「这串字在正文里出现过」。</summary>
    private static List<string> Spans(string text)
    {
        var ticks = text.Count(c => c == '`');
        Assert.True(ticks % 2 == 0,
            $"那段里有 {ticks} 个反引号（奇数）：少配一个反引号会把后面的跨度整体错位，"
            + "下一条读到的「报告行标签」就成了正文里夹带的别的字——这种绿是假绿。");
        return Regex.Matches(text, @"`([^`]+)`").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
    }

    /// <summary>产出物清单每一行点到的归档目录名（按行号取，不拍平成一张大集合）。</summary>
    private static Dictionary<int, List<string>> EvidenceDirsByRow()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var line in Repo.Source("docs", "evidence", "real-machine", "README.md").Split('\n'))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count < 2 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            map[int.Parse(cells[0])] = Regex.Matches(cells[1], @"docs/evidence/real-machine/([^/`]+)/")
                .Cast<Match>().Select(m => m.Groups[1].Value).ToList();
        }

        return map;
    }

    /// <summary>产出物清单每一行的「产出物」格点到的文件名。</summary>
    private static Dictionary<int, List<string>> EvidenceFilesByRow()
    {
        var map = new Dictionary<int, List<string>>();
        foreach (var line in Repo.Source("docs", "evidence", "real-machine", "README.md").Split('\n'))
        {
            var cells = line.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToList();
            if (cells.Count < 3 || !Regex.IsMatch(cells[0], @"^\d+$")) continue;
            map[int.Parse(cells[0])] = Regex.Matches(cells[2], @"`([^`]+)`").Cast<Match>()
                .Select(m => m.Groups[1].Value).Where(n => n.Contains('.')).ToList();
        }

        return map;
    }

    /// <summary>docs/NOTES.md 的 G-D5 动作单每一行：行号 → 该行自己的名字（<c>N. 名字：</c> 里那一段）。</summary>
    private static Dictionary<int, string> ActionRowTitles()
    {
        var lines = Repo.Source("docs", "NOTES.md").Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimEnd() == "## G-D5 真机复核清单（可勾选）");
        Assert.True(start >= 0, "docs/NOTES.md 找不到 G-D5 动作单那一节：那段文字指的回写位置就是悬空的。");
        var end = Array.FindIndex(lines, start + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        var titles = new Dictionary<int, string>();
        foreach (var m in lines[start..(end < 0 ? lines.Length : end)]
                 .Select(l => Regex.Match(l, @"^\| \[[ x]\] \| (\d+)\. ([^：|]+)：")))
        {
            if (m.Success) titles[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Trim();
        }

        Assert.True(titles.Count == 7, $"G-D5 动作单解析出 {titles.Count} 行（应为 7 行）：取法变了先改这一处，别让门静默绿着。");
        return titles;
    }

    /// <summary>
    /// 那段点名的两行报告行，必须是冒烟器真的会打进报告的那两行。
    /// <para>
    /// <b>补的是哪个缺口</b>：第二百零九轮把两个数收进了报告，但「真机当天先看哪两行」仍是读的人自己推断——
    /// 现场翻报告翻错一行，抄回来的就是别一个上下文的数，而这条悬案最贵的正是「抄错了还以为抄对了」。
    /// 这一段把两行的名字写死在文档里；写死的代价是冒烟器改名时文档会悬空，所以这里判的就是那个代价：
    /// 文档里那两个跨度必须逐个在 <c>ApifoxHelper.Smoke/Program.cs</c> 里出现（同一族见
    /// <c>RealMachineEvidenceTests.ToolRoster_NamesAToolForEveryPresetSlotAndOnlyRealReportLines</c>）。
    /// </para>
    /// </summary>
    [Fact]
    public void Readme_NamesTwoReportLinesTheSmokeProgramActuallyPrints()
    {
        var spans = Spans(DivergenceSection());
        var smoke = Smoke;

        foreach (var label in ReportLineLabels)
        {
            Assert.True(spans.Contains(label),
                $".winecheck/README.md 那段没把报告行「{label}」单独写成一个反引号跨度：真机当天照着搜，"
                + "搜到的是正文里的别的字，抄回来的数对不上那一段的判定。");
            Assert.True(smoke.Contains(label, StringComparison.Ordinal),
                $"那段让读报告里的「{label}」，而冒烟器源码里没有这几个字：指一行冒烟器根本不打的字，"
                + "等于让下一个人对着空手找那一跑是哪一个上下文。");
        }

        // 两行都各自带着那两个数（只指行名、不说行里念什么，当天仍然不知道该抄哪两个数）。
        Assert.Contains("控件自报", DivergenceSection(), StringComparison.Ordinal);
        Assert.Contains("进程", DivergenceSection(), StringComparison.Ordinal);
    }

    /// <summary>
    /// 那段让记进的第 3 / 第 4 条，必须是动作单里真存在的行、用该行自己的名字、配的归档目录也是那一行的。
    /// <para>
    /// <b>判三件事，一件比一件容易漂</b>：① 行号在动作单里真的存在（写了个不存在的第 8 条即红）；
    /// ② 那一段用的名字必须是动作单该行<b>自己的</b>名字——「运行中改缩放」与「运行中改系统缩放」差一个字，
    /// 人读得懂，下一轮按名字去找那一行就找不到；③ 括号里配的归档目录必须是<b>产出物清单该行</b>点到的那一个，
    /// 并且磁盘上真有那个目录——把第 3 条的读数归档到 <c>scale-change/</c>，两条动作的落点就互换了两支场景。
    /// </para>
    /// <para><b>不判</b>：不判真机那天两个数到底是多少（那要真机），也不判该记进哪一条（那是人现场的判断）。
    /// 这一段的作用是把「往哪写」提前定死，而不是替那天做决定。</para>
    /// </summary>
    [Fact]
    public void Readme_PointsAtActionRowsAndArchiveDirectoriesThatExist()
    {
        var section = DivergenceSection();
        var spans = Spans(section);
        var titles = ActionRowTitles();
        var dirsByRow = EvidenceDirsByRow();

        foreach (var row in new[] { 3, 4 })
        {
            var title = titles[row];
            Assert.True(Regex.IsMatch(section, @"第 " + row + @" 条\*{0,2}（" + Regex.Escape(title)),
                $".winecheck/README.md 那段说「第 {row} 条」时用的不是动作单该行自己的名字（实际是「{title}」）："
                + "差一个字，下一轮按名字回头找那一行就找不到，回写位置又变回口头约定。");

            var dirs = dirsByRow[row];
            Assert.Single(dirs);
            var cited = dirs.Where(d => spans.Contains(d + "/")).ToList();
            Assert.True(cited.Count > 0,
                $".winecheck/README.md 那段说「第 {row} 条」时要归档到 {dirs[0]}/，而那段的反引号跨度里"
                + $"没有出现它（跨度里的目录：{string.Join("、", spans.Where(s => s.EndsWith("/")))}）："
                + "两条动作的落点互换，当天量到的数就写进了别一支场景的归档里。");
            Assert.True(Directory.Exists(Path.Combine(Repo.Root, "docs", "evidence", "real-machine", dirs[0])),
                $"那一段让把读数归档到 {dirs[0]}/，而 docs/evidence/real-machine/ 下没有这个目录：当天没有落点。");
        }

        // 「不归因」这句必须跟着这段一起留着，否则这一节会被读成「已经知道差在哪了」。
        Assert.Contains("不归因", section, StringComparison.Ordinal);
    }

    /// <summary>
    /// 产出物清单第 3、4 行给那两行读数一个落点，且它点的报告文件名是清单第 7 行真有的那一个。
    /// <para>
    /// <b>为什么单独判这一条</b>：那两个目录的既有判据是「一行三个数」（因子 / 客户区宽 / 说明行可用宽），
    /// 三个数都是<b>量出来的几何</b>，念不出「那一跑是哪一个 DPI 上下文」——而那正是这支悬案唯一缺的对照。
    /// 补上的这一格若只写「抄下 DPI 上下文」而不点明从哪份文件抄，真机当天就要现场决定去哪找；
    /// 点名的文件又必须是<b>第 7 行已经归档的那一份</b>（<c>smoke-report.txt</c>），
    /// 自己发明一个文件名，归档核对那支脚本就会把它当「清单没点到的东西」。
    /// </para>
    /// </summary>
    [Fact]
    public void EvidenceReadme_GivesThoseTwoLinesAPlaceToLand()
    {
        var readme = Repo.Source("docs", "evidence", "real-machine", "README.md");
        var filesByRow = EvidenceFilesByRow();
        Assert.True(filesByRow[7].Contains("smoke-report.txt", StringComparer.Ordinal),
            "产出物清单第 7 行的产出物里没有 `smoke-report.txt`（现读到的：" + string.Join("、", filesByRow[7]) + "）："
            + "第 3、4 行让抄的那两行读数出自这一份报告，报告本身不在归档清单里，那两句就指着一份没人收的东西。");

        var cells = new Dictionary<int, string>();
        foreach (var line in readme.Split('\n'))
        {
            var c = line.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToList();
            if (c.Count >= 4 && Regex.IsMatch(c[0], @"^\d+$")) cells[int.Parse(c[0])] = c[3];
        }

        foreach (var row in new[] { 3, 4 })
        {
            var cell = cells[row];
            foreach (var keyword in new[] { "DPI 上下文", "控件自报", "进程", "smoke-report.txt" })
            {
                Assert.True(cell.Contains(keyword, StringComparison.Ordinal),
                    $"产出物清单第 {row} 行的「必须有的数字」那一格没写「{keyword}」："
                    + "那一行只有几何读数，真机抄完仍然答不出那两行念的是哪一个上下文。");
            }

            // 那一格里点到的每一个文件名，都必须是该行自己的产出物或第 7 行那份报告：
            // 自己起一个名字，当天归档进去没人认，而核对那支脚本会把它念成「清单没点到的东西」。
            var allowed = filesByRow[row].Concat(filesByRow[7]).ToHashSet(StringComparer.Ordinal);
            var named = Regex.Matches(cell, @"`([^`]+\.[a-z]{3,8})`").Cast<Match>()
                .Select(m => m.Groups[1].Value).ToList();
            foreach (var file in named.Where(f => !allowed.Contains(f)))
            {
                Assert.Fail($"产出物清单第 {row} 行的读数那一格点了「{file}」，"
                    + $"它既不是该行「产出物」那一格（{string.Join("、", filesByRow[row])}）里的文件名，"
                    + "也不是第 7 行归档的那一份报告：现场发明的文件名，当天没有判据认它。");
            }

            Assert.NotEmpty(named);
        }

        Assert.True(cells[3].Contains(".winecheck", StringComparison.Ordinal),
            "产出物清单第 3 行没指回 .winecheck/README.md 那一节：两个数怎么算「不对应」在这一层没有判据出处。");
    }
}
